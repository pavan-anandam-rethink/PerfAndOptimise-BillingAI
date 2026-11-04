using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Domain;

public sealed class DomainModelTests
{
    [Fact]
    public void Completed_outcome_has_no_failure()
    {
        var outcome = InboundFileOutcome.Completed("a.x12", InboundFileStatus.RemoteDeleted);

        outcome.FileName.Should().Be("a.x12");
        outcome.Status.Should().Be(InboundFileStatus.RemoteDeleted);
        outcome.Failure.Should().BeNull();
        outcome.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void Failed_outcome_carries_status_and_failure()
    {
        var outcome = InboundFileOutcome.Failed("a.x12", InboundFileStatus.Stored, "nope");

        outcome.Status.Should().Be(InboundFileStatus.Stored);
        outcome.Failure.Should().Be("nope");
        outcome.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Outcome_with_empty_failure_is_not_failed()
    {
        new InboundFileOutcome("a.x12", InboundFileStatus.Queued, string.Empty).IsFailed.Should().BeFalse();
    }

    [Fact]
    public void Run_factories_set_kind_and_empty_files()
    {
        InboundDownloadResult.Invalid(5).Should().BeEquivalentTo(new { Kind = DownloadRunKind.Invalid, ClearingHouseId = 5, FailedCount = 0 });
        InboundDownloadResult.Empty(5).Kind.Should().Be(DownloadRunKind.Empty);
        InboundDownloadResult.ConnectionFailed(5).Kind.Should().Be(DownloadRunKind.Failed);
        InboundDownloadResult.ConnectionFailed(5).Files.Should().BeEmpty();
    }

    [Fact]
    public void FromOutcomes_classifies_like_the_legacy_service()
    {
        var completed = InboundFileOutcome.Completed("a", InboundFileStatus.RemoteDeleted);
        var failed = InboundFileOutcome.Failed("b", InboundFileStatus.Stored, "x");

        InboundDownloadResult.FromOutcomes(1, []).Kind.Should().Be(DownloadRunKind.Empty);
        InboundDownloadResult.FromOutcomes(1, [completed]).Kind.Should().Be(DownloadRunKind.Completed);

        var mixed = InboundDownloadResult.FromOutcomes(1, [completed, failed]);
        mixed.Kind.Should().Be(DownloadRunKind.Failed);
        mixed.FailedCount.Should().Be(1);
        mixed.Files.Should().HaveCount(2);
    }

    [Fact]
    public void FromOutcomes_rejects_null()
    {
        var act = () => InboundDownloadResult.FromOutcomes(1, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejected_registration_without_reason_uses_legacy_message(string? failure)
    {
        var registration = BillingRegistration.Rejected(failure);

        registration.Accepted.Should().BeFalse();
        registration.Queued.Should().BeFalse();
        registration.FileId.Should().BeNull();
        registration.Failure.Should().Be(InboundFailureMessages.BillingRejected);
    }

    [Fact]
    public void Registration_factories_set_flags()
    {
        BillingRegistration.Rejected("bad").Failure.Should().Be("bad");
        BillingRegistration.NotQueued(7).Should().Be(new BillingRegistration(true, false, 7, InboundFailureMessages.BillingDidNotQueue));
        BillingRegistration.Succeeded(7).Should().Be(new BillingRegistration(true, true, 7, null));
    }

    [Fact]
    public void Failure_messages_match_legacy_text()
    {
        InboundFailureMessages.BillingRejected.Should().Be("Billing rejected the file.");
        InboundFailureMessages.BillingDidNotQueue.Should().Be("Billing did not queue the file.");
        InboundFailureMessages.RemoteDeleteFailed.Should().Be("Remote delete failed.");
        InboundFailureMessages.DownloadFailed.Should().Be("Download failed.");
    }

    [Fact]
    public void Backup_result_is_a_value_object()
    {
        new BackupWriteResult("a", true).Should().Be(new BackupWriteResult("a", true));
        new BackupWriteResult("a", true).Should().NotBe(new BackupWriteResult("a", false));
    }
}
