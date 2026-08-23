using System.Collections.Generic;
using RecipePlanner.Core.Pricing;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Stats;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// <c>IngredientStat.TotalCost</c> was declared, rendered whenever non-zero, and never
    /// populated — so the report's ingredient cost column silently never appeared. Confirmed live:
    /// a save showing "$420.00" in total cost had every per-ingredient cost at zero.
    ///
    /// It could not simply be computed in the fold. <c>UnitCost</c> is the SUM across the chain, so
    /// the event log alone can say a batch cost $12 but not how that split, and
    /// <c>StatisticsService</c> is deliberately a pure fold with no price source — a property worth
    /// keeping, because it is what makes every statistic rebuildable.
    ///
    /// So the split is recorded at pricing time, where it is briefly known.
    /// </summary>
    public class IngredientCostTests
    {
        private sealed class Prices : IPriceSource
        {
            public readonly Dictionary<string, double> Costs =
                new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);

            public bool TryGetProductValue(string productId, string quality, out double unitValue)
            {
                unitValue = 100d;
                return true;
            }

            public bool TryGetIngredientCost(string ingredientId, out double unitCost) =>
                Costs.TryGetValue(ingredientId ?? "", out unitCost);
        }

        private static ProductionEvent Batch(int quantity, params string[] chain) =>
            new ProductionEvent
            {
                ProfileId = "p",
                Kind = ProductionKind.Mixed,
                Attribution = Attribution.Local,
                OutputProductId = "output",
                BaseProductId = "base",
                IngredientId = chain.Length > 0 ? chain[0] : null,
                IngredientChain = new List<string>(chain),
                Quantity = quantity,
            };

        [Fact]
        public void Pricing_records_what_each_ingredient_contributed()
        {
            var prices = new Prices();
            prices.Costs["chili"] = 3d;
            prices.Costs["banana"] = 5d;

            var batch = Batch(10, "chili", "banana");
            new PricingEngine(prices).Price(batch);

            Assert.Equal(8d, batch.UnitCost);
            Assert.Equal(80d, batch.TotalCost);
            Assert.Equal(3d, batch.IngredientUnitCosts["chili"]);
            Assert.Equal(5d, batch.IngredientUnitCosts["banana"]);
        }

        /// <summary>A recipe can use the same ingredient twice, and each use is a real cost.</summary>
        [Fact]
        public void A_repeated_ingredient_is_counted_each_time()
        {
            var prices = new Prices();
            prices.Costs["chili"] = 3d;

            var batch = Batch(1, "chili", "chili");
            new PricingEngine(prices).Price(batch);

            Assert.Equal(6d, batch.UnitCost);
            Assert.Equal(6d, batch.IngredientUnitCosts["chili"]);
        }

        /// <summary>
        /// Null rather than empty when nothing could be priced, so "we have no cost data" stays
        /// distinguishable from "every ingredient is genuinely free".
        /// </summary>
        [Fact]
        public void Unpriced_ingredients_leave_the_breakdown_absent()
        {
            var batch = Batch(10, "chili");
            new PricingEngine(new Prices()).Price(batch);

            Assert.Null(batch.IngredientUnitCosts);
            Assert.Equal(0d, batch.TotalCost);
        }

        [Fact]
        public void Statistics_attribute_the_recorded_split()
        {
            var prices = new Prices();
            prices.Costs["chili"] = 3d;
            prices.Costs["banana"] = 5d;

            var batch = Batch(10, "chili", "banana");
            new PricingEngine(prices).Price(batch);

            var stats = StatisticsService.Build("p", new[] { batch }, System.DateTime.UtcNow);

            Assert.Equal(30d, stats.ByIngredient["chili"].TotalCost);
            Assert.Equal(50d, stats.ByIngredient["banana"].TotalCost);

            // And the parts still add up to the whole, which is the check that matters.
            Assert.Equal(stats.Personal.TotalCost,
                         stats.ByIngredient["chili"].TotalCost + stats.ByIngredient["banana"].TotalCost);
        }

        /// <summary>
        /// Events written before this existed carry no breakdown. Their cost is left unattributed
        /// rather than split evenly across the chain — an even split is wrong the moment two
        /// ingredients differ in price, which is nearly always, and a plausible wrong number is the
        /// failure this project keeps deciding against.
        /// </summary>
        [Fact]
        public void Older_events_are_left_unattributed_rather_than_guessed_at()
        {
            var legacy = Batch(10, "chili", "banana");
            legacy.UnitCost = 8d;
            legacy.TotalCost = 80d;
            legacy.IngredientUnitCosts = null;

            var stats = StatisticsService.Build("p", new[] { legacy }, System.DateTime.UtcNow);

            Assert.Equal(80d, stats.Personal.TotalCost);
            Assert.Equal(0d, stats.ByIngredient["chili"].TotalCost);
            Assert.Equal(0d, stats.ByIngredient["banana"].TotalCost);

            // Usage is still counted — only the money is unknown.
            Assert.Equal(10, stats.ByIngredient["chili"].UnitsConsumed);
        }
    }
}
