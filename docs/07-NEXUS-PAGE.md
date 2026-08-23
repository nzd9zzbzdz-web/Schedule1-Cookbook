# Nexus page — draft copy

Paste-ready text for the mod page, plus the form fields Nexus asks for. Nothing here is live until
[05-RELEASE-ROADMAP.md](05-RELEASE-ROADMAP.md) R1/R5/R6 are confirmed on a running game.

---

## Form fields

| Field | Value |
|---|---|
| **Name** | Schedule I Cookbook |
| **Summary** | An automatic cookbook and production record for your mixes. Never writes to your save. |
| **Category** | Miscellaneous (or Gameplay, if Nexus has no better fit for Schedule I) |
| **Version** | 0.9.0 |
| **Requirements** | MelonLoader v0.7.3 |
| **Licence** | MIT — see [LICENSE](../LICENSE) |

### Permissions

MIT permits all of the below. Set the Nexus fields to match, or the licence and the page contradict
each other:

| Permission | Setting |
|---|---|
| Users can upload this file to other sites | Yes |
| Users can convert this file to work on other games | Yes |
| Users can modify and release under their own name | Yes — credit appreciated, not required |
| Users can use assets without permission | Yes |

---

## Description

### Your cookbook, written for you

Schedule I remembers your recipes. It does not help you *use* them. Once you are a few hundred mixes
in, the product list is an unsorted wall and the only way to remember how you made something is to
remember how you made it.

This mod keeps the record for you, automatically.

**You never log anything.** There is no "record this cook" button. The mod hooks the game's own
mixing-completion events, so every batch is captured as it happens — including the ones your
employees cook while you are somewhere else.

### What it does

- **A cookbook on your phone.** Every recipe you have discovered, grouped by strain, with the full
  ingredient chain that leads to each one — so you can see how a product was actually built up,
  step by step.
- **Sort seven ways** — name, units made, value, most recent, chain length, addictiveness — with
  favourites pinned to the top.
- **Hide the clutter.** Hiding a recipe removes it from the list only. It stays in the game, and its
  history and statistics are untouched.
- **Automatic production history.** Units, batches, per-product breakdown, ingredient usage.
- **Recipes discover themselves.** Invent a mix in-game and it appears in the cookbook unprompted.
- **Employee cooks are tracked but kept separate**, so your personal totals stay yours.

### It will not touch your save

The mod never writes to your game save. Not once. Its own records live in
`%APPDATA%\Schedule1RecipePlanner\`, and uninstalling leaves your game exactly as it was.

It also never counts your inventory — inventory cannot tell a product you made from one you bought,
were given, or spawned. Only real completion events are counted, which is why the numbers are
trustworthy.

### It fails safely when the game updates

Schedule I updates often, and mods that read the game's internals break when it does. Most break
silently, and quietly record nonsense.

This one checks all 13 of the game functions it depends on **before** it patches anything. If even
one has moved, it disables tracking, says so in the log, and records nothing. Wrong statistics are
worse than no statistics.

### Which Steam branch?

Both work. They do not get the same features.

| | Default branch | `alternate` branch |
|---|---|---|
| Production tracking, history, statistics | ✅ | ✅ |
| Automatic recipe discovery | ✅ | ✅ |
| **Cookbook app on the in-game phone** | ❌ | ✅ |

The tracking half works everywhere. The phone app needs the `alternate` (Mono) branch — it has to
build UI inside the game, which the default branch's scripting backend makes far harder.

The mod detects your branch at startup and tells you which mode it is in. On the default branch it
loads and tracks normally; it does not fail.

To switch: Steam → right-click **Schedule I** → **Properties** → **Betas** → **`alternate`**.
Saves are shared between branches — back them up first, as with any version change.

### Install

1. Install **MelonLoader v0.7.3**.
2. **Launch the game once and let it reach the menu.** First run generates files and can look frozen
   for a minute. Skipping this is the most common cause of "it didn't load".
3. Copy **all** the `.dll` files into `Schedule I\Mods\`.

Full guide, including uninstall and troubleshooting: [06-INSTALL.md](06-INSTALL.md).

### Not included (yet)

Being straight about scope, because the name used to promise more:

- **No recipe planning or prediction.** It records what you *have* made; it does not tell you what
  to make next, or predict a mix's effects before you cook it.
- **No recipe optimisation or comparison.**
- **No search box** in the cookbook. A text field inside the running game fights your movement keys
  for keyboard focus. Sorting and filters cover the same ground; search comes back when it can be
  done without that trade-off.
- **Money figures may show `$0`** in some setups — being fixed.

### Multiplayer

<!-- TODO: fill in after R6. Do not publish with this section unwritten — it will be the first
     question in the comments. Test as host and as client, then state plainly what each one gets. -->

---

## Screenshots needed

The page must not go up without these — the phone app is the entire selling point and there is
currently not one image of it.

1. The cookbook list, showing several strain sections.
2. A strain section expanded, showing the ingredient chain for one product.
3. The toolbar, showing sort and filter controls.
4. The statistics view.
5. Optional: the MelonLoader console showing a `Production Detected` block — proof of the automatic
   tracking, which is the hardest claim to believe from text alone.

---

## Changelog for 0.9.0

First public release.

- Automatic production tracking via the game's own mixing-completion events
- Production history that survives restarts, with employee cooks attributed separately
- Automatic recipe discovery
- Cookbook phone app with strain grouping, ingredient chains, 7 sort orders, filters, favourites
  and hiding (`alternate` branch)
- Lifetime statistics
- 13/13 hook verification that fails closed on a game update
