using System;
using System.Collections.Generic;
using RecipePlanner.Core.Production;

namespace RecipePlanner.Core.Recipes
{
    /// <summary>
    /// Repairs recipes that were recorded before their mix had a name.
    ///
    /// A batch cooked before the player names a new mix has no product yet, so the recipe was
    /// created with an empty <see cref="Recipe.OutputProductId"/>. Naming the mix repaired the
    /// event log, but nothing back-filled the recipe — so it stayed parentless forever, and a
    /// recipe with no output cannot be placed in the lineage tree. In the cookbook that shows up as
    /// "origin unknown" on precisely the recipe the player just invented.
    ///
    /// <see cref="RecipeDiscoveryService.OnProduced"/> now back-fills on the next cook, but that
    /// only helps recipes that are cooked again. This repairs the ones already on disk, the same
    /// way <see cref="LegacyEventRepair"/> does for events.
    ///
    /// Runs once per save load, is idempotent, and only ever fills blanks.
    /// </summary>
    public static class RecipeRepair
    {
        /// <summary>Returns how many recipes were repaired.</summary>
        public static int BackfillFromEvents(IRecipeRepository repo, IEnumerable<ProductionEvent> events)
        {
            if (repo == null) return 0;

            var byRecipeId = Index(events);
            if (byRecipeId.Count == 0) return 0;

            var repaired = 0;
            foreach (var recipe in Snapshot(repo))
            {
                if (recipe == null || string.IsNullOrEmpty(recipe.RecipeId)) continue;
                if (!string.IsNullOrEmpty(recipe.OutputProductId)) continue;

                ProductionEvent evt;
                if (!byRecipeId.TryGetValue(recipe.RecipeId, out evt)) continue;

                recipe.OutputProductId = evt.OutputProductId;
                if (string.IsNullOrEmpty(recipe.DrugType)) recipe.DrugType = evt.DrugType;
                if (string.IsNullOrEmpty(recipe.BaseProductId)) recipe.BaseProductId = evt.BaseProductId;
                if (string.IsNullOrEmpty(recipe.Name))
                    recipe.Name = evt.OutputProductName ?? evt.OutputProductId;

                repo.Upsert(recipe);
                repaired++;
            }

            return repaired;
        }

        /// <summary>
        /// The newest named event per recipe id. Events still awaiting a name carry no product, so
        /// they cannot repair anything and are skipped — otherwise the repair would write back the
        /// same blank it is trying to fix.
        /// </summary>
        private static Dictionary<string, ProductionEvent> Index(IEnumerable<ProductionEvent> events)
        {
            var map = new Dictionary<string, ProductionEvent>(StringComparer.Ordinal);
            if (events == null) return map;

            foreach (var evt in events)
            {
                if (evt == null || evt.IsAwaitingName) continue;
                if (string.IsNullOrEmpty(evt.OutputProductId)) continue;

                var id = evt.RecipeId ?? evt.ComputeRecipeId();
                if (string.IsNullOrEmpty(id)) continue;

                ProductionEvent existing;
                if (map.TryGetValue(id, out existing) && existing.RealTimeUtc >= evt.RealTimeUtc) continue;
                map[id] = evt;
            }

            return map;
        }

        /// <summary>Materialised so the repository can be written to while iterating.</summary>
        private static List<Recipe> Snapshot(IRecipeRepository repo)
        {
            var list = new List<Recipe>();
            var all = repo.All();
            if (all != null) list.AddRange(all);
            return list;
        }
    }
}
