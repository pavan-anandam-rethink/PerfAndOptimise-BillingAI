using System.Text.Json;
using System.Text.Json.Serialization;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Messaging;

/// <summary>Message body format: compact camel-case JSON, source generated (no reflection, trimming safe).</summary>
public static class WorkItemSerializer
{
    /// <summary>Body content type.</summary>
    public const string ContentType = "application/json";

    /// <summary>Serializes <paramref name="item"/> to UTF-8 JSON.</summary>
    public static BinaryData Serialize(InboundFileWorkItem item)
        => BinaryData.FromBytes(JsonSerializer.SerializeToUtf8Bytes(item, WorkItemJsonContext.Default.InboundFileWorkItem));

    /// <summary>Reads a work item, or returns null when the body is not one (malformed, empty, or missing the file id).</summary>
    public static InboundFileWorkItem? TryDeserialize(BinaryData body)
    {
        ArgumentNullException.ThrowIfNull(body);

        try
        {
            var item = JsonSerializer.Deserialize(body.ToMemory().Span, WorkItemJsonContext.Default.InboundFileWorkItem);
            return item is null || item.FileId == Guid.Empty || item.Attempt < 0 ? null : item;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Source-generated JSON metadata for <see cref="InboundFileWorkItem"/>.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(InboundFileWorkItem))]
internal sealed partial class WorkItemJsonContext : JsonSerializerContext;
