using System;
using System.IO;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Stats;
using RecipePlanner.Core.Storage;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// The event log is append-only except for one case: batches of a mix recorded before the
    /// player named it gain their product identity retroactively.
    /// </summary>
    public class HistoryRewriteTests : IDisposable
    {
        private readonly string _root;
        private readonly StorageLayout _layout;
        private readonly ProductionHistoryRepository _repo;
        private const string Profile = "0123456789abcdef0123456789abcdef";

        public HistoryRewriteTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "rp-rw-" + Guid.NewGuid().ToString("N"));
            _layout = new StorageLayout(_root);
            _repo = new ProductionHistoryRepository(_layout, Profile);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private ProductionEvent Unnamed(string @base, string ingredient, int time)
        {
            var evt = new ProductionEvent
            {
                EventKey = $"s|{@base}+{ingredient}|d40-{time}",
                Kind = ProductionKind.Mixed,
                Attribution = Attribution.Local,
                ProfileId = Profile,
                BaseProductId = @base,
                IngredientId = ingredient,
                OutputProductId = null,
                Quantity = 20,
                ElapsedDays = 40,
                TimeOfDay = time,
                RealTimeUtc = DateTime.UtcNow
            };
            evt.RecipeId = evt.ComputeRecipeId();
            return evt;
        }

        [Fact]
        public void Naming_a_mix_repairs_every_earlier_batch_on_disk()
        {
            _repo.Append(Unnamed("megasmegma", "banana", 900));
            _repo.Append(Unnamed("megasmegma", "banana", 1000));
            _repo.Append(Unnamed("strawberrypunch", "megabean", 1100));

            var events = _repo.ReadAll();
            var applied = PendingNameResolver.Apply(events, "megasmegma", "banana", "purplehaze", "Purple Haze");
            _repo.Rewrite(events);

            Assert.Equal(2, applied);

            var reloaded = _repo.ReadAll();
            Assert.Equal(3, reloaded.Count);
            Assert.Equal(2, reloaded.Count(e => e.OutputProductId == "purplehaze"));
            Assert.Single(reloaded.Where(e => e.IsAwaitingName));   // the unrelated one is untouched
        }

        [Fact]
        public void Statistics_reflect_the_repair_after_a_reload()
        {
            _repo.Append(Unnamed("megasmegma", "banana", 900));
            _repo.Append(Unnamed("megasmegma", "banana", 1000));

            var before = StatisticsService.Build(Profile, _repo.ReadAll(), DateTime.UtcNow);
            Assert.False(before.ByProduct.ContainsKey("purplehaze"));

            var events = _repo.ReadAll();
            PendingNameResolver.Apply(events, "megasmegma", "banana", "purplehaze", "Purple Haze");
            _repo.Rewrite(events);

            var after = StatisticsService.Build(Profile, _repo.ReadAll(), DateTime.UtcNow);
            Assert.Equal(40, after.ByProduct["purplehaze"].Units);
            Assert.Equal(40, after.Personal.UnitsProduced);   // total is unchanged by naming
        }

        [Fact]
        public void Rewrite_preserves_every_field_not_being_repaired()
        {
            var original = Unnamed("megasmegma", "banana", 900);
            original.Quality = "Heavenly";
            original.Effects.Add("Spicy");
            original.ConsoleEnabled = true;
            _repo.Append(original);

            var events = _repo.ReadAll();
            PendingNameResolver.Apply(events, "megasmegma", "banana", "purplehaze", "Purple Haze");
            _repo.Rewrite(events);

            var reloaded = _repo.ReadAll().Single();
            Assert.Equal("Heavenly", reloaded.Quality);
            Assert.Equal(new[] { "Spicy" }, reloaded.Effects);
            Assert.True(reloaded.ConsoleEnabled);
            Assert.Equal(original.EventKey, reloaded.EventKey);
        }

        [Fact]
        public void Appending_after_a_rewrite_still_works()
        {
            _repo.Append(Unnamed("a", "b", 900));

            var events = _repo.ReadAll();
            PendingNameResolver.Apply(events, "a", "b", "x", "X");
            _repo.Rewrite(events);

            _repo.Append(Unnamed("c", "d", 1000));

            Assert.Equal(2, _repo.ReadAll().Count);
        }

        [Fact]
        public void Rewriting_an_empty_log_is_harmless()
        {
            _repo.Rewrite(new ProductionEvent[0]);
            Assert.Empty(_repo.ReadAll());
        }
    }
}
