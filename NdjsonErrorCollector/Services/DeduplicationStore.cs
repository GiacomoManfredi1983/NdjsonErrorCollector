using System.IO;
using System.Text.Json;
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
            return JsonSerializer.Deserialize<DeduplicationState>(json) ?? new DeduplicationState();
        }

        public void Save(DeduplicationState state)
        {
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_filePath, json);
        }
    }
}
