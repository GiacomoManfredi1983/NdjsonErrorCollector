using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    }
}
