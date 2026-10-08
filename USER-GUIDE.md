# NdjsonErrorCollector user guide

This guide explains how to build, deploy, configure, test, and schedule NdjsonErrorCollector on a Windows server.

## 1. Build the release

### Prerequisites

- Windows PowerShell
- .NET 10 SDK on the build machine
- A local copy of the repository

Open PowerShell in the repository root and run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\package-release.ps1
```

The script creates a self-contained `win-x64` release under:

```text
artifacts\NdjsonErrorCollector\
├── configuration\
│   └── appsettings.json
├── data\
└── programs\
	├── NdjsonErrorCollector.exe
	└── runtime files
```

A successful build prints `Release package created` and the path of `NdjsonErrorCollector.exe`. The target server does not need a separate .NET runtime because the release is self-contained.

## 2. Copy the release to the server

Create a deployment folder on the server, for example:

```text
E:\NdjsonErrorCollector
```

Copy the complete contents of `artifacts\NdjsonErrorCollector` into that folder. Preserve the three directories:

```text
E:\NdjsonErrorCollector\
├── configuration\
├── data\
└── programs\
```

Do not move `NdjsonErrorCollector.exe` out of `programs`. The collector uses this folder layout to find the sibling `configuration` and `data` directories.

For an upgrade of an existing installation:

1. Disable the scheduled task and wait for any running collector process to finish.
2. Back up `configuration\appsettings.json` and the complete `data` directory.
3. Replace the `programs` directory with the new release.
4. Review and update `configuration\appsettings.json` without deleting the existing `data` directory.
5. Run one manual validation before enabling the scheduled task again.

The Windows account that runs the collector must be able to:

- call the configured server-discovery URL;
- read every required DFS/UNC log share;
- write to the deployment `data` directory;
- connect to the configured SMTP relay.

## 3. Configure the collector

Edit:

```text
E:\NdjsonErrorCollector\configuration\appsettings.json
```

Example:

```json
{
  "Collector": {
	"ServersApiUrl": "http://server.example.local/api/servers",
	"LogFilePattern": "*.log",
	"OutputNdjsonPath": "data\\output\\errors.ndjson",
	"StateDirectory": "data\\state",
	"ErrorChannel": "SERVICE_ERROR",
	"GroupThresholds": {
	  "sviluppo": "2026-09-30T15:00:00Z",
	  "consulenza": "2026-09-30T15:00:00Z",
	  "utente": "2026-09-30T15:00:00Z",
	  "consulenza-1": "2026-09-30T15:00:00Z"
	},
	"Email": {
	  "Host": "smtp.example.local",
	  "Port": 25,
	  "FromAddress": "collector@example.com",
	  "To": [ "operations@example.com" ],
	  "Cc": [],
	  "UseSsl": false,
	  "SubjectPrefix": "[NdjsonErrorCollector]",
	  "PickupDirectory": "data\\maildrop"
	}
  }
}
```

### Important settings

- `ServersApiUrl` is the API that returns each installation's sibling `name`, `group`, and `db` properties.
- `OutputNdjsonPath` and `StateDirectory` may be relative to the deployment root.
- `GroupThresholds` must contain an ISO 8601 UTC timestamp for every group that should be collected.
- The first successful report for a group starts at its configured threshold.
- Later reports start after the previous successful email boundary.
- An unconfigured group is skipped and generates a configuration-warning email.
- `Email.To` and `Email.Cc` are JSON arrays, including when they contain only one address.

Validate the JSON before running the application:

```powershell
Get-Content "E:\NdjsonErrorCollector\configuration\appsettings.json" -Raw |
	ConvertFrom-Json
```

If this command reports a parsing error, correct the indicated JSON line before continuing. Make sure quoted values have closing quotes and Windows paths use escaped backslashes (`\\`).

## 4. Run the first execution manually

Open PowerShell using the same Windows account that will run the scheduled task:

```powershell
cd "E:\NdjsonErrorCollector\programs"
.\NdjsonErrorCollector.exe
$LASTEXITCODE
```

Exit codes:

- `0`: the collector completed successfully;
- `1`: the collector failed; inspect the diagnostic log.

Review the latest diagnostics:

```powershell
Get-Content "E:\NdjsonErrorCollector\data\state\run.log" -Tail 100
```

Verify that:

- the server API was reached;
- the expected installation groups and DFS folders were discovered;
- required folders were accessible;
- there are no configuration, access-denied, JSON, or SMTP errors;
- `data\state\checkpoints.json` was created;
- `data\state\deduplication-state.json` was created;
- `data\output\errors.ndjson` was created when errors were successfully emailed;
- the expected email or configuration-warning email arrived.

The first execution can process and email all matching errors between each group threshold and the execution cutoff. Choose threshold dates carefully before this run.

If SMTP delivery fails, pending records remain in `deduplication-state.json` and are retried on the next execution. Do not delete the state files unless a deliberate full reset is required.

## 5. Schedule the collector with Windows Task Scheduler

### Create the task

1. Open **Task Scheduler** on the server.
2. Select **Task Scheduler Library** and choose **Create Task**.
3. On **General**:
   - enter a descriptive name such as `NdjsonErrorCollector Daily`;
   - select the service account that has API, DFS, data-folder, and SMTP access;
   - select **Run whether user is logged on or not**;
   - select **Run with highest privileges** only if required by the server policy.
4. On **Triggers**:
   - create a **Daily** trigger;
   - choose the required execution time;
   - ensure the trigger is enabled.
5. On **Actions**, create **Start a program**:
   - **Program/script:** `E:\NdjsonErrorCollector\programs\NdjsonErrorCollector.exe`
   - **Start in:** `E:\NdjsonErrorCollector\programs`
   - leave **Add arguments** empty.
6. On **Conditions**, review power and network conditions for the server environment.
7. On **Settings**:
   - enable **Run task as soon as possible after a scheduled start is missed**;
   - for **If the task is already running**, select **Do not start a new instance**;
   - optionally configure an execution timeout appropriate for the DFS volume.
8. Save the task and provide the service-account password if requested.

The **Start in** value is important: it ensures the executable resolves the sibling configuration and data directories correctly.

### Test the scheduled task

1. Right-click the task and select **Run**.
2. Wait for it to finish.
3. Confirm that **Last Run Result** is `0x0`.
4. Check `data\state\run.log` and verify the expected email behavior.
5. Enable Task Scheduler history if additional diagnostics are required.

## Daily operation

- The recommended schedule is one execution per day.
- Deduplication applies within one reporting window and installation.
- The same error on different installations produces one entry per installation.
- The same error can be reported again in a later daily window.
- Multiple executions per day are supported, but each successful execution creates a new reporting window and can report a repeated error again later that day.
- Preserve the `data` directory between upgrades because it contains checkpoints, pending delivery records, and successful-send boundaries.
