using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.TestSupport;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Application;

public sealed class InboundFileProcessorTests
{
    private readonly RemoteFile _file = TestData.File("AV_999_20261104_0001.x12");

    [Fact]
    public async Task Happy_path_backs_up_registers_and_deletes()
    {
        var pipeline = new DownloadPipeline();
        var content = TestData.Sample("AV_999_20261104_0001.x12");
        pipeline.Session.With(_file, content);
        var progress = new InboundFileProgress();

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, progress, CancellationToken.None);

        outcome.Should().Be(InboundFileOutcome.Completed(_file.Name, InboundFileStatus.RemoteDeleted));
        pipeline.Backup.Stored[_file.Name].Should().Equal(content);
        pipeline.Billing.Registered[_file.Name].Should().BeEquivalentTo((1, "Availity", content));
        pipeline.Session.Deleted.Should().ContainSingle().Which.Should().Be(_file.Name);
        progress.BackupStored.Should().BeTrue();
        progress.BillingQueued.Should().BeTrue();
        pipeline.Sink.SingleEvent(1010).State["BackupState"].Should().Be("uploaded");
        pipeline.Sink.SingleEvent(1011).State["Accepted"].Should().Be(true);
        pipeline.Sink.SingleEvent(1014).State["FileStatus"].Should().Be("RemoteDeleted");
    }

    [Fact]
    public async Task Downloaded_content_is_released_before_the_remote_delete()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);

        await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        pipeline.Session.Downloads.Should().ContainSingle().Which.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task Existing_backup_is_logged_as_already_existed()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);
        pipeline.Backup.Existing.Add(_file.Name);

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        outcome.Status.Should().Be(InboundFileStatus.RemoteDeleted);
        pipeline.Sink.SingleEvent(1010).State["BackupState"].Should().Be("already existed");
    }

    [Fact]
    public async Task Billing_rejection_keeps_file_on_sftp_with_trimmed_reason()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);
        pipeline.Billing.Responses[_file.Name] = BillingRegistration.Rejected("  duplicate file  ");
        var progress = new InboundFileProgress();

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, progress, CancellationToken.None);

        outcome.Should().Be(InboundFileOutcome.Failed(_file.Name, InboundFileStatus.Stored, "duplicate file"));
        pipeline.Session.Deleted.Should().BeEmpty();
        progress.BackupStored.Should().BeTrue();
        progress.BillingQueued.Should().BeFalse();
        var warning = pipeline.Sink.SingleEvent(1012);
        warning.Level.Should().Be(LogLevel.Warning);
        warning.State["BackupUploaded"].Should().Be(true);
        warning.State["BillingUploaded"].Should().Be(false);
    }

    [Fact]
    public async Task Billing_rejection_without_reason_uses_legacy_message()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);
        pipeline.Billing.Responses[_file.Name] = BillingRegistration.Rejected(null);

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        outcome.Failure.Should().Be(InboundFailureMessages.BillingRejected);
        outcome.Status.Should().Be(InboundFileStatus.Stored);
    }

    [Fact]
    public async Task Billing_registered_but_not_queued_keeps_file()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);
        pipeline.Billing.Responses[_file.Name] = BillingRegistration.NotQueued(55);

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        outcome.Should().Be(InboundFileOutcome.Failed(_file.Name, InboundFileStatus.Registered, InboundFailureMessages.BillingDidNotQueue));
        pipeline.Session.Deleted.Should().BeEmpty();
        pipeline.Sink.SingleEvent(1011).State["BillingFileId"].Should().Be(55);
    }

    [Fact]
    public async Task Remote_delete_off_completes_as_queued()
    {
        var pipeline = new DownloadPipeline(options => options.DeleteRemoteFiles = false);
        pipeline.Session.With(_file);

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        outcome.Should().Be(InboundFileOutcome.Completed(_file.Name, InboundFileStatus.Queued));
        pipeline.Session.Deleted.Should().BeEmpty();
        pipeline.Sink.SingleEvent(1013).State["FileName"].Should().Be(_file.Name);
    }

    [Fact]
    public async Task Remote_delete_failure_is_logged_and_reported_as_queued_failure()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);
        pipeline.Session.DeleteFailures[_file.Name] = new IOException("permission denied");

        var outcome = await pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        outcome.Should().Be(InboundFileOutcome.Failed(_file.Name, InboundFileStatus.Queued, InboundFailureMessages.RemoteDeleteFailed));
        var fault = pipeline.Sink.SingleEvent(1020);
        fault.State["OperationName"].Should().Be("DeleteRemoteFile");
        fault.Exception.Should().BeOfType<IOException>();
    }

    [Fact]
    public async Task Download_failure_propagates_to_the_batch_boundary()
    {
        var pipeline = new DownloadPipeline();
        pipeline.Session.With(_file);
        pipeline.Session.DownloadFailures[_file.Name] = new IOException("connection reset");

        var act = () => pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, new InboundFileProgress(), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>().WithMessage("connection reset");
        pipeline.Backup.Stored.Should().BeEmpty();
        pipeline.Billing.Registered.Should().BeEmpty();
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        var pipeline = new DownloadPipeline();
        var progress = new InboundFileProgress();

        await FluentActions.Invoking(() => pipeline.Processor.ProcessAsync(TestData.Availity, null!, _file, progress, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, null!, progress, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => pipeline.Processor.ProcessAsync(TestData.Availity, pipeline.Session, _file, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
    }
}
