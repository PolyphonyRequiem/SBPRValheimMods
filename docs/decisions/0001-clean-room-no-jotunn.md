---
title: "ADR-0001: Clean-room reimplementation — no Jotunn or mod-loader code"
status: accepted
---

# ADR-0001: Clean-room reimplementation — no Jotunn or mod-loader code

- **Status:** accepted
- **Date:** 2026-06-03
- **Deciders:** Daniel + Starbright

## Context

Valheim modding has mature helper libraries (Jotunn et al., MIT-licensed) that
make item/recipe/piece registration trivial. Using one would be faster. But SBPR
mods are distributed (eventually Thunderstore) and the project's doctrine is that
all gameplay behavior is the authors' own work, with a clean license story and no
dependency on another loader's lifecycle.

## Decision

Trailborne is a **clean-room** reimplementation. We register content directly
against vanilla's own Harmony-patchable surface. We may reference *vanilla* public
API names (verified against `assembly_valheim.dll` metadata) and may read
Jotunn/others only to understand *vanilla* behavior — never to copy their code.
Nothing copyrighted (game binaries, decompiled source, other mods' source) is
committed.

## Consequences

- More upfront work (we write our own ObjectDB/ZNetScene/PieceTable wiring) and
  more exposure to vanilla-internals drift — mitigated by the reflection
  drift-guard in CI (see ADR-0004).
- A clean MIT license with no third-party loader runtime dependency.
- **Do not introduce a Jotunn (or similar) dependency without a new ADR.**

## Alternatives considered

- **Depend on Jotunn:** faster registration, but adds a runtime dependency,
  couples us to its lifecycle/versioning, and muddies the clean-room story.
  Rejected.

## Clarification (2026-06-09, Daniel)

The original "clean-room" wording above was repeatedly **misread by contributors
(human and agent) as a firewall around vanilla Valheim** — i.e. "don't read or
adapt the decompiled IronGate source." **That reading is wrong and has cost real
time** (architect workers tying themselves in knots avoiding the decomp, then
timing out). To be explicit:

- **The firewall is around OTHER developers' mod code only** (Jotunn, other
  Thunderstore mods) — authors who never consented to be our reference.
- **Vanilla Valheim disassembly is fair game to read AND adapt.** It is the game
  we are modding; lifting/adapting its own logic (e.g. `GlobalWind`'s wind driver)
  into our implementation is normal engineering, not a violation.
- **Other mods' *functionality* may still be reproduced — but only via a clean-room
  RE process (a Chinese wall).** A reviewer (`reviewer-cleanroom` / `re-analyst`)
  reads the other mod's source and writes a behavioral *description* in its own
  words (no code copied); a *separate* implementer who never saw that source
  reproduces the behavior from the description alone. One agent must never both read
  the original and write our version of it. You may also simply *ask questions*
  about another mod to learn *where* to investigate the vanilla internals yourself.
- **The two hard limits that remain:** (a) no *direct* copying of other mods' code
  (use the RE wall), and (b) don't *commit* copyrighted files (game binaries,
  decompiled IronGate source, other mods' source) into this MIT repo. Reading them
  locally is fine; checking them in is not.

This clarifies — does not reverse — the decision: a clean MIT license with no
third-party loader code and no committed copyrighted files. The "names only"
phrasing elsewhere in the docs is superseded by this clarification.
