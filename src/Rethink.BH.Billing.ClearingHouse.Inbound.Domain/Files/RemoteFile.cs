namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Files;

/// <summary>A file listed in a clearing house download directory.</summary>
/// <param name="RemotePath">Full path on the SFTP server.</param>
/// <param name="Name">File name without the directory.</param>
/// <param name="Size">Size in bytes reported by the listing.</param>
/// <param name="ModifiedAtUtc">Last write time reported by the listing.</param>
public sealed record RemoteFile(string RemotePath, string Name, long Size, DateTime ModifiedAtUtc);
