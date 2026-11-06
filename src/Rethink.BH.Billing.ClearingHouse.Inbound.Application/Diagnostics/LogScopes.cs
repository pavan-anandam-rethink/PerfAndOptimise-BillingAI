using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.ClearingHouses;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Diagnostics;

/// <summary>
/// Log scope payloads. Application Insights writes each key as a custom dimension on every log and exception
/// inside the scope, so a failure can be traced by run, clearing house, or file name.
/// </summary>
public static class LogScopes
{
    /// <summary>Scope key for the download run id.</summary>
    public const string DownloadRunId = "DownloadRunId";

    /// <summary>Scope key for the clearing house id.</summary>
    public const string ClearingHouseId = "ClearingHouseId";

    /// <summary>Scope key for the clearing house title.</summary>
    public const string ClearingHouseTitle = "ClearingHouse";

    /// <summary>Scope key for the remote file name.</summary>
    public const string FileName = "FileName";

    /// <summary>Scope key for the remote file size.</summary>
    public const string FileSizeBytes = "FileSizeBytes";

    /// <summary>Scope key for the MediatR request name.</summary>
    public const string RequestName = "RequestName";

    /// <summary>Scope key for the manifest file id.</summary>
    public const string FileId = "FileId";

    /// <summary>Scope key for the pipeline stage.</summary>
    public const string PipelineStage = "PipelineStage";

    /// <summary>Scope key for the delivery attempt.</summary>
    public const string Attempt = "Attempt";

    /// <summary>Scope for one queue message.</summary>
    public static IReadOnlyDictionary<string, object> ForWorkItem(InboundWorkStage stage, InboundFileWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return new Dictionary<string, object>(4, StringComparer.Ordinal)
        {
            [FileId] = workItem.FileId.ToString("N"),
            [PipelineStage] = stage.ToString(),
            [Attempt] = workItem.Attempt,
            [ClearingHouseId] = workItem.ClearingHouseId,
        };
    }

    /// <summary>Scope for one download run.</summary>
    public static IReadOnlyDictionary<string, object> ForRun(ClearingHouseDescriptor clearingHouse, string downloadRunId)
    {
        ArgumentNullException.ThrowIfNull(clearingHouse);

        return new Dictionary<string, object>(3, StringComparer.Ordinal)
        {
            [DownloadRunId] = downloadRunId,
            [ClearingHouseId] = clearingHouse.Id,
            [ClearingHouseTitle] = clearingHouse.Title,
        };
    }

    /// <summary>Scope for one remote file.</summary>
    public static IReadOnlyDictionary<string, object> ForFile(RemoteFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        return new Dictionary<string, object>(2, StringComparer.Ordinal)
        {
            [FileName] = file.Name,
            [FileSizeBytes] = file.Size,
        };
    }

    /// <summary>Scope for one MediatR request.</summary>
    public static IReadOnlyDictionary<string, object> ForRequest(string requestName)
        => new Dictionary<string, object>(1, StringComparer.Ordinal) { [RequestName] = requestName };
}
