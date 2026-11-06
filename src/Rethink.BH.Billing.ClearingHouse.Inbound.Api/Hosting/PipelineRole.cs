namespace Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

/// <summary>What this process runs. One image serves every role; Helm sets <c>Pipeline__Role</c> per workload.</summary>
public enum PipelineRole
{
    /// <summary>The HTTP API only (legacy endpoint plus operator endpoints). Default.</summary>
    Api,

    /// <summary>Lists every configured clearing house once, then exits (Kubernetes CronJob).</summary>
    Lister,

    /// <summary>Reads the download queue.</summary>
    DownloadWorker,

    /// <summary>Reads the process queue.</summary>
    ProcessWorker,
}

/// <summary>Reads the role and the operator key.</summary>
public static class PipelineHostSettings
{
    /// <summary>Role configuration key.</summary>
    public const string RoleKey = "Pipeline:Role";

    /// <summary>
    /// Operator key for the pipeline endpoints (header <c>x-operator-key</c>). Supplied by Key Vault or an environment
    /// variable; when empty the endpoints answer 404.
    /// </summary>
    public const string OperatorKeyKey = "Pipeline:OperatorKey";

    /// <summary>The configured role; <see cref="PipelineRole.Api"/> when unset.</summary>
    public static PipelineRole Role(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.GetValue<PipelineRole?>(RoleKey) ?? PipelineRole.Api;
    }
}
