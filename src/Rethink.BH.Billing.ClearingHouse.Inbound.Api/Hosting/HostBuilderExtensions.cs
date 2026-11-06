using Rethink.BH.Billing.ClearingHouse.Inbound.Application.DependencyInjection;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.DependencyInjection;
using RethinkCore.Common.Configuration;
using RethinkCore.Common.HealthCheck.Extensions;
using RethinkCore.Common.Infrastructure.Extensions;
using RethinkCore.Common.Logging;
using RethinkCore.Common.Logging.Core.DataProviders;
using RethinkCore.Common.Logging.Extensions;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>
/// Configures the host the same way the legacy ClearingHouse service and the Accounts service do: Azure App Configuration
/// and Key Vault, Application Insights logging with the application key enricher, Rethink infrastructure, and health checks.
/// </summary>
public static class HostBuilderExtensions
{
    /// <summary>Adds configuration, centralized logging, platform services, and the inbound download services.</summary>
    public static WebApplicationBuilder AddInboundHost(this WebApplicationBuilder builder, string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddPlatformConfiguration(builder, args);
        AddCentralizedLogging(builder);

        var services = builder.Services;
        var configuration = builder.Configuration;
        services.AddRethinkInfra(configuration);
        services.AddRethinkHealthCheck(configuration);
        services.AddHttpLogging(_ => { });

        services.AddInboundApplication(configuration);
        services.AddInboundInfrastructure(configuration);
        services.AddInboundApi(configuration);
        services.AddPipelineRole(configuration);
        return builder;
    }

    private static void AddPlatformConfiguration(WebApplicationBuilder builder, string[] args)
    {
        using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var bootstrapLogger = bootstrapLoggerFactory.CreateLogger(typeof(HostBuilderExtensions).FullName!);
        builder.Host.LoadCustomAppConfiguration(args, builder.Services, bootstrapLogger);
    }

    private static void AddCentralizedLogging(WebApplicationBuilder builder)
    {
        builder.Logging.AddAppInsightLogger(builder.Configuration, logging =>
        {
            logging.Services.AddTransient<ILogDataProvider, LogApplicationKeyProvider>();
            return logging;
        });
        builder.Services.AddRethinkLogging(builder.Configuration);
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.AddConsole();
        }
    }
}
