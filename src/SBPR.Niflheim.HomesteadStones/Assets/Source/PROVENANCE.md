# Accepted V12 Meadows Homesteading Stone — provenance

This directory contains the promoted source/import artifacts for the accepted
`MeadowsHomesteadingStone` visual candidate. Binary source files are tracked by
Git LFS; numbered V1–V12 scratch directories are intentionally excluded.

## Inputs

| Repository file | Original local source | Role |
|---|---|---|
| `guardian_stone_ivy_v9.blend` | `tobi-trellis/guardian/ivy_v9/guardian_stone_ivy_v9.blend` | editable Blender source |
| `guardian_stone_ivy_v9.fbx` | `tobi-trellis/guardian/ivy_v9/guardian_stone_ivy_v9.fbx` | Unity model import |
| `guardian_stone_ivy_v9.glb` | `tobi-trellis/guardian/ivy_v9/guardian_stone_ivy_v9.glb` | portable preview |
| `guardian_basecolor.png` | `tobi-trellis/guardian/polished/guardian_basecolor.png` | stone albedo |
| `guardian_emission.png` | `tobi-trellis/guardian/polished/guardian_emission.png` | cyan rune/fissure emission |

The blockout began as a TRELLIS image-to-3D result, then received cleanup,
texturing, ivy, animation, Unity bundling, and live Valheim review. It is an
accepted playtest candidate, not hand-retopologized final production topology.

## Runtime contract

- prefab: `MeadowsHomesteadingStone`
- stable bundle: `sbpr_niflheim_homestead_stones.unity3d`
- stable asset path: `assets/sbpr/niflheim/homesteadstones/meadowshomesteadingstone.prefab`
- source candidate height: approximately 1.8 m
- idle: four-second subtle hover/yaw loop
- runtime hierarchy: gameplay root at the terrain seat; this animated visual as
  a child at local Y `+1.0 m`

The original loose V12 proof bundle had SHA-256
`e497bacda194dcdd2a618c6bce730f029c129a6c49f0b3ad770dee7564e3f30f`.
That hash proves the accepted prototype only; the reproducibly rebuilt stable
bundle has its own checked-in `.sha256` sidecar.
