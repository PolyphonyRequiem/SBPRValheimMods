---
title: "Iron Compass → minimap + full-map north-ring — impl-spec (buildable; engineer-ui)"
status: proposed
purpose: "Buildable spec graduating the GATED design doc docs/design/iron-compass-minimap-ring.md (card t_85a46f42, PR #226 @ main cdd5cc2). Daniel locked all 4 answers (HUD needle hides / N+ticks on a compass-gated IRON bezel that reverts to bronze / opt-in auto-north default OFF / disc AND full-map modal) and RATIFIED the Q1 thesis supersession ('never UNGATED north on the map; the compass-gated ring is the sanctioned exception'). This doc is the buildable HOW in two milestones: M1 = the compass-gated iron-bezel recolor + the N-glyph chevron-sibling renderer (high value, low risk); M2 = the opt-in north-up lock (§6, bigger lift). The N marker is a PLAYER-CHEVRON SIBLING on the rotating _mapContainer — NO new pull-seam — because the shipped IThreatMarkerProvider (t_91e86951) is the WRONG shape (world-positioned / per-rebuild / disc-only). Every code line re-grounded against main @ cdd5cc2. Render-only: SpecCheck +0, no new prefab (ADR-0006 N/A), clean-side (ADR-0001). Daniel gates the impl-spec at doc review AND the in-game ATs. This PR ALSO carries the §4.3 supersession re-wording of the shipped specs (spec+doc move together — AGENTS.md)."
owner: Daniel (design + merge authority); Starbright (architect — spec); engineer-ui (impl)
design_source: "docs/design/iron-compass-minimap-ring.md (GATED, PR #226 @ main cdd5cc2)"
supersedes_partial:
  - "docs/v3/planning/iron-compass-impl-spec.md §0 thesis (:21-22), AT-COMPASS-NOMAP-SAFE (:529-533), decision-log (:569-570) — the §4.3 re-wording (this PR)"
  - "docs/v2/planning/cartography-impl-spec.md §2H.1 (:725), AT-LMAP-TC-5 (:2777-2778), AT-TABLEVIEW-ROT-1 (:2784-2786), mechanic-5 lock (:2728-2731) — the §4.3 carve-out (this PR)"
---

# Iron Compass → minimap + full-map north-ring — impl-spec (buildable)

The design note ([`../../design/iron-compass-minimap-ring.md`](../../design/iron-compass-minimap-ring.md),
**GATED**, PR #226) is the locked *what*: when the Iron Compass is worn AND an SBPR
map surface (carry-disc OR full-map modal) is showing, draw a **compass-gated north
ring** on that surface as an **iron bezel + N + ticks**; else fall back to the
unchanged TopCenter HUD needle. This doc is the buildable *how*. It is the structural
**twin** of the Sunstone Lens handoff ([`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md),
card `t_91e86951`) — both render a HUD feature onto a map surface, resolved by **one**
invariant, *north is compass-gated* (§5). The twin MUST NOT show north; this one MUST.

> **Clean-side (ADR-0001):** every vanilla fact cited is base-game (`Hud`,
> `GameCamera`, `Utils`, `Player`, `Inventory`) — fair to read and adapt. No
> third-party mod code is read or copied. SBPR lines are `main` @ `cdd5cc2` (every
> cited line re-grounded against current main, NOT the design doc's stale `b618aa8`).
>
> **No ADR-0006 concern:** this card adds **no item prefab** — it is render-only over
> the existing Iron Compass item and the existing cartography surfaces. The N glyph +
> ticks are built additively (`new GameObject()` + `Image`/`RectTransform` under
> `_mapContainer`), reading no vanilla prefab as a mutable base.
>
> **SpecCheck/manifest impact: NONE.** Render-only; no recipe/piece/station/item
> change. The Iron Compass recipe row in `SpecCheck.cs` is untouched.

## 0. The gated decisions (build constraints)

| Knob | 🟢 Locked value (Daniel) | Build consequence |
|---|---|---|
| ① HUD needle vs surface ring | **HUD needle HIDES** when the surface ring is up (*"it goes away"*) | `CompassDiscMode.DiscWhenBound` (default) hides the HUD `_content` when a surface is fed; the host stays alive (§6.3 / AT-COMPASS-DISC-PUMP). |
| ② North representation | **N + ticks** on a compass-gated **IRON** bezel that reverts to **bronze** when unworn | Per-frame `_bezel.color` tint off `IsWearingCompass`; the N + ticks toggle on the same gate. No persistence flag. |
| ③ Auto-north-orient | **Add it, default OFF** (opt-in north-up lock) | `CompassAutoNorthUp` Config bool, default `false`. **M2** — the surface locks north-up only when worn AND the bool is set. |
| ④ Scope | **disc AND full-map modal** (not disc-only) | One renderer + one gate on `MapSurface`; both surfaces tick the same `TickRotation` path (§3.2). |
| Q1 thesis | **RATIFIED** — supersede "never a north arrow on any map" → "never **UNGATED**; the compass-gated ring is the sanctioned exception" | The §4.3 re-wording ships in THIS PR (§7 below). |

**Architect-proposed defaults (reversible config — Daniel may adjust; NOT his locks):**

| Knob | Architect default | Where |
|---|---|---|
| Q3 — N-ring while north-up-locked | **hide the orbiting N** (the static N is noise when the whole map is north-up; the iron bezel already signals the compass is active) | M2, §6 |
| Q3 — enum vs bool | **independent `CompassAutoNorthUp` bool** composing with `CompassDiscMode` (two orthogonal axes beat a 4-way enum) | §4 config |
| Q3 — scope of north-up | **both surfaces** (same `MapRotationSign` path drives both) | M2, §6 |
| Q4 — nomap-OFF / vanilla minimap | **HUD needle stays** (north is free on the north-up vanilla minimap → a ring there is redundant, and the iron-bezel reskin is an SBPR-surface element that can't apply) | §1, Knob 6 |
| Q5 — milestone split | **M1 (iron bezel + N-ring) before M2 (opt-in north-up lock)** | §3 vs §6 |

## 1. File map — what this card builds

```
src/SBPR.Trailborne/Features/Exploration/
  SBPR_CompassHud.cs    ← EDIT (M1): promote IsWearingCompass(Player) :329 from `private static`
                              to `internal static` (the only compass-side change — the gate read).
                              The Update pump (:290-301 yaw read) and SetVisible(:356-361 _content
                              toggle) are UNCHANGED; ① "HUD needle hides" is a Config-mode branch
                              on the EXISTING _content toggle, never the host (§6.3).
  IronCompass.cs        ← EDIT (M1): bind CompassDiscMode + (M2) CompassAutoNorthUp Config entries
                              in the "IronCompass" section (mirrors the Sunstone live-enum pattern,
                              Plugin.cs:127-128). Expose CompassDiscMode/CompassAutoNorthUp statics.

src/SBPR.Trailborne/Features/Cartography/
  CompassNorthGate.cs   ← NEW (M1): engine-free (UnityEngine-free) policy — the CompassDiscMode
                              truth table {HudOnly, DiscWhenBound, Both} → (ShowSurfaceRing,
                              HideHudNeedle). Link-compiled into the test project → CI-gated,
                              exactly like DiscRingGeometry / LensHandoffDecision.
  MapSurface.cs         ← EDIT (M1): (a) per-frame iron/bronze bezel tint off the gate (extends
                              the EnsureBuilt _bezel.color = white :1450 hook); (b) the N-glyph +
                              ticks chevron-sibling under _mapContainer (mirrors _playerMarker
                              :149 / :1098), toggled + oriented in TickRotation:1102 →
                              ApplyFieldOrientation:1077. (M2) the CompassAutoNorthUp rotZ=0 branch
                              at :1085-1086 + chevron-spins inverse.
  MapViewer.cs          ← EDIT (M1): the one-bool Cartography-owned push setter
                              (SetCompassNorth(bool)) the compass feature calls each frame; MapViewer
                              forwards to both _disc and _modal (the Update :117-128 tick already
                              touches both). Cartography never references SBPR_CompassHud/IronCompass.

src/SBPR.Trailborne/
  Plugin.cs             ← EDIT (M1): register the IronCompass Config binds (if IronCompass doesn't
                              self-bind) — mirror the Sunstone enum binds.

tests/
  CompassNorthGateTests.cs      ← NEW (M1): the full CompassDiscMode truth table (AT-COMPASS-GATE).
  SBPR.Trailborne.Tests.csproj  ← EDIT: link-compile CompassNorthGate.cs.
```

No `SpecCheck.cs` change (render-only; §8). **Build target: 0 warnings / 0 errors**
(`<TreatWarningsAsErrors>` ON). The new CI unit is the `CompassNorthGate` truth table;
everything geometric/visual is an in-game AT (logs-green ≠ playable).

## 2. The trigger + the signal — re-grounded against main @ cdd5cc2

Two reads, both already public on `main`, ANDed together. No new state on either
feature — this card adds only a new *consumer*:

```
(a) a surface is showing:  CartographyViewer.IsMinimapBound   (CartographyViewer.cs:257, static)
                             → MapViewer.IsMinimapBound        (MapViewer.cs:113: _disc != null && _disc.IsActive)
                             → MapSurface.IsActive              (MapSurface.cs:198: _root != null && _root.activeSelf)
(b) the compass is worn:   SBPR_CompassHud.IsWearingCompass   (SBPR_CompassHud.cs:329, Trinket-slot + name match)
```

🔵 **Both surfaces, one class.** The carry-disc and the full-map (M) modal are the
SAME `MapSurface` class (`MapSurface.cs:1370-1372` 🔵 *"SHARED by the modal + the
disc — the only differences are config-driven: TargetPx (900 vs ~200) …"*),
config-differentiated only by `TargetPx` (`:45` = 900 modal / ~200 disc),
sortingOrder, anchor, backdrop/prompts. Both run the same `TickRotation()` →
`ApplyFieldOrientation()` rotation path (§3.2). One renderer + one gate covers both
surfaces; the modal is not a separate implementation (Daniel ④).

🔴 **The disc binds only in nomap-ON** (`LocalMapController.cs:147-150` 🔵:
`shouldBindDisc = _provider != null && Game.m_noMap && LocalMap.IsImprinted(_provider!)
&& LocalMap.ReadSurvey(_provider!) != null`). The full-map modal opens in both nomap
modes. So the gate resolves per surface (design §1 table):

| Runtime | Surface showing | Compass worn? | Compass north shows as… |
|---|---|---|---|
| nomap-ON, no map open | none | yes | **HUD needle** (today — unchanged) |
| nomap-ON, carry-disc bound | disc | yes | **iron bezel + N + ticks on the disc**; HUD needle hidden (① / `DiscWhenBound`) |
| nomap-ON/OFF, full-map (M) open | modal | yes | **iron bezel + N + ticks on the modal** (④) |
| any | disc/modal | **no** | nothing (north-blind surface — correct) |
| **nomap-OFF**, no SBPR surface, vanilla minimap in corner | vanilla minimap | yes | **HUD needle stays** (Knob 6 default — north is free on the north-up vanilla minimap) |

**The compass signal feeds the ring for free.** The compass's north bearing is one
render-agnostic scalar computed every frame in the HUD overlay's `Update()`:

```csharp
Vector3 euler = cam.transform.eulerAngles;   // .y = yaw (heading)     SBPR_CompassHud.cs:293
float targetZ = -euler.y;                     // needle rotates by −yaw  SBPR_CompassHud.cs:301
```

🔵 This is the **exact** camera yaw the map surface already reads for its own rotation
(`ApplyFieldOrientation`, `MapSurface.cs:1084`: `float camYaw = cam.transform.eulerAngles.y`).
Both consume `GameCamera.instance` yaw, so a glyph placed in surface-space inherits
the surface's rotation frame **for free** (§3.2). 🟢 **No lifted `NorthScreenBearing()`
scalar is needed** — a glyph riding the rotating `_mapContainer` at container-local
"up" tracks world-north with **zero yaw math** (the existing player-marker idiom,
`:1085-1098`). The compass feature only needs to expose `IsWearingCompass` (promote
`private static` → `internal static`, trivial) and push one bool to the surface.

---

## 3. M1 — the iron bezel recolor + the N-glyph chevron-sibling

> **M1 = the high-value, low-risk milestone.** It adds an overlay child + a per-frame
> color write. It does **not** touch the rotation math (that is M2, §6). Ship M1 first.

### 3.1 Why NOT the shipped `IThreatMarkerProvider` seam 🔵

`Features/Cartography/IThreatMarkerProvider.cs` (merged via `t_91e86951`) is a registry
the disc pulls each rebuild. Its marker struct (`DiscThreatMarker { Vector3 WorldPos;
Color Tint; Sprite? Icon; int Stars; }`) is the **opposite of the compass N marker on
all three axes**:

| Axis | Shipped `DiscThreatMarker` (Sunstone) | Compass N marker |
|---|---|---|
| Position | **world** (`WorldPos`, projected via `WorldToSurfacePx`) | **screen-bearing** — no world point; rides container-local-up |
| Cadence | **per-rebuild pull** (~0.25 s, inside `RebuildOverlay`, `MapSurface.cs:681`) | **per-frame** — must orbit smoothly as the camera turns (`TickRotation`, not rebuild) |
| Surfaces | **disc only** (`MapSurface.cs:675-681` 🔵 *"Disc-only because the modal is a nav surface, not a threat"*) | **disc AND modal** (Daniel ④) |

🟢 **Forcing the N marker through `IThreatMarkerProvider` would refactor merged,
shipped Sunstone code for negative benefit.** We do NOT. The seams stay separate
because the marker *kinds* are genuinely different. **No new pull-seam is added.**

### 3.2 The N marker is a PLAYER-CHEVRON SIBLING on `_mapContainer` 🟢

The cleanest fit is the surface's existing persistent overlay idiom — the **player
marker**: built once, parented under the rotating `_mapContainer` (`:139`), and
counter-rotated each frame to stay upright. The N glyph is the identical idiom,
**pinned to north instead of to the player**:

```csharp
// player marker, the template — ApplyFieldOrientation, MapSurface.cs:1084-1098 🔵
float camYaw = cam.transform.eulerAngles.y;                                   // :1084
float rotZ = MapRotationSign * camYaw;                                        // :1085
_mapContainer.localRotation = Quaternion.Euler(0f, 0f, rotZ);                 // :1086  world spins under you
_playerMarker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -rotZ);  // :1098  glyph stays upright
```

🟢 **The N glyph:** a persistent child of `_mapContainer` at a **fixed container-local
point** `(0, +r)` (local-up, `r ≈ DiscRingGeometry.HoleRadius(_cfg.TargetPx)` §3.3).
The container's per-frame `rotZ` carries it to world-north's screen position
automatically — **zero yaw math**. Counter-rotate the glyph itself by `−rotZ` for
legibility (the `:1098` idiom). Face north → N at 12 o'clock; face east → N orbits to
9 o'clock. The tick marks ride the same `_mapContainer` parent at 90°/180°/270°
offsets (or one static ticked sprite as a single child). Built additively
(`new GameObject()` + `Image`, ADR-0006); toggled by the compass gate — no pull, no
per-rebuild gather, no registry.

🟢 **The one-bool Cartography push.** The surface needs only "compass worn? show the N
layer : hide it." Lowest-coupling shape: a Cartography-owned setter
`MapViewer.SetCompassNorth(bool)` the compass feature calls each frame; `MapViewer`
forwards the flag into both `_disc` and `_modal` (its `Update()` `:117-128` already
ticks both). The dependency arrow stays `Exploration → Cartography` (Cartography never
references `SBPR_CompassHud` or `IronCompass`). The N-glyph build + orient + toggle
live in `MapSurface` (driven from `TickRotation()` `:1102` → `ApplyFieldOrientation()`
`:1077`, the same per-frame path the player marker rides).

> 🔴 **Parent split — the load-bearing impl subtlety.** The **bezel is a child of the
> NON-rotating `_frame`** (`MapSurface.cs:1446`), so it never spins — correct for a
> recolor (an iron *ring* looks identical at every rotation). The **N glyph must
> parent to the rotating `_mapContainer`** (`:139`) so it orbits. They share the bezel
> *radius*, not the bezel *parent*. Pin N to the fixed `_frame` by mistake and you get
> a dead, non-orbiting glyph. The iron skin and the N marker ride the same *gate*,
> different *parents*.

### 3.3 Radius — the bezel itself becomes the ring (Daniel ②) 🔵

Daniel ② makes the bezel *be* the ring: *"Replace the bronze ring with an iron one."*
The bezel is drawn by `EnsureBezelTexture` (`MapSurface.cs:1315-1361` 🔵), a bronze
band between `holeR` and `ringOuterR` from the shared `DiscRingGeometry` helper:

```csharp
float holeR      = DiscRingGeometry.HoleRadius(_cfg.TargetPx);     // MapSurface.cs:1335
float ringOuterR = DiscRingGeometry.RingOuterRadius(_cfg.TargetPx); // MapSurface.cs:1336
var ringColor    = new Color(0.62f, 0.55f, 0.42f, 1f);             // MapSurface.cs:1339  (bronze)
```

🟢 **The N glyph rides at `r ≈ holeR` (on/just inside the iron band).** Bind it
**symbolically** to `DiscRingGeometry.HoleRadius(_cfg.TargetPx)` (helper at
`DiscRingGeometry.cs:107`), NEVER a hard-coded pixel radius — see §3.5.

🔴 **Sunstone threat zone now REACHES the compass N — margin closed at 70 m detect (t_4b9f8889, 2026-06-24).** The Sunstone
detection radius (70 m) now exceeds the ~62.5 m visible disc: hostiles in the 62.5–70 m
band rim-clamp to ~92 px (`ThreatRimInset 0.92`), and the compass N rides at ≈`holeR`
(~94 px). The earlier "~14 px clear, disjoint by construction" guarantee (true at 50 m,
~80 px) **no longer holds** — a max-range blip on the N bearing reaches the N glyph.
The two overlays remain distinct layers (no shared state, §5) but are **no longer
spatially disjoint**; flagged for Daniel's in-game eye (tunable via `ThreatRimInset` /
blip size / N-glyph priority — not a build blocker).

### 3.4 The iron recolor — gated, reverts to bronze (Daniel ②) 🟢🔵

🟢 Daniel ② locked the skin as **compass-gated, not permanent**: *"of course it goes
back to bronze when you take the compass off."* One condition (`IsWearingCompass`)
drives the whole compass-disc skin: worn → iron bezel + N + ticks; unworn → bronze
bezel, north-blind. No persistence, no "ever held a compass" flag — a per-frame render
branch off the existing equip-gate.

🔴 **Impl path — the bezel texture is CACHED** (`_bezelTex` early-returned at
`MapSurface.cs:1317`). The recolor must NOT mutate the bronze constant inside the
cached texture. **Build it as a per-frame tint on the RawImage `color`:**

```csharp
// EnsureBuilt sets _bezel.color = Color.white (MapSurface.cs:1450); the texture is a
// white-bronze band, so a per-frame color write recolors it with ZERO texture rebuild.
_bezel.color = compassWorn ? IronTint : Color.white;   // white = the texture's native bronze
```

🟢 **This is design Path (a)** — the cheapest path, one color write per frame (or only
on the gate edge). The bronze band texture already bakes `(0.62, 0.55, 0.42)`; tinting
by `Color.white` shows native bronze, tinting by `IronTint` shows iron.

🔴 **IronTint — ground the exact RGB at build.** Iron in Valheim reads as a cool
desaturated steel-grey. ~~Proposed starting value: `new Color(0.66f, 0.68f, 0.72f, 1f)`~~
**Retuned to `new Color(0.677f, 0.764f, 1.0f, 1f)` (card t_540ace8c).** The first
guess multiplied the warm bronze base to `(104,95,77)` = #685F4D — a muddy dark
brown-grey, which Daniel reported as "the wrong color." The retune lands a NEUTRAL
medium grey: `(0.677,0.764,1.0) × (0.62,0.55,0.42)` = `(107,107,107)` = **#6B6B6B**.
**Cap to know:** because the tint multiplies the baked bronze base, a *neutral* grey
is capped at RGB ≤ 107 on this tint-only path (the base's blue channel = 0.42 →
1.0×0.42×255 = 107). #6B6B6B is the brightest neutral the clean single-constant edit
reaches; a *lighter* neutral grey needs the baked base at `MapSurface.cs:1423` lifted
(SHARED with the unworn disc rim → recolors the off-compass disc too) or a separate
worn-state texture bake.
(a touch blue-cool, lighter than bronze so the ring reads "iron" not "dark bronze").
Because it multiplies the bronze-baked texture, the engineer-ui must **eyeball the
product in-game** against an actual iron item and tune (AT-COMPASS-BEZEL-GATED is
Daniel's visual gate). Do NOT ship the first guess unverified — logs-green ≠ playable.
The tint is the one genuinely visual constant in M1.

> The N glyph + ticks `Image` color: a high-contrast off-white
> (`new Color(0.92f, 0.93f, 0.96f, 1f)`) so it reads on the iron band; final value is
> the engineer-ui's call, gated by the same in-game AT.

### 3.5 🔴 Coordinate with `t_12e15162` / PR #213 (the `DiscRingGeometry` margin) — MERGED

🔵 **Grounding update (RULE 5):** PR #213 (spec `541ef7b`) + its impl PR #216 (fix
`8601647`) **already merged below the design doc's `b618aa8` baseline AND below
current main `cdd5cc2`.** So the margin work is DONE and the `DiscRingGeometry` helper
(`HoleRadius` / `RingOuterRadius` / `RectEdge`) is the SHIPPED single source of truth.

**The discipline that follows:** this card changes the bezel *color/tint* only —
orthogonal to #213's *radius/margin* but the **same code region** (`EnsureBezelTexture`
+ `_bezel`). Therefore:
- The iron tint is a `_bezel.color` write — it touches **no** radius arithmetic, so it
  cannot regress #213's margin.
- The N glyph's radius MUST read `DiscRingGeometry.HoleRadius(_cfg.TargetPx)` at
  render time (the post-#213 helper), NEVER a literal. If a future margin tune moves
  `HoleRadius`, the N glyph tracks it for free.
- 🔴 **Do not hard-code `r = 94f` (or any number).** The design doc's "≈94 px"
  figure is illustrative arithmetic for the 200 px disc only; the modal is 900 px.
  Bind to the helper so both surfaces and any future margin change stay correct.

---

## 4. Config — the live entries (`IronCompass` section)

The Sunstone twin shipped `LensMinimapHandoffMode` + `LensMinimapBlipStyle` as live
`Config.Bind` enums (`Plugin.cs:127-128` 🔵). The compass mirrors that pattern in the
`"IronCompass"` section:

```csharp
// CompassDiscMode — the §0-① surface-vs-HUD policy (M1)
CompassDiscMode = Config.Bind("IronCompass", "DiscMode",
    CompassDiscModeEnum.DiscWhenBound,                       // ← 🟢 DEFAULT (Daniel ①)
    "HudOnly: ignore the surface, HUD needle always (today's behaviour; escape hatch). " +
    "DiscWhenBound (default): worn AND a surface showing → HUD needle hides, surface ring shows; else HUD. " +
    "Both: HUD needle AND surface ring both render whenever a surface is showing.");

// CompassAutoNorthUp — the §6 opt-in north-up lock (M2)
CompassAutoNorthUp = Config.Bind("IronCompass", "AutoNorthUp",
    false,                                                    // ← 🟢 Daniel ③ (default OFF)
    "false (default): surface stays heading-up; the iron N-ring orbits the rim. " +
    "true: worn + surface showing → the surface locks north-up and the player chevron rotates (opt-in §6).");
```

The `CompassDiscModeEnum` lives in the engine-free `CompassNorthGate.cs` (so the
Config bind and the unit test share one definition — the `LensHandoffDecision`
precedent). `CompassAutoNorthUp` is a plain `bool` — an **independent** axis composing
with the mode enum (architect lean: two orthogonal config entries beat a 4-way enum
that conflates "HUD-vs-surface" with "heading-up-vs-north-up").

🟢 **Knob 6 (nomap-OFF / vanilla minimap): HUD needle stays** (architect default,
reversible). In nomap-OFF there is no SBPR surface; the vanilla corner minimap is
north-up, so north is already free there — a compass ring would be redundant
decoration, and the iron-bezel reskin is an SBPR-surface element that can't apply.
The surface ring is a no-map-world feature where orientation is actually withheld. No
new config entry; this is simply the absence of an SBPR surface to draw on.

## 5. The `CompassNorthGate` truth table + the shared invariant (§5 rule)

The load-bearing policy (does the surface ring show, does the HUD needle hide) is
extracted into `CompassNorthGate` — pure, `UnityEngine`-free, link-compiled into the
test project exactly like `DiscRingGeometry` / `LensHandoffDecision`. The compass
overlay reduces live world state to two booleans and this decides:

```csharp
public enum CompassDiscModeEnum { HudOnly, DiscWhenBound, Both }

public readonly struct CompassRenderPlan {
    public readonly bool ShowSurfaceRing;  // feed the iron bezel + N + ticks to the surface
    public readonly bool HideHudNeedle;    // hide the HUD overlay's _content (NEVER the host — §6.3)
}

// surfaceShowing = MapViewer.IsMinimapBound (disc) OR the modal IsActive; compassWorn = IsWearingCompass
public static CompassRenderPlan Resolve(bool surfaceShowing, bool compassWorn, CompassDiscModeEnum mode)
```

The truth table (asserted exhaustively in `CompassNorthGateTests.cs` — **AT-COMPASS-GATE**):

| `mode` | compassWorn | surfaceShowing | ShowSurfaceRing | HideHudNeedle |
|---|---|---|---|---|
| any | **false** | any | false | false (HUD needle is itself hidden by the compass's own equip-gate) |
| `HudOnly` | true | any | **false** | false (escape hatch — HUD needle always, ignore the surface) |
| `DiscWhenBound` (default) | true | false | false | false (no surface → HUD needle) |
| `DiscWhenBound` (default) | true | **true** | **true** | **true** (① the needle "goes away") |
| `Both` | true | false | false | false |
| `Both` | true | **true** | **true** | **false** (HUD needle AND surface ring both show) |

🔵 **Note `HideHudNeedle` only ever means "hide `_content`."** When `compassWorn` is
false the HUD needle is already hidden by the compass's *own* equip-gate
(`SetVisible(false)` at `SBPR_CompassHud.cs:250` when not wearing) — the gate's
`HideHudNeedle=false` there just means "don't *additionally* force-hide; the equip
state already governs it." The gate only *adds* a hide when a surface is consuming the
north payoff under `DiscWhenBound`.

**The shared invariant (one rule reconciles both twins):**

> **An SBPR map surface renders a north marker IFF the Iron Compass is equipped.**

- **No compass worn** → the surface stays north-blind (cartography §2H.1 default
  holds), bezel stays **bronze**, and Sunstone blips remain pure camera-relative world
  positions. Neither feature shows north.
- **Compass worn** → the compass gate flips the iron bezel + N + ticks on; it is the
  **only** surface element permitted to encode north. Sunstone blips are *still* not
  north — they remain world-positioned dots at a disjoint radius (§3.3); north is a
  separate, compass-owned overlay element drawn alongside them.

North is never a property of the *surface* — it is a property of the *compass*, drawn
on the surface only when worn. Sunstone-on-disc and Compass-on-disc can both be active
at once (worn compass + worn lens + bound disc) without contaminating each other's
invariant.

🔴 **AT-DISC-NORTH-GATED** (the rule's guard, shared with the Sunstone twin): with a
bound disc, wearing **only the Sunstone Lens** (no compass) shows threat blips, a
**bronze** bezel, and **no** north marker; donning the compass turns the bezel
**iron** and makes the N + ticks appear; doffing reverts the bezel to bronze and
removes the N while the blips persist. The surface is north-blind exactly when the
compass is off.

---

## 6. M2 — the opt-in auto-north-orient lock (Daniel ③, default OFF)

> **M2 is a separable second milestone behind M1.** It changes the
> `ApplyFieldOrientation` rotation path conditionally — a bigger lift than adding an
> overlay child. Ship M1 (high-value, low-risk) first; M2 follows. Both are gated on
> Daniel having ratified §4 (he has — Q1 RATIFIED).

### 6.1 What it overrides 🔵

The disc/modal rotation is hard-locked to heading with an explicit "no alternative"
comment:

```csharp
// §2H.1 b4 build-calibration knob: rotation sense … Single knob, shared by both
// surfaces; no north-up alternative (disorientation is the intended design — Daniel).
private const float MapRotationSign = 1f;     // MapSurface.cs:119  (comment :116-118)
```

Auto-north-orient is exactly the "north-up alternative" that comment says doesn't
exist. 🟢 **It is sanctioned ONLY because it is (a) compass-gated AND (b) opt-in,
default OFF.** The default experience is unchanged: heading-up disorientation with the
iron N-ring riding the rim (M1). A player must both wear the compass AND flip a
non-default config to get a north-up-locked map. The pillar's default is intact.

### 6.2 What it does — the inverse of §3.2

When `CompassAutoNorthUp == true` **AND** the compass is worn, the surface locks
north-up instead of heading-up. Mechanically the inverse of M1's orbit:

```csharp
// in ApplyFieldOrientation (MapSurface.cs:1085-1098), M2 branch:
bool northUp = compassWorn && IronCompass.CompassAutoNorthUp;
float rotZ = northUp ? 0f : (MapRotationSign * camYaw);   // north-up → container pinned to north
_mapContainer.localRotation = Quaternion.Euler(0f, 0f, rotZ);
// player chevron: heading-up keeps it screen-up (−rotZ); north-up makes it ROTATE to show heading
_playerMarker.rectTransform.localRotation =
    Quaternion.Euler(0f, 0f, northUp ? (MapRotationSign * camYaw) : -rotZ);
```

Today the container spins (`rotZ`) and the chevron counter-spins (`−rotZ`); in
north-up-lock the container is fixed (`rotZ = 0`) and the chevron spins
(`MapRotationSign * camYaw`, vanilla-style "you-arrow"). 🔴 The `MapRotationSign`
const is **shared by both surfaces** (`:119`), so the north-up override MUST be a
**per-surface runtime branch** (the `northUp` local above), NOT a const flip — a const
flip would force north-up on every surface for every player, exactly the ungated
change the pillar forbids.

**Architect-proposed M2 interaction defaults (reversible; Daniel may adjust):**
1. **Hide the orbiting N when north-up-locked.** With the whole map north-up, north is
   always at 12 o'clock, so the orbiting N is noise. Keep the iron bezel (the
   "compass-active" tell); drop the N glyph (cheap `northUp ? hide N` branch).
2. **Both surfaces.** Same `MapRotationSign` path drives disc + modal (`:1085`), so the
   per-surface `northUp` branch covers both consistently.

### 6.3 The dead-pump guard (#208/#209 — load-bearing across M1 AND M2) 🔴🔵

Daniel ① locked: when the surface ring is up, the **HUD needle hides** (*"it goes
away"*). The pitfall this MUST NOT step on is the one both overlays already learned:

```csharp
// SBPR_CompassHud — SetVisible toggles _content; the HOST (_root) stays active so
// Update keeps pumping (t_61aff612).   SBPR_CompassHud.cs:356-361 🔵
private void SetVisible(bool on) { … _content.gameObject.SetActive(on); }   // NEVER _root
```

🔴 **"HUD needle hides" means toggle the HUD overlay's `_content` child, NEVER
deactivate the host (`_root`) or stop `Update()`.** The host (`_root`, *"ALWAYS
ACTIVE — carries this MonoBehaviour's Update pump"*, `:106`) carries the `Update` pump
that reads `cam.transform.eulerAngles.y` (`:293`) — the **same yaw the disc/modal ring
consumes** (§2). Freeze that pump and both the HUD needle AND the surface ring die.
The compass HUD already shipped this exact fix (#208, `t_61aff612`; `SetVisible`
toggles `_content`). The `DiscWhenBound` hide reuses the EXISTING `SetVisible(false)`
on `_content` — it does NOT introduce a new host-deactivation path.

🔴 **AT-COMPASS-DISC-PUMP:** under `DiscWhenBound`, hiding the HUD needle keeps the
`Update` pump alive (the yaw read never freezes); closing the surface restores the
needle with **no dead frame**.

## 7. The §4.3 supersession re-wording — carried in THIS PR

Per the design doc §4.3 (Daniel RATIFIED Q1) and the repo's spec-first rule (AGENTS.md:
*"spec and code change together"*), this PR carries the re-wording the design doc
deferred. The standalone design doc landed in #226; the dependent surgery lands here
with the impl-spec — the same standalone-then-graduate path the Sunstone twin followed.
The exact before/after edits (grounded line numbers against `main @ cdd5cc2`):

### 7.1 `docs/v3/planning/iron-compass-impl-spec.md`

| Site (main @ cdd5cc2) | Re-wording |
|---|---|
| §0 thesis `:21-22` | *"…grants it on a **separate HUD overlay**, *never* by adding a north arrow back onto the map."* → add: north is granted on the HUD overlay **or, when the Iron Compass is worn and an SBPR map surface (carry-disc or full-map) is showing, as a compass-gated north ring on that surface** — never by adding north to the map **ungated**. North remains an earned, compass-only payoff; a player without the compass sees a north-blind map on every surface. |
| AT-COMPASS-NOMAP-SAFE `:529-533` | keep the NoMap-HUD-safe guarantee; append a sibling line — *"and it adds no **ungated** north indicator to any map. The compass-gated ring (`iron-compass-minimap-ring.md`) is the sanctioned exception: north appears on an SBPR surface **iff the compass is worn**, so the map stays north-blind for the compass-less player."* |
| decision-log `:569-570` | *"the compass NEVER adds a north arrow to any map … non-negotiable design thesis, not a knob."* → *"the compass NEVER adds **ungated** north to any map; the compass-gated ring is the **one** sanctioned exception (Daniel, 2026-06-20). The withheld orientation stays withheld for anyone not wearing the compass. The **gating** is non-negotiable; the surface (HUD vs map ring) and the opt-in north-up lock are the knobs."* |

### 7.2 `docs/v2/planning/cartography-impl-spec.md`

| Site (main @ cdd5cc2) | Re-wording |
|---|---|
| §2H.1 `:725` | *"…only the circular interior rotates (the bezel/frame is fixed); there is **no** north indicator."* → append: *"— except the compass-gated north ring, which renders on the disc AND the full-map/table view only while the Iron Compass is equipped (`iron-compass-minimap-ring.md` §5). The surface itself remains north-blind; the ring is the compass's payoff drawn on the surface, not a property of the map."* |
| AT-LMAP-TC-5 `:2777-2778` | *"there is **no** north indicator, compass rose, north-up mode, or any orienting aid anywhere on the held Local Map."* → append: *"— except the compass-gated north ring (iron bezel + N + ticks), which appears IFF the Iron Compass is worn (`iron-compass-minimap-ring.md` §5); the surface stays north-blind for the compass-less player."* |
| AT-TABLEVIEW-ROT-1 `:2784-2786` | *"there is **no** North indicator/compass rose on the table view"* → append: *"— except the compass-gated ring when the Iron Compass is worn (same rule as the disc; `iron-compass-minimap-ring.md` §5)."* |
| mechanic-5 lock `:2728-2731` | *"Do not add a north-up mode, a compass rose, a North arrow, a fixed-North bezel mark, or any orienting aid to the held Local Map."* → carve out: *"…to the held Local Map **for the compass-less player. The Iron Compass, when worn, is the sanctioned exception (the earned tool the no-map pillar always pointed toward — see this section's own 'future swamp-tier compass' note): it draws a compass-gated iron N-ring on the surface, and an opt-in (default-OFF) north-up lock. Both are gated on the equipped compass; the default no-compass experience is unchanged.**"* |

🔴 **Mechanically (per `sbpr-docs-conventions` + the design doc §4.3): these are
APPEND/carve-out edits, not deletions** — the original no-north lock stays the default
for the compass-less player; the compass is named as the one gated exception. The
`MapRotationSign` "no north-up alternative" code comment (`MapSurface.cs:116-118`) is
**left as-is** (it correctly describes the *const*, which still has no alternative —
the M2 north-up lock is a per-surface *runtime branch*, not a const flip, §6.2). The
spec's mechanic-5 already anticipated "a future swamp-tier compass item"; this carve-out
is that compass arriving.

---

## 8. Acceptance tests

**CI unit (M1, must be green): `tests/CompassNorthGateTests.cs` — AT-COMPASS-GATE.**
Exhaustively asserts the §5 truth table: no compass → no ring at any mode; `HudOnly`
→ ring never, needle never force-hidden; `DiscWhenBound` → ring+needle-hide IFF worn &
surface showing; `Both` → ring shows, needle stays. Engine-free, link-compiled.

**In-game (qa-playtest card; logs-green ≠ playable — Daniel's eyeball):**

- **AT-COMPASS-DISC-RING** (M1) — nomap-ON + carry-disc bound + compass worn → the
  bezel renders **iron** with N + ticks on the rim; per `CompassDiscMode` the HUD
  needle hides (`DiscWhenBound`), both show (`Both`), or only HUD (`HudOnly`).
- **AT-COMPASS-MODAL-RING** (M1, 🔴 Daniel ④) — opening the full-map (M) / table view
  with the compass worn → the same iron bezel + N + ticks render on the **modal** at
  its 900 px scale. The modal is in scope, not disc-only.
- **AT-COMPASS-DISC-ROTATE** (M1, 🔴 geometry) — standing still + rotating the camera
  sweeps the N around the rim: face north → N at 12 o'clock; face east → N at 9
  o'clock; a full 360° camera turn sweeps N 360°. The N glyph stays upright
  (counter-rotated, §3.2).
- **AT-COMPASS-BEZEL-GATED** (M1, 🔴 Daniel ②) — donning the compass turns the bezel
  iron; doffing reverts it to bronze (no persistence, no "ever held" flag). The iron
  RGB reads as iron (not dark bronze) against an actual iron item — §3.4 tuning gate.
- **AT-DISC-NORTH-GATED** (M1, 🔴 §5 rule — shared with the Sunstone twin) — bound
  surface, wearing only the Sunstone Lens (no compass) shows blips + **bronze** bezel
  + **no** north; donning the compass turns the bezel iron + shows N; doffing removes
  N + reverts bronze while blips persist.
- **AT-COMPASS-DISC-PUMP** (M1, 🔴 #208/#209 guard) — under `DiscWhenBound`, hiding
  the HUD needle keeps the overlay's `Update` pump alive (the yaw read never freezes);
  closing the surface restores the HUD needle with **no dead frame**.
- **AT-COMPASS-AUTONORTH** (M2, 🔴 §6 opt-in) — with `CompassAutoNorthUp = true` +
  compass worn, the surface locks north-up (map stops rotating, chevron rotates); with
  it `false` (default), the surface stays heading-up with the orbiting N-ring. The
  toggle is compass-gated (no compass → heading-up regardless).
- **AT-COMPASS-DISC-CLEAN** (clean-room) — no third-party mod code; all hooks are
  base-game primitives (`Hud`, `GameCamera`, `Utils`, `Inventory`) + SBPR-owned
  Cartography types.

## 9. Clean/dirty routing + SpecCheck impact

**Clean/dirty: CLEAN-SIDE.** 🟢 All SBPR-authored (`Features/Exploration/` +
`Features/Cartography/`). Reads vanilla `Hud`/`GameCamera`/`Utils`/`Player`/`Inventory`
only — base-game, fair to read+adapt (ADR-0001). No third-party mod code. **Route
`architect` (this doc) → `engineer-ui` (impl).**

**SpecCheck/manifest: NONE.** 🔵 Render-only; no recipe/piece/station/item change. The
Iron Compass recipe row in `SpecCheck.cs` is untouched. **Patches: NONE new** — the N
glyph + bezel tint live inside the existing `MapSurface` render path
(`TickRotation`/`ApplyFieldOrientation`/`EnsureBuilt`); no vanilla method is patched.

**ADR-0006 (additive):** the N glyph + ticks are built additively (`new GameObject()`
+ `Image`/`RectTransform` under `_mapContainer`), reading no vanilla prefab as a
mutable base.

## 10. Sibling docs that move in this PR

- `docs/v3/planning/iron-compass-impl-spec.md` — the §7.1 re-wording (§0, AT-COMPASS-
  NOMAP-SAFE, decision-log) + a pointer to this impl-spec.
- `docs/v2/planning/cartography-impl-spec.md` — the §7.2 carve-out (§2H.1, AT-LMAP-TC-5,
  AT-TABLEVIEW-ROT-1, mechanic-5).
- `docs/design/nomap.md` — one-line pointer to the surface-ring branch of the compass.
- `docs/design/map-provider-model.md` — the disc + modal gain a north-overlay element;
  note it is a chevron-sibling, NOT routed through `IThreatMarkerProvider` (§3.1).
- `docs/design/sunstone-lens-minimap-handoff.md` — cross-ref: the compass N marker is
  deliberately **separate** from the shipped `IThreatMarkerProvider` (different marker
  kind, §3.1); the single "north is compass-gated" rule (§5) reconciles the two.
- `docs/v3/planning/index.md` — manifest row for this new spec (two-file rule).

## Links

- **Design (GATED):** [`../../design/iron-compass-minimap-ring.md`](../../design/iron-compass-minimap-ring.md) (card `t_85a46f42`, PR #226 @ main `cdd5cc2`).
- **Twin:** [`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md) (card `t_91e86951`); the shipped `IThreatMarkerProvider` is **deliberately not reused** here (§3.1).
- Compass: `Features/Exploration/IronCompass.cs`, `SBPR_CompassHud.cs`; the §7.1 supersession target [`iron-compass-impl-spec.md`](iron-compass-impl-spec.md).
- Surfaces: `MapViewer.cs`, `MapSurface.cs`, `LocalMapController.cs`, `CartographyViewer.cs`, `DiscRingGeometry.cs`; the §7.2 target [`../../v2/planning/cartography-impl-spec.md`](../../v2/planning/cartography-impl-spec.md), [`../../design/map-provider-model.md`](../../design/map-provider-model.md).
- Sibling cartography work (MERGED, grounded): `t_12e15162` (disc-ring margin, PR #213 + impl #216) — same `EnsureBezelTexture` region (§3.5).
- Card `t_ed803a83` (graduates `t_85a46f42`). Grounded against `main` @ `cdd5cc2` (every cited line read directly).
