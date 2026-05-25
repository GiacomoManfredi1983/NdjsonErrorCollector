namespace NdjsonErrorCollector.Models
{
    class NormalizedErrorRecord
    {
        public string Key { get; set; }

        public string Source { get; set; }

        public string Timestamp { get; set; }

        public string ServiceId { get; set; }

        public string Channel { get; set; }

        public int? ErrorCode { get; set; }

        public string ErrorMsg { get; set; }

        public string Description { get; set; }

        public string Caller { get; set; }

        public string Command { get; set; }

        public string Arguments { get; set; }

        public string Stack { get; set; }
    }
}
