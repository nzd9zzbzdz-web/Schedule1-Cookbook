using System;
using System.Collections.Generic;
using System.Reflection;
using RecipePlanner.Core.Pricing;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Prices read from the game rather than reimplemented.
    ///
    /// Product value comes from <c>ProductManager</c>'s own price table, which is the number the
    /// game itself quotes the player — reproducing its formula would drift the moment the devs
    /// tune it. Ingredient cost comes from the item definition's purchase price.
    ///
    /// Values are cached per save: they change only when the player re-prices a product, and a
    /// reflective walk of every definition on each batch would be wasteful.
    /// </summary>
    public sealed class GamePriceSource : IPriceSource
    {
        private readonly ILog _log;
        private readonly Type _productManagerType;
        private readonly Type _registryType;

        private readonly Dictionary<string, double> _productValues =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _ingredientCosts =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private bool _loaded;

        public GamePriceSource(IEnumerable<Assembly> assemblies, ILog log)
        {
            _log = log ?? NullLog.Instance;
            var list = new List<Assembly>(assemblies ?? new Assembly[0]);

            _productManagerType = SymbolGuard.ResolveType(list, HookTable.NsProduct + "ProductManager");
            _registryType = SymbolGuard.ResolveType(list, "ScheduleOne.Registry");
        }

        /// <summary>Drops the cache. Call on save load and when a product is re-priced.</summary>
        public void Invalidate()
        {
            _loaded = false;
            _productValues.Clear();
            _ingredientCosts.Clear();
        }

        public bool TryGetProductValue(string productId, string quality, out double unitValue)
        {
            unitValue = 0d;
            if (string.IsNullOrEmpty(productId)) return false;

            EnsureLoaded();
            return _productValues.TryGetValue(productId, out unitValue);
        }

        public bool TryGetIngredientCost(string ingredientId, out double unitCost)
        {
            unitCost = 0d;
            if (string.IsNullOrEmpty(ingredientId)) return false;

            EnsureLoaded();
            return _ingredientCosts.TryGetValue(ingredientId, out unitCost);
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;   // set first: a failure should not retry on every single batch

            try
            {
                LoadProductValues();
                LoadIngredientCosts();
                _log.Info($"Prices loaded: {_productValues.Count} products, {_ingredientCosts.Count} ingredients.");
            }
            catch (Exception ex)
            {
                _log.Warn("Could not load prices; monetary figures will stay at zero. " + ex.Message);
            }
        }

        /// <summary>
        /// Prefers the live listing price the player set, then the definition's own Price /
        /// MarketValue / BasePrice. Anything unresolved is simply absent, which the pricing engine
        /// treats as zero rather than guessing.
        /// </summary>
        private void LoadProductValues()
        {
            var manager = Singleton(_productManagerType);
            if (manager == null) return;

            // ProductPrices is a Dictionary<string, float> of what each product currently sells for.
            var priceTable = Reflect.Get(manager, "ProductPrices");
            foreach (var pair in EnumerateDictionary(priceTable))
            {
                var id = Reflect.AsString(pair.Key);
                if (string.IsNullOrEmpty(id)) continue;
                if (TryToDouble(pair.Value, out var value)) _productValues[id] = value;
            }

            foreach (var definition in Reflect.Enumerate(Reflect.Get(manager, "AllProducts")))
            {
                var id = Reflect.GetString(definition, "ID");
                if (string.IsNullOrEmpty(id) || _productValues.ContainsKey(id)) continue;

                foreach (var member in new[] { "Price", "MarketValue", "BasePrice" })
                {
                    if (!TryToDouble(Reflect.Get(definition, member), out var value) || value <= 0) continue;
                    _productValues[id] = value;
                    break;
                }
            }
        }

        /// <summary>
        /// Mixer items carry their shop price as <c>StorableItemDefinition.BasePurchasePrice</c>.
        ///
        /// Member names verified against the shipped Assembly-CSharp: Registry exposes
        /// <c>ItemDictionary</c> and <c>ItemRegistry</c> — not "Items" or "ItemDefinitions", which
        /// is what I assumed first. The dictionary is preferred; the list is the fallback.
        /// </summary>
        private void LoadIngredientCosts()
        {
            var registry = Singleton(_registryType);
            if (registry == null) return;

            foreach (var pair in EnumerateDictionary(Reflect.Get(registry, "ItemDictionary")))
                Record(Reflect.AsString(pair.Key), pair.Value);

            if (_ingredientCosts.Count > 0) return;

            foreach (var entry in Reflect.Enumerate(Reflect.Get(registry, "ItemRegistry")))
            {
                // The list may hold definitions directly or wrapper entries around them.
                var definition = Reflect.Get(entry, "Definition") ?? entry;
                Record(Reflect.GetString(definition, "ID"), definition);
            }
        }

        private void Record(string id, object definition)
        {
            if (string.IsNullOrEmpty(id) || definition == null || _ingredientCosts.ContainsKey(id)) return;

            if (TryToDouble(Reflect.Get(definition, "BasePurchasePrice"), out var price) && price > 0)
                _ingredientCosts[id] = price;
        }

        /// <summary>Walks a dictionary reflectively — the concrete generic type is unknown here.</summary>
        private static IEnumerable<KeyValuePair<object, object>> EnumerateDictionary(object dictionary)
        {
            foreach (var entry in Reflect.Enumerate(dictionary))
            {
                if (entry == null) continue;
                var key = Reflect.Get(entry, "Key");
                var value = Reflect.Get(entry, "Value");
                if (key != null) yield return new KeyValuePair<object, object>(key, value);
            }
        }

        private static bool TryToDouble(object value, out double result)
        {
            result = 0d;
            if (value == null) return false;
            try { result = Convert.ToDouble(value); return true; }
            catch { return false; }
        }

        private object Singleton(Type type)
        {
            if (type == null) return null;
            return Reflect.GetStatic(type, "Instance") ?? Reflect.GetStatic(type, "instance");
        }
    }
}
