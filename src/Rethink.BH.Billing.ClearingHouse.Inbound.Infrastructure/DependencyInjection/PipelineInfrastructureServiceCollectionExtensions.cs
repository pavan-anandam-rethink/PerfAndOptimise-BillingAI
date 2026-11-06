using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Persistence;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the pipeline adapters: SQL manifest, landing Blob store and lease, and Service Bus. Every adapter is lazy (no
/// connection until first use), so they are registered in every mode; connection settings are required only when
/// <c>Pipeline:Mode</c> is not <c>Off</c>.
/// </summary>
public static class PipelineInfrastructureServiceCollectionExtensions
{
    /// <summary>Adds the pipeline adapters.</summary>
    public static IServiceCollection AddPipelineInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var enabled = IsPipelineEnabled(configuration);
        AddManifest(services, enabled);
        AddLanding(services, configuration);
        AddServiceBus(services, configuration, enabled);
        return services;
    }

    /// <summary>Adds a hosted worker that reads the <paramref name="stage"/> queue.</summary>
    public static IServiceCollection AddInboundWorker(this IServiceCollection services, InboundWorkStage stage)
    {
        services.AddSingleton<IHostedService>(provider =>
        {
            var pipeline = provider.GetRequiredService<IOptions<PipelineOptions>>().Value;
            var serviceBus = provider.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
            var settings = new InboundWorkerSettings(
                stage,
                serviceBus.QueueFor(stage),
                stage == InboundWorkStage.Download ? pipeline.DownloadConcurrency : pipeline.ProcessConcurrency,
                TimeSpan.FromMinutes(serviceBus.MaxAutoLockRenewalMinutes));
            return new InboundWorkerHostedService(
                settings,
                provider.GetRequiredService<ServiceBusClientProvider>(),
                provider.GetRequiredService<InboundWorkMessageHandler>(),
                provider.GetRequiredService<ILogger<InboundWorkerHostedService>>());
        });
        return services;
    }

    private static bool IsPipelineEnabled(IConfiguration configuration)
        => configuration.GetSection(PipelineOptions.SectionName).Get<PipelineOptions>()?.IsEnabled ?? false;

    private static void AddManifest(IServiceCollection services, bool enabled)
    {
        services.AddOptions<ManifestStorageOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                config.GetSection(ManifestStorageOptions.SectionName).Bind(options);
                options.ConnectionString = config[ManifestStorageOptions.ConnectionStringKey] ?? string.Empty;
            })
            .ValidateDataAnnotations()
            .Validate(options => !enabled || !string.IsNullOrWhiteSpace(options.ConnectionString), $"{ManifestStorageOptions.ConnectionStringKey} is required when Pipeline:Mode is not Off.")
            .ValidateOnStart();
        services.AddSingleton<IManifestDbContextFactory, SqlManifestDbContextFactory>();
        services.AddSingleton<IInboundFileManifest, SqlInboundFileManifest>();
    }

    private static void AddLanding(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LandingStorageOptions>()
            .Bind(configuration.GetSection(LandingStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<LandingContainer>();
        services.AddSingleton<ILandingFileStore, AzureBlobLandingFileStore>();
        services.AddSingleton<IRunLeaseProvider, BlobRunLeaseProvider>();
    }

    private static void AddServiceBus(IServiceCollection services, IConfiguration configuration, bool enabled)
    {
        services.AddOptions<ServiceBusOptions>()
            .Bind(configuration.GetSection(ServiceBusOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !enabled || options.IsConfigured, "ServiceBus:FullyQualifiedNamespace or ServiceBus:ConnectionString is required when Pipeline:Mode is not Off.")
            .ValidateOnStart();
        services.AddSingleton<ServiceBusClientProvider>();
        services.AddSingleton<ServiceBusInboundWorkSender>();
        services.AddSingleton<IInboundWorkSender>(provider => provider.GetRequiredService<ServiceBusInboundWorkSender>());
        services.AddSingleton<InboundWorkMessageHandler>();
    }
}
