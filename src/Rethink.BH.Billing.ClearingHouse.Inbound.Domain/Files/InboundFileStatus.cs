namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

/// <summary>Where processing of one downloaded file stopped. Not returned on the HTTP response.</summary>
public enum InboundFileStatus
{
    /// <summary>The backup blob was written.</summary>
    Stored,

    /// <summary>Billing accepted the file but did not queue it.</summary>
    Registered,

    /// <summary>Billing queued the file. The remote file is still present when remote delete is off.</summary>
    Queued,

    /// <summary>The remote file was deleted after Billing queued it.</summary>
    RemoteDeleted,

    /// <summary>The file could not be downloaded, backed up, or registered.</summary>
    Failed,
}
