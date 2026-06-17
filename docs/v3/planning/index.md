# index — docs/v3/planning

| file | purpose |
|------|---------|
| README.md | Human orientation for v3 (Swamp tier) planning |
| index.md | This manifest |
| twisted-portal-impl-spec.md | Build-ready spec for the **Twisted Portal** — a distinct portal class that teleports even where vanilla portals are blocked (`NoPortals`), addressed by player-assigned RUNE NAMES (`sbpr_rune_name` ZDO slot, off `s_tag`), accessed via a food-charged TRINKET key (durability-as-charge, burns per teleport, Pukeberry purge accelerator, the Sunstone Lens energy precedent). Reimplements teleport via `Player.TeleportTo` (omitting the NoPortals gate) rather than inheriting `TeleportWorld`. Catches the multiplayer 300m-sync fork the design doc missed (the Twisted equivalent of the Ancient Portal's PortalPrefabHash gotcha) and routes travel through server-side name-pairing (Model A). Decomposes into impl cards C1–C3. SpecCheck +2 rows. **BLOCKED on Daniel: Q1 coexist/replace, Q2 charge economy, Q3 destination UX (card t_f9cab392).** |
| trail-lights-impl-spec.md | Architect+spec for the v3 trail-light family (card t_117bc232): two distinct ETERNAL Spade-placed pieces — a tall far-reaching Beacon (light 1.5/12, kept from nomap §3) and a small Surtling-Ember Lamp — gated by Surtling core (+ Iron on the Beacon per the Q3 lean). Records the decomposition rationale (v1's Path Lamp already fills the fuelled niche), corrects the card's stale `ConfigureCosmeticFire` reference to the current `Assets.GraftTorchFire` eternal-flame pattern (no Fireplace → no add-fuel hover by construction), and reserves the v5 lighthouse-promotion hook. SpecCheck +2 build-piece rows. **status: proposed** — needs Daniel's confirm on 4 open questions before an impl card is cut. |

<!-- Sibling v3 brick: sunstone-lens-impl-spec.md (card t_2fd7bc7f) shipped on its own
     docs+code PR (#163, base main). When that line reaches v1, union its row here. -->
