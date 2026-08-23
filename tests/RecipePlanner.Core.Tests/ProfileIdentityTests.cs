using System;
using RecipePlanner.Core.Identity;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// Phase 8 exit test: two different saves produce two different ProfileIds, the same save
    /// reloaded produces the same one, and recreating a deleted slot does not collide.
    /// </summary>
    public class ProfileIdentityTests
    {
        [Fact]
        public void Same_save_always_produces_the_same_id()
        {
            var a = ProfileId.Compute(TestKit.Identity());
            var b = ProfileId.Compute(TestKit.Identity());
            Assert.Equal(a, b);
            Assert.True(ProfileId.IsValid(a));
            Assert.Equal(ProfileId.HexLength, a.Length);
        }

        [Fact]
        public void Slot_number_and_path_are_not_part_of_the_key()
        {
            // The same character moved to a different slot must keep its statistics.
            var slot1 = PlayerContext.From(TestKit.Identity(), 1, "…/SaveGame_1");
            var slot3 = PlayerContext.From(TestKit.Identity(), 3, "…/SaveGame_3");
            Assert.Equal(slot1.ProfileId, slot3.ProfileId);
        }

        [Theory]
        [InlineData("76561198000000001", "Echo", 157034955)]   // different steam account
        [InlineData(TestKit.SteamId, "Delta", 157034955)]      // different character name
        [InlineData(TestKit.SteamId, "Echo", 999)]             // different world seed
        public void Any_component_change_produces_a_different_id(string steam, string org, int seed)
        {
            var baseline = ProfileId.Compute(TestKit.Identity());
            var variant = ProfileId.Compute(TestKit.Identity(steam, org, seed));
            Assert.NotEqual(baseline, variant);
        }

        [Fact]
        public void Recreating_a_deleted_slot_with_the_same_name_does_not_collide()
        {
            // The exact scenario the slot number cannot survive: delete SaveGame_2, start a new
            // character with the same organisation name in the same slot.
            var original = TestKit.Identity(org: "Echo", created: new DateTime(2026, 4, 11, 14, 26, 51));
            var replacement = TestKit.Identity(org: "Echo", created: new DateTime(2026, 8, 22, 9, 0, 0), seed: 42);

            Assert.NotEqual(ProfileId.Compute(original), ProfileId.Compute(replacement));
        }

        [Fact]
        public void Creation_date_is_serialized_without_timezone_drift()
        {
            var identity = TestKit.Identity();
            Assert.Equal("2026-04-11T14:26:51", identity.CreationDateIso);
            Assert.Equal(DateTimeKind.Unspecified, identity.CreationDate.Kind);
        }

        [Fact]
        public void Incomplete_identity_is_refused_rather_than_hashed()
        {
            // Hashing a blank identity would give every broken save the same profile — the exact
            // "statistics mixed between characters" failure this key exists to prevent.
            var broken = new SaveIdentity { SteamId64 = TestKit.SteamId };
            Assert.Throws<ArgumentException>(() => ProfileId.Compute(broken));
        }

        [Fact]
        public void Separator_prevents_component_boundary_collisions()
        {
            // "Echo" + seed 12 must not hash the same as "Echo1" + seed 2.
            var a = ProfileId.Compute(TestKit.Identity(org: "Echo", seed: 12));
            var b = ProfileId.Compute(TestKit.Identity(org: "Echo1", seed: 2));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Profile_record_appends_slot_history_instead_of_overwriting()
        {
            var record = new ProfileRecord();
            var now = new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

            record.Observe(TestKit.Context(slot: 1), now);
            record.Observe(TestKit.Context(slot: 1), now.AddMinutes(5));   // same slot -> touch
            record.Observe(TestKit.Context(slot: 3), now.AddMinutes(10));  // moved -> append

            Assert.Equal(2, record.SlotHistory.Count);
            Assert.Equal(1, record.SlotHistory[0].Slot);
            Assert.Equal(3, record.SlotHistory[1].Slot);
            Assert.Equal(now, record.FirstSeenUtc);
        }

        [Fact]
        public void Console_enabled_is_sticky_once_observed()
        {
            var record = new ProfileRecord();
            var now = DateTime.UtcNow;

            var dirty = TestKit.Context();
            dirty.ConsoleEnabled = true;
            record.Observe(dirty, now);

            record.Observe(TestKit.Context(), now.AddMinutes(1)); // console off again

            Assert.True(record.ConsoleEverEnabled);
        }
    }
}
