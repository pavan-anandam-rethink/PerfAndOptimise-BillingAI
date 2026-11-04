using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Diagnostics;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Resolves credentials and opens a pooled SSH.NET session on the clearing house download directory.</summary>
public sealed class SshNetSftpSessionFactory : ISftpSessionFactory
{
    private readonly ISftpCredentialProvider _credentialProvider;
    private readonly ISftpClientFactory _clientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly InboundDownloadOptions _options;
    private readonly ILogger<SshNetSftpSessionFactory> _logger;

    /// <summary>Creates the factory.</summary>
    public SshNetSftpSessionFactory(
        ISftpCredentialProvider credentialProvider,
        ISftpClientFactory clientFactory,
        TimeProvider timeProvider,
        IOptions<InboundDownloadOptions> options,
        ILogger<SshNetSftpSessionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _credentialProvider = credentialProvider;
        _clientFactory = clientFactory;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ISftpSession> OpenAsync(ClearingHouseDescriptor clearingHouse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clearingHouse);

        var credentials = await _credentialProvider.GetAsync(clearingHouse.Id, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(credentials.HostKeyFingerprint))
        {
            InfrastructureLog.SftpHostKeyNotPinned(_logger, credentials.Host);
        }

        InfrastructureLog.SftpPoolOpening(_logger, credentials.Host, credentials.Port, _options.SftpPoolSize);
        var startedAt = _timeProvider.GetTimestamp();

        var session = await SshNetSftpSession
            .OpenAsync(_clientFactory, credentials, _options.SftpPoolSize, TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds), _logger, cancellationToken)
            .ConfigureAwait(false);

        InfrastructureLog.SftpPoolConnected(_logger, credentials.DownloadDirectory, (long)_timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        return session;
    }
}
