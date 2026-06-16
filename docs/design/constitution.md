---
title: "Trailborne Constitution — governing principles for spec-driven v2"
status: living
purpose: The single machine-referenceable home for Trailborne's load-bearing principles, stated in the Spec Kit vocabulary adopted by ADR-0005. References the ADRs; does not duplicate them.
---

# Trailborne Constitution

> Adopted by [ADR-0005](../decisions/0005-spec-kit-adoption-v2.md) (Option C, accepted 2026-06-05).
> This document is the **constitution** in the Spec Kit sense: the principles every
> `specify → clarify → plan → tasks → implement → analyze` cycle must uphold. It
> **references** the ADRs and `AGENTS.md` as the source of truth — it does not restate them.
> When this file and an ADR disagree, the **ADR wins** and this file is corrected.

## The shared vocabulary (ADR-0005 §Stage mapping)

For v2, humans and agents speak one dialect — the Spec Kit stage names — mapped onto our
existing SDD roles. Use these words in cards, PRs, and discussion:

| Stage | Means | Our role(s) |
|---|---|---|
| **constitution** | the principles below | this doc + ADRs + `AGENTS.md` |
| **specify** | write the spec | `spec-initializer`/`shaper`/`writer` |
| **clarify** | 1–3 Q&A per round to remove ambiguity | `spec-shaper` loop |
| **plan** | technical plan (research / data-model / contracts) | architect role |
| **tasks** | decompose into kanban cards | `task-list-creator` |
| **implement** | build to the spec | `implementer` (kanban worker) |
| **analyze** | verify spec ↔ code ↔ build | `spec-verifier` + `implementation-verifier` (**different agent**) |

## Articles (the load-bearing invariants)

1. **Spec-first.** Spec and code change in the **same PR**. Code that diverges from the locked
   spec is a bug. ([ADR-0002](../decisions/0002-spec-first-drift-checked.md), `AGENTS.md`.)
2. **Runtime conformance.** The `SpecCheck.cs` boot-time watchdog holds a locked manifest and
   diffs every registered recipe/buildable at server start. It is **not optional** and has no
   Spec Kit equivalent — it caught the 4-recipe drift + a null-resource bug. Keep its count in sync.
3. **Corpus-first.** Grep `~/valheim/sbpr-corpus/wiki/fandom/` before asserting any vanilla
   Valheim fact, name, or prefab. Never guess a vanilla identifier.
4. **Clean-room = a firewall around OTHER developers' mod code, not vanilla.**
   Reading *and adapting* Valheim's own decompiled source to write our impl is fair
   game (it's the game we're modding). For **other mods** (Jotunn, etc.): no direct
   copying, but you MAY *reproduce* their functionality through a clean-room RE
   process — a `reviewer-cleanroom` reads the original and writes a behavioral
   description, a separate implementer reproduces it from that description without
   ever seeing the source (Chinese wall); or simply ask questions to learn where to
   investigate vanilla ourselves. Never *commit* copyrighted files (game binaries,
   decompiled IronGate source, other mods' source) into the MIT repo. Verify vanilla
   names against `assembly_valheim.dll` metadata when uncertain.
   ([ADR-0001](../decisions/0001-clean-room-no-jotunn.md).)
5. **Writer ≠ verifier.** The agent that verifies a spec or an implementation is a **different
   agent** (often a stronger model) from the one that wrote it. Spec Kit's same-agent `analyze`
   does not satisfy this.
6. **Daniel gates every merge.** Open a PR; never self-merge feature work. Spec Kit automation
   must never auto-merge. ([ADR-0004](../decisions/0004-deterministic-publish-then-pr-releases.md).)
7. **Incremental delivery.** Milestone (M0→Mₙ) ladder with **named acceptance tests**. Don't jump
   milestones. "Logs green ≠ playable" — distinguish built / deployed / playtested.
8. **Semver docs tree.** `docs/vX.Y.Z/`, never date-stamped dirs. Every `docs/` subfolder keeps
   its README.md + index.md and content docs carry a `status:` frontmatter (`docs-lint` enforces).

## Boundaries adopted by ADR-0005

- **GO:** Spec Kit **vocabulary** (above), its markdown **templates** (cherry-picked into our
  semver tree with our frontmatter), and this **constitution**.
- **NO-GO (deferred):** the `specify` **CLI**, the `.specify/` directory, and the
  `specs/NNN-feature/` layout — until a throwaway spike proves a preset can carry Articles 2–5
  **and** pass `docs-lint`. Do not `specify init` this repo before then.
- **Scope:** v2-greenfield only. Do **not** retrofit the locked, `SpecCheck`-bound v0.1.0 docs.

## How a v2 feature flows

`constitution` (this doc, always on) → **specify** a `docs/vX.Y.Z/` spec (status frontmatter) →
**clarify** with Daniel (1–3 Qs) → **plan** the patch surface (corpus-first, clean-room) →
**tasks** as kanban cards that *cite the spec section* and carry named accept-tests →
**implement** to the spec → **analyze** with a separate verifier + `SpecCheck` + `docs-lint`.
Daniel gates the merge. This is exactly the spec→card pipeline already dogfooded on the
Painted Sign and Cairn-visual specs.
