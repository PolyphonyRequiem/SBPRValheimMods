# docs/v3 — Swamp tier (v3)

Planning + impl specs for the **v3 Swamp tier** of SBPR Trailborne.

## What's here

- [`twisted-portal-impl-spec.md`](twisted-portal-impl-spec.md) — the build-ready spec for the
  **Twisted Portal**, a long-range named portal network. A distinct portal class that teleports
  even where vanilla portals are blocked (the `NoPortals` global key), addressed by player-assigned
  **rune names**, accessed via a **food-charged key** (a Trinket whose durability is its charge —
  the Sunstone Lens energy model). Nearby portal names are listed on-step. **Blocked on three
  Daniel design decisions** (coexist-vs-replace, charge economy, destination UX) — see the spec's
  §1/§2. Decomposes into impl cards C1–C3 once unblocked (card t_f9cab392).
- [`trail-lights-impl-spec.md`](trail-lights-impl-spec.md) — architect+spec for the v3
  **trail-light family** (card t_117bc232): two distinct **eternal** Spade-placed pieces, a
  tall far-reaching **Beacon** and a small **Surtling-Ember Lamp**, gated by Surtling core.
  Reformats the deferred design (nomap §3, requirements §A3.8/§567, PARKED §v3/§v5) and
  corrects the card's stale `ConfigureCosmeticFire` reference to the current
  `Assets.GraftTorchFire` eternal-flame pattern. **status: proposed** — 4 open questions
  await Daniel's confirm before build; the Beacon is the named seed for the §v5 lighthouse
  promotion.

## Tier framing

v3 is the **Swamp** tier (iron is the Swamp metal — `requirements.md` §A "Iron Compass — v3
(Swamps tier)"). v3 items are gated by Swamp-tier materials (Iron, Guck, Bloodbag) and the new
**Sunstone** material (Iceland-spar / *sólarsteinn*, locked in
[`../../design/swamp-detection-item.md`](../../design/swamp-detection-item.md)). The Twisted
Portal network is the **second consumer of Sunstone** (the Sunstone Lens is the first) — the
material's "more than one use" design intent realized.

The trail-light family adds a second v3 gate axis — **Surtling core** (the eternal-heat
material; corpus `Surtling_core.md`). Because Surtling core is Black-Forest-reachable, the
Beacon adds an **Iron co-gate** to hold the far landmark to true Swamp tier while the small
Ember Lamp stays Surtling-core-only (the tier-split lean in `trail-lights-impl-spec.md` §1 Q3,
pending Daniel's confirm).
