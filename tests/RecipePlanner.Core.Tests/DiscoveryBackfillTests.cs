using System;
using System.Collections.Generic;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Both discovery paths must back-fill a parentless recipe, not just the one that was fixed.
    ///
    /// <c>OnProduced</c> was repaired when a live save showed recipes stuck with no
    /// OutputProductId; <c>OnGameDiscovery</c> had the identical defect in the same file and was
    /// missed. A recipe with no output cannot be placed in the lineage tree, which is what puts it
    /// under "origin unknown" — so whichever path learns the answer first has to record it.
    /// </summary>
    public class DiscoveryBackfillTests
    {
        private static readonly DateTime When = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>The shape found on disk: recorded from an unnamed mix, so it has no product.</summary>
        private static Recipe Parentless() => new Recipe
        {
            RecipeId = "purpleexpress>gasoline",
            Name = "Aspen Piss",
            BaseProductId = "purpleexpress",
            Steps = new List<string> { "gasoline" },
            Source = "auto",
        };

        [Fact]
        public void Game_discovery_fills_in_a_parentless_recipe()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(Parentless());

            new RecipeDiscoveryService(repo).OnGameDiscovery(
                "purpleexpress", new[] { "gasoline" }, "aspenpiss", "Aspen Piss", When);

            Assert.Equal("aspenpiss", repo.Get("purpleexpress>gasoline").OutputProductId);
        }

        /// <summary>
        /// Blank-only. A recipe id is base + steps, so the same recipe must always yield the same
        /// product — a conflicting value means something is wrong upstream, and quietly taking the
        /// newer one would hide it.
        /// </summary>
        [Fact]
        public void An_existing_product_is_never_overwritten()
        {
            var repo = new InMemoryRecipeRepository();
            var recipe = Parentless();
            recipe.OutputProductId = "aspenpiss";
            repo.Upsert(recipe);

            new RecipeDiscoveryService(repo).OnGameDiscovery(
                "purpleexpress", new[] { "gasoline" }, "somethingelse", "Other", When);

            Assert.Equal("aspenpiss", repo.Get("purpleexpress>gasoline").OutputProductId);
        }

        [Fact]
        public void A_recipe_the_game_announces_first_is_recorded_whole()
        {
            var repo = new InMemoryRecipeRepository();

            new RecipeDiscoveryService(repo).OnGameDiscovery(
                "ogkush", new[] { "banana" }, "purpleexpress", "Purple Express", When);

            var recipe = repo.Get("ogkush>banana");
            Assert.Equal("purpleexpress", recipe.OutputProductId);
            Assert.Equal("Purple Express", recipe.Name);
            Assert.True(recipe.Has(RecipeStatus.Discovered));
        }
    }
}
