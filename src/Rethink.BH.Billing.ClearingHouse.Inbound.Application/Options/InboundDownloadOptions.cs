using System.ComponentModel.DataAnnotations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;

/// <summary>Settings for one inbound download run. Bound from <c>InboundDownload</c>.</summary>
public sealed class InboundDownloadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "InboundDownload";

    /// <summary>Stops the run so the caller finishes inside its timeout. Remaining files stay on SFTP.</summary>
    [Range(1, 1440)]
    public int MaxRunMinutes { get; set; } = 13;

    /// <summary>How many files are processed at once.</summary>
    [Range(1, 64)]
    public int BatchConcurrency { get; set; } = 2;

    /// <summary>Maximum SFTP connections. List, download, and delete share the pool.</summary>
    [Range(1, 64)]
    public int SftpPoolSize { get; set; } = 4;

    /// <summary>SSH connect timeout for each pooled connection.</summary>
    [Range(1, 600)]
    public int ConnectTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum length of a failure reason kept on a file outcome.</summary>
    [Range(1, 10_000)]
    public int FailureMessageMaxLength { get; set; } = 500;

    /// <summary>When false, files are stored and registered but left on SFTP (shadow comparison mode).</summary>
    public bool DeleteRemoteFiles { get; set; } = true;

    /// <summary>
    /// Files modified more recently than this are skipped until the next run, so a file still being written is never read.
    /// Zero disables the check and processes every listed file.
    /// </summary>
    [Range(0, 86_400)]
    public int MinimumFileAgeSeconds { get; set; }

    /// <summary>Retries of a transient SFTP connect, list, or download failure. Zero disables retries.</summary>
    [Range(0, 10)]
    public int SftpRetryCount { get; set; }

    /// <summary>First retry delay. Later retries back off exponentially with jitter.</summary>
    [Range(1, 60_000)]
    public int SftpRetryBaseDelayMilliseconds { get; set; } = 500;

    /// <summary>The run limit as a time span.</summary>
    public TimeSpan MaxRunDuration => TimeSpan.FromMinutes(MaxRunMinutes);
}
