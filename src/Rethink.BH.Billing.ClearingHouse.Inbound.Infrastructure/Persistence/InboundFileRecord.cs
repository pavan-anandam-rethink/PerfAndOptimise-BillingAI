namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

/// <summary>Row of <c>inbound.InboundFile</c>. State is stored by name so the table stays readable and enum values can be added.</summary>
public sealed class InboundFileRecord
{
    /// <summary>Primary key; also the queue message id.</summary>
    public Guid Id { get; set; }

    /// <summary>Clearing house id.</summary>
    public int ClearingHouseId { get; set; }

    /// <summary>Full remote path.</summary>
    public string RemotePath { get; set; } = string.Empty;

    /// <summary>Remote file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Listed size.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Listed modified time (UTC).</summary>
    public DateTime RemoteModifiedUtc { get; set; }

    /// <summary>State name.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>Failed attempts.</summary>
    public int Attempts { get; set; }

    /// <summary>Landing blob name.</summary>
    public string? LandingBlobName { get; set; }

    /// <summary>Upper-case hex SHA-256 of the landed content.</summary>
    public string? Sha256 { get; set; }

    /// <summary>Billing file id.</summary>
    public int? BillingFileId { get; set; }

    /// <summary>When Billing queued the file (UTC).</summary>
    public DateTime? BillingQueuedUtc { get; set; }

    /// <summary>Last failure reason.</summary>
    public string? LastError { get; set; }

    /// <summary>Claim expiry of the current worker (UTC).</summary>
    public DateTime? ClaimExpiresUtc { get; set; }

    /// <summary>Registration time (UTC).</summary>
    public DateTime DiscoveredUtc { get; set; }

    /// <summary>Last change (UTC).</summary>
    public DateTime UpdatedUtc { get; set; }
}
