using System.Text.Json;
using System.Text.Json.Serialization;

namespace NdjsonErrorCollector.Models
{
    class RawLogEntry
    {
        [JsonPropertyName("TS")]
        public int[] TimestampParts { get; set; }

        public string ServiceID { get; set; }

        public string Channel { get; set; }

        public string Caller { get; set; }

        public int? ErrorCode { get; set; }

        public string ErrorMsg { get; set; }

        public string Description { get; set; }

        public string[] Stack { get; set; }

        public CommandInfo CommandInfo { get; set; }

        [JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, JsonElement> ExtraFields { get; set; }
    }

    class CommandInfo
    {
        public string Command { get; set; }

        public JsonElement Argument { get; set; }
    }
}
