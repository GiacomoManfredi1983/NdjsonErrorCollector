using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class OutputWriter
    {
        public string LegacyBackupPath { get; private set; }

        public void Append(string outputPath, IEnumerable<NormalizedErrorRecord> records)
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            foreach (var record in records)
            {
                writer.WriteLine(JsonSerializer.Serialize(record));
            }
        }

        public void RemoveGroups(string outputPath, IEnumerable<string> groups)
        {
            if (string.IsNullOrWhiteSpace(outputPath) || !File.Exists(outputPath))
            {
                return;
            }

            var groupsToRemove = new HashSet<string>(groups ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (groupsToRemove.Count == 0)
            {
                return;
            }

            var retainedLines = new List<string>();
            foreach (var line in File.ReadLines(outputPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(line);
                    if (!document.RootElement.TryGetProperty("Group", out var groupElement) || groupElement.ValueKind != JsonValueKind.String)
                    {
                        BackupLegacyOutput(outputPath);
                        File.WriteAllText(outputPath, string.Empty);
                        return;
                    }

                    if (!groupsToRemove.Contains(groupElement.GetString()))
                    {
                        retainedLines.Add(line);
                    }
                }
                catch (JsonException)
                {
                    retainedLines.Add(line);
                }
            }

            var tempPath = outputPath + ".tmp";
            File.WriteAllLines(tempPath, retainedLines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, outputPath, overwrite: true);
        }

        private void BackupLegacyOutput(string outputPath)
        {
            LegacyBackupPath = outputPath + ".legacy.bak";
            File.Copy(outputPath, LegacyBackupPath, overwrite: true);
        }
    }
}
