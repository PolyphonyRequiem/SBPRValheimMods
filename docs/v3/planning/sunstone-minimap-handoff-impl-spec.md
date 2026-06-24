---
title: "Sunstone Lens → minimap handoff — impl-spec (AS-BUILT; engineer-ui)"
status: current
purpose: "Buildable + AS-BUILT spec for the Sunstone Lens → minimap detection handoff (card t_91e86951, graduated from the ACCEPTED design doc docs/design/sunstone-lens-minimap-handoff.md / PR #214). Daniel gated all 3 knobs (MinimapHandoffMode=DiscWhenBound, blips=dots+aggro-tint, the universal any-minimap-present rule including the vanilla minimap in nomap-OFF) and then directed 'build the whole thing' — so this doc records what SHIPPED, not just intent: the engine-free LensHandoffDecision truth table (CI-gated), the SunstoneProjection/ThreatBlip lift, the Cartography IThreatMarkerProvider seam for the SBPR carry-disc, the custom-overlay path on the vanilla Minimap for nomap-OFF (Minimap.AddPin grounded OUT — vanilla clobbers pin colour every refresh), the two Config enums, the #209 dead-Update-pump guard, and the AT-LENS-DISC-* tests. Every vanilla fact cited against the decomp (assembly_valheim) + re-verified against SBPR source on main @ 8601647. Built + tested by engineer-ui (0 warnings, 186/186 unit tests). Daniel gates the merge."
owner: Daniel (design + merge authority); Starbright (architect — spec); engineer-ui (impl)
design_source: "docs/design/sunstone-lens-minimap-handoff.md (ACCEPTED, PR #214 @ main 7a35cb5)"
---

# Sunstone Lens → minimap handoff — impl-spec (as-built)

The design note ([`../../design/sunstone-lens-minimap-handoff.md`](../../design/sunstone-lens-minimap-handoff.md),
**ACCEPTED**, PR #214) is the locked *what*: when ANY minimap is present, the Lens'
hostile detection moves onto it; the camera-relative trophy ring is the
**no-minimap fallback only.** This doc is the buildable *how*, and — because Daniel
directed "build the whole thing" rather than stop at a spec — it records the **shipped
implementation** so the doc and the code agree (repo AGENTS.md: spec and code move together).

> **Clean-side (ADR-0001):** every vanilla fact cited is base-game `assembly_valheim`
> — fair to read and adapt. Vanilla projection math is reproduced (not reflected) in
> our own helper; SBPR lines are `main` @ `8601647`. No third-party mod code was read
> or copied.
>
> **No ADR-0006 concern:** this card adds **no item prefab** — it is render-only over
> the existing Sunstone Lens item and the existing cartography / vanilla map surfaces.
> No `new GameObject()` content prefab, no cloning.

## 0. The gated decisions (build constraints)

| Knob | 🟢 Locked value | Build consequence |
|---|---|---|
| `MinimapHandoffMode` | **`DiscWhenBound`** (default; enum live-tunable) | Ring hides when a minimap is present; renders only as the no-minimap fallback. |
| Blip representation | **dots + aggro-tint** (default; `BlipStyle` enum) | Every minimap surface draws a tinted dot, not trophy art, by default. The screen-space ring always shows full trophy art. |
| nomap-OFF case | **draw on the vanilla minimap** (universal rule) | A custom overlay on `Minimap.instance` — NOT `Minimap.AddPin`. |

> **AS-BUILT — minimap blip SIZE (card `t_bc017af4`, Daniel 2026-06-24: "minimap icons
> notably too small, ~75% larger").** This tunes an *unspecified default* — it is NOT a
> change to any gated decision above (representation is still dots+aggro-tint), so
> `requirements.md` is untouched.
>
> - **Default blip size:** **24.5 px** (= the prior `14 × 1.75`), applied on BOTH minimap
>   surfaces (the SBPR carry-disc + the vanilla corner overlay).
> - **Live knob:** `SunstoneLens / MinimapBlipPx`, range **[8, 48]**, default 24.5 — edit on
>   a joined client and the next detection sweep rescales; no rebuild. Both surfaces read the
>   single resolved symbol `Plugin.ResolvedMinimapBlipPx`, so they **cannot desync** by
>   construction (AT-BLIP-PARITY).
> - **Shared home:** the size/ratio constants live in **`Features/Cartography/MinimapThreatMetrics.cs`**
>   (`DefaultBlipPx`, `PipToBlipRatio`, `RimScale`) — homed in *Cartography*, the lower layer
>   both surfaces already depend on, so neither surface inverts the blessed Sunstone→Cartography
>   dependency arrow (`IThreatMarkerProvider.cs`). It is engine-free → link-compiled into the
>   test project, so the ratios are CI-pinned (`MinimapThreatMetricsTests`).
> - **Pips + rim scale WITH the blip:** star-pip size = **blip × 0.5** (`PipToBlipRatio`,
>   reproducing the historical `7/14`); off-edge rim indicator = **blip × 0.6** (`RimScale`,
>   value unchanged). Deriving the pip from the blip also collapsed a second latent desync (the
>   per-surface `MinimapPipPx`/`ThreatPipPx`, both `7f`). The screen-space ring is unaffected
>   (its own distance→size encoding); `RimInset`/`ThreatRimInset` (0.92, *position*) untouched.

**Three render surfaces, one detection feed.** The detection mechanic
(`SunstoneLens.GatherHostiles`) and the per-hostile visual derivation
(`SunstoneProjection`) are single-sourced; only the world→screen projection differs
per surface:

| Surface | When | Projection | Orientation |
|---|---|---|---|
| **Ring HUD** | no minimap present (fallback) | camera-relative bearing | camera/forward-up |
| **SBPR carry-disc** | nomap-ON + local map bound | `MapSurface.WorldToSurfacePx` | camera/forward-up (rides the rotating disc) |
| **Vanilla minimap overlay** | nomap-OFF (vanilla corner map) | vanilla `WorldToMapPoint` (reproduced) | north-up (exempt — design §6) |

## 1. File map — what shipped

```
src/SBPR.Trailborne/Features/Sunstone/
  LensHandoffDecision.cs        ← NEW: engine-free enums (MinimapHandoffMode, BlipStyle,
                                       LensSurface) + the pure surface-cascade/mode truth table.
                                       Link-compiled into the test project → CI-gated.
  SunstoneProjection.cs         ← NEW: ThreatBlip struct + the lifted derivation (AggroTint,
                                       ResolveTrophySprite, StarSprite, ThreatGlyph, DotSprite).
  SunstoneMinimapThreatLayer.cs ← NEW: the vanilla-Minimap custom overlay (nomap-OFF path).
  SunstoneLensHudOverlay.cs     ← EDIT: consume SunstoneProjection; per-tick handoff plan; ring
                                       gate on _content; register the disc provider; drive the
                                       vanilla overlay. (Trophy/star/tint helpers removed — lifted.)

src/SBPR.Trailborne/Features/Cartography/
  IThreatMarkerProvider.cs      ← NEW: the seam interface + ThreatMarkers registry + DiscThreatMarker.
  MapSurface.cs                 ← EDIT: RebuildOverlay pulls ThreatMarkers.Collect (DISC ONLY) →
                                       SpawnThreatMarker, added to _pinObjects (rides rotation, clears).

src/SBPR.Trailborne/
  Plugin.cs                     ← EDIT: bind LensMinimapHandoffMode + LensMinimapBlipStyle enums.

tests/
  LensHandoffDecisionTests.cs   ← NEW: the full truth table (AT-LENS-DISC-NODRIFT policy half).
  SBPR.Trailborne.Tests.csproj  ← EDIT: link-compile LensHandoffDecision.cs.
```

No `SpecCheck.cs` change (render-only; §8). **Build: 0 warnings / 0 errors
(`<TreatWarningsAsErrors>` ON). Tests: 186/186 pass** (167 prior + the new truth table).

## 2. The `SunstoneProjection` lift — `Character → ThreatBlip`

Before this card the per-hostile visual derivation was **private to the ring overlay**.
It is lifted to `SunstoneProjection` (public static) so all three surfaces consume one
mapping (zero-drift). The struct:

```csharp
public readonly struct ThreatBlip {
    public readonly Character Character;   // live ref
    public readonly Vector3   WorldPos;    // transform.position at sweep time
    public readonly Color     Tint;        // AggroTint — 🟡 alerted / 🟠 aggro-other / 🔴 aggro-you
    public readonly Sprite?   Trophy;      // ResolveTrophySprite (null → ThreatGlyph fallback)
    public readonly int       Stars;       // Mathf.Max(0, GetLevel() - 1)
}
```

`SunstoneProjection.Project(IReadOnlyList<Character>, Player, List<ThreatBlip>)` is the
ONE place tint/trophy/stars derive. The lifted helpers (verbatim from the old overlay):
`AggroTint` (vanilla `BaseAI.IsAlerted`/`GetTargetCreature`), `ResolveTrophySprite`
(`CharacterDrop` → `ItemType.Trophy` → `m_icons[0]`, cached), `StarSprite` (harvested from
`EnemyHud.m_baseHud` `level_2`/`level_3`), `ThreatGlyph`/`ProceduralThreatGlyph`
(trophy-less fallback), plus a new `DotSprite()` (the minimap blip dot). The colour
consts `CYellow`/`COrange`/`CRed` moved here too.

`SunstoneLens.GatherHostiles` is unchanged — still the detection mechanic, public static,
in `SunstoneLens.cs`. The overlay's `Update` sweeps (throttled), then calls
`SunstoneProjection.Project` every frame (the cheap part) so the ring keeps per-frame
aggro freshness and all surfaces read the same derivation.

> **AT-LENS-DISC-NODRIFT (the lift's invariant).** Ring, disc, and vanilla overlay all
> consume the single `SunstoneProjection.Project` output (`_blips`). The ring's
> `ApplySlot` reads `blip.Tint`/`blip.Trophy`/`blip.Stars`; the disc adapter and the
> vanilla layer read the same struct. There is exactly one derivation path — they cannot
> desync by construction. (The *policy* half of NODRIFT — which surface is live — is the
> CI-gated truth table, §3.)

## 3. The handoff decision — engine-free + CI-gated (`LensHandoffDecision`)

The load-bearing policy (which surface owns detection, and is the ring visible) is
extracted into `LensHandoffDecision` — pure, `UnityEngine`-free, link-compiled into the
test project exactly like `DiscRingGeometry`/`BoundedMapMath`. The overlay reads the live
world state, reduces it to two booleans, and this decides:

```csharp
LensSurface ResolveSurface(bool sbprDiscBound, bool vanillaMinimapShowing)
  // sbprDiscBound → SbprDisc ; else vanillaMinimapShowing → VanillaMinimap ; else Ring

LensRenderPlan Resolve(LensSurface surface, MinimapHandoffMode mode)
```

The truth table (asserted exhaustively in `LensHandoffDecisionTests.cs`):

| `MinimapHandoffMode` | surface == Ring | surface == SbprDisc / VanillaMinimap |
|---|---|---|
| `RingOnly` | ring shows | **ring shows** (minimap suppressed — the escape hatch) |
| `DiscWhenBound` (default) | ring shows | **ring hides; minimap surface shows threats** |
| `Both` | ring shows | **ring shows AND minimap surface shows threats** |

`LensRenderPlan { bool RingContentVisible; bool FeedMinimap; LensSurface MinimapTarget; }`.
The overlay's `Update` consumes it:

```csharp
bool sbprDiscBound = CartographyViewer.IsMinimapBound;                          // nomap-ON + bound + imprinted
bool vanillaMinimapShowing = Minimap.instance?.m_mode == Minimap.MapMode.Small; // nomap-OFF (SetMapMode forces None under m_noMap)
LensRenderPlan plan = LensHandoffDecision.Resolve(sbprDiscBound, vanillaMinimapShowing, mode);
```

**Why `m_mode == Small` ⇔ nomap-OFF:** vanilla `Minimap.SetMapMode` forces `MapMode.None`
whenever `Game.m_noMap`, so the corner `Small` map can only show in nomap-OFF — no explicit
`Game.m_noMap` read needed. The modal `Large` (M-key) is deliberately NOT a Lens surface
(detection belongs on the always-on corner map, not a navigation full-screen the player opens).

### 3.1 The #209 invariant — the highest-risk line (AT-LENS-DISC-PUMP) 🔴

"Ring hides" = `_content.SetActive(false)` on the **content child**, NEVER the host
`_root`. The detection sweep + projection are driven *only* from
`SunstoneLensHudOverlay.Update()`, and PR #209 (t_d5949685) already fixed the exact bug
where deactivating the host froze the `Update` pump dead. Under `DiscWhenBound` with a
minimap present, the implemented `Update`:

1. resolves the plan (ring not visible, feed minimap),
2. keeps the host active (`SetVisible(true)` toggles `_content`, not `_root`),
3. keeps sweeping + projecting (the pump is alive),
4. hides `_content` (no ring trophies drawn), and
5. feeds the active minimap surface.

⇒ **A minimap rendering threats while the ring is hidden still depends on the ring
overlay's `Update` being alive.** That is the load-bearing line. The inert early-returns
(not worn / depleted / no player) call `StandDownMinimaps()` so an unequipped or depleted
lens cannot leave stale threats on a minimap.

## 4. The SBPR carry-disc seam — `IThreatMarkerProvider` (nomap-ON)

Cartography owns the disc; it pulls registered threat providers each `RebuildOverlay`,
mirroring exactly how it already pulls `WorldPins.CollectInDiscPins`. The seam
(`Features/Cartography/IThreatMarkerProvider.cs`):

```csharp
public interface IThreatMarkerProvider {
    void CollectThreatBlips(Vector3 origin, float radius, List<DiscThreatMarker> into);
}
public readonly struct DiscThreatMarker { Vector3 WorldPos; Color Tint; Sprite? Icon; int Stars; }
public static class ThreatMarkers {           // the registry (mirrors WorldPins as a static seam)
    Register / Unregister / HasProviders / Collect(origin, radius, into)  // each provider guarded
}
```

`DiscThreatMarker` is Cartography's OWN type (not Sunstone's `ThreatBlip`) so Cartography
has **zero** compile dependency on `Features/Sunstone`. The dependency arrow is one-way:
`Sunstone → (registers into) → Cartography.ThreatMarkers`.

**Sunstone's adapter** (`SunstoneThreatProvider`, nested in the overlay) forwards the pull
to `SunstoneLensHudOverlay.CollectDiscThreats`, which appends `_blips` as `DiscThreatMarker`s
**only when `_feedDiscNow`** (set by the plan when the target is the SBPR disc) — under
`BlipStyle.Dots` the marker carries a null `Icon` (the disc draws a tinted dot), under
`Trophy` it carries the trophy sprite.

**In `MapSurface.RebuildOverlay`** (DISC ONLY — `_cfg.PlayerCentred`), after the survey-pin
loop:

```csharp
if (_cfg.PlayerCentred && ThreatMarkers.HasProviders) {
    ThreatMarkers.Collect(FrameCenter(), radius, _threatScratch);
    foreach (var t in _threatScratch) {
        if (!BoundedMapMath.InDisc(t.WorldPos.x, t.WorldPos.z, origin.x, origin.z, radius)) continue;
        Vector2 anchored = WorldToSurfacePx(t.WorldPos, survey);     // SAME projection as pins
        if (anchored.sqrMagnitude > discR * discR) continue;          // clip to the visible circle
        SpawnThreatMarker(t, anchored);
    }
}
```

`SpawnThreatMarker` instantiates an `Image` under `_overlayLayer`, sets `Image.color =
t.Tint` and the dot (or trophy) sprite, and **adds the GameObject to `_pinObjects`**. That
single choice is load-bearing: `_pinObjects` already rides the rotating disc container for
position, is counter-rotated upright by `CounterRotatePins` (so dots stay screen-upright),
and is cleared each rebuild by `ClearPinObjects` — **so the threat layer needs no new
rotation or lifecycle plumbing** (AT-LENS-DISC-CAMREL rides the same camera-yaw frame as
the pins). The blip OWNS its `Image.color`, which is why the disc honours the aggro tint
where a vanilla `AddPin` would be clobbered (§5).

**Cadence.** The disc rebuilds via `LocalMapController`'s 0.25 s poll → `BindMinimap` →
`MapSurface.Render` → `RebuildOverlay`. The Lens sweeps every `DefaultDetectInterval`
(0.5 s); the provider hands out the last swept blip set. 0.25–0.5 s is fine for threat
blips; no faster path added for v1.

## 5. The vanilla-minimap overlay — `SunstoneMinimapThreatLayer` (nomap-OFF)

🔴 **Why NOT `Minimap.AddPin`.** Vanilla `UpdatePins()` hard-overwrites
`pin.m_iconElement.color` on EVERY refresh (`color2 = (ownerID != 0) ? grey : Color.white;
pin.m_iconElement.color = color2;`). So a vanilla pin **cannot carry the per-aggro dynamic
tint** Daniel locked in Knob 2 — it would be stomped white every frame. (SBPR already feels
this: `WorldPins.ReapplyColors` exists solely to re-stamp pin colour after the stomp.)
Daniel's steer 2026-06-20: "maybe you don't use the actual pinning system?" → the nomap-OFF
path is a **custom overlay** that owns its own `Image.color`.

`SunstoneMinimapThreatLayer.Render(IReadOnlyList<ThreatBlip>, BlipStyle)`:
- Mounts a `RectTransform` layer lazily under `Minimap.instance.m_pinRootSmall` (re-mounts
  if the Minimap was rebuilt on world-load/relog).
- Projects each blip via `TryVanillaSmallMapPos`, which **reproduces** vanilla's private
  world→small-map math (trivial arithmetic, fair to adapt per ADR-0001):
  `WorldToMapPoint` (`mx = p.x/m_pixelSize + textureSize/2`, normalized) → `IsPointVisible`
  (within `m_mapImageSmall.uvRect`, else off-map skip) → `MapPointToLocalGuiPos`
  (`((m - uvMin)/uvW) * rectW`, centred), reading the public `m_pixelSize`/`m_textureSize`/
  `m_mapImageSmall`/`m_pinRootSmall`. No reflection into privates.
- Sets `img.color = blip.Tint` (OUR colour — no vanilla `UpdatePins` runs on our layer, so
  it survives) and the dot (or trophy) sprite. Pools images; parks the tail.

**Orientation — deliberately north-up, EXEMPT from the thesis (design §6).** The layer
parents under the non-rotating `m_pinRootSmall`, so blips sit at fixed north-up map
positions exactly like vanilla pins. **Do NOT counter-rotate** (that is the disc's
behaviour). Verified north-up against decomp (only `m_smallShipMarker` rotates) AND SBPR
source (zero code rotates `m_smallRoot`/`m_pinRootSmall`; SBPR's free-rotate is the
nomap-ON `MapSurface` disc only). Client-only by construction (`Minimap.instance` is null on
a dedicated server).

> **One in-game registration caveat to verify (honesty gate).** `TryVanillaSmallMapPos`
> places blips from the rect CENTRE (our layer is centre-pivoted; vanilla's pin root layout
> is scene-authored). The math is the vanilla chain, but the exact pixel registration of a
> blip vs the terrain cell it annotates is a **client-visual** that can't be proven headless
> — it is an in-game AT (AT-LENS-DISC-NOMAP-OFF below), Daniel's eyeball. Logs-green ≠
> playable.

## 6. Config — the two live enums (`Plugin.cs`)

```csharp
LensMinimapHandoffMode = Config.Bind("SunstoneLens", "MinimapHandoffMode",
    MinimapHandoffMode.DiscWhenBound, "…DiscWhenBound (default): ring hides, threats move onto the minimap…");
LensMinimapBlipStyle = Config.Bind("SunstoneLens", "MinimapBlipStyle",
    BlipStyle.Dots, "…Dots (default): a small aggro-tinted dot…Trophy: the creature trophy sprite + tint…");
```

The enums live in `LensHandoffDecision.cs` (engine-free) so the Config bind and the unit
test share one definition. ⚠️ `DiscWhenBound` is a slight misnomer under the universal rule
(it means "hand off whenever ANY minimap is present"); the value name is kept stable so a
Config key Daniel may already have bound doesn't churn (the XML-doc states the broadened
meaning). A rename to `MinimapWhenPresent` is a cosmetic follow-up, not this card.

## 7. Acceptance tests

**CI unit (shipped, green): `tests/LensHandoffDecisionTests.cs` — AT-LENS-DISC-NODRIFT
(policy half).** Exhaustively asserts the truth table: the ring is the fallback at every
mode when no minimap is present; `DiscWhenBound` hands off to EITHER minimap (ring hidden,
minimap fed) and the vanilla minimap gets identical treatment to the SBPR disc (the
universal rule); `RingOnly` keeps the ring + suppresses the minimap; `Both` shows both; and
the end-to-end nomap-OFF default renders on the vanilla minimap, NOT "ring stays." 186/186
total tests pass.

> The NODRIFT *derivation* half (one tint/trophy/stars path) is enforced by construction:
> there is a single `SunstoneProjection.Project`, and all three surfaces read its
> `ThreatBlip` output — verified by review/grep (no surface re-derives tint locally).

**In-game (qa-playtest card; logs-green ≠ playable):**
- **AT-LENS-DISC-HANDOFF** — nomap-ON + bound disc + Lens worn & charged: hostiles within
  50 m render as dots+tint on the disc; per `MinimapHandoffMode` the ring hides
  (`DiscWhenBound`), both show (`Both`), or only the ring shows (`RingOnly`).
- **AT-LENS-DISC-PUMP** (🔴 #209 guard) — under `DiscWhenBound`, when the ring hides the
  sweep keeps running (the `content → hidden` diagnostic line still appears; the disc keeps
  receiving fresh blips). Unequip the local map → ring returns with no dead frame.
- **AT-LENS-DISC-CAMREL** (🔴 thesis guard) — on the SBPR disc, standing still + rotating
  the camera sweeps every threat blip around the disc; no cardinal orientation. **Does NOT
  apply to the vanilla-minimap path** (deliberately north-up).
- **AT-LENS-DISC-NOMAP-OFF** — in nomap-OFF (vanilla corner map up), Lens detection
  **renders on the vanilla minimap** as dots+tint (the custom overlay, §5) — NOT "ring
  stays." Ring hidden under `DiscWhenBound`. Blips sit north-up.
- **AT-LENS-DISC-VANILLA-TINT** — on the vanilla minimap, an aggro'd hostile's blip shows
  the 🔴 aggro-you tint and KEEPS it across minimap refreshes (proves the custom overlay's
  `Image.color` survives where `AddPin` would be clobbered white).

## 8. Clean/dirty routing + SpecCheck impact

**Clean/dirty: CLEAN-SIDE.** All SBPR-authored (`Features/Sunstone/` +
`Features/Cartography/`). Reads vanilla `Minimap`/`Character`/`BaseAI`/`EnemyHud` only —
base-game, fair to read+adapt (ADR-0001). No third-party mod code. **SpecCheck/manifest:
NONE** (render-only; no recipe/piece/station/item change). **Patches: NONE new** — the disc
seam is a pull from inside the existing `RebuildOverlay`; the vanilla overlay mounts under an
existing vanilla `RectTransform` (no `Minimap` method patched).

## 9. Sibling docs that move in this PR (design §7)

- `sunstone-lens-trophy-ring.md` — §0 NoMap⇒HUD rationale → CONDITIONAL; AT-LENS-RING-CAMREL
  re-scoped to NoMap-worlds-only. (Done in this PR.)
- `sunstone-lens-impl-spec.md` §5 — render-surface carve-out: "when a minimap is present."
- `map-provider-model.md` — the disc gains its first non-cartography consumer + the
  `IThreatMarkerProvider` seam.

## Links

- Design (ACCEPTED): [`../../design/sunstone-lens-minimap-handoff.md`](../../design/sunstone-lens-minimap-handoff.md) (PR #214).
- Ring render: [`../../design/sunstone-lens-trophy-ring.md`](../../design/sunstone-lens-trophy-ring.md);
  base Lens spec [`sunstone-lens-impl-spec.md`](sunstone-lens-impl-spec.md).
- Cartography: [`../../design/map-provider-model.md`](../../design/map-provider-model.md);
  code `MapSurface.cs`, `MapViewer.cs`, `LocalMapController.cs`, `CartographyViewer.cs`.
- Seam precedent: `Features/MarkerSigns/WorldPins.cs` (`CollectInDiscPins`); the colour-stomp
  defence `WorldPins.ReapplyColors`.
- Cards: design `t_3129842a` (PR #214 merged) → this impl card `t_91e86951` (engineer-ui).
  /bug thread `ticket-sunstone-minimap`.
