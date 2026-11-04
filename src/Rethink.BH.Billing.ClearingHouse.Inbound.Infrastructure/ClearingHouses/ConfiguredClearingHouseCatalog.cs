using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Abstractions;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;

/// <summary>Clearing houses from configuration, indexed by id once at startup. When an id repeats, the first section wins.</summary>
public sealed class ConfiguredClearingHouseCatalog : IClearingHouseCatalog, IClearingHouseSettingsSource
{
    private readonly Dictionary<int, ClearingHouseSettings> _settingsById;
    private readonly Dictionary<int, ClearingHouseDescriptor> _descriptorsById;

    /// <summary>Creates the catalog.</summary>
    public ConfiguredClearingHouseCatalog(IOptions<ClearingHouseCatalogOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _settingsById = [];
        foreach (var settings in options.Value.ClearingHouses)
        {
            _settingsById.TryAdd(settings.Id, settings);
        }

        _descriptorsById = _settingsById.Values.ToDictionary(settings => settings.Id, settings => new ClearingHouseDescriptor(settings.Id, settings.Title));
    }

    /// <inheritdoc />
    public ClearingHouseDescriptor? FindById(int clearingHouseId)
        => _descriptorsById.GetValueOrDefault(clearingHouseId);

    /// <inheritdoc />
    public ClearingHouseSettings GetRequired(int clearingHouseId)
        => _settingsById.TryGetValue(clearingHouseId, out var settings)
            ? settings
            : throw new InvalidOperationException($"Clearing house {clearingHouseId} is not configured.");
}
