# Setup

## Already done on this machine

- **MelonLoader v0.7.3 installed** into `Schedule I\` (adds `MelonLoader\` and `version.dll`; nothing
  was overwritten). To uninstall, delete those two plus `Mods\`.
- **Mod built and installed** to `Schedule I\Mods\`.
- **Launched and confirmed live on the IL2CPP branch:**

  ```
  [Recipe_Planner] Recipe Planner starting — verifying game symbols before patching.
  [Recipe_Planner] Symbol check PASSED (13/13 hooks resolved)
  [Recipe_Planner] Patched ScheduleOne.ObjectScripts.MixingStation.MixingDone()
  [Recipe_Planner] Patched ScheduleOne.ObjectScripts.MixingStationMk2.MixingDone()
  [Recipe_Planner] Production tracking ENABLED.
  ```

  Il2Cpp proxy generation took ~26 s, not the several minutes usually quoted.

The only thing left is the Phase 9 acceptance test: load a save and cook one batch.

## Build layout

| Project | Target | Needs the game? |
|---|---|---|
| `RecipePlanner.Core` | netstandard2.0 | No — all tracking decisions live here |
| `RecipePlanner.Game` | netstandard2.0 | No — reflection bindings + `SymbolGuard` |
| `RecipePlanner.Mod` | netstandard2.1 | Only MelonLoader + Harmony |
| `RecipePlanner.PhoneApp` | netstandard2.1 | **Yes — links Assembly-CSharp; Mono branch only** |
| `RecipePlanner.Core.Tests` | net10.0 | No — 90 tests |
| `tools/HookVerifier` | net10.0 | Reads game assemblies offline |

```bash
dotnet build      # also stages the payload into dist\
dotnet test       # 90 passing
```

Core, Game and Mod reach the game only by reflection, and `SymbolGuard` resolves both
`ScheduleOne.*` and the `Il2CppScheduleOne.*` proxy names Il2CppInterop generates — so those three
stay branch-agnostic.

`RecipePlanner.PhoneApp` is the exception, and the only Mono-only piece. Building UI means creating
components *inside* the game, and subclassing the generic `App<T>` base through Il2CppInterop is the
case it handles worst. If IL2CPP support is ever wanted again, only that project needs reworking.

`Newtonsoft.Json` is **not** shipped — MelonLoader and the game both provide it.

## Step 1 — branch choice (optional)

The mod is branch-agnostic, so **the default IL2CPP branch works**. The Mono branch is still the
nicer development target — from Steam's own app metadata for app `3164500`:

> **`alternate`** — *"Uses Mono instead of IL2CPP as the scripting backend. Less performant than the
> default version, but less prone to crashes."*

Steam → right-click **Schedule I** → **Properties** → **Betas** → select **`alternate`**.

On Mono the install gains `Schedule I_Data/Managed/Assembly-CSharp.dll` and loses `GameAssembly.dll`;
MelonLoader then hosts mods on .NET Framework instead of .NET 6, and patching is direct rather than
going through generated proxies.

> Saves are shared between branches and both write to
> `LocalLow\TVGS\Schedule I\Saves\<SteamID64>\`. **Back that folder up before switching**, as you
> would before any game version change.

## Step 2 — rebuild after changes

```bash
dotnet build -c Release
```

The build prints the resolved MelonLoader path and stages all four assemblies into `dist\` —
`RecipePlanner.dll`, `RecipePlanner.Core.dll`, `RecipePlanner.Game.dll` and
`RecipePlanner.PhoneApp.dll`. Copy `dist\*.dll`
into `Schedule I\Mods\`. If Steam installed the game elsewhere:

```bash
dotnet build -c Release -p:GameDir="D:\Steam\steamapps\common\Schedule I"
```

## Step 3 — first launch

Launch Schedule I from Steam.

**The first launch on the IL2CPP (default) branch is slower.** MelonLoader downloads Cpp2IL and
generates Il2Cpp proxy assemblies from `GameAssembly.dll` before any mod loads. Measured on this
machine: **~26 seconds** end to end, not the several minutes often quoted. It happens once; later
launches are normal. Watch the MelonLoader console for `Il2CppAssemblyGenerator` progress.

Once it finishes, `Schedule I\MelonLoader\Il2CppAssemblies\` will exist, and you can re-run the
offline hook check against the *actual* proxies the mod will see:

```bash
dotnet run --project tools/HookVerifier -- "C:\Program Files (x86)\Steam\steamapps\common\Schedule I\MelonLoader\Il2CppAssemblies"
```

## Step 4 — verify the symbol check

Read the MelonLoader console. Expected on success:

```
[Recipe_Planner] Recipe Planner starting — verifying game symbols before patching.
[Recipe_Planner] Symbol check PASSED (13/13 hooks resolved)
[Recipe_Planner] Patched ScheduleOne.ObjectScripts.MixingStation.MixingDone()
[Recipe_Planner] Patched ScheduleOne.ObjectScripts.MixingStationMk2.MixingDone()
[Recipe_Planner] Production tracking ENABLED.
```

If the game has updated and a hook moved, you get this instead — and **no statistics are recorded**,
which is the intended behaviour:

```
[Recipe_Planner] Symbol check FAILED — tracking disabled to avoid recording incorrect statistics.
[Recipe_Planner]   [BLOCKING] ScheduleOne.ObjectScripts.MixingStation: missing MixingDone()
[Recipe_Planner]   Hook table was verified against game version 0.4.5f2. If the game updated,
                   the hook table needs re-auditing: node tools/il2cpp-dump/dump.js '<type-regex>'
```

To fix a break, re-run the audit tooling against the new build and update
[`HookTable.cs`](../src/RecipePlanner.Game/Binding/HookTable.cs):

```bash
node tools/il2cpp-dump/find.js '^MixingDone$'
node tools/il2cpp-dump/dump.js '^ScheduleOne\.ObjectScripts\.MixingStation$'
```

> The dump tooling reads `global-metadata.dat`, which only exists on the **IL2CPP** branch. Keep a
> copy of the file, or temporarily switch back, when re-auditing after an update.

## Step 5 — Phase 9 acceptance

Cook one batch. Expected, exactly once:

```
[Recipe_Planner] Production Detected
  Profile   : Echo (SaveGame_1, 9f2c4ab1…)
  Station   : MixingStationMk2 3059421d… (mixingstationmk2)
  Product   : greencrack + mouthwash -> Blue Lightning
  Quantity  : 20 units (Premium)
  Effects   : Energizing, Euphoric
  Recipe    : greencrack>mouthwash
  Attributed: local
  EventKey  : 3059421d…|greencrack+mouthwash|d40-924
```

Then run the negative tests from the [roadmap](02-ROADMAP.md#phase-9--the-first-real-proof). Each
must produce a `Production ignored (…)` line or nothing at all — never a second `Production Detected`.

## Where your data lives

```
%APPDATA%\Schedule1RecipePlanner\profiles\<ProfileId>\
```

Never inside the game's save folder — that tree is Steam-Cloud-synced and the game prunes files it
does not recognise. Deleting `stats.json` is always safe; it rebuilds from `events.jsonl`.
