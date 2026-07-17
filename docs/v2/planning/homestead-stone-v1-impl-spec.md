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
3. Compute the global selected set independently of scene realization, but defer terrain/collider
   validation and creation until that selected Location's zone is loaded on the current peer.
4. Resolve Y against the local `Heightmap`.
5. Attribute live host structural bounds from nearby enabled, non-trigger `Piece` colliders; reject the
   Stone capsule footprint when it overlaps those colliders and require at least 1.75 m horizontal AABB
   clearance.
6. Score every valid attempt using that clearance, the host-radius + 2.5 m yard/readability band, radial
   distance penalty, then attempt index as deterministic tie-break. Choose the best attempt; if all eight
   fail, skip and warn rather than force an invalid seat.
7. Before creating, search all Stone ZDOs—not only loaded GameObjects—for the same host zone coord.
   Stamp world identity, selector version, host prefab, and host zone coord on the new Stone's ZDO.
   During this explicitly pre-ratification build, remove stale Stone ZDOs whose complete assignment
   metadata is absent from the current selected set so selector/config rerolls cannot accumulate a union.

The Unity Preview Lab is the exhaustive static composition surface. It already established that compact
`WoodHouse1` and `WoodHouse2` need attempt 3 in the accepted composition model. The V12 visual seat is a
provisional playtest tune: it was first modelled at local Y `+1.0 m`, then raised to `+2.0 m` at uniform
2× scale per Daniel's 2026-07-15 real-client feedback (see §2). Live Valheim is a smaller integration gate for real terrain, runtime
materials/emission/animation, persistence, and generator-backed Farm/Village behavior.

## 5a. Dedicated-server realization gate (realization lifecycle fix)

The initial live proof (T009L2) failed because realization never occurred on a headless dedicated
server, silently. Root cause, confirmed against decompiled vanilla (`assembly_valheim`, base-game RE
per ADR-0001):

- `ZoneSystem.IsZoneLoaded(zone)` is true only when `zone ∈ ZoneSystem.m_zones`. `m_zones` is populated
  exclusively by `CreateLocalZones`, which iterates the active area around `ZNet.GetReferencePosition()`.
- On a dedicated server there is no local player; `Game` sets the reference position to a far-away
  sentinel (`~1e6,0,1e6`) and never moves it. A joined **peer's** location zone is realized only via
  `CreateGhostZones` → `SpawnZone(SpawnMode.Ghost)`, which places the location's ZDOs (the resident
  structure ZDOs an observer sees) but **never adds the zone to `m_zones`**.
- Consequently `IsZoneLoaded(zone)` is permanently false for every peer zone, and the old placement loop
  dropped all selected candidates at that gate before any seat evaluation — with no diagnostic.
- Additionally, `ZNetScene` instantiates GameObjects (live `Heightmap`, vanilla `Piece` colliders) only
  around `GetReferencePosition()`, so collider-aware seat evaluation and `Heightmap.GetHeight` are also
  unavailable around a peer zone on a dedicated server.

Realization therefore uses **server-owned data**, not local scene state:

1. Trigger on `ZoneSystem.m_locationInstances[zone].m_placed` — set in BOTH ghost and full spawn, so it is
   true on a dedicated server for peer-realized zones — instead of `IsZoneLoaded`.
2. When the candidate's zone IS scene-instantiated on this peer (listen server / singleplayer host), keep
   the full collider-aware best-of-eight seat evaluation and live `Heightmap` height (unchanged path).
3. When it is not (headless dedicated server), fall back to the deterministic first seat with height from
   `WorldGenerator.GetHeight` — the same terrain source vanilla used to place the host — which is
   available without scene realization and is stable across restarts.
4. Idempotence and restart-reuse are unchanged: the pre-create Stone-ZDO search by host zone coord still
   guarantees exactly one persistent Stone per selected zone and no duplication on restart.

Bounded, actionable diagnostics replace the prior silent-nothing behavior (no per-tick spam):

- prefab-not-registered logs once per missing-state (nothing can realize until fixed);
- a per-pass gate summary (selected / realized-this-pass / already-resident / zone-not-placed / eligible /
  seat-skipped) logs only when the pass shape changes;
- a selected zone whose host location is placed but which stays Stone-less past a bounded interval (30 s)
  emits exactly one actionable warning per stone-less episode, and re-arms if the zone relapses.

The gate, the change-gated pass reporter, and the stone-less watch are engine-free
(`Domain/HomesteadRealizationDiagnostics.cs`) and drift-guarded by `HomesteadRealizationDiagnosticsTests`.

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
