# il2cpp-dump

Two small Node scripts that read Schedule I's `global-metadata.dat` directly and print the game's
real class / method / field / event / property names. No decompiler, no Il2CppDumper, no game launch.

They exist so every claim in the [Phase 0 audit](../../docs/00-PHASE-0-AUDIT.md) is reproducible, and
so hook signatures can be re-verified in seconds after a game update.

## Requirements

Node (any recent version) and an installed copy of the game. The metadata path is hard-coded at the
top of each script:

```
C:/Program Files (x86)/Steam/steamapps/common/Schedule I/Schedule I_Data/il2cpp_data/Metadata/global-metadata.dat
```

## dump.js — dump whole types

```bash
node dump.js '<type-name-regex>' ['<namespace-regex>']
```

The regex matches the **fully-qualified** name.

```bash
node dump.js '^ScheduleOne\.ObjectScripts\.MixingStation$'
node dump.js '^ScheduleOne\.Product\.ProductManager$'
node dump.js '^ScheduleOne\.ObjectScripts\.(LabOven|ChemistryStation|Cauldron|DryingRack)$'
```

Output is `EVENTS` / `FIELDS` / `PROPS` / `METHODS`, with methods printed as `Name/paramCount`.

FishNet's code generator adds a lot of noise (`RpcWriter___*`, `RpcLogic___*`, `sync___*`,
`NetworkInitialize*`). Filter it:

```bash
node dump.js '^ScheduleOne\.ObjectScripts\.MixingStation$' | ../../tools/il2cpp-dump/clean.sh
```

## find.js — reverse lookup

Find which type owns a member, when you know the member name but not the class:

```bash
node find.js '^(onMixCompleted|onNewProductCreated|onMixRecipeAdded)$'
node find.js 'Discover'
```

This is how `ProductManager` was identified as the owner of every discovery event.

## How it works

`global-metadata.dat` is a flat binary with a header of `(offset, size)` pairs followed by packed
struct tables. For metadata **version 31** (Unity 2022.3) the relevant tables and their struct sizes:

| Header offset | Table | Struct size |
|---|---|---|
| 24 | strings | null-terminated |
| 32 | events | 24 |
| 40 | properties | 20 |
| 48 | methods | 36 |
| 96 | fields | 12 |
| 160 | typeDefinitions | 88 |

Each type definition holds `nameIndex`, `namespaceIndex`, and a `start` + `count` pair into each of
the member tables, so members can be attributed to their owning type exactly.

Struct sizes were confirmed by checking that each table size divides evenly:
`1668744 / 88 = 18963` types, `5310468 / 36 = 147513` methods.

**If a future game update ships a different metadata version, these sizes change.** The scripts
print the version; verify the divisions still come out whole before trusting the output.

## Limits

Names only — no method bodies, no IL, no field offsets. Enough to confirm that a hook target exists
and its parameter count, which is what the audit needed. For actual decompiled logic, use Cpp2IL or
Il2CppDumper against `GameAssembly.dll`.
