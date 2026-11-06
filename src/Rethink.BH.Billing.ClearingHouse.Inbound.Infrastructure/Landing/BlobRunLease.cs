using Azure;
using Microsoft.Extensions.Logging;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>Renew and release operations on one acquired lease.</summary>
public interface IRunLeaseHandle
{
    /// <summary>Extends the lease by its full duration.</summary>
    Task RenewAsync(CancellationToken cancellationToken);

    /// <summary>Releases the lease so the next run can start at once.</summary>
    Task ReleaseAsync(CancellationToken cancellationToken);
}

/// <summary>
/// An acquired lease. A background loop renews it every third of its duration; disposing stops the loop and releases it.
/// Storage errors while renewing or releasing are logged, not thrown: the lease then simply expires.
/// </summary>
public sealed class BlobRunLease : IAsyncDisposable
{
    private readonly IRunLeaseHandle _handle;
    private readonly string _leaseName;
    private readonly TimeSpan _leaseDuration;
    private readonly ILogger _logger;
    private readonly PeriodicTimer _timer;
    private Task _renewal = Task.CompletedTask;
    private int _disposed;

    private BlobRunLease(IRunLeaseHandle handle, string leaseName, TimeSpan leaseDuration, ILogger logger)
    {
        _handle = handle;
        _leaseName = leaseName;
        _leaseDuration = leaseDuration;
        _logger = logger;
        _timer = new PeriodicTimer(leaseDuration / 3);
    }

    /// <summary>Starts renewing an acquired lease.</summary>
    public static BlobRunLease Start(IRunLeaseHandle handle, string leaseName, TimeSpan leaseDuration, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(logger);

        var lease = new BlobRunLease(handle, leaseName, leaseDuration, logger);
        lease._renewal = lease.RenewUntilDisposedAsync();
        return lease;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _timer.Dispose();
        await _renewal.ConfigureAwait(false);
        try
        {
            await _handle.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (RequestFailedException releaseFailed)
        {
            InfrastructureLog.LeaseReleaseFailed(_logger, _leaseName, (int)_leaseDuration.TotalSeconds, releaseFailed);
        }
    }

    private async Task RenewUntilDisposedAsync()
    {
        while (await _timer.WaitForNextTickAsync().ConfigureAwait(false))
        {
            try
            {
                await _handle.RenewAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (RequestFailedException renewFailed)
            {
                InfrastructureLog.LeaseRenewalFailed(_logger, _leaseName, renewFailed);
                return;
            }
        }
    }
}
