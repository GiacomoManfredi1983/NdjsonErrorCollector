# NdjsonErrorCollector

NdjsonErrorCollector is a standalone .NET console application that collects new NDJSON error log entries from multiple shared folders, normalizes and deduplicates them across sources, writes unique errors to a consolidated NDJSON output file, and sends one summary notification per run when new errors are found.

## Features

- Fetches server metadata from a configurable JSON endpoint
- Derives log folders from configured database paths
- Reads source log files incrementally using persisted checkpoints
- Detects truncated or replaced files and safely resets checkpoints
- Filters for error records only
- Normalizes error identity and suppresses duplicates across reruns
- Writes unique errors to a consolidated NDJSON file
- Sends a single aggregated notification per run
- Persists diagnostics, checkpoints, and notification state locally

## Project layout

- `NdjsonErrorCollector/` - application source
- `NdjsonErrorCollector.Tests/` - automated tests

## Getting started

1. Copy `NdjsonErrorCollector/appsettings.sample.json` to `appsettings.json` in the repository root for local execution, or provide equivalent values through environment variables.
2. Fill in the endpoint, output paths, and notification settings for your environment.
3. Run the application locally or from a scheduler.

## Build and test

- Build: `dotnet build Solution2.sln`
- Test: `dotnet test Solution2.sln`

## License

This project is licensed under the MIT License. See `LICENSE` for details.
