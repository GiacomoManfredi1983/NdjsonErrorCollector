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

        public CheckpointStore(string stateDirectory)
        {
            Directory.CreateDirectory(stateDirectory);
            _checkpointFilePath = Path.Combine(stateDirectory, "checkpoints.json");
        }

        public IDictionary<string, FileCheckpoint> Load()
        {
            if (!File.Exists(_checkpointFilePath))
            {
                return new Dictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(_checkpointFilePath);
            var checkpoints = JsonSerializer.Deserialize<Dictionary<string, FileCheckpoint>>(json);
            return checkpoints ?? new Dictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
        }

        public void Save(IDictionary<string, FileCheckpoint> checkpoints)
        {
            var json = JsonSerializer.Serialize(checkpoints, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_checkpointFilePath, json);
        }
    }
}
