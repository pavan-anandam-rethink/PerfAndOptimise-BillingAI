using System.Text.Json;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Registrations;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>
/// Interprets Billing <c>UploadEDIResponseFile</c> responses exactly as the legacy client did:
/// a non-success status is a rejection carrying the body; <c>true</c>/<c>false</c> (quoted or not) means accepted and queued;
/// a JSON object uses <c>success</c>, <c>queued</c>, and <c>fileId</c>; anything else is a rejection.
/// </summary>
public static class BillingRegistrationParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly RegistrationWire NotRegistered = new(false, false, null);

    /// <summary>Parses one response.</summary>
    public static BillingRegistration Parse(bool isSuccessStatusCode, string? body)
    {
        if (!isSuccessStatusCode)
        {
            return BillingRegistration.Rejected(body);
        }

        var wire = ReadRegistration(ReadTextPayload(body ?? string.Empty));
        var accepted = wire.Success ?? (wire.Queued == true || wire.FileId is not null);
        if (!accepted)
        {
            return BillingRegistration.Rejected(null);
        }

        return wire.Queued ?? true ? BillingRegistration.Succeeded(wire.FileId) : BillingRegistration.NotQueued(wire.FileId);
    }

    private static string ReadTextPayload(string body)
    {
        var trimmed = body.Trim();
        return trimmed.StartsWith('"')
            ? JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty
            : trimmed;
    }

    private static RegistrationWire ReadRegistration(string payload)
    {
        var trimmed = payload.Trim();
        if (bool.TryParse(trimmed, out var accepted))
        {
            return new RegistrationWire(accepted, accepted, null);
        }

        return trimmed.StartsWith('{')
            ? JsonSerializer.Deserialize<RegistrationWire>(trimmed, SerializerOptions) ?? NotRegistered
            : NotRegistered;
    }

    private sealed record RegistrationWire(bool? Success, bool? Queued, int? FileId);
}
