using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;

/// <summary>
/// Reads the allow-listed identifiers (never PHI) from action arguments, route values, and the query string so request
/// logs can be correlated by clearing house or funder.
/// </summary>
public static class SafeIdReader
{
    private static readonly string[] SafeIdNames = ["FunderId", "AccountInfoId", "MemberId", "ClearingHouseId"];

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SafeProperties = new();

    /// <summary>Returns the safe identifiers as <c>Name=value</c> pairs separated by commas.</summary>
    public static string Format(ActionExecutingContext context)
        => string.Join(", ", Read(context).Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>Returns the safe identifiers, first value wins.</summary>
    public static IReadOnlyDictionary<string, object> Read(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ids = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in context.ActionArguments)
        {
            AddArgument(ids, name, value);
        }

        foreach (var name in SafeIdNames)
        {
            if (context.RouteData.Values.TryGetValue(name, out var routeValue))
            {
                TryAdd(ids, name, routeValue);
            }

            if (context.HttpContext.Request.Query.TryGetValue(name, out var queryValue))
            {
                TryAdd(ids, name, queryValue.ToString());
            }
        }

        return ids;
    }

    private static void AddArgument(Dictionary<string, object> ids, string name, object? value)
    {
        if (value is null)
        {
            return;
        }

        if (IsSimple(value))
        {
            TryAdd(ids, name, value);
            return;
        }

        foreach (var property in SafeProperties.GetOrAdd(value.GetType(), FindSafeProperties))
        {
            TryAdd(ids, property.Name, property.GetValue(value));
        }
    }

    private static void TryAdd(Dictionary<string, object> ids, string name, object? value)
    {
        var canonicalName = CanonicalName(name);
        if (canonicalName is null || value is null || (value is string text && string.IsNullOrWhiteSpace(text)))
        {
            return;
        }

        ids.TryAdd(canonicalName, value);
    }

    private static string? CanonicalName(string name)
        => Array.Find(SafeIdNames, safeName => safeName.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static PropertyInfo[] FindSafeProperties(Type type)
        => type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.GetMethod is not null && CanonicalName(property.Name) is not null)
            .ToArray();

    private static bool IsSimple(object value)
        => value.GetType().IsPrimitive || value is string or decimal or Guid;
}
