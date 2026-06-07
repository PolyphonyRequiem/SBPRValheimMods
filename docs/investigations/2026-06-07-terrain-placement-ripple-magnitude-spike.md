---
title: "Spike: scale the placement ground-ripple with terrain-op magnitude"
status: historical
last_updated: 2026-06-07
implemented_by: "src/SBPR.Trailborne/Features/Trailblazing/PlacementMarkerRadiusPatch.cs (Request 1, 2026-06-07)"
---

# Spike: scale the placement ground-ripple with terrain-op magnitude (Request 1)

> **IMPLEMENTED 2026-06-07** — `PlacementMarkerRadiusPatch` (postfix on
> `Player.UpdatePlacementGhost`). Two deviations from the recommendation below, both
> driven by a `MetadataLoadContext` probe of the shipped `assembly_valheim.dll`
> (clean-room: member names/types only, no decompiled source read):
>
> 1. **Radius source = the op-registration table, not a runtime `TerrainModifier`
>    max-of-enabled-radii mirror.** The probe confirmed `TerrainModifier.m_raise` /
>    `m_raiseRadius` (referenced in "The magnitude source" below) **do NOT exist on
>    this build** — the real radius fields are `m_levelRadius` / `m_smoothRadius` /
>    `m_paintRadius` only. More importantly, a narrow replant op leaves
>    `m_levelRadius`/`m_smoothRadius` at vanilla stock (~2 m) while its *intended*
>    footprint is `m_paintRadius` = 1.5 m, so a "max of enabled radii" read would
>    OVERSHOOT. The patch instead reads the intended width from
>    `Trailblazing.TryGetSpadeOpRadius` — the same `variants` table that sets the
>    radii at registration — which is exact and self-maintaining.
> 2. **Gotcha 1 is moot for our ops:** Daniel observed the (wrongly-sized) ripple on
>    these exact path/replant ops in-game, so the marker demonstrably renders for
>    them; they do not set `m_groundOnly`. The patch still no-ops safely if the marker
>    is hidden (it only acts on an active marker).
>
> Everything else (postfix surface, `m_placementMarkerInstance`/`m_placementGhost`
> reflection, `CircleProjector.m_radius`/`m_nrOfSegments`, the shared-instance reset of
> Gotcha 2) shipped as recommended. Original research preserved below.

- **Date:** 2026-06-07
- **Investigator:** Starbright (with Daniel)
- **Trigger:** Daniel, in-game: the rippling ground circle shown while aiming to
  place a path / replant op is a FIXED size; it should scale with the op's
  effect magnitude (radius), so a 5 m path previews a 5 m ripple, not a 2 m one.
- **Status:** Root cause + fix surface located. SMALL, isolated, patchable.
  No production code written in this spike (research only).

## TL;DR — what the ripple actually is

The "rippling waves on the ground" while aiming a placement is Valheim's
**placement marker**, an instance of `Player.m_placeMarker` held in
`Player.m_placementMarkerInstance`. The animated ring is a **`CircleProjector`**
component (decomp `assembly_valheim.decompiled.cs:29653`) whose `Update()` lays
out `m_nrOfSegments` projected prefab segments on a circle of radius `m_radius`
(default **5 m**, line 29655).

The marker is instantiated and positioned in `Player.UpdatePlacementGhost`
(decomp ~L18784):

```csharp
if (m_placementMarkerInstance == null)
    m_placementMarkerInstance = Object.Instantiate(m_placeMarker, point, Quaternion.identity);
m_placementMarkerInstance.SetActive(true);
m_placementMarkerInstance.transform.position = point;
m_placementMarkerInstance.transform.rotation = Quaternion.LookRotation(normal, ...);
// ...nothing here ever sets the CircleProjector.m_radius from the op.
```

**The bug, precisely:** the marker radius is whatever `m_placeMarker`'s
`CircleProjector.m_radius` ships as — a constant. Vanilla NEVER reads the piece's
TerrainOp radius to size the marker, because vanilla terrain tools (Hoe,
Cultivator) all operate at ~2 m and never needed it. Our Trailborne spade ops
register multiple width variants (paths 1.5 / 3 / 5 m, replant likewise) by
setting different `TerrainModifier.m_levelRadius` / `m_smoothRadius` /
`m_paintRadius` — but the shared placement marker keeps its constant radius, so a
wide op previews a narrow ripple. Cosmetic-only (the actual op still applies at
its real radius), but it misleads the player about the affected area.

## The magnitude source (what to scale TO)

`TerrainModifier` (a.k.a. the op on the piece; decomp class `TerrainComp` applies
it, fields on the modifier) carries the radii:

- `m_levelRadius` (default `2f`, decomp L123820)
- `m_smoothRadius` (default `2f`, L123827)
- `m_paintRadius` (default `2f`, L123838)
- `m_raiseRadius` (raise ops)

Vanilla already has the exact "largest active radius" helper we want —
`TerrainModifier.GetRadius()` style logic at L123917-123927 takes the max of the
enabled op radii:

```csharp
float num = 0f;
if (m_level   && m_levelRadius  > num) num = m_levelRadius;
if (m_smooth  && m_smoothRadius > num) num = m_smoothRadius;
if (m_paintCleared && m_paintRadius > num) num = m_paintRadius;
// (raise similar)
```

So the value to feed the marker is "the max enabled op radius on the ghost's
TerrainModifier."

## Fix surface (recommended)

**Harmony postfix on `Player.UpdatePlacementGhost`** (the method that already
positions `m_placementMarkerInstance` every frame). After vanilla runs:

1. Bail unless the marker instance is active AND the current ghost
   (`m_placementGhost`) carries a `TerrainModifier` that's one of OURS
   (gate on our piece name / an SBPR tag, so we never touch vanilla placements —
   server-gated-by-sanity doctrine, but this is client-cosmetic so gate on the
   piece identity instead).
2. Read the max enabled radius off that `TerrainModifier` (mirror the
   L123917 logic).
3. Set the marker's `CircleProjector.m_radius` to that value (and optionally bump
   `m_nrOfSegments` proportionally so the ring doesn't look sparse when widened —
   e.g. `segments = Mathf.Max(20, Mathf.RoundToInt(radius * 8))`).

```csharp
[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
static class PlacementMarkerRadiusPatch
{
    [HarmonyPostfix]
    static void Postfix(Player __instance)
    {
        var marker = /* reflect Player.m_placementMarkerInstance */;
        var ghost  = /* reflect Player.m_placementGhost */;
        if (marker == null || ghost == null || !marker.activeSelf) return;

        var tm = ghost.GetComponent<TerrainModifier>();
        if (tm == null || !IsOurPiece(ghost)) return;   // ours only

        float r = MaxEnabledRadius(tm);                 // mirror L123917 logic
        var proj = marker.GetComponentInChildren<CircleProjector>();
        if (proj != null && r > 0f)
        {
            proj.m_radius = r;
            proj.m_nrOfSegments = Mathf.Max(20, Mathf.RoundToInt(r * 8f));
        }
    }
}
```

Both `m_placementMarkerInstance` and `m_placementGhost` are **private** Player
fields → use `AccessTools.Field(...)` reflection (verify the names against the
live `assembly_valheim.dll` via the probe pattern before shipping; private-field
name drift silently no-ops a reflection patch — see valheim-mod-development
"verify reflection targets").

## Risk / scope

- **Low.** One client-only Harmony postfix on an existing per-frame method; no
  ZDO, no server state, no new prefab. Pure cosmetic accuracy.
- **Clean-room safe:** all surfaces (`CircleProjector.m_radius/m_nrOfSegments`,
  `TerrainModifier.m_*Radius`, `Player.m_placementMarkerInstance/m_placementGhost`)
  are public-or-reflected vanilla members, verified against the metadata, not
  copied source.
- **Gotcha 1 — ground-only ops hide the marker.** `UpdatePlacementGhost`
  (~L18790) calls `m_placementMarkerInstance.SetActive(false)` when
  `component.m_groundOnly || m_groundPiece || m_cultivatedGroundOnly`. If our
  spade path/replant pieces set any of those, the marker is HIDDEN and there's no
  ripple to scale — confirm our pieces don't, or the request is moot for them.
  **This is the first thing to check when implementing.**
- **Gotcha 2 — shared instance.** `m_placementMarkerInstance` is reused across
  ALL placements. Our postfix sets `m_radius` each frame while OUR ghost is
  active; when the player switches back to a vanilla piece our gate
  (`IsOurPiece`) stops firing, but the projector keeps our last radius until
  vanilla's own marker logic runs. Vanilla never writes `m_radius` per-frame, so
  we should RESET it to the marker prefab's default in our postfix when the ghost
  is NOT ours (or cache + restore), so we don't leave a widened ring on a
  subsequent vanilla hoe. Cheap; just don't forget it.

## Recommendation

Ship as its own small client-cosmetic PR after confirming Gotcha 1 (our spade
pieces don't set `m_groundOnly`, else there's nothing to scale). Estimated effort:
~30-60 LOC + the reflection-name probe + a manual in-game eyeball (client-only
surface — can't be proven on the headless server).
