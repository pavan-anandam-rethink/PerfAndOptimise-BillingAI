using System.Globalization;
using System.Text;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Landing;

/// <summary>Stages numbered blocks on a block blob and commits them in order. Uncommitted blocks are never readable.</summary>
public sealed class BlockBlobStager
{
    /// <summary>Metadata key holding the SHA-256 of the committed blob.</summary>
    public const string Sha256MetadataKey = "sha256";

    private readonly BlockBlobClient _blob;
    private readonly List<string> _blockIds = [];

    /// <summary>Creates the stager for <paramref name="blob"/>.</summary>
    public BlockBlobStager(BlockBlobClient blob) => _blob = blob;

    /// <summary>Stages the first <paramref name="count"/> bytes of <paramref name="buffer"/> as the next block, without copying.</summary>
    public async Task StageAsync(byte[] buffer, int count, CancellationToken cancellationToken)
    {
        var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(_blockIds.Count.ToString("D6", CultureInfo.InvariantCulture)));
        using var content = new MemoryStream(buffer, 0, count, writable: false);
        await _blob.StageBlockAsync(blockId, content, cancellationToken: cancellationToken).ConfigureAwait(false);
        _blockIds.Add(blockId);
    }

    /// <summary>Commits every staged block with the content hash as metadata.</summary>
    public Task CommitAsync(string sha256, CancellationToken cancellationToken)
    {
        var options = new CommitBlockListOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { [Sha256MetadataKey] = sha256 },
        };
        return _blob.CommitBlockListAsync(_blockIds, options, cancellationToken);
    }
}
