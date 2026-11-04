using System.Text.RegularExpressions;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Secrets;

/// <summary>
/// Legacy rule for Billing API settings: a value made only of letters, digits, and dashes (at most 127 characters)
/// is a Key Vault secret name; anything else, such as a URL, is a literal. Secret values are trimmed and unquoted.
/// </summary>
public sealed partial class SecretNamePatternPolicy : ISecretReferencePolicy
{
    /// <summary>Longest Key Vault secret name.</summary>
    public const int MaxSecretNameLength = 127;

    /// <summary>Shared instance. The policy is stateless.</summary>
    public static readonly SecretNamePatternPolicy Instance = new();

    private SecretNamePatternPolicy()
    {
    }

    /// <inheritdoc />
    public bool IsSecretReference(string configuredValue)
    {
        ArgumentNullException.ThrowIfNull(configuredValue);
        return configuredValue.Length <= MaxSecretNameLength && SecretNamePattern().IsMatch(configuredValue);
    }

    /// <inheritdoc />
    public string NormalizeSecretValue(string secretValue)
    {
        ArgumentNullException.ThrowIfNull(secretValue);
        return secretValue.Trim().Trim('"');
    }

    [GeneratedRegex("^[0-9a-zA-Z-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SecretNamePattern();
}
