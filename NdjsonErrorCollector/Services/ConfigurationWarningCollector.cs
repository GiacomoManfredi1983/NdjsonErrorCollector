using System;
using System.Collections.Generic;
using System.Linq;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class ConfigurationWarningCollector
    {
        public void Collect(
            IReadOnlyCollection<ServerLogLocation> locations,
            IReadOnlyDictionary<string, DateTime> thresholdsUtc,
            DeduplicationState reportingState,
            RunDiagnostics diagnostics)
        {
            foreach (var configuredGroup in thresholdsUtc.Keys)
            {
                reportingState.NotifiedWarningKeys.Remove($"group:{configuredGroup}");
                reportingState.PendingWarnings.Remove($"group:{configuredGroup}");
            }

            foreach (var unknownGroup in locations
                .Where(location => !string.IsNullOrWhiteSpace(location.Group) && !thresholdsUtc.ContainsKey(location.Group))
                .GroupBy(location => location.Group, StringComparer.OrdinalIgnoreCase))
            {
                var warningKey = $"group:{unknownGroup.Key}";
                if (reportingState.NotifiedWarningKeys.Contains(warningKey) || reportingState.PendingWarnings.ContainsKey(warningKey))
                {
                    continue;
                }

                var installations = string.Join(", ", unknownGroup
                    .Select(location => location.Installation ?? location.DatabasePath ?? "<unknown installation>")
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                reportingState.PendingWarnings[warningKey] = $"Discovered unconfigured group '{unknownGroup.Key}' on: {installations}. Add Collector.GroupThresholds.{unknownGroup.Key} before its logs can be collected.";
                diagnostics?.Warning(reportingState.PendingWarnings[warningKey]);
            }

            foreach (var location in locations.Where(location => string.IsNullOrWhiteSpace(location.Group)))
            {
                var identity = location.Installation ?? location.DatabasePath ?? location.LogFolderPath ?? "<unknown installation>";
                var warningKey = $"missing-group:{identity}";
                if (reportingState.NotifiedWarningKeys.Contains(warningKey) || reportingState.PendingWarnings.ContainsKey(warningKey))
                {
                    continue;
                }

                reportingState.PendingWarnings[warningKey] = $"Installation '{identity}' has no group in server discovery and cannot be collected.";
                diagnostics?.Warning(reportingState.PendingWarnings[warningKey]);
            }
        }
    }
}
