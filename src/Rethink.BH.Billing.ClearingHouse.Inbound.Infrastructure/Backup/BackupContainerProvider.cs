using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Storage;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;

/// <summary>The <c>AvailityBackup</c> container client, created once and reused. A failed resolution is not cached.</summary>
public sealed class BackupContainerProvider : IBackupContainerProvider, IDisposable
{
    private readonly BlobContainerProvider _provider;

    /// <summary>Creates the provider with the default <see cref="BlobServiceClient"/> factory.</summary>
    public BackupContainerProvider(IOptions<BackupStorageOptions> options, ISecretResolver secretResolver)
        : this(options, secretResolver, connectionString => new BlobServiceClient(connectionString))
    {
    }

    /// <summary>Creates the provider with a custom service client factory.</summary>
    public BackupContainerProvider(IOptions<BackupStorageOptions> options, ISecretResolver secretResolver, Func<string, BlobServiceClient> serviceClientFactory)
    {
        ArgumentNullException.ThrowIfNull(options);

        _provider = new BlobContainerProvider(
            options.Value.ContainerName,
            options.Value.ConnectionString,
            BackupStorageOptions.ConnectionStringKey,
            secretResolver,
            serviceClientFactory);
    }

    /// <inheritdoc />
    public ValueTask<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken) => _provider.GetContainerAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
