namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>Outcome kind of one lister run.</summary>
public enum InboundListingKind
{
    /// <summary>Listed, registered, and enqueued.</summary>
    Completed,

    /// <summary>The pipeline is off; nothing was listed.</summary>
    Disabled,

    /// <summary>The clearing house id is not configured.</summary>
    Invalid,

    /// <summary>Another lister holds the lease for this clearing house.</summary>
    LeaseHeld,

    /// <summary>Connect, list, registration, or enqueue failed. Logged by the fault boundary.</summary>
    Failed,
}

/// <summary>Summary of one lister run.</summary>
/// <param name="ClearingHouseId">Clearing house listed.</param>
/// <param name="Kind">Outcome kind.</param>
/// <param name="Listed">Files in the remote directory.</param>
/// <param name="Selected">Files old enough to be complete.</param>
/// <param name="Enqueued">Download messages sent (new files plus files a previous run registered but did not enqueue).</param>
/// <param name="Requeued">Stalled unfinished files sent again (crash recovery).</param>
public sealed record InboundListingResult(int ClearingHouseId, InboundListingKind Kind, int Listed, int Selected, int Enqueued, int Requeued)
{
    /// <summary>A run that did not list.</summary>
    public static InboundListingResult NotRun(int clearingHouseId, InboundListingKind kind) => new(clearingHouseId, kind, 0, 0, 0, 0);
}
