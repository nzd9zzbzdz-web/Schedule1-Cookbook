using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Production;

namespace RecipePlanner.Core.Stats
{
    public sealed class Totals
    {
        public long UnitsProduced { get; set; }
        public long Batches { get; set; }
        public double TotalCost { get; set; }
        public double TotalValue { get; set; }
        public double EstimatedProfit { get; set; }
    }

    public sealed class ProductStat
    {
        public string ProductId { get; set; }
        public string DisplayName { get; set; }
        public string DrugType { get; set; }
        public long Units { get; set; }
        public long Batches { get; set; }
        public double Cost { get; set; }
        public double Value { get; set; }
        public double Profit => Value - Cost;
        public DateTime FirstProducedUtc { get; set; }
        public DateTime LastProducedUtc { get; set; }
    }

    public sealed class IngredientStat
    {
        public string IngredientId { get; set; }
        public long TimesUsed { get; set; }
        public long UnitsConsumed { get; set; }
        public double TotalCost { get; set; }
    }

    public sealed class RecipeStat
    {
        public string RecipeId { get; set; }
        public string DisplayName { get; set; }
        public long TimesCooked { get; set; }
        public long UnitsProduced { get; set; }
        public double TotalCost { get; set; }
        public double TotalValue { get; set; }
        public double EstimatedProfit => TotalValue - TotalCost;
        public DateTime FirstProducedUtc { get; set; }
        public DateTime LastProducedUtc { get; set; }
    }

    public sealed class Records
    {
        public string MostUsedRecipeId { get; set; }
        public string MostProducedProductId { get; set; }
        public string MostUsedIngredientId { get; set; }
        public string HighestValueRecipeId { get; set; }
        public string MostProfitableRecipeId { get; set; }
        public long LargestBatchUnits { get; set; }
        public string LargestBatchProductId { get; set; }
    }

    /// <summary>
    /// Derived aggregate. Always rebuildable from events.jsonl — which is simultaneously the
    /// crash-recovery story and the schema-migration story.
    /// </summary>
    public sealed class PlayerStatistics
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProfileId { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public long EventsFolded { get; set; }

        public Totals Personal { get; set; } = new Totals();
        public Dictionary<string, Totals> ByDrugType { get; set; } = new Dictionary<string, Totals>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, ProductStat> ByProduct { get; set; } = new Dictionary<string, ProductStat>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IngredientStat> ByIngredient { get; set; } = new Dictionary<string, IngredientStat>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RecipeStat> ByRecipe { get; set; } = new Dictionary<string, RecipeStat>(StringComparer.Ordinal);

        /// <summary>Transforms and other players' work — tracked, but never in Personal.</summary>
        public Totals Excluded { get; set; } = new Totals();
        public Dictionary<string, long> ExcludedByReason { get; set; } = new Dictionary<string, long>(StringComparer.Ordinal);

        public Records Records { get; set; } = new Records();
        /// <summary>
        /// Null-guarded because this type is deserialized from stats.json, and a file truncated by
        /// a crash mid-write can leave any of the dictionaries null. stats.json is a derived cache
        /// that is always rebuildable from the event log, so a damaged one must degrade to zero
        /// rather than throw — the alternative is an exception on the shutdown path.
        /// </summary>
        public long UniqueRecipesProduced => ByRecipe == null ? 0 : ByRecipe.Count;
    }

    /// <summary>Pure fold over the event log. No I/O, no game types, fully deterministic.</summary>
    public static class StatisticsService
    {
        public static PlayerStatistics Build(string profileId, IEnumerable<ProductionEvent> events, DateTime nowUtc)
        {
            var stats = new PlayerStatistics { ProfileId = profileId, GeneratedUtc = nowUtc };
            if (events == null) return stats;

            foreach (var e in events)
            {
                if (e == null) continue;
                if (profileId != null && !string.Equals(e.ProfileId, profileId, StringComparison.Ordinal)) continue;

                stats.EventsFolded++;

                if (!e.CountsTowardPersonalTotals)
                {
                    Accumulate(stats.Excluded, e);
                    var reason = e.Attribution != Attribution.Local
                        ? "attribution:" + e.Attribution
                        : "kind:" + e.Kind;
                    stats.ExcludedByReason.TryGetValue(reason, out var n);
                    stats.ExcludedByReason[reason] = n + 1;
                    continue;
                }

                Accumulate(stats.Personal, e);
                FoldDrugType(stats, e);
                FoldProduct(stats, e);
                FoldIngredients(stats, e);
                FoldRecipe(stats, e);
            }

            ComputeRecords(stats);
            return stats;
        }

        private static void Accumulate(Totals t, ProductionEvent e)
        {
            t.UnitsProduced += e.Quantity;
            t.Batches += 1;
            t.TotalCost += e.TotalCost;
            t.TotalValue += e.TotalValue;
            t.EstimatedProfit += e.EstimatedProfit;
        }

        private static void FoldDrugType(PlayerStatistics s, ProductionEvent e)
        {
            var key = string.IsNullOrWhiteSpace(e.DrugType) ? "Unknown" : e.DrugType;
            if (!s.ByDrugType.TryGetValue(key, out var t)) s.ByDrugType[key] = t = new Totals();
            Accumulate(t, e);
        }

        private static void FoldProduct(PlayerStatistics s, ProductionEvent e)
        {
            // ProductKey, not OutputProductId: an unnamed mix has no product yet and must not
            // collapse into a shared "unknown" bucket with every other unnamed mix.
            var key = e.ProductKey;
            if (!s.ByProduct.TryGetValue(key, out var p))
            {
                s.ByProduct[key] = p = new ProductStat
                {
                    ProductId = key,
                    DisplayName = e.OutputProductName ?? DescribeUnnamed(e),
                    DrugType = e.DrugType,
                    FirstProducedUtc = e.RealTimeUtc
                };
            }
            if (string.IsNullOrEmpty(p.DisplayName)) p.DisplayName = e.OutputProductName;
            p.Units += e.Quantity;
            p.Batches += 1;
            p.Cost += e.TotalCost;
            p.Value += e.TotalValue;
            if (e.RealTimeUtc < p.FirstProducedUtc) p.FirstProducedUtc = e.RealTimeUtc;
            if (e.RealTimeUtc > p.LastProducedUtc) p.LastProducedUtc = e.RealTimeUtc;
        }

        /// <summary>Readable stand-in until the player names the mix.</summary>
        private static string DescribeUnnamed(ProductionEvent e) =>
            $"Unnamed mix ({e.BaseProductId} + {e.IngredientId})";

        private static void FoldIngredients(PlayerStatistics s, ProductionEvent e)
        {
            var chain = (e.IngredientChain != null && e.IngredientChain.Count > 0)
                ? (IEnumerable<string>)e.IngredientChain
                : new[] { e.IngredientId };

            foreach (var ingredient in chain)
            {
                if (string.IsNullOrWhiteSpace(ingredient)) continue;
                if (!s.ByIngredient.TryGetValue(ingredient, out var stat))
                    s.ByIngredient[ingredient] = stat = new IngredientStat { IngredientId = ingredient };
                stat.TimesUsed += 1;
                stat.UnitsConsumed += e.Quantity;

                // Only when the event actually carries the breakdown. It was added after the first
                // release, so older events have none — and splitting their TotalCost across the
                // chain would be a guess, wrong whenever the ingredients differed in price. An
                // absent cost stays absent; the report hides the column rather than showing a
                // fabricated one.
                if (e.IngredientUnitCosts == null) continue;

                if (e.IngredientUnitCosts.TryGetValue(ingredient, out var unitCost))
                    stat.TotalCost += unitCost * e.Quantity;
            }
        }

        private static void FoldRecipe(PlayerStatistics s, ProductionEvent e)
        {
            var key = e.RecipeId ?? e.ComputeRecipeId();
            if (!s.ByRecipe.TryGetValue(key, out var r))
            {
                s.ByRecipe[key] = r = new RecipeStat
                {
                    RecipeId = key,
                    DisplayName = e.OutputProductName,
                    FirstProducedUtc = e.RealTimeUtc
                };
            }
            if (string.IsNullOrEmpty(r.DisplayName)) r.DisplayName = e.OutputProductName;
            r.TimesCooked += 1;
            r.UnitsProduced += e.Quantity;
            r.TotalCost += e.TotalCost;
            r.TotalValue += e.TotalValue;
            if (e.RealTimeUtc < r.FirstProducedUtc) r.FirstProducedUtc = e.RealTimeUtc;
            if (e.RealTimeUtc > r.LastProducedUtc) r.LastProducedUtc = e.RealTimeUtc;

            if (e.Quantity > s.Records.LargestBatchUnits)
            {
                s.Records.LargestBatchUnits = e.Quantity;
                s.Records.LargestBatchProductId = e.OutputProductId;
            }
        }

        private static void ComputeRecords(PlayerStatistics s)
        {
            s.Records.MostUsedRecipeId = Top(s.ByRecipe, r => r.TimesCooked);
            s.Records.HighestValueRecipeId = Top(s.ByRecipe, r => r.TotalValue);
            s.Records.MostProfitableRecipeId = Top(s.ByRecipe, r => r.EstimatedProfit);
            s.Records.MostProducedProductId = Top(s.ByProduct, p => p.Units);
            s.Records.MostUsedIngredientId = Top(s.ByIngredient, i => i.UnitsConsumed);
        }

        /// <summary>Highest scoring key; ties break on the key itself so results are deterministic.</summary>
        private static string Top<T>(Dictionary<string, T> map, Func<T, double> score)
        {
            if (map.Count == 0) return null;
            return map.OrderByDescending(kv => score(kv.Value))
                      .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                      .First().Key;
        }
    }
}
