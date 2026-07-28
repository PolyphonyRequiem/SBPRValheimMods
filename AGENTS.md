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

- **GABS never reaps the game processes it forks.** A client that exits leaves a
  `<defunct>` `valheim.x86_64` zombie parented to the long-lived GABS daemon. GABS's
  single-gameId liveness model counts that zombie as "running" (its name-based `ps`
  finder still matches the zombie's `comm` — `GABS/internal/process/controller.go:296-302`),
  so `games.status` reports "running" and the next `games.start` is a silent no-op
  (`GABS/internal/mcp/stdio_server.go:761-764`). This is a GABS-side bug we cannot fix
  from our repo; the **permanent** runner-side workaround is to force GABS's view to
  match reality before every launch — `_reset_gabs_state` in
  `qa/runner/runner_core/live_composition.py` issues `games.stop` (which, as the child's
  parent, actually `Wait()`s and reaps the zombie) and verifies the state cleared. It is
  gated on there being **zero live non-zombie** `valheim.x86_64` (a zombie exposes no
  readable `/proc/<pid>/exe`), so it can never touch Daniel's own Steam Valheim.
- **Whoever launches a verification/proof client MUST reap it.** Do not leave a launched
  `valheim.x86_64` un-reaped when your card completes — confirm the process is fully gone
  (not merely signalled), e.g. `games.stop` the gameId your throwaway daemon owns, or
  `pkill -9 -f <your-child-binary>` then verify no child remains under your daemon. An
  un-reaped proof client is the exact zombie that made run 8's re-rolls silent no-ops.
- **QA workers use isolated `git worktree` checkouts, not the shared clone.** Concurrent
  workers on the single `~/repos/SBPRValheimMods` checkout is a corruption hazard (a
  concurrent worker moved HEAD mid-run in run 8). `git worktree add` a per-task tree off
  the resolved `origin/main` and work there.
