using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NdjsonErrorCollector;
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
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var checkpoints = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.DictionaryStringFileCheckpoint);
                return checkpoints ?? new Dictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return new Dictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void Save(IDictionary<string, FileCheckpoint> checkpoints)
        {
            var json = JsonSerializer.Serialize(checkpoints, AppJsonSerializerContextIndented.Default.DictionaryStringFileCheckpoint);

            var tempFilePath = _checkpointFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _checkpointFilePath, overwrite: true);
        }
    }
}
