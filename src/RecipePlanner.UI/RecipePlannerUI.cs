using System;
using RecipePlanner.Game.Binding;

namespace RecipePlanner.UI
{
    /// <summary>
    /// The seam between the mod host and whatever is drawing the cookbook.
    ///
    /// Both sides bind here and neither references the other: the mod fills these in at startup
    /// knowing nothing about Unity, and the phone app reads them knowing nothing about MelonLoader.
    ///
    /// It lives in this assembly rather than alongside the UI precisely so the mod can set it on
    /// the IL2CPP branch, where <c>RecipePlanner.PhoneApp</c> cannot load at all. Nothing here may
    /// ever reference UnityEngine or ScheduleOne — that would put the blocker straight back.
    /// </summary>
    public static class RecipePlannerUI
    {
        public static ILog Log { get; set; }

        /// <summary>Supplies what the screen renders. Set by the mod at startup.</summary>
        public static Func<CookbookViewModel> DataSource { get; set; }

        /// <summary>
        /// Hides or restores a recipe in the cookbook view. Display only — the recipe stays in the
        /// game, and its production history and statistics are untouched.
        /// </summary>
        public static Action<string, bool> SetRecipeHidden { get; set; }

        /// <summary>
        /// Raised when the cached view data is dropped, so the UI can drop whatever it cached
        /// alongside it. The phone app binds its sprite cache here.
        ///
        /// This exists because product ids are player-generated and only unique within a save: a
        /// sprite cached under the previous character's id would be drawn against a different
        /// product with the same id. The invalidation has to happen, and the data builder is where
        /// it is known to be needed — but the builder must not reach into Unity to do it, or it
        /// stops loading on the IL2CPP branch and takes the mod down with it.
        /// </summary>
        public static Action CacheInvalidated { get; set; }
    }
}
