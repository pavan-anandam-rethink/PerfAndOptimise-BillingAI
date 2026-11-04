namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;

/// <summary>Configured SFTP settings for a clearing house.</summary>
public interface IClearingHouseSettingsSource
{
    /// <summary>Returns the settings for <paramref name="clearingHouseId"/>. Throws when it is not configured.</summary>
    ClearingHouseSettings GetRequired(int clearingHouseId);
}
