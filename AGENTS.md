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

- **Clean-room.** Do NOT copy Jotunn or any other mod-loader's code. Vanilla
  public API names only (verify against `assembly_valheim.dll` metadata). Nothing
  copyrighted is committed.
- **net48 / BepInEx / HarmonyX.** Build:
  `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` →
  0 errors, ~29 known nullable warnings (add none).
- **Daniel gates every merge.** Open a PR; never self-merge feature work.
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
