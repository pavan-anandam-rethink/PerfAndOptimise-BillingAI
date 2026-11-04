using MediatR;
using Microsoft.Extensions.Logging;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>
/// Logs the start, end, and duration of every MediatR request inside a request scope.
/// Exceptions are not caught here; they reach the API global exception handler, which logs them once.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public sealed class RequestLoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly string RequestName = typeof(TRequest).Name;

    private readonly ILogger<RequestLoggingBehavior<TRequest, TResponse>> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the behavior.</summary>
    public RequestLoggingBehavior(ILogger<RequestLoggingBehavior<TRequest, TResponse>> logger, TimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        using var scope = _logger.BeginScope(LogScopes.ForRequest(RequestName));
        var startedAt = _timeProvider.GetTimestamp();
        InboundLog.RequestStarted(_logger, RequestName);

        var response = await next().ConfigureAwait(false);

        InboundLog.RequestFinished(_logger, RequestName, (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        return response;
    }
}
