---
spec_name: trailborne-v1
shaped_at: 2026-06-03
shaper: spec-shaper (Starbright, in-session with Daniel)
status: IN PROGRESS — Round 2 corrected against design/PARKED-2026-06-03.md
correction_note: |
  Initial Round 2 questions were posed as if pieces were undefined.
  They were not — design/PARKED-2026-06-03.md locked most of this last
  night. Daniel snapped me back. This version REPLACES the prior pass
  with the actual locked decisions.
---

# Requirements: SBPR Trailborne v1

> **Working document.** Each shaper round is appended here as it happens.
> When shaper completes, this file is promoted to "final" state and handed
> to spec-writer. Until then, the latest round may be partial.

## Source idea

See `planning/initialization.md` for the verbatim raw idea + carried doctrine + concept-seed inventory.

**Critical primary-source reference:** `design/PARKED-2026-06-03.md` in this repo
already contains substantial v1 design lock-in from 2026-06-02 evening session.
This requirements.md MUST stay consistent with that document. Where this
document diverges from the parked doc, the parked doc wins unless explicitly
overridden in a numbered round below.

---

## Q&A round-by-round

### Round 1 — Scope & Purpose ✅ COMPLETE

**Q1.1 — v1 piece roster:** Propose v1 ships exactly: Explorer's Bench, Cairns, Pigments, Painted Signs, Trailblazer's Tools (single tool item, hoe/hammer tier-equivalent), **Path Lamps**.

**A1.1:** ✅ Yes — INCLUDING Path Lamps. They're a philosophy-completing piece (night-time trail illumination), not scope creep. Without them, the trail-discipline loop is complete-by-day, broken-by-night, and players will misuse vanilla torches to fill the gap. Path Lamps belong in v1.

**Explicitly OUT of Trailborne entirely (different mod, different family):**
- **Guardian Stones (active OR inert)** — server worldbuilding artifact, separate mod (`SBPR.Wardens` or similar), gated on `valheim-regions` macro-boundary work. Stripped from Trailborne scope entirely.

---

**Q1.2 — Map-nerf scope for v1:** *CORRECTED from initial pass.* v1 DOES nerf the Cartography Table — existing in-world Cartography Tables lose functionality; new ones cannot be built.

**Map situation for v1 (locked):**
- `nomap=ON` (server setting) → **no map at all** (vanilla nomap mode)
- `nomap=OFF` (default) → **minimap ONLY**, freely rotating, **no north indicator**, **no M-key map**
- Cartography Table: disabled functionality if pre-existing; cannot be built

**A1.2:** ✅ Locked per parked doc.

---

**Q1.3 — Server-gated vs always-on:** Remove `SBPRContext.OnSBServer` gate for Trailborne. Always-on, configurable via BepInEx config.

**A1.3:** ✅ Agreed for now.

---

### 🆕 DOCTRINE REFINEMENT from Round 1 (added by Daniel, captured for spec-writer):

**"Leverage Unity indirectly, not directly."** This refines fact #112. What we CAN do at runtime:
- Compose vanilla Unity prefabs via Harmony + reflection
- Instantiate vanilla `ParticleSystem` instances for visual effects
- Reflect vanilla materials onto vanilla meshes with runtime tinting
- Reuse vanilla sprites for menu icons where available
- Load PNG icons via `File.ReadAllBytes` → `Texture2D.LoadImage` → `Sprite.Create`

What we will NOT do (in v1):
- Open the Unity Editor
- Bake `.unity3d` assetbundles
- Author custom meshes, materials, ParticleSystems, or animations in Unity

**v1 visual approach: kitbash prototype assets where possible for playtesting.** Composite existing vanilla prefabs/materials/particles into Trailborne pieces. Visual polish (custom materials, custom icons that aren't kitbashed) is a v1.1+ concern. Goal is *playtest-quality mechanics* in v1, not *ship-quality art*.

**Reserved exception for v∞:** when **Locations** become a thing, Daniel reserves the right to revisit this doctrine — Locations need baked scene hierarchies that can't be assembled at runtime. NOT a v1 problem.

---

### Round 2 — Mechanics ✅ COMPLETE (corrected against design/PARKED-2026-06-03.md)

#### A2.1 — Cairn mechanics (LOCKED)

| Aspect | Locked value |
|---|---|
| **Activation** | Always-on once built (no fuel) |
| **Comfort tier ladder** | 5-tier: stones can stack to give comfort floors of **3 / 4 / 5 / 6 / 7** |
| **Comfort interaction with vanilla** | `max()` clamp — cairn never *reduces* effective comfort, only raises floor |
| **Implementation surface** | Patch `SE_Rested.CalculateComfortLevel` directly (cairn is NOT in vanilla `ComfortGroup` enum so it bypasses vanilla's same-group dedup) |
| **Decay** | ⚠️ **MANDATORY decay** — cairns ARE destructible. Downgrade @25% HP, collapse @0%. Cairns are *evidence of a trail still being walked* — abandonment = collapse. Re-correction: I (Starbright) proposed indestructible in error this morning; Daniel snapped me back. Decay is the design's *core thesis-in-a-piece*. |
| **Repair** | Flat **3 stone + 1 resin** regardless of damage level |
| **Pigment / banner persistence** | Persist across rebuilds — applied colors survive damage + repair cycles |
| **Downgrade re-ignite of resin** | OPEN — Daniel: "lean deliberate-only" (i.e. requires explicit player action, not auto-re-ignite on repair) |
| **Visual (playtest)** | Kitbash: procedural stack of vanilla `rock_low` prefabs assembled at runtime, capped with vanilla-rune-glow ParticleSystem instance, pigment-tinted via runtime material reflection. Per "leverage Unity indirectly" doctrine. |
| **Build cost** | TBD — design/PARKED-2026-06-03.md doesn't explicitly list per-tier stone costs beyond the comfort floor numbers (3/4/5/6/7 stones may BE the build cost — needs Round 2.5 verification with Daniel) |

#### A2.2 — Trailblazer's Tools (LOCKED — single item, NOT options)

**Single tool item.** Hoe/hammer tier-equivalent. Its own slot in the player's inventory, its own keybind, its own selection wheel.

| Capability | Detail |
|---|---|
| **Path widths** | **1.5m / 3m / 5m** — three selectable widths (analogous to hoe's flatten radii) |
| **Cultivate replant** | When laying a path through cultivated ground, replants underlying vegetation appropriately (i.e. doesn't permanently destroy cultivation) |
| **ClearVegetation** | Removes existing vegetation along the laid path (small brush, grass, mushrooms — NOT trees) so the path is *visually a path*, not a stripe through bushes |
| **Implementation surface** | Likely a new item class analogous to `Hoe`, with custom `m_operations` array entries for the three widths + cultivate-replant + clear-vegetation. May patch `Hoe` directly OR introduce a new `TrailblazerTool` MonoBehaviour. Decision for spec-writer. |

#### A2.3 — Explorer's Bench (LOCKED — kitbash for playtest)

**v1 approach:** kitbash the vanilla Workbench. Tier 1 reuse — vanilla Workbench mesh + Trailborne material tint (or just unmodified vanilla Workbench for the very first playtest). Trailborne recipes register as new tabs OR new entries on the vanilla Workbench.

**v1.1+ path:** graduate to a visually-distinct Explorer's Bench once mechanics are validated. Possibly retains thematic anchor (its own recipe gate, its own discovery moment). Decision deferred until post-v1-playtest signal.

**Recipe (v1):** TBD — likely vanilla Workbench recipe unchanged (10 Wood + 4 Stone), since we're literally using the vanilla Workbench.

#### A2.4 — Path Lamps (LOCKED, added in this round per Daniel)

**Recipe:** Wood + Resin (per parked doc; exact quantities TBD).

| Mechanic | Detail |
|---|---|
| **Light source** | Passive — like vanilla torch but slightly **dimmer** (trail-illumination, not base-illumination) |
| **Fuel duration** | **Longer** than vanilla torch (so a string of them doesn't become a refuel chore — *evidence-of-trail* shape rather than *maintenance burden*) |
| **Chain ignition** | Walking close to a lit Path Lamp with an unlit Path Lamp in proximity should light the unlit one (gives the satisfying "lighting the path home" moment without manual torch-by-torch interaction) — OPEN: Daniel to confirm |
| **Implementation surface** | Likely Tier 1 reuse — vanilla `Torch` prefab + custom light intensity + custom fuel rate + custom recipe. Chain-ignition would require a small `MonoBehaviour` on the lamp that polls for nearby lit lamps. |
| **Visual (playtest)** | Kitbash: vanilla torch model + dimmer light intensity reflection + (optional) pigment-tinted flame via runtime ParticleSystem property edit |

#### A2.5 — Pigments (LOCKED per parked doc)

| Aspect | Locked value |
|---|---|
| **Colors** | Red, White, Black, Blue (4 basic pigments) |
| **Output per craft** | 2 pigments per craft |
| **Stack size** | 20 |
| **Weight** | 0.1 |
| **Recipe inputs** | TBD per color — needs Round 3 with Daniel (likely: berries/clay/coal/mushroom mapped to colors) |
| **Craft station** | Explorer's Bench (v1 = vanilla Workbench kitbash) |

#### A2.6 — Painted Signs (LOCKED per parked doc)

| Aspect | Locked value |
|---|---|
| **Base** | Variant of vanilla sign |
| **Interaction** | `E` = set text color; `Shift+E` = set accent color |
| **Two-tone pin variants** | Sign places a corresponding pin on minimap (when `nomap=OFF`) with matching two-tone color scheme |
| **No-op fallback** | If `nomap=ON`, pin placement is no-op (sign still works in-world; minimap just doesn't exist to pin onto) |
| **Implementation surface** | Patch vanilla `Sign` class for the E/Shift+E binding extension; patch `Minimap.AddPin` for the two-tone color application |

---

### Round 3 — Open mechanical questions (NEXT)

These are the items still TBD that need Daniel input before spec-writer can lock:

**Q3.1 — Cairn build cost:** Are the 3/4/5/6/7 stone counts ALSO the build cost? i.e. building a tier-1 cairn costs 3 stone, tier-5 costs 7 stone? Or is build cost separate from the comfort tier number?

**Q3.2 — Pigment recipe inputs:** What goes into each color? Proposed: Red = Raspberries (Meadows) + Coal; White = Bone fragments (Meadows) + Coal; Black = Coal + Resin; Blue = Blueberries (Black Forest) + Coal. Issue: Blueberries are Black Forest tier — if pigments are v1 Meadows-only, Blue might need a Meadows-sourceable input. *Will grep wiki to verify Meadows-available blue-source.*

**Q3.3 — Path Lamp chain-ignition:** Yes/no on the auto-chain-ignite mechanic from a lit lamp to a nearby unlit one?

**Q3.4 — Trailblazer's Tools recipe + crafting station:** Built at Explorer's Bench (vanilla Workbench kitbash for v1)? Recipe guess: 5 Wood + 2 Stone + 2 LeatherScraps (mirror of Hoe recipe).

**Q3.5 — Cairn re-ignite of resin on repair:** Confirm "deliberate-only" (manual re-ignite required) vs. auto-re-ignite on repair?

---

### Round 4 — Reusability scan against decomp + wiki
*(NOT YET PERFORMED — will execute after Round 3 answers + with the grep-wiki-first discipline)*

Planned scans:
- `Hoe` class in decomp → for Trailblazer's Tools implementation pattern
- `Sign` class in decomp → for Painted Signs interaction extension
- `SE_Rested.CalculateComfortLevel` in decomp → for cairn comfort patch surface
- `Minimap.AddPin` + pin data structures in decomp → for two-tone pins
- `Torch` class in decomp → for Path Lamp Tier 1 reuse + chain-ignition surface
- `Piece.m_resources` shape in decomp → for recipe registration
- Wiki: `Cartography_Table.md` → for disable-mechanism surface
- Wiki: `Raspberries.md`, `Blueberries.md`, `Coal.md`, `Bone_fragments.md`, `Resin.md` → for pigment recipe-input availability per biome
- Wiki: `Banner.md` → for the +1 comfort radius/contribution Cairns are modeled after

---

### Round 5 — Visual assets
*(NOT YET ASKED — will ask in next round)*

---

### Round 6 — Scope boundaries / out-of-scope
*(NOT YET ASKED — will ask after Round 4)*

---

## Explicit features requested (running list)

1. Explorer's Bench (v1 = kitbash vanilla Workbench)
2. Cairns — 5-tier comfort floor 3/4/5/6/7, decay-mandatory, downgrade@25%/collapse@0%, repair flat-cost, pigment+banner persist
3. Pigments — R/W/B/Blue, 2/craft, stack 20, weight 0.1
4. Painted Signs — vanilla sign variant + E/Shift+E color binding + two-tone pins (no-op if nomap=ON)
5. Trailblazer's Tools — single tool item, hoe/hammer-equivalent, 1.5/3/5m path widths, Cultivate replant, ClearVegetation
6. Path Lamps — Wood+Resin recipe, dimmer-than-torch, longer fuel, chain-ignition (TBD)
7. Map disable in v1 — Cartography Table disabled (no build, no functionality on existing); nomap=ON → no map; nomap=OFF → minimap only (no M-key, no north indicator)

## Constraints stated

- Standalone-by-default; solo install must be complete good experience
- "Leverage Unity indirectly" — runtime composition of vanilla prefabs/materials/ParticleSystems OK; Unity Editor + assetbundles NOT in v1
- v1 visual approach is "kitbash for playtest" — playtest-quality mechanics ≠ ship-quality art
- No server-gating for Trailborne (philosophy mod, not house-rules mod)
- All v1 pieces are Meadows tier (no biome-progression gate in v1)
- v1 DOES nerf vanilla map (Cartography Table disabled, no M-key map)
- v1 doctrine: corpus-first — grep `~/valheim/sbpr-corpus/wiki/fandom/` BEFORE claiming any vanilla-content fact

## Out of scope (user-confirmed)

- **Guardian Stones (active OR inert)** — entirely separate mod family, NOT Trailborne
- Local Maps + Map Stations — v2 (Black Forest tier)
- Real Tents (Bear hide) — v2 (Black Forest tier)
- Cartographer's Kit (gated on 4 pigment discovery) — v2
- Iron Compass — v3 (Swamps tier, iron is Swamps metal)
- Twisted Portal, Beacons, Ember magic, Scrying Altar, Smokeless Cookfire — v3
- Seer's Amulet (crystal-gated, Stone Golem drop) — v4 (Mountains tier, sole headline)
- Plains sailing tier, lighthouse-promote, Star Glass — v5
- Portable map magic — v6 (Mistlands)
- Custom Unity-authored assets — deferred to Locations work (v∞)
- Ship-quality custom art for v1 pieces — v1.1+ polish

## Reusability notes
*(will be populated by Round 4 — decomp + wiki scan)*

## Visual assets
*(none provided yet — will be requested in Round 5)*

## Open questions / TBD

- Q3.1: Cairn build cost = comfort tier stone count, or separate?
- Q3.2: Blue pigment Meadows-availability (Blueberry is Black Forest)
- Q3.3: Path Lamp chain-ignition yes/no?
- Q3.4: Trailblazer's Tools recipe specifics
- Q3.5: Cairn re-ignite of resin on repair (deliberate-only vs auto)
- Round 4 decomp/wiki scans pending
- Round 5 visual assets pending
- Round 6 out-of-scope confirmation pending

## Vision context

Aligned with holographic facts:
- `#111` Trailborne naming lock
- `#112` Trailborne asset doctrine (Round 1 refined as "leverage Unity indirectly")
- `#93` Niflheim parked design
- `#94` Corpus-first rule (must grep wiki before claiming vanilla-content facts)
- `#110` Kanban Swarm execution handoff after spec lock

And primary-source design lock: `design/PARKED-2026-06-03.md` in this repo.

Bigger picture: Trailborne v1 is SBPR's first public Thunderstore release.
Its reception sets the brand for everything downstream (Guardian Stones as
separate mod family, Niflheim modpack, the eventual `niflheim.wiki`).
Standalone-install experience is non-negotiably good.
