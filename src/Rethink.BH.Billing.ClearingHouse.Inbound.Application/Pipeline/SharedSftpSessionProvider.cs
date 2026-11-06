using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>
/// Opens one session per clearing house on first use and keeps it for the life of the process. The session's own pool
/// bounds connections and reconnects dropped clients, so concurrent messages share at most <c>SftpPoolSize</c> connections.
/// </summary>
public sealed class SharedSftpSessionProvider : ISftpSessionProvider, IAsyncDisposable
{
    private readonly ISftpSessionFactory _sessionFactory;
    private readonly Dictionary<int, ISftpSession> _sessions = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    /// <summary>Creates the provider.</summary>
    public SharedSftpSessionProvider(ISftpSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    /// <inheritdoc />
    public async Task<ISftpSession> GetAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clearingHouse);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var release = new LockRelease(_lock);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_sessions.TryGetValue(clearingHouse.Id, out var existing))
        {
            return existing;
        }

        var opened = await _sessionFactory.OpenAsync(clearingHouse, cancellationToken).ConfigureAwait(false);
        _sessions[clearingHouse.Id] = opened;
        return opened;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        using (new LockRelease(_lock))
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var session in _sessions.Values)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }

            _sessions.Clear();
        }
    }

    private sealed class LockRelease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public LockRelease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }
}
