using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.DependencyInjection;

/// <summary>Registers the inbound download use case, its options, and the cross-cutting pipeline.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Adds MediatR, the request logging behavior, the fault boundary, and the download services.</summary>
    public static IServiceCollection AddInboundApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<InboundDownloadOptions>()
            .Bind(configuration.GetSection(InboundDownloadOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMediatR(mediatr =>
        {
            mediatr.RegisterServicesFromAssemblyContaining<DownloadInboundFilesCommand>();
            mediatr.AddOpenBehavior(typeof(RequestLoggingBehavior<,>));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IFaultBoundary, FaultBoundary>();
        services.AddSingleton<IFailureMessageFormatter, FailureMessageFormatter>();
        services.AddSingleton<IRemoteFileSelector, RemoteFileSelector>();
        services.AddSingleton<IInboundFileProcessor, InboundFileProcessor>();
        services.AddSingleton<IInboundFileBatchProcessor, InboundFileBatchProcessor>();
        AddPipeline(services, configuration);
        return services;
    }

    private static void AddPipeline(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PipelineOptions>()
            .Bind(configuration.GetSection(PipelineOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !options.IsEnabled || options.ClearingHouseIds.Length > 0, "Pipeline:ClearingHouseIds must list at least one clearing house when the pipeline is on.")
            .Validate(options => options.RetryMaxDelaySeconds >= options.RetryBaseDelaySeconds, "Pipeline:RetryMaxDelaySeconds must be at least RetryBaseDelaySeconds.")
            .ValidateOnStart();

        services.AddSingleton<SharedSftpSessionProvider>();
        services.AddSingleton<ISftpSessionProvider>(provider => provider.GetRequiredService<SharedSftpSessionProvider>());
        services.AddSingleton<WorkItemRetryPolicy>();
        services.AddSingleton<WorkItemClaimer>();
        services.AddSingleton<IWorkItemFailureHandler, WorkItemFailureHandler>();
        services.AddSingleton<StalledFileReconciler>();
        services.AddSingleton<InboundFileLister>();
        services.AddSingleton<InboundFileLander>();
        services.AddSingleton<ILandedFileRegistrar, LandedFileRegistrar>();
        services.AddSingleton<RemoteFileFinalizer>();
        services.AddSingleton<BillingRegistrationStep>();
        services.AddTransient<IInboundWorkDispatcher, InboundWorkDispatcher>();
    }
}
