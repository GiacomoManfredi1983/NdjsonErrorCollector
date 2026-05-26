using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

                var firstRead = reader.ReadNewErrors(path, checkpoint, "SERVICE_ERROR", null);
                var secondRead = reader.ReadNewErrors(path, checkpoint, "SERVICE_ERROR", null);

                Assert.Single(firstRead);
                Assert.Empty(secondRead);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void DeduplicationState_CanTrackPendingNotifications()
        {
            var state = new DeduplicationState();
            var record = new NormalizedErrorRecord { Key = "abc", ErrorMsg = "error" };

            state.PendingNotifications[record.Key] = record;

            Assert.Single(state.PendingNotifications);
            Assert.DoesNotContain(record.Key, state.NotifiedKeys);
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
        public void Enumerate_LogsScanningAndFoundFileCount()
        {
            var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            var logDirectory = Path.Combine(rootDirectory, "Logs");
            var stateDirectory = Path.Combine(rootDirectory, "state");

            Directory.CreateDirectory(logDirectory);

            try
            {
                File.WriteAllText(Path.Combine(logDirectory, "a.log"), "{}");
                File.WriteAllText(Path.Combine(logDirectory, "b.log"), "{}");

                var diagnosticsType = typeof(SourceLogEnumerator).Assembly.GetType("NdjsonErrorCollector.Services.RunDiagnostics", throwOnError: true);
                var diagnostics = Activator.CreateInstance(diagnosticsType, stateDirectory);
                var enumerator = new SourceLogEnumerator();

                var result = enumerator.Enumerate(
                    new[]
                    {
                        new ServerLogLocation
                        {
                            LogFolderPath = logDirectory
                        }
                    },
                    "*.log",
                    (RunDiagnostics)diagnostics);

                var logPath = Path.Combine(stateDirectory, "run.log");
                var logContent = File.ReadAllText(logPath);

                Assert.Equal(2, result.Count);
                Assert.Contains($"Scanning log folder: {logDirectory}", logContent);
                Assert.Contains($"Found 2 log files in folder: {logDirectory}", logContent);
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
        public void CheckpointStore_SaveAndLoad_PersistsEntries()
        {
            var stateDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

            try
            {
                var store = new CheckpointStore(stateDirectory);
                var checkpoints = new Dictionary<string, FileCheckpoint>
                {
                    ["a.log"] = new FileCheckpoint
                    {
                        FilePath = "a.log",
                        Offset = 42,
                        LastKnownSize = 42,
                        LastWriteTimeUtcTicks = 123456789
                    }
                };

                store.Save(checkpoints);
                var loaded = store.Load();

                Assert.True(loaded.ContainsKey("a.log"));
                Assert.Equal(42, loaded["a.log"].Offset);
                Assert.Equal(42, loaded["a.log"].LastKnownSize);
                Assert.Equal(123456789, loaded["a.log"].LastWriteTimeUtcTicks);
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

                Assert.Empty(loaded);
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

                Assert.Empty(loaded.ExportedKeys);
                Assert.Empty(loaded.NotifiedKeys);
                Assert.Empty(loaded.PendingNotifications);
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
    }
}
