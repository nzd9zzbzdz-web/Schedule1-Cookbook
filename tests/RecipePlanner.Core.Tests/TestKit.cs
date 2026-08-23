using System;
using RecipePlanner.Core.Identity;
using RecipePlanner.Core.Production;

namespace RecipePlanner.Core.Tests
{
    public sealed class FakeLoadState : IGameLoadState
    {
        public bool IsGameLoaded { get; set; } = true;
        public double SecondsSinceLoadComplete { get; set; } = 999;

        public static FakeLoadState Ready() => new FakeLoadState();
        public static FakeLoadState JustLoaded() => new FakeLoadState { SecondsSinceLoadComplete = 0.2 };
        public static FakeLoadState NotLoaded() => new FakeLoadState { IsGameLoaded = false };
    }

    /// <summary>Builders that keep the tests about behaviour rather than object construction.</summary>
    public static class TestKit
    {
        public const string SteamId = "76561190000000001";
        public static readonly DateTime Created = new DateTime(2026, 4, 11, 14, 26, 51, DateTimeKind.Unspecified);

        public static SaveIdentity Identity(
            string steam = SteamId, string org = "Echo", int seed = 157034955, DateTime? created = null) =>
            new SaveIdentity(steam, org, created ?? Created, seed);

        public static PlayerContext Context(SaveIdentity id = null, int slot = 1)
        {
            var identity = id ?? Identity();
            var ctx = PlayerContext.From(identity, slot, $"…/SaveGame_{slot}");
            ctx.GameVersion = "0.4.5f2";
            return ctx;
        }

        public static ProductionCandidate Mix(
            string station = "station-a",
            string @base = "greencrack",
            string ingredient = "mouthwash",
            string output = "bluelightning",
            int quantity = 20,
            int day = 40,
            int time = 924,
            bool local = true,
            bool npc = false,
            ProductionKind kind = ProductionKind.Mixed)
        {
            return new ProductionCandidate
            {
                Kind = kind,
                StationGuid = station,
                StationType = "MixingStation",
                StationItemId = "mixingstationmk2",
                IsLocalPlayerUser = local,
                HasNpcUser = npc,
                BaseProductId = @base,
                IngredientId = ingredient,
                OutputProductId = output,
                OutputProductName = "Blue Lightning",
                DrugType = "Marijuana",
                Quality = "Premium",
                Quantity = quantity,
                ElapsedDays = day,
                TimeOfDay = time
            };
        }

        public static ProductionTracker Tracker(
            IGameLoadState load = null,
            ISeenEventKeys seen = null,
            DateTime? now = null)
        {
            return new ProductionTracker(
                load ?? FakeLoadState.Ready(),
                seen ?? new InMemorySeenEventKeys(),
                null,
                now.HasValue ? (Func<DateTime>)(() => now.Value) : null);
        }
    }
}
