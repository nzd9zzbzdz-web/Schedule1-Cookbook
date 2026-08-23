# 1.1 — Supporting the default Steam branch

The single biggest barrier to this mod being used. Everyone lands on the default (IL2CPP) branch;
nobody reaches `alternate` by accident. A player who installs on default today gets tracking that
probably works and no Cookbook app at all.

## What actually blocks it

`CookbookApp : App<CookbookApp> : PlayerSingleton<CookbookApp>`.

Injecting a managed C# type whose base is an **IL2CPP generic instantiation** is the case
Il2CppInterop handles worst. Checked against the shipped assemblies: there is no non-generic `App`
to derive from instead — every app in the game uses `App<T>`, and `App<T>.GetApp` returning `App`
in a signature dump is the tool eliding the arity, not a second base type.

So the direct port is blocked on the hardest part of the interop layer, and that is the thing to
route around rather than fight.

## The approach: don't subclass anything

The installer already clones the Products app's GameObject. Today it strips the original component
and adds ours. Instead:

1. **Keep the cloned `ProductManagerApp` component.** It is a real IL2CPP type, so nothing needs
   injecting — which removes the blocker entirely.
2. **Harmony-patch its methods, filtered to our clone's instance.** `SetOpen` to build and refresh
   our screen, `Update` and `Start` to no-op for the clone if they misbehave against a hierarchy
   whose children we have replaced.
3. **Build the screen into the clone's container** exactly as now. `CookbookScreen` needs Unity, not
   Assembly-CSharp, so most of it should port with little change.

### The dangerous part, named up front

`PlayerSingleton<ProductManagerApp>.Awake` assigns `Instance`. A second live instance would have our
clone **steal the singleton from the player's real Products app**. The prefix that prevents this has
to be exactly right: get it wrong and we do not degrade our own mod, we break a screen the player
depends on.

That is the reason this is not a remote push. It wants a machine that can iterate.

## Build mechanics

`RecipePlanner.PhoneApp` currently references `Schedule I_Data/Managed/Assembly-CSharp.dll`, which
only exists on Mono. On the default branch the equivalent is generated at
`MelonLoader/Il2CppAssemblies/`, and MelonLoader writes it on first launch.

The csproj already detects the branch and compiles to an empty stub when Managed is absent, so the
work is to add a second reference path rather than to restructure anything.

## Order of work

Each step is cheap and rules something out, so nothing large is attempted on an untested assumption.

| # | Step | Proves |
|---|---|---|
| 1 | Publish 1.0 for Mono first | The port cannot delay a finished release |
| 2 | Switch to the default branch, launch once | Il2CppAssemblies exist |
| 3 | `HookVerifier` against `MelonLoader/Il2CppAssemblies` | The hook table resolves against proxy names — **the whole tracking half, before any UI work** |
| 4 | Confirm tracking live on default | Production detection, pricing, cookbook.md, all of it |
| 5 | Point PhoneApp at the proxies, get it compiling | The build story |
| 6 | Clone + keep component + patch `SetOpen` | An app icon appears and opens |
| 7 | Build the screen inside it | The port is done |

Steps 3 and 4 alone are worth doing even if the UI work is abandoned: they turn "tracking probably
works on IL2CPP" into something verified, which is the difference between a claim and a fact.

## What would make this not worth finishing

Worth deciding in advance, so sunk cost does not answer it later:

- If step 3 fails outright, the hook table needs per-branch entries and the cost roughly doubles.
- If the singleton patch cannot be made safe, stop. Breaking the player's Products app is a worse
  outcome than not supporting the branch.
- If real users switch branches without complaint after 1.0, the barrier was smaller than it looks
  and the effort belongs elsewhere.

---

## Measured, 2026-08-23 — the port is far smaller than this plan assumed

Everything above was written from reading. Then two things were actually run, both against the
`MelonLoader/Il2CppAssemblies` proxies already on disk from an earlier stint on the default branch.
Neither needed a branch switch, which is worth remembering before spending 7 GB to answer a
question: **the proxies outlive the branch, and they are enough to check almost everything.**

### Step 3 is done, and it passed

```
HookVerifier … \MelonLoader\Il2CppAssemblies
Symbol check PASSED (30/30 hooks resolved)
RESULT: hook table matches this build — safe to track.
```

**Every hook resolves on IL2CPP.** Production detection, attribution, pricing, the mix guide, the
clock — the entire tracking half of the mod, verified against the default branch's own metadata.
This was the single largest unknown and it is now closed.

What this does *not* prove is that the hooks behave the same once running. Metadata says the
symbols exist and have the right shapes; only a live run says the patches fire. That is step 4 and
it still needs the branch.

### The UI is 12 errors away, not a rewrite

`tools/Il2CppProbe` compiles the real phone-app sources against the proxies. Baseline:

| | |
|---|---|
| Total distinct errors | **12** |
| Files affected | **5 of 10** |
| Lines compiling unchanged | **~3,700 of 4,784 (77%)** |

`CookbookScreen.cs` — 2,291 lines, the entire cookbook — compiles **clean**. So do `MixGuideScreen`,
`StatsScreen`, `AppIconFactory` and `UiFeatures`. All the drawing, layout, sprite generation and
interaction logic is already branch-agnostic, which is the payoff for having kept Unity-only code
free of game types.

The 12 errors are three problems, not twelve:

1. **Namespace rename — 8 errors, `ScheduleOne.*` is `Il2CppScheduleOne.*`.** Mechanical. Affects
   `IconSource`, `CookbookAppInstaller`, `CookbookApp`.

2. **Unity's UI event interfaces are emitted as *classes* — 4 errors.** Il2CppInterop renders
   `IPointerEnterHandler`, `IScrollHandler` and friends as classes, so `: MonoBehaviour,
   IPointerEnterHandler` becomes "multiple base classes". Hits `HoverGlow` (hover glow) and
   `SmoothScroll`. Real work, but confined to two small behaviours, and both degrade gracefully:
   worst case the app ships on IL2CPP without hover glow and with default scrolling.

3. **`App<>` cannot be found — 1 error, `CookbookApp.cs:25`.** The known blocker, and the whole
   reason for the clone-and-patch approach above.

### The approach is confirmed against real metadata

- `Il2CppScheduleOne.UI.Phone.ProductManagerApp.ProductManagerApp` is a real proxy type. Nothing to
  inject.
- It declares `virtual Awake()`, `virtual Start()`, `virtual SetOpen(Boolean)` and `LateUpdate()` —
  every patch target this plan named, all present and all virtual.
- Its base really is `Il2CppScheduleOne.UI.App\`1[[ProductManagerApp]]`, so the blocker is exactly
  where it was expected.
- `App\`1` exposes `appContainer`, `_screen`, the static `Apps` list, `SetOpen`, `Update` and
  `GenerateHomeScreenIcon` — the surface the screen needs.
- `PlayerSingleton\`1` declares `virtual Awake()`, which is where `Instance` is assigned. **The
  singleton-theft risk named above is confirmed real, not hypothetical.**

One thing the probe could not settle: the tool renders `App\`1`'s own base as empty, so whether
`App<T>` derives from `PlayerSingleton<T>` on this branch is still unconfirmed. It decides whether
the singleton risk applies to our clone at all, and it is the first thing to check on a live run.

### Revised order of work

Steps 1–3 are done. The remaining sequence, with the cheap parts first:

| # | Step | Needs the branch? |
|---|---|---|
| 4 | Fix the 8 namespace errors | No — the probe verifies |
| 5 | Rework `HoverGlow` and `SmoothScroll` for interop | No — the probe verifies |
| 6 | Clone, keep `ProductManagerApp`, patch `SetOpen` instance-filtered | Yes |
| 7 | Confirm tracking actually fires live | Yes |

Steps 4 and 5 take the probe from 12 errors to 1 without ever leaving the Mono branch, and that
remaining 1 is the blocker the clone approach is designed to route around. The branch switch is
only needed once there is something to run.
