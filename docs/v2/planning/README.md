# README — docs/v2/planning

Planning artifacts for the Black Forest tier. Same role as
`docs/v0.1.0/planning/` plays for Meadows.

- **`requirements.md`** — the locked v2 cartography requirements, distilled from the
  ratified decisions in [`../../design/cartography-v2.md`](../../design/cartography-v2.md).
  The design doc carries the *why* and the decision history; this carries the locked
  *what* in build-ready form. **All open items closed** (architect spec-pass 2026-06-10).
- **`cartography-impl-spec.md`** — the buildable *how*: one tight section per feature
  (Surveyor's Table, Local Map + viewer, Cartographer's Kit) an implementer picks up
  cold — observable acceptance criteria, exact vanilla decomp hooks, the
  `Features/Cartography/` placement, and each feature's SpecCheck manifest row. This is
  what gates the three implementation cards (filed as children of the spec-pass + the
  UI-fork spike).

As more v2 features (Real Tents, lamp/pigment graduation) get specced, their
requirements land here too.
