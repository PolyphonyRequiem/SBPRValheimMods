---
status: current
---

# T031 — Warrior Weapon Discipline implementation evidence

Author: `engineer-systems` (implementer). This is the node's own implementation
evidence (DoD items 1–8, 10 pre-merge). The independent joined-client in-world
skill-cap capture (DoD item 9) and the Tracer-8 gate verdict are QA / T032
(non-author) and remain gated on client availability.

Acceptance owned here: `AT-WEAPON-DISCIPLINE-CHOICE`, `AT-WEAPON-CAP-LIFECYCLE`.

## What Weapon Discipline is (spec-grounded)

Weapon Discipline is the Warrior Tree, Level-1 **personal Permanent Effect**
(`WeaponDiscipline@1`; data-model.md roster). Per spec §"Warrior" it "grants one
permanent, idempotent choice among at least two authored melee skill-cap tiers".
Per contracts.md §`ChooseWeaponDisciplineSkill` the command "Commits one permanent
choice and one cap-provider provenance record. It cannot be spent twice and cannot
raise every melee cap." Per contracts.md §Warrior the `SkillCapProvider` "supplies
the one selected authored cap tier, highest-wins."

It registers **no** SBPR recipe or buildable, so the `SpecCheck.cs` recipe manifest
is unchanged.

## The vanilla seam (decomp — vanilla is fair game, AGENTS.md / ADR-0001)

A skill's hard ceiling is the vanilla `Skills` ceiling of **100**
(`Skills.m_skillCeiling`, decomp `assembly_valheim`); skill gain, the level
display, and death loss all clamp to that ceiling on multiple use/display/death
paths (research.md §"Skill caps"). A per-skill cap raise therefore composes against
that baseline and is itself clamped to it — the "values ≤100" invariant. This slice
delivers the **authoritative durable choice + the pure highest-wins cap
composition**; the net48 runtime seam that reads the composed cap on the live
`Skills` UI / gain / death path (the analogue of the Ready Hands
`Player.QueueEquipAction` patch) is the follow-on runtime wiring captured with the
joined-client artifact, not this engine-free CLEAN slice.

## The choice model (permanent, idempotent, one target skill)

Weapon Discipline is unlike the relationship-gated Character/Local effects: its
outcome is a **durable choice record**, not a per-evaluation active bit. Once
committed it is a Permanent Effect and **survives relationship loss, death, and Tree
revocation** (data-model.md invariant "Permanent Effects and Progression Keys
survive relationship loss and Tree revocation").

The authored choice catalog offers **at least two** melee skill-cap tiers, each
naming **one** target melee skill from the eligible Ready-Hands registry
(Swords/Knives/Clubs/Polearms/Spears/Axes). Because a choice raises exactly its one
target skill, the node "cannot raise every melee cap." The shipped provisional
catalog (playtest-only; final skill-cap ladder is deferred per spec §Non-goals)
offers two distinct target skills (Sword Mastery, Axe Mastery).

## What landed

- `src/SBPR.Niflheim.HomesteadStones/Domain/CharacterProgression/SkillCapChoices.cs`
  — the durable-choice domain. `SkillCapChoiceRecord` is the persisted
  {grant node identity/version, choice-catalog version, selected stable choice id,
  target melee skill, cap tier value, source op} provenance record (data-model.md
  "Skill-cap choices"). `SkillCapChoices.Choose` is the pure transition: it validates
  the accepted contract (purchased/eligible, ≥2 authored choices, offered selection,
  ≤100 cap, no prior committed choice) and produces the next character with exactly
  ONE appended choice record. Never mutates its input; never journals.
- `src/SBPR.Niflheim.HomesteadStones/Adapters/Warrior/SkillCapProvider.cs`
  — the authored choice catalog (≥2 tiers) + the highest-wins effective-cap
  composition. `Resolve` maps a caller selection to the committed value; `EffectiveCap`
  composes the per-skill cap highest-wins against a baseline (default vanilla 100),
  never below the baseline, never above the hard cap, and ONLY from choices whose
  target skill matches. Stateless — reads only persisted choice provenance, so the
  same character composes the same cap after death / relationship loss / revocation.
- `src/SBPR.Niflheim.HomesteadStones/Application/Commands/PurchaseCommands.cs`
  — `ChooseWeaponDisciplineSkillCommand` + `WeaponDisciplineCommandHandler`. The
  receipt-backed command mirrors `PurchaseCommandHandler` exactly: an append-only,
  per-boundary-fsync'd journal IS the transaction; the character store is an
  idempotent projection. Same op replays the recorded terminal (Replayed); a
  conflicting binding/payload under a committed op rejects `OperationConflict`; CAS on
  Stone/character revisions before any mutation.
- `CharacterStoneRecord` gains a backward-compatible `SkillCapChoices` list (pre-T031
  snapshots deserialize without it), threaded through every record-rewrite path
  (purchase, BP credit, relationship update) so a choice is never dropped.

## Tests (red-first, then green)

`tests/NiflheimWeaponDisciplineTests.cs` — 18 named facts across both acceptance ids,
driven end-to-end through the real T012/T013 develop→offer→purchase pipeline so the
choice is exercised against a genuinely purchased Weapon Discipline node.

- **AT-WEAPON-DISCIPLINE-CHOICE:** catalog offers ≥2 distinct eligible-melee tiers; a
  purchased character picks one offered tier; a choice without a purchase rejects
  `NotPurchased`; an unoffered id / stale catalog version rejects `ChoiceNotOffered`;
  replay of the same op is idempotent (one record); a SECOND distinct choice rejects
  `AlreadyChosen` (cannot be spent twice) with the original intact; conflicting op-id
  reuse rejects `OperationConflict`; a one-choice catalog rejects `CatalogTooSmall`; an
  authored cap >100 rejects `CapExceedsMax`; the selection raises ONLY the chosen skill
  (every other eligible melee skill stays at baseline under a sub-100 baseline probe);
  hostile identity rejects `PrincipalMismatch`; stale character revision rejects with
  zero mutation.
- **AT-WEAPON-CAP-LIFECYCLE:** the effective cap never exceeds the hard cap of 100;
  the permanent choice survives relationship release (still present, cap still
  composed); save/restart rehydrates the choice from journal truth and replay is pure;
  the choice record round-trips through the aggregate snapshot codec byte-stably.
- **Highest-wins composition** proved directly: a lower contributor never lowers the
  baseline; below a sub-100 baseline the highest contributor wins; a >100 contributor
  clamps to 100; no contributors returns the baseline.

Red-first verification: with the `EffectiveCap` target-skill filter removed (so a
choice would raise EVERY skill), `Choice_raises_only_the_chosen_skill_never_every_cap`
went RED (`Expected: 50, Actual: 100`) for the intended reason, then restored green.

## Gate results (pre-merge)

- Full suite: **1413/1413** passing (Weapon Discipline 18/18).
- net48 Release builds: **0 warnings / 0 errors** (HomesteadStones and Trailborne).
- `python3 scripts/docs-lint.py`: **OK**.
- `git diff --check`: clean.
- `SpecCheck.cs` recipe manifest: **unchanged** (Weapon Discipline registers no recipe).

## Honesty — logs-green ≠ playable

The above proves the pure choice/cap grammar, the receipt-backed choice command, and
the durable/permanent lifecycle (round-trip, restart, relationship-loss survival). It
does **not** prove a joined Valheim client observes the raised cap in the skills UI, on
skill gain, or on death loss in-world. That is DoD item 9 — the independent
joined-client capture, produced by qa-playtest and recorded alongside this file, and
gated on client availability (a live Valheim client was running on this host during
implementation, so no QA client was launched — server/engine-free work continued).
The net48 runtime seam that binds the composed cap to the live `Skills` path is the
follow-on runtime wiring for that capture, exactly as Ready Hands (T030) landed its
pure provider first and its `Player.QueueEquipAction` patch + joined-client proof as a
tracked remediation. Tracer-8 sign-off is T032 (non-author).
