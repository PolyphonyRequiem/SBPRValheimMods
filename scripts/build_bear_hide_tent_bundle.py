#!/usr/bin/env python3
"""
Build the SBPR Bear Hide Tent AssetBundle (placeholder art) from vanilla TraderTent.

WHY THIS EXISTS
---------------
The Bear Hide Tent's placeholder art is the vanilla `TraderTent` mesh (Haldor's
market tent — stitched-hide canopy, the closest vanilla look to "bear hide").
Unlike every other SBPR kitbash donor, `TraderTent` is **location decoration in a
lazy SoftReference bundle** — it is NOT in either ZNetScene serialized prefab list
(`m_prefabs` / `m_nonNetViewPrefabs`), so `ZNetScene.GetPrefab("TraderTent")`
returns null at runtime (verified against Jotunn's prefab-list.md: all known
buildable donors present, TraderTent + HildirTent absent). The graft-by-name
pattern the Surveyor's Table uses therefore CANNOT reach it.

So we ship the mesh ourselves in a custom AssetBundle — SBPR's FIRST custom bundle.

THE PIPELINE (empirically proven, 2026-06-25)
---------------------------------------------
Valheim runs Unity 6000.0.61f1. An AssetBundle must match that Unity version or it
won't load. We dodge version-authoring entirely: we take the game's OWN bundle
(which is already Unity 6000.0.61f1), rename the TraderTent mesh to a stable,
unique, loader-findable name, and repack via UnityPy. The Unity-version metadata is
preserved BY CONSTRUCTION, so the result loads in-game. Round-trip verified: mesh
pid 7540785263153509521, 526 verts, name survives reload, version string intact.

We do NOT surgically strip the bundle to a single object — UnityPy 1.25 has no clean
object-removal API and hand-editing the CAB risks breaking cross-refs. The full
repacked bundle is ~435 KB (one tent's worth of incidental Vendor assets ride along
harmlessly); the C# loader pulls our mesh out BY NAME via LoadAllAssets, ignoring the
rest. Pragmatic > clever (ADR / "simple over clever").

SHADERS ARE NOT IN THIS BUNDLE (by design)
------------------------------------------
The dedicated-server payload strips material shaders (TraderTent_cloth shader PPtr =
null; verified). So this bundle ships the MESH only. The C# side
(`BearHideTent.cs`) builds the material at RUNTIME — borrowing a live in-game shader
off a vanilla piece + the diffuse/normal PNGs shipped in assets/textures/. This is
the same runtime-material discipline SBPR already uses for held-mesh albedos
(Assets.LoadPngAsTexture). A bundle-baked material would render magenta.

USAGE
-----
    python3 scripts/build_bear_hide_tent_bundle.py
Outputs:
    assets/bundles/sbpr_tradertent.unity3d   (committed binary art artifact)

Re-run only when the vanilla TraderTent asset changes (a Valheim art patch) — the
bundle is otherwise stable and committed, so a normal build does NOT need this.
This script is RequiemSoul-bound (needs the local Valheim server payload); it is the
provenance record + regenerator, not part of CI.
"""
import os
import sys
import shutil

# ── locate the vanilla source bundle (RequiemSoul-local server payload) ─────────
SERVER_DATA = os.path.expanduser(
    "~/valheim/niflheim/data/server/valheim_server_Data"
)
SRC_BUNDLE = os.path.join(SERVER_DATA, "StreamingAssets/SoftRef/Bundles/17a773de")

# The TraderTent build mesh — resolved via GameObject->MeshFilter->m_Mesh (526 verts,
# 3 submeshes). Stable path_id in this game version; re-confirm after a Valheim update.
TARGET_MESH_PID = 7540785263153509521
ASSET_NAME = "SBPR_TraderTentMesh"  # what BearHideTent.cs LoadAllAssets-filters on

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
OUT_DIR = os.path.join(REPO_ROOT, "assets", "bundles")
OUT_BUNDLE = os.path.join(OUT_DIR, "sbpr_tradertent.unity3d")


def main() -> int:
    try:
        import UnityPy
    except ImportError:
        print(
            "ERROR: UnityPy not available. This script runs in the vprefab venv:\n"
            "  ~/valheim/prefab-tools/.venv/bin/python scripts/build_bear_hide_tent_bundle.py",
            file=sys.stderr,
        )
        return 2

    if not os.path.exists(SRC_BUNDLE):
        print(f"ERROR: vanilla source bundle not found: {SRC_BUNDLE}", file=sys.stderr)
        print("  (this script is RequiemSoul-bound — needs the local server payload)", file=sys.stderr)
        return 2

    print(f"== loading vanilla bundle: {SRC_BUNDLE} ({os.path.getsize(SRC_BUNDLE)} bytes)")
    env = UnityPy.load(SRC_BUNDLE)
    ver = env.assets[0].unity_version if env.assets else "?"
    print(f"== source Unity version: {ver}  (must match Valheim: 6000.0.61f1)")

    # rename the target mesh so the C# loader can find it unambiguously by name
    renamed = False
    for o in env.objects:
        if o.type.name == "Mesh" and o.path_id == TARGET_MESH_PID:
            d = o.read()
            vc = getattr(getattr(d, "m_VertexData", None), "m_VertexCount", None)
            d.m_Name = ASSET_NAME
            d.save()
            renamed = True
            print(f"== renamed mesh pid={TARGET_MESH_PID} -> '{ASSET_NAME}' (verts={vc})")
            break
    if not renamed:
        print(
            f"ERROR: target mesh pid {TARGET_MESH_PID} not found — the vanilla asset "
            "may have changed (Valheim art patch). Re-resolve via "
            "GameObject->MeshFilter->m_Mesh on TraderTent and update TARGET_MESH_PID.",
            file=sys.stderr,
        )
        return 3

    os.makedirs(OUT_DIR, exist_ok=True)
    data = env.file.save(packer="lz4")  # lz4 = Valheim's native bundle compression
    with open(OUT_BUNDLE, "wb") as f:
        f.write(data)
    print(f"== wrote {OUT_BUNDLE} ({os.path.getsize(OUT_BUNDLE)} bytes)")

    # verify: reload and confirm the renamed mesh survives at the right version
    env2 = UnityPy.load(OUT_BUNDLE)
    ver2 = env2.assets[0].unity_version if env2.assets else "?"
    hit = [
        (o.path_id, o.read().m_Name)
        for o in env2.objects
        if o.type.name == "Mesh" and getattr(o.read(), "m_Name", "") == ASSET_NAME
    ]
    if ver2 == "6000.0.61f1" and hit:
        print(f"== ROUND-TRIP OK: version={ver2}, '{ASSET_NAME}' present {hit}")
        return 0
    print(f"ERROR: round-trip verify FAILED (version={ver2}, hit={hit})", file=sys.stderr)
    return 4


if __name__ == "__main__":
    sys.exit(main())
