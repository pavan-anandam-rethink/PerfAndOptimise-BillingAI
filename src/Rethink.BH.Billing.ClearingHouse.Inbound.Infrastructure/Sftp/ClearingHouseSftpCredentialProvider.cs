using System.Globalization;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Resolves the five SFTP settings (and the optional host key) in parallel through Key Vault.</summary>
public sealed class ClearingHouseSftpCredentialProvider : ISftpCredentialProvider
{
    private readonly IClearingHouseSettingsSource _settingsSource;
    private readonly ISecretResolver _secretResolver;

    /// <summary>Creates the provider.</summary>
    public ClearingHouseSftpCredentialProvider(IClearingHouseSettingsSource settingsSource, ISecretResolver secretResolver)
    {
        _settingsSource = settingsSource;
        _secretResolver = secretResolver;
    }

    /// <inheritdoc />
    public async Task<SftpCredentials> GetAsync(int clearingHouseId, CancellationToken cancellationToken)
    {
        var settings = _settingsSource.GetRequired(clearingHouseId);
        var section = settings.ConfigurationPath;

        var host = ResolveAsync(settings.Hosts, $"{section}:Hosts", cancellationToken);
        var portText = ResolveAsync(settings.Port, $"{section}:Port", cancellationToken);
        var userName = ResolveAsync(settings.UserName, $"{section}:UserName", cancellationToken);
        var password = ResolveAsync(settings.UserPassword, $"{section}:UserPassword", cancellationToken);
        var directory = ResolveAsync(settings.DownloadDirectory, $"{section}:DownloadDirectory", cancellationToken);
        var hostKey = string.IsNullOrWhiteSpace(settings.HostKeyFingerprint)
            ? Task.FromResult<string?>(null)
            : ResolveOptionalAsync(settings.HostKeyFingerprint, $"{section}:HostKeyFingerprint", cancellationToken);

        await Task.WhenAll(host, portText, userName, password, directory).ConfigureAwait(false);
        var hostKeyFingerprint = await hostKey.ConfigureAwait(false);

        if (!int.TryParse(await portText.ConfigureAwait(false), NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0)
        {
            throw new InvalidOperationException($"{section}:Port must be a positive number.");
        }

        return new SftpCredentials(
            await host.ConfigureAwait(false),
            port,
            await userName.ConfigureAwait(false),
            await password.ConfigureAwait(false),
            await directory.ConfigureAwait(false),
            hostKeyFingerprint);
    }

    private Task<string> ResolveAsync(string configuredValue, string settingName, CancellationToken cancellationToken)
        => _secretResolver.ResolveAsync(configuredValue, settingName, LiteralWhenContainsEqualsPolicy.Instance, cancellationToken);

    private async Task<string?> ResolveOptionalAsync(string configuredValue, string settingName, CancellationToken cancellationToken)
        => await _secretResolver.ResolveAsync(configuredValue, settingName, SecretNamePatternPolicy.Instance, cancellationToken).ConfigureAwait(false);
}
