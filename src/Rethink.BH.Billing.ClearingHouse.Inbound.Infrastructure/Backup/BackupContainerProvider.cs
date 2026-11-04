using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;

/// <summary>
/// Creates the container client once and reuses it (Azure SDK clients are thread-safe and pool connections).
/// A failed resolution is not cached, so the next file retries it.
/// </summary>
public sealed class BackupContainerProvider : IBackupContainerProvider, IDisposable
{
    private readonly BackupStorageOptions _options;
    private readonly ISecretResolver _secretResolver;
    private readonly Func<string, BlobServiceClient> _serviceClientFactory;
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private BlobContainerClient? _container;

    /// <summary>Creates the provider with the default <see cref="BlobServiceClient"/> factory.</summary>
    public BackupContainerProvider(IOptions<BackupStorageOptions> options, ISecretResolver secretResolver)
        : this(options, secretResolver, connectionString => new BlobServiceClient(connectionString))
    {
    }

    /// <summary>Creates the provider with a custom service client factory.</summary>
    public BackupContainerProvider(IOptions<BackupStorageOptions> options, ISecretResolver secretResolver, Func<string, BlobServiceClient> serviceClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _secretResolver = secretResolver;
        _serviceClientFactory = serviceClientFactory;
    }

    /// <inheritdoc />
    public async ValueTask<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _container);
        if (existing is not null)
        {
            return existing;
        }

        await _creationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var release = new SemaphoreRelease(_creationLock);
        if (_container is not null)
        {
            return _container;
        }

        var connectionString = await _secretResolver
            .ResolveAsync(_options.ConnectionString, BackupStorageOptions.ConnectionStringKey, LiteralWhenContainsEqualsPolicy.Instance, cancellationToken)
            .ConfigureAwait(false);

        var container = _serviceClientFactory(connectionString).GetBlobContainerClient(_options.ContainerName);
        Volatile.Write(ref _container, container);
        return container;
    }

    /// <inheritdoc />
    public void Dispose() => _creationLock.Dispose();

    private sealed class SemaphoreRelease : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;

        public SemaphoreRelease(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => _semaphore.Release();
    }
}
