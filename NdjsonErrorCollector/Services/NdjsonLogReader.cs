using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class NdjsonLogReader
    {
        public IReadOnlyList<ParsedLogRecord> ReadNewErrors(string filePath, FileCheckpoint checkpoint, string errorChannel, RunDiagnostics diagnostics)
        {
            var results = new List<ParsedLogRecord>();
            if (checkpoint == null || !File.Exists(filePath))
            {
                diagnostics?.Warning($"Source file missing: {filePath}");
                return results;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(checkpoint.Offset, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);

            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                {
                    checkpoint.Offset = stream.Position;
                    continue;
                }

                try
                {
                    var entry = JsonSerializer.Deserialize<RawLogEntry>(line);
                    if (entry != null && string.Equals(entry.Channel, errorChannel, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new ParsedLogRecord
                        {
                            SourceFilePath = filePath,
                            TimestampUtc = ParseTimestamp(entry.TimestampParts),
                            Entry = entry
                        });
                    }
                }
                catch (JsonException)
                {
                    diagnostics?.Warning($"Malformed NDJSON line skipped in {filePath} at offset {checkpoint.Offset}.");
                }

                checkpoint.Offset = stream.Position;
            }

            checkpoint.LastKnownSize = stream.Length;
            checkpoint.LastWriteTimeUtcTicks = File.GetLastWriteTimeUtc(filePath).Ticks;
            return results;
        }

        private static DateTime ParseTimestamp(int[] parts)
        {
            if (parts == null || parts.Length < 7)
            {
                return DateTime.MinValue;
            }

            return new DateTime(parts[0], parts[1], parts[2], parts[3], parts[4], parts[5], DateTimeKind.Utc)
                .AddMilliseconds(parts[6]);
        }
    }
}
