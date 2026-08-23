using System.Collections.Generic;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Stats;

namespace RecipePlanner.UI
{
    /// <summary>
    /// Everything the app draws, already computed. The screen does layout only — no queries, no
    /// game access — so all the decisions stay in Core where they are tested.
    /// </summary>
    public sealed class CookbookViewModel
    {
        public string ProfileLabel { get; set; }
        public List<CookbookSection> Sections { get; set; } = new List<CookbookSection>();
        public PlayerStatistics Stats { get; set; }
        public CookbookQuery Query { get; set; } = new CookbookQuery();

        /// <summary>Resolves a product id to the name the game shows the player.</summary>
        public System.Func<string, string> DisplayName { get; set; } = id => id;

        public bool IsEmpty
        {
            get
            {
                if (Sections == null) return true;
                foreach (var s in Sections) if (s.Count > 0) return false;
                return true;
            }
        }

        public int TotalRecipes
        {
            get
            {
                var n = 0;
                if (Sections != null) foreach (var s in Sections) n += s.Count;
                return n;
            }
        }
    }
}
