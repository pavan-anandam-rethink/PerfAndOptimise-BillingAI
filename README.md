# Rethink BH Billing – Clearing House Inbound Service

Phase 1 of the clearing house performance work: the inbound SFTP download (`POST /ClearingHouseClaimProcessor/downloadEdiDataFromSftp`)
rebuilt as a standalone .NET 8 service with centralized logging and an optimized download path. The HTTP contract and the Billing
calls are the same as the legacy `Rethink.BH.Billing.ClearingHouseService` (branch `new-tenant-bill/main`).

The scheduled pickup for those same clearing houses (Availity id 1, Stedi id 8) is off until an environment turns it on.
What to run, how a file moves, the dev configuration, and the 1,000-file test are in
[Clearing house inbound EDI](#clearing-house-inbound-edi-availity-and-stedi). The setting key is `Pipeline`; that name is only
the configuration section.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `Domain` | Immutable records: `ClearingHouseDescriptor`, `RemoteFile`, `InboundFileOutcome`, `InboundDownloadResult`, `BillingRegistration`, `BackupWriteResult`. No dependencies. |
| `Application` | The use case. `DownloadInboundFilesCommandHandler` (MediatR) opens SFTP, lists, selects, and hands the files to `InboundFileBatchProcessor`, which runs `InboundFileProcessor` per file with bounded parallelism. Ports (`ISftpSession`, `IBackupFileStore`, `IBillingFileRegistrar`, …) live in `Abstractions`. |
| `Infrastructure` | Adapters: SSH.NET connection pool with optional host-key pinning and Polly retries, Azure Blob write-once backup, Billing `UploadEDIResponseFile` client with a streamed JSON body, Key Vault secret resolution with caching. |
| `Contracts` | Wire models (`DownloadSftpFileRequest`, `DownloadFailureCounts`). |
| `Api` (`website.dll`) | Controller, legacy API-key middleware, global exception handler, request trace filter, host setup through RethinkCore (App Configuration, Key Vault, Application Insights, health checks). |
| `tests/…UnitTests` | 373 xUnit tests (pipeline tests run against SQLite in memory), including an in-process host (`WebApplicationFactory`) that exercises the HTTP contract end to end. |

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

Event ids are stable: `1000–1031` application, `2000–2010` infrastructure (SFTP, Billing), `3000–3021` API. The pipeline adds
`1100–1130`, `2020–2027`, and `3030–3033` ([docs/pipeline.md](docs/pipeline.md#monitoring)). File names and
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

## Clearing house inbound EDI (Availity and Stedi)

The service downloads inbound EDI from a clearing house SFTP folder, stores a backup, and registers each file with Billing
(`ClearingHouse/UploadEDIResponseFile`). Two clearing houses are configured:

| Clearing house | Id | SFTP settings | Dev secret names in `appsettings.json` |
| --- | --- | --- | --- |
| Availity | `1` | `Clearinghouses:Availity` | `availity-hosts`, `availity-port`, `availity-username`, `availity-password`, `availity-downloadDirectory` |
| Stedi | `8` | `Clearinghouses:Stedi` | `stedi-hosts`, `stedi-port`, `stedi-username`, `stedi-password`, `stedi-downloadDirectory` |

A value with no `=` is a Key Vault secret name (dev vault `https://bai-ch-eus2-dev-kv.vault.azure.net/`, or
`https://rta-billing-dev-kv.vault.azure.net/` when `ASPNETCORE_ENVIRONMENT` is Development). A value that contains `=` is used
as written, which is how a local connection string is set.

### How one file is processed

**HTTP download (this is what dev runs today).** Something calls:

`POST /ClearingHouseClaimProcessor/downloadEdiDataFromSftp`

with body `1` for Availity or `8` for Stedi, and header `XApiKey`. The request lists that clearing house's download directory,
skips directories and names that start with `.`, then for each file: download, write the backup blob, call Billing, and delete
the SFTP file only after Billing has accepted it and queued it. The call returns `200` when every file is finished, `204` when
the folder had nothing to process, `500` with `{"failed":n,"total":m}` when any file is still on SFTP, and `400` with
`Invalid clearing house ID.` for an unknown id. One request stops after `InboundDownload:MaxRunMinutes` (13). Files not reached
stay on SFTP for the next call.

**Scheduled pickup (off until you enable it).** The same image runs four roles. Helm sets `Pipeline__Role` on each one.

1. The lister CronJob (`<release>-lister`, hourly at minute 5, `America/New_York`) lists Availity and Stedi. It skips a file
   modified in the last `Pipeline:MinimumFileAgeSeconds` (120), so a file still uploading is left for the next hour. New files
   are inserted into Azure SQL table `inbound.InboundFile` and one Service Bus message (the file id only) is sent to
   `inbound-download`.
2. A download worker streams the file from SFTP into blob container `inbound-landing` and stores its SHA-256.
3. A process worker checks that hash, writes the same backup the HTTP path writes, calls Billing the same way, then deletes the
   SFTP file. While `Pipeline:Mode` is `Shadow`, this step does not run and Billing is not called. `Active` runs it.
   `Off` (the default) means the lister does nothing and only the HTTP download runs.
4. A failed file is retried with backoff (`Pipeline:RetryBaseDelaySeconds`, doubling, cap `Pipeline:RetryMaxDelaySeconds`).
   After `Pipeline:MaxAttempts` (5) it is quarantined. `POST /inbound-pipeline/files/{id}/replay` with header `x-operator-key`
   sends it again. A file Billing already accepted is not sent to Billing twice; the replay only retries the SFTP delete.

Workers scale from the queue length (KEDA) and scale to zero when the queues are empty. The cutover order (Shadow, stop the
HTTP caller, clear the shadow rows, then Active) is in [docs/pipeline.md](docs/pipeline.md).

### What to configure

Dev, QA, Staging, and Production do not get extra appsettings files. Dev defaults live in `appsettings.json` (`Pipeline:Mode` is
`Off`). Every other environment sets the same keys as environment variables (`__` for a nested key).

| To | Set | Dev default |
| --- | --- | --- |
| Leave the HTTP download as the only path | `Pipeline__Mode=Off` and Helm `pipeline.enabled: false` | already set |
| Land files without calling Billing | `pipeline.enabled: true`, `pipeline.mode: Shadow` | off |
| Download, call Billing, and delete from SFTP on the schedule | `pipeline.enabled: true`, `pipeline.mode: Active` | off |
| Which clearing houses the lister reads | `pipeline.clearingHouseIds` | `1`, `8` |
| Service Bus namespace | `pipeline.serviceBusNamespace` | empty until enabled |
| Manifest database secret name | `ConnectionStrings__InboundManifest` (Helm `pipeline.manifestConnectionSecretName`) | secret name `Dev--Clearinghouse--ConnectionStrings--InboundManifest`; Helm value empty until enabled |
| Operator endpoints | `pipeline.operatorKeySecretName` (Kubernetes secret key `operatorKey`) | unset, so the endpoints answer 404 |
| Keep an uploading file until the next listing | `Pipeline__MinimumFileAgeSeconds` | `120` |
| Stop the HTTP request | `InboundDownload__MaxRunMinutes` | `13` |
| Files processed at once on the HTTP path | `InboundDownload__BatchConcurrency` | `2` |

Enabling the schedule also needs the Azure resources in [docs/pipeline.md](docs/pipeline.md#azure-resources-and-access-platform-team):
the two Service Bus queues, the `inbound.InboundFile` table (`deployment/sql/inbound-manifest.sql`), container `inbound-landing`,
and the workload-identity roles. The API deployment alone does not list or download on a schedule.

### How to test in the dev environment

The 1,000 files are synthetic X12 5010 (999, 277CA, 835, and TA1), 250 of each, named `AV_999_20261107_0001.x12` through
`AV_TA1_20261107_1000.x12`. Every name, id, and amount is made up. They are in
[samples/inbound/dev-edi](samples/inbound/dev-edi). Regenerate them with `samples/inbound/New-DevEdiFiles.ps1`.

Use a directory that does not also receive real Availity or Stedi files. Point the dev secret
`availity-downloadDirectory` at that directory for the test, and put it back afterwards.

1. Copy `samples/inbound/dev-edi` to that SFTP directory. The HTTP path takes files of any age. The scheduled pickup waits until
   a file is 120 seconds old.
2. Start the API with the dev vault (`KeyVaultUri` in `appsettings.json`, or the Development override) and the dev workload identity.
3. Send the Availity download. `src/Rethink.BH.Billing.ClearingHouse.Inbound.Api/inbound-download.http` is the same call:

```http
POST /ClearingHouseClaimProcessor/downloadEdiDataFromSftp
Content-Type: application/json
XApiKey: <dev application key>

1
```

4. Expect `200` when Billing queued all 1,000 and SFTP is empty, or `500` with `{"failed":n,"total":1000}` when some files remain.
   A run that reaches 13 minutes returns and leaves the rest on SFTP; call it again until the folder is empty. Stedi is the same
   call with body `8` and the Stedi directory.
5. In Application Insights, follow one file with `customDimensions.FileName == "AV_835_20261107_0003.x12"` and one clearing house
   with `customDimensions.ClearingHouse == "Availity"`. Event `1011` is the Billing registration. Event `1014` is the file finished.
6. To test the schedule in dev, set `pipeline.enabled: true` and `pipeline.mode: Shadow` first. Billing is not called. After the
   :05 listing, `inbound.InboundFile` should hold 1,000 `Landed` rows and the SFTP files should still be there. Move to `Active`
   only by the cutover steps in [docs/pipeline.md](docs/pipeline.md#modes-and-cutover-runbook). Then the same rows move to `Done`
   and the SFTP directory is empty. Quarantined files show at `GET /inbound-pipeline/quarantined` when `x-operator-key` is set.

A local rehearsal uses the same files and the same HTTP call against `http://localhost:7400`, with `Clearinghouses:Availity:Hosts`
(and the other SFTP values) set to literals that contain `=` so Key Vault is not queried, `InboundDownload:DeleteRemoteFiles` set
to `false` if the files should stay on the local SFTP server, and `BillingApiUrl` left at `http://localhost:5001`.

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

Quality gates: cyclomatic complexity = 10 (CA1502), maintainability index = 40 (CA1505), class coupling limits (CA1506),
no vulnerable packages (transitive pins in `Directory.Packages.props`).

Sample files and scenarios: [samples/inbound/README.md](samples/inbound/README.md).
