# Roadmap

Adopted as specified, with Phase 0 findings folded in. Each phase has an **exit test** — a concrete
thing that must be demonstrably true before moving on.

Legend: ✅ done and tested · 🟡 logic complete and tested, needs the live game to confirm · ⬜ not started

| Phase | Name | Exit test | Status |
|---|---|---|---|
| **0** | **Schedule I Technical Audit** | Hook table confirmed against the shipped binary | ✅ [audit](00-PHASE-0-AUDIT.md) |
| **1** | **Basic Mod Skeleton** | Mod loads, verifies symbols, survives a save load/unload cycle | ✅ verified live |
| 2 | Game Data Reader | Dump the live mix map, product definitions and prices for the loaded save | ✅ the mix-map walk is what the mixing guide is built on |
| 3 | Ingredient & Effect Database | Every ingredient and all 34 effects resolved from game data, not hard-coded | ✅ `MixGuideReader` — read live, per save |
| 4 | Recipe Calculation Engine | Predicted effects match the game's actual output for 20 known mixes | 🟡 the map is read and transformations derived; PREDICTING a whole mix is still outstanding |
| 5 | Pricing Engine | Predicted value matches `CalculateProductValue` within rounding for 20 products | 🟡 engine, tests and `GamePriceSource` all written; needs live confirmation (release roadmap R5) |
| 6 | Basic Recipe Planner UI | Plan a recipe and see cost / value / effects | ⬜ |
| 7 | Recipe Saving / Cookbook | Recipes persist across a game restart | ✅ `FileRecipeRepository`, round-trip tested |
| **8** | **Player & Save Identification** | Two saves → two `ProfileId`s; same save reloaded → same id; recreating a deleted slot does **not** collide | ✅ verified live against 4 real saves |
| **9** | **Production Detection** | Cook one batch, get exactly one `Production Detected` line | ✅ verified live |
| 10 | Production History | Events survive a restart; replaying the log reproduces identical totals | ✅ verified live across a full process restart |
| 11 | Player Statistics | Units, batches, per-product breakdown, ingredient usage, value, profit | ✅ verified live (money pending Phase 5) |
| 12 | Automatic Recipe Discovery | A recipe invented in-game appears in the cookbook unprompted | ✅ verified live |
| 13 | Cookbook &amp; Stats app | A phone app showing recipes grouped by strain, with the chain | ✅ cookbook, statistics screen and mixing guide, all in-app |
| 14 | Recipe Comparison | — | ⬜ |
| 15 | Inventory Integration | — | ⬜ |
| 16 | Recipe Optimization | — | ⬜ |
| 17 | Import / Export / Sharing | — | ⬜ |
| 18 | Polish & Update Protection | `SymbolGuard` degrades gracefully against a deliberately broken hook table | ✅ 7 tests |

The phases landed out of order on purpose: everything that could be built and proven **without** the
game was built first, so the only work left gated on a game launch is the thin binding layer.

### Immediate next step

**Ship what exists.** Feature work is paused: the cookbook and the production record are done and
the remaining phases are all additive. What stands between this and a public release is packaging
and live verification, not features — see [05-RELEASE-ROADMAP.md](05-RELEASE-ROADMAP.md).

The two feature items still worth noting:

- **Phase 5** — `GamePriceSource` is written and reads the game's own price table. It has never been
  confirmed against a running game, and it fails silently to `$0`, which would look like a bug to a
  player. Release roadmap R5.
- **Phase 13** — the app has sort, filter, hide and favourites. There is deliberately **no search
  box**: a uGUI `InputField` inside the running game steals keyboard focus from the player. Sort and
  filter cover the same need without that risk.
- **Phases 2 and 3 closed by the mixing guide.** `MixGuideReader` walks the live mix maps and reads
  every effect and mixable ingredient out of the running game. Phase 4 is partly there with it: the
  transformation table is derived, but predicting a whole mix end to end is not built.

### The mixing guide

Reading the maps turned out to be the easy part once the shapes were found:

- `Effect` carries `MixDirection`, `MixMagnitude`, `Tier`, `Addictiveness`, `ValueChange` and the
  game's own `LabelColor` — which the cookbook now uses instead of colours of our own invention.
- `MixerMap` holds an effect circle per region plus `GetEffectAtPoint`, so mixing resolves
  spatially rather than through a lookup table.
- `PropertyItemDefinition.Properties` is how both products and mixers carry their effects.

The guide calls the game's own `GetEffectAtPoint` wherever it can, and only falls back to
`MixMapSolver` — our reading of the same geometry — when it cannot, flagging itself as derived so
the UI can say so.

### What the player asked the Cookbook to fix

Their words, and where each is handled:

| Complaint | Where it is solved |
|---|---|
| Hundreds of recipes, impossible to navigate | Collapsible strain sections + 7 sort orders + filters (no search box — see Phase 13 above) |
| Wants to hide recipes from the UI, not the game | `RecipeStatus.Hidden` — greyed and sunk to the bottom, never deleted, one click to restore |
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
