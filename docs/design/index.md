# index — docs/design

| file | purpose |
|------|---------|
| README.md | Human orientation for the design folder |
| index.md | This manifest |
| trailborne-vision.md | North star: the Explorer role and what success feels like |
| design-pillars.md | The load-bearing design constraints, non-negotiable |
| nomap.md | Investigation: how Valheim tracks the map + no-map patch-surface cross-reference |
| map-provider-model.md | LIVING design (Daniel 2026-06-15 revision): the map provider model — M-key behavior, two map types (personal global map + local-map ARTIFACTS), equipped-local-map "provider" binding, Cartographer's-tools + Surveyor's-Table bidirectional global↔local sync, nomap-on/off split, Eye of Odin (Mistlands global-map unlock). Serialization grounded vs decomp (carried customData ≠ networked → artifact model safe). Supersedes the M/E-open + nerf portions of cartography-impl-spec §2G; iterating on §9 open questions before it graduates to specs |
| pin-sharing.md | Investigation: multiplayer pin-sharing surface under server gating |
| constitution.md | Governing principles for spec-driven v2 (ADR-0005 Option C) — Spec Kit vocabulary + load-bearing invariants |
| cartography-v2.md | DRAFT design: Black Forest cartography — Map Station + Local Maps + Cartographer's Kit (grounded on vanilla MapTable/Minimap; open questions pending Daniel) |
| marker-signs-worldpin.md | LOCKED design: Marker Signs + the SBPR WorldPin substrate — durable ZDO-anchored map pins, Shift+E pin/unpin, destroy-safe offline via derive-by-scan reconcile (the pin model the v2 cartography tier consumes) |
| swamp-detection-item.md | IDEA: Swamp-tier solar-charged monster-detection accessory — charges durability in sunlight, drains while equipped, reveals nearby hostiles. Theme + sourcing DECIDED 2026-06-13 (Sunstone / Iceland-spar, dual-source: swamp surface chests + rare Draugr Elite); mechanics still proposed |
| pocket-portal.md | PROPOSED design: Portal Seed → Ancient Portal (Black Forest v2) — single 25 kg field item, Hammer-placed no-bench, plants and grows over 15 s into a horizontal overhead root-portal, otherwise a regular portal (keeps ore ban), collapses back to a replantable seed when broken |
| ancient-portal-placeholder-art.md | PROPOSED placeholder art: doctrine-clean kitbash for the Ancient Portal (horizontal overhead disc on root-pillars) from verified vanilla parts — small_portal ring + Greydwarf_Root tangle + stubbe legs, all additive (ADR-0006), scale-lerp grow fake. Art brief for the impl card |
| stone-of-drought-feasibility.md | RESEARCH: feasibility of a Stone of Drought (placeable that carves a curved water divot / repels water). Grounded in vanilla WaterVolume.GetWaterSurface (NOT Rune Magic — clean-room). Key finding: logical water-repel is one Harmony postfix; the VISUAL divot fights a proprietary GPU shader (the hard part). Phased path + Phase-0 spike recommended |
| maritime-exploration-tools.md | BRAINSTORM (idea): v5 Plains maritime exploration tools — Daniel's lighthouse (v3-Beacon promotion, dep t_117bc232) + fog-buoy (🔴 unverified `Floating` buoyancy) seeds, plus the wider set (Star Glass, sea-route/depth/harbor markers, sextant, message-bottle) as prompts only. Grounded vs PARKED:40 / requirements.md:569. No-map/disorientation framing stated; open questions left open for Daniel |
| PARKED-2026-06-03.md | Working-state resume point from the 2026-06-03 session |
