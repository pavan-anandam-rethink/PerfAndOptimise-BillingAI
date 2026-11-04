using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>Prefixes every Swagger path with the virtual directory, as the RethinkCore filter does.</summary>
public sealed class PathPrefixDocumentFilter : IDocumentFilter
{
    private readonly string _pathPrefix;

    /// <summary>Creates the filter.</summary>
    public PathPrefixDocumentFilter(string pathPrefix)
    {
        _pathPrefix = pathPrefix;
    }

    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);

        var prefixed = new OpenApiPaths();
        foreach (var (path, item) in swaggerDoc.Paths)
        {
            prefixed.Add(_pathPrefix + path, item);
        }

        swaggerDoc.Paths = prefixed;
    }
}
