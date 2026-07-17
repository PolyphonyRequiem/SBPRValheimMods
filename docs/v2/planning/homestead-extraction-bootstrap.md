---
title: "Homestead static-geometry extraction — bootstrap from a clean checkout"
status: current
---

# Homestead static-geometry extraction — bootstrap

This is the reproducible bootstrap for `scripts/extract_homestead_geometry.py`, the offline
extractor that produces the checked-in static-geometry catalog the Homestead Stone
realization lifecycle depends on. R7 (Blocker 3) requires extraction to be **self-contained
from checkout**: pinned dependencies and a documented, executable bootstrap live in the repo
so anyone with the base-game assets can regenerate the catalog byte-for-byte.

## What the extractor produces

One deterministic JSON catalog, checked in **twice** (byte-identical):

- `tests/Fixtures/homestead-static-geometry.json` — the fixture the unit tests validate.
- `src/SBPR.Niflheim.HomesteadStones/Assets/homestead-static-geometry.json` — the mod's
  embedded resource, hash-pinned at server startup.

`tests/HomesteadCatalogDriftGuardTests` fails the build if those two ever diverge, if the
host set changes, or if any semantic hash drifts.

## Why extraction is NOT in CI

CI has **no Valheim assets** (only the free dedicated-server managed assemblies for the
net48 build — not the AssetBundles the extractor reads). Regenerating the catalog is a
deliberate, asset-bearing act done locally by a developer who has the game installed. CI's
job is to guard the checked-in artifact, not to re-derive it. See ADR-0001 (clean-room):
the game AssetBundles are read offline and are never committed to this MIT repo.

## Dependencies

Two things, both external to the repo (they require the game payload, which must not be
committed):

1. **UnityPy 1.25 + codecs** — pinned in
   [`scripts/homestead-extraction-requirements.txt`](../../../scripts/homestead-extraction-requirements.txt).
2. **The `valheim_prefab` X-ray module** — the offline prefab reader (`valheim-prefab-tools`,
   the same tool documented by the `valheim-prefab-inspection` skill). Point
   `VALHEIM_PREFAB_TOOLS` at the directory that holds `valheim_prefab.py`.

## Bootstrap steps

```bash
# 1) Isolated venv with the pinned extraction deps.
python3 -m venv .homestead-extract
. .homestead-extract/bin/activate
pip install -r scripts/homestead-extraction-requirements.txt

# 2) Point the extractor at the offline X-ray module and the dedicated-server asset payload.
export VALHEIM_PREFAB_TOOLS=/path/to/valheim/prefab-tools        # dir containing valheim_prefab.py
export VALHEIM_SERVER_DATA=/path/to/valheim_server_Data          # dedicated-server asset payload

# 3) Regenerate the catalog. HOMESTEAD_ALL_BUNDLES=1 loads every client bundle into one
#    UnityPy env so cross-bundle MeshCollider references resolve (R6: conservative mesh
#    bounds, never silently discard).
HOMESTEAD_ALL_BUNDLES=1 python scripts/extract_homestead_geometry.py \
  > tests/Fixtures/homestead-static-geometry.json

# 4) Mirror the exact bytes into the embedded mod copy (the drift guard enforces identity).
cp tests/Fixtures/homestead-static-geometry.json \
   src/SBPR.Niflheim.HomesteadStones/Assets/homestead-static-geometry.json

# 5) Verify: the drift guard + catalog tests must pass.
dotnet test tests/SBPR.Trailborne.Tests.csproj -c Release
```

If a regenerated catalog changes any WoodHouse host's semantic hash, that is a **content
provenance change**: the pinned hashes in `HomesteadCatalogDriftGuardTests` and the selector
version in `HomesteadStoneWorldPlacement` must move together (the hash is stamped onto every
Stone ZDO and compared by the reconciler, so a silent geometry change would orphan every
already-placed Stone). Spec and code change together (AGENTS.md).

## Extraction semantics (what the catalog encodes)

The extractor honours the conservative-union rules the R6/R7 spec require:

- **Full transform matrices** at every hierarchy level (position × rotation quaternion ×
  scale), honouring parent rotation and non-uniform / negative scale.
- **Per-shape collider math** into host space, then a conservative axis-aligned XZ AABB
  (never `collider.bounds`): box (8 transformed corners), capsule (direction/height/radius
  extent box), sphere (scaled radius = `radius × max(|sx|,|sy|,|sz|)`), mesh (transformed
  mesh-bounds AABB, else the collider is recorded UNRESOLVED and the host **fails closed**).
- **RandomSpawn branches** are unioned even when inactive — the live world may pick any
  branch, so all possible branches are cleared conservatively.
- **One canonical semantic hash** over the stored footprint rows (`cx,cz,halfX,halfZ`),
  identical to `HomesteadGeometryHash.Compute` in C#, so extractor, JSON, embedded catalog,
  runtime loader, ZDO stamp, and tests agree byte-for-byte.
