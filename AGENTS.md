# AGENTS.md — SBPR Trailborne

Operating rules for AI coding agents (Kanban workers, Codex/Claude/Copilot, etc.)
working in this repo. Read this first. Full detail in
[`CONTRIBUTING.md`](CONTRIBUTING.md).

## The one rule that's violated most

**Spec and code change together, in the same commit/PR.** This repo is
spec-first. If you change a recipe, piece, station, item, or mechanic, you MUST
also update the spec/docs. Code that diverges from the spec is a bug.

- **Locked spec:** `docs/v0.1.0/planning/requirements.md`
- **Earlier locked design (wins where requirements is silent):**
  `docs/design/PARKED-2026-06-03.md`
- **Drift manifest (checked at server boot):**
  `src/SBPR.Trailborne/Runtime/SpecCheck.cs` — keep its recipe count in sync.
- **Dataset doc:** `docs/datasets/PIECES_AND_CRAFTABLES.md`

"Done" = code **and** spec **and** SpecCheck manifest all consistent. If you
collapse/add/remove pieces or recipes, all three move in the same PR.

## Read before you write

Read EVERY `*.md` relevant to your feature before proposing changes — not just
the code. The spec records multiple rounds of rework caused by skipping this.
If spec and code disagree, **the spec wins** unless Daniel explicitly overrides.

## Hard constraints

- **Clean-room = a firewall around OTHER developers' mod code, NOT around the base
  game.** **Vanilla Valheim is fair game: you MAY read AND adapt its decompiled
  source** to write our implementation — reading/adapting the game we're modding is
  normal and allowed, not a clean-room violation. **Other mods (Jotunn, etc.) are
  different:** do NOT copy their code directly, but you MAY still *reproduce* their
  functionality through a proper **clean-room RE process** — a `reviewer-cleanroom`
  reads the original and writes a behavioral *description*, then a separate
  implementer who never saw that source reproduces it from the description (a
  Chinese wall). You may also just *ask questions* about another mod to learn
  *where* to investigate vanilla yourself. The hard limits: (a) no direct copying
  of other mods' code (use the RE wall instead), and (b) don't *commit* copyrighted
  files (game binaries, decompiled IronGate source, other mods' source) into this
  MIT repo. Verify vanilla names against `assembly_valheim.dll` metadata when in
  doubt. See ADR-0001.
- **Additive construction — NO runtime prefab cloning (ADR-0006).** Build content
  prefabs from `new GameObject()` + `AddComponent` of only the components you
  intend. Do NOT `Instantiate` a vanilla/ZNetView-bearing prefab as a mutable base
  and then strip the parts you don't want — that subtractive pattern caused every
  major bug to date (the v0.2.7 ZDO-orphan crash, the cairn-as-bonfire fire leak).
  You MAY read vanilla prefabs as *blueprints* (shared mesh/material/EffectList/
  field values via `ZNetScene.GetPrefab`, which fires no Awake) — reading an asset
  is not cloning. Use `vprefab inspect <name>` to read the blueprint first. See
  `docs/decisions/0006-additive-prefab-construction.md`.
- **net48 / BepInEx / HarmonyX.** Build:
  `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` →
  0 errors, **0 warnings (clean build)**. `<TreatWarningsAsErrors>` is ON, so
  any new nullable (or other) warning fails the build — keep it clean.
- **Incremental delivery.** Milestone-based with named acceptance tests. Don't
  jump milestones.

## Honesty rules (load-bearing)

- **"Logs green ≠ playable."** Server-side registration succeeding does NOT prove
  a joined client can craft/build it. State which you actually verified.
- Don't claim success you didn't check. If you're unsure a step ran, say so and
  verify. Distinguish "built + compiles" from "deployed" from "tested in-game."

## Build references (CI has no Valheim)

The build needs Valheim managed assemblies. Locally: `scripts/setup.sh` +
`scripts/fetch-sdk.sh`. In CI: the free dedicated server (Steam app 896660,
anonymous) supplies them — see `.github/workflows/`.

## Kanban workers

When filing or closing a bug card, if the fix changes behavior, **explicitly
note that the spec/docs must be updated too** (per the rule above). A card isn't
done when the code works — it's done when code and spec agree.

## QA live-harness process discipline (M6)

- **GABS zombie liveness: FIXED upstream in our fork (2026-07-29). Do not design around
  it.** The historical bug: a client that exited left a `<defunct>` `valheim.x86_64`
  parented to the long-lived GABS daemon, and GABS's name-based `ps` finder matched the
  zombie's `comm`, so `games.status` reported "running" forever and the next
  `games.start` was a silent no-op. That is what made run 8's re-rolls silent.

  Two commits in `PolyphonyRequiem/GABS` fix it at the source — it was never actually
  unfixable, only unfixable *from this repo*:
  - `b679943` reaps the child on every kill/terminate exit path (the children GABS
    itself stops).
  - `79e1779` adds `state=` to the finder's `ps` format and skips any process in state
    `Z` (whatever zombies remain — a client that crashes, quits in-game, or is killed
    externally is reaped by no terminate path, so the *lookup* must also tell the truth).

  Verified in production on 2026-07-29: 10 `<defunct>` `valheim.x86_64` parented to the
  primary daemon, 0 live clients, and `games_status` correctly reported **stopped**.
  Both daemons (uid 1000 on :8080, uid 1001 on :8081) already run the fixed binary.

  **Consequence for the runner:** `_reset_gabs_state` in
  `qa/runner/runner_core/live_composition.py` is now **defence in depth, not
  load-bearing**. Keep it — it is cheap, idempotent, and correctly gated on there being
  zero live non-zombie `valheim.x86_64` (a zombie exposes no readable
  `/proc/<pid>/exe`), so it can never touch Daniel's own Steam Valheim. But do NOT build
  new workarounds on the premise that GABS lies about liveness, and if you observe it
  lying again, that is a regression in the fork to fix there — not a fact to route
  around here.
- **There is one GABS daemon PER UID, and that is the correct topology.** uid 1000 runs
  `gabs server --http localhost:8080`; uid 1001 (`valbot`) runs its own on `:8081` with
  its own `~/.gabs/config.json`. A daemon must run as the identity whose game it
  launches, because the child inherits that uid and its Steam session. Do not try to
  drive both clients from one daemon.

  Note that valbot's GABS is `launchMode: DirectPath` — a thin shim, not a second
  launcher. It execs a controller chain ending in `request_valbot_app_launch 892970`,
  and **Steam** performs the spawn, because Steam is what supplies the second identity's
  licence and session. The controller's `prespawn_identity_gate` refuses to launch if
  valbot's Steam resolves to the primary SteamID. "Launch as the other Steam identity"
  *is* the AppID request, so GABS cannot replace it.
- **Whoever launches a verification/proof client MUST reap it.** Do not leave a launched
  `valheim.x86_64` un-reaped when your card completes — confirm the process is fully gone
  (not merely signalled), e.g. `games.stop` the gameId your throwaway daemon owns, or
  `pkill -9 -f <your-child-binary>` then verify no child remains under your daemon. This
  is still required after the fix above: the finder now tells the truth about zombies, but
  a leaked zombie is still a leaked resource and still confuses a human reading `ps`.
- **QA workers use isolated `git worktree` checkouts, not the shared clone.** Concurrent
  workers on the single `~/repos/SBPRValheimMods` checkout is a corruption hazard (a
  concurrent worker moved HEAD mid-run in run 8). `git worktree add` a per-task tree off
  the resolved `origin/main` and work there.
