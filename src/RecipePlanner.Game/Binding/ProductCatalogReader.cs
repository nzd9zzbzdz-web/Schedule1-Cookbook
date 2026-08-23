using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RecipePlanner.Core.Recipes;

namespace RecipePlanner.Game.Binding
{
    /// <summary>One product as the game knows it.</summary>
    public sealed class ProductInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string DrugType { get; set; }
        public List<string> Effects { get; set; } = new List<string>();
        public bool IsDiscovered { get; set; }
        public bool IsFavourited { get; set; }
        public bool IsListed { get; set; }
        public float Price { get; set; }

        /// <summary>The game's own suggested price — ProductDefinition.MarketValue.</summary>
        public float SuggestedPrice { get; set; }

        /// <summary>0..1, matching what the Products app shows as a percentage.</summary>
        public float Addictiveness { get; set; }

        public override string ToString() => $"{Name ?? Id} ({DrugType})";
    }

    /// <summary>Everything the cookbook needs, read once per save load.</summary>
    public sealed class ProductCatalog
    {
        public List<ProductInfo> Products { get; set; } = new List<ProductInfo>();
        public List<MixRecipeRow> Recipes { get; set; } = new List<MixRecipeRow>();
        public List<string> DiscoveredProductIds { get; set; } = new List<string>();
        public List<string> ValidMixIngredients { get; set; } = new List<string>();

        /// <summary>
        /// The game's own base products, from <c>DefaultKnownProducts</c> plus the per-drug-type
        /// defaults. Read rather than assumed: every product below a base is named by the player
        /// and its id derives from that name, so nothing downstream can be hard-coded — and a game
        /// update could add a strain.
        /// </summary>
        public List<string> BaseProductIds { get; set; } = new List<string>();

        public bool IsUsable => Products.Count > 0 || Recipes.Count > 0;
    }

    /// <summary>
    /// Pulls the product and recipe tables out of the live <c>ProductManager</c>.
    ///
    /// Products, discovery flags and prices come from the running game, because with
    /// <c>UseRandomizedMixMaps</c> enabled they differ per save (audit §3) and only the live manager
    /// is right for the save currently loaded.
    ///
    /// Recipes need a fallback. <c>mixRecipes</c> was observed empty at runtime on a save holding
    /// 81 recorded recipes — the game seems to fill it as recipes are learned rather than on load —
    /// so <c>Products.json</c> is read when the runtime list comes back with nothing.
    /// </summary>
    public sealed class ProductCatalogReader
    {
        private readonly ILog _log;
        private readonly Type _productManagerType;

        public ProductCatalogReader(IEnumerable<Assembly> assemblies, ILog log)
        {
            _log = log ?? NullLog.Instance;
            _productManagerType = SymbolGuard.ResolveType(
                new List<Assembly>(assemblies ?? new Assembly[0]), HookTable.NsProduct + "ProductManager");
        }

        /// <summary>
        /// Reads the catalogue. <paramref name="saveFolderPath"/> is optional but strongly
        /// recommended: the runtime recipe list is frequently empty (see <see cref="ReadRecipes"/>),
        /// and the save folder is the fallback.
        /// </summary>
        public ProductCatalog Read(string saveFolderPath = null)
        {
            var catalog = new ProductCatalog();

            var manager = _productManagerType == null
                ? null
                : Reflect.GetStatic(_productManagerType, "Instance") ?? Reflect.GetStatic(_productManagerType, "instance");

            if (manager == null)
            {
                _log.Warn("ProductManager not available yet — cookbook will stay empty this session.");
                return catalog;
            }

            catalog.DiscoveredProductIds = ReadIdList(manager, "DiscoveredProducts");
            catalog.ValidMixIngredients = ReadIdList(manager, "ValidMixIngredients");
            catalog.BaseProductIds = ReadBaseProducts(manager);

            var favourites = new HashSet<string>(ReadIdList(manager, "FavouritedProducts"), StringComparer.OrdinalIgnoreCase);
            var listed = new HashSet<string>(ReadIdList(manager, "ListedProducts"), StringComparer.OrdinalIgnoreCase);
            var discovered = new HashSet<string>(catalog.DiscoveredProductIds, StringComparer.OrdinalIgnoreCase);

            foreach (var definition in Reflect.Enumerate(Reflect.Get(manager, "AllProducts")))
            {
                var id = Reflect.GetString(definition, "ID");
                if (string.IsNullOrEmpty(id)) continue;

                catalog.Products.Add(new ProductInfo
                {
                    Id = id,
                    Name = Reflect.GetString(definition, "Name") ?? id,
                    DrugType = Reflect.AsString(Reflect.Get(definition, "DrugType")),
                    Effects = ReadEffects(definition),
                    IsDiscovered = discovered.Contains(id),
                    IsFavourited = favourites.Contains(id),
                    IsListed = listed.Contains(id),
                    Price = ReadPrice(definition),
                    SuggestedPrice = ReadFloat(definition, "MarketValue"),
                    Addictiveness = ReadAddictiveness(definition)
                });
            }

            catalog.Recipes = ReadRecipes(manager);
            var source = "runtime";

            // Observed live: ProductManager.mixRecipes reads as empty even on a save with 81
            // recorded recipes — the game appears to populate it as recipes are learned rather
            // than on load. Products.json is the persisted record and is authoritative for
            // anything discovered in an earlier session.
            if (catalog.Recipes.Count == 0 && !string.IsNullOrEmpty(saveFolderPath))
            {
                catalog.Recipes = ReadRecipesFromSave(saveFolderPath);
                source = "save file";
            }

            _log.Info($"Catalog: {catalog.Products.Count} products, {catalog.Recipes.Count} recipes " +
                      $"({source}), {catalog.DiscoveredProductIds.Count} discovered.");
            return catalog;
        }

        /// <summary>
        /// Rows come back exactly as stored — including the ones with the sides reversed. Sorting
        /// that out is <see cref="RecipeGraph"/>'s job, and it must be done by membership rather
        /// than by field name (audit §2.7).
        /// </summary>
        private static List<MixRecipeRow> ReadRecipes(object manager)
        {
            var rows = new List<MixRecipeRow>();
            foreach (var entry in Reflect.Enumerate(Reflect.Get(manager, "mixRecipes")))
            {
                var product = Reflect.GetString(entry, "Product");
                var mixer = Reflect.GetString(entry, "Mixer");
                var output = Reflect.GetString(entry, "Output");

                // The runtime type may hold definitions rather than plain strings.
                product = product ?? Reflect.GetString(Reflect.Get(entry, "Product"), "ID");
                mixer = mixer ?? Reflect.GetString(Reflect.Get(entry, "Mixer"), "ID");
                output = output ?? Reflect.GetString(Reflect.Get(entry, "Output"), "ID");

                if (!string.IsNullOrEmpty(output))
                    rows.Add(new MixRecipeRow(product, mixer, output));
            }
            return rows;
        }

        /// <summary>
        /// Base products straight from the game: the <c>DefaultKnownProducts</c> list plus the four
        /// per-drug-type defaults, which cover the case where a strain is a default without being
        /// in the list.
        /// </summary>
        private static List<string> ReadBaseProducts(object manager)
        {
            var bases = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string id)
            {
                if (!string.IsNullOrEmpty(id) && id != "null" && seen.Add(id)) bases.Add(id);
            }

            foreach (var id in ReadIdList(manager, "DefaultKnownProducts")) Add(id);

            foreach (var member in new[] { "DefaultWeed", "DefaultMeth", "DefaultCocaine", "DefaultShroom" })
            {
                var definition = Reflect.Get(manager, member);
                Add(Reflect.GetString(definition, "ID") ?? Reflect.AsString(definition));
            }

            return bases;
        }

        /// <summary>
        /// Reads MixRecipes out of the save's Products.json.
        ///
        /// Rows come back exactly as stored, sides and all — including the roughly one in five the
        /// game writes reversed. Sorting that out is <see cref="RecipeGraph"/>'s job and must be
        /// done by membership, never by field name (audit §2.7).
        /// </summary>
        private List<MixRecipeRow> ReadRecipesFromSave(string saveFolderPath)
        {
            var rows = new List<MixRecipeRow>();
            try
            {
                var path = Path.Combine(saveFolderPath, "Products.json");
                if (!File.Exists(path)) return rows;

                var recipes = JObject.Parse(File.ReadAllText(path))["MixRecipes"];
                if (recipes == null) return rows;

                foreach (var row in recipes)
                {
                    var output = (string)row["Output"];
                    if (!string.IsNullOrEmpty(output))
                        rows.Add(new MixRecipeRow((string)row["Product"], (string)row["Mixer"], output));
                }
            }
            catch (Exception ex)
            {
                _log.Warn("Could not read recipes from the save folder: " + ex.Message);
            }
            return rows;
        }

        private static List<string> ReadIdList(object owner, string member)
        {
            var list = new List<string>();
            foreach (var item in Reflect.Enumerate(Reflect.Get(owner, member)))
            {
                var id = Reflect.GetString(item, "ID") ?? Reflect.AsString(item);
                if (!string.IsNullOrEmpty(id) && id != "null") list.Add(id);
            }
            return list;
        }

        private static List<string> ReadEffects(object definition)
        {
            var names = new List<string>();
            foreach (var property in Reflect.Enumerate(Reflect.Get(definition, "Properties")))
            {
                var name = Reflect.GetString(property, "Name") ?? Reflect.AsString(property);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }

        /// <summary>
        /// Addictiveness as the game itself reports it.
        ///
        /// GetAddictiveness() is a METHOD, not a field: it sums BaseAddictiveness with every
        /// property the product carries, then clamps to 0..1. Reading the bare BaseAddictiveness
        /// field would under-report every mixed product, because mixing is what adds the
        /// properties. The Products app calls this same method and prints floor(value * 100) + "%".
        /// </summary>
        private static float ReadAddictiveness(object definition)
        {
            try
            {
                var computed = Reflect.Call(definition, "GetAddictiveness");
                if (computed != null) return Convert.ToSingle(computed);
            }
            catch { /* fall through to the raw field */ }

            return ReadFloat(definition, "BaseAddictiveness");
        }

        private static float ReadFloat(object definition, string member)
        {
            var value = Reflect.Get(definition, member);
            if (value == null) return 0f;
            try { return Convert.ToSingle(value); } catch { return 0f; }
        }

        private static float ReadPrice(object definition)
        {
            foreach (var member in new[] { "Price", "MarketValue", "BasePrice" })
            {
                var value = Reflect.Get(definition, member);
                if (value == null) continue;
                try { return Convert.ToSingle(value); } catch { }
            }
            return 0f;
        }
    }
}
