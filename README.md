# Schedule I — Recipe Planner & Production Manager

A mod that turns Schedule I's mixing system into a personal cookbook and production record.

It answers three questions:

- **What can I make?** — recipe planning and optimisation
- **How do I make it?** — ingredients, order, effects, costs
- **What have I made?** — automatic production history, lifetime statistics, discovered recipes

Production is tracked **automatically**. The player never tells the mod they cooked something —
the mod hooks the game's own completion events.

## Status

**Working in-game.** Automatic production tracking is confirmed end-to-end on a live session.

- `dotnet test` -> **119 passing**
- Hook table verified **13/13** against both the shipped binary and the live IL2CPP proxies
- Phases 0, 1, 7, 8, 9, 10, 11, 12, 18 confirmed live; see the [roadmap](docs/02-ROADMAP.md)

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

**Next:** the statistics dashboard (Phase 13) and a real price source (Phase 5) — every money figure
currently reads 0.

| Project | Target | Needs the game? |
|---|---|---|
| `RecipePlanner.Core` | netstandard2.0 | No — identity, tracker, statistics, storage, recipes |
| `RecipePlanner.Game` | netstandard2.0 | No — reflection bindings + `SymbolGuard` |
| `RecipePlanner.Mod` | netstandard2.0 | Only MelonLoader + Harmony |

One assembly serves **both** Steam branches: there is no reference to `Assembly-CSharp` anywhere,
and `SymbolGuard` resolves `ScheduleOne.*` and the `Il2CppScheduleOne.*` proxy names alike.

Headline audit findings:

- **Production detection is solved.** `MixingStation.MixingDone()` is reached on every client, after
  completion is confirmed, with `CurrentMixOperation` still populated — product, ingredient,
  quantity and quality all readable in one place.
- **Identity is solved.** Saves are already keyed by SteamID64 on disk, and `Player.PlayerCode`
  exposes it at runtime. `OrganisationName` is the character name; `CreationDate` + `Seed` make a
  stable profile key that survives slot reuse.
- **Recipe discovery is a first-class game event.** `ProductManager.onMixRecipeAdded`,
  `onNewProductCreated` and `onProductDiscovered` already exist — the cookbook can build itself.
- **Randomized mix maps exist** (`Game.json` → `UseRandomizedMixMaps`), which rules out shipping a
  static recipe table copied from a wiki.
- **A Mono branch exists and is the build target.** Steam's own metadata describes the `alternate`
  branch as *"Uses Mono instead of IL2CPP as the scripting backend."* The default branch is IL2CPP.
- **`MixingStationMk2` overrides `MixingDone`**, so both it and the base class must be patched —
  missing this would silently detect nothing on Mk2 stations.

## Documentation

| Doc | What's in it |
|---|---|
| [00-PHASE-0-AUDIT.md](docs/00-PHASE-0-AUDIT.md) | The audit: identity, production hooks, call flows, multiplayer, accuracy risks |
| [01-ARCHITECTURE.md](docs/01-ARCHITECTURE.md) | Layers, services, and the one flow that matters |
| [02-ROADMAP.md](docs/02-ROADMAP.md) | Phases 0–18, each with an exit test and current status |
| [03-DATA-MODEL.md](docs/03-DATA-MODEL.md) | Storage layout, event schema, durability rules |
| [04-SETUP.md](docs/04-SETUP.md) | Branch switch, MelonLoader, building, and Phase 9 acceptance |

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
