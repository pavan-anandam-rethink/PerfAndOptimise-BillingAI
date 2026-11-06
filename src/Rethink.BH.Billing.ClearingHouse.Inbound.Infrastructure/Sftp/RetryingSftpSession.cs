using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Decorator that retries transient list and download failures. Streaming copy and delete pass straight through.</summary>
public sealed class RetryingSftpSession : ISftpSession
{
    private readonly ISftpSession _inner;
    private readonly SftpRetryPipelines _pipelines;

    /// <summary>Wraps <paramref name="inner"/>; disposing this session disposes it.</summary>
    public RetryingSftpSession(ISftpSession inner, SftpRetryPipelines pipelines)
    {
        _inner = inner;
        _pipelines = pipelines;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RemoteFile>> ListAsync(CancellationToken cancellationToken)
        => await _pipelines.List
            .ExecuteAsync(async token => await _inner.ListAsync(token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IFileContent> DownloadAsync(RemoteFile file, CancellationToken cancellationToken)
        => await _pipelines.Download
            .ExecuteAsync(async token => await _inner.DownloadAsync(file, token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public Task CopyToAsync(RemoteFile file, Stream destination, CancellationToken cancellationToken) => _inner.CopyToAsync(file, destination, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(RemoteFile file, CancellationToken cancellationToken) => _inner.DeleteAsync(file, cancellationToken);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
