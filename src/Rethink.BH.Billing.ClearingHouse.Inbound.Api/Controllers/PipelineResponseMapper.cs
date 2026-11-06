using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers;

/// <summary>Maps pipeline results to response bodies. Only ids, counts, and times leave the service.</summary>
public static class PipelineResponseMapper
{
    /// <summary>Backlog body.</summary>
    public static PipelineBacklogResponse ToResponse(PipelineBacklog backlog)
    {
        ArgumentNullException.ThrowIfNull(backlog);

        var counts = backlog.CountsByState.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value, StringComparer.Ordinal);
        return new PipelineBacklogResponse(counts, backlog.OldestPendingDiscoveredUtc);
    }

    /// <summary>Replay body.</summary>
    public static ReplayResponse ToResponse(Guid fileId, ReplayResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReplayResponse(fileId, result.Outcome.ToString(), result.State?.ToString());
    }

    /// <summary>Listing body.</summary>
    public static ListingResponse ToResponse(InboundListingResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ListingResponse(result.ClearingHouseId, result.Kind.ToString(), result.Listed, result.Selected, result.Enqueued, result.Requeued);
    }

    /// <summary>Quarantined-file bodies.</summary>
    public static IReadOnlyList<QuarantinedFileResponse> ToResponses(IReadOnlyList<InboundFileEntry> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        return files
            .Select(file => new QuarantinedFileResponse(file.Id, file.ClearingHouseId, file.SizeBytes, file.Attempts, file.DiscoveredUtc, file.UpdatedUtc))
            .ToList();
    }
}
