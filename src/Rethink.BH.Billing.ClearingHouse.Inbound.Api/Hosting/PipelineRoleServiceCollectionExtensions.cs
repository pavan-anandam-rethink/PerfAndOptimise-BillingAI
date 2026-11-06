using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.DependencyInjection;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>Adds the background work for the configured <see cref="PipelineRole"/>. The HTTP API runs in every role, so health probes work.</summary>
public static class PipelineRoleServiceCollectionExtensions
{
    /// <summary>Adds the operator filter and the hosted service for the role.</summary>
    public static IServiceCollection AddPipelineRole(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<OperatorKeyFilter>();
        switch (PipelineHostSettings.Role(configuration))
        {
            case PipelineRole.Lister:
                services.AddHostedService<ListerRunHostedService>();
                break;
            case PipelineRole.DownloadWorker:
                services.AddInboundWorker(InboundWorkStage.Download);
                break;
            case PipelineRole.ProcessWorker:
                services.AddInboundWorker(InboundWorkStage.Process);
                break;
            default:
                break;
        }

        return services;
    }
}
