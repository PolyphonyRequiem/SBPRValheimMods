# Contributing to SBPR Trailborne

Welcome — human or AI. This repo has a few **load-bearing conventions** that are
easy to violate by accident. The single most important one:

> ## 🔴 Spec and code change together, in the same commit.
> Trailborne is **spec-first**. The locked design lives in
> [`docs/v0.1.0/planning/requirements.md`](docs/v0.1.0/planning/requirements.md)
> (and the durable *why* in [`docs/design/`](docs/design/)). If you change a
> recipe, piece, station, item, or mechanic, you **must** update the spec/docs
> in the **same PR** as the code. A code change that diverges from the spec is a
> bug, even if it compiles and runs.

This isn't bureaucracy — the project already drifted once because changes landed
in code without the spec being updated, and nobody could tell what was intended
vs. accidental. `SpecCheck.cs` now screams at server boot when the recipe set
diverges from the manifest. Don't make it scream.

---

## Before you touch anything

1. **Read the spec for the area you're changing.** At minimum:
   - `docs/v0.1.0/planning/requirements.md` — the locked, round-by-round spec.
   - `docs/design/PARKED-2026-06-03.md` — earlier locked design; **wins** where
     requirements.md is silent.
   - `docs/design/trailborne-vision.md` + `design-pillars.md` — the *why*.
   - `PLAYER_GUIDE.md` — player-facing behavior.
2. **Read every `*.md` that touches your feature before proposing changes.** The
   spec history records multiple rounds where work was redone because docs
   weren't read first. Reading is cheaper than redoing.
3. If the spec and the code disagree, **the spec is the source of truth** unless
   the user (Daniel) explicitly overrides it in the PR. Note the discrepancy.

## The spec ⇄ code ⇄ manifest triangle

Three things must stay in sync. Change one → update all three in the same PR:

| Layer | Where | Notes |
|------|-------|-------|
| **Spec** | `docs/v0.1.0/planning/requirements.md`, `docs/design/*`, `docs/datasets/PIECES_AND_CRAFTABLES.md` | Human-readable intent. |
| **Code** | `src/SBPR.Trailborne/**` | The implementation. |
| **Drift manifest** | `src/SBPR.Trailborne/Runtime/SpecCheck.cs` | The locked recipe set, checked at boot. Its header even says: *"Update BOTH this manifest AND the spec in the same commit when intentionally changing a recipe."* |

If you change the number of recipes/pieces (e.g. collapsing 4 sign variants into
1), the `SpecCheck` manifest count **and** the spec doc **and** the code all move
together — or the server logs a drift ERROR on next boot.

### Every `[HarmonyPatch]` class must be registered (enforced at boot)

`SpecCheck` has a sibling: `src/SBPR.Trailborne/Runtime/PatchCheck.cs`. A Harmony
patch class that's attributed but never handed to `harmony.PatchAll(typeof(X))` in
`Plugin.Awake()` compiles fine, ships in the DLL, and silently does **nothing** —
that's exactly how the underwater-cairn gate shipped dead in v0.2.10. `PatchCheck`
runs as the last line of `Awake()` and ERROR-logs (naming the class) if any
`[HarmonyPatch]` type in our assembly produced no woven method we own. **So: when
you add a new `[HarmonyPatch]` class, add its `harmony.PatchAll(typeof(X))` line in
`Plugin.Awake()` in the same change — or the server screams `UNREGISTERED PATCH
CLASS` on next boot.** (Nested patch containers count individually, just as
`Plugin.Awake()` registers each `SignPanelInputBlock.*` separately.)


## Clean-room rule (non-negotiable)

The clean-room firewall is around **other developers' mod code** — Jotunn and any
other mod loader / third-party mod. **It is NOT a firewall around vanilla Valheim.**

- ✅ **Vanilla disassembly is fair game.** You MAY read *and adapt* Valheim's own
  decompiled source to write our implementation (e.g. lifting `GlobalWind`'s
  wind-driver logic). Reading and replicating the behavior of the game we're
  modding is normal engineering, not a violation. Verify names against
  `assembly_valheim.dll` metadata when uncertain.
- ❌ **Do not copy code from Jotunn or any other mod loader / third-party mod
  directly.** Those authors never consented to be our line-by-line reference.
- ✅ **You MAY reproduce another mod's *functionality* via a clean-room RE process
  (a Chinese wall).** The mechanism, using our standing profiles:
  1. A **`reviewer-cleanroom`** (or `re-analyst`) reads the other mod's source and
     writes a *behavioral description* — what it does, the observable
     inputs/outputs/algorithm — **in its own words, copying no code**.
  2. A **separate implementer** who has **never seen** the original source
     reproduces the behavior *from that description only*.
  This separation is what makes the reproduction legally clean. Never let one
  agent both read the original source and write our implementation of it.
- ✅ **You may also just *ask questions*** about another mod ("how does X
  approach Y?") to learn *where* to investigate the *vanilla* internals ourselves —
  often the cheapest path, and it keeps us on vanilla (which is fair game anyway).
- ❌ **Do not *commit* copyrighted files** (game binaries, decompiled IronGate
  source, other mods' source) into this MIT repo. Reading them locally is fine;
  checking them in is not.

In short: take what you need from the base game; keep other developers' code out.

## Building

The build needs Valheim's managed assemblies (from your own install or the free
dedicated server) + BepInEx core. No machine-specific paths are committed.

```bash
scripts/setup.sh        # auto-detect Valheim + write .env   (PS: scripts/setup.ps1)
scripts/fetch-sdk.sh    # fetch pinned BepInEx pack into .sdk/ (PS: .ps1)
dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release
```

A clean build is **0 errors**; the ~29 nullable warnings are a known baseline —
don't add new ones. See [`.env.example`](.env.example).

## CI / releases

- **CI** (`.github/workflows/ci.yml`) builds every push/PR against the free
  dedicated-server assemblies and runs a **reflection-drift guard** (asserts the
  private vanilla members our Harmony patches hook still exist). Don't merge red.
- **Releases** (`.github/workflows/release.yml`) are tag-driven and
  **publish-then-PR**: a tagged build publishes an immutable modpack asset, then
  opens a PR bumping the installer's pinned checksum. See
  [`.github/workflows/README.md`](.github/workflows/README.md).
- The modpack is assembled by `scripts/pack-modpack.sh` (the single source of
  truth — runs locally and in CI).

## Pull requests

- **Daniel gates every merge.** Open the PR; don't self-merge feature work.
- Keep PRs scoped to one concern. Reference the spec section you're satisfying.
- **Delivery is incremental + milestone-based.** M0 = "plugin of nothing"; each
  milestone has named acceptance tests Daniel signs off. Don't jump ahead.
- State what you verified and — honestly — what you did **not**. "Logs green ≠
  playable": server-side success does not prove a joined client can craft it.
  Say which you actually checked.

## Bug reports / tasks (Kanban)

Work is tracked on the Hermes Kanban board (`hermes kanban`). A good bug card:
- quotes the playtest observation,
- points at the **root cause in source** (file + symbol) if known,
- states **expected** vs **actual**,
- and — when the fix changes behavior — **explicitly says to update the spec/docs**
  (per the triangle above), so "done" means code *and* spec.

If you're an automated worker, see [`AGENTS.md`](AGENTS.md) for the condensed
operating rules.
