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
| **Licence** | All rights reserved — see [LICENSE](../LICENSE) |

### Permissions

**All rights reserved.** Set every Nexus permission to **No**, or the page and the licence
contradict each other — and a contradiction is what gets argued about later:

| Permission | Setting |
|---|---|
| Users can upload this file to other sites | **No** |
| Users can convert this file to work on other games | **No** |
| Users can modify and release under their own name | **No** |
| Users can use assets without permission | **No** |
| Users can modify for their own personal use | **No** |
| Users can use this in a modpack / collection | **No** — ask first |

### The "ask me first" note

Nexus has a free-text permissions box. Every checkbox above stays **No** — this is the human
explanation of *why*, and it is what people will actually read. Pure refusal reads as hostile;
refusal plus an open door reads as reasonable, and costs you no rights whatever.

Pick one.

**Short — for the permissions box:**

> All rights reserved, but I'd rather talk than lawyer.
>
> Please don't re-upload this mod, mirror it, put it in a collection or modpack, or reuse its code.
> The source is public so you can see exactly what it does to your machine and your saves — that's
> transparency, not a licence to reuse it.
>
> If you want to do any of that, just ask me first. I'm not precious about it, I'd just like to know
> where my work is going and be able to fix it if it breaks. I say yes more often than no.
>
> And if I ever go quiet and stop updating this, ask me and I'll almost certainly hand it over
> rather than let it rot.

**Longer — if you want it on the page itself as well:**

> **Permissions — the short version: ask me.**
>
> This mod is all rights reserved. Practically, that means: use it, enjoy it, tell people about it.
> Don't re-upload it anywhere, don't mirror it, don't bundle it into a collection or modpack, don't
> translate or port it, and don't reuse its code in your own project — not without asking me first.
>
> The source code is on GitHub. That's there so you can check what a mod that hooks your game and
> writes files is actually doing, before you trust it. Being able to read it isn't the same as being
> licensed to reuse it.
>
> None of this is me being territorial. It's that a mod like this breaks every time the game updates
> — that's why it verifies its own hooks before it records anything — and a stale copy floating
> around on another site, silently recording wrong numbers with my name on it, is bad for whoever
> downloaded it and bad for me.
>
> And if I ever go quiet and stop updating this, ask me and I'll almost certainly hand it over
> rather than let it rot. I'd rather someone else kept it working than have it die quietly with a
> broken hook table.
>
> So: ask. I'm genuinely easy to deal with, and the answer is usually yes.

Both versions now carry the hand-it-over line, at the author's decision. It defuses the single most
common objection to a restrictive licence — *"what happens when you abandon it?"* — and commits you
to nothing: you are still the one being asked, and "almost certainly" is not "definitely".

Keep it in both places if you use both. An assurance that appears on the page but not in the
permissions box looks like it was quietly withdrawn.

### Replies you will need later

Requests will come. Having these ready keeps your tone consistent when you are answering on a phone
at midnight. Adjust to taste.

**Translation request** — say yes:

> Yes, please do. Two conditions: link back to this page rather than re-hosting the DLLs, and let
> me know when it's up so I can link it from here.

**Modpack / collection request** — this is the one worth thinking about:

> Thanks for asking. I'd rather it wasn't bundled — this mod breaks whenever the game updates, and a
> collection pinning an old copy means people get silently wrong statistics and come to me about it.
> Link to the mod page instead and I'm happy.

**Someone re-uploaded it** — firm, not aggressive, and always first contact rather than a report:

> Hi — I'm the author of Schedule I Cookbook. It's all rights reserved and I didn't give permission
> for this upload. Please take it down. If you had a reason for mirroring it, tell me and we can
> sort something out, but I'd like it removed either way.

**"Why not open source it?"**:

> The source is public — it's on GitHub, so you can read exactly what it does. I just haven't
> licensed it for reuse. Happy to talk if there's something specific you want to do with it.

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
- **A readable cookbook outside the game.** Your recipes, chains and totals are also written to a
  `cookbook.md` file you can open in any editor, keep, or share. Works on **both** Steam branches.

### It will not touch your save

The mod never writes to your game save. Not once. Its own records live in
`%APPDATA%\Schedule1RecipePlanner\`, and uninstalling leaves your game exactly as it was.

It also never counts your inventory — inventory cannot tell a product you made from one you bought,
were given, or spawned. Only real completion events are counted, which is why the numbers are
trustworthy.

### It fails safely when the game updates

Schedule I updates often, and mods that read the game's internals break when it does. Most break
silently, and quietly record nonsense.

This one checks all 16 of the game symbols it depends on **before** it patches anything. If even
one has moved, it disables tracking, says so in the log, and records nothing. Wrong statistics are
worse than no statistics.

### Which Steam branch?

Both work. They do not get the same features.

| | Default branch | `alternate` branch |
|---|---|---|
| Production tracking, history, statistics | ✅ | ✅ |
| Automatic recipe discovery | ✅ | ✅ |
| **Readable `cookbook.md` export** | ✅ | ✅ |
| **Cookbook app on the in-game phone** | ❌ | ✅ |

The tracking half works everywhere. The phone app needs the `alternate` (Mono) branch — it has to
build UI inside the game, which the default branch's scripting backend makes far harder.

**On the default branch you still get everything except the in-game screen:** tracking runs
normally, and your cookbook is written to a readable `cookbook.md` you can open any time.

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
- **Money figures may be unavailable** in some setups. When the mod cannot read the game's price
  table it leaves money out entirely rather than showing a confident `$0`; everything else still
  works.

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
- 16/16 hook verification that fails closed on a game update
