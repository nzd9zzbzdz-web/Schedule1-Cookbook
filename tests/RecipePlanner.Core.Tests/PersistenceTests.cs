using System;
using System.IO;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Stats;
using RecipePlanner.Core.Storage;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Phase 10 exit test: events survive a restart, and replaying the log reproduces identical
    /// totals. Uses a real temp directory — this is I/O behaviour, so mocking it would prove nothing.
    /// </summary>
    public class PersistenceTests : IDisposable
    {
        private readonly string _root;
        private readonly StorageLayout _layout;
        private readonly string _profile;

        public PersistenceTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "rp-tests-" + Guid.NewGuid().ToString("N"));
            _layout = new StorageLayout(_root);
            _profile = TestKit.Context().ProfileId;
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private ProductionEvent Record(string station, int qty)
        {
            var tracker = TestKit.Tracker();
            ProductionEvent captured = null;
            tracker.ProductionRecorded += e => captured = e;
            tracker.Track(TestKit.Mix(station: station, quantity: qty, time: qty), TestKit.Context());
            return captured;
        }

        [Fact]
        public void Events_survive_closing_and_reopening_the_game()
        {
            var repo = new ProductionHistoryRepository(_layout, _profile);
            repo.Append(Record("a", 20));
            repo.Append(Record("b", 10));

            // Simulate a restart: brand new repository object over the same files.
            var reopened = new ProductionHistoryRepository(_layout, _profile);
            var loaded = reopened.ReadAll();

            Assert.Equal(2, loaded.Count);
            Assert.Equal(30, loaded.Sum(e => e.Quantity));
        }

        [Fact]
        public void Replaying_the_log_reproduces_identical_totals()
        {
            var repo = new ProductionHistoryRepository(_layout, _profile);
            var live = new[] { Record("a", 20), Record("b", 10), Record("c", 5) };
            foreach (var e in live) repo.Append(e);

            var fromDisk = repo.ReadAll();
            var now = DateTime.UtcNow;

            var liveStats = StatisticsService.Build(_profile, live, now);
            var replayed = StatisticsService.Build(_profile, fromDisk, now);

            Assert.Equal(liveStats.Personal.UnitsProduced, replayed.Personal.UnitsProduced);
            Assert.Equal(liveStats.Personal.Batches, replayed.Personal.Batches);
            Assert.Equal(liveStats.UniqueRecipesProduced, replayed.UniqueRecipesProduced);
        }

        [Fact]
        public void A_torn_final_line_costs_one_event_not_the_whole_history()
        {
            var repo = new ProductionHistoryRepository(_layout, _profile);
            repo.Append(Record("a", 20));
            repo.Append(Record("b", 10));

            // Crash mid-append: a partial JSON object at the end of the file.
            File.AppendAllText(repo.Path, "{\"EventKey\":\"c|x+y|d1-1\",\"Quan");

            var loaded = repo.ReadAll(out var corrupt);

            Assert.Equal(2, loaded.Count);
            Assert.Equal(1, corrupt);
        }

        [Fact]
        public void Deleting_stats_json_is_safe_because_it_rebuilds()
        {
            var repo = new ProductionHistoryRepository(_layout, _profile);
            foreach (var e in new[] { Record("a", 20), Record("b", 10) }) repo.Append(e);

            var store = new StatsStore(_layout);
            store.Save(StatisticsService.Build(_profile, repo.ReadAll(), DateTime.UtcNow));
            Assert.NotNull(store.Load(_profile));

            File.Delete(_layout.StatsFile(_profile));
            Assert.Null(store.Load(_profile));

            var rebuilt = StatisticsService.Build(_profile, repo.ReadAll(), DateTime.UtcNow);
            Assert.Equal(30, rebuilt.Personal.UnitsProduced);
        }

        [Fact]
        public void Profiles_are_stored_separately_and_never_bleed()
        {
            var echo = TestKit.Context(TestKit.Identity(org: "Echo"));
            var delta = TestKit.Context(TestKit.Identity(org: "Delta"));

            new ProductionHistoryRepository(_layout, echo.ProfileId).Append(Record("a", 20));
            new ProductionHistoryRepository(_layout, delta.ProfileId).Append(Record("b", 999));

            var echoEvents = new ProductionHistoryRepository(_layout, echo.ProfileId).ReadAll();

            Assert.Single(echoEvents);
            Assert.Equal(20, echoEvents[0].Quantity);
            Assert.Equal(2, _layout.ListProfileIds().Count());
        }

        [Fact]
        public void We_never_write_inside_the_games_save_tree()
        {
            var layout = new StorageLayout();
            Assert.DoesNotContain("LocalLow", layout.Root, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Schedule I", layout.Root, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(StorageLayout.AppFolderName, layout.Root);
        }

        [Fact]
        public void Profile_record_round_trips_through_disk()
        {
            var store = new ProfileStore(_layout);
            var ctx = TestKit.Context();
            var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

            store.Observe(ctx, now);
            var loaded = store.Load(ctx.ProfileId);

            Assert.Equal(ctx.ProfileId, loaded.ProfileId);
            Assert.Equal("Echo", loaded.Identity.OrganisationName);
            Assert.Equal(157034955, loaded.Identity.Seed);
            // The plaintext components must survive so the key can be recomputed or migrated.
            Assert.Equal("2026-04-11T14:26:51", loaded.Identity.CreationDateIso);
        }

        [Fact]
        public void Recipes_round_trip_through_disk()
        {
            var repo = new FileRecipeRepository(_layout, _profile);
            var discovery = new RecipeDiscoveryService(repo);
            discovery.OnProduced(Record("a", 20));

            var reopened = new FileRecipeRepository(_layout, _profile);
            var recipe = reopened.All().Single();

            Assert.Equal("greencrack>mouthwash", recipe.RecipeId);
            Assert.True(recipe.Has(RecipeStatus.Produced));
            Assert.True(recipe.Has(RecipeStatus.Discovered));
        }
    }
}
