---
title: "Iron Compass → minimap + full-map north-ring — the compass-gated cardinal overlay (design decision, awaiting Daniel's gate)"
status: proposed
purpose: "Architect design decision for Daniel's 2026-06-20 idea: when the Iron Compass is equipped AND an SBPR map surface (carry-disc OR full-map modal) is showing, draw a compass-gated north indicator ON that surface as an IRON bezel + N + ticks; else fall back to the current TopCenter HUD needle. STRUCTURAL TWIN of sunstone-lens-minimap-handoff.md (t_3129842a, PR #214 merged). Both render a HUD feature onto a map surface, resolved by ONE invariant — 'north is compass-gated.' Sunstone MUST NOT show north; Compass MUST. This doc (a) consciously SUPERSEDES the iron-compass-impl-spec §0 / AT-COMPASS-NOMAP-SAFE / decision-log 'never a north arrow on any map' thesis — 'never UNGATED; the compass-gated ring is the sanctioned exception'; (b) RE-GROUNDS the integration against what actually SHIPPED (IThreatMarkerProvider landed via t_91e86951 — world-positioned/per-rebuild/disc-only, the WRONG shape for a screen-bearing N marker, which is instead a player-chevron-sibling needing NO new pull-seam); (c) folds in Daniel's 4 answers: HUD needle hides, N+ticks on a compass-gated iron bezel that reverts to bronze when unworn, an OPT-IN auto-north-orient config (default OFF), and the FULL-MAP modal in scope (not disc-only). Every code line cited against main @ b618aa8. Card t_85a46f42. Daniel gates the decision AND the merge."
owner: Daniel (design authority); Starbright (architect — capture + grounding)
supersedes_partial:
  - "docs/v3/planning/iron-compass-impl-spec.md §0 thesis (:21-24) — 'never by adding a north arrow back onto the map' becomes 'never UNGATED; the compass-gated map ring is the sanctioned exception'"
  - "docs/v3/planning/iron-compass-impl-spec.md AT-COMPASS-NOMAP-SAFE (:529-533) — 'the map stays north-blind' gains the compass-gated carve-out"
  - "docs/v3/planning/iron-compass-impl-spec.md decision-log (:569-570) — 'the compass NEVER adds a north arrow to any map … non-negotiable' re-worded to the gated exception"
  - "docs/v2/planning/cartography-impl-spec.md §2H.1 (:725) / AT-LMAP-TC-5 (:2777-2778) / AT-TABLEVIEW-ROT-1 (:2784-2786) — 'no north indicator anywhere' gains 'except the compass-gated ring when the Iron Compass is worn'"
  - "MapSurface.cs:117-118 (MapRotationSign comment 'no north-up alternative') — gains the OPT-IN auto-north-orient override (default OFF) per §6"
---

# Iron Compass → minimap + full-map north-ring — the compass-gated cardinal overlay

> **STATUS: PROPOSED — Daniel gates this before it becomes an impl-spec.** Structural
> **twin** of [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)
> (card `t_3129842a`, merged PR #214; impl graduated via `t_91e86951`). They are
> resolved by **one** invariant — *north is compass-gated* (§5). 🟢 DECIDED = Daniel's
> locked call or forced-by-grounding architecture; 🟡 OPEN = a knob Daniel is still
> exploring; 🔵 GROUNDED = verified against `main` @ `b618aa8`.
>
> 🔴 **This doc consciously SUPERSEDES two locked theses** (§4 the "never a north
> arrow on any map" thesis; §6 the "no north-up alternative" disc-rotation lock). Both
> are Daniel deliberately re-opening his own prior locks, not contradictions smuggled
> in. Read §4 first if you read only one section.

Daniel's idea, verbatim (2026-06-20, v0.2.30-playtest, in-game):

> "or rather design, when the compass is equipped, I would like for the minimap to
> show a compass ring indicating north superimposed over it when the minimap is
> available, otherwise fall back to the current behavior."

Daniel's four follow-up answers (2026-06-20, ticket thread `compass-minimap-ring`),
folded in throughout and tabled in §7:

> ① "it goes away." (the HUD needle, when the ring is up)
> ② "just N plus ticks. Replace the bronze ring with an iron one 🙂" … "of course it
>    goes back to bronze when you take the compass off."
> ③ "consider having a setting to auto north orient the minimap when the compass is
>    equipped but don't make it the default setting."
> ④ "We should add a similar indicator for the full map view as well."

---

## 0. The idea, and the coherence that makes it survive the no-north pillar

The Iron Compass today grants cardinal orientation on a **separate TopCenter HUD
needle** (`SBPR_CompassHud.cs`), deliberately NOT on the map — because the map's
withheld orientation IS the design (the no-north pillar, `requirements.md:646`,
re-locked in cartography §2H.1 / AT-LMAP-TC-5 🔵). Daniel's idea: when an SBPR map
surface *is* showing and the compass *is* worn, draw the north indicator **on that
surface**; when none is, fall back to the unchanged HUD needle.

This is coherent with the no-north pillar **only because the ring is
compass-gated.** North never becomes a property of the *map* (which stays
north-blind by default — cartography §2H.1 🔵). It is a property of the *compass*,
merely drawn on the map surface when both are present. A player without the compass
sees a north-blind disc and a north-blind full-map, exactly as today. The compass is
still the sole grantor; it just gains a second (and third) surface to grant on.
**This is the whole argument for superseding the "never on the map" thesis — §4.**

The symmetry with the Sunstone twin is exact and opposite:

| | Sunstone Lens (`t_3129842a`) | Iron Compass (this card) |
|---|---|---|
| Renders on the surface when… | a minimap is present (Lens worn) | a surface is present **AND** compass worn |
| Must show north? | **NO** — blips are world-positional, camera-relative | **YES** — the cardinal marker IS the payoff |
| Resolved by | the surface is north-blind | the surface shows north **iff the compass is worn** |

🟢 **One rule reconciles both (§5): the surface renders a north marker IFF the Iron
Compass is equipped.** No compass → north-blind surface, Sunstone blips stay
pure-relative. Compass worn → the gated ring appears. The two features never conflict
because north is never the surface's property — it is the compass's, drawn on the
surface when worn.

---

## 1. The trigger — what fires the ring, on which surfaces 🔵 GROUNDED

Two reads, both already public on `main`, ANDed together. No new state on either
feature — this card adds only a new *consumer*:

```
(a) a surface is showing:  CartographyViewer.IsMinimapBound   (CartographyViewer.cs:257, static)
                             → MapViewer.IsMinimapBound        (MapViewer.cs:113)
                             → MapSurface.IsActive             (MapSurface.cs:188)
(b) the compass is worn:   SBPR_CompassHud.IsWearingCompass   (SBPR_CompassHud.cs:329, Trinket-slot + name match)
```

🟢 **Scope is BOTH SBPR surfaces (Daniel answer ④), not disc-only.** The carry-disc
and the full-map (M) modal are **the same `MapSurface` class**, config-differentiated
only by `TargetPx` (≈200 disc / 900 modal), sorting order, anchor, and
backdrop/prompts — *"The hierarchy … is identical"* (`MapSurface.cs:1366-1369` 🔵).
Both run the same rotation path (§3.2). So one renderer + one gate covers both
surfaces at once; the modal is not a separate implementation.

🔴 **The disc binds only in nomap-ON** (`LocalMapController.cs:147-150` 🔵:
`shouldBindDisc = … && Game.m_noMap && IsImprinted && ReadSurvey != null`). The
full-map modal opens in both nomap modes (it's the held-Local-Map / Surveyor's-Table
view). So the gate resolves per surface:

| Runtime | Surface showing | Compass worn? | Compass north shows as… |
|---|---|---|---|
| nomap-ON, no map open | none | yes | **HUD needle** (today — unchanged) |
| nomap-ON, carry-disc bound | disc | yes | **iron bezel + N + ticks on the disc**; HUD needle hidden (§7-①) |
| nomap-ON/OFF, full-map (M) open | modal | yes | **iron bezel + N + ticks on the modal** (Daniel ④) |
| any | disc/modal | **no** | nothing (north-blind surface — correct) |
| **nomap-OFF**, no SBPR surface, vanilla minimap in corner | vanilla minimap | yes | 🟡 **OPEN — §7 knob 4** |

🟢 **The SBPR-surface trigger is `IsMinimapBound`** for the disc; the modal's own
`IsActive` covers the full-map case (same `MapSurface.IsActive`, different instance).
The **vanilla** corner minimap (nomap-OFF, no SBPR surface) is the one open question
(§7 knob 4) — and note its asymmetry with the Sunstone twin, which Daniel sent ONTO
the vanilla minimap (north-up, EXEMPT there). North on the *north-up* vanilla minimap
is redundant, not a payoff — see §7 knob 4.

---

## 2. The compass signal — what feeds the ring 🔵 GROUNDED

The compass's north bearing is one render-agnostic scalar, computed every frame in
the HUD overlay's `Update()`:

```csharp
Vector3 euler = cam.transform.eulerAngles;   // .y = yaw (heading)   SBPR_CompassHud.cs:293
float targetZ = -euler.y;                     // needle rotates by −yaw to hold world-north   :301
```

🔵 **This is the exact camera yaw the map surface already reads** for its own
rotation (`ApplyFieldOrientation`, `MapSurface.cs:1084`: `float camYaw =
cam.transform.eulerAngles.y`). Both consume one signal — `Utils.GetMainCamera` /
`GameCamera.instance` yaw — so a marker placed in surface-space inherits the
surface's rotation frame **for free** (§3.2).

🟢 **DECIDED (architecture): the ring needs NO lifted `NorthScreenBearing()`
scalar.** The earlier (closed PR #217) design proposed lifting `−camYaw` to a shared
helper. Grounding shows that is **unnecessary** for the chosen geometry (§3.2): a
glyph riding the rotating `_mapContainer` at container-local "up" tracks world-north
with **zero yaw math** — the existing player-marker idiom (`:1097-1098`). No
compass-side code is lifted at all; the compass feature only needs to expose
`IsWearingCompass` (already `private static`, trivially promotable) and the surface
needs a per-frame "compass worn?" push. Strictly *less* coupling than first thought.

---

## 3. The integration — RE-GROUNDED against what SHIPPED 🔵

> 🔴 **This section is the single biggest change from the earlier (closed PR #217)
> design.** That draft predated the Sunstone twin's *implementation*. The twin has
> since SHIPPED its seam (`IThreatMarkerProvider`, card `t_91e86951`, on `main`),
> and what shipped is the **wrong shape** to carry the compass N marker. The closed
> draft's "create a generic `IDiscOverlayProvider` and fold Sunstone into it" is
> therefore **falsified by reality** and is dropped. The honest architecture below
> needs **no new pull-seam at all.**

### 3.1 What actually shipped — and why the N marker doesn't fit it 🔵

`src/SBPR.Trailborne/Features/Cartography/IThreatMarkerProvider.cs` (merged) is a
registry the disc pulls each rebuild. Its marker struct:

```csharp
public readonly struct DiscThreatMarker {      // IThreatMarkerProvider.cs:41-58
    public readonly Vector3 WorldPos;          // world-positioned
    public readonly Color   Tint;
    public readonly Sprite? Icon;
    public readonly int     Stars;
}
```

The compass N marker is the **opposite of this struct on all three axes**:

| Axis | Shipped `DiscThreatMarker` (Sunstone) | Compass N marker |
|---|---|---|
| Position | **world** (`WorldPos`, projected via `WorldToSurfacePx`) | **screen-bearing** — no world point; rides container-local-up |
| Cadence | **per-rebuild pull** (~0.25 s, inside `RebuildOverlay`, `MapSurface.cs:640`) | **per-frame** — must orbit smoothly as the camera turns (`TickRotation`, not rebuild) |
| Surfaces | **disc only** — `MapSurface.cs:181` 🔵: *"Disc-only; the modal is … not a threat surface"* | **disc AND modal** (Daniel ④) |

🟢 **Forcing the N marker through `IThreatMarkerProvider` would require refactoring
merged, shipped Sunstone code for negative benefit** — adding a screen-bearing,
per-frame, both-surfaces concept into a world-positioned, per-rebuild, disc-only
seam. We do **not** do that. The seams stay separate because the *marker kinds* are
genuinely different (the closed draft's "two marker kinds, one seam" union was a
guess that the shipped reality contradicts).

### 3.2 The N marker is a PLAYER-CHEVRON SIBLING, not a pulled blip 🟢 (forced by grounding)

The cleanest fit is the surface's **existing** persistent overlay element: the
player marker. It is built once, parented under the rotating `_mapContainer`, and
**counter-rotated each frame to stay upright** — exactly what a north glyph needs,
mirror-imaged (pinned to north instead of to the player):

```csharp
// player marker, the template — ApplyFieldOrientation, MapSurface.cs:1085-1098 🔵
float rotZ = MapRotationSign * camYaw;
_mapContainer.localRotation = Quaternion.Euler(0,0, rotZ);     // world spins under you
_playerMarker.rectTransform.localRotation = Quaternion.Euler(0,0, -rotZ);  // glyph stays upright
```

🟢 **The N glyph is the identical idiom, pinned to north:** a persistent child of
`_mapContainer` at a **fixed container-local point** `(0, +r)` (local-up, r ≈ the
bezel radius §3.3). The container's per-frame `localRotation = rotZ` carries it to
world-north's screen position automatically — **zero yaw math**. Counter-rotate the
glyph itself by `−rotZ` for legibility (the `:1098` idiom). Face north → N at 12
o'clock; face east → N orbits to 9 o'clock. Built additively (`new GameObject()` +
`Image`, ADR-0006), toggled by the compass gate — no pull, no per-rebuild gather, no
registry. The tick marks ride the same parent at 90°/180°/270° offsets (or a static
ticked sprite).

🟢 **No new Cartography pull-seam.** The surface needs only a **one-bool push**:
"compass worn? show the N layer : hide it." The lowest-coupling shape is a tiny
Cartography-owned setter the compass feature calls (the dependency arrow stays
`Exploration → Cartography`, an interface/method Cartography owns; Cartography never
references `SBPR_CompassHud` or `IronCompass`). The impl-spec picks the exact shape
(a `MapViewer.SetCompassNorth(bool)` setter, or a `Func<bool>` the surface polls in
`TickRotation`); both keep Cartography unaware of *what* drives the gate.

### 3.3 Radius — the bezel itself becomes the ring (Daniel ②) 🔵

Daniel's answer ② makes the bezel *be* the ring: **"Replace the bronze ring with an
iron one."** The bezel is drawn by `EnsureBezelTexture` (`MapSurface.cs:1315-1361`
🔵), a bronze band between `holeR` and `ringOuterR` from the shared `DiscRingGeometry`
helper (`:1335-1336`), bronze constant `new Color(0.62f, 0.55f, 0.42f, 1f)` (`:1339`
🔵). The N glyph rides at `r ≈ holeR` (on/just inside the iron band).

🔵 **Clear of the Sunstone threat zone — but the margin narrowed at 50 m detect.** The twin quantified threats
landing within the inner ~80 % (~80 px) of the 200 px disc; the compass N at ~94 px
radius still clears a worst-case Sunstone blip (a ~14 px margin, down from ~46 px under the
old 30 m radius). The two overlays remain spatially disjoint — co-existence holds (§5), though
the margin is now modest; worth an in-game eye that a max-range blip near the N bearing doesn't read as cluttered.

> 🔴 **Parent split — the load-bearing impl subtlety.** The **bezel is a child of the
> NON-rotating `_frame`** (`MapSurface.cs:1446` 🔵), so it never spins — correct for
> a recolor (an iron *ring* looks identical at every rotation). But the **N glyph
> must parent to the rotating `_mapContainer`** (§3.2) so it orbits. They share the
> bezel *radius*, not the bezel *parent*. Pin N to the fixed frame by mistake and you
> get a dead, non-orbiting glyph. The iron skin and the N marker ride the same
> *gate*, different *parents*.

### 3.4 The iron recolor — gated, reverts to bronze (Daniel ②) 🟢🔵

🟢 Daniel ② locked the skin as **compass-gated, not permanent**: *"of course it goes
back to bronze when you take the compass off."* One condition (`IsWearingCompass`)
drives the whole compass-disc skin: worn → iron bezel + N + ticks; unworn → bronze
bezel, north-blind. No persistence, no "have I ever held a compass" flag — a
per-frame render branch off the existing equip-gate, same as the N marker.

🔴 **Impl note — the bezel texture is CACHED** (`_bezelTex`, early-returned at
`:1317` 🔵). The recolor cannot just mutate the bronze constant. Two clean paths for
the impl-spec to choose: **(a)** tint the `_bezel` RawImage's `color` (it's set to
`Color.white` at `:1450`; the texture is a white-bronze band, so a per-frame
`_bezel.color = worn ? iron : bronze` recolors it with zero texture rebuild — the
cheapest path); or **(b)** build+cache a second iron texture and swap `_bezel.texture`
on the gate edge. Path (a) is almost certainly right (one color write per gate
change). The impl-spec grounds the exact iron RGB.

🔴 **Coordinate with `t_12e15162` / PR #213 (the `DiscRingGeometry` margin).** That
work tunes the bezel *radius/margin* via the shared `DiscRingGeometry` helper
(`:1335-1336`). This card changes the bezel *color/tint* only — orthogonal, but the
**same code region.** Sequence so the iron recolor reads the margin helper's
post-#213 radii, and the N glyph's `r ≈ holeR` tracks whatever `DiscRingGeometry`
finalizes. Do not hard-code a radius that #213 then moves.

---

## 4. 🔴 The central decision — superseding the "never a north arrow on any map" thesis

This is the load-bearing decision Daniel is locking. The Iron Compass impl-spec
currently forbids exactly this feature, in absolute language. The supersession is
**conscious and worded**, not a silent contradiction.

### 4.1 What the spec says today (verbatim, verified 🔵)

| Site | Current text |
|---|---|
| `iron-compass-impl-spec.md:21-24` (§0 thesis) | *"it grants it on a **separate HUD overlay**, never by adding a north arrow back onto the map. **Putting a north arrow on the local map would delete this item's entire reason to exist** and reverse a Daniel-locked difficulty choice."* |
| `:529-533` **AT-COMPASS-NOMAP-SAFE** | *"the map stays north-blind."* (the overlay adds no north indicator to any map) |
| `:569-570` (decision log) | *"the compass **NEVER** adds a north arrow to any map; the withheld map-orientation stays withheld. This is **non-negotiable design thesis, not a knob.**"* |
| cartography `§2H.1 :725` + `AT-LMAP-TC-5 :2777-2778` | *"there is **no** north indicator, compass rose, north-up mode, or any orienting aid anywhere on the held Local Map."* (and `AT-TABLEVIEW-ROT-1 :2784-2786` for the table/modal view) |

### 4.2 Why the original fear does NOT apply to a compass-gated ring 🟢

The thesis rests on one fear, stated explicitly: *"a north arrow on the map would
delete the compass's reason to exist."* That fear is **correct for an UNGATED north
arrow** — if the map showed north to everyone, the compass would be pointless. It is
**false for a compass-gated ring**:

- The ring appears **only when the compass is worn** (§1 gate, §5 rule). A player
  without the compass sees a north-blind disc AND a north-blind full-map — the
  disorientation pillar is intact for them on every surface.
- The compass is **still the grantor.** It does not give north *to the map*; it
  grants north *through itself*, now on more surfaces. The earned payoff is unchanged
  — you still must craft and wear the iron-tier trinket to get any cardinal reference.
- North is never a **property of the map.** It is a property of the **compass**,
  drawn on the map when both are present (§5). Remove the compass and the map is
  north-blind again, instantly (and the iron bezel reverts to bronze, §3.4).

So the supersession does not reverse the difficulty choice — it **relocates the
compass's existing payoff onto better surfaces** while preserving every gate that
made it earned. The thesis was over-broad: it banned "north on the map" when what it
meant to ban was "**ungated** north on the map."

### 4.3 The exact re-wording (carried by the post-gate impl-spec PR, NOT this PR) 🟢

| Site | Becomes |
|---|---|
| §0 thesis `:21-24` | *"it grants it on a separate HUD overlay **or, when the Iron Compass is worn and an SBPR map surface (carry-disc or full-map) is showing, as a compass-gated north ring on that surface** — never by adding north to the map **ungated**. North remains an earned, compass-only payoff; a player without the compass sees a north-blind map on every surface."* |
| AT-COMPASS-NOMAP-SAFE `:529-533` | keep the NoMap-HUD-safe guarantee; add a sibling line — *"and it adds no **ungated** north indicator to any map. The compass-gated ring (iron-compass-minimap-ring.md) is the sanctioned exception: north appears on an SBPR surface **iff the compass is worn**, so the map stays north-blind for the compass-less player."* |
| decision-log `:569-570` | *"the compass NEVER adds **ungated** north to any map; the compass-gated ring is the **one** sanctioned exception (Daniel, 2026-06-20). The withheld orientation stays withheld for anyone not wearing the compass. The **gating** is non-negotiable; the surface (HUD vs map ring) and the opt-in north-up lock (§6) are the §7 knobs."* |
| cartography `§2H.1` / `AT-LMAP-TC-5` / `AT-TABLEVIEW-ROT-1` | append: *"— except the compass-gated north ring, which renders on the disc AND the full-map/table view only while the Iron Compass is equipped (iron-compass-minimap-ring.md §5). The surface itself remains north-blind; the ring is the compass's payoff drawn on the surface, not a property of the map."* |

🟡 **This re-wording is the gate.** Daniel ratifies the supersession (or rejects it,
leaving the compass HUD-only). Nothing is edited in the shipped specs until he does —
the same standalone-then-graduate path the Sunstone twin followed (§8).

---

## 5. The invariant interaction — one rule resolves both twins 🟢

The two cards carry **opposite** north-invariants:

- **Sunstone MUST NOT show north** — `AT-LENS-DISC-CAMREL` /
  `AT-LENS-RING-CAMREL` (camera-relative, never north-up; grants no cardinal
  orientation; `sunstone-lens-minimap-handoff.md` §6 🔵).
- **Compass MUST show north** — the cardinal marker IS the payoff (§0, §4).

🟢 **Both are resolved by a single rule:**

> **An SBPR map surface renders a north marker IFF the Iron Compass is equipped.**

- **No compass worn** → the surface stays north-blind (cartography §2H.1 default
  holds), and Sunstone blips remain pure camera-relative world positions. Neither
  feature shows north.
- **Compass worn** → the compass gate flips the iron bezel + N + ticks on; it is the
  **only** surface element permitted to encode north. Sunstone blips are *still* not
  north — they remain world-positioned dots; north is a separate, compass-owned
  overlay element drawn alongside them.

The features never conflict because **north is never a property of the surface** — it
is a property of the **compass**, drawn on the surface only when worn. Sunstone-on-disc
and Compass-on-disc can both be active at once (worn compass + worn lens + bound
disc): the lens contributes world-blips in the inner ~80 %, the compass contributes
one N glyph at the bezel radius (§3.3), spatially disjoint, neither contaminating the
other's invariant.

🔴 **New acceptance test — AT-DISC-NORTH-GATED** (the rule's guard): with a bound
disc, wearing **only the Sunstone Lens** (no compass) shows threat blips and **no**
north marker, and the bezel stays **bronze**; donning the compass turns the bezel
**iron** and makes the N + ticks appear; doffing it reverts the bezel to bronze and
removes the N while the blips persist. The surface is north-blind exactly when the
compass is off.

---

## 6. 🔴 The SECOND supersession — opt-in auto-north-orient (Daniel ③, default OFF)

Daniel ③: *"consider having a setting to auto north orient the minimap when the
compass is equipped but don't make it the default setting."* This is a **second,
deeper** opt-in representation of compass-gated north, and it **consciously
re-opens a different lock** than §4.

### 6.1 What it overrides 🔵

The disc/modal rotation is hard-locked to heading with an explicit "no alternative"
comment:

```csharp
// §2H.1 b4 … rotation sense … Single knob, shared by both surfaces; no north-up
// alternative (disorientation is the intended design — Daniel).   MapSurface.cs:117-118 🔵
private const float MapRotationSign = 1f;
```

Auto-north-orient is exactly the "north-up alternative" that comment says doesn't
exist. 🟢 **It is sanctioned ONLY because it is (a) compass-gated AND (b) opt-in,
default OFF.** The default experience is unchanged: heading-up disorientation with
the iron N-ring riding the rim (§3). A player must both wear the compass AND flip a
non-default config to get a north-up-locked map. The pillar's default is intact.

### 6.2 What it does, and the interaction with the ring 🟡 (design for Daniel)

When enabled **and** the compass is worn, the surface **locks north-up** instead of
heading-up: the map stops rotating (`_mapContainer.localRotation` pinned to north,
i.e. `rotZ = 0`), and the **player chevron rotates** to show heading (vanilla-style)
instead of staying screen-up. Mechanically this is the inverse of §3.2: today the
container spins and the chevron counter-spins; in north-up-lock the container is
fixed and the chevron spins.

🟡 **Open interaction questions (architect surfaces, Daniel decides):**
1. **Does the N-ring still render when north-up-locked?** Two readings: **(a)** N is
   redundant when the whole map is north-up (north is always at 12 o'clock), so hide
   the orbiting N but keep the iron bezel as the "compass-gated" tell; or **(b)** keep
   a fixed N at 12 o'clock as confirmation. *Architect lean: (a) — a static N at
   top is noise when the entire map is north-up; the iron bezel already signals the
   compass is active. Cheap to flip.*
2. **Exclusive modes or independent?** Is auto-north-orient a third value of the
   §7-knob-1 enum (`HudOnly` / `DiscWhenBound` / `DiscNorthUp`), or an independent
   bool that composes with it? *Architect lean: an INDEPENDENT bool
   (`CompassAutoNorthUp`, default false). The §7-knob-1 enum decides HUD-vs-surface;
   this bool decides heading-up-vs-north-up for the surface case. Composing two small
   orthogonal config entries is cleaner than a 4-way enum that conflates two axes.*
3. **Both surfaces?** Does north-up-lock apply to the disc only, or disc + modal
   (Daniel ④ pulled the modal into scope generally)? *Architect lean: both, for
   consistency — same `MapRotationSign` path drives both (`:1085`).* 🔴 Note: the
   `MapRotationSign` const is **shared by both surfaces** (`:117`), so a north-up
   override must be a per-surface runtime branch, not a const flip (a const flip would
   force north-up on every surface for every player — exactly the ungated change the
   pillar forbids).

🔴 **Impl note:** this is a bigger lift than the N-ring (§3) — it changes the
`ApplyFieldOrientation` rotation path conditionally, not just adds an overlay child.
The impl-spec should sequence it as a **separable second milestone** behind the
iron-bezel + N-ring (§3), so the simple, high-value default ships first and the
opt-in north-up-lock follows. Both are gated on Daniel ratifying §4 first.

### 6.3 The dead-pump guard (#208/#209 — load-bearing across BOTH features) 🔴🔵

Daniel ① locked: when the surface ring is up, the **HUD needle hides** (*"it goes
away"*). The pitfall this MUST NOT step on is the one both overlays already learned:

```csharp
// SBPR_CompassHud.Update — visibility toggles _content; the HOST stays active so
// Update keeps pumping (t_61aff612).   SBPR_CompassHud.cs:250,254-259 🔵
bool wearing = IsWearingCompass(player);
if (wasVisible != wearing) SetVisible(wearing);   // toggles _content child, NEVER _root host
```

🔴 **"HUD needle hides" means toggle the HUD overlay's `_content` child, NEVER
deactivate the host or stop `Update()`.** The host carries the `Update` pump that
reads `cam.transform.eulerAngles.y` (`:293`) — the **same yaw the disc/modal ring
consumes** (§2). Freeze that pump and both the HUD needle AND the surface ring die.
The compass HUD already shipped this exact fix (#208/#209, `SetVisible` toggles
`_content` at `:250-259`). Locked as **AT-COMPASS-DISC-PUMP**: under `DiscWhenBound`,
hiding the HUD needle keeps the `Update` pump alive; closing the surface restores the
needle with no dead frame.

---

## 7. The knobs — Daniel's 4 answers (folded) + what remains open

| # | Knob | Status |
|---|---|---|
| 1 | **HUD needle: replace vs both** | 🟢 **ANSWERED (Daniel ①): HUD needle hides** (*"it goes away"*). Realized as a live `CompassDiscMode` enum (escape hatch), default `DiscWhenBound`. |
| 2 | **North representation** | 🟢 **ANSWERED (Daniel ②): N + ticks** (not full N/E/S/W), on a **compass-gated IRON bezel** that reverts to bronze when unworn (§3.3-3.4). |
| 3 | **Auto-north-orient** | 🟢 **ANSWERED (Daniel ③): add it, default OFF** — opt-in north-up lock (§6). The *interaction* sub-questions (§6.2) remain open. |
| 4 | **Scope** | 🟢 **ANSWERED (Daniel ④): disc AND full-map modal** (§1), not disc-only. |
| 5 | **Ring radius** | 🟢 **Subsumed by ②** — the ring IS the iron bezel (`r ≈ holeR`, §3.3). Not a separate knob. |
| 6 | **nomap-OFF / vanilla minimap** | 🟡 **OPEN** — see below. |
| 7 | **Seam sequencing with the twin** | 🟢 **Resolved by grounding** — no shared seam; the N marker is a chevron-sibling (§3.2), independent of the shipped `IThreatMarkerProvider`. Nothing to co-sequence. |

### The live config (banner-windsock pattern, mirroring the shipped Sunstone enums) 🔵

The Sunstone twin shipped `LensMinimapHandoffMode` + `LensMinimapBlipStyle` as live
`Config.Bind` enums (`Plugin.cs:127-128` 🔵). The compass mirrors that:

```
IronCompass.CompassDiscMode  (Config enum, "IronCompass" section, live-tunable)
  • HudOnly       — ignore the surface; HUD needle always (today's behaviour; escape hatch)
  • DiscWhenBound — worn AND a surface showing: HUD needle hides, surface ring shows; else HUD  ← 🟢 DEFAULT (Daniel ①)
  • Both          — HUD needle AND surface ring both render whenever a surface is showing

IronCompass.CompassAutoNorthUp  (Config bool, default FALSE)  ← 🟢 Daniel ③ (§6)
  • false — surface stays heading-up; iron N-ring orbits the rim (the default)
  • true  — worn + surface showing → surface locks north-up, chevron rotates (opt-in §6)
```

🟡 **Knob 6 — nomap-OFF / vanilla minimap (the one genuinely open knob).** In
nomap-OFF there is no SBPR surface; the vanilla corner minimap owns the corner.
Options: **(a)** HUD needle stays (clean — the surface ring is an SBPR-surface
feature; matches where SBPR surfaces exist); **(b)** draw on the vanilla minimap;
**(c)** out of scope. 🔴 **Asymmetry with the Sunstone twin worth Daniel's eye:** the
twin sent its detection ONTO the vanilla minimap in nomap-OFF, reasoning the vanilla
minimap is **north-up and EXEMPT** (the player already has free cardinal orientation
there, so detection leaks nothing). That same fact **argues AGAINST** a compass ring
there: on a north-up vanilla minimap, north is *already* at 12 o'clock for free — a
compass N-ring would be **redundant decoration, not an earned payoff**, and the iron
bezel reskin can't apply (it's an SBPR-surface element). *Architect lean: (a) — HUD
needle stays in nomap-OFF; the surface ring is a no-map-world feature where
orientation is actually withheld. Daniel confirms.*

---

## 8. Clean/dirty routing + spec impact

**Clean/dirty: CLEAN-SIDE.** 🟢 All SBPR-authored (`Features/Exploration/` +
`Features/Cartography/`). Reads vanilla `Hud`/`GameCamera`/`Utils`/`Player`/
`Inventory` only — base-game, fair to read+adapt per ADR-0001. No third-party mod
code. Route **`architect`** (this doc) → **engineer-ui** (impl).

**SpecCheck/manifest impact: NONE.** 🔵 Render-only; no recipe/piece/station/item
change. The Iron Compass recipe row in `SpecCheck.cs` is untouched.

**ADR-0006 (additive):** the N glyph + ticks are built additively
(`new GameObject()` + `Image`/`RectTransform` under `_mapContainer`), reading no
vanilla prefab as a mutable base.

**Docs that move (deferred to the post-gate impl-spec PR — NOT this PR).** This PR
adds only this standalone design doc + its manifest rows. The dependent surgery
depends on which `CompassDiscMode` Daniel ratifies and on §4 being gated, so it lands
with the impl-spec — the standalone-then-graduate path `sunstone-lens-minimap-handoff.md`
followed:

| Doc | Change |
|---|---|
| `docs/v3/planning/iron-compass-impl-spec.md` | **The heavy one** — the §4.3 re-wording of §0 `:21-24`, AT-COMPASS-NOMAP-SAFE `:529-533`, decision-log `:569-570`; add the `CompassDiscMode` enum + `CompassAutoNorthUp` bool + AT-COMPASS-DISC-* tests. |
| `docs/v2/planning/cartography-impl-spec.md` | §2H.1 `:725` + AT-LMAP-TC-5 `:2777-2778` + AT-TABLEVIEW-ROT-1 `:2784-2786` gain the compass-gated carve-out (§4.3); the `MapRotationSign` "no north-up alternative" note (`:117`) gains the opt-in §6 override. |
| `docs/design/nomap.md` | one-line pointer to the surface-ring branch of the compass. |
| `docs/design/map-provider-model.md` | the disc + modal gain a north-overlay element; note it is a chevron-sibling, NOT routed through `IThreatMarkerProvider` (§3.1). |
| `docs/design/sunstone-lens-minimap-handoff.md` | cross-ref: the compass N marker is deliberately **separate** from the shipped `IThreatMarkerProvider` (different marker kind — §3.1); the single "north is compass-gated" rule (§5) reconciles the two features. |
| **NEW** `docs/v3/planning/iron-compass-minimap-ring-impl-spec.md` | The buildable spec: the iron-bezel gated recolor, the N-glyph chevron-sibling renderer (Option A), the one-bool Cartography push-setter, the `CompassDiscMode` enum + `CompassAutoNorthUp` bool, and the AT-COMPASS-DISC-* + AT-DISC-NORTH-GATED tests. **Two milestones**: M1 = iron bezel + N-ring (high value, low risk); M2 = opt-in north-up lock (§6, bigger lift). |

🔴 **Sequencing with the twin — NO shared seam (re-grounded).** The closed draft
recommended a co-owned disc-overlay seam. That is **moot**: the Sunstone seam shipped
(`IThreatMarkerProvider`), and the compass N marker doesn't use it (§3.1-3.2). The
two impl-specs are independent. The compass impl-spec can graduate the moment Daniel
gates §4 — it waits on nothing from Sunstone.

---

## 9. Proposed acceptance tests (impl-spec will formalize)

- **AT-COMPASS-DISC-RING** — nomap-ON + a carry-disc bound + the Iron Compass worn →
  the bezel renders **iron** with an N + ticks on the rim; per `CompassDiscMode` the
  HUD needle hides (`DiscWhenBound`), both show (`Both`), or only HUD (`HudOnly`).
- **AT-COMPASS-MODAL-RING** (🔴 Daniel ④) — opening the full-map (M) / table view with
  the compass worn → the same iron bezel + N + ticks render on the **modal**, at the
  modal's 900 px scale. The modal is in scope, not disc-only.
- **AT-COMPASS-DISC-ROTATE** (🔴 geometry) — standing still and rotating the camera
  sweeps the N around the rim: face north → N at 12 o'clock; face east → N at 9
  o'clock; a full 360° camera turn sweeps N a full 360°. The N glyph stays upright
  (counter-rotated, §3.2).
- **AT-COMPASS-BEZEL-GATED** (🔴 Daniel ②) — donning the compass turns the bezel iron;
  doffing it reverts the bezel to bronze (no persistence, no "ever held" flag).
- **AT-DISC-NORTH-GATED** (🔴 §5 rule — shared invariant with the Sunstone twin) —
  with a bound surface, wearing only the Sunstone Lens (no compass) shows blips, a
  **bronze** bezel, and **no** north; donning the compass turns the bezel iron and
  shows N; doffing removes N + reverts bronze while blips persist.
- **AT-COMPASS-DISC-PUMP** (🔴 #208/#209 guard) — under `DiscWhenBound`, hiding the
  HUD needle keeps the overlay's `Update` pump alive (the yaw read never freezes);
  closing the surface restores the HUD needle with **no dead frame**.
- **AT-COMPASS-AUTONORTH** (🔴 §6, opt-in) — with `CompassAutoNorthUp = true` + compass
  worn, the surface locks north-up (map stops rotating, chevron rotates); with it
  `false` (default), the surface stays heading-up with the orbiting N-ring. The toggle
  is compass-gated (no compass → heading-up regardless).
- **AT-COMPASS-DISC-CLEAN** (clean-room) — no third-party mod code; all hooks are
  base-game primitives (`Hud`, `GameCamera`, `Utils`, `Inventory`) + SBPR-owned
  Cartography types.

---

## 10. Open questions for Daniel (the gate)

1. 🔴 **Ratify the §4 thesis supersession?** The Iron Compass impl-spec says, in
   non-negotiable language, "the compass NEVER adds a north arrow to any map." This
   card re-words that to "never **ungated**; the compass-gated ring is the sanctioned
   exception." **This is the central decision** — everything else is downstream.
   (Reject → the compass stays HUD-only and this card closes.)
2. **`CompassDiscMode` default** — `DiscWhenBound` (proposed, matches Daniel ①),
   `Both`, or `HudOnly`?
3. **§6 auto-north-orient interaction** (§6.2) — (a) does the N-ring still show when
   north-up-locked? (b) independent bool vs enum value? (c) disc-only or disc+modal?
   *Architect leans: hide orbiting N / independent bool / both surfaces.*
4. **Knob 6 — nomap-OFF / vanilla minimap** — HUD needle stays (proposed), draw on
   the vanilla minimap, or out of scope? (Architect lean: HUD stays — north is free on
   the north-up vanilla minimap, so a ring there is redundant.)
5. **Milestone split** — ship M1 (iron bezel + N-ring) first, M2 (opt-in north-up
   lock) second? (Architect recommendation: yes — M1 is high-value/low-risk.)

On Daniel's answers — especially Q1 — this graduates to
`iron-compass-minimap-ring-impl-spec.md` and an impl card is cut for **engineer-ui**.
It waits on nothing from the Sunstone twin (§8).

---

## Links

- **Twin:** [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)
  (card `t_3129842a`, PR #214 merged; impl `t_91e86951`). The shipped
  `IThreatMarkerProvider` is **deliberately not reused** here (§3.1); the single
  "north is compass-gated" rule (§5) reconciles the two.
- Compass: `Features/Exploration/IronCompass.cs`, `SBPR_CompassHud.cs`; impl-spec
  [`../v3/planning/iron-compass-impl-spec.md`](../v3/planning/iron-compass-impl-spec.md)
  (the §4 supersession target).
- Surfaces: `MapViewer.cs`, `MapSurface.cs`, `LocalMapController.cs`,
  `CartographyViewer.cs`, `DiscRingGeometry.cs`; specs
  [`../v2/planning/cartography-impl-spec.md`](../v2/planning/cartography-impl-spec.md),
  [`map-provider-model.md`](map-provider-model.md).
- Shipped seam (NOT reused): `Features/Cartography/IThreatMarkerProvider.cs`.
- Sibling cartography work to coordinate: `t_12e15162` (disc-ring margin, PR #213) —
  same `EnsureBezelTexture` region (§3.4).
- Reported via /bug thread `compass-minimap-ring`. Card `t_85a46f42`.
- Grounded against `main` @ `b618aa8` (every cited code line read directly).
