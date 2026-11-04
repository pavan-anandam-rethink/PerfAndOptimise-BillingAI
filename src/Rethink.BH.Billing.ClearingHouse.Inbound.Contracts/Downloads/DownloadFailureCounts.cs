namespace Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Downloads;

/// <summary>Body returned when a download run fails. File names and exception text stay off the response.</summary>
/// <param name="Failed">Files left on SFTP.</param>
/// <param name="Total">Files processed in the run.</param>
public sealed record DownloadFailureCounts(int Failed, int Total);
