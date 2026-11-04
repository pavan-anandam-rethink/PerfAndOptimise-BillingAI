using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;

/// <summary>Logs the start and end of every action with its safe identifiers, status code, and elapsed time.</summary>
public sealed class RequestTraceFilter : IAsyncActionFilter
{
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RequestTraceFilter> _logger;

    /// <summary>Creates the filter.</summary>
    public RequestTraceFilter(TimeProvider timeProvider, ILogger<RequestTraceFilter> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var controller = context.RouteData.Values["controller"]?.ToString();
        var action = context.RouteData.Values["action"]?.ToString();
        var request = context.HttpContext.Request;
        ApiLog.ActionStarting(_logger, controller, action, request.Method, request.Path.Value, SafeIdReader.Format(context));

        var startedAt = _timeProvider.GetTimestamp();
        var executed = await next().ConfigureAwait(false);
        ApiLog.ActionFinished(_logger, controller, action, GetStatusCode(executed), (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
    }

    /// <summary>Status code of the action result, 500 for an unhandled action exception, otherwise the response status.</summary>
    public static int GetStatusCode(ActionExecutedContext executed)
    {
        ArgumentNullException.ThrowIfNull(executed);

        if (executed.Result is IStatusCodeActionResult { StatusCode: { } statusCode })
        {
            return statusCode;
        }

        return executed.Exception is not null && !executed.ExceptionHandled
            ? StatusCodes.Status500InternalServerError
            : executed.HttpContext.Response.StatusCode;
    }
}
