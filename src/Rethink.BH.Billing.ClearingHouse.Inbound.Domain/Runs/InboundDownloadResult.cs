using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Runs;

/// <summary>Outcome of one <c>downloadEdiDataFromSftp</c> run.</summary>
/// <param name="Kind">How the run is reported.</param>
/// <param name="ClearingHouseId">Requested clearing house id.</param>
/// <param name="Files">Per-file outcomes in completion order.</param>
public sealed record InboundDownloadResult(DownloadRunKind Kind, int ClearingHouseId, IReadOnlyList<InboundFileOutcome> Files)
{
    /// <summary>Number of files that stay on SFTP.</summary>
    public int FailedCount => Files.Count(file => file.IsFailed);

    /// <summary>The clearing house id is not configured.</summary>
    public static InboundDownloadResult Invalid(int clearingHouseId) => new(DownloadRunKind.Invalid, clearingHouseId, []);

    /// <summary>Nothing was processed.</summary>
    public static InboundDownloadResult Empty(int clearingHouseId) => new(DownloadRunKind.Empty, clearingHouseId, []);

    /// <summary>The connection or listing failed before any file was processed.</summary>
    public static InboundDownloadResult ConnectionFailed(int clearingHouseId) => new(DownloadRunKind.Failed, clearingHouseId, []);

    /// <summary>Classifies a run from its file outcomes: none is empty, any failure is failed, otherwise completed.</summary>
    public static InboundDownloadResult FromOutcomes(int clearingHouseId, IReadOnlyList<InboundFileOutcome> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            return new InboundDownloadResult(DownloadRunKind.Empty, clearingHouseId, files);
        }

        var kind = files.Any(file => file.IsFailed) ? DownloadRunKind.Failed : DownloadRunKind.Completed;
        return new InboundDownloadResult(kind, clearingHouseId, files);
    }
}
