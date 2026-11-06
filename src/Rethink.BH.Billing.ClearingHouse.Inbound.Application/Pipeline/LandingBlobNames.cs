using System.Globalization;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Landing blob layout: <c>{clearingHouseId}/{yyyy}/{MM}/{dd}/{fileId}/{fileName}</c>, partitioned by discovery date for lifecycle rules.</summary>
public static class LandingBlobNames
{
    /// <summary>The landing blob name of <paramref name="entry"/>.</summary>
    public static string For(InboundFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{entry.ClearingHouseId}/{entry.DiscoveredUtc:yyyy/MM/dd}/{entry.Id:N}/{entry.FileName}");
    }
}
