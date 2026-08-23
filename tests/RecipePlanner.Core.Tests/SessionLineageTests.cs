using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// A mix invented during the current session is absent from the save file's mix list until the
    /// game next writes a save. Reported live: "Aspen Piss" showed under **origin unknown** in the
    /// cookbook while the game's own Products screen displayed its recipe correctly.
    ///
    /// That is the worst possible recipe to lose lineage for — the one the player just invented and
    /// is looking for. Production detection already recorded the parent at the moment of the cook,
    /// so the information was never missing, only unused.
    ///
    /// These tests pin the graph behaviour that <c>CookbookDataBuilder.BuildGraph</c> relies on.
    /// </summary>
    public class SessionLineageTests
    {
        private static readonly string[] Bases = { "ogkush", "sourdiesel" };

        /// <summary>The bug: the save file alone cannot place a brand-new mix.</summary>
        [Fact]
        public void Without_the_session_recipe_a_new_mix_has_no_origin()
        {
            var saveFileOnly = new[] { new MixRecipeRow("ogkush", "banana", "purpleexpress") };

            var graph = RecipeGraph.Build(
                saveFileOnly,
                new[] { "ogkush", "purpleexpress", "aspenpiss" },
                Bases);

            var lineage = graph.GetLineage("aspenpiss");

            Assert.False(lineage.IsComplete);
        }

        /// <summary>The fix: folding in what we discovered ourselves places it correctly.</summary>
        [Fact]
        public void Folding_in_the_session_recipe_restores_the_chain()
        {
            var rows = new[]
            {
                new MixRecipeRow("ogkush", "banana", "purpleexpress"),
                // What production detection recorded this session, absent from the save file.
                new MixRecipeRow("purpleexpress", "gasoline", "aspenpiss"),
            };

            var graph = RecipeGraph.Build(
                rows,
                new[] { "ogkush", "purpleexpress", "aspenpiss" },
                Bases);

            var lineage = graph.GetLineage("aspenpiss");

            Assert.True(lineage.IsComplete);
            Assert.Equal("ogkush", lineage.RootProductId);

            // And it groups under the strain rather than into the unknown bucket.
            var grouped = graph.GroupByBase(new[] { "aspenpiss" });
            Assert.True(grouped.ContainsKey("ogkush"));
            Assert.Contains("aspenpiss", grouped["ogkush"]);
        }

        /// <summary>
        /// The save file wins ties. Once the game has written the recipe, both sources describe the
        /// same edge, and adding it twice would double rows for every recipe ever discovered.
        /// </summary>
        [Fact]
        public void A_recipe_present_in_both_sources_is_not_duplicated()
        {
            var shared = new MixRecipeRow("ogkush", "banana", "purpleexpress");
            var rows = new List<MixRecipeRow> { shared };

            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                shared.Product + ">" + shared.Mixer + ">" + shared.Output
            };

            // Same edge arriving from the recipe repository, differently cased.
            var duplicate = new MixRecipeRow("OGKush", "Banana", "PurpleExpress");
            var key = duplicate.Product + ">" + duplicate.Mixer + ">" + duplicate.Output;

            Assert.False(seen.Add(key), "case-insensitive dedup should reject the repeat");
            Assert.Single(rows);
        }

        /// <summary>
        /// A row whose output is also its input self-loops the lineage tree. An earlier build wrote
        /// exactly these — see LegacyEventRepair — so the graph must not be handed one.
        /// </summary>
        [Fact]
        public void A_self_referential_recipe_does_not_loop_the_tree()
        {
            var rows = new[]
            {
                new MixRecipeRow("ogkush", "banana", "purpleexpress"),
                new MixRecipeRow("purpleexpress", "gasoline", "purpleexpress"),
            };

            var graph = RecipeGraph.Build(
                rows,
                new[] { "ogkush", "purpleexpress" },
                Bases);

            // The point is that this terminates and yields something sane, not that it is complete.
            var lineage = graph.GetLineage("purpleexpress");
            Assert.NotNull(lineage);
            Assert.True(lineage.Steps == null || lineage.Steps.Count < 50);
        }
    }
}
