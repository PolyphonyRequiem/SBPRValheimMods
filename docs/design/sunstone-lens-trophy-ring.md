---
title: "Sunstone Lens detection — the trophy ring (render redesign, supersedes the text-HUD placeholder)"
status: current
purpose: "Architect render-design for the Sunstone Lens' monster detection: a screen-space, camera-relative RING of creature TROPHIES around the player — angular position = bearing, icon size ∝ proximity, star pips for star-levels. Supersedes the shipped bottom-center text-HUD placeholder (SunstoneLensHudOverlay.cs, PR #163), which was always self-described as a placeholder. The detection MECHANIC (who/when — SunstoneLens.GatherHostiles, the energy model, the equip-gate) is UNCHANGED and correct; only the RENDER SURFACE is redesigned. Every vanilla hook line-cited against assembly_valheim. Card t_b8a19487; Daniel gates the merge."
---

# Sunstone Lens detection — the trophy ring

Daniel's exact intent (2026-06-18), verbatim:

> "the sunstone lens is supposed to display a monster's **trophy** facing the camera with
> its **size proportional to its closeness** to the player at a **fixed distance in a ring
> around the player** showing the **direction** of the enemy with **little stars above their
> heads if they have star levels**"

This doc is the buildable render redesign for that. It **replaces** the render surface only —
the shipped `SunstoneLensHudOverlay.cs` (PR #163) is a bottom-center text readout ("⚠ N
hostiles near · nearest 12m ◄ · charge 80%") whose own header calls it *"a FUNCTIONAL
placeholder indicator (text + a simple arrow glyph)… polished threat-overlay art is a v0.x
follow-up."* Daniel has now given the real design; this is that follow-up.

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

> **ADR-0006 (load-bearing):** the ring overlay is built **additively** — `new GameObject()` +
> `AddComponent<Image>()`/`RectTransform` parented under the existing `Hud.m_rootObject`,
> reusing the trophy items' own `m_icons[0]` sprites (reading a sprite is not cloning). No
> vanilla prefab is `Instantiate`d as a mutable base.

---

## 0. The four grounding questions — RESOLVED against the decomp

The card posed four questions to resolve before impl. All four are answered here against real
decomp; **Q2 carries an architect recommendation Daniel should confirm** (the diegetic-3D vs
screen-space radar fork), the other three are decomp facts with no open choice.

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

**Fallback for trophy-less hostiles (Q1's open sub-question — ANSWERED):** not every hostile
has a trophy (e.g. summoned/spawned minions, some boss adds). When the cache resolves `null`,
render a **generic threat glyph** in the trophy's place on the ring — a simple skull/danger
sprite shipped in the bundle (`assets/icons/ui/threat_fallback_v0.1.png`), tinted by the same
proximity-scale and star-pip rules. The fallback is a *defined slot*, not a skip: a trophy-less
hostile still appears on the ring at the correct bearing/size — it just wears the generic glyph
instead of a species trophy. (Bundle a placeholder per the icon doctrine; "you can tell it's a
threat" is the bar, not ship-art.)

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

### Q2 — diegetic world-space ring vs screen-space HUD radar (ARCHITECT RECOMMENDATION)

Daniel said "a ring around the player." That phrase has two readable implementations, and this
is the one genuine design fork in the card. **Architect recommendation: screen-space HUD radar
ring, camera-relative, with billboarded trophy sprites.** Reasoning, grounded:

- **Screen-space (RECOMMENDED).** A `RectTransform` ring centered on the screen under
  `Hud.m_rootObject`; each trophy is a UGUI `Image` placed on the ring's circumference at the
  angle = bearing-to-enemy. This is the **same render doctrine the shipped Iron Compass and the
  current lens overlay already use** (`Hud.Awake` postfix → overlay under `m_rootObject`,
  `iron-compass-impl-spec.md` §4) — NoMap-safe, no world objects, no ZNetView, no per-creature
  GameObject lifecycle to manage, invisible to other players and to vanilla/other-modded
  clients. It reuses the existing `HudBootstrap` mount that's *already woven* (Plugin.cs:564).
- **World-space (NOT recommended for v0.1).** Floating quads in the 3D world on a physical ring
  around the player would need: per-frame billboard math against the camera, occlusion/ZTest
  handling so trophies don't hide behind terrain, a pool of world GameObjects created/destroyed
  as hostiles enter/leave range, and a depth-sorting story. That's the Twisted Portal
  through-terrain-overlay tier of work (`nomap.md` risk-rank #9), for marginal benefit over a
  clean screen radar. Defer it; if Daniel specifically wants diegetic 3D later it's a separate
  card.
- **"Facing the camera" is satisfied by both** — a screen-space `Image` is trivially
  camera-facing (it's 2D UGUI); a world quad would need explicit billboarding. Another point for
  screen-space.

> 🔴 **The thesis guard Daniel must not lose (architect flag).** The ring is **camera-relative,
> NOT north-up.** "Up" on the ring = *the direction the player is looking* (camera forward), and
> a trophy's angle = the enemy's bearing **relative to camera forward** — exactly what the
> existing `BearingGlyph` already computes (`Vector3.SignedAngle(camForward, toEnemy, up)`,
> `SunstoneLensHudOverlay.cs:243`). **It must NOT be a north-up compass radar.** A north-up
> radar would hand the player cardinal orientation — which is the *entire* withheld-payoff the
> Iron Compass exists to grant (`iron-compass-impl-spec.md` "the withheld orientation IS the
> design"). A north-up lens radar would delete the Compass's reason to exist and reverse a
> Daniel-locked no-north difficulty choice. The lens answers *"where is the threat relative to
> where I'm facing,"* never *"where is north."* Keep the bearing camera-relative.

### Q3 — reconcile with the existing design + impl docs

- **`docs/design/swamp-detection-item.md` (PR #144)** is the IDEA note — theme/material/sourcing
  only. It explicitly leaves the render surface open ("if the minimap is off by default, the
  reveal needs its own surface"). Nothing in it describes the ring. **No conflict; no edit
  needed** beyond this doc superseding its render-open-question.
- **`docs/v3/planning/sunstone-lens-impl-spec.md`** is the buildable spec. Its §4 ("Render
  v0.1") and §5 ("Render surface under NoMap") describe the *text/arrow placeholder*. **Those
  two sections are superseded by this doc** — they are edited (in this same PR) to point here
  for the render, while keeping their detection-mechanic + NoMap-doctrine content (which is
  still correct). The §8 acceptance tests gain the new AT-LENS-RING-* rows (below). This is
  **net-new render design**, not a duplicate — the impl spec described detection; this describes
  how detection is drawn.
- **Dataset (`PIECES_AND_CRAFTABLES.md`)** Lens row "Patch surface" / "Visual notes" are updated
  to name the trophy-ring render (in this PR).
- **`PLAYER_GUIDE.md`** lens paragraph currently says "a HUD readout warns you how many hostiles
  are near, how far the closest one is, and roughly which way it's lurking" — reworded (this PR)
  to describe the trophy ring without over-promising art polish.

---

## 1. Render architecture — the trophy ring overlay

The ring **replaces the body of `SunstoneLensHudOverlay`**, not its scaffolding. Keep:
`EnsureBuilt`, the `HudBootstrap` `Hud.Awake` postfix (already woven, Plugin.cs:564), the
`Update` visibility/charge gate (worn? charged? depleted?), and the throttled
`GatherHostiles` sweep. Replace: the two `Text` children and `RenderThreats` with a ring of
trophy `Image` slots. The class stays a client-only `MonoBehaviour` under `Hud.m_rootObject`.

### 1.1 The ring container

A single `RectTransform` (`_ringRoot`) centered on screen, anchored center
(`anchorMin = anchorMax = pivot = (0.5, 0.5)`), under `Hud.m_rootObject`. The ring **radius is
a fixed screen-space constant** (config `Sunstone.RingRadiusPx`, default ~180px) — radius is
fixed (Daniel: "fixed distance in a ring"); only trophy *size* encodes range (§1.3). Center the
ring on screen center (or slightly above, config `Sunstone.RingCenterOffsetY`) so it frames the
player's view without covering the crosshair.

### 1.2 Per-hostile slot — a pooled `Image` on the circumference

Pool a small list of slot objects (`_slots`), each a `GameObject` with an `Image` (the trophy
or fallback glyph) plus up to N child `Image` star-pips. **Pool, don't create/destroy per
frame** — reuse `Mathf.Max(_slots.Count, hostiles.Count)` slots, `SetActive(false)` the unused
tail. Cap the live count at config `Sunstone.RingMaxIcons` (default ~12) so a horde doesn't
spawn 80 images; if more hostiles than the cap, show the nearest N (sort by distance, the sweep
already has positions).

**Angular placement (camera-relative — the Q2 thesis guard):** reuse the existing bearing math.
For each hostile, compute `signed = Vector3.SignedAngle(camForward_flat, toEnemy_flat, up)` (the
exact formula at `SunstoneLensHudOverlay.cs:243`, `Utils.GetMainCamera()` `assembly_utils:6705`).
Map `signed` (degrees, 0 = dead ahead, +90 = hard right) onto the ring: place the slot at
`anchoredPosition = (sin(signed) * R, cos(signed) * R)` so 0° sits at the top of the ring
(straight ahead), +90° at the right, ±180° at the bottom (behind you). Camera-null safe — hide
the ring if `GetMainCamera()` is null.

### 1.3 Trophy size ∝ proximity (fixed ring radius)

Daniel: "size proportional to its closeness… distance maps to scale, NOT to ring radius." So:

```
t = 1 - Clamp01(distance / DetectRadius)     // 1 at the player, 0 at the edge of range
scale = Lerp(RingIconMinPx, RingIconMaxPx, t)  // far = small, near = big
```

`DetectRadius` is the existing `Plugin.LensDetectRadius` (default 30m). `RingIconMinPx` /
`RingIconMaxPx` are config (defaults ~28px / ~64px). A creature right on top of you renders at
max size; one at the detection edge renders at min size. Set the slot `Image`'s
`rectTransform.sizeDelta = (scale, scale)`. (Optional polish, config-flagged: a slight alpha
fade as `t → 0` so distant threats are fainter — defer if it complicates v0.1.)

### 1.4 Trophy sprite + the fallback glyph

`slot.Image.sprite = ResolveTrophySprite(creature)` (the cached `CharacterDrop` walk, §Q1) —
or the bundled `threat_fallback` sprite when that returns null. `preserveAspect = true` so
non-square trophy icons don't stretch. The sprite is read from the trophy item's own
`m_icons[0]` — no new art needed for creatures that have trophies (most do); only the single
generic fallback glyph is a new asset.

### 1.5 Star pips above the trophy

`stars = creature.GetLevel() - 1` (§Q4). Render that many ★ as small `Image`s (or a single
`Text` of "★"×N if a star sprite isn't bundled) parented to the slot, laid out in a row centered
**above** the trophy (`anchoredPosition.y = +scale/2 + pipPad`). 0 stars → no pips. Pool the pips
per slot (cap at a sane N, e.g. 5 visible) and `SetActive` only as many as `stars`.

### 1.6 Empty + depleted states (AT-LENS-RING-5)

- **Zero hostiles:** all slots `SetActive(false)`. The ring is **empty/invisible** — no
  text-spam, no "clear" readout. (Optional config `Sunstone.ShowEmptyRing` to draw a faint ring
  outline when worn+charged-but-clear, default OFF — Daniel's call; default to nothing on
  screen.)
- **Depleted lens (charge < `MinChargeToDetect`):** ring off entirely (same as not worn). No
  sweep runs. The durability bar on the trinket already signals "dim" — the ring doesn't
  duplicate it. (The old text "Sunstone Lens — dim" line is dropped; if Daniel wants a depleted
  hint, a single faint ring outline is the tasteful version — config, default off.)
- **Not worn:** ring hidden, `Update` early-returns (unchanged from current behavior).

### 1.7 Optional debug fallback (keep the text, hidden)

Retain the old text readout behind a config flag `Sunstone.DebugTextReadout` (default **OFF**).
When on, draw the legacy "⚠ N hostiles · nearest Xm" line *in addition to* the ring — useful for
diagnosing "is detection finding anything?" without reading the ring. Default off so players only
see the ring. This makes the redesign non-destructive: the working text path becomes a debug aid,
not dead code.

---

## 2. Why the lens "seems to do nothing" — the wiring audit (cause-b, CHECKED)

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
crafted lens. AT-LENS-RING-1 covers this (worn + charged → trophy appears).

---

## 3. Named acceptance tests (logs-green ≠ playable — Daniel's in-game look is the accept)

Replaces the placeholder `AT-LENS-DETECT` render half; the detection-mechanic ATs
(`AT-LENS-CHARGE`, `-DRAIN-CONST`, `-ZERO-INERT`, etc. in the impl spec §8) are unchanged.

- **AT-LENS-RING-1** — worn + charged, a hostile within `DetectRadius` → its **trophy** appears
  on the ring at the correct **bearing relative to where the player is facing** (turn so the
  enemy is on your right → its trophy sits on the right of the ring; face it → top).
- **AT-LENS-RING-2** — as a hostile **approaches**, its trophy **grows**; as it recedes, it
  shrinks. The ring **radius stays fixed** — only icon size changes with distance.
- **AT-LENS-RING-3** — a **1-star** enemy shows **★** above its trophy; a **2-star** shows
  **★★**. A 0-star enemy shows no pips. (Read via `GetLevel()-1`.)
- **AT-LENS-RING-4** — a **trophy-less** hostile (e.g. a summoned minion) appears on the ring at
  the right bearing/size wearing the **generic fallback glyph**, not missing entirely.
- **AT-LENS-RING-5** — **zero** hostiles → the ring is **empty** (nothing on screen, no text
  spam). A **depleted** lens → ring **off**. Removing the lens → ring gone immediately.
- **AT-LENS-RING-CAMREL** (🔴 thesis guard) — the ring is **camera-relative, never north-up**.
  Standing still and rotating the camera sweeps every trophy around the ring; the ring grants
  **no cardinal orientation** (that stays the Iron Compass's exclusive payoff). No north arrow,
  no N/E/S/W letters.
- **AT-LENS-RING-PERF** — a Swamp horde (10+ hostiles) does not tank framerate: icons are pooled
  and capped at `RingMaxIcons`; the trophy-sprite cache resolves each species once.

---

## 4. Config + assets impact

**New config (Plugin `Sunstone` section, all live-tunable so Daniel converges feel in one
joined session — the banner-windsock pattern):** `RingRadiusPx` (~180), `RingCenterOffsetY`
(0), `RingIconMinPx` (~28), `RingIconMaxPx` (~64), `RingMaxIcons` (~12), `ShowEmptyRing`
(false), `DebugTextReadout` (false). Defaults baked as `SunstoneLensHudOverlay` consts (single
source of truth, the existing `Default*` pattern). The existing detection knobs
(`DetectRadius`, `DetectIntervalSeconds`, charge economy) are unchanged.

**New asset (one):** `assets/icons/ui/threat_fallback_v0.1.png` — the generic glyph for
trophy-less hostiles. Placeholder-grade (skull/danger) per the icon doctrine. Optionally a
`star_pip_v0.1.png` if "★" glyph text isn't crisp enough — but the Unicode ★ is acceptable for
v0.1 (zero art dependency). **No per-creature art** — trophies reuse vanilla `m_icons[0]`.

**Files the impl card touches:** `SunstoneLensHudOverlay.cs` (the render rewrite),
`Plugin.cs` (the new config binds), this doc + `sunstone-lens-impl-spec.md` §4/§5/§8 (render
sections superseded) + `PIECES_AND_CRAFTABLES.md` (Lens visual/patch-surface rows) +
`PLAYER_GUIDE.md` (lens paragraph). **SpecCheck manifest: no change** (no recipe/piece change —
this is render-only). Spec-and-code move together per AGENTS.md.

---

## 5. Open questions for Daniel (genuine calls, not architect-decidable)

1. **Q2 confirmation:** OK with **screen-space camera-relative radar** (recommended), or do you
   specifically want **diegetic 3D world-space** trophies floating around the player? (3D is a
   separate, larger card.)
2. **Ring center + radius feel:** screen-center, or offset up/down? Radius ~180px a good frame,
   or tighter/wider? (Config-tunable — converge in a joined session.)
3. **Empty-ring affordance:** when worn+charged but nothing's near, show **nothing** (default),
   or a faint ring outline so you know the lens is live? (`ShowEmptyRing`.)
4. **Depleted hint:** ring fully off when out of charge (default), or a dim outline as a "I'm
   inert, recharge me" cue?
5. **Star pips:** Unicode ★ acceptable for v0.1, or do you want an authored star sprite?

These are pacing/UX/art-scope calls; the build is structurally identical whichever way each
lands (each changes an isolated config default or one asset).
