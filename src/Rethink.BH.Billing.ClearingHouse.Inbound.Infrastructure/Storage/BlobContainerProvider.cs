using Azure.Storage.Blobs;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Storage;

/// <summary>
/// Creates one container client after resolving the connection string, then reuses it (Azure SDK clients are thread-safe
/// and pool connections). A failed resolution is not cached, so the next caller retries it.
/// </summary>
public sealed class BlobContainerProvider : IDisposable
{
    private readonly string _containerName;
    private readonly string _connectionString;
    private readonly string _connectionSettingName;
    private readonly ISecretResolver _secretResolver;
    private readonly Func<string, BlobServiceClient> _serviceClientFactory;
    private readonly SemaphoreSlim _creationLock = new(1, 1);
    private BlobContainerClient? _container;

    /// <summary>Creates the provider.</summary>
    /// <param name="containerName">Container name.</param>
    /// <param name="connectionString">Connection string or Key Vault secret name (no <c>=</c>).</param>
    /// <param name="connectionSettingName">Configuration key, used in error messages.</param>
    /// <param name="secretResolver">Resolves Key Vault secret names.</param>
    /// <param name="serviceClientFactory">Creates the service client from the resolved connection string.</param>
    public BlobContainerProvider(
        string containerName,
        string connectionString,
        string connectionSettingName,
        ISecretResolver secretResolver,
        Func<string, BlobServiceClient> serviceClientFactory)
    {
        _containerName = containerName;
        _connectionString = connectionString;
        _connectionSettingName = connectionSettingName;
        _secretResolver = secretResolver;
        _serviceClientFactory = serviceClientFactory;
    }

    /// <summary>Returns the shared container client.</summary>
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
            .ResolveAsync(_connectionString, _connectionSettingName, LiteralWhenContainsEqualsPolicy.Instance, cancellationToken)
            .ConfigureAwait(false);

        var container = _serviceClientFactory(connectionString).GetBlobContainerClient(_containerName);
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
