using System;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Recovering batches whose naming event the mod never saw.
    ///
    /// The live path only fires while the player is typing the name. Anything that misses that one
    /// moment — mod installed after the mix, hook attached late, naming done in a session the mod
    /// was not loaded for — used to be stranded permanently, because both load-time repairs skip
    /// pending batches by design and nothing else came back for them.
    ///
    /// Taken from a real save: four consecutive invented mixes, every one of them demonstrably
    /// named by the player, all four still recorded as unnamed and worth zero.
    /// </summary>
    public class PendingNameRecoveryTests
    {
        private static ProductionEvent Pending(string baseProduct, string ingredient, int time = 900)
        {
            var evt = new ProductionEvent
            {
                Kind = ProductionKind.Mixed,
                Attribution = Attribution.Local,
                ProfileId = "p",
                BaseProductId = baseProduct,
                IngredientId = ingredient,
                OutputProductId = null,
                Quantity = 20,
                ElapsedDays = 40,
                TimeOfDay = time,
                RealTimeUtc = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc)
            };
            evt.RecipeId = evt.ComputeRecipeId();
            return evt;
        }

        private static ResolvedStep Step(string baseProduct, string additive, string output) =>
            new ResolvedStep { BaseProductId = baseProduct, AdditiveId = additive, OutputProductId = output };

        [Fact]
        public void A_pending_batch_is_named_from_the_games_recipe_list()
        {
            var evt = Pending("megasmegma", "banana");
            var steps = new[] { Step("megasmegma", "banana", "strawberrypunch") };

            var applied = PendingNameResolver.ResolveFromRecipes(new[] { evt }, steps);

            Assert.Equal(1, applied);
            Assert.Equal("strawberrypunch", evt.OutputProductId);
            Assert.False(evt.IsAwaitingName);
        }

        [Fact]
        public void The_display_name_comes_from_the_game_not_the_id()
        {
            var evt = Pending("megasmegma", "banana");
            var steps = new[] { Step("megasmegma", "banana", "strawberrypunch") };

            PendingNameResolver.ResolveFromRecipes(
                new[] { evt }, steps, id => id == "strawberrypunch" ? "Strawberry Punch" : null);

            Assert.Equal("Strawberry Punch", evt.OutputProductName);
        }

        [Fact]
        public void An_unknown_display_name_falls_back_to_the_id_rather_than_null()
        {
            var evt = Pending("megasmegma", "banana");
            var steps = new[] { Step("megasmegma", "banana", "strawberrypunch") };

            PendingNameResolver.ResolveFromRecipes(new[] { evt }, steps, id => null);

            Assert.Equal("strawberrypunch", evt.OutputProductName);
        }

        /// <summary>
        /// The exact chain observed on the real save. Each mix was named by the player — provable
        /// because the next cook used that name as its base — yet all four stayed pending.
        /// </summary>
        [Fact]
        public void The_whole_observed_chain_recovers_in_one_pass()
        {
            var events = new[]
            {
                Pending("megasmegma", "banana", 1618),
                Pending("strawberrypunch", "megabean", 1725),
                Pending("ultramclovin", "motoroil", 1833),
                Pending("girlscoutshart", "paracetamol", 1954)
            };

            var steps = new[]
            {
                Step("megasmegma", "banana", "strawberrypunch"),
                Step("strawberrypunch", "megabean", "ultramclovin"),
                Step("ultramclovin", "motoroil", "girlscoutshart"),
                Step("girlscoutshart", "paracetamol", "thickmonkey")
            };

            var applied = PendingNameResolver.ResolveFromRecipes(events, steps);

            Assert.Equal(4, applied);
            Assert.All(events, e => Assert.False(e.IsAwaitingName));
            Assert.Equal(
                new[] { "strawberrypunch", "ultramclovin", "girlscoutshart", "thickmonkey" },
                events.Select(e => e.OutputProductId));
        }

        /// <summary>
        /// A self-loop is a real row in the game data. It says the mix produced nothing new, so
        /// naming a batch after its own base would invent an identity the player never gave it —
        /// and self-looping the lineage tree is the exact bug LegacyEventRepair exists to undo.
        /// </summary>
        [Fact]
        public void A_self_loop_never_names_a_batch_after_its_own_base()
        {
            var evt = Pending("thickdick", "paracetamol");
            var steps = new[] { Step("thickdick", "paracetamol", "thickdick") };

            var applied = PendingNameResolver.ResolveFromRecipes(new[] { evt }, steps);

            Assert.Equal(0, applied);
            Assert.True(evt.IsAwaitingName);
        }

        [Fact]
        public void A_batch_with_no_matching_recipe_is_left_pending()
        {
            var evt = Pending("megasmegma", "banana");
            var steps = new[] { Step("ogkush", "cuke", "somethingelse") };

            Assert.Equal(0, PendingNameResolver.ResolveFromRecipes(new[] { evt }, steps));
            Assert.True(evt.IsAwaitingName);
        }

        [Fact]
        public void Already_named_batches_are_never_touched()
        {
            var evt = Pending("megasmegma", "banana");
            evt.OutputProductId = "realname";
            evt.OutputProductName = "Real Name";
            var steps = new[] { Step("megasmegma", "banana", "differentproduct") };

            Assert.Equal(0, PendingNameResolver.ResolveFromRecipes(new[] { evt }, steps));
            Assert.Equal("realname", evt.OutputProductId);
        }

        [Fact]
        public void No_recipes_means_no_repair_rather_than_a_guess()
        {
            var evt = Pending("megasmegma", "banana");

            Assert.Equal(0, PendingNameResolver.ResolveFromRecipes(new[] { evt }, new ResolvedStep[0]));
            Assert.Equal(0, PendingNameResolver.ResolveFromRecipes(new[] { evt }, null));
            Assert.True(evt.IsAwaitingName);
        }

        /// <summary>
        /// The game stores roughly one row in five with its sides reversed. RecipeGraph is what
        /// sorts that out, so the repair must consume its output rather than raw rows — otherwise
        /// a reversed row names the batch after the wrong side of the mix.
        /// </summary>
        [Fact]
        public void A_reversed_row_still_names_the_batch_correctly()
        {
            var evt = Pending("megasmegma", "banana");

            // Stored the wrong way round: the additive is in Product, the base in Mixer.
            var graph = RecipeGraph.Build(
                new[] { new MixRecipeRow("banana", "megasmegma", "strawberrypunch") },
                new[] { "megasmegma", "strawberrypunch" },
                new[] { "shroom" });

            var applied = PendingNameResolver.ResolveFromRecipes(new[] { evt }, graph.Steps);

            Assert.Equal(1, applied);
            Assert.Equal("strawberrypunch", evt.OutputProductId);
        }

        /// <summary>
        /// Naming is what unblocks pricing: RepriceZeroValued skips pending batches, so a batch
        /// that stays unnamed can never gain a value no matter how many times the game loads.
        /// </summary>
        [Fact]
        public void Naming_is_what_makes_a_batch_priceable_at_all()
        {
            var evt = Pending("megasmegma", "banana");
            Assert.True(evt.IsAwaitingName, "precondition: a pending batch is skipped by repricing");

            PendingNameResolver.ResolveFromRecipes(
                new[] { evt }, new[] { Step("megasmegma", "banana", "strawberrypunch") });

            Assert.False(evt.IsAwaitingName);
            Assert.False(string.IsNullOrEmpty(evt.OutputProductId));
        }

        [Fact]
        public void Nulls_are_survivable()
        {
            Assert.Equal(0, PendingNameResolver.ResolveFromRecipes(null, new ResolvedStep[0]));
            Assert.Equal(0, PendingNameResolver.ResolveFromRecipes(
                new ProductionEvent[] { null }, new[] { Step("a", "b", "c") }));
        }
    }
}
