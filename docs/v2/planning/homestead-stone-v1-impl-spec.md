---
title: "Homestead Stone v1 (Niflheim) — current live-integration spec"
status: current playtest implementation hypothesis (2026-07-11) — provisional until final pre-release playtest ratification
---

# Homestead Stone v1 — current live-integration spec

This is the buildable contract for the first real Homestead Stone integration cut. The Niflheim
planning repo owns product/design decisions; this sibling project in `SBPRValheimMods` owns the
runtime implementation. All assignment tuning and save/migration policy remain provisional until the
final pre-release playtest explicitly ratifies them.

## 1. Project and package

- Project: `src/SBPR.Niflheim.HomesteadStones/` on net48/BepInEx/HarmonyX.
- Niflheim-only sibling plugin; it is not part of the standalone Trailborne product surface.
- Build: `dotnet build src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj -c Release`.
- Required clean result: 0 warnings, 0 errors.
- Stable runtime files beside the DLL:
  - `sbpr_niflheim_homestead_stones.unity3d`
  - `sbpr_niflheim_homestead_stones.unity3d.sha256`
- The csproj fails packaging if either required bundle artifact is absent.

## 2. Additive prefab and accepted V12 presentation

Prefab name: `piece_niflheim_homestead_stone`.

Construct the gameplay root with `new GameObject()` and only intended components:

- `ZNetView`: persistent, solid, non-distant;
- `HomesteadStoneIdentity`: dedicated marker/state seam;
- explicit root `CapsuleCollider` for gameplay collision.

The root deliberately has no `Piece` or `WearNTear`. Those components would opt this slice into
ordinary build removal, support, damage, repair, and destruction policy that has not been designed.
Use the neutral `Default` layer plus the explicit interaction/targeting collider until later interaction
and lifecycle slices settle those semantics.

Do not clone a vanilla network prefab. Load the resident AssetBundle from `Plugin.PluginFolder`, load
`assets/sbpr/niflheim/homesteadstones/meadowshomesteadingstone.prefab`, and instantiate it beneath the
root as presentation only:

```text
piece_niflheim_homestead_stone gameplay root   <- terrain seat, ZNetView, identity, collision
└── MeadowsHomesteadingStone visual            <- local Y +2.0 m, uniform scale 2×, Animator/renderers only
```

The accepted V12 visual authors at approximately 1.8 m; the current playtest tune (Daniel real-client
manual-walk feedback, 2026-07-15, verbatim: **"Needs to be about twice as big in all directions, and
float about a meter higher, but otherwise looks great."**) renders it at **uniform 2× scale** (approx.
3.6 m tall about its base pivot) floating at **local Y +2.0 m** — exactly +1.0 m above the prior +1.0 m
seat. The engine-free `Domain/HomesteadStonePresentation` contract owns these constants (scale, local Y,
and the refit collider) and is pinned by `HomesteadStonePresentationTests`; `HomesteadStoneRegistrar`
reads every number from it so code and this spec cannot silently diverge.

The explicit gameplay-root `CapsuleCollider` is refit to the enlarged, raised envelope: **radius 1.3 m**
(2× the prior 0.65 m), **height ≈ 5.6 m** spanning ground → the enlarged visual top, **center Y ≈ 2.8 m**
(midpoint). This keeps collision/targeting deliberately sized to the ~3.6 m stone now floating at +2.0 m
rather than obviously undersized or ghostly. It remains additive: no `Piece`/`WearNTear`/destruction
policy is added.

The stone is ivy-covered and cyan-emissive. The runtime supplies the four-second subtle hover/yaw contract
procedurally when the stable Linux bundle omits its authored Animator, so motion remains testable without
treating Unity's stripped animation module as success. Strip accidental `ZNetView`, `Piece`, `WearNTear`,
collider, or rigidbody components from its presentation subtree. Keep the bundle loaded while live
instances reference its assets.

This scale/hover tune is a **PROVISIONAL playtest presentation tune, not a final art lock.**

Promoted editable/import source and provenance live under
`src/SBPR.Niflheim.HomesteadStones/Assets/Source/`; large binaries use Git LFS. The repository-relative
Unity builder is `Assets/Editor/BuildNiflheimHomesteadStone.cs` and is executed from the Unity
6000.0.61f1 Preview Lab.

### 2.1 Texture import policy (provisional Valheim pixel look)

Per Daniel (2026-07-15, "Use these settings, 256–512, point no filter for the Valheim pixel look"), both
authored 512×512 textures (`guardian_basecolor.png` RGB albedo, `guardian_emission.png` grayscale
emission) are imported under a single data-named policy, `Assets/Editor/HomesteadTextureImportPolicy.cs`,
called by both the repository builder and the mirrored Preview-Lab builder so they cannot drift. The
policy pins the exact Inspector values:

- Texture Type **Default**, Shape **2D**;
- **sRGB on** for the albedo, **off** for the emission;
- Filter Mode **Point (no filter)**;
- Max Size **512** (authored inputs are 512; the enlarged 2× model benefits from retained source texels —
  a 256 cap is A/B-testable by changing only `HomesteadTextureImportPolicy.MaxTextureSize`);
- Wrap **Repeat**, Aniso **1**;
- Mipmaps **on** (retained pending a real-client A/B at ordinary distance);
- Alpha Source **None**;
- platform format **Automatic**, compression **Low Quality (Compressed LQ)**, **Crunch on** at quality
  **100**.

The builder's `ConfigureTexture` applies the policy then calls `HomesteadTextureImportPolicy.AssertMatches`,
which throws (failing the reproducible bundle build) on any importer drift for either texture. This is a
**PROVISIONAL playtest style tune, not a final art lock.**

### 2.2 Visual LOD and 90–120 m renderer culling (provisional performance tune)

Per Daniel/Soloredis guidance (2026-07-16): add an `LODGroup` to the whole presentation visual so every
base/ivy/emission child renderer is culled together at range, targeting a **90–120 m** maximum visibility,
with **5–7% relative screen height** as a size-dependent starting hypothesis.

The V12 model has **no authored lower-poly mesh**, so the builder authors a **single visual LOD** (LOD0 =
every renderer) followed by Unity's implicit hard cull region — *not* a duplicated fake lower LOD and *not*
destructive geometry. `BuildNiflheimHomesteadStone.cs` attaches one `LODGroup` to the `Visual` parent, sets
`fadeMode = None`, and calls `SetLODs` with a single `LOD` whose renderer array is the complete
`GetComponentsInChildren<Renderer>` set. A build-time assertion (fails the reproducible bundle build) then
re-reads the saved prefab and verifies exactly one LOD whose renderer membership is set-equal to every visual
renderer, so nothing renders outside the cull group.

**Only renderers cull.** The additive gameplay root (`ZNetView`, `HomesteadStoneIdentity`, `CapsuleCollider`,
placement object, and progression state) is a separate GameObject that the `LODGroup` never touches, so
identity, collision, seating, and AP behaviour stay alive when the visual is culled.

The cull distance is deterministic. Unity culls a group when its screen-relative height drops below the last
LOD's transition height `H`:

```text
screenHeight(d) = worldSize / (d · 2 · tan(fovVertical/2)) · lodBias
cullDistance    = worldSize · lodBias / (2 · H · tan(fovVertical/2))
```

The two engine constants are read from the shipped `assembly_valheim.dll` (vanilla is fair game per AGENTS.md):
**vertical FOV 65** (`GameCamera.m_fov`) and **default `lodBias` 2** (`GraphicsSettings.GetLodBias`, quality
level 2 — the default preset). `worldSize` is the runtime LOD group envelope: the authored world-space renderer
AABB (~1.88 m, the guardian FBX bakes a large import scale) × the registrar's uniform **2×** scale = **~3.77 m**.
The builder solves `H` from the target cull distance (`TargetCullDistanceMeters = 105 m`, the 90–120 m midpoint),
yielding **`H ≈ 0.0563` (5.63% relative screen height)** — squarely inside Daniel's 5–7% seed. The engine-free
`Domain/HomesteadStonePresentation` contract owns these constants (`LodCameraFovVerticalDegrees`,
`LodBiasReference`, `TargetCullDistanceMeters`, `Min/MaxAcceptableCullDistanceMeters`) plus the
`ComputeCullScreenHeight`/`ComputeCullDistance` formulas, pinned by `HomesteadStonePresentationTests`; the
builder inlines the same four numbers (it cannot reference the runtime assembly) and logs the resulting cull
distance for reviewer cross-check.

Deterministic real-engine evidence (Unity 6000.0.61f1, the same engine + LOD screen-height math the live client
runs) confirms the computed cull distance is **105.0 m** at FOV 65 / lodBias 2 with approach frames captured at
60/90/120/150 m. The live Valheim client capture (via the MCP/GABS harness) remains gated on a gateway restart
Daniel controls; this deterministic calibration is the strongest evidence obtainable headless.

This LOD/cull tune is a **PROVISIONAL performance tune, not a final art lock.**

## 3. Stable identity and reserved data keys

D3 is decided for this playtest build: the host Valheim Location's zone coordinate `(zoneX,zoneZ)`.
Store both coordinates explicitly:

- `niflheim.homestead.location_zone_x`
- `niflheim.homestead.location_zone_z`

Do not substitute:

- ZDOID/network owner;
- WorldZones transient identity;
- a minted GUID;
- raw world position.

`niflheim.homestead.resource_owner` is reserved for a future Niflheim account/entity identity; do not
blindly freeze Valheim PlayerID as the social model. `region_key` is reserved location metadata only.
There is no wild/Wyrd refusal rule.

## 4. Current deterministic assignment hypothesis

Eligible host types are exactly:

- `WoodHouse1` through `WoodHouse13`;
- `WoodFarm1`;
- `WoodVillage1`.

All identified non-Meadows hosts remain future candidates.

At runtime, after `ZoneSystem.m_locationInstances` is generated:

1. Build stable candidate identities from invariant **world UID**, selector version, prefab key, and
   Location zone coord. The canonical hash input is exactly
   `uid:<worldUID>|<selectorVersion>|<prefab>|<zoneX>|<zoneZ>`; world display name and an `assign`
   discriminator are not inputs.
2. Enforce a configurable minimum distance first; initial value: **128 m** between assigned Homesteads.
3. Pursue a configurable target per location type; current value: **40%**.
4. Use stable SHA-256 type-local priority with fair type rounds, independent of discovery/realization order.
5. Warn when proximity makes a target unattainable; never violate the minimum to meet density.

Astley's planning prototype contains 285 eligible candidates and targets 114 assignments. The current
algorithm/tuning is deliberately allowed to reroll while this remains a playtest hypothesis.

## 5. Current deterministic seating hypothesis

For each selected Location:

1. Generate exactly **8** pseudorandom XZ candidates deterministically from invariant world UID, selector
   version, host prefab, host zone coord, and attempt index.
2. Keep candidates within 92% of the Location exterior radius with a 1.75 m center guard.
3. Compute the global selected set deterministically and independently of scene realization. Terrain/
   collider validation and creation happen at **event time**, during vanilla fresh zone/location
   realization (see §5a), when the host's real colliders and the live `Heightmap` exist.
4. Resolve Y against the live `Heightmap` under the chosen seat at event time.
5. Attribute live host structural bounds from nearby enabled, non-trigger `Piece` colliders; reject the
   Stone capsule footprint when it overlaps those colliders and require at least 1.75 m horizontal AABB
   clearance.
6. Score every valid attempt using that clearance, an **actual structural-bounds** yard/readability band
   (the live host collider AABB half-extent + 2.5 m — **not** the coarse Location exterior radius; R4 fix),
   radial distance penalty, then attempt index as deterministic tie-break. Choose the best attempt; if all
   eight fail, skip and warn rather than force an invalid seat. The Location exterior radius survives only
   as the hard host-attribution / seat-ring constraint (step 2 / step 5), never as the scoring reference.
7. Before creating, search all Stone ZDOs—not only loaded GameObjects—for the same host zone coord.
   Stamp world identity, selector version, host prefab, and host zone coord on the new Stone's ZDO.
   During this explicitly pre-ratification build, a periodic **metadata-aware selected-set reconciliation**
   (R4 fix) walks every resident Stone ZDO: a ZDO matching the current selected world/selector/host prefab/
   zone is reused (one canonical kept per zone, lowest ZDOID wins on duplicates, extras removed); a ZDO
   absent from the current selected set or with mismatched metadata is removed, so selector/config rerolls
   cannot accumulate a union and a stale Stone can never suppress fresh creation for a currently-selected
   zone.

The Unity Preview Lab is the exhaustive static composition surface. It already established that compact
`WoodHouse1` and `WoodHouse2` need attempt 3 in the accepted composition model. The V12 visual seat is a
provisional playtest tune: it was first modelled at local Y `+1.0 m`, then raised to `+2.0 m` at uniform
2× scale per Daniel's 2026-07-15 real-client feedback (see §2). Live Valheim is a smaller integration gate for real terrain, runtime
materials/emission/animation, persistence, and generator-backed Farm/Village behavior.

## 5a. Server-authoritative realization lifecycle (R4 — event-time creation, finalized terrain)

The initial live proof (T009L2) failed because realization never occurred on a headless dedicated
server, silently. Root cause, confirmed against decompiled vanilla (`assembly_valheim`, base-game RE
per ADR-0001):

- `ZoneSystem.IsZoneLoaded(zone)` is true only when `zone ∈ ZoneSystem.m_zones`. `m_zones` is populated
  exclusively by `CreateLocalZones`, which iterates the active area around `ZNet.GetReferencePosition()`.
- On a dedicated server there is no local player; `Game` sets the reference position to a far-away
  sentinel and never moves it. A joined **peer's** location zone is realized only via `CreateGhostZones`
  → `SpawnZone(SpawnMode.Ghost)`, which never adds the zone to `m_zones`. So `IsZoneLoaded(zone)` is
  permanently false for every peer zone, and the old placement loop dropped all candidates at that gate.

### The R1/R2 dead end (removed)

R1 fell back to base `WorldGenerator.GetHeight` + first-seat. R2 tried to reconstruct the host footprint
and leveled surface from the location's **generic persisted structure ZDOs** (creator == 0 within the
location radius). Fresh review (PR #323) rejected R2 as unsound, and this build **removes** it:

- A ZDO transform pivot is neither a collider bound nor a terrain sample. Pivot distance cannot preserve
  capsule/clearance; a min-pivot Y can bury or float the Stone; rotation/prefab/role/bounds are lost.
- The `creator == 0` in-radius harvest also swept in the `LocationProxy`, zone-control, and vegetation —
  the proxy alone defeats the intended defer semantics.
- Headless scoring used the location radius rather than live structural-bounds extent.

**This build does not reconstruct scene geometry from generic persisted ZDO pivots.**

### Event-time creation seam

Creation is grounded in the vanilla fresh-zone realization event, where the real geometry actually
exists. `ZoneSystem.SpawnZone(Ghost|Full)` calls
`PlaceLocations(zoneID, …, hmap, clearAreas, mode, spawnedObjects)`, which:

1. freshly `Instantiate`s the host location's own `ZNetView` children at their final world positions with
   **real colliders**, over the **live `Heightmap`**, after the location's `TerrainModifier`/`TerrainComp`
   leveling has been applied;
2. sets `m_locationInstances[zone].m_placed = true`;
3. returns — and only **after** it returns does `SpawnZone` destroy the ghost temp objects (Ghost mode).

A Harmony **postfix on `ZoneSystem.PlaceLocations`** therefore observes the authoritative live geometry —
on **both** a listen server (`SpawnMode.Full`) and a headless dedicated server (`SpawnMode.Ghost`) — while
it still exists, before vanilla destroys it. `PlaceLocations` fires **exactly once per zone per world**
(vanilla guards its body on `!m_placed` and `SpawnZone` guards on `!IsZoneGenerated`), so fresh creation
is a strict one-shot event.

For a **freshly-placed selected host**, the postfix:

- **finalizes the live terrain first (R4 fix).** Vanilla `TerrainModifier.Awake` pokes each covering
  `Heightmap` with a **delayed** rebuild (`Poke(delayed:true)` sets `m_doLateUpdate`); the actual
  `Regenerate()` only runs later in `Heightmap.CustomLateUpdate`. Because this postfix runs synchronously
  before `SpawnZone` resumes, a covering Heightmap can still carry the pre-leveling surface. The postfix
  finds the specific Heightmap(s) covering the host footprint (`Heightmap.FindHeightmap(point, radius, …)`)
  and, for each with `HaveQueuedRebuild()`, forces the instance `Regenerate()` **now** — while the location
  modifiers and temporary host geometry still exist — using the narrowest safe vanilla op rather than the
  global `Heightmap.ForceGenerateAll()`. Then `Physics.SyncTransforms()` flushes the just-instantiated host
  transforms and the regenerated terrain colliders into the physics engine;
- captures the host structural AABB from the real freshly-spawned host colliders (an `OverlapSphere`
  filtered to enabled, non-trigger `Piece` colliders attributed to the host by the existing `creator == 0`,
  inside-radius contract — the same live attribution the prior listen path used);
- runs the full **all-eight best-of-score** seat contract against those live bounds (engine-free
  `HomesteadEventSeatScorer`), enforcing the 1.75 m keep-out and the **actual structural-extent** yard band
  (`LiveHostBounds.Extent`, not the coarse Location radius — R4 fix), or an honest 8-of-8 skip;
- resolves the final Y from the now-finalized **live `Heightmap`** under the chosen seat;
- instantiates the additive Stone bracketed in `ZNetView.StartGhostInit()`/`FinishGhostInit()` in Ghost
  mode so the `ZNetView` creates a **persistent ZDO** in `ZDOMan` and returns before `AddInstance`
  (verified against decompiled `ZNetView.Awake`; `OnDestroy` never destroys the ZDO). The temp GameObject
  is then destroyed like vanilla's ghost temp objects, but its persistent ZDO survives and is saved with
  the world. In Full mode the Stone remains a live scene instance;
- stamps identity (world identity, selector version, host prefab, host zone coord) atomically on the
  created ZDO; a stamping failure destroys the instance and creates nothing;
- **records the fresh-event provenance outcome** (R4 fix) — `FreshCreated`, `FreshInvalidSeats` (terminal
  8-of-8 skip), or `FreshTransientFailure` (prefab-missing / stamp failure / exception with live geometry
  available) — so the periodic migration scan can never mislabel a fresh-event failure as a pre-fix
  migration.

### Distinct lifecycle cases (provenance-tracked)

- **Fresh generation → create.** A selected host being placed for the first time creates exactly one
  Stone, at event time, from live (terrain-finalized) geometry (above). Outcome recorded `FreshCreated`.
- **Persisted Stone → reuse.** Before creating, the seam checks all Stone ZDOs for the host zone coord.
  On a restart the persisted Stone ZDO reloads and the one-shot event does not re-fire; if any path reaches
  the decision with a resident Stone it reuses it. Exactly one Stone per selected zone; no duplication on
  retries or restart.
- **Fresh event failure ≠ migration (R4 fix).** A fresh event that fired this session but produced no Stone
  is classified by its recorded provenance, NOT relabelled as migration once vanilla marks the zone
  generated:
  - `FreshInvalidSeats` — honest all-eight rejection against live bounds: a **terminal fresh skip** with a
    distinct warning, no retry.
  - `FreshTransientFailure` — prefab-missing / stamp failure / exception: a **creation-failed** diagnosis
    eligible for a bounded retry policy **only while authoritative geometry remains available** (the zone is
    not yet marked generated); once the geometry is gone or the retry budget is exhausted it is reported as
    a terminal creation failure, still distinct from migration.
- **Already-generated host, no Stone, and no fresh event this session → deferred migration.** Only a
  selected host whose zone was **already generated at session start**, still has no Stone, and saw **no
  fresh event this session** is a **pre-fix world**: its one-shot placement event fired before this fix and
  its live geometry is gone. The periodic reconcile emits exactly one `migration-required` diagnostic per
  such zone and **never** forces a seat or guesses geometry. Pre-release migration is deferred; regenerating
  a disposable fresh world (the Astley seed reproduces the layout) is the supported path, and a future
  migration/provider seam is preserved. The classification is a pure decision (`HomesteadMigrationClassifier`)
  driven by session-recorded provenance, so fresh failures can never be misreported as migration.

Base `WorldGenerator.GetHeight`, unconditional first-seat selection, and generic-ZDO geometry
reconstruction are **explicitly not used**.

Per-world diagnostic state (world identity, selection, per-zone event provenance, generated-at-start set,
transient-retry budget, migration-warned / fresh-skip-warned sets, prefab-missing latch) is recreated on
every `ZoneSystem.Start` and cleared on destroy, so no state survives a world reload. The
prefab-not-registered error logs once per missing-state. The event-time lifecycle decisions
(`HomesteadStoneLifecycle`), the terrain-finalization decision (`HomesteadTerrainFinalization`), the
metadata-aware reconciliation (`HomesteadStoneReconciler`), the provenance-aware migration classifier
(`HomesteadMigrationClassifier`), the live-bounds clearance geometry (`LiveHostBounds`), and the
best-of-eight seat scorer (`HomesteadEventSeatScorer`) are all engine-free
(`Domain/HomesteadStoneRealization.cs`) and drift-guarded by `HomesteadStoneRealizationTests`.

## 6. Explicit cuts for this integration slice

Do not implement stale proposal behavior:

- no forge/smelter/kiln demo bundle;
- no build denial or build-permission patch;
- no wild/Wyrd refusal rule;
- no progression purchases/effects;
- no final claim/account policy;
- no final persistence migration/compatibility freeze.

The inspect-only advancement-tree panel and claim/account identity are later slices. This cut first proves
the asset/package path, additive network prefab, deterministic selected placement, ZDO identity stamp,
and reload idempotence.

## 7. Verification

### Automated/static

- Production selector is deterministic for identical inputs.
- Per-type targets and 128 m exclusion are tested.
- Unattainable targets warn rather than violate distance.
- Eight seat attempts are stable and bounded.
- Best-of-eight structure-clearance scoring and honest 8-of-8 skip behavior are tested.
- Full repository test suite passes.
- net48 mod build is 0 warnings/0 errors.
- Packaged bundle checksum validates beside the DLL.

### Live joined-client gate

Use a disposable/local Astley client and verify representative cases rather than reviving the old
15-hop screenshot harness:

1. compact host: `WoodHouse1` or `WoodHouse2`, proving the retry seat;
2. ordinary house using an early attempt;
3. generator-backed Farm/Village if runtime placement is ready.

For each accepted frame verify the exact Location, ready terrain, gameplay root seated at ground, V12
child visibly hovering at +2.0 m (uniform 2× scale, ~3.6 m tall), host and Stone both identifiable, no wall/roof intersection, animation
advance, and correct day/night materials/emission. Reload and verify no duplicate Stone appears and the
host zone identity persists.

Finally stop the GABS client and verify `ps -C valheim.x86_64 --no-headers` is empty. Do not call this
final design ratification; it is a real integration proof of the current hypothesis.
