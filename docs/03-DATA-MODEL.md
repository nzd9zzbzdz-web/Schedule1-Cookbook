# Data Model & Persistence

Storage root — deliberately **outside** the game's Steam-Cloud-synced save tree
(see [audit §1.4](00-PHASE-0-AUDIT.md)):

```
%APPDATA%\Schedule1RecipePlanner\
├── config.json
└── profiles\<ProfileId>\
    ├── profile.json
    ├── events.jsonl        <- append-only, SOURCE OF TRUTH
    ├── stats.json          <- derived cache, safe to delete
    ├── recipes.json
    └── snapshots\
```

## profile.json

Identity components are stored in plaintext beside the hash so the key can be recomputed or migrated
if the game ever changes a field.

```json
{
  "profileId": "9f2c4ab1e0d7…",
  "identity": {
    "steamId64": "76561190000000001",
    "organisationName": "Echo",
    "creationDate": "2026-04-11T14:26:51",
    "seed": 157034955
  },
  "slotHistory": [{ "slot": 1, "path": "…/SaveGame_1", "lastSeen": "2026-08-22T13:40:00Z" }],
  "gameVersionFirstSeen": "0.4.5f2",
  "consoleEverEnabled": false,
  "useRandomizedMixMaps": false,
  "firstSeen": "2026-08-22T13:40:00Z",
  "lastSeen": "2026-08-22T13:40:00Z"
}
```

`consoleEverEnabled` and `useRandomizedMixMaps` are recorded because both change how the numbers
should be read. A profile that had the debug console on is annotated, not silently trusted.

## events.jsonl

One JSON object per line, append-only, flushed on write. Every statistic in the mod must be
recomputable from this file alone.

```json
{
  "v": 1,
  "eventKey": "3059421d-3982-47e6-9984-b4b32e892489|greencrack+mouthwash|d40-0924",
  "kind": "mixed",
  "attribution": "local",
  "producedByPlayerCode": "76561190000000001",
  "stationGuid": "3059421d-3982-47e6-9984-b4b32e892489",
  "stationType": "MixingStation",
  "stationItemId": "mixingstationmk2",
  "drugType": "Marijuana",
  "baseProductId": "greencrack",
  "ingredientId": "mouthwash",
  "outputProductId": "…",
  "outputProductName": "…",
  "recipeId": "…",
  "ingredientChain": ["…"],
  "effects": ["…"],
  "quality": "Premium",
  "quantity": 20,
  "unitCost": 12.0,
  "unitValue": 31.0,
  "totalCost": 240.0,
  "totalValue": 620.0,
  "estimatedProfit": 380.0,
  "gameTime": { "elapsedDays": 40, "timeOfDay": 924 },
  "realTime": "2026-08-22T13:41:07Z",
  "gameVersion": "0.4.5f2",
  "flags": { "consoleEnabled": false, "randomizedMixMaps": false }
}
```

### Field notes

| Field | Purpose |
|---|---|
| `eventKey` | Idempotency. `stationGuid + operationSignature + completionGameTime`. Written before the event is folded into stats; a repeat key is dropped. |
| `kind` | `mixed` · `cooked` · `harvested` · `dried` · `bricked` · `packaged`. Only `mixed`, `cooked` and `harvested` count toward "Total Drugs Made" — the rest are transforms (audit §2.5). |
| `attribution` | `local` · `employee` · `remote` · `unattributed` (audit §4). Only `local` counts toward personal totals; the rest stay on disk so the dashboard can re-slice later. |
| `ingredientChain` | The full resolved recipe path, not just the last step — this is what makes Production History readable. |
| `flags` | Trust annotations. Carried per event so a save that later enables the console does not retroactively taint clean history. |
| `v` | Schema version. Bump on any breaking change; the reader migrates forward on replay. |

## stats.json

A pure fold over `events.jsonl`. Deletable and rebuildable at any time — this doubles as the
crash-recovery path and the schema-migration path.

```
totals            unitsProduced, batches, totalCost, totalValue, estimatedProfit
byDrugType        Marijuana | Methamphetamine | Cocaine | Shrooms | MDMA | Heroin
byProduct         units, batches, cost, value, firstProduced, lastProduced
byIngredient      timesUsed, unitsConsumed, totalCost
byRecipe          timesCooked, unitsProduced, totalCost, totalValue, profit,
                  firstProduced, lastProduced
records           mostUsedRecipe, mostProducedDrug, mostUsedIngredient,
                  highestValueRecipe, mostProfitableRecipe, largestBatch
counts            uniqueRecipesCreated, uniqueRecipesProduced
```

`byDrugType` enumerates all six `EDrugType` values including `MDMA` and `Heroin` — both exist in the
game's enum today (audit §2.8), so the schema tolerates them appearing without a migration.

## recipes.json

```json
{
  "recipeId": "…",
  "name": "Blue Lightning",
  "baseProductId": "meth",
  "steps": ["ingredientA", "ingredientB", "ingredientC"],
  "effects": ["…"],
  "status": ["Discovered", "Produced", "Favourite"],
  "discoveredAt": "…",
  "source": "auto"
}
```

`status` is an additive flag set, not a linear state — a recipe can be planned, then discovered in
game, then produced, then favourited, and the history of all four matters.
`source` is `manual` (player planned it) or `auto` (Recipe Discovery Service found it).

## Durability rules

1. Append to `events.jsonl` **before** updating `stats.json`. A crash between the two costs nothing,
   because the next start rebuilds stats from the log.
2. Write `stats.json` and `recipes.json` atomically — temp file, then rename.
3. Snapshot into `snapshots\` periodically so a corrupted tail in the log costs one interval, not
   the whole history.
4. Never write anything into `LocalLow\TVGS\Schedule I\`.
