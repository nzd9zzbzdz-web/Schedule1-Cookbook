# Install

For players. If you are building from source, see [04-SETUP.md](04-SETUP.md) instead.

## What you need

- **Schedule I** on Steam, switched to the **`alternate` branch** (Step 0 below — this is required)
- **MelonLoader v0.7.3** — <https://melonwiki.xyz/#/README?id=automated-installation>

That is all. Do **not** download Newtonsoft.Json or anything else separately — MelonLoader already
ships everything this mod needs.

## Step 0 — switch to the `alternate` Steam branch

**Required.** Do this first, before installing MelonLoader. The Cookbook app cannot run on the
default branch.

1. Open Steam and go to **Library**
2. **Right-click Schedule I** → **Properties**
3. In the left sidebar, click **Betas**
4. In the **Beta Participation** dropdown, choose **`alternate`**
5. Close the window — Steam downloads the change on its own

Steam's own description of that branch:

> Uses Mono instead of IL2CPP as the scripting backend. Less performant than the default version,
> but less prone to crashes.

> **Back up your saves first**, as you would before any game version change. Saves are shared
> between the branches, so switching does not delete anything, but the game is a different build
> and a backup costs nothing:
>
> ```
> %USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Saves\
> ```

The game is around 7 GB, so expect a substantial download when you switch — and again if you ever
switch back.

### Why is this needed?

The mod builds a real app inside the game's phone. That means creating UI components at runtime,
which the default branch's scripting backend (IL2CPP) makes dramatically harder — specifically,
subclassing the game's generic `App<T>` base is the case the IL2CPP interop layer handles worst.

The `alternate` branch uses Mono instead, where this is straightforward.

### What if I install it on the default branch anyway?

It will not crash. The mod checks at startup, skips the UI, and tells you:

```
IL2CPP branch detected. This mod is built and tested for the 'alternate' (Mono) Steam branch.
```

Production tracking may well still work there — it reads the game by reflection and handles both
naming schemes — but **it is untested and unsupported**, and there is no Cookbook app. If you report
a problem from the default branch, the first answer will be "please switch branches".

## Step 1 — install MelonLoader

Run the MelonLoader installer, point it at your Schedule I folder, and install **v0.7.3**.

To find the folder: Steam → right-click **Schedule I** → **Manage** → **Browse local files**.

It adds a `MelonLoader\` folder and a `version.dll`. Nothing of the game's is overwritten.

## Step 2 — launch the game once, and wait

**Do this before installing the mod.** MelonLoader sets itself up on first run, and the game can
look like it has frozen while it does. It has not. Let it finish and reach the main menu, then quit.

Skipping this step is the single most common way to get a mod that "doesn't load".

## Step 3 — install the mod

Copy **all** the `.dll` files from the download into `Schedule I\Mods\`:

```
Schedule I\
└── Mods\
    ├── RecipePlanner.dll
    ├── RecipePlanner.Core.dll
    ├── RecipePlanner.Game.dll
    ├── RecipePlanner.UI.dll
    └── RecipePlanner.PhoneApp.dll      (this one draws the app)
```

All of them are required. Copying only `RecipePlanner.dll` will not work.

## Step 4 — check it loaded

Launch the game and watch the MelonLoader console, or open
`Schedule I\MelonLoader\Latest.log` afterwards. You want:

```
[Schedule_I_Cookbook] Schedule I Cookbook starting — verifying game symbols before patching.
[Schedule_I_Cookbook] Symbol check PASSED (22/22 hooks resolved)
[Schedule_I_Cookbook] Production tracking ENABLED — waiting for a save to load.
```

Load a save and cook one batch. You should get exactly one `Production Detected` block.
## Where your data goes

The mod **never writes to your game saves.** Everything it records is its own, here:

```
%APPDATA%\Schedule1RecipePlanner\
└── profiles\
    └── <profile-id>\
        ├── cookbook.md     ← THE ONE TO OPEN: your cookbook, readable
        ├── events.jsonl     every production event, append-only
        ├── recipes.json     your cookbook, machine-readable
        ├── stats.json       computed totals
        └── profile.json
```

Each character gets its own profile, keyed so that deleting a save slot and making a new one in its
place does not merge the two.

### `cookbook.md` — your cookbook outside the game

Every time a save unloads, the mod writes a readable Markdown file containing your recipes grouped
by strain with their full ingredient chains, your production totals, per-product and per-ingredient
breakdowns, and your records.

It opens in any text editor, and renders nicely in anything that understands Markdown — Obsidian,
VS Code, GitHub, Discord. Handy for keeping a copy of a cookbook, or sharing one.

It is rewritten from scratch each time, so do not edit it — copy it somewhere else first if you want
to keep a version. Nothing is lost either way: it is derived entirely from `events.jsonl`.

## Uninstalling

- **The mod:** delete its `.dll` files from `Schedule I\Mods\`.
- **Its data:** delete `%APPDATA%\Schedule1RecipePlanner\`.
- **MelonLoader:** delete `MelonLoader\`, `version.dll` and `Mods\` from the game folder.

Removing any of it leaves your saves untouched, because nothing was ever written to them.

## Troubleshooting

**Nothing appears in the log at all.** MelonLoader is not installed or did not load. Check that
`version.dll` is in the game folder.

**`No Schedule I game assembly found`.** Step 2 was skipped or interrupted. Quit, launch again, and
let the first-run generation finish.

**`Production tracking DISABLED`, with a list of missing hooks.** The game updated and the mod has
not caught up. This is deliberate — it refuses to record numbers it cannot trust rather than
recording wrong ones. Report it with the log and wait for an update.

**No Cookbook app on the phone.** Check you are on the `alternate` branch (Step 0) — the app cannot
run on the default one. If you are, check `RecipePlanner.PhoneApp.dll` actually made it into the
Mods folder.

**No money figures anywhere.** If the mod could not read the game's price table, it says so in the
log and leaves money out of `cookbook.md` entirely rather than printing a confident `$0`. Units,
batches and recipes are unaffected. Include the log if you report it.

### Reporting a bug

Include `Schedule I\MelonLoader\Latest.log`. Almost every question is answered by the first ten
lines of it. Say which branch you are on, too.
