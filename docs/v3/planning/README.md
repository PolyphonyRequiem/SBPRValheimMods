# docs/v3 — Swamp tier (v3)

Planning + impl specs for the **v3 Swamp tier** of SBPR Trailborne.

## What's here

- [`trail-lights-impl-spec.md`](trail-lights-impl-spec.md) — architect+spec for the v3
  **trail-light family** (card t_117bc232): two distinct **eternal** Spade-placed pieces, a
  tall far-reaching **Beacon** and a small **Surtling-Ember Lamp**, gated by Surtling core.
  Reformats the deferred design (nomap §3, requirements §A3.8/§567, PARKED §v3/§v5) and
  corrects the card's stale `ConfigureCosmeticFire` reference to the current
  `Assets.GraftTorchFire` eternal-flame pattern. **status: proposed** — 4 open questions
  await Daniel's confirm before build; the Beacon is the named seed for the §v5 lighthouse
  promotion.

> **Note for whoever merges second:** the **Sunstone Lens** spec
> (`sunstone-lens-impl-spec.md`, card t_2fd7bc7f) is a sibling v3 brick in flight on its own
> docs+code PR. It also creates `docs/v3/planning/{README.md,index.md}`. Whichever of the two
> v3 PRs merges second must union these two scaffold files (add the other spec's row to
> `index.md` and its bullet here) — a trivial add/add resolution. Both bricks were cut from
> `v1` independently to avoid coupling a docs spec to unrelated code.

## Tier framing

v3 is the **Swamp** tier (iron is the Swamp metal — `requirements.md` §A "Iron Compass —
v3 (Swamps tier)"). v3 items are gated by Swamp-tier materials (Iron, Guck).

The trail-light family adds a second v3 gate axis — **Surtling core** (the eternal-heat
material; corpus `Surtling_core.md`). Because Surtling core is Black-Forest-reachable, the
Beacon adds an **Iron co-gate** to hold the far landmark to true Swamp tier while the small
Ember Lamp stays Surtling-core-only (the tier-split lean in `trail-lights-impl-spec.md` §1 Q3,
pending Daniel's confirm).
