# Configuration

`appsettings.json` holds the shared defaults (Development secret names). `appsettings.Development.json` holds local overrides.
Everything that differed per environment in the legacy `appsettings.QA.json`, `appsettings.Staging.json`, and
`appsettings.Production.json` is now set by Helm:

- `deployment/helm/values-<env>.yaml` → `inboundConfig` → ConfigMap `<fullname>-inbound-config` → container environment (`envFrom`).
- ASP.NET Core maps `__` to `:`, so `ConnectionStrings__BlobStorage__ConnectionString` overrides `ConnectionStrings:BlobStorage:ConnectionString`.
- Values are literals or Key Vault secret **names**. No secret value is stored in the chart.

`DeploymentConfigurationTests` checks that every key below overrides a setting the service reads and resolves to the legacy value.

## Per-environment keys (`inboundConfig`)

| Environment variable | Setting | QA | Staging | Production |
| --- | --- | --- | --- | --- |
| `KeyVaultUri` | Vault used when `KEY_VAULT_NAME` is blank or `NotConfigured` | `https://bai-ch-eus2-qas-kv.vault.azure.net/` | `https://bai-ch-eus2-prd-kv.vault.azure.net/` | `https://rta-billing-prd-kv.vault.azure.net/` |
| `ApplicationInsights__InstrumentationKey` | secret name | `QA--Clearinghouse--ApplicationInsights--InstrumentationKey` | `Stg--…` | `Prd--…` |
| `ApplicationInsights__APPLICATIONINSIGHTS_CONNECTION_STRING` | secret name | `QA--Billing--ApplicationInsights--APPLICATIONINSIGHTS--CONNECTIONSTRING` | `Stg--…` | `Prd--…` |
| `BillingApiUrl` | secret name (or literal URL) | `QA--Clearinghouse--BillingApiUrl` | `Stg--…` | `Prd--…` |
| `BillingApiKey` | secret name (or literal) | `QA--Clearinghouse--BillingApiKey` | `Stg--…` | `Prd--…` |
| `ConnectionStrings__BlobStorage__ConnectionString` | secret name (or literal connection string) | `QA--Clearinghouse--ConnectionStrings--BlobStorage--ConnectionString` | `Stg--…` | `Prd--Clearinghouse--ConnectionStrings--BlobStoragge--ConnectionString` (spelling matches Key Vault) |
| `Swagger__Show` | Swagger outside Development | `true` | `true` | `true` |

## Platform keys (unchanged from the legacy chart, `constConfig`)

| Environment variable | Purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `constConfig.aspEnv` (`QA`, `Staging`, `Production`) |
| `KEY_VAULT_NAME` | Vault name; wins over `KeyVaultUri` |
| `AZURE_CLIENT_ID` | Workload identity used by `DefaultAzureCredential` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING`, `APPINSIGHTS_INSTRUMENTATIONKEY` | Application Insights |
| `AppVirtualDirectory` | Path base (`/` when unset) |
| `DD_SERVICE`, `DD_ENV` | Datadog, when `datadog.enabled` |

## Shared defaults (same in every environment; override only if needed)

| Setting | Default | Notes |
| --- | --- | --- |
| `Clearinghouses:<name>:Id`, `Title` | `1 Availity`, `8 Stedi` | |
| `Clearinghouses:<name>:Hosts`, `Port`, `UserName`, `UserPassword`, `DownloadDirectory` | `availity-hosts`, … | Key Vault names unless the value contains `=` |
| `Clearinghouses:<name>:HostKeyFingerprint` | empty | Optional SHA-256 pin (`SHA256:…`); a letters/digits/dashes value is read from Key Vault |
| `AvailityBackup` | `billing-availity-backup` | Backup container |
| `HeaderKey` | `x-application-key` | Header that carries the Billing key; `XApiKey` is always sent too |
| `ApplicationSettings:ApplicationKeySettings:CheckAcceptedApplicationKeys` | `false` | RethinkCore API-key filter |
| `InboundDownload:MaxRunMinutes` | `13` | Run limit |
| `InboundDownload:BatchConcurrency` | `2` | Files processed at once (1–64) |
| `InboundDownload:SftpPoolSize` | `4` | SFTP connections (1–64) |
| `InboundDownload:ConnectTimeoutSeconds` | `30` | |
| `InboundDownload:FailureMessageMaxLength` | `500` | |
| `InboundDownload:DeleteRemoteFiles` | `true` | `false` = shadow mode, files stay on SFTP |
| `InboundDownload:MinimumFileAgeSeconds` | `0` | New; skips files modified more recently |
| `InboundDownload:SftpRetryCount`, `SftpRetryBaseDelayMilliseconds` | `0`, `500` | New; transient SFTP retries |
| `KeyVault:SecretCacheMinutes` | `15` | New; `0` disables caching |
| `Cors:AllowedOrigins` | empty | New; browser origins allowed to call the API |
| `Logging__Console__FormatterName`, `Logging__Console__FormatterOptions__IncludeScopes` | not set | Set to `json` / `true` for structured Datadog logs |

Example:

```yaml
inboundConfig:
  InboundDownload__BatchConcurrency: "4"
  InboundDownload__MinimumFileAgeSeconds: "120"
  Clearinghouses__Availity__HostKeyFingerprint: "Prd--Clearinghouse--Availity--HostKeyFingerprint"
```

## Queue pipeline (`pipeline` in the Helm values)

Off by default everywhere: `pipeline.enabled: false` renders no pipeline resources, and `Pipeline:Mode` is `Off` in
`appsettings.json`. See [pipeline.md](pipeline.md) for the cutover runbook. Enabling the pipeline adds the ConfigMap
`<fullname>-pipeline-config` to every pod, built from these values:

| Helm value | Environment variable | Default | Notes |
| --- | --- | --- | --- |
| `pipeline.mode` | `Pipeline__Mode` | `Off` | `Off`, `Shadow`, or `Active` |
| `pipeline.clearingHouseIds` | `Pipeline__ClearingHouseIds__<n>` | `1`, `8` | Clearing houses the lister lists |
| `pipeline.serviceBusNamespace` | `ServiceBus__FullyQualifiedNamespace` | required | `<name>.servicebus.windows.net`, workload identity |
| `pipeline.downloadQueueName`, `processQueueName` | `ServiceBus__DownloadQueueName`, `ServiceBus__ProcessQueueName` | `inbound-download`, `inbound-process` | |
| `pipeline.manifestConnectionSecretName` | `ConnectionStrings__InboundManifest` | required | Key Vault secret name (or a literal connection string) |
| `pipeline.operatorKeySecretName` | `Pipeline__OperatorKey` (API pod only) | not set | Kubernetes Secret with key `operatorKey`; unset hides the operator endpoints |
| `pipeline.settings` | any | `{}` | Extra `Pipeline__*` or `ServiceBus__*` overrides |
| � | `Pipeline__Role` | `Api` | Set by each template: `Lister`, `DownloadWorker`, `ProcessWorker` |

Service settings (defaults in `appsettings.json`):

| Setting | Default | Notes |
| --- | --- | --- |
| `Pipeline:MinimumFileAgeSeconds` | `120` | Files modified more recently wait for the next listing |
| `Pipeline:DownloadConcurrency`, `ProcessConcurrency` | `4`, `4` | Messages per worker replica (1�64) |
| `Pipeline:MaxAttempts` | `5` | Failures before quarantine |
| `Pipeline:RetryBaseDelaySeconds`, `RetryMaxDelaySeconds` | `30`, `1800` | Exponential backoff between attempts |
| `Pipeline:ClaimTimeoutMinutes` | `30` | How long a worker owns an in-progress file |
| `Pipeline:StalledAfterMinutes`, `StalledBatchSize` | `120`, `500` | Crash recovery by the lister |
| `Pipeline:Landing:ContainerName`, `BlockSizeKiB`, `LeaseSeconds` | `inbound-landing`, `4096`, `60` | Landing container on the backup storage account |
| `Pipeline:Manifest:CommandTimeoutSeconds`, `MaxRetryCount` | `30`, `5` | SQL timeout and transient retries |
| `ServiceBus:ConnectionString` | not set | Local development only; a Key Vault name or literal. Deployed pods use the namespace and workload identity |
| `ServiceBus:MaxAutoLockRenewalMinutes` | `15` | Longest a worker keeps renewing a message lock |
