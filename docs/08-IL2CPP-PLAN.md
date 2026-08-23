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

---

## Steps 4 and 5 done — the probe reads zero

```
dotnet build tools/Il2CppProbe
    0 Error(s)
Il2CppProbe.dll  86,528 bytes
```

**The entire phone app now compiles against the IL2CPP proxies**, `CookbookApp : App<CookbookApp>`
included. The Mono build stayed green throughout and all 283 tests still pass.

### First, a correction

The measured section above claimed `CookbookScreen.cs` and three other files "compile clean" and
that 77% of the UI was already branch-agnostic. That was wrong, and wrong in the flattering
direction. Those files had no errors *reported yet* — the compiler stops doing semantic analysis on
code whose types failed to resolve, so fixing the early errors revealed later ones. The count went
12 → 7 → 14 → 0, not 12 → 0. The final number is real; the intermediate optimism was not.

### What actually differed between the branches

Four things, and only four:

1. **Namespace.** `ScheduleOne.*` is `Il2CppScheduleOne.*`. Conditional using-directives, plus one
   fully-qualified `Registry` call behind a `using GameRegistry =` alias.

2. **Override access.** `App<T>.Start` and `PlayerSingleton<T>.Awake` are `protected` on Mono and
   `public` on the proxies, and C# forbids narrowing access when overriding.

3. **Unity's EventSystems interfaces are emitted as classes**, so a `MonoBehaviour` cannot also
   implement them. `HoverGlow` and `SmoothScroll` drop the interfaces on IL2CPP and go inert:
   `SmoothScroll` never sets `_gliding` so its `LateUpdate` returns immediately and the `ScrollRect`
   handles the wheel itself, and `HoverGlow` stays at its rest colour. The cost is a hover effect
   and eased scrolling — not function.

4. **`UnityAction` is a class, not a delegate**, so lambdas cannot be handed to `AddListener`
   without being marshalled first. Collected in [`UiInterop`](../src/RecipePlanner.PhoneApp/UiInterop.cs)
   along with `new GameObject(name, typeof(T))`, which wants a native type array on IL2CPP and is
   simply written as construct-then-add instead — identical in effect, and it compiles on both
   branches with no conditional at all.

That seam is one small file. The alternative was `#if` scattered through two thousand lines of
layout code, where a branch-specific bug could hide indefinitely.

### The blocker was not what this document said it was

This plan opened by asserting that subclassing `App<T>` was the thing to route around, and proposed
an elaborate clone-and-patch approach to avoid it. **`CookbookApp : App<CookbookApp>` compiles
against the proxies without complaint.** The clone-and-patch design was solving a problem that does
not exist at compile time.

It may still exist at *runtime*, which is the part worth being careful about. The real question was
never whether the C# compiler accepts the declaration — it is whether
`ClassInjector.RegisterTypeInIl2Cpp<CookbookApp>()` succeeds for a managed type whose base is an
IL2CPP generic instantiation. Compiling proves the shapes line up. It proves nothing about
injection, and injection is where Il2CppInterop is known to be weakest.

So the honest position: the port is much further along than expected, one genuine unknown remains,
and it can only be answered by running it.

Also settled along the way, from a compiler error rather than a guess: `App<T>` **does** derive from
`PlayerSingleton<T>` — the override error named `PlayerSingleton<CookbookApp>.Awake()` directly. The
plan listed that as unconfirmed. The singleton concern is real if the clone approach is ever needed.

### What is left

| # | Step | Needs the branch? |
|---|---|---|
| 6 | Try direct injection — register `CookbookApp` and see whether it takes | **Yes** |
| 7 | If injection fails, fall back to clone-and-patch as described above | Yes |
| 8 | Confirm tracking actually fires live, not just that its symbols resolve | Yes |

Step 6 is now a ten-minute experiment rather than a rewrite: the code already builds, so it is one
launch and one log line. That is a very different proposition from where this document started, and
it is the point at which the branch switch finally earns its 7 GB.
