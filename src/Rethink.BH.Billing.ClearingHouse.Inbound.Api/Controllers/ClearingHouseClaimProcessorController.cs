using MediatR;
using Microsoft.AspNetCore.Mvc;
using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Diagnostics;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Downloads;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Controllers;

/// <summary>Inbound response download. Route, body, and status codes match the legacy controller.</summary>
[Route("[controller]")]
[ApiController]
public sealed class ClearingHouseClaimProcessorController : ControllerBase
{
    private const string ControllerName = nameof(ClearingHouseClaimProcessorController);

    private readonly IMediator _mediator;
    private readonly ILogger<ClearingHouseClaimProcessorController> _logger;

    /// <summary>Creates the controller.</summary>
    public ClearingHouseClaimProcessorController(IMediator mediator, ILogger<ClearingHouseClaimProcessorController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>
    /// Downloads response files for a configured clearing house. The body is a JSON number, which is what the scheduled
    /// download posts.
    /// </summary>
    /// <param name="clearingHouseId">Clearing house id.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">Every file was backed up, queued in Billing, and removed from SFTP.</response>
    /// <response code="204">No files were waiting, or the run limit was reached before any file finished.</response>
    /// <response code="400">The clearing house id is not configured.</response>
    /// <response code="500">At least one file was left on SFTP; the body has the failed and total counts.</response>
    [HttpPost("downloadEdiDataFromSftp")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(string), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(DownloadFailureCounts), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadEdiDataFromSftp([FromBody] int clearingHouseId, CancellationToken cancellationToken)
    {
        ApiLog.DownloadStarting(_logger, ControllerName, clearingHouseId);

        var result = await _mediator.Send(new DownloadInboundFilesCommand(clearingHouseId), cancellationToken).ConfigureAwait(false);
        return result.Kind switch
        {
            DownloadRunKind.Invalid => InvalidClearingHouse(clearingHouseId),
            DownloadRunKind.Empty => EmptyDownload(clearingHouseId),
            DownloadRunKind.Failed => FailedDownload(result),
            _ => CompletedDownload(result),
        };
    }

    private BadRequestObjectResult InvalidClearingHouse(int clearingHouseId)
    {
        ApiLog.InvalidClearingHouse(_logger, ControllerName, clearingHouseId);
        return BadRequest("Invalid clearing house ID.");
    }

    private NoContentResult EmptyDownload(int clearingHouseId)
    {
        ApiLog.NoFilesDownloaded(_logger, ControllerName, clearingHouseId);
        return NoContent();
    }

    private ObjectResult FailedDownload(InboundDownloadResult result)
    {
        var failed = result.FailedCount;
        ApiLog.DownloadLeftFiles(_logger, ControllerName, result.ClearingHouseId, result.Files.Count, failed);
        return StatusCode(StatusCodes.Status500InternalServerError, new DownloadFailureCounts(failed, result.Files.Count));
    }

    private OkResult CompletedDownload(InboundDownloadResult result)
    {
        ApiLog.DownloadCompleted(_logger, ControllerName, result.ClearingHouseId, result.Files.Count, result.Files.Count);
        return Ok();
    }
}
