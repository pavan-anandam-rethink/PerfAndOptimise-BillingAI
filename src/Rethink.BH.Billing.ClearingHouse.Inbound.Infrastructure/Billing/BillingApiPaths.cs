namespace Rethink.BH.Billing.ClearingHouse.Inbound.Infrastructure.Billing;

/// <summary>Relative paths on the Billing clearing house API.</summary>
public static class BillingApiPaths
{
    /// <summary>Inbound response upload used by the SFTP download.</summary>
    public const string UploadEdiResponseFile = "ClearingHouse/UploadEDIResponseFile";

    /// <summary>Named HttpClient used for Billing calls.</summary>
    public const string HttpClientName = "BillingApi";

    /// <summary>Legacy header that also carries the application key on every Billing request.</summary>
    public const string LegacyApiKeyHeader = "XApiKey";
}
