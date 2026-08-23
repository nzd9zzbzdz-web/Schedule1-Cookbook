using System;
using System.Collections.Generic;
using RecipePlanner.Core.Recipes;

namespace RecipePlanner.Core.Production
{
    /// <summary>
    /// Gives a name to batches that were recorded before the player named the mix.
    ///
    /// A brand-new mix completes with no product: the game only creates one when the player types a
    /// name (<c>ProductManager.FinishAndNameMix</c>). The batch is real and already recorded, so
    /// rather than guessing an identity at completion — which credited the units to the input
    /// product — the event is stored unnamed and reconciled here.
    /// </summary>
    public static class PendingNameResolver
    {
        /// <summary>
        /// Applies a naming to every unnamed event that matches the base + ingredient pair.
        ///
        /// Matching on the pair rather than on time: the player may cook several batches of a new
        /// mix before naming it, and all of them are the same product.
        /// </summary>
        public static int Apply(
            IEnumerable<ProductionEvent> events,
            string baseProductId,
            string ingredientId,
            string productId,
            string productName)
        {
            if (events == null || string.IsNullOrEmpty(productId)) return 0;

            var applied = 0;
            foreach (var e in events)
            {
                if (e == null || !e.IsAwaitingName) continue;
                if (!Same(e.BaseProductId, baseProductId)) continue;
                if (!Same(e.IngredientId, ingredientId)) continue;

                e.OutputProductId = productId;
                e.OutputProductName = productName ?? productId;
                e.WasNewDiscovery = true;
                applied++;
            }
            return applied;
        }

        /// <summary>
        /// Names pending batches from the game's own recipe list, rather than waiting for the
        /// naming event to arrive.
        ///
        /// <see cref="Apply"/> only ever runs while the player is typing the name, which makes the
        /// whole repair depend on catching one live event. Miss it — the mod was installed after
        /// the mix, the hook attached late, the naming happened in a session the mod did not see —
        /// and the batch is stranded permanently: it has no product, so it cannot be priced, cannot
        /// be placed under a strain, and every load-time repair pass skips it precisely *because*
        /// it is pending. Nothing ever came back for it.
        ///
        /// Observed on a real save: four consecutive invented mixes, each one demonstrably named by
        /// the player — the next cook used that name as its base — all four still recorded as
        /// unnamed and worth zero.
        ///
        /// The game knew the answer the whole time. Each step carries base + additive to output, so
        /// a pending batch can simply be looked up. Steps come from <see cref="RecipeGraph"/>
        /// because it has already done the hard part: the game stores roughly one row in five with
        /// its sides reversed, and reading them by field name would invert the recipe.
        /// </summary>
        /// <param name="nameOf">Resolves a product id to its display name; may return null.</param>
        public static int ResolveFromRecipes(
            IEnumerable<ProductionEvent> events,
            IEnumerable<ResolvedStep> steps,
            Func<string, string> nameOf = null)
        {
            if (events == null || steps == null) return 0;

            var outputOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var step in steps)
            {
                if (step == null) continue;
                if (string.IsNullOrEmpty(step.BaseProductId) || string.IsNullOrEmpty(step.AdditiveId)) continue;
                if (string.IsNullOrEmpty(step.OutputProductId)) continue;

                // A self-loop (thickdick + paracetamol -> thickdick) is a real row, but it says the
                // mix produced no new product. Naming a pending batch after its own base would
                // invent an identity the player never gave it.
                if (Same(step.OutputProductId, step.BaseProductId)) continue;

                var key = step.BaseProductId + "|" + step.AdditiveId;
                if (!outputOf.ContainsKey(key)) outputOf[key] = step.OutputProductId;
            }

            if (outputOf.Count == 0) return 0;

            var applied = 0;
            foreach (var e in events)
            {
                if (e == null || !e.IsAwaitingName) continue;
                if (string.IsNullOrEmpty(e.BaseProductId) || string.IsNullOrEmpty(e.IngredientId)) continue;

                if (!outputOf.TryGetValue(e.BaseProductId + "|" + e.IngredientId, out var productId)) continue;

                e.OutputProductId = productId;
                e.OutputProductName = nameOf == null ? productId : (nameOf(productId) ?? productId);
                e.WasNewDiscovery = true;
                applied++;
            }
            return applied;
        }

        /// <summary>True when any recorded batch is still waiting for a name.</summary>
        public static bool HasPending(IEnumerable<ProductionEvent> events)
        {
            if (events == null) return false;
            foreach (var e in events) if (e != null && e.IsAwaitingName) return true;
            return false;
        }

        private static bool Same(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
