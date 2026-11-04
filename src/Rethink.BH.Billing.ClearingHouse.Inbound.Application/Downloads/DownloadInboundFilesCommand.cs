using MediatR;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Downloads every response file waiting in a clearing house SFTP directory.</summary>
/// <param name="ClearingHouseId">Clearing house id posted by the scheduler.</param>
public sealed record DownloadInboundFilesCommand(int ClearingHouseId) : IRequest<InboundDownloadResult>;
