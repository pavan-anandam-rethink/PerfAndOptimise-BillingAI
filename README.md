# Rethink BH Billing – Clearing House Inbound Service

Phase 1 of the clearing house performance work: the inbound SFTP download (`POST /ClearingHouseClaimProcessor/downloadEdiDataFromSftp`)
rebuilt as a standalone .NET 8 service with centralized logging and an optimized download path. The HTTP contract and the Billing
calls are the same as the legacy `Rethink.BH.Billing.ClearingHouseService` (branch `new-tenant-bill/main`).

## Solution layout

| Project | Responsibility |
| --- | --- |
| `Domain` | Immutable records: `ClearingHouseDescriptor`, `RemoteFile`, `InboundFileOutcome`, `InboundDownloadResult`, `BillingRegistration`, `BackupWriteResult`. No dependencies. |
| `Application` | The use case. `DownloadInboundFilesCommandHandler` (MediatR) opens SFTP, lists, selects, and hands the files to `InboundFileBatchProcessor`, which runs `InboundFileProcessor` per file with bounded parallelism. Ports (`ISftpSession`, `IBackupFileStore`, `IBillingFileRegistrar`, …) live in `Abstractions`. |
| `Infrastructure` | Adapters: SSH.NET connection pool with optional host-key pinning and Polly retries, Azure Blob write-once backup, Billing `UploadEDIResponseFile` client with a streamed JSON body, Key Vault secret resolution with caching. |
| `Contracts` | Wire models (`DownloadSftpFileRequest`, `DownloadFailureCounts`). |
| `Api` (`website.dll`) | Controller, legacy API-key middleware, global exception handler, request trace filter, host setup through RethinkCore (App Configuration, Key Vault, Application Insights, health checks). |
| `tests/…UnitTests` | 241 xUnit tests, including an in-process host (`WebApplicationFactory`) that exercises the HTTP contract end to end. |

Every service is a `sealed` singleton (stateless or thread-safe); policies with no state (`LiteralWhenContainsEqualsPolicy`,
`SecretNamePatternPolicy`) are shared static instances; log definitions are `static partial` source-generated classes.

## Centralized logging and error tracing

Business code has no `try`/`catch`. Exceptions are caught in exactly three places, each logging once:

| Where | What it catches | Event |
| --- | --- | --- |
| `FaultBoundary` (Application) | Per-file failures, remote-delete failures, SFTP connect/list failures, and the run limit | `1020 OperationFaulted` (Error, with exception) / `1021 OperationTimedOut` |
| `GlobalExceptionHandler` (Api) | Anything that escapes a request | `3020 RequestFailed` (Error) / `3021 RequestRejected` (Warning) |
| `KeyVaultSecretResolver` | Translates a Key Vault 404 into a message that names the secret and the setting | rethrown, then logged by one of the above |

Every log written during a run carries these scope values, so one failure can be followed from the request to the file:

| Scope key | Set by | Example |
| --- | --- | --- |
| `RequestName` | `RequestLoggingBehavior` | `DownloadInboundFilesCommand` |
| `DownloadRunId` | handler, one per run | `9f1c…` |
| `ClearingHouseId`, `ClearingHouse` | handler | `1`, `Availity` |
| `FileName`, `FileSizeBytes` | batch processor, one per file | `AV_835_20261104_0003.x12`, `5120` |
| correlation id | `UseCorrelationId` (header `X-Correlation-ID`) | also written on `3020`/`3021` |

Event ids are stable: `1000–1031` application, `2000–2010` infrastructure (SFTP, Billing), `3000–3021` API. File names and
exception types are logged; file contents, credentials, and API keys never are (`SftpCredentials.ToString()` and
`BillingApiSettings.ToString()` redact them).

**Application Insights** (logs go through `RethinkCore.Common.Logging`, the same provider as the Accounts service):

```kusto
// Every failure in the last day, with the file and run it belongs to
traces
| where timestamp > ago(1d) and severityLevel >= 3
| project timestamp, message, customDimensions.OperationName, customDimensions.ExceptionType,
          customDimensions.FileName, customDimensions.DownloadRunId, customDimensions.ClearingHouse
// Then follow one run end to end
traces | where customDimensions.DownloadRunId == "<id>" | order by timestamp asc
```

**Datadog** collects container stdout (enable `datadog.enabled` in the Helm values). The ASP.NET Core console logger is on in every
environment, as in the legacy service. For structured JSON with the scope values above, set these through `inboundConfig` without a
code change: `Logging__Console__FormatterName: "json"` and `Logging__Console__FormatterOptions__IncludeScopes: "true"`. Each line
then has `EventId`, `LogLevel`, `Category`, `Message`, `State` (the structured fields), and `Scopes` (the run and file values).

## Behavior parity with the legacy service

Verified by tests in `tests/…UnitTests` (named in brackets).

| Behavior | Legacy | This service |
| --- | --- | --- |
| Route, body | `POST /ClearingHouseClaimProcessor/downloadEdiDataFromSftp`, JSON number | same [`ApiHostTests`] |
| Unknown id | `400` text `Invalid clearing house ID.` | same |
| Nothing to process, or run limit hit before any file finished | `204` | same |
| Any file left on SFTP | `500` `{ "failed": n, "total": m }` (Newtonsoft camelCase, indented) | same, byte-identical body |
| All files done | `200`, empty body | same |
| File order | modified time, then ordinal name; directories and dot files skipped | same [`SelectionAndFormattingTests`, `SftpTests`] |
| Per file | download, then backup (write-once to `AvailityBackup`), then Billing, then remote delete only when Billing accepted and queued | same [`InboundFileProcessorTests`] |
| Billing rejected / not queued / delete off / delete failed | `Stored` / `Registered` / `Queued` / `Queued` + "Remote delete failed." | same |
| Failure text | trimmed, cut at `FailureMessageMaxLength` (500) | same |
| Billing request | `ClearingHouse/UploadEDIResponseFile`, `{"clearingHouseId","Data"(base64),"FileName","Title"}`, headers `x-application-key` (or `HeaderKey`) and `XApiKey` | byte-identical body [`BillingTests`] |
| Billing response | `true`/`false`, quoted string, or `{success,queued,fileId}` | same parser |
| Secrets | SFTP/blob value without `=` is a Key Vault name; Billing value matching `^[0-9a-zA-Z-]{1,127}$` is a Key Vault name, trimmed and unquoted | same [`SecretsTests`] |
| Key Vault | `KEY_VAULT_NAME` (unless blank or `NotConfigured`), else `KeyVaultUri` | same |
| Startup validation messages | `Clearinghouses is not configured.`, `AvailityBackup is not configured.`, … | same [`InfrastructureRegistrationTests`] |
| API key | `XApiKey` copied to `x-application-key`; RethinkCore `XAppKeyActionFilter` | same |
| Path base, Swagger, health | `AppVirtualDirectory`, `Swagger:Show` or Development, `/healthcheck/live` | same |

### Legacy quirks kept on purpose

- An `OperationCanceledException` inside a file (including an `HttpClient` timeout to Billing) stops the whole run instead of
  failing only that file. Finished files are still reported.
- A run-limit timeout during connect or list reports `204`, not `500`.
- `APP_VERSION` in the Helm deployment reads the `KvAppName` key (copied unchanged from the legacy chart).

### Intended differences

| Change | Default keeps legacy behavior? |
| --- | --- |
| `InboundDownload:MinimumFileAgeSeconds` skips files still being uploaded | yes, `0` = off |
| `InboundDownload:SftpRetryCount` retries transient connect/list/download failures with backoff | yes, `0` = off |
| `Clearinghouses:<name>:HostKeyFingerprint` pins the SFTP host key (warning `2005` when not set) | yes, empty = not pinned |
| Additional SFTP pool connections open on demand; the first connects eagerly so a bad host fails fast | same results |
| Key Vault secrets cached for `KeyVault:SecretCacheMinutes` (15) | same values |
| Billing body streamed with exact `Content-Length` instead of three in-memory copies; downloads use pooled buffers | same bytes |
| CORS allows only `Cors:AllowedOrigins` (legacy allowed any origin). Server-to-server callers are unaffected. | security hardening |
| Container image no longer installs `git` and `bash` | smaller attack surface |

## Configuration

Only `appsettings.json` and `appsettings.Development.json` ship. QA, Staging, and Production values come from Helm as environment
variables; see [docs/configuration.md](docs/configuration.md).

## Build and test

```powershell
dotnet restore
dotnet build            # warnings are errors: .NET analyzers (latest-recommended), SonarAnalyzer, code-metric limits
dotnet test --settings coverlet.runsettings --collect:"XPlat Code Coverage"
```

Restoring needs access to the Rethink Azure Artifacts feeds. NuGet audit reads vulnerability data from every feed, so without feed
credentials run `dotnet restore -p:NuGetAudit=false` and check advisories with
`dotnet list package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json`.

Quality gates: cyclomatic complexity ≤ 10 (CA1502), maintainability index ≥ 40 (CA1505), class coupling limits (CA1506),
no vulnerable packages (transitive pins in `Directory.Packages.props`).

Sample files and scenarios: [samples/inbound/README.md](samples/inbound/README.md).
