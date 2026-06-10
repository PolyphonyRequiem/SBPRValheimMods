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
- **`marker-signs-impl-spec.md`** — the buildable *how* for **Marker Signs + the
  WorldPin substrate** (design lock:
  [`../../design/marker-signs-worldpin.md`](../../design/marker-signs-worldpin.md)).
  Four additive Spade-placed marker pieces (POI / mining / shelter / portal) that
  pin/unpin themselves on the map via Shift+E with custom marker icons, plus the
  durable destroy-safe WorldPin engine (derive-by-scan reconcile keyed on the sign's
  ZDOID). **This is the pin model the Local Map viewer + Surveyor's Table consume** —
  built once, not forked. SpecCheck delta = **+4 build pieces**.

As more v2 features (Real Tents, lamp/pigment graduation) get specced, their
requirements land here too.
