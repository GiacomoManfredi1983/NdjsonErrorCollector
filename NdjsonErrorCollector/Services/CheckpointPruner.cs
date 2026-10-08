using System;
using System.Collections.Generic;
using System.Linq;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class CheckpointPruner
    {
        public int Prune(
            CheckpointState checkpointState,
            IReadOnlyDictionary<string, DateTime> thresholdsUtc,
            RunDiagnostics diagnostics)
        {
            if (checkpointState?.Groups == null || thresholdsUtc == null)
            {
                return 0;
            }

            var totalRemoved = 0;
            foreach (var threshold in thresholdsUtc)
            {
                if (!checkpointState.Groups.TryGetValue(threshold.Key, out var groupState) || groupState?.Files == null)
                {
                    continue;
                }

                var obsoletePaths = groupState.Files
                    .Where(checkpoint => checkpoint.Value == null || checkpoint.Value.LastWriteTimeUtcTicks < threshold.Value.Ticks)
                    .Select(checkpoint => checkpoint.Key)
                    .ToList();

                foreach (var obsoletePath in obsoletePaths)
                {
                    groupState.Files.Remove(obsoletePath);
                }

                if (obsoletePaths.Count > 0)
                {
                    diagnostics?.Info($"Group {threshold.Key}: pruned {obsoletePaths.Count} checkpoints older than {threshold.Value:O}.");
                    totalRemoved += obsoletePaths.Count;
                }
            }

            return totalRemoved;
        }
    }
}
