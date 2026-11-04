using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Rethink.BH.Billing.ClearingHouse.Inbound.Contracts.Billing;

/// <summary>
/// Body posted to Billing <c>ClearingHouse/UploadEDIResponseFile</c>. Property names and order match the Billing model.
/// The service streams this shape directly; the type documents the wire format and is used to verify it.
/// </summary>
public sealed class DownloadSftpFileRequest
{
    /// <summary>JSON name of <see cref="ClearingHouseId"/>.</summary>
    public const string ClearingHouseIdJsonName = "clearingHouseId";

    /// <summary>JSON name of <see cref="Data"/>.</summary>
    public const string DataJsonName = "Data";

    /// <summary>JSON name of <see cref="FileName"/>.</summary>
    public const string FileNameJsonName = "FileName";

    /// <summary>JSON name of <see cref="Title"/>.</summary>
    public const string TitleJsonName = "Title";

    /// <summary>Clearing house id.</summary>
    [JsonPropertyName(ClearingHouseIdJsonName)]
    public int ClearingHouseId { get; set; }

    /// <summary>Raw response file bytes, serialized as Base64.</summary>
    [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Wire model; Billing binds a byte array.")]
    [JsonPropertyName(DataJsonName)]
    public byte[] Data { get; set; } = [];

    /// <summary>Original remote file name.</summary>
    [JsonPropertyName(FileNameJsonName)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>Clearing house title, for example Availity or Stedi.</summary>
    [JsonPropertyName(TitleJsonName)]
    public string Title { get; set; } = string.Empty;
}
