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
sprite shipped in the bundle (`assets/icons/items/threat_fallback_v0.1.png`, flat-packed +
loaded by bare filename like the other v0.1 icons), tinted by the same
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

### 1.5 Star pips above the trophy — REUSE the real vanilla nameplate star art (Daniel, 2026-06-19)

🟢 **DECIDED (Daniel, 2026-06-19):** *"use the Valheim stars used to decorate the monster
nameplates"* — NOT a Unicode ★ and NOT a new authored sprite. Pull the exact star sprite vanilla
draws on enemy nameplates.

`stars = creature.GetLevel() - 1` (§Q4). Render that many star `Image`s parented to the slot,
laid out in a row centered **above** the trophy (`anchoredPosition.y = +scale/2 + pipPad`).
0 stars → no pips. Pool the pips per slot and `SetActive` only as many as `stars`.

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

### 1.6 Empty + depleted states (AT-LENS-RING-5)

🟢 **DECIDED (Daniel, 2026-06-19):** empty ring → show a **faint solar ring** (not nothing);
depleted lens → ring **off**.

- **Zero hostiles:** all trophy slots `SetActive(false)`, but draw a **faint solar ring outline**
  (a thin warm/amber circle at the ring radius) so the player can see the lens is live and watching.
  `Sunstone.ShowEmptyRing` config — **default ON** (flipped from the original draft's OFF per
  Daniel's call). The ring tone is the sunstone's warm amber (thematic: the stored daylight glowing
  faintly), low alpha (~0.18) so it frames the view without clutter. When ≥1 hostile is present the
  faint ring may stay (it's the substrate the trophies sit on) — the trophies are what draw the eye.
- **Depleted lens (charge < `MinChargeToDetect`):** ring **off entirely** (same as not worn) —
  `Sunstone.ShowDepletedHint` default **OFF** per Daniel. No sweep runs. The durability bar on the
  trinket already signals "dim." The old text "Sunstone Lens — dim" line is dropped.
- **Not worn:** ring hidden, `Update` early-returns (unchanged from current behavior).

### 1.7 Optional debug fallback (keep the text, hidden)

Retain the old text readout behind a config flag `Sunstone.DebugTextReadout` (default **OFF**).
When on, draw the legacy "⚠ N hostiles · nearest Xm" line *in addition to* the ring — useful for
diagnosing "is detection finding anything?" without reading the ring. Default off so players only
see the ring. This makes the redesign non-destructive: the working text path becomes a debug aid,
not dead code.

### 1.8 Aggro-state colour coding — the "Rune of Awareness" element (Daniel, 2026-06-19)

🟢 **DECIDED (Daniel, 2026-06-19):** *"take a look at how the rune of awareness works in runemagic
mod, I want something very similar."* Grounded: the reference is the **Rune of Alertness** in
**Rune Magic** by **hyleanlegend** (Thunderstore `hyleanlegend/Rune_Magic`, Nexus #1359). Its
public description (clean-room: read the PUBLIC store description for behaviour, NOT the mod's code):

> *"detect nearby enemies and their direction/distance, indicated by the size/angle of the phantom
> heads floating around you. Any alerted enemies will have a **glowing yellow** indicator above
> their heads, which turns **orange if they aggro another player, and red if they're aggroed on
> you**."*

Our trophy ring already matches the core (per-creature marker around the player, size = distance,
angle = bearing). The **one new element** is the **threat-state colour tint**. Fold it in: tint each
trophy slot (the trophy `Image.color`, and its star pips) by the creature's aggro state toward the
local player:

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
  uses, `:38538`). Null-safe: a hostile with no BaseAI (rare) defaults to yellow.

**Why this is the right "very similar":** Rune of Alertness floats *phantom heads*; we float
*creature trophies* (Daniel's locked choice — more legible, real art) — same mechanic, better read.
The yellow→orange→red escalation is the load-bearing "awareness" feel he's pointing at, and it's a
genuinely useful threat-priority cue in a Swamp horde (which of the six blobs is actually on me?).
Tint multiplies onto the trophy sprite so the species is still recognisable through the colour.

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
- **AT-LENS-RING-3** — a **1-star** enemy shows the **vanilla nameplate star** above its trophy; a
  **2-star** shows **two**. A 0-star enemy shows no pips. (Count = `GetLevel()-1`; the pip sprite is
  harvested from vanilla `EnemyHud.m_baseHud` level_2/level_3, NOT a Unicode ★ — Daniel 2026-06-19.)
- **AT-LENS-RING-4** — a **trophy-less** hostile (e.g. a summoned minion) appears on the ring at
  the right bearing/size wearing the **generic fallback glyph**, not missing entirely.
- **AT-LENS-RING-5** — **zero** hostiles → the trophy slots are empty but a **faint solar ring**
  outline shows (worn + charged; `ShowEmptyRing` default ON). A **depleted** lens → ring **off**.
  Removing the lens → ring gone immediately.
- **AT-LENS-RING-AGGRO** (the Rune-of-Awareness element, Daniel 2026-06-19) — a hostile that is
  **idle/unalerted** renders its trophy **yellow**; once it **aggros another player** the trophy
  turns **orange**; once it **targets YOU** it turns **red**. State follows the creature's own
  `BaseAI` (IsAlerted / GetTargetCreature vs the local player) — the same surface vanilla's nameplate
  uses.
- **AT-LENS-RING-CAMREL** (🔴 thesis guard) — the ring is **camera-relative, never north-up**.
  Standing still and rotating the camera sweeps every trophy around the ring; the ring grants
  **no cardinal orientation** (that stays the Iron Compass's exclusive payoff). No north arrow,
  no N/E/S/W letters.
- **AT-LENS-RING-PERF** — a Swamp horde (10+ hostiles) does not tank framerate: icons are pooled
  and capped at `RingMaxIcons`; the trophy-sprite + star-pip caches resolve each species/sprite once.

---

## 4. Config + assets impact

**New config (Plugin `Sunstone` section, all live-tunable so Daniel converges feel in one
joined session — the banner-windsock pattern):** `RingRadiusPx` (~180), `RingCenterOffsetY`
(0), `RingIconMinPx` (~28), `RingIconMaxPx` (~64), `RingMaxIcons` (~12), `ShowEmptyRing`
(**true** — Daniel 2026-06-19, faint solar ring), `ShowDepletedHint` (**false** — ring off when
depleted), `DebugTextReadout` (false). Defaults baked as `SunstoneLensHudOverlay` consts (single
source of truth, the existing `Default*` pattern). The existing detection knobs
(`DetectRadius`, `DetectIntervalSeconds`, charge economy) are unchanged.

**New asset (one):** `assets/icons/items/threat_fallback_v0.1.png` — the generic glyph for
trophy-less hostiles. Placeholder-grade (skull/danger) per the icon doctrine. **Star pips reuse the
real vanilla nameplate star** harvested from `EnemyHud.m_baseHud` (Daniel 2026-06-19) — NOT a new
asset and NOT Unicode ★ (the ★ survives only as a last-ditch fallback if the harvest fails).
**No per-creature art** — trophies reuse vanilla `m_icons[0]`.

**Files the impl card touches:** `SunstoneLensHudOverlay.cs` (the render rewrite),
`Plugin.cs` (the new config binds), this doc + `sunstone-lens-impl-spec.md` §4/§5/§8 (render
sections superseded) + `PIECES_AND_CRAFTABLES.md` (Lens visual/patch-surface rows) +
`PLAYER_GUIDE.md` (lens paragraph). **SpecCheck manifest: no change** (no recipe/piece change —
this is render-only). Spec-and-code move together per AGENTS.md.

---

## 5. Open questions for Daniel — ✅ ALL RESOLVED (2026-06-19)

1. **Q2 confirmation:** ✅ **screen-space camera-relative radar** (Daniel: "not relevant I think" =
   not asking for diegetic 3D). Build the recommended option.
2. **Ring center + radius feel:** defaults stand (screen-center, ~180px) — config-tunable, converge
   live. Not blocking.
3. **Empty-ring affordance:** ✅ **faint solar ring** (Daniel: "faint effect of some sort, like a
   very faint solar ring") → `ShowEmptyRing` default **ON**.
4. **Depleted hint:** ✅ **off** (Daniel) → `ShowDepletedHint` default **OFF**.
5. **Star pips:** ✅ **use the vanilla nameplate stars** (Daniel) — harvested from `EnemyHud`, §1.5.

**Plus a new locked element (Daniel 2026-06-19):** aggro-state colour coding (§1.8) — yellow/orange/
red threat tint, modelled on Rune Magic's Rune of Alertness, reproduced from vanilla `BaseAI`
primitives.

These were pacing/UX/art-scope calls; the build is structurally identical whichever way each
lands (each changes an isolated config default or one asset).
