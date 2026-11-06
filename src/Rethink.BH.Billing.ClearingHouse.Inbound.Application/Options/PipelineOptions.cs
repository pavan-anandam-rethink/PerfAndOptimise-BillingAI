using System.ComponentModel.DataAnnotations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;

/// <summary>How the queue pipeline runs.</summary>
public enum PipelineMode
{
    /// <summary>The pipeline does nothing. The HTTP download endpoint is the only path (legacy behavior).</summary>
    Off,

    /// <summary>List and land files, but never call Billing or delete remote files. Used to compare with the legacy path.</summary>
    Shadow,

    /// <summary>List, land, register with Billing, and delete remote files.</summary>
    Active,
}

/// <summary>Settings for the queue pipeline. Bound from <c>Pipeline</c>.</summary>
public sealed class PipelineOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Pipeline";

    /// <summary>Pipeline mode. Defaults to <see cref="PipelineMode.Off"/>.</summary>
    public PipelineMode Mode { get; set; } = PipelineMode.Off;

    /// <summary>Clearing houses the scheduled lister lists.</summary>
    public int[] ClearingHouseIds { get; set; } = [];

    /// <summary>Files modified more recently than this are left for the next listing, so a file still being uploaded is never taken.</summary>
    [Range(0, 86_400)]
    public int MinimumFileAgeSeconds { get; set; } = 120;

    /// <summary>Download messages handled at once by one worker replica. Bounded by the SFTP pool size.</summary>
    [Range(1, 64)]
    public int DownloadConcurrency { get; set; } = 4;

    /// <summary>Process messages handled at once by one worker replica.</summary>
    [Range(1, 64)]
    public int ProcessConcurrency { get; set; } = 4;

    /// <summary>Failed attempts before a file is quarantined.</summary>
    [Range(1, 50)]
    public int MaxAttempts { get; set; } = 5;

    /// <summary>Delay before the first retry. Later retries double it.</summary>
    [Range(1, 3_600)]
    public int RetryBaseDelaySeconds { get; set; } = 30;

    /// <summary>Longest delay between retries.</summary>
    [Range(1, 86_400)]
    public int RetryMaxDelaySeconds { get; set; } = 1_800;

    /// <summary>
    /// How long a worker owns an in-progress file. A redelivered message can reclaim the file only after this, so it must
    /// exceed the longest download or Billing call.
    /// </summary>
    [Range(1, 1_440)]
    public int ClaimTimeoutMinutes { get; set; } = 30;

    /// <summary>Unfinished files untouched for this long are re-enqueued by the next lister run (crash recovery).</summary>
    [Range(5, 10_080)]
    public int StalledAfterMinutes { get; set; } = 120;

    /// <summary>Most stalled files re-enqueued per lister run.</summary>
    [Range(1, 10_000)]
    public int StalledBatchSize { get; set; } = 500;

    /// <summary>True when Billing registration and remote delete run.</summary>
    public bool IsActive => Mode == PipelineMode.Active;

    /// <summary>The claim timeout as a time span.</summary>
    public TimeSpan ClaimTimeout => TimeSpan.FromMinutes(ClaimTimeoutMinutes);

    /// <summary>The stalled-file age as a time span.</summary>
    public TimeSpan StalledAfter => TimeSpan.FromMinutes(StalledAfterMinutes);

    /// <summary>True unless the mode is <see cref="PipelineMode.Off"/>.</summary>
    public bool IsEnabled => Mode != PipelineMode.Off;

    /// <summary>The lister's stable-file age as a time span.</summary>
    public TimeSpan MinimumFileAge => TimeSpan.FromSeconds(MinimumFileAgeSeconds);
}
