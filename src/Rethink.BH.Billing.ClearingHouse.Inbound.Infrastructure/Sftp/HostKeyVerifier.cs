namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Sftp;

/// <summary>Compares a presented SSH host key fingerprint with the pinned one.</summary>
public static class HostKeyVerifier
{
    private const string Sha256Prefix = "SHA256:";

    /// <summary>
    /// True when no fingerprint is pinned (legacy behavior) or when the SHA-256 fingerprints match.
    /// The <c>SHA256:</c> prefix and base64 padding are ignored on both sides.
    /// </summary>
    public static bool IsTrusted(string? presentedFingerprintSha256, string? pinnedFingerprintSha256)
    {
        if (string.IsNullOrWhiteSpace(pinnedFingerprintSha256))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(presentedFingerprintSha256)
            && string.Equals(Normalize(presentedFingerprintSha256), Normalize(pinnedFingerprintSha256), StringComparison.Ordinal);
    }

    private static string Normalize(string fingerprint)
    {
        var value = fingerprint.Trim();
        if (value.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[Sha256Prefix.Length..];
        }

        return value.TrimEnd('=');
    }
}
