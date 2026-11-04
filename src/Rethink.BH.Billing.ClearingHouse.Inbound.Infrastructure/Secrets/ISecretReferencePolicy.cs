namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>Decides whether a configured value is a Key Vault secret name or a literal, and how a secret value is cleaned.</summary>
public interface ISecretReferencePolicy
{
    /// <summary>True when <paramref name="configuredValue"/> names a Key Vault secret.</summary>
    bool IsSecretReference(string configuredValue);

    /// <summary>Cleans a value read from Key Vault.</summary>
    string NormalizeSecretValue(string secretValue);
}
