using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.ExceptionHandling;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;
using RethinkCore.Common.Infrastructure.Attributes;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>Registers MVC, JSON, Swagger, CORS, and exception handling with the legacy settings.</summary>
public static class ApiServiceCollectionExtensions
{
    /// <summary>Name of the CORS policy applied to every endpoint.</summary>
    public const string CorsPolicyName = "AllowAll";

    /// <summary>Legacy Newtonsoft date format.</summary>
    public const string DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    /// <summary>Adds the API layer.</summary>
    public static IServiceCollection AddInboundApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddControllers(options =>
            {
                options.Filters.Add<XAppKeyActionFilter>();
                options.Filters.Add<RequestTraceFilter>();
            })
            .AddNewtonsoftJson(options =>
            {
                options.SerializerSettings.DateFormatString = DateFormat;
                options.SerializerSettings.Formatting = Formatting.Indented;
            });

        services.AddProblemDetails();
        services.AddSingleton<IProblemMapper, ValidationProblemMapper>();
        services.AddSingleton<IProblemMapper, NotImplementedProblemMapper>();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        var allowedOrigins = HostSettings.AllowedCorsOrigins(configuration);
        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
        AddSwagger(services, configuration);
        return services;
    }

    private static void AddSwagger(IServiceCollection services, IConfiguration configuration)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(swagger =>
        {
            swagger.SwaggerDoc("v1", new OpenApiInfo
            {
                Version = "v1",
                Title = "Clearing House API",
                Description = "Rethink Architecture - Clearing House Service (inbound SFTP download)",
            });
            swagger.DocumentFilter<PathPrefixDocumentFilter>(configuration[HostSettings.VirtualDirectoryKey] ?? string.Empty);

            var xmlPath = Path.Combine(AppContext.BaseDirectory, "website.xml");
            if (File.Exists(xmlPath))
            {
                swagger.IncludeXmlComments(xmlPath);
            }

            var scheme = new OpenApiSecurityScheme
            {
                Name = ApiHeaderNames.ApplicationKey,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "API key needed to access the endpoints.",
            };
            swagger.AddSecurityDefinition(ApiHeaderNames.ApplicationKey, scheme);
            swagger.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = ApiHeaderNames.ApplicationKey },
                    },
                    Array.Empty<string>()
                },
            });
        });
    }
}
