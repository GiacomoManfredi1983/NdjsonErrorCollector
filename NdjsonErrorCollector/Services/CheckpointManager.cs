using System;
using System.IO;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class CheckpointManager
    {
        public FileCheckpoint Prepare(string filePath, FileCheckpoint checkpoint, RunDiagnostics diagnostics)
        {
            if (!File.Exists(filePath))
            {
                diagnostics?.Warning($"Checkpoint skipped because source file is missing: {filePath}");
                return null;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                var currentSize = fileInfo.Length;
                var currentLastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;
                var effectiveCheckpoint = checkpoint ?? new FileCheckpoint { FilePath = filePath };

                if (effectiveCheckpoint.Offset > currentSize || currentLastWriteTicks < effectiveCheckpoint.LastWriteTimeUtcTicks)
                {
                    diagnostics?.Warning($"Checkpoint reset for {filePath} due to truncation or replacement.");
                    effectiveCheckpoint.Offset = 0;
                }

                effectiveCheckpoint.FilePath = filePath;
                effectiveCheckpoint.LastKnownSize = currentSize;
                effectiveCheckpoint.LastWriteTimeUtcTicks = currentLastWriteTicks;
                return effectiveCheckpoint;
            }
            catch (UnauthorizedAccessException ex)
            {
                diagnostics?.Warning($"Checkpoint skipped because access is denied for source file: {filePath}. {ex.Message}");
                return null;
            }
        }

        public bool ShouldScan(FileCheckpoint checkpoint)
        {
            if (checkpoint == null)
            {
                return false;
            }

            return checkpoint.Offset == 0
                || checkpoint.LastKnownSize != checkpoint.Offset
                || checkpoint.LastWriteTimeUtcTicks == 0;
        }
    }
}
