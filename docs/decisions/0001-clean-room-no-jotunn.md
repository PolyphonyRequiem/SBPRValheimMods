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
