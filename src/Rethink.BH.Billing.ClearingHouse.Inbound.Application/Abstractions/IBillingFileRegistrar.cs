using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;

/// <summary>
/// Billing boundary for downloaded response files.
/// Billing stores the file and is the only publisher of the response-processing message.
/// </summary>
public interface IBillingFileRegistrar
{
    /// <summary>Registers one downloaded file with Billing.</summary>
    Task<BillingRegistration> RegisterAsync(
        ClearingHouseDescriptor clearingHouse,
        string fileName,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}
