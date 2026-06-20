---
title: "Sunstone Lens → minimap handoff — the detection-overlay handoff (ACCEPTED — Daniel gated 2026-06-20)"
status: accepted
purpose: "Architect design decision for Daniel's 2026-06-20 idea: when ANY minimap is present, move the Sunstone Lens' hostile detection onto it instead of the camera-relative trophy-ring HUD. Daniel gated all 3 knobs 2026-06-20 (MinimapHandoffMode=DiscWhenBound, blips=dots+aggro-tint, and the UNIVERSAL rule: any minimap present — SBPR carry-disc OR vanilla minimap in nomap-OFF — gets the handoff; ring is the no-minimap fallback only). Grounds the SBPR-disc trigger (CartographyViewer.IsMinimapBound = nomap-ON + local map bound), the NEW vanilla-minimap trigger (nomap-OFF), the integration seam (a Cartography transient-threat-marker provider mirroring WorldPins.CollectInDiscPins; a parallel custom overlay on Minimap.instance for the vanilla path), the shared (Character → pos/tint/trophy/pips) projection both renderers consume, and the load-bearing invariants that must survive the move (AT-LENS-RING-CAMREL re-scoped to NoMap-worlds-only; the #209 dead-Update-pump pitfall). Every code line cited against `main` @ 5037af6 (SBPR) and the vanilla decomp. Card t_3129842a (design) → t_91e86951 (impl-spec graduate). Daniel gated the decision; he gates the merge."
owner: Daniel (design authority); Starbright (architect — capture + grounding)
supersedes_partial:
  - "docs/design/sunstone-lens-trophy-ring.md §0 NoMap⇒HUD rationale — becomes CONDITIONAL (the ring is the surface only when NO minimap is present at all)"
  - "docs/v3/planning/sunstone-lens-impl-spec.md §5 (Render surface under NoMap) — gains a 'when a minimap is present' carve-out"
graduated_to: "docs/v3/planning/sunstone-minimap-handoff-impl-spec.md (the buildable impl-spec; card t_91e86951)"
---

# Sunstone Lens → minimap handoff — the detection-overlay handoff

> **STATUS: ACCEPTED — Daniel gated all 3 knobs 2026-06-20.** This doc is now the
> locked design intent; the buildable spec graduated to
> [`../v3/planning/sunstone-minimap-handoff-impl-spec.md`](../v3/planning/sunstone-minimap-handoff-impl-spec.md)
> (card `t_91e86951`). 🟢 DECIDED rows are Daniel's locked calls; 🔵 GROUNDED rows
> are facts verified against `main` @ `5037af6` (SBPR) or the vanilla decomp
> (`~/valheim/sbpr-corpus/subsystems/Minimap.cs`).

Daniel's idea, verbatim (2026-06-20, v0.2.30-playtest, in-game):

> "or really design. Let's move the sunstone overlay to the minimap when one is
> available!"

Daniel's gate, verbatim (2026-06-20):

> "Ring only, take the architects preference, if the minimap is present for any
> reason it should have this behavior. You can extrapolate to nomap on"

### 🟢 The three knobs — DECIDED (Daniel 2026-06-20)

| Knob | 🟢 Decision | Source |
|---|---|---|
| **1 — replace vs supplement** | **`MinimapHandoffMode = DiscWhenBound`** (architect's proposed default). The ring is the **fallback-only** surface: it renders ONLY when no minimap is present at all. "Ring only" = the no-minimap fallback; "take the architect's preference" = `DiscWhenBound` (NOT the `RingOnly` enum value, which would make the feature inert). Enum stays live-tunable (`RingOnly`/`DiscWhenBound`/`Both`), default `DiscWhenBound`. | "take the architects preference" + §4 |
| **2 — blip representation** | **dots + aggro-tint** (architect's §3.3 lean). Every threat sits within the inner ~48 % of the disc where trophy art is ~48 px-from-centre small. Keep a live Config enum if cheap, default dots+tint. | §3.3 geometry |
| **3 — nomap-OFF case** | 🔴 **OVERRIDES the architect's "ring stays" lean.** UNIVERSAL rule: **ANY minimap present, for ANY reason, gets the handoff — including the VANILLA minimap in nomap-OFF.** *Minimap present (SBPR carry-disc OR vanilla) → detection on it; ring only when no minimap exists at all.* | "if the minimap is present for any reason it should have this behavior. You can extrapolate to nomap on" |

Knob 3 EXPANDS this doc beyond its original SBPR-disc-only scope. §1, §3.7 (NEW),
§5, and §6 below are revised to carry the universal rule and the vanilla-minimap
path; the impl-spec grounds the buildable shape.

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
Daniel's idea: when a minimap *is* available, move the detections onto it. Daniel's
gate broadened "a minimap" to **any** minimap — the SBPR carry-disc (nomap-ON) OR
the vanilla minimap (nomap-OFF) — so the ring is now the **no-minimap fallback
only.**

This doc grounds **how**, names the **invariants that must survive the move** (the
thesis re-scope, §6; the #209 pump guard, §4), and records Daniel's **3 gated
decisions** (the knob table above; §5).

---

## 1. The trigger — what "when one is available" keys on 🔵 GROUNDED

There is a single clean public read for "a disc is showing":

```
CartographyViewer.IsMinimapBound   (CartographyViewer.cs:257, static)
  → MapViewer.IsMinimapBound        (MapViewer.cs:113)
  → _disc.IsActive                  (MapSurface.cs IsActive)
```

🔴 **The scope nuance — now resolved into a THREE-WAY rule by Daniel's gate.** The
SBPR disc renders **only in nomap-ON** (`LocalMapController.cs:147-150` 🔵):

```csharp
bool shouldBindDisc = _provider != null
                      && Game.m_noMap                       // §5 — nomap-ON only
                      && LocalMap.IsImprinted(_provider!)    // a blank provider shows no disc
                      && LocalMap.ReadSurvey(_provider!) != null;
```

In **nomap-OFF** the vanilla global minimap owns the corner and the **SBPR disc
stands down** — there is a minimap, but **no SBPR surface to draw on.** So
`IsMinimapBound` is true exactly in *nomap-ON + a local map bound + imprinted.*
🔵 The two paths are **mutually exclusive by vanilla construction**: vanilla's
`Minimap.SetMapMode` forces `MapMode.None` whenever `Game.m_noMap` is true
(`Minimap.cs:963-965` 🔵), so the vanilla corner minimap (`m_smallRoot`) can never
be showing at the same time as the SBPR disc. The trigger is a clean cascade.

🟢 **DECIDED (Daniel 2026-06-20) — the UNIVERSAL rule.** *Any* minimap present, for
any reason, gets the handoff. The ring is the fallback only when **no** minimap
exists at all:

| Runtime config | Is there a minimap? | Lens detection surface | Trigger read |
|---|---|---|---|
| **nomap-ON**, no local map bound | No | **Ring HUD** (fallback) | `!IsMinimapBound && !vanillaMinimapShowing` |
| **nomap-ON**, local map bound + imprinted | Yes (the SBPR disc) | **SBPR disc** ← original card | `CartographyViewer.IsMinimapBound` 🔵 |
| **nomap-OFF** (vanilla minimap owns corner) | Yes (vanilla) | **Vanilla minimap** ← Knob-3 expansion (NEW path) | vanilla corner minimap showing (`Minimap.instance.m_mode == Small`, or `m_smallRoot.activeInHierarchy` 🔵) |

🟢 **DECIDED by grounding (not a Daniel knob): the SBPR-disc trigger is
`CartographyViewer.IsMinimapBound`.** It is the only public read that means
exactly "an SBPR disc is currently showing," and Sunstone consuming it introduces
no new Cartography state. The **vanilla-minimap trigger** (the new Knob-3 path) is
grounded in §3.7.

---

## 2. The shared detection projection — the seam both renderers consume

The detection **mechanic** is render-agnostic and stays exactly as-is. The
**per-hostile visual derivation** is what must be lifted out of the overlay so the
ring, the SBPR disc, and the vanilla-minimap overlay are **three consumers of one
mapping** (§3.7).

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
impl-spec's job (`docs/v3/planning/sunstone-minimap-handoff-impl-spec.md`).

**3.7 The vanilla-minimap path (nomap-OFF) — NEW, Knob-3 grounding.** 🔴 The SBPR
disc does not exist in nomap-OFF (§1), so the universal rule's nomap-OFF branch
must target the **vanilla `Minimap`** directly. Two ways to land a blip there; the
decomp decides between them:

- **Option (a) — `Minimap.AddPin`** (`Minimap.cs:1985` 🔵). The seam WorldPins
  already uses: `AddPin(pos, type, name, save:false, ...)` then override
  `PinData.m_icon` (`WorldPins.ProjectPin`, `WorldPins.cs:340-360` 🔵). The icon
  override is **stable** across rebuilds. **BUT** — 🔴 **vanilla's `UpdatePins()`
  hard-overwrites `pin.m_iconElement.color` to white/grey on EVERY refresh**
  (`Minimap.cs:1351-1352` 🔵: `color2 = ((pin.m_ownerID != 0L) ? grey : Color.white);
  pin.m_iconElement.color = color2;`). **A vanilla pin therefore CANNOT carry the
  per-aggro dynamic tint** Daniel locked in Knob 2 — the tint would be clobbered
  to white every frame. Option (a) can only ship monochrome type-pins (loses the
  🟡/🟠/🔴 Rune-of-Awareness colour language AND trophy/star identity).

- **Option (b) — a custom transient overlay on `Minimap.instance`.** Mirror the
  disc threat-layer: a parallel `RectTransform` layer parented under the vanilla
  pin root (`m_pinRootSmall` for the corner minimap, `m_pinRootLarge` for the modal
  — `Minimap.cs:140-142` 🔵), projecting each `ThreatBlip` via the same vanilla
  `WorldToMapPoint`/`MapPointToLocalGuiPos` math the pins use (`:1496-1503`,
  `:1457-1464` 🔵), and owning its own `Image.color` so the aggro tint survives.
  More work (it re-implements the disc renderer against vanilla's projection), but
  it honors Knob 2 faithfully.

🟢 **DECIDED (architect, forced by the decomp): the vanilla-minimap path is
Option (b) — a custom overlay.** Daniel locked Knob 2 = **dots + aggro-tint**, and
the decomp proves Option (a) structurally cannot carry tint (vanilla clobbers pin
colour every refresh). Shipping (a) would silently violate the gated Knob-2
decision. (b) is the only path that honors both gated knobs together. The
fidelity caveat the task flagged (§3.5 / task item 3) is therefore **resolved by
grounding, not a real tradeoff to surface**: the decomp removes the choice. The
impl-spec records this and the §3.5 marker-model limitation as the *reason* the
vanilla path can't reuse `AddPin`.

> **One projection, three consumers.** The ring, the SBPR disc, and the vanilla
> overlay all consume the single `SunstoneProjection` (§2) and the same
> `GatherHostiles` feed. The only thing that differs across the three surfaces is
> the world→screen projection (ring = camera-relative bearing; SBPR disc =
> `WorldToSurfacePx`; vanilla overlay = vanilla `WorldToMapPoint`). Tint, trophy,
> and pips derive identically — AT-LENS-DISC-NODRIFT extends to cover all three.

---

## 4. The load-bearer — replace/supplement as a live Config enum 🟢 DECIDED

🟢 **DECIDED (Daniel 2026-06-20): `MinimapHandoffMode = DiscWhenBound`** (the
architect's proposed default — "take the architect's preference"). When a minimap
is present, the camera-relative ring **hides** and the minimap surface shows the
threats; the ring renders only as the no-minimap fallback. The enum stays
**live-tunable** so Daniel can still flip to `Both`/`RingOnly` in-game:

```
SunstoneLens.MinimapHandoffMode  (Config enum, "SunstoneLens" section, live-tunable)
  • RingOnly      — ignore the minimap; ring always renders (the escape hatch)
  • DiscWhenBound — when a minimap is present: ring hides, minimap shows threats; else ring  ← 🟢 DEFAULT (Daniel)
  • Both          — ring AND minimap both render threats whenever a minimap is present
```

> ⚠️ **Naming note for the impl-spec.** The enum value name `DiscWhenBound` is now
> a slight misnomer — under the universal rule it means "hand off whenever ANY
> minimap is present," not just the SBPR disc. Keep the *value name* stable
> (`DiscWhenBound`) to avoid churning a Config key Daniel may already have bound,
> but the impl-spec's XML-doc must state the broadened meaning. (A future rename to
> `MinimapWhenPresent` is a cosmetic follow-up, not this card.)

This is the **banner-windsock pattern this codebase already uses twice** —
`ShowEmptyRing` and every `RingIcon*` knob are live `Config.Bind`s precisely so
"Daniel converges feel in one joined session without a rebuild"
(`SunstoneLensHudOverlay.cs:52` 🔵; `sunstone-lens-trophy-ring.md` §4 🔵). Daniel
locked the default; the enum stays live so the other two values are one flip away.

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
hides (the existing `SetVisible(false)` path); the active minimap surface (SBPR
disc OR vanilla overlay) returns the same blips.** The detection feed is
single-sourced from the live overlay pump regardless of which surface draws it.
**A minimap that renders threats while the ring is hidden still depends on the
ring overlay's `Update` being alive.** This is the single highest-risk line in the
whole card; the impl-spec locks it as an acceptance test (AT-LENS-DISC-PUMP).

---

## 5. The three knobs — 🟢 DECIDED (Daniel 2026-06-20)

1. 🟢 **Replace vs supplement → `MinimapHandoffMode = DiscWhenBound`** ("take the
   architect's preference"). Ring is the fallback-only surface; when a minimap is
   present the ring hides and the minimap shows threats. Enum stays live-tunable
   (§4). Daniel may still flip `Both`/`RingOnly` in-game.

2. 🟢 **Blip representation → dots + aggro-tint** (the architect's §3.3 lean,
   ratified). The geometry decides it: every threat sits within the inner ~48 % of
   the disc, where trophy art is ~48 px-from-centre small; dots + the 🟡/🟠/🔴
   aggro tint read cleanly there. *Keep a live Config enum (e.g. `BlipStyle`) if
   cheap so Daniel can compare `Dots` vs `Trophy` in-game, default `Dots`.* The
   rejected option (directional edge ticks) stays rejected by geometry — threats
   never reach the bezel.

3. 🟢 **The nomap-OFF case → DRAW ON THE VANILLA MINIMAP.** 🔴 Daniel **overrode**
   the architect's "ring stays" lean. The universal rule: *any minimap present,
   for any reason, gets the handoff — including the vanilla minimap in nomap-OFF.*
   The architect's original (a) "ring stays" and (c) "out of scope" are **rejected
   by Daniel's gate**; option (b) "draw on the vanilla minimap" is **selected**,
   and §3.7 grounds it as a **custom overlay** (not `AddPin`, which can't carry the
   locked aggro tint). The thesis concern the architect raised against (b) is
   **resolved by the §6 re-scope** — the vanilla minimap is north-up, but in
   nomap-OFF the player already has free cardinal orientation, so detection there
   leaks no Iron-Compass payoff.

---

## 6. The thesis guard, RE-SCOPED — AT-LENS-RING-CAMREL is NoMap-worlds-only 🟢

The ring's load-bearing invariant: **camera-relative, NEVER north-up** — "up" =
where the player is looking; the surface grants **no cardinal orientation** (that
stays the Iron Compass's exclusive earned payoff). Locked by
**AT-LENS-RING-CAMREL** (`sunstone-lens-trophy-ring.md:383-386` 🔵).

🟢 **DECIDED — the thesis only protects the NoMap experience (re-scope, not a
violation).** Daniel's universal rule (Knob 3) lands detection on the *north-up*
vanilla minimap in nomap-OFF. The merged doc treated that as a thesis VIOLATION
(granting cardinal orientation = the Iron Compass's exclusive payoff). The correct
reading — adopted here as the locked design intent:

> **In nomap-OFF the player already has a north-up vanilla minimap and full
> cardinal orientation independent of the Sunstone.** There is no Iron-Compass
> payoff to protect in that world, so detection on the vanilla minimap **leaks
> nothing the player didn't already have.** The thesis defends the *NoMap*
> exploration pillar — the worlds where cardinal orientation is the earned reward.
> nomap-OFF is not such a world.

🔴 **The vanilla minimap IS north-up — verified against decomp + SBPR source, not
assumed.** Vanilla `WorldToMapPoint` maps world→texture with **zero rotation**
(`mx = p.x/m_pixelSize + textureSize/2`, normalized — no yaw term); the map texture
is fixed north-up and only the player **chevron rotates** (`UpdatePlayerMarker` sets
`m_smallShipMarker.rotation`, the ship/player marker — the map itself never spins).
Pins parent under the **non-rotating** `m_pinRootSmall`. Crucially, **SBPR never
rotates the vanilla small map**: a full grep of `src/` finds zero code setting a
`localRotation` on `m_smallRoot` / `m_pinRootSmall`; the SBPR free-rotate behaviour
is exclusive to `MapSurface` (the nomap-ON carry-disc), which `LocalMapController`
gates on `Game.m_noMap` and which stands down entirely in nomap-OFF. So in nomap-OFF
the player sees the **stock vanilla north-up corner map** and genuinely has a
north-up read — which is exactly why detection on it is safe ONLY there.

> **Dispute resolution (in-card, 2026-06-20).** A mid-card comment proposed that
> SBPR ships a *free-rotating* small minimap in nomap-OFF (which would mean no
> cardinal orientation there, so the thesis would still apply). Grounded against the
> code, that is **false**: the free-rotate small minimap is the nomap-**ON**
> SBPR carry-disc (`MapSurface`, `Game.m_noMap`-gated); in nomap-OFF SBPR renders no
> disc and never touches the vanilla map's orientation. The earlier `cartography-v2`
> passage cited for "free-rotate" is itself the block stamped **SUPERSEDED/REJECTED**
> (the v0.2.22 player-centred free-rotate model Daniel rejected). The implemented
> reality on `main` is the north-up model above; the impl-spec builds to it. (Even
> if a future change ever did rotate the vanilla small map, the threat layer parents
> under `m_pinRootSmall`, so it inherits whatever frame vanilla uses for pins — the
> "threats are safe on the vanilla minimap" conclusion is invariant either way.)

🟢 **Formal re-scope:**

| Surface | World | Orientation | Thesis guard |
|---|---|---|---|
| Ring HUD | any (fallback) | camera/forward-up | **AT-LENS-RING-CAMREL** (NoMap-worlds-only) |
| SBPR carry-disc | nomap-ON | camera/forward-up (`ApplyFieldOrientation` camera-yaw) | **AT-LENS-DISC-CAMREL** (guards the NoMap surfaces) |
| Vanilla minimap | nomap-OFF | north-up (vanilla) | **EXEMPT** — cardinal orientation is already free; nothing to protect |

**AT-LENS-RING-CAMREL is re-scoped to "NoMap worlds only": the ring + SBPR disc
stay camera/forward-up in NoMap; the vanilla-minimap path in nomap-OFF is EXEMPT.**
This is a *scope* change, not a violation — the invariant is narrowed to the worlds
it was always meant to protect. AT-LENS-DISC-CAMREL still guards the NoMap surfaces
(ring + SBPR disc).

🔴 **The invariant the SBPR-disc path still carries (impl-spec locks it):** the
disc threat layer must key its rotation on the **same camera-yaw frame** as the
disc interior — it rides the rotating container and is *not* separately oriented to
player-body-heading or to north. If a future change re-bases disc rotation on
player-body-yaw instead of camera-yaw, AT-LENS-RING-CAMREL would silently break on
the disc surface. **AT-LENS-DISC-CAMREL** — standing still and rotating the camera
sweeps every threat blip around the SBPR disc; the disc grants no cardinal
orientation. (This test does NOT apply to the vanilla-minimap path, which is
deliberately north-up per the re-scope above.)

---

## 7. Clean/dirty routing + spec impact

**Clean/dirty: CLEAN-SIDE.** 🟢 All SBPR-authored (`Features/Sunstone/` +
`Features/Cartography/`). Reads vanilla `Minimap`/`Character`/`BaseAI`/
`CharacterDrop`/`EnemyHud` only — base-game, fair to read+adapt per ADR-0001. No
third-party mod code. Route to **`architect`** (this doc) → **engineer-ui** (impl).

**SpecCheck/manifest impact: NONE.** 🔵 Render-only; no recipe/piece/station/item
change. The Lens row in `SpecCheck.cs` is untouched (the card verified
`:160-176`); no new SpecCheck row.

**Docs that move (land WITH the impl-spec PR — card t_91e86951).** Daniel gated
all three knobs, so the dependent surgery below is no longer conditional on an
unanswered mode and lands alongside the impl-spec:

| Doc | Change |
|---|---|
| `docs/design/sunstone-lens-trophy-ring.md` | §0 NoMap⇒HUD rationale becomes **conditional** — the ring is the surface *only when no minimap is present at all*; add a pointer to this doc for the minimap branch. AT-LENS-RING-CAMREL gains a note that it is **re-scoped to NoMap-worlds-only** (§6 here). |
| `docs/v3/planning/sunstone-lens-impl-spec.md` | §5 gains a "when a minimap is present" carve-out pointing here (the universal rule). |
| `docs/design/map-provider-model.md` | Note the disc gains its **first non-cartography consumer** + the new transient-threat-marker provider seam (§6 nomap-OFF split also gains the vanilla-minimap Sunstone overlay). |
| **NEW** `docs/v3/planning/sunstone-minimap-handoff-impl-spec.md` | The buildable spec: the `SunstoneProjection`/`ThreatBlip` lift, the Cartography `IThreatMarkerProvider` seam (SBPR disc), the NEW vanilla-`Minimap` overlay path (nomap-OFF), the `MinimapHandoffMode` enum (default `DiscWhenBound`) + blip enum (default dots+tint), the #209 dead-Update-pump guard, and the AT-LENS-DISC-* tests. |

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
- **AT-LENS-DISC-NOMAP-OFF** — in nomap-OFF (no SBPR disc; vanilla minimap owns
  the corner), Lens detection **renders on the vanilla minimap** (the custom
  overlay, §3.7) — NOT "ring stays." Hostiles within 30 m appear as dots +
  aggro-tint on the vanilla minimap; the ring is hidden under `DiscWhenBound`.
- **AT-LENS-DISC-NODRIFT** — the tint/trophy/pips a hostile shows on ANY surface
  (ring, SBPR disc, vanilla overlay) are byte-identical (all consume the single
  `SunstoneProjection`); changing the aggro-colour rule changes all three surfaces
  together.

---

## 9. The gate — 🟢 ANSWERED (Daniel 2026-06-20)

1. **`MinimapHandoffMode` default** → 🟢 **`DiscWhenBound`** ("take the architect's
   preference"). Enum stays live-tunable.
2. **Blip representation** → 🟢 **dots + aggro-tint** (per §3.3 geometry). Live
   enum if cheap.
3. **nomap-OFF behaviour** → 🟢 **draw on the vanilla minimap** (Daniel overrode
   the architect's "ring stays" lean → the universal "any minimap present" rule).

This doc is now **accepted**. It graduated to
[`../v3/planning/sunstone-minimap-handoff-impl-spec.md`](../v3/planning/sunstone-minimap-handoff-impl-spec.md)
(the buildable spec; card `t_91e86951`), and the impl card is cut for
**engineer-ui** from there.

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
- Reported via /bug thread `ticket-sunstone-minimap`. Design card `t_3129842a`
  (PR #214 merged); impl-spec graduate card `t_91e86951`.
