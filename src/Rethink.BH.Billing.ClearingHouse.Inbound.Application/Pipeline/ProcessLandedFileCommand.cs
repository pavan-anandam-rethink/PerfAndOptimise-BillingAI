using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Backs up and registers one landed file with Billing, then deletes the remote copy.</summary>
/// <param name="WorkItem">The process message.</param>
public sealed record ProcessLandedFileCommand(InboundFileWorkItem WorkItem) : IRequest<WorkItemDisposition>;
