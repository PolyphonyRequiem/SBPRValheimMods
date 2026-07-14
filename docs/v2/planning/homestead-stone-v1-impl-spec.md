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
└── MeadowsHomesteadingStone visual            <- local Y +1.0 m, scale 1, Animator/renderers only
```

The accepted V12 visual is approximately 1.8 m, ivy-covered and cyan-emissive. The runtime supplies the
four-second subtle hover/yaw contract procedurally when the stable Linux bundle omits its authored Animator,
so motion remains testable without treating Unity's stripped animation module as success. Strip accidental
`ZNetView`, `Piece`, `WearNTear`, collider, or rigidbody components from its presentation subtree. Keep the
bundle loaded while live instances reference its assets.

Promoted editable/import source and provenance live under
`src/SBPR.Niflheim.HomesteadStones/Assets/Source/`; large binaries use Git LFS. The repository-relative
Unity builder is `Assets/Editor/BuildNiflheimHomesteadStone.cs` and is executed from the Unity
6000.0.61f1 Preview Lab.

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
`WoodHouse1` and `WoodHouse2` need attempt 3 in the accepted composition model and that the V12 visual
must sit at local Y `+1.0 m`. Live Valheim is a smaller integration gate for real terrain, runtime
materials/emission/animation, persistence, and generator-backed Farm/Village behavior.

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
child visibly hovering at +1.0 m, host and Stone both identifiable, no wall/roof intersection, animation
advance, and correct day/night materials/emission. Reload and verify no duplicate Stone appears and the
host zone identity persists.

Finally stop the GABS client and verify `ps -C valheim.x86_64 --no-headers` is empty. Do not call this
final design ratification; it is a real integration proof of the current hypothesis.
