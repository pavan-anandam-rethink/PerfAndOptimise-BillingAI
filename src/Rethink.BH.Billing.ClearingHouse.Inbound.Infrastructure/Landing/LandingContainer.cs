using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Storage;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>The landing container client, shared by the landing store and the lister lease. Uses the backup storage account.</summary>
public sealed class LandingContainer : IDisposable
{
    /// <summary>Creates the container holder with the default <see cref="BlobServiceClient"/> factory.</summary>
    public LandingContainer(IOptions<LandingStorageOptions> landing, IOptions<BackupStorageOptions> storage, ISecretResolver secretResolver)
        : this(landing, storage, secretResolver, connectionString => new BlobServiceClient(connectionString))
    {
    }

    /// <summary>Creates the container holder with a custom service client factory.</summary>
    public LandingContainer(
        IOptions<LandingStorageOptions> landing,
        IOptions<BackupStorageOptions> storage,
        ISecretResolver secretResolver,
        Func<string, BlobServiceClient> serviceClientFactory)
    {
        ArgumentNullException.ThrowIfNull(landing);
        ArgumentNullException.ThrowIfNull(storage);

        Provider = new BlobContainerProvider(
            landing.Value.ContainerName,
            storage.Value.ConnectionString,
            BackupStorageOptions.ConnectionStringKey,
            secretResolver,
            serviceClientFactory);
    }

    /// <summary>The shared container provider.</summary>
    public BlobContainerProvider Provider { get; }

    /// <inheritdoc />
    public void Dispose() => Provider.Dispose();
}
