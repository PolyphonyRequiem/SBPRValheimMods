---
title: "ADR-0007: Docs evolution strategy — conflict-free manifests, folder boundaries, freshness"
status: proposed
last_reviewed: 2026-06-17
---

# ADR-0007: Docs evolution strategy

- **Status:** proposed
- **Date:** 2026-06-17
- **Deciders:** Daniel (gate) + architect (this proposal)
- **Supersedes:** nothing. Extends the conventions in `docs/README.md`,
  `docs/index.md`, and the `sbpr-docs-conventions` skill.

## Context

The docs tree is 72 `.md` files across `docs/{datasets, decisions, design,
investigations, v0.1.0, v1, v2, v3}`. It works, and `scripts/docs-lint.py`
mechanically enforces the two-file rule, a `status:` vocabulary, and link
integrity (76 docs, green). But three growing pains are now measurable, and
they will compound as more content tiers land:

1. **🔴 The per-folder `index.md` + `README.md` manifests collide on every
   parallel docs PR.** In the 2026-06-17 burst, `docs/v3/planning/{index,README}.md`
   had to be hand-unioned **four times** (trail-lights #165, twisted-portal #166,
   sunstone-lens, iron-compass #170). This is structural, not bad luck: every
   spec PR appends a row to the *same region of the same shared file*, so any two
   concurrent docs PRs touching one tier conflict by construction. The cost scales
   with parallelism — exactly the wrong direction for a kanban-swarm repo.

2. **Fuzzy folder boundaries.** `design/` (17), `decisions/` (9 ADRs), and
   `investigations/` (7) have overlapping-sounding charters. A new doc's correct
   home is not obvious from the folder names alone, so docs drift into the wrong
   folder and the tree's legibility erodes.

3. **Freshness is guessed, not detectable.** `status:` tells you a doc's
   lifecycle tier but not *when it was last confirmed true*. A doc whose body was
   edited without bumping its date reads as fresh when it isn't — already observed:
   `docs/datasets/PIECES_AND_CRAFTABLES.md` carries `last_updated: 2026-06-03`
   but its body was edited as recently as 2026-06-17 (it grew the v2 cartography
   and v3 sunstone rows). There is no mechanical staleness signal.

A fourth question is explicitly on the table: should the parallel `v0.1.0/`,
`v1/`, `v2/`, `v3/` trees **collapse** into one living spec? They look like
sprawl. But they are load-bearing in ways that are easy to miss (see Decision §1).

This ADR exists because the docs structure is the kind of thing an agent could
"tidy up" in the wrong direction — collapsing the version trees would break 17+
source-comment references and lose history while not even fixing the actual pain.
The constraint needs a record.

## Decision

Four decisions, smallest-blast-radius first. The throughline: **fix the measured
pain at the manifest layer; do not restructure the tree.**

### 1. Do NOT collapse the version trees. Keep `v0.1.0/`, `v1/`, `v2/`, `v3/`.

The version dirs *look* like sprawl but the sprawl is cosmetic and the trees are
load-bearing:

- **17+ source files hard-reference `docs/v2/planning/*` and `docs/v3/planning/*`**
  by exact path (`SpecCheck.cs`, `Portals.cs`, `MarkerSigns.cs`, `SunstoneLens.cs`,
  the icon-gen scripts, …). Collapsing the trees breaks every one of those pointers
  in lockstep and discards the git history that ties a spec to the commit that
  shipped it.
- **`docs/v0.1.0/planning/requirements.md` is the locked-spec anchor** named by
  exact path in `AGENTS.md`, `CONTRIBUTING.md` (×3), and `SpecCheck.cs`. It must
  not move.
- **The version dir is not the cause of the collision** — the shared *manifest
  files inside it* are (Decision §2 fixes that without touching the tree).

The version dirs *do* conflate three different axes, and that is worth naming so
nobody "fixes" it by moving files:

| dir | what it actually is | keep as-is because |
|---|---|---|
| `v0.1.0/` | a **semver release** (Meadows tier) | locked-spec anchor, hard-referenced |
| `v1/` | an **impl-line refactor** (vertical-slice `Features/`) | shipped; history anchor |
| `v2/`, `v3/` | **biome content tiers** (Black Forest, Swamp) | 17+ source refs; per-tier specs |

**Convention going forward (the one real change):** new content tiers continue as
`docs/v<N>/planning/` semver siblings. We do **not** create new *frozen* parallel
scaffolds that duplicate orientation prose; the conflict-free manifest pattern
(§2) means a new tier is just a folder of spec files plus a thin stable README and
a *generated* index. The "living spec with status-per-feature" the collapse
question imagined is delivered by §2+§4 (generated index carrying each spec's
`status:`), **without** the migration cost.

**If a future collapse is ever justified** (e.g. tiers proliferate past
readability), the migration path that preserves history is: `git mv` each file
(true rename, history intact), update the 17+ source references in the *same*
commit (spec-and-code-together, ADR-0002), and leave `v0.1.0/planning/requirements.md`
in place as the permanent locked-spec anchor. That is a deliberate, gated
migration — not a tidy-up. This ADR's position is: **not now, and not without a
new ADR.**

### 2. Conflict-free manifest pattern: generate `index.md`, de-enumerate `README.md`.

The collision has two halves and both get fixed:

**(a) `index.md` becomes a generated artifact.** A new `scripts/docs-index.py`
walks `docs/`, reads each file's frontmatter (`title`/`status`/`purpose`), and
emits each folder's `index.md` from a deterministic template. **Content PRs never
hand-edit `index.md`.** Two parallel spec PRs each add only their own content
file — different filenames, zero shared-file edits, **zero collision**. The
manifest is regenerated deterministically from the union of files that exist on
`main`. Generation is wired into CI (`docs.yml`): regenerate, and **fail the check
if the committed `index.md` differs** from the generated output (so it can never
silently drift), the same shape as a `gofmt -l` / `black --check` gate.

**(b) `README.md` stops enumerating files.** The READMEs collide because they
duplicate the manifest into hand-maintained "What's here" bullet lists that every
spec PR appends to — which violates the convention's own README-vs-index split
(README = stable narrative, index = manifest). Going forward a folder README is
**stable tier-orientation prose only**: what this tier is, why it exists, the 2–3
anchor docs to read first. The exhaustive per-file list lives **only** in the
generated `index.md`. A stable README is edited rarely and meaningfully, so its
residual conflict rate drops to near zero.

Net effect: a docs spec PR touches exactly one new file (its spec) plus, rarely,
a hand-written README pointer. The append-collision is designed out, not merged
around.

### 3. One-line "what goes where" rule for the three durable folders.

Resolve a new doc's home by asking, in order:

1. **Is it a settled call that must not be silently reversed?** → `decisions/`
   (an ADR — *why a constraint exists*, immutable, append-only).
2. **Is it a dated post-mortem of a specific failure?** → `investigations/`
   (*what actually happened when X broke* — evidence trail, `YYYY-MM-DD-*.md`).
3. **Is it design intent — how a feature should work and why it's shaped that
   way?** → `design/` (*durable narrative intent*, living, pre-spec).
4. **Is it a build-ready spec or planning artifact for a specific version?** →
   `docs/v<N>/planning/` (the *what-to-build* for that tier).

Mnemonic: **decisions = why-locked · investigations = what-broke ·
design = what-we-intend · v«N»/planning = what-to-build-now.**

### 4. Freshness convention: `last_reviewed:` + a documented status vocabulary.

- **Add `last_reviewed: YYYY-MM-DD`** to every content doc (the date a human or
  agent last confirmed the doc still reflects reality — distinct from
  `last_updated`, which is when the body last changed). Scaffolding
  (`README.md`/`index.md`/`TEMPLATE.md`) stays exempt, as with `status:`.
- **Mechanical staleness signal:** a git-aware check (the archivist remit, §
  Archivist below, or a `--freshness` mode on the lint) flags any doc whose
  `last_reviewed` is **older than its last git-commit date** — that catches the
  PIECES_AND_CRAFTABLES "body edited, date not bumped" class mechanically. A
  soft threshold (living/current docs not reviewed in >30 days) surfaces a
  review queue, but **never auto-cuts** — deletion is always Daniel-gated.
- **Status vocabulary, documented (no new values; current set stays):**

  | status | axis | meaning |
  |---|---|---|
  | `idea` | content | named, pre-spec; no mechanics locked, no impl card |
  | `proposed` | content / ADR | drafted, awaiting Daniel's ratify |
  | `current` | content | reflects shipped reality for its version |
  | `living` | content | continuously revised intent, no single "done" |
  | `historical` | content | frozen point-in-time record; intentionally not updated |
  | `superseded` | content / ADR | replaced; points at its successor |
  | `accepted` | ADR | ratified, append-only |
  | `template` | scaffolding | a copy-me skeleton |

## Consequences

- **Easier:** parallel docs PRs stop colliding — the #1 measured pain is designed
  out. New-doc placement becomes a 4-question decision. Stale docs become
  *detectable* instead of guessed.
- **Harder / constrained:** `index.md` is now generated — **you must not
  hand-edit it** (CI rejects drift); edit frontmatter and let the generator run.
  READMEs must stay narrative — **do not** re-add per-file enumeration (that
  re-introduces the collision). Every content doc now carries `last_reviewed`.
- **Spec-first preserved (ADR-0002):** none of this weakens "spec and code change
  together." Specs still live in `v<N>/planning/`; the generated index *reads*
  their frontmatter, it does not author content. The locked-spec anchor does not
  move.
- **Load-bearing line:** *do not collapse the version trees, and do not re-add
  file enumeration to READMEs, without a new ADR.* Both would re-break things this
  decision deliberately fixed.
- **Sequencing:** this ADR is the **decision**. Its machinery (the generator
  script, the lint additions, the README de-enumeration, the `last_reviewed`
  backfill) is execution that lands in a **separate consistency PR** *after*
  Daniel ratifies this ADR — so the schema is locked before anything normalizes
  to it.

## Alternatives considered

- **Collapse `v1/v2/v3` into one living spec with a CHANGELOG.** Rejected: breaks
  17+ source references, discards spec↔commit history, and *doesn't even fix the
  collision* unless you also fix the manifest pattern — so it's all cost, little
  benefit. The "living, status-per-feature" benefit it promised is delivered by
  the generated index (§2+§4) at a fraction of the blast radius.
- **Append-only per-feature manifest fragments** (`_index/<feature>.md`, concatenated).
  Conflict-free, but more files and more machinery than a generator that already
  has every file's frontmatter to read. Heavier than the pain warrants.
- **Drop `index.md` entirely; let `docs-lint` enumerate on demand.** Loses the
  committed, greppable, browseable manifest (humans and `git grep` both read
  `index.md` today). Generating-and-committing keeps that affordance; dropping it
  trades a real convenience for a marginal simplification.
- **A `docs-bot` that auto-commits the regenerated index after each merge.**
  Works, but adds a write-capable bot and a second commit per PR. The CI
  *check-and-fail-on-drift* gate gets the same guarantee with no bot — the author
  runs `scripts/docs-index.py` locally and commits the result, exactly like a
  formatter.

---

## Addendum — `archivist` profile recommendation

Daniel floated a standing `archivist` profile to own freshness review, staleness
flagging, consistency enforcement, and manifest coherence. **Recommendation:
worth it, but stage it — start as a skill set on an existing profile + a cron,
not a new profile. Promote to a profile only if the cron remit outgrows it.**

Reasoning (anti-cathedral):

- The *mechanical* half of the archivist's job is a script: regenerate the index
  (§2a), run the git-aware freshness check (§4), run `docs-lint`. That wants to be
  **`scripts/docs-index.py` + a `--freshness` lint mode + a weekly cron**, not a
  reasoning agent. Most of the value needs zero LLM.
- The *judgement* half — "is this investigation still worth keeping, or has its
  fix shipped and it's now noise?" — is real agent work, but it's **periodic and
  low-volume** (a weekly/biweekly pass over a 72-file tree). That does not justify
  a standing profile with its own system prompt and identity; it justifies a
  **`docs-freshness-review` skill** loaded by a scheduled run.
- The spec-first rule (AGENTS.md) constrains the archivist hard: it may **flag**
  staleness and **propose** cuts, but it **never deletes** (Daniel gates every
  deletion) and it **never edits a spec's substance** (specs change only with
  their code). Its safe write surface is: regenerate the index, bump
  `last_reviewed` on docs it has re-confirmed, open a kill-list comment/PR for
  Daniel. That remit fits a skill-guided cron cleanly.

**Concrete proposal (lightest thing that solves the measured pain):**

1. `scripts/docs-index.py` — the generator (§2a). *(consistency PR)*
2. `docs-lint.py --freshness` — the git-aware staleness check (§4). *(consistency PR)*
3. A **`docs-freshness-review` skill** — the periodic-pass playbook: run the two
   scripts, walk anything flagged, draft a kill-list comment, never delete.
4. A **`doc-placement` skill** — encodes the §3 "what goes where" rule for any
   worker creating a doc (cheap, high-leverage, prevents drift at the source).
5. A **weekly cron** running the freshness review on whichever profile owns docs
   hygiene (`architect` already does spec+docs work and is the natural holder).

**Promote to a standing `archivist` profile only if** the cron remit grows beyond
docs (e.g. it starts owning corpus freshness, wiki sync, dataset reconciliation)
— i.e. when there's a *cathedral's worth of ground built*. Until then a profile is
heavier than the pain. If Daniel green-lights, the follow-up work is a child card
of this task (skills + cron), **not** an auto-created profile.
