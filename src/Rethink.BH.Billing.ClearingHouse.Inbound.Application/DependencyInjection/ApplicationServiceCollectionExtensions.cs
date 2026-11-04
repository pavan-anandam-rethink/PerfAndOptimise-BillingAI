using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;

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
        return services;
    }
}
