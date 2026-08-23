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
