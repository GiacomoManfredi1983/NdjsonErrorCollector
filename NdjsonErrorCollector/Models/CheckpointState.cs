using System;
using System.Collections.Generic;

namespace NdjsonErrorCollector.Models
{
    class CheckpointState
    {
        public Dictionary<string, GroupCheckpointState> Groups { get; set; } = new Dictionary<string, GroupCheckpointState>(StringComparer.OrdinalIgnoreCase);
    }

    class GroupCheckpointState
    {
        public string AppliedThresholdUtc { get; set; }

        public Dictionary<string, FileCheckpoint> Files { get; set; } = new Dictionary<string, FileCheckpoint>(StringComparer.OrdinalIgnoreCase);
    }
}
