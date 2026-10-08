using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class DeduplicationStore
    {
        private readonly string _filePath;

        public string LegacyBackupPath { get; private set; }

        public DeduplicationStore(string stateDirectory)
        {
            Directory.CreateDirectory(stateDirectory);
            _filePath = Path.Combine(stateDirectory, "deduplication-state.json");
        }

        public DeduplicationState Load()
        {
            if (!File.Exists(_filePath))
            {
                return new DeduplicationState();
            }

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new DeduplicationState();
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("SchemaVersion", out var schemaVersion)
                    || schemaVersion.ValueKind != JsonValueKind.Number
                    || schemaVersion.GetInt32() != 2)
                {
                    BackupLegacyState();
                    return new DeduplicationState();
                }

                var state = JsonSerializer.Deserialize<DeduplicationState>(json) ?? new DeduplicationState();
                state.Groups = new Dictionary<string, GroupReportingState>(state.Groups ?? new Dictionary<string, GroupReportingState>(), StringComparer.OrdinalIgnoreCase);
                state.NotifiedWarningKeys = new HashSet<string>(state.NotifiedWarningKeys ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
                state.PendingWarnings = new Dictionary<string, string>(state.PendingWarnings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                foreach (var group in state.Groups.Values)
                {
                    group.PendingRecords = new Dictionary<string, NormalizedErrorRecord>(group.PendingRecords ?? new Dictionary<string, NormalizedErrorRecord>(), StringComparer.OrdinalIgnoreCase);
                    group.DeferredRecords ??= new List<NormalizedErrorRecord>();
                }

                return state;
            }
            catch (JsonException)
            {
                return new DeduplicationState();
            }
        }

        public void Save(DeduplicationState state)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempFilePath = _filePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }

        private void BackupLegacyState()
        {
            LegacyBackupPath = _filePath + ".legacy.bak";
            File.Copy(_filePath, LegacyBackupPath, overwrite: true);
        }
    }
}
