using System;
using System.Collections.Generic;
using System.Reflection;
using RecipePlanner.Core.Mixing;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Builds the mixing guide out of the live game.
    ///
    /// Read rather than tabulated, and read per save, because Schedule I can randomise its mix maps
    /// (`Game.json` → `UseRandomizedMixMaps`, audit §3). A chart shipped as a constant would be
    /// confidently wrong on exactly the saves that most need one, and a confidently wrong answer is
    /// the failure this project keeps deciding against.
    ///
    /// Everything here is reflective and every member it touches is in <see cref="HookTable"/>, so
    /// a game update that moves one is reported by the symbol check rather than discovered as an
    /// empty chart.
    /// </summary>
    public sealed class MixGuideReader
    {
        private readonly ILog _log;
        private readonly List<Assembly> _assemblies;
        private readonly Type _productManagerType;
        private readonly Type _registryType;

        /// <summary>The four per-drug-type maps the game exposes on ProductManager.</summary>
        private static readonly string[] MapMembers =
        {
            "WeedMixMap", "MethMixMap", "CokeMixMap", "ShroomMixMap",
        };

        private static readonly string[] MapDrugTypes =
        {
            "Marijuana", "Methamphetamine", "Cocaine", "Shrooms",
        };

        public MixGuideReader(IEnumerable<Assembly> assemblies, ILog log)
        {
            _log = log ?? NullLog.Instance;
            _assemblies = new List<Assembly>(assemblies ?? new Assembly[0]);

            _productManagerType = SymbolGuard.ResolveType(_assemblies, HookTable.NsProduct + "ProductManager");
            _registryType = SymbolGuard.ResolveType(_assemblies, "ScheduleOne.Registry");
        }

        public MixGuide Read()
        {
            var guide = new MixGuide();

            try
            {
                var manager = Singleton(_productManagerType);
                if (manager == null)
                {
                    _log.Warn("Mix guide unavailable: ProductManager is not up yet.");
                    return guide;
                }

                var effects = new Dictionary<string, EffectInfo>(StringComparer.OrdinalIgnoreCase);
                var maps = ReadMaps(manager, effects);

                ReadIngredients(manager, guide, effects);

                guide.Effects = new List<EffectInfo>(effects.Values);
                BuildTransforms(guide, maps);

                _log.Info($"Mix guide: {guide.Ingredients.Count} ingredients, {guide.Effects.Count} effects, " +
                          $"{guide.Transforms.Count} transformation(s)" +
                          (guide.TransformsApproximate ? " (derived locally)" : "") + ".");
            }
            catch (Exception ex)
            {
                _log.Warn("Could not build the mix guide: " + ex.Message);
            }

            return guide;
        }

        // ---- maps ----

        /// <summary>A map paired with the live object it came from, so the game can resolve points.</summary>
        private sealed class LoadedMap
        {
            public MixMap Model;
            public object Native;
        }

        private List<LoadedMap> ReadMaps(object manager, Dictionary<string, EffectInfo> effects)
        {
            var maps = new List<LoadedMap>();

            for (var i = 0; i < MapMembers.Length; i++)
            {
                var mapObject = Reflect.Get(manager, MapMembers[i]);
                if (mapObject == null) continue;

                var map = new MixMap
                {
                    DrugType = MapDrugTypes[i],
                    MapRadius = ReadFloat(mapObject, "MapRadius"),
                };

                foreach (var entry in Reflect.Enumerate(Reflect.Get(mapObject, "Effects")))
                {
                    if (entry == null) continue;

                    var property = Reflect.Get(entry, "Property");
                    var effect = Record(property, effects);
                    if (effect == null) continue;

                    var position = Reflect.Get(entry, "Position");
                    map.Regions.Add(new MapRegion
                    {
                        EffectId = effect.Id,
                        X = ReadFloat(position, "x"),
                        Y = ReadFloat(position, "y"),
                        Radius = ReadFloat(entry, "Radius"),
                    });
                }

                if (map.Regions.Count > 0) maps.Add(new LoadedMap { Model = map, Native = mapObject });
            }

            return maps;
        }

        // ---- ingredients ----

        /// <summary>
        /// Mixable ingredients, from <c>ValidMixIngredients</c> where the game has filled it in and
        /// from the item registry where it has not.
        ///
        /// The fallback is not defensive padding — the list was observed EMPTY at runtime on a save
        /// with a full cookbook, which is the same behaviour the audit records for
        /// <c>mixRecipes</c>: the game appears to populate these as things are learned rather than
        /// on load. Without the fallback the guide reports "0 ingredients" and every transformation
        /// silently disappears with them.
        /// </summary>
        private IEnumerable<object> MixableDefinitions(object manager, object registry)
        {
            // The definitions themselves, not their ids. Looking each id back up through
            // Registry.GetItem was a needless round trip AND a broken one: the game declares two
            // GetItem(String) overloads, one of them generic, so binding by signature is ambiguous
            // and threw for every single ingredient. The list already holds what is needed.
            var declared = new List<object>();
            foreach (var entry in Reflect.Enumerate(Reflect.Get(manager, "ValidMixIngredients")))
                if (entry != null) declared.Add(entry);

            if (declared.Count > 0) return declared;

            // Anything carrying an effect is a candidate; products carry effects too, so they are
            // subtracted rather than guessed at by name.
            var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var product in Reflect.Enumerate(Reflect.Get(manager, "AllProducts")))
            {
                var productId = Reflect.GetString(product, "ID");
                if (!string.IsNullOrEmpty(productId)) products.Add(productId);
            }

            var found = new List<object>();
            foreach (var pair in Reflect.Enumerate(Reflect.Get(registry, "ItemDictionary")))
            {
                var id = Reflect.AsString(Reflect.Get(pair, "Key"));
                if (string.IsNullOrEmpty(id) || products.Contains(id)) continue;

                var definition = Reflect.Get(pair, "Value");
                if (definition == null) continue;

                var carriesAnEffect = false;
                foreach (var property in Reflect.Enumerate(Reflect.Get(definition, "Properties")))
                    if (property != null) { carriesAnEffect = true; break; }

                if (carriesAnEffect) found.Add(definition);
            }

            _log.Info($"ValidMixIngredients was empty; found {found.Count} mixable item(s) in the registry.");
            return found;
        }

        private void ReadIngredients(object manager, MixGuide guide, Dictionary<string, EffectInfo> effects)
        {
            var registry = Singleton(_registryType);

            foreach (var definition in MixableDefinitions(manager, registry))
            {
                var id = Reflect.GetString(definition, "ID");
                if (string.IsNullOrEmpty(id)) continue;

                var info = new IngredientInfo
                {
                    Id = id,
                    Name = Reflect.GetString(definition, "Name") ?? id,
                    Price = ReadFloat(definition, "BasePurchasePrice"),
                };

                // An ingredient imparts one effect. Taking the first is not a guess: the game's own
                // mixing takes a single Property from the mixer, and an item with none simply adds
                // nothing — which is worth showing rather than hiding.
                foreach (var property in Reflect.Enumerate(Reflect.Get(definition, "Properties")))
                {
                    var effect = Record(property, effects);
                    if (effect == null) continue;
                    info.EffectId = effect.Id;
                    break;
                }

                guide.Ingredients.Add(info);
            }
        }

        // ---- transformations ----

        /// <summary>
        /// For every ingredient and every effect already on a product, work out what that effect
        /// becomes.
        ///
        /// The game's own <c>GetEffectAtPoint</c> is called wherever it can be reached, because it
        /// is the real answer rather than our reading of the geometry. <see cref="MixMapSolver"/>
        /// only stands in when that fails, and the guide is flagged approximate so the UI can say
        /// so — a derived answer and an authoritative one must not look identical to the player.
        /// </summary>
        private void BuildTransforms(MixGuide guide, List<LoadedMap> maps)
        {
            if (maps.Count == 0 || guide.Ingredients.Count == 0) return;

            var usedFallback = false;

            foreach (var loaded in maps)
            {
                var map = loaded.Model;
                var native = MapPointResolver.For(loaded.Native, _log);

                foreach (var ingredient in guide.Ingredients)
                {
                    var effect = guide.Effect(ingredient.EffectId);
                    if (effect == null) continue;
                    if (effect.MixMagnitude == 0f) continue;   // moves nothing, rewrites nothing

                    foreach (var region in map.Regions)
                    {
                        var x = region.X + effect.MixDirectionX * effect.MixMagnitude;
                        var y = region.Y + effect.MixDirectionY * effect.MixMagnitude;

                        string landed;
                        if (native != null)
                        {
                            landed = native.EffectIdAt(x, y);
                        }
                        else
                        {
                            usedFallback = true;
                            landed = MixMapSolver.EffectAtPoint(map, x, y);
                        }

                        if (string.IsNullOrEmpty(landed)) continue;
                        if (string.Equals(landed, region.EffectId, StringComparison.OrdinalIgnoreCase)) continue;

                        guide.Transforms.Add(new MixTransform
                        {
                            IngredientId = ingredient.Id,
                            FromEffectId = region.EffectId,
                            ToEffectId = landed,
                            DrugType = map.DrugType,
                        });
                    }
                }
            }

            guide.TransformsAvailable = guide.Transforms.Count > 0;
            guide.TransformsApproximate = usedFallback;
        }

        // ---- helpers ----

        /// <summary>
        /// Records an effect the first time it is seen and returns it. Effects arrive from two
        /// directions — the maps and the item definitions — and the same ScriptableObject appears
        /// in both, so they are keyed by id.
        /// </summary>
        private EffectInfo Record(object property, Dictionary<string, EffectInfo> effects)
        {
            if (property == null) return null;

            var id = Reflect.GetString(property, "ID");
            if (string.IsNullOrEmpty(id)) id = Reflect.GetString(property, "Name");
            if (string.IsNullOrEmpty(id)) return null;

            EffectInfo existing;
            if (effects.TryGetValue(id, out existing)) return existing;

            var direction = Reflect.Get(property, "MixDirection");
            var colour = Reflect.Get(property, "LabelColor");

            var info = new EffectInfo
            {
                Id = id,
                Name = Reflect.GetString(property, "Name") ?? id,
                Description = Reflect.GetString(property, "Description"),
                Tier = Reflect.GetInt(property, "Tier"),
                Addictiveness = ReadFloat(property, "Addictiveness"),
                ValueChange = Reflect.GetInt(property, "ValueChange"),
                ValueMultiplier = ReadFloat(property, "ValueMultiplier"),
                MixDirectionX = ReadFloat(direction, "x"),
                MixDirectionY = ReadFloat(direction, "y"),
                MixMagnitude = ReadFloat(property, "MixMagnitude"),
            };

            if (colour != null)
            {
                info.ColourR = ReadFloat(colour, "r", 1f);
                info.ColourG = ReadFloat(colour, "g", 1f);
                info.ColourB = ReadFloat(colour, "b", 1f);
            }

            effects[id] = info;
            return info;
        }


        private static float ReadFloat(object instance, string member, float fallback = 0f)
        {
            var value = Reflect.Get(instance, member);
            if (value == null) return fallback;
            try { return Convert.ToSingle(value); }
            catch { return fallback; }
        }

        private object Singleton(Type type)
        {
            if (type == null) return null;
            return Reflect.GetStatic(type, "Instance") ?? Reflect.GetStatic(type, "instance");
        }
    }
}
