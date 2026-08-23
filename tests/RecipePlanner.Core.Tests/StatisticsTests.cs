using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Pricing;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Stats;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    public class StatisticsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);

        private sealed class FixedPrices : IPriceSource
        {
            public bool TryGetProductValue(string productId, string quality, out double unitValue)
            { unitValue = 31d; return true; }

            public bool TryGetIngredientCost(string ingredientId, out double unitCost)
            { unitCost = 12d; return true; }
        }

        private static List<ProductionEvent> Sample()
        {
            var tracker = new ProductionTracker(
                FakeLoadState.Ready(),
                new InMemorySeenEventKeys(),
                new PricingEngine(new FixedPrices()),
                () => T0);

            var ctx = TestKit.Context();
            var events = new List<ProductionEvent>();
            tracker.ProductionRecorded += events.Add;

            tracker.Track(TestKit.Mix(station: "a", time: 100, quantity: 20), ctx);
            tracker.Track(TestKit.Mix(station: "b", time: 200, quantity: 10), ctx);
            tracker.Track(TestKit.Mix(station: "c", time: 300, quantity: 5,
                @base: "meth", ingredient: "battery", output: "methx"), ctx);

            // Noise that must never reach personal totals.
            tracker.Track(TestKit.Mix(station: "d", time: 400, quantity: 99, local: false, npc: true), ctx);
            tracker.Track(TestKit.Mix(station: "e", time: 500, quantity: 77, kind: ProductionKind.Packaged), ctx);

            return events;
        }

        [Fact]
        public void Personal_totals_exclude_employees_and_transforms()
        {
            var stats = StatisticsService.Build(TestKit.Context().ProfileId, Sample(), T0);

            Assert.Equal(35, stats.Personal.UnitsProduced);   // 20 + 10 + 5, not 99 or 77
            Assert.Equal(3, stats.Personal.Batches);
            Assert.Equal(5, stats.EventsFolded);
            Assert.Equal(2, stats.Excluded.Batches);
        }

        [Fact]
        public void Exclusions_record_why_they_were_excluded()
        {
            var stats = StatisticsService.Build(TestKit.Context().ProfileId, Sample(), T0);

            Assert.Equal(1, stats.ExcludedByReason["attribution:Employee"]);
            Assert.Equal(1, stats.ExcludedByReason["kind:Packaged"]);
        }

        [Fact]
        public void Money_is_folded_from_the_pricing_engine()
        {
            var stats = StatisticsService.Build(TestKit.Context().ProfileId, Sample(), T0);

            // 35 units * $31 value, 35 units * $12 cost (one ingredient each).
            Assert.Equal(35 * 31d, stats.Personal.TotalValue);
            Assert.Equal(35 * 12d, stats.Personal.TotalCost);
            Assert.Equal(35 * 19d, stats.Personal.EstimatedProfit);
        }

        [Fact]
        public void Breakdowns_are_produced_per_product_ingredient_and_recipe()
        {
            var stats = StatisticsService.Build(TestKit.Context().ProfileId, Sample(), T0);

            Assert.Equal(30, stats.ByProduct["bluelightning"].Units);
            Assert.Equal(5, stats.ByProduct["methx"].Units);

            Assert.Equal(30, stats.ByIngredient["mouthwash"].UnitsConsumed);
            Assert.Equal(2, stats.ByIngredient["mouthwash"].TimesUsed);

            Assert.Equal(2, stats.UniqueRecipesProduced);
            Assert.Equal(2, stats.ByRecipe["greencrack>mouthwash"].TimesCooked);
        }

        [Fact]
        public void Records_identify_the_leaders()
        {
            var stats = StatisticsService.Build(TestKit.Context().ProfileId, Sample(), T0);

            Assert.Equal("greencrack>mouthwash", stats.Records.MostUsedRecipeId);
            Assert.Equal("bluelightning", stats.Records.MostProducedProductId);
            Assert.Equal("mouthwash", stats.Records.MostUsedIngredientId);
            Assert.Equal(20, stats.Records.LargestBatchUnits);
            Assert.Equal("bluelightning", stats.Records.LargestBatchProductId);
        }

        [Fact]
        public void Largest_batch_ignores_excluded_events()
        {
            // The 99-unit employee batch and 77-unit repackage are both bigger than anything the
            // player personally made.
            var stats = StatisticsService.Build(TestKit.Context().ProfileId, Sample(), T0);
            Assert.Equal(20, stats.Records.LargestBatchUnits);
        }

        [Fact]
        public void Another_characters_events_are_never_folded_in()
        {
            var mine = TestKit.Context(TestKit.Identity(org: "Echo"));
            var theirs = TestKit.Context(TestKit.Identity(org: "Delta"));

            var events = Sample();
            foreach (var e in events.Take(2)) e.ProfileId = theirs.ProfileId;

            var stats = StatisticsService.Build(mine.ProfileId, events, T0);

            Assert.Equal(1, stats.Personal.Batches);  // only the third local batch survives
            Assert.Equal(5, stats.Personal.UnitsProduced);
        }

        [Fact]
        public void The_fold_is_deterministic()
        {
            var events = Sample();
            var a = StatisticsService.Build(TestKit.Context().ProfileId, events, T0);
            var b = StatisticsService.Build(TestKit.Context().ProfileId, events, T0);

            Assert.Equal(a.Personal.UnitsProduced, b.Personal.UnitsProduced);
            Assert.Equal(a.Records.MostUsedRecipeId, b.Records.MostUsedRecipeId);
            Assert.Equal(a.Records.MostUsedIngredientId, b.Records.MostUsedIngredientId);
        }

        [Fact]
        public void Empty_history_produces_empty_stats_not_a_crash()
        {
            var stats = StatisticsService.Build("abc", new List<ProductionEvent>(), T0);

            Assert.Equal(0, stats.Personal.UnitsProduced);
            Assert.Equal(0, stats.UniqueRecipesProduced);
            Assert.Null(stats.Records.MostUsedRecipeId);
        }
    }
}
