---
title: "ADR-0005: GitHub Spec Kit adoption for v2 (Option C — ACCEPTED)"
status: accepted
---

# ADR-0005: GitHub Spec Kit adoption for v2 (Option C)

- **Status:** **ACCEPTED 2026-06-05 (Daniel).** Option C ratified: adopt Spec Kit vocabulary + templates + a constitution now; defer the `specify` CLI / `.specify/` / `specs/NNN/` machinery behind a throwaway spike; keep our runtime watchdog, corpus-first grounding, and writer ≠ verifier discipline unchanged. v2-greenfield only — do not retrofit v0.1.0.
- **Date:** 2026-06-04 (drafted) · 2026-06-05 (accepted)
- **Deciders:** Daniel (gated + accepted) — drafted by architect (Kanban worker, card `t_0911c126`)

> **Acceptance note (2026-06-05):** Daniel accepted Option C and authorized implementation. The four open questions are resolved at the bottom of this ADR ("Open questions — RESOLVED"). Implementation tracked on the Kanban board (constitution authoring + vocabulary-mapping doc). The `specify` CLI spike remains **NO-GO** until separately authorized.

> **Renumber note:** the card asked for `0004-spec-kit-adoption-v2.md`, but `0004`
> is already taken by *deterministic-publish-then-pr-releases*. This is `0005`
> (next free number per the `index.md` rule "highest + 1").

> **Scope guard:** this is **v2 prep / brainstorm only**. No build code changed, no
> repo restructured, nothing installed globally. It does **not** touch the open v1
> bug-fix PRs. Output is this doc for Daniel to accept, amend, or reject.

---

## Context

Daniel wants v2 work to integrate **[GitHub Spec Kit](https://github.com/github/spec-kit)**
— the `/specify → /plan → /tasks → /implement` spec-driven workflow (MIT, GitHub, Inc.).
Trailborne already has **two** spec-driven assets in play, so v2 must not run a third
methodology blindly on top of them:

1. **Our home-grown SDD skill** — a 7-stage pipeline (`spec-initializer → spec-shaper →
   spec-writer → spec-verifier → task-list-creator → implementer → implementation-verifier`),
   adapted from `brobertsaz/claude-os` Agent-OS (MIT). It caught the 2026-06-03 four-recipe
   drift on v0.1.0. Its load-bearing extras: corpus-first grounding, re-read the locked spec
   every round, writer ≠ verifier, and the runtime conformance watchdog.
2. **This repo is already spec-first** — `AGENTS.md`, the locked
   [`requirements.md`](../v0.1.0/planning/requirements.md), the `SpecCheck.cs` boot-time drift
   watchdog (see [ADR-0002](0002-spec-first-drift-checked.md)), and `scripts/docs-lint.py`.

The question is **reconciliation**, not replacement. Any Spec Kit adoption must preserve
the invariants those three assets already enforce.

### What I verified (and what I could not)

| Claim | Verified? | Evidence |
|---|---|---|
| Spec Kit's model & layout | ✅ | Pulled `github/spec-kit` README directly. Commands are now namespaced **`/speckit.*`** (constitution, specify, clarify, plan, tasks, implement, + optional analyze/checklist). Layout: `.specify/{memory/constitution.md, scripts/, templates/}` and `specs/NNN-feature/{spec,plan,tasks,research,data-model,quickstart}.md` + `contracts/`. CLI `specify` installs via `uv tool install` / `uvx` / `pipx`. **MIT licensed** (clean-room safe). Has an **extensions / presets / project-local-overrides** customization stack. |
| `hermes kanban specify` / `decompose` relate to Spec Kit | ✅ (they do **not**) | `hermes kanban specify --help` and `decompose --help`: both are **triage-column board verbs** that flesh-out / split *Kanban tasks* via a specifier/decomposer profile. They operate on the SQLite task board, **not** on spec markdown. The shared word "specify" is a **name collision**, not shared wiring. |
| `valheim-regions` uses Spec Kit (the cited precedent) | ❌ **could not verify** | The repo is **not on this machine** (`~/repos/` has `SBPRValheimMods`, `valheim-seed-fix`, `starbright-spaces`, `home-assistant-voice-pe`, `wakewords` — no `valheim-regions`; no `.specify/` dir anywhere under `~`). Trailborne's own docs reference `valheim-regions` only as a **future runtime dependency** (Guardian Stone placement gated on its macro-boundaries — [PARKED](../design/PARKED-2026-06-03.md)), never as an inspectable spec-kit precedent. **I am not guessing at its layout.** A human with access should confirm what convention it actually chose before we treat it as precedent. |

---

## Decision

> Stated as a **recommendation** for Daniel to ratify. Nothing here is auto-accepted.

**Adopt Spec Kit's _vocabulary and templates_ for v2 now; defer its _CLI and `.specify/`
directory machinery_ behind a throwaway spike. Keep our directory tree, our runtime
watchdog, and our writer ≠ verifier discipline unchanged. v2-greenfield only — do not
retrofit v0.1.0.** (This is **Option C** in *Alternatives considered*.)

Concretely, the recommended v2 shape:

1. **Vocabulary as lingua franca.** Speak Spec Kit's stage names (`constitution → specify
   → clarify → plan → tasks → implement → analyze`) as the shared dialect, mapped onto our
   SDD roles (table below). One vocabulary for Daniel + every agent kills the "which
   methodology are we in" ambiguity.
2. **Cherry-pick the markdown templates.** Copy Spec Kit's `spec-template.md`,
   `plan-template.md`, `tasks-template.md`, and the `constitution.md` idea into our existing
   `docs/vX.Y.Z/planning/` tree — adding the frontmatter `status:` field and the README/index
   two-file scaffolding that `docs-lint` requires. They are MIT; this is a clean lift.
3. **Keep the semver `docs/vX.Y.Z/` tree. Do NOT introduce `specs/NNN-feature/`.** That
   layout fights the repo's "semver dirs, never date stamps" rule *and* fails `docs-lint`
   (no README/index, no `status:` frontmatter). If a per-feature sub-structure is wanted,
   use `docs/vX.Y.Z/features/<name>/` and satisfy the two-file rule there.
4. **Write a constitution, but locate it in our tree** (e.g. `docs/design/constitution.md`
   or folded into `AGENTS.md`), not in `.specify/memory/`. It should encode the invariants
   that are *already* load-bearing: clean-room, spec-first (spec+code same PR), corpus-first,
   Daniel-gates-every-merge.
5. **Keep our load-bearing extras that vanilla Spec Kit lacks** (see *Consequences*): the
   `SpecCheck.cs` runtime watchdog, corpus-first verification, **writer ≠ verifier** as
   *separate agents*, and milestone (M0→Mₙ) delivery.
6. **Do NOT install the `specify` CLI globally in v2 work yet.** If we later want the CLI +
   `.specify/` machinery, spike it in a throwaway dir first and prove a **preset/override**
   can carry our watchdog + corpus-first + cross-agent-verifier discipline without fighting
   `docs-lint`. If adopted, capture the tool in chezmoi per `AGENTS.md` RULE 1.
7. **Migration: v2-greenfield only.** Do **not** retrofit v0.1.0 docs — they are locked and
   `SpecCheck`-bound; rewriting them buys nothing and risks pure drift.

### Stage mapping — Spec Kit ↔ our 7-stage SDD

| Spec Kit command | Our SDD stage(s) | Fit |
|---|---|---|
| `/speckit.constitution` | *(none — closest: `AGENTS.md` + ADRs + clean-room rules)* | **New & useful.** Gives a machine-referenced home for principles we currently spread across AGENTS.md/ADRs. |
| `/speckit.specify` | `spec-initializer` + `spec-shaper` + `spec-writer` | Spec Kit **collapses our three write-stages into one command**. Lower ceremony; also less separation. |
| `/speckit.clarify` | `spec-shaper` (iterative 1–3 Q&A per round) | Direct overlap — this is exactly our shaper loop. |
| `/speckit.plan` | *(architect role / `feature-slice-plan.md`)* | Partial. Our SDD has no distinct "plan" stage; Spec Kit's `plan.md` (+ `research.md`, `data-model.md`, `contracts/`) is richer here. |
| `/speckit.tasks` | `task-list-creator` | Direct overlap. |
| `/speckit.implement` | `implementer` | Direct overlap. |
| `/speckit.analyze` | `spec-verifier` + `implementation-verifier` | **Overlap with a gap:** Spec Kit's analyze is **same-agent self-check**; our verifiers are a **different agent** (often a stronger model). We keep cross-agent separation. |
| `/speckit.checklist` | `spec-verifier` (acceptance checklist) | Partial overlap. |

### Overlap and tension (the actual conflicts)

1. **Directory convention — three-way conflict.** Repo uses **semver** (`docs/v0.1.0/`,
   `docs/v1/`; "never date stamps"). Our SDD skill defaults to **date** (`specs/YYYY-MM-DD-name/`).
   Spec Kit wants **sequential** (`specs/NNN-feature/`). They cannot all win. → **Repo semver
   convention wins** (it is enforced by `docs-lint` + `docs/index.md`); both spec methodologies
   adapt to it.
2. **`docs-lint` vs Spec Kit's `specs/` layout.** Spec Kit's `specs/NNN/` dirs ship **no
   README.md/index.md and no `status:` frontmatter** → they would **fail `docs-lint`'s
   two-file + status checks** if dropped under `docs/`. This is the single most concrete
   blocker to a raw `specify init` adoption.
3. **Branch model.** Spec Kit auto-creates a per-feature branch (`001-create-taskify`). We use
   PR-gated `pr-X` / `fix/<slug>-t_<taskid>` branches with **Daniel gating every merge**
   ([ADR-0004](0004-deterministic-publish-then-pr-releases.md) ordering matters). Spec Kit's
   automation must not auto-merge.
4. **CLI name collision.** `specify` (Spec Kit, `uvx`/`pipx`) vs `hermes kanban specify`
   (board triage verb). Different tools, different invocation, zero functional overlap — but a
   real **human-confusion** hazard. Document the distinction; never alias them.
5. **Two homes for "governing principles."** Spec Kit's `constitution.md` overlaps `AGENTS.md`
   + ADRs. Pick **one** source of truth (recommend: constitution *references* the ADRs, doesn't
   duplicate them).
6. **Install hygiene.** `uv tool install specify-cli` is a persistent/global-ish install →
   `AGENTS.md` RULE 1 (capture in chezmoi). For *this* brainstorm run it is explicitly **out of
   scope** (no global installs).

---

## Consequences

**What survives unchanged (load-bearing — do NOT let a Spec Kit migration quietly drop these):**

- **`SpecCheck.cs` runtime conformance watchdog** — Spec Kit has **no equivalent**. It is a
  code-resident locked manifest diffed at boot ([ADR-0002](0002-spec-first-drift-checked.md));
  it caught the 4-recipe drift *and* a silent null-resource bug on first run. Keep it. Spec Kit
  validates artifacts at author-time; we additionally validate **at runtime on every boot**.
- **Corpus-first verification** — grep `~/valheim/sbpr-corpus/wiki/fandom/` before asserting any
  vanilla Valheim fact or name. Not in Spec Kit. Keep it.
- **Writer ≠ verifier as _separate agents_** — Spec Kit's `/speckit.analyze` is a same-agent
  self-check. Our pipeline keeps a distinct (often stronger) verifier. Keep it.
- **Milestone (M0→Mₙ) delivery** with named acceptance tests (`AGENTS.md`: "incremental
  delivery"). Spec Kit's `/tasks` is a flat list; our milestone ladder is the shippable-build
  cadence. Keep it.
- **Semver `docs/vX.Y.Z/` tree + `docs-lint` two-file rule + `status:` frontmatter + ADRs.**
- **Spec-first invariant** (spec + code in the same PR) and **Daniel gates every merge.**
- **Clean-room** (no Jotunn / decompiled IronGate source — [ADR-0001](0001-clean-room-no-jotunn.md)).

**What becomes easier:**

- One shared **vocabulary** for humans and agents (constitution/specify/clarify/plan/tasks/implement).
- Spec Kit's `plan.md` / `research.md` / `contracts/` templates give us a richer **plan** stage
  than our SDD currently has.
- A `constitution.md` gives principles a single referenceable home.

**What becomes harder / constrained:**

- We must **maintain the mapping** between Spec Kit vocabulary and our SDD roles + watchdog —
  the methodologies do not align 1:1, and the gaps (cross-agent verify, runtime watchdog,
  corpus-first) are exactly the parts that have saved us.
- If we ever adopt the CLI, every `specs/NNN/` artifact needs a **preset/override** to satisfy
  `docs-lint`, or it lives outside `docs/`.

**Load-bearing line:** *Do not `specify init` this repo and migrate to `specs/NNN-feature/`
without first proving (via spike) that a Spec Kit preset can carry the `SpecCheck` watchdog,
corpus-first grounding, and cross-agent verification, and pass `docs-lint`.* Dropping any of
those to fit vanilla Spec Kit re-opens the exact drift class ADR-0002 closed.

---

## Alternatives considered

- **Option A — Adopt Spec Kit raw (`specify init`, migrate to `specs/NNN-feature/`,
  exempt that subtree from `docs-lint`).** Rejected for v2-now. Highest drift: fights the
  semver tree, breaks `docs-lint`, orphans the watchdog, and would tempt a retro of locked
  v0.1.0 docs. Re-evaluate only if a spike proves the preset path.
- **Option B — Cherry-pick templates only; ignore vocabulary + CLI.** Viable and low-risk, but
  leaves humans/agents without the shared stage language that is half of Spec Kit's value.
  Folded into Option C.
- **Option C — Vocabulary + templates now; CLI/`.specify/` deferred behind a spike;
  watchdog/corpus/verifier kept as our "preset."** **Recommended.** Captures Spec Kit's
  clarity benefits at near-zero drift, keeps every invariant, and leaves a clean upgrade path
  to the full CLI if a spike earns it.
- **Option D — Reject Spec Kit; keep SDD as-is.** The no-go baseline. Loses the shared
  vocabulary and the richer plan templates for no gain beyond "no change."

---

## Open questions for Daniel (gate) — RESOLVED 2026-06-05 (Daniel: accept + implement)

1. **`valheim-regions` precedent:** **Won't-block.** Repo isn't on this machine and isn't a v2 dependency yet; we are NOT adopting the `specs/NNN/` layout regardless (Option C keeps the semver tree), so its convention doesn't gate anything. Revisit only if/when an Option-A spike is ever authorized.
2. **Constitution home:** **Standalone `docs/design/constitution.md`** that *references* the ADRs (does not duplicate them). Keeps `AGENTS.md` lean and gives principles one machine-referenceable home.
3. **Spike the CLI?** **NO-GO for now** (deferred). No `specify init`, no global install this cycle. A throwaway `/tmp` spike must be separately authorized later and must prove a preset can carry the `SpecCheck` watchdog + corpus-first + cross-agent verify AND pass `docs-lint` before any real adoption.
4. **Vocabulary commitment:** **ADOPT `/speckit.*` stage names** (constitution → specify → clarify → plan → tasks → implement → analyze) as the team dialect for v2, mapped onto our SDD roles via the table above. One shared vocabulary for Daniel + every agent.

**Final go / no-go (ratified):**
- ✅ **GO** — vocabulary + templates + constitution.
- ⛔ **NO-GO / deferred** — `specify` CLI + `.specify/` + `specs/NNN/` layout, pending a future authorized spike.
