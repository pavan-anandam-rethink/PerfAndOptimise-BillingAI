using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.UnitTests.Domain;

public sealed class PipelineStagesTests
{
    [Theory]
    [InlineData(InboundFileState.Discovered, InboundWorkStage.Download)]
    [InlineData(InboundFileState.Queued, InboundWorkStage.Download)]
    [InlineData(InboundFileState.Downloading, InboundWorkStage.Download)]
    [InlineData(InboundFileState.Landed, InboundWorkStage.Process)]
    [InlineData(InboundFileState.Processing, InboundWorkStage.Process)]
    [InlineData(InboundFileState.BillingQueued, InboundWorkStage.Process)]
    [InlineData(InboundFileState.Deleting, InboundWorkStage.Process)]
    public void Unfinished_states_map_to_the_stage_that_continues_them(InboundFileState state, InboundWorkStage expected)
        => PipelineStages.StageFor(state).Should().Be(expected);

    [Theory]
    [InlineData(InboundFileState.Done)]
    [InlineData(InboundFileState.Superseded)]
    [InlineData(InboundFileState.Quarantined)]
    public void Finished_states_need_no_stage(InboundFileState state)
        => PipelineStages.StageFor(state).Should().BeNull();

    [Fact]
    public void Recovery_states_are_exactly_the_unfinished_states()
    {
        PipelineStages.AllRecoveryStates.Should().OnlyContain(state => PipelineStages.StageFor(state) != null);
        PipelineStages.DownloadRecoveryStates.Should().OnlyContain(state => PipelineStages.StageFor(state) == InboundWorkStage.Download);
    }

    [Theory]
    [InlineData(null, null, false, InboundFileState.Queued)]
    [InlineData("blob", null, false, InboundFileState.Queued)]
    [InlineData(null, "hash", false, InboundFileState.Queued)]
    [InlineData("blob", "hash", false, InboundFileState.Landed)]
    [InlineData(null, null, true, InboundFileState.BillingQueued)]
    public void Quarantined_files_resume_from_the_last_safe_state(string? blob, string? sha256, bool billingQueued, InboundFileState expected)
    {
        var entry = new InboundFileEntry
        {
            Id = Guid.NewGuid(),
            ClearingHouseId = 1,
            RemotePath = "/ReceiveFiles/a.x12",
            FileName = "a.x12",
            SizeBytes = 1,
            RemoteModifiedUtc = DateTime.UtcNow,
            State = InboundFileState.Quarantined,
            LandingBlobName = blob,
            Sha256 = sha256,
            BillingQueuedUtc = billingQueued ? DateTime.UtcNow : null,
            DiscoveredUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        PipelineStages.ResumeStateFor(entry).Should().Be(expected);
        FluentActions.Invoking(() => PipelineStages.ResumeStateFor(null!)).Should().Throw<ArgumentNullException>();
    }
}
