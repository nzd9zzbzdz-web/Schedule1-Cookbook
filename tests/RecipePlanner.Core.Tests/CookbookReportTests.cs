using System;
using System.Collections.Generic;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Reporting;
using RecipePlanner.Core.Stats;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// The readable export is the ONLY output a player on the default (IL2CPP) branch ever sees,
    /// because the phone app is Mono-only. That makes it load-bearing rather than a nicety, and it
    /// has to survive the shapes real data actually takes — including the empty and broken ones.
    /// </summary>
    public class CookbookReportTests
    {
        private static readonly DateTime Now = new DateTime(2026, 8, 23, 14, 30, 0, DateTimeKind.Utc);

        [Fact]
        public void A_brand_new_profile_renders_without_throwing()
        {
            var text = CookbookReport.Render("Fresh Start", new PlayerStatistics(), new Recipe[0], Now);

            Assert.Contains("Fresh Start", text);
            Assert.Contains("Nothing discovered yet", text);
        }

        /// <summary>
        /// Everything here is nullable in practice: stats come off disk, recipes come off disk, and
        /// a truncated write leaves nulls behind. Throwing on the shutdown path would be the worst
        /// possible time to fail.
        /// </summary>
        [Fact]
        public void Nulls_everywhere_do_not_throw()
        {
            var text = CookbookReport.Render(null, null, null, Now);
            Assert.False(string.IsNullOrWhiteSpace(text));

            var halfBuilt = new PlayerStatistics
            {
                Personal = null, Records = null, Excluded = null,
                ByProduct = null, ByIngredient = null, ByRecipe = null, ByDrugType = null
            };
            var second = CookbookReport.Render("X", halfBuilt, new Recipe[] { null }, Now);
            Assert.False(string.IsNullOrWhiteSpace(second));
        }

        /// <summary>
        /// The whole reason this distinction exists: GamePriceSource fails silently to zero, and a
        /// confident "$0.00 profit" is a wrong answer presented as a real one.
        /// </summary>
        [Fact]
        public void Missing_prices_are_reported_as_unavailable_not_as_zero()
        {
            var stats = new PlayerStatistics
            {
                Personal = new Totals { UnitsProduced = 40, Batches = 2, TotalValue = 0, TotalCost = 0 }
            };

            var text = CookbookReport.Render("Broke", stats, new Recipe[0], Now);

            Assert.Contains("not available", text);
            Assert.DoesNotContain("$0.00", text);
            Assert.Contains("40", text);   // the non-monetary facts still show
        }

        /// <summary>
        /// Real data from a live save surfaced this: unnamed mixes have no price, so they rendered
        /// "$0.00" in a table alongside products with real figures — reading as "worthless" rather
        /// than "unpriced". The unavailable/zero distinction has to hold per row, not just per table.
        /// </summary>
        [Fact]
        public void An_unpriced_product_shows_a_dash_not_zero()
        {
            var stats = new PlayerStatistics
            {
                Personal = new Totals { UnitsProduced = 120, Batches = 6, TotalValue = 5400, TotalCost = 420 },
                ByProduct = new Dictionary<string, ProductStat>
                {
                    ["priced"] = new ProductStat { ProductId = "priced", DisplayName = "Mega Smegma", Units = 100, Batches = 5, Value = 5400, Cost = 420 },
                    ["unpriced"] = new ProductStat { ProductId = "unpriced", DisplayName = "Unnamed mix (a + b)", Units = 20, Batches = 1, Value = 0, Cost = 0 },
                }
            };

            var text = CookbookReport.Render("Echo", stats, new Recipe[0], Now);

            Assert.Contains("$5,400.00", text);           // the priced one still shows
            Assert.DoesNotContain("$0.00", text);         // the unpriced one must not claim zero
            Assert.Contains("Unnamed mix (a + b)", text); // but is still listed
            Assert.Contains("—", text);
        }

        [Fact]
        public void Real_prices_are_shown()
        {
            var stats = new PlayerStatistics
            {
                Personal = new Totals
                {
                    UnitsProduced = 20, Batches = 1,
                    TotalValue = 1234.5, TotalCost = 234.5, EstimatedProfit = 1000
                }
            };

            var text = CookbookReport.Render("Rich", stats, new Recipe[0], Now);

            Assert.Contains("$1,234.50", text);
            Assert.Contains("$1,000.00", text);
            Assert.DoesNotContain("not available", text);
        }

        [Fact]
        public void Recipes_group_under_their_base_product_and_show_the_chain()
        {
            var recipes = new[]
            {
                new Recipe
                {
                    RecipeId = "r1", Name = "Extreme Assblaster", BaseProductId = "ogkush",
                    Steps = new List<string> { "hairypuke", "paracetamol" },
                    Effects = new List<string> { "Paranoia", "Sneaky" },
                    TimesProduced = 3
                },
                new Recipe { RecipeId = "r2", Name = "Sour Diesel Mix", BaseProductId = "sourdiesel" }
            };

            var text = CookbookReport.Render("Chef", new PlayerStatistics(), recipes, Now);

            Assert.Contains("### ogkush", text);
            Assert.Contains("### sourdiesel", text);
            Assert.Contains("ogkush → hairypuke → paracetamol", text);
            Assert.Contains("Paranoia, Sneaky", text);
        }

        /// <summary>
        /// Hiding is display-only everywhere else in the mod; the export must agree, or the player
        /// hides something in game and finds it staring back out of the file.
        /// </summary>
        [Fact]
        public void Hidden_recipes_are_omitted_but_counted()
        {
            var recipes = new[]
            {
                new Recipe { RecipeId = "keep", Name = "Kept", BaseProductId = "ogkush" },
                new Recipe { RecipeId = "gone", Name = "SecretSauce", BaseProductId = "ogkush",
                             Status = RecipeStatus.Hidden }
            };

            var text = CookbookReport.Render("Chef", new PlayerStatistics(), recipes, Now);

            Assert.Contains("Kept", text);
            Assert.DoesNotContain("SecretSauce", text);
            Assert.Contains("1 hidden", text);
        }

        /// <summary>
        /// A player whose total looks lower than they expect needs to see that it was a deliberate
        /// exclusion, not a miscount. This is the single most likely "is the mod broken?" report.
        /// </summary>
        [Fact]
        public void Excluded_production_is_explained_rather_than_hidden()
        {
            var stats = new PlayerStatistics
            {
                Personal = new Totals { UnitsProduced = 20, Batches = 1 },
                Excluded = new Totals { UnitsProduced = 20, Batches = 1 },
                ExcludedByReason = new Dictionary<string, long> { ["Employee"] = 1 }
            };

            var text = CookbookReport.Render("Boss", stats, new Recipe[0], Now);

            Assert.Contains("Not counted as yours", text);
            Assert.Contains("Employee", text);
        }

        /// <summary>
        /// Records store ids. An id is not an answer — "r1" tells the player nothing — so they are
        /// resolved to the names the game shows, falling back to the id only when nothing better
        /// exists.
        /// </summary>
        [Fact]
        public void Records_show_names_rather_than_raw_ids()
        {
            var stats = new PlayerStatistics
            {
                ByRecipe = new Dictionary<string, RecipeStat>
                {
                    ["r1"] = new RecipeStat { RecipeId = "r1", DisplayName = "Extreme Assblaster" }
                },
                ByProduct = new Dictionary<string, ProductStat>
                {
                    ["eab"] = new ProductStat { ProductId = "eab", DisplayName = "Extreme Assblaster" }
                },
                Records = new Records
                {
                    MostUsedRecipeId = "r1",
                    MostProducedProductId = "eab",
                    LargestBatchUnits = 20,
                    LargestBatchProductId = "eab",
                    HighestValueRecipeId = "unknown-recipe"
                }
            };

            var text = CookbookReport.Render("Chef", stats, new Recipe[0], Now);

            Assert.Contains("Most used recipe: Extreme Assblaster", text);
            Assert.Contains("Largest batch: 20 unit(s) of Extreme Assblaster", text);
            Assert.DoesNotContain(": r1", text);

            // Nothing better available: the id is still more useful than an empty line.
            Assert.Contains("unknown-recipe", text);
        }

        /// <summary>
        /// Renders on any machine regardless of locale. A German Windows box would otherwise write
        /// "1.234,50" into a file this claims is stable.
        /// </summary>
        [Fact]
        public void Formatting_is_culture_invariant()
        {
            var original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture =
                    new System.Globalization.CultureInfo("de-DE");

                var stats = new PlayerStatistics
                {
                    Personal = new Totals { UnitsProduced = 1234, Batches = 2, TotalValue = 1234.5 }
                };

                var text = CookbookReport.Render("Hans", stats, new Recipe[0], Now);

                Assert.Contains("1,234", text);      // thousands separator stays a comma
                Assert.Contains("$1,234.50", text);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }
    }
}
