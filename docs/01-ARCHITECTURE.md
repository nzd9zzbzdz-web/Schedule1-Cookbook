# Architecture

Every binding below is a real symbol confirmed in [Phase 0](00-PHASE-0-AUDIT.md).

## Layer diagram

```
                        Schedule I (Unity 2022.3 / IL2CPP / FishNet)
                                         │
┌────────────────────────────────────────┴────────────────────────────────────────┐
│  GAME BINDING LAYER  (the only code that knows Schedule I types exist)          │
│                                                                                  │
│  SymbolGuard          verifies every hooked type/method at startup;              │
│                       disables the tracker instead of crashing on a game update  │
│  GameHooks            Harmony patches: MixingStation.MixingDone,                 │
│                       LabOven.CreateStationItems, ChemistryStation.Finalize-     │
│                       Operation, Cauldron.FinishCookOperation, DryingRack.Try-   │
│                       EndOperation, Pot/HarvestPlant, ProductManager events      │
│  GameDataReader       ProductManager mix maps, ProductDefinition prices,         │
│                       Registry item lookups, EffectMixCalculator                 │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │  emits plain DTOs — no game types cross this line
┌────────────────────────────────────────┴────────────────────────────────────────┐
│  DOMAIN LAYER  (pure C#, unit-testable, zero Unity dependencies)                 │
│                                                                                  │
│  PlayerContextService   resolves ProfileId; owns save/character identity         │
│  ProductionTracker      dedupes, classifies, validates -> ProductionEvent        │
│  RecipeEngine           resolves a mix chain into a full recipe + effects        │
│  RecipeDiscoveryService records recipes the player actually creates              │
│  PricingEngine          cost / value / profit, backed by the game's own maths    │
│  PlayerStatisticsService  folds events into lifetime aggregates                  │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
┌────────────────────────────────────────┴────────────────────────────────────────┐
│  PERSISTENCE LAYER  (per-profile, outside the game's save tree)                  │
│                                                                                  │
│  ProfileStore   RecipeRepository   ProductionHistoryRepository   StatsStore      │
│  events.jsonl is the source of truth; stats.json is a derived cache              │
└────────────────────────────────────────┬────────────────────────────────────────┘
                                         │
┌────────────────────────────────────────┴────────────────────────────────────────┐
│  UI LAYER — Recipe Planner · Cookbook · My Cooking Stats · Production History    │
└──────────────────────────────────────────────────────────────────────────────────┘
```

## The one flow that matters

```
  Player finishes a mix in-game
        │
        ▼
  MixingStation.MixingDone()                    ← Harmony postfix
        │  read CurrentMixOperation {ProductID, IngredientID, Quantity, ProductQuality}
        │  read PlayerUserObject / NPCUserObject   (attribution)
        │  read GridItem.GUID                      (idempotency)
        ▼
  ProductionTracker
        │  reject if: not loaded yet · duplicate key · not local player · transform stage
        ▼
  RecipeEngine ── resolves base + ingredient chain + resulting effects
        │
        ▼
  PricingEngine ── ingredient cost, product value, estimated profit
        │
        ├──▶ ProductionHistoryRepository   (append to events.jsonl)
        ├──▶ RecipeDiscoveryService        (new recipe? add to cookbook)
        └──▶ PlayerStatisticsService       (fold into stats.json)
                     │
                     ▼
                 UI Dashboard
```

## Service contracts

### PlayerContextService
Resolves and caches the active profile.

- Subscribes to `LoadManager.onLoadComplete`, `LoadManager.onPreSceneChange`,
  `Player.onLocalPlayerSpawned`.
- Reads `LoadManager.ActiveSaveInfo` (`SavePath`, `SaveSlotNumber`, `OrganisationName`,
  `DateCreated`), `Player.Local.PlayerCode`, `Lobby.IsHost`, and `Game.json.Seed`.
- Produces `ProfileId` per [audit §1.3](00-PHASE-0-AUDIT.md).
- **Every other service is inert until this reports a profile.** That single rule prevents the
  "stats mixed between characters" failure and most of the reload-recount failures at once.

### ProductionTracker
Consumes binding-layer callbacks, emits validated `ProductionEvent`s.

Rejection ladder, in order — first match wins:

1. no active profile
2. `LoadManager.IsGameLoaded == false`, or inside the post-load settle window
3. duplicate idempotency key
4. attribution is not `local` (recorded, but excluded from personal totals)
5. event kind is a transform (`dried`, `bricked`, `packaged`) — recorded in its own bucket

### RecipeEngine
Turns `{base product, ingredient}` into a full recipe with resulting effects.

- Mix map from `ProductManager.GetMixerMap/1` (**per profile** — randomized mix maps, audit §3).
- Effects via `EffectMixCalculator.MixProperties/3`.
- Chain resolution walks `ProductManager.mixRecipes` / `GetRecipe/2`.

### RecipeDiscoveryService
Subscribes to `ProductManager.onMixRecipeAdded`, `onNewProductCreated`, `onProductDiscovered`,
and `FinishAndNameMix`. Assigns each recipe a status:

`Planned` -> `Discovered` -> `Produced` -> `Favourite` (statuses are additive flags, not a strict chain).

### PlayerStatisticsService
Pure fold over `events.jsonl`. `stats.json` is a cache and can be deleted at any time and rebuilt —
which is the crash-recovery story and the schema-migration story in one.

## Design rules

1. **No game type crosses the domain boundary.** The binding layer converts to plain DTOs. This is
   what makes the domain unit-testable without launching Unity, and what contains the blast radius
   of a game update to one layer.
2. **Events are the source of truth; aggregates are derived.** Any statistic must be recomputable
   from `events.jsonl` alone.
3. **Hook transitions, never poll state.** See audit §5.
4. **Fail closed.** If `SymbolGuard` cannot verify a hook, the tracker disables itself and says so.
   Wrong statistics are worse than absent statistics.
5. **Never write to the game's save tree.** Audit §1.4.
