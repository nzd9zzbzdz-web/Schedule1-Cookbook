using System;
using System.Globalization;

namespace RecipePlanner.Core.Identity
{
    /// <summary>
    /// The four immutable facts that identify one character/save, per Phase 0 audit §1.3.
    ///
    /// Deliberately NOT the save slot: slots get deleted and reused, so SaveGame_2 today is not
    /// SaveGame_2 next week. CreationDate and Seed never change for the life of a save.
    /// </summary>
    public sealed class SaveIdentity
    {
        /// <summary>Steam account that owns the save. Runtime: Player.Local.PlayerCode.</summary>
        public string SteamId64 { get; set; }

        /// <summary>Chosen at character creation. Game.json -&gt; OrganisationName.</summary>
        public string OrganisationName { get; set; }

        /// <summary>
        /// Metadata.json -&gt; CreationDate. The game stores bare Y/M/D/H/M/S with no timezone, so
        /// this is always <see cref="DateTimeKind.Unspecified"/> and is never timezone-converted.
        /// </summary>
        public DateTime CreationDate { get; set; }

        /// <summary>Game.json -&gt; Seed. Immutable world identity.</summary>
        public int Seed { get; set; }

        public SaveIdentity() { }

        public SaveIdentity(string steamId64, string organisationName, DateTime creationDate, int seed)
        {
            SteamId64 = steamId64;
            OrganisationName = organisationName;
            CreationDate = DateTime.SpecifyKind(creationDate, DateTimeKind.Unspecified);
            Seed = seed;
        }

        /// <summary>ISO-8601 to the second, culture-invariant. This exact string feeds the hash.</summary>
        public string CreationDateIso =>
            CreationDate.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(SteamId64) &&
            !string.IsNullOrWhiteSpace(OrganisationName) &&
            CreationDate != default;

        public override string ToString() =>
            $"{OrganisationName} ({SteamId64}, created {CreationDateIso}, seed {Seed})";
    }
}
