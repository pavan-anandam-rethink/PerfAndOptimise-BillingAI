using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;
using RethinkCore.Common.CorrelationId.Extensions;
using RethinkCore.Common.HealthCheck.Extensions;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>Builds the request pipeline in the legacy order.</summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>Adds the middleware and endpoints.</summary>
    public static WebApplication UseInboundPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UsePathBase(HostSettings.VirtualDirectory(app.Configuration));
        app.UseExceptionHandler();
        UseSwaggerWhenEnabled(app);
        UseRequestMiddleware(app);
        app.MapControllers();
        app.ConfigureRethinkMappHealthCheck();
        return app;
    }

    private static void UseSwaggerWhenEnabled(WebApplication app)
    {
        if (app.Environment.IsDevelopment() || HostSettings.ShowSwagger(app.Configuration))
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
    }

    private static void UseRequestMiddleware(IApplicationBuilder app)
    {
        app.UseCorrelationId();
        app.UseHttpLogging();
        app.UseHttpsRedirection();
        app.UseMiddleware<LegacyApiKeyMiddleware>();
        app.UseAuthorization();
        app.UseCors(ApiServiceCollectionExtensions.CorsPolicyName);
    }
}
