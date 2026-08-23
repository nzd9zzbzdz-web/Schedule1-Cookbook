using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Found live. A mix cooked before the player names it has no product yet, so the recipe was
    /// stored with a blank OutputProductId — and nothing ever filled it in, because the update path
    /// only runs for recipes that already exist and it never touched that field.
    ///
    /// Real data from the reporting save, both parentless:
    ///
    ///   { "RecipeId": "purpleexpress>gasoline", "Name": "Aspen Piss"    }   no OutputProductId
    ///   { "RecipeId": "purpleexpress>motoroil", "Name": "Tokyo Splooge" }   no OutputProductId
    ///
    /// A recipe with no output cannot be placed in the lineage tree, so both showed as
    /// "origin unknown" — on exactly the recipes the player had just invented.
    /// </summary>
    public class RecipeRepairTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 23, 13, 0, 0, DateTimeKind.Utc);

        private static ProductionEvent Named(string baseId, string ingredient, string output, DateTime when) =>
            new ProductionEvent
            {
                BaseProductId = baseId,
                IngredientId = ingredient,
                OutputProductId = output,
                OutputProductName = output,
                DrugType = "Marijuana",
                RecipeId = baseId + ">" + ingredient,
                RealTimeUtc = when,
                Quantity = 10,
                Kind = ProductionKind.Mixed,
            };

        private static Recipe Parentless(string baseId, string ingredient, string name) =>
            new Recipe
            {
                RecipeId = baseId + ">" + ingredient,
                Name = name,
                BaseProductId = baseId,
                Steps = new List<string> { ingredient },
                Source = "auto",
                // OutputProductId deliberately absent — this is the shape found on disk.
            };

        [Fact]
        public void A_recipe_named_after_the_cook_gets_its_product_back()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(Parentless("purpleexpress", "gasoline", "Aspen Piss"));

            var repaired = RecipeRepair.BackfillFromEvents(
                repo,
                new[] { Named("purpleexpress", "gasoline", "aspenpiss", T0) });

            Assert.Equal(1, repaired);
            Assert.Equal("aspenpiss", repo.Get("purpleexpress>gasoline").OutputProductId);
        }

        /// <summary>The whole point: a repaired recipe can be placed in the tree.</summary>
        [Fact]
        public void A_repaired_recipe_is_no_longer_origin_unknown()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(Parentless("purpleexpress", "gasoline", "Aspen Piss"));
            RecipeRepair.BackfillFromEvents(
                repo, new[] { Named("purpleexpress", "gasoline", "aspenpiss", T0) });

            var recipe = repo.Get("purpleexpress>gasoline");
            var graph = RecipeGraph.Build(
                new[]
                {
                    new MixRecipeRow("ogkush", "banana", "purpleexpress"),
                    new MixRecipeRow(recipe.BaseProductId, recipe.Steps[0], recipe.OutputProductId),
                },
                new[] { "ogkush", "purpleexpress", "aspenpiss" },
                new[] { "ogkush" });

            Assert.True(graph.GetLineage("aspenpiss").IsComplete);
            Assert.Equal("ogkush", graph.GetLineage("aspenpiss").RootProductId);
        }

        /// <summary>
        /// An event still awaiting a name carries no product, so it cannot repair anything. Using
        /// it would write back the same blank the repair exists to fix.
        /// </summary>
        [Fact]
        public void An_event_still_awaiting_a_name_repairs_nothing()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(Parentless("purpleexpress", "motoroil", "Tokyo Splooge"));

            var pending = Named("purpleexpress", "motoroil", null, T0);
            pending.OutputProductName = null;

            Assert.Equal(0, RecipeRepair.BackfillFromEvents(repo, new[] { pending }));
            Assert.True(string.IsNullOrEmpty(repo.Get("purpleexpress>motoroil").OutputProductId));
        }

        /// <summary>
        /// Only ever fills blanks. A recipe id is base + steps, so the same recipe must yield the
        /// same product; a conflicting value means something is wrong upstream, and quietly taking
        /// the newer one would hide it.
        /// </summary>
        [Fact]
        public void An_existing_product_is_never_overwritten()
        {
            var repo = new InMemoryRecipeRepository();
            var recipe = Parentless("ogkush", "banana", "Purple Express");
            recipe.OutputProductId = "purpleexpress";
            repo.Upsert(recipe);

            var repaired = RecipeRepair.BackfillFromEvents(
                repo, new[] { Named("ogkush", "banana", "somethingelse", T0.AddHours(1)) });

            Assert.Equal(0, repaired);
            Assert.Equal("purpleexpress", repo.Get("ogkush>banana").OutputProductId);
        }

        [Fact]
        public void Running_it_twice_changes_nothing_the_second_time()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(Parentless("purpleexpress", "gasoline", "Aspen Piss"));
            var events = new[] { Named("purpleexpress", "gasoline", "aspenpiss", T0) };

            Assert.Equal(1, RecipeRepair.BackfillFromEvents(repo, events));
            Assert.Equal(0, RecipeRepair.BackfillFromEvents(repo, events));
        }

        [Fact]
        public void Nulls_and_empties_do_not_throw()
        {
            Assert.Equal(0, RecipeRepair.BackfillFromEvents(null, null));

            var repo = new InMemoryRecipeRepository();
            Assert.Equal(0, RecipeRepair.BackfillFromEvents(repo, null));
            Assert.Equal(0, RecipeRepair.BackfillFromEvents(repo, new ProductionEvent[] { null }));
        }

        /// <summary>The newest named cook wins when a recipe has been made more than once.</summary>
        [Fact]
        public void The_most_recent_named_event_is_used()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(Parentless("purpleexpress", "gasoline", "Aspen Piss"));

            RecipeRepair.BackfillFromEvents(repo, new[]
            {
                Named("purpleexpress", "gasoline", "oldname", T0),
                Named("purpleexpress", "gasoline", "aspenpiss", T0.AddMinutes(5)),
            });

            Assert.Equal("aspenpiss", repo.Get("purpleexpress>gasoline").OutputProductId);
        }
    }
}
