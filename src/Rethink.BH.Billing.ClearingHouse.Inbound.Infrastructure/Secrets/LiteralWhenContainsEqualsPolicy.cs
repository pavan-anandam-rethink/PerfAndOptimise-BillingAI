namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>
/// Legacy rule for SFTP and blob settings: a value containing <c>=</c> (a connection string) is literal;
/// any other value is a Key Vault secret name. Secret values are used unchanged.
/// </summary>
public sealed class LiteralWhenContainsEqualsPolicy : ISecretReferencePolicy
{
    /// <summary>Shared instance. The policy is stateless.</summary>
    public static readonly LiteralWhenContainsEqualsPolicy Instance = new();

    private LiteralWhenContainsEqualsPolicy()
    {
    }

    /// <inheritdoc />
    public bool IsSecretReference(string configuredValue)
    {
        ArgumentNullException.ThrowIfNull(configuredValue);
        return !configuredValue.Contains('=', StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public string NormalizeSecretValue(string secretValue) => secretValue;
}
