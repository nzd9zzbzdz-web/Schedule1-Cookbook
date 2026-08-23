using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Mixing;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// The mixing guide answers two questions: "what does this ingredient do?" and "how do I get
    /// that effect?". Both are derived, so both need pinning — a chart that is quietly wrong is
    /// worse than no chart, because the player acts on it.
    /// </summary>
    public class MixGuideTests
    {
        // A deliberately simple map: three effects in a row, one unit apart, radius 0.4 each, so a
        // shift of exactly 1.0 lands dead centre on a neighbour and 0.5 lands in the gap between.
        private static MixMap Line() => new MixMap
        {
            DrugType = "Marijuana",
            MapRadius = 10f,
            Regions = new List<MapRegion>
            {
                new MapRegion { EffectId = "calming", X = 0f, Y = 0f, Radius = 0.4f },
                new MapRegion { EffectId = "toxic",   X = 1f, Y = 0f, Radius = 0.4f },
                new MapRegion { EffectId = "sneaky",  X = 2f, Y = 0f, Radius = 0.4f },
            },
        };

        [Fact]
        public void A_point_inside_a_region_resolves_to_it()
        {
            Assert.Equal("toxic", MixMapSolver.EffectAtPoint(Line(), 1.1f, 0f));
        }

        [Fact]
        public void A_point_in_open_space_resolves_to_nothing()
        {
            // Halfway between two circles of radius 0.4 that are 1.0 apart.
            Assert.Null(MixMapSolver.EffectAtPoint(Line(), 0.5f, 0f));
        }

        /// <summary>
        /// Overlapping regions are real on the game's maps. Nearest centre wins, rather than
        /// whichever happens to come first in the list — list order is a serialisation detail, not
        /// a rule, and depending on it would make the chart unstable between saves.
        /// </summary>
        [Fact]
        public void Where_regions_overlap_the_nearest_centre_wins()
        {
            var map = new MixMap
            {
                Regions = new List<MapRegion>
                {
                    new MapRegion { EffectId = "wide",  X = 0f, Y = 0f, Radius = 5f },
                    new MapRegion { EffectId = "close", X = 3f, Y = 0f, Radius = 5f },
                },
            };

            Assert.Equal("close", MixMapSolver.EffectAtPoint(map, 3.2f, 0f));
            Assert.Equal("wide", MixMapSolver.EffectAtPoint(map, 0.5f, 0f));
        }

        [Fact]
        public void A_shift_moves_one_effect_into_another()
        {
            Assert.Equal("sneaky", MixMapSolver.Shift(Line(), "toxic", 1f, 0f, 1f));
        }

        /// <summary>
        /// Landing in open space, or back inside the same region, both mean "unchanged". Reporting
        /// them would fill the chart with rows saying Toxic becomes Toxic.
        /// </summary>
        [Fact]
        public void A_shift_that_changes_nothing_reports_nothing()
        {
            Assert.Null(MixMapSolver.Shift(Line(), "toxic", 1f, 0f, 0.5f));   // open space
            Assert.Null(MixMapSolver.Shift(Line(), "toxic", 1f, 0f, 0.1f));   // still itself
        }

        [Fact]
        public void An_unknown_effect_or_a_null_map_does_not_throw()
        {
            Assert.Null(MixMapSolver.Shift(Line(), "notaneffect", 1f, 0f, 1f));
            Assert.Null(MixMapSolver.Shift(null, "toxic", 1f, 0f, 1f));
            Assert.Null(MixMapSolver.EffectAtPoint(null, 0f, 0f));
            Assert.Null(MixMapSolver.EffectAtPoint(new MixMap { Regions = null }, 0f, 0f));
        }

        // ---- guide queries ----

        private static MixGuide Guide() => new MixGuide
        {
            Effects =
            {
                new EffectInfo { Id = "toxic",  Name = "Toxic",  Tier = 1 },
                new EffectInfo { Id = "sneaky", Name = "Sneaky", Tier = 3 },
                new EffectInfo { Id = "foggy",  Name = "Foggy",  Tier = 2 },
            },
            Ingredients =
            {
                new IngredientInfo { Id = "banana", Name = "Banana", Price = 2f, EffectId = "foggy" },
                new IngredientInfo { Id = "cuke",   Name = "Cuke",   Price = 1f, EffectId = "sneaky" },
                new IngredientInfo { Id = "chili",  Name = "Chili",  Price = 7f, EffectId = "sneaky" },
            },
            Transforms =
            {
                new MixTransform { IngredientId = "banana", FromEffectId = "toxic",  ToEffectId = "sneaky" },
                new MixTransform { IngredientId = "banana", FromEffectId = "foggy",  ToEffectId = "toxic" },
                new MixTransform { IngredientId = "cuke",   FromEffectId = "toxic",  ToEffectId = "foggy" },
            },
        };

        [Fact]
        public void An_ingredient_lists_everything_it_rewrites()
        {
            var banana = Guide().ByIngredient("banana");

            Assert.Equal(2, banana.Count);
            Assert.Contains(banana, t => t.FromEffectId == "toxic" && t.ToEffectId == "sneaky");
            Assert.Contains(banana, t => t.FromEffectId == "foggy" && t.ToEffectId == "toxic");
        }

        /// <summary>
        /// The planning question. Both routes matter: an ingredient that adds the effect outright,
        /// and one that converts something you already have into it.
        /// </summary>
        [Fact]
        public void An_effect_lists_both_routes_to_it()
        {
            var routes = Guide().RoutesTo("sneaky");

            Assert.Equal(new[] { "Chili", "Cuke" }, routes.AddedDirectlyBy.Select(i => i.Name).ToArray());
            Assert.Single(routes.ConvertedFrom);
            Assert.Equal("banana", routes.ConvertedFrom[0].IngredientId);
            Assert.False(routes.IsEmpty);
        }

        [Fact]
        public void An_effect_nothing_reaches_reports_empty_rather_than_throwing()
        {
            var routes = Guide().RoutesTo("nosucheffect");
            Assert.True(routes.IsEmpty);
            Assert.True(Guide().RoutesTo(null).IsEmpty);
        }

        [Fact]
        public void Effect_names_fall_back_to_the_id()
        {
            var guide = Guide();
            Assert.Equal("Toxic", guide.EffectName("toxic"));
            Assert.Equal("unknown", guide.EffectName("unknown"));
        }

        [Fact]
        public void Ingredients_sort_cheapest_first_and_effects_by_tier()
        {
            var guide = Guide();

            Assert.Equal(new[] { "Cuke", "Banana", "Chili" },
                         guide.IngredientsByPrice().Select(i => i.Name).ToArray());

            Assert.Equal(new[] { "Sneaky", "Foggy", "Toxic" },
                         guide.EffectsByTier().Select(e => e.Name).ToArray());
        }

        [Fact]
        public void An_empty_guide_is_not_usable_and_does_not_throw()
        {
            var empty = new MixGuide();

            Assert.False(empty.IsUsable);
            Assert.Empty(empty.ByIngredient("banana"));
            Assert.True(empty.RoutesTo("toxic").IsEmpty);
            Assert.Null(empty.Effect(null));
        }
    }
}
