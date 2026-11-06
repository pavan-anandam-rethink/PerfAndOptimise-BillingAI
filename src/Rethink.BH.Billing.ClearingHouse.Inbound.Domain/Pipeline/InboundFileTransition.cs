namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>
/// A compare-and-set state change. It applies only while the file is in one of <see cref="FromStates"/>, or — when
/// <see cref="ReclaimExpiredBeforeUtc"/> is set — while it is already in <see cref="ToState"/> with a claim that expired
/// before that time. Two workers that receive the same message therefore cannot both claim the file.
/// Null optional values leave the stored value unchanged.
/// </summary>
public sealed record InboundFileTransition
{
    /// <summary>File to change.</summary>
    public required Guid FileId { get; init; }

    /// <summary>States the change is allowed from.</summary>
    public required IReadOnlyCollection<InboundFileState> FromStates { get; init; }

    /// <summary>New state.</summary>
    public required InboundFileState ToState { get; init; }

    /// <summary>When set, a file already in <see cref="ToState"/> whose claim expired before this time is also matched.</summary>
    public DateTime? ReclaimExpiredBeforeUtc { get; init; }

    /// <summary>New claim expiry, or null to keep it.</summary>
    public DateTime? ClaimExpiresUtc { get; init; }

    /// <summary>New attempt count, or null to keep it.</summary>
    public int? Attempts { get; init; }

    /// <summary>Landing blob name, or null to keep it.</summary>
    public string? LandingBlobName { get; init; }

    /// <summary>SHA-256 of the landed content, or null to keep it.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Billing file id, or null to keep it.</summary>
    public int? BillingFileId { get; init; }

    /// <summary>When Billing queued the file, or null to keep it.</summary>
    public DateTime? BillingQueuedUtc { get; init; }

    /// <summary>Failure reason, or null to keep it.</summary>
    public string? LastError { get; init; }

    /// <summary>A transition with no optional values.</summary>
    public static InboundFileTransition Create(Guid fileId, InboundFileState toState, params InboundFileState[] fromStates)
        => new() { FileId = fileId, ToState = toState, FromStates = fromStates };
}
