# NdjsonErrorCollector

## Purpose

This standalone console application runs once per invocation. An external scheduler such as TeamCity, Windows Task Scheduler, or another automation system should trigger it at the desired interval. Each run:

1. Calls a configurable JSON endpoint that provides server metadata
2. Extracts each installation's `name`, `group`, and `db` path from the returned JSON
3. Converts each `...\DB` path to `...\DFS\Logs`
4. Reads `.log` NDJSON files incrementally from those share folders
5. Builds a UTC reporting window from the last successful email (or the group threshold for the first report) to the current run cutoff
6. Keeps only `SERVICE_ERROR` entries inside that reporting window
7. Deduplicates matching error information within each installation and reporting window
8. Sends one grouped daily summary, with `sviluppo` first, then appends delivered records to the consolidated NDJSON output

## Configuration

Settings are stored in `appsettings.json` under `Collector`. For public repositories, keep real environment values out of source control and use `appsettings.sample.json` as the template.

- `ServersApiUrl`: source endpoint for server metadata
- `LogFilePattern`: log file search pattern, default `*.log`
- `OutputNdjsonPath`: output file containing one delivered record per installation/error/reporting window
- `StateDirectory`: local folder for checkpoints, reporting-window delivery state, and run log
- `ErrorChannel`: channel filter, default `SERVICE_ERROR`
- `GroupThresholds`: required map from API `group` values to their DFS deployment timestamps in ISO 8601 UTC format
- `Email.Host`: SMTP relay host
- `Email.Port`: SMTP relay port
- `Email.FromAddress`: sender address
- `Email.To`: recipient addresses
- `Email.Cc`: CC recipient addresses
- `Email.UseSsl`: whether SMTP should use SSL/TLS
- `Email.SubjectPrefix`: subject prefix for aggregated notifications
- `Email.PickupDirectory`: legacy pickup folder setting, no longer used by SMTP delivery

Environment overrides are supported with the `NDJSONCOLLECTOR_` prefix.

Example thresholds:

```json
"GroupThresholds": {
  "sviluppo": "2026-03-15T14:30:00Z",
  "consulenza": "2026-02-10T09:00:00Z"
}
```

Group names are matched case-insensitively. The first reporting window includes errors exactly at the threshold. Later windows start exclusively after the last successful-send boundary and end inclusively at the current UTC cutoff. Discovered groups without a configured threshold are skipped and produce an email warning, even when no errors are collected.

## Release folder layout

The published release can be arranged as:

- `NdjsonErrorCollector\programs\` - executable and runtime files
- `NdjsonErrorCollector\data\` - output and persisted state
- `NdjsonErrorCollector\configuration\appsettings.json` - runtime configuration

When the collector is launched from `programs`, it automatically loads configuration from the sibling `configuration` folder and resolves relative `data` paths from the `NdjsonErrorCollector` release root.

### Packaging script

You can generate this layout with:

- `powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1`

The script publishes the app to `artifacts\NdjsonErrorCollector`, creates the `programs` and `data` folders, and moves runtime files into `programs` while keeping `configuration\appsettings.json` at the release root.

### Example server metadata shape

The collector expects a JSON payload that contains one or more `db` values anywhere in the document. Example:

```json
[
  {
	"name": "example-server",
	"group": "sviluppo",
	"db": "\\\\server\\share\\Application\\DB"
  }
]
```

## State folder layout

Inside `StateDirectory` the app stores:

- `checkpoints.json`: per-group deployment threshold and per-file offsets, sizes, and last write timestamps
- `deduplication-state.json`: schema-versioned per-group successful-send boundaries, pending/deferred records, and warning-delivery state
- `run.log`: diagnostic log for unattended executions

Legacy checkpoint state and permanent-deduplication state are backed up as `*.legacy.bak`, then reset when their schema is incompatible.

## Checkpoint behavior

- Log folders for missing or unconfigured groups are skipped before file enumeration
- Files with `LastWriteTimeUtc` older than their current reporting-window start are excluded before opening or checkpointing
- Files modified exactly at the reporting-window start are eligible
- Record timestamps remain authoritative and are checked against both UTC window boundaries
- Each folder produces one diagnostic summary with total, eligible, and pre-threshold file counts
- Persisted checkpoints older than the current reporting-window start are pruned on every run
- Existing files resume from the last stored byte offset
- Missing files are skipped
- Truncated or replaced files are reset to offset `0`
- Matching errors are deduplicated only within the current window and installation; the same error can be reported again on a later day
- The same error from different installation names produces one record per installation
- Changing a group's configured threshold resets only that group's checkpoints and reporting-window state
- A changed group is rescanned from offset `0`, but records older than its new threshold remain excluded
- Existing output records for the changed group are removed atomically before the rescan
- Legacy ungrouped output is backed up as `errors.ndjson.legacy.bak` and reset on the first rollover

## Delivery behavior

- Pending records and warnings are saved before SMTP delivery
- If SMTP delivery fails, the successful boundary does not advance and pending data is retried on the next run
- `errors.ndjson` is appended only after SMTP delivery succeeds
- Post-cutoff records encountered during a run are deferred rather than lost after checkpoint advancement
- A warning-only email is sent when a new missing or unconfigured API group is discovered
- A crash after SMTP accepts a message but before local state is saved can cause that message to be retried; exact-once email delivery requires an idempotent external mail API

## Output NDJSON schema

Each appended line is a JSON object with fields such as:

- `key`
- `installation`
- `group`
- `windowStartUtc`
- `windowEndUtc`
- `timestamp`
- `source`
- `serviceId`
- `channel`
- `errorCode`
- `errorMsg`
- `description`
- `caller`
- `command`
- `arguments`
- `stack`

## Scheduler guidance

Configure your scheduler to run the application on the target machine, for example:

`dotnet run --project NdjsonErrorCollector/NdjsonErrorCollector.csproj --configuration Release`

Recommended schedule:

- trigger execution once per day
- ensure the execution account can access the UNC shares and the configured endpoint
- ensure the configured output and state directories are writable
- configure a UTC deployment threshold for every `group` returned by the server API
