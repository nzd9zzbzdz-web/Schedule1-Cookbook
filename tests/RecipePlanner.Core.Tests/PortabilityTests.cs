using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Product ids below a base are generated from the name the PLAYER types when they discover a
    /// mix (FinishAndNameMix -> MakeIDFileSafe). So "thickmonkey" exists only in the save that
    /// created it: another player, or a new game, produces entirely different ids.
    ///
    /// These tests pin the rule that nothing derived may be assumed.
    /// </summary>
    public class PortabilityTests
    {
        [Fact]
        public void Base_products_come_from_the_game_not_from_our_constants()
        {
            // A hypothetical future strain the mod has never heard of.
            var bases = new[] { "ogkush", "brandnewstrain" };
            var graph = RecipeGraph.Build(
                new[] { new MixRecipeRow("brandnewstrain", "cuke", "someplayername") },
                new[] { "brandnewstrain", "someplayername" },
                bases);

            var lineage = graph.GetLineage("someplayername");

            Assert.True(lineage.IsComplete);
            Assert.Equal("brandnewstrain", lineage.RootProductId);
            Assert.True(graph.IsBaseProduct("brandnewstrain"));
        }

        [Fact]
        public void The_fallback_list_is_used_only_when_the_game_supplies_nothing()
        {
            var fromGame = RecipeGraph.Build(new MixRecipeRow[0], new string[0], new[] { "onlythis" });
            Assert.Equal(new[] { "onlythis" }, fromGame.BaseProducts);

            var fallback = RecipeGraph.Build(new MixRecipeRow[0], new string[0], null);
            Assert.Contains("ogkush", fallback.BaseProducts);

            // An empty list is treated as "nothing supplied", not as "no bases exist".
            var empty = RecipeGraph.Build(new MixRecipeRow[0], new string[0], new string[0]);
            Assert.Contains("ogkush", empty.BaseProducts);
        }

        [Fact]
        public void Another_players_save_resolves_with_completely_different_ids()
        {
            // Same structure as the local save, entirely different player-chosen names.
            var graph = RecipeGraph.Build(
                new[]
                {
                    new MixRecipeRow("ogkush", "cuke", "dragonfire"),
                    new MixRecipeRow("dragonfire", "banana", "dragonfirev2"),
                },
                new[] { "ogkush", "dragonfire", "dragonfirev2" },
                new[] { "ogkush" });

            var lineage = graph.GetLineage("dragonfirev2");

            Assert.True(lineage.IsComplete);
            Assert.Equal("ogkush + cuke + banana", lineage.Describe());
        }

        [Fact]
        public void Section_order_is_supplied_rather_than_assumed()
        {
            var graph = RecipeGraph.Build(
                new[]
                {
                    new MixRecipeRow("strainb", "cuke", "one"),
                    new MixRecipeRow("straina", "cuke", "two")
                },
                new[] { "straina", "strainb", "one", "two" },
                new[] { "straina", "strainb" });

            var entries = Cookbook.Compose(
                new[]
                {
                    new ProductRow { Id = "one", Name = "One" },
                    new ProductRow { Id = "two", Name = "Two" }
                },
                graph, null, new InMemoryRecipeRepository());

            var order = Cookbook.Build(entries, graph, null, null, new[] { "strainb", "straina" })
                                .Select(s => s.RootProductId).ToList();

            Assert.Equal(new[] { "strainb", "straina" }, order);
        }

        [Fact]
        public void Display_names_are_never_inferred_from_the_id()
        {
            // The UI must render whatever the game says the product is called.
            var entries = Cookbook.Compose(
                new[] { new ProductRow { Id = "xyz123", Name = "Player Chosen Name" } },
                RecipeGraph.Build(new MixRecipeRow[0], new[] { "xyz123" }, new[] { "ogkush" }),
                null, new InMemoryRecipeRepository());

            Assert.Equal("Player Chosen Name", entries.Single().DisplayName);
        }

        [Fact]
        public void No_shipped_source_file_hard_codes_a_derived_product_id()
        {
            // Guard against someone pasting an id from a dump into production code. Only the
            // documented fallback base list may name products at all.
            var root = FindRepoRoot();
            Skip.If(root is null, "Repository root not found.");

            var derivedIds = new[] { "thickmonkey", "megasmegma", "deathfuel", "bluelightning", "slimycrack" };
            var offenders = new List<string>();

            foreach (var file in System.IO.Directory.GetFiles(
                         System.IO.Path.Combine(root, "src"), "*.cs", System.IO.SearchOption.AllDirectories))
            {
                foreach (var raw in System.IO.File.ReadAllLines(file))
                {
                    // Comments may name ids — that is how the rule is documented.
                    var line = raw.TrimStart();
                    if (line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*")) continue;

                    foreach (var id in derivedIds)
                        if (line.IndexOf("\"" + id + "\"", StringComparison.OrdinalIgnoreCase) >= 0)
                            offenders.Add($"{System.IO.Path.GetFileName(file)} -> {id}");
                }
            }

            Assert.Empty(offenders);
        }

        private static string FindRepoRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (var i = 0; i < 8 && dir != null; i++)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir, "src"))) return dir;
                dir = System.IO.Directory.GetParent(dir)?.FullName;
            }
            return null;
        }
    }
}
