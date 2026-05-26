using System.IO;
using System.Text.Json;
using NdjsonErrorCollector;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class DeduplicationStore
    {
        private readonly string _filePath;

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
                return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.DeduplicationState) ?? new DeduplicationState();
            }
            catch (JsonException)
            {
                return new DeduplicationState();
            }
        }

        public void Save(DeduplicationState state)
        {
            var json = JsonSerializer.Serialize(state, AppJsonSerializerContextIndented.Default.DeduplicationState);

            var tempFilePath = _filePath + ".tmp";
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _filePath, overwrite: true);
        }
    }
}
