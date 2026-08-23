using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>Phase 12: a recipe invented in game appears in the cookbook unprompted.</summary>
    public class RecipeDiscoveryTests
    {
        private static ProductionEvent Produced(
            string @base = "meth",
            string[] chain = null,
            string output = "bluelightning",
            ProductionKind kind = ProductionKind.Mixed,
            DateTime? when = null)
        {
            var evt = new ProductionEvent
            {
                Kind = kind,
                Attribution = Attribution.Local,
                BaseProductId = @base,
                IngredientId = chain != null && chain.Length > 0 ? chain[0] : "battery",
                IngredientChain = new List<string>(chain ?? new[] { "battery" }),
                OutputProductId = output,
                OutputProductName = "Blue Lightning",
                Effects = new List<string> { "Energizing", "Euphoric" },
                Quantity = 20,
                RealTimeUtc = when ?? new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc)
            };
            evt.RecipeId = evt.ComputeRecipeId();
            return evt;
        }

        [Fact]
        public void Producing_an_unseen_combination_adds_it_to_the_cookbook()
        {
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);
            var announced = new List<Recipe>();
            service.RecipeDiscovered += announced.Add;

            var recipe = service.OnProduced(Produced(chain: new[] { "a", "b", "c" }));

            Assert.Single(announced);
            Assert.Equal("meth>a>b>c", recipe.RecipeId);
            Assert.Equal(new[] { "a", "b", "c" }, recipe.Steps);
            Assert.Equal("auto", recipe.Source);
            Assert.True(recipe.Has(RecipeStatus.Discovered));
            Assert.True(recipe.Has(RecipeStatus.Produced));
        }

        [Fact]
        public void Ingredient_order_makes_a_different_recipe()
        {
            // a>b>c and c>b>a are genuinely different products in Schedule I.
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);

            service.OnProduced(Produced(chain: new[] { "a", "b", "c" }));
            service.OnProduced(Produced(chain: new[] { "c", "b", "a" }));

            Assert.Equal(2, repo.All().Count());
        }

        [Fact]
        public void Cooking_a_known_recipe_again_announces_nothing_new()
        {
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);
            var announced = new List<Recipe>();
            service.RecipeDiscovered += announced.Add;

            service.OnProduced(Produced());
            service.OnProduced(Produced());
            service.OnProduced(Produced());

            Assert.Single(announced);
            Assert.Equal(3, repo.All().Single().TimesProduced);
        }

        [Fact]
        public void First_and_last_produced_dates_are_tracked()
        {
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);

            var early = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var late = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

            service.OnProduced(Produced(when: late));
            service.OnProduced(Produced(when: early));

            var recipe = repo.All().Single();
            Assert.Equal(early, recipe.FirstProducedUtc);
            Assert.Equal(late, recipe.LastProducedUtc);
        }

        [Theory]
        [InlineData(ProductionKind.Dried)]
        [InlineData(ProductionKind.Bricked)]
        [InlineData(ProductionKind.Packaged)]
        public void Transforms_do_not_create_cookbook_entries(ProductionKind kind)
        {
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);

            var recipe = service.OnProduced(Produced(kind: kind));

            Assert.Null(recipe);
            Assert.Empty(repo.All());
        }

        [Fact]
        public void A_game_discovery_signal_records_a_recipe_we_never_saw_produced()
        {
            // ProductManager.onMixRecipeAdded fires for recipes learned without us observing a
            // station completion.
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);

            var recipe = service.OnGameDiscovery(
                "ogkush", new[] { "cuke" }, "thickmonkey", "Thick Monkey", DateTime.UtcNow);

            Assert.Equal("ogkush>cuke", recipe.RecipeId);
            Assert.True(recipe.Has(RecipeStatus.Discovered));
            Assert.False(recipe.Has(RecipeStatus.Produced));
        }

        [Fact]
        public void Producing_a_previously_planned_recipe_keeps_the_planned_flag()
        {
            var repo = new InMemoryRecipeRepository();
            repo.Upsert(new Recipe
            {
                RecipeId = "meth>battery",
                Name = "My Plan",
                BaseProductId = "meth",
                Steps = new List<string> { "battery" },
                Status = RecipeStatus.Planned,
                Source = "manual"
            });

            var service = new RecipeDiscoveryService(repo);
            var recipe = service.OnProduced(Produced());

            Assert.True(recipe.Has(RecipeStatus.Planned));
            Assert.True(recipe.Has(RecipeStatus.Produced));
            Assert.Equal("manual", recipe.Source);   // the player's own entry is not overwritten
            Assert.Equal("My Plan", recipe.Name);
        }

        [Fact]
        public void Effects_are_captured_from_the_first_production()
        {
            var repo = new InMemoryRecipeRepository();
            var service = new RecipeDiscoveryService(repo);

            var recipe = service.OnProduced(Produced());

            Assert.Equal(new[] { "Energizing", "Euphoric" }, recipe.Effects);
        }
    }
}
