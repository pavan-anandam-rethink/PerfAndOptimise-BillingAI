namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>Host-level configuration keys shared with the legacy service and the Helm chart.</summary>
public static class HostSettings
{
    /// <summary>Path base injected by Helm (<c>AppVirtualDirectory</c>).</summary>
    public const string VirtualDirectoryKey = "AppVirtualDirectory";

    /// <summary>Shows Swagger outside Development when true.</summary>
    public const string SwaggerShowKey = "Swagger:Show";

    /// <summary>Browser origins allowed to call the API. Server-to-server callers such as the scheduled download are unaffected.</summary>
    public const string CorsAllowedOriginsKey = "Cors:AllowedOrigins";

    /// <summary>Returns the configured CORS origins; empty when none are configured.</summary>
    public static string[] AllowedCorsOrigins(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetSection(CorsAllowedOriginsKey).Get<string[]>() ?? [];
    }

    /// <summary>Returns the configured path base, or <c>/</c>.</summary>
    public static string VirtualDirectory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[VirtualDirectoryKey] ?? "/";
    }

    /// <summary>True when <c>Swagger:Show</c> is true.</summary>
    public static bool ShowSwagger(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetValue<bool?>(SwaggerShowKey) ?? false;
    }
}
