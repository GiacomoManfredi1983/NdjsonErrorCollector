using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class SourceLogEnumerator
    {
        public IReadOnlyList<SourceLogFile> Enumerate(
            IEnumerable<ServerLogLocation> locations,
            string searchPattern,
            IReadOnlyDictionary<string, DateTime> thresholdsUtc,
            RunDiagnostics diagnostics)
        {
            var results = new List<SourceLogFile>();

            foreach (var location in locations ?? Array.Empty<ServerLogLocation>())
            {
                if (string.IsNullOrWhiteSpace(location.Group))
                {
                    diagnostics?.Warning($"Skipping log folder without an installation group: {location.LogFolderPath}");
                    continue;
                }

                if (thresholdsUtc == null || !thresholdsUtc.TryGetValue(location.Group, out var thresholdUtc))
                {
                    diagnostics?.Warning($"Skipping log folder for unconfigured group {location.Group}: {location.LogFolderPath}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(location.LogFolderPath) || !Directory.Exists(location.LogFolderPath))
                {
                    diagnostics?.Warning($"Log folder unavailable: {location?.LogFolderPath}");
                    continue;
                }

                try
                {
                    diagnostics?.Info($"Scanning log folder: {location.LogFolderPath}");
                    var filePaths = Directory.EnumerateFiles(location.LogFolderPath, searchPattern ?? "*.log", SearchOption.TopDirectoryOnly).ToList();
                    var eligibleCount = 0;

                    foreach (var filePath in filePaths)
                    {
                        var fileInfo = new FileInfo(filePath);
                        if (fileInfo.LastWriteTimeUtc < thresholdUtc)
                        {
                            continue;
                        }

                        results.Add(new SourceLogFile
                        {
                            Installation = location.Installation,
                            Group = location.Group,
                            FolderPath = location.LogFolderPath,
                            FilePath = filePath,
                            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
                        });
                        eligibleCount++;
                    }

                    diagnostics?.Info($"Found {filePaths.Count} log files in folder: {location.LogFolderPath}. Eligible: {eligibleCount}. Skipped as older than {thresholdUtc:O}: {filePaths.Count - eligibleCount}.");
                }
                catch (IOException ex)
                {
                    diagnostics?.Warning($"Failed to enumerate {location.LogFolderPath}: {ex.Message}");
                }
            }

            return results
                .OrderBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(file => file.LastWriteTimeUtc)
                .ToList();
        }
    }
}
