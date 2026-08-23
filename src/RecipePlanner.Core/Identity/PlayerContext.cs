using System;
using System.Collections.Generic;

namespace RecipePlanner.Core.Identity
{
    /// <summary>
    /// The active profile. Produced by the binding layer once a save is fully loaded; consumed by
    /// every other service. While this is null, the tracker records nothing.
    /// </summary>
    public sealed class PlayerContext
    {
        public string ProfileId { get; set; }
        public SaveIdentity Identity { get; set; }

        /// <summary>Current slot. Informational only — never part of the key.</summary>
        public int SaveSlotNumber { get; set; }
        public string SavePath { get; set; }

        /// <summary>Lobby.IsHost. False means our ServerRpc bodies do not run locally.</summary>
        public bool IsHost { get; set; }

        /// <summary>Player.Local.PlayerCode — the SteamID64 of whoever is at this keyboard.</summary>
        public string LocalPlayerCode { get; set; }

        /// <summary>
        /// The game version recorded in the SAVE, which is the version that last WROTE it —
        /// not necessarily the one running now. A save written on one branch and then loaded
        /// on the other carries the old string, so this identifies the save's provenance
        /// rather than the session's. See <see cref="Branch"/> for what is actually running.
        /// </summary>
        public string GameVersion { get; set; }

        /// <summary>
        /// Which scripting backend is running: "Mono" or "IL2CPP".
        ///
        /// Detected live, unlike <see cref="GameVersion"/>. Observed on a real save: an event
        /// cooked on the default branch recorded "0.4.6f13 Alternate", because the save had
        /// last been written on Mono. The mod supports both branches and behaves differently
        /// on each, so a bug report needs to say which one produced the batch — and a field
        /// that names the wrong one is worse than no field at all.
        /// </summary>
        public string Branch { get; set; }

        /// <summary>
        /// Trust annotations, stamped onto every event. A save that later enables the debug console
        /// must not retroactively taint history that was clean when it was recorded.
        /// </summary>
        public bool ConsoleEnabled { get; set; }

        /// <summary>
        /// Game.json -&gt; Settings.UseRandomizedMixMaps. When true, wiki recipe tables are wrong for
        /// this save and all recipe data must come from the live ProductManager mix maps.
        /// </summary>
        public bool UseRandomizedMixMaps { get; set; }

        public static PlayerContext From(SaveIdentity identity, int slot, string savePath)
        {
            return new PlayerContext
            {
                ProfileId = RecipePlanner.Core.Identity.ProfileId.Compute(identity),
                Identity = identity,
                SaveSlotNumber = slot,
                SavePath = savePath,
                LocalPlayerCode = identity.SteamId64
            };
        }

        /// <summary>
        /// What the player calls this character — just the organisation name.
        ///
        /// Separate from ToString(), which stays noisy on purpose: the slot and profile hash are
        /// what make a log line identify one save out of several, and they are exactly what nobody
        /// wants across the top of the screen.
        /// </summary>
        public string DisplayName => Identity?.OrganisationName ?? "Unknown";

        public override string ToString() =>
            $"{Identity?.OrganisationName} (SaveGame_{SaveSlotNumber}, {ProfileId?.Substring(0, 8)}…)";
    }

    /// <summary>Persisted form of a profile, including the plaintext key components.</summary>
    public sealed class ProfileRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProfileId { get; set; }
        public SaveIdentity Identity { get; set; }
        public List<SlotSighting> SlotHistory { get; set; } = new List<SlotSighting>();
        public string GameVersionFirstSeen { get; set; }
        public bool ConsoleEverEnabled { get; set; }
        public bool UseRandomizedMixMaps { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }

        /// <summary>
        /// Folds a freshly observed context into the stored record. Slot changes are appended to
        /// history rather than overwriting, and ConsoleEverEnabled is sticky.
        /// </summary>
        public void Observe(PlayerContext ctx, DateTime nowUtc)
        {
            ProfileId = ctx.ProfileId;
            Identity = ctx.Identity;
            UseRandomizedMixMaps = ctx.UseRandomizedMixMaps;
            ConsoleEverEnabled |= ctx.ConsoleEnabled;

            if (FirstSeenUtc == default)
            {
                FirstSeenUtc = nowUtc;
                GameVersionFirstSeen = ctx.GameVersion;
            }
            LastSeenUtc = nowUtc;

            var last = SlotHistory.Count > 0 ? SlotHistory[SlotHistory.Count - 1] : null;
            if (last != null && last.Slot == ctx.SaveSlotNumber && last.Path == ctx.SavePath)
                last.LastSeenUtc = nowUtc;
            else
                SlotHistory.Add(new SlotSighting
                {
                    Slot = ctx.SaveSlotNumber,
                    Path = ctx.SavePath,
                    FirstSeenUtc = nowUtc,
                    LastSeenUtc = nowUtc
                });
        }
    }

    public sealed class SlotSighting
    {
        public int Slot { get; set; }
        public string Path { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }
}
