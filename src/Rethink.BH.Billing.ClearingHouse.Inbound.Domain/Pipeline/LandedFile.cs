namespace Rethink.BH.Billing.ClearingHouse.Inbound.Domain.Pipeline;

/// <summary>A file written to landing storage.</summary>
/// <param name="BlobName">Landing blob name.</param>
/// <param name="Length">Bytes written.</param>
/// <param name="Sha256">Upper-case hex SHA-256 of the bytes written.</param>
public sealed record LandedFile(string BlobName, long Length, string Sha256);
