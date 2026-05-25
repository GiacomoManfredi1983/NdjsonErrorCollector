using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class ErrorNormalizer
    {
        public NormalizedErrorRecord Normalize(ParsedLogRecord record)
        {
            var normalized = new NormalizedErrorRecord
            {
                Source = record.SourceFilePath,
                Timestamp = record.TimestampUtc == DateTime.MinValue ? null : record.TimestampUtc.ToString("O"),
                ServiceId = record.Entry.ServiceID,
                Channel = record.Entry.Channel,
                ErrorCode = record.Entry.ErrorCode,
                ErrorMsg = record.Entry.ErrorMsg,
                Description = NormalizeWhitespace(record.Entry.Description),
                Caller = record.Entry.Caller,
                Command = record.Entry.CommandInfo?.Command,
                Arguments = record.Entry.CommandInfo?.Argument.ValueKind == JsonValueKind.Undefined ? null : record.Entry.CommandInfo.Argument.GetRawText(),
                Stack = record.Entry.Stack == null ? null : string.Join("|", record.Entry.Stack)
            };

            normalized.Key = ComputeKey(normalized);
            return normalized;
        }

        private static string ComputeKey(NormalizedErrorRecord record)
        {
            var keyPayload = string.Join("\n", new[]
            {
                record.ErrorCode?.ToString(),
                record.ErrorMsg,
                record.Description,
                record.Caller,
                record.Command,
                record.Arguments,
                record.Stack
            }.Select(value => NormalizeWhitespace(value) ?? string.Empty));

            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyPayload));
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : string.Join(" ", value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
