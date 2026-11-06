using MediatR;
using Microsoft.AspNetCore.Mvc;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Filters;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Pipeline;
using Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers;

/// <summary>
/// Operator endpoints for the queue pipeline: backlog, quarantined files, replay, and an on-demand listing. They need the
/// application key and the operator key, and answer 404 while the pipeline is off.
/// </summary>
[Route("inbound-pipeline")]
[ApiController]
[ServiceFilter(typeof(OperatorKeyFilter))]
public sealed class InboundPipelineController : ControllerBase
{
    private const int DefaultPageSize = 100;

    private readonly ISender _sender;
    private readonly ILogger<InboundPipelineController> _logger;

    /// <summary>Creates the controller.</summary>
    public InboundPipelineController(ISender sender, ILogger<InboundPipelineController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    /// <summary>File count per state and the oldest unfinished file.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("backlog")]
    [ProducesResponseType(typeof(PipelineBacklogResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBacklogAsync(CancellationToken cancellationToken)
    {
        var backlog = await _sender.Send(new GetPipelineBacklogQuery(), cancellationToken).ConfigureAwait(false);
        return Ok(PipelineResponseMapper.ToResponse(backlog));
    }

    /// <summary>Quarantined files, oldest first.</summary>
    /// <param name="maxCount">Most files returned (1–500).</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpGet("quarantined")]
    [ProducesResponseType(typeof(IReadOnlyList<QuarantinedFileResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListQuarantinedAsync([FromQuery] int maxCount = DefaultPageSize, CancellationToken cancellationToken = default)
    {
        var files = await _sender.Send(new ListQuarantinedFilesQuery(maxCount), cancellationToken).ConfigureAwait(false);
        return Ok(PipelineResponseMapper.ToResponses(files));
    }

    /// <summary>Resets a quarantined file and sends it back to the stage it failed in.</summary>
    /// <param name="fileId">Manifest id.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="202">The file was requeued.</response>
    /// <response code="404">No file has that id.</response>
    /// <response code="409">The file is not quarantined.</response>
    [HttpPost("files/{fileId:guid}/replay")]
    [ProducesResponseType(typeof(ReplayResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ReplayResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ReplayResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplayAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ReplayQuarantinedFileCommand(fileId), cancellationToken).ConfigureAwait(false);
        ApiLog.ReplayRequested(_logger, fileId, result.Outcome.ToString());

        var body = PipelineResponseMapper.ToResponse(fileId, result);
        return result.Outcome switch
        {
            ReplayOutcome.Requeued => Accepted(body),
            ReplayOutcome.NotFound => NotFound(body),
            _ => Conflict(body),
        };
    }

    /// <summary>Lists one clearing house now instead of waiting for the hourly run.</summary>
    /// <param name="clearingHouseId">Clearing house id.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    [HttpPost("clearing-houses/{clearingHouseId:int}/list")]
    [ProducesResponseType(typeof(ListingResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync(int clearingHouseId, CancellationToken cancellationToken)
    {
        var result = await _sender.Send(new ListInboundFilesCommand(clearingHouseId), cancellationToken).ConfigureAwait(false);
        return Ok(PipelineResponseMapper.ToResponse(result));
    }
}
