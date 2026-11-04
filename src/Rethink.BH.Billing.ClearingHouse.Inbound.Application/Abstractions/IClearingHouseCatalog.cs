using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>Configured clearing houses. Ids and titles come from configuration, never from code.</summary>
public interface IClearingHouseCatalog
{
    /// <summary>Returns the clearing house with <paramref name="clearingHouseId"/>, or null when it is not configured.</summary>
    ClearingHouseDescriptor? FindById(int clearingHouseId);
}
