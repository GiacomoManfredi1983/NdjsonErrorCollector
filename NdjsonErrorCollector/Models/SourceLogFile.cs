using System;

namespace NdjsonErrorCollector.Models
{
    class SourceLogFile
    {
        public string Installation { get; set; }

        public string Group { get; set; }

        public string FolderPath { get; set; }

        public string FilePath { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }
    }
}
