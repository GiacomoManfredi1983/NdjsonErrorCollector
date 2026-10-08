using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NdjsonErrorCollector.Models;
using NdjsonErrorCollector.Services;
using Xunit;

namespace NdjsonErrorCollector.Tests
{
    public class UnitTest1
    {
        [Fact]
        public void DeriveLogFolder_ReplacesDbWithDfsLogs()
        {
            var result = ServerDiscoveryService.DeriveLogFolder("\\\\AZWESofia09\\sofiaw\\AllianzCon\\DB");

            Assert.Equal("\\\\AZWESofia09\\sofiaw\\AllianzCon\\DFS\\Logs", result);
        }

        [Fact]
        public void Normalize_GeneratesSameKeyForEquivalentErrors()
        {
            var normalizer = new ErrorNormalizer();
            var record1 = new ParsedLogRecord
            {
                SourceFilePath = "a.log",
                TimestampUtc = DateTime.UtcNow,
                Entry = new RawLogEntry
                {
                    ServiceID = "7",
                    Channel = "SERVICE_ERROR",
                    Caller = "Caller",
                    ErrorCode = 22,
                    ErrorMsg = "22 FILE NAME ERROR",
                    Description = " DFS Encountered an error ",
                    Stack = new[] { "A", "B" },
                    CommandInfo = new CommandInfo { Command = "FSTIE" }
                }
            };
            var record2 = new ParsedLogRecord
            {
                SourceFilePath = "b.log",
                TimestampUtc = DateTime.UtcNow.AddMinutes(1),
                Entry = new RawLogEntry
                {
                    ServiceID = "8",
                    Channel = "SERVICE_ERROR",
                    Caller = "Caller",
                    ErrorCode = 22,
                    ErrorMsg = "22 FILE NAME ERROR",
                    Description = "DFS Encountered an error",
                    Stack = new[] { "A", "B" },
                    CommandInfo = new CommandInfo { Command = "FSTIE" }
                }
            };

            Assert.Equal(normalizer.Normalize(record1).Key, normalizer.Normalize(record2).Key);
        }

        [Fact]
        public void Normalize_AllowsMissingCommandArgument()
        {
            var normalizer = new ErrorNormalizer();
            var record = new ParsedLogRecord
            {
                SourceFilePath = "a.log",
                TimestampUtc = DateTime.UtcNow,
                Entry = new RawLogEntry
                {
                    ServiceID = "7",
                    Channel = "SERVICE_ERROR",
                    ErrorCode = 908,
                    ErrorMsg = "ERR_AUTH",
                    CommandInfo = new CommandInfo
                    {
                        Command = "LOGIN"
                    }
                }
            };

            var normalized = normalizer.Normalize(record);

            Assert.Equal("LOGIN", normalized.Command);
            Assert.Null(normalized.Arguments);
            Assert.False(string.IsNullOrWhiteSpace(normalized.Key));
        }

        [Fact]
        public async Task DiscoverAsync_ExtractsGroupAlongsideDatabasePath()
        {
            var json = "[{\"name\":\"server-a\",\"group\":\"sviluppo\",\"db\":\"\\\\\\\\server\\\\share\\\\DB\"}]";
            var service = new ServerDiscoveryService(new HttpClient(new StubHttpMessageHandler(json)));

            var locations = await service.DiscoverAsync("http://example/servers");

            Assert.Single(locations);
            Assert.Equal("server-a", locations[0].Installation);
            Assert.Equal("sviluppo", locations[0].Group);
            Assert.Equal("\\\\server\\share\\DFS\\Logs", locations[0].LogFolderPath);
        }

        [Fact]
        public void ErrorThresholdFilter_IncludesBoundaryAndRejectsOlderOrMissingTimestamps()
        {
            var filter = new ErrorThresholdFilter();
            var threshold = new DateTime(2026, 3, 15, 14, 30, 0, DateTimeKind.Utc);

            Assert.True(filter.ShouldInclude(new ParsedLogRecord { TimestampUtc = threshold }, threshold, null));
            Assert.False(filter.ShouldInclude(new ParsedLogRecord { TimestampUtc = threshold.AddTicks(-1) }, threshold, null));
            Assert.False(filter.ShouldInclude(new ParsedLogRecord { TimestampUtc = DateTime.MinValue }, threshold, null));
        }

        [Fact]
        public void GroupRolloverService_ResetsOnlyChangedGroup()
        {
            var oldThreshold = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var newThreshold = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
            var unchangedThreshold = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            var checkpoints = new CheckpointState();
            var deduplication = new DeduplicationState();
            checkpoints.Groups["sviluppo"] = new GroupCheckpointState { AppliedThresholdUtc = oldThreshold.ToString("O") };
            checkpoints.Groups["sviluppo"].Files["a.log"] = new FileCheckpoint { Offset = 10 };
            deduplication.Groups["sviluppo"] = new GroupReportingState { AppliedThresholdUtc = oldThreshold.ToString("O") };
            deduplication.Groups["sviluppo"].PendingRecords["old"] = new NormalizedErrorRecord();
            checkpoints.Groups["consulenza"] = new GroupCheckpointState { AppliedThresholdUtc = unchangedThreshold.ToString("O") };
            checkpoints.Groups["consulenza"].Files["b.log"] = new FileCheckpoint { Offset = 20 };
            deduplication.Groups["consulenza"] = new GroupReportingState { AppliedThresholdUtc = unchangedThreshold.ToString("O") };
            deduplication.Groups["consulenza"].PendingRecords["keep"] = new NormalizedErrorRecord();

            var rolledOver = new GroupRolloverService().Prepare(
                checkpoints,
                deduplication,
                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sviluppo"] = newThreshold,
                    ["consulenza"] = unchangedThreshold
                },
                null);

            Assert.Equal(new[] { "sviluppo" }, rolledOver);
            Assert.Empty(checkpoints.Groups["sviluppo"].Files);
            Assert.Empty(deduplication.Groups["sviluppo"].PendingRecords);
            Assert.Single(checkpoints.Groups["consulenza"].Files);
            Assert.Contains("keep", deduplication.Groups["consulenza"].PendingRecords.Keys);
        }

        [Fact]
        public void GroupRolloverService_UnchangedThresholdPreservesState()
        {
            var threshold = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
            var checkpoints = new CheckpointState();
            var deduplication = new DeduplicationState();
            checkpoints.Groups["sviluppo"] = new GroupCheckpointState { AppliedThresholdUtc = threshold.ToString("O") };
            checkpoints.Groups["sviluppo"].Files["current.log"] = new FileCheckpoint { Offset = 100 };
            deduplication.Groups["sviluppo"] = new GroupReportingState { AppliedThresholdUtc = threshold.ToString("O") };
            deduplication.Groups["sviluppo"].PendingRecords["already-pending"] = new NormalizedErrorRecord();

            var rolledOver = new GroupRolloverService().Prepare(
                checkpoints,
                deduplication,
                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sviluppo"] = threshold
                },
                null);

            Assert.Empty(rolledOver);
            Assert.Contains("current.log", checkpoints.Groups["sviluppo"].Files.Keys);
            Assert.Contains("already-pending", deduplication.Groups["sviluppo"].PendingRecords.Keys);
        }

        [Fact]
        public void ChangedThreshold_RebuildsDeduplicationOnlyFromIncludedRecords()
        {
            var oldThreshold = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc);
            var newThreshold = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
            var checkpoints = new CheckpointState();
            var deduplication = new DeduplicationState();
            checkpoints.Groups["sviluppo"] = new GroupCheckpointState { AppliedThresholdUtc = oldThreshold.ToString("O") };
            deduplication.Groups["sviluppo"] = new GroupReportingState { AppliedThresholdUtc = oldThreshold.ToString("O") };
            deduplication.Groups["sviluppo"].PendingRecords["old-range-key"] = new NormalizedErrorRecord();

            new GroupRolloverService().Prepare(
                checkpoints,
                deduplication,
                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sviluppo"] = newThreshold
                },
                null);

            var entry = new RawLogEntry
            {
                Channel = "SERVICE_ERROR",
                ErrorCode = 22,
                ErrorMsg = "FILE NAME ERROR",
                Description = "same normalized error"
            };
            var beforeThreshold = new ParsedLogRecord { Installation = "A", Group = "sviluppo", SourceFilePath = "a.log", TimestampUtc = newThreshold.AddTicks(-1), Entry = entry };
            var atThreshold = new ParsedLogRecord { Installation = "A", Group = "sviluppo", SourceFilePath = "a.log", TimestampUtc = newThreshold, Entry = entry };
            var repeatedAfterThreshold = new ParsedLogRecord { Installation = "A", Group = "sviluppo", SourceFilePath = "b.log", TimestampUtc = newThreshold.AddMinutes(1), Entry = entry };
            var filter = new ErrorThresholdFilter();
            var normalizer = new ErrorNormalizer();
            var aggregator = new ReportingWindowAggregator();
            var reportingState = deduplication.Groups["sviluppo"];

            Assert.False(filter.ShouldInclude(beforeThreshold, newThreshold, null));
            Assert.True(filter.ShouldInclude(atThreshold, newThreshold, null));
            Assert.True(aggregator.TryAdd(reportingState, normalizer.Normalize(atThreshold), newThreshold, newThreshold.AddDays(1), null));
            Assert.True(filter.ShouldInclude(repeatedAfterThreshold, newThreshold, null));
            Assert.False(aggregator.TryAdd(reportingState, normalizer.Normalize(repeatedAfterThreshold), newThreshold, newThreshold.AddDays(1), null));
            Assert.Single(reportingState.PendingRecords);
            Assert.DoesNotContain("old-range-key", reportingState.PendingRecords.Keys);
        }

        [Fact]
        public void ErrorThresholdFilter_UsesInclusiveFirstAndExclusiveLaterWindows()
        {
            var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddDays(1);
            var filter = new ErrorThresholdFilter();
            var boundary = new ParsedLogRecord { TimestampUtc = start };
            var afterEnd = new ParsedLogRecord { TimestampUtc = end.AddTicks(1) };

            Assert.Equal(ReportingWindowDisposition.InWindow, filter.Classify(boundary, start, end, includeWindowStart: true, null));
            Assert.Equal(ReportingWindowDisposition.BeforeWindow, filter.Classify(boundary, start, end, includeWindowStart: false, null));
            Assert.Equal(ReportingWindowDisposition.AfterWindow, filter.Classify(afterEnd, start, end, includeWindowStart: false, null));
        }

        [Fact]
        public void ReportingWindowAggregator_DeduplicatesWithinInstallationOnly()
        {
            var start = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddDays(1);
            var aggregator = new ReportingWindowAggregator();
            var state = new GroupReportingState();
            var installationA = new NormalizedErrorRecord { Installation = "Installation A", Key = "same-error" };
            var installationADuplicate = new NormalizedErrorRecord { Installation = "installation a", Key = "same-error" };
            var installationB = new NormalizedErrorRecord { Installation = "Installation B", Key = "same-error" };

            Assert.True(aggregator.TryAdd(state, installationA, start, end, null));
            Assert.False(aggregator.TryAdd(state, installationADuplicate, start, end, null));
            Assert.True(aggregator.TryAdd(state, installationB, start, end, null));
            Assert.Equal(2, state.PendingRecords.Count);

            var nextWindowState = new GroupReportingState();
            Assert.True(aggregator.TryAdd(nextWindowState, new NormalizedErrorRecord { Installation = "Installation A", Key = "same-error" }, end, end.AddDays(1), null));
        }

        [Fact]
        public void ReportingState_RetainsPendingUntilSuccessfulDelivery()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var deliveredThrough = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

            try
            {
                var state = new DeduplicationState();
                state.Groups["sviluppo"] = new GroupReportingState { PendingThroughUtc = deliveredThrough.ToString("O") };
                state.Groups["sviluppo"].PendingRecords["pending"] = new NormalizedErrorRecord { Installation = "A", Key = "error" };
                state.PendingWarnings["group:new"] = "warning";
                var store = new DeduplicationStore(stateDirectory);
                store.Save(state);

                var afterFailedSend = store.Load();
                Assert.Single(afterFailedSend.Groups["sviluppo"].PendingRecords);
                Assert.Single(afterFailedSend.PendingWarnings);
                Assert.Null(afterFailedSend.Groups["sviluppo"].LastSuccessfulSendUtc);

                new ReportingDeliveryCoordinator().CompleteSuccessfulDelivery(afterFailedSend, deliveredThrough);

                Assert.Empty(afterFailedSend.Groups["sviluppo"].PendingRecords);
                Assert.Empty(afterFailedSend.PendingWarnings);
                Assert.Contains("group:new", afterFailedSend.NotifiedWarningKeys);
                Assert.Equal(deliveredThrough.ToString("O"), afterFailedSend.Groups["sviluppo"].LastSuccessfulSendUtc);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void ConfigurationWarningCollector_AddsOneWarningPerUnknownGroup()
        {
            var state = new DeduplicationState();
            var locations = new[]
            {
                new ServerLogLocation { Installation = "Test A", Group = "collaudo" },
                new ServerLogLocation { Installation = "Test B", Group = "Collaudo" }
            };
            var thresholds = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
            {
                ["sviluppo"] = DateTime.UtcNow
            };
            var collector = new ConfigurationWarningCollector();

            collector.Collect(locations, thresholds, state, null);
            collector.Collect(locations, thresholds, state, null);

            Assert.Single(state.PendingWarnings);
            Assert.Contains("Test A", state.PendingWarnings["group:collaudo"]);
            Assert.Contains("Test B", state.PendingWarnings["group:collaudo"]);
        }

        [Fact]
        public void DeduplicationStore_BacksUpPermanentDeduplicationSchema()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stateDirectory);

            try
            {
                File.WriteAllText(Path.Combine(stateDirectory, "deduplication-state.json"), "{\"Groups\":{\"sviluppo\":{\"ExportedKeys\":[\"old\"]}}}");
                var store = new DeduplicationStore(stateDirectory);

                var state = store.Load();

                Assert.Equal(2, state.SchemaVersion);
                Assert.Empty(state.Groups);
                Assert.True(File.Exists(Path.Combine(stateDirectory, "deduplication-state.json.legacy.bak")));
            }
            finally
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }

        [Fact]
        public void OutputWriter_RemoveGroups_PreservesOtherGroups()
        {
            var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var outputPath = Path.Combine(rootDirectory, "errors.ndjson");
            Directory.CreateDirectory(rootDirectory);

            try
            {
                var writer = new OutputWriter();
                writer.Append(outputPath, new[]
                {
                    new NormalizedErrorRecord { Group = "sviluppo", Key = "dev" },
                    new NormalizedErrorRecord { Group = "consulenza", Key = "consulting" }
                });

                writer.RemoveGroups(outputPath, new[] { "SVILUPPO" });

                var output = File.ReadAllText(outputPath);
                Assert.DoesNotContain("\"Key\":\"dev\"", output);
                Assert.Contains("\"Key\":\"consulting\"", output);
            }
            finally
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }

        [Fact]
        public void EmailBody_PlacesSviluppoBeforeOtherGroups()
        {
            var buildBody = typeof(EmailNotificationService).GetMethod("BuildBody", BindingFlags.Static | BindingFlags.NonPublic);
            var records = new[]
            {
                new NormalizedErrorRecord { Group = "consulenza", Installation = "Con-A", Key = "other" },
                new NormalizedErrorRecord { Group = "sviluppo", Installation = "Dev-A", Key = "dev" }
            };

            var body = (string)buildBody.Invoke(null, new object[] { records, Array.Empty<string>() });

            Assert.True(body.IndexOf("SVILUPPO", StringComparison.Ordinal) < body.IndexOf("CONSULENZA", StringComparison.Ordinal));
            Assert.Contains("SVILUPPO (1 errors)", body);
            Assert.Contains("CONSULENZA (1 errors)", body);
            Assert.Contains("Dev-A (1 errors)", body);
        }

        [Fact]
        public void EmailBody_CanContainConfigurationWarningsWithoutErrors()
        {
            var buildBody = typeof(EmailNotificationService).GetMethod("BuildBody", BindingFlags.Static | BindingFlags.NonPublic);

            var body = (string)buildBody.Invoke(null, new object[]
            {
                Array.Empty<NormalizedErrorRecord>(),
                new[] { "Discovered unconfigured group 'collaudo'." }
            });

            Assert.Contains("CONFIGURATION WARNINGS", body);
            Assert.Contains("WARNING: Discovered unconfigured group 'collaudo'.", body);
        }

        [Fact]
        public void CheckpointStore_Load_BacksUpLegacyFlatState()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stateDirectory);

            try
            {
                File.WriteAllText(Path.Combine(stateDirectory, "checkpoints.json"), "{\"a.log\":{\"Offset\":42}}");
                var store = new CheckpointStore(stateDirectory);

                var loaded = store.Load();

                Assert.Empty(loaded.Groups);
                Assert.True(File.Exists(Path.Combine(stateDirectory, "checkpoints.json.legacy.bak")));
            }
            finally
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }

        [Fact]
        public void Prepare_ResetsOffsetWhenFileShrinks()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, "12345");
                var manager = new CheckpointManager();
                var checkpoint = new FileCheckpoint { FilePath = path, Offset = 20, LastWriteTimeUtcTicks = DateTime.UtcNow.Ticks };

                var prepared = manager.Prepare(path, checkpoint, null);

                Assert.Equal(0, prepared.Offset);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void ShouldScan_ReturnsFalseWhenFileIsUnchanged()
        {
            var manager = new CheckpointManager();
            var checkpoint = new FileCheckpoint
            {
                FilePath = "a.log",
                Offset = 128,
                LastKnownSize = 128,
                LastWriteTimeUtcTicks = DateTime.UtcNow.Ticks
            };

            var shouldScan = manager.ShouldScan(checkpoint);

            Assert.False(shouldScan);
        }

        [Fact]
        public void ShouldScan_ReturnsTrueWhenFileHasUnreadContent()
        {
            var manager = new CheckpointManager();
            var checkpoint = new FileCheckpoint
            {
                FilePath = "a.log",
                Offset = 128,
                LastKnownSize = 256,
                LastWriteTimeUtcTicks = DateTime.UtcNow.Ticks
            };

            var shouldScan = manager.ShouldScan(checkpoint);

            Assert.True(shouldScan);
        }

        [Fact]
        public void ReadNewErrors_ReadsIncrementally()
        {
            var path = Path.GetTempFileName();
            try
            {
                var line = "{\"TS\":[2026,5,22,0,1,36,232],\"ServiceID\":\"7\",\"Channel\":\"SERVICE_ERROR\",\"ErrorCode\":22,\"ErrorMsg\":\"22 FILE NAME ERROR\"}";
                File.WriteAllLines(path, new[] { line });
                var reader = new NdjsonLogReader();
                var checkpoint = new FileCheckpoint { FilePath = path, Offset = 0 };

                var firstRead = reader.ReadNewErrors(path, "Installation A", "sviluppo", checkpoint, "SERVICE_ERROR", null);
                var secondRead = reader.ReadNewErrors(path, "Installation A", "sviluppo", checkpoint, "SERVICE_ERROR", null);

                Assert.Single(firstRead);
                Assert.Equal("Installation A", firstRead[0].Installation);
                Assert.Equal("sviluppo", firstRead[0].Group);
                Assert.Empty(secondRead);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void DeduplicationState_CanTrackPendingWindowRecords()
        {
            var state = new DeduplicationState();
            var record = new NormalizedErrorRecord { Key = "abc", ErrorMsg = "error" };
            state.Groups["sviluppo"] = new GroupReportingState();

            state.Groups["sviluppo"].PendingRecords[record.Key] = record;

            Assert.Single(state.Groups["sviluppo"].PendingRecords);
        }

        [Fact]
        public void RunDiagnostics_Info_AppendsMessageToRunLog()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var diagnosticsType = typeof(SourceLogEnumerator).Assembly.GetType("NdjsonErrorCollector.Services.RunDiagnostics", throwOnError: true);
                var diagnostics = Activator.CreateInstance(diagnosticsType, stateDirectory);
                var infoMethod = diagnosticsType.GetMethod("Info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                infoMethod.Invoke(diagnostics, new object[] { "Available log folder: \\server\\share\\DFS\\Logs" });

                var logPath = Path.Combine(stateDirectory, "run.log");
                var logContent = File.ReadAllText(logPath);
                Assert.Contains("Available log folder: \\server\\share\\DFS\\Logs", logContent);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void Enumerate_FiltersFilesByThresholdAndLogsAggregateCounts()
        {
            var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var logDirectory = Path.Combine(rootDirectory, "Logs");
            var stateDirectory = Path.Combine(rootDirectory, "state");
            var threshold = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

            Directory.CreateDirectory(logDirectory);

            try
            {
                var oldPath = Path.Combine(logDirectory, "old.log");
                var boundaryPath = Path.Combine(logDirectory, "boundary.log");
                var newPath = Path.Combine(logDirectory, "new.log");
                File.WriteAllText(oldPath, "{}");
                File.WriteAllText(boundaryPath, "{}");
                File.WriteAllText(newPath, "{}");
                File.SetLastWriteTimeUtc(oldPath, threshold.AddSeconds(-1));
                File.SetLastWriteTimeUtc(boundaryPath, threshold);
                File.SetLastWriteTimeUtc(newPath, threshold.AddSeconds(1));

                var diagnostics = new RunDiagnostics(stateDirectory);
                var enumerator = new SourceLogEnumerator();

                var result = enumerator.Enumerate(
                    new[]
                    {
                        new ServerLogLocation
                        {
                            Group = "sviluppo",
                            LogFolderPath = logDirectory
                        }
                    },
                    "*.log",
                    new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sviluppo"] = threshold
                    },
                    diagnostics);

                var logPath = Path.Combine(stateDirectory, "run.log");
                var logContent = File.ReadAllText(logPath);

                Assert.Equal(2, result.Count);
                Assert.DoesNotContain(result, file => file.FilePath == oldPath);
                Assert.Contains(result, file => file.FilePath == boundaryPath);
                Assert.Contains(result, file => file.FilePath == newPath);
                Assert.Contains($"Scanning log folder: {logDirectory}", logContent);
                Assert.Contains($"Found 3 log files in folder: {logDirectory}. Eligible: 2.", logContent);
                Assert.Contains("Skipped as older than", logContent);
            }
            finally
            {
                if (Directory.Exists(rootDirectory))
                {
                    Directory.Delete(rootDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void Enumerate_SkipsUnconfiguredGroupBeforeFolderAccess()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var diagnostics = new RunDiagnostics(stateDirectory);
                var result = new SourceLogEnumerator().Enumerate(
                    new[]
                    {
                        new ServerLogLocation
                        {
                            Group = "collaudo",
                            LogFolderPath = Path.Combine(stateDirectory, "missing")
                        }
                    },
                    "*.log",
                    new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["sviluppo"] = DateTime.UtcNow
                    },
                    diagnostics);

                var logContent = File.ReadAllText(Path.Combine(stateDirectory, "run.log"));
                Assert.Empty(result);
                Assert.Contains("Skipping log folder for unconfigured group collaudo", logContent);
                Assert.DoesNotContain("Log folder unavailable", logContent);
            }
            finally
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }

        [Fact]
        public void CheckpointPruner_RemovesOnlyPreThresholdEntries()
        {
            var threshold = new DateTime(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);
            var state = new CheckpointState();
            state.Groups["sviluppo"] = new GroupCheckpointState();
            state.Groups["sviluppo"].Files["old.log"] = new FileCheckpoint { LastWriteTimeUtcTicks = threshold.AddTicks(-1).Ticks };
            state.Groups["sviluppo"].Files["boundary.log"] = new FileCheckpoint { LastWriteTimeUtcTicks = threshold.Ticks };
            state.Groups["sviluppo"].Files["new.log"] = new FileCheckpoint { LastWriteTimeUtcTicks = threshold.AddTicks(1).Ticks };
            state.Groups["collaudo"] = new GroupCheckpointState();
            state.Groups["collaudo"].Files["unconfigured.log"] = new FileCheckpoint { LastWriteTimeUtcTicks = 0 };

            var removed = new CheckpointPruner().Prune(
                state,
                new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sviluppo"] = threshold
                },
                null);

            Assert.Equal(1, removed);
            Assert.DoesNotContain("old.log", state.Groups["sviluppo"].Files.Keys);
            Assert.Contains("boundary.log", state.Groups["sviluppo"].Files.Keys);
            Assert.Contains("new.log", state.Groups["sviluppo"].Files.Keys);
            Assert.Contains("unconfigured.log", state.Groups["collaudo"].Files.Keys);
        }

        [Fact]
        public void CheckpointStore_SaveAndLoad_PersistsEntries()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var store = new CheckpointStore(stateDirectory);
                var checkpoints = new CheckpointState();
                checkpoints.Groups["sviluppo"] = new GroupCheckpointState
                {
                    AppliedThresholdUtc = "2026-03-15T14:30:00.0000000Z"
                };
                checkpoints.Groups["sviluppo"].Files["a.log"] = new FileCheckpoint
                    {
                        FilePath = "a.log",
                        Offset = 42,
                        LastKnownSize = 42,
                        LastWriteTimeUtcTicks = 123456789
                    };

                store.Save(checkpoints);
                var loaded = store.Load();

                Assert.True(loaded.Groups["SVILUPPO"].Files.ContainsKey("a.log"));
                Assert.Equal(42, loaded.Groups["sviluppo"].Files["a.log"].Offset);
                Assert.Equal(42, loaded.Groups["sviluppo"].Files["a.log"].LastKnownSize);
                Assert.Equal(123456789, loaded.Groups["sviluppo"].Files["a.log"].LastWriteTimeUtcTicks);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void CheckpointStore_Load_ReturnsEmptyWhenFileIsBlank()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(stateDirectory);
                File.WriteAllText(Path.Combine(stateDirectory, "checkpoints.json"), string.Empty);
                var store = new CheckpointStore(stateDirectory);

                var loaded = store.Load();

                Assert.Empty(loaded.Groups);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void DeduplicationStore_Load_ReturnsEmptyStateWhenFileIsBlank()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                Directory.CreateDirectory(stateDirectory);
                File.WriteAllText(Path.Combine(stateDirectory, "deduplication-state.json"), string.Empty);
                var store = new DeduplicationStore(stateDirectory);

                var loaded = store.Load();

                Assert.Empty(loaded.Groups);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void FileReadRetryHandler_ReturnsResultAfterRetry()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var diagnostics = new RunDiagnostics(stateDirectory);
                var handler = new FileReadRetryHandler(maxAttempts: 3, delayMilliseconds: 0);
                var attempts = 0;

                var result = handler.Execute(
                    "a.log",
                    () =>
                    {
                        attempts++;
                        if (attempts == 1)
                        {
                            throw new IOException("temporary network error");
                        }

                        return new[] { new ParsedLogRecord() };
                    },
                    diagnostics);

                Assert.Single(result);
                Assert.Equal(2, attempts);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void FileReadRetryHandler_ReturnsEmptyAfterMaxRetries()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var diagnostics = new RunDiagnostics(stateDirectory);
                var handler = new FileReadRetryHandler(maxAttempts: 3, delayMilliseconds: 0);
                var attempts = 0;

                var result = handler.Execute(
                    "a.log",
                    () =>
                    {
                        attempts++;
                        throw new IOException("persistent network error");
                    },
                    diagnostics);

                var logContent = File.ReadAllText(Path.Combine(stateDirectory, "run.log"));
                Assert.Empty(result);
                Assert.Equal(3, attempts);
                Assert.Contains("Skipping file.", logContent);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void FileReadRetryHandler_ReturnsEmptyWhenAccessIsDenied()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var diagnostics = new RunDiagnostics(stateDirectory);
                var handler = new FileReadRetryHandler(maxAttempts: 3, delayMilliseconds: 0);
                var attempts = 0;

                var result = handler.Execute(
                    "a.log",
                    () =>
                    {
                        attempts++;
                        throw new UnauthorizedAccessException("Access to the path is denied.");
                    },
                    diagnostics);

                var logContent = File.ReadAllText(Path.Combine(stateDirectory, "run.log"));
                Assert.Empty(result);
                Assert.Equal(1, attempts);
                Assert.Contains("Access denied reading a.log", logContent);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void RunDiagnostics_Error_AppendsExceptionContextToRunLog()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var diagnostics = new RunDiagnostics(stateDirectory);
                diagnostics.Error("Collector run failed (System.UnauthorizedAccessException): Access to the path is denied. Last file: \\server\\share\\Logs\\1.log. Folder: \\server\\share\\Logs.");

                var logContent = File.ReadAllText(Path.Combine(stateDirectory, "run.log"));
                Assert.Contains("System.UnauthorizedAccessException", logContent);
                Assert.Contains("Last file: \\server\\share\\Logs\\1.log", logContent);
                Assert.Contains("Folder: \\server\\share\\Logs", logContent);
            }
            finally
            {
                if (Directory.Exists(stateDirectory))
                {
                    Directory.Delete(stateDirectory, recursive: true);
                }
            }
        }

        [Fact]
        public void EmailOptions_CanStoreSmtpConfigurationArrays()
        {
            var options = new EmailOptions
            {
                Host = "mailrelay.scdom.net",
                Port = 25,
                FromAddress = "giacomo.manfredi@simcorp.com",
                To = new[] { "gmmd@simcorp.com" },
                Cc = new[] { "snlh@simcorp.com", "mszc@simcorp.com" },
                UseSsl = false,
                SubjectPrefix = "[NdjsonErrorCollector]"
            };

            Assert.Equal("mailrelay.scdom.net", options.Host);
            Assert.Equal(25, options.Port);
            Assert.Equal("giacomo.manfredi@simcorp.com", options.FromAddress);
            Assert.Single(options.To);
            Assert.Equal(2, options.Cc.Length);
            Assert.False(options.UseSsl);
        }

        [Fact]
        public void AppSettings_IsPublishedUnderConfigurationFolder()
        {
            var projectFile = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NdjsonErrorCollector", "NdjsonErrorCollector.csproj"));

            Assert.Contains("<TargetPath>configuration\\appsettings.json</TargetPath>", projectFile);
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _responseBody;

            public StubHttpMessageHandler(string responseBody)
            {
                _responseBody = responseBody;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
