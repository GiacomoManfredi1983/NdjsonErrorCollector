using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector
{
    [JsonSerializable(typeof(RawLogEntry))]
    [JsonSerializable(typeof(CommandInfo))]
    [JsonSerializable(typeof(DeduplicationState))]
    [JsonSerializable(typeof(NormalizedErrorRecord))]
    [JsonSerializable(typeof(FileCheckpoint))]
    [JsonSerializable(typeof(Dictionary<string, FileCheckpoint>))]
    [JsonSourceGenerationOptions(WriteIndented = false)]
    internal partial class AppJsonSerializerContext : JsonSerializerContext
    {
    }

    [JsonSerializable(typeof(DeduplicationState))]
    [JsonSerializable(typeof(Dictionary<string, FileCheckpoint>))]
    [JsonSourceGenerationOptions(WriteIndented = true)]
    internal partial class AppJsonSerializerContextIndented : JsonSerializerContext
    {
    }
}
