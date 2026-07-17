---
title: "Homestead Stone realization lifecycle (R5) — engine-free static-geometry seat authority"
status: current playtest implementation hypothesis (R5, t_2a8a8aaa) — provisional until final pre-release playtest ratification
---

# Homestead Stone realization lifecycle (R5)

This spec is the same-PR buildable contract for how a Homestead Stone is *created, reused,
cleaned up, and deferred* on the authoritative server. It supersedes the R1–R4 realization
approaches, which all depended on live host colliders / a built Heightmap in the server physics
scene — proven impossible on a headless dedicated server by spike `t_24a5d20d` (NO-GO) and
re-scoped by spike `t_fd1f1698` (GO, split by host class).

Companion runtime: `src/SBPR.Niflheim.HomesteadStones/`. Engine-free seams live under `Domain/`
and are unit-tested headless (`tests/HomesteadStaticGeometryTests.cs`,
`tests/HomesteadWorldLedgerTests.cs`, `tests/StoneReconcilerTests.cs`); the net48 Unity adapter
(`Features/HomesteadStone/`) only reads live components + engine callsites and delegates every
decision to the engine-free core.

## 0. Why the physics scorer was abandoned (load-bearing context)

Spike 1 ran the R4 `Physics.OverlapSphere` / `Heightmap.GetHeight` seat scorer at every candidate
barrier on a real headless server and got **NO VALID SEAT everywhere** — `allColliders48m = 0`,
`groundHit = False` at both an ordinary `WoodHouse13` and a generator-backed `WoodVillage1`. Root
cause: a headless dedicated server never instantiates host colliders or a built Heightmap into a
physics scene without a joined GPU client, so *any* server-side geometry query is doomed regardless
of where the realization barrier is scoped. R5 therefore reads **authored static geometry** instead
of querying a physics scene, and takes terrain Y from **`WorldGenerator` pure noise** instead of a
live Heightmap.

## 1. Host classes (spike `t_fd1f1698`)

| Class | Hosts | Share | Seat authority |
|-------|-------|-------|----------------|
| Ordinary | `WoodHouse1..13` | 104/114 (91.2%) | Static collider footprints read off the live host root (`Approach A`) |
| Generator | `WoodFarm1`, `WoodVillage1` | 10/114 (8.8%) | Versioned manifest only (`Approach C`) — layout is terrain-gated and not seed-reproducible |

`HomesteadHostClassifier` owns this split. Generator hosts NEVER take a static-geometry seat and
NEVER replay the DungeonGenerator; with no matching manifest row they skip explicitly
(`ManifestRequired`).

## 2. Fresh creation (ordinary host)

1. **Assignment** is computed once, after `ZoneSystem.LocationsGenerated`, by the stable SHA-256
   selector (unchanged from prior rounds), keyed on world UID + selector version + host zone.
2. For each selected, **loaded** host not already terminal in the ledger (see §5):
   - Read the live host root's **authored** `Box/Capsule/Sphere` colliders via
     `GetComponentsInChildren<Collider>` + serialized shape + `Transform.TransformPoint` math
     (NOT `collider.bounds`, which is physics-scene populated). This is host-local, de-rotated into
     the host frame; the realized host yaw is read from the live root and re-applied analytically.
   - Enumerate a deterministic polar seat lattice inside the flatten **level radius (≤ 6.0 m)** and
     score each seat by true clearance to the nearest footprint edge. The best seat with clearance
     ≥ `SeatKeepOut` (1.75 m) wins. If none qualifies → `NoValidSeat` (terminal, see §5).
   - Terrain Y = `WorldGenerator.instance.GetHeight(seatX, seatZ)` — exact host-origin Y because the
     seat is clamped to the level radius where the location's `flatten` TerrainModifier levels the
     ground flat. (Invariant INV-1.)
3. Instantiate the additive Stone prefab at the seat, stamp the full assignment metadata onto its
   ZDO, and record `Created` in the durable ledger.

**Invariants (all unit-tested):** every seat's `radialFromHost ≤ 6.0 m` (INV-1); no `Physics.*` /
`Heightmap` symbol appears in the seat path (INV-2); the ordinary seat is a pure function of
(host geometry hash, host pos, host yaw, world seed) (INV-3); generator hosts route only to the
manifest (INV-4); the host geometry semantic hash pins against silent AssetBundle drift (INV-5).

## 3. Persisted reuse

A resident Stone ZDO whose full assignment metadata (`world + selector + prefab + zone`) matches a
selected assignment is **kept** and suppresses re-creation for that zone. The event gate validates
the *full* metadata, not just zone coordinates, so a stale same-zone Stone cannot mask a needed
re-creation. Reconciliation is keyed on the **full stable `ZDOID(UserID, ID)`** — never a truncated
numeric ID — so two ZDOs that share a numeric ID across different UserIDs are never conflated.

## 4. Selector cleanup (pre-ratification policy)

This build is explicitly pre-ratification: selector/config changes may reroll the disposable
playtest world. `StoneReconciler` therefore reaps, deterministically:

- **Unkeyed** Stones (no valid zone coordinates);
- **Unselected** Stones (zone not in the current assignment);
- **Mismatched** Stones (same zone, drifted world/selector/prefab metadata);
- **Duplicate** Stones (a second Stone for a zone already satisfied) — the lowest-`ZDOID` Stone is
  kept, so the SAME Stone survives across restarts regardless of enumeration order.

Kept Stones are re-registered into the live Stone-Area membership every tick (idempotent).

## 5. Durable provenance & no phantom retries

Every event outcome — `Created`, `NoValidSeat`, `ManifestRequired`, `GeometryUnavailable`,
`Exception`, `MigrationDeferred` — is captured in a **versioned per-world ledger**
(`HomesteadWorldLedger`), persisted as a sidecar text file under the world save directory
(`HomesteadLedgerStore`), and rehydrated on startup. Consequences:

- A fresh-world **failure survives restart** as a terminal fact — not a session-only dictionary that
  a restart clears into a silent retry.
- A same-selector-version failure re-observation is a **no-op** — this is what prevents counter-only
  phantom retries after vanilla has set its generated flag.
- `Created` is sticky (never overwritten); a **selector-version change** legitimately reopens a prior
  failure for a fresh attempt.
- **Exceptions are captured**, not swallowed.

## 6. Migration defer

An existing generated world with a Stone-less host and no runtime geometry to reconstruct is recorded
as **`MigrationDeferred`** — explicit, never a runtime geometry guess. Existing production worlds are
untouched by this change.

## 7. Startup drift assertions (bounded)

`HomesteadRuntimeDriftCheck.Verify()` runs once at plugin load and asserts exactly the required
Harmony targets / engine callsites / fields exist (`ZoneSystem.Start/OnDestroy`, `ZNetScene.Awake`,
`WorldGenerator.GetHeight(float,float)`, `WorldGenerator.instance`, `ZoneSystem.LocationsGenerated`,
`ZoneSystem.m_locationInstances`, `LocationProxy`). It logs one bounded summary and one error per
missing symbol, and reports rather than throws so a drifted game update degrades to "no realization +
a loud error" instead of a hard crash.

## 8. What remains post-merge (not in this card)

The live host-root discovery, realized-rotation recovery, and end-to-end fresh-world realization are
verified by the scheduled **T009L2** rerun on the spike's preserved disposable world fixture
(world UID `-898655635`, seed `kniTMtyDpB`) plus AP/retry/reconnect/restart — a joined-GPU-client
path that cannot be exercised headless. This PR delivers the engine-free authority + adapter wiring;
T009L2 is the in-engine acceptance gate.
