#!/usr/bin/env python3
"""Extract host-local static collider geometry for the Homestead eligible hosts.

Emits a deterministic JSON fixture consumed by the R5 engine-free seat resolver
tests. For each eligible host prefab it records, in HOST-LOCAL space (host root
= origin, no host rotation applied), every Box/Capsule/Sphere collider as an
axis-aligned XZ footprint box: (cx, cz, halfX, halfZ). Per-node local ROTATION
is intentionally ignored for the footprint (conservative AABB), matching the
production probe_house_slots_v2 pipeline and SPIKE 2. Host-level rotation is
applied analytically by the resolver, not here.

Also records a stable semantic hash of the collider inventory (order-independent)
so tests can pin each house's geometry against silent AssetBundle drift.

Fully offline base-game read (ADR-0001). No Physics, no Heightmap.
"""
import os, sys, json, math, hashlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import UnityPy  # noqa
import valheim_prefab as vp

INFLATE = 0.15  # production footprint inflation (matches probe_house_slots_v2)
HOSTS = ["WoodHouse%d" % i for i in range(1, 14)] + ["WoodFarm1", "WoodVillage1"]


def v3(v):
    if v is None:
        return (0.0, 0.0, 0.0)
    return (getattr(v, "x", 0.0), getattr(v, "y", 0.0), getattr(v, "z", 0.0))


def is_active(go):
    # Unity GameObject.m_IsActive gates whether the collider ships live.
    return bool(getattr(go, "m_IsActive", True))


def walk(tr, ppos, out, parent_active):
    go = vp.deref(getattr(tr, "m_GameObject", None))
    lp = getattr(tr, "m_LocalPosition", None)
    lpos = (getattr(lp, "x", 0.0), getattr(lp, "y", 0.0), getattr(lp, "z", 0.0)) if lp else (0, 0, 0)
    wpos = (ppos[0] + lpos[0], ppos[1] + lpos[1], ppos[2] + lpos[2])
    active = parent_active and (is_active(go) if go else True)
    if go:
        name = getattr(go, "m_Name", "?")
        for cp in (getattr(go, "m_Component", None) or []):
            ptr = getattr(cp, "component", None)
            tn = vp.ptr_type(ptr)
            if tn in ("BoxCollider", "CapsuleCollider", "SphereCollider"):
                c = vp.deref(ptr)
                enabled = bool(getattr(c, "m_Enabled", True))
                is_trigger = bool(getattr(c, "m_IsTrigger", False))
                cen = v3(getattr(c, "m_Center", None))
                if tn == "BoxCollider":
                    size = v3(getattr(c, "m_Size", None))
                    hx = abs(size[0]) / 2 + INFLATE
                    hz = abs(size[2]) / 2 + INFLATE
                else:
                    r = getattr(c, "m_Radius", 0.5) + INFLATE
                    hx = hz = r
                out.append({
                    "node": name,
                    "kind": tn,
                    "cx": round(wpos[0] + cen[0], 4),
                    "cz": round(wpos[2] + cen[2], 4),
                    "halfX": round(hx, 4),
                    "halfZ": round(hz, 4),
                    "active": active,
                    "enabled": enabled,
                    "trigger": is_trigger,
                })
    for ch in (getattr(tr, "m_Children", None) or []):
        cht = vp.deref(ch)
        if cht is not None:
            walk(cht, wpos, out, active)


def get_root_tr(prefab):
    idx = vp.load_index()
    locs = idx["prefabs"].get(prefab)
    if not locs:
        return None
    loc = next((l for l in locs if l["root"]), locs[0])
    env = vp._resolve_env(idx["data_dir"], loc["file"])
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


def semantic_hash(colliders):
    # Order-independent: sort canonical tuples, hash the joined string.
    rows = sorted(
        "%s|%.4f|%.4f|%.4f|%.4f|%d" % (c["kind"], c["cx"], c["cz"], c["halfX"], c["halfZ"],
                                        1 if (c["enabled"] and not c["trigger"]) else 0)
        for c in colliders
    )
    return hashlib.sha256("\n".join(rows).encode("utf-8")).hexdigest()


def extract(prefab):
    tr = get_root_tr(prefab)
    if tr is None:
        return {"prefab": prefab, "error": "no root transform"}
    out = []
    # Normalize so the host root sits at local origin: start the walk with the
    # negated root local position, cancelling the root's authored offset so every
    # collider footprint is expressed relative to the host origin (== in-game
    # location instance position), independent of authoring layout in the bundle.
    rlp = getattr(tr, "m_LocalPosition", None)
    root_off = (-getattr(rlp, "x", 0.0), -getattr(rlp, "y", 0.0), -getattr(rlp, "z", 0.0)) if rlp else (0, 0, 0)
    walk(tr, root_off, out, True)
    # Only load-bearing footprint colliders: enabled, non-trigger, active.
    load_bearing = [c for c in out if c["enabled"] and not c["trigger"] and c["active"]]
    return {
        "prefab": prefab,
        "colliderCount": len(load_bearing),
        "rawColliderCount": len(out),
        "semanticHash": semantic_hash(load_bearing),
        "colliders": [
            {"cx": c["cx"], "cz": c["cz"], "halfX": c["halfX"], "halfZ": c["halfZ"]}
            for c in load_bearing
        ],
    }


def main():
    hosts = sys.argv[1:] or HOSTS
    result = {
        "schema": "niflheim-homestead-static-geometry-v1",
        "inflate": INFLATE,
        "note": "Host-local axis-aligned XZ collider footprints (enabled, non-trigger, active). "
                "Per-node rotation ignored (conservative). Offline base-game read (ADR-0001).",
        "hosts": {h: extract(h) for h in hosts},
    }
    print(json.dumps(result, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
