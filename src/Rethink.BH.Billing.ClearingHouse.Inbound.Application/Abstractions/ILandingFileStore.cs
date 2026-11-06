using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Encrypted landing storage that downloaded files are streamed into before processing.</summary>
public interface ILandingFileStore
{
    /// <summary>
    /// Streams content into <paramref name="blobName"/>, replacing any partial earlier attempt, and returns the byte count and
    /// SHA-256 computed while writing. <paramref name="writeContent"/> writes to the stream it is given and must not dispose it.
    /// </summary>
    Task<LandedFile> WriteAsync(string blobName, Func<Stream, CancellationToken, Task> writeContent, CancellationToken cancellationToken);

    /// <summary>Reads a landed file. The caller owns and must dispose the returned content.</summary>
    Task<IFileContent> ReadAsync(string blobName, long expectedLength, CancellationToken cancellationToken);
}
