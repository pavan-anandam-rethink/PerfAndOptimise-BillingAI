namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>
/// Queue message body. Carries ids only, never file names or content, so queue storage and dead-letter tooling hold no PHI.
/// </summary>
/// <param name="FileId">Manifest id of the file.</param>
/// <param name="ClearingHouseId">Clearing house of the file.</param>
/// <param name="Attempt">Zero for the first delivery; incremented for each scheduled retry.</param>
public sealed record InboundFileWorkItem(Guid FileId, int ClearingHouseId, int Attempt)
{
    /// <summary>The first message for a file.</summary>
    public static InboundFileWorkItem First(InboundFileEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new InboundFileWorkItem(entry.Id, entry.ClearingHouseId, 0);
    }

    /// <summary>The message for the next retry.</summary>
    public InboundFileWorkItem NextAttempt() => this with { Attempt = Attempt + 1 };
}
