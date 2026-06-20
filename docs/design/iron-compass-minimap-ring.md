---
title: "Iron Compass → minimap disc north-ring — the compass-gated cardinal overlay (design decision, awaiting Daniel's gate)"
status: proposed
purpose: "Architect design decision for Daniel's 2026-06-20 idea: when the Iron Compass is equipped AND a local-map minimap disc is available, draw a north indicator ON the disc (compass-gated), else fall back to the current TopCenter HUD needle. STRUCTURAL TWIN of sunstone-lens-minimap-handoff.md (t_3129842a): both render a HUD feature onto the disc when one is bound, share ONE disc-superimpose seam, and are resolved by ONE invariant — 'north is compass-gated.' Sunstone MUST NOT show north; Compass MUST. This doc (a) consciously SUPERSEDES the iron-compass-impl-spec §0 / AT-COMPASS-NOMAP-SAFE / decision-log 'never a north arrow on any map' thesis — re-wording absolute 'never' to 'never UNGATED; the compass-gated disc ring is the sanctioned exception'; (b) designs the shared MapSurface disc-overlay seam carrying BOTH world-positioned blips (Sunstone) and a single cardinal/bearing marker (Compass); (c) locks rotate-with-heading geometry (north rides container-local-up, same frame as terrain). Every code line cited against main @ 1e58196. Card t_85a46f42. Daniel gates the decision AND the merge."
owner: Daniel (design authority); Starbright (architect — capture + grounding)
supersedes_partial:
  - "docs/v3/planning/iron-compass-impl-spec.md §0 thesis (:20-24) — 'never by adding a north arrow back onto the map' becomes 'never UNGATED; the compass-gated disc ring is the sanctioned exception'"
  - "docs/v3/planning/iron-compass-impl-spec.md AT-COMPASS-NOMAP-SAFE (:529-533) — 'adds no north indicator to any map' gains the compass-gated disc carve-out"
  - "docs/v3/planning/iron-compass-impl-spec.md decision-log (:569-570) — 'the compass NEVER adds a north arrow to any map … non-negotiable' re-worded to the gated exception"
  - "docs/v2/planning/cartography-impl-spec.md §2H.1 / AT-LMAP-TC-5 (:725, :2777-2778) — 'no north indicator … anywhere on the held Local Map' gains 'except the compass-gated disc ring when the Iron Compass is worn'"
---

# Iron Compass → minimap disc north-ring — the compass-gated cardinal overlay

> **STATUS: PROPOSED — Daniel gates this before it becomes an impl-spec.** This is
> the structural **twin** of [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)
> (card `t_3129842a`, merged PR #214). Design them together: they share **one**
> disc-superimpose seam (§3) and are resolved by **one** invariant —
> *north is compass-gated* (§5). 🟡 OPEN rows are knobs Daniel is exploring (NOT
> pre-decided); 🟢 DECIDED rows are architecture or Daniel's locked calls;
> 🔵 GROUNDED rows are facts verified against `main` @ `1e58196`.
>
> 🔴 **This doc consciously SUPERSEDES a "non-negotiable" spec thesis** (§4). It is
> not a contradiction smuggled in — it is Daniel deliberately re-opening the Iron
> Compass impl-spec's absolute "never a north arrow on any map" language and
> re-wording it to "never *ungated*; the compass-gated disc ring is the sanctioned
> exception." Read §4 first if you only read one section.

Daniel's idea, verbatim (2026-06-20, v0.2.30-playtest, in-game):

> "or rather design, when the compass is equipped, I would like for the minimap to
> show a compass ring indicating north superimposed over it when the minimap is
> available, otherwise fall back to the current behavior."

---

## 0. The idea, and the coherence that makes it survive the no-north pillar

The Iron Compass today grants cardinal orientation on a **separate TopCenter HUD
needle** (`SBPR_CompassHud.cs`), deliberately NOT on the map — because the map's
withheld orientation IS the design (the no-north pillar, `requirements.md:646`,
re-locked in cartography §2H.1). Daniel's idea: when the carry-state minimap disc
*is* up and the compass *is* worn, draw the north indicator **on the disc**; when
no disc is bound, fall back to the unchanged HUD needle.

This is coherent with the no-north pillar **only because the ring is
compass-gated.** North never becomes a property of the *disc* (which stays
north-blind by default — cartography §2H.1, AT-LMAP-TC-5 🔵). It is a property of
the *compass*, merely drawn on the disc surface when both are present. A player
without the compass sees a north-blind disc, exactly as today. The compass is
still the grantor; it just gains a second surface to grant on. **This is the whole
argument for superseding the "never on the map" thesis — see §4.**

The symmetry with the Sunstone twin is exact and opposite:

| | Sunstone Lens (`t_3129842a`) | Iron Compass (this card) |
|---|---|---|
| Renders on the disc when… | `IsMinimapBound` (no equip-gate beyond the Lens itself) | `IsMinimapBound` **AND** compass worn |
| Must show north? | **NO** — blips are world-positional, camera-relative (AT-LENS-RING-CAMREL) | **YES** — the cardinal marker IS the payoff |
| Resolved by | the disc is north-blind | the disc shows north **iff the compass is worn** |

🟢 **One rule reconciles both (§5): the disc renders a north marker IFF the Iron
Compass is equipped.** No compass → north-blind disc, Sunstone blips stay
pure-relative. Compass worn → the gated ring appears. The two features never
conflict because north is never the disc's property — it is the compass's, drawn
on the disc when worn.

---

## 1. The trigger — what "when the minimap is available" keys on 🔵 GROUNDED

Identical trigger to the Sunstone twin — the single clean public read for "an SBPR
disc is showing":

```
CartographyViewer.IsMinimapBound   (CartographyViewer.cs:257, static)
  → MapViewer.IsMinimapBound        (MapViewer.cs:113: _disc != null && _disc.IsActive)
  → MapSurface.IsActive             (MapSurface.cs:188: _root != null && _root.activeSelf)
```

🔴 **The same nomap-ON-only scope nuance the Sunstone card hit.** The SBPR disc
binds **only in nomap-ON** (`LocalMapController.cs:147-150` 🔵):

```csharp
bool shouldBindDisc = _provider != null
                      && Game.m_noMap                       // §5 — nomap-ON only
                      && LocalMap.IsImprinted(_provider!)
                      && LocalMap.ReadSurvey(_provider!) != null;
```

In **nomap-OFF** the vanilla global minimap owns the corner and the SBPR disc
stands down — there is a minimap but **no SBPR surface to draw on**. So the compass
ring's full gate is:

| Runtime config | Compass worn? | SBPR disc? | Compass north shows as… |
|---|---|---|---|
| nomap-ON, no local map bound | yes | No | **HUD needle** (today — unchanged) |
| nomap-ON, local map bound + imprinted | yes | **Yes** | **Ring on the disc** (this card) — HUD needle suppressed per §6 knob |
| nomap-ON, disc bound | **no** | Yes | nothing (north-blind disc — correct) |
| **nomap-OFF** (vanilla minimap) | yes | No | 🟡 **OPEN — Daniel decides (§6 knob 4)** |

🟢 **DECIDED by grounding: the disc-availability trigger is
`CartographyViewer.IsMinimapBound`** (same as Sunstone). The *additional* compass
gate is `SBPR_CompassHud.IsWearingCompass` (already exists, `:329` 🔵). Both reads
exist today; this card adds no new state to either feature — only a new consumer.

---

## 2. The compass signal — what feeds the disc ring 🔵 GROUNDED

The compass's north bearing is a single render-agnostic scalar, computed every
frame inside the HUD overlay's `Update()`:

```csharp
Vector3 euler = cam.transform.eulerAngles;   // .y = yaw (heading)   SBPR_CompassHud.cs:293
float targetZ = -euler.y;                     // needle rotates by −yaw to hold world-north   :301
```

🔵 **This is the exact same camera yaw the disc already reads** for its own
rotation (`ApplyFieldOrientation`, `MapSurface.cs:963`: `float camYaw =
cam.transform.eulerAngles.y`). Both surfaces consume one signal — `Utils.GetMainCamera`
yaw — so a ring placed in disc-space inherits the disc's rotation frame for free
(§3.2). That shared-signal fact is the whole reason the geometry is locked, not a
knob (§3.2).

🟢 **DECIDED (architecture): the disc ring does NOT need a lifted
`NorthScreenBearing()` scalar.** The ticket proposed lifting `−camYaw` to a shared
`static float NorthScreenBearing()`. **Grounding shows that lift is unnecessary for
the chosen geometry (§3.2 Option A):** when the north glyph rides the rotating
`_mapContainer` at container-local "up," it tracks world-north with **zero yaw
math** — exactly as the terrain and pins already do. The scalar lift is only needed
for the *inferior* Option B (place on the fixed frame, set `localRotation = −camYaw`
each frame — the literal needle analog). We pick Option A, so **no compass-side
code is lifted at all**; the compass feature only needs to (a) expose `IsWearingCompass`
(already public-adjacent at `:329`) and (b) register a cardinal-marker provider
(§3). This is strictly *less* coupling than the ticket anticipated.

> 🔴 **Honesty note — one correction to the ticket's seam framing.** The ticket and
> the merged Sunstone doc both describe `WorldPins.CollectInDiscPins` as "the
> registered-provider pattern to mirror." Grounding (`MapSurface.cs:606` 🔵) shows
> it is actually a **hard outbound call**: `MapSurface.RebuildOverlay` directly
> calls `WorldPins.CollectInDiscPins(origin, radius)` — a compile-time edge
> Cartography→MarkerSigns, NOT a registration. Mirroring it *literally* would make
> Cartography accrue a hard `using SBPR…Exploration` + `using SBPR…Sunstone` edge
> for every disc feature — the exact coupling the "lowest-coupling direction" prose
> says to avoid. **§3 designs the genuine inversion the prose intends** (a
> registration seam), not the literal call-out the existing WorldPins code is.

---

## 3. The SHARED disc-overlay seam — designed ONCE for both twins 🟢 DECIDED (architecture)

This is the joint deliverable the two cards share. Both Sunstone and Compass need
to put transient, per-rebuild visual elements onto the disc that the `SurveyPin`
model can't carry. Rather than two divergent hacks, **one generic disc-overlay
registration seam** on Cartography serves both.

### 3.1 Why a new seam at all 🔵

- The disc's pin path resolves a sprite by `pin.Type` from `MarkerTypes`
  (`SpawnPinMarker`/`ResolvePinSprite`, `MapSurface.cs:738-748` 🔵); `SurveyPin`
  carries only `{Name, Type:int, Pos, Checked, OwnerId}` 🔵 — **no slot for a
  cardinal marker, trophy art, aggro tint, or pips.** Neither twin fits the pin
  model.
- `MapSurface.cs:774` 🔵 even hard-asserts the player marker is "**NOT** a north
  indicator." There is no cardinal seam anywhere in the public surface
  (`:188-211`). One must be added.
- **Zero `Exploration→Cartography` and zero `Sunstone→Cartography` coupling exists
  today** (grep returns only prose comments — `SBPR_CompassHud.cs:48,123,364` 🔵).
  Each twin adds the *first* such dependency. **There is no zero-API option** for
  either; the only architectural choice is *which direction the arrow points.*

### 3.2 The geometry — rotate-with-heading is LOCKED, not a knob 🔵

The disc interior rotates every frame by camera yaw — `rotZ = MapRotationSign *
cam.transform.eulerAngles.y` (`MapSurface.cs:963-965` 🔵, `MapRotationSign = 1f`
`:119`). World-north maps to container-local **+y (up)** (`WorldToSurfacePx`
comment `:479`: "north=+Z = +y (up)" 🔵). The player marker and pins already ride
this rotating `_mapContainer` and counter-rotate to stay upright (`:966`, `:973-974` 🔵).

🟢 **The N marker is the identical pattern, pinned to north instead of the
player** — two impl paths, one locked outcome:

- **Option A (chosen): ride the rotating container.** Place the N glyph on
  `_mapContainer` at a fixed container-local point `(0, +r)` (local-up). The
  container's per-frame `localRotation = rotZ` carries it to world-north's screen
  position automatically — **zero yaw math, no `NorthScreenBearing()` lift** (§2).
  Counter-rotate the glyph itself by `−rotZ` for legibility (the exact player-marker
  idiom, `:974`). Face north → N at 12 o'clock; face east → N orbits to 9 o'clock.
- **Option B (rejected): ride the fixed frame.** Place the N on the non-rotating
  `_frame`, set `localRotation = MapRotationSign * −camYaw` each frame (the literal
  needle analog). Works, but re-derives the yaw the container already applies, and
  needs the scalar lift. Option A is strictly less code and cannot desync from the
  terrain frame.

🟢 **Rotate-with-heading itself is LOCKED by the forward-up disc** — only the impl
path (A vs B) was ever open, and A wins on grounding. A north marker that did NOT
rotate with heading would be a north-*up* lock, which §4/§5 and cartography
AT-LMAP-TC-5 forbid.

### 3.3 Radius — on/near the bronze bezel, clear of the threat zone 🔵

The bezel is a bronze band drawn between `holeR` and `ringOuterR`
(`EnsureBezelTexture`, `MapSurface.cs:1210-1211` 🔵), where `holeR = discEdge −
TargetPx*6/900` and `ringOuterR = holeR + max(TargetPx*10/900, 4.5px)` — i.e. a
thin band just inside the disc edge (`discEdge = TargetPx*0.5 = 100px` at the
200px disc `:1207`, `MapViewer.cs:36` 🔵). The N glyph rides at radius `r ≈ holeR`
(on/just inside the bezel). 🔵 **This is clear of the Sunstone threat zone**: §3.3
of the twin doc quantified threats landing within the inner ~48% (~48px) of the
disc; the compass N at ~94px radius never collides with a Sunstone blip. The two
overlays are spatially disjoint by construction — a clean co-existence.

> 🔴 **Bezel is a child of the NON-rotating `frame`** (`MapSurface.cs:1318-1329` 🔵),
> so the bezel itself never spins. The N marker must NOT parent to the bezel — it
> parents to the rotating `_mapContainer` (Option A) so it orbits. It merely shares
> the bezel's *radius*, not its parent. Stated so the impl doesn't accidentally pin
> N to the fixed frame and get a dead, non-orbiting glyph.

### 3.4 The seam shape — a registration inversion carrying BOTH marker kinds 🟢

The lowest-coupling shape (the genuine version of what the WorldPins prose
*intends*, per §2's correction):

> **Cartography exposes a disc-overlay provider registry; each feature registers a
> provider. `MapSurface.RebuildOverlay` pulls from all registered providers each
> rebuild** — Cartography asking "any overlay markers?", not features reaching into
> `MapSurface`. Cartography depends on an **interface it owns**, never on Sunstone
> or Exploration types.

The provider yields a small tagged union of two marker kinds — enough for both
twins and nothing more:

```
// Owned by Cartography. Features depend on THIS; Cartography depends on NO feature.
interface IDiscOverlayProvider {
    void CollectDiscOverlay(Vector3 discOrigin, float radiusMeters, DiscOverlaySink sink);
}

// The sink carries exactly two marker kinds — the union of both twins' needs:
//  • WorldBlip   { Vector3 WorldPos; Color Tint; Sprite? Icon; int Pips }  — Sunstone
//  • CardinalMark{ float WorldBearingDeg /*=0 for north*/; Sprite Glyph; float RadiusFrac } — Compass
// CardinalMark ignores WorldPos: it rides container-local-up (§3.2 Option A), the
// degenerate "one marker at world-north" case of a world-positioned marker.
```

🟢 **DECIDED (architecture, not a Daniel knob):**
- **The seam is owned by Cartography** (the disc owner), consumed by Sunstone and
  Exploration. Dependency arrow: `Sunstone → Cartography.IDiscOverlayProvider ←
  Exploration`. Cartography stays unaware of *what* registered (it sees providers,
  not a Lens or a Compass).
- **`WorldPins.CollectInDiscPins` is migrated onto this seam too** (it becomes the
  third provider), retiring the existing hard `MapSurface→WorldPins` call-out
  (`:606`) — so the seam has three consumers from day one and the §2 coupling wart
  is paid down, not added to. *(This migration is optional-but-recommended; the
  impl-spec scopes it as a clearly-separable step so it can be deferred if it
  widens the PR too far.)*
- **The compass is a degenerate provider**: one `CardinalMark` at world-bearing 0
  (north), no world-position gather, no per-frame cost beyond a transform set. It
  is the *only* disc element permitted to encode north (§5).

This is the **single shared seam both cards asked for.** The Sunstone impl-spec
(`sunstone-minimap-handoff-impl-spec.md`, proposed) and this card's impl-spec both
build against it; whichever ships first lands the seam, the second consumes it.

---

## 4. 🔴 The central decision — superseding the "never a north arrow on any map" thesis

This is the load-bearing decision Daniel is locking. The Iron Compass impl-spec
currently forbids exactly this feature, in absolute language. The supersession is
**conscious and worded**, not a silent contradiction.

### 4.1 What the spec says today (verbatim, verified 🔵)

| Site | Current text |
|---|---|
| `iron-compass-impl-spec.md:21-24` (§0 thesis) | *"it grants it on a **separate HUD overlay**, never by adding a north arrow back onto the map. **Putting a north arrow on the local map would delete this item's entire reason to exist** and reverse a Daniel-locked difficulty choice."* |
| `:529-533` **AT-COMPASS-NOMAP-SAFE** | *"it adds **no** north indicator to any map — the Local Map's no-north disorientation (cartography §2H.1) is unchanged by this feature. The compass is the separate earned tool; the map stays north-blind."* |
| `:569-570` (decision log) | *"the compass **NEVER** adds a north arrow to any map; the withheld map-orientation stays withheld. This is **non-negotiable design thesis, not a knob.**"* |
| cartography `§2H.1 :725` + `AT-LMAP-TC-5 :2777-2778` | *"there is **no** north indicator … or any orienting aid anywhere on the held Local Map."* |

### 4.2 Why the original fear does NOT apply to a compass-gated ring 🟢

The original thesis rests on one fear, stated explicitly: *"a north arrow on the
map would delete the compass's reason to exist."* That fear is **correct for an
UNGATED north arrow** — if the disc showed north to everyone, the compass would be
pointless. But it is **false for a compass-gated ring**:

- The ring appears **only when the compass is worn** (§1 gate, §5 rule). A player
  without the compass sees a north-blind disc — the disorientation pillar is intact
  for them.
- The compass is **still the grantor.** It does not give north *to the map*; it
  grants north *through itself*, now on a second surface. The earned payoff is
  unchanged — you still must craft and wear the iron-tier trinket to get any
  cardinal reference at all.
- North is never a **property of the disc.** It is a property of the **compass**,
  drawn on the disc when both are present (§5). Remove the compass and the disc is
  north-blind again, instantly.

So the supersession does not reverse the difficulty choice — it **relocates the
compass's existing payoff onto a better surface** while preserving every gate that
made it earned. The thesis was over-broad: it banned "north on the map" when what
it meant to ban was "**ungated** north on the map."

### 4.3 The exact re-wording (carried by the post-gate impl-spec PR, NOT this PR) 🟢

| Site | Becomes |
|---|---|
| §0 thesis `:21-24` | *"it grants it on a separate HUD overlay **or, when the Iron Compass is worn and an SBPR minimap disc is bound, as a compass-gated north ring superimposed on that disc** — never by adding north to the map **ungated**. North remains an earned, compass-only payoff; a player without the compass sees a north-blind map on every surface."* |
| AT-COMPASS-NOMAP-SAFE `:529-533` | split: the NoMap-HUD-safe guarantee stays; add a sibling line — *"and it adds no **ungated** north indicator to any map. The compass-gated disc ring (iron-compass-minimap-ring.md) is the sanctioned exception: north appears on the disc **iff the compass is worn**, so the map stays north-blind for the compass-less player."* |
| decision-log `:569-570` | *"the compass NEVER adds **ungated** north to any map; the compass-gated disc ring is the **one** sanctioned exception (Daniel, 2026-06-20). The withheld orientation stays withheld for anyone not wearing the compass. The **gating** is non-negotiable; the surface (HUD vs disc ring) is the §6 knob."* |
| cartography `§2H.1` / `AT-LMAP-TC-5` | append: *"— except the compass-gated north ring, which renders on the disc only while the Iron Compass is equipped (iron-compass-minimap-ring.md §5). The disc itself remains north-blind; the ring is the compass's payoff drawn on the disc, not a property of the map."* |

🟡 **This re-wording is the gate.** Daniel ratifies the supersession (or rejects
it, leaving the compass HUD-only). Nothing is edited in the shipped specs until he
does — exactly the standalone-then-graduate path the Sunstone twin followed (§7).

---

## 5. The invariant interaction — one rule resolves both twins 🟢

The two cards carry **opposite** north-invariants:

- **Sunstone MUST NOT show north** — `AT-LENS-RING-CAMREL`
  (`sunstone-lens-trophy-ring.md:383-385` 🔵: "the ring is camera-relative, never
  north-up … grants no cardinal orientation").
- **Compass MUST show north** — the cardinal marker IS the payoff (§0, §4).

🟢 **Both are resolved by a single rule:**

> **The disc renders a north marker IFF the Iron Compass is equipped.**

- **No compass worn** → the disc stays north-blind (cartography §2H.1 default
  holds), and Sunstone blips remain pure camera-relative world positions. Neither
  feature shows north.
- **Compass worn** → the compass-gated `CardinalMark` provider (§3.4) emits one
  north glyph; it is the **only** disc element permitted to encode north. Sunstone
  blips are *still* not north — they remain world-positioned dots; north is a
  separate, compass-owned overlay element drawn alongside them.

The features never conflict because **north is never a property of the disc** — it
is a property of the **compass**, drawn on the disc only when worn. Sunstone-on-disc
and Compass-on-disc can both be active simultaneously (a worn compass + a worn lens
+ a bound disc): the lens contributes world-blips in the inner ~48%, the compass
contributes one N glyph at the bezel radius (§3.3), spatially disjoint, neither
contaminating the other's invariant.

🔴 **New acceptance test — AT-DISC-NORTH-GATED** (the rule's guard): with a bound
disc, wearing **only the Sunstone Lens** (no compass) shows threat blips and **no**
north marker; donning the compass makes the N ring appear; doffing it removes the N
ring while the blips persist. The disc is north-blind exactly when the compass is
off.

---

## 6. The knobs that are genuinely Daniel's (live Config where reversible)

1. 🔑 🟡 **Replace vs both — the load-bearer (asked in the ticket thread).** When
   the disc ring is up, does the **HUD needle hide** or do **both** render?
   Resolved structurally as a live Config enum, mirroring the Sunstone twin's
   `MinimapHandoffMode` (same banner-windsock pattern):

   ```
   IronCompass.CompassDiscMode  (Config enum, "IronCompass" section, live-tunable)
     • HudOnly       — ignore the disc; HUD needle always (today's behaviour; escape hatch)
     • DiscWhenBound — when worn AND IsMinimapBound: HUD needle hides, disc ring shows; else HUD  ← proposed DEFAULT
     • Both          — HUD needle AND disc ring both render whenever the disc is bound
   ```

   > 🟢 **Proposed default: `DiscWhenBound`** (architect's reversible lean — NOT
   > Daniel's locked call). Daniel's phrasing — *"superimposed … otherwise fall
   > back to the current behavior"* — reads as one compass at a time on whichever
   > surface exists: disc ring when bound, HUD needle when not. But `Both` is one
   > flip away if he wants the needle's redundancy, and `HudOnly` disables the whole
   > feature without a rebuild. Defaulting the enum is reversible; hard-coding the
   > fork is not.

   🔴 **The pitfall this enum must NOT step on (#208/#209 — load-bearing).** "HUD
   needle hides" must mean **toggle the overlay's `_content` child, NEVER deactivate
   the host or stop `Update()`.** The compass HUD already learned this exact lesson:
   `SetVisible` toggles `_content`, never `_root`, *"because the host carries this
   MonoBehaviour's Update pump, so deactivating it would freeze the overlay
   un-recoverably (t_61aff612)"* (`SBPR_CompassHud.cs:356-362` 🔵). The needle's yaw
   read at `:293→:301` must keep pumping even while the HUD needle is hidden — the
   **disc ring provider depends on that live signal** (or computes its own from
   `Utils.GetMainCamera`, but the cleanest impl single-sources the camera yaw). Locked
   as **AT-COMPASS-DISC-PUMP**: under `DiscWhenBound`, hiding the HUD needle keeps the
   `Update` pump alive; unbinding the disc restores the needle with no dead frame.

2. 🟡 **N-only vs N/E/S/W vs ticks.** The HUD dial carries full N/E/S/W labels today
   (`SBPR_CompassHud.cs:174-180` 🔵). On the disc the options are: a single **N
   glyph** (cleanest, the "indicating north" Daniel literally asked for); **N/E/S/W**
   at 90° spacing on the bezel radius (richer, but four glyphs orbiting may clutter
   the 200px disc); or **cardinal ticks** + a single N. *Architect lean: a single N
   glyph at the bezel radius (§3.3) — it is exactly "a compass ring indicating
   north," and the disc's small scale argues against four orbiting labels. Make it a
   live enum if Daniel wants to compare.* The `CardinalMark` model (§3.4) already
   carries `WorldBearingDeg`, so N/E/S/W is just four marks at 0/90/180/270 — cheap
   to offer later.

3. 🟡 **Ring radius vs the bronze bezel** — on the bezel (`r ≈ holeR`), just inside,
   or just outside. *Architect lean: on/just-inside the bezel (§3.3), clear of the
   Sunstone threat zone and reading as a rim element, not a map-content element.*
   Cheap live float if Daniel wants to nudge it.

4. 🟡 **nomap-OFF behaviour** (same knob as the Sunstone twin's §5-knob-3). With the
   SBPR disc standing down (§1), the options: (a) **HUD needle stays** (clean —
   matches where the disc exists; the disc handoff is a nomap-ON feature); (b) draw
   the ring on the *vanilla* minimap (re-opens the north-up-vanilla-surface question
   — the vanilla minimap is north-up, which would hand cardinal orientation in a way
   §4/§5 only sanctioned for the *SBPR* disc; and vanilla pins resolve sprite by type,
   same limitation as §3.1); (c) out of scope. *Architect lean: (a) — HUD needle
   stays in nomap-OFF. (b) fights the thesis on a north-up surface and the marker
   model. Daniel confirms.*

🟢 **Geometry (rotate-with-heading, Option A) is CONFIRMED, not a knob** (§3.2) —
only the four items above are open.

---

## 7. Clean/dirty routing + spec impact

**Clean/dirty: CLEAN-SIDE.** 🟢 All SBPR-authored (`Features/Exploration/` +
`Features/Cartography/`). Reads vanilla `Hud`/`GameCamera`/`Utils`/`Player`/
`Inventory` only — base-game, fair to read+adapt per ADR-0001. No third-party mod
code. Route to **`architect`** (this doc, jointly with `t_3129842a`) →
**engineer-ui** (impl).

**SpecCheck/manifest impact: NONE.** 🔵 Render-only; no recipe/piece/station/item
change. The Iron Compass recipe row in `SpecCheck.cs` is untouched (the card
verified `:178-193`). No new SpecCheck row.

**ADR-0006 (additive):** the disc N glyph is built additively — `new GameObject()`
+ `AddComponent<Image>()`/`RectTransform` parented under `_mapContainer`, reading no
vanilla prefab as a mutable base. The glyph sprite reuses the procedural-sprite idiom
the compass HUD already ships (`DiscSprite`, `:373` 🔵) — zero asset-bundle
dependency.

**Docs that move (deferred to the post-gate impl-spec PR — NOT this PR).** This PR
adds only this standalone design doc + its manifest rows. The dependent surgery
below depends on which `CompassDiscMode` Daniel ratifies (`HudOnly` changes the
specs not at all) and on the §4 supersession being gated, so it lands with the
impl-spec — the same standalone-then-graduate path `sunstone-lens-minimap-handoff.md`
(the twin) and `travellers-cache.md` follow:

| Doc | Change |
|---|---|
| `docs/v3/planning/iron-compass-impl-spec.md` | **The heavy one** — the §4.3 re-wording of §0 `:21-24`, AT-COMPASS-NOMAP-SAFE `:529-533`, decision-log `:569-570`; add the `CompassDiscMode` enum + AT-COMPASS-DISC-* tests. |
| `docs/v2/planning/cartography-impl-spec.md` | §2H.1 `:725` + AT-LMAP-TC-5 `:2777-2778` gain the compass-gated carve-out (§4.3). |
| `docs/design/nomap.md` §8 | one-line pointer to the disc-ring branch of the compass. |
| `docs/design/map-provider-model.md` | the disc gains its **second** non-cartography consumer (Sunstone is the first) + the shared `IDiscOverlayProvider` seam (§3.4). |
| `docs/design/sunstone-lens-minimap-handoff.md` | cross-ref: its proposed Cartography threat-marker seam is **generalized** into the shared `IDiscOverlayProvider` (§3.4) carrying both marker kinds; its `WorldBlip` is one of the two. |
| **NEW** `docs/v3/planning/iron-compass-minimap-ring-impl-spec.md` | The buildable spec: the shared `IDiscOverlayProvider`/`DiscOverlaySink` seam, the compass `CardinalMark` provider, the `CompassDiscMode` enum, the disc N-glyph renderer (Option A), and the AT-COMPASS-DISC-* + AT-DISC-NORTH-GATED tests. **Co-authored with the Sunstone impl-spec** so the one seam serves both. |

🔴 **Sequencing with the twin.** The shared seam (§3.4) should land **once**. Two
clean paths: (a) one combined `disc-overlay-seam-impl-spec.md` that both features'
impl cards build on; or (b) whichever twin Daniel gates first lands the seam, the
second consumes it. *Architect recommendation: (a) — a single seam impl-spec + impl
card for `engineer-ui`, then thin per-feature provider cards — so the seam has one
owner and one review, not two half-seams that must agree.* Daniel picks at gate.

---

## 8. Proposed acceptance tests (impl-spec will formalize)

- **AT-COMPASS-DISC-RING** — with nomap-ON + a local map bound (disc showing) + the
  Iron Compass worn, a north indicator renders **on the disc** at the bezel radius;
  per `CompassDiscMode`, the HUD needle hides (`DiscWhenBound`), both show (`Both`),
  or only the HUD needle shows (`HudOnly`).
- **AT-COMPASS-DISC-ROTATE** (🔴 the geometry) — standing still and rotating the
  camera sweeps the N marker around the disc rim: face north → N at 12 o'clock; face
  east → N at 9 o'clock; a full 360° camera turn sweeps N a full 360° around the
  bezel. The N glyph itself stays upright (counter-rotated, §3.2 Option A).
- **AT-COMPASS-DISC-PUMP** (🔴 #208/#209 guard) — under `DiscWhenBound`, hiding the
  HUD needle keeps the overlay's `Update` pump alive (the yaw read never freezes);
  unbinding the disc (unequip the local map) restores the HUD needle with **no dead
  frame**. The pump is never gated on which surface draws.
- **AT-DISC-NORTH-GATED** (🔴 the §5 rule — shared with the Sunstone twin) — with a
  bound disc, wearing only the Sunstone Lens (no compass) shows threat blips and
  **no** north marker; donning the compass makes the N appear; doffing it removes the
  N while blips persist. The disc is north-blind exactly when the compass is off.
- **AT-COMPASS-DISC-NOMAP-OFF** — in nomap-OFF (no SBPR disc), the compass behaves
  per Daniel's §6-knob-4 decision (architect-proposed default: HUD needle stays).
- **AT-COMPASS-DISC-CLEAN** (clean-room) — no third-party mod code read or copied;
  all hooks are base-game primitives (`Hud`, `GameCamera`, `Utils`, `Inventory`) +
  the SBPR-owned `IDiscOverlayProvider` seam.

---

## 9. Open questions for Daniel (the gate)

1. 🔴 **Ratify the §4 thesis supersession?** The Iron Compass impl-spec currently
   says, in non-negotiable language, "the compass NEVER adds a north arrow to any
   map." This card re-words that to "never **ungated**; the compass-gated disc ring
   is the sanctioned exception." **This is the central decision** — everything else
   is downstream of it. (Reject → the compass stays HUD-only and this card closes.)
2. **`CompassDiscMode` default** — `DiscWhenBound` (proposed), `Both`, or `HudOnly`?
   (Reversible; picks the default for the live enum.)
3. **North representation on the disc** — a single **N glyph** (proposed), full
   **N/E/S/W**, or a live enum to compare in-game?
4. **Ring radius** — on the bezel (proposed), just inside, or just outside?
5. **nomap-OFF behaviour** — HUD needle stays (proposed), draw on the vanilla
   minimap, or out of scope?
6. **Seam sequencing** — one combined disc-overlay-seam impl-spec co-owned with the
   Sunstone twin (architect recommendation), or land the seam under whichever twin
   gates first?

Nothing here is an impl card yet. On Daniel's answers — and especially Q1 — this
graduates to `iron-compass-minimap-ring-impl-spec.md` (co-authored with the Sunstone
impl-spec for the shared seam) and the impl card is cut for **engineer-ui**.

---

## Links

- **Twin — design jointly:** [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)
  (card `t_3129842a`, merged PR #214); the shared `IDiscOverlayProvider` seam (§3.4)
  generalizes its proposed threat-marker provider.
- Compass design: [`nomap.md` §8](nomap.md); impl-spec
  [`../v3/planning/iron-compass-impl-spec.md`](../v3/planning/iron-compass-impl-spec.md)
  (the §4 supersession target); code `Features/Exploration/IronCompass.cs`,
  `SBPR_CompassHud.cs`.
- Minimap disc + provider binding: [`map-provider-model.md`](map-provider-model.md),
  [`cartography-v2.md`](cartography-v2.md); spec
  [`../v2/planning/cartography-impl-spec.md`](../v2/planning/cartography-impl-spec.md)
  (§2H.1 / AT-LMAP-TC-5 carve-out target); code `MapViewer.cs`, `MapSurface.cs`,
  `LocalMapController.cs`, `CartographyViewer.cs`.
- Seam precedent (the call-out to invert into a registration): `WorldPins.CollectInDiscPins`
  (`Features/MarkerSigns/WorldPins.cs:302`, called from `MapSurface.cs:606`).
- Sibling cartography work shipped: `t_642687dd` (disc margin, PR #213),
  `t_423f5bd7` (modal chevron, PR #212).
- Reported via /bug thread `ticket-compass-minimap-ring`. Card `t_85a46f42`.
