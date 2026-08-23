using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RecipePlanner.Game.Binding;

namespace RecipePlanner.Mod
{
    /// <summary>
    /// Hooks the moment a brand-new mix gets its name.
    ///
    /// A new mix completes with no product — the game creates one only when the player names it —
    /// so those batches are recorded unnamed and reconciled here. Patching the four-argument
    /// overload specifically: it is the ObserversRpc body, so it runs on every client, and it
    /// carries <c>mixID</c>, the id of the product that was just created.
    /// </summary>
    internal static class ProductManagerPatches
    {
        private const string TypeName = HookTable.NsProduct + "ProductManager";
        private const string MethodName = HookTable.FinishAndNameMix;

        private static ModHost _host;
        private static ILog _log;

        public static void Apply(HarmonyLib.Harmony harmony, IEnumerable<Assembly> gameAssemblies, ModHost host, ILog log)
        {
            _host = host;
            _log = log ?? NullLog.Instance;

            var type = SymbolGuard.ResolveType(gameAssemblies.ToList(), TypeName);
            if (type == null)
            {
                _log.Warn($"{TypeName} not found — unnamed mixes will not be reconciled.");
                return;
            }

            // Four string parameters: (productID, ingredientID, mixName, mixID). The three-argument
            // overload is the local caller and does not carry the resulting id.
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m =>
                    m.Name == MethodName &&
                    m.GetParameters().Length == 4 &&
                    m.GetParameters().All(p => p.ParameterType == typeof(string)));

            if (method == null)
            {
                _log.Warn($"{TypeName}.{MethodName}(string,string,string,string) not found — " +
                          "unnamed mixes will stay unnamed until the game is restarted.");
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(
                typeof(ProductManagerPatches).GetMethod(nameof(AfterFinishAndNameMix),
                    BindingFlags.NonPublic | BindingFlags.Static)));

            _log.Info($"Patched {TypeName}.{MethodName}()");
        }

        private static void AfterFinishAndNameMix(string productID, string ingredientID, string mixName, string mixID)
        {
            try
            {
                _host?.OnMixNamed(productID, ingredientID, mixID, mixName);
            }
            catch (Exception ex)
            {
                // Never propagate into the game's own naming flow.
                _log?.Error("Failed to reconcile a named mix: " + ex);
            }
        }
    }
}
