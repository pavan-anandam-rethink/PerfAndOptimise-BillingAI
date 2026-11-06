using Microsoft.Extensions.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Application.Options;
using Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Application.Downloads;

/// <summary>
/// Orders files by modified time, then by ordinal file name, which is the legacy order.
/// When <see cref="InboundDownloadOptions.MinimumFileAgeSeconds"/> is set, files that may still be uploading are skipped.
/// </summary>
public sealed class RemoteFileSelector : IRemoteFileSelector
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumAge;

    /// <summary>Creates the selector.</summary>
    public RemoteFileSelector(TimeProvider timeProvider, IOptions<InboundDownloadOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider;
        _minimumAge = TimeSpan.FromSeconds(options.Value.MinimumFileAgeSeconds);
    }

    /// <inheritdoc />
    public IReadOnlyList<RemoteFile> SelectForProcessing(IReadOnlyList<RemoteFile> listed)
        => SelectStable(listed, _timeProvider.GetUtcNow().UtcDateTime, _minimumAge);

    /// <summary>Files modified at least <paramref name="minimumAge"/> before <paramref name="nowUtc"/>, in legacy order.</summary>
    public static IReadOnlyList<RemoteFile> SelectStable(IReadOnlyList<RemoteFile> listed, DateTime nowUtc, TimeSpan minimumAge)
    {
        ArgumentNullException.ThrowIfNull(listed);

        IEnumerable<RemoteFile> candidates = listed;
        if (minimumAge > TimeSpan.Zero)
        {
            var newestAllowed = nowUtc - minimumAge;
            candidates = candidates.Where(file => file.ModifiedAtUtc <= newestAllowed);
        }

        return candidates
            .OrderBy(file => file.ModifiedAtUtc)
            .ThenBy(file => file.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
