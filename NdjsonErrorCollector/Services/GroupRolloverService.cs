using System;
using System.Collections.Generic;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class GroupRolloverService
    {
        public IReadOnlyCollection<string> Prepare(
            CheckpointState checkpointState,
            DeduplicationState deduplicationState,
            IReadOnlyDictionary<string, DateTime> thresholdsUtc,
            RunDiagnostics diagnostics)
        {
            var rolledOverGroups = new List<string>();

            foreach (var threshold in thresholdsUtc)
            {
                checkpointState.Groups.TryGetValue(threshold.Key, out var checkpointGroup);
                deduplicationState.Groups.TryGetValue(threshold.Key, out var reportingGroup);
                var thresholdText = threshold.Value.ToString("O");
                var thresholdChanged = checkpointGroup == null
                    || reportingGroup == null
                    || !string.Equals(checkpointGroup.AppliedThresholdUtc, thresholdText, StringComparison.Ordinal)
                    || !string.Equals(reportingGroup.AppliedThresholdUtc, thresholdText, StringComparison.Ordinal);

                if (!thresholdChanged)
                {
                    continue;
                }

                checkpointState.Groups[threshold.Key] = new GroupCheckpointState
                {
                    AppliedThresholdUtc = thresholdText
                };
                deduplicationState.Groups[threshold.Key] = new GroupReportingState
                {
                    AppliedThresholdUtc = thresholdText
                };
                rolledOverGroups.Add(threshold.Key);
                diagnostics?.Info($"Group {threshold.Key}: deployment threshold changed to {thresholdText}; checkpoints and reporting-window state reset.");
            }

            return rolledOverGroups;
        }
    }
}
