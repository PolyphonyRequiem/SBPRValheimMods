---
title: "Sunstone Lens → minimap handoff — buildable impl spec (v3)"
status: implemented
purpose: "The buildable how for the Sunstone Lens → minimap detection handoff (card t_54c989d3), graduating the merged+gated design (docs/design/sunstone-lens-minimap-handoff.md, ACCEPTED 2026-06-20). One shared SunstoneProjection (Character → ThreatBlip) feeding THREE surface-agnostic consumers — the camera-relative trophy ring, the SBPR carry-disc (nomap-ON, via a Cartography IThreatMarkerProvider seam), and a CUSTOM overlay on the vanilla small minimap (nomap-OFF). The MinimapHandoffMode/BlipStyle live Config enums (Daniel-gated DiscWhenBound + DotsAndTint), the #209 dead-Update-pump guard, the camera-relative thesis guard, and the AT-LENS-DISC-* acceptance tests. Built + shipped in ONE PR with the code (AGENTS.md: spec and code change together) by engineer-ui; Daniel gates the merge."
owner: Daniel (design authority) / engineer-ui (impl)
graduated_from: "docs/design/sunstone-lens-minimap-handoff.md (ACCEPTED — supersedes the spec-only framing of card t_91e86951)"
---

# Sunstone Lens → minimap handoff — buildable impl spec

The design ([`../../design/sunstone-lens-minimap-handoff.md`](../../design/sunstone-lens-minimap-handoff.md),
ACCEPTED — Daniel gated all 3 knobs 2026-06-20) is the locked **what**. This doc is the
buildable **how**, and it ships in the SAME PR as the code (AGENTS.md: spec and code change
together). Every seam is grounded to `main` file:line and to the vanilla decomp.

> **STATUS: implemented (card t_54c989d3).** Build `dotnet build
> src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` → 0/0; `dotnet test` →
> all green (+19 SunstoneHandoffPolicy cases). logs-green ≠ playable — Daniel verifies the
> AT-LENS-DISC-* tests in-game.

---

## 0. SpecCheck manifest impact (read first)

**SpecCheck/manifest: +0.** Render-only — no recipe / piece / station / item / ZDO change.
The Lens row in `SpecCheck.cs` is untouched; no new SpecCheck row. The whole card is cosmetic
detection rendering: it reads game state, never writes it.

---

## 1. The three Daniel-gated knobs (locked — do NOT re-litigate)

| Knob | Decision | Where it lives |
|---|---|---|
| **1 — replace vs supplement** | `MinimapHandoffMode = DiscWhenBound` (default). Ring is the FALLBACK-only surface (renders threats when NO minimap is present). | `SunstoneHandoffPolicy.DefaultMode`; live `Config.Bind` "SunstoneLens"/"MinimapHandoffMode" |
| **2 — blip representation** | `BlipStyle = DotsAndTint` (default). Trophy art is too small at the disc's inner ~48 % to read; a tinted dot reads cleaner. | `SunstoneHandoffPolicy.DefaultBlipStyle`; live `Config.Bind` "SunstoneLens"/"BlipStyle" |
| **3 — nomap-OFF case** | "Any minimap, any reason." The VANILLA small minimap (nomap-OFF) hosts detection too — via a custom overlay (NOT vanilla pins). | `SunstoneVanillaMinimapOverlay` |

---

## 2. One producer — `SunstoneProjection` (AT-LENS-DISC-NODRIFT)

The detection **mechanic** is render-agnostic and unchanged:
`SunstoneLens.GatherHostiles(player, radius, results)` (`SunstoneLens.cs:386`, public static)
returns `List<Character>`.

The per-hostile **visual derivation** — which USED to be private to
`SunstoneLensHudOverlay` — is lifted to `SunstoneProjection` so all three surfaces are
consumers of ONE mapping:

```
SunstoneProjection.Project(Character c, Player p) → ThreatBlip { Vector3 WorldPos, Color Tint, Sprite? Trophy, int Stars }
```

| Datum | Lifted helper (now in SunstoneProjection) | Was (private to overlay) |
|---|---|---|
| aggro tint (🟡/🟠/🔴) | `AggroTint(c, player)` | `SunstoneLensHudOverlay.cs:584` |
| trophy sprite | `ResolveTrophySprite(c)` | `:506` |
| star sprite | `StarSprite()` | `:547` |
| pip count | `Mathf.Max(0, c.GetLevel()-1)` | `:331` |
| trophy-less glyph | `ThreatGlyph()` | `:609` |

The ring's `RenderRing()` now calls `SunstoneProjection.Project(c, player)` and consumes the
`ThreatBlip` (no inline derivation) — so a future tweak to the aggro-colour rule changes EVERY
surface together. `ThreatBlip` / `IThreatMarkerProvider` / `ThreatMarkerRegistry` / `ThreatBlipArt`
live in the **Cartography** namespace (`Features/Cartography/ThreatMarkers.cs`) so the dependency
arrow is **Sunstone → Cartography** (Sunstone registers INTO the registry; Cartography never
references Sunstone — mirrors WorldPins being consumed by MarkerSigns).

---

## 3. Three surface-agnostic consumers

### 3a. Camera-relative ring (refactor only)

`RenderRing()` (`SunstoneLensHudOverlay.cs:285`) keeps its geometry (camera-relative bearing,
size ∝ proximity, ring layout) byte-unchanged; only the visual derivation routes through
`SunstoneProjection.Project`. No behavioural change to the ring itself.

### 3b. SBPR carry-disc (nomap-ON) — `IThreatMarkerProvider` seam

- Cartography owns the disc and ASKS a registered provider each rebuild —
  `MapSurface.RebuildOverlay` (`:621`) now calls `RebuildThreatLayer(survey)`, which pulls from
  `ThreatMarkerRegistry.Collect(origin, radius, blips)`, mirroring its existing
  `WorldPins.CollectInDiscPins` pull (`:633`).
- Sunstone registers a `SunstoneThreatProvider` (forwards to
  `SunstoneProjection.CollectThreatBlips`) once, in `SunstoneLensHudOverlay.EnsureBuilt`.
- Each blip is placed via `WorldToSurfacePx(blip.WorldPos, survey)` (`:481`) — the SAME
  projection the disc's pins + player marker use, so a blip lands on the exact terrain cell.
- A NEW transient threat layer (`_threatObjects`), PARALLEL to the survey-pin overlay — NOT
  `SurveyPin` (`SurveyData.cs:35-41` can't carry tint/trophy/pips). Cleared + rebuilt each
  `RebuildOverlay`. Disc-only (`_cfg.PlayerCentred`) — the modal is the focused full survey, not
  a glanceable threat radar.

### 3c. Vanilla small minimap (nomap-OFF) — CUSTOM overlay, NOT pins

`SunstoneVanillaMinimapOverlay` draws a parallel `RectTransform` layer under
`Minimap.m_pinRootSmall`, projecting each `ThreatBlip` through vanilla's OWN world→minimap-pixel
math, re-implemented from the decomp (clean-side):

- `WorldToMapPoint` (decomp `Minimap.cs:47977`): world → normalized texture coords, **zero
  rotation** (north-up).
- `MapPointToLocalGuiPos` (decomp `:47938`): map-point → anchored px in the small-map rect,
  honouring the live `uvRect` zoom window.
- visibility cull mirrors vanilla `IsPointVisible` (`:47957`).

**Why a custom overlay, NOT `Minimap.AddPin`:** vanilla `UpdatePins()` hard-overwrites every
pin's `m_iconElement.color` to white/grey on EVERY refresh (decomp `:47832-47836`). A vanilla pin
therefore **cannot** carry the per-aggro dynamic tint Daniel locked in Knob 2 — it would be
clobbered to white every frame. The custom layer owns its own `Image.color`, so the aggro tint
survives. (Precedent that the "custom sprite on the vanilla minimap" trick is shipped:
`MarkerSigns`/`WorldPins.ProjectPin` overrides `PinData.m_icon` after AddPin — we go further and
skip AddPin entirely.)

> **One projection, three consumers.** Only the world→screen projection differs (ring =
> camera-relative bearing; disc = `WorldToSurfacePx`; vanilla overlay = vanilla `WorldToMapPoint`).
> Tint / trophy / pips derive identically (AT-LENS-DISC-NODRIFT).

---

## 4. The load-bearer — `MinimapHandoffMode` + the #209 guard

`SunstoneHandoffPolicy` (engine-free, link-compiled into the test suite) is the pure decision
table: `RingShowsThreats(mode, anyMinimapPresent)` and `MinimapShowsThreats(mode,
anyMinimapPresent)`. The overlay's `Update` reads them each frame:

```
anyMinimap = IsAnyMinimapPresent()           // CartographyViewer.IsMinimapBound OR vanilla small minimap shown
ringShowsThreats    = SunstoneHandoffPolicy.RingShowsThreats(mode, anyMinimap)
minimapShowsThreats = SunstoneHandoffPolicy.MinimapShowsThreats(mode, anyMinimap)
```

`IsAnyMinimapPresent` = `CartographyViewer.IsMinimapBound` (`:257`, nomap-ON disc) **OR**
`SunstoneVanillaMinimapOverlay.IsVanillaSmallMinimapShown()` (`Minimap.instance.m_mode ==
MapMode.Small`, nomap-OFF). The two are mutually exclusive by vanilla construction (`SetMapMode`
forces `None` under `Game.m_noMap`), so it is a clean OR.

### 🔴 THE #209 LANDMINE (AT-LENS-DISC-PUMP — highest-risk line)

"Ring hides" means **suppress the ring's threat VISUALS while keeping the overlay's `Update` pump
alive**, NOT deactivate the host. The detection sweep is driven ONLY by
`SunstoneLensHudOverlay.Update()` (`:218`), and the disc/vanilla surfaces depend on that pump for
their blip feed.

- When `!ringShowsThreats`: the overlay hides the ring's slots + solar ring + debug text
  WITHOUT touching `_content` activation or the host — the sweep keeps running and publishes
  `LiveHostilesOrNull` each tick.
- The detection sweep runs **regardless** of which surface draws — single-sourced from the live
  pump. The disc provider + vanilla overlay both read the published list; the disc provider also
  gates on `SunstoneLens.IsLensActive` so a disc never shows threats without a worn+charged lens.
- The host `GameObject` is NEVER deactivated; `SetVisible` only toggles `_content` (the PR #209 /
  t_d5949685 fix path). Deactivating the host would freeze the pump dead — the exact #209 bug.
- When detection goes inactive (no player / lens unworn / inert), `ClearDetectionSurfaces()`
  empties the published list and hides the vanilla overlay so no stale blips linger.

---

## 5. 🔴 THESIS GUARD (AT-LENS-DISC-CAMREL) + the nomap-OFF re-scope

Every SBPR map surface is forward-up/free-rotate, NO north (preserves the Iron Compass's
exclusive north payoff — `IronCompass.cs:10-13`). The disc threat layer keys rotation on the SAME
camera-yaw frame as its host:

- The disc threat layer rides `_mapContainer`'s `+rotZ` (`ApplyFieldOrientation`,
  `MapSurface.cs:986`) for POSITION; `CounterRotateThreats(-rotZ)` cancels the spin so the
  dot/trophy stays screen-upright (same idiom as `CounterRotatePins`). Standing still + rotating
  the camera sweeps every disc blip around the disc; no cardinal orientation.

### The nomap-OFF vanilla minimap is north-up — and EXEMPT (decomp-verified)

The vanilla minimap is north-up: `WorldToMapPoint` (decomp `:47977`) maps world→texture with
**zero rotation**; the map texture is fixed north-up and the player chevron `m_smallMarker`
rotates instead (`:47897`). In nomap-OFF the player ALREADY has a north-up vanilla minimap and
full cardinal orientation independent of the Sunstone, so detection there leaks nothing the
player didn't already have. The thesis defends the **NoMap** worlds (ring + SBPR disc, both
camera-relative); nomap-OFF is not such a world. **The vanilla overlay is therefore deliberately
north-up and EXEMPT** from AT-LENS-DISC-CAMREL.

> ⚠️ **REVIEW FLAG (the one contradiction surfaced).** The impl card's item-5 note claimed "SBPR's
> nomap-OFF minimap is SBPR-built free-rotate, NOT vanilla north-up." That is NOT what ships: NO
> SBPR patch makes the vanilla small minimap free-rotate (the v1 "minimap freely rotating"
> baseline from `PARKED-2026-06-03.md:20` was NEVER implemented — `cartography-v2.md:258` confirms
> the nerf was never built; no code touches `m_smallRoot`/`m_mapImageSmall` rotation). The LOCKED,
> Daniel-gated design doc §6 ("the vanilla minimap IS north-up — verified") is authoritative and
> decomp-confirmed, so the implementation follows the DESIGN DOC. Surfaced here + in the PR so a
> reviewer can confirm the design-doc reading wins over the card note.

---

## 6. Acceptance tests (named — logs-green ≠ playable)

- **AT-LENS-DISC-HANDOFF** — nomap-ON + bound disc + Lens worn/charged → hostiles ≤30 m render as
  blips on the disc; per `MinimapHandoffMode` the ring hides (`DiscWhenBound`) / both show
  (`Both`) / only the ring shows (`RingOnly`). *(The decision table is unit-fenced by
  `SunstoneHandoffPolicyTests`; the draw is Daniel's in-game eyeball.)*
- **AT-LENS-MINIMAP-OFF** — nomap-OFF → threats render as a CUSTOM overlay on the vanilla small
  minimap (full tint; trophy/pips under `TrophyArt`); no vanilla pins added.
- **AT-LENS-DISC-PUMP** (🔴 #209) — when the ring hides under `DiscWhenBound`, the detection sweep
  keeps running; surfaces keep getting fresh blips; unequipping the map restores the ring with no
  dead frame. (Verify: the host's `Update` never stops — `SetVisible`/slot-hide only, never host
  deactivate.)
- **AT-LENS-DISC-CAMREL** (🔴 thesis) — rotating the camera while standing still sweeps all blips
  around the SBPR disc; no cardinal orientation; no N/E/S/W decoration. (Does NOT apply to the
  nomap-OFF vanilla overlay, which is deliberately north-up per §5.)
- **AT-LENS-DISC-NODRIFT** — tint/trophy/pips identical across surfaces (one projection); changing
  the aggro-colour rule changes all surfaces together. (Structurally guaranteed: one
  `SunstoneProjection.Project` feeds all three.)

---

## 7. Files

| File | Change |
|---|---|
| `Features/Cartography/ThreatMarkers.cs` | NEW — `ThreatBlip`, `IThreatMarkerProvider`, `ThreatMarkerRegistry`, shared `ThreatBlipArt` dot. |
| `Features/Sunstone/SunstoneHandoffPolicy.cs` | NEW — engine-free `MinimapHandoffMode`/`BlipStyle` enums + the decision table + the gated defaults (link-compiled into the test suite). |
| `Features/Sunstone/SunstoneProjection.cs` | NEW — the lifted `Character → ThreatBlip` producer + the disc's `SunstoneThreatProvider`. |
| `Features/Sunstone/SunstoneVanillaMinimapOverlay.cs` | NEW — the nomap-OFF custom overlay on the vanilla small minimap. |
| `Features/Sunstone/SunstoneLensHudOverlay.cs` | REFACTOR — ring consumes the projection; handoff routing; #209-safe ring-hide; provider registration; drives the vanilla overlay. |
| `Features/Cartography/MapSurface.cs` | the disc transient threat layer (`RebuildThreatLayer` / `SpawnThreatMarker` / `CounterRotateThreats`). |
| `Plugin.cs` | `LensMinimapHandoffMode` + `LensBlipStyle` live `Config.Bind`s. |
| `tests/SunstoneHandoffPolicyTests.cs` | NEW — 19 xUnit cases fencing the locked decision table + gated defaults. |

---

## 8. Clean/dirty routing

**CLEAN-SIDE (ADR-0001).** All SBPR-authored. Reads vanilla
`Minimap`/`Character`/`CharacterDrop`/`BaseAI`/`EnemyHud` only (base-game, fair to read+adapt) and
reproduces the vanilla world→minimap-pixel projection from the decomp. The uGUI surfaces are our
own (the MapSurface / SignPaintPanel idiom). No vanilla UI prefab cloned; no third-party mod code.

**Build:** `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` → 0/0
(`<TreatWarningsAsErrors>` ON). **PR:** against `main`; self-block `review-required` (engineer-ui
has no merge creds). Parent design card `t_3129842a` (merged + gated). /bug thread
`ticket-sunstone-minimap`.
