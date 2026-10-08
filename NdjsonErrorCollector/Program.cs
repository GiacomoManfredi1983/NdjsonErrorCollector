using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            var releaseRoot = ResolveReleaseRoot();
            var configuration = BuildConfiguration(releaseRoot);
            var settings = configuration.GetSection("Collector").Get<CollectorOptions>();
            NormalizeCollectorPaths(settings, releaseRoot);
            var handler = new HttpClientHandler
            {
                UseDefaultCredentials = true
            };
            var discoveryService = new ServerDiscoveryService(new HttpClient(handler));
            var sourceLogEnumerator = new SourceLogEnumerator();
            var checkpointStore = new CheckpointStore(settings?.StateDirectory ?? "data\\state");
            var checkpointManager = new CheckpointManager();
            var checkpointPruner = new CheckpointPruner();
            var ndjsonLogReader = new NdjsonLogReader();
            var fileReadRetryHandler = new FileReadRetryHandler();
            var errorThresholdFilter = new ErrorThresholdFilter();
            var errorNormalizer = new ErrorNormalizer();
            var reportingWindowAggregator = new ReportingWindowAggregator();
            var reportingDeliveryCoordinator = new ReportingDeliveryCoordinator();
            var configurationWarningCollector = new ConfigurationWarningCollector();
            var deduplicationStore = new DeduplicationStore(settings?.StateDirectory ?? "data\\state");
            var outputWriter = new OutputWriter();
            var emailNotificationService = new EmailNotificationService();
            var diagnostics = new RunDiagnostics(settings?.StateDirectory ?? "data\\state");

            Console.WriteLine("NdjsonErrorCollector configured.");
            Console.WriteLine($"Servers API: {settings?.ServersApiUrl}");
            Console.WriteLine($"Output NDJSON: {settings?.OutputNdjsonPath}");

            try
            {
                var thresholdsUtc = ParseGroupThresholds(settings?.GroupThresholds);
                var runCutoffUtc = DateTime.UtcNow;
                var locations = discoveryService.DiscoverAsync(settings?.ServersApiUrl).GetAwaiter().GetResult();
                diagnostics.Info($"Discovered {locations.Count} log folder candidates.");
                foreach (var location in locations)
                {
                    diagnostics.Info($"Available log folder for installation {location.Installation ?? "<missing>"}, group {location.Group ?? "<missing>"}: {location.LogFolderPath}");
                }
                var checkpointState = checkpointStore.Load();
                var deduplicationState = deduplicationStore.Load();
                if (!string.IsNullOrWhiteSpace(checkpointStore.LegacyBackupPath))
                {
                    diagnostics.Warning($"Legacy checkpoint state was backed up to {checkpointStore.LegacyBackupPath} and reset for group-aware processing.");
                }
                if (!string.IsNullOrWhiteSpace(deduplicationStore.LegacyBackupPath))
                {
                    diagnostics.Warning($"Legacy deduplication state was backed up to {deduplicationStore.LegacyBackupPath} and reset for group-aware processing.");
                }

                configurationWarningCollector.Collect(locations, thresholdsUtc, deduplicationState, diagnostics);

                diagnostics.Info($"Loaded {checkpointState.Groups.Sum(group => group.Value.Files.Count)} file checkpoints across {checkpointState.Groups.Count} groups.");
                diagnostics.Info($"Loaded {deduplicationState.Groups.Sum(group => group.Value.PendingRecords.Count)} pending records across {deduplicationState.Groups.Count} groups.");

                var rolloverService = new GroupRolloverService();
                var rolledOverGroups = rolloverService.Prepare(checkpointState, deduplicationState, thresholdsUtc, diagnostics);
                var windowStartsUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                var includeWindowStart = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (var threshold in thresholdsUtc)
                {
                    var reportingState = deduplicationState.Groups[threshold.Key];
                    var hasSuccessfulSend = TryParseUtc(reportingState.LastSuccessfulSendUtc, out var lastSuccessfulSendUtc);
                    windowStartsUtc[threshold.Key] = hasSuccessfulSend ? lastSuccessfulSendUtc : threshold.Value;
                    includeWindowStart[threshold.Key] = !hasSuccessfulSend;
                    PromoteDeferredRecords(
                        reportingState,
                        windowStartsUtc[threshold.Key],
                        runCutoffUtc,
                        includeWindowStart[threshold.Key],
                        reportingWindowAggregator,
                        diagnostics);
                }

                checkpointPruner.Prune(checkpointState, windowStartsUtc, diagnostics);
                outputWriter.RemoveGroups(settings?.OutputNdjsonPath, rolledOverGroups);
                if (!string.IsNullOrWhiteSpace(outputWriter.LegacyBackupPath))
                {
                    diagnostics.Warning($"Legacy ungrouped output was backed up to {outputWriter.LegacyBackupPath} and reset.");
                }
                checkpointStore.Save(checkpointState);
                deduplicationStore.Save(deduplicationState);

                var sourceFiles = sourceLogEnumerator.Enumerate(locations, settings?.LogFilePattern, windowStartsUtc, diagnostics);
                diagnostics.Info($"Discovered {sourceFiles.Count} reporting-window-eligible source log files.");

                foreach (var sourceFile in sourceFiles)
                {
                    if (string.IsNullOrWhiteSpace(sourceFile.Group))
                    {
                        diagnostics.Warning($"Skipping source file without an installation group: {sourceFile.FilePath}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(sourceFile.Installation))
                    {
                        diagnostics.Warning($"Skipping source file without an installation name: {sourceFile.FilePath}");
                        continue;
                    }

                    if (!windowStartsUtc.TryGetValue(sourceFile.Group, out var windowStartUtc))
                    {
                        diagnostics.Warning($"Skipping source file for unconfigured group {sourceFile.Group}: {sourceFile.FilePath}");
                        continue;
                    }

                    var groupCheckpoints = checkpointState.Groups[sourceFile.Group];
                    var groupReportingState = deduplicationState.Groups[sourceFile.Group];
                    try
                    {
                        currentFilePath = sourceFile.FilePath;
                        currentFolderPath = sourceFile.FolderPath;
                        diagnostics.Info($"Processing source file for installation {sourceFile.Installation}, group {sourceFile.Group}: {currentFilePath}");
                        groupCheckpoints.Files.TryGetValue(sourceFile.FilePath, out var checkpoint);
                        var preparedCheckpoint = checkpointManager.Prepare(sourceFile.FilePath, checkpoint, diagnostics);
                        groupCheckpoints.Files[sourceFile.FilePath] = preparedCheckpoint;

                        if (!checkpointManager.ShouldScan(preparedCheckpoint))
                        {
                            diagnostics.Info($"{sourceFile.FilePath}: skipped because file is unchanged since last run.");
                            checkpointStore.Save(checkpointState);
                            continue;
                        }

                        var newErrors = fileReadRetryHandler.Execute(
                            sourceFile.FilePath,
                            () => ndjsonLogReader.ReadNewErrors(sourceFile.FilePath, sourceFile.Installation, sourceFile.Group, preparedCheckpoint, settings?.ErrorChannel, diagnostics),
                            diagnostics);
                        diagnostics.Info($"{sourceFile.FilePath}: read {newErrors.Count} new error records.");

                        foreach (var newError in newErrors)
                        {
                            var disposition = errorThresholdFilter.Classify(
                                newError,
                                windowStartUtc,
                                runCutoffUtc,
                                includeWindowStart[sourceFile.Group],
                                diagnostics);
                            if (disposition == ReportingWindowDisposition.BeforeWindow || disposition == ReportingWindowDisposition.InvalidTimestamp)
                            {
                                continue;
                            }

                            var normalized = errorNormalizer.Normalize(newError);
                            if (disposition == ReportingWindowDisposition.AfterWindow)
                            {
                                groupReportingState.DeferredRecords.Add(normalized);
                                continue;
                            }

                            reportingWindowAggregator.TryAdd(groupReportingState, normalized, windowStartUtc, runCutoffUtc, diagnostics);
                        }

                        checkpointStore.Save(checkpointState);
                        deduplicationStore.Save(deduplicationState);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        diagnostics.Warning($"Access denied while processing source file {sourceFile.FilePath} in folder {sourceFile.FolderPath}: {ex.Message}. Skipping file.");
                    }
                }

                currentFilePath = null;
                currentFolderPath = null;

                foreach (var group in deduplicationState.Groups.Values)
                {
                    group.PendingThroughUtc = runCutoffUtc.ToString("O");
                    foreach (var record in group.PendingRecords.Values)
                    {
                        record.WindowEndUtc = group.PendingThroughUtc;
                    }
                }

                checkpointStore.Save(checkpointState);
                deduplicationStore.Save(deduplicationState);

                var pendingNotifications = deduplicationState.Groups.Values
                    .SelectMany(group => group.PendingRecords.Values)
                    .ToList();
                var pendingWarnings = deduplicationState.PendingWarnings.Values.ToList();
                if (pendingNotifications.Count > 0 || pendingWarnings.Count > 0)
                {
                    if (emailNotificationService.SendSummary(settings?.Email, pendingNotifications, pendingWarnings))
                    {
                        if (pendingNotifications.Count > 0)
                        {
                            outputWriter.Append(settings?.OutputNdjsonPath, pendingNotifications);
                            diagnostics.Info($"Appended {pendingNotifications.Count} delivered daily errors to output.");
                        }

                        reportingDeliveryCoordinator.CompleteSuccessfulDelivery(deduplicationState, runCutoffUtc);

                        deduplicationStore.Save(deduplicationState);
                        diagnostics.Info("Daily summary notification sent; reporting windows advanced.");
                    }
                    else
                    {
                        diagnostics.Warning("Daily summary was not sent because email configuration is incomplete; pending records were retained.");
                    }
                }

                checkpointStore.Save(checkpointState);
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

        private static void PromoteDeferredRecords(
            Models.GroupReportingState reportingState,
            DateTime windowStartUtc,
            DateTime windowEndUtc,
            bool includeWindowStart,
            ReportingWindowAggregator aggregator,
            RunDiagnostics diagnostics)
        {
            var stillDeferred = new List<Models.NormalizedErrorRecord>();
            foreach (var record in reportingState.DeferredRecords)
            {
                if (!TryParseUtc(record.Timestamp, out var timestampUtc))
                {
                    continue;
                }

                if (timestampUtc > windowEndUtc)
                {
                    stillDeferred.Add(record);
                    continue;
                }

                if (timestampUtc < windowStartUtc || (!includeWindowStart && timestampUtc == windowStartUtc))
                {
                    continue;
                }

                aggregator.TryAdd(reportingState, record, windowStartUtc, windowEndUtc, diagnostics);
            }

            reportingState.DeferredRecords = stillDeferred;
        }

        private static bool TryParseUtc(string value, out DateTime valueUtc)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                valueUtc = parsed.UtcDateTime;
                return true;
            }

            valueUtc = default;
            return false;
        }

        private static IReadOnlyDictionary<string, DateTime> ParseGroupThresholds(IDictionary<string, string> configuredThresholds)
        {
            if (configuredThresholds == null || configuredThresholds.Count == 0)
            {
                throw new InvalidOperationException("Collector.GroupThresholds must contain at least one installation group.");
            }

            var thresholdsUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach (var configuredThreshold in configuredThresholds)
            {
                if (string.IsNullOrWhiteSpace(configuredThreshold.Key)
                    || !DateTimeOffset.TryParse(configuredThreshold.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedThreshold)
                    || parsedThreshold.Offset != TimeSpan.Zero)
                {
                    throw new InvalidOperationException($"Collector.GroupThresholds.{configuredThreshold.Key} must be an ISO 8601 UTC timestamp, for example 2026-03-15T14:30:00Z.");
                }

                thresholdsUtc[configuredThreshold.Key] = parsedThreshold.UtcDateTime;
            }

            return thresholdsUtc;
        }

        private static IConfiguration BuildConfiguration(string releaseRoot)
        {
            var configurationDirectory = Path.Combine(releaseRoot, "configuration");
            var configurationPath = Path.Combine(configurationDirectory, "appsettings.json");
            var basePath = File.Exists(configurationPath)
                ? configurationDirectory
                : Directory.GetCurrentDirectory();
            var settingsFileName = File.Exists(configurationPath)
                ? "appsettings.json"
                : "appsettings.json";

            return new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile(settingsFileName, optional: false, reloadOnChange: false)
                .AddEnvironmentVariables(prefix: "NDJSONCOLLECTOR_")
                .Build();
        }

        private static string ResolveReleaseRoot()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var directoryInfo = new DirectoryInfo(currentDirectory);

            if (directoryInfo.Name.Equals("programs", StringComparison.OrdinalIgnoreCase) && directoryInfo.Parent != null)
            {
                return directoryInfo.Parent.FullName;
            }

            return currentDirectory;
        }

        private static void NormalizeCollectorPaths(CollectorOptions settings, string releaseRoot)
        {
            if (settings == null)
            {
                return;
            }

            settings.OutputNdjsonPath = ResolveAgainstReleaseRoot(settings.OutputNdjsonPath, releaseRoot);
            settings.StateDirectory = ResolveAgainstReleaseRoot(settings.StateDirectory, releaseRoot);

            if (settings.Email != null)
            {
                settings.Email.PickupDirectory = ResolveAgainstReleaseRoot(settings.Email.PickupDirectory, releaseRoot);
            }
        }

        private static string ResolveAgainstReleaseRoot(string path, string releaseRoot)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(releaseRoot, path));
        }
    }

    class CollectorOptions
    {
        public string ServersApiUrl { get; set; }

        public string LogFilePattern { get; set; }

        public string OutputNdjsonPath { get; set; }

        public string StateDirectory { get; set; }

        public string ErrorChannel { get; set; }

        public Dictionary<string, string> GroupThresholds { get; set; }

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
