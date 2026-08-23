using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using RecipePlanner.Core.Identity;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Stats;

namespace RecipePlanner.Core.Storage
{
    /// <summary>
    /// Resolves where our data lives. Deliberately NOT inside the game's save tree: Steam Cloud
    /// syncs TVGS/Schedule I/Saves/{64BitSteamID}/*.json, and the game prunes files it does not
    /// recognise there (Property.DeleteUnapprovedFiles). See Phase 0 audit §1.4.
    /// </summary>
    public sealed class StorageLayout
    {
        public const string AppFolderName = "Schedule1RecipePlanner";

        public string Root { get; }

        public StorageLayout(string root = null)
        {
            Root = root ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppFolderName);
        }

        public string ProfilesRoot => Path.Combine(Root, "profiles");
        public string ConfigFile => Path.Combine(Root, "config.json");
        public string ProfileDir(string profileId) => Path.Combine(ProfilesRoot, profileId);
        public string ProfileFile(string profileId) => Path.Combine(ProfileDir(profileId), "profile.json");
        public string EventLogFile(string profileId) => Path.Combine(ProfileDir(profileId), "events.jsonl");

        /// <summary>
        /// Production removed from the live log because the player quit without saving. Kept so the
        /// one operation that deletes history stays auditable.
        /// </summary>
        public string RolledBackLogFile(string profileId) => Path.Combine(ProfileDir(profileId), "rolled-back.jsonl");
        public string StatsFile(string profileId) => Path.Combine(ProfileDir(profileId), "stats.json");
        public string RecipesFile(string profileId) => Path.Combine(ProfileDir(profileId), "recipes.json");
        public string SnapshotsDir(string profileId) => Path.Combine(ProfileDir(profileId), "snapshots");

        public void EnsureProfileDir(string profileId)
        {
            Directory.CreateDirectory(ProfileDir(profileId));
            Directory.CreateDirectory(SnapshotsDir(profileId));
        }

        public IEnumerable<string> ListProfileIds()
        {
            if (!Directory.Exists(ProfilesRoot)) yield break;
            foreach (var dir in Directory.GetDirectories(ProfilesRoot))
            {
                var name = Path.GetFileName(dir);
                if (RecipePlanner.Core.Identity.ProfileId.IsValid(name)) yield return name;
            }
        }
    }

    public static class Json
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            Formatting = Formatting.None
        };

        public static readonly JsonSerializerSettings Pretty = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
            Formatting = Formatting.Indented
        };

        public static string Write(object o, bool pretty = false) =>
            JsonConvert.SerializeObject(o, pretty ? Pretty : Settings);

        public static T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        /// <summary>
        /// Write to a temp file then replace, so a crash mid-write cannot leave a half-written file
        /// where a valid one used to be.
        /// </summary>
        public static void WriteFileAtomic(string path, object o, bool pretty = true)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Write(o, pretty), new UTF8Encoding(false));

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static T ReadFileOrDefault<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            try { return Read<T>(File.ReadAllText(path, Encoding.UTF8)); }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>
    /// Append-only production log — the source of truth. Every statistic in the mod must be
    /// recomputable from this file alone, which is what makes stats.json safe to delete.
    /// </summary>
    public sealed class ProductionHistoryRepository
    {
        private readonly StorageLayout _layout;
        private readonly string _profileId;

        public ProductionHistoryRepository(StorageLayout layout, string profileId)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _profileId = profileId ?? throw new ArgumentNullException(nameof(profileId));
        }

        public string Path => _layout.EventLogFile(_profileId);
        public string RolledBackPath => _layout.RolledBackLogFile(_profileId);

        public void Append(ProductionEvent evt)
        {
            if (evt == null) return;
            _layout.EnsureProfileDir(_profileId);
            // One line, no newlines inside — Formatting.None guarantees it.
            File.AppendAllText(Path, Json.Write(evt) + "\n", new UTF8Encoding(false));
        }

        /// <summary>
        /// Replays the log. A corrupted trailing line — the classic crash-during-append artefact —
        /// is skipped rather than taking the whole history down with it.
        /// </summary>
        public List<ProductionEvent> ReadAll(out int corruptLines)
        {
            corruptLines = 0;
            var list = new List<ProductionEvent>();
            if (!File.Exists(Path)) return list;

            foreach (var line in File.ReadLines(Path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var evt = Json.Read<ProductionEvent>(line);
                    if (evt != null) list.Add(evt);
                    else corruptLines++;
                }
                catch (JsonException) { corruptLines++; }
            }
            return list;
        }

        public List<ProductionEvent> ReadAll() => ReadAll(out _);

        /// <summary>
        /// Files events away as belonging to an abandoned timeline.
        ///
        /// Written BEFORE the live log is rewritten without them, so an interruption between the
        /// two leaves a harmless duplicate record rather than losing the events entirely.
        /// </summary>
        public void ArchiveRolledBack(IEnumerable<ProductionEvent> events)
        {
            if (events == null) return;
            _layout.EnsureProfileDir(_profileId);

            var text = new StringBuilder();
            foreach (var evt in events)
                if (evt != null) text.Append(Json.Write(evt)).Append('\n');

            if (text.Length == 0) return;
            File.AppendAllText(RolledBackPath, text.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Replaces the whole log atomically.
        ///
        /// The log is append-only in normal operation; this exists for one case: a mix that was
        /// recorded before the player named it, whose earlier batches gain their product identity
        /// retroactively. Written to a temp file and renamed, so an interruption leaves the
        /// previous log intact rather than a half-written one.
        /// </summary>
        public void Rewrite(IEnumerable<ProductionEvent> events)
        {
            if (events == null) return;
            _layout.EnsureProfileDir(_profileId);

            var tmp = Path + ".tmp";
            var encoding = new UTF8Encoding(false);

            using (var writer = new StreamWriter(tmp, false, encoding))
                foreach (var evt in events)
                    if (evt != null) writer.Write(Json.Write(evt) + "\n");

            if (File.Exists(Path)) File.Delete(Path);
            File.Move(tmp, Path);
        }
    }

    public sealed class ProfileStore
    {
        private readonly StorageLayout _layout;
        public ProfileStore(StorageLayout layout) { _layout = layout; }

        public ProfileRecord Load(string profileId) =>
            Json.ReadFileOrDefault<ProfileRecord>(_layout.ProfileFile(profileId)) ?? new ProfileRecord();

        public void Save(ProfileRecord record)
        {
            _layout.EnsureProfileDir(record.ProfileId);
            Json.WriteFileAtomic(_layout.ProfileFile(record.ProfileId), record);
        }

        /// <summary>Load, fold in the current sighting, save. Called once per save load.</summary>
        public ProfileRecord Observe(PlayerContext ctx, DateTime nowUtc)
        {
            var record = Load(ctx.ProfileId);
            record.Observe(ctx, nowUtc);
            Save(record);
            return record;
        }
    }

    public sealed class StatsStore
    {
        private readonly StorageLayout _layout;
        public StatsStore(StorageLayout layout) { _layout = layout; }

        public PlayerStatistics Load(string profileId) =>
            Json.ReadFileOrDefault<PlayerStatistics>(_layout.StatsFile(profileId));

        public void Save(PlayerStatistics stats)
        {
            _layout.EnsureProfileDir(stats.ProfileId);
            Json.WriteFileAtomic(_layout.StatsFile(stats.ProfileId), stats);
        }
    }

    /// <summary>File-backed recipe repository. Loads once, writes atomically on change.</summary>
    public sealed class FileRecipeRepository : IRecipeRepository
    {
        private readonly StorageLayout _layout;
        private readonly string _profileId;
        private readonly Dictionary<string, Recipe> _map = new Dictionary<string, Recipe>(StringComparer.Ordinal);

        public FileRecipeRepository(StorageLayout layout, string profileId)
        {
            _layout = layout;
            _profileId = profileId;
            var loaded = Json.ReadFileOrDefault<List<Recipe>>(_layout.RecipesFile(profileId));
            if (loaded != null)
                foreach (var r in loaded)
                    if (r?.RecipeId != null) _map[r.RecipeId] = r;
        }

        public Recipe Get(string recipeId) =>
            recipeId != null && _map.TryGetValue(recipeId, out var r) ? r : null;

        public IEnumerable<Recipe> All() => _map.Values;

        public void Upsert(Recipe recipe)
        {
            if (recipe?.RecipeId == null) return;
            _map[recipe.RecipeId] = recipe;
            Flush();
        }

        public void Flush()
        {
            _layout.EnsureProfileDir(_profileId);
            Json.WriteFileAtomic(_layout.RecipesFile(_profileId), new List<Recipe>(_map.Values));
        }
    }
}
