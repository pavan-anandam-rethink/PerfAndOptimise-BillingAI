using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.ExceptionHandling;

/// <summary>
/// The single place where an unhandled request exception is logged (with path, route, trace id, and correlation id)
/// and turned into ProblemDetails. Client aborts are left for the host to ignore.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IReadOnlyList<IProblemMapper> _mappers;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>Creates the handler. Mappers are tried in registration order.</summary>
    public GlobalExceptionHandler(IEnumerable<IProblemMapper> mappers, ILogger<GlobalExceptionHandler> logger)
    {
        _mappers = mappers.ToArray();
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return false;
        }

        var mapper = _mappers.FirstOrDefault(candidate => candidate.CanMap(exception));
        Log(httpContext, exception, mapper?.IsClientError == true);

        var response = mapper?.Map(exception) ?? UnknownProblem();
        httpContext.Response.StatusCode = response.StatusCode;
        await WriteProblemAsync(httpContext, response.Problem, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void Log(HttpContext httpContext, Exception exception, bool isClientError)
    {
        var request = httpContext.Request;
        request.RouteValues.TryGetValue("controller", out var controller);
        request.RouteValues.TryGetValue("action", out var action);
        var correlationId = request.Headers[ApiHeaderNames.CorrelationId].ToString();

        if (isClientError)
        {
            ApiLog.RequestRejected(_logger, exception, request.Path.Value, request.Method, controller, action, httpContext.TraceIdentifier, correlationId);
            return;
        }

        ApiLog.RequestFailed(_logger, exception, request.Path.Value, request.Method, controller, action, httpContext.TraceIdentifier, correlationId);
    }

    private static ProblemResponse UnknownProblem() => new(
        StatusCodes.Status500InternalServerError,
        new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An error occurred while processing your request.",
            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
        });

    private static Task WriteProblemAsync(HttpContext httpContext, ProblemDetails problem, CancellationToken cancellationToken)
        => problem is ValidationProblemDetails validation
            ? httpContext.Response.WriteAsJsonAsync(validation, cancellationToken)
            : httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
}
