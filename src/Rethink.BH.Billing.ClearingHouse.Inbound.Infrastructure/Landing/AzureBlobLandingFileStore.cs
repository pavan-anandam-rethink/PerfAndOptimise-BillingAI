using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Storage;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>
/// Landing storage on Azure Blob. Writes stream through <see cref="StagedBlockUploadStream"/> (one pooled block in memory,
/// committed only on success); reads fill a pooled buffer sized from the listed length.
/// </summary>
public sealed class AzureBlobLandingFileStore : ILandingFileStore
{
    private readonly BlobContainerProvider _containerProvider;
    private readonly int _blockSize;

    /// <summary>Creates the store.</summary>
    public AzureBlobLandingFileStore(LandingContainer container, IOptions<LandingStorageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(options);

        _containerProvider = container.Provider;
        _blockSize = options.Value.BlockSizeKiB * 1024;
    }

    /// <inheritdoc />
    public async Task<LandedFile> WriteAsync(string blobName, Func<Stream, CancellationToken, Task> writeContent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentNullException.ThrowIfNull(writeContent);

        var container = await _containerProvider.GetContainerAsync(cancellationToken).ConfigureAwait(false);
        await using var upload = new StagedBlockUploadStream(new BlockBlobStager(container.GetBlockBlobClient(blobName)), _blockSize);
        await writeContent(upload, cancellationToken).ConfigureAwait(false);
        var (length, sha256) = await upload.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new LandedFile(blobName, length, sha256);
    }

    /// <inheritdoc />
    public async Task<IFileContent> ReadAsync(string blobName, long expectedLength, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);

        var container = await _containerProvider.GetContainerAsync(cancellationToken).ConfigureAwait(false);
        var content = new PooledFileContentStream(expectedLength);
        using var guard = new OwnershipGuard<PooledFileContentStream>(content);
        await container.GetBlobClient(blobName).DownloadToAsync(content, cancellationToken).ConfigureAwait(false);
        return guard.Release();
    }
}
