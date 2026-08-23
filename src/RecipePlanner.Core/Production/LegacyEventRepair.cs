using System;
using System.Collections.Generic;

namespace RecipePlanner.Core.Production
{
    /// <summary>
    /// Repairs events written by a build that mishandled unnamed mixes.
    ///
    /// That build substituted the base product when a new mix had no product yet, recording
    /// "megasmegma + banana -> megasmegma": units credited to the input, and a self-loop that
    /// corrupts the lineage tree. Those events are rewritten back to "awaiting name", so the
    /// normal <see cref="PendingNameResolver"/> path can give them their real identity when the
    /// player names the mix.
    /// </summary>
    public static class LegacyEventRepair
    {
        /// <summary>
        /// Only events matching the exact fingerprint of the defect are touched.
        ///
        /// The output equalling the input is NOT sufficient on its own: genuine identity mixes
        /// exist in this game (<c>thickdick + paracetamol -> thickdick</c> is real recipe data).
        /// The distinguishing signal is that the defect only ever occurred when the output was
        /// unknown — a real identity mix resolves to a product that is already discovered.
        /// </summary>
        public static int Apply(IEnumerable<ProductionEvent> events)
        {
            if (events == null) return 0;

            var repaired = 0;
            foreach (var e in events)
            {
                if (!ShouldRepair(e)) continue;

                e.OutputProductId = null;
                e.OutputProductName = null;
                repaired++;
            }
            return repaired;
        }

        public static bool ShouldRepair(ProductionEvent e)
        {
            if (e == null) return false;
            if (e.Kind != ProductionKind.Mixed) return false;
            if (string.IsNullOrEmpty(e.OutputProductId) || string.IsNullOrEmpty(e.BaseProductId)) return false;

            // A genuine identity mix has a known output; the defect only produced unknown ones.
            if (!e.WasNewDiscovery) return false;

            return string.Equals(e.OutputProductId, e.BaseProductId, StringComparison.OrdinalIgnoreCase);
        }

        public static int Count(IEnumerable<ProductionEvent> events)
        {
            if (events == null) return 0;
            var n = 0;
            foreach (var e in events) if (ShouldRepair(e)) n++;
            return n;
        }
    }
}
