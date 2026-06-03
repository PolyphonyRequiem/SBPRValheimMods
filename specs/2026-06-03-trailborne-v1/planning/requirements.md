---
spec_name: trailborne-v1
shaped_at: 2026-06-03
shaper: spec-shaper (Starbright, in-session with Daniel)
status: IN PROGRESS — Round 3 closed, Round 4 (decomp/wiki scan) next
correction_notes: |
  - Initial Round 2 questions posed mechanics as undefined; they weren't.
    design/PARKED-2026-06-03.md locked most v1 design on 2026-06-02.
  - Round 2 corrections committed in c73ba19.
  - Round 3 (this revision): Starbright failed AGAIN to read the
    existing PLAYER_GUIDE.md (20KB locked design) and design/nomap.md
    (20KB patch-surface cross-ref) before posing Round 3 questions.
    Re-baselined against ALL repo docs now. Most "Round 3 answers"
    were partially or fully already in PLAYER_GUIDE.md. Where Daniel's
    today-answers SUPERSEDE PLAYER_GUIDE.md text, the PLAYER_GUIDE
    needs a follow-up doc-PR (tracked at bottom of this file).
  - Process lesson: read EVERY *.md in repo before EVERY shaper round,
    not just stage 1. Skill patched (commit pending).
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

#### A2.3 — Orienteering Table (LOCKED — kitbash for playtest)

⚠️ **NAME CORRECTION**: this section was originally written "Explorer's Bench" — that was Starbright inventing a name today. The locked station name from `PLAYER_GUIDE.md` + `design/nomap.md` is **Orienteering Table**. See the dedicated Orienteering Table section below Round 3 for the full locked definition. Brief summary for Round 2 closure:

**v1 approach:** kitbash the vanilla Workbench. Tier 1 reuse — vanilla Workbench mesh + Trailborne material tint + visual props (half-rolled hide-map + bone-needle-in-stone-disk per `design/nomap.md` §1). Trailborne recipes register as new tabs on the Orienteering Table (its own CraftingStation, NOT the vanilla Workbench).

**v1.1+ path:** graduate to a visually-distinct mesh once mechanics validate. Retains thematic anchor (own recipe, own discovery moment).

**Recipe (v1, from `design/nomap.md` §1):** 20 Wood + 4 Stone + 4 Bone fragment + 2 Greydwarf eye + 2 Deer hide.

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

### Round 3 — Open mechanical questions ✅ CLOSED (Daniel's answers + repo re-read)

**CRITICAL re-baseline:** Before this round, Starbright re-read `PLAYER_GUIDE.md` (20KB locked design) and `design/nomap.md` (20KB patch-surface cross-reference) which she had not read since session start. The crafting station is called **Orienteering Table**, not "Explorer's Bench" — that name was a hallucination today. Updating throughout.

#### A3.1 — Cairn build cost (NOT YET ANSWERED)

Daniel: "we covered build costs. find the answer or ask me again. Don't guess."
Starbright searched: `design/PARKED-2026-06-03.md`, `design/nomap.md`, `design/pin-sharing.md`, `PLAYER_GUIDE.md`, `README.md`, and the holographic fact store. **Cairn per-tier build cost is NOT in any current repo doc or fact entry.** What IS in the repo:
- 3/4/5/6/7 stone comfort *floor* per tier (PARKED-2026-06-03.md)
- Repair cost: flat 3 stone + 1 resin (PARKED-2026-06-03.md)
- Downgrade@25%, collapse@0% (PARKED-2026-06-03.md)
- Pigment+banner persist across rebuilds (PARKED-2026-06-03.md)

But NOT the per-tier build cost. **Re-asking Daniel as instructed. ⏳**

#### A3.2 — Blue pigment Meadows-availability ✅ LOCKED

Daniel: "no, blueberries it is. V1"

**Decision:** Blue pigment recipe is `1 blueberry → 2 blue pigment` per `PLAYER_GUIDE.md` line 238. Blueberries are Black Forest tier and that's acceptable — players will craft Blue pigment once they push into Black Forest, even within v1's "Meadows progression" framing. This means v1 effectively spans Meadows through early Black Forest for the pigment ladder, while keeping all the *pieces* themselves Meadows-tier-craftable (just with Blue pigment requiring the player to walk to a blueberry bush).

**Pigment recipes (v1, locked):**
- Red: 1 raspberry → 2 red pigment
- White: 1 bone fragment → 2 white pigment
- Black: 1 coal → 2 black pigment
- Blue: 1 blueberry → 2 blue pigment

Yellow (cloudberry, Plains tier) is v5+ and explicitly NOT in v1.

#### A3.3 — Path Lamp chain-ignition ✅ DROPPED

Daniel: "this isn't really a thing we discussed"

**Decision:** Chain-ignition was a Starbright-invented mechanic that did not come from any prior discussion. Dropping it. v1 Path Lamps work like vanilla torches — manual ignition, no chain effect.

**Path Lamp mechanics (v1, locked):**
- Recipe: Wood + Resin (exact quantities TBD, mirror vanilla torch shape)
- Light source: dimmer than vanilla torch (trail-illumination, not base-illumination)
- Fuel duration: longer than vanilla torch (evidence-of-trail shape, not maintenance burden)
- Per PLAYER_GUIDE: "3m corewood torches, resin-fueled, long burn" — note this hints at corewood not regular wood; needs Round 3.5 confirmation on recipe materials
- Ember Lamps (eternal, ember-fueled) are Black Forest tier per PLAYER_GUIDE and may or may not be v1 — currently in scope per the "v1 = Meadows through early Black Forest" pigment-ladder framing

#### A3.4 — Trailblazer's Tools recipe ✅ LOCKED (corrected from Starbright's guess)

Starbright proposed: 5 Wood + 2 Stone + 2 LeatherScraps
Daniel corrected: **"Leather Hides not scraps. Flint, not stone. So 5w/2f/2h"**

**Trailblazer's Tools recipe (v1, locked):** 5 Wood + 2 Flint + 2 Leather Hides

Note: PLAYER_GUIDE.md line 67 says "wood, tin, flint" for Trailblazer's Tools recipe. Daniel's today-answer SUPERSEDES that — the correct v1 recipe is wood/flint/leather hides, no tin. PLAYER_GUIDE needs a doc-PR followup.

Craft station: Orienteering Table (per PLAYER_GUIDE.md — Trailblazer's Tools are crafted at the Orienteering Table, not the Workbench).

#### A3.5 — Cairn resin re-ignite on repair ✅ LOCKED

Daniel: "it reignites if the cairn is in the 'pristine' piece state rather than the lower tiers of wear and tear. It's just a visual on the wear and tear system. 75% threshold as discussed to 'fizzle out'"

**Cairn resin glow mechanic (v1, locked):**
- Cairn HP state mapping:
  - **≥75% HP** = pristine — resin glows (visual)
  - **<75% HP** = fizzled — resin no longer glows (visual signal: "this cairn needs maintenance")
  - **<25% HP** = downgrade tier (per PARKED-2026-06-03.md)
  - **0% HP** = collapse (per PARKED-2026-06-03.md)
- Re-ignite trigger: AUTOMATIC when HP returns to ≥75% via repair. **No player action required.** Purely a visual on the wear-and-tear system.
- Implementation surface: postfix on `WearNTear.OnDamage` (and/or `OnRepair`) to update the cairn's ParticleSystem `enableEmission` based on HP threshold.

---

### MAJOR NAME CORRECTION

The crafting station is **Orienteering Table**, not "Explorer's Bench."

I (Starbright) invented "Explorer's Bench" today as if from scratch when posing Round 2 — *despite* the actual name being locked in `PLAYER_GUIDE.md` and `design/nomap.md` since at least 2026-06-02. Apology to Daniel. Correcting throughout this document.

**Orienteering Table (Meadows, locked, v1):**

| Aspect | Value |
|---|---|
| Name | Orienteering Table (NOT Explorer's Bench) |
| Recipe | 20 Wood + 4 Stone + 4 Bone fragment + 2 Greydwarf eye + 2 Deer hide (from `design/nomap.md` §1) |
| Function | Crafting hub for all Trailborne pieces and Trailborne items |
| Piece category | `PieceCategory.Crafting` |
| Implementation pattern | Clone `piece_workbench` prefab → name `SBPR_OrienteeringTable`, add `CraftingStation` component with `m_name = "$sbpr_piece_orienteering"`. Per `design/nomap.md` §1: "**Patch surface: none — pure prefab work.**" |
| v1 visual approach | Per Daniel today: "kitbash the workbench a bit for the playtest" — Tier 1 reuse, vanilla workbench mesh with minor visual differentiation (material tint, kit-bashed surface props from `design/nomap.md`'s "half-rolled hide-map + bone needle stuck in a stone disk" hint) |

---

### Round 4 — Reusability scan against decomp + wiki (NEXT)

`design/nomap.md` already has substantial decomp-surface work done — Starbright must reference it (lines + classes + methods) before doing fresh scans. Planned scans:
- **Cross-reference `design/nomap.md` first** (it has Minimap, Hammer/Hoe, Sign, Fireplace, TeleportWorld, ZoneSystem, ObjectDB, etc. line-references already done)
- Decomp scan for `WearNTear` (for cairn resin glow + decay implementation)
- Decomp scan for `SE_Rested.CalculateComfortLevel` (for cairn comfort patch)
- Decomp scan for `MapTable` (for v1 disable mechanism)
- Wiki grep for: Raspberries, Bone fragments, Coal, Resin (for pigment recipe input availability per biome confirmation)
- Wiki grep for: Banner (for cairn comfort comparison)
- Wiki grep for: Cartography Table (for disable-surface and player-expectation calibration)
- Wiki grep for: Torch (for Path Lamp Tier 1 reuse pattern + fuel mechanics)

---

### Round 5 — Visual assets
*(NOT YET ASKED — will ask after Round 4)*

---

### Round 6 — Scope boundaries / out-of-scope
*(NOT YET ASKED — will ask after Round 4)*

---

### Round 3.5 — Remaining open questions for Daniel (asked this turn)

**Q3.6 — Cairn per-tier build cost** (re-asked per Daniel's instruction): The 3/4/5/6/7 stone *comfort floors* are locked. What's the per-tier *build cost*? Are they the same number (tier-1 cairn = 3 stones to build, tier-5 = 7 stones)? Or a separate cost ladder?

**Q3.7 — Path Lamps wood material** (raised by repo re-read): PLAYER_GUIDE.md line 110 says Path Lamps are "3m corewood torches, resin-fueled, long burn." Corewood is Black Forest tier (cut from pines). Does that hold? — i.e. Path Lamps are *technically* Black Forest tier despite being introduced under the Meadows section because the player will need to walk to pines to craft them. Or do v1 Path Lamps use regular wood and corewood is a v1.1 graduation?

**Q3.8 — Ember Lamps in v1 yes/no:** PLAYER_GUIDE.md introduces Ember Lamps as a Black Forest piece (eternal, ember-fueled, reddish glow). They're a clear v1 piece IF we're shipping "v1 = Meadows through early Black Forest" framing. Are Ember Lamps in v1, or v1.1?

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

1. **Orienteering Table** (Meadows, v1 = kitbash vanilla Workbench, recipe 20W+4Stone+4BoneFrag+2GreydwarfEye+2DeerHide per design/nomap.md §1)
2. **Cairns** — 5-tier comfort floor 3/4/5/6/7, mandatory decay, ≥75% pristine (resin glows) / <75% fizzled / <25% downgrade / 0% collapse, repair flat 3 stone + 1 resin, pigment+banner persist, auto-re-ignite glow on repair-to-pristine, per-tier *build cost* TBD (Q3.6)
3. **Pigments** — R/W/B/Blue, 2/craft, stack 20, weight 0.1, recipes: R=raspberry, W=bone fragment, B=coal, Blue=blueberry (1:2 each)
4. **Painted Signs** — vanilla sign variant + E/Shift+E color binding + two-tone pins (no-op if nomap=ON), default pin keybind TBD
5. **Trailblazer's Tools** — single tool item, hoe/hammer-equivalent, 1.5/3/5m path widths, Replant Grass same radii, Clear Vegetation wide-radius, recipe **5 Wood + 2 Flint + 2 Leather Hides**, crafted at Orienteering Table
6. **Path Lamps** — Wood + Resin (corewood-vs-regular-wood TBD per Q3.7), dimmer than torch, longer fuel, manual ignition (no chain ignition — that was hallucinated)
7. **Map disable in v1** — Cartography Table disabled (no build, no functionality on existing); nomap=ON → no map; nomap=OFF → minimap only (no M-key, no north indicator)
8. **Ember Lamps?** — Q3.8 pending Daniel decision (in v1 or v1.1)

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

- **Q3.6: Cairn per-tier build cost** — `design/PARKED-2026-06-03.md` has the comfort *floors* (3/4/5/6/7 stone) but not the build *cost* ladder. Daniel says we discussed it; not in repo or facts. Asking him to recall.
- **Q3.7: Path Lamp wood material** — `PLAYER_GUIDE.md` line 110 says corewood (Black Forest tier). Confirm corewood-not-regular-wood, OR v1 uses regular wood with corewood being a v1.1 graduation.
- **Q3.8: Ember Lamps in v1 yes/no** — early Black Forest piece per PLAYER_GUIDE. Currently uncategorized for v1 scope.
- Round 4 decomp/wiki scans pending (will leverage `design/nomap.md`'s existing line-references first)
- Round 5 visual assets pending
- Round 6 out-of-scope confirmation pending

## PLAYER_GUIDE.md doc-PR follow-up tracker

After spec finalization, the following PLAYER_GUIDE.md updates are needed to keep it consistent with this requirements.md (which is the authoritative v1 spec going forward):

1. **Trailblazer's Tools recipe** — line 67 currently says "wood, tin, flint". Daniel's today-answer: 5 Wood + 2 Flint + 2 Leather Hides. No tin in v1. Update.
2. **v1 Cartography Table behavior** — PLAYER_GUIDE.md §"Cartography Table (vanilla) — but rebalanced" describes the regional-observation-post model. That's actually the v2 **Map Station** shape. In v1, vanilla Cartography Table is DISABLED, not "rebalanced." Either move that section to a future-v2 doc or annotate it as "v2 design" inline.
3. **Path Lamps material confirmation** — line 110 says corewood; needs Daniel confirm (Q3.7 above) before doc lock.
4. **Pin button keybind** — line 253 says "default keybind _TBD_" for Painted Sign pin trigger. Spec needs a default; PLAYER_GUIDE doc will inherit.
5. **Cairn lifecycle prose** — PLAYER_GUIDE references "the way Cairns are maintained" in Guardian Stones forward-pointer (lines 351-353). The Cairn lifecycle is now fully specified here in requirements.md (3/4/5/6/7 comfort floor, 75% pristine glow threshold, 25% downgrade, 0% collapse, 3 stone + 1 resin flat repair). PLAYER_GUIDE should get a brief Cairn lifecycle section in §Meadows so the Guardian Stones forward-pointer has something to point at.

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
