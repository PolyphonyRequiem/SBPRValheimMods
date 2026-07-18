#!/usr/bin/env python3
"""Extract host-local static collider geometry for the Homestead eligible hosts (R6).

Emits a deterministic JSON catalog consumed by the R6 engine-free seat resolver
(as the production authority, embedded in the mod) and validated by the tests.

R6 CORRECTIONS (over the R5 extractor that the review rejected):
  * FULL transform matrices. Each node's local-to-host transform is composed from
    (LocalPosition, LocalRotation quaternion, LocalScale) at every level of the
    hierarchy — not just summed local positions. Parent ROTATION and NON-UNIFORM /
    NEGATIVE SCALE are honoured.
  * Per-collider shape math into world/host space, then a conservative axis-aligned
    XZ AABB (transformed corners / extents), never collider.bounds:
      - Box:     transform all 8 local corners, take min/max XZ.
      - Capsule: honour direction (0=X,1=Y,2=Z) + height + radius; build the capsule's
                 local AABB (a box of the capsule's extent), transform its 8 corners.
      - Sphere:  scaled radius = radius * max(|sx|,|sy|,|sz|); transform center, expand.
      - Mesh:    conservative transformed mesh-bounds AABB when mesh bounds are present;
                 otherwise the collider is recorded as UNRESOLVED and the host FAILS
                 CLOSED (never silently discarded).
  * RandomSpawn branch semantics: colliders under an inactive RandomSpawn branch are
    still UNIONED into the footprint (conservative — we do not know which branch the
    live world picked, so we must clear ALL possible branches). Active/enabled/trigger
    state is preserved for the load-bearing filter.
  * ONE canonical semantic hash schema: the hash is computed over the STORED footprint
    rows (kind-free: cx,cz,halfX,halfZ), matching HomesteadGeometryHash exactly, so the
    extractor, the checked-in catalog, the runtime catalog loader, the ZDO stamp, and
    the tests all agree byte-for-byte. Production recomputes and pins this at startup.

Repro:  scripts/extract_homestead_geometry.py > tests/Fixtures/homestead-static-geometry.json
        (then copy the same bytes to src/SBPR.Niflheim.HomesteadStones/Assets/)
Deps:   UnityPy 1.25 + the offline `valheim_prefab` X-ray module. Both are pinned and
        documented in scripts/homestead-extraction-requirements.txt +
        docs/v2/planning/homestead-extraction-bootstrap.md so extraction is reproducible
        from a clean checkout. Offline base-game read only (ADR-0001). No Physics, no
        Heightmap.

Bootstrap (see docs/v2/planning/homestead-extraction-bootstrap.md for full steps):
  python3 -m venv .homestead-extract && . .homestead-extract/bin/activate
  pip install -r scripts/homestead-extraction-requirements.txt
  export VALHEIM_PREFAB_TOOLS=/path/to/valheim/prefab-tools   # dir holding valheim_prefab.py
  export VALHEIM_SERVER_DATA=/path/to/valheim_server_Data     # dedicated-server asset payload
  HOMESTEAD_ALL_BUNDLES=1 python scripts/extract_homestead_geometry.py > tests/Fixtures/homestead-static-geometry.json

CI note: CI has NO Valheim assets, so it does NOT re-extract. Instead the drift guard
  (tests/HomesteadCatalogDriftGuardTests) pins the exact 13 ordinary house names,
  every semantic hash + the schema, and asserts the fixture and the
  embedded mod catalog are byte-identical. Regeneration is a deliberate, asset-bearing act.
"""
import os, sys, json, math, hashlib
# The extractor needs the offline `valheim_prefab` X-ray module. It is an external tool
# (it must read the game's dedicated-server AssetBundles, which are not — and must not be —
# committed to this MIT repo). Resolve it from VALHEIM_PREFAB_TOOLS so extraction is
# reproducible from a clean checkout without hard-coding a machine-specific path; fall back
# to the script directory (for a vendored copy) so a self-contained bundle also works.
_PREFAB_TOOLS = os.environ.get("VALHEIM_PREFAB_TOOLS", os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, _PREFAB_TOOLS)
try:
    import UnityPy  # noqa
    import valheim_prefab as vp
except ImportError as exc:  # pragma: no cover - operator bootstrap guard
    sys.stderr.write(
        "extract_homestead_geometry: missing extraction deps (%s).\n"
        "  pip install -r scripts/homestead-extraction-requirements.txt\n"
        "  export VALHEIM_PREFAB_TOOLS=<dir containing valheim_prefab.py>\n"
        "  See docs/v2/planning/homestead-extraction-bootstrap.md.\n" % exc)
    raise

INFLATE = 0.15  # production footprint inflation (matches the seat keep-out tuning)
HOSTS = ["WoodHouse%d" % i for i in range(1, 14)]
COLLIDER_TYPES = ("BoxCollider", "CapsuleCollider", "SphereCollider", "MeshCollider")


# ---- tiny 4x4 matrix math (row-major, right-handed, Unity conventions) --------------

def mat_identity():
    return [[1.0, 0, 0, 0], [0, 1.0, 0, 0], [0, 0, 1.0, 0], [0, 0, 0, 1.0]]


def mat_mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def mat_translate(t):
    m = mat_identity()
    m[0][3], m[1][3], m[2][3] = t[0], t[1], t[2]
    return m


def mat_scale(s):
    return [[s[0], 0, 0, 0], [0, s[1], 0, 0], [0, 0, s[2], 0], [0, 0, 0, 1.0]]


def mat_from_quat(q):
    # q = (x,y,z,w). Standard quaternion-to-rotation-matrix.
    x, y, z, w = q
    n = math.sqrt(x * x + y * y + z * z + w * w)
    if n == 0:
        return mat_identity()
    x, y, z, w = x / n, y / n, z / n, w / n
    return [
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w),     2 * (x * z + y * w),     0.0],
        [2 * (x * y + z * w),     1 - 2 * (x * x + z * z), 2 * (y * z - x * w),     0.0],
        [2 * (x * z - y * w),     2 * (y * z + x * w),     1 - 2 * (x * x + y * y), 0.0],
        [0.0, 0.0, 0.0, 1.0],
    ]


def mat_trs(t, q, s):
    # Unity local-to-parent = T * R * S.
    return mat_mul(mat_translate(t), mat_mul(mat_from_quat(q), mat_scale(s)))


def transform_point(m, p):
    x = m[0][0] * p[0] + m[0][1] * p[1] + m[0][2] * p[2] + m[0][3]
    y = m[1][0] * p[0] + m[1][1] * p[1] + m[1][2] * p[2] + m[1][3]
    z = m[2][0] * p[0] + m[2][1] * p[1] + m[2][2] * p[2] + m[2][3]
    return (x, y, z)


# ---- field readers -------------------------------------------------------------------

def v3f(v, default=(0.0, 0.0, 0.0)):
    if v is None:
        return default
    return (float(getattr(v, "x", 0.0)), float(getattr(v, "y", 0.0)), float(getattr(v, "z", 0.0)))


def quat(v):
    if v is None:
        return (0.0, 0.0, 0.0, 1.0)
    return (float(getattr(v, "x", 0.0)), float(getattr(v, "y", 0.0)),
            float(getattr(v, "z", 0.0)), float(getattr(v, "w", 1.0)))


def is_active(go):
    return bool(getattr(go, "m_IsActive", True))


def local_matrix(tr):
    t = v3f(getattr(tr, "m_LocalPosition", None))
    q = quat(getattr(tr, "m_LocalRotation", None))
    s = v3f(getattr(tr, "m_LocalScale", None), (1.0, 1.0, 1.0))
    return mat_trs(t, q, s)


# ---- collider → world-space AABB corners ---------------------------------------------

def box_corners(center, size):
    hx, hy, hz = abs(size[0]) / 2, abs(size[1]) / 2, abs(size[2]) / 2
    cx, cy, cz = center
    return [(cx + sx * hx, cy + sy * hy, cz + sz * hz)
            for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]


def capsule_local_box(center, radius, height, direction):
    # Capsule extent along its axis is height/2 (>= radius), radius on the other two axes.
    half = max(height / 2.0, radius)
    if direction == 0:      # X
        return center, (2 * half, 2 * radius, 2 * radius)
    elif direction == 2:    # Z
        return center, (2 * radius, 2 * radius, 2 * half)
    else:                   # Y (default)
        return center, (2 * radius, 2 * half, 2 * radius)


def mat_world_scale(m):
    # Per-axis world scale = length of each basis column of the upper-3x3.
    sx = math.sqrt(m[0][0] ** 2 + m[1][0] ** 2 + m[2][0] ** 2)
    sy = math.sqrt(m[0][1] ** 2 + m[1][1] ** 2 + m[2][1] ** 2)
    sz = math.sqrt(m[0][2] ** 2 + m[1][2] ** 2 + m[2][2] ** 2)
    return (sx, sy, sz)


def collider_world_corners(tn, c, world_mat):
    """Return (corners, unresolved) — a list of world-space corner points whose XZ
    min/max form the conservative footprint, or unresolved=True to fail closed."""
    center = v3f(getattr(c, "m_Center", None))
    if tn == "BoxCollider":
        size = v3f(getattr(c, "m_Size", None))
        local = box_corners(center, size)
    elif tn == "SphereCollider":
        # Sphere scaled-radius rule (R6/R7 documented): a sphere stays a sphere under scale, so its
        # world radius is radius * max(|sx|,|sy|,|sz|) of the world scale — NOT a per-axis-scaled box.
        # We transform ONLY the center through the full matrix (position/rotation), then expand by the
        # scaled radius as an axis-aligned box in world space. Transforming a local 2r cube instead
        # would understate/overstate the footprint under non-uniform scale (the R6 bug).
        r = float(getattr(c, "m_Radius", 0.5))
        wc = transform_point(world_mat, center)
        ws = mat_world_scale(world_mat)
        sr = r * max(abs(ws[0]), abs(ws[1]), abs(ws[2]))
        return ([(wc[0] + sx * sr, wc[1] + sy * sr, wc[2] + sz * sr)
                 for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)], False)
    elif tn == "CapsuleCollider":
        r = float(getattr(c, "m_Radius", 0.5))
        h = float(getattr(c, "m_Height", 2 * r))
        d = int(getattr(c, "m_Direction", 1))
        cen, size = capsule_local_box(center, r, h, d)
        local = box_corners(cen, size)
    elif tn == "MeshCollider":
        mesh = vp.deref(getattr(c, "m_Mesh", None))
        aabb = getattr(mesh, "m_LocalAABB", None) if mesh else None
        if aabb is None:
            return [], True   # cannot bound the mesh → fail closed
        mc = v3f(getattr(aabb, "m_Center", None))
        me = v3f(getattr(aabb, "m_Extent", None))
        local = box_corners(mc, (2 * me[0], 2 * me[1], 2 * me[2]))
    else:
        return [], True
    return [transform_point(world_mat, p) for p in local], False


# ---- hierarchy walk ------------------------------------------------------------------

def walk(tr, parent_mat, out, parent_active, under_random_spawn=False, piece_ancestor=None):
    go = vp.deref(getattr(tr, "m_GameObject", None))
    world_mat = mat_mul(parent_mat, local_matrix(tr))
    active = parent_active and (is_active(go) if go else True)
    # RandomSpawn / Piece ancestry + GameObject layer are RECORDED semantics (R7 Blocker 3): a collider
    # under a RandomSpawn branch is unioned regardless of active state (we cannot know which branch the live
    # world picks); nearest Piece ancestry + layer are recorded so the load-bearing filter can include only
    # intended structural candidates under explicit rules.
    this_random_spawn = False
    node_layer = -1
    node_piece = piece_ancestor
    if go:
        name = getattr(go, "m_Name", "?")
        node_layer = int(getattr(go, "m_Layer", -1))
        for cp in (getattr(go, "m_Component", None) or []):
            tn = vp.ptr_type(getattr(cp, "component", None))
            if tn == "RandomSpawn":
                this_random_spawn = True
            if tn == "Piece":
                node_piece = name
        for cp in (getattr(go, "m_Component", None) or []):
            ptr = getattr(cp, "component", None)
            tn = vp.ptr_type(ptr)
            if tn in COLLIDER_TYPES:
                c = vp.deref(ptr)
                if c is None:
                    out.append({"node": name, "kind": tn, "unresolved": True,
                                "randomSpawn": under_random_spawn, "layer": node_layer,
                                "piece": node_piece})
                    continue
                enabled = bool(getattr(c, "m_Enabled", True))
                is_trigger = bool(getattr(c, "m_IsTrigger", False))
                corners, unresolved = collider_world_corners(tn, c, world_mat)
                if unresolved:
                    out.append({"node": name, "kind": tn, "unresolved": True,
                                "active": active, "enabled": enabled, "trigger": is_trigger,
                                "randomSpawn": under_random_spawn, "layer": node_layer,
                                "piece": node_piece})
                    continue
                xs = [p[0] for p in corners]
                zs = [p[2] for p in corners]
                cx = (min(xs) + max(xs)) / 2.0
                cz = (min(zs) + max(zs)) / 2.0
                hx = (max(xs) - min(xs)) / 2.0 + INFLATE
                hz = (max(zs) - min(zs)) / 2.0 + INFLATE
                out.append({
                    "node": name, "kind": tn, "unresolved": False,
                    "cx": round(cx, 4), "cz": round(cz, 4),
                    "halfX": round(hx, 4), "halfZ": round(hz, 4),
                    "active": active, "enabled": enabled, "trigger": is_trigger,
                    "randomSpawn": under_random_spawn, "layer": node_layer,
                    "piece": node_piece,
                })
    child_under_rs = under_random_spawn or this_random_spawn
    for ch in (getattr(tr, "m_Children", None) or []):
        cht = vp.deref(ch)
        if cht is not None:
            # RandomSpawn branches: recurse into ALL children regardless of active state so
            # every possible branch collider is unioned (we clear all of them, conservatively).
            walk(cht, world_mat, out, active, child_under_rs, node_piece)


def get_root_tr(prefab):
    idx = vp.load_index()
    locs = idx["prefabs"].get(prefab)
    if not locs:
        return None
    loc = next((l for l in locs if l["root"]), locs[0])
    env = _env_for(idx, loc["file"])
    for obj in env.objects:
        if obj.type.name != "GameObject":
            continue
        d = obj.read()
        if getattr(d, "m_Name", "") != prefab:
            continue
        tr = vp.deref(getattr(d, "m_Transform", None))
        if tr is not None and vp.deref(getattr(tr, "m_Father", None)) is None:
            return tr
    return None


# When HOMESTEAD_ALL_BUNDLES=1, load EVERY client bundle into one UnityPy environment so
# cross-bundle external (CAB) references — notably MeshCollider meshes, which live in a
# different bundle than the WoodHouse prefab — resolve. This is required to bound mesh
# colliders (R6: conservative mesh bounds, never silently discard). Without it, meshes
# deref to None on the split-bundle load and the host fails closed.
_ALL_ENV = None


def _env_for(idx, target_file):
    global _ALL_ENV
    if os.environ.get("HOMESTEAD_ALL_BUNDLES") != "1":
        return vp._resolve_env(idx["data_dir"], target_file)
    if _ALL_ENV is None:
        import glob
        bundles = sorted(glob.glob(os.path.join(
            idx["data_dir"], "StreamingAssets", "SoftRef", "Bundles", "*")))
        for shared in ("resources.assets", "sharedassets0.assets"):
            p = os.path.join(idx["data_dir"], shared)
            if os.path.isfile(p):
                bundles.append(p)
        sys.stderr.write("loading %d client bundles into one env (mesh resolution)...\n" % len(bundles))
        _ALL_ENV = UnityPy.load(*bundles)
    return _ALL_ENV


def canonical_hash(footprints):
    # ONE canonical schema, identical to HomesteadGeometryHash.Compute in C#:
    #   sorted rows of "{cx:0.0000}|{cz:0.0000}|{halfX:0.0000}|{halfZ:0.0000}", '\n'-joined, SHA-256 hex.
    def z(value):
        # Python preserves the sign bit for -0.0 while Mono does not. Canonicalize zero explicitly.
        return 0.0 if value == 0.0 else value
    rows = sorted("%.4f|%.4f|%.4f|%.4f" %
                  (z(f["cx"]), z(f["cz"]), z(f["halfX"]), z(f["halfZ"]))
                  for f in footprints)
    return hashlib.sha256("\n".join(rows).encode("utf-8")).hexdigest().upper()


def extract(prefab):
    tr = get_root_tr(prefab)
    if tr is None:
        return {"prefab": prefab, "error": "no root transform"}
    out = []
    # Normalize the host root to local origin: invert the root's own translation so every
    # footprint is expressed relative to the host origin (the in-game location instance
    # position), independent of authoring layout. Root rotation/scale are baked into children.
    root_t = v3f(getattr(tr, "m_LocalPosition", None))
    base = mat_translate((-root_t[0], -root_t[1], -root_t[2]))
    walk(tr, base, out, True)

    unresolved = [c for c in out if c.get("unresolved")]
    # Load-bearing = enabled, non-trigger colliders that are EITHER active in the base branch OR sit under a
    # RandomSpawn ancestor (in which case active=false only reflects the authored default branch; the live
    # world may pick that branch, so we conservatively UNION it). This is the R7 Blocker-3 correction: a
    # RandomSpawn alternative is no longer dropped solely because active=false.
    load_bearing = [c for c in out
                    if not c.get("unresolved") and c["enabled"] and not c["trigger"]
                    and (c["active"] or c.get("randomSpawn"))]
    result = {
        "prefab": prefab,
        "colliderCount": len(load_bearing),
        "rawColliderCount": len([c for c in out if not c.get("unresolved")]),
        "unresolvedCount": len(unresolved),
        "randomSpawnColliderCount": len([c for c in out if c.get("randomSpawn") and not c.get("unresolved")]),
        "colliders": [{"cx": c["cx"], "cz": c["cz"], "halfX": c["halfX"], "halfZ": c["halfZ"]}
                      for c in load_bearing],
    }
    # Fail closed on an unresolvable collider on an ORDINARY host (a house we must seat by
    # catalog): the geometry is incomplete, so the host must not ship a partial footprint.
    if unresolved:
        result["error"] = "unresolved colliders: %d (mesh bounds missing?)" % len(unresolved)
    result["semanticHash"] = canonical_hash(load_bearing)
    return result


def main():
    hosts = sys.argv[1:] or HOSTS
    result = {
        "schema": "niflheim-homestead-static-geometry-v3",
        "inflate": INFLATE,
        "note": "Host-local axis-aligned XZ collider footprints from FULL transform matrices "
                "(rotation + nonuniform/negative scale), capsule direction/height, sphere SCALED "
                "radius (r*max(|sx|,|sy|,|sz|), center transformed then axis-aligned expansion), "
                "conservative mesh bounds or fail-closed. RandomSpawn branches unioned regardless of "
                "active state (conservative); nearest Piece ancestry + GameObject layer recorded. "
                "Load-bearing = enabled, non-trigger, and (active OR under a RandomSpawn ancestor). "
                "Canonical hash == HomesteadGeometryHash. Offline base-game read (ADR-0001).",
        "hosts": {h: extract(h) for h in hosts},
    }
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
