namespace NdjsonErrorCollector.Models
{
    class FileCheckpoint
    {
        public string FilePath { get; set; }

        public long Offset { get; set; }

        public long LastKnownSize { get; set; }

        public long LastWriteTimeUtcTicks { get; set; }
    }
}
