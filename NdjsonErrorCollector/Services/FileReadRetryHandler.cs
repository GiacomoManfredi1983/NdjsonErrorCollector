using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class FileReadRetryHandler
    {
        private readonly int _maxAttempts;
        private readonly int _delayMilliseconds;

        public FileReadRetryHandler(int maxAttempts = 3, int delayMilliseconds = 500)
        {
            _maxAttempts = maxAttempts < 1 ? 1 : maxAttempts;
            _delayMilliseconds = delayMilliseconds < 0 ? 0 : delayMilliseconds;
        }

        public IReadOnlyList<ParsedLogRecord> Execute(string filePath, Func<IReadOnlyList<ParsedLogRecord>> operation, RunDiagnostics diagnostics)
        {
            for (var attempt = 1; attempt <= _maxAttempts; attempt++)
            {
                try
                {
                    return operation();
                }
                catch (IOException ex)
                {
                    if (attempt == _maxAttempts)
                    {
                        diagnostics?.Warning($"Network error reading {filePath} on attempt {attempt} of {_maxAttempts}: {ex.Message}. Skipping file.");
                        return Array.Empty<ParsedLogRecord>();
                    }

                    diagnostics?.Warning($"Network error reading {filePath} on attempt {attempt} of {_maxAttempts}: {ex.Message}. Retrying.");
                    if (_delayMilliseconds > 0)
                    {
                        Thread.Sleep(_delayMilliseconds);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    diagnostics?.Warning($"Access denied reading {filePath}: {ex.Message}. Skipping file.");
                    return Array.Empty<ParsedLogRecord>();
                }
            }

            return Array.Empty<ParsedLogRecord>();
        }
    }
}
