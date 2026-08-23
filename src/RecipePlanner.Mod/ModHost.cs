using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using RecipePlanner.Core.Identity;
using RecipePlanner.Core.Mixing;
using RecipePlanner.Core.Production;
using RecipePlanner.Core.Pricing;
using RecipePlanner.Core.Recipes;
using RecipePlanner.Core.Reporting;
using RecipePlanner.Core.Stats;
using RecipePlanner.Core.Storage;
using RecipePlanner.Game.Binding;
using RecipePlanner.UI;

namespace RecipePlanner.Mod
{
    /// <summary>
    /// Wires the domain services together and owns per-profile state. Everything here is plumbing:
    /// the decisions all live in Core, which is why Core is the part with the tests.
    /// </summary>
    internal sealed class ModHost : IGameLoadState
    {
        private readonly ILog _log;
        private readonly StorageLayout _layout = new StorageLayout();
        private readonly InMemorySeenEventKeys _seen = new InMemorySeenEventKeys();

        private ProductionTracker _tracker;
        private ProductionHistoryRepository _history;
        private FileRecipeRepository _recipes;
        private RecipeDiscoveryService _discovery;
        private CookbookDataBuilder _cookbookData;
        private GamePriceSource _prices;
        private IPricingEngine _pricing;
        private MixGuideReader _mixGuideReader;
        private MixGuide _mixGuide;

        public PlayerContext Context { get; private set; }

        // --- IGameLoadState, driven by the patches / LoadManager polling ---
        public bool IsGameLoaded { get; private set; }
        private DateTime _loadCompletedUtc = DateTime.MaxValue;
        public double SecondsSinceLoadComplete =>
            _loadCompletedUtc == DateTime.MaxValue ? 0 : (DateTime.UtcNow - _loadCompletedUtc).TotalSeconds;

        public ModHost(ILog log)
        {
            _log = log ?? NullLog.Instance;
            _tracker = new ProductionTracker(this, _seen);
            _tracker.ProductionRecorded += OnRecorded;
            _tracker.ProductionRejected += OnRejected;
        }

        /// <summary>
        /// Called once a save is fully loaded. Until this runs, Context is null and the tracker
        /// rejects everything — the single rule that stops statistics leaking between characters.
        /// </summary>
        public void OnSaveLoaded(PlayerContext context)
        {
            Context = context;
            IsGameLoaded = true;
            _loadCompletedUtc = DateTime.UtcNow;

            AdoptExistingProfile(context);

            _layout.EnsureProfileDir(context.ProfileId);
            new ProfileStore(_layout).Observe(context, DateTime.UtcNow);

            _history = new ProductionHistoryRepository(_layout, context.ProfileId);
            _recipes = new FileRecipeRepository(_layout, context.ProfileId);
            _discovery = new RecipeDiscoveryService(_recipes);
            _discovery.RecipeDiscovered += r =>
                _log.Info($"New recipe discovered: {r.Name} [{r.RecipeId}]");

            // Replay the log so a batch recorded last session is not recorded again this session.
            var existing = _history.ReadAll(out var corrupt);

            // One-off migration for events written before unnamed mixes were handled, which
            // recorded a new mix as producing its own input. Left alone they would credit units to
            // the wrong product and self-loop the lineage tree.
            var repaired = LegacyEventRepair.Apply(existing);
            if (repaired > 0)
            {
                _history.Rewrite(existing);
                _log.Warn($"Repaired {repaired} event(s) recorded by an earlier build that named a " +
                          "new mix after its own input. They now await the real name.");
            }

            existing = ReconcileAgainstSave(context, existing);

            // Point the catalogue at this save and drop anything cached for the previous one,
            // before any repair reads it. This used to happen at the end of the method, which was
            // fine while nothing here needed the game's own data — it is not fine now that the
            // pending-name repair does.
            if (_cookbookData != null) _cookbookData.SaveFolderPath = context.SavePath;
            _cookbookData?.Invalidate();
            _prices?.Invalidate();

            // Batches whose naming event we never saw. Must run before the two repairs below:
            // both skip pending batches by design, so giving them a product first is what lets
            // them be placed and priced in the same load rather than staying stranded.
            var named = ResolvePendingNames(existing);
            if (named > 0)
            {
                _history.Rewrite(existing);
                _log.Info($"Named {named} batch(es) from the game's own recipe list — their naming " +
                          "was never seen by the mod.");
            }

            // Recipes recorded before their mix was named have no output product, which makes them
            // unplaceable in the lineage tree. Repaired here rather than on write, because the ones
            // already on disk would otherwise stay broken until that exact recipe was cooked again.
            var recipesRepaired = RecipeRepair.BackfillFromEvents(_recipes, existing);
            if (recipesRepaired > 0)
            {
                _recipes.Flush();
                _log.Info($"Repaired {recipesRepaired} recipe(s) that were recorded before their mix " +
                          "was named; they can now be placed under their strain.");
            }

            // Batches cooked before their mix was named recorded a value of zero, because the
            // product did not exist to price yet. Repaired on load for the ones already written;
            // RepriceNamed handles them at naming time from here on.
            var valued = RepriceZeroValued(existing);
            if (valued > 0)
            {
                _history.Rewrite(existing);
                _log.Info($"Priced {valued} batch(es) recorded before their product existed.");
            }

            _seen.Seed(existing);
            if (corrupt > 0)
                _log.Warn($"{corrupt} unreadable line(s) in events.jsonl were skipped (likely a crash mid-write).");

            var stats = StatisticsService.Build(context.ProfileId, existing, DateTime.UtcNow);
            _log.Info($"Profile {context} — {stats.Personal.UnitsProduced} units across " +
                      $"{stats.Personal.Batches} batches, {stats.UniqueRecipesProduced} recipes.");

            // Said out loud on every load, not only in co-op. Which side of a session the mod
            // thinks it is on decides how every later batch gets attributed, and reading it back
            // from the log beats inferring it from whether the numbers came out plausible.
            _log.Info($"Session role: {(context.IsHost ? "host or single-player" : "guest in another player's world")}.");

            if (PendingNameResolver.HasPending(existing))
                _log.Info("Some batches are waiting for their mix to be named; they will be " +
                          "credited to the product as soon as you name it in game.");


            // Mix maps are randomised per save, so a guide built for the previous character is
            // not merely stale — it is wrong.
            _mixGuide = null;
        }

        /// <summary>
        /// Keeps a character's history if their profile id changes underneath them.
        ///
        /// The id hashes the organisation name along with three immutable facts, and that name is a
        /// mutable field on the game's side. If it can be changed, the id changes with it and every
        /// statistic, recipe and event for the character is orphaned in a folder nothing looks in
        /// again — a total, silent loss.
        ///
        /// Only runs when the computed id has no folder yet, so an existing profile in use is never
        /// taken from the character using it, and the common path costs one directory check.
        /// </summary>
        private void AdoptExistingProfile(PlayerContext context)
        {
            try
            {
                if (context?.Identity == null || string.IsNullOrEmpty(context.ProfileId)) return;
                if (Directory.Exists(_layout.ProfileDir(context.ProfileId))) return;

                var store = new ProfileStore(_layout);
                var orphan = ProfileAdoption.FindOrphan(
                    _layout.ListProfileIds(),
                    id => store.Load(id)?.Identity,
                    context.Identity);

                if (orphan == null) return;

                _log.Warn($"Profile id changed for this character — adopting the existing history " +
                          $"under {orphan} rather than starting over. This happens if the " +
                          "organisation name changed.");

                context.ProfileId = orphan;
            }
            catch (Exception ex)
            {
                // Starting a fresh profile is a bad outcome but a survivable one; throwing here
                // would stop the save loading at all.
                _log.Warn("Could not check for an existing profile: " + ex.Message);
            }
        }

        /// <summary>
        /// Removes production that the loaded save does not contain, i.e. batches from a session
        /// the player quit without saving. Those stations replay on load and produce the batch
        /// again, so keeping the abandoned record would either double-count it or — because a
        /// deterministic replay lands on the same game-minute — make the genuine batch look like a
        /// duplicate and get it thrown away instead.
        ///
        /// Fails open: if the save's clock cannot be read, nothing is removed.
        /// </summary>
        private List<ProductionEvent> ReconcileAgainstSave(PlayerContext context, List<ProductionEvent> events)
        {
            var clock = SaveClockReader.Read(context.SavePath);
            if (!clock.IsUsable)
            {
                _log.Info("Skipping rollback check — " + clock.Error);
                return events;
            }

            var result = RollbackReconciler.Apply(events, clock.ElapsedDays, clock.TimeOfDay);
            if (!result.Changed)
            {
                // Said out loud even when nothing is removed: a silent check is indistinguishable
                // from one that never ran, and this is the code path that deletes history.
                _log.Info($"Rollback check: save written at {GameClock.Describe(clock.ElapsedDays, clock.TimeOfDay)}, " +
                          $"all {result.Kept.Count} recorded batch(es) predate it — nothing discarded.");
                return events;
            }

            // Archive first: an interruption after this point costs a duplicate record, not history.
            _history.ArchiveRolledBack(result.RolledBack);
            _history.Rewrite(result.Kept);

            _log.Warn(RollbackReconciler.Summarise(result, clock.ElapsedDays, clock.TimeOfDay));
            return result.Kept;
        }

        public void OnSaveUnloaded()
        {
            Flush();
            MixingStationPatches.ResetStationUsers();
            Context = null;
            IsGameLoaded = false;
            _loadCompletedUtc = DateTime.MaxValue;
        }

        /// <summary>
        /// The player has just named a new mix, so the game has finally created the product.
        /// Batches of it recorded beforehand were stored unnamed; give them their identity now.
        /// </summary>
        public void OnMixNamed(string baseProductId, string ingredientId, string productId, string productName)
        {
            if (Context == null || _history == null || string.IsNullOrEmpty(productId)) return;

            try
            {
                var events = _history.ReadAll();
                var applied = PendingNameResolver.Apply(events, baseProductId, ingredientId, productId, productName);
                if (applied == 0) return;

                RepriceNamed(events, productId);
                _history.Rewrite(events);
                _cookbookData?.Invalidate();
                _log.Info($"Named mix '{productName}' ({productId}) — {applied} earlier batch(es) updated.");

                // The recipe now has a real product, so let the cookbook learn it properly.
                foreach (var evt in events)
                    if (string.Equals(evt.OutputProductId, productId, StringComparison.OrdinalIgnoreCase))
                        _discovery?.OnProduced(evt);
            }
            catch (Exception ex)
            {
                _log.Warn("Could not reconcile the named mix: " + ex.Message);
            }
        }

        /// <summary>
        /// Swaps the null pricing engine for one backed by the game. Done once at startup, before
        /// any batch can arrive, so no event is ever priced by the placeholder.
        /// </summary>
        public void AttachPricing(GamePriceSource prices)
        {
            _prices = prices;
            _pricing = new PricingEngine(prices);
            _tracker = new ProductionTracker(this, _seen, _pricing);
            _tracker.ProductionRecorded += OnRecorded;
            _tracker.ProductionRejected += OnRejected;
        }

        /// <summary>
        /// Prices batches that were cooked before their mix had a name.
        ///
        /// They were priced at completion, when the product did not exist yet — the game only
        /// creates it when the player types a name — so every one of them recorded a value of zero
        /// and nothing ever went back for them. Observed on a real save: a batch of a
        /// newly-invented mix sat at TotalValue 0.0 while an already-named product beside it
        /// recorded 900.0. It affected exactly the recipes a player cares most about, the ones they
        /// had just invented.
        ///
        /// The price cache has to be dropped first. It is built per save and this product did not
        /// exist when it was built, so re-pricing against it would confidently produce zero again.
        /// </summary>
        /// <summary>
        /// One-off repair for events already on disk with no value.
        ///
        /// Only touches events that have a product and no value at all — a batch that genuinely
        /// priced to zero is indistinguishable from one that was never priced, so this will retry
        /// it harmlessly rather than guess. Returns how many actually gained a value, and the
        /// caller only rewrites the log when that is non-zero: retrying every load is free, but
        /// rewriting the file every load is not.
        ///
        /// Batches still awaiting a name are skipped. There is nothing to price them against yet,
        /// which is the entire reason they are pending.
        /// </summary>
        /// <summary>
        /// Gives an identity to batches whose naming event the mod never saw.
        ///
        /// The live path (<see cref="OnMixNamed"/>) only fires if the mod is loaded and hooked at
        /// the exact moment the player types the name. Every batch that misses that window is
        /// stranded: with no product it cannot be priced or placed, and both load-time repairs skip
        /// pending batches by design. Nothing else ever came back for them.
        ///
        /// The game's recipe list has held the answer all along, so this asks it instead of waiting.
        ///
        /// A null graph means the catalogue could not be read, and is deliberately treated as "do
        /// not repair". Naming batches from data we could not load is the one outcome worse than
        /// leaving them pending.
        /// </summary>
        private int ResolvePendingNames(List<ProductionEvent> events)
        {
            if (_cookbookData == null || events == null) return 0;
            if (!PendingNameResolver.HasPending(events)) return 0;

            try
            {
                var graph = _cookbookData.TryBuildGraph(_recipes);
                if (graph == null) return 0;

                return PendingNameResolver.ResolveFromRecipes(
                    events, graph.Steps, _cookbookData.DisplayNameOf);
            }
            catch (Exception ex)
            {
                _log.Warn("Could not name pending batches from the recipe list: " + ex.Message);
                return 0;
            }
        }

        private int RepriceZeroValued(List<ProductionEvent> events)
        {
            if (_pricing == null || events == null) return 0;

            var priced = 0;
            foreach (var evt in events)
            {
                if (evt == null || evt.IsAwaitingName) continue;
                if (string.IsNullOrEmpty(evt.OutputProductId)) continue;
                if (evt.TotalValue > 0d) continue;

                _pricing.Price(evt);
                if (evt.TotalValue > 0d) priced++;
            }

            return priced;
        }

        private void RepriceNamed(List<ProductionEvent> events, string productId)
        {
            if (_pricing == null || events == null) return;

            _prices?.Invalidate();

            var repriced = 0;
            foreach (var evt in events)
            {
                if (evt == null) continue;
                if (!string.Equals(evt.OutputProductId, productId, StringComparison.OrdinalIgnoreCase)) continue;
                if (evt.TotalValue > 0d) continue;   // already priced; leave it alone

                _pricing.Price(evt);
                if (evt.TotalValue > 0d) repriced++;
            }

            if (repriced > 0)
                _log.Info($"Priced {repriced} batch(es) of '{productId}' that were cooked before it was named.");
        }

        /// <summary>Hands the phone app a live view. Called once, at startup.</summary>
        public void AttachCookbook(CookbookDataBuilder data, ILog log)
        {
            _cookbookData = data;
            RecipePlannerUI.Log = log;
            RecipePlannerUI.DataSource = BuildCookbookView;
            RecipePlannerUI.SetRecipeHidden = SetRecipeHidden;
            RecipePlannerUI.MixGuideSource = BuildMixGuide;
        }

        /// <summary>Hands the mix guide its reader. Called once, at startup.</summary>
        public void AttachMixGuide(MixGuideReader reader)
        {
            _mixGuideReader = reader;
        }

        /// <summary>
        /// Built once per save and then cached.
        ///
        /// The maps are fixed for a given save — <c>UseRandomizedMixMaps</c> randomises them when
        /// the save is created, not while it is played — so the several thousand point lookups
        /// behind the chart are worth doing once rather than every time the screen opens. The cache
        /// is dropped on save load along with everything else keyed to a character.
        /// </summary>
        private MixGuide BuildMixGuide()
        {
            if (_mixGuide != null) return _mixGuide;
            if (_mixGuideReader == null) return new MixGuide();

            try { _mixGuide = _mixGuideReader.Read(); }
            catch (Exception ex)
            {
                _log.Warn("Mix guide could not be built: " + ex.Message);
                _mixGuide = new MixGuide();
            }

            return _mixGuide;
        }

        /// <summary>
        /// Display-only. The recipe keeps its history and statistics; only the cookbook stops
        /// listing it. Creates the record if the player hides something discovered from game data
        /// that has never been produced.
        /// </summary>
        private void SetRecipeHidden(string recipeId, bool hidden)
        {
            if (_recipes == null || string.IsNullOrEmpty(recipeId)) return;

            var recipe = _recipes.Get(recipeId) ?? new Recipe { RecipeId = recipeId, Source = "auto" };
            recipe.SetHidden(hidden);
            _recipes.Upsert(recipe);

            _log.Info($"{(hidden ? "Hid" : "Restored")} recipe {recipeId} in the cookbook view.");
        }

        private CookbookViewModel BuildCookbookView()
        {
            if (Context == null || _cookbookData == null) return new CookbookViewModel();

            var stats = StatisticsService.Build(Context.ProfileId, _history?.ReadAll(), DateTime.UtcNow);
            return _cookbookData.Build(Context.DisplayName, stats, _recipes, _cookbookQuery);
        }

        /// <summary>Current sort/filter state, owned here so it survives closing the app.</summary>
        private readonly RecipePlanner.Core.Recipes.CookbookQuery _cookbookQuery =
            new RecipePlanner.Core.Recipes.CookbookQuery();

        public void Submit(ProductionCandidate candidate)
        {
            if (candidate == null) return;
            _tracker.Track(candidate, Context);
        }

        private void OnRecorded(ProductionEvent evt)
        {
            _history?.Append(evt);
            _discovery?.OnProduced(evt);

            _log.Info(
                "Production Detected\n" +
                $"  Profile   : {Context}\n" +
                $"  Station   : {evt.StationType} {Short(evt.StationGuid)} ({evt.StationItemId})\n" +
                $"  Product   : {evt.BaseProductId} + {evt.IngredientId} -> {evt.OutputProductName ?? evt.OutputProductId}\n" +
                $"  Quantity  : {evt.Quantity} units ({evt.Quality})\n" +
                $"  Effects   : {Join(evt.Effects)}\n" +
                $"  Recipe    : {evt.RecipeId}\n" +
                $"  Attributed: {evt.Attribution}{Who(evt)}\n" +
                $"  EventKey  : {evt.EventKey}");
        }

        /// <summary>
        /// Names the other player on a batch that was not ours.
        ///
        /// Attribution alone says "Remote", which is the right word and the wrong amount of
        /// information when the question being asked is whether co-op attribution actually works.
        /// </summary>
        private static string Who(ProductionEvent evt) =>
            evt.Attribution == Attribution.Remote && !string.IsNullOrEmpty(evt.ProducedByPlayerCode)
                ? " (player " + evt.ProducedByPlayerCode + ")"
                : string.Empty;

        private void OnRejected(ProductionCandidate candidate, RejectionReason reason)
        {
            // Silent drops are undebuggable, but a rejected duplicate is normal (the Mk2 override
            // calling base), so it stays at info level rather than warning.
            _log.Info($"Production ignored ({reason}): station {Short(candidate?.StationGuid)}, " +
                      $"{candidate?.BaseProductId}+{candidate?.IngredientId} x{candidate?.Quantity}");
        }

        public void Flush()
        {
            if (Context == null || _history == null) return;
            try
            {
                var stats = StatisticsService.Build(Context.ProfileId, _history.ReadAll(), DateTime.UtcNow);
                new StatsStore(_layout).Save(stats);
                _recipes?.Flush();
                WriteReport(stats);
            }
            catch (Exception ex)
            {
                // stats.json is a derived cache — losing it costs nothing, so never let it take the
                // game down on the way out.
                _log.Warn("Could not write derived stats on shutdown: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes the human-readable cookbook next to the JSON.
        ///
        /// On the IL2CPP branch this is the only output the player ever sees, so its failure is
        /// worth a line in the log — but never worth taking the shutdown path down, which is why it
        /// carries its own catch rather than relying on Flush's.
        /// </summary>
        private void WriteReport(PlayerStatistics stats)
        {
            try
            {
                var report = CookbookReport.Render(
                    Context.DisplayName,
                    stats,
                    _recipes?.All(),
                    DateTime.UtcNow);

                var path = _layout.ReportFile(Context.ProfileId);
                File.WriteAllText(path, report, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                _log.Warn("Could not write the readable cookbook: " + ex.Message);
            }
        }

        /// <summary>A new mix has no product until the player names it — say so plainly.</summary>
        private static string Describe(ProductionEvent evt) =>
            evt.IsAwaitingName ? "(new mix, awaiting name)" : (evt.OutputProductName ?? evt.OutputProductId);

        private static string Short(string guid) =>
            string.IsNullOrEmpty(guid) ? "?" : (guid.Length > 8 ? guid.Substring(0, 8) + "…" : guid);

        private static string Join(List<string> items) =>
            items == null || items.Count == 0 ? "(none resolved)" : string.Join(", ", items);
    }
}
