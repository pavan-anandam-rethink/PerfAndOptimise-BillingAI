namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>Resolves the Billing API address and key.</summary>
public interface IBillingApiSettingsProvider
{
    /// <summary>Returns the resolved settings. Throws when the URL is missing or not absolute.</summary>
    Task<BillingApiSettings> GetAsync(CancellationToken cancellationToken);
}
