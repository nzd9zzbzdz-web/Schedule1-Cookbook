using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Recipes;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Hiding a recipe used to remove it from the list entirely. Nothing was ever deleted — the
    /// record, its history and its statistics all survived — but the row vanished, which is
    /// indistinguishable from deletion to the person who just clicked the button, and left no
    /// visible way back.
    ///
    /// Hidden entries now stay in the list, sink to the bottom and render greyed, with the same
    /// button restoring them.
    /// </summary>
    public class HiddenRecipeTests
    {
        private static CookbookEntry Entry(string id, bool hidden = false, bool favourite = false) =>
            new CookbookEntry
            {
                ProductId = id,
                DisplayName = id,
                RootProductId = "ogkush",
                OriginKnown = true,
                IsHidden = hidden,
                IsFavourite = favourite,
            };

        private static List<CookbookEntry> Ordered(params CookbookEntry[] entries) =>
            Cookbook.Sort(entries, new CookbookQuery()).ToList();

        [Fact]
        public void A_hidden_entry_stays_in_the_list_by_default()
        {
            var kept = Cookbook.Filter(new[] { Entry("gone", hidden: true) }, new CookbookQuery()).ToList();
            Assert.Single(kept);
        }

        [Fact]
        public void Collapsing_removes_it_only_when_asked()
        {
            var query = new CookbookQuery { CollapseHidden = true };
            Assert.Empty(Cookbook.Filter(new[] { Entry("gone", hidden: true) }, query));
        }

        [Fact]
        public void Hidden_entries_sink_below_visible_ones()
        {
            var order = Ordered(Entry("aaa", hidden: true), Entry("zzz"));
            Assert.Equal(new[] { "zzz", "aaa" }, order.Select(e => e.ProductId).ToArray());
        }

        /// <summary>
        /// Hidden beats favourite. Favourites normally pin to the top, but a hidden favourite is
        /// something the player has explicitly set aside, and pinning it would defeat the hiding.
        /// </summary>
        [Fact]
        public void A_hidden_favourite_still_sinks()
        {
            var order = Ordered(Entry("fav", hidden: true, favourite: true), Entry("plain"));
            Assert.Equal(new[] { "plain", "fav" }, order.Select(e => e.ProductId).ToArray());
        }

        [Fact]
        public void Favourites_still_pin_among_the_visible()
        {
            var order = Ordered(Entry("aaa"), Entry("zzz", favourite: true));
            Assert.Equal(new[] { "zzz", "aaa" }, order.Select(e => e.ProductId).ToArray());
        }
    }
}
