using System;

namespace NdjsonErrorCollector.Models
{
    class ParsedLogRecord
    {
        public string SourceFilePath { get; set; }

        public DateTime TimestampUtc { get; set; }

        public RawLogEntry Entry { get; set; }
    }
}
