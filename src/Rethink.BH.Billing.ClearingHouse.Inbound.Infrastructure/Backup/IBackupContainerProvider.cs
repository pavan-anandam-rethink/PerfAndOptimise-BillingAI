using Azure.Storage.Blobs;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;

/// <summary>Provides the shared, thread-safe backup container client.</summary>
public interface IBackupContainerProvider
{
    /// <summary>Returns the container client, resolving the connection string on first use.</summary>
    ValueTask<BlobContainerClient> GetContainerAsync(CancellationToken cancellationToken);
}
