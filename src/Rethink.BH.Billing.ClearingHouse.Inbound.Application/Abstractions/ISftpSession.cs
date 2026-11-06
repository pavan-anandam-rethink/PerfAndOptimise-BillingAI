using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>A bounded pool of SFTP connections on one download directory. Safe for concurrent use.</summary>
public interface ISftpSession : IAsyncDisposable
{
    /// <summary>Lists files in the download directory. Directories and dot files are excluded.</summary>
    Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Downloads one file. The caller owns and must dispose the returned content.</summary>
    Task<IFileContent> DownloadAsync(RemoteFile file, CancellationToken cancellationToken);

    /// <summary>Streams one file into <paramref name="destination"/> without buffering the whole file. Never retried, because the destination may hold a partial copy.</summary>
    Task CopyToAsync(RemoteFile file, Stream destination, CancellationToken cancellationToken);

    /// <summary>Deletes one remote file. Throws when the delete fails.</summary>
    Task DeleteAsync(RemoteFile file, CancellationToken cancellationToken);
}
