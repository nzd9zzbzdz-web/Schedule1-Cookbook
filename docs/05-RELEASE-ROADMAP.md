# Release Roadmap — Nexus Mods

Getting from "works on my machine, on one Steam branch" to a first public release.

Same convention as [02-ROADMAP.md](02-ROADMAP.md): every step has an **exit test** — a concrete
thing that must be demonstrably true before the step is done.

Legend: ⬜ not started · 🟡 in progress · ✅ done

---

## R0 — Get the repo under version control ⬜

`master` currently has **zero commits**. Everything in the working tree is untracked, there is no
rollback, and there is no way to tag a release. Nothing else in this roadmap should start first,
because every step after this one wants a checkpoint to fall back to.

1. Confirm [.gitignore](../.gitignore) covers `bin/`, `obj/`, `*.user`, `.vs/` — it does.
2. Decide whether `dist/` belongs in the repo. Recommendation: **ignore it** and attach built
   artifacts to the release instead, so the tree never carries stale binaries.
3. Commit the tree as-is, before any of the refactoring below.

**Exit test:** `git log` shows a root commit; `git status` is clean; a fresh `git clone` plus
`dotnet test` passes.

---

## R1 — Survive the default (IL2CPP) branch 🔴 **BLOCKER** ⬜

### The problem

`RecipePlanner.PhoneApp.dll` hard-links `Assembly-CSharp` and `ScheduleOne.Core`, which exist
**only** on the Mono `alternate` branch. `RecipePlanner.dll` references PhoneApp, and
[RecipePlannerMod.cs](../src/RecipePlanner.Mod/RecipePlannerMod.cs) touches `CookbookDataBuilder`
and `CookbookAppInstaller` directly inside `OnInitializeMelon`.

On the default IL2CPP branch that assembly does not exist — MelonLoader generates
`Il2CppScheduleOne.*` proxies instead — so the type load fails and **the whole mod dies at init**,
including the tracking that is otherwise branch-agnostic and works fine.

It is worse than a missing feature: the `SymbolGuard` fail-closed design, the best thing in the
codebase, never gets to run. Most Schedule I players are on the default branch.

There is also a build-time version of the same bug: on an IL2CPP-only machine the PhoneApp csproj
strips all its sources (`Compile Remove="**/*.cs"`), so `RecipePlanner.Mod` no longer compiles at all.

### The fix — split the assembly

The dependency audit says this is a move, not a rewrite. Current PhoneApp sources by dependency:

| File | Needs the game? |
|---|---|
| `CookbookViewModel.cs` | **No** — Core only |
| `CookbookDataBuilder.cs` | **No** — Core + `RecipePlanner.Game.Binding` (reflection) |
| `RecipePlannerUI` (inside `CookbookApp.cs:145`) | **No** — a static bridge of delegates |
| `CookbookScreen.cs`, `AppIconFactory.cs`, `SmoothScroll.cs` | UnityEngine only |
| `CookbookApp.cs`, `CookbookAppInstaller.cs`, `IconSource.cs` | `ScheduleOne.*` — Mono-only |

1. Create **`RecipePlanner.Cookbook`** (netstandard2.0, no game references). Move
   `CookbookViewModel.cs` and `CookbookDataBuilder.cs` into it, and **extract** the
   `RecipePlannerUI` static class out of `CookbookApp.cs` into its own file there.
2. Leave the six Unity / `ScheduleOne` files in `RecipePlanner.PhoneApp`.
3. `RecipePlanner.Mod` references `RecipePlanner.Cookbook` directly and **drops its ProjectReference
   to `RecipePlanner.PhoneApp` entirely**. The mod assembly must not name PhoneApp in its metadata.
4. Load the UI by reflection at runtime — `Assembly.LoadFrom` the PhoneApp DLL sitting next to the
   mod, find `CookbookAppInstaller.TryInstall`, invoke it. Wrap in try/catch and log a clear line on
   failure. The existing `Reflect` helper in `RecipePlanner.Game.Binding` already does this kind of work.
5. Detect the branch up front and log which mode is active, e.g.
   `Cookbook app unavailable on the IL2CPP branch — production tracking is running normally.`
6. Fix the misleading error string at
   [RecipePlannerMod.cs:60-62](../src/RecipePlanner.Mod/RecipePlannerMod.cs#L60-L62): it currently
   promises "The planner UI will still work", which is exactly backwards.
7. Make the PhoneApp csproj's empty-stub fallback harmless now that nothing links it at compile time.

**Exit test:** on the **default IL2CPP branch**, with only the shipped DLLs in `Mods\`: the mod
loads, `Symbol check PASSED`, `Production tracking ENABLED`, one cooked batch produces exactly one
`Production Detected` line, and the log states plainly that the cookbook app is Mono-only. No
unhandled exception anywhere in `MelonLoader/Latest.log`. Confirm `RecipePlanner.dll`'s assembly
references no longer include `RecipePlanner.PhoneApp`.

---

## R2 — Decide the scope the release is sold as ⬜

The README opens promising "recipe planning and optimisation". Phases 2, 3, 4, 6, 14, 16 — game data
reader, effect database, calculation engine, planner UI, comparison, optimisation — are all ⬜ not
started. What exists is a **cookbook plus automatic production history**, which is worth publishing
on its own merits.

Shipping it as *Recipe Planner* invites "where's the planner?" as the first comment. Rename the
Nexus page (and ideally the MelonInfo display name) to something honest about what it does —
*Cookbook & Production Tracker* or similar — and keep planning as a stated future goal.

**Exit test:** the Nexus title, the `MelonInfo` name, and the README's first paragraph all describe
the same feature set, and every feature named in them is one a player can actually use today.

---

## R3 — Truth-up the documentation ⬜

Reviewers read the README, and yours currently **undersells** the project and contradicts the code:

| Claim | Reality |
|---|---|
| `dotnet test` → **119 passing** | **208 passing** |
| "every money figure currently reads 0" | [`GamePriceSource`](../src/RecipePlanner.Game/Binding/GamePriceSource.cs) is fully implemented |
| Roadmap Phase 13 "controls outstanding" | [CookbookScreen.cs:1137-1146](../src/RecipePlanner.PhoneApp/CookbookScreen.cs#L1137-L1146) — 7 sort buttons, filter cycling, hide and favourites all present |
| Phase 5 🟡 "needs a real `IPriceSource`" | `GamePriceSource` is that source; it needs live confirmation, not writing |

Also split the audience. The current README and [04-SETUP.md](04-SETUP.md) are developer documents —
04-SETUP opens with "Already done on this machine", which is a personal work log. Keep them, and add
a separate user-facing install guide (R4).

**Exit test:** every number and status marker in [README.md](../README.md) and
[02-ROADMAP.md](02-ROADMAP.md) matches a command you can run; no ⬜ phase is described in the present
tense anywhere.

---

## R4 — Write the user-facing install guide ⬜

There is currently **no document a Nexus downloader could follow**. It needs to cover:

- MelonLoader version required (0.7.3), installed into the Schedule I folder, and **run the game
  once** before installing the mod — IL2CPP proxy generation takes ~26s the first time. Say so, or
  people will think it hung.
- Drop the DLLs into `Schedule I\Mods\`. List them by name.
- Which branch gets which features, stated plainly and without apology, per R1.
- Where data lives: `%APPDATA%\Schedule1RecipePlanner\`. Note that the mod **never writes to game
  saves** — that is a genuine selling point, put it high.
- How to uninstall cleanly, and how to remove the mod's data.
- Where to find `MelonLoader/Latest.log` when reporting a bug.

**Exit test:** someone who has never modded Schedule I follows the guide on a clean install and gets
a working mod without asking a question. If you cannot recruit a tester, do it yourself from a fresh
game folder and a wiped `%APPDATA%` directory.

---

## R5 — Verify pricing live ⬜

[`GamePriceSource`](../src/RecipePlanner.Game/Binding/GamePriceSource.cs) is written and reads
`ProductManager.ProductPrices` and `Registry.ItemDictionary` reflectively, but Phase 5 is still
marked as needing live confirmation — and its failure mode is silent: `EnsureLoaded` catches, logs a
warning, and every money figure quietly stays 0.

Two of the seven cookbook sort orders are useless if this is broken, and a stats screen full of `$0`
is the kind of thing that gets reported as "mod is broken".

1. Confirm live that `Prices loaded: N products, M ingredients` reports non-zero counts on both branches.
2. Spot-check several products against what the game quotes the player.
3. If prices cannot load, make the UI say so rather than rendering a confident `$0`.

**Exit test:** cook a batch, and the recorded value matches the game's own quoted price within
rounding. The `Prices loaded` line shows non-zero counts.

---

## R6 — Answer the multiplayer question before it is asked ⬜

The mod already distinguishes employee-cooked batches from personal ones — the README shows
`Attributed: Employee` and a total that correctly excludes it. What is not written down anywhere is
what happens **as a multiplayer client versus as host**.

This will be the first or second question in the comments. Test both, and state the answer on the
Nexus page.

**Exit test:** a sentence on the mod page describing client and host behaviour, backed by an actual
session in each role.

---

## R7 — Package the release ⬜

- **LICENSE file.** None exists. Pick one deliberately and fill in the Nexus permissions fields to
  match — they are separate things and Nexus asks for both.
- **Version.** `MelonInfo` says `0.1.0` while the README implies feature-complete. Pick the real
  number and keep the assembly, the Nexus page and the git tag identical.
- **`Newtonsoft.Json.dll`.** Not shipped in `dist/`; you rely on MelonLoader or the game providing
  it. Verify that holds on a clean install of exactly the MelonLoader version you list as required.
- **Build from Release.** `dist/` currently matches the Release netstandard2.1 output — good. Keep
  it that way; a bare `dotnet build` stages Debug.
- **No PDBs in the archive.**
- **Archive layout** should mirror the game folder (`Mods/…`) so mod managers extract it correctly.

**Exit test:** extract the archive over a clean game install with MelonLoader, launch, and it works
with no extra steps.

---

## R8 — Make the page worth clicking ⬜

- **Screenshots.** The phone app is the entire selling point and there is currently not one image of
  it. At minimum: the cookbook list, a strain section expanded, and the stats view.
- Lead the description with the strongest claim you have, which is unusual for this genre:
  **tracking is automatic and never writes to your save**. Most competing tools ask the player to
  log cooks by hand.
- Mention the guardrails — `SymbolGuard` verifies 13/13 hooks and disables tracking rather than
  recording wrong numbers after a game update. That is a real differentiator on a game that patches
  often.
- Set expectations for what is *not* built yet, per R2. Under-promising here costs you nothing and
  prevents most one-star comments.

**Exit test:** the page has images, an accurate feature list, requirements, install steps, and a
known-limitations section.

---

## Suggested order

R0 first, always. Then R1, because everything else is wasted effort if the mod does not load for
most of its audience. R5 and R6 both need live game sessions, so batch them together. R2, R3 and R4
are writing and can happen while the game is closed. R7 and R8 are the last mile.

```
R0 ──► R1 ──► R5 ──┬──► R7 ──► R8 ──► publish
              R6 ──┘
   R2 ──► R3 ──► R4 ───────┘
```

## Explicitly out of scope for v1

Do not let these hold up the release — they are the next release, not this one:

- **IL2CPP support for the cookbook UI.** `CookbookScreen`, `AppIconFactory` and `SmoothScroll` need
  only UnityEngine and would likely port; subclassing the generic `App<T>` base in `CookbookApp`
  through Il2CppInterop is the genuinely miserable part. R1 makes shipping without it acceptable.
- **Roadmap phases 2, 3, 4, 6, 14, 15, 16, 17** — the planning and optimisation half.
- **A `MelonPreferences` config.** Players will ask for toggles; that is a fine v1.1 response.
