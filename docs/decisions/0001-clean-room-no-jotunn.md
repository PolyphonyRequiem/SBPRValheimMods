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
- **Do not introduce a Jotunn (or similar) RUNTIME dependency without a new ADR.**
  (Source *adaptation* from permissively-licensed works is allowed under the
  2026-08-04 amendment below, with notices + inline attribution.)

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

## Amendment (2026-08-04, Daniel): permissive-licence source adaptation is allowed

The 2026-06-09 clarification fixed one misreading (the vanilla firewall) but left
a second one standing: contributors read the Chinese-wall requirement as a **legal
constraint**. It is not. Jotunn is **MIT** (`Copyright (c) 2021 JotunnLib Team`,
verified against `Valheim-Modding/Jotunn@dev/LICENSE`), and MIT grants use, copy,
modify, merge, publish, distribute, sublicense and sell, subject to exactly one
condition: the copyright and permission notice must travel with the work.

Re-reading the original Decision, none of its three reasons is a legal one:

1. *"all gameplay behavior is the authors' own work"* — an authorship preference.
2. *"a clean license story"* — MIT-into-MIT is already clean; this reason is thin.
3. *"no dependency on another loader's lifecycle"* — **the one load-bearing reason,
   and it argues against a RUNTIME DEPENDENCY, not against reading their source.**

Therefore, amended:

- **Reading any third-party mod source to learn WHERE to investigate vanilla is
  unrestricted.** No wall, no notice, no ceremony. It creates no obligation.
- **ADAPTING source from a PERMISSIVELY-licensed work (MIT/BSD/Apache-2.0/similar)
  is ALLOWED**, subject to two mandatory conditions:
  - the work is listed in [`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md)
    with its verbatim licence text and exact copyright line, **before** the adapted
    code lands; and
  - each adapted site carries an inline comment naming the source work (see the
    format in that file). Attribution lives in the code, not only in a manifest.
- **The clean-room Chinese wall (`reviewer-cleanroom` → behavioural description →
  separate implementer) is now required ONLY for non-permissive, unlicensed, or
  licence-unknown sources.** It is no longer required for MIT-licensed works. It
  remains available as a tool when we WANT independent derivation for its own sake.
- **Taking a third-party mod loader as a RUNTIME DEPENDENCY still requires a new
  ADR.** Reason 3 above is untouched by this amendment. Listing a work in
  `THIRD-PARTY-NOTICES.md` records a *source adaptation compiled into our own
  assemblies*; it does not authorise a runtime dependency.
- **Committing copyrighted files is still forbidden** (game binaries, decompiled
  IronGate source, other mods' source trees). Adapting into our own files is not
  the same as vendoring theirs.
- **Vanilla Valheim remains outside all of this** and gains no notices entry: it is
  the game we are modding, per the 2026-06-09 clarification.

### Why this amendment exists

The wall was costing real engineering time to satisfy a constraint the licence
never imposed — the same class of failure as the 2026-06-09 misreading, one level
up. Recording it here so it is not rediscovered a third time.

**What this amendment does NOT license:** adapting code because a library is
merely adjacent to the conversation. Adaptation should follow measured friction in
our own codebase, not enthusiasm. Reason 1 above (authorship) is still a legitimate
reason to write our own version — it is now a *choice* to be made per case, with
eyes open, rather than a blanket prohibition.
