namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>Turns a configured value into the value the service uses, reading Key Vault when the value is a secret name.</summary>
public interface ISecretResolver
{
    /// <summary>
    /// Returns the literal value or the Key Vault secret. Throws <see cref="InvalidOperationException"/> when the setting
    /// is blank, when a secret is needed but no vault is configured, or when the secret is empty.
    /// </summary>
    Task<string> ResolveAsync(string? configuredValue, string settingName, ISecretReferencePolicy policy, CancellationToken cancellationToken);
}
