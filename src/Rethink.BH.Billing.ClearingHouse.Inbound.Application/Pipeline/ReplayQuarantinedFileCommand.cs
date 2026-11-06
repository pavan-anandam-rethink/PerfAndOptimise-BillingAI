using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Outcome of a replay request.</summary>
public enum ReplayOutcome
{
    /// <summary>No file has that id.</summary>
    NotFound,

    /// <summary>The file is not quarantined; nothing changed.</summary>
    NotQuarantined,

    /// <summary>The file was reset and sent to its stage.</summary>
    Requeued,
}

/// <summary>Result of a replay request.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="State">The file's state after the request, or null when not found.</param>
public sealed record ReplayResult(ReplayOutcome Outcome, InboundFileState? State);

/// <summary>Operator action: reset a quarantined file's attempts and send it back to the stage it failed in.</summary>
/// <param name="FileId">Manifest id.</param>
public sealed record ReplayQuarantinedFileCommand(Guid FileId) : IRequest<ReplayResult>;
