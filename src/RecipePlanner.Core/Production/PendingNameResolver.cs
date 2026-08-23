using System;
using System.Collections.Generic;

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
