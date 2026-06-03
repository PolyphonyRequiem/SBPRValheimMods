---
spec_name: trailborne-v1
shaped_at: 2026-06-03
shaper: spec-shaper (Starbright, in-session with Daniel)
status: IN PROGRESS — appending Q&A round-by-round as we go
---

# Requirements: SBPR Trailborne v1

> **Working document.** Each shaper round is appended here as it happens.
> When shaper completes, this file is promoted to "final" state and handed
> to spec-writer. Until then, the latest round may be partial.

## Source idea

See `planning/initialization.md` for the verbatim raw idea + carried doctrine + concept-seed inventory.

---

## Q&A round-by-round

### Round 1 — Scope & Purpose ✅ COMPLETE

**Q1.1 — v1 piece roster:** Propose v1 ships exactly: Explorer's Bench, Cairns, Pigments, Painted Signs, Trailblazer's Tools. All Meadows tier so a fresh-character solo player gets the full experience from start (no biome-progression-gating in v1). Path Lamps + Inert Guardian Stones held as late-polish, added only if under budget.

**A1.1:** ✅ Yes. (Daniel: "I very much had the same idea.")

---

**Q1.2 — Map-nerf scope for v1:** Propose v1 is ADD-only — no touching the vanilla Cartography Table at all. Solo-install players still get free vanilla cartography; cairns/signs are *additive* tools. Actual map nerf happens in v2 when Map Stations ship as replacement. Rationale: nerf-without-replacement would tank standalone-install experience and the Thunderstore page would fill with complaints.

**A1.2:** ✅ Yes.

---

**Q1.3 — Server-gated vs always-on:** Propose the `SBPRContext.OnSBServer` gate is REMOVED for Trailborne specifically. Always-on regardless of server, configurable via standard BepInEx config (`Trailborne.Enabled = true/false`). Rationale: server-gated pattern stays correct for *future* SBPR mods that enforce Niflheim house rules (Pact, Guardian Stones, region-gated content), but Trailborne is *philosophy*, not house rules.

**A1.3:** ✅ Yes.

---

### 🆕 DOCTRINE REFINEMENT from Round 1 (added by Daniel, captured for spec-writer):

**"Leverage Unity indirectly, not directly."** This refines the prior "zero Unity assetbundles" doctrine (fact #112). What we CAN do:

- Compose vanilla Unity prefabs at runtime via Harmony + reflection
- Instantiate vanilla `ParticleSystem` instances at runtime for visual effects
- Reflect vanilla materials onto vanilla meshes with runtime tinting
- Reuse vanilla sprites for menu icons (if available)
- Load PNG icons at runtime via `File.ReadAllBytes` → `Texture2D.LoadImage` → `Sprite.Create`

What we will NOT do (in v1):

- Open the Unity Editor
- Bake `.unity3d` assetbundles
- Author custom meshes, materials, ParticleSystems, or animations in Unity

**Reserved exception for the future:** when **Locations** (custom dungeons, POIs, regional structures) become a thing, Daniel reserves the right to revisit this doctrine — Locations *can't* be assembled at runtime, they need baked scene hierarchies. v∞ problem, NOT a v1 problem.

---

### Round 2 — Mechanics 🟡 IN PROGRESS (questions sent 2026-06-03, awaiting Daniel)

**Q2.1 — Cairn mechanics package** (Activation: always-on / Comfort: +1 / Radius: ~10m / Soft-cap: build cost only / Decay: none / Visual: Tier 2 procedural stack of vanilla `rock_low` prefabs + vanilla rune-glow ParticleSystem).
**A2.1:** ⏳ pending

**Q2.2 — Trailblazer's Tools shape** (Option A: lean — just a new build tab on vanilla Hammer, no new items; vs Option B: rich — new tab + consumable Trailblazer's Tools class with Trail Marker Stake / Pigment Brush / Cairn Lodestone).
**A2.2:** ⏳ pending

**Q2.3 — Explorer's Bench station** (Option A: custom station (Tier 1 reuse, recipe gate, thematic anchor) vs Option B: reuse vanilla Workbench (zero install-to-first-play friction)).
**A2.3:** ⏳ pending

---

### Round 3 — Pigments + Painted Signs mechanics
*(not yet asked)*

---

### Round 4 — Reusability scan against decomp + wiki
*(not yet performed)*

---

### Round 5 — Visual assets
*(not yet asked)*

---

### Round 6 — Scope boundaries / out-of-scope
*(not yet asked)*

---

## Explicit features requested
*(will be populated as rounds complete)*

## Constraints stated
- Standalone-by-default; solo install must be complete good experience
- Zero Unity Editor in v1; runtime composition only ("indirectly leverage Unity")
- No server-gating for Trailborne (philosophy mod, not house-rules mod)
- v1 is ADD-only — no vanilla Cartography Table nerf
- All v1 pieces are Meadows tier (no biome-progression gate)

## Out of scope (user-confirmed)
- Vanilla Cartography Table nerf — slip to v2
- Local Maps + Map Stations — v2
- Real Tents — v2
- Iron Compass — v3
- Seer's Amulet — v4
- Plains sailing — v5
- On-demand map viewing — v6
- Active Guardian Stones — separate mod family
- Custom Unity-authored assets — deferred to Locations work (v∞)

## Reusability notes
*(will be populated by Round 4)*

## Visual assets
*(none provided yet — will be requested in Round 5)*

## Open questions / TBD
- All Round 2/3/4/5/6 questions still pending

## Vision context

Aligned with holographic facts:
- `#111` Trailborne naming lock
- `#112` Trailborne asset doctrine (Round 1 refinement above sharpens this)
- `#93` Niflheim parked design
- `#94` Corpus-first rule (must grep `~/valheim/sbpr-corpus/wiki/fandom/`)
- `#110` Kanban Swarm execution handoff after spec lock

Bigger picture: Trailborne v1 is SBPR's first public Thunderstore release.
Its reception sets the brand for everything downstream (Guardian Stones,
Niflheim modpack, the eventual `niflheim.wiki`). Standalone-install
experience is therefore non-negotiably good.
