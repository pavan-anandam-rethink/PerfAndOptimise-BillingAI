using System.Diagnostics.CodeAnalysis;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Result of claiming a file for one message.</summary>
public sealed record WorkItemClaim
{
    private WorkItemClaim(InboundFileEntry? entry, ClearingHouseDescriptor? clearingHouse, WorkItemDisposition disposition)
    {
        Entry = entry;
        ClearingHouse = clearingHouse;
        Disposition = disposition;
    }

    /// <summary>The claimed file, already in its in-progress state. Null when not claimed.</summary>
    public InboundFileEntry? Entry { get; }

    /// <summary>The file's clearing house. Null when not claimed.</summary>
    public ClearingHouseDescriptor? ClearingHouse { get; }

    /// <summary>How to settle the message when the file was not claimed.</summary>
    public WorkItemDisposition Disposition { get; }

    /// <summary>True when this worker owns the file.</summary>
    [MemberNotNullWhen(true, nameof(Entry), nameof(ClearingHouse))]
    public bool IsClaimed => Entry is not null && ClearingHouse is not null;

    /// <summary>A successful claim.</summary>
    public static WorkItemClaim Claimed(InboundFileEntry entry, ClearingHouseDescriptor clearingHouse) => new(entry, clearingHouse, WorkItemDisposition.Completed);

    /// <summary>No claim; settle the message with <paramref name="disposition"/>.</summary>
    public static WorkItemClaim NotClaimed(WorkItemDisposition disposition) => new(null, null, disposition);
}
