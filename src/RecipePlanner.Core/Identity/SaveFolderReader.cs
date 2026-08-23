using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RecipePlanner.Core.Identity
{
    /// <summary>
    /// Reads the two files that carry a save's permanent identity, straight off disk:
    /// <c>Game.json</c> (OrganisationName, Seed, Settings) and <c>Metadata.json</c> (CreationDate).
    ///
    /// Disk rather than reflection on purpose. <c>SaveInfo</c> exposes most of this at runtime, but
    /// the world <c>Seed</c> is not on it, and these files are written by the game before the save
    /// finishes loading — so they are available earlier and are stable.
    ///
    /// Read-only. Nothing here ever writes into the game's save tree.
    /// </summary>
    public static class SaveFolderReader
    {
        public sealed class SaveFolderInfo
        {
            public SaveIdentity Identity { get; set; }
            public string GameVersion { get; set; }
            public bool ConsoleEnabled { get; set; }
            public bool UseRandomizedMixMaps { get; set; }

            /// <summary>Why the read failed, when <see cref="Identity"/> is null.</summary>
            public string Error { get; set; }
            public bool IsUsable => Identity != null && Identity.IsComplete;
        }

        /// <summary>
        /// Builds identity from a loaded save folder. <paramref name="steamId64"/> comes from the
        /// live <c>Player.Local.PlayerCode</c>; the folder name is a usable fallback because the
        /// game stores saves under <c>Saves\&lt;SteamID64&gt;\</c>.
        /// </summary>
        public static SaveFolderInfo Read(string saveFolderPath, string steamId64 = null)
        {
            var info = new SaveFolderInfo();

            if (string.IsNullOrWhiteSpace(saveFolderPath) || !Directory.Exists(saveFolderPath))
            {
                info.Error = "Save folder does not exist: " + (saveFolderPath ?? "<null>");
                return info;
            }

            var game = ReadJson(Path.Combine(saveFolderPath, "Game.json"), info);
            if (game == null) return info;

            var metadata = ReadJson(Path.Combine(saveFolderPath, "Metadata.json"), info);
            if (metadata == null) return info;

            var steam = !string.IsNullOrWhiteSpace(steamId64)
                ? steamId64
                : SteamIdFromPath(saveFolderPath);

            if (string.IsNullOrWhiteSpace(steam))
            {
                info.Error = "Could not determine the SteamID64 for this save.";
                return info;
            }

            var creation = ReadDateTime(metadata["CreationDate"]);
            if (creation == default)
            {
                info.Error = "Metadata.json has no readable CreationDate.";
                return info;
            }

            info.Identity = new SaveIdentity(
                steam,
                (string)game["OrganisationName"],
                creation,
                game["Seed"] != null ? (int)game["Seed"] : 0);

            info.GameVersion = (string)game["GameVersion"];

            var settings = game["Settings"];
            if (settings != null)
            {
                info.ConsoleEnabled = settings["ConsoleEnabled"] != null && (bool)settings["ConsoleEnabled"];
                info.UseRandomizedMixMaps = settings["UseRandomizedMixMaps"] != null && (bool)settings["UseRandomizedMixMaps"];
            }

            if (!info.Identity.IsComplete)
                info.Error = "Save identity is incomplete: " + info.Identity;

            return info;
        }

        /// <summary>
        /// Builds the full runtime context. Slot number is informational — never part of the key.
        /// </summary>
        public static PlayerContext BuildContext(string saveFolderPath, string steamId64, int slotNumber, bool isHost)
        {
            var info = Read(saveFolderPath, steamId64);
            if (!info.IsUsable) return null;

            var ctx = PlayerContext.From(info.Identity, slotNumber, saveFolderPath);
            ctx.GameVersion = info.GameVersion;
            ctx.ConsoleEnabled = info.ConsoleEnabled;
            ctx.UseRandomizedMixMaps = info.UseRandomizedMixMaps;
            ctx.IsHost = isHost;
            ctx.LocalPlayerCode = steamId64 ?? info.Identity.SteamId64;
            return ctx;
        }

        /// <summary>Pulls the slot number out of a "…\SaveGame_3" path. 0 when absent.</summary>
        public static int SlotFromPath(string saveFolderPath)
        {
            var name = SafeLeafName(saveFolderPath);
            if (name == null) return 0;

            const string prefix = "SaveGame_";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;

            return int.TryParse(name.Substring(prefix.Length), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var slot) ? slot : 0;
        }

        /// <summary>
        /// The parent folder of a save is the owning account: Saves\&lt;SteamID64&gt;\SaveGame_N.
        /// Used only when the live PlayerCode is unavailable.
        /// </summary>
        public static string SteamIdFromPath(string saveFolderPath)
        {
            try
            {
                var parent = Directory.GetParent(saveFolderPath.TrimEnd('\\', '/'));
                var name = parent?.Name;
                if (string.IsNullOrEmpty(name)) return null;

                // A SteamID64 is 17 digits; anything else is a folder we should not trust.
                if (name.Length != 17) return null;
                foreach (var c in name) if (c < '0' || c > '9') return null;
                return name;
            }
            catch (Exception) { return null; }
        }

        private static JObject ReadJson(string path, SaveFolderInfo info)
        {
            try
            {
                if (!File.Exists(path))
                {
                    info.Error = "Missing " + Path.GetFileName(path);
                    return null;
                }
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                info.Error = $"Could not read {Path.GetFileName(path)}: {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// The game stores dates as loose Y/M/D/H/M/S components with no timezone, so this builds an
        /// Unspecified-kind DateTime and never converts it.
        /// </summary>
        private static DateTime ReadDateTime(JToken token)
        {
            if (token == null) return default;
            try
            {
                return new DateTime(
                    (int)token["Year"], (int)token["Month"], (int)token["Day"],
                    (int)token["Hour"], (int)token["Minute"], (int)token["Second"],
                    DateTimeKind.Unspecified);
            }
            catch (Exception) { return default; }
        }

        private static string SafeLeafName(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return new DirectoryInfo(path.TrimEnd('\\', '/')).Name; }
            catch (Exception) { return null; }
        }
    }
}
