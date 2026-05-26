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
        public void SendSummary(EmailOptions emailOptions, IReadOnlyCollection<NormalizedErrorRecord> records)
        {
            if (emailOptions == null || records == null || records.Count == 0 || string.IsNullOrWhiteSpace(emailOptions.Host) || string.IsNullOrWhiteSpace(emailOptions.FromAddress) || emailOptions.To == null || emailOptions.To.Length == 0)
            {
                return;
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

            message.Subject = $"{emailOptions.SubjectPrefix} {records.Count} new errors detected";
            message.Body = new TextPart("plain")
            {
                Text = BuildBody(records)
            };

            using var client = new MailKit.Net.Smtp.SmtpClient();
            client.Connect(emailOptions.Host, emailOptions.Port, emailOptions.UseSsl);
            client.Send(message);
            client.Disconnect(true);
        }

        private static string BuildBody(IEnumerable<NormalizedErrorRecord> records)
        {
            var builder = new StringBuilder();
            builder.AppendLine("New unique errors detected in this run:");
            builder.AppendLine();

            foreach (var record in records)
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

            return builder.ToString().TrimEnd();
        }
    }
}
