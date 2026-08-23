using System;
using System.Collections.Generic;
using System.Reflection;
using RecipePlanner.Core.Identity;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Detects when a save becomes active and builds the <see cref="PlayerContext"/> from it.
    ///
    /// This is the runtime half of Phase 8. Until it produces a context the tracker rejects
    /// everything — which is the rule that keeps statistics from landing on the wrong character.
    ///
    /// Detection is a polled transition on <c>LoadManager.IsGameLoaded</c> rather than a Harmony
    /// patch: the load path is a coroutine with several exit points (new game, load, join as
    /// client, exit to menu), and a single observed boolean covers all of them without patching
    /// anything the game update might reshape.
    /// </summary>
    public sealed class SaveContextReader
    {
        private readonly ILog _log;
        private readonly Type _loadManagerType;
        private readonly Type _playerType;
        private readonly Type _lobbyType;

        public SaveContextReader(IEnumerable<Assembly> assemblies, ILog log)
        {
            _log = log ?? NullLog.Instance;
            var list = new List<Assembly>(assemblies ?? new Assembly[0]);

            _loadManagerType = SymbolGuard.ResolveType(list, HookTable.NsPersist + "LoadManager");
            _playerType = SymbolGuard.ResolveType(list, HookTable.NsPlayer + "Player");
            _lobbyType = SymbolGuard.ResolveType(list, HookTable.NsNet + "Lobby");
        }

        /// <summary>True once the game reports a fully loaded save.</summary>
        public bool IsGameLoaded => Reflect.GetBool(LoadManager(), "IsGameLoaded");

        /// <summary>
        /// Builds the context for the currently loaded save, or null if it is not resolvable yet.
        /// Callers must treat null as "not ready" and retry, not as an error.
        /// </summary>
        public PlayerContext TryBuildContext()
        {
            var loadManager = LoadManager();
            if (loadManager == null) return null;

            var folder = Reflect.GetString(loadManager, "LoadedGameFolderPath");
            if (string.IsNullOrEmpty(folder)) return null;

            // Player.Local is the authoritative SteamID64; the folder name is the fallback.
            var localPlayer = _playerType == null ? null : Reflect.GetStatic(_playerType, "Local");
            var steamId = Reflect.GetString(localPlayer, "PlayerCode");

            var slot = SlotNumber(loadManager, folder);
            var isHost = _lobbyType == null || Reflect.GetBool(Singleton(_lobbyType), "IsHost", true);

            var context = SaveFolderReader.BuildContext(folder, steamId, slot, isHost);
            if (context == null)
            {
                var info = SaveFolderReader.Read(folder, steamId);
                _log.Warn("Could not resolve save identity yet: " + (info.Error ?? "unknown reason"));
            }
            return context;
        }

        /// <summary>Prefers the game's own SaveInfo, falling back to parsing the folder name.</summary>
        private int SlotNumber(object loadManager, string folder)
        {
            var saveInfo = Reflect.Get(loadManager, "ActiveSaveInfo");
            var slot = Reflect.GetInt(saveInfo, "SaveSlotNumber");
            return slot > 0 ? slot : SaveFolderReader.SlotFromPath(folder);
        }

        private object LoadManager() => Singleton(_loadManagerType);

        private object Singleton(Type type)
        {
            if (type == null) return null;
            return Reflect.GetStatic(type, "Instance") ?? Reflect.GetStatic(type, "instance");
        }
    }

    /// <summary>
    /// Turns the polled loaded/unloaded boolean into edge callbacks, and retries context
    /// construction while the save is still settling.
    /// </summary>
    public sealed class SaveLifecycleWatcher
    {
        private readonly SaveContextReader _reader;
        private readonly ILog _log;

        private bool _wasLoaded;
        private bool _contextDelivered;
        private int _attempts;

        /// <summary>
        /// Player.Local and the save files are not necessarily ready the instant IsGameLoaded flips,
        /// so give it a bounded number of retries before reporting failure once and going quiet.
        /// </summary>
        private const int MaxAttempts = 60;

        public SaveLifecycleWatcher(SaveContextReader reader, ILog log)
        {
            _reader = reader;
            _log = log ?? NullLog.Instance;
        }

        public event Action<PlayerContext> SaveLoaded;
        public event Action SaveUnloaded;

        /// <summary>Call on a throttled tick (about once a second). Cheap: one boolean read.</summary>
        public void Poll()
        {
            bool loaded;
            try { loaded = _reader.IsGameLoaded; }
            catch (Exception) { return; }

            if (loaded && !_wasLoaded)
            {
                _wasLoaded = true;
                _contextDelivered = false;
                _attempts = 0;
            }
            else if (!loaded && _wasLoaded)
            {
                _wasLoaded = false;
                _contextDelivered = false;
                SaveUnloaded?.Invoke();
                return;
            }

            if (!_wasLoaded || _contextDelivered) return;

            _attempts++;
            var context = _reader.TryBuildContext();
            if (context != null)
            {
                _contextDelivered = true;
                SaveLoaded?.Invoke(context);
            }
            else if (_attempts == MaxAttempts)
            {
                _contextDelivered = true;   // stop retrying; stay silent rather than spam the log
                _log.Error("Gave up resolving the save identity. Production tracking stays off for " +
                           "this session to avoid recording against the wrong profile.");
            }
        }
    }
}
