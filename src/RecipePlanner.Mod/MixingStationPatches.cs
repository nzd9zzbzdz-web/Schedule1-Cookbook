using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RecipePlanner.Game.Binding;

namespace RecipePlanner.Mod
{
    /// <summary>
    /// The Phase 9 hook. Patches MixingStation.MixingDone() — reached on every client, after
    /// completion is confirmed, with CurrentMixOperation still populated (audit §2.1).
    ///
    /// Patches are resolved by string rather than by [HarmonyPatch(typeof(...))] so this assembly
    /// builds without a compile-time reference to the game and survives a type moving namespace.
    /// </summary>
    internal static class MixingStationPatches
    {
        private static ModHost _host;
        private static ILog _log;
        private static IGameFacts _facts;

        public static void Apply(HarmonyLib.Harmony harmony, IEnumerable<Assembly> gameAssemblies, ModHost host, ILog log)
        {
            _host = host;
            _log = log ?? NullLog.Instance;

            var assemblies = gameAssemblies.ToList();
            _facts = new ReflectionGameFacts(assemblies, _log);

            var donePostfix = new HarmonyMethod(
                typeof(MixingStationPatches).GetMethod(nameof(AfterMixingDone),
                    BindingFlags.NonPublic | BindingFlags.Static));

            var startPostfix = new HarmonyMethod(
                typeof(MixingStationPatches).GetMethod(nameof(AfterMixingStart),
                    BindingFlags.NonPublic | BindingFlags.Static));

            // Both the base class AND the Mk2 override must be patched: Harmony targets one method
            // body, and an override is a different body (audit §2.1 caveat 2). If Mk2 also calls
            // base.MixingDone(), both fire for one batch — the tracker's idempotency key absorbs it.
            var targets = new[] { HookTable.MixingStation, HookTable.MixingStationMk2 };
            var patched = 0;

            foreach (var typeName in targets)
            {
                var type = SymbolGuard.ResolveType(assemblies, typeName);
                if (type == null)
                {
                    _log.Warn($"{typeName} not present on this build — skipping (expected for optional variants).");
                    continue;
                }

                if (TryPatch(harmony, type, typeName, HookTable.MixingDone, donePostfix)) patched++;

                // MixingStart is the only moment the game knows who set the batch running:
                // PlayerUserObject is cleared once the player walks away, long before it completes.
                TryPatch(harmony, type, typeName, HookTable.MixingStart, startPostfix);
            }

            if (patched == 0)
                throw new InvalidOperationException(
                    "No mixing station completion hook could be patched — refusing to run with no tracking.");
        }

        /// <summary>Dropped on save unload — station GUIDs belong to the save going away.</summary>
        public static void ResetStationUsers() => (_facts as ReflectionGameFacts)?.ResetStationUsers();

        /// <summary>
        /// Patches one method if the type declares it itself.
        ///
        /// The DeclaringType check matters: without it GetMethod returns the inherited base method
        /// for a subclass that does NOT override, and we would patch the same body twice.
        /// </summary>
        private static bool TryPatch(
            HarmonyLib.Harmony harmony, Type type, string typeName, string methodName, HarmonyMethod postfix)
        {
            var method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, Type.EmptyTypes, null);

            if (method == null || method.DeclaringType != type)
            {
                _log.Info($"{typeName} does not declare its own {methodName}() — covered by the base patch.");
                return false;
            }

            harmony.Patch(method, postfix: postfix);
            _log.Info($"Patched {typeName}.{methodName}()");
            return true;
        }

        /// <summary>
        /// Records who started the batch. Runs while the player is still at the station, which is
        /// the only point at which PlayerUserObject is populated.
        /// </summary>
        private static void AfterMixingStart(object __instance)
        {
            try { (_facts as ReflectionGameFacts)?.CaptureStarter(__instance); }
            catch (Exception ex) { _log?.Error("Failed to capture the mix starter: " + ex.Message); }
        }

        /// <summary>
        /// Harmony postfix. Typed as object so no game reference is needed; Harmony accepts a
        /// widened __instance parameter.
        /// </summary>
        private static void AfterMixingDone(object __instance)
        {
            try
            {
                var candidate = MixingStationReader.Read(__instance, _facts);
                _host?.Submit(candidate);

                // The station is free to run a different batch next, possibly started by someone
                // else, so a stale starter must not carry over.
                if (candidate?.StationGuid != null)
                    (_facts as ReflectionGameFacts)?.ForgetStation(candidate.StationGuid);
            }
            catch (Exception ex)
            {
                // A throw here would propagate into the game's own MixingDone call stack and could
                // break the station mid-operation. Never let tracking damage the game.
                _log?.Error("Production hook threw; batch not recorded. " + ex);
            }
        }
    }
}
