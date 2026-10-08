using System;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    enum ReportingWindowDisposition
    {
        BeforeWindow,
        InWindow,
        AfterWindow,
        InvalidTimestamp
    }

    class ErrorThresholdFilter
    {
        public bool ShouldInclude(ParsedLogRecord record, DateTime thresholdUtc, RunDiagnostics diagnostics)
        {
            return Classify(record, thresholdUtc, DateTime.MaxValue, includeWindowStart: true, diagnostics) == ReportingWindowDisposition.InWindow;
        }

        public ReportingWindowDisposition Classify(
            ParsedLogRecord record,
            DateTime windowStartUtc,
            DateTime windowEndUtc,
            bool includeWindowStart,
            RunDiagnostics diagnostics)
        {
            if (record.TimestampUtc == DateTime.MinValue)
            {
                diagnostics?.Warning($"Skipped error with a missing or invalid timestamp in {record.SourceFilePath}.");
                return ReportingWindowDisposition.InvalidTimestamp;
            }

            if (record.TimestampUtc < windowStartUtc || (!includeWindowStart && record.TimestampUtc == windowStartUtc))
            {
                return ReportingWindowDisposition.BeforeWindow;
            }

            if (record.TimestampUtc > windowEndUtc)
            {
                return ReportingWindowDisposition.AfterWindow;
            }

            return ReportingWindowDisposition.InWindow;
        }
    }
}
