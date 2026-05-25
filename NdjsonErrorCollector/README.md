# NdjsonErrorCollector

## Purpose

This standalone console application runs once per invocation. An external scheduler such as TeamCity, Windows Task Scheduler, or another automation system should trigger it at the desired interval. Each run:

1. Calls a configurable JSON endpoint that provides server metadata
2. Extracts all `db` paths from the returned JSON
3. Converts each `...\DB` path to `...\DFS\Logs`
4. Reads `.log` NDJSON files incrementally from those share folders
5. Keeps only `SERVICE_ERROR` entries
6. Normalizes each error into a stable deduplication key
7. Appends only never-seen errors to the consolidated output NDJSON file
8. Sends one summary email per run only when there are pending new errors

## Configuration

Settings are stored in `appsettings.json` under `Collector`. For public repositories, keep real environment values out of source control and use `appsettings.sample.json` as the template.

- `ServersApiUrl`: source endpoint for server metadata
- `LogFilePattern`: log file search pattern, default `*.log`
- `OutputNdjsonPath`: output file containing unique normalized errors
- `StateDirectory`: local folder for checkpoints, deduplication state, and run log
- `ErrorChannel`: channel filter, default `SERVICE_ERROR`
- `Email.From`: sender address
- `Email.To`: recipient address
- `Email.SubjectPrefix`: subject prefix for aggregated notifications
- `Email.PickupDirectory`: local pickup folder used by the current mail integration

Environment overrides are supported with the `NDJSONCOLLECTOR_` prefix.

### Example server metadata shape

The collector expects a JSON payload that contains one or more `db` values anywhere in the document. Example:

```json
[
  {
	"name": "example-server",
	"db": "\\\\server\\share\\Application\\DB"
  }
]
```

## State folder layout

Inside `StateDirectory` the app stores:

- `checkpoints.json`: per-file offsets, sizes, and last write timestamps
- `deduplication-state.json`: exported keys, notified keys, and pending notifications
- `run.log`: diagnostic log for unattended executions

## Checkpoint behavior

- Existing files resume from the last stored byte offset
- Missing files are skipped
- Truncated or replaced files are reset to offset `0`
- Duplicate exports and duplicate notifications are prevented by persisted normalized keys

## Output NDJSON schema

Each appended line is a JSON object with fields such as:

- `key`
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

- trigger execution every hour or any required interval
- ensure the execution account can access the UNC shares and the configured endpoint
- ensure the configured output and state directories are writable
- if a real SMTP service is required, replace the pickup-directory implementation with the environment-specific email transport
