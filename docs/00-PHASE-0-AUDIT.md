# Phase 0 — Schedule I Technical Audit

**Status:** Complete (first pass), evidence-backed.
**Audited build:** Steam AppID `3164500`, buildid `24705572`, depot `3164501`.
**Game version (from save files):** `0.4.5f2`
**Engine:** Unity `2022.3.62f2`
**Scripting backend:** **IL2CPP** (`GameAssembly.dll` + `Schedule I_Data/il2cpp_data/Metadata/global-metadata.dat`, metadata version 31)
**Root namespace:** `ScheduleOne`
**Networking:** FishNet (Steam transport)
**Surface size:** 18,963 types / 147,513 methods

Everything below was read directly out of the shipped `global-metadata.dat` and out of real save
files. Nothing in the "verified" sections is inferred from wikis or memory. Items that still need
runtime confirmation are marked **[VERIFY AT RUNTIME]**.

Reproduce any of it with the tooling in [`tools/il2cpp-dump/`](../tools/il2cpp-dump/).

---

## 1. Player, Save and Character Identity

### 1.1 On-disk layout (verified)

```
%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\
├── Player.log                     <- managed stack traces, useful for auditing
└── Saves\
    └── <SteamID64>\               <- the Steam account that OWNS these saves
        ├── steam_autocloud.vdf    <- Steam Cloud syncs this tree
        ├── Backups\
        └── SaveGame_1 .. _N\
            ├── Game.json          <- OrganisationName, Seed, Settings
            ├── Metadata.json      <- CreationDate, LastPlayedDate, versions
            ├── Money.json         <- OnlineBalance, Networth, LifetimeEarnings
            ├── Time.json          <- ElapsedDays, Playtime
            ├── Rank.json          <- Rank, Tier, XP, TotalXP
            ├── Products.json      <- DiscoveredProducts, MixRecipes, prices
            ├── Players\
            │   ├── Player_0\                  <- the HOST
            │   └── Player_<SteamID64>\        <- each joined client
            │       └── Player.json  -> { "PlayerCode": "<SteamID64>", ... }
            └── Properties\*.json   <- all placed stations live here
```

Two findings settle the identity question:

1. **The save folder is already keyed by Steam ID** — `Saves\<SteamID64>\`.
2. **`Player.json` carries `PlayerCode`, which is the SteamID64 — including for `Player_0`.**
   The host is stored as `Player_0` on disk but still records its real Steam ID inside.

`Game.json` (real sample):

```json
{ "GameVersion": "0.4.5f2", "OrganisationName": "Echo", "Seed": 157034955,
  "Settings": { "ConsoleEnabled": false, "UseRandomizedMixMaps": false } }
```

`Metadata.json` carries `CreationDate` (Y/M/D/H/M/S), `LastPlayedDate`, `CreationVersion`,
`LastSaveVersion`.

### 1.2 Runtime API (verified in metadata)

| Class | Members we care about |
|---|---|
| `ScheduleOne.Persistence.LoadManager` | `ActiveSaveInfo`, `LoadedGameFolderPath`, `IsGameLoaded`, `IsLoading`, `SaveGames`, `LastPlayedGame`; events `onPreLoad`, `onLoadComplete`, `OnLocalSaveLoadStart`, `onSaveInfoLoaded`, `onPreSceneChange`, `onSceneChangeDone`; `StartGame/3`, `ExitToMenu/3` |
| `ScheduleOne.Persistence.SaveInfo` | `SavePath`, `SaveSlotNumber`, `OrganisationName`, `DateCreated`, `DateLastPlayed`, `Networth`, `SaveVersion`, `MetaData` |
| `ScheduleOne.Persistence.SaveManager` | events `onSaveStart`, `onSaveComplete`; `SaveName`, `PlayersSavePath`, `BackupFolderPath`, `SAVE_GAME_PREFIX`, `SAVE_SLOT_COUNT` |
| `ScheduleOne.PlayerScripts.Player` | **static** `Local`, **static** `PlayerList`, **static** events `onLocalPlayerSpawned` / `onPlayerSpawned` / `onPlayerDespawned`; instance `PlayerCode`, `PlayerName`, `Connection`, `IsLocalPlayer`, `CurrentProperty` |
| `ScheduleOne.PlayerScripts.PlayerManager` | `GetPlayer/1`, `PlayerList` |
| `ScheduleOne.Networking.Lobby` | `IsHost`, `LobbyID`, `IsInLobby`, `PlayerCount`, `GetLobbyMemberIDs/0`; event `OnLobbyChange` |

### 1.3 Recommended profile key

**Do not key on the save slot.** Slots get deleted and reused — `SaveGame_2` today is not
`SaveGame_2` next week. Do not key on folder path alone either.

```
ProfileId = SHA256( SteamID64 | OrganisationName | CreationDate(ISO-8601) | Seed )  [first 16 bytes, hex]
```

Rationale:

- `SteamID64` — separates accounts, and in multiplayer separates *you* from the host.
- `OrganisationName` — the closest thing the game has to a **character name**; chosen at creation.
- `CreationDate` — immutable for the life of the save; makes two saves with the same org name distinct.
- `Seed` — immutable world identity; catches the pathological "same name, same second" case.

Store the four components in plaintext next to the hash so the key can be recomputed or migrated if
the game changes a field. Keep a `slotHistory` array so the UI can say "this profile currently lives
in SaveGame_3" without ever *trusting* the slot.

**[VERIFY AT RUNTIME]** Confirm `SaveInfo.DateCreated` and the world `Seed` are reachable from a
loaded session. If `Seed` is not exposed at runtime, read it once from
`LoadManager.LoadedGameFolderPath + "/Game.json"` — that path is verified to exist.

### 1.4 Where our data goes

The requirement was: **do not modify the game's save data.** We will not.

```
%APPDATA%\Schedule1RecipePlanner\
├── config.json
└── profiles\<ProfileId>\
    ├── profile.json        <- identity components, slot history, first/last seen
    ├── events.jsonl        <- append-only production event log (source of truth)
    ├── stats.json          <- derived aggregates (rebuildable from events.jsonl)
    ├── recipes.json        <- planned + discovered + favourite recipes
    └── snapshots\          <- periodic compaction checkpoints
```

Deliberately **outside** `LocalLow\TVGS\Schedule I\Saves\`, because that tree is Steam-Cloud-synced
(`steam_autocloud.vdf` is present). Writing there risks cloud conflicts — and the game exposes
`Property.DeleteUnapprovedFiles/1`, meaning it actively prunes files it does not recognise inside its
own save tree. That method name alone is reason enough to stay out.

Trade-off to accept: our stats will not follow the player to another PC. Offer explicit
Import/Export (Phase 17) instead of piggybacking on Steam Cloud.

---

## 2. THE CRITICAL QUESTION — Detecting Completed Production

> *What exact classes/methods/events let us reliably know the local player completed production,
> including product, quantity, ingredients, effects and recipe?*

Schedule I has **four distinct production pipelines plus three transform stages**. They must be
treated separately — conflating them is the single biggest risk to statistical accuracy.

### 2.1 MixingStation — the main recipe system

`ScheduleOne.ObjectScripts.MixingStation`

**Operation struct** — `ScheduleOne.ObjectScripts.MixOperation`:
fields `ProductID`, `ProductQuality`, `IngredientID`, `Quantity`; methods `GetOutput/1`, `IsOutputKnown/1`.

- **State:** `CurrentMixOperation`, `CurrentMixTime`, `IsMixingDone`
- **Slots:** `ProductSlot`, `MixerSlot`, `OutputSlot`
- **Attribution:** `PlayerUserObject`, `NPCUserObject`
- **Events (fields):** `onMixStart`, `onMixDone`, `onOutputCollected`, `onStartButtonClicked`
- **Config:** `MixTimePerItem`, `MaxMixQuantity`

**Verified call flow:**

```
StartButtonClicked(..)
  └─ CanStartMix()
      └─ SendMixingOperation(..)        [ServerRpc]
          └─ SetMixOperation(..)        [ObserversRpc + TargetRpc]
              └─ MixingStart()                      -- fires onMixStart
                  └─ OnMinPass() / OnTimePass(..)   -- decrements CurrentMixTime
                      └─ IsCurrentMixingOperationComplete()
                          └─ MixingDone_Networked() [ObserversRpc]
                              └─ MixingDone()       -- fires onMixDone   ** HOOK HERE **
                                  └─ TryCreateOutputItems()  [ServerRpc - host only]
                                      └─ OutputChanged()  -- fires onOutputCollected
```

**Primary hook: `MixingStation.MixingDone()`**

- It is reached on **every client** (dispatched via the `MixingDone_Networked` ObserversRpc).
- It runs **after** completion is confirmed, so no partial or cancelled operation reaches it.
- `CurrentMixOperation` is **still populated** at that point — this is where we read the batch.

At the hook we can read, with no guessing:

| Wanted | Source |
|---|---|
| Base product | `CurrentMixOperation.ProductID` (also `GetProduct()`) |
| Ingredient | `CurrentMixOperation.IngredientID` (also `GetMixer()`) |
| Units produced | `CurrentMixOperation.Quantity` (also `GetMixQuantity()`) |
| Input quality | `CurrentMixOperation.ProductQuality` (`EQuality`) |
| Resulting product | `MixOperation.GetOutput(..)` |
| Was it a new discovery | `MixOperation.IsOutputKnown(..)` |
| All ingredients | `GetIngredients()` |
| Produced by whom | `PlayerUserObject` / `NPCUserObject` |
| Station identity | `GridItem.GUID` (persisted as `"GUID"` in the station's save `BaseData`) |

**Caveat 1:** `TryCreateOutputItems()` is `[ServerRpc]` — on a non-host client the items materialise
through slot replication, not locally. So `MixingDone` proves *the operation completed*; if you also
need *"items actually landed"*, additionally observe `onOutputCollected` / output-slot change.

**Caveat 3 — the user objects are `NetworkObject`s, not `Player`s.**

```
prop NetworkObject PlayerUserObject      prop NetworkObject NPCUserObject
```

`PlayerUserObject` is a FishNet `NetworkObject`. It has no `PlayerCode`, so reading attribution
straight off it silently yields null. Map it back to a `Player` by scanning the static
`Player.PlayerList` and matching `NetworkObject` — see `ReflectionGameFacts.ResolvePlayer`.

Also note `BuildableItem.GUID` is a `System.Guid`, not a `string`; `ToString()` produces exactly the
value the save file records, which is what makes the idempotency key stable across sessions.

**Caveat 2 — the Mk2 trap. This one will silently break Phase 9 if missed.**

`ScheduleOne.ObjectScripts.MixingStationMk2` is a subclass that **overrides**
`MixingDone/0`, `MixingStart/0`, `OnTimePass/1` and `SetMixerToLowered/0`:

```
=== ScheduleOne.ObjectScripts.MixingStationMk2 ===
  METHODS: OnTimePass/1, MixingStart/0, MixingDone/0, EnableScreen/0,
           UpdateScreen/0, DisableScreen/0, SetMixerToLowered/0, .ctor/0, Awake/0
```

It declares no operation state of its own — `CurrentMixOperation` is inherited — so all the reads in
the table above still work. But a Harmony patch targets **one method body**, and an override is a
different method body. Patching only `MixingStation.MixingDone` would miss every Mk2 station.

This is not hypothetical: the audited save's own station is `"ID": "mixingstationmk2"`.

**Patch both `MixingStation.MixingDone` and `MixingStationMk2.MixingDone`.**
**[VERIFY AT RUNTIME]** whether the Mk2 override calls `base.MixingDone()`. If it does, both patches
fire for one batch — which the `eventKey` idempotency guard (§5) already absorbs. Confirm the guard
actually catches it rather than assuming, and log when it does.

The same shape applies to `ScheduleOne.Packaging.PackagingStationMk2` and
`ScheduleOne.PlayerTasks.PackageProductTaskMk2`. Assume every station may gain a `MkN` variant in a
future update, and have `SymbolGuard` enumerate subclasses rather than hard-coding one type name.

### 2.2 LabOven — meth

`ScheduleOne.ObjectScripts.LabOven`, operation `ScheduleOne.ObjectScripts.OvenCookOperation`
(`IngredientID`, `IngredientQuality`, `IngredientQuantity`, `ProductID`, `CookProgress`,
`cookDuration`; `UpdateCookProgress/1`, `IsComplete/0`, `IsReady/0`, `GetProductItem/1`,
`GetCookDuration/0`).

```
SendCookOperation(..) -> SetCookOperation(..) -> OnUncappedMinPass()/OnTimePass(..)
  -> IsReadyForHarvest() -> CreateStationItems(..) -> OutputSlotChanged()   ** HOOK **
```

The oven has a **manual harvest step** (`CreateHammer`, `Shatter/2`, `ClearShards`) — the player
smashes the tray. "Cook complete" and "units obtained" are genuinely different moments here; track
`IsReadyForHarvest -> CreateStationItems` for units.

### 2.3 ChemistryStation — meth / cocaine intermediates

Operation `ScheduleOne.ObjectScripts.ChemistryCookOperation` (`recipe`, `RecipeID`,
`ProductQuality`, `CurrentTime`; `Progress/1`, `IsComplete/0`). Recipes are
`ScheduleOne.StationFramework.StationRecipe`.

```
SendCookOperation(..) -> SetCookOperation(..) -> OnMinPass()/OnTimePass(..)
  -> IsComplete() -> FinalizeOperation()   ** HOOK **  -> ResetStation()
```

Also useful: `HasIngredientsForRecipe/1`, `GetIngredients/0`, `DoesOutputHaveSpace/1`, `CreateTrash/1`.

### 2.4 Cauldron — cocaine base

Events: `onCookStart`, `onCookEnd`, `onStartButtonClicked`.
Fields: `COCA_LEAF_REQUIRED`, `INGREDIENT_SLOT_COUNT`, `CookTime`, `RemainingCookTime`, `InputQuality`.

```
ButtonClicked(..) -> HasIngredients() -> SendCookOperation(..) -> StartCookOperation(..)
  -> OnMinPass()/OnTimePass(..) -> FinishCookOperation()  ** HOOK ** (or subscribe onCookEnd)
  -> RemoveIngredients()
```

### 2.5 Transform stages — MUST NOT be counted as production

| Stage | Class | Why it is a transform |
|---|---|---|
| Drying | `DryingRack` — events `onOperationStart`, `onOperationComplete`, `onOperationsChanged`; `StartOperation/0`, `TryEndOperation/4`, `GetOperationsAtTargetQuality/0`, `DRY_MINS_PER_TIER` | Raises **quality** of existing units. Counting it double-counts every bud and shroom. |
| Bricking | `BrickPress` — `CompletePress/1`, `GetProductInMould/0`, `HasSufficientProduct/1` | Compresses existing cocaine into bricks. |
| Packaging | `ScheduleOne.Packaging.PackagingStationMk2.StartTask`, `PackagingTool.DeployPackaging`, `ScheduleOne.PlayerTasks.PackageProductTaskMk2` | Re-packages existing units (jar / baggie / brick). |
| Growing | `ScheduleOne.ObjectScripts.Pot` (`PlantSeed_Client`, `SetHarvestableActive_Client`), `ScheduleOne.PlayerTasks.HarvestPlant`, `onFullyHarvested` | *Is* genuine creation of raw bud — count it, but as **"harvested"**, in its own bucket, never as a mixed batch. |

Record all of these as **separate event kinds** so the dashboard can show them without polluting
"Total Drugs Made".

### 2.6 `ProductManager` — the central authority

`ScheduleOne.Product.ProductManager` (singleton, `NetworkBehaviour`, `ISaveable`)

**Events (plain fields — hookable):**
`onProductDiscovered`, `onMixCompleted`, `onNewProductCreated`, `onSecondUniqueProductCreated`,
`onMixRecipeAdded`, `onProductListed`, `onProductDelisted`, `onProductFavourited`,
`onProductUnfavourited`, `onContractReceiptRecorded`, `onProductDataSentToConnection`

**Collections:** `DiscoveredProducts`, `ListedProducts`, `FavouritedProducts`, `AllProducts`,
`DefaultKnownProducts`, `ValidMixIngredients`, `ProductNames`, `mixRecipes`, `ProductPrices`,
`createdProducts`, `ContractReceipts`

**Mix maps:** `WeedMixMap`, `MethMixMap`, `CokeMixMap`, `ShroomMixMap`, `GetMixerMap/1`

**Creation:** `CreateWeed/6`, `CreateMeth/6`, `CreateCocaine/6`, `CreateShroom_Client/6`
(plus `CreateWeed_Server/5`, `CreateMeth_Server/5`, `CreateCocaine_Server/5`, `CreateShroom_Server/5`)

**Recipes:** `CreateMixRecipe/4`, `SendMixRecipe/3`, `GetRecipe/2`, `GetKnownProduct/2`

**Discovery / naming:** `DiscoverProduct/1`, `SetProductDiscovered/3`, `CheckDiscovery/1`,
`FinishAndNameMix/3`, `FinishAndNameMix/4`, `SendFinishAndNameMix/4`, `IsMixNameValid/1`,
`MakeIDFileSafe/1`; flags `MethDiscovered`, `CocaineDiscovered`, `ShroomsDiscovered`

**Valuation:** `CalculateProductValue/2`, `GetPrice/1`, `SetPrice/3`, `SendPrice/2`,
`MIN_PRICE`, `MAX_PRICE`, `RefreshHighestValueProduct/0`, `highestValueProduct`

### 2.7 Automatic recipe discovery — exact hooks

Everything Phase 12 needs already exists as first-class game events:

| Signal | Hook |
|---|---|
| A brand-new single-step recipe was learned | `ProductManager.onMixRecipeAdded` / `CreateMixRecipe/4` |
| A brand-new product came into existence | `ProductManager.onNewProductCreated` |
| Player's 2nd unique creation (progression beat) | `ProductManager.onSecondUniqueProductCreated` |
| A product became "discovered" | `ProductManager.onProductDiscovered`, `DiscoverProduct/1`, `CheckDiscovery/1` |
| Player is naming a new mix | `ProductManager.FinishAndNameMix/3` and `/4`, `ScheduleOne.UI.NewMixScreen.onMixNamed` |
| UI detected an unknown mix in the station | `ScheduleOne.UI.Stations.MixingStationInterface.CheckForUnknownMix/0` |
| Mix finished (manager-level) | `ProductManager.onMixCompleted` |

The save file already proves the shape of a discovered recipe — `Products.json` stores
`MixRecipes: [{ "Product": "...", "Mixer": "...", "Output": "..." }]`, a flat `DiscoveredProducts`
list, and a live `ActiveMixOperation`.

#### ⚠ RESOLVED (corrected): `MixRecipeData` fields are inconsistent — classify by membership

**An earlier revision of this document claimed `Product` holds the additive. That was wrong** — it
came from misreading a raw JSON dump rather than parsing it. Measured properly across all 81 rows of
a real save:

| | count |
|---|---|
| `Product` is a known product, `Mixer` is an additive | **66** |
| `Mixer` is a known product, `Product` is an additive | **15** |
| both, or neither | **0** |

So the field names are usually right — `Product` is the base — but **15 rows are stored reversed**.
Trusting the names would silently invert one recipe in five.

**The reliable rule:** for each row, whichever side appears in `DiscoveredProducts` is the base and
the other is the additive. Against a real save this classifies **81/81 rows with zero ambiguity**
(no row has both sides as products, or neither), and yields lineages like:

```
ogkush  +cuke   -> thickmonkey  +viagor -> deathfuel
ogkush  +banana -> thickdick    +donut  -> californiaghost  +donut -> californiacake
```

Two further facts the same analysis established:

* **Base products** (never produced by any recipe): `ogkush`, `sourdiesel`, `greencrack`,
  `granddaddypurple`, `meth`, `cocaine`, `shroom`. Note `granddaddypurple` *does* appear as a recipe
  output, so bases must be a known set, not merely inferred from "never an output".
* **Self-loops exist.** `thickdick + paracetamol -> thickdick` is a real row. Any lineage walk must
  guard against cycles or it will hang.
* **Orphans are normal.** `MixRecipes` only records recipes the player has *discovered*; 20 of 78
  outputs in the sampled save had no derivable lineage. The UI must show these as "origin unknown"
  rather than treating it as an error.

---

#### Superseded note (kept for the record)

Real type, confirmed against the shipped `Assembly-CSharp`:

```
=== ScheduleOne.Product.MixRecipeData ===
  field String Product      field String Mixer      field String Output
```

The API parameter names agree with the intuitive reading —
`GetRecipe(String product, String mixer)`, `SendMixRecipe(String product, String mixer, String output)`,
`FinishAndNameMix(String productID, String ingredientID, String mixName)`.

**But the persisted data does not.** Cross-referencing five rows of `Products.json` against that
save's own `DiscoveredProducts` list:

| Row | `Product` | `Mixer` | Which is the base? |
|---|---|---|---|
| 1 | `cuke` | `ogkush` | `ogkush` — it's in `DiscoveredProducts`; `cuke` is not |
| 2 | `viagor` | `thickmonkey` | `thickmonkey` |
| 3 | `paracetamol` | `ogkush` | `ogkush` |
| 4 | `banana` | `ogkush` | `ogkush` |
| 5 | `donut` | `ultracake` | `ultracake` |

**5/5: the field named `Product` holds the *additive*, and `Mixer` holds the *base product*.**

Consequence for the tracker: **do not** use `mixRecipes` for chain resolution until this is confirmed
against a live session — an inverted assumption would reverse every recipe in the cookbook.
`ReflectionGameFacts.ResolveIngredientChain` deliberately returns `null` for exactly this reason.

The primary tracking path is **unaffected and unambiguous**: `MixOperation.ProductID` /
`IngredientID` are read straight off the station, and the save confirms their meaning —
`CurrentMixOperation: {ProductID: "greencrack", IngredientID: "mouthwash", Quantity: 20}`, where
Green Crack is a strain and Mouthwash an additive.

#### ⚠ RESOLVED: `GetOutput` does not return the output product

```
=== ScheduleOne.ObjectScripts.MixOperation ===
  field  String ProductID     field EQuality ProductQuality
  field  String IngredientID  field Int32    Quantity
  method EDrugType GetOutput(List properties)
  method Boolean   IsOutputKnown(ProductDefinition& knownProduct)
```

`GetOutput` returns an **`EDrugType`**, not a product. The resulting product comes from
**`IsOutputKnown(out ProductDefinition knownProduct)`**, which conveniently answers both questions at
once: whether the mix was already known, and what it produces.

For a **brand-new** combination `IsOutputKnown` returns `false` with a null product — because at
`MixingDone` the product genuinely does not exist yet; the player names it afterwards via
`FinishAndNameMix`. Record the batch flagged as a new discovery and reconcile the name later.

### 2.8 Effects ("Properties")

- Effects are called **Properties** in code: `ScheduleOne.Product.PropertyItemDefinition`,
  `PropertyContainer`, `PropertyUtility`.
- Mixing engine: `ScheduleOne.Effects.EffectMixCalculator.MixProperties/3`, with `MAX_PROPERTIES`
  and `MAX_DELTA_DIFFERENCE` constants.
- 34 concrete effect classes in `ScheduleOne.Effects`:
  `AntiGravity, Athletic, Balding, BrightEyed, Calming, CalorieDense, Cyclopean, Disorienting,
  Electrifying, Energizing, Euphoric, Explosive, Focused, Foggy, Gingeritis, LongFaced, Glowie,
  Jennerising, Laxative, Lethal, Munchies, Paranoia, Refreshing, Schizophrenic, Sedating, Seizure,
  Shrinking, Slippery, Smelly, Sneaky, Spicy, ThoughtProvoking, Toxic, TropicThunder, Zombifying`
  (plus base `Effect`, `EffectController`, `EffectHandler`).

**Enums (verified):**

- `EDrugType` = `Marijuana, Methamphetamine, Cocaine, MDMA, Shrooms, Heroin`
  — note **MDMA and Heroin exist in the enum**; design the stats schema to tolerate them appearing.
- `EQuality` = `Trash, Poor, Standard, Premium, Heavenly`
- `EProperty` = `Mild, Potent, Overwhelming, Sedating, Calming, Refreshing, Stimulating, Cerebral,
  Physical, Psychedelic, Dissociative, Hallucinogenic, Focused, Uplifting, Euphoric, Addictive,
  HighlyAddictive`

### 2.9 Product value

- `ProductManager.CalculateProductValue/2` — use the game's own maths; do not reimplement it.
- `ProductItemInstance.GetMonetaryValue/0`, `GetTotalAmount/0`, `GetAddictiveness/0`, `GetSimilarity/2`
- `ProductDefinition` — `BasePrice`, `MarketValue`, `Price`, `DrugType`/`DrugTypes`, `Recipes`,
  `BaseAddictiveness`, `ValidPackaging`, `LawIntensityChange`
- Ingredient cost: `ScheduleOne.Registry` plus item definitions; shop prices in `Shops.json`.

---

## 3. Critical finding: randomized mix maps

`Game.json` contains `"Settings": { "UseRandomizedMixMaps": bool }` — **per save**.

If a player enables it, the mixing map for that save is randomized. **Any hard-coded recipe table
copied from a wiki will be wrong for that save.** The planner must read `WeedMixMap` / `MethMixMap` /
`CokeMixMap` / `ShroomMixMap` from the live `ProductManager` for the *currently loaded save*, and
must key cached recipe data by `ProfileId`.

This single flag is the strongest argument for building Phase 2 (Game Data Reader) properly and never
shipping a static recipe database.

---

## 4. Multiplayer

- **Stack:** FishNet, host-authoritative. The codegen signatures are all over the logs
  (`RpcWriter___Server_*`, `RpcLogic___*`, `RpcReader___Observers_*`, `RpcReader___Target_*`).
- **Identity:** Steam-authenticated via `ScheduleOne.Networking.FishNetSteamAuthenticator` and
  `SteamLobbyService`. `Lobby` exposes `IsHost`, `LobbyID`, `PlayerCount`, `GetLobbyMemberIDs/0`.
- **Per player:** every `Player` has `PlayerCode` (SteamID64), `PlayerName`, `Connection`, `IsLocalPlayer`.
- **Per station:** `PlayerUserObject` (the `Player` currently using it) and `NPCUserObject`
  (the employee — `ChemistData`, `BotanistData`, `PackagerData` all appear in save files).

### Attribution rule for v1

At a completion hook, classify the batch:

| Condition | Classification |
|---|---|
| `station.PlayerUserObject == Player.Local` | `local` — counts toward "My Cooking Stats" |
| `station.NPCUserObject != null` | `employee` — counts toward the save, flagged as automated |
| `station.PlayerUserObject` is another `Player` | `remote` — record `PlayerCode`, exclude from personal totals in v1 |
| neither set | `unattributed` — record, exclude from personal totals |

Store the classification on every event so the dashboard can re-slice later without re-collecting.
This satisfies "prioritise the local player" while keeping the door open for a full multiplayer view,
with **zero** networking code of our own.

**Client-side limitation to design around:** `[ServerRpc]` bodies (`TryCreateOutputItems`,
`CreateWeed_Server`, …) execute **only on the host**. A client-side tracker must never depend on
those firing locally — hook the Observers-dispatched methods (`MixingDone`, `FinishCookOperation`)
instead, which is what §2 recommends.

---

## 5. Preventing Incorrect Statistics

| Risk | Mitigation |
|---|---|
| Counting a batch twice | Idempotency key `stationGUID + operationSignature + completionGameTime`. Station GUIDs are real and stable — persisted as `"GUID"` in each station's save `BaseData`. |
| Duplicate game events | Hook the **transition** (`MixingDone`), never poll state (`IsMixingDone` would re-fire every frame). Keep a per-station `lastCompletedOperationId` guard, since `MixingDone_Networked` can arrive on host *and* as a local call. |
| Cancelled production | Naturally excluded — cancellation clears `CurrentMixOperation` without ever reaching `MixingDone`. |
| Failed production | Same path; also watch `CreateTrash/1` on Chemistry / Cauldron as a negative signal. |
| Reloading a save and recounting | Suppress the tracker until `LoadManager.onLoadComplete` **plus** a settle delay. Saved stations resume mid-operation with `CurrentMixTime` already set, so start-of-load replays are a real hazard. Persist `lastProcessedEventId` per profile. |
| Counting inventory transfers | **Never count inventory.** Only completion events. Inventory deltas cannot distinguish produced / bought / transferred / spawned. |
| Debug or admin spawns | `Game.json` -> `Settings.ConsoleEnabled`. Stamp it on every event; annotate a profile that ever had the console enabled rather than silently trusting it. |
| Counting transforms as production | Drying / bricking / packaging get their own event kinds (§2.5). |
| Another player's production | Attribution rule (§4). |
| Mod crash mid-production | Append-only `events.jsonl`, flushed on write; aggregates are **derived** and fully rebuildable by replaying the log. Never keep totals only in memory. |
| Game update breaks a hook | Startup symbol verification: reflectively assert every hooked type and method exists; on mismatch log loudly and **disable the tracker** rather than record garbage or crash. |

Guiding principle, per the brief: **accurate statistics over counting inventory items.**

### Handled: reloading to before a batch

Events are written to `events.jsonl` the moment a batch completes, which is what makes the history
survive a crash. A player who cooks, **does not save**, and reloads gets the product rolled back
in-game while our record still counts it.

An earlier revision of this document argued the idempotency key absorbed most of this, because a
replayed batch produces the same `stationGuid|recipe|dayN-time` key and is rejected. **That
reading was wrong, and a live session on 0.4.6f13 showed why.**

Observed: `SaveGame_1Time.json` held day 40, 13:55. The player then cooked four batches up to
day 40 15:14 and quit without saving. On reload the stations replayed, and the log shows:

```
[16:34:00] Production Detected
[16:34:00] Production ignored (DuplicateEvent): station b83253bc…, shroom+chili x20
[16:34:11] Production ignored (DuplicateEvent): station eeb1f5a2…, hairypuke+paracetamol x20
[16:34:11] Production ignored (DuplicateEvent): station eeb1f5a2…, hairypuke+paracetamol x20
```

The key does not *absorb* the rollback — it **inverts** it. A replayed operation is deterministic,
so it completes at the same game-minute and collides with the abandoned record. The phantom batch
is the one that survives; the batch that genuinely happened in the surviving timeline is thrown
away as a duplicate. The counts happen to look plausible, which is what made this easy to miss.

The two obvious designs trade against each other:

| Approach | Survives a crash | Matches the game's truth |
|---|---|---|
| Write immediately | yes | no — over-counts on rollback |
| Write only on `SaveManager.onSaveComplete` | no — loses everything since the last save | yes |

Neither is right on its own, so writes stay immediate but are now **revocable**.

**Implemented.** `Time.json` in the save folder carries the clock the save was written at —
`{ "TimeOfDay": 1355, "ElapsedDays": 40 }`, produced by `TimeManager.GetSaveString()`. On load,
`RollbackReconciler` compares every recorded event against it and removes anything later. The
replay is then free to re-record those batches with real values, because their keys are no longer
claimed.

Comparison is by ordinal, not by the packed time. `TimeOfDay` is packed decimal (1507 = 15:07),
and `TimeManager` increments `ElapsedDays` as the clock rolls 23:59 → 00:00, so
`days * 1440 + (t / 100) * 60 + t % 100` is the monotonic form — mirroring the game's own
`GetTotalMinSum()`. See `GameClock`.

Three deliberate choices:

* An event landing **exactly** on the save minute is kept. It is genuinely ambiguous, and keeping a
  real batch is the cheaper error — a duplicate is visible in the history, a deleted cook is not.
* An unreadable, missing, or out-of-range `Time.json` means **reconcile nothing**. A brand-new
  save has not written one, and a multiplayer client has no local save folder at all; acting on a
  misread clock would delete real production.
* Removed events are appended to `rolled-back.jsonl` **before** the live log is rewritten without
  them. This is the only operation in the mod that deletes its own history, so it stays auditable,
  and an interruption between the two writes costs a duplicate record rather than the events.

Residual gap: a player who saves, then cooks, then reloads *that same save* loses nothing — correct.
A player who uses the console to move time backwards will have real production discarded. Saves with
`ConsoleEnabled` are already flagged on every event, and the archive makes it recoverable.

### Verified: the three remaining production stations

Traced through the decompiled 0.4.6f13 `Assembly-CSharp`, not inferred from method names.

**LabOven (§2.2)** has *no* completion method. Both paths add the product themselves and then clear
the operation:

| Path | Site |
|---|---|
| Player | `SmashLabOvenTask.Shatter()` → `Oven.OutputSlot.AddItem(...)` → `Oven.SendCookOperation(null)` |
| Employee | `FinishLabOvenBehaviour` coroutine → `targetOven.OutputSlot.AddItem(...)` → `targetOven.SendCookOperation(null)` |

`SendCookOperation(null)` is called from exactly those two sites in the whole assembly — start
passes a new `OvenCookOperation`, and no cancel path calls it at all — which makes null the
unambiguous completion signal. It must be patched as a **prefix**: the call is what clears
`CurrentOperation`. Units are `Cookable.ProductQuantity * IngredientQuantity`.

An earlier revision of the hook table named `CreateStationItems` as the production hook. It is
not — it instantiates the cosmetic tray and liquid props and creates no inventory. Corrected.

**ChemistryStation (§2.3)** and **Cauldron (§2.4)** both create their output inside the FishNet
generated `RpcLogic___` body, guarded by `InstanceFinder.IsServer`:

* `RpcLogic___FinalizeOperation_2166136261()` — `CurrentCookOperation.Recipe.GetProductInstance(quality)`
  into `OutputSlot`, then nulls `CurrentCookOperation`. Read it in a prefix.
* `RpcLogic___FinishCookOperation_2166136261()` — fixed output: `CocaineBaseDefinition` x10 at
  `InputQuality`.

The public `FinalizeOperation`/`FinishCookOperation` are the Observers RPC *writers*; patching
those would miss clients. The `RpcLogic___` body runs exactly once per machine per completion,
which is the fire-once property the tracker needs.

### Two bugs the first live run exposed

Both were invisible to the unit tests because the fakes were more forgiving than the real game.
Both are fixed, and the fakes now mirror reality exactly so the old code cannot compile.

**1. `TimeManager.TimeOfDay` does not exist.** The runtime property is **`CurrentTime`**;
`TimeOfDay` is only the serialized key inside the save file's `TimeData`. Reflection returned null,
the code fell back to `0`, and the idempotency key silently collapsed to
`stationGuid|recipe|dayN` — so a second identical batch on the same station on the same in-game day
was discarded as a duplicate. Observed live: a genuine 20-unit batch lost.

*Lesson: a name in the save file is not a name on the runtime class. Verify against the metadata.*

**2. `PlayerUserObject` is not "who made this".** It tracks who is at the station's UI *right now*
and is cleared on `OnEndUse`. Since the player always walks away while a mix runs, it is null at
completion — so every batch recorded as `Unattributed` with
`CountsTowardPersonalTotals: false`, and lifetime totals stayed at zero.

Attribution now resolves in order: whoever is at the station → an `NPCUserObject` employee (which
*does* survive to completion) → whoever was recorded pressing Start (`MixingStart` is patched for
exactly this) → in single-player only, the local player. In multiplayer an unobserved batch stays
unattributed rather than crediting the wrong person.

---

## 6. Sales / Economy (secondary, Phase 15+)

Feasible, and the data is real:

- `ProductManager.onContractReceiptRecorded`, `RecordContractReceipt/2`, `GetContractReceipts/3`,
  `ContractReceipts`, `CONTRACT_RECEIPT_MAX_COUNT` — the genuine "sold / revenue" source.
- `ScheduleOne.Economy.Customer` (`ProcessHandoverClient`, `RecommendSupplier`), `Dealer`, `Supplier`.
- `Money.json` -> `LifetimeEarnings`, `Networth`, `OnlineBalance`, `WeeklyDepositSum`.
- Deliveries: `ScheduleOne.Delivery.DeliveryManager` (`ReceiveDelivery`, `SetDeliveryState`).

Do not build this until production tracking is proven.

---

## 7. Modding stack — decision required

The installed build is **IL2CPP**. That has real consequences:

- Requires **MelonLoader + Il2CppInterop**; proxy assemblies must be generated (MelonLoader's
  Il2CppAssemblyGenerator on first run, or Cpp2IL).
- HarmonyX patching works, but against generated proxy methods.
- Game events are `Il2CppSystem.Action`; subscribing needs interop delegate wrapping, not a plain `+=`.
- Some private fields need explicit Il2CppInterop field accessors.
- String and collection marshalling has real cost — never do interop work per frame.

### The Mono branch exists — CONFIRMED

Read directly out of Steam's own app metadata (`Steam/appcache/appinfo.vdf`, Schedule I section):

> **`alternate`** — *"Uses Mono instead of IL2CPP as the scripting backend. Less performant than the
> default version, but less prone to crashes."*

Branches published for app `3164500`:

| Branch | Purpose |
|---|---|
| *(default)* | IL2CPP — what is currently installed |
| **`alternate`** | **Mono scripting backend** — the modding target |
| `beta` | "Hosts the beta version of Schedule I. May be unstable." |
| *(alternate beta)* | "Hosts the beta version of Schedule I in alternate mode. May be unstable." |
| *(previous)* | "The last major default version." / "The last major alternate version." |

**Decision: build against `alternate` (Mono).** Every class, method and event named in this document
is identical on both branches, so this audit holds either way — only the plumbing changes.

To switch: Steam → Schedule I → Properties → Betas → select `alternate`. The current install is on
the default branch (`appmanifest_3164500.acf` has no `betakey`).

### Bonus confirmation: the save tree really is cloud-synced

The same metadata block declares Schedule I's Steam Cloud mapping:

```
WinAppDataLocalLow  ->  TVGS/Schedule I/Saves/{64BitSteamID}  ->  *.json
```

This **confirms** §1.4 rather than merely assuming it: every `*.json` under the save tree is
Steam-Cloud-synced. Writing our data there would put it in the sync set and risk conflicts. Our
storage stays in `%APPDATA%\Schedule1RecipePlanner\`.

---

## 8. Reproducing this audit

```bash
node tools/il2cpp-dump/dump.js '^ScheduleOne\.ObjectScripts\.MixingStation$'
node tools/il2cpp-dump/find.js '^onMix|^onProduct'
```

See [`tools/il2cpp-dump/README.md`](../tools/il2cpp-dump/README.md).
