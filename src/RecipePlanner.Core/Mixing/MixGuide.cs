using System;
using System.Collections.Generic;
using System.Linq;

namespace RecipePlanner.Core.Mixing
{
    /// <summary>
    /// One effect as the game defines it.
    ///
    /// Colour is stored as three floats rather than a UnityEngine.Color because this assembly has
    /// no game reference and must not gain one — that property is what lets Core be tested without
    /// launching anything.
    /// </summary>
    public sealed class EffectInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public int Tier { get; set; }
        public float Addictiveness { get; set; }

        /// <summary>Flat cash added by the effect, and the multiplier applied to base value.</summary>
        public int ValueChange { get; set; }
        public float ValueMultiplier { get; set; }

        /// <summary>The game's own label colour, 0..1 per channel.</summary>
        public float ColourR { get; set; } = 1f;
        public float ColourG { get; set; } = 1f;
        public float ColourB { get; set; } = 1f;

        /// <summary>
        /// How adding this effect shifts existing effects across the mix map — a unit direction and
        /// a distance. This is the whole mechanism behind "adding X turns Toxic into Sneaky": the
        /// old effect's point is moved by this vector and whatever region it lands in wins.
        /// </summary>
        public float MixDirectionX { get; set; }
        public float MixDirectionY { get; set; }
        public float MixMagnitude { get; set; }

        public override string ToString() => Name ?? Id;
    }

    /// <summary>A mixable ingredient and the effect it imparts.</summary>
    public sealed class IngredientInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public float Price { get; set; }

        /// <summary>The effect this ingredient adds. Null when the game lists none.</summary>
        public string EffectId { get; set; }

        public override string ToString() => Name ?? Id;
    }

    /// <summary>
    /// "Adding <see cref="IngredientId"/> to a product that already has <see cref="FromEffectId"/>
    /// turns that effect into <see cref="ToEffectId"/>."
    /// </summary>
    public sealed class MixTransform
    {
        public string IngredientId { get; set; }
        public string FromEffectId { get; set; }
        public string ToEffectId { get; set; }
        public string DrugType { get; set; }
    }

    /// <summary>
    /// Everything needed to answer "what does this ingredient do?" and "how do I get that effect?".
    ///
    /// Built from the live game rather than from a table copied off a wiki, because Schedule I can
    /// randomise its mix maps per save (`Game.json` → `UseRandomizedMixMaps`, audit §3). A static
    /// chart would be confidently wrong on exactly the saves that most need one.
    /// </summary>
    public sealed class MixGuide
    {
        public List<EffectInfo> Effects { get; set; } = new List<EffectInfo>();
        public List<IngredientInfo> Ingredients { get; set; } = new List<IngredientInfo>();
        public List<MixTransform> Transforms { get; set; } = new List<MixTransform>();

        /// <summary>
        /// False when the transformation table could not be derived at all — the ingredient list
        /// and effect reference are still usable, and the UI says so rather than showing an empty
        /// table that looks like "this ingredient changes nothing".
        /// </summary>
        public bool TransformsAvailable { get; set; }

        /// <summary>
        /// True when transforms were resolved by our own circle test rather than by the game's
        /// <c>GetEffectAtPoint</c>. Surfaced in the UI: a derived answer and an authoritative one
        /// should not look identical to the player.
        /// </summary>
        public bool TransformsApproximate { get; set; }

        public bool IsUsable => Ingredients.Count > 0 || Effects.Count > 0;

        // ---- lookups ----

        private Dictionary<string, EffectInfo> _byEffectId;

        public EffectInfo Effect(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return null;

            if (_byEffectId == null)
            {
                _byEffectId = new Dictionary<string, EffectInfo>(StringComparer.OrdinalIgnoreCase);
                foreach (var effect in Effects)
                    if (effect != null && !string.IsNullOrEmpty(effect.Id)) _byEffectId[effect.Id] = effect;
            }

            EffectInfo found;
            return _byEffectId.TryGetValue(effectId, out found) ? found : null;
        }

        /// <summary>Display name for an effect id, falling back to the id itself.</summary>
        public string EffectName(string effectId)
        {
            var effect = Effect(effectId);
            return effect != null && !string.IsNullOrEmpty(effect.Name) ? effect.Name : effectId;
        }

        // ---- the two questions the guide exists to answer ----

        /// <summary>
        /// "I am holding this ingredient — what happens?" Its own effect, plus every existing
        /// effect it rewrites. Ordered so the reference reads the same way twice.
        /// </summary>
        public List<MixTransform> ByIngredient(string ingredientId) =>
            Transforms
                .Where(t => t != null && string.Equals(t.IngredientId, ingredientId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => EffectName(t.FromEffectId), StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// "I want this effect — how do I get it?" Both routes: ingredients that add it outright,
        /// and every (existing effect + ingredient) pair that converts into it.
        /// </summary>
        public EffectRoutes RoutesTo(string effectId)
        {
            var routes = new EffectRoutes { EffectId = effectId };
            if (string.IsNullOrEmpty(effectId)) return routes;

            routes.AddedDirectlyBy = Ingredients
                .Where(i => i != null && string.Equals(i.EffectId, effectId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Name ?? i.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            routes.ConvertedFrom = Transforms
                .Where(t => t != null && string.Equals(t.ToEffectId, effectId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => EffectName(t.FromEffectId), StringComparer.OrdinalIgnoreCase)
                .ToList();

            return routes;
        }

        /// <summary>Ingredients sorted for display: cheapest first, then by name.</summary>
        public List<IngredientInfo> IngredientsByPrice() =>
            Ingredients
                .Where(i => i != null)
                .OrderBy(i => i.Price)
                .ThenBy(i => i.Name ?? i.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// Effects sorted by tier then name. Tier is the game's own notion of how valuable an
        /// effect is, so it is the order a player scanning for something good wants.
        /// </summary>
        public List<EffectInfo> EffectsByTier() =>
            Effects
                .Where(e => e != null)
                .OrderByDescending(e => e.Tier)
                .ThenBy(e => e.Name ?? e.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>Every way to reach one effect.</summary>
    public sealed class EffectRoutes
    {
        public string EffectId { get; set; }
        public List<IngredientInfo> AddedDirectlyBy { get; set; } = new List<IngredientInfo>();
        public List<MixTransform> ConvertedFrom { get; set; } = new List<MixTransform>();

        public bool IsEmpty => AddedDirectlyBy.Count == 0 && ConvertedFrom.Count == 0;
    }
}
