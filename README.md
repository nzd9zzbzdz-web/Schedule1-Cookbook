# Schedule I Cookbook

An automatic cookbook and production record for Schedule I's mixing system.

It answers two questions:

- **How do I make it?** — every recipe you have discovered, grouped by strain, with the full
  ingredient chain that leads to it
- **What have I made?** — production history and lifetime statistics, recorded automatically

Production is tracked **automatically**. You never tell the mod you cooked something — it hooks the
game's own completion events. It never writes to your save.

> **Not a planner yet.** Recipe *planning*, prediction and optimisation are on the roadmap but are
> not built. What works today is the cookbook and the production record. See
> [02-ROADMAP.md](docs/02-ROADMAP.md).

## Status

**Working in-game**, pre-release. Automatic production tracking is confirmed end-to-end on a live
session.

- `dotnet test` → **221 passing**
- Hook table verified **16/16** against both the shipped binary and the live IL2CPP proxies
- Roadmap phases 0, 1, 7, 8, 9, 10, 11, 12, 18 confirmed live

Real output from a running game:

```
Production Detected
  Product   : hairypuke + paracetamol -> Extreme Assblaster
  Quantity  : 20 units (Standard)
  Effects   : Paranoia, Sneaky, Calorie-Dense
  Attributed: Employee
Production ignored (DuplicateEvent): station eeb1f5a2…, hairypuke+paracetamol x20
```

After a full process restart: `20 units across 1 batches, 1 recipes` — 20 rather than 40, because
the employee-cooked batch is recorded but kept out of personal totals.

### Before release

Release readiness is tracked in [05-RELEASE-ROADMAP.md](docs/05-RELEASE-ROADMAP.md). Outstanding:

| Step | What is left |
|---|---|
| R1 | Live confirmation on **both** Steam branches — the code is done and statically verified |
| R5 | Live confirmation that prices load; the code is written but its failure mode is a silent `$0` |
| R6 | Multiplayer client-vs-host behaviour needs testing and documenting |
| R8 | Screenshots |

## Which Steam branch?

Both work, but they do not get the same features.

| | Default (IL2CPP) | `alternate` (Mono) |
|---|---|---|
| Production tracking, history, statistics | ✅ | ✅ |
| Automatic recipe discovery | ✅ | ✅ |
| **Readable `cookbook.md` export** | ✅ | ✅ |
| **Cookbook app on the in-game phone** | ❌ | ✅ |

The tracking half reaches the game purely by reflection and resolves `ScheduleOne.*` and the
`Il2CppScheduleOne.*` proxy names alike, so it is branch-agnostic. The phone UI is not: building UI
means creating components *inside* the game, and subclassing the generic `App<T>` base is the case
Il2CppInterop handles worst.

The mod detects the branch at startup and says which mode it is in. On IL2CPP it loads, tracks, and
tells you the app is unavailable — it does not fail.

## Install

See **[06-INSTALL.md](docs/06-INSTALL.md)**.

## Layout

| Project | Target | Needs the game? |
|---|---|---|
| `RecipePlanner.Core` | netstandard2.0 | No — identity, tracker, statistics, storage, recipes |
| `RecipePlanner.Game` | netstandard2.0 | No — reflection bindings + `SymbolGuard` |
| `RecipePlanner.UI` | netstandard2.0 | No — view model, data builder, the UI seam |
| `RecipePlanner.Mod` | netstandard2.0 | Only MelonLoader + Harmony |
| `RecipePlanner.PhoneApp` | netstandard2.1 | **Yes — links Assembly-CSharp; Mono branch only** |

Four of the five assemblies have no game reference at all. `RecipePlanner.PhoneApp` is the single
Mono-only piece, and nothing links it at compile time — `PhoneAppLoader` loads it by name at
runtime, which is what keeps the mod alive on the IL2CPP branch.

```bash
dotnet build -c Release   # also stages the payload into dist\
dotnet test               # 221 passing
```

`Newtonsoft.Json` is **not** shipped — MelonLoader provides 13.0.4 on every host.

## Headline audit findings

- **Production detection is solved.** `MixingStation.MixingDone()` is reached on every client, after
  completion is confirmed, with `CurrentMixOperation` still populated — product, ingredient,
  quantity and quality all readable in one place.
- **Identity is solved.** Saves are already keyed by SteamID64 on disk, and `Player.PlayerCode`
  exposes it at runtime. `OrganisationName` is the character name; `CreationDate` + `Seed` make a
  stable profile key that survives slot reuse.
- **Recipe discovery is a first-class game event.** `ProductManager.onMixRecipeAdded`,
  `onNewProductCreated` and `onProductDiscovered` already exist — the cookbook builds itself.
- **Randomized mix maps exist** (`Game.json` → `UseRandomizedMixMaps`), which rules out shipping a
  static recipe table copied from a wiki.
- **`MixingStationMk2` overrides `MixingDone`**, so both it and the base class must be patched —
  missing this would silently detect nothing on Mk2 stations.

## Documentation

| Doc | What's in it |
|---|---|
| [00-PHASE-0-AUDIT.md](docs/00-PHASE-0-AUDIT.md) | The audit: identity, production hooks, call flows, multiplayer, accuracy risks |
| [01-ARCHITECTURE.md](docs/01-ARCHITECTURE.md) | Layers, services, and the one flow that matters |
| [02-ROADMAP.md](docs/02-ROADMAP.md) | Feature phases 0–18, each with an exit test and current status |
| [03-DATA-MODEL.md](docs/03-DATA-MODEL.md) | Storage layout, event schema, durability rules |
| [04-SETUP.md](docs/04-SETUP.md) | Developer setup: branch switch, MelonLoader, building |
| [05-RELEASE-ROADMAP.md](docs/05-RELEASE-ROADMAP.md) | What is left before publishing to Nexus |
| [06-INSTALL.md](docs/06-INSTALL.md) | **Player-facing install guide** |
| [07-NEXUS-PAGE.md](docs/07-NEXUS-PAGE.md) | Draft copy for the Nexus mod page |

## Tooling

[`tools/il2cpp-dump/`](tools/il2cpp-dump/) reads the game's `global-metadata.dat` directly and prints
real class / method / event names. Every claim in the audit is reproducible with it, and hook
signatures can be re-verified in seconds after a game update:

```bash
node tools/il2cpp-dump/dump.js '^ScheduleOne\.ObjectScripts\.MixingStation$'
node tools/il2cpp-dump/find.js '^onMix|^onProduct'
```

[`tools/HookVerifier/`](tools/HookVerifier/) checks the shipped hook table against a real build
**offline**, using `MetadataLoadContext` so no game code is ever executed. Point it at Cpp2IL stub
output, `Schedule I_Data/Managed` (Mono branch), or `MelonLoader/Il2CppAssemblies`:

```bash
dotnet run --project tools/HookVerifier -- <assembly-dir>
dotnet run --project tools/HookVerifier -- <assembly-dir> --list '^ScheduleOne\.Product\.ProductManager$'
```

`--list` prints real signatures **including parameter names**, which is how the `MixRecipeData`
field-order question and the `GetOutput` return type were settled. Exit code is non-zero when the
hook table no longer matches, so it drops straight into CI.

## Principles

1. **Never write to the game's save data.** Our data lives in `%APPDATA%\Schedule1RecipePlanner\`.
2. **Never count inventory.** Only completion events — inventory cannot distinguish produced from
   bought, transferred, or spawned.
3. **Events are the source of truth.** Every statistic is recomputable from the event log.
4. **Fail closed.** If a hook cannot be verified against the running game, the tracker disables
   itself and says so. Wrong statistics are worse than no statistics.

## Licence

**All rights reserved.** See [LICENSE](LICENSE).

The source is public so you can see exactly what the mod does to your machine and your saves — not
so it can be reused. Please do not re-upload it, bundle it, or reuse the code. **Contributions are
not being accepted**, so please do not open pull requests.

If you want to do something with it, ask first — I say yes more often than no.

And if I ever go quiet and stop updating this, ask me and I'll almost certainly hand it over rather
than let it rot.
