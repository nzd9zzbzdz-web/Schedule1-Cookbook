using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RecipePlanner.Core.Identity
{
    /// <summary>
    /// Stable per-character key: SHA256(SteamId64 | OrganisationName | CreationDate | Seed),
    /// truncated to 16 bytes and hex-encoded.
    ///
    /// This is the single value that keeps statistics from leaking between characters. Every
    /// service in the mod is inert until one of these exists.
    /// </summary>
    public static class ProfileId
    {
        public const int HexLength = 32;

        /// <summary>Separator chosen so it cannot appear in a SteamID64 or a numeric seed.</summary>
        private const char Separator = '|';

        public static string Compute(SaveIdentity identity)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (!identity.IsComplete)
                throw new ArgumentException(
                    "SaveIdentity is incomplete; refusing to compute a profile id that could collide. " +
                    "Got: " + identity, nameof(identity));

            var payload = string.Join(
                Separator.ToString(),
                identity.SteamId64.Trim(),
                identity.OrganisationName.Trim(),
                identity.CreationDateIso,
                identity.Seed.ToString(CultureInfo.InvariantCulture));

            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var sb = new StringBuilder(HexLength);
                for (int i = 0; i < HexLength / 2; i++) sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool IsValid(string profileId) =>
            !string.IsNullOrEmpty(profileId) &&
            profileId.Length == HexLength &&
            IsLowerHex(profileId);

        private static bool IsLowerHex(string s)
        {
            foreach (var c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
