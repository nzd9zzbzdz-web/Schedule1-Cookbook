using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Stats;

namespace RecipePlanner.Core.Recipes
{
    public enum CookbookSort
    {
        Name,
        TimesProduced,
        UnitsProduced,
        Value,
        Profit,
        RecentlyProduced,
        Addictiveness,
        ChainLength
    }

    /// <summary>One row in the cookbook: a product, how it is made, and how it has performed.</summary>
    public sealed class CookbookEntry
    {
        public string ProductId { get; set; }
        public string DisplayName { get; set; }
        public string DrugType { get; set; }
        public List<string> Effects { get; set; } = new List<string>();

        /// <summary>The strain or base drug this descends from; null when the origin is unknown.</summary>
        public string RootProductId { get; set; }
        public List<ResolvedStep> Steps { get; set; } = new List<ResolvedStep>();
        public bool OriginKnown { get; set; }
        public int ChainLength => Steps.Count;

        public bool IsFavourite { get; set; }
        public bool IsHidden { get; set; }
        public float SuggestedPrice { get; set; }
        public float Addictiveness { get; set; }
        public bool IsListed { get; set; }

        public long TimesProduced { get; set; }
        public long UnitsProduced { get; set; }
        public double TotalValue { get; set; }
        public double TotalProfit { get; set; }
        public DateTime? LastProducedUtc { get; set; }
        public float UnitPrice { get; set; }

        /// <summary>e.g. "OG Kush + Cuke + Viagor".</summary>
        public string RecipeText =>
            OriginKnown && Steps.Count > 0
                ? RootProductId + " + " + string.Join(" + ", Steps.Select(s => s.AdditiveId))
                : "(origin unknown)";
    }

    /// <summary>A collapsible strain section, with its progression tree.</summary>
    public sealed class CookbookSection
    {
        public string RootProductId { get; set; }
        public string DisplayName { get; set; }
        public bool IsUnknownOrigin { get; set; }
        public List<CookbookEntry> Entries { get; set; } = new List<CookbookEntry>();
        public LineageNode Tree { get; set; }
        public int Count => Entries.Count;
        public long TotalUnits => Entries.Sum(e => e.UnitsProduced);
    }

    public sealed class CookbookQuery
    {
        public string Search { get; set; }
        public CookbookSort Sort { get; set; } = CookbookSort.Name;
        public bool Descending { get; set; }
        /// <summary>
        /// Removes hidden recipes from the list entirely.
        ///
        /// Off by default: hiding sinks a recipe to the bottom and greys it, which keeps the way
        /// back visible. This is the escape hatch for a player who has hidden dozens and wants them
        /// gone from view — the recipes are still there and still recorded either way.
        ///
        /// Replaces an earlier ShowHidden flag whose default did the opposite, removing hidden
        /// recipes unless the player went looking for a toggle. That made hiding look like
        /// deleting.
        /// </summary>
        public bool CollapseHidden { get; set; }
        public bool FavouritesOnly { get; set; }
        public bool ProducedOnly { get; set; }
        public string DrugType { get; set; }
    }

    /// <summary>
    /// Assembles what the phone app renders: products grouped under the strain they descend from,
    /// each carrying its full recipe chain and lifetime numbers.
    ///
    /// Pure — no game types, no I/O — so the whole view can be exercised in tests before any UI
    /// exists.
    /// </summary>
    public static class Cookbook
    {
        /// <summary>
        /// Preferred order of the strain sections, supplied by the caller from the game's own
        /// DefaultKnownProducts.
        ///
        /// Deliberately not hard-coded. Base products are fixed game content today, but a game
        /// update could add a strain — and everything below a base is player-named per save, so
        /// nothing derived may ever be baked in.
        /// </summary>
        public static IReadOnlyList<string> DefaultSectionOrder { get; set; } = new string[0];

        public const string UnknownOrigin = "(unknown)";

        public static List<CookbookSection> Build(
            IEnumerable<CookbookEntry> entries,
            RecipeGraph graph,
            CookbookQuery query = null,
            Func<string, string> displayName = null,
            IReadOnlyList<string> sectionOrder = null)
        {
            query = query ?? new CookbookQuery();
            displayName = displayName ?? (id => id);

            var filtered = Filter(entries ?? Enumerable.Empty<CookbookEntry>(), query).ToList();

            var sections = filtered
                .GroupBy(e => e.OriginKnown ? (e.RootProductId ?? UnknownOrigin) : UnknownOrigin,
                         StringComparer.OrdinalIgnoreCase)
                .Select(g => new CookbookSection
                {
                    RootProductId = g.Key,
                    DisplayName = g.Key == UnknownOrigin ? "Origin unknown" : displayName(g.Key),
                    IsUnknownOrigin = g.Key == UnknownOrigin,
                    Entries = Sort(g, query).ToList(),
                    Tree = g.Key == UnknownOrigin ? null : graph?.BuildTree(g.Key)
                })
                .ToList();

            sections.Sort(SectionComparer(sectionOrder));
            return sections;
        }

        private static Comparison<CookbookSection> SectionComparer(IReadOnlyList<string> order)
        {
            var known = order ?? DefaultSectionOrder ?? new string[0];

            return (a, b) =>
            {
                // Unknown-origin always sinks to the bottom; it is a dumping ground, not a strain.
                if (a.IsUnknownOrigin != b.IsUnknownOrigin) return a.IsUnknownOrigin ? 1 : -1;

                var ia = IndexOf(known, a.RootProductId);
                var ib = IndexOf(known, b.RootProductId);

                return ia != ib
                    ? ia.CompareTo(ib)
                    : string.Compare(a.RootProductId, b.RootProductId, StringComparison.OrdinalIgnoreCase);
            };
        }

        private static int IndexOf(IReadOnlyList<string> order, string value)
        {
            for (var i = 0; i < order.Count; i++)
                if (string.Equals(order[i], value, StringComparison.OrdinalIgnoreCase)) return i;
            return int.MaxValue;   // unlisted sections follow the listed ones, alphabetically
        }

        public static IEnumerable<CookbookEntry> Filter(IEnumerable<CookbookEntry> entries, CookbookQuery query)
        {
            foreach (var e in entries)
            {
                if (e == null) continue;

                // Hidden entries are kept unless the player explicitly collapses them away. They
                // sink to the bottom of their section and render greyed instead of vanishing —
                // hiding a recipe used to remove it from the list entirely, which looked exactly
                // like deletion even though nothing was ever deleted, and left no obvious way back.
                if (e.IsHidden && query.CollapseHidden) continue;
                if (query.FavouritesOnly && !e.IsFavourite) continue;
                if (query.ProducedOnly && e.TimesProduced == 0) continue;

                if (!string.IsNullOrWhiteSpace(query.DrugType) &&
                    !string.Equals(e.DrugType, query.DrugType, StringComparison.OrdinalIgnoreCase)) continue;

                if (!string.IsNullOrWhiteSpace(query.Search) && !Matches(e, query.Search)) continue;

                yield return e;
            }
        }

        /// <summary>
        /// Searches the name, the product id, the effects AND the ingredients — so "banana" finds
        /// every mix that uses it, which is the question a player with hundreds of recipes actually
        /// asks.
        /// </summary>
        private static bool Matches(CookbookEntry e, string search)
        {
            if (Contains(e.DisplayName, search) || Contains(e.ProductId, search)) return true;
            if (Contains(e.RootProductId, search)) return true;

            foreach (var effect in e.Effects) if (Contains(effect, search)) return true;
            foreach (var step in e.Steps) if (Contains(step.AdditiveId, search)) return true;

            return false;
        }

        private static bool Contains(string haystack, string needle) =>
            haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Favourites always sit at the top of their section — mirroring the game's own product app,
        /// which gives favourites a container of their own. Below that, the chosen key applies.
        ///
        /// Each key has a natural direction (biggest first for counts and money, A-Z for names);
        /// <see cref="CookbookQuery.Descending"/> inverts that key only, never the favourites.
        /// </summary>
        public static IEnumerable<CookbookEntry> Sort(IEnumerable<CookbookEntry> entries, CookbookQuery query)
        {
            // Hidden last, before anything else is considered — a hidden favourite still belongs at
            // the bottom, because the player has said they are done with it.
            var ordered = entries
                .OrderBy(e => e.IsHidden)
                .ThenByDescending(e => e.IsFavourite);
            var flip = query.Descending;

            switch (query.Sort)
            {
                case CookbookSort.TimesProduced:
                    ordered = Then(ordered, e => (double)e.TimesProduced, biggestFirst: !flip); break;
                case CookbookSort.UnitsProduced:
                    ordered = Then(ordered, e => (double)e.UnitsProduced, biggestFirst: !flip); break;
                case CookbookSort.Value:
                    // Sell price, NOT lifetime value produced.
                    //
                    // TotalValue is zero for every recipe the player has never cooked, so on a
                    // strain they have not worked through, every row tied at zero and the list
                    // fell through to the alphabetical tiebreak — sorting correctly on a column of
                    // zeros, and looking completely broken. Price is the number this column shows,
                    // so it is the number it sorts by; lifetime value breaks ties beneath it.
                    ordered = Then(ordered, SellPrice, biggestFirst: !flip);
                    ordered = Then(ordered, e => e.TotalValue, biggestFirst: !flip);
                    break;
                case CookbookSort.Profit:
                    ordered = Then(ordered, e => e.TotalProfit, biggestFirst: !flip); break;
                case CookbookSort.Addictiveness:
                    ordered = Then(ordered, e => (double)e.Addictiveness, biggestFirst: !flip); break;
                case CookbookSort.RecentlyProduced:
                    ordered = Then(ordered, e => (e.LastProducedUtc ?? DateTime.MinValue).Ticks, biggestFirst: !flip); break;
                case CookbookSort.ChainLength:
                    ordered = Then(ordered, e => (double)e.ChainLength, biggestFirst: flip); break;
                default:
                    ordered = flip
                        ? ordered.ThenByDescending(e => e.DisplayName ?? e.ProductId, StringComparer.OrdinalIgnoreCase)
                        : ordered.ThenBy(e => e.DisplayName ?? e.ProductId, StringComparer.OrdinalIgnoreCase);
                    break;
            }

            // Stable final tiebreak so the list never reshuffles between frames.
            return ordered.ThenBy(e => e.ProductId, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// What the product sells for: the player's asking price, or the game's suggested value
        /// when they have not set one. Mirrors what the row displays.
        /// </summary>
        public static double SellPrice(CookbookEntry entry) =>
            entry.UnitPrice > 0f ? entry.UnitPrice : entry.SuggestedPrice;

        private static IOrderedEnumerable<CookbookEntry> Then<TKey>(
            IOrderedEnumerable<CookbookEntry> source, Func<CookbookEntry, TKey> key, bool biggestFirst) =>
            biggestFirst ? source.ThenByDescending(key) : source.ThenBy(key);

        /// <summary>Joins game catalogue data, resolved lineage and recorded statistics into rows.</summary>
        public static List<CookbookEntry> Compose(
            IEnumerable<ProductRow> products,
            RecipeGraph graph,
            PlayerStatistics stats,
            IRecipeRepository recipes)
        {
            var list = new List<CookbookEntry>();

            foreach (var p in products ?? Enumerable.Empty<ProductRow>())
            {
                if (p?.Id == null) continue;

                var lineage = graph?.GetLineage(p.Id) ?? new RecipeLineage { ProductId = p.Id };
                var entry = new CookbookEntry
                {
                    ProductId = p.Id,
                    DisplayName = p.Name ?? p.Id,
                    DrugType = p.DrugType,
                    Effects = p.Effects ?? new List<string>(),
                    IsFavourite = p.IsFavourite,
                    IsListed = p.IsListed,
                    UnitPrice = p.Price,
                    SuggestedPrice = p.SuggestedPrice,
                    Addictiveness = p.Addictiveness,
                    RootProductId = lineage.RootProductId,
                    Steps = lineage.Steps,
                    OriginKnown = lineage.IsComplete
                };

                if (stats != null && stats.ByProduct.TryGetValue(p.Id, out var ps))
                {
                    entry.UnitsProduced = ps.Units;
                    entry.TimesProduced = ps.Batches;
                    entry.TotalValue = ps.Value;
                    entry.TotalProfit = ps.Profit;
                    entry.LastProducedUtc = ps.LastProducedUtc == default ? (DateTime?)null : ps.LastProducedUtc;
                }

                var recipeId = Recipe.ComputeId(entry.RootProductId, entry.Steps.Select(s => s.AdditiveId));
                var stored = recipes?.Get(recipeId);
                if (stored != null)
                {
                    entry.IsHidden = stored.IsHidden;
                    entry.IsFavourite |= stored.Has(RecipeStatus.Favourite);
                }

                list.Add(entry);
            }

            return list;
        }
    }

    /// <summary>Flat product record handed in by the binding layer. No game types.</summary>
    public sealed class ProductRow
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DrugType { get; set; }
        public List<string> Effects { get; set; }
        public bool IsFavourite { get; set; }
        public bool IsListed { get; set; }
        public float Price { get; set; }

        /// <summary>Game's suggested price, independent of what the player is asking.</summary>
        public float SuggestedPrice { get; set; }

        /// <summary>0..1. Rendered as a percentage, the way the game shows it.</summary>
        public float Addictiveness { get; set; }
    }
}
