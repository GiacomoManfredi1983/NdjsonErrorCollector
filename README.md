# NdjsonErrorCollector

NdjsonErrorCollector is a standalone .NET console application that reads DFS NDJSON logs from multiple installations and sends a daily error summary. It uses per-group UTC start thresholds, incremental file checkpoints, and installation-aware deduplication within each reporting window.

For step-by-step Windows build, deployment, first-run validation, upgrade, and Task Scheduler instructions, see [USER-GUIDE.md](USER-GUIDE.md).

## Features

- Discovers each installation's `name`, `group`, and `db` path from a configurable JSON endpoint
- Derives `DFS\Logs` folders from discovered database paths
- Uses a different ISO 8601 UTC start threshold for each installation group
- Reads only new file content through persisted byte checkpoints
- Skips files whose last-write time predates the current reporting window
- Deduplicates matching errors within the same installation and reporting window
- Reports the same error separately when it occurs on different installations
- Retains pending records and retries them when SMTP delivery fails
- Sends warning-only emails for newly discovered unconfigured groups
- Appends successfully delivered records to a consolidated NDJSON output file

## Reporting model

The collector is designed to run once per day.

- The first reporting window for a group starts inclusively at its configured threshold.
- Later windows start exclusively after the last successful email boundary.
- Every window ends inclusively at the UTC cutoff captured for the current run.
- Matching error information is deduplicated only within the same installation and reporting window.
- The same error on three installations produces three report entries.
- The same error can be reported again in a later window.
- A failed SMTP delivery does not advance the successful boundary; pending records are retried.

Multiple executions per day are safe, but every successful execution creates a new reporting window. An error that occurs again after an earlier successful run can therefore be reported again on the same UTC day.

## Configuration

Settings are stored under `Collector` in `appsettings.json`. Use [NdjsonErrorCollector/appsettings.sample.json](NdjsonErrorCollector/appsettings.sample.json) as a template and keep environment-specific values out of source control.

```json
{
  "Collector": {
	"ServersApiUrl": "https://example.local/api/servers",
	"LogFilePattern": "*.log",
	"OutputNdjsonPath": "data\\output\\errors.ndjson",
	"StateDirectory": "data\\state",
	"ErrorChannel": "SERVICE_ERROR",
	"GroupThresholds": {
	  "sviluppo": "2026-03-15T14:30:00Z",
	  "consulenza": "2026-02-10T09:00:00Z"
	},
	"Email": {
	  "Host": "smtp.example.com",
	  "Port": 25,
	  "FromAddress": "collector@example.com",
	  "To": [ "operations@example.com" ],
	  "Cc": [],
	  "UseSsl": false,
	  "SubjectPrefix": "[NdjsonErrorCollector]"
	}
  }
}
```

### Collector settings

- `ServersApiUrl`: endpoint that returns server metadata
- `LogFilePattern`: source-file pattern, normally `*.log`
- `OutputNdjsonPath`: output containing one delivered record per installation, error, and reporting window
- `StateDirectory`: persisted checkpoints, reporting state, and diagnostic log
- `ErrorChannel`: accepted NDJSON channel, normally `SERVICE_ERROR`
- `GroupThresholds`: required case-insensitive map of group names to ISO 8601 UTC start timestamps
- `Email.Host`: SMTP relay
- `Email.Port`: SMTP port
- `Email.FromAddress`: sender address
- `Email.To`: recipient array
- `Email.Cc`: optional CC recipient array
- `Email.UseSsl`: whether SMTP SSL/TLS is enabled
- `Email.SubjectPrefix`: summary-email subject prefix

Environment overrides are supported with the `NDJSONCOLLECTOR_` prefix.

### Server metadata

The API must return `name`, `group`, and `db` as sibling properties of the same installation object:

```json
[
  {
	"name": "example-server",
	"group": "sviluppo",
	"db": "\\\\server\\share\\Application\\DB"
  }
]
```

- `name` is the stable installation identity used for deduplication and email grouping.
- `group` selects the configured start threshold.
- `db` is converted from `...\DB` to `...\DFS\Logs`.
- Missing or unconfigured groups are skipped and produce a configuration-warning email.

## State and checkpoints

Inside `StateDirectory`, the collector stores:

- `checkpoints.json`: per-group thresholds and per-file offsets, sizes, and last-write timestamps
- `deduplication-state.json`: schema-versioned successful-send boundaries, pending/deferred records, and warning-delivery state
- `run.log`: unattended execution diagnostics

Behavior:

- Files older than the current reporting-window start are excluded before opening or checkpointing.
- File and record boundaries are inclusive for the first window and exclusive at the previous successful boundary for later windows.
- Existing files resume from their last stored byte offset.
- Truncated or replaced files reset to offset `0`.
- Obsolete checkpoints are pruned automatically.
- Changing a group's threshold resets only that group's checkpoints and reporting-window state.
- Incompatible legacy state is backed up as `*.legacy.bak` and reset.
- Post-cutoff records encountered during a run are deferred rather than lost.

## Delivery behavior

- Pending records and warnings are persisted before SMTP delivery.
- Successful SMTP delivery appends records to `errors.ndjson`, clears pending records, and advances group boundaries.
- Failed SMTP delivery retains pending data for the next run.
- If no email is sent, successful boundaries do not advance; checkpoints still prevent old content from being reread.
- Warning-only emails are sent for newly discovered missing or unconfigured groups.
- A crash after SMTP accepts a message but before local state is saved can cause a retry; exact-once email delivery requires an idempotent external mail API.

## Output NDJSON schema

Successfully delivered records contain fields including:

- `Key`
- `Installation`
- `Group`
- `WindowStartUtc`
- `WindowEndUtc`
- `Timestamp`
- `Source`
- `ServiceId`
- `Channel`
- `ErrorCode`
- `ErrorMsg`
- `Description`
- `Caller`
- `Command`
- `Arguments`
- `Stack`

## Project layout

- `NdjsonErrorCollector/` - application source and sample configuration
- `NdjsonErrorCollector.Tests/` - automated tests
- `scripts/package-release.ps1` - self-contained Windows release packaging
- `USER-GUIDE.md` - operator deployment and scheduling guide

## Build, test, and package

```powershell
dotnet build NdjsonErrorCollector/NdjsonErrorCollector.csproj
dotnet test NdjsonErrorCollector.Tests/NdjsonErrorCollector.Tests.csproj
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

The package is written to `artifacts\NdjsonErrorCollector` with this layout:

```text
NdjsonErrorCollector\
├── configuration\
│   └── appsettings.json
├── data\
└── programs\
	└── NdjsonErrorCollector.exe
```

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
