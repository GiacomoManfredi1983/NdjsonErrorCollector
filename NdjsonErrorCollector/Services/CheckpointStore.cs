using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class CheckpointStore
    {
        private readonly string _checkpointFilePath;

        public string LegacyBackupPath { get; private set; }

        public CheckpointStore(string stateDirectory)
        {
            Directory.CreateDirectory(stateDirectory);
            _checkpointFilePath = Path.Combine(stateDirectory, "checkpoints.json");
        }

        public CheckpointState Load()
        {
            if (!File.Exists(_checkpointFilePath))
            {
                return new CheckpointState();
            }

            var json = File.ReadAllText(_checkpointFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new CheckpointState();
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("Groups", out _))
                {
                    BackupLegacyState();
                    return new CheckpointState();
                }

                var state = JsonSerializer.Deserialize<CheckpointState>(json) ?? new CheckpointState();
                state.Groups = new Dictionary<string, GroupCheckpointState>(state.Groups ?? new Dictionary<string, GroupCheckpointState>(), StringComparer.OrdinalIgnoreCase);
                foreach (var group in state.Groups.Values)
                {
                    group.Files = new Dictionary<string, FileCheckpoint>(group.Files ?? new Dictionary<string, FileCheckpoint>(), StringComparer.OrdinalIgnoreCase);
                }

                return state;
            }
            catch (JsonException)
            {
                return new CheckpointState();
            }
        }

        public void Save(CheckpointState state)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var tempFilePath = _checkpointFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _checkpointFilePath, overwrite: true);
        }

        private void BackupLegacyState()
        {
            LegacyBackupPath = _checkpointFilePath + ".legacy.bak";
            File.Copy(_checkpointFilePath, LegacyBackupPath, overwrite: true);
        }
    }
}
