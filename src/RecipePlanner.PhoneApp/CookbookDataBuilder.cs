using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Stats;
using RecipePlanner.Game.Binding;

namespace RecipePlanner.PhoneApp
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
        private RecipeGraph _graph;
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
            _graph = null;
            _names = null;

            // Product ids are player-generated and only unique within a save, so a cached sprite
            // from the previous character would show against a different product with the same id.
            IconSource.Clear();
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

            if (_catalog == null || _graph == null) return model;

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

            var entries = Cookbook.Compose(rows, _graph, stats, recipes);

            model.Sections = Cookbook.Build(
                entries, _graph, model.Query, Name,
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

                _graph = RecipeGraph.Build(
                    _catalog.Recipes,
                    _catalog.DiscoveredProductIds.Concat(_catalog.Products.Select(p => p.Id)),
                    _catalog.BaseProductIds);

                _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in _catalog.Products)
                    if (!string.IsNullOrEmpty(p.Id)) _names[p.Id] = p.Name ?? p.Id;

                if (_graph.Ambiguous.Count > 0)
                    _log.Warn($"{_graph.Ambiguous.Count} recipe row(s) could not be classified and were skipped.");
            }
            catch (Exception ex)
            {
                _log.Error("Could not build the cookbook catalogue: " + ex);
                _catalog = null;
            }
        }

        /// <summary>Always the game's own name for a product — ids are player-generated.</summary>
        private string Name(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return productId;
            return _names != null && _names.TryGetValue(productId, out var name) ? name : productId;
        }
    }
}
