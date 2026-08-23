using System;
using System.Collections.Generic;
using System.IO;
using RecipePlanner.Core.Identity;
using RecipePlanner.Core.Production;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Covers the one operation that removes history: discarding production the player quit
    /// without saving.
    ///
    /// The scenario these guard came from a live session on 0.4.6f13 — a save written at day 40
    /// 13:55, four batches recorded up to day 40 15:14, then a reload. The replayed batches
    /// collided with the abandoned ones' idempotency keys, so the real cooks were rejected as
    /// duplicates while the phantom ones survived.
    /// </summary>
    public class GameClockTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(900, 540)]
        [InlineData(1507, 907)]
        [InlineData(1355, 835)]
        [InlineData(2359, 1439)]
        public void UnpacksPackedDecimalTime(int packed, int expectedMinutes)
        {
            Assert.Equal(expectedMinutes, GameClock.MinutesOfDay(packed));
        }

        [Fact]
        public void OrdinalOrdersAcrossAnHourBoundary()
        {
            // 14:59 and 15:00 are one minute apart, but 1459 and 1500 are 41 apart as raw integers.
            // Anything comparing the packed value directly gets this right by accident; the gap is
            // what breaks it.
            Assert.Equal(1, GameClock.Ordinal(40, 1500) - GameClock.Ordinal(40, 1459));
        }

        [Fact]
        public void OrdinalOrdersAcrossMidnight()
        {
            // TimeManager increments ElapsedDays as 23:59 rolls to 00:00, so the ordinal keeps
            // rising even though TimeOfDay drops to zero.
            Assert.True(GameClock.Ordinal(41, 0) > GameClock.Ordinal(40, 2359));
        }
    }

    public class RollbackReconcilerTests
    {
        private static ProductionEvent At(int day, int time, int quantity = 20, string product = "megasmegma")
        {
            return new ProductionEvent
            {
                EventKey = $"station|shroom+chili|d{day}-{time}",
                ProfileId = "p",
                StationGuid = "station",
                BaseProductId = "shroom",
                IngredientId = "chili",
                OutputProductId = product,
                Quantity = quantity,
                ElapsedDays = day,
                TimeOfDay = time
            };
        }

        [Fact]
        public void KeepsEverythingWhenNothingPostdatesTheSave()
        {
            var events = new List<ProductionEvent> { At(40, 1200), At(40, 1300), At(40, 1355) };

            var result = RollbackReconciler.Apply(events, 40, 1355);

            Assert.False(result.Changed);
            Assert.Equal(3, result.Kept.Count);
            Assert.Empty(result.RolledBack);
        }

        [Fact]
        public void DropsProductionRecordedAfterTheSaveWasWritten()
        {
            // The live case: saved at 13:55, played on to 15:14, quit without saving.
            var events = new List<ProductionEvent>
            {
                At(40, 1200), At(40, 1502), At(40, 1507), At(40, 1509), At(40, 1514)
            };

            var result = RollbackReconciler.Apply(events, 40, 1355);

            Assert.True(result.Changed);
            Assert.Single(result.Kept);
            Assert.Equal(4, result.RolledBack.Count);
            Assert.Equal(80, result.Units);
        }

        [Fact]
        public void KeepsAnEventLandingExactlyOnTheSaveMinute()
        {
            // Ambiguous by construction. Keeping a real batch beats deleting one: a duplicate is
            // visible in the history, a vanished cook is not.
            var result = RollbackReconciler.Apply(new[] { At(40, 1355) }, 40, 1355);

            Assert.False(result.Changed);
            Assert.Single(result.Kept);
        }

        [Fact]
        public void ComparesByOrdinalNotPackedTime()
        {
            // 14:59 postdates a save at 14:20 even though a naive digit comparison of 1459 vs 1420
            // happens to agree — this pins the case where it does not: 09:70 is not a valid time,
            // so use the boundary that actually differs, an event on a later day at an earlier
            // clock time.
            var result = RollbackReconciler.Apply(new[] { At(41, 100) }, 40, 2300);

            Assert.True(result.Changed);
            Assert.Single(result.RolledBack);
        }

        [Fact]
        public void SurvivesNullsAndAnEmptyLog()
        {
            Assert.False(RollbackReconciler.Apply(null, 40, 1355).Changed);

            var result = RollbackReconciler.Apply(new ProductionEvent[] { null, At(40, 1500) }, 40, 1355);
            Assert.Single(result.RolledBack);
            Assert.Empty(result.Kept);
        }

        [Fact]
        public void SummaryNamesTheNewestDiscardedBatch()
        {
            var result = RollbackReconciler.Apply(
                new[] { At(40, 1502), At(40, 1514, 20, "extremeassblaster"), At(40, 1507) }, 40, 1355);

            var summary = RollbackReconciler.Summarise(result, 40, 1355);

            Assert.Contains("extremeassblaster", summary);
            Assert.Contains("day 40, 15:14", summary);
            Assert.Contains("day 40, 13:55", summary);
        }

        [Fact]
        public void SummaryIsNullWhenNothingChanged()
        {
            var result = RollbackReconciler.Apply(new[] { At(40, 1200) }, 40, 1355);
            Assert.Null(RollbackReconciler.Summarise(result, 40, 1355));
        }
    }

    public class SaveClockReaderTests : IDisposable
    {
        private readonly string _root;

        public SaveClockReaderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "rp-clock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private string Write(string json)
        {
            File.WriteAllText(Path.Combine(_root, "Time.json"), json);
            return _root;
        }

        [Fact]
        public void ReadsTheShippedTimeDataShape()
        {
            // Verbatim from a real SaveGame_1\Time.json on 0.4.6f13.
            var path = Write(@"{
    ""DataType"": ""TimeData"",
    ""DataVersion"": 0,
    ""GameVersion"": ""0.4.6f13"",
    ""TimeOfDay"": 1355,
    ""ElapsedDays"": 40,
    ""Playtime"": 60656
}");

            var clock = SaveClockReader.Read(path);

            Assert.True(clock.IsUsable);
            Assert.Equal(40, clock.ElapsedDays);
            Assert.Equal(1355, clock.TimeOfDay);
        }

        [Fact]
        public void MissingFileIsNotUsableButIsNotFatal()
        {
            var clock = SaveClockReader.Read(_root);
            Assert.False(clock.IsUsable);
            Assert.Contains("Time.json", clock.Error);
        }

        [Fact]
        public void NullPathIsHandled()
        {
            Assert.False(SaveClockReader.Read(null).IsUsable);
        }

        [Fact]
        public void CorruptJsonIsRejectedRatherThanGuessed()
        {
            Assert.False(SaveClockReader.Read(Write("{ not json")).IsUsable);
        }

        [Theory]
        [InlineData(@"{""ElapsedDays"": 40}")]
        [InlineData(@"{""TimeOfDay"": 1355}")]
        [InlineData(@"{""ElapsedDays"": -1, ""TimeOfDay"": 1355}")]
        [InlineData(@"{""ElapsedDays"": 40, ""TimeOfDay"": 2400}")]
        [InlineData(@"{""ElapsedDays"": 40, ""TimeOfDay"": 1370}")]
        public void OutOfRangeOrIncompleteClocksAreUnusable(string json)
        {
            // An unusable clock means "reconcile nothing". Acting on a misread would delete real
            // production, so every doubtful shape has to fail closed.
            Assert.False(SaveClockReader.Read(Write(json)).IsUsable);
        }
    }
}
