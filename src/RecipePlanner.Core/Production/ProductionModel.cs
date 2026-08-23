using System;
using System.Collections.Generic;
using System.Globalization;

namespace RecipePlanner.Core.Production
{
    /// <summary>
    /// What physically happened. Only <see cref="Mixed"/>, <see cref="Cooked"/> and
    /// <see cref="Harvested"/> create new units — the rest transform units that already exist and
    /// must never reach "Total Drugs Made" (Phase 0 audit §2.5).
    /// </summary>
    public enum ProductionKind
    {
        Mixed,      // MixingStation      - new product from base + ingredient
        Cooked,     // LabOven / ChemistryStation / Cauldron
        Harvested,  // Pot / HarvestPlant - raw bud
        Dried,      // DryingRack   - quality transform
        Bricked,    // BrickPress   - form transform
        Packaged    // PackagingStation - packaging transform
    }

    /// <summary>Who produced it. Only <see cref="Local"/> counts toward personal totals.</summary>
    public enum Attribution
    {
        Local,          // station.PlayerUserObject == Player.Local
        Employee,       // station.NPCUserObject != null  (Chemist / Botanist)
        Remote,         // another Player in the lobby
        Unattributed    // neither user object set
    }

    public enum RejectionReason
    {
        None = 0,
        NoActiveProfile,
        GameNotLoaded,
        WithinLoadSettleWindow,
        DuplicateEvent,
        MalformedCandidate
    }

    /// <summary>
    /// Raw, untrusted observation handed over by the game binding layer at a completion hook.
    /// Contains no game types — this is the boundary that keeps the domain unit-testable.
    /// </summary>
    public sealed class ProductionCandidate
    {
        public ProductionKind Kind { get; set; }

        // --- station identity (idempotency) ---
        public string StationGuid { get; set; }
        public string StationType { get; set; }
        public string StationItemId { get; set; }

        // --- attribution inputs ---
        public bool IsLocalPlayerUser { get; set; }
        public bool HasNpcUser { get; set; }
        public string ProducedByPlayerCode { get; set; }

        // --- what was made ---
        public string BaseProductId { get; set; }
        public string IngredientId { get; set; }
        public string OutputProductId { get; set; }
        public string OutputProductName { get; set; }
        public string DrugType { get; set; }
        public string Quality { get; set; }
        public int Quantity { get; set; }
        public bool OutputWasAlreadyKnown { get; set; } = true;

        public List<string> IngredientChain { get; set; } = new List<string>();
        public List<string> Effects { get; set; } = new List<string>();

        // --- when, in game time (part of the idempotency key) ---
        public int ElapsedDays { get; set; }
        public int TimeOfDay { get; set; }

        /// <summary>
        /// stationGuid | base+ingredient | day-time. Two completions of the same operation on the
        /// same station at the same in-game minute are the same batch by definition, which is what
        /// absorbs a double-fire from the MixingStationMk2 override calling base.MixingDone().
        /// </summary>
        public string BuildEventKey() => string.Concat(
            StationGuid ?? "?", "|",
            BaseProductId ?? "?", "+", IngredientId ?? "-", "|",
            "d", ElapsedDays.ToString(CultureInfo.InvariantCulture),
            "-", TimeOfDay.ToString(CultureInfo.InvariantCulture));

        public bool IsWellFormed =>
            !string.IsNullOrWhiteSpace(StationGuid) &&
            Quantity > 0 &&
            !string.IsNullOrWhiteSpace(OutputProductId ?? BaseProductId);

        public Attribution ResolveAttribution()
        {
            if (IsLocalPlayerUser) return Attribution.Local;
            if (HasNpcUser) return Attribution.Employee;
            if (!string.IsNullOrWhiteSpace(ProducedByPlayerCode)) return Attribution.Remote;
            return Attribution.Unattributed;
        }
    }

    /// <summary>A validated, persisted production record. One line of events.jsonl.</summary>
    public sealed class ProductionEvent
    {
        public int SchemaVersion { get; set; } = 1;
        public string EventKey { get; set; }
        public ProductionKind Kind { get; set; }
        public Attribution Attribution { get; set; }

        public string ProfileId { get; set; }
        public string ProducedByPlayerCode { get; set; }

        public string StationGuid { get; set; }
        public string StationType { get; set; }
        public string StationItemId { get; set; }

        public string DrugType { get; set; }
        public string BaseProductId { get; set; }
        public string IngredientId { get; set; }
        public string OutputProductId { get; set; }
        public string OutputProductName { get; set; }
        public string RecipeId { get; set; }
        public string Quality { get; set; }
        public int Quantity { get; set; }
        public bool WasNewDiscovery { get; set; }

        public List<string> IngredientChain { get; set; } = new List<string>();
        public List<string> Effects { get; set; } = new List<string>();

        public double UnitCost { get; set; }
        public double UnitValue { get; set; }
        public double TotalCost { get; set; }
        public double TotalValue { get; set; }
        public double EstimatedProfit { get; set; }

        public int ElapsedDays { get; set; }
        public int TimeOfDay { get; set; }
        public DateTime RealTimeUtc { get; set; }

        public string GameVersion { get; set; }
        public bool ConsoleEnabled { get; set; }
        public bool RandomizedMixMaps { get; set; }

        /// <summary>
        /// True only for units this player personally created. Transforms are excluded because the
        /// units already existed; non-local attribution is excluded because someone else made them.
        /// </summary>
        public bool CountsTowardPersonalTotals =>
            Attribution == Attribution.Local && CreatesNewUnits(Kind);

        /// <summary>
        /// True for a mix whose result the player has not named yet, so the game has not created
        /// the product. The batch is real and counts; it simply has no product identity until
        /// <c>FinishAndNameMix</c> runs.
        /// </summary>
        public bool IsAwaitingName => string.IsNullOrEmpty(OutputProductId);

        /// <summary>
        /// Stable key for grouping, usable before the product exists. Falls back to the recipe, so
        /// two different unnamed mixes never merge and the same one never splits.
        /// </summary>
        public string ProductKey =>
            IsAwaitingName ? "unnamed:" + (RecipeId ?? ComputeRecipeId()) : OutputProductId;

        public static bool CreatesNewUnits(ProductionKind kind) =>
            kind == ProductionKind.Mixed ||
            kind == ProductionKind.Cooked ||
            kind == ProductionKind.Harvested;

        /// <summary>Stable identity of the recipe, independent of what the player named it.</summary>
        public string ComputeRecipeId()
        {
            var chain = (IngredientChain != null && IngredientChain.Count > 0)
                ? string.Join(">", IngredientChain)
                : (IngredientId ?? string.Empty);
            return string.Concat(BaseProductId ?? "?", ">", chain);
        }
    }

    public sealed class TrackResult
    {
        public bool Accepted { get; private set; }
        public RejectionReason Reason { get; private set; }
        public ProductionEvent Event { get; private set; }

        public static TrackResult Reject(RejectionReason reason) =>
            new TrackResult { Accepted = false, Reason = reason };

        public static TrackResult Accept(ProductionEvent e) =>
            new TrackResult { Accepted = true, Reason = RejectionReason.None, Event = e };
    }
}
