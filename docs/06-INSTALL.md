# Install

For players. If you are building from source, see [04-SETUP.md](04-SETUP.md) instead.

## What you need

- **Schedule I** on Steam
- **MelonLoader v0.7.3** — <https://melonwiki.xyz/#/README?id=automated-installation>

That is all. Do **not** download Newtonsoft.Json or anything else separately — MelonLoader already
ships everything this mod needs.

## Step 1 — install MelonLoader

Run the MelonLoader installer, point it at your Schedule I folder, and install **v0.7.3**.

To find the folder: Steam → right-click **Schedule I** → **Manage** → **Browse local files**.

It adds a `MelonLoader\` folder and a `version.dll`. Nothing of the game's is overwritten.

## Step 2 — launch the game once, and wait

**Do this before installing the mod.** On the default branch MelonLoader has to generate its proxy
assemblies on first run. It takes anywhere from ~30 seconds to a couple of minutes, and the game
will look like it has frozen. It has not. Let it finish and reach the main menu, then quit.

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
    └── RecipePlanner.PhoneApp.dll      (Mono branch only — see below)
```

All of them are required. Copying only `RecipePlanner.dll` will not work.

## Step 4 — check it loaded

Launch the game and watch the MelonLoader console, or open
`Schedule I\MelonLoader\Latest.log` afterwards. You want:

```
[Schedule_I_Cookbook] Schedule I Cookbook starting — verifying game symbols before patching.
[Schedule_I_Cookbook] Symbol check PASSED (13/13 hooks resolved)
[Schedule_I_Cookbook] Production tracking ENABLED — waiting for a save to load.
```

Load a save and cook one batch. You should get exactly one `Production Detected` block.

## Which Steam branch, and what you get

Both branches work. They do not get the same features.

| | Default branch | `alternate` branch |
|---|---|---|
| Production tracking, history, statistics | ✅ | ✅ |
| Automatic recipe discovery | ✅ | ✅ |
| **Cookbook app on the in-game phone** | ❌ | ✅ |

The tracking works everywhere. The phone app cannot run on the default branch for a technical
reason that is not worth working around yet — it has to build UI objects inside the game, and the
default branch's scripting backend makes that far harder.

The mod tells you which mode it is in at startup:

```
IL2CPP branch detected — tracking, history and statistics all work normally, but the
in-game Cookbook app is Mono-only and will not appear.
```

That message is expected on the default branch. It is not an error.

### Switching to the `alternate` branch (optional)

Steam → right-click **Schedule I** → **Properties** → **Betas** → select **`alternate`**.

Steam's own description of it:

> Uses Mono instead of IL2CPP as the scripting backend. Less performant than the default version,
> but less prone to crashes.

Saves are shared between the two branches. **Back up your saves before switching**, as you would
before any game version change — they live in:

```
%USERPROFILE%\AppData\LocalLow\TVGS\Schedule I\Saves\
```

After switching, launch once before playing so MelonLoader settles.

## Where your data goes

The mod **never writes to your game saves.** Everything it records is its own, here:

```
%APPDATA%\Schedule1RecipePlanner\
└── profiles\
    └── <profile-id>\
        ├── events.jsonl     every production event, append-only
        ├── recipes.json     your cookbook
        ├── stats.json       computed totals
        └── profile.json
```

Each character gets its own profile, keyed so that deleting a save slot and making a new one in its
place does not merge the two.

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

**No Cookbook app on the phone.** Expected on the default branch — see above. On the `alternate`
branch, check `RecipePlanner.PhoneApp.dll` actually made it into `Mods\`.

**Money figures all show `$0`.** Known, being worked on. It means the mod could not read the game's
price table; nothing else is affected.

### Reporting a bug

Include `Schedule I\MelonLoader\Latest.log`. Almost every question is answered by the first ten
lines of it. Say which branch you are on, too.
