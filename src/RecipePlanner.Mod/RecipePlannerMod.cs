using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using RecipePlanner.Game.Binding;
using RecipePlanner.UI;

// Name, version and author are the three things a player sees in the MelonLoader console and in
// mod managers, so they must match the Nexus page exactly. "Cookbook" rather than "Recipe Planner":
// planning and optimisation are not built yet, and a name that promises them invites the complaint.
// See docs/05-RELEASE-ROADMAP.md R2.
[assembly: MelonInfo(typeof(RecipePlanner.Mod.RecipePlannerMod), "Schedule I Cookbook", "1.0.0", "Sean")]
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
        private PhoneAppLoader _phoneApp;
        private DateTime _lastPoll = DateTime.MinValue;
        private bool _appInstalled;

        public bool TrackingEnabled { get; private set; }

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("Schedule I Cookbook starting — verifying game symbols before patching.");

            var gameAssemblies = SymbolGuard
                .GameAssemblies(AppDomain.CurrentDomain.GetAssemblies())
                .ToList();

            if (gameAssemblies.Count == 0)
            {
                LoggerInstance.Error(
                    "No Schedule I game assembly found, so nothing was patched. On the default " +
                    "IL2CPP branch this usually means MelonLoader has not finished generating its " +
                    "proxy assemblies yet — close the game, launch it again, and let the first-run " +
                    "generation complete (it can take a minute).");
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
                LoggerInstance.Error(
                    "Production tracking DISABLED — the game's symbols no longer match the hook " +
                    "table, most likely after an update. Nothing will be recorded until the mod is " +
                    "updated. Recording wrong numbers would be worse than recording none.");
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
                _host.AttachMixGuide(new MixGuideReader(gameAssemblies, log));
                _host.AttachCookbook(new CookbookDataBuilder(new ProductCatalogReader(gameAssemblies, log), log), log);

                _harmony = new HarmonyLib.Harmony("com.schedule1.recipeplanner");
                MixingStationPatches.Apply(_harmony, gameAssemblies, _host, log);
                ProductManagerPatches.Apply(_harmony, gameAssemblies, _host, log);

                TrackingEnabled = true;
                LoggerInstance.Msg("Production tracking ENABLED — waiting for a save to load.");

                // Loaded by name, never linked: see PhoneAppLoader. Tracking above is already live
                // at this point, so nothing the UI does can take it down.
                //
                // Left null on IL2CPP rather than left to fail: the loader would only discover the
                // same thing the hard way, and an expected configuration should not surface as a
                // warning the player has to interpret.
                if (SymbolGuard.IsMonoBranch(gameAssemblies))
                {
                    _phoneApp = new PhoneAppLoader(log);
                    LoggerInstance.Msg("Mono branch detected — the Cookbook app will appear on the phone.");
                }
                else
                {
                    // Deliberately does NOT claim tracking works here. It very probably does — this
                    // assembly reaches the game only by reflection and resolves the Il2Cpp proxy
                    // names too — but "probably" is not "tested", and the mod's whole reputation
                    // rests on not asserting things it has not verified.
                    LoggerInstance.Warning(
                        "IL2CPP branch detected. This mod is built and tested for the 'alternate' " +
                        "(Mono) Steam branch. The in-game Cookbook app CANNOT run here, and nothing " +
                        "on this branch is supported or tested. To switch: Steam > right-click " +
                        "Schedule I > Properties > Betas > 'alternate'.");
                }
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

            // The phone — and the app installed into it — is destroyed and rebuilt with each save.
            // Clearing the latch on unload is what makes the SECOND save of a session get an app:
            // CookbookAppInstaller is idempotent and would have handled the reinstall happily, but
            // it never got asked, because this flag latched true on the first save and stayed there.
            if (_host != null && !_host.IsGameLoaded) _appInstalled = false;

            // The phone only exists once a save is in; retry each tick until it takes. Null on the
            // IL2CPP branch, where there is no UI to install.
            if (!_appInstalled && _phoneApp != null && _host != null && _host.IsGameLoaded)
                _appInstalled = _phoneApp.TryInstall();
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
