using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace RecipePlanner.Game.Binding
{
    /// <summary>
    /// Reads the parts of the game that live outside the station object.
    ///
    /// Every signature here was confirmed against the real Assembly-CSharp with
    /// <c>tools/HookVerifier</c>. Two of them are counter-intuitive and were wrong in the first
    /// draft:
    ///
    ///   * <c>MixingStation.PlayerUserObject</c> is a FishNet <c>NetworkObject</c>, NOT a Player —
    ///     it has no PlayerCode, so the Player has to be found by matching NetworkObject.
    ///   * <c>MixOperation.GetOutput(List)</c> returns an <c>EDrugType</c>, not a product. The
    ///     resulting product comes from <c>IsOutputKnown(out ProductDefinition)</c>.
    ///
    /// Every lookup is defensive: an unresolved member returns null and the batch is still recorded
    /// with whatever did resolve. Losing a display name is cosmetic; dropping the batch is not.
    /// </summary>
    public sealed class ReflectionGameFacts : IGameFacts
    {
        private readonly ILog _log;
        private readonly Type _playerType;
        private readonly Type _timeManagerType;
        private readonly Type _productManagerType;
        private readonly Type _lobbyType;
        private readonly StationUserRegistry _stationUsers;

        public ReflectionGameFacts(List<Assembly> assemblies, ILog log, StationUserRegistry stationUsers = null)
        {
            _log = log ?? NullLog.Instance;
            _stationUsers = stationUsers ?? new StationUserRegistry();
            _lobbyType = SymbolGuard.ResolveType(assemblies, HookTable.NsNet + "Lobby");
            _playerType = SymbolGuard.ResolveType(assemblies, HookTable.NsPlayer + "Player");
            _timeManagerType = SymbolGuard.ResolveType(assemblies, "ScheduleOne.GameTime.TimeManager");
            _productManagerType = SymbolGuard.ResolveType(assemblies, HookTable.NsProduct + "ProductManager");
        }

        // ---------- time (part of the idempotency key) ----------

        public int ElapsedDays => ReadTime("ElapsedDays");

        /// <summary>
        /// The runtime property is <c>CurrentTime</c> (e.g. 924 for 09:24). It is NOT
        /// <c>TimeOfDay</c> — that is only the serialized key in the save file's TimeData, and
        /// reading it returns null, which silently collapsed the idempotency key to
        /// station+recipe+day and made two identical same-day mixes look like duplicates.
        /// </summary>
        public int TimeOfDay
        {
            get
            {
                var instance = Singleton(_timeManagerType);
                if (instance == null) return 0;

                var value = Reflect.Get(instance, "CurrentTime");
                if (value != null) { try { return Convert.ToInt32(value); } catch { } }

                return Reflect.GetInt(instance, "TimeOfDay");
            }
        }

        private int ReadTime(string member)
        {
            var instance = Singleton(_timeManagerType);
            return instance == null ? 0 : Reflect.GetInt(instance, member);
        }

        // ---------- station ----------

        /// <summary>
        /// BuildableItem.GUID is a System.Guid, not a string — ToString() gives the same value the
        /// save file records, which is what makes the idempotency key stable across sessions.
        /// </summary>
        public string GetStationGuid(object station) => Reflect.AsString(Reflect.Get(station, "GUID"));

        public string GetStationItemId(object station)
        {
            var itemInstance = Reflect.Get(station, "ItemInstance");
            return Reflect.GetString(itemInstance, "ID") ?? station?.GetType().Name;
        }

        // ---------- player ----------

        public bool IsLocalPlayer(object playerUserObject)
        {
            var player = ResolvePlayer(playerUserObject);
            if (player == null || _playerType == null) return false;

            var flag = Reflect.Get(player, "IsLocalPlayer");
            if (flag is bool b) return b;

            return ReferenceEquals(Reflect.GetStatic(_playerType, "Local"), player);
        }

        public string GetPlayerCode(object playerUserObject) =>
            Reflect.GetString(ResolvePlayer(playerUserObject), "PlayerCode");

        public string LocalPlayerCode =>
            _playerType == null ? null
            : Reflect.GetString(Reflect.GetStatic(_playerType, "Local"), "PlayerCode");

        /// <summary>
        /// Counts players rather than trusting a lobby flag: Lobby.IsInLobby can be true for a
        /// solo-hosted session that nobody has joined, and that is still single-player for
        /// attribution purposes.
        /// </summary>
        public bool IsMultiplayerSession
        {
            get
            {
                if (_lobbyType != null)
                {
                    var lobby = Singleton(_lobbyType);
                    if (lobby != null)
                    {
                        var count = Reflect.GetInt(lobby, "PlayerCount", -1);
                        if (count >= 0) return count > 1;
                    }
                }

                var players = 0;
                foreach (var _ in Reflect.Enumerate(Reflect.GetStatic(_playerType, "PlayerList"))) players++;
                return players > 1;
            }
        }

        public StationStarter GetStarter(string stationGuid) => _stationUsers?.Get(stationGuid);

        /// <summary>Captured at MixingStart, because PlayerUserObject is gone by completion.</summary>
        public StationStarter CaptureStarter(object station)
        {
            var guid = GetStationGuid(station);
            if (string.IsNullOrEmpty(guid)) return null;

            var playerUser = Reflect.Get(station, "PlayerUserObject");
            var npcUser = Reflect.Get(station, "NPCUserObject");

            var starter = new StationStarter
            {
                StationGuid = guid,
                WasNpc = Reflect.IsAlive(npcUser),
                IsLocalPlayer = Reflect.IsAlive(playerUser) && IsLocalPlayer(playerUser),
                PlayerCode = Reflect.IsAlive(playerUser) ? GetPlayerCode(playerUser) : null
            };

            // A start with no observed user in single-player is still the local player: nobody else
            // could have pressed the button.
            if (!starter.WasNpc && !starter.IsLocalPlayer && starter.PlayerCode == null && !IsMultiplayerSession)
            {
                starter.IsLocalPlayer = true;
                starter.PlayerCode = LocalPlayerCode;
            }

            _stationUsers?.RecordStart(starter);
            return starter;
        }

        public void ForgetStation(string stationGuid) => _stationUsers?.Clear(stationGuid);
        public void ResetStationUsers() => _stationUsers?.Reset();

        /// <summary>
        /// Maps a station's NetworkObject user back to the Player that owns it by scanning the
        /// static Player.PlayerList. Cheap — the list is at most a handful of entries.
        /// </summary>
        private object ResolvePlayer(object networkObject)
        {
            if (!Reflect.IsAlive(networkObject) || _playerType == null) return null;

            foreach (var player in Reflect.Enumerate(Reflect.GetStatic(_playerType, "PlayerList")))
            {
                if (player == null) continue;
                var owned = Reflect.Get(player, "NetworkObject");
                if (ReferenceEquals(owned, networkObject)) return player;
            }
            return null;
        }

        // ---------- products ----------

        /// <summary>
        /// Resolves the product a completed mix produced.
        ///
        /// Returns null for a brand-new combination: at MixingDone the product does not exist yet,
        /// because the player has not named it (ProductManager.FinishAndNameMix). The batch is
        /// still recorded, flagged via <see cref="IsOutputKnownForCurrent"/>.
        /// </summary>
        public string ResolveOutputProductId(object mixOperation, string baseProductId, string ingredientId)
        {
            var known = Reflect.CallOut(mixOperation, "IsOutputKnown", out var productDefinition);
            _lastOutputWasKnown = known is bool k && k;
            return _lastOutputWasKnown ? Reflect.GetString(productDefinition, "ID") : null;
        }

        private bool _lastOutputWasKnown = true;

        /// <summary>
        /// Reports the discovery flag captured during the matching
        /// <see cref="ResolveOutputProductId"/> call. The reader always calls them in that order on
        /// one thread, which is why this can be a field rather than a second reflective call.
        /// </summary>
        public bool IsProductDiscovered(string productId) => _lastOutputWasKnown;

        public string GetProductDisplayName(string productId) =>
            Reflect.GetString(FindProductDefinition(productId), "Name") ?? productId;

        public string GetDrugType(string productId) =>
            Reflect.AsString(Reflect.Get(FindProductDefinition(productId), "DrugType"));

        /// <summary>
        /// Full recipe path from a base strain. Deliberately unresolved.
        ///
        /// It requires walking ProductManager.mixRecipes, whose MixRecipeData fields are named
        /// {Product, Mixer, Output} but hold them the OTHER way round in real save data — verified
        /// across five rows, see audit §2.7. Until that is confirmed live, returning null makes the
        /// reader fall back to the single known ingredient: correct-but-shallow rather than
        /// confidently inverted.
        /// </summary>
        public List<string> ResolveIngredientChain(string productId) => null;

        public List<string> ResolveEffects(string productId)
        {
            var definition = FindProductDefinition(productId);
            var properties = Reflect.Enumerate(Reflect.Get(definition, "Properties"));
            var names = new List<string>();
            foreach (var property in properties)
            {
                var name = Reflect.GetString(property, "Name") ?? Reflect.AsString(property);
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names.Count > 0 ? names : null;
        }

        private object FindProductDefinition(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return null;
            var manager = Singleton(_productManagerType);
            foreach (var definition in Reflect.Enumerate(Reflect.Get(manager, "AllProducts")))
            {
                var id = Reflect.GetString(definition, "ID");
                if (string.Equals(id, productId, StringComparison.OrdinalIgnoreCase)) return definition;
            }
            return null;
        }

        /// <summary>Schedule I managers derive from a Singleton&lt;T&gt; base exposing a static Instance.</summary>
        private object Singleton(Type type)
        {
            if (type == null) return null;
            return Reflect.GetStatic(type, "Instance") ?? Reflect.GetStatic(type, "instance");
        }
    }
}
