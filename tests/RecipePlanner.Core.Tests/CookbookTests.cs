using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Stats;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// The cookbook view — the answer to "hundreds of recipes and it's hard to navigate".
    /// </summary>
    public class CookbookTests
    {
        private static readonly string[] KnownProducts =
        {
            "ogkush", "sourdiesel", "shroom",
            "thickmonkey", "deathfuel", "thickdick", "megasmegma", "mysteryproduct"
        };

        private static RecipeGraph Graph() => RecipeGraph.Build(
            new[]
            {
                new MixRecipeRow("ogkush", "cuke", "thickmonkey"),
                new MixRecipeRow("viagor", "thickmonkey", "deathfuel"),   // stored reversed
                new MixRecipeRow("ogkush", "banana", "thickdick"),
                new MixRecipeRow("shroom", "chili", "megasmegma")
            },
            KnownProducts);

        private static List<ProductRow> Products() => new List<ProductRow>
        {
            new ProductRow { Id = "thickmonkey", Name = "Thick Monkey", DrugType = "Marijuana", Effects = new List<string>{"Athletic"} },
            new ProductRow { Id = "deathfuel",   Name = "Death Fuel",   DrugType = "Marijuana", Effects = new List<string>{"Euphoric"} },
            new ProductRow { Id = "thickdick",   Name = "Thick Dick",   DrugType = "Marijuana", Effects = new List<string>{"Sedating"} },
            new ProductRow { Id = "megasmegma",  Name = "Mega Smegma",  DrugType = "Shrooms",   Effects = new List<string>{"Spicy"} },
            new ProductRow { Id = "mysteryproduct", Name = "Mystery",   DrugType = "Marijuana", Effects = new List<string>() }
        };

        private static PlayerStatistics Stats()
        {
            var s = new PlayerStatistics();
            s.ByProduct["deathfuel"] = new ProductStat
            {
                ProductId = "deathfuel", Units = 200, Batches = 10, Value = 6000, Cost = 1000,
                LastProducedUtc = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc)
            };
            s.ByProduct["thickmonkey"] = new ProductStat
            {
                ProductId = "thickmonkey", Units = 60, Batches = 3, Value = 900, Cost = 300,
                LastProducedUtc = new DateTime(2026, 8, 22, 0, 0, 0, DateTimeKind.Utc)
            };
            return s;
        }

        private static List<CookbookEntry> Entries(IRecipeRepository repo = null) =>
            Cookbook.Compose(Products(), Graph(), Stats(), repo ?? new InMemoryRecipeRepository());

        // ---------------- grouping by strain (requirement 6) ----------------

        [Fact]
        public void Products_are_grouped_under_the_strain_they_descend_from()
        {
            var sections = Cookbook.Build(Entries(), Graph());

            var ogkush = sections.Single(s => s.RootProductId == "ogkush");
            Assert.Equal(new[] { "Death Fuel", "Thick Dick", "Thick Monkey" },
                         ogkush.Entries.Select(e => e.DisplayName).OrderBy(n => n).ToArray());

            Assert.Single(sections.Single(s => s.RootProductId == "shroom").Entries);
        }

        [Fact]
        public void Strain_sections_come_in_a_stable_deliberate_order()
        {
            var sections = Cookbook.Build(Entries(), Graph());
            var order = sections.Select(s => s.RootProductId).ToList();

            Assert.True(order.IndexOf("ogkush") < order.IndexOf("shroom"));
            Assert.Equal(Cookbook.UnknownOrigin, order.Last());   // always sinks to the bottom
        }

        [Fact]
        public void A_product_with_no_discovered_recipe_lands_in_origin_unknown()
        {
            var sections = Cookbook.Build(Entries(), Graph());
            var unknown = sections.Single(s => s.IsUnknownOrigin);

            Assert.Equal("mysteryproduct", unknown.Entries.Single().ProductId);
            Assert.Equal("(origin unknown)", unknown.Entries.Single().RecipeText);
            Assert.Equal("Origin unknown", unknown.DisplayName);
        }

        // ---------------- the chain (requirements 3 and 4) ----------------

        [Fact]
        public void Each_entry_carries_its_full_chain_back_to_the_strain()
        {
            var deathfuel = Entries().Single(e => e.ProductId == "deathfuel");

            Assert.True(deathfuel.OriginKnown);
            Assert.Equal("ogkush", deathfuel.RootProductId);
            Assert.Equal(2, deathfuel.ChainLength);
            Assert.Equal("ogkush + cuke + viagor", deathfuel.RecipeText);
        }

        [Fact]
        public void Each_section_exposes_the_progression_tree()
        {
            var sections = Cookbook.Build(Entries(), Graph());
            var tree = sections.Single(s => s.RootProductId == "ogkush").Tree;

            Assert.Equal("ogkush", tree.ProductId);
            var monkey = tree.Children.Single(c => c.ProductId == "thickmonkey");
            Assert.Equal("cuke", monkey.AdditiveId);
            Assert.Equal("deathfuel", monkey.Children.Single().ProductId);
            Assert.Equal("viagor", monkey.Children.Single().AdditiveId);
        }

        // ---------------- hiding (requirement 2) ----------------

        [Fact]
        public void A_hidden_recipe_disappears_from_the_list_but_not_from_the_data()
        {
            var repo = new InMemoryRecipeRepository();
            var recipe = new Recipe { RecipeId = "ogkush>cuke", Name = "Thick Monkey" };
            recipe.SetHidden(true);
            repo.Upsert(recipe);

            var visible = Cookbook.Build(Entries(repo), Graph());
            Assert.DoesNotContain(visible.SelectMany(s => s.Entries), e => e.ProductId == "thickmonkey");

            // Still there, and its statistics are untouched.
            var all = Cookbook.Build(Entries(repo), Graph(), new CookbookQuery { ShowHidden = true });
            var entry = all.SelectMany(s => s.Entries).Single(e => e.ProductId == "thickmonkey");
            Assert.True(entry.IsHidden);
            Assert.Equal(60, entry.UnitsProduced);
        }

        [Fact]
        public void Hiding_is_reversible()
        {
            var recipe = new Recipe { RecipeId = "x" };
            recipe.SetHidden(true);
            Assert.True(recipe.IsHidden);

            recipe.SetHidden(false);
            Assert.False(recipe.IsHidden);
            Assert.Equal(RecipeStatus.None, recipe.Status);
        }

        // ---------------- sorting and search (requirements 1 and 5) ----------------

        [Fact]
        public void Sorting_by_units_puts_the_biggest_earner_first()
        {
            var sections = Cookbook.Build(Entries(), Graph(), new CookbookQuery { Sort = CookbookSort.UnitsProduced });
            var ogkush = sections.Single(s => s.RootProductId == "ogkush");

            Assert.Equal("deathfuel", ogkush.Entries.First().ProductId);
        }

        [Fact]
        public void Sorting_by_recency_puts_the_latest_cook_first()
        {
            var sections = Cookbook.Build(Entries(), Graph(), new CookbookQuery { Sort = CookbookSort.RecentlyProduced });
            var ogkush = sections.Single(s => s.RootProductId == "ogkush");

            Assert.Equal("thickmonkey", ogkush.Entries.First().ProductId);
        }

        [Fact]
        public void Sorting_by_chain_length_surfaces_the_simplest_recipes()
        {
            var sections = Cookbook.Build(Entries(), Graph(), new CookbookQuery { Sort = CookbookSort.ChainLength });
            var ogkush = sections.Single(s => s.RootProductId == "ogkush");

            Assert.Equal(1, ogkush.Entries.First().ChainLength);
            Assert.Equal(2, ogkush.Entries.Last().ChainLength);
        }

        [Fact]
        public void Search_finds_a_recipe_by_the_ingredient_it_uses()
        {
            // The question a player with hundreds of mixes actually asks: "what uses banana?"
            var sections = Cookbook.Build(Entries(), Graph(), new CookbookQuery { Search = "banana" });
            var hits = sections.SelectMany(s => s.Entries).ToList();

            Assert.Single(hits);
            Assert.Equal("thickdick", hits[0].ProductId);
        }

        [Fact]
        public void Search_also_matches_name_and_effect()
        {
            Assert.Equal("deathfuel", Find("Death").Single().ProductId);
            Assert.Equal("megasmegma", Find("Spicy").Single().ProductId);

            List<CookbookEntry> Find(string term) =>
                Cookbook.Build(Entries(), Graph(), new CookbookQuery { Search = term })
                        .SelectMany(s => s.Entries).ToList();
        }

        [Fact]
        public void Filtering_by_drug_type_keeps_only_that_family()
        {
            var sections = Cookbook.Build(Entries(), Graph(), new CookbookQuery { DrugType = "Shrooms" });

            Assert.Equal(new[] { "megasmegma" },
                         sections.SelectMany(s => s.Entries).Select(e => e.ProductId).ToArray());
        }

        [Fact]
        public void Produced_only_hides_recipes_never_actually_cooked()
        {
            var sections = Cookbook.Build(Entries(), Graph(), new CookbookQuery { ProducedOnly = true });
            var ids = sections.SelectMany(s => s.Entries).Select(e => e.ProductId).ToList();

            Assert.Contains("deathfuel", ids);
            Assert.DoesNotContain("thickdick", ids);   // in the cookbook, never cooked
        }

        [Fact]
        public void Favourites_float_within_their_section()
        {
            var products = Products();
            products.Single(p => p.Id == "thickdick").IsFavourite = true;

            var entries = Cookbook.Compose(products, Graph(), Stats(), new InMemoryRecipeRepository());
            var sections = Cookbook.Build(entries, Graph(), new CookbookQuery { Sort = CookbookSort.UnitsProduced });
            var ogkush = sections.Single(s => s.RootProductId == "ogkush");

            // thickdick has zero units but is favourited, so it outranks the rest.
            Assert.Equal("thickdick", ogkush.Entries.First().ProductId);
        }

        [Fact]
        public void Statistics_are_joined_onto_the_entries()
        {
            var deathfuel = Entries().Single(e => e.ProductId == "deathfuel");

            Assert.Equal(200, deathfuel.UnitsProduced);
            Assert.Equal(10, deathfuel.TimesProduced);
            Assert.Equal(6000, deathfuel.TotalValue);
            Assert.Equal(5000, deathfuel.TotalProfit);
        }

        [Fact]
        public void An_empty_cookbook_produces_no_sections_rather_than_throwing()
        {
            Assert.Empty(Cookbook.Build(new List<CookbookEntry>(), Graph()));
            Assert.Empty(Cookbook.Build(null, null));
        }
    }
    /// <summary>
    /// Guards the path from the game's product definition to what a cookbook row shows.
    ///
    /// Price and addictiveness are copied through three types on the way to the screen, and a
    /// field that quietly fails to copy reads as a legitimately free, non-addictive product
    /// rather than as a bug — so it would be blamed on the game, not on us.
    /// </summary>
    public class ProductDetailPlumbingTests
    {
        private static CookbookEntry ComposeOne(ProductRow row)
        {
            var entries = Cookbook.Compose(
                new[] { row }, RecipeGraph.Build(null, new[] { row.Id }, new[] { row.Id }),
                new PlayerStatistics(), new InMemoryRecipeRepository());

            return Assert.Single(entries);
        }

        [Fact]
        public void CarriesPriceAddictivenessAndSuggestedPrice()
        {
            var entry = ComposeOne(new ProductRow
            {
                Id = "californiaghost",
                Name = "California Ghost",
                DrugType = "Marijuana",
                Price = 60f,
                SuggestedPrice = 58f,
                Addictiveness = 0.47f
            });

            Assert.Equal(60f, entry.UnitPrice);
            Assert.Equal(58f, entry.SuggestedPrice);
            Assert.Equal(0.47f, entry.Addictiveness, 3);
        }

        [Fact]
        public void MissingDetailStaysZeroRatherThanGuessing()
        {
            var entry = ComposeOne(new ProductRow { Id = "unpriced", Name = "Unpriced" });

            Assert.Equal(0f, entry.UnitPrice);
            Assert.Equal(0f, entry.SuggestedPrice);
            Assert.Equal(0f, entry.Addictiveness);
        }
    }
    /// <summary>
    /// The Value sort orders by the price shown on the row.
    ///
    /// It used to order by lifetime value produced, which is zero for every recipe never cooked —
    /// so on a strain the player had not worked through, every row tied and the list silently fell
    /// back to alphabetical. It looked broken while being, technically, correct.
    /// </summary>
    public class ValueSortTests
    {
        private static ProductRow Row(string id, float price, float suggested = 0f) =>
            new ProductRow { Id = id, Name = id, DrugType = "Marijuana", Price = price, SuggestedPrice = suggested };

        private static List<string> SortedIds(params ProductRow[] rows)
        {
            var ids = rows.Select(r => r.Id).ToArray();
            var entries = Cookbook.Compose(
                rows, RecipeGraph.Build(null, ids, ids), new PlayerStatistics(), new InMemoryRecipeRepository());

            var query = new CookbookQuery { Sort = CookbookSort.Value };
            return Cookbook.Sort(entries, query).Select(e => e.ProductId).ToList();
        }

        [Fact]
        public void OrdersByPriceEvenWhenNothingHasBeenProduced()
        {
            // Deliberately alphabetical-ascending ids with non-matching prices: if the sort falls
            // through to the name tiebreak again, this fails.
            var order = SortedIds(Row("aaa", 44f), Row("bbb", 76f), Row("ccc", 61f));

            Assert.Equal(new[] { "bbb", "ccc", "aaa" }, order);
        }

        [Fact]
        public void FallsBackToTheSuggestedPriceWhenNothingIsListed()
        {
            var order = SortedIds(Row("aaa", 0f, 90f), Row("bbb", 50f), Row("ccc", 0f, 10f));

            Assert.Equal(new[] { "aaa", "bbb", "ccc" }, order);
        }

        [Fact]
        public void DescendingInvertsIt()
        {
            var rows = new[] { Row("aaa", 44f), Row("bbb", 76f), Row("ccc", 61f) };
            var ids = rows.Select(r => r.Id).ToArray();
            var entries = Cookbook.Compose(
                rows, RecipeGraph.Build(null, ids, ids), new PlayerStatistics(), new InMemoryRecipeRepository());

            var order = Cookbook.Sort(entries, new CookbookQuery { Sort = CookbookSort.Value, Descending = true })
                                .Select(e => e.ProductId).ToList();

            Assert.Equal(new[] { "aaa", "ccc", "bbb" }, order);
        }
    }
    public class AddictivenessSortTests
    {
        private static List<string> SortedIds(bool descending, params (string id, float addictiveness)[] rows)
        {
            var products = rows
                .Select(r => new ProductRow
                {
                    Id = r.id, Name = r.id, DrugType = "Marijuana", Addictiveness = r.addictiveness
                })
                .ToArray();

            var ids = products.Select(r => r.Id).ToArray();
            var entries = Cookbook.Compose(
                products, RecipeGraph.Build(null, ids, ids), new PlayerStatistics(), new InMemoryRecipeRepository());

            return Cookbook
                .Sort(entries, new CookbookQuery { Sort = CookbookSort.Addictiveness, Descending = descending })
                .Select(e => e.ProductId)
                .ToList();
        }

        [Fact]
        public void MostAddictiveFirst()
        {
            // Ids ascend alphabetically while addictiveness does not, so a fall-through to the name
            // tiebreak — the failure the Value sort had — would be caught here too.
            var order = SortedIds(false, ("aaa", 0.35f), ("bbb", 1.0f), ("ccc", 0.05f));

            Assert.Equal(new[] { "bbb", "aaa", "ccc" }, order);
        }

        [Fact]
        public void DescendingInvertsIt()
        {
            var order = SortedIds(true, ("aaa", 0.35f), ("bbb", 1.0f), ("ccc", 0.05f));

            Assert.Equal(new[] { "ccc", "aaa", "bbb" }, order);
        }
    }
}
