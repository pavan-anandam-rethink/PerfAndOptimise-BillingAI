using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Backups;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Backup;

/// <summary>
/// Write-once backup in the <c>AvailityBackup</c> container: an existing blob is left unchanged, otherwise the file is
/// uploaded straight from the downloaded buffer without copying it.
/// </summary>
public sealed class AzureBlobBackupFileStore : IBackupFileStore
{
    private readonly IBackupContainerProvider _containerProvider;

    /// <summary>Creates the store.</summary>
    public AzureBlobBackupFileStore(IBackupContainerProvider containerProvider)
    {
        _containerProvider = containerProvider;
    }

    /// <inheritdoc />
    public async Task<BackupWriteResult> WriteOnceAsync(string fileName, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("File name is required.");
        }

        var container = await _containerProvider.GetContainerAsync(cancellationToken).ConfigureAwait(false);
        var blob = container.GetBlobClient(fileName);
        if ((await blob.ExistsAsync(cancellationToken).ConfigureAwait(false)).Value)
        {
            return new BackupWriteResult(fileName, AlreadyExisted: true);
        }

        await blob.UploadAsync(BinaryData.FromBytes(content), overwrite: true, cancellationToken).ConfigureAwait(false);
        return new BackupWriteResult(fileName, AlreadyExisted: false);
    }
}
