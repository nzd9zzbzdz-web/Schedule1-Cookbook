# Nexus page — draft copy

Paste-ready text for the mod page, plus the form fields Nexus asks for.

**1.0 ships for the `alternate` (Mono) branch only.** Every claim below is something that has been
verified on a running game — see [05-RELEASE-ROADMAP.md](05-RELEASE-ROADMAP.md). The one section
still unwritten is multiplayer (R6).

---

## Form fields

| Field | Value |
|---|---|
| **Name** | Schedule I Cookbook |
| **Summary** | An automatic cookbook, production record and mixing guide, read from your own save. Never writes to it. Requires the `alternate` branch. |
| **Category** | Miscellaneous (or Gameplay, if Nexus has no better fit for Schedule I) |
| **Version** | 0.9.0 |
| **Requirements** | MelonLoader v0.7.3 · Schedule I on the **`alternate` (Mono) Steam branch** |
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

### Three screens on your phone

**📖 Cookbook** — every recipe you have discovered, grouped by strain, each showing the full
ingredient chain that built it. Hover a recipe to see all its effects. Sort seven ways — name, units
made, value, most recent, chain length, addictiveness — filter to favourites or to what you have
actually cooked, and hide the ones you are done with. Hiding is display only: the recipe stays in
the game and keeps its history.

**📊 Statistics** — units, batches, value, cost and profit for the character you are playing. A
breakdown by drug type, your most-produced products, your most-used ingredients, and your records.
Cooks done by your employees are tracked but kept out of your personal totals, and the screen says
how many so a lower number than you expected never looks like a miscount.

**🌿 Mix Guide** — what every ingredient actually does. Pick an ingredient and see the effect it
adds and what it turns each existing effect into. Or pick an effect you want and see every route to
it — which ingredients add it outright, and which combinations convert into it.

That last one is read out of **your own save**, not copied from a wiki. Schedule I can randomise its
mix maps per game, so a static chart is simply wrong for some players. This one asks your game.

### And it keeps the record for you

- **Recipes discover themselves.** Invent a mix in-game and it appears in the cookbook unprompted,
  placed under its strain with its full ancestry — no saving or reloading needed.
- **Nothing is ever logged by hand.** The mod hooks the game's own mixing-completion events.
- **A readable copy outside the game.** Everything is also written to a `cookbook.md` you can open
  in any editor, keep, or share.

### It will not touch your save

The mod never writes to your game save. Not once. Its own records live in
`%APPDATA%\Schedule1RecipePlanner\`, and uninstalling leaves your game exactly as it was.

It also never counts your inventory — inventory cannot tell a product you made from one you bought,
were given, or spawned. Only real completion events are counted, which is why the numbers are
trustworthy.

### It fails safely when the game updates

Schedule I updates often, and mods that read the game's internals break when it does. Most break
silently, and quietly record nonsense.

This one checks all 30 of the game symbols it depends on **before** it patches anything. If even
one has moved, it disables tracking, says so in the log, and records nothing. Wrong statistics are
worse than no statistics.

### ⚠️ Requires the `alternate` Steam branch

**Read this before downloading.** This mod needs Schedule I switched to the **`alternate` (Mono)**
branch. The Cookbook app cannot run on the default branch.

Switching takes four clicks:

Steam → right-click **Schedule I** → **Properties** → **Betas** → choose **`alternate`**.

Steam's own description of that branch: *"Uses Mono instead of IL2CPP as the scripting backend. Less
performant than the default version, but less prone to crashes."*

Two things to know: the game is around 7 GB, so expect a real download; and **back up your saves
first**, as with any version change. Saves are shared between branches, so nothing is lost by
switching, but a backup costs nothing.

**Why?** The mod builds an actual app inside the game's phone. That means creating UI at runtime,
and subclassing the game's generic `App<T>` base is the single case the default branch's interop
layer handles worst. On Mono it is straightforward.

**If you install it on the default branch anyway,** it will not crash — it detects the branch, skips
the UI, and says so in the log. Production tracking may well still work there, but that is untested
and unsupported, and there is no Cookbook app.

### Install

1. Install **MelonLoader v0.7.3**.
2. **Launch the game once and let it reach the menu.** First run generates files and can look frozen
   for a minute. Skipping this is the most common cause of "it didn't load".
3. Copy **all** the `.dll` files into `Schedule I\Mods\`.

Full guide, including uninstall and troubleshooting: [06-INSTALL.md](06-INSTALL.md).

### Not included (yet)

Being straight about scope, because the name used to promise more:

- **No full-mix prediction.** The Mix Guide tells you what any one ingredient does to any one
  effect, which covers most planning. What it will not do is simulate a whole recipe end to end and
  hand you the finished effect list before you cook it.
- **No recipe optimisation or comparison.**
- **No search box** in the cookbook. A text field inside the running game fights your movement keys
  for keyboard focus. Sorting and filters cover the same ground; search comes back when it can be
  done without that trade-off.
- **Money figures may be unavailable** in some setups. When the mod cannot read the game's price
  table it leaves money out entirely rather than showing a confident `$0`; everything else still
  works.

### Multiplayer — honest answer

Use this until R6 is tested, then replace it with what you actually saw. Silence is worse than an
admission: "does this work in co-op?" is the question that gets asked either way, and answering it
on the page beats answering it forty times in the comments.

> **Not fully tested yet.** Here is what the mod is built to do, and what I have not confirmed.
>
> Each player's copy records to their own machine — nothing is shared or synced between you. Cooks
> done by another player are recorded but deliberately kept out of your personal totals, the same
> way employee cooks are, so your numbers stay yours.
>
> What I have not verified is how that behaves as a joining client rather than as the host. It
> should be fine; I have not proven it. If you play co-op I would genuinely like to hear what you
> see — a log and a sentence is plenty.

Two things to actually check when you can, because they are the ones most likely to be wrong:

1. **On a client, does another player's cook get recorded as yours?** The game dispatches the
   completion event to every machine, so a client's mod sees the host's batches too. Attribution is
   supposed to exclude them.
2. **Does the host see a guest's cook as `remote` rather than `local`?**

<!-- TODO: fill in after R6. Do not publish with this section unwritten — it will be the first
     question in the comments. Test as host and as client, then state plainly what each one gets. -->

---

## Screenshots needed

The app is the entire selling point, and a mod page with no pictures of it gets scrolled past. In
rough order of how much work each one does for you:

1. **A strain filtered to one section, showing deep ingredient chains.** This is the shot. A product
   four levels down with its whole ancestry visible is the one thing no other tool shows, and it is
   immediately obvious what it means without reading a word.
2. **The Mix Guide, on the "By effect" tab, with a real effect selected** — the routes list is the
   answer to the question players actually arrive with.
3. **The Statistics screen**, scrolled to show the by-type bars.
4. **The cookbook at ALL STRAINS**, for the sense of scale.
5. **The effects card open over a row** — it shows the hover interaction, which is otherwise
   invisible in a still.
6. Optional: the MelonLoader console showing a `Production Detected` block. It is proof of the
   automatic tracking, which is the hardest claim to believe from text alone.

Put **1** first in the gallery. Nexus shows the first image as the card thumbnail, and that is the
one deciding whether anyone clicks at all.

---

## Changelog for 0.9.0

First public release.

**On your phone**

- Cookbook — recipes grouped by strain, full ingredient chains, effects on hover, 7 sort orders,
  filters and hiding
- Statistics — lifetime totals, value and profit, breakdown by drug type, top products and
  ingredients, records, and separately-tracked employee production
- Mix Guide — what each ingredient adds and converts, and every route to a given effect, read from
  your own save rather than a fixed table

**Underneath**

- Automatic production tracking via the game's own mixing-completion events; nothing is logged by hand
- History that survives restarts, with employee cooks recorded but kept out of personal totals
- Recipes discover themselves and are placed under their strain immediately, with no save needed
- A readable `cookbook.md` export written alongside the data
- Never writes to your game save
- 30/30 symbol verification that disables tracking rather than recording wrong numbers after a game
  update
