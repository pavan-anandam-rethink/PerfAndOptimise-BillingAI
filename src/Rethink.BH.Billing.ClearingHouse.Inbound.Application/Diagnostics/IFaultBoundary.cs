namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>
/// The single place where business operations capture failures. Each failure is logged once, with the active
/// log scope, and returned as a <see cref="GuardedResult{T}"/> so callers never need try/catch blocks.
/// Cancellation is never swallowed except as described on <see cref="RunWithRunLimitAsync{T}"/>.
/// </summary>
public interface IFaultBoundary
{
    /// <summary>Runs <paramref name="action"/>. Exceptions other than cancellation become a faulted result.</summary>
    Task<GuardedResult<T>> RunAsync<T>(string operation, Func<Task<T>> action);

    /// <summary>
    /// Runs <paramref name="action"/> under a run limit. Cancellation that did not come from
    /// <paramref name="requestToken"/> means the run limit elapsed and becomes a timed-out result.
    /// Cancellation from <paramref name="requestToken"/> is rethrown.
    /// </summary>
    Task<GuardedResult<T>> RunWithRunLimitAsync<T>(string operation, Func<Task<T>> action, CancellationToken requestToken);
}
