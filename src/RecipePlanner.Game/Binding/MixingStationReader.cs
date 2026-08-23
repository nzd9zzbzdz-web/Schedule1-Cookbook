using System.Collections.Generic;
using RecipePlanner.Core.Production;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Converts a live MixingStation at the moment MixingDone() fires into a plain
    /// ProductionCandidate. This is the boundary the audit's architecture depends on: nothing
    /// downstream of here knows Schedule I exists.
    ///
    /// Call flow context (audit §2.1):
    ///   MixingDone_Networked [ObserversRpc] -> MixingDone()  &lt;- we are here
    ///   CurrentMixOperation is still populated; TryCreateOutputItems has not run yet.
    /// </summary>
    public static class MixingStationReader
    {
        // Field names verified against 0.4.5f2 — see docs/00-PHASE-0-AUDIT.md §2.1.
        private const string CurrentMixOperation = "CurrentMixOperation";
        private const string PlayerUserObject = "PlayerUserObject";
        private const string NpcUserObject = "NPCUserObject";

        public static ProductionCandidate Read(object station, IGameFacts facts)
        {
            if (station == null || facts == null) return null;

            var op = Reflect.Get(station, CurrentMixOperation);
            if (op == null) return null;   // cancelled or already cleared — not a batch

            var stationGuid = facts.GetStationGuid(station);
            var baseProduct = Reflect.GetString(op, "ProductID");
            var ingredient = Reflect.GetString(op, "IngredientID");

            var candidate = new ProductionCandidate
            {
                Kind = ProductionKind.Mixed,

                StationGuid = stationGuid,
                StationType = station.GetType().Name,
                StationItemId = facts.GetStationItemId(station),

                BaseProductId = baseProduct,
                IngredientId = ingredient,
                Quality = Reflect.AsString(Reflect.Get(op, "ProductQuality")),
                Quantity = Reflect.GetInt(op, "Quantity"),

                ElapsedDays = facts.ElapsedDays,
                TimeOfDay = facts.TimeOfDay
            };

            ApplyAttribution(station, stationGuid, facts, candidate);

            // A brand-new mix has NO output product yet: the game only creates one once the player
            // names it (ProductManager.FinishAndNameMix). Leave the id null in that case.
            //
            // Do NOT fall back to the base product. Doing so records "megasmegma + banana ->
            // megasmegma": a recipe that appears to do nothing, credits the units to the wrong
            // product, and puts a self-loop in the lineage tree.
            var output = facts.ResolveOutputProductId(op, baseProduct, ingredient);
            candidate.OutputWasAlreadyKnown = facts.IsProductDiscovered(output);
            candidate.OutputProductId = string.IsNullOrEmpty(output) ? null : output;
            candidate.OutputProductName = candidate.OutputProductId == null
                ? null
                : facts.GetProductDisplayName(candidate.OutputProductId);

            // The drug family does not change across a mix, so the base still answers this even
            // when the result is unnamed.
            candidate.DrugType = facts.GetDrugType(candidate.OutputProductId ?? baseProduct);

            candidate.IngredientChain = facts.ResolveIngredientChain(candidate.OutputProductId)
                                       ?? Chain(ingredient);
            candidate.Effects = facts.ResolveEffects(candidate.OutputProductId) ?? new List<string>();

            return candidate;
        }

        /// <summary>
        /// Works out who this batch belongs to, in order of decreasing reliability.
        ///
        /// The obvious source — <c>PlayerUserObject</c> at completion — is almost always null,
        /// because it tracks who is at the station's UI *right now* and the player walks away while
        /// the mix runs. So it is only the first choice, not the only one.
        /// </summary>
        private static void ApplyAttribution(
            object station, string stationGuid, IGameFacts facts, ProductionCandidate candidate)
        {
            // 1. Someone is standing at the station right now.
            var playerUser = Reflect.Get(station, PlayerUserObject);
            if (Reflect.IsAlive(playerUser))
            {
                candidate.IsLocalPlayerUser = facts.IsLocalPlayer(playerUser);
                candidate.ProducedByPlayerCode = facts.GetPlayerCode(playerUser);
                candidate.HasNpcUser = false;
                return;
            }

            // 2. An employee is operating it — that survives to completion.
            if (Reflect.IsAlive(Reflect.Get(station, NpcUserObject)))
            {
                candidate.HasNpcUser = true;
                return;
            }

            // 3. We saw who pressed Start on this station earlier this session.
            var starter = facts.GetStarter(stationGuid);
            if (starter != null)
            {
                candidate.HasNpcUser = starter.WasNpc;
                candidate.IsLocalPlayerUser = starter.IsLocalPlayer;
                candidate.ProducedByPlayerCode = starter.PlayerCode;
                return;
            }

            // 4. Nothing observed — e.g. a mix that was already running when the save loaded.
            //    In a single-player session there is nobody else it could belong to, so attributing
            //    it to the local player is correct rather than merely convenient. In multiplayer we
            //    genuinely cannot tell, and guessing would put someone else's work in your totals.
            if (!facts.IsMultiplayerSession)
            {
                candidate.IsLocalPlayerUser = true;
                candidate.ProducedByPlayerCode = facts.LocalPlayerCode;
            }
        }

        private static List<string> Chain(string ingredient)
        {
            var list = new List<string>();
            if (!string.IsNullOrEmpty(ingredient)) list.Add(ingredient);
            return list;
        }
    }

    /// <summary>
    /// Everything the reader needs that lives outside the station object. Implemented against the
    /// live game by the mod, and against fixtures in tests.
    /// </summary>
    public interface IGameFacts
    {
        int ElapsedDays { get; }
        int TimeOfDay { get; }

        /// <summary>SteamID64 of whoever is at this keyboard.</summary>
        string LocalPlayerCode { get; }

        /// <summary>
        /// True when more than one player is in the session. Attribution falls back to the local
        /// player only when this is false — in multiplayer, guessing would credit you with someone
        /// else's production.
        /// </summary>
        bool IsMultiplayerSession { get; }

        /// <summary>Who pressed Start on this station, if we observed it. Null if not.</summary>
        StationStarter GetStarter(string stationGuid);

        string GetStationGuid(object station);
        string GetStationItemId(object station);

        bool IsLocalPlayer(object playerObject);
        string GetPlayerCode(object playerObject);

        /// <summary>MixOperation.GetOutput(), or a mix-map lookup as a fallback.</summary>
        string ResolveOutputProductId(object mixOperation, string baseProductId, string ingredientId);

        string GetProductDisplayName(string productId);
        string GetDrugType(string productId);
        bool IsProductDiscovered(string productId);

        /// <summary>Full recipe path from a base strain, not just the final step.</summary>
        List<string> ResolveIngredientChain(string productId);
        List<string> ResolveEffects(string productId);
    }
}
