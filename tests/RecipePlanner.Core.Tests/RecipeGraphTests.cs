using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    public class RecipeGraphTests
    {
        private static readonly string[] Products =
        {
            "ogkush", "thickmonkey", "deathfuel", "thickdick", "californiaghost",
            "californiacake", "ultracake", "thickcake", "shroom", "megasmegma"
        };

        private static RecipeGraph Build(params MixRecipeRow[] rows) =>
            RecipeGraph.Build(rows, Products);

        [Fact]
        public void Sides_are_identified_by_membership_not_by_field_name()
        {
            // Same recipe stored both ways round — the game really does this.
            var graph = Build(
                new MixRecipeRow("ogkush", "cuke", "thickmonkey"),        // conventional
                new MixRecipeRow("viagor", "thickmonkey", "deathfuel"));  // reversed

            Assert.Empty(graph.Ambiguous);
            Assert.All(graph.Steps, s => Assert.Contains(s.BaseProductId, Products));
            Assert.Equal("thickmonkey", graph.Steps[0].OutputProductId);
            Assert.Equal("viagor", graph.Steps[1].AdditiveId);
            Assert.Equal("thickmonkey", graph.Steps[1].BaseProductId);
        }

        [Fact]
        public void A_row_with_two_products_is_flagged_rather_than_guessed()
        {
            var graph = Build(new MixRecipeRow("ogkush", "shroom", "deathfuel"));

            Assert.Single(graph.Ambiguous);
            Assert.Empty(graph.Steps);
        }

        [Fact]
        public void Lineage_walks_all_the_way_back_to_the_base_strain()
        {
            var graph = Build(
                new MixRecipeRow("ogkush", "banana", "thickdick"),
                new MixRecipeRow("thickdick", "donut", "californiaghost"),
                new MixRecipeRow("californiaghost", "donut", "californiacake"));

            var lineage = graph.GetLineage("californiacake");

            Assert.True(lineage.IsComplete);
            Assert.Equal("ogkush", lineage.RootProductId);
            Assert.Equal(3, lineage.Depth);
            Assert.Equal(new[] { "banana", "donut", "donut" }, lineage.Additives);
            Assert.Equal("ogkush + banana + donut + donut", lineage.Describe());
        }

        [Fact]
        public void A_base_product_is_its_own_complete_lineage()
        {
            var lineage = Build().GetLineage("ogkush");

            Assert.True(lineage.IsComplete);
            Assert.Equal("ogkush", lineage.RootProductId);
            Assert.Equal(0, lineage.Depth);
        }

        [Fact]
        public void A_product_with_no_discovered_recipe_reports_origin_unknown()
        {
            var lineage = Build().GetLineage("deathfuel");

            Assert.False(lineage.IsComplete);
            Assert.Equal("(origin unknown)", lineage.Describe());
        }

        [Fact]
        public void A_self_loop_does_not_hang_or_become_its_own_ancestor()
        {
            // thickdick + paracetamol -> thickdick is a real row in real save data.
            var graph = Build(
                new MixRecipeRow("ogkush", "banana", "thickdick"),
                new MixRecipeRow("thickdick", "paracetamol", "thickdick"));

            var lineage = graph.GetLineage("thickdick");

            Assert.True(lineage.IsComplete);
            Assert.Equal("ogkush", lineage.RootProductId);
            Assert.Single(lineage.Steps);
        }

        [Fact]
        public void A_cycle_between_two_products_terminates()
        {
            var graph = RecipeGraph.Build(
                new[]
                {
                    new MixRecipeRow("thickdick", "donut", "californiaghost"),
                    new MixRecipeRow("californiaghost", "donut", "thickdick")
                },
                Products);

            var lineage = graph.GetLineage("californiaghost");
            Assert.False(lineage.IsComplete);   // never reaches a base, but returns
        }

        [Fact]
        public void The_tree_shows_the_progression_under_a_base()
        {
            var graph = Build(
                new MixRecipeRow("ogkush", "banana", "thickdick"),
                new MixRecipeRow("ogkush", "cuke", "thickmonkey"),
                new MixRecipeRow("thickdick", "donut", "californiaghost"));

            var tree = graph.BuildTree("ogkush");

            Assert.Equal(2, tree.Children.Count);
            var branch = tree.Children.Single(c => c.ProductId == "thickdick");
            Assert.Equal("banana", branch.AdditiveId);
            Assert.Equal("californiaghost", branch.Children.Single().ProductId);
            Assert.Equal(2, branch.Children.Single().Depth);
        }

        [Fact]
        public void Products_group_under_the_strain_they_descend_from()
        {
            var graph = Build(
                new MixRecipeRow("ogkush", "banana", "thickdick"),
                new MixRecipeRow("shroom", "chili", "megasmegma"));

            var groups = graph.GroupByBase(new[] { "thickdick", "megasmegma", "deathfuel" });

            Assert.Equal(new[] { "thickdick" }, groups["ogkush"]);
            Assert.Equal(new[] { "megasmegma" }, groups["shroom"]);
            Assert.Contains("deathfuel", groups["(unknown)"]);   // no discovered recipe
        }

        // ---------------- against the player's real save ----------------

        private static string RealProductsJson()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow", "TVGS", "Schedule I", "Saves");
            if (!Directory.Exists(root)) return null;

            return Directory.GetDirectories(root)
                .SelectMany(a => Directory.GetDirectories(a, "SaveGame_*"))
                .Select(d => Path.Combine(d, "Products.json"))
                .FirstOrDefault(File.Exists);
        }

        private static RecipeGraph BuildFromRealSave(out List<string> discovered)
        {
            discovered = null;
            var path = RealProductsJson();
            if (path == null) return null;

            var json = JObject.Parse(File.ReadAllText(path));
            discovered = json["DiscoveredProducts"]?.Select(t => (string)t).ToList() ?? new List<string>();

            var rows = (json["MixRecipes"] ?? new JArray())
                .Select(r => new MixRecipeRow((string)r["Product"], (string)r["Mixer"], (string)r["Output"]))
                .ToList();

            return RecipeGraph.Build(rows, discovered);
        }

        [SkippableFact]
        public void Every_row_of_a_real_save_classifies_without_ambiguity()
        {
            var graph = BuildFromRealSave(out _);
            Skip.If(graph is null, "Schedule I saves not present on this machine.");
            Skip.If(graph.Steps.Count == 0, "No recipes recorded in this save yet.");

            // This is the claim the whole feature rests on: membership beats field names.
            Assert.Empty(graph.Ambiguous);
        }

        [SkippableFact]
        public void Real_lineages_resolve_to_real_base_strains()
        {
            var graph = BuildFromRealSave(out var discovered);
            Skip.If(graph is null, "Schedule I saves not present on this machine.");
            Skip.If(graph.Steps.Count == 0, "No recipes recorded in this save yet.");

            var resolved = discovered
                .Where(p => !graph.IsBaseProduct(p))
                .Select(p => graph.GetLineage(p))
                .Where(l => l.IsComplete)
                .ToList();

            Assert.NotEmpty(resolved);
            Assert.All(resolved, l => Assert.True(graph.IsBaseProduct(l.RootProductId)));
            Assert.All(resolved, l => Assert.NotEmpty(l.Steps));
        }

        [SkippableFact]
        public void Real_data_terminates_rather_than_hanging()
        {
            var graph = BuildFromRealSave(out var discovered);
            Skip.If(graph is null, "Schedule I saves not present on this machine.");

            // Cycles and self-loops exist in real data; every lookup must return.
            foreach (var p in discovered) graph.GetLineage(p);
            foreach (var b in graph.BaseProducts) graph.BuildTree(b);
        }
    }
}
