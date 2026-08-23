using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Stats;
using RecipePlanner.Game.Binding;

namespace RecipePlanner.UI
{
    /// <summary>
    /// Turns the live game catalogue plus recorded statistics into the view model the screen draws.
    ///
    /// The catalogue is read once per save and cached: it only changes when the player discovers or
    /// names a product, and rebuilding it on every phone open would walk every product definition
    /// reflectively each time.
    /// </summary>
    public sealed class CookbookDataBuilder
    {
        private readonly ProductCatalogReader _reader;
        private readonly ILog _log;

        private ProductCatalog _catalog;
        private Dictionary<string, string> _names;

        public CookbookDataBuilder(ProductCatalogReader reader, ILog log)
        {
            _reader = reader;
            _log = log ?? NullLog.Instance;
        }

        /// <summary>Save folder of the active profile; the recipe fallback needs it.</summary>
        public string SaveFolderPath { get; set; }

        /// <summary>Drops the cache. Call on save load, and when a mix is named.</summary>
        public void Invalidate()
        {
            _catalog = null;
            _names = null;

            // Product ids are player-generated and only unique within a save, so a cached sprite
            // from the previous character would show against a different product with the same id.
            // Routed through the seam rather than calling IconSource directly: that type is Unity's
            // and lives in the Mono-only assembly. See RecipePlannerUI.CacheInvalidated.
            try { RecipePlannerUI.CacheInvalidated?.Invoke(); }
            catch (Exception ex) { _log.Warn("Icon cache invalidation failed: " + ex.Message); }
        }

        public CookbookViewModel Build(
            string profileLabel,
            PlayerStatistics stats,
            IRecipeRepository recipes,
            CookbookQuery query)
        {
            EnsureCatalog();

            var model = new CookbookViewModel
            {
                ProfileLabel = profileLabel,
                Stats = stats,
                Query = query ?? new CookbookQuery(),
                DisplayName = Name
            };

            if (_catalog == null) return model;

            // Built here rather than in EnsureCatalog because it depends on the recipe repository,
            // which changes the moment the player invents a mix — long before the game writes that
            // mix into the save file the catalogue is read from.
            var graph = BuildGraph(recipes);
            if (graph == null) return model;

            var rows = _catalog.Products.Select(p => new ProductRow
            {
                Id = p.Id,
                Name = p.Name,
                DrugType = p.DrugType,
                Effects = p.Effects,
                IsFavourite = p.IsFavourited,
                IsListed = p.IsListed,
                Price = p.Price,
                SuggestedPrice = p.SuggestedPrice,
                Addictiveness = p.Addictiveness
            });

            var entries = Cookbook.Compose(rows, graph, stats, recipes);

            model.Sections = Cookbook.Build(
                entries, graph, model.Query, Name,
                // Section order comes from the game's own base list, never a constant of ours.
                _catalog.BaseProductIds);

            return model;
        }

        private void EnsureCatalog()
        {
            if (_catalog != null) return;

            try
            {
                _catalog = _reader?.Read(SaveFolderPath);
                if (_catalog == null) return;

                _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _catalog.Products)
                    if (!string.IsNullOrEmpty(p.Id)) _names[p.Id] = p.Name ?? p.Id;
            }
            catch (Exception ex)
            {
                _log.Error("Could not build the cookbook catalogue: " + ex);
                _catalog = null;
            }
        }

        /// <summary>
        /// Lineage from the save file **plus** everything we have discovered ourselves.
        ///
        /// The save file's mix list only gains a recipe when the game next writes a save, so a mix
        /// invented this session is missing from it — and a product with no known parent falls into
        /// "origin unknown", which is precisely the recipe the player most wants to see placed. We
        /// already know the parent: production detection recorded it at the moment of the cook.
        ///
        /// Rebuilt on each open rather than cached with the catalogue, because the repository moves
        /// independently of it. The cost is a fold over a few hundred rows of plain data; the
        /// expensive part — the reflective catalogue read — stays cached.
        /// </summary>
        private RecipeGraph BuildGraph(IRecipeRepository recipes)
        {
            var rows = new List<MixRecipeRow>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in _catalog.Recipes ?? Enumerable.Empty<MixRecipeRow>())
            {
                if (row == null) continue;
                rows.Add(row);
                seen.Add(Key(row.Product, row.Mixer, row.Output));
            }

            var added = 0;
            foreach (var recipe in Own(recipes))
            {
                // One discovered recipe is one mixing step: BaseProductId is the immediate parent
                // and the last step is the ingredient added to it. Anything without all three sides
                // cannot form an edge, so it is skipped rather than guessed at.
                var mixer = LastStep(recipe);
                if (string.IsNullOrEmpty(recipe.BaseProductId) ||
                    string.IsNullOrEmpty(recipe.OutputProductId) ||
                    string.IsNullOrEmpty(mixer)) continue;

                // Self-referential rows would loop the lineage tree. LegacyEventRepair exists
                // because an earlier build wrote exactly these.
                if (string.Equals(recipe.BaseProductId, recipe.OutputProductId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(Key(recipe.BaseProductId, mixer, recipe.OutputProductId))) continue;

                rows.Add(new MixRecipeRow(recipe.BaseProductId, mixer, recipe.OutputProductId));
                added++;
            }

            if (added > 0)
                _log.Info($"Lineage: {rows.Count - added} recipe(s) from the save file plus {added} " +
                          "discovered this session that it does not know about yet.");

            var graph = RecipeGraph.Build(
                rows,
                _catalog.DiscoveredProductIds
                    .Concat(_catalog.Products.Select(p => p.Id))
                    .Concat(Own(recipes).Select(r => r.OutputProductId)),
                _catalog.BaseProductIds);

            if (graph.Ambiguous.Count > 0)
                _log.Warn($"{graph.Ambiguous.Count} recipe row(s) could not be classified and were skipped.");

            return graph;
        }

        private static IEnumerable<Recipe> Own(IRecipeRepository recipes) =>
            (recipes?.All() ?? Enumerable.Empty<Recipe>()).Where(r => r != null);

        private static string LastStep(Recipe recipe)
        {
            if (recipe.Steps == null) return null;
            for (var i = recipe.Steps.Count - 1; i >= 0; i--)
                if (!string.IsNullOrWhiteSpace(recipe.Steps[i])) return recipe.Steps[i];
            return null;
        }

        /// <summary>
        /// Separated deliberately: plain concatenation would make ("ab","c") collide with
        /// ("a","bc"). '>' is safe as a separator because the game's MakeIDFileSafe strips it
        /// from product ids, so it cannot occur inside one.
        /// </summary>
        private static string Key(string product, string mixer, string output) =>
            (product ?? "") + ">" + (mixer ?? "") + ">" + (output ?? "");

        /// <summary>Always the game's own name for a product — ids are player-generated.</summary>
        private string Name(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return productId;
            return _names != null && _names.TryGetValue(productId, out var name) ? name : productId;
        }
    }
}
