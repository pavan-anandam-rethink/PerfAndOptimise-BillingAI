namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

/// <summary>How a download run is reported to the scheduler.</summary>
public enum DownloadRunKind
{
    /// <summary>The id is not in the configured clearing house catalog.</summary>
    Invalid,

    /// <summary>The download directory had no files, or the run limit stopped the run before any file finished.</summary>
    Empty,

    /// <summary>Every listed file finished, or was intentionally left on SFTP because remote delete is off.</summary>
    Completed,

    /// <summary>At least one file failed, or the SFTP connection failed.</summary>
    Failed,
}
