using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Reads a landed file, verifies it, writes the legacy backup, and registers it with Billing.</summary>
public interface ILandedFileRegistrar
{
    /// <summary>Registers <paramref name="entry"/>. Throws when the landed content is missing or does not match its hash.</summary>
    Task<BillingRegistration> RegisterAsync(ClearingHouseDescriptor clearingHouse, InboundFileEntry entry, CancellationToken cancellationToken);
}
