using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using RecipePlanner.Game.Binding;
using RecipePlanner.PhoneApp;

[assembly: MelonInfo(typeof(RecipePlanner.Mod.RecipePlannerMod), "Recipe Planner", "0.1.0", "Schedule1RecipePlanner")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace RecipePlanner.Mod
{
    /// <summary>
    /// Phase 1 skeleton + Phase 18 update protection.
    ///
    /// Deliberate ordering: verify every symbol first, and only patch if the check passes. Wrong
    /// statistics are worse than absent statistics (audit §5), so this fails closed by design.
    /// </summary>
    public class RecipePlannerMod : MelonMod
    {
        internal static RecipePlannerMod Instance { get; private set; }

        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private HarmonyLib.Harmony _harmony;
        private ModHost _host;
        private SaveLifecycleWatcher _watcher;
        private DateTime _lastPoll = DateTime.MinValue;
        private bool _appInstalled;

        public bool TrackingEnabled { get; private set; }

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("Recipe Planner starting — verifying game symbols before patching.");

            var gameAssemblies = SymbolGuard
                .GameAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                .ToList();

            if (gameAssemblies.Count == 0)
            {
                LoggerInstance.Error(
                    "No Schedule I game assembly found. On the IL2CPP branch, proxy assemblies must " +
                    "be generated first. See docs/04-SETUP.md — the Mono 'alternate' branch is the " +
                    "supported target.");
                return;
            }

            var report = SymbolGuard.Verify(gameAssemblies);
            foreach (var line in report.Describe().Split('\n'))
            {
                if (report.SafeToTrack) LoggerInstance.Msg(line.TrimEnd());
                else LoggerInstance.Error(line.TrimEnd());
            }

            if (!report.SafeToTrack)
            {
                LoggerInstance.Error("Production tracking DISABLED. The planner UI will still work; " +
                                     "statistics will not be recorded until the hook table is updated.");
                return;
            }

            try
            {
                var log = new MelonLog(LoggerInstance);
                _host = new ModHost(log);

                // Phase 8 wiring: nothing is recorded until a save resolves to a profile.
                _watcher = new SaveLifecycleWatcher(new SaveContextReader(gameAssemblies, log), log);
                _watcher.SaveLoaded += _host.OnSaveLoaded;
                _watcher.SaveUnloaded += _host.OnSaveUnloaded;

                // The phone app reads through this; the catalogue is cached per save.
                // Prices come from the game itself; reimplementing its maths would drift.
                var prices = new GamePriceSource(gameAssemblies, log);
                _host.AttachPricing(prices);
                _host.AttachCookbook(new CookbookDataBuilder(new ProductCatalogReader(gameAssemblies, log), log), log);

                _harmony = new HarmonyLib.Harmony("com.schedule1.recipeplanner");
                MixingStationPatches.Apply(_harmony, gameAssemblies, _host, log);
                ProductManagerPatches.Apply(_harmony, gameAssemblies, _host, log);

                TrackingEnabled = true;
                LoggerInstance.Msg("Production tracking ENABLED — waiting for a save to load.");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error("Failed to apply patches; tracking disabled. " + ex);
                TrackingEnabled = false;
            }
        }

        /// <summary>
        /// Throttled to roughly once a second. The poll itself is a single boolean read, but doing
        /// even that every frame is wasted work in a game loop.
        /// </summary>
        public override void OnUpdate()
        {
            if (!TrackingEnabled || _watcher == null) return;

            // Wall clock rather than UnityEngine.Time, so this assembly keeps its zero-game-reference
            // property and stays loadable on both the Mono and IL2CPP hosts.
            var now = DateTime.UtcNow;
            if ((now - _lastPoll) < PollInterval) return;
            _lastPoll = now;

            try { _watcher.Poll(); }
            catch (Exception ex) { LoggerInstance.Error("Save watcher failed: " + ex.Message); }

            // The phone only exists once a save is in; retry each tick until it takes.
            if (!_appInstalled && _host != null && _host.IsGameLoaded)
                _appInstalled = CookbookAppInstaller.TryInstall();
        }

        public override void OnDeinitializeMelon()
        {
            _appInstalled = false;
            try { _harmony?.UnpatchSelf(); } catch { /* shutting down anyway */ }
            _host?.Flush();
        }
    }

    /// <summary>Keeps MelonLoader's logger out of Core and Game.</summary>
    internal sealed class MelonLog : ILog
    {
        private readonly MelonLogger.Instance _inner;
        public MelonLog(MelonLogger.Instance inner) { _inner = inner; }
        public void Info(string message) => _inner.Msg(message);
        public void Warn(string message) => _inner.Warning(message);
        public void Error(string message) => _inner.Error(message);
    }
}
