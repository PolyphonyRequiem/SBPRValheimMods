---
title: "SPIKE findings — bounded 1000 m fork of the vanilla map UI"
status: current
date: 2026-06-10
card: t_e8bbbe48
branch: spike/v2-map-ui-fork
verdict: GO-WITH-CAVEATS
purpose: >
  Throwaway spike retiring the single biggest unknown in the v2 cartography tier:
  can we render a bounded, fixed-zoom 1000 m map view reading our OWN windowed fog
  array (not vanilla's full 256² world array)? Findings + a GO/NO-GO verdict so the
  architect can spec the v2 impl cards against proven reality.
---

# SPIKE — bounded 1000 m map-UI fork (findings)

> **Verdict: 🟢 GO-WITH-CAVEATS.** Every load-bearing mechanism the v2 cartography
> tier hangs on is proven feasible against the *real* decomp + *real* game DLLs.
> The over-provisioning fix, the world→window cell math, the disc clip, and the
> edge-clamp-to-disc are proven **by execution** (31/31 assertions, exit 0). The
> render path compiles **0/0 against the actual `assembly_valheim.dll`** and uses
> only API this repo already ships in-game (`SignPaintPanel`'s runtime uGUI
> Canvas). The caveats are all "known, bounded, and an impl-card concern" — none
> is a wall. **No reason to redesign the tier.**

This is a **throwaway** proof (per the card): the deliverable is *this doc + a
working-or-not verdict*, not production code. The proof code lives on
`spike/v2-map-ui-fork` only and **must not** be merged to `v1`. The architect
integrates *this findings doc* (docs-only) into `v1`; the proof code stays on the
branch as reference.

---

## TL;DR for the architect

| Spike question | Answer | Evidence |
|---|---|---|
| 1. Can we window the fog to 1000 m at native resolution (not the full 256²)? | **Yes.** 33×33 = **1089 cells** vs 65536 — a **60.2× shrink**. | Executed: P1, P3, P4 |
| — What is the real `m_pixelSize` the personal auto-map uses? | **`64f`** — the *same* `Minimap.m_pixelSize`. There is no separate-resolution personal map. The flagged unknown is **resolved**. | Decomp + types.tsv (one `Minimap`, one `m_pixelSize`) |
| 2. Can we render OUR windowed texture into a map view at fixed zoom? | **Yes.** Paint our own `Texture2D` from the windowed array onto a custom `RawImage`/`Canvas`. Compiles 0/0 against the real DLLs. | `SpikeMapViewer.cs` builds; same idiom as shipped `SignPaintPanel` |
| 3. Is the edge-clamp-to-disc sound (not `ClampToScreenEdge`)? | **Yes.** Polar projection onto the 1000 m circle. | Executed: P6 (incl. 3-4-5 → (600,800)) |

**Caveats (all impl-card scope, none blocking):** (C1) `m_explored` is **private** → reflection; (C2) we **paint our own texture**, we do *not* fork vanilla's 4-texture composite shader; (C3) pixel output itself is unverified headless — Daniel eyeballs it on a joined client per "logs-green ≠ playable"; (C4) the personal fog only exists client-side, so the Table's *shared* survey needs its own serialization (already anticipated in requirements §2).

---

## What was actually proven (and how)

Two complementary proofs, because the render fork splits cleanly into "math" (CPU,
testable headless) and "pixels" (GPU, needs a client):

### A. The risk-bearing math — proven **by execution** (31/31, exit 0)

`tools/spike-harness/` compiles the **identical** `BoundedMapMath.cs` source the
in-game viewer uses (via `<Compile Include>` of the mod's file — one source of
truth, no copy) into a net8 console app and runs hard assertions:

```
=== 31/31 checks passed ===
RESULT: ALL SPIKE MATH PROOFS PASS   (exit 0)
```

- **P1 — over-provisioning fix (Daniel's core ask):** `ComputeWindow(R=1000,
  pixelSize=64)` → `CellRadius = ceil(1000/64) = 16`, window **33×33 = 1089
  cells**, vs the full `256²=65536` → **60.2× smaller**. We keep ~1.7% of the
  array. ✅
- **P2 — vanilla fidelity:** our `WorldToCell` is byte-faithful to vanilla
  `WorldToPixel` (`px = round(x/64 + 128)`): `world(0,0)→cell(128,128)`,
  `world(320,−640)→cell(133,118)`, and cell-center round-trips are stable across
  the whole −8000…8000 world span (0 mismatches). ✅
- **P3 — windowing + disc clip:** an explored square at origin + a far patch at
  (5000,5000); the window copies exactly the in-disc explored cells (361) and the
  far patch leaks **zero** cells. ✅
- **P4 — disc containment:** the 33×33 window **fully contains** the 1000 m disc —
  in-disc cell count computed over the *full* grid (769) equals the windowed pass
  (769); nothing is clipped by the window edge. (≈ analytic `π·(1000/64)² ≈ 767`.) ✅
- **P5 — boundary correctness:** a cell center at 960 m lights; at 1024 m it
  shrouds. ✅
- **P6 — edge-clamp-to-disc:** player at (2000,0) clamps onto the circle at
  exactly r=1000, bearing 0°; the 3-4-5 case (3000,4000) clamps to (600,800) at
  53.13°; a player *inside* the disc is not clamped; on-circle is treated as
  inside. ✅
- **P7 — world-edge origin:** origin near the +X world edge (x≈8100) → the east
  half of the window falls **off** the 256² array; those cells shroud cleanly with
  **no exception / no OOB**, lit count drops below a centred disc as expected. ✅

> The lone first-run FAIL was a wrong *test expectation* (I asserted a cell band
> that assumed 600 m full-width when the helper takes 600 m half-extent → 19×19=361
> in-disc cells); the windowing itself was correct. Corrected to the exact 361 and
> re-ran green. Recording it here because an honest spike shows its work.

### B. The render path — proven **by compile against the real game**

`src/SBPR.Trailborne/Features/CartographySpike/SpikeMapViewer.cs` +
`SpikeBootstrapPatch.cs` build **0 errors / 0 warnings** under the repo's
`TreatWarningsAsErrors` gate, against the actual `assembly_valheim.dll` +
`UnityEngine.UI.dll`. That compile is the proof that every API the fork needs is
real and reachable:

- **Read the live fog:** reflect `Minimap.m_explored` (private `bool[]`) and read
  the **public** `m_pixelSize` / `m_textureSize` straight off `Minimap.instance`.
- **Window it:** `BoundedMapMath.ComputeWindow` + `BuildWindowedFog` (the executed
  math above).
- **Paint our own texture:** `new Texture2D(size,size,RGBA32)` + `SetPixels32` +
  `Apply`, `FilterMode.Point` for crisp cells at fixed zoom.
- **Show it full-screen:** a custom `Canvas` (`ScreenSpaceOverlay`, sortingOrder
  5000) + centered `RawImage` — **the same runtime-uGUI idiom the shipped
  `SignPaintPanel` already renders in-game**, so "our bounded texture on screen" is
  not a new capability for this repo, it's a proven one.
- **Hotkey wiring:** F9 toggles the viewer (binds origin to player pos), F10
  re-binds & rebuilds (walk-test). Client-only by construction (no Minimap on the
  dedicated server). Bootstrap is a registered Harmony postfix on `Minimap.Start`,
  so the `PatchCheck` watchdog stays green.

On boot it logs a greppable `[Spike]` line with the live numbers, e.g.:

```
[Spike] Bounded viewer rebuilt @origin=(x,z) | pixelSize=64 textureSize=256 |
        window=33x33=1089 cells (vs full 65536) | discCells=769 exploredInDisc=N
        copiedFromSource=N | over-provisioning shrink = 60.2x
```

**What B does *not* prove:** the actual pixels on a real screen. Texture upload /
overlay compositing is GPU-side and cannot be eyeballed headless. Per the repo's
**"logs-green ≠ playable"** rule, the in-game pixel render is unverified until
**Daniel presses F9 on a joined client** and confirms the parchment disc shows the
explored fog windowed to 1000 m with the rest shrouded. The math underneath that
render *is* executed-proven; the on-screen result is the one open verification.

---

## Grounded vanilla surface (clean-side, ADR-0001 — all base-game)

Confirmed against `assembly_valheim.decompiled.cs` (Bog Witch / Ashlands):

- **One map class only.** `types.tsv` → a single `Minimap` (`:46483`) and a tiny
  `MapTable` (`:114014`). **There is no separate personal-map class at a different
  resolution** — the "personal auto-map" *is* `Minimap.m_explored`. This **resolves
  the build-time unknown** the requirements flagged (§ "Fog storage", §7.3).
- **`m_textureSize = 256`** (`:46692`, **public**), **`m_pixelSize = 64f`**
  (`:46694`, **public**) → world = 256·64 = **16384 m**. Read at runtime; **never
  hardcode**.
- **Fog arrays:** `private bool[] m_explored` / `m_exploredOthers`, sized
  `m_textureSize²` (`:46762/:46764`, allocated `:46910`). **Private → reflection.**
- **Vanilla world→cell:** `WorldToPixel` (`:47998`): `px = RoundToInt(x/64 + 128)`,
  `py = RoundToInt(z/64 + 128)`; explored index `= py*256 + px` (`Explore`,
  `:48038`). Our `BoundedMapMath` reproduces this exactly (P2).
- **Exploration is NOT gated by the map nerf.** `Minimap.Update` calls
  `UpdateExplore` (`:47056`) **before** any `m_noMap` / `MapMode` gate, and
  `UpdateExplore`→`Explore(playerPos, m_exploreRadius=100f)` fills `m_explored` as
  you walk regardless of whether any map is *shown*. **So under v1's minimap-only
  nerf the fog we need to window still accumulates** — confirmed in code, not
  assumed. (Caveat C4 below distinguishes this client fog from the Kit-gate.)
- **Vanilla render = a 4-texture shader composite, NOT a plain blit.** `Start`
  (`:46894-:46923`) builds `m_mapTexture`/`m_forestMaskTexture`/`m_heightTexture`/
  `m_fogTexture` (all **private** 256² `Texture2D`) and feeds them as
  `_MainTex`/`_MaskTex`/`_HeightTex`/`_FogTex` to a **per-instance material**
  (`m_mapLargeShader`) on `m_mapImageLarge` (a `RawImage`). Zoom/pan is the
  RawImage `uvRect` + shader uniforms `_zoom`/`_pixelSize`/`_mapCenter`
  (`CenterMap`, `:47483-:47515`). **Implication:** *forking vanilla's shader* would
  mean reproducing that whole composite — high-risk. **We don't.** We paint our own
  single `Texture2D` from the windowed array onto our own `RawImage`. Lower risk,
  and exactly what requirements §2 ("the forked viewer renders the windowed array
  directly") already calls for.
- **`ClampToScreenEdge` is the WRONG precedent — confirmed.** It lives on **`Hud`**
  (`:34731`), and clamps to `Screen.width`/`Screen.height` (a *screen-rect* clamp
  for off-screen chat/markers). The v2 edge indicator is a **map-space polar clamp
  to the 1000 m disc** — a different operation, validated in P6. Do not reach for
  `ClampToScreenEdge`.

---

## The world→window cell math (for the impl spec)

Given bound origin `O=(ox,oz)`, radius `R=1000`, live `pixelSize=64`,
`textureSize=256`:

```
cellRadius   = ceil(R / pixelSize)                    = 16
centerCell   = ( round(ox/64 + 128), round(oz/64 + 128) )     # vanilla WorldToPixel
windowSize   = 2*cellRadius + 1                       = 33      (33x33 = 1089 cells)
originCell   = centerCell - (cellRadius, cellRadius)            # top-left source cell

for each window cell (wx,wy):
    src = originCell + (wx,wy)
    if src off [0,256)²            -> SHROUD (world edge)        # P7
    cellCenterWorld = (src - 128) * 64
    inDisc = |cellCenterWorld - O|² <= R²                        # WORLD-space clip
    lit    = inDisc AND m_explored[src.y*256 + src.x]
    out[wy*33 + wx] = lit ? 1 : 0
```

Stored per the requirements: **the windowed cell range + the bound-origin world
coordinate** — NOT the full 256² array, NOT a resample. The clip is done in
**world space** (cell *centers*), so the boundary is a true 1000 m circle
regardless of where O sits inside its cell.

Edge indicator (player `P` outside the disc):
```
d = P - O ; dist = |d|
if dist <= R: render player marker at its real position (no clamp)
else:         clampedPoint = O + (d/dist)*R ; arrowBearing = atan2(d.z, d.x)
```

---

## Caveats / open items for the impl cards (none blocking)

- **C1 — `m_explored` is private.** Reflection (`GetField("m_explored",
  NonPublic|Instance)`) works and is proven to compile/reach. The impl can cache
  the `FieldInfo` once (the spike does). Low risk; it's a stable vanilla field.
- **C2 — we paint our own texture; we do NOT fork vanilla's composite shader.**
  This is the *right* call (requirements §2 says so) and avoids reproducing the
  4-texture/height/forest-mask pipeline. Consequence: our viewer is a flat
  parchment/shroud render, not the biome-coloured vanilla look. If the v2 design
  later wants biome tint, that's an additive impl decision (sample
  `WorldGenerator`/`Heightmap.GetBiome` per cell, or read vanilla's
  `m_mapTexture`), not a spike blocker.
- **C3 — pixels unverified headless (the one real open verification).** Math is
  executed-proven; the on-screen result needs Daniel's F9 check on a joined client.
  Budget one in-game playtest pass for AT-SPIKE-RENDER's pixel half before the impl
  cards lean on the exact visual.
- **C4 — client fog ≠ shared Table survey.** `m_explored` is per-client and only
  reflects what *this* player walked (with the Kit, per the gate). The Surveyor's
  Table's *shared, cumulative* survey (requirements §1) is a **separate persisted
  blob** (Table ZDO) that merges multiple surveyors' in-disc fog — its serialization
  is its own impl card, but it windows the **same** way (same grid, same cell math).
  The spike proves the *windowing+render*; the *shared-merge persistence* is
  downstream plumbing (lower risk, no novel UI).
- **C5 — fixed zoom.** Trivially enforced by never wiring scroll/`m_largeZoom`; our
  viewer sizes the RawImage by a constant upscale. No vanilla zoom code is reused.
  Not a risk; noted so the impl doesn't accidentally inherit `UpdateMap`'s zoom
  handling.

---

## Verdict → impl-card guidance

**🟢 GO-WITH-CAVEATS.** Spec the v2 cartography impl cards against this:

1. **Fog windowing + storage** is a solved, executed-proven mechanism — spec it as
   "window `Minimap.m_explored` to the bound disc via the P-proven cell math; store
   windowed-range + origin-world-coord." Don't re-litigate the over-provisioning
   question; it's settled (60× shrink, native resolution, no resample).
2. **The viewer** is "paint our own `Texture2D` → custom `RawImage`/`Canvas` at
   fixed zoom," NOT "fork vanilla's shader." Reuse the `SignPaintPanel` uGUI idiom
   the repo already ships.
3. **Edge indicator** is the P6 polar clamp; explicitly *not* `Hud.ClampToScreenEdge`.
4. **Carry C3 as the one in-game verification** the first impl card must close
   (Daniel's F9 pixel check) before the equip/Table/pin cards build on the exact
   visual.

The proof code (`Features/CartographySpike/`, `tools/spike-harness/`) is throwaway
reference on `spike/v2-map-ui-fork`. **Do not open a feature PR to `v1`.** Only this
findings doc is integration-worthy (docs-only).

---

## Reproduce

```bash
# math proof (executes the shared BoundedMapMath.cs; exit 0 = all pass):
dotnet run -c Release --project tools/spike-harness/spike-harness.csproj

# render-path compile proof (0 errors / 0 warnings against the real game DLLs):
dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release

# in-game (Daniel, joined client): press F9 to toggle the bounded 1000 m viewer,
# F10 to re-bind origin to current pos. Grep the client log for '[Spike]'.
```
