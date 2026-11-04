using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>Chooses which listed files a run processes and in what order.</summary>
public interface IRemoteFileSelector
{
    /// <summary>Returns the files to process, oldest first, then by file name.</summary>
    IReadOnlyList<RemoteFile> SelectForProcessing(IReadOnlyList<RemoteFile> listed);
}
