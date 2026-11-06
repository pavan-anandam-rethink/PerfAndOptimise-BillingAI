using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Streams one listed file from SFTP into landing storage.</summary>
/// <param name="WorkItem">The download message.</param>
public sealed record DownloadInboundFileCommand(InboundFileWorkItem WorkItem) : IRequest<WorkItemDisposition>;
