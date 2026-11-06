using System.ComponentModel.DataAnnotations;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;

/// <summary>
/// Service Bus namespace and stage queues. Bound from <c>ServiceBus</c>. Prefer <see cref="FullyQualifiedNamespace"/>
/// (workload identity, no secret); <see cref="ConnectionString"/> may be a literal or a Key Vault secret name.
/// </summary>
public sealed class ServiceBusOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ServiceBus";

    /// <summary>Configuration key of the connection string, used in error messages.</summary>
    public const string ConnectionStringKey = "ServiceBus:ConnectionString";

    /// <summary>Namespace host, for example <c>sb-inbound-prod.servicebus.windows.net</c>.</summary>
    public string FullyQualifiedNamespace { get; set; } = string.Empty;

    /// <summary>Connection string or Key Vault secret name. Used only when set.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Queue read by download workers.</summary>
    [Required]
    public string DownloadQueueName { get; set; } = "inbound-download";

    /// <summary>Queue read by process workers.</summary>
    [Required]
    public string ProcessQueueName { get; set; } = "inbound-process";

    /// <summary>How long a worker keeps renewing a message lock while it is being handled.</summary>
    [Range(1, 60)]
    public int MaxAutoLockRenewalMinutes { get; set; } = 15;

    /// <summary>True when either connection setting is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(FullyQualifiedNamespace) || !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>Queue for <paramref name="stage"/>.</summary>
    public string QueueFor(InboundWorkStage stage) => stage == InboundWorkStage.Download ? DownloadQueueName : ProcessQueueName;
}
