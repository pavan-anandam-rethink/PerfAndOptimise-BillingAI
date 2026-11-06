namespace Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Pipeline;

/// <summary>Pipeline backlog: file count per state and the discovery time of the oldest unfinished file.</summary>
/// <param name="CountsByState">Files per state name.</param>
/// <param name="OldestPendingDiscoveredUtc">Oldest unfinished file, or null when nothing is pending.</param>
public sealed record PipelineBacklogResponse(IReadOnlyDictionary<string, int> CountsByState, DateTime? OldestPendingDiscoveredUtc);

/// <summary>
/// A quarantined file. File names and error text stay off the response; search the logs by <paramref name="FileId"/>
/// for the failure detail.
/// </summary>
/// <param name="FileId">Manifest id, used for replay and log search.</param>
/// <param name="ClearingHouseId">Clearing house.</param>
/// <param name="SizeBytes">Listed size.</param>
/// <param name="Attempts">Failed attempts.</param>
/// <param name="DiscoveredUtc">When the lister first saw the file.</param>
/// <param name="UpdatedUtc">When the file was quarantined.</param>
public sealed record QuarantinedFileResponse(Guid FileId, int ClearingHouseId, long SizeBytes, int Attempts, DateTime DiscoveredUtc, DateTime UpdatedUtc);

/// <summary>Result of a replay request.</summary>
/// <param name="FileId">Manifest id.</param>
/// <param name="Outcome">NotFound, NotQuarantined, or Requeued.</param>
/// <param name="State">State after the request, or null when not found.</param>
public sealed record ReplayResponse(Guid FileId, string Outcome, string? State);

/// <summary>Result of an on-demand listing.</summary>
/// <param name="ClearingHouseId">Clearing house listed.</param>
/// <param name="Outcome">Completed, Disabled, Invalid, LeaseHeld, or Failed.</param>
/// <param name="Listed">Files in the remote directory.</param>
/// <param name="Selected">Files old enough to take.</param>
/// <param name="Enqueued">Download messages sent.</param>
/// <param name="Requeued">Stalled files sent again.</param>
public sealed record ListingResponse(int ClearingHouseId, string Outcome, int Listed, int Selected, int Enqueued, int Requeued);
