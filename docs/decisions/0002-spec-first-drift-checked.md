---
title: "ADR-0002: Spec-first — code and spec change together, drift-checked at boot"
status: accepted
---

# ADR-0002: Spec-first — code and spec change together, drift-checked at boot

- **Status:** accepted
- **Date:** 2026-06-03
- **Deciders:** Daniel + Starbright

## Context

Early in development the implementation silently diverged from the design across
milestone iterations because nobody re-read the spec on every change. Recipes and
piece definitions drifted from intent and it was hard to tell deliberate change
from accident.

## Decision

The project is **spec-first**. The locked spec
(`docs/v0.1.0/planning/requirements.md`, with durable intent in `docs/design/`)
and the code change **in the same PR**. A code change that diverges from the spec
is a bug. A machine-checked recipe manifest in
`src/SBPR.Trailborne/Runtime/SpecCheck.cs` runs at server boot and logs an ERROR
on any drift from the locked set.

## Consequences

- "Done" means code **and** spec **and** the SpecCheck manifest are consistent.
- Reviewers (human or agent) can trust the spec as the source of truth; where
  spec and code disagree, the spec wins unless Daniel overrides in the PR.
- Slightly more work per change (update three surfaces) — but drift is caught at
  boot instead of in playtest.
- **Do not weaken or bypass SpecCheck to make a build pass.** Fix the divergence.

## Alternatives considered

- **Docs-as-afterthought** (update spec "later"): this is exactly what caused the
  original drift. Rejected.
- **Spec only in markdown** (no boot check): markdown can't fail a build, so drift
  returns. The in-code manifest is what makes the rule enforceable.
