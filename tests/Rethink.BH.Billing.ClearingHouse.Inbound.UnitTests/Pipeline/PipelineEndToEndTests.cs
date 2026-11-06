using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Pipeline;

/// <summary>
/// Hardening scenarios over the real handlers and the real SQL manifest: duplicates, crashes, outages, quarantine, and
/// replay must never lose a file, bill it twice, or delete it before Billing queued it.
/// </summary>
public sealed class PipelineEndToEndTests : IAsyncDisposable
{
    private static readonly TimeSpan PastAnyRetry = TimeSpan.FromHours(1);

    private PipelineHarness _harness = new();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Active_run_lands_bills_backs_up_and_deletes_every_file_once()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~")).With(TestData.File("b.999", 1, size: 10), TestData.Bytes("ISA*b.999~"));

        var listing = await _harness.ListAsync();
        await _harness.PumpAsync();

        listing.Should().Be(new InboundListingResult(1, InboundListingKind.Completed, 2, 2, 2, 0));
        var entries = await _harness.AllEntriesAsync();
        entries.Should().HaveCount(2).And.OnlyContain(entry => entry.State == InboundFileState.Done && entry.BillingQueuedUtc != null && entry.Sha256 != null && entry.BillingFileId == 100);
        _harness.Billing.Registered.Keys.Should().BeEquivalentTo("a.835", "b.999");
        _harness.Backup.Stored.Keys.Should().BeEquivalentTo("a.835", "b.999");
        _harness.Session.Deleted.Should().BeEquivalentTo("a.835", "b.999");
        _harness.Leases.Acquired.Should().Equal("inbound-lister-1");
        _harness.Leases.Released.Should().Be(1);
        _harness.Handled.Should().OnlyContain(handled => handled.Disposition == WorkItemDisposition.Completed);
        _harness.Sink.ByEventId(1116).Should().HaveCount(2);
    }

    [Fact]
    public async Task Listing_again_before_the_workers_run_enqueues_nothing_new()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));

        await _harness.ListAsync();
        var second = await _harness.ListAsync();

        second.Enqueued.Should().Be(0);
        second.Listed.Should().Be(1);
        _harness.Queue.Pending.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_deliveries_after_completion_do_nothing()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();
        await _harness.PumpAsync();
        var entry = await _harness.SingleEntryAsync();

        _harness.Queue.Duplicate(InboundWorkStage.Download, InboundFileWorkItem.First(entry));
        _harness.Queue.Duplicate(InboundWorkStage.Process, InboundFileWorkItem.First(entry));
        await _harness.PumpAsync();

        _harness.Billing.Registered.Should().HaveCount(1);
        _harness.Landing.Writes.Should().Be(1);
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
        _harness.Sink.ByEventId(1111).Should().HaveCount(2);
    }

    [Fact]
    public async Task Unknown_file_message_is_completed_as_an_orphan()
    {
        var disposition = await _harness.Get<IInboundWorkDispatcher>()
            .DispatchAsync(InboundWorkStage.Download, new InboundFileWorkItem(Guid.NewGuid(), 1, 0), CancellationToken.None);

        disposition.Should().Be(WorkItemDisposition.Completed);
        _harness.Sink.ByEventId(1110).Should().ContainSingle();
    }

    [Fact]
    public async Task Download_failure_is_retried_with_backoff_then_succeeds()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Session.DownloadFailures["a.835"] = new IOException("connection reset");
        await _harness.ListAsync();

        await _harness.PumpAsync();
        var retrying = await _harness.SingleEntryAsync();
        _harness.Session.DownloadFailures.Clear();
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);

        retrying.State.Should().Be(InboundFileState.Queued);
        retrying.Attempts.Should().Be(1);
        retrying.LastError.Should().Contain("connection reset");
        _harness.Queue.Sent.Should().Contain(sent => sent.Stage == InboundWorkStage.Download && sent.Delay == TimeSpan.FromSeconds(30) && sent.Item.Attempt == 1);
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
        _harness.Sink.ByEventId(1120).Should().ContainSingle();
    }

    [Fact]
    public async Task Exhausted_download_is_quarantined_then_replay_finishes_it()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Session.DownloadFailures["a.835"] = new IOException("host unreachable");
        await _harness.ListAsync();

        await _harness.PumpAsync();
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);
        var quarantined = await _harness.SingleEntryAsync();

        _harness.Session.DownloadFailures.Clear();
        var replay = await _harness.SendAsync(new ReplayQuarantinedFileCommand(quarantined.Id));
        await _harness.PumpAsync();

        quarantined.State.Should().Be(InboundFileState.Quarantined);
        quarantined.Attempts.Should().Be(3);
        _harness.Handled.Should().Contain(handled => handled.Disposition == WorkItemDisposition.Quarantined);
        replay.Should().Be(new ReplayResult(ReplayOutcome.Requeued, InboundFileState.Queued));
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
        _harness.Sink.ByEventId(1121).Should().ContainSingle();
        _harness.Sink.ByEventId(1130).Should().ContainSingle();
    }

    [Fact]
    public async Task Replay_of_unknown_or_active_file_changes_nothing()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();
        var entry = await _harness.SingleEntryAsync();

        var unknown = await _harness.SendAsync(new ReplayQuarantinedFileCommand(Guid.NewGuid()));
        var active = await _harness.SendAsync(new ReplayQuarantinedFileCommand(entry.Id));

        unknown.Should().Be(new ReplayResult(ReplayOutcome.NotFound, null));
        active.Should().Be(new ReplayResult(ReplayOutcome.NotQuarantined, InboundFileState.Queued));
    }

    [Fact]
    public async Task Billing_outage_is_retried_from_the_landed_copy_without_downloading_again()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Billing.Failures["a.835"] = new HttpRequestException("503");
        await _harness.ListAsync();

        await _harness.PumpAsync();
        var waiting = await _harness.SingleEntryAsync();
        _harness.Billing.Failures.Clear();
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);

        waiting.State.Should().Be(InboundFileState.Landed);
        _harness.Landing.Writes.Should().Be(1);
        _harness.Billing.Registered.Should().ContainKey("a.835");
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Billing_rejection_or_not_queued_is_retried(bool acceptedButNotQueued)
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Billing.Responses["a.835"] = acceptedButNotQueued ? BillingRegistration.NotQueued(7) : BillingRegistration.Rejected("duplicate");
        await _harness.ListAsync();

        await _harness.PumpAsync();

        var entry = await _harness.SingleEntryAsync();
        entry.State.Should().Be(InboundFileState.Landed);
        entry.BillingQueuedUtc.Should().BeNull();
        _harness.Session.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Landed_copy_that_fails_its_hash_check_is_never_sent_to_billing()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();
        _harness.Queue.TryTakeDue(out var download).Should().BeTrue();
        await _harness.Get<IInboundWorkDispatcher>().DispatchAsync(download.Stage, download.Item, CancellationToken.None);
        var landed = await _harness.SingleEntryAsync();
        _harness.Landing.Corrupt(landed.LandingBlobName!);

        await _harness.PumpAsync();

        _harness.Billing.Registered.Should().BeEmpty();
        var entry = await _harness.SingleEntryAsync();
        entry.State.Should().Be(InboundFileState.Landed);
        entry.LastError.Should().Contain("SHA-256");
    }

    [Fact]
    public async Task Delete_failure_after_billing_retries_only_the_delete_and_never_bills_twice()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Session.DeleteFailures["a.835"] = new IOException("permission denied");
        await _harness.ListAsync();

        await _harness.PumpAsync();
        var afterBilling = await _harness.SingleEntryAsync();
        _harness.Session.DeleteFailures.Clear();
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);

        afterBilling.State.Should().Be(InboundFileState.BillingQueued);
        afterBilling.BillingQueuedUtc.Should().NotBeNull();
        _harness.Billing.Registered.Should().HaveCount(1);
        _harness.Backup.Stored.Should().HaveCount(1);
        _harness.Session.Deleted.Should().Equal("a.835");
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
    }

    [Fact]
    public async Task Quarantined_after_billing_replays_only_the_delete()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Session.DeleteFailures["a.835"] = new IOException("permission denied");
        await _harness.ListAsync();
        await _harness.PumpAsync();
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);
        var quarantined = await _harness.SingleEntryAsync();

        _harness.Session.DeleteFailures.Clear();
        var replay = await _harness.SendAsync(new ReplayQuarantinedFileCommand(quarantined.Id));
        await _harness.PumpAsync();

        quarantined.State.Should().Be(InboundFileState.Quarantined);
        replay.State.Should().Be(InboundFileState.BillingQueued);
        _harness.Billing.Registered.Should().HaveCount(1);
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
    }

    [Fact]
    public async Task Lost_messages_are_recovered_by_a_later_listing()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();
        _harness.Queue.Clear();

        _harness.Time.Advance(TimeSpan.FromMinutes(121));
        var recovery = await _harness.ListAsync();
        await _harness.PumpAsync();

        recovery.Requeued.Should().Be(1);
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
        _harness.Sink.ByEventId(1105).Should().ContainSingle();
    }

    [Fact]
    public async Task Worker_crash_mid_download_is_reclaimed_only_after_the_claim_expires()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();
        _harness.Queue.TryTakeDue(out var message).Should().BeTrue();
        var entry = await _harness.SingleEntryAsync();
        var crashed = InboundFileTransition.Create(entry.Id, InboundFileState.Downloading, InboundFileState.Queued) with
        {
            ClaimExpiresUtc = _harness.Time.GetUtcNow().UtcDateTime.AddMinutes(30),
        };
        (await _harness.Manifest.TryTransitionAsync(crashed, CancellationToken.None)).Should().BeTrue();

        _harness.Queue.Duplicate(message.Stage, message.Item);
        await _harness.PumpAsync();
        var whileClaimed = await _harness.SingleEntryAsync();
        _harness.Time.Advance(TimeSpan.FromMinutes(31));
        _harness.Queue.Duplicate(message.Stage, message.Item);
        await _harness.PumpAsync();

        whileClaimed.State.Should().Be(InboundFileState.Downloading);
        _harness.Sink.Entries.Should().Contain(entry => entry.EventId.Id == 1111 || entry.EventId.Id == 1112);
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
    }

    [Fact]
    public async Task File_that_changed_after_listing_is_superseded()
    {
        _harness.Session.With(TestData.File("a.835", size: 999), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();

        await _harness.PumpAsync();

        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Superseded);
        _harness.Billing.Registered.Should().BeEmpty();
        _harness.Sink.ByEventId(1114).Should().ContainSingle();
    }

    [Fact]
    public async Task Landing_storage_outage_is_retried()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Landing.WriteFailure = new IOException("blob 503");
        await _harness.ListAsync();

        await _harness.PumpAsync();

        var entry = await _harness.SingleEntryAsync();
        entry.State.Should().Be(InboundFileState.Queued);
        entry.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Landing_read_outage_is_retried_back_to_landed()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Landing.ReadFailure = new IOException("blob 503");
        await _harness.ListAsync();

        await _harness.PumpAsync();

        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Landed);
    }

    [Fact]
    public async Task Queue_outage_during_listing_fails_the_run_and_the_next_listing_sends_the_files()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Queue.SendFailure = new InvalidOperationException("service bus down");

        var failed = await _harness.ListAsync();
        var stranded = await _harness.SingleEntryAsync();
        _harness.Queue.SendFailure = null;
        var next = await _harness.ListAsync();

        failed.Kind.Should().Be(InboundListingKind.Failed);
        stranded.State.Should().Be(InboundFileState.Discovered);
        next.Enqueued.Should().Be(1);
        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Queued);
    }

    [Fact]
    public async Task Sftp_list_failure_fails_the_run_and_releases_the_lease()
    {
        _harness.Session.ListFailure = new IOException("auth failed");

        var result = await _harness.ListAsync();

        result.Kind.Should().Be(InboundListingKind.Failed);
        _harness.Leases.Released.Should().Be(1);
    }

    [Fact]
    public async Task Held_lease_skips_the_run()
    {
        _harness.Leases.HeldElsewhere.Add("inbound-lister-1");

        var result = await _harness.ListAsync();

        result.Should().Be(InboundListingResult.NotRun(1, InboundListingKind.LeaseHeld));
        _harness.SessionFactory.Verify(f => f.OpenAsync(It.IsAny<ClearingHouseDescriptor>(), It.IsAny<CancellationToken>()), Times.Never);
        _harness.Sink.ByEventId(1101).Should().ContainSingle();
    }

    [Fact]
    public async Task Unknown_clearing_house_is_invalid()
    {
        var result = await _harness.ListAsync(99);

        result.Should().Be(InboundListingResult.NotRun(99, InboundListingKind.Invalid));
    }

    [Fact]
    public async Task Files_younger_than_the_minimum_age_wait_for_the_next_listing()
    {
        await _harness.DisposeAsync();
        _harness = new PipelineHarness(settings: new Dictionary<string, string?> { ["Pipeline:MinimumFileAgeSeconds"] = "7200" });
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));

        var result = await _harness.ListAsync();

        result.Should().Be(new InboundListingResult(1, InboundListingKind.Completed, 1, 0, 0, 0));
    }

    [Fact]
    public async Task Shadow_mode_lands_files_but_never_bills_or_deletes()
    {
        await _harness.DisposeAsync();
        _harness = new PipelineHarness(PipelineMode.Shadow);
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));

        await _harness.ListAsync();
        await _harness.PumpAsync();
        var entry = await _harness.SingleEntryAsync();
        _harness.Queue.Duplicate(InboundWorkStage.Process, InboundFileWorkItem.First(entry));
        await _harness.PumpAsync();

        entry.State.Should().Be(InboundFileState.Landed);
        _harness.Billing.Registered.Should().BeEmpty();
        _harness.Session.Deleted.Should().BeEmpty();
        _harness.Queue.Sent.Should().OnlyContain(sent => sent.Stage == InboundWorkStage.Download);
        _harness.Sink.ByEventId(1106).Should().ContainSingle();
    }

    [Fact]
    public async Task Off_mode_lists_nothing()
    {
        await _harness.DisposeAsync();
        _harness = new PipelineHarness(PipelineMode.Off, new Dictionary<string, string?> { ["Pipeline:ClearingHouseIds:0"] = null });

        var result = await _harness.ListAsync();

        result.Should().Be(InboundListingResult.NotRun(1, InboundListingKind.Disabled));
        _harness.Sink.ByEventId(1103).Should().ContainSingle();
    }

    [Fact]
    public async Task Keeping_remote_files_finishes_without_deleting()
    {
        await _harness.DisposeAsync();
        _harness = new PipelineHarness(settings: new Dictionary<string, string?> { ["InboundDownload:DeleteRemoteFiles"] = "false" });
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));

        await _harness.ListAsync();
        await _harness.PumpAsync();

        (await _harness.SingleEntryAsync()).State.Should().Be(InboundFileState.Done);
        _harness.Session.Deleted.Should().BeEmpty();
    }

    [Fact]
    public async Task Backlog_and_quarantine_queries_read_the_manifest()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~")).With(TestData.File("b.999", 1, size: 999), TestData.Bytes("ISA*b.999~"));
        _harness.Session.DownloadFailures["a.835"] = new IOException("down");
        await _harness.ListAsync();
        await _harness.PumpAsync();
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);
        await _harness.AdvanceAndPumpAsync(PastAnyRetry);

        var backlog = await _harness.SendAsync(new GetPipelineBacklogQuery());
        var quarantined = await _harness.SendAsync(new ListQuarantinedFilesQuery(0));

        backlog.CountsByState[InboundFileState.Quarantined].Should().Be(1);
        backlog.CountsByState[InboundFileState.Superseded].Should().Be(1);
        backlog.CountsByState[InboundFileState.Done].Should().Be(0);
        backlog.OldestPendingDiscoveredUtc.Should().BeNull();
        quarantined.Should().ContainSingle().Which.FileName.Should().Be("a.835");
    }

    [Fact]
    public async Task Backlog_reports_the_oldest_unfinished_file()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        await _harness.ListAsync();

        var backlog = await _harness.SendAsync(new GetPipelineBacklogQuery());

        backlog.CountsByState[InboundFileState.Queued].Should().Be(1);
        backlog.OldestPendingDiscoveredUtc.Should().Be(_harness.Time.GetUtcNow().UtcDateTime);
        backlog.OldestPendingDiscoveredUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Manifest_outage_during_listing_fails_the_run()
    {
        _harness.Session.With(TestData.File("a.835", size: 10), TestData.Bytes("ISA*a.835~"));
        _harness.Database.Failure = new TimeoutException("sql timeout");

        var result = await _harness.ListAsync();

        result.Kind.Should().Be(InboundListingKind.Failed);
    }
}
