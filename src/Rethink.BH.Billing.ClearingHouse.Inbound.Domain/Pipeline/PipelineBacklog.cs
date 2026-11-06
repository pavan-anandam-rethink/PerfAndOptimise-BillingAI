namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>Operational snapshot of the manifest.</summary>
/// <param name="CountsByState">Number of files in each state. States with no files are included with zero.</param>
/// <param name="OldestPendingDiscoveredUtc">Discovery time of the oldest file that is not finished, or null when none is pending.</param>
public sealed record PipelineBacklog(IReadOnlyDictionary<InboundFileState, int> CountsByState, DateTime? OldestPendingDiscoveredUtc)
{
    /// <summary>States that need no more work.</summary>
    public static readonly IReadOnlySet<InboundFileState> FinishedStates = new HashSet<InboundFileState>
    {
        InboundFileState.Done,
        InboundFileState.Superseded,
        InboundFileState.Quarantined,
    };
}
