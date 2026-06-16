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

## Tier framing

v3 is the **Swamp** tier (iron is the Swamp metal — `requirements.md` §A "Iron Compass — v3
(Swamps tier)"). v3 items are gated by Swamp-tier materials (Iron, Guck, Bloodbag) and the new
**Sunstone** material (Iceland-spar / *sólarsteinn*, locked in
[`../../design/swamp-detection-item.md`](../../design/swamp-detection-item.md)). The Twisted
Portal network is the **second consumer of Sunstone** (the Sunstone Lens is the first) — the
material's "more than one use" design intent realized.
