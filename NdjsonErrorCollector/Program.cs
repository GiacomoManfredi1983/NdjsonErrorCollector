using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using NdjsonErrorCollector.Services;

namespace NdjsonErrorCollector
{
    class Program
    {
        static int Main(string[] args)
        {
            var configuration = BuildConfiguration();
            var settings = configuration.GetSection("Collector").Get<CollectorOptions>();
            var discoveryService = new ServerDiscoveryService(new HttpClient());
            var sourceLogEnumerator = new SourceLogEnumerator();
            var checkpointStore = new CheckpointStore(settings?.StateDirectory ?? "data\\state");
            var checkpointManager = new CheckpointManager();
            var ndjsonLogReader = new NdjsonLogReader();
            var errorNormalizer = new ErrorNormalizer();
            var deduplicationStore = new DeduplicationStore(settings?.StateDirectory ?? "data\\state");
            var outputWriter = new OutputWriter();
            var emailNotificationService = new EmailNotificationService();
            var diagnostics = new RunDiagnostics(settings?.StateDirectory ?? "data\\state");

            Console.WriteLine("NdjsonErrorCollector configured.");
            Console.WriteLine($"Servers API: {settings?.ServersApiUrl}");
            Console.WriteLine($"Output NDJSON: {settings?.OutputNdjsonPath}");

            try
            {
                var locations = discoveryService.DiscoverAsync(settings?.ServersApiUrl).GetAwaiter().GetResult();
                diagnostics.Info($"Discovered {locations.Count} log folder candidates.");
                var sourceFiles = sourceLogEnumerator.Enumerate(locations, settings?.LogFilePattern, diagnostics);
                diagnostics.Info($"Discovered {sourceFiles.Count} source log files.");
                var checkpoints = checkpointStore.Load();
                diagnostics.Info($"Loaded {checkpoints.Count} file checkpoints.");
                var deduplicationState = deduplicationStore.Load();
                diagnostics.Info($"Loaded {deduplicationState.ExportedKeys.Count} exported keys and {deduplicationState.NotifiedKeys.Count} notified keys.");
                var newUniqueErrors = new List<Models.NormalizedErrorRecord>();

                foreach (var sourceFile in sourceFiles)
                {
                    checkpoints.TryGetValue(sourceFile.FilePath, out var checkpoint);
                    var preparedCheckpoint = checkpointManager.Prepare(sourceFile.FilePath, checkpoint, diagnostics);
                    checkpoints[sourceFile.FilePath] = preparedCheckpoint;
                    var newErrors = ndjsonLogReader.ReadNewErrors(sourceFile.FilePath, preparedCheckpoint, settings?.ErrorChannel, diagnostics);
                    diagnostics.Info($"{sourceFile.FilePath}: read {newErrors.Count} new error records.");

                    foreach (var newError in newErrors)
                    {
                        var normalized = errorNormalizer.Normalize(newError);
                        if (deduplicationState.ExportedKeys.Add(normalized.Key))
                        {
                            newUniqueErrors.Add(normalized);
                            deduplicationState.PendingNotifications[normalized.Key] = normalized;
                        }
                    }
                }

                outputWriter.Append(settings?.OutputNdjsonPath, newUniqueErrors);
                diagnostics.Info($"Appended {newUniqueErrors.Count} unique errors to output.");

                if (deduplicationState.PendingNotifications.Count > 0)
                {
                    emailNotificationService.SendSummary(settings?.Email, new List<Models.NormalizedErrorRecord>(deduplicationState.PendingNotifications.Values));
                    foreach (var key in new List<string>(deduplicationState.PendingNotifications.Keys))
                    {
                        deduplicationState.NotifiedKeys.Add(key);
                        deduplicationState.PendingNotifications.Remove(key);
                    }

                    diagnostics.Info("Summary notification queued.");
                }

                checkpointStore.Save(checkpoints);
                deduplicationStore.Save(deduplicationState);
            }
            catch (Exception ex)
            {
                diagnostics.Error($"Collector run failed: {ex.Message}");
                return 1;
            }

            return 0;
        }

        private static IConfiguration BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "NDJSONCOLLECTOR_")
                .Build();
        }
    }

    class CollectorOptions
    {
        public string ServersApiUrl { get; set; }

        public string LogFilePattern { get; set; }

        public string OutputNdjsonPath { get; set; }

        public string StateDirectory { get; set; }

        public string ErrorChannel { get; set; }

        public EmailOptions Email { get; set; }
    }

    class EmailOptions
    {
        public string From { get; set; }

        public string To { get; set; }

        public string SubjectPrefix { get; set; }

        public string PickupDirectory { get; set; }
    }
}
