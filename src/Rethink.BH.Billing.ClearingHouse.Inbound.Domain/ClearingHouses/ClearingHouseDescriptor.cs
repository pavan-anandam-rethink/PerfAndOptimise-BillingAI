namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

/// <summary>A configured clearing house whose SFTP download directory is polled.</summary>
/// <param name="Id">Clearing house id posted by the scheduler, for example 1 for Availity or 8 for Stedi.</param>
/// <param name="Title">Title sent to Billing with every registered file.</param>
public sealed record ClearingHouseDescriptor(int Id, string Title);
