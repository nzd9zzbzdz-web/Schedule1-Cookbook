using System;
using System.Collections.Generic;

namespace RecipePlanner.Core.Mixing
{
    /// <summary>One effect's circle on a mix map.</summary>
    public sealed class MapRegion
    {
        public string EffectId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Radius { get; set; }
    }

    /// <summary>A mix map for one drug type: a plane with an effect circle at each point.</summary>
    public sealed class MixMap
    {
        public string DrugType { get; set; }
        public float MapRadius { get; set; }
        public List<MapRegion> Regions { get; set; } = new List<MapRegion>();
    }

    /// <summary>
    /// Works out what an existing effect becomes when an ingredient is added.
    ///
    /// Schedule I resolves this spatially rather than with a lookup table: every effect occupies a
    /// circle on a 2D map, every effect also carries a <c>MixDirection</c> and <c>MixMagnitude</c>,
    /// and adding an ingredient shifts each existing effect's point by the ingredient's own
    /// direction and distance. Whatever circle the point lands in is what that effect turns into.
    ///
    /// This is a **fallback**. The game exposes <c>MixerMap.GetEffectAtPoint</c> and calling it is
    /// always preferred, because it is the real answer rather than our reading of it. This exists
    /// for when that method cannot be resolved — after a game update, say — so the guide degrades
    /// to "derived, and labelled as derived" instead of vanishing.
    ///
    /// Pure maths on floats, so it is testable without the game, which is the whole reason the
    /// geometry is read out into plain numbers rather than kept as Unity types.
    /// </summary>
    public static class MixMapSolver
    {
        /// <summary>
        /// The effect at a point, or null if the point is in open space.
        ///
        /// Circles can overlap. Where they do, the nearest centre wins: a point deep inside one
        /// effect and barely clipping another belongs to the one it is deep inside. Picking the
        /// first match in list order instead would make the result depend on however the game
        /// happened to serialise the list, which is not a rule at all.
        /// </summary>
        public static string EffectAtPoint(MixMap map, float x, float y)
        {
            if (map == null || map.Regions == null) return null;

            string best = null;
            var bestDistanceSquared = float.MaxValue;

            foreach (var region in map.Regions)
            {
                if (region == null || string.IsNullOrEmpty(region.EffectId)) continue;

                var dx = x - region.X;
                var dy = y - region.Y;
                var distanceSquared = dx * dx + dy * dy;

                if (distanceSquared > region.Radius * region.Radius) continue;
                if (distanceSquared >= bestDistanceSquared) continue;

                bestDistanceSquared = distanceSquared;
                best = region.EffectId;
            }

            return best;
        }

        /// <summary>
        /// Where <paramref name="fromEffectId"/> ends up after the shift, or null when nothing
        /// changes.
        ///
        /// Returns null both when the point lands in open space and when it lands back inside the
        /// same effect — in both cases the effect is unchanged, and listing "Toxic becomes Toxic"
        /// as a transformation would pad the chart with rows that say nothing.
        /// </summary>
        public static string Shift(MixMap map, string fromEffectId, float directionX, float directionY, float magnitude)
        {
            if (map == null || string.IsNullOrEmpty(fromEffectId)) return null;

            var origin = Region(map, fromEffectId);
            if (origin == null) return null;

            var landed = EffectAtPoint(
                map,
                origin.X + directionX * magnitude,
                origin.Y + directionY * magnitude);

            if (string.IsNullOrEmpty(landed)) return null;
            if (string.Equals(landed, fromEffectId, StringComparison.OrdinalIgnoreCase)) return null;

            return landed;
        }

        private static MapRegion Region(MixMap map, string effectId)
        {
            if (map.Regions == null) return null;
            foreach (var region in map.Regions)
                if (region != null && string.Equals(region.EffectId, effectId, StringComparison.OrdinalIgnoreCase))
                    return region;
            return null;
        }
    }
}
