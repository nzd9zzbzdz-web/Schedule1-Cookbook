using System;
using System.Collections.Generic;

namespace RecipePlanner.Core.Identity
{
    /// <summary>
    /// Finds a character's existing history when their profile id changes underneath them.
    ///
    /// <see cref="ProfileId"/> hashes four facts, and three of them are genuinely immutable:
    /// SteamID64, the save's creation date, and its world seed. The fourth, OrganisationName, is a
    /// plain mutable field on the game's GameManager. Nothing in the shipped assemblies proves the
    /// player cannot change it, and if they can, the id changes and every statistic, recipe and
    /// event for that character is orphaned in a folder nothing looks in again.
    ///
    /// That failure would be total, silent, and indistinguishable from the mod forgetting
    /// everything. This is insurance against it, not a fix for an observed bug.
    ///
    /// The name is deliberately NOT dropped from the hash instead. Changing the scheme would
    /// re-key every profile already on disk, which is the exact harm this exists to prevent —
    /// causing it for certain to avoid it happening maybe is a bad trade.
    /// </summary>
    public static class ProfileAdoption
    {
        /// <summary>
        /// A profile already on disk describing the same character, or null.
        ///
        /// Only ever consulted when the computed id has no folder of its own. An existing profile
        /// is never taken from a character who is using it.
        /// </summary>
        public static string FindOrphan(
            IEnumerable<string> knownProfileIds,
            Func<string, SaveIdentity> identityOf,
            SaveIdentity current)
        {
            if (knownProfileIds == null || identityOf == null || current == null) return null;
            if (!current.IsComplete) return null;

            foreach (var candidate in knownProfileIds)
            {
                if (string.IsNullOrEmpty(candidate)) continue;

                SaveIdentity stored;
                try { stored = identityOf(candidate); }
                catch { continue; }   // an unreadable profile is simply not a match

                if (stored == null || !SameCharacter(stored, current)) continue;

                // Same character, different id — which can only mean the mutable part changed.
                return candidate;
            }

            return null;
        }

        /// <summary>
        /// The three facts that cannot change for the life of a save.
        ///
        /// Creation date is compared through its ISO string rather than as a DateTime, because that
        /// string is what the id is hashed from — comparing the underlying value could call two
        /// saves the same character that hash differently, which would be worse than not matching.
        /// </summary>
        private static bool SameCharacter(SaveIdentity a, SaveIdentity b) =>
            a.Seed == b.Seed &&
            string.Equals(a.SteamId64?.Trim(), b.SteamId64?.Trim(), StringComparison.Ordinal) &&
            string.Equals(a.CreationDateIso, b.CreationDateIso, StringComparison.Ordinal);
    }
}
