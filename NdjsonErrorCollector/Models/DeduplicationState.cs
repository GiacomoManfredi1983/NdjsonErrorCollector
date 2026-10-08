using System;
using System.Collections.Generic;

namespace NdjsonErrorCollector.Models
{
    class DeduplicationState
    {
        public int SchemaVersion { get; set; } = 2;

        public Dictionary<string, GroupReportingState> Groups { get; set; } = new Dictionary<string, GroupReportingState>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> NotifiedWarningKeys { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> PendingWarnings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    class GroupReportingState
    {
        public string AppliedThresholdUtc { get; set; }

        public string LastSuccessfulSendUtc { get; set; }

        public string PendingThroughUtc { get; set; }

        public Dictionary<string, NormalizedErrorRecord> PendingRecords { get; set; } = new Dictionary<string, NormalizedErrorRecord>(StringComparer.OrdinalIgnoreCase);

        public List<NormalizedErrorRecord> DeferredRecords { get; set; } = new List<NormalizedErrorRecord>();
    }
}
