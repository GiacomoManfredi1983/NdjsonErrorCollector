using System;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class ReportingWindowAggregator
    {
        public bool TryAdd(
            GroupReportingState reportingState,
            NormalizedErrorRecord record,
            DateTime windowStartUtc,
            DateTime windowEndUtc,
            RunDiagnostics diagnostics)
        {
            if (reportingState == null || record == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(record.Installation))
            {
                diagnostics?.Warning($"Skipped error without an installation name from {record.Source}.");
                return false;
            }

            record.WindowStartUtc = windowStartUtc.ToString("O");
            record.WindowEndUtc = windowEndUtc.ToString("O");
            var windowKey = CreateWindowKey(record.Installation, record.Key);
            return reportingState.PendingRecords.TryAdd(windowKey, record);
        }

        public static string CreateWindowKey(string installation, string errorKey)
        {
            return $"{installation?.Trim()}\n{errorKey}";
        }
    }
}
