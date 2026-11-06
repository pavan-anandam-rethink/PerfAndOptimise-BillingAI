using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;

/// <summary>Lists one clearing house, registers new stable files in the manifest, and enqueues their downloads.</summary>
/// <param name="ClearingHouseId">Clearing house to list.</param>
public sealed record ListInboundFilesCommand(int ClearingHouseId) : IRequest<InboundListingResult>;
