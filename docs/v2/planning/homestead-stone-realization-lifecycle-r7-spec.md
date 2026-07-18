---
title: "Homestead Stone realization lifecycle — R7 authored-seat contract"
status: current playtest implementation hypothesis (2026-07-18) — provisional until final pre-release playtest ratification
---

# Homestead Stone realization lifecycle — R7 authored-seat contract

**Status:** implementation contract for the current pre-release playtest
**Scope:** `WoodHouse1..13` only
**Supersedes:** R1–R6 runtime collider scoring, generator manifests, and dynamic seat selection

## 1. Goal

For 40% of ordinary Meadows house locations, create exactly one persistent Homestead Stone at a
Daniel-approved transform authored in the house prefab's local coordinate system. The dedicated
server must derive the world transform from authoritative LocationProxy ZDO position/rotation,
without requiring live Unity colliders, a built Heightmap, Python, or a runtime geometry catalog.

`WoodFarm1` and `WoodVillage1` are not Homestead Stone hosts. They belong to a future village-like
system with a separate lifecycle.

## 2. Assignment

- Eligible hosts: exact prefab names `WoodHouse1..13`.
- Density: 40% per type; current world fixture has 260 candidates and 104 selected houses.
- Minimum selected-host spacing: 128 m.
- Stable assignment identity: world UID + selector version + host prefab + host zone.
- Selector version: `niflheim-homestead-playtest-v1` for this disposable pre-release world.

## 3. Authored transform table

`HomesteadAuthoredSeatCatalog` is the sole runtime seating authority. Each row contains a local
position and local yaw. Runtime applies the same transform composition as a prefab child:

```text
worldPosition = hostPosition + hostRotation * localPosition
worldRotation = hostRotation * localRotation
```

| Host | Local position (x,y,z) | Local yaw |
|---|---:|---:|
| WoodHouse1 | (-5.999, 0, 0.125) | 91.2° |
| WoodHouse2 | (-0.062, 0, 6.000) | 179.4° |
| WoodHouse3 | (-4.795, 0, 3.607) | 127.0° |
| WoodHouse4 | (5.267, 0, 2.873) | -118.6° |
| WoodHouse5 | (-5.078, 0, 3.196) | 122.2° |
| WoodHouse6 | (5.671, 0, 1.961) | -109.1° |
| WoodHouse7 | (-0.561, 0, -5.974) | 5.4° |
| WoodHouse8 | (-2.472, 0, 0.373) | 98.6° |
| WoodHouse9 | (-5.078, 0, 3.196) | 122.2° |
| WoodHouse10 | (-5.207, 0, 2.982) | 119.8° |
| WoodHouse11 | (6.000, 0, 0.000) | -90.0° |
| WoodHouse12 | (3.249, 0, -5.044) | -32.8° |
| WoodHouse13 | (2.595, 0, -5.410) | -25.6° |

Every row was selected from three Unity Preview Lab renders at least 2 m apart. The checked-in
collider catalog and Python extractor remain build-time evidence for clearance review only. They are
not loaded, parsed, hashed, or consulted by the runtime.

The authored-seat authority version is `niflheim-homestead-authored-seats-v2`. V2 also reproduces
the prefab-child clear-area consequence for Stones realized after vanilla vegetation placement:
before first creation, the authoritative server destroys only ZDOs whose prefab is present in
`ZoneSystem.m_vegetation` and whose XZ center lies within 2.5 m of the authored seat. It never removes
structures, player pieces, creatures, or arbitrary nearby ZDOs. Reconciliation/restart does not run
the clear again for a matching existing Stone.

## 4. Authoritative host pose

The server resolves the host by:

1. exact `ZDOVars.s_location` stable hash;
2. exact candidate zone;
3. LocationProxy ZDO `GetPosition()` and `GetRotation()`.

No nearest-proxy guess and no live child-hierarchy discovery are allowed. A missing matching proxy is
retryable; it does not create a failure Stone or fall back to a guessed position.

## 5. Realization timing

The placement coroutine starts after `ZoneSystem.LocationsGenerated`. A selected host is eligible for
creation when its authoritative `m_locationInstances[zone].m_placed` is true. Do not gate on
`ZoneSystem.IsZoneLoaded`: peer Ghost-generated locations can be persistently placed without entering
the dedicated server's live `m_zones` set.

The loop reconciles existing Stones, creates missing eligible Stones, reconciles Stone Areas, then
repeats every five seconds.

## 6. Persistence and reconciliation

A correctly stamped Stone ZDO is creation truth.

The stamp contains:

- provenance schema version;
- world UID;
- selector version;
- host prefab;
- host zone X/Z;
- provider kind;
- authored-seat table version;
- authored-seat table content hash;
- generation 0.

Reconciliation uses full stable ZDOID `(UserID, ID)` and full provenance. It deterministically:

- keeps the first matching Stone per selected zone;
- destroys unkeyed, unselected, mismatched, stale-provenance, and duplicate Stones;
- clears an advisory ledger `Created` entry when stale provenance requires recreation.

The sidecar ledger records outcomes and is not creation truth. Ledger I/O/corruption fails closed.
Both reconciliation and creation persistence run inside the same tick-scoped `LedgerIoException`
boundary: a transient durability failure aborts that tick and retries on the next five-second pass;
it must not kill the coroutine until process restart.

## 7. Ground following

The persistent ZNetView/ZDO root remains fixed at the authored world transform. A non-networked child
`GroundAnchor` owns the visual and targeting collider.

On a client with a loaded Heightmap, `HomesteadStoneGroundFollower` samples current terrain every
0.5 s and adjusts only `GroundAnchor.localPosition.y` when the difference is at least 0.02 m.
Therefore hoe/pickaxe elevation edits move the visible Stone and collider without letting a client
rewrite authoritative Stone identity or Area position. When no Heightmap exists (including a
headless dedicated server), the anchor retains its prior height and retries later.

Stone Area membership is XZ-only with a 20 m radius, so local vertical following does not change
progression authority.

## 8. Drift and build-time evidence

Runtime startup pins:

- the exact required Valheim methods/fields;
- exactly 13 authored seat rows;
- the authored table version/content hash.

Runtime startup must not load or verify the offline collider catalog. Build-time tests may regenerate
and compare that catalog to detect vanilla prefab drift and to validate authored-seat clearance.

## 9. Verified evidence

On disposable world UID `-898655635`, seed `kniTMtyDpB`:

- authored seat pin: 13 rows;
- assignment: 260 candidates / 104 selected;
- old static-catalog provenance Stones were reaped and recreated;
- WoodHouse6 host pose `(193.633,46.869,126.330)`, yaw `67.50°`;
- expected authored-B Stone `(197.615,46.869,121.841)`, yaw `318.40°`;
- actual Stone matched position and rotation exactly;
- target zone contained exactly one Stone, stable ZDOID `1:12445`;
- restart reused the same ZDO with zero new placement and zero duplicate.
- first live-client load sampled terrain `47.645`, moved `GroundAnchor.localY` to `0.775`, and left
  root/ZDO Y fixed at `46.869`;
- a real persisted `TerrainOp` raised terrain by `0.750 m`; anchor and collider rose by exactly
  `0.750 m` while root position, ZDO position, yaw, and stable ZDOID stayed unchanged;
- the inverse real `TerrainOp` restored terrain, anchor, and collider to their exact baseline values;
- a second independent +0.500/-0.500 m cycle reproduced the result;
- V2 vegetation clearing removed the two Beech ZDOs that obscured/intersected the authored seat;
  live client probes found zero vegetation trees within 2.5 m and restart preserved that result;
- GPU evidence shows the Stone clearly visible beside the WoodHouse6 host after clearing.

## 10. Delivery gates

Before merge:

1. Homestead and Trailborne net48 builds: 0 warnings / 0 errors.
2. Full net8 suite and workbench suite pass.
3. Docs lint/freshness pass.
4. Production authored-transform + restart evidence remains green.
5. Joined-client ground-follow test passes with reversible real terrain operations and fixed root/ZDO.
6. Fresh independent review of the updated PR head.

After merge, rerun T009L2: joined client → Attunement → Foundational placement → AP receipt →
reconnect/restart recovery. Logs green are not a playable verdict.
