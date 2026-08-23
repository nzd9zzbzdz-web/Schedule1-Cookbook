using System;
using System.Collections.Generic;

namespace RecipePlanner.Game.Binding
{
    /// <summary>Who set a station running, captured at start time.</summary>
    public sealed class StationStarter
    {
        public string StationGuid { get; set; }
        public string PlayerCode { get; set; }
        public bool IsLocalPlayer { get; set; }
        public bool WasNpc { get; set; }
    }

    /// <summary>
    /// Remembers who started each station's current operation.
    ///
    /// Necessary because <c>MixingStation.PlayerUserObject</c> means "who is interacting with this
    /// station's UI right now", not "who started this batch" — the game clears it on OnEndUse, and
    /// the player always walks away while a mix runs. Reading it at MixingDone therefore always
    /// yields nobody, and every batch lands as Unattributed.
    ///
    /// So the starter is captured at <c>MixingStart</c> and looked up again at completion.
    /// </summary>
    public sealed class StationUserRegistry
    {
        private readonly Dictionary<string, StationStarter> _byStation =
            new Dictionary<string, StationStarter>(StringComparer.OrdinalIgnoreCase);

        private readonly object _gate = new object();

        public void RecordStart(StationStarter starter)
        {
            if (starter?.StationGuid == null) return;
            lock (_gate) _byStation[starter.StationGuid] = starter;
        }

        public StationStarter Get(string stationGuid)
        {
            if (stationGuid == null) return null;
            lock (_gate) return _byStation.TryGetValue(stationGuid, out var s) ? s : null;
        }

        /// <summary>
        /// Called once the batch is recorded. The station is free to run a different operation
        /// next, possibly started by someone else, so the stale entry must not linger.
        /// </summary>
        public void Clear(string stationGuid)
        {
            if (stationGuid == null) return;
            lock (_gate) _byStation.Remove(stationGuid);
        }

        /// <summary>Dropped on save unload — station GUIDs belong to the save that is going away.</summary>
        public void Reset()
        {
            lock (_gate) _byStation.Clear();
        }

        public int Count { get { lock (_gate) return _byStation.Count; } }
    }
}
