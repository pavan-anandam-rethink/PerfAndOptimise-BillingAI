using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;

/// <summary>
/// Guards the operator endpoints on top of the application key. They do not exist (404) while the pipeline is off or no
/// operator key is configured; a wrong or missing key gets 401. Keys are compared in fixed time.
/// </summary>
public sealed class OperatorKeyFilter : IAuthorizationFilter
{
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<PipelineOptions> _pipeline;
    private readonly ILogger<OperatorKeyFilter> _logger;

    /// <summary>Creates the filter.</summary>
    public OperatorKeyFilter(IConfiguration configuration, IOptionsMonitor<PipelineOptions> pipeline, ILogger<OperatorKeyFilter> logger)
    {
        _configuration = configuration;
        _pipeline = pipeline;
        _logger = logger;
    }

    /// <inheritdoc />
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var expected = _configuration[PipelineHostSettings.OperatorKeyKey];
        if (!_pipeline.CurrentValue.IsEnabled || string.IsNullOrEmpty(expected))
        {
            context.Result = new NotFoundResult();
            return;
        }

        var supplied = context.HttpContext.Request.Headers[ApiHeaderNames.OperatorKey].ToString();
        if (!KeysMatch(expected, supplied))
        {
            ApiLog.OperatorKeyRejected(_logger, context.HttpContext.Request.Path.Value ?? string.Empty);
            context.Result = new UnauthorizedResult();
        }
    }

    /// <summary>Fixed-time comparison of the UTF-8 bytes.</summary>
    public static bool KeysMatch(string expected, string supplied)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied ?? string.Empty));
}
