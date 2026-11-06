namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Middleware;

/// <summary>Header names used by the host and the scheduled download.</summary>
public static class ApiHeaderNames
{
    /// <summary>Header this host authorizes.</summary>
    public const string ApplicationKey = "x-application-key";

    /// <summary>Header posted by the scheduled SFTP download.</summary>
    public const string LegacyApiKey = "XApiKey";

    /// <summary>Operator key for the pipeline endpoints.</summary>
    public const string OperatorKey = "x-operator-key";

    /// <summary>Correlation header set by <c>UseCorrelationId</c>.</summary>
    public const string CorrelationId = "X-Correlation-ID";
}
