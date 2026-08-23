using System;
using System.Collections.Generic;
using RecipePlanner.Core.Identity;
using Xunit;

namespace RecipePlanner.Core.Tests
{
    /// <summary>
    /// ProfileId hashes four facts, and only three of them are immutable: SteamID64, creation date
    /// and world seed. The fourth, OrganisationName, is a plain mutable field on the game's side —
    /// nothing in the shipped assemblies proves a player cannot change it.
    ///
    /// If they can, the id changes and every statistic, recipe and event for that character is
    /// orphaned in a folder nothing looks in again. Total, silent, and indistinguishable from the
    /// mod having forgotten everything.
    ///
    /// These pin the recovery. It is insurance against an unproven risk rather than a fix for an
    /// observed bug — but the cost of being wrong is a player's entire history.
    /// </summary>
    public class ProfileAdoptionTests
    {
        private static readonly DateTime Created = new DateTime(2026, 3, 4, 12, 30, 0);

        private static SaveIdentity Identity(string org, string steam = "76561190000000001", int seed = 12345) =>
            new SaveIdentity(steam, org, Created, seed);

        private static string FindOrphan(SaveIdentity current, params SaveIdentity[] stored)
        {
            var ids = new List<string>();
            var byId = new Dictionary<string, SaveIdentity>(StringComparer.Ordinal);

            foreach (var identity in stored)
            {
                var id = ProfileId.Compute(identity);
                ids.Add(id);
                byId[id] = identity;
            }

            return ProfileAdoption.FindOrphan(ids, id => byId[id], current);
        }

        [Fact]
        public void A_renamed_organisation_finds_its_old_profile()
        {
            var before = Identity("Wobbly Hobbies");
            var after = Identity("Serious Business Ltd");

            // Renaming really does produce a different id — the premise, not an assumption.
            Assert.NotEqual(ProfileId.Compute(before), ProfileId.Compute(after));

            Assert.Equal(ProfileId.Compute(before), FindOrphan(after, before));
        }

        [Fact]
        public void A_different_character_is_never_adopted()
        {
            var mine = Identity("Same Name");
            var theirs = Identity("Same Name", steam: "76561190000000002");

            Assert.Null(FindOrphan(mine, theirs));
        }

        /// <summary>
        /// Same account, same name, different world. Two genuinely separate playthroughs, and
        /// merging them would silently pool one character's statistics into another's.
        /// </summary>
        [Fact]
        public void A_different_seed_is_a_different_character()
        {
            var first = Identity("Acme", seed: 111);
            var second = Identity("Acme", seed: 222);

            Assert.Null(FindOrphan(second, first));
        }

        [Fact]
        public void A_different_creation_date_is_a_different_character()
        {
            var first = new SaveIdentity("76561190000000001", "Acme", Created, 5);
            var second = new SaveIdentity("76561190000000001", "Acme", Created.AddSeconds(1), 5);

            Assert.Null(FindOrphan(second, first));
        }

        [Fact]
        public void Nothing_on_disk_means_nothing_to_adopt()
        {
            Assert.Null(FindOrphan(Identity("Acme")));
            Assert.Null(ProfileAdoption.FindOrphan(null, id => null, Identity("Acme")));
        }

        /// <summary>An incomplete identity cannot be trusted to match anything.</summary>
        [Fact]
        public void An_incomplete_identity_adopts_nothing()
        {
            var partial = new SaveIdentity { SteamId64 = "76561190000000001" };
            Assert.Null(FindOrphan(partial, Identity("Acme")));
        }

        /// <summary>A profile whose file will not load must not abort the search for the others.</summary>
        [Fact]
        public void An_unreadable_profile_is_skipped_rather_than_fatal()
        {
            var good = Identity("Old Name");
            var goodId = ProfileId.Compute(good);

            var found = ProfileAdoption.FindOrphan(
                new[] { "corrupt", goodId },
                id => id == "corrupt" ? throw new InvalidOperationException("bad json") : good,
                Identity("New Name"));

            Assert.Equal(goodId, found);
        }
    }
}
