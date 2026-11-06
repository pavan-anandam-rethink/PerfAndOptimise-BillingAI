namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Distributed mutual exclusion, so overlapping scheduled runs never list the same directory at once.</summary>
public interface IRunLeaseProvider
{
    /// <summary>
    /// Acquires <paramref name="leaseName"/> and keeps it until the returned handle is disposed.
    /// Returns null when another process holds it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string leaseName, CancellationToken cancellationToken);
}
