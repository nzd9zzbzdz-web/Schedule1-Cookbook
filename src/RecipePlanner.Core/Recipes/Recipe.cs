using System;
using System.Collections.Generic;
using System.Linq;
using RecipePlanner.Core.Production;

namespace RecipePlanner.Core.Recipes
{
    /// <summary>
    /// Additive flags, not a linear state machine. A recipe can be planned, then discovered in
    /// game, then produced, then favourited — and the history of all four matters.
    /// </summary>
    [Flags]
    public enum RecipeStatus
    {
        None = 0,
        Planned = 1 << 0,
        Discovered = 1 << 1,
        Produced = 1 << 2,
        Favourite = 1 << 3,

        /// <summary>
        /// Hidden from the cookbook UI only. The recipe stays in the game and stays in our history
        /// and statistics — this exists purely so a player with hundreds of discovered mixes can
        /// clear the clutter without losing data.
        /// </summary>
        Hidden = 1 << 4
    }

    public sealed class Recipe
    {
        public string RecipeId { get; set; }
        public string Name { get; set; }
        public string BaseProductId { get; set; }
        public List<string> Steps { get; set; } = new List<string>();
        public List<string> Effects { get; set; } = new List<string>();
        public string OutputProductId { get; set; }
        public string DrugType { get; set; }
        public RecipeStatus Status { get; set; } = RecipeStatus.None;

        /// <summary>"manual" when the player planned it, "auto" when discovery found it.</summary>
        public string Source { get; set; } = "manual";

        public DateTime? DiscoveredUtc { get; set; }
        public DateTime? FirstProducedUtc { get; set; }
        public DateTime? LastProducedUtc { get; set; }
        public long TimesProduced { get; set; }

        public bool Has(RecipeStatus flag) => (Status & flag) == flag;

        /// <summary>Hiding never removes anything — it only sets a display flag.</summary>
        public void SetHidden(bool hidden)
        {
            if (hidden) Status |= RecipeStatus.Hidden;
            else Status &= ~RecipeStatus.Hidden;
        }

        public bool IsHidden => Has(RecipeStatus.Hidden);

        public static string ComputeId(string baseProductId, IEnumerable<string> steps)
        {
            var chain = steps == null ? string.Empty : string.Join(">", steps.Where(s => !string.IsNullOrWhiteSpace(s)));
            return string.Concat(baseProductId ?? "?", ">", chain);
        }
    }

    public interface IRecipeRepository
    {
        Recipe Get(string recipeId);
        IEnumerable<Recipe> All();
        void Upsert(Recipe recipe);
    }

    public sealed class InMemoryRecipeRepository : IRecipeRepository
    {
        private readonly Dictionary<string, Recipe> _map = new Dictionary<string, Recipe>(StringComparer.Ordinal);

        public Recipe Get(string recipeId)
        {
            if (recipeId == null) return null;
            return _map.TryGetValue(recipeId, out var r) ? r : null;
        }

        public IEnumerable<Recipe> All() => _map.Values;

        public void Upsert(Recipe recipe)
        {
            if (recipe?.RecipeId == null) return;
            _map[recipe.RecipeId] = recipe;
        }
    }

    /// <summary>
    /// Grows the cookbook from what the player actually does in game. Two entry points: a
    /// production event (they made it), and a game discovery signal (ProductManager raised one of
    /// onMixRecipeAdded / onNewProductCreated / onProductDiscovered).
    /// </summary>
    public sealed class RecipeDiscoveryService
    {
        private readonly IRecipeRepository _repo;

        public RecipeDiscoveryService(IRecipeRepository repo)
        {
            _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        }

        /// <summary>Raised only the first time a recipe enters the cookbook.</summary>
        public event Action<Recipe> RecipeDiscovered;

        /// <summary>
        /// Folds a production event into the cookbook. Only events that created new units count as
        /// "produced" — drying or packaging an existing product is not cooking a recipe.
        /// </summary>
        public Recipe OnProduced(ProductionEvent evt)
        {
            if (evt == null || !ProductionEvent.CreatesNewUnits(evt.Kind)) return null;

            var id = evt.RecipeId ?? evt.ComputeRecipeId();
            var recipe = _repo.Get(id);
            var isNew = recipe == null;

            if (isNew)
            {
                recipe = new Recipe
                {
                    RecipeId = id,
                    Name = evt.OutputProductName ?? evt.OutputProductId,
                    BaseProductId = evt.BaseProductId,
                    Steps = BuildSteps(evt),
                    OutputProductId = evt.OutputProductId,
                    DrugType = evt.DrugType,
                    Source = "auto",
                    DiscoveredUtc = evt.RealTimeUtc
                };
                recipe.Status |= RecipeStatus.Discovered;
            }

            if (recipe.Effects.Count == 0 && evt.Effects != null && evt.Effects.Count > 0)
                recipe.Effects = new List<string>(evt.Effects);
            if (string.IsNullOrEmpty(recipe.Name))
                recipe.Name = evt.OutputProductName ?? evt.OutputProductId;

            // Back-fill what the recipe could not know when it was first recorded.
            //
            // A mix cooked before the player names it has no product yet, so the recipe is created
            // with an empty OutputProductId — and nothing ever filled it in afterwards, because
            // this branch only runs for recipes that already exist. The result was a permanently
            // parentless recipe: PendingNameResolver would repair the *events*, this method would
            // be called again with the now-named event, and the one field that identifies what the
            // recipe actually produces stayed blank forever.
            Backfill(recipe, evt.OutputProductId, evt.DrugType, evt.BaseProductId, recipe.Name);

            recipe.Status |= RecipeStatus.Produced;
            recipe.TimesProduced++;
            if (recipe.FirstProducedUtc == null || evt.RealTimeUtc < recipe.FirstProducedUtc)
                recipe.FirstProducedUtc = evt.RealTimeUtc;
            if (recipe.LastProducedUtc == null || evt.RealTimeUtc > recipe.LastProducedUtc)
                recipe.LastProducedUtc = evt.RealTimeUtc;

            _repo.Upsert(recipe);
            if (isNew) RecipeDiscovered?.Invoke(recipe);
            return recipe;
        }

        /// <summary>
        /// Records a recipe the game told us about directly, without the player having produced it
        /// through a station we observed.
        /// </summary>
        public Recipe OnGameDiscovery(
            string baseProductId,
            IEnumerable<string> steps,
            string outputProductId,
            string outputName,
            DateTime whenUtc)
        {
            var stepList = steps?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? new List<string>();
            var id = Recipe.ComputeId(baseProductId, stepList);

            var recipe = _repo.Get(id);
            var isNew = recipe == null;
            if (isNew)
            {
                recipe = new Recipe
                {
                    RecipeId = id,
                    Name = outputName ?? outputProductId,
                    BaseProductId = baseProductId,
                    Steps = stepList,
                    OutputProductId = outputProductId,
                    Source = "auto",
                    DiscoveredUtc = whenUtc
                };
            }

            recipe.Status |= RecipeStatus.Discovered;
            if (recipe.DiscoveredUtc == null) recipe.DiscoveredUtc = whenUtc;

            // The same back-fill OnProduced does, and for the same reason. A recipe first recorded
            // from an unnamed mix has no product; if the game later announces that recipe, this is
            // the moment its identity becomes knowable. Without it the record stays parentless even
            // though the answer arrived — and a recipe with no output cannot be placed in the
            // lineage tree, which is what puts it under "origin unknown".
            Backfill(recipe, outputProductId, null, baseProductId, outputName);

            _repo.Upsert(recipe);
            if (isNew) RecipeDiscovered?.Invoke(recipe);
            return recipe;
        }

        /// <summary>
        /// Fills in what a recipe could not know when it was first recorded.
        ///
        /// Blank-only, never overwriting. A recipe id is base + steps, so the same recipe must
        /// always yield the same product; a differing value means something is wrong upstream, and
        /// quietly taking the newer one would hide it.
        /// </summary>
        private static void Backfill(
            Recipe recipe, string outputProductId, string drugType, string baseProductId, string name)
        {
            if (string.IsNullOrEmpty(recipe.OutputProductId) && !string.IsNullOrEmpty(outputProductId))
                recipe.OutputProductId = outputProductId;

            if (string.IsNullOrEmpty(recipe.DrugType) && !string.IsNullOrEmpty(drugType))
                recipe.DrugType = drugType;

            if (string.IsNullOrEmpty(recipe.BaseProductId) && !string.IsNullOrEmpty(baseProductId))
                recipe.BaseProductId = baseProductId;

            if (string.IsNullOrEmpty(recipe.Name) && !string.IsNullOrEmpty(name))
                recipe.Name = name;
        }

        private static List<string> BuildSteps(ProductionEvent evt)
        {
            if (evt.IngredientChain != null && evt.IngredientChain.Count > 0)
                return new List<string>(evt.IngredientChain);
            return string.IsNullOrWhiteSpace(evt.IngredientId)
                ? new List<string>()
                : new List<string> { evt.IngredientId };
        }
    }
}
