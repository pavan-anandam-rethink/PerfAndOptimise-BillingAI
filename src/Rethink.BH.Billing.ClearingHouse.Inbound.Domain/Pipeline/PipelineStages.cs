namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>Which stage picks up a file in each unfinished state.</summary>
public static class PipelineStages
{
    /// <summary>Unfinished states handled by the download stage (the lister itself re-sends Discovered files).</summary>
    public static readonly IReadOnlyCollection<InboundFileState> DownloadRecoveryStates = [InboundFileState.Queued, InboundFileState.Downloading];

    /// <summary>Unfinished states handled by either stage.</summary>
    public static readonly IReadOnlyCollection<InboundFileState> AllRecoveryStates =
    [
        InboundFileState.Queued,
        InboundFileState.Downloading,
        InboundFileState.Landed,
        InboundFileState.Processing,
        InboundFileState.BillingQueued,
        InboundFileState.Deleting,
    ];

    /// <summary>The stage that continues a file in <paramref name="state"/>, or null when the file needs no more work.</summary>
    public static InboundWorkStage? StageFor(InboundFileState state) => state switch
    {
        InboundFileState.Discovered or InboundFileState.Queued or InboundFileState.Downloading => InboundWorkStage.Download,
        InboundFileState.Landed or InboundFileState.Processing or InboundFileState.BillingQueued or InboundFileState.Deleting => InboundWorkStage.Process,
        _ => null,
    };

    /// <summary>The state a quarantined file resumes from: after Billing queued it, only the delete is repeated.</summary>
    public static InboundFileState ResumeStateFor(InboundFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.BillingQueuedUtc is not null)
        {
            return InboundFileState.BillingQueued;
        }

        return entry.LandingBlobName is not null && entry.Sha256 is not null ? InboundFileState.Landed : InboundFileState.Queued;
    }
}
