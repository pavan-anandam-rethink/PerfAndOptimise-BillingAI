using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>
/// A bounded pool of SFTP connections. The first connection is opened eagerly so a bad host or credential fails the run
/// immediately; further connections are opened only when concurrent work needs them, up to the pool size.
/// A disconnected client is reconnected the next time it is leased.
/// </summary>
public sealed class SshNetSftpSession : ISftpSession, IDisposable
{
    private readonly ISftpClientFactory _clientFactory;
    private readonly SftpCredentials _credentials;
    private readonly TimeSpan _connectTimeout;
    private readonly string _directory;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _slots;
    private readonly ConcurrentBag<ISftpClient> _idleClients = [];
    private readonly ConcurrentQueue<ISftpClient> _createdClients = new();
    private int _disposed;

    private SshNetSftpSession(ISftpClientFactory clientFactory, SftpCredentials credentials, int poolSize, TimeSpan connectTimeout, ILogger logger)
    {
        _clientFactory = clientFactory;
        _credentials = credentials;
        _connectTimeout = connectTimeout;
        _directory = credentials.DownloadDirectory.TrimEnd('/');
        _logger = logger;
        _slots = new SemaphoreSlim(poolSize, poolSize);
    }

    /// <summary>Number of SFTP clients created so far. Never exceeds the pool size.</summary>
    public int CreatedClientCount => _createdClients.Count;

    /// <summary>Creates the pool and connects its first client. Every created client is disposed if the connect fails.</summary>
    public static async Task<SshNetSftpSession> OpenAsync(
        ISftpClientFactory clientFactory,
        SftpCredentials credentials,
        int poolSize,
        TimeSpan connectTimeout,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentOutOfRangeException.ThrowIfLessThan(poolSize, 1);

        var session = new SshNetSftpSession(clientFactory, credentials, poolSize, connectTimeout, logger);
        using var guard = new OwnershipGuard<SshNetSftpSession>(session);
        await session.UseClientAsync(static (_, _) => Task.FromResult(true), cancellationToken).ConfigureAwait(false);
        return guard.Release();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken cancellationToken)
        => UseClientAsync(ListDirectoryAsync, cancellationToken);

    /// <inheritdoc />
    public Task<IFileContent> DownloadAsync(RemoteFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        return UseClientAsync((client, token) => DownloadFileAsync(client, file, token), cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(RemoteFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        return UseClientAsync(
            async (client, token) =>
            {
                await client.DeleteFileAsync(file.RemotePath, token).ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        while (_createdClients.TryDequeue(out var client))
        {
            client.Dispose();
        }

        _slots.Dispose();
    }

    private async Task<T> UseClientAsync<T>(Func<ISftpClient, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);

        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var lease = new ClientLease(this);
        var client = lease.Attach(TakeOrCreateClient());
        if (!client.IsConnected)
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }

        return await operation(client, cancellationToken).ConfigureAwait(false);
    }

    private ISftpClient TakeOrCreateClient()
    {
        if (_idleClients.TryTake(out var idle))
        {
            return idle;
        }

        var created = _clientFactory.Create(_credentials, _connectTimeout);
        _createdClients.Enqueue(created);
        return created;
    }

    private void ReleaseSlot(ISftpClient? client)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        if (client is not null)
        {
            _idleClients.Add(client);
        }

        _slots.Release();
    }

    private async Task<IReadOnlyList<RemoteFile>> ListDirectoryAsync(ISftpClient client, CancellationToken cancellationToken)
    {
        var files = new List<RemoteFile>();
        await foreach (var entry in client.ListDirectoryAsync(_directory, cancellationToken).ConfigureAwait(false))
        {
            if (entry.IsDirectory || entry.Name.StartsWith('.'))
            {
                continue;
            }

            files.Add(new RemoteFile(entry.FullName, entry.Name, entry.Length, entry.LastWriteTimeUtc));
        }

        InfrastructureLog.SftpDirectoryListed(_logger, files.Count, _directory);
        return files;
    }

    private async Task<IFileContent> DownloadFileAsync(ISftpClient client, RemoteFile file, CancellationToken cancellationToken)
    {
        var content = new PooledFileContentStream(file.Size);
        using var guard = new OwnershipGuard<PooledFileContentStream>(content);
        await client.DownloadFileAsync(file.RemotePath, content, cancellationToken).ConfigureAwait(false);
        InfrastructureLog.SftpFileDownloaded(_logger, file.Name, content.Length);
        return guard.Release();
    }

    /// <summary>Owns one pool slot from the moment it is acquired, so a failed client creation still frees the slot.</summary>
    private sealed class ClientLease : IDisposable
    {
        private readonly SshNetSftpSession _session;
        private ISftpClient? _client;

        public ClientLease(SshNetSftpSession session)
        {
            _session = session;
        }

        public ISftpClient Attach(ISftpClient client) => _client = client;

        public void Dispose() => _session.ReleaseSlot(_client);
    }
}
