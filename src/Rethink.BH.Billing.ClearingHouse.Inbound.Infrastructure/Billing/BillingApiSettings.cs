namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>Resolved Billing API settings. <see cref="ToString"/> never includes the API key.</summary>
public sealed class BillingApiSettings
{
    /// <summary>Creates the settings.</summary>
    public BillingApiSettings(Uri baseAddress, string? apiKey, string headerName)
    {
        BaseAddress = baseAddress;
        ApiKey = apiKey;
        HeaderName = headerName;
    }

    /// <summary>Billing base address.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Application key, or null when none is configured.</summary>
    public string? ApiKey { get; }

    /// <summary>Header that carries the application key.</summary>
    public string HeaderName { get; }

    /// <inheritdoc />
    public override string ToString() => BaseAddress.ToString();
}
