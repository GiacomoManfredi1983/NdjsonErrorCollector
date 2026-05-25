using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using NdjsonErrorCollector.Models;

namespace NdjsonErrorCollector.Services
{
    class EmailNotificationService
    {
        public void SendSummary(EmailOptions emailOptions, IReadOnlyCollection<NormalizedErrorRecord> records)
        {
            if (emailOptions == null || records == null || records.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(emailOptions.PickupDirectory);

            using var message = new MailMessage(emailOptions.From, emailOptions.To)
            {
                Subject = $"{emailOptions.SubjectPrefix} {records.Count} new errors detected",
                Body = BuildBody(records),
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };

            using var client = new SmtpClient
            {
                DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                PickupDirectoryLocation = emailOptions.PickupDirectory
            };

            client.Send(message);
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
