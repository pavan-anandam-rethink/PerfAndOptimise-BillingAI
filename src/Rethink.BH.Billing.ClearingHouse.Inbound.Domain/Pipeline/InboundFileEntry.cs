using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>
/// One tracked remote file. A file is identified by clearing house, remote path, size, and modified time, so a re-uploaded
/// file with new content is tracked as a new entry and an unchanged file is never processed twice.
/// </summary>
public sealed record InboundFileEntry
{
    /// <summary>Manifest id; also the id carried by queue messages.</summary>
    public required Guid Id { get; init; }

    /// <summary>Clearing house the file was listed from.</summary>
    public required int ClearingHouseId { get; init; }

    /// <summary>Full remote path.</summary>
    public required string RemotePath { get; init; }

    /// <summary>Remote file name.</summary>
    public required string FileName { get; init; }

    /// <summary>Size reported by the listing.</summary>
    public required long SizeBytes { get; init; }

    /// <summary>Modified time reported by the listing.</summary>
    public required DateTime RemoteModifiedUtc { get; init; }

    /// <summary>Current state.</summary>
    public required InboundFileState State { get; init; }

    /// <summary>Failed attempts so far.</summary>
    public int Attempts { get; init; }

    /// <summary>Landing blob name once landed.</summary>
    public string? LandingBlobName { get; init; }

    /// <summary>Upper-case hex SHA-256 of the landed content.</summary>
    public string? Sha256 { get; init; }

    /// <summary>Billing file id once Billing queued the file, when Billing returned one.</summary>
    public int? BillingFileId { get; init; }

    /// <summary>When Billing accepted and queued the file. Once set, the file is never sent to Billing again.</summary>
    public DateTime? BillingQueuedUtc { get; init; }

    /// <summary>Last failure reason, already trimmed and truncated.</summary>
    public string? LastError { get; init; }

    /// <summary>When the current worker's claim on an in-progress state expires; another worker may reclaim after that.</summary>
    public DateTime? ClaimExpiresUtc { get; init; }

    /// <summary>When the lister registered the file.</summary>
    public required DateTime DiscoveredUtc { get; init; }

    /// <summary>Last state change.</summary>
    public required DateTime UpdatedUtc { get; init; }

    /// <summary>The listing view of this file, for SFTP operations.</summary>
    public RemoteFile ToRemoteFile() => new(RemotePath, FileName, SizeBytes, RemoteModifiedUtc);
}
