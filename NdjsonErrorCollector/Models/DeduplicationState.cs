using System.Collections.Generic;

namespace NdjsonErrorCollector.Models
{
    class DeduplicationState
    {
        public HashSet<string> ExportedKeys { get; set; } = new HashSet<string>();

        public HashSet<string> NotifiedKeys { get; set; } = new HashSet<string>();

        public Dictionary<string, NormalizedErrorRecord> PendingNotifications { get; set; } = new Dictionary<string, NormalizedErrorRecord>();
    }
}
