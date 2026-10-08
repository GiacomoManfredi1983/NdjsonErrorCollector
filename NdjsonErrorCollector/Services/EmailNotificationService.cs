using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using MailKit.Net.Smtp;
using MimeKit;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class EmailNotificationService
    {
        public bool SendSummary(
            EmailOptions emailOptions,
            IReadOnlyCollection<NormalizedErrorRecord> records,
            IReadOnlyCollection<string> warnings)
        {
            var errorCount = records?.Count ?? 0;
            var warningCount = warnings?.Count ?? 0;
            if (emailOptions == null || (errorCount == 0 && warningCount == 0) || string.IsNullOrWhiteSpace(emailOptions.Host) || string.IsNullOrWhiteSpace(emailOptions.FromAddress) || emailOptions.To == null || emailOptions.To.Length == 0)
            {
                return false;
            }

            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(emailOptions.FromAddress));

            foreach (var recipient in emailOptions.To.Where(address => !string.IsNullOrWhiteSpace(address)))
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }

            foreach (var recipient in (emailOptions.Cc ?? new string[0]).Where(address => !string.IsNullOrWhiteSpace(address)))
            {
                message.Cc.Add(MailboxAddress.Parse(recipient));
            }

            message.Subject = errorCount > 0
                ? $"{emailOptions.SubjectPrefix} {errorCount} daily errors detected"
                : $"{emailOptions.SubjectPrefix} configuration warning";
            message.Body = new TextPart("plain")
            {
                Text = BuildBody(records ?? new NormalizedErrorRecord[0], warnings ?? new string[0])
            };

            using var client = new MailKit.Net.Smtp.SmtpClient();
            client.Connect(emailOptions.Host, emailOptions.Port, emailOptions.UseSsl);
            client.Send(message);
            client.Disconnect(true);
            return true;
        }

        private static string BuildBody(IEnumerable<NormalizedErrorRecord> records, IEnumerable<string> warnings)
        {
            var builder = new StringBuilder();
            var warningList = warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)).ToList();
            if (warningList.Count > 0)
            {
                builder.AppendLine("========== CONFIGURATION WARNINGS ==========");
                foreach (var warning in warningList)
                {
                    builder.AppendLine($"WARNING: {warning}");
                }

                builder.AppendLine();
            }

            var recordList = records.ToList();
            if (recordList.Count > 0)
            {
                builder.AppendLine("Errors detected in this reporting window:");
                builder.AppendLine();
            }

            var groupedRecords = recordList
                .GroupBy(record => record.Group ?? "UNKNOWN", System.StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => string.Equals(group.Key, "sviluppo", System.StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(group => group.Key, System.StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedRecords)
            {
                builder.AppendLine($"========== {group.Key.ToUpperInvariant()} ({group.Count()} errors) ==========");
                var windowStarts = group.Select(record => record.WindowStartUtc).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
                var windowEnds = group.Select(record => record.WindowEndUtc).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct().ToList();
                if (windowStarts.Count == 1 && windowEnds.Count == 1)
                {
                    builder.AppendLine($"Reporting window: {windowStarts[0]} to {windowEnds[0]}");
                }

                builder.AppendLine();

                foreach (var installation in group
                    .GroupBy(record => record.Installation ?? "UNKNOWN INSTALLATION", System.StringComparer.OrdinalIgnoreCase)
                    .OrderBy(records => records.Key, System.StringComparer.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"----- {installation.Key} ({installation.Count()} errors) -----");
                    builder.AppendLine();

                    foreach (var record in installation)
                    {
                        builder.AppendLine($"Key: {record.Key}");
                        builder.AppendLine($"Timestamp: {record.Timestamp}");
                        builder.AppendLine($"Source: {record.Source}");
                        builder.AppendLine($"ErrorCode: {record.ErrorCode}");
                        builder.AppendLine($"ErrorMsg: {record.ErrorMsg}");
                        builder.AppendLine($"Description: {record.Description}");
                        builder.AppendLine($"Command: {record.Command}");
                        builder.AppendLine($"Arguments: {record.Arguments}");
                        builder.AppendLine();
                    }
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
