using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class SourceLogEnumerator
    {
        public IReadOnlyList<SourceLogFile> Enumerate(IEnumerable<ServerLogLocation> locations, string searchPattern, RunDiagnostics diagnostics)
        {
            var results = new List<SourceLogFile>();

            foreach (var location in locations ?? Array.Empty<ServerLogLocation>())
            {
                if (string.IsNullOrWhiteSpace(location.LogFolderPath) || !Directory.Exists(location.LogFolderPath))
                {
                    diagnostics?.Warning($"Log folder unavailable: {location?.LogFolderPath}");
                    continue;
                }

                try
                {
                    diagnostics?.Info($"Scanning log folder: {location.LogFolderPath}");
                    var filePaths = Directory.EnumerateFiles(location.LogFolderPath, searchPattern ?? "*.log", SearchOption.TopDirectoryOnly).ToList();
                    diagnostics?.Info($"Found {filePaths.Count} log files in folder: {location.LogFolderPath}");

                    foreach (var filePath in filePaths)
                    {
                        var fileInfo = new FileInfo(filePath);
                        results.Add(new SourceLogFile
                        {
                            FolderPath = location.LogFolderPath,
                            FilePath = filePath,
                            LastWriteTimeUtc = fileInfo.LastWriteTimeUtc
                        });
                    }
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
