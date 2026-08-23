# Release Roadmap — Nexus Mods

Getting from "works on my machine, on one Steam branch" to a first public release.

Same convention as [02-ROADMAP.md](02-ROADMAP.md): every step has an **exit test** — a concrete
thing that must be demonstrably true before the step is done.

Legend: ⬜ not started · 🟡 in progress · ✅ done

---

## R0 — Get the repo under version control ✅

`master` currently has **zero commits**. Everything in the working tree is untracked, there is no
rollback, and there is no way to tag a release. Nothing else in this roadmap should start first,
because every step after this one wants a checkpoint to fall back to.

1. Confirm [.gitignore](../.gitignore) covers `bin/`, `obj/`, `*.user`, `.vs/` — it does.
2. Decide whether `dist/` belongs in the repo. Recommendation: **ignore it** and attach built
   artifacts to the release instead, so the tree never carries stale binaries.
3. Commit the tree as-is, before any of the refactoring below.

**Exit test:** `git log` shows a root commit; `git status` is clean; a fresh `git clone` plus
`dotnet test` passes.

**Done.** Pushed to <https://github.com/nzd9zzbzdz-web/Schedule1-Cookbook> as `main`. `dist/` is
gitignored per the recommendation above.

One thing was scrubbed on the way out: the tests and [03-DATA-MODEL.md](03-DATA-MODEL.md) contained
two **real** SteamID64s — one almost certainly yours, one apparently a co-op partner's from
multiplayer testing. A SteamID64 resolves straight to a public Steam profile, and public git history
is permanent, so both were replaced with synthetic ids below the valid range
(`76561190000000001` / `...002`). The tests treat them as opaque strings and still pass.
**Keep it that way** — never commit a real one.

---

## R1 — Survive the default (IL2CPP) branch 🟡 code done, needs a live IL2CPP run

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

**Implemented.** The new assembly is called **`RecipePlanner.UI`**, not `RecipePlanner.Cookbook` as
first planned: `RecipePlanner.Core.Recipes` already exports a class named `Cookbook`, and a
namespace of the same name shadowed it at every call site (`Cookbook.Compose` stopped resolving).

The plan below was also very slightly wrong about the dependency split, in a way worth recording:
`CookbookDataBuilder` called `IconSource.Clear()`. `IconSource` is a Unity type in the *same
namespace*, so it needed no `using` and did not show up in a per-file import audit — only the
compiler found it. That call is now routed through `RecipePlannerUI.CacheInvalidated`, an `Action`
the phone app binds to `IconSource.Clear` when it installs. Behaviour is unchanged; the dependency
is gone. **Lesson: a `using`-based dependency audit misses same-namespace types.**

Where each source ended up:

| File | Needs the game? | Now lives in |
|---|---|---|
| `CookbookViewModel.cs` | No — Core only | `RecipePlanner.UI` |
| `CookbookDataBuilder.cs` | No, *after* the `IconSource` call was routed through the seam | `RecipePlanner.UI` |
| `RecipePlannerUI` (was inside `CookbookApp.cs`) | No — a static bridge of delegates | `RecipePlanner.UI` |
| `CookbookScreen.cs`, `AppIconFactory.cs`, `SmoothScroll.cs` | UnityEngine only | `RecipePlanner.PhoneApp` |
| `CookbookApp.cs`, `CookbookAppInstaller.cs`, `IconSource.cs` | `ScheduleOne.*` — Mono-only | `RecipePlanner.PhoneApp` |

What was done:

1. Created **`RecipePlanner.UI`** (netstandard2.0, no game references) holding the three game-free
   pieces above.
2. Left the six Unity / `ScheduleOne` files in `RecipePlanner.PhoneApp`.
3. `RecipePlanner.Mod` references `RecipePlanner.UI` and **no longer names `RecipePlanner.PhoneApp`
   at compile time**. The PhoneApp `ProjectReference` survives only as
   `ReferenceOutputAssembly="false"`, so it is still built and staged but never enters the mod's
   metadata. *If that attribute is ever flipped to `true`, the IL2CPP branch breaks again.*
4. Added [`PhoneAppLoader`](../src/RecipePlanner.Mod/PhoneAppLoader.cs): `Assembly.LoadFrom` the
   PhoneApp DLL beside the mod, find `CookbookAppInstaller.TryInstall`, invoke it, and degrade to a
   logged line on any failure.
5. Added `SymbolGuard.IsMonoBranch` (3 tests) to detect the branch up front and log which mode is
   active. On IL2CPP the loader is never even constructed.
6. Rewrote the startup error that promised "The planner UI will still work" when tracking was
   disabled — it had the two exactly backwards. Also rewrote the "no game assembly found" message,
   which told players the Mono branch was the only supported target.
7. `RecipePlanner.UI.dll` added to the `StageMod` payload — without it the mod cannot load at all.

**Exit test:** on the **default IL2CPP branch**, with only the shipped DLLs in `Mods\`: the mod
loads, `Symbol check PASSED`, `Production tracking ENABLED`, one cooked batch produces exactly one
`Production Detected` line, and the log states plainly that the cookbook app is Mono-only. No
unhandled exception anywhere in `MelonLoader/Latest.log`. Confirm `RecipePlanner.dll`'s assembly
references no longer include `RecipePlanner.PhoneApp`.

### What is verified so far

The **static** half of the exit test passes:

| Check | Result |
|---|---|
| `RecipePlanner.dll` references | `Core`, `Game`, `UI`, MelonLoader, 0Harmony — **no `PhoneApp`** |
| `RecipePlanner.UI.dll` references | `netstandard` only — no Unity, no `ScheduleOne`, no `Assembly-CSharp` |
| `RecipePlanner.PhoneApp.dll` | still carries every game reference, now quarantined behind the loader |
| `dotnet test` | 220 passing (208 + 3 branch-detection + 9 report tests) |
| `dist/` payload | `RecipePlanner.dll`, `Core`, `Game`, **`UI`**, `PhoneApp` |

Bonus: with the game references gone, `RecipePlanner.Mod` dropped from `netstandard2.1` back to
**`netstandard2.0`**, which is the wider of the two targets.

### Still outstanding

The **live** half. Everything above is static analysis — it proves the mod can no longer fail *the
way it used to*, not that it runs. Switch Steam to the default branch and confirm the log, the
`IL2CPP branch detected` line, and one clean `Production Detected`.

Do **not** skip re-testing the Mono branch. The app installs through a new path now, and a
regression there would be invisible to every check in the table above. `PhoneAppLoader` resolves its
own folder via `Assembly.Location` with a `CodeBase` fallback, because the two MelonLoader hosts
disagree about which one works — if the Cookbook app fails to appear on Mono, that is the first
place to look.

---

## R2 — Decide the scope the release is sold as ✅

The README opens promising "recipe planning and optimisation". Phases 2, 3, 4, 6, 14, 16 — game data
reader, effect database, calculation engine, planner UI, comparison, optimisation — are all ⬜ not
started. What exists is a **cookbook plus automatic production history**, which is worth publishing
on its own merits.

Shipping it as *Recipe Planner* invites "where's the planner?" as the first comment. Rename the
Nexus page (and ideally the MelonInfo display name) to something honest about what it does —
*Cookbook & Production Tracker* or similar — and keep planning as a stated future goal.

**Exit test:** the Nexus title, the `MelonInfo` name, and the README's first paragraph all describe
the same feature set, and every feature named in them is one a player can actually use today.

**Done.** The name is **"Schedule I Cookbook"** everywhere — `MelonInfo`, the README heading, the
Nexus draft, and the repo, which was already `Schedule1-Cookbook`. The startup log line matches.

Only the *display* name changed. The assemblies are still `RecipePlanner.*`, because renaming them
is pure churn that no player ever sees and every namespace touches. If you want them renamed too,
that is a mechanical follow-up, not a release blocker.

The README now carries an explicit "**Not a planner yet**" callout, and the Nexus draft has a
"Not included (yet)" section. Under-promising costs nothing and prevents most one-star comments.

---

## R3 — Truth-up the documentation ✅

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

**Done.** [README.md](../README.md) rewritten: 211 tests, the pricing claim corrected, the branch
table made explicit, `RecipePlanner.UI` added to the layout table, and a "Before release" table
pointing at what is still outstanding.

[02-ROADMAP.md](02-ROADMAP.md) Phase 5 and Phase 13 statuses corrected, and the stale "Immediate
next step" section replaced — it still said the app had no controls.

One extra correction found while checking: the "what the player asked for" table credited **search**
as a solution. There is no search box, deliberately — a uGUI `InputField` in the running game steals
keyboard focus from the player's movement keys
([CookbookScreen.cs:1124-1129](../src/RecipePlanner.PhoneApp/CookbookScreen.cs#L1124-L1129)). The
table now says so, and the Nexus draft lists it under "Not included".

---

## R4 — Write the user-facing install guide ✅

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

**Written:** [06-INSTALL.md](06-INSTALL.md), covering requirements, the launch-once-first step, the
file list, how to confirm it loaded, the branch table, data location, uninstall, and a
troubleshooting section keyed to the actual log messages the mod emits.

A condensed version ships inside the archive as `README.txt`, because plenty of people never open
the mod page after downloading.

**The exit test itself is not met** — nobody has yet followed it on a clean install. That is the
check to run alongside R7's live-install test.

---

## R9 — The default branch had nothing to show ✅

Added after the fact, because it is the thing that most affects whether this release lands well.

**The problem.** R1 made the mod *survive* the default branch. It did not make it *worth
installing* there. A default-branch player installed it, played, and saw nothing — the phone app is
Mono-only, and everything the mod recorded went into `events.jsonl` and `stats.json`, which nobody
wants to read. That is a background service, not a mod, and it would have been reviewed as one.

**The fix.** [`CookbookReport`](../src/RecipePlanner.Core/Reporting/CookbookReport.cs) renders the
whole thing as readable Markdown — recipes grouped by strain with their ingredient chains, totals,
per-product and per-ingredient breakdowns, records, and the employee-excluded figures. It is written
to `cookbook.md` in the profile folder whenever a save unloads.

Pure Core: no I/O, no game types, no clock of its own, so it is fully tested (9 tests) without
launching anything. It is also genuinely useful on the Mono branch — a cookbook you can keep, search
properly, or paste to someone.

**Money is omitted rather than zeroed.** If prices could not be read, the report says
"not available" instead of printing `$0.00`. A confident wrong number is worse than an admitted gap.

**Exit test:** on the default branch, after cooking a batch and quitting to menu,
`%APPDATA%\Schedule1RecipePlanner\profiles\<id>\cookbook.md` exists and describes the batch.
*(Needs a live run, like everything else remaining.)*

---

## R5 — Verify pricing live 🟡 statically verified; needs one live run

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

### What was found and fixed without the game

**The pricing path was entirely outside the fail-closed guarantee.** The mod's headline safety
claim — "verifies its hooks and refuses to record numbers it cannot trust" — did not cover pricing
at all. `ProductManager.ProductPrices`, `AllProducts`, the whole `ScheduleOne.Registry` type,
and `StorableItemDefinition.BasePurchasePrice` were reached by reflection and never verified. A
rename in any of them would have left `SymbolGuard` reporting a confident PASS while every money
figure silently read `$0`.

All four are now in the hook table as **Optional** — verified, but degrading to a warning rather
than disabling tracking, because prices are a display concern and killing the tracker over a renamed
price field would be the worse failure.

**Then the member names were checked against the real shipped assemblies**, offline, with
`tools/HookVerifier` — no game launch needed:

```
Symbol check PASSED (16/16 hooks resolved)
```

Every pricing member exists and is spelled correctly in 0.4.5f2. That removes "the names are wrong"
from the list of things R5 might discover, which was the most likely explanation for the `$0`
symptom. What is left to test live is only whether the game's singletons are populated at the moment
the first batch asks — a much narrower question.

**The silent failure itself is fixed.** `GamePriceSource` used to log
`Prices loaded: 0 products, 0 ingredients` at **Info** level, which reads as success. Now:

- a fully empty load is retried up to 3 times, in case it was simply asked before the game was ready
- a partially empty load warns which half is missing
- a permanently empty load warns loudly and says what to check
- `CookbookReport` omits money entirely rather than rendering `$0.00`

A false zero is worse than an admitted gap, and this is principle 4 of the project applied to the
one place that was violating it.

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

## R7 — Package the release 🟡 done except the live-install check

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

**Done, except the exit test itself.**

- **LICENCE** — [MIT](../LICENSE) added. This was a call made on your behalf because a repo with no
  licence is "all rights reserved" by default, which is worse than any choice. MIT is the common
  default for game mods and matches fully permissive Nexus fields.
  **Change it now if you disagree** — it is far easier before the first release than after.
- **Version** — `0.9.0`, in `MelonInfo`, the Nexus draft and the archive name. Deliberately not
  `1.0.0`: R1, R5 and R6 are unverified, so calling it 1.0 would be a claim not yet earned. Bump to
  `1.0.0` when they pass, and tag the commit to match.
- **`Newtonsoft.Json`** — **verified, not assumed.** MelonLoader 0.7.3 ships `13.0.4` in all three
  host folders (`net35`, `net472`, `net6`), and the game carries its own copy in
  `Schedule I_Data/Managed`. We compile against `13.0.3` and do not ship it. Nothing to do.
- **Release build** — `dist/` is the Release output; a bare `dotnet build` would stage Debug, so
  [`tools/package.ps1`](../tools/package.ps1) always forces `-c Release`.
- **No PDBs** — the staging list names DLLs explicitly, so none can leak in.
- **Archive layout** — `Mods/` prefixed, so mod managers extract to the right place. Produced by
  `pwsh tools/package.ps1`, which reads the version from `MelonInfo` (so the archive name cannot
  drift), refuses to build if a required DLL is missing, warns loudly if `RecipePlanner.PhoneApp.dll`
  is absent (an IL2CPP-only build would silently ship with no UI at all), and prints the archive
  contents back for inspection.

Current output: `release/Schedule-I-Cookbook-0.9.0.zip`, 88.8 KB, containing `LICENSE`,
`README.txt` and the five DLLs under `Mods/`.

---

## R8 — Make the page worth clicking 🟡 copy written; screenshots outstanding

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

**Copy written:** [07-NEXUS-PAGE.md](07-NEXUS-PAGE.md) — paste-ready description, the form fields,
permissions set to match the MIT licence, and a 0.9.0 changelog. It leads on "you never log
anything" and "it never writes to your save", and carries the fail-closed hook check as the
differentiator, per the notes above.

**Two things are still missing and only you can supply them:**

1. **Screenshots.** Not one image of the phone app exists. The list of five worth taking is at the
   bottom of [07-NEXUS-PAGE.md](07-NEXUS-PAGE.md). This is the single highest-value remaining task:
   the app is the selling point and nobody will read past a page with no pictures of it.
2. **The multiplayer section**, which is left as a `TODO` marker in the draft, blocked on R6.
   Do not publish with it unwritten.

---

## Where this stands

| Step | State |
|---|---|
| R0 repo | ✅ done |
| R1 IL2CPP blocker | 🟡 code done and statically verified; **needs a live run on both branches** |
| R2 scope / naming | ✅ done |
| R3 documentation | ✅ done |
| R4 install guide | ✅ written; **needs one clean-install walkthrough** |
| R5 pricing | 🟡 members verified offline (16/16) and silent-failure fixed; needs one live run |
| R6 multiplayer | ⬜ **needs a live host + client session** |
| R7 packaging | 🟡 done; **needs the extract-and-launch check** |
| R8 page copy | 🟡 written; **needs screenshots** |
| R9 default-branch value | ✅ readable `cookbook.md` export, 9 tests |

Everything that could be done without launching the game is done. **Every remaining item needs a
running game**, which is the one thing this could not do.

### The session that finishes this

One sitting, in this order:

1. **Default (IL2CPP) branch.** Extract `release/Schedule-I-Cookbook-0.9.0.zip` over the game folder
   — that also discharges R7's exit test and R4's walkthrough. Launch, check the log for
   `Symbol check PASSED`, `Production tracking ENABLED` and `IL2CPP branch detected`. Cook one
   batch, confirm exactly one `Production Detected`, then quit to menu and open
   the profile's `cookbook.md` under %APPDATA%\Schedule1RecipePlanner\ — it should describe
   that batch (**R9**). **That closes R1's live half**, the thing that was blocking release.
2. **Same session:** check the log for `Prices loaded: N products, M ingredients` with non-zero
   counts, and compare a recorded batch value against the game's own quoted price. **R5.**
3. **Switch to the `alternate` branch.** Confirm the Cookbook app still appears — it installs
   through a new code path now and a regression there is invisible to every static check. Take the
   five screenshots while you are in there. **R8.**
4. **Multiplayer, host and client.** Note what each one records. Write it into the `TODO` block in
   [07-NEXUS-PAGE.md](07-NEXUS-PAGE.md). **R6.**
5. Bump `MelonInfo` to `1.0.0`, re-run `pwsh tools/package.ps1`, tag the commit, publish.

If step 1 fails, everything else waits — that is still the blocker.

## Explicitly out of scope for v1

Do not let these hold up the release — they are the next release, not this one:

- **IL2CPP support for the cookbook UI.** `CookbookScreen`, `AppIconFactory` and `SmoothScroll` need
  only UnityEngine and would likely port; subclassing the generic `App<T>` base in `CookbookApp`
  through Il2CppInterop is the genuinely miserable part. R1 makes shipping without it acceptable.
- **Roadmap phases 2, 3, 4, 6, 14, 15, 16, 17** — the planning and optimisation half.
- **A `MelonPreferences` config.** Players will ask for toggles; that is a fine v1.1 response.
