using System;
using System.IO;
using System.Linq;
using RecipePlanner.Core.Identity;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Exercises identity resolution against synthetic folders, and — when Schedule I is installed
    /// on this machine — against the player's REAL save files. The real-save tests skip cleanly
    /// elsewhere so CI stays green.
    /// </summary>
    public class SaveFolderReaderTests : IDisposable
    {
        private readonly string _root;

        public SaveFolderReaderTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "rp-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private string MakeSave(
            string steamId = "76561190000000001",
            string slot = "SaveGame_1",
            string org = "Echo",
            int seed = 157034955,
            bool console = false,
            bool randomized = false)
        {
            var dir = Path.Combine(_root, steamId, slot);
            Directory.CreateDirectory(dir);

            File.WriteAllText(Path.Combine(dir, "Game.json"), $@"{{
                ""DataType"": ""GameData"", ""GameVersion"": ""0.4.5f2"",
                ""OrganisationName"": ""{org}"", ""Seed"": {seed},
                ""Settings"": {{ ""ConsoleEnabled"": {console.ToString().ToLowerInvariant()},
                                 ""UseRandomizedMixMaps"": {randomized.ToString().ToLowerInvariant()} }} }}");

            File.WriteAllText(Path.Combine(dir, "Metadata.json"), @"{
                ""DataType"": ""MetaData"", ""GameVersion"": ""0.4.5f2"",
                ""CreationDate"": { ""Year"": 2026, ""Month"": 4, ""Day"": 11,
                                    ""Hour"": 14, ""Minute"": 26, ""Second"": 51 } }");
            return dir;
        }

        [Fact]
        public void Reads_identity_out_of_a_save_folder()
        {
            var info = SaveFolderReader.Read(MakeSave());

            Assert.True(info.IsUsable, info.Error);
            Assert.Equal("76561190000000001", info.Identity.SteamId64);
            Assert.Equal("Echo", info.Identity.OrganisationName);
            Assert.Equal(157034955, info.Identity.Seed);
            Assert.Equal("2026-04-11T14:26:51", info.Identity.CreationDateIso);
            Assert.Equal("0.4.5f2", info.GameVersion);
        }

        [Fact]
        public void Trust_flags_are_carried_off_disk()
        {
            var info = SaveFolderReader.Read(MakeSave(console: true, randomized: true));

            Assert.True(info.ConsoleEnabled);
            Assert.True(info.UseRandomizedMixMaps);
        }

        [Fact]
        public void Steam_id_falls_back_to_the_parent_folder_name()
        {
            // Saves live under Saves\<SteamID64>\SaveGame_N, so the folder identifies the owner
            // when Player.Local is not up yet.
            var info = SaveFolderReader.Read(MakeSave());
            Assert.Equal("76561190000000001", info.Identity.SteamId64);
        }

        [Fact]
        public void A_live_player_code_wins_over_the_folder_name()
        {
            // In multiplayer the folder belongs to the host; the batch belongs to whoever is here.
            var info = SaveFolderReader.Read(MakeSave(), "76561190000000002");
            Assert.Equal("76561190000000002", info.Identity.SteamId64);
        }

        [Theory]
        [InlineData("SaveGame_1", 1)]
        [InlineData("SaveGame_4", 4)]
        [InlineData("NotASave", 0)]
        public void Slot_number_is_parsed_from_the_path(string leaf, int expected)
        {
            Assert.Equal(expected, SaveFolderReader.SlotFromPath(Path.Combine(_root, leaf)));
        }

        [Fact]
        public void A_non_steam_parent_folder_is_not_trusted_as_an_id()
        {
            var dir = Path.Combine(_root, "SomeBackupFolder", "SaveGame_1");
            Directory.CreateDirectory(dir);
            Assert.Null(SaveFolderReader.SteamIdFromPath(dir));
        }

        [Fact]
        public void A_missing_folder_reports_why_instead_of_throwing()
        {
            var info = SaveFolderReader.Read(Path.Combine(_root, "nope"));

            Assert.False(info.IsUsable);
            Assert.Contains("does not exist", info.Error);
        }

        [Fact]
        public void A_corrupt_Game_json_reports_why_instead_of_throwing()
        {
            var dir = MakeSave();
            File.WriteAllText(Path.Combine(dir, "Game.json"), "{ this is not json");

            var info = SaveFolderReader.Read(dir);

            Assert.False(info.IsUsable);
            Assert.Contains("Game.json", info.Error);
        }

        [Fact]
        public void Two_saves_of_the_same_character_name_get_different_profiles()
        {
            var a = SaveFolderReader.Read(MakeSave(slot: "SaveGame_1", org: "Echo", seed: 111));
            var b = SaveFolderReader.Read(MakeSave(slot: "SaveGame_2", org: "Echo", seed: 222));

            Assert.NotEqual(ProfileId.Compute(a.Identity), ProfileId.Compute(b.Identity));
        }

        [Fact]
        public void Build_context_produces_a_ready_to_use_profile()
        {
            var dir = MakeSave(slot: "SaveGame_3", randomized: true);

            var ctx = SaveFolderReader.BuildContext(dir, "76561190000000001", 3, isHost: true);

            Assert.NotNull(ctx);
            Assert.True(ProfileId.IsValid(ctx.ProfileId));
            Assert.Equal(3, ctx.SaveSlotNumber);
            Assert.True(ctx.IsHost);
            Assert.True(ctx.UseRandomizedMixMaps);
            Assert.Equal("0.4.5f2", ctx.GameVersion);
        }

        // ---------------- against the real installed game ----------------

        private static string RealSavesRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "TVGS", "Schedule I", "Saves");

        private static string FirstRealSave()
        {
            if (!Directory.Exists(RealSavesRoot)) return null;
            return Directory.GetDirectories(RealSavesRoot)
                .SelectMany(account => Directory.GetDirectories(account, "SaveGame_*"))
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "Game.json")) &&
                                     File.Exists(Path.Combine(d, "Metadata.json")));
        }

        [SkippableFact]
        public void Reads_a_real_Schedule_I_save()
        {
            var save = FirstRealSave();
            Skip.If(save is null, "Schedule I saves not present on this machine.");

            var info = SaveFolderReader.Read(save);

            Assert.True(info.IsUsable, info.Error);
            Assert.Equal(17, info.Identity.SteamId64.Length);
            Assert.False(string.IsNullOrWhiteSpace(info.Identity.OrganisationName));
            Assert.NotEqual(default, info.Identity.CreationDate);
            Assert.True(ProfileId.IsValid(ProfileId.Compute(info.Identity)));
        }

        [SkippableFact]
        public void Every_real_save_resolves_to_a_distinct_profile()
        {
            Skip.If(!Directory.Exists(RealSavesRoot), "Schedule I saves not present on this machine.");

            var identities = Directory.GetDirectories(RealSavesRoot)
                .SelectMany(a => Directory.GetDirectories(a, "SaveGame_*"))
                .Select(d => SaveFolderReader.Read(d))
                .Where(i => i.IsUsable)
                .ToList();

            Skip.If(identities.Count < 2, "Needs at least two real saves to compare.");

            var ids = identities.Select(i => ProfileId.Compute(i.Identity)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [SkippableFact]
        public void A_real_save_reread_produces_the_same_profile_id()
        {
            var save = FirstRealSave();
            Skip.If(save is null, "Schedule I saves not present on this machine.");

            var first = ProfileId.Compute(SaveFolderReader.Read(save).Identity);
            var second = ProfileId.Compute(SaveFolderReader.Read(save).Identity);

            Assert.Equal(first, second);
        }
    }
}
