using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NdjsonErrorCollector;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class OutputWriter
    {
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
                writer.WriteLine(JsonSerializer.Serialize(record, AppJsonSerializerContext.Default.NormalizedErrorRecord));
            }
        }
    }
}
