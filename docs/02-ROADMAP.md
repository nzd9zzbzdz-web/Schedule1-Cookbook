# Roadmap

Adopted as specified, with Phase 0 findings folded in. Each phase has an **exit test** — a concrete
thing that must be demonstrably true before moving on.

Legend: ✅ done and tested · 🟡 logic complete and tested, needs the live game to confirm · ⬜ not started

| Phase | Name | Exit test | Status |
|---|---|---|---|
| **0** | **Schedule I Technical Audit** | Hook table confirmed against the shipped binary | ✅ [audit](00-PHASE-0-AUDIT.md) |
| **1** | **Basic Mod Skeleton** | Mod loads, verifies symbols, survives a save load/unload cycle | ✅ verified live |
| 2 | Game Data Reader | Dump the live mix map, product definitions and prices for the loaded save | 🟡 `GameFacts` reads products; mix-map walk outstanding |
| 3 | Ingredient & Effect Database | Every ingredient and all 34 effects resolved from game data, not hard-coded | ⬜ |
| 4 | Recipe Calculation Engine | Predicted effects match the game's actual output for 20 known mixes | ⬜ |
| 5 | Pricing Engine | Predicted value matches `CalculateProductValue` within rounding for 20 products | 🟡 engine + tests done; needs a real `IPriceSource` |
| 6 | Basic Recipe Planner UI | Plan a recipe and see cost / value / effects | ⬜ |
| 7 | Recipe Saving / Cookbook | Recipes persist across a game restart | ✅ `FileRecipeRepository`, round-trip tested |
| **8** | **Player & Save Identification** | Two saves → two `ProfileId`s; same save reloaded → same id; recreating a deleted slot does **not** collide | ✅ verified live against 4 real saves |
| **9** | **Production Detection** | Cook one batch, get exactly one `Production Detected` line | ✅ verified live |
| 10 | Production History | Events survive a restart; replaying the log reproduces identical totals | ✅ verified live across a full process restart |
| 11 | Player Statistics | Units, batches, per-product breakdown, ingredient usage, value, profit | ✅ verified live (money pending Phase 5) |
| 12 | Automatic Recipe Discovery | A recipe invented in-game appears in the cookbook unprompted | ✅ verified live |
| 13 | Cookbook &amp; Stats app | A phone app showing recipes grouped by strain, with the chain | 🟡 app installs and renders; controls outstanding |
| 14 | Recipe Comparison | — | ⬜ |
| 15 | Inventory Integration | — | ⬜ |
| 16 | Recipe Optimization | — | ⬜ |
| 17 | Import / Export / Sharing | — | ⬜ |
| 18 | Polish & Update Protection | `SymbolGuard` degrades gracefully against a deliberately broken hook table | ✅ 7 tests |

The phases landed out of order on purpose: everything that could be built and proven **without** the
game was built first, so the only work left gated on a game launch is the thin binding layer.

### Immediate next step

**Finish the Cookbook app.** It installs onto the phone and the data layer behind it is fully
tested, but the screen has no controls yet — search, sort and hide all exist in
[`Cookbook.cs`](../src/RecipePlanner.Core/Recipes/Cookbook.cs) with tests, and nothing calls them.

**Phase 5 — a real `IPriceSource`** should land alongside, because every money figure currently
reads 0, which makes two of the seven sort orders useless.

### What the player asked the Cookbook to fix

Their words, and where each is handled:

| Complaint | Where it is solved |
|---|---|
| Hundreds of recipes, impossible to navigate | Collapsible strain sections + search + sort |
| Wants to hide recipes from the UI, not the game | `RecipeStatus.Hidden` — display only; history and stats untouched |
| Recipes should connect as a production process | `RecipeGraph` lineage, verified on 81 real recipes |
| Wants to see the progression | `RecipeGraph.BuildTree` per strain |
| Sorting | 7 orders, favourites pinned above them |
| Weed strains in their own sections | `GroupByBase`, rooted on the game's own base list |

## Phase 9 — confirmed live, 2026-08-22

Real output from the running game:

```
Production Detected
  Profile   : Echo (SaveGame_1, 16696ce8…)
  Station   : MixingStationMk2 eeb1f5a2… (mixingstationmk2)
  Product   : hairypuke + paracetamol -> Extreme Assblaster
  Quantity  : 20 units (Standard)
  Effects   : Paranoia, Sneaky, Calorie-Dense
  Recipe    : hairypuke>paracetamol
  Attributed: Employee
  EventKey  : eeb1f5a2-848e-43ca-8e2a-894b6b0caade|hairypuke+paracetamol|d40-1513
Production ignored (DuplicateEvent): station eeb1f5a2…, hairypuke+paracetamol x20
```

Confirmed in that session:

- **The Mk2 double-fire is real.** `MixingStationMk2.MixingDone()` does call `base.MixingDone()`,
  both patches fire per batch, and the idempotency key absorbs the second every time.
- **Local vs employee attribution works unprompted** — one batch `Local` (counts), one `Employee`
  (recorded, excluded).
- **Recipe discovery fires** for combinations not previously in the cookbook.
- **Statistics survive a full process restart**: `20 units across 1 batches, 1 recipes` — 20 rather
  than 40, because the employee batch stays out of personal totals.

### Negative tests still outstanding

- start a mix and cancel it
- reload a save with a mix already in progress
- move product between storage containers
- dry, brick, or package existing product *(packaging observed to produce nothing, but only because
  no packaging hook exists yet — not yet a real test of the exclusion rule)*
- spawn product via the debug console

## Sequencing notes

- **Phase 8 before 9, strictly.** Detection without identity writes stats into the wrong character.
- **Phase 2 is load-bearing.** `UseRandomizedMixMaps` (audit §3) means a static recipe table is
  simply wrong for some saves. Read the live map or the planner lies.
- **Phase 1 depends on the branch decision** (audit §7): Mono is materially less work than IL2CPP.
  Resolve that before starting Phase 1.
