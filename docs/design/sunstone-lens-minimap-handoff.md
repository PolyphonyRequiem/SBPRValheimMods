---
title: "Sunstone Lens → minimap disc — the detection-overlay handoff (design decision, awaiting Daniel's gate)"
status: proposed
purpose: "Architect design decision for Daniel's 2026-06-20 idea: when a local-map minimap disc is available, move the Sunstone Lens' hostile detection onto the disc instead of the camera-relative trophy-ring HUD. Grounds the trigger (CartographyViewer.IsMinimapBound = nomap-ON + local map bound), the integration seam (a new Cartography transient-threat-marker provider mirroring WorldPins.CollectInDiscPins), the shared (Character → pos/tint/trophy/pips) projection both renderers consume, and the load-bearing invariants that must survive the move (AT-LENS-RING-CAMREL camera-relative thesis; the #209 dead-Update-pump pitfall). The replace-vs-supplement knob is converted from a build-blocking fork into a live-tunable Config enum (the banner-windsock pattern). Every code line cited against `main` @ 5037af6. Card t_3129842a. Daniel gates the decision AND the merge."
owner: Daniel (design authority); Starbright (architect — capture + grounding)
supersedes_partial:
  - "docs/design/sunstone-lens-trophy-ring.md §0 NoMap⇒HUD rationale — becomes CONDITIONAL (the ring is the surface only when no disc is bound)"
  - "docs/v3/planning/sunstone-lens-impl-spec.md §5 (Render surface under NoMap) — gains a 'when a disc is bound' carve-out"
---

# Sunstone Lens → minimap disc — the detection-overlay handoff

> **STATUS: PROPOSED — Daniel gates this before it becomes an impl-spec.** The
> knobs Daniel is exploring (replace vs supplement, blip representation, the
> nomap-OFF case) are marked 🟡 OPEN and are NOT pre-decided here. This doc's job
> is to make every one of those knobs *cheap to answer in-game* — the
> load-bearer (replace vs supplement) is converted from a structural fork into a
> single live-tunable Config enum (§4), so Daniel converges feel on a joined
> client without a rebuild, exactly the banner-windsock pattern the Lens ring and
> the cartography disc already use. 🟢 DECIDED rows are Daniel's locked calls;
> 🔵 GROUNDED rows are facts verified against `main` @ `5037af6`.

Daniel's idea, verbatim (2026-06-20, v0.2.30-playtest, in-game):

> "or really design. Let's move the sunstone overlay to the minimap when one is
> available!"

---

## 0. The reframe — why this premise flipped

The Sunstone Lens renders hostile detection as a **camera-relative trophy-ring
HUD**, deliberately NOT minimap pins. The reason is documented and load-bearing:
the SB server runs **NoMap by default** (`NoMapEnforcer`), so historically there
was *no map surface to pin to*
(`docs/v3/planning/sunstone-lens-impl-spec.md:240-261` §5;
`SunstoneLens.cs:16-19` comment 🔵).

That premise is now **conditionally false.** The v2 cartography tier shipped a
**carry-state minimap disc** — a player-centred, rotate-to-heading map surface
bound to an equipped local map (`docs/design/map-provider-model.md` §2;
`MapSurface.cs` 🔵). When that disc is up, there *is* a map surface in the corner.
Daniel's idea: when the disc *is* available, move the detections onto it.

This doc grounds **how**, names the **invariants that must survive the move**,
and hands Daniel the **3 knobs** that are genuinely his call.

---

## 1. The trigger — what "when one is available" keys on 🔵 GROUNDED

There is a single clean public read for "a disc is showing":

```
CartographyViewer.IsMinimapBound   (CartographyViewer.cs:257, static)
  → MapViewer.IsMinimapBound        (MapViewer.cs:113)
  → _disc.IsActive                  (MapSurface.cs IsActive)
```

🔴 **The scope nuance that decides the nomap-OFF knob.** The SBPR disc renders
**only in nomap-ON** (`LocalMapController.cs:147-150` 🔵):

```csharp
bool shouldBindDisc = _provider != null
                      && Game.m_noMap                       // §5 — nomap-ON only
                      && LocalMap.IsImprinted(_provider!)    // a blank provider shows no disc
                      && LocalMap.ReadSurvey(_provider!) != null;
```

In **nomap-OFF** the vanilla global minimap owns the corner and the **SBPR disc
stands down** — there is a minimap, but **no SBPR surface to draw on**. So
`IsMinimapBound` is true exactly in *nomap-ON + a local map bound + imprinted* —
which is **precisely the configuration where the old "no surface" rationale was
true**. The handoff lands the detections on a surface that only exists in the
config that originally lacked one. Clean fit.

| Runtime config | Is there a minimap? | Is there an **SBPR** surface? | Lens detection surface |
|---|---|---|---|
| **nomap-ON**, no local map bound | No | No | **Ring HUD** (today's behaviour — unchanged) |
| **nomap-ON**, local map bound + imprinted | Yes (the SBPR disc) | **Yes (the disc)** | **Disc** ← this card, gated by `IsMinimapBound` |
| **nomap-OFF** (vanilla minimap owns corner) | Yes (vanilla) | No | 🟡 **OPEN — Daniel decides (§5 knob 3)** |

🟢 **DECIDED by grounding (not a Daniel knob): the trigger is
`CartographyViewer.IsMinimapBound`.** It is the only public read that means
exactly "an SBPR disc is currently showing," and Sunstone consuming it introduces
no new Cartography state. The nomap-OFF *behaviour* is still Daniel's call (§5).

---

## 2. The shared detection projection — the seam both renderers consume

The detection **mechanic** is render-agnostic and stays exactly as-is. The
**per-hostile visual derivation** is what must be lifted out of the overlay so
the ring and the disc are two consumers of one mapping.

🔵 **Already shared (public static, render-agnostic):**
`SunstoneLens.GatherHostiles(player, radius, results)` (`SunstoneLens.cs:386`)
returns a `List<Character>` — raw hostile world positions, no render coupling.

🔴 **Currently private to the overlay** (must be lifted to a shared/internal
projection so neither renderer forks the mapping):

| Datum | Source helper (current) | Line |
|---|---|---|
| aggro tint (🟡/🟠/🔴 Rune-of-Awareness) | `AggroTint(c, player)` | `SunstoneLensHudOverlay.cs:584` |
| trophy sprite | `ResolveTrophySprite(c)` | `:506` |
| star sprite | `StarSprite()` | `:547` |
| pip count | `Mathf.Max(0, c.GetLevel() - 1)` | `:331` |
| generic threat glyph (trophy-less) | `ThreatGlyph()` | `:609` |

🟢 **DECIDED (architecture, not a Daniel knob): introduce a single
`SunstoneProjection` that maps `Character → ThreatBlip { Vector3 WorldPos, Color
Tint, Sprite? Trophy, int Stars }`.** The ring's `RenderRing()` and the disc
renderer become two callers of it. This is the *zero-drift* requirement: if the
ring and the disc derived tint or trophy independently, a future tweak to the
aggro-colour rule would silently desync the two surfaces. One projection, two
consumers — the same anti-drift rule `WorldPins.ResolveLabel` already enforces
for the minimap-vs-viewer label sites (`WorldPins.cs:413-419` 🔵).

> The `ThreatBlip.WorldPos` is the creature's live `c.transform.position`. The
> ring discards it (it only needs the *bearing*); the disc uses it directly
> (§3). Carrying the full world position in the shared model costs nothing and
> keeps the ring free to keep encoding distance as icon size.

---

## 3. The integration seam — how a blip lands on the disc 🔵 GROUNDED

The disc already ingests live world-positioned markers each rebuild. The Sunstone
feed mirrors it exactly.

**3.1 Projection exists.** `MapSurface.WorldToSurfacePx(world, survey)`
(`:481-494`) maps a world point to disc-px about `FrameCenter()` = the live
player position (`:346-354`, `PlayerCentred=true`). A blip at
`WorldToSurfacePx(blip.WorldPos, survey)` lands on the correct terrain cell.

**3.2 Rotation is free and PRESERVES the thesis (§6).** The disc interior rotates
every frame by **camera yaw** — `rotZ = MapRotationSign * cam.transform.eulerAngles.y`
(`ApplyFieldOrientation`, `:963-965` 🔵). This is the *same camera-relative
reference frame the ring uses* (`cam.transform.forward`,
`SunstoneLensHudOverlay.cs:318` 🔵). Forward = screen-up on both surfaces. A blip
placed in disc-space rotates with heading automatically; it inherits the
no-north invariant for free (§6).

**3.3 The geometry — quantified (this is new, the card flagged it as
"confirm").** 🔵

- Detection radius: **30 m** (`SunstoneLens.DefaultDetectRadius`, `:91`).
- Disc view span: **125 m edge-to-edge** → ~**62.5 m radius**
  (`MapViewer.DiscViewSpanMeters`, `:46`).
- Disc pixel size: **200 px** (`DiscTargetPx`, `:36`) → ~100 px radius.

⇒ **Every detected hostile falls within the inner ~48 % of the disc** (30/62.5),
i.e. inside the central ~96 px diameter. Threats never approach the bezel. Two
consequences the representation knob (§5) must weigh:

1. There is **plenty of unused outer disc** — no edge-clipping pressure; the §3.1
   clip-to-visible-circle guard (`MapSurface.cs:628`) will essentially never
   fire for a threat blip.
2. **Trophy art at true scale will be small.** A 30 m-distant hostile sits ~48 px
   from centre on a 100 px-radius disc; a trophy sprite there competes with the
   cartography texture and the player chevron. This is the strongest argument for
   **dots + aggro-tint** over trophy art on the disc (§5 knob 2) — but it's
   Daniel's eyes, so it's a knob, made cheap by §4.

**3.4 Cadence.** The disc overlay rebuilds per `Render` (~0.25 s,
`MapSurface.cs:169` 🔵); rotation is per-frame. The ring polls detection on a
0.5 s sweep (`DefaultDetectInterval`, `:92` 🔵). 0.25–0.5 s is acceptable for
threat blips (a hostile crosses ~48 px in seconds at walk speed). Architect note:
do **not** add a faster path for v1 — match the existing rebuild cadence; revisit
only if Daniel reports lag in playtest.

**3.5 The marker model can't carry threat data — a parallel layer is required.**
🔴 The disc's pin path resolves a sprite by `pin.Type` from `MarkerTypes`
(`SpawnPinMarker`/`ResolvePinSprite`, `MapSurface.cs:738-748` 🔵), and `SurveyPin`
carries only `{Name, Type:int, Pos, Checked, OwnerId}` (`SurveyData.cs:35-41` 🔵)
— **no slot for trophy sprite, aggro tint, or star pips.** So threats can NOT
reuse the `SurveyPin`/`WorldPins` path. A **separate transient "threat layer"** on
the disc is required, parallel to the survey-pin overlay, fed by `ThreatBlip`
(§2), cleared and rebuilt each `RebuildOverlay`.

**3.6 The coupling direction — lowest-coupling, grounded.** 🔴 **Zero
Sunstone→Cartography coupling exists today** (only a prose comment in
`SunstoneLens.cs:16-19`). This card introduces the **first** dependency; there is
**no zero-API option** — a new public Cartography seam is required. The
lowest-coupling shape mirrors `WorldPins`:

> **Cartography exposes a transient-threat-marker provider seam; Sunstone
> registers a provider into it.** `MapSurface.RebuildOverlay` already calls *out*
> to `WorldPins.CollectInDiscPins(origin, radius)` each rebuild (`:606` 🔵) —
> Cartography pulling from a registered provider, not the feature reaching into
> `MapSurface` internals. The Sunstone threat feed follows the identical
> inversion: **Cartography owns the disc; it asks a registered provider "any
> threat blips in range?" each rebuild.** Sunstone never touches `MapSurface`,
> `WorldToSurfacePx`, or the overlay layer directly.

This keeps the dependency arrow **Sunstone → (registers into) → Cartography
seam**, with Cartography unaware of *what* Sunstone is (it sees an
`IThreatMarkerProvider`, not a Lens). The buildable shape of this seam is the
impl-spec's job (`docs/v3/planning/sunstone-minimap-handoff-impl-spec.md`,
proposed alongside this doc).

---

## 4. The load-bearer, de-risked — replace/supplement as a live Config enum

🟡 **OPEN — Daniel's call.** When `IsMinimapBound`, does the camera-relative ring
**hide** (the disc takes over) or do **both** render?

**Architect move: this does NOT need to be answered before the build.** Make it a
single live-tunable Config enum, default to the architect's reversible lean, and
let Daniel flip it on a joined client:

```
SunstoneLens.MinimapHandoffMode  (Config enum, "SunstoneLens" section, live-tunable)
  • RingOnly      — ignore the disc; ring always renders (today's behaviour; the escape hatch)
  • DiscWhenBound — when IsMinimapBound: ring hides, disc shows threats; else ring  ← proposed DEFAULT
  • Both          — ring AND disc both render threats whenever the disc is bound
```

This is the **banner-windsock pattern this codebase already uses twice** —
`ShowEmptyRing` and every `RingIcon*` knob are live `Config.Bind`s precisely so
"Daniel converges feel in one joined session without a rebuild"
(`SunstoneLensHudOverlay.cs:52` 🔵; `sunstone-lens-trophy-ring.md` §4 🔵). The
replace/supplement question is *exactly* a feel question; it belongs on the same
windsock.

> 🟢 **Proposed default: `DiscWhenBound`** (architect's reversible lean — NOT
> Daniel's locked call). Rationale: Daniel's "**move** the overlay to the minimap"
> phrasing leans replace, and a single threat surface avoids the double-encoding of
> the same hostiles in two places on screen. But the ring carries legibility the
> ~200 px disc may not (full trophy art, the faint `ShowEmptyRing` solar
> affordance), so `Both` and `RingOnly` are one config flip away if `DiscWhenBound`
> reads worse in-game. **Defaulting the enum is reversible; hard-coding the fork is
> not.** Daniel ratifies the default at playtest.

🔴 **The pitfall this enum must NOT step on (#209 — load-bearing).** "Ring hides"
must mean **suppress the ring's visuals while keeping the overlay's `Update()`
pump alive**, NOT deactivate the overlay. The Lens detection sweep is driven
*only* from `SunstoneLensHudOverlay.Update()` (`:218-228` 🔵), and the codebase
**already shipped a fix (PR #209, t_d5949685)** for exactly the bug where
deactivating the host `GameObject` froze the `Update` pump dead → the overlay
rendered nothing forever. The fix toggles a `_content` child, never the host
(`SetVisible`, `:201-216` 🔵).

⇒ **DiscWhenBound is implemented as: pump keeps running; `GatherHostiles` keeps
sweeping; the projection keeps producing `ThreatBlip`s; the RING's `_content`
hides (the existing `SetVisible(false)` path); the disc provider returns the same
blips.** The detection feed is single-sourced from the live overlay pump
regardless of which surface draws it. **A disc that renders threats while the ring
is hidden still depends on the ring overlay's `Update` being alive.** This is the
single highest-risk line in the whole card; the impl-spec locks it as an
acceptance test (AT-LENS-DISC-PUMP).

---

## 5. The three knobs that are genuinely Daniel's

1. 🟡 **Replace vs supplement** → resolved structurally as the `MinimapHandoffMode`
   enum (§4); Daniel picks the default at playtest. *No build dependency on his
   answer.*

2. 🟡 **Blip representation on the disc.** Options, with the geometry (§3.3)
   weighing in:
   - **Dots + aggro-tint** (cheap, legible at the disc's small threat-zone scale;
     architect lean given §3.3 — trophy art is ~48 px-from-centre small).
   - **Trophy art + tint + pips** at world position (maximum information; reuses
     the §2 projection wholesale; risk: illegible at disc scale).
   - **Directional edge ticks** (rejected by geometry — threats never reach the
     edge; §3.3).
   *Daniel's "what does a blip look like" answer decides. Make the blip a live
   Config enum too if he wants to compare in-game (cheap, same windsock).*

3. 🟡 **The nomap-OFF case.** With the SBPR disc standing down (§1), the options
   are: (a) ring stays (the surface is still the ring there — clean, consistent
   with "the disc is the SBPR surface"); (b) draw threats on the *vanilla*
   minimap via `Minimap.AddPin` (new coupling to vanilla minimap, loses
   trophy/tint/pips since vanilla pins resolve sprite by type — same limitation as
   §3.5; and re-introduces the north-up surface the thesis forbids, §6); (c) out
   of scope for this card. *Architect lean: (a) — ring stays in nomap-OFF; the
   disc handoff is a nomap-ON feature, matching where the disc exists. (b) fights
   the thesis (§6) and the marker model (§3.5). Daniel confirms.*

---

## 6. The thesis guard restated for the disc — AT-LENS-RING-CAMREL survives 🟢

The ring's load-bearing invariant: **camera-relative, NEVER north-up** — "up" =
where the player is looking; the ring grants **no cardinal orientation** (that
stays the Iron Compass's exclusive earned payoff). Locked by
**AT-LENS-RING-CAMREL** (`sunstone-lens-trophy-ring.md:383-386` 🔵).

🟢 **The disc PRESERVES this — verified, not assumed.** The disc rotates to
**camera yaw** (`ApplyFieldOrientation`, `:963-965`: `cam.transform.eulerAngles.y`
🔵), is forward-up not north-up (`cartography-v2.md:263-276` 🔵 — "rotate-to-heading,
no north-up lock anywhere"; AT-LMAP-TC-5), and adds **no N/E/S/W decoration**.
Moving threats onto it therefore does NOT hand the player cardinal orientation —
the disc shows "where is the threat relative to where I'm facing," exactly as the
ring does. **The Iron Compass's reason to exist is intact.**

🔴 **The invariant this introduces for the disc path (impl-spec locks it):** the
threat layer must key its rotation on the **same camera-yaw frame** as the disc
interior — it rides the rotating container and is *not* separately oriented to
player-body-heading or to north. If a future change re-bases disc rotation on
player-body-yaw instead of camera-yaw, AT-LENS-RING-CAMREL would silently break on
the disc surface. New acceptance test: **AT-LENS-DISC-CAMREL** — standing still
and rotating the camera sweeps every threat blip around the disc; the disc grants
no cardinal orientation.

---

## 7. Clean/dirty routing + spec impact

**Clean/dirty: CLEAN-SIDE.** 🟢 All SBPR-authored (`Features/Sunstone/` +
`Features/Cartography/`). Reads vanilla `Minimap`/`Character`/`BaseAI`/
`CharacterDrop`/`EnemyHud` only — base-game, fair to read+adapt per ADR-0001. No
third-party mod code. Route to **`architect`** (this doc) → **engineer-ui** (impl).

**SpecCheck/manifest impact: NONE.** 🔵 Render-only; no recipe/piece/station/item
change. The Lens row in `SpecCheck.cs` is untouched (the card verified
`:160-176`); no new SpecCheck row.

**Docs that move (deferred to the post-gate impl-spec PR — NOT this PR).** This
PR adds only this standalone design doc; the dependent surgery below depends on
which `MinimapHandoffMode` Daniel ratifies (e.g. `RingOnly` changes nothing in the
first two rows), so it lands with the impl-spec once the mode is locked — the same
standalone-then-graduate path `forge-masters-trinket.md` and `travellers-cache.md`
follow:

| Doc | Change |
|---|---|
| `docs/design/sunstone-lens-trophy-ring.md` | §0 NoMap⇒HUD rationale becomes **conditional** — the ring is the surface *when no disc is bound*; add a pointer to this doc for the disc branch. AT-LENS-RING-CAMREL gains a sibling note that the disc surface preserves it. |
| `docs/v3/planning/sunstone-lens-impl-spec.md` | §5 gains a "when a local-map disc is bound" carve-out pointing here. |
| `docs/design/map-provider-model.md` | Note the disc gains its **first non-cartography consumer** + the new transient-threat-marker provider seam. |
| **NEW** `docs/v3/planning/sunstone-minimap-handoff-impl-spec.md` | The buildable spec: the `SunstoneProjection`/`ThreatBlip` lift, the Cartography `IThreatMarkerProvider` seam, the `MinimapHandoffMode` enum, the disc threat-layer renderer, and AT-LENS-DISC-* acceptance tests. |

---

## 8. Proposed acceptance tests (impl-spec will formalize)

- **AT-LENS-DISC-HANDOFF** — with nomap-ON + a local map bound (disc showing) +
  the Lens worn & charged, hostiles within 30 m render as blips **on the disc**;
  per `MinimapHandoffMode`, the ring hides (`DiscWhenBound`), both show (`Both`),
  or only the ring shows (`RingOnly`).
- **AT-LENS-DISC-PUMP** (🔴 #209 guard) — when the ring hides under
  `DiscWhenBound`, the Lens detection sweep **keeps running** (the overlay's
  `Update` pump is never frozen); the disc keeps receiving fresh blips. Toggling
  the disc off (unequip the local map) restores the ring with no dead frame.
- **AT-LENS-DISC-CAMREL** (🔴 thesis guard) — standing still and rotating the
  camera sweeps every disc threat blip around the disc; the disc grants no
  cardinal orientation; no N/E/S/W decoration appears.
- **AT-LENS-DISC-NOMAP-OFF** — in nomap-OFF (no SBPR disc), Lens detection behaves
  per Daniel's §5-knob-3 decision (architect-proposed default: ring stays).
- **AT-LENS-DISC-NODRIFT** — the tint/trophy/pips a hostile shows on the disc are
  byte-identical to what it would show on the ring (both consume the single
  `SunstoneProjection`); changing the aggro-colour rule changes both surfaces
  together.

---

## 9. Open questions for Daniel (the gate)

1. **`MinimapHandoffMode` default** — `DiscWhenBound` (proposed), `Both`, or
   `RingOnly`? (Reversible; picks the default for the live enum.)
2. **Blip representation** — dots+tint (proposed, per §3.3 geometry), trophy
   art+tint+pips, or make it a live enum to compare in-game?
3. **nomap-OFF behaviour** — ring stays (proposed), draw on vanilla minimap, or
   out of scope?

Nothing here is an impl card yet. On Daniel's answers, this graduates to
`sunstone-minimap-handoff-impl-spec.md` and the impl card is cut for
**engineer-ui**.

---

## Links

- Sunstone design: [`sunstone-lens-trophy-ring.md`](sunstone-lens-trophy-ring.md);
  idea note [`swamp-detection-item.md`](swamp-detection-item.md);
  impl-spec [`../v3/planning/sunstone-lens-impl-spec.md`](../v3/planning/sunstone-lens-impl-spec.md).
- Minimap disc + provider binding: [`map-provider-model.md`](map-provider-model.md),
  [`cartography-v2.md`](cartography-v2.md); code `MapViewer.cs`, `MapSurface.cs`,
  `LocalMapController.cs`, `CartographyViewer.cs`.
- WorldPins precedent (the seam to mirror): `Features/MarkerSigns/WorldPins.cs`
  (`CollectInDiscPins`).
- Sibling cartography work in flight: `t_642687dd` (disc margin),
  `t_423f5bd7` (modal chevron).
- Reported via /bug thread `ticket-sunstone-minimap`. Card `t_3129842a`.
