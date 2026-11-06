using System.ComponentModel.DataAnnotations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>Landing container for streamed downloads and the lister lease blobs. Bound from <c>Pipeline:Landing</c>.</summary>
public sealed class LandingStorageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Pipeline:Landing";

    /// <summary>Container name. Created by the platform with encryption at rest, soft delete, and lifecycle rules.</summary>
    [Required]
    public string ContainerName { get; set; } = "inbound-landing";

    /// <summary>Block size used when staging a streamed download, in KiB. Memory per download in flight is one block.</summary>
    [Range(256, 102_400)]
    public int BlockSizeKiB { get; set; } = 4_096;

    /// <summary>Lister lease duration in seconds (Azure allows 15–60). The lease is renewed at a third of this.</summary>
    [Range(15, 60)]
    public int LeaseSeconds { get; set; } = 60;
}
