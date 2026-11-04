using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Decorator that retries a transient connect failure and returns a session that retries list and download.</summary>
public sealed class RetryingSftpSessionFactory : ISftpSessionFactory
{
    private readonly ISftpSessionFactory _inner;
    private readonly SftpRetryPipelines _pipelines;

    /// <summary>Wraps <paramref name="inner"/>.</summary>
    public RetryingSftpSessionFactory(ISftpSessionFactory inner, SftpRetryPipelines pipelines)
    {
        _inner = inner;
        _pipelines = pipelines;
    }

    /// <inheritdoc />
    public async Task<ISftpSession> OpenAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken)
    {
        var session = await _pipelines.Open
            .ExecuteAsync(async token => await _inner.OpenAsync(clearingHouse, token).ConfigureAwait(false), cancellationToken)
            .ConfigureAwait(false);

        return new RetryingSftpSession(session, _pipelines);
    }
}
