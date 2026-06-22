---
title: "Sunstone Lens detection — the eidetic trophy ring (world-space head-halo render, supersedes the screen-space radar)"
status: current
purpose: "Architect render-design for the Sunstone Lens' standalone (no-minimap) detection surface: a DIEGETIC, world-space head-halo of creature TROPHIES floating around the player — real world bearings (camera-relative, never north-up), a FIXED ring distance with scale-only range cue (full ≤10m to 0.25 at the detection edge, the 10m knee; bug-fix t_10bacccf 2026-06-22 supersedes the earlier variable-radius+scale lock), vanilla nameplate star pips, aggro-state tint. As of 2026-06-21 this SUPERSEDES the earlier screen-space camera-relative radar (§Q2/§5, card t_b8a19487) on Daniel's eidetic request, modelled on Rune Magic's Rune of Alertness. The detection MECHANIC (who/when — SunstoneLens.GatherHostiles, the energy model, the equip-gate) and the SunstoneProjection→ThreatBlip derivation are UNCHANGED; only the standalone-ring RENDER SURFACE is redesigned. The minimap surfaces (SBPR carry-disc, vanilla corner map) are DECOUPLED and untouched. Every vanilla hook line-cited against assembly_valheim. Card t_68672b6b (this redesign) supersedes the §Q2/§5 lock in t_b8a19487; Daniel pre-approved the four knobs (\"just implement as directed\")."
---

# Sunstone Lens detection — the eidetic trophy ring (world-space)

> # 🐛 GEOMETRY RE-LOCK (2026-06-22, bug-fix card t_10bacccf) — Knob #2 is now a FIXED-distance ring + scale-only range cue (10m knee), NOT variable radius+scale
>
> **The shipped world-halo (PR #242, card t_d17d9b58) used a VARIABLE radius AND scale, both ∝
> distance** (Knob #2 as originally locked below). In play that pushed a far enemy to the **outer**
> radius (away from your face) **and** shrank it toward ~0.12 world-units — far + tiny = effectively
> invisible. Daniel reported it (Discord `ticket-diegetic-halo-render`, 2026-06-22), verbatim:
> > *"creatures should be at a fixed distance from the player but grow in scale from .25 at the far
> > edges to 1.0 when within 10m. Right now they're seemingly too far from the player to be clearly
> > visible."*
>
> **Knob #2 is re-locked (Daniel is the design authority on this report):**
> - **Placement is at a FIXED distance.** Every trophy renders equidistant from the eye-point — a
>   true fixed-radius ring. The old `HaloRadiusMin`/`HaloRadiusMax` collapse to a single
>   **`HaloRadius`** (directional start ~2.0m, AT-gated — Daniel eyeballs the metres on a GPU client).
>   **No outward push for far enemies.**
> - **SCALE carries ALL the distance info**, with a **10m knee**: enemy **≤10m → full scale** (the
>   locked **"1.0"**); enemy at the **50m detection edge → 25% scale** (the locked **"0.25"**); linear
>   between. `scaleNear` (the absolute world-size "1.0" maps to) stays the AT-gated tunable
>   (`HaloScaleMax` ≈ 0.6, kept); the old `HaloScaleMin` is **derived** (`0.25 × scaleNear`), not an
>   independent knob. The **10m knee + 0.25 floor + 1.0 ceiling are LOCKED**.
>
> **What this re-lock does NOT change:** placement is still along the **real `dirToEnemy` bearing**
> (camera-relative, the thesis guard — §Q2 #1, AT-EIDETIC-CAMREL); still **flat billboarded trophies**
> (Knob #4); still the **hybrid trophy-less** policy (Knob #3); still the **aggro tint** + **star
> pips**. Only the radius (variable → fixed) and the scale curve (linear-over-0..50m →
> 10m-knee) change. **§Q2 Knob #2, §1.2, §1.3 below are rewritten for this**; the historical
> variable-radius prose is struck through / marked SUPERSEDED so the reversal is auditable. The pure
> fixed-distance + knee math lives in the engine-free, **CI-gated** `SunstoneHaloGeometry`
> (AT-HALO-FIXED-DIST / AT-HALO-SCALE-KNEE) so it cannot silently regress again.

> # 🔄 RENDER REVERSAL (2026-06-21, card t_68672b6b) — the standalone ring is now DIEGETIC (world-space 3D), not a flat HUD radar
>
> **This doc was a screen-space camera-relative radar.** Daniel reversed that top-level
> choice (Discord #bugs, ticket-sunstone-eidetic-ring), verbatim:
> > *"I'd like to make the standalone sunstone ring 'eidetic' in that I'd like it not to appear
> > as a flat UI layer, but rather as a 3d model of a ring around the player similar to how
> > 'Rune magic' mod does for the rune of alertness functionality."*
>
> The earlier doc explicitly deferred world-space (`§Q2`: *"if Daniel specifically wants
> diegetic 3D later it's a separate card"*). **This is that separate card, and it is now
> LOCKED the opposite way:** the standalone (no-minimap) ring is a real **world-space head-halo
> of billboarded trophies** floating around the player at their true bearings. Sections **§Q2,
> §1, §3 (the ATs, renamed AT-EIDETIC-\*), §4, §5** below are rewritten for that. The
> **detection mechanic, the `SunstoneProjection`→`ThreatBlip` derivation, §Q1/§Q4/§Q3, and the
> §2 wiring audit are UNCHANGED** and carried verbatim.
>
> **Scope guard — STANDALONE means the no-minimap FALLBACK surface ONLY.** When a minimap is
> present (SBPR carry-disc nomap-ON, or vanilla corner map nomap-OFF) detection hands off to it
> and the ring hides (`MinimapHandoffMode`, default `DiscWhenBound`). The minimap surfaces draw
> flat icons via **separate code** (`SunstoneMinimapThreatLayer`, `MapSurface.SpawnThreatMarker`)
> and are **untouched by this change** — only `RenderRing` + the slot scaffolding in
> `SunstoneLensHudOverlay.cs` is rewritten. (The minimap *representation* is its own in-flight
> card, `t_aab051ae`; same `SunstoneProjection` source of truth, no collision.)
>
> **The four open knobs are LOCKED** (Daniel answered them, then said *"just implement as
> directed"* — doc-review gate WAIVED): (1) **occlusion** → head-halo placement (the "Rune Magic
> dodge", rarely occluded, no through-terrain material); (2) **geometry** → 🐛 **re-locked by
> t_10bacccf (2026-06-22): a FIXED-distance ring + scale-only range cue with a 10m knee** (the
> original t_68672b6b lock was variable radius AND scale ∝ distance, which hid far enemies — see the
> GEOMETRY RE-LOCK banner above); (3) **trophy-less** → hybrid variant→sibling
> remap table + generic fallback, with a **default-ON startup dump** of unmapped creatures for
> review; (4) **trophy render** → **flat billboarded sprites** (the existing `m_icons[0]`), NOT
> 3D `attach` meshes. See §Q2 and §5 for the locked rationale.
>
> The historical screen-space rationale is preserved inline (struck through / marked SUPERSEDED)
> so the reversal is auditable, not erased.

---

Daniel's original intent (2026-06-18), verbatim — the trophy/size/bearing/stars core that
SURVIVES the reversal (only the *surface* — screen-space → world-space — changed):

> "the sunstone lens is supposed to display a monster's **trophy** facing the camera with
> its **size proportional to its closeness** to the player at a **fixed distance in a ring
> around the player** showing the **direction** of the enemy with **little stars above their
> heads if they have star levels**"

This doc is the buildable render redesign for that. It **replaces** the render surface only —
the shipped `SunstoneLensHudOverlay.cs` (PR #163) is a bottom-center text readout ("⚠ N
hostiles near · nearest 12m ◄ · charge 80%") whose own header calls it *"a FUNCTIONAL
placeholder indicator (text + a simple arrow glyph)… polished threat-overlay art is a v0.x
follow-up."* Daniel has now given the real design; this is that follow-up.

> **⚠ CONDITIONAL since the minimap-handoff (2026-06-20, design ACCEPTED):** the
> camera-relative trophy ring is now the **no-minimap FALLBACK surface only.** When
> a minimap is present — the SBPR carry-disc (nomap-ON) or the vanilla corner map
> (nomap-OFF) — Lens detection moves onto it and the ring hides (default
> `MinimapHandoffMode = DiscWhenBound`). The ring renders only when no minimap
> exists at all. See [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)
> and the impl-spec [`../v3/planning/sunstone-minimap-handoff-impl-spec.md`](../v3/planning/sunstone-minimap-handoff-impl-spec.md).
> Everything below still describes the ring faithfully — it is just no longer the
> *only* surface.

> **What is NOT changing (load-bearing — do not re-litigate).** The detection *mechanic* is
> correct and stays as-is: `SunstoneLens.GatherHostiles` (the `Character.GetAllCharacters` +
> `BaseAI.IsEnemy` hostile sweep), the solar-battery energy model (`DrainGate` prefix on
> `Humanoid.DrainEquipedItemDurability`), the Trinket equip-gate (`GetEquippedLens`), the
> recharge predicate (`CanRecharge`), and all `SunstoneLens` config knobs. This card swaps the
> **`SunstoneLensHudOverlay` render** from text to the trophy ring. Everything upstream of
> "here is the live `List<Character>` of hostiles + the charge %" is untouched.

> **Clean-side note (ADR-0001):** every decomp line cited here is base-game `assembly_valheim`,
> fair game to read and adapt (repo AGENTS.md + the 2026-06-09 clarification). Line numbers are
> from `/home/polyphonyrequiem/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`,
> grepped live this pass — re-confirm if the decomp drifts. The trophy-ring render is net-new
> SBPR fiction reproduced from vanilla primitives only; no third-party mod code was read.

> **ADR-0006 (load-bearing):** the standalone ring is built **additively** — `new GameObject()`
> + `AddComponent<MeshRenderer>()`/billboarded quad for each world-space slot (a client-local
> cosmetic with **no** `ZNetView`/`Piece`/networked skeleton), reusing the trophy items' own
> `m_icons[0]` sprites on the quad's material (reading a sprite is not cloning). This is exactly
> the "visuals are constructed GameObjects carrying only MeshFilter/MeshRenderer" pattern ADR-0006
> blesses (`0006-additive-prefab-construction.md:60-63`) — **NOT** a clone-and-strip of a vanilla
> prefab. The minimap surfaces keep their own additive `Image` scaffolding unchanged.

---

## 0. The four grounding questions — RESOLVED against the decomp (+ the world-space reversal)

The card posed four questions to resolve before impl. **§Q1 (trophy mapping), §Q3 (doc
reconciliation), §Q4 (star read) are decomp facts and are UNCHANGED by the world-space
reversal — they carry verbatim below.** **§Q2 was the diegetic-3D vs screen-space fork; it is
now LOCKED to world-space** (Daniel's eidetic request, t_68672b6b) and rewritten below. The
four *render* knobs that the world-space port opens (occlusion, geometry, trophy-less, mesh-vs-
sprite) are LOCKED in §Q2 and detailed in §5.

### Q1 — creature → trophy mapping + the trophy-less fallback

**Mapping (grounded):** a creature's trophy is an entry in its **`CharacterDrop`** component
(`CharacterDrop : MonoBehaviour`, `:11318`; `public List<Drop> m_drops`, `:11340`). Each
`Drop` (`:11321`) has `public GameObject m_prefab` (`:11323`). The trophy is the `m_prefab`
whose `ItemDrop.m_itemData.m_shared.m_itemType == ItemType.Trophy` (enum **13**, `:57641`).
The trophy's billboard sprite is that prefab's `ItemDrop.m_itemData.m_shared.m_icons[0]`
(`m_itemData` is **public**, `:58641`; `m_icons` is `public Sprite[]`, `:57719`).

- **Resolve once, cache by prefab name.** A creature's `CharacterDrop` is fixed per
  prefab, so resolve `creatureName → trophySprite` lazily on first sighting and cache it in a
  `Dictionary<string, Sprite>` (keyed by `StripCloneSuffix(creature.name)`). Never walk
  `CharacterDrop` every frame — resolve, cache, reuse. A creature with no `CharacterDrop` or no
  Trophy-typed drop resolves to `null` once and caches the null (so we don't re-scan it).
- **This is how vanilla itself identifies trophies** — `Player.AddTrophy` (`:20193`) keys on
  `m_shared.m_itemType == Trophy` then takes `item.m_dropPrefab.name` as the identity. We use
  the same identity surface from the other direction (creature → its trophy drop).

**Fallback for trophy-less hostiles (Q1's open sub-question — now LOCKED to a HYBRID, Daniel
2026-06-21).** Not every hostile has a trophy (Greyling — drops Resin only, no Trophy entry,
wiki `Greyling.md`; summoned/spawned minions; some boss adds). Daniel locked a **hybrid** of
three layers, modelled on (but reproduced independently of) Rune Magic's approach (§"Behavioral
reference" below):

1. **Variant→sibling REMAP table (new).** A hardcoded `Dictionary<string,string>` maps a
   trophy-less variant onto a sibling species that *does* carry a Trophy drop, so the variant
   wears the sibling's trophy. **Greyling → Greydwarf** is the canonical first entry (a Greyling
   shows a Greydwarf trophy). Seed it with the obvious Swamp/early-game variants
   (Greyling→Greydwarf, Boar_piggy→Boar, DraugrRanged→Draugr, Wolf_cub→Wolf, Lox_Calf→Lox, and
   similar); the table is data, growable by Daniel from the startup dump (layer 3).
2. **Generic 3D fallback (keep SBPR's "never skip" intent).** Anything still unmapped after the
   remap renders the bundled **generic threat glyph** (`threat_fallback_v0.1.png`) as a
   billboarded quad in the trophy's place — same head-halo placement, distance-scale, aggro-tint,
   star pips. The fallback is a *defined slot, never a skip*: a trophy-less hostile still appears
   at its correct world bearing/size, just wearing the generic glyph. **This is where SBPR
   deliberately diverges from Rune Magic** (which *omits* unmapped trophy-less creatures) — we
   never drop a threat.
3. **Default-ON startup unmapped-creature DUMP (new sub-knob, Daniel 2026-06-21).** A config
   `DumpUnmappedCreatures` (**default ON**): at `ZNetScene`-ready, enumerate **all** registered
   Character prefabs (`ZNetScene.instance.m_prefabs`, public `:69091`), resolve each to
   (trophy | remap-sibling | none), and log the **"none" set as one reviewable block** so Daniel
   can grow the remap table over time. This is a **full-catalog scan at startup**, NOT a lazy
   log-on-first-sighting trickle (Rune Magic resolves lazily) — "on startup … for review" means
   the complete list in one place. (Filter the enumeration to prefabs carrying a `Character`
   component; skip non-creature prefabs.)

The per-creature trophy resolution + cache (`ResolveTrophySprite`, the `CharacterDrop`→
`ItemType.Trophy`→`m_icons[0]` walk) is UNCHANGED; the remap table is consulted *before* caching
a null (remap key → resolve the sibling's sprite → cache under the original key).

### Q4 — star detection read

**Use `Character.GetLevel()` (public, `:7417`), NOT the private `m_level` field.** The card
wrote `m_level==2 → ★`, `m_level==3 → ★★` — the *values* are right but `m_level` is **private**
(`:7163`, default 1) and won't compile from an overlay. The public getter is `GetLevel()`
(`:7417`); the setter is `SetLevel(int)` (`:7403`). Star count = **`GetLevel() - 1`**:
level 1 → 0 stars, level 2 → ★, level 3 → ★★. This is exactly vanilla's own convention —
`CharacterDrop.GenerateDropList` (`:11375`) computes the level drop-multiplier as
`Mathf.Pow(2f, m_character.GetLevel() - 1)`, i.e. `GetLevel()-1` IS the star count. Render that
many ★ pips above the trophy. Cap the rendered pips at the value `GetLevel()` returns (don't
assume a max of 2 — modded/event creatures can exceed 2 stars; render N pips for N stars).

### Q2 — diegetic world-space ring vs screen-space HUD radar — 🔄 LOCKED: WORLD-SPACE (Daniel 2026-06-21)

Daniel said "a ring around the player." That phrase had two readable implementations, and this
was the one genuine design fork in the card. **It is now LOCKED to the diegetic world-space
reading** (card t_68672b6b, Daniel's "eidetic" request modelled on Rune Magic's Rune of
Alertness). The standalone (no-minimap) ring is a **head-centric halo of billboarded trophy
sprites floating in the 3D world** around the player, at the enemies' real bearings.

> **SUPERSEDED — the earlier screen-space recommendation (card t_b8a19487, kept for audit).**
> The prior architect recommended *screen-space HUD radar* and rejected world-space for v0.1 on
> four costs (billboard math, occlusion/ZTest, a GameObject pool, depth-sorting), calling it
> "Twisted Portal through-terrain-overlay tier" work for marginal benefit. Daniel chose the
> opposite. The four costs re-assess as cheap against grounding (next), and the head-halo
> placement dissolves the occlusion cost entirely, so the original objection no longer holds.

**Why world-space is cheaply buildable now (the four old costs, re-graded):**

| Old-doc cost | Grounded verdict |
|---|---|
| per-frame billboard math | **SOLVED** — vanilla `Billboard : MonoBehaviour` (decomp `:99987-100021`): `LateUpdate` does `transform.LookAt(Utils.GetMainCamera())`, `m_vertical=true` by default (yaws to camera, stays upright). Base-game, fair to read+adapt (ADR-0001). `AddComponent<Billboard>()` or reproduce its ~6-line LookAt. |
| occlusion / ZTest (hide behind terrain) | **DISSOLVED by the locked head-halo (Knob #1).** Trophies float in a tight halo around the player's eye-point, rarely occluded — **no through-terrain material needed.** Honest depth is fine because the halo sits close to the camera. |
| pool of world GameObjects | **MITIGATED** — reuse the shipped `_slots` pool pattern; never create/destroy per frame (the same discipline the screen-space `_slots` already used). |
| depth-sorting | **DISSOLVED** in true 3D — the GPU depth-sorts world geometry natively; it was a screen-overlay problem only. |

**The four render knobs the world-space port opened are LOCKED (Daniel 2026-06-21, then "just
implement as directed"):**

1. **Occlusion → head-halo "Rune Magic dodge".** Trophies float in a tight halo around the
   player's eye-point (`Character.GetEyePoint()`, public `:8655`), rarely occluded; no special
   material. (Collapses the occlusion fork into the placement choice — Knob #1 and #2 are one
   decision.)
2. **Geometry → FIXED-distance ring + scale-only range cue (10m knee).** 🐛 **Re-locked by
   bug-fix t_10bacccf (2026-06-22)** — supersedes the original t_68672b6b lock of *"head-centric
   halo with VARIABLE radius AND scale, both ∝ distance"* (struck through below), which pushed far
   enemies to the **outer** radius (away from your face) **and** shrank them toward ~nothing, so a
   30–50m hostile read as far + tiny = invisible (exactly Daniel's report). The model is now:
   `pos = eyePoint + dirToEnemy * HaloRadius` (a **single fixed** distance for every trophy — no
   range-dependent push), and `scale` carries all the range info through a **10m knee**:
   `k = 1 - Clamp01((dist - 10) / (detectRadius - 10)); scale = Lerp(scaleNear·0.25, scaleNear, k)`
   — enemy **≤10m → full** (the "1.0" → `scaleNear`), enemy at the **50m edge → 0.25·scaleNear**
   (the "0.25"), linear between. The **10m knee + 0.25 floor + 1.0 ceiling are LOCKED** (Daniel's
   numbers). `scaleNear` (= config `HaloScaleMax` ≈ 0.6) is the AT-gated eyeball tunable; the old
   `HaloScaleMin` is **derived** (`0.25·scaleNear`), not an independent knob; `HaloRadiusMin/Max`
   collapse to one `HaloRadius`. (§1.2/§1.3 below are rewritten accordingly; the pure math is the
   CI-gated `SunstoneHaloGeometry` — AT-HALO-FIXED-DIST / AT-HALO-SCALE-KNEE.)

   > ~~**SUPERSEDED (original t_68672b6b lock):** head-centric halo with VARIABLE radius AND scale,
   > both ∝ distance — `pos = eyePoint + dirToEnemy * Lerp(radiusMin, radiusMax, dist/maxDist)`,
   > `scale = Lerp(scaleMax, scaleMin, dist/maxDist)`; near enemies at the inner radius and big, far
   > enemies pushed to the outer radius and shrunk toward nothing.~~ This is the design Daniel
   > reversed in t_10bacccf because the outward push + shrink made far enemies invisible.
3. **Trophy-less → hybrid** (variant→sibling remap table + generic 3D fallback + default-ON
   startup dump) — see the §Q1 rewrite above.
4. **Trophy render → FLAT billboarded sprites for ALL trophies** (the existing `m_icons[0]` on a
   world-space billboarded quad), **NOT** the 3D `attach` mesh. This is the lower-risk,
   higher-reuse choice: it sidesteps the entire per-creature mesh-tuning burden (Rune Magic carries
   a big per-species scale/rotation/offset override table precisely because raw trophy meshes don't
   billboard cleanly), and it maximizes reuse — the existing star-pip + trophy-tint rendering is
   already sprite-based, so the change is "reparent the existing sprite from a screen-space
   `RectTransform` to a world-space billboarded quad + swap fixed-radius placement for the
   head-halo," NOT a mesh-harvest pipeline. Net result: trophy *cards* orbiting the head rather
   than sculptural 3D props — still fully diegetic (world-space, distance-scaled, real bearings),
   and flat trophy icons are legible silhouettes (good for Daniel's by-eye/colorblind validation).
   (A sculptural 3D-mesh look is a future follow-up if Daniel wants it; flat-for-v1 is coherent
   and far cheaper.)

> 🔴 **The thesis guard — UNCHANGED and now NATURALLY satisfied.** The ring is **camera-relative,
> NOT north-up.** In world-space this falls out for free: the trophies ARE at the enemies' real
> world bearings around you, so turning the camera sweeps them exactly as a camera-relative ring
> should. **The architect/engineer must NOT "improve" this into a north-locked halo** — that would
> hand the player cardinal orientation, the Iron Compass's *exclusive* withheld payoff
> (`iron-compass-impl-spec.md` "the withheld orientation IS the design"). A north-up lens halo
> would delete the Compass's reason to exist and reverse a Daniel-locked no-north difficulty. The
> lens answers *"where is the threat relative to me,"* never *"where is north."* (The world
> bearing is intrinsically relative-to-you, not relative-to-north — so as long as we place by the
> real `dirToEnemy` and never inject a cardinal frame, the guard holds by construction.)

### Q3 — reconcile with the existing design + impl docs

- **`docs/design/swamp-detection-item.md` (PR #144)** is the IDEA note — theme/material/sourcing
  only. It explicitly leaves the render surface open ("if the minimap is off by default, the
  reveal needs its own surface"). Nothing in it describes the ring. **No conflict; no edit
  needed** beyond this doc superseding its render-open-question.
- **`docs/v3/planning/sunstone-lens-impl-spec.md`** is the buildable spec. Its §4 ("Render
  v0.1") and §5 ("Render surface under NoMap") describe the *text/arrow placeholder*. **Those
  two sections' render cross-ref banners are re-pointed by this PR** to name the world-space
  head-halo (they already delegate the render to this doc; the banners are updated from
  "screen-space trophy ring" to "world-space eidetic head-halo"), while keeping their
  detection-mechanic + NoMap-doctrine content (still correct). The §8 acceptance tests' render
  half re-points to the new AT-EIDETIC-* rows (below). This is **net-new render design**, not a
  duplicate — the impl spec described detection; this describes how detection is drawn.
- **Dataset (`PIECES_AND_CRAFTABLES.md`)** Lens row "Render" / "Visual notes" / "Config" /
  "Patch surface" / "Status" are updated to name the world-space head-halo render (in this PR).
- **`PLAYER_GUIDE.md`** lens paragraph (currently "Threats appear as a **ring of trophies around
  you** … on a ring at the bearing it's actually in") is reworded (this PR) to describe the
  trophies floating **in the world** around you (the eidetic halo) without over-promising art
  polish.

---

## 1. Render architecture — the world-space eidetic halo

The world halo **replaces the render body of `SunstoneLensHudOverlay`** (`RenderRing` + the
screen-space `Slot`/`Image`/`RectTransform` scaffolding, `SunstoneLensHudOverlay.cs:364-568`),
**not** the host MonoBehaviour or the sweep. **KEEP, unchanged:**
- the `HudBootstrap` `Hud.Awake` postfix mount (already woven, `Plugin.cs:564`),
- the `Update` visibility/charge gate (worn? charged? depleted?) and the throttled
  `SunstoneLens.GatherHostiles` sweep,
- the `SunstoneProjection.Project → List<ThreatBlip>` derivation (tint/trophy/stars + caches),
- the `MinimapHandoffMode` gate (the halo only renders when no minimap is bound).

> 🔴 **Load-bearing invariant — the #209 dead-Update-pump fix MUST survive (see §2.0).**
> `SetVisible` toggles a `_content` CHILD, **never** the host GameObject. Unity stops calling
> `Update()` on a component whose GameObject is inactive, and `Update()` is the only thing that
> pumps the sweep + feeds the minimap surfaces. **In the world-space rebuild the same discipline
> holds:** the host `MonoBehaviour` stays active and keeps pumping; what `SetVisible` toggles is
> now a **world-space content root** (`_worldContent`, a plain `GameObject` parented in the scene,
> NOT under `Hud.m_rootObject`) carrying the billboarded slot pool. The host can even keep its old
> `RectTransform`-under-Hud for the empty/depleted affordance if §1.6 wants a screen element — but
> the detection-feed pump is independent of which surface draws, exactly as today.

> **NOTE on the class's home.** The screen-space ring lived entirely under `Hud.m_rootObject`. The
> world halo's slot objects are **scene objects, not UI** — they live under a `_worldContent`
> GameObject in world space, not under the Hud canvas. The host `MonoBehaviour` may stay attached
> where it is (it's just a pump + lifecycle owner); only the *visuals* move from canvas-space to
> world-space. A new `SunstoneWorldRing.cs` carrying the world-slot pool + billboard + placement
> is a reasonable home so `SunstoneLensHudOverlay` doesn't bloat — engineer's call.

### 1.1 The halo anchor — the player's eye-point

There is **no screen-space ring container** any more. The halo is anchored to the player's
**eye-point** each frame: `eye = player.GetEyePoint()` (`Character.GetEyePoint()`, public
`:8655`, returns `m_eye.position`). Every slot is placed relative to `eye` (§1.2). A small
config vertical offset (`Sunstone.HaloEyeOffsetY`, default ~0) lifts the halo plane slightly so
trophies don't clip the crosshair. Recompute `eye` every frame (the player moves/turns); the
billboard component keeps each trophy facing the camera regardless.

### 1.2 Per-hostile slot — a pooled billboarded quad at a real world bearing

Pool a small list of slot objects (`_slots`), each a `GameObject` carrying a **billboarded quad**
(a `MeshFilter` + `MeshRenderer` with a quad mesh and an unlit transparent material showing the
trophy/glyph sprite as its texture — OR the equivalent world-space-canvas `Image`; engineer's
call, the sprite is the same `m_icons[0]`), plus a `Billboard` component (vanilla
`AddComponent<Billboard>()`, `m_vertical=true` — yaws to camera, stays upright — or a reproduced
~6-line `LookAt(Utils.GetMainCamera())`), plus up to N child star-pip quads. **Pool, don't
create/destroy per frame** — reuse `Mathf.Max(_slots.Count, hostiles.Count)` slots,
`SetActive(false)` the unused tail. Cap the live count at config `Sunstone.RingMaxIcons`
(default ~12) so a horde doesn't spawn 80 objects; if more hostiles than the cap, show the
nearest N (the sweep already has world positions; sort by distance).

**World placement (camera-relative by construction — the thesis guard).** For each hostile:
```
dir   = (blip.WorldPos - eye)           // real world vector to the enemy
dist  = dir.magnitude                    // for the SCALE knee only (§1.3) — NOT for placement distance
dirN  = dir.normalized
pos   = eye + dirN * HaloRadius          // FIXED-distance ring: every trophy sits at the SAME radius on its true bearing
slot.transform.position = pos
// Billboard component handles facing — no manual angle math, no SignedAngle.
```
Because `pos` is placed along the **real** `dir` to the enemy, the trophy is automatically at the
enemy's true world bearing around you — turn the camera and the halo sweeps correctly, with **no
north frame injected** (thesis guard holds by construction; do NOT recompute a `SignedAngle`
against camera-forward and re-project — that was the screen-space hack, unnecessary and risky in
world space). The placement **distance** is the single fixed `HaloRadius` for **every** enemy
(near or far — no range-dependent push, the t_10bacccf fix); `dist` feeds only the SCALE knee
(§1.3). Eye-point/camera-null safe — hide the halo if `player` or `GetMainCamera()` is null.

> **SUPERSEDED — the old screen-space angular placement.** The screen ring computed
> `signed = Vector3.SignedAngle(camForward_flat, toEnemy_flat, up)` and placed a `RectTransform`
> at `(sin·R, cos·R)` on a fixed-radius circle (`SunstoneLensHudOverlay.cs:398-401`). That math is
> **deleted** — world-space placement along the real `dir` subsumes it and is strictly simpler.

### 1.3 FIXED-distance ring + scale-only range cue (the 10m knee, Knob #2 re-locked by t_10bacccf)

🐛 **RE-LOCKED (bug-fix t_10bacccf, Daniel 2026-06-22) — supersedes the variable-radius+scale model
below.** The shipped variable-radius design pushed far enemies to the **outer** radius (away from
your face) **and** shrank them toward nothing, so a 30–50m hostile was far + tiny = invisible.
Daniel's correction: **placement is at a FIXED distance; SCALE alone carries range, with a 10m
knee.**
```
// PLACEMENT — fixed distance for every trophy (no range dependence):
pos   = eye + dirN * HaloRadius                              // single HaloRadius, all enemies

// SCALE — the 10m knee (all the distance information lives here):
k     = 1 - Clamp01((dist - 10) / (DetectRadius - 10))       // 1 at ≤10m, 0 at the detection edge
scale = Lerp(scaleNear * 0.25, scaleNear, k)                 // full ≤10m, 0.25·scaleNear at the edge
```
- **Placement is range-independent.** Every trophy sits at the same `HaloRadius` from the eye-point
  on its real bearing. A near enemy and a far enemy are at the **same distance from your face** —
  only their **size** differs. (This is the whole reversal: no more outward push.)
- **Scale ≤10m is FULL** (`scaleNear`, the locked "1.0"). Closer than 10m does **not** render
  bigger — it's clamped at full so a point-blank enemy doesn't balloon.
- **Scale at the 50m edge is `0.25·scaleNear`** (the locked "0.25" floor) — a readable quarter-size,
  **not** shrunk toward nothing. Linear between the knee and the edge.
- **The 10m knee + 0.25 floor + 1.0 ceiling are LOCKED** (Daniel's numbers). `DetectRadius` is the
  existing `Plugin.LensDetectRadius` (default 50m). `HaloRadius` (world metres, default ~2.0m — a
  tight head-halo, AT-gated) and `scaleNear` (= config `HaloScaleMax` ≈ 0.6, the full-size world
  units) are config. `HaloScaleMin` is **gone** — the edge scale is derived (`0.25·scaleNear`).
  All **live-tunable** so Daniel converges feel in one joined session (the banner-windsock pattern).
  Set the slot transform's `localScale` to `scale` (uniform). The pure curve is the engine-free,
  CI-gated `SunstoneHaloGeometry` (AT-HALO-FIXED-DIST / AT-HALO-SCALE-KNEE).

> The `HaloRadius` is small on purpose — this is a **halo around your head**, not a ground ring at
> detection distance. A 50m Draugr is represented by a small (0.25-scale) trophy ~2m out on its
> bearing, not a trophy literally 50m away (which would be invisible/occluded). That is exactly the
> Rune Magic dodge: the representation lives near the camera, so terrain rarely occludes it and no
> through-terrain material is needed (Knob #1). The **size**, not the distance, tells you how close
> the threat actually is.

> ~~**SUPERSEDED — the original t_68672b6b variable-radius+scale lock:**~~
> ~~`u = Clamp01(dist / DetectRadius); radius = Lerp(HaloRadiusMin, HaloRadiusMax, u); scale =
> Lerp(HaloScaleMax, HaloScaleMin, u)` — near enemies at the inner radius and big, far enemies pushed
> to the outer radius and shrunk toward `HaloScaleMin` (≈0 at the edge).~~ Reversed by t_10bacccf
> because the outward-push + shrink-to-nothing made far enemies unreadable. The `HaloRadiusMin/Max`
> and `HaloScaleMin` config knobs are removed (→ single `HaloRadius`; edge scale derived).

### 1.4 Trophy sprite + the hybrid fallback (Knob #3 LOCKED)

The quad's texture/sprite is `blip.Trophy ?? SunstoneProjection.ThreatGlyph()` — the **same
shared derivation** the disc + vanilla minimap consume (AT-EIDETIC-MINIMAP-UNAFFECTED holds: one
source, divergent surfaces). With the **hybrid trophy-less policy** (§Q1): the projection's trophy
resolution consults the **variant→sibling remap table** first (Greyling→Greydwarf etc.), so most
"trophy-less" hostiles wear a sensible sibling trophy; only the genuinely-unmapped fall through to
the generic `threat_fallback` glyph (never a skip). `preserveAspect`/correct UVs so non-square
trophy icons don't stretch. The sprite is **flat** (Knob #4 — `m_icons[0]`, NOT the 3D `attach`
mesh): a billboarded trophy *card*, not a sculptural prop. No new per-creature art — trophies
reuse vanilla `m_icons[0]`; only the single generic glyph + the remap-table *data* are new.

### 1.5 Star pips above the trophy — REUSE the real vanilla nameplate star art (Daniel, 2026-06-19)

🟢 **DECIDED (Daniel, 2026-06-19):** *"use the Valheim stars used to decorate the monster
nameplates"* — NOT a Unicode ★ and NOT a new authored sprite. Pull the exact star sprite vanilla
draws on enemy nameplates.

`stars = creature.GetLevel() - 1` (§Q4). Render that many star pips parented to the slot, laid out
in a row centered **above** the trophy quad (offset up by `+scale/2 + pipPad` in the slot's local
space, so the row billboards along with the trophy). 0 stars → no pips. Pool the pips per slot and
`SetActive` only as many as `stars`. **(World-space change is parenting only:** the pips are child
quads/world-canvas elements of the billboarded slot instead of `RectTransform` children of a
screen `Image` — the star-sprite *harvest* is identical.)

**Where the vanilla star sprite lives (grounded):** vanilla `EnemyHud` (decomp `:38343`) holds a
public `m_baseHud` GameObject (`:38382`) — the nameplate template. Its children **`level_2`** and
**`level_3`** (resolved at `:38487-38488` via `m_gui.transform.Find("level_2"/"level_3")`) are the
1★ / 2★ decorations, toggled by `SetActive(level==2/3)` (`:38532/:38536`). `EnemyHud.instance` is a
public static accessor (`:38402`). So at runtime we read the star `Image.sprite` off
`EnemyHud.instance.m_baseHud`'s `level_2`/`level_3` child (whichever carries the `Image`), cache it
once, and reuse it — **zero new art, exact vanilla look.** Reading a sprite off a prefab is not
cloning (ADR-0006-safe).

> **Vanilla authors only 1★ and 2★ art** (level_2 / level_3 nameplate children — base game caps
> wild creatures at 2 stars). For `GetLevel()-1 > 2` (modded/event creatures), repeat the 2★
> (`level_3`) star sprite N times so a 3★+ still reads as "very starred." If the harvest fails
> (EnemyHud not yet built, or the child has no Image), fall back to a Unicode ★ `Text` pip so the
> star count is never lost — the look degrades, the information doesn't.

### 1.6 Empty + depleted states (AT-EIDETIC-5)

🟢 **DECIDED (Daniel, 2026-06-19, carried into world-space):** empty halo → show a **faint solar
ring** (not nothing); depleted lens → halo **off**.

- **Zero hostiles:** all trophy slots `SetActive(false)`, but draw a **3D pulsing sun-corona disc**
  so the player can see the lens is live and watching. `Sunstone.ShowEmptyRing` config — **default
  ON**. ~~In world-space this is a thin warm/amber **horizontal halo ring** rendered around the
  eye-point at `HaloRadius` (a flat billboarded annulus, or a line-loop in the halo plane), low
  alpha (~0.18) … *(Engineer may instead keep a screen-space faint ring under `Hud.m_rootObject` …
  either surface is acceptable …)*~~ 🐛 **SUPERSEDED by the `/bug` report on card `t_2d500d45`
  (Daniel, 2026-06-22): "the ring itself is just a screen space circle, not a 3d slowly pulsing 'sun
  corona' disc like we discussed."** The empty-state affordance is now a **world-space, slowly-
  pulsing filled sun-corona disc** — by DEFAULT a flat "sun on the floor" disc (`CoronaOrientation =
  GroundPlane`, live-flippable to camera-facing) co-located with this trophy halo in the **same
  world-content scene root**, breathing on a slow ~0.25 Hz alpha pulse (α ≈ 0.10↔0.28 around the
  old 0.18 baseline). The escape-hatch "keep it screen-space" choice is **RESOLVED to world-space**
  (the flat screen annulus was the literal bug). The corona **is** the substrate the trophies orbit
  (Knob #4: replace, one coherent element). Full buildable detail:
  [`../v3/planning/sunstone-lens-corona-impl-spec.md`](../v3/planning/sunstone-lens-corona-impl-spec.md).
  When ≥1 hostile is present the corona stays (it's the substrate the trophies orbit) — the trophies
  are what draw the eye.
- **Depleted lens (charge < `MinChargeToDetect`):** halo **off entirely** (same as not worn) —
  `Sunstone.ShowDepletedHint` default **OFF** per Daniel. No sweep runs. The durability bar on the
  trinket already signals "dim." The old text "Sunstone Lens — dim" line is dropped.
- **Not worn:** halo hidden, `Update` early-returns (unchanged from current behavior). **The host
  MonoBehaviour stays active** (the #209 discipline) — only `_worldContent` is toggled.

### 1.7 Optional debug fallback (keep the text, hidden)

Retain the old text readout behind a config flag `Sunstone.DebugTextReadout` (default **OFF**).
When on, draw the legacy "⚠ N hostiles · nearest Xm" line under `Hud.m_rootObject` *in addition
to* the world halo — useful for diagnosing "is detection finding anything?" without reading the
halo (and for headless/GPU-less sanity since it's a plain `Text`). Default off so players only see
the halo. This makes the redesign non-destructive: the working text path becomes a debug aid, not
dead code. (The `DebugMount` diagnostic from the #209 fix is likewise retained — log the halo
mount + visibility transitions + first-show world placement so a fresh client `LogOutput.log`
splits a mount/pump failure from an in-world-but-unseen halo.)

### 1.8 Aggro-state colour coding — the "Rune of Awareness" element (Daniel, 2026-06-19)

🟢 **DECIDED (Daniel, 2026-06-19):** *"take a look at how the rune of awareness works in runemagic
mod, I want something very similar."* Grounded: the reference is the **Rune of Alertness** in
**Rune Magic** by **hyleanlegend** (Thunderstore `hyleanlegend/Rune_Magic`, Nexus #1359). Its
public description (clean-room: read the PUBLIC store description for behaviour, NOT the mod's code):

> *"detect nearby enemies and their direction/distance, indicated by the size/angle of the phantom
> heads floating around you. Any alerted enemies will have a **glowing yellow** indicator above
> their heads, which turns **orange if they aggro another player, and red if they're aggroed on
> you**."*

Our eidetic halo already matches the core (per-creature trophy floating around the player, scale =
distance, position = real bearing). The **one carried-over element** is the **threat-state colour
tint**. Fold it in: tint each trophy slot (the quad's material `color`, and its star pips) by the
creature's aggro state toward the local player:

| State | Colour | Meaning |
|---|---|---|
| idle / not targeting anyone | 🟡 **yellow-warm** (`#F2D24A`-ish) | detected, hasn't locked on |
| aggroed on ANOTHER player | 🟠 **orange** (`#F28C28`-ish) | hunting someone else |
| aggroed on YOU (local player) | 🔴 **red** (`#E5402B`-ish) | coming for you — top priority |

**Grounded against vanilla primitives (clean-side — this is reproduced from the BASE GAME's own
nameplate logic, no Rune Magic code read or needed):**
- `BaseAI.IsAlerted()` — public, decomp `:5450`. `BaseAI.HaveTarget()` — public, `:5460`. Vanilla's
  own `EnemyHud` toggles its `m_alerted` vs `m_aware` nameplate icons off exactly these two
  (`:38538`), so this is the game's own "is this thing roused / hunting" surface.
- `BaseAI.GetTargetCreature()` — public virtual, `:5564`. Compare its result against
  `Player.m_localPlayer`:
  - target == local player → **red**;
  - target != null && target != local player (another player/character) → **orange**;
  - no target (or not alerted) → **yellow**.
- All public accessors, all base-game. Access via `creature.GetBaseAI()` (the same path EnemyHud
  uses, `:38538`). Null-safe: a hostile with no BaseAI (rare) defaults to yellow. **This is the
  UNCHANGED `SunstoneProjection.AggroTint` already in the shared derivation** — the halo consumes
  `blip.Tint`, no new tint code.

**Why this is the right "very similar":** Rune of Alertness floats *phantom heads*; we float
*creature trophies* (Daniel's locked choice — more legible, real art) — same mechanic, better read.
The yellow→orange→red escalation is the load-bearing "awareness" feel he's pointing at, and it's a
genuinely useful threat-priority cue in a Swamp horde (which of the six blobs is actually on me?).
Tint multiplies onto the trophy sprite so the species is still recognisable through the colour.

> ⚠️ **Colorblind note carried forward (not re-opened here).** Daniel is colour-blind, and a prior
> GPU-confirmed failure (`Signs.cs` red→maroon, `requirements.md §A2.6`) means the yellow/orange/red
> hue triplet may not fully distinguish for him on ANY surface (this same `AggroTint` feeds the
> minimap surfaces too — see the in-flight card `t_aab051ae`, item ④). The eidetic halo does **not**
> change the tint encoding (it consumes the shared `blip.Tint` unchanged), so if a non-hue state
> encoding (shape/lightness/blink/outline) is later locked, it lands once in `SunstoneProjection`
> and every surface inherits it — including this halo. **Out of scope for this card** (which is the
> world-space port, not the colour language); flagged so it isn't lost. The flat trophy silhouettes
> at least keep the *species* legible independent of the tint, which is a colorblind-friendly
> property of the Knob-#4 flat-sprite choice.

---

## 2. Why the lens "seems to do nothing" — the wiring audit (cause-b, CHECKED)

> ### 🔴 2.0 CORRECTION (card t_d5949685, 2026-06-19) — there WAS a second bug: a dead Update pump
>
> The audit below concluded *"no wiring bug found; the cause is purely cause-(a) (wrong surface)."*
> **That conclusion was wrong.** A real, deterministic render bug existed on top of the wrong-surface
> issue — the **same self-deactivating-host Update-pump bug the Iron Compass had** (found + fixed there
> first, card t_61aff612 / PR #208):
>
> - `SunstoneLensHudOverlay` is a `MonoBehaviour` whose `_root` *was* its own host GameObject
>   (`_root = gameObject.AddComponent<RectTransform>()`).
> - `SetVisible(on)` did `_root.gameObject.SetActive(on)` — i.e. the component deactivated **the
>   GameObject it lives on**.
> - Unity does not call `Update()` on a component whose GameObject is inactive, and `Update()` is the
>   *only* caller of `SetVisible(true)`. So `Build()`'s closing `SetVisible(false)` froze the overlay
>   inactive **permanently** — the pump that would un-hide it had been switched off by the very call that
>   hid it. Total, deterministic absence whether or not the lens is worn/charged.
>
> So the audit's individual findings (both patches woven, equip-gate sound) were each correct, but they
> were necessary-not-sufficient: the overlay mounted and the gate matched, yet `Update` never ran past
> the first frame, so even the trophy ring would have rendered nothing.
>
> **Fix (card t_d5949685):** visibility now toggles a dedicated **content child** (`_content`), never
> the host. The host stays active so its `Update()` keeps pumping; `_content` carries everything visible
> (solar ring, trophy slots, debug text) and is what `SetVisible` activates/deactivates. A
> `DebugMount`-gated diagnostic (default ON for this cut, `SunstoneLens.DebugMount` config) logs the
> mount + visibility transitions + first-show placement so a fresh client `LogOutput.log` splits a
> mount/pump failure from an on-screen-but-empty ring. Build 0/0.
>
> **Builtin-resource fragility audit (the card's secondary ask):** the compass fix also had to replace
> a builtin sprite (`Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd")`, which fails to load on
> Valheim's 0.221.x Unity build). The Sunstone overlay has **no equivalent fragility** — the solar ring
> (`RingSprite()`), trophy-less glyph (`ProceduralThreatGlyph()`), and a missing trophy/star sprite all
> already degrade procedurally or fail safe. The only builtin it touches is the legacy *debug-text*
> font (`Resources.GetBuiltinResource<Font>("Arial.ttf")`, behind `VanillaUISkin.Font`), which is a
> genuine Unity builtin font (NOT in the stripped-`.psd` class) and is only used by the default-off
> debug readout. No sprite/asset change was needed.
>
> The original audit (still accurate on its own terms) follows for the record.

The card flagged a possible wiring bug making even the text not show. **Audited this pass —
no wiring bug found; the cause is purely cause-(a) (wrong surface).** Evidence:

- **Both patches are woven.** `Plugin.Awake` calls `harmony.PatchAll(...DrainGate)` (Plugin.cs:563)
  AND `harmony.PatchAll(...SunstoneLensHudOverlay.HudBootstrap)` (Plugin.cs:564). PatchCheck would
  ERROR at boot if either shipped dead; both are registered. The overlay *mounts*.
- **The equip-gate idiom is proven in-game.** `GetEquippedLens` matches
  `StripCloneSuffix(item.m_dropPrefab.name) == "SBPR_SunstoneLens"` gating on `ItemType.Trinket`
  (`SunstoneLens.cs:283-299`). This is the **exact same** `m_dropPrefab.name` equip-gate the
  **shipped, in-game-verified** Cartographer's Kit (`CartographersKit.cs:245`) and Iron Compass
  (`SBPR_CompassHud.cs:296`) use. `m_dropPrefab` is reliably set by `ItemDrop.Awake` on any
  instantiated item (`LocalMapNamePatch.cs:151` documents this). The prefab name is the locked
  const `LensName = "SBPR_SunstoneLens"` — matches the registered prefab. **The gate is sound.**
- **The real "nothing" is the surface.** Even fully working, the shipped overlay is a 20px text
  line anchored bottom-center at `anchoredPosition(0, 170)` (`SunstoneLensHudOverlay.cs:86`) —
  easy to miss, possibly behind the hotbar, and *not* the trophy ring Daniel pictured. "Does
  nothing" = "is not the feature I imagined," not "is unwired."

**One thing to verify in-game when building (cheap, name it in the AT):** confirm a freshly
*looted/crafted* lens (not a `spawn`-ed debug one) reads as worn — `spawn`-ed items can have a
clone-suffix or null `m_dropPrefab` edge the strip handles, but Daniel's accept is a real
crafted lens. AT-EIDETIC-1 covers this (worn + charged → trophy appears in the world halo).

---

## 3. Named acceptance tests — AT-EIDETIC-* (logs-green ≠ playable — Daniel's in-game eye is the accept)

Replaces the screen-space `AT-LENS-RING-*` rows (superseded with the screen ring); the
detection-mechanic ATs (`AT-LENS-CHARGE`, `-DRAIN-CONST`, `-ZERO-INERT`, etc. in the impl spec §8)
are unchanged. This is a **visual** feature: logs-green proves the sweep + projection run, NOT that
the halo reads right — Daniel's eye on a GPU client in the next playtest build is the final accept.

- **AT-EIDETIC-1** — worn + charged, a hostile within `DetectRadius` → its **3D-floating trophy**
  (a billboarded trophy card) appears **in the world** at the correct **bearing relative to where
  the player is facing** (turn so the enemy is on your right → its trophy floats on your right;
  face it → straight ahead). Uses a real crafted/looted lens (not a `spawn`-ed one).
- **AT-EIDETIC-2 / AT-HALO-SCALE-KNEE** — as a hostile **approaches**, its trophy **grows** (scale
  rises toward full at 10m) while staying at the **same fixed ring distance** from your face; as it
  recedes, it **shrinks** toward the 0.25 floor at the 50m edge — but **never moves closer to or
  farther from your face** (fixed-distance ring; only SCALE varies, the 10m knee, Knob #2 re-locked
  by t_10bacccf).
- **AT-HALO-FIXED-DIST** — every trophy renders at the **same fixed distance** from the eye-point
  regardless of enemy range; a near enemy and a 50m enemy sit at the same `HaloRadius`, differing
  only in size. **No outward push** for far enemies (the bug that hid them).
- **AT-HALO-SCALE-KNEE** — enemy **≤10m → full scale** (the "1.0"); enemy at the **50m detection
  edge → 25% scale** (the "0.25"); linear between; the edge trophy is still clearly readable, never
  shrunk toward nothing. (Pinned headlessly in `SunstoneHaloGeometryTests.cs`; the in-game read is
  Daniel's GPU-client accept.)
- **AT-EIDETIC-3** — a **1-star** enemy shows the **vanilla nameplate star** above its trophy; a
  **2-star** shows **two**; a **0-star** shows none. (Count = `GetLevel()-1`; the pip sprite is
  harvested from vanilla `EnemyHud.m_baseHud` level_2/level_3, NOT a Unicode ★.)
- **AT-EIDETIC-4** — a **trophy-less** hostile appears per the locked **hybrid** Knob #3:
  a **Greyling** shows a **Greydwarf** trophy (remap table); a genuinely-unmapped trophy-less
  hostile shows the **generic 3D fallback glyph** — **never silently missing**. Plus: on world
  load, the **startup unmapped-creature dump** (`DumpUnmappedCreatures` default ON) logs the
  complete "no trophy / no remap" set as one reviewable block.
- **AT-EIDETIC-5** — **zero** hostiles → the trophy slots are empty but the **empty-state affordance**
  shows (worn + charged; `ShowEmptyRing` default ON). 🐛 That affordance is now the **3D pulsing
  sun-corona disc** (card `t_2d500d45`, §1.6), not the old flat ring. A **depleted** lens → halo
  **off**. Unequip → halo gone immediately. (The detection-feed pump keeps running regardless — #209
  discipline preserved.)
- **AT-EIDETIC-AGGRO** — a hostile that is **idle/unalerted** renders its trophy **yellow**; once it
  **aggros another player** the trophy turns **orange**; once it **targets YOU** it turns **red**.
  State follows the creature's own `BaseAI` (IsAlerted / GetTargetCreature vs the local player) —
  the same `SunstoneProjection.AggroTint` every surface uses.
- **AT-EIDETIC-CAMREL** (🔴 thesis guard) — the halo is **camera-relative, never north-up**.
  Standing still and rotating the camera sweeps every trophy around you; the halo grants **no
  cardinal orientation** (that stays the Iron Compass's exclusive payoff). No north arrow, no
  N/E/S/W. (In world-space this holds by construction — trophies sit on the real `dirToEnemy`.)
- **AT-EIDETIC-OCCLUDE** — behaves per the locked Knob #1: the head-halo placement keeps trophies
  in a tight halo near the eye-point so terrain **rarely** occludes them; there is **no**
  through-terrain ZTest-Always material (honest depth). A trophy may briefly clip behind very close
  geometry — that's the accepted honest-depth tradeoff, not a bug.
- **AT-EIDETIC-PERF** — a Swamp horde (10+) does not tank FPS: pooled **world** objects capped at
  `RingMaxIcons`, billboards reuse the pool (never create/destroy per frame), and the
  trophy-sprite + star-pip + remap caches resolve each species once.
- **AT-EIDETIC-MINIMAP-UNAFFECTED** — with a minimap present (SBPR carry-disc nomap-ON or vanilla
  corner map nomap-OFF), detection still hands off to the minimap (flat icons) exactly as before;
  the world halo renders **only** in the no-minimap fallback (`MinimapHandoffMode` honored). The
  minimap surfaces' flat-icon look is unchanged by this card.

---

## 4. Config + assets impact

**Config (Plugin `Sunstone` section, all live-tunable so Daniel converges feel in one joined
session — the banner-windsock pattern).** The screen-space knobs are replaced by world-space ones:

| Knob | Default | Role |
|---|---|---|
| `HaloRadius` (m) | ~2.0 | **FIXED** ring distance — EVERY trophy is equidistant from the eye (no range-dependent push) — Knob #2 (t_10bacccf) |
| `HaloScaleMax` | ~0.6 | trophy world-scale at FULL size (enemy ≤10m → the "1.0") — Knob #2; the AT-gated eyeball tunable (`scaleNear`) |
| _(derived)_ edge scale | `0.25 × HaloScaleMax` | trophy scale at the 50m detection edge (the locked "0.25" floor) — **derived, not a knob** (old `HaloScaleMin` removed) |
| `HaloEyeOffsetY` (m) | ~0 | lift the halo plane off the eye-point so trophies clear the crosshair |
| `RingMaxIcons` | ~12 | pooled-slot cap (nearest N shown in a horde) — carried over |
| `ShowEmptyRing` | **true** | faint solar ring when nothing's near (§1.6) — carried over |
| `ShowDepletedHint` | **false** | halo off when depleted (§1.6) — carried over |
| `DumpUnmappedCreatures` | **true** | startup full-catalog dump of trophy-less/unmapped creatures (Knob #3) — **NEW** |
| `DebugTextReadout` | false | legacy text line as a debug aid (§1.7) — carried over |
| `DebugMount` | true (this cut) | log halo mount/visibility/first-show world placement (#209 diagnostic) — carried over |

Defaults baked as consts (single source of truth, the existing `Default*` pattern). The
**screen-space knobs `RingRadiusPx` / `RingCenterOffsetY` / `RingIconMinPx` / `RingIconMaxPx` are
removed** (no screen ring). The detection knobs (`DetectRadius`, `DetectIntervalSeconds`, charge
economy) are unchanged.

**Assets:** **NO new asset required** beyond what already shipped. The generic glyph
`threat_fallback_v0.1.png` already exists; star pips reuse the harvested vanilla nameplate star;
trophies reuse vanilla `m_icons[0]`. The **variant→sibling remap table is code/data**, not art.
(The world-space quad uses an unlit transparent material constructed at runtime from the existing
sprites — no shipped shader/material asset.)

**Files the impl card touches:** `SunstoneLensHudOverlay.cs` (`RenderRing` + slot scaffolding →
world-space pool; possibly extracted into a new `SunstoneWorldRing.cs`), `SunstoneProjection.cs`
(the variant→sibling remap table + the startup dump scan — these belong in the shared projection
so the trophy resolution stays one copy), `Plugin.cs` (the new config binds + the
ZNetScene-ready dump hook), this doc + `sunstone-lens-impl-spec.md` §4/§5/§8 render banners +
`PIECES_AND_CRAFTABLES.md` (Lens Render/Visual/Config/Patch/Status rows) + `PLAYER_GUIDE.md`
(lens paragraph). **SpecCheck manifest: no change** (no recipe/piece change — render-only).
Spec-and-code move together per AGENTS.md.

---

## 5. The four design knobs — ✅ ALL LOCKED (Daniel, 2026-06-21, "just implement as directed")

Daniel answered all four open knobs in the ticket thread, then said *"just implement as directed"*
— the doc-review gate is **WAIVED** and these are **final decisions**, not directional input.

1. **Occlusion policy → 🔒 head-halo "Rune Magic dodge".** (Daniel: *"use the rune magic dodge."*)
   Trophies float in a tight halo around the eye-point, rarely occluded; **no through-terrain
   material.** Honest depth. Collapses with Knob #2. (§Q2 #1, §1.3.)
2. **Ring geometry → 🔒 FIXED-distance ring + scale-only range cue (10m knee).** 🐛 **Re-locked by
   bug-fix t_10bacccf (Daniel 2026-06-22, verbatim: "creatures should be at a fixed distance from
   the player but grow in scale from .25 at the far edges to 1.0 when within 10m").** `pos = eye +
   dirN·HaloRadius` (single fixed distance), `scale = Lerp(scaleNear·0.25, scaleNear, 1 -
   Clamp01((dist-10)/(detectRadius-10)))` (full ≤10m → 0.25 at the 50m edge). **SUPERSEDES** the
   original t_68672b6b lock of variable radius AND scale ∝ distance (which pushed far enemies out
   and shrank them to invisibility). 10m knee + 0.25 floor + 1.0 ceiling LOCKED; `scaleNear`
   (=HaloScaleMax) AT-gated. Bearing stays camera-relative (thesis guard). (§Q2 #2, §1.2–§1.3.)
3. **Trophy-less fallback → 🔒 hybrid (remap table + generic fallback + default-ON startup dump).**
   (Daniel: *"Hybrid with a setting default on to dump unmapped creatures on startup for review."*)
   Greyling→Greydwarf-style variant→sibling remap, generic 3D fallback for the rest (never omit),
   and `DumpUnmappedCreatures` default ON doing a full-catalog scan at ZNetScene-ready. (§Q1, §Q2 #3.)
4. **Trophy render → 🔒 flat billboarded sprites for ALL trophies** (the existing `m_icons[0]`), NOT
   the 3D `attach` mesh. (Daniel: *"Makes sense to me to use the flat images?"*) Lower-risk,
   higher-reuse; sidesteps Rune Magic's per-species mesh-tuning table; reuses the existing
   sprite + star + tint rendering. (§Q2 #4, §1.4.) *(Mild tension noted: "flat images" pulls
   slightly away from Rune Magic's literal-3D-heads look that "more like rune magic's" pulls toward
   — net is trophy cards orbiting the head, not sculptural props. Still fully diegetic; a sculptural
   look is a future follow-up.)*

**Carried-over locked elements (unchanged from the screen-space design, still in force):** vanilla
nameplate star pips (§1.5), aggro-state yellow/orange/red tint via `BaseAI` (§1.8), empty-state
affordance default ON / depleted off (§1.6 — now the **3D pulsing sun-corona**, card `t_2d500d45`,
not the flat ring), the camera-relative thesis guard, and the #209
Update-pump-alive discipline (§1, §2.0).

These knobs are pacing/feel/diegesis calls; the build is structurally the same whichever way each
lands (each is an isolated placement/material/asset/data decision), and they are now locked.

---

## 6. 🔴 Behavioral reference — how Rune Magic's Rune of Alertness works (clean-room description, NOT code to copy)

Daniel pointed at **Rune Magic** (`hyleanlegend/Rune_Magic` v1.4.0) as the model and asked
specifically how it handles floating trophies for enemies without trophies (greylings). A
`reviewer-cleanroom` read the decompiled `SE_Alertness` to answer that **behaviorally**. 🔴 **The
engineer implements SBPR's halo from vanilla primitives + SBPR's own design above — NOT from Rune
Magic's code. Do NOT commit Rune Magic's binary or decompiled source anywhere in this MIT repo.**
This section is a behavioral north-star only; SBPR's locked design (§Q1–§5) wins wherever they
differ.

**The trophy-less answer (the thing Daniel asked).** Rune Magic does **NOT** simply omit
greylings. It hedges in three layers, and only truly-unmappable creatures are skipped:

1. **Variant→sibling remap table.** A hardcoded map remaps a trophy-less variant onto a sibling
   that *does* have a trophy. **Greyling → Greydwarf is the very first entry** (a greyling wears a
   Greydwarf trophy). Other entries: Boar_piggy→Boar, DraugrRanged→Draugr, Wolf_cub→Wolf,
   GoblinArcher→Goblin, GoblinShaman_Hildir→GoblinShaman, Lox_Calf→Lox, all Dverger mages→Dverger,
   Charred variants→base Charred, Asksvin_hatchling→Asksvin, Morgen_NonSleeping→Morgen.
2. **Bundled custom-trophy meshes** for creatures vanilla gives no trophy at all (Ghost, Hare,
   Dverger, SeekerBrood — its own `vfx_Trophy*` assets).
3. **Special-case mesh harvest** for BlobLava / BlobFrost — pulls the blob's visual
   `SkinnedMeshRenderer.sharedMesh` directly into a fresh `MeshFilter`.

Only after all three miss does it return null → and **then it omits the creature entirely**
(`isTrackable()` false → never drawn). **There is NO generic fallback glyph in Rune Magic** —
unmapped trophy-less hostiles are invisible to the rune.

**The rest of the Alertness behavior (parity reference):**
- Sweeps `Character.GetAllCharacters()` within a max distance; tracks each trackable creature once
  (dictionary keyed by `Character`).
- Each trophy is the trophy prefab's **`attach` 3D mesh**, all non-mesh components stripped,
  parented to a holder, scaled to a target on-screen height via a vertex-bounds raycast.
- **Placement:** `trophyPos = playerEyePoint + dirToEnemy * Lerp(hoverRadiusMin, hoverRadiusMax,
  dist/maxDist)` — a **head-centric halo**: near enemies at the *inner* radius (close to your face),
  far at the *outer*. **Scale** = `Lerp(trophyMaxScale, 0, dist/maxDist)` — near big, far → shrinks
  to nothing. Trophy rotated to face the player (`LookRotation(-dir)`).
- **Aggro colour** is a *separate* glowing indicator object floating above the trophy
  (`vfx_AlertnessIndicator`), colored via `MaterialPropertyBlock` `_EmissionColor`: green = idle
  (indicator HIDDEN — only the trophy shows), yellow = alerted/no target, orange = targeting another
  player, red = targeting the local player. Trophies removed on death/despawn; all cleared on stop.

**Where SBPR DIVERGES from Rune Magic (reconciled in the locked design above):**
- **Placement/geometry:** SBPR adopts Rune Magic's **head-halo placement near the eye-point** (Knob
  #1) but **diverges on the distance cue** (Knob #2, re-locked by t_10bacccf): where Rune Magic
  varies BOTH radius and scale with distance, SBPR uses a **FIXED ring distance** and lets **scale
  alone** carry range (the 10m knee: full ≤10m → 0.25 at the edge). Daniel reversed the earlier
  "adopt Rune Magic's variable radius wholesale" lock because the outward push hid far enemies.
- **Trophy art:** SBPR uses **flat billboarded `m_icons[0]` sprites** (Knob #4), NOT the 3D `attach`
  mesh — sidesteps Rune Magic's whole per-species mesh-tuning override table
  (`AlertnessScaleOverride-X`/`RotationOverride-X`/`OffsetOverride-X`/`TrophyPosOverride-X` exist
  precisely because raw trophy meshes don't billboard cleanly). Net: trophy *cards*, not sculptural
  heads.
- **Aggro surface:** SBPR tints the **trophy itself** (yellow/orange/red via `BaseAI`) + keeps
  vanilla nameplate **star pips** — Rune Magic tints a *separate glow object* and has no stars. The
  aggro primitives (`BaseAI.IsAlerted`/`GetTargetCreature`) are vanilla either way.
- **Trophy-less:** SBPR keeps a **hybrid** — adopt the greyling→greydwarf-style remap for common
  variants **AND** a generic 3D fallback for the rest (Knob #3), so SBPR **never omits** a threat
  (Rune Magic omits the unmapped). Plus the default-ON startup dump to grow the remap table.

---

## 7. Clean-side / ADR notes

- Every vanilla hook cited (`Billboard`, `Character.GetEyePoint`, `ZNetScene.m_prefabs`,
  `CharacterDrop`/`ItemType.Trophy`, `EnemyHud.m_baseHud`, `BaseAI.IsAlerted`/`GetTargetCreature`,
  `Character.GetLevel`) is base-game `assembly_valheim` decomp — **fair to read+adapt** (ADR-0001,
  repo AGENTS.md + 2026-06-09 clarification). Line numbers from
  `/home/polyphonyrequiem/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`, grepped
  live this pass — re-confirm if the decomp drifts.
- The world halo is built **additively** (ADR-0006): `new GameObject()` + `AddComponent` of only
  `MeshFilter`/`MeshRenderer` (or a world-canvas `Image`) + `Billboard`, carrying **no**
  `ZNetView`/`Piece`/networked skeleton — purely cosmetic, client-local. **NOT** a clone-and-strip
  of a vanilla prefab. Trophy/star sprites are **read** as blueprint (reading an asset is not
  cloning).
- **Rune Magic is a BEHAVIORAL reference only** (§6 clean-room description). Do NOT copy its code;
  do NOT commit its binary/decompiled source into this MIT repo. Reproduce from vanilla primitives +
  SBPR's locked design.
- **SpecCheck manifest: no change** (render-only; no recipe/piece/station change).
