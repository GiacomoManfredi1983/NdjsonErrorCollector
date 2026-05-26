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
            string currentFilePath = null;
            string currentFolderPath = null;
            var configuration = BuildConfiguration();
            var settings = configuration.GetSection("Collector").Get<CollectorOptions>();
            var handler = new HttpClientHandler
            {
                UseDefaultCredentials = true
            };
            var discoveryService = new ServerDiscoveryService(new HttpClient(handler));
            var sourceLogEnumerator = new SourceLogEnumerator();
            var checkpointStore = new CheckpointStore(settings?.StateDirectory ?? "data\\state");
            var checkpointManager = new CheckpointManager();
            var ndjsonLogReader = new NdjsonLogReader();
            var fileReadRetryHandler = new FileReadRetryHandler();
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
                foreach (var location in locations)
                {
                    diagnostics.Info($"Available log folder: {location.LogFolderPath}");
                }
                var sourceFiles = sourceLogEnumerator.Enumerate(locations, settings?.LogFilePattern, diagnostics);
                diagnostics.Info($"Discovered {sourceFiles.Count} source log files.");
                var checkpoints = checkpointStore.Load();
                diagnostics.Info($"Loaded {checkpoints.Count} file checkpoints.");
                var deduplicationState = deduplicationStore.Load();
                diagnostics.Info($"Loaded {deduplicationState.ExportedKeys.Count} exported keys and {deduplicationState.NotifiedKeys.Count} notified keys.");
                var newUniqueErrors = new List<Models.NormalizedErrorRecord>();

                foreach (var sourceFile in sourceFiles)
                {
                    try
                    {
                        currentFilePath = sourceFile.FilePath;
                        currentFolderPath = sourceFile.FolderPath;
                        diagnostics.Info($"Processing source file: {currentFilePath}");
                        checkpoints.TryGetValue(sourceFile.FilePath, out var checkpoint);
                        var preparedCheckpoint = checkpointManager.Prepare(sourceFile.FilePath, checkpoint, diagnostics);
                        checkpoints[sourceFile.FilePath] = preparedCheckpoint;

                        if (!checkpointManager.ShouldScan(preparedCheckpoint))
                        {
                            diagnostics.Info($"{sourceFile.FilePath}: skipped because file is unchanged since last run.");
                            checkpointStore.Save(checkpoints);
                            continue;
                        }

                        var newErrors = fileReadRetryHandler.Execute(
                            sourceFile.FilePath,
                            () => ndjsonLogReader.ReadNewErrors(sourceFile.FilePath, preparedCheckpoint, settings?.ErrorChannel, diagnostics),
                            diagnostics);
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

                        checkpointStore.Save(checkpoints);
                        deduplicationStore.Save(deduplicationState);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        diagnostics.Warning($"Access denied while processing source file {sourceFile.FilePath} in folder {sourceFile.FolderPath}: {ex.Message}. Skipping file.");
                    }
                }

                currentFilePath = null;
                currentFolderPath = null;

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
                var failureContext = string.IsNullOrWhiteSpace(currentFilePath)
                    ? string.Empty
                    : $" Last file: {currentFilePath}. Folder: {currentFolderPath}.";
                diagnostics.Error($"Collector run failed ({ex.GetType().FullName}): {ex.Message}.{failureContext}");
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
        public string Host { get; set; }

        public int Port { get; set; }

        public string FromAddress { get; set; }

        public string[] To { get; set; }

        public string[] Cc { get; set; }

        public bool UseSsl { get; set; }

        public string SubjectPrefix { get; set; }

        public string PickupDirectory { get; set; }
    }
}
