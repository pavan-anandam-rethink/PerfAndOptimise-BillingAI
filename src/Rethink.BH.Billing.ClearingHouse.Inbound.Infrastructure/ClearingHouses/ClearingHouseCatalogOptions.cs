using Microsoft.Extensions.Configuration;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.ClearingHouses;

/// <summary>Clearing houses bound from <c>Clearinghouses</c>.</summary>
public sealed class ClearingHouseCatalogOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Clearinghouses";

    /// <summary>Clearing houses that have an id and a title.</summary>
    public IReadOnlyList<ClearingHouseSettings> ClearingHouses { get; set; } = [];

    /// <summary>True when at least one clearing house is configured and every one is complete.</summary>
    public bool IsValid => ClearingHouses.Count > 0 && ClearingHouses.All(settings => settings.IsComplete);

    /// <summary>Binds every child of <c>Clearinghouses</c> that has a positive id and a title.</summary>
    public static IReadOnlyList<ClearingHouseSettings> Bind(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var clearingHouses = new List<ClearingHouseSettings>();
        foreach (var section in configuration.GetSection(SectionName).GetChildren())
        {
            var settings = section.Get<ClearingHouseSettings>() ?? new ClearingHouseSettings();
            settings.SectionKey = section.Key;
            if (settings.Id > 0 && !string.IsNullOrWhiteSpace(settings.Title))
            {
                clearingHouses.Add(settings);
            }
        }

        return clearingHouses;
    }
}
