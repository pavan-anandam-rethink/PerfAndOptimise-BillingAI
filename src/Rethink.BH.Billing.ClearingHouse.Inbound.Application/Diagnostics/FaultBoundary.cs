using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>Logs each captured failure once with the caller's log scope and turns it into a result.</summary>
public sealed class FaultBoundary : IFaultBoundary
{
    private readonly ILogger<FaultBoundary> _logger;

    /// <summary>Creates the boundary.</summary>
    public FaultBoundary(ILogger<FaultBoundary> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Central fault boundary: every failure is logged once and returned as a result.")]
    public async Task<GuardedResult<T>> RunAsync<T>(string operation, Func<Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return GuardedResult.Success(await action().ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            InboundLog.OperationFaulted(_logger, operation, exception.GetType().Name, exception);
            return GuardedResult.Fault<T>(exception);
        }
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Central fault boundary: every failure is logged once and returned as a result.")]
    public async Task<GuardedResult<T>> RunWithRunLimitAsync<T>(string operation, Func<Task<T>> action, CancellationToken requestToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            return GuardedResult.Success(await action().ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            InboundLog.OperationTimedOut(_logger, operation);
            return GuardedResult.TimedOut<T>();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            InboundLog.OperationFaulted(_logger, operation, exception.GetType().Name, exception);
            return GuardedResult.Fault<T>(exception);
        }
    }
}
