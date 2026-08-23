using System;
using System.Collections.Generic;
using UnityEngine;
using ScheduleOne.DevUtilities;
using ScheduleOne.ItemFramework;
using ScheduleOne.Product;
using RecipePlanner.UI;


namespace RecipePlanner.PhoneApp
{
    /// <summary>
    /// Resolves the game's own artwork for products and ingredients.
    ///
    /// For a product the artwork wanted here is the bare bud — the same sprite the Products app
    /// shows — which lives on the item definition. The game generates it when the product is first
    /// named:
    ///
    ///     weedDefinition.Icon = Singleton&lt;ProductIconManager&gt;.Instance.GenerateIcons(id);
    ///
    /// so it is present for player-created mixes too, not just the shipped strains. That is also
    /// the path the game itself takes for an unpackaged product — <c>ProductItemInstance.GetIcon()</c>
    /// only consults ProductIconManager when packaging has been applied, and otherwise falls
    /// through to the definition.
    ///
    /// ProductIconManager is kept as a fallback, but it must be asked for the literal packaging
    /// <c>"none"</c>. Its table is populated in Awake from the real packaging IDs plus "none", and
    /// <c>GetIcon</c> matches the packaging ID exactly — so an empty string matches nothing and
    /// silently returns null. That was a real bug: every product row rendered without artwork while
    /// the ingredient icons beside them worked fine.
    ///
    /// Both are cached: the list rebuilds on every refresh, and a miss is worth remembering too.
    /// </summary>
    public static class IconSource
    {
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Ids already reported as having no artwork, so the log stays readable.</summary>
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The packaging id the game uses for an unwrapped product. Not "" and not null.</summary>
        private const string Unpackaged = "none";

        public static void Clear()
        {
            Cache.Clear();
            Reported.Clear();
        }

        /// <summary>The bud artwork for a product, or null if the game has none for it.</summary>
        public static Sprite Product(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;

            var key = "product:" + productId;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var sprite = FromDefinition(productId) ?? FromIconManager(productId);

            if (sprite == null && Reported.Add(productId))
                RecipePlannerUI.Log?.Warn(
                    $"No artwork for product '{productId}' — neither its definition nor " +
                    "ProductIconManager has one. The row will render without an icon.");

            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>The shop icon for an ingredient item.</summary>
        public static Sprite Item(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            var key = "item:" + itemId;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            var sprite = FromDefinition(itemId);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// The definition's own sprite. This is the primary source for products AND ingredients —
        /// products are items, and Registry holds both.
        /// </summary>
        private static Sprite FromDefinition(string id)
        {
            try
            {
                var definition = ScheduleOne.Registry.GetItem(id);
                return definition != null ? definition.Icon : null;
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Warn($"Icon lookup for '{id}' failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fallback for a product whose definition has not had its icon generated yet — a mix named
        /// this session, before the catalogue is re-read.
        /// </summary>
        private static Sprite FromIconManager(string productId)
        {
            try
            {
                var manager = Singleton<ProductIconManager>.Instance;
                if (manager == null) return null;

                // ignoreError: a freshly created mix legitimately has no entry, and the game logs
                // an error to the in-game console otherwise.
                return manager.GetIcon(productId, Unpackaged, true);
            }
            catch (Exception ex)
            {
                RecipePlannerUI.Log?.Warn($"ProductIconManager lookup for '{productId}' failed: {ex.Message}");
                return null;
            }
        }
    }
}
