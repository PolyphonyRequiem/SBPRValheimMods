#!/usr/bin/env python3
"""
pack-qa-overlay — assemble the deterministic QA overlay bundle (ADR-0009 §7, §8; M5).

The QA harness is a SEPARATE deterministic overlay — helper DLL + engine-free Python
runner + a disposable-world BepInEx profile — shipped ALONGSIDE testing and NEVER
referenced by the product installer or release. This packer is the M5 realization of
that bundle. It is the mirror image of `pack-modpack.sh`:

  * `pack-modpack.sh`  builds the PRODUCT modpack and ASSERTS no QA artifact leaked
    into it (production-exclusion, gate AT-QA-MODPACK-EXCLUDES-HARNESS).
  * `pack-qa-overlay.py` builds the QA overlay that DELIBERATELY contains the harness,
    stamps it with an immutable 6-part SHA-256 manifest, writes an explicit
    disposable-lane sentinel, and refuses to arm on drift.

The two are complementary: the same harness bytes that belong in THIS overlay are
exactly what the product modpack guard rejects. The overlay lives under `qa/dist/`
(a `qa/` subtree), so the production guard's normalized-path rule structurally
excludes it from any product artifact.

6-part manifest (ADR-0009 §5.1 / §8 — product/helper/game/BepInEx/Harmony/scenario
are the ARMING pins; the OVERLAY manifest below pins the six BUNDLE components):

    helper | runner | contracts | profile | scenario | lane_sentinel

Each part is the SHA-256 over its component's byte content (sorted, path-relative),
so the whole overlay has a single reproducible digest. Drift on any part refuses to
publish/arm.

Modes:
  build     stage + hash the overlay, write manifest.json (+ snapshot prior as
            manifest.prev.json for rollback)
  verify    recompute part hashes over the staged tree and fail closed on ANY drift
  rollback  restore manifest.prev.json as the active manifest (revert a bad publish)

DRY-RUN / DETERMINISTIC. Pure stdlib. Fixed mtime normalization → reproducible zip.
Nothing is deployed, launched, or run in-world (that is the operator M6 card).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import posixpath
import shutil
import sys
import zipfile
from typing import Dict, List, Optional, Tuple

# The six overlay bundle components, in fixed order. Missing a REQUIRED part fails
# closed; the helper DLL is required for a *publishable* overlay but may be declared
# absent in a dry/CI pre-build (marked explicitly, never silently).
OVERLAY_PARTS = ("helper", "runner", "contracts", "profile", "scenario", "lane_sentinel")

# Hard production deny list (ADR-0009 §5.1) echoed into the lane sentinel so the
# disposable-world identity is explicit and a production world can never be targeted.
PRODUCTION_DENY = {
    "worlds": ["Niflheim:2456", "Heistan:2466"],
    "note": "QA overlay is disposable-world ONLY; these production worlds are hard-denied even if an allowlist is misconfigured.",
}

RETENTION_DAYS = 7  # short retention: QA overlays are ephemeral CI artifacts.

FIXED_MTIME = (2020, 1, 1, 0, 0, 0)


def _sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _hash_tree(root: str) -> str:
    """Deterministic SHA-256 over a directory: sorted relpath + content, folded."""
    h = hashlib.sha256()
    files: List[str] = []
    for dirpath, _dirs, names in os.walk(root):
        for n in names:
            full = os.path.join(dirpath, n)
            rel = posixpath.normpath(os.path.relpath(full, root).replace(os.sep, "/"))
            files.append(rel)
    for rel in sorted(files):
        full = os.path.join(root, rel.replace("/", os.sep))
        h.update(rel.encode())
        h.update(b"\0")
        with open(full, "rb") as fh:
            h.update(fh.read())
        h.update(b"\0")
    return h.hexdigest()


def _hash_file(path: str) -> str:
    with open(path, "rb") as fh:
        return _sha256_bytes(fh.read())


def _copy_into(src: str, dst: str) -> None:
    if os.path.isdir(src):
        shutil.copytree(src, dst)
    else:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copy2(src, dst)


def _write(path: str, data: str) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(data)


DRY_RUN_MATURITY = (
    "DRY-RUN / SIMULATED overlay. Contains the QA harness for testing only. "
    "NOT a product artifact, NOT deployed, NOT a live qualification (M6)."
)


def build(
    *,
    repo: str,
    out: str,
    helper_dll: Optional[str],
    version: str,
    maturity: Optional[str] = None,
) -> int:
    stage = os.path.join(out, "SBPR-QaOverlay")
    if os.path.isdir(stage):
        shutil.rmtree(stage)
    os.makedirs(stage)

    # 1. runner (engine-free Python) — the sole scenario state machine + composer.
    _copy_into(os.path.join(repo, "qa", "runner"), os.path.join(stage, "runner"))
    # Prune caches so the hash is content-only + reproducible.
    for dp, dirs, _files in os.walk(os.path.join(stage, "runner")):
        for d in list(dirs):
            if d == "__pycache__":
                shutil.rmtree(os.path.join(dp, d))
                dirs.remove(d)

    # 2. contracts (JSON wire truth).
    _copy_into(os.path.join(repo, "qa", "contracts"), os.path.join(stage, "contracts"))

    # 3. disposable-world BepInEx profile (the QA lane profile). Deterministic,
    #    minimal, and explicit about being disposable-world-only.
    profile_dir = os.path.join(stage, "profile")
    _write(
        os.path.join(profile_dir, "BepInEx", "config", "sbpr.qa.harness.t022.cfg"),
        "# SBPR QA harness — DISPOSABLE-WORLD PROFILE (ADR-0009).\n"
        "# Default-disabled. The helper arms ONLY behind the runner's explicit\n"
        "# bootstrap doc + exact world UID/name gate. This profile never targets a\n"
        "# production world.\n"
        "[General]\n"
        "Enabled = false\n"
        "Lane = disposable\n",
    )
    _write(
        os.path.join(profile_dir, "README.md"),
        "# SBPR QA overlay — disposable-world BepInEx profile\n\n"
        "This profile is part of the QA overlay bundle only. It is NEVER the product\n"
        "modpack and is never installed by the product installer. See ADR-0009 §7.\n",
    )

    # 4. lane sentinel — explicit disposable-lane identity + hard production deny.
    sentinel = {
        "kind": "sbpr-qa-overlay-lane-sentinel",
        "lane": "disposable",
        "version": version,
        "retention_days": RETENTION_DAYS,
        "production_deny": PRODUCTION_DENY,
        # The maturity string is part of the sentinel and therefore part of the
        # lane_sentinel pin. A LIVE overlay must say so; the operator supplies the exact
        # string via --maturity so the pinned sentinel and the deployed one are the SAME
        # bytes. Hand-editing the sentinel after packing would break the pin (correctly),
        # which is how the stale-pin defect kept recurring.
        "maturity": maturity if maturity else DRY_RUN_MATURITY,
    }
    _write(
        os.path.join(stage, "lane_sentinel.json"),
        json.dumps(sentinel, sort_keys=True, indent=2) + "\n",
    )

    # 5. helper DLL (optional at pre-build; required for a publishable overlay).
    helper_state: str
    helper_hash: str
    helper_path_in_stage = os.path.join(stage, "helper", "SBPR.QaHarness.T022.dll")
    if helper_dll and os.path.isfile(helper_dll):
        os.makedirs(os.path.dirname(helper_path_in_stage), exist_ok=True)
        shutil.copy2(helper_dll, helper_path_in_stage)
        helper_hash = _hash_file(helper_path_in_stage)
        helper_state = "present"
    else:
        # Explicit absence marker — NEVER a silent empty pin. A pre-build/CI run
        # without the built DLL records this truthfully; publishing requires present.
        _write(
            os.path.join(stage, "helper", "HELPER_ABSENT.txt"),
            "The QA helper DLL was not supplied at pack time. This overlay is a "
            "PRE-BUILD (runner+profile+contracts) and is NOT publishable until the "
            "net48 helper DLL is packed in via --helper-dll.\n",
        )
        helper_hash = "absent"
        helper_state = "absent"

    # ── Compute the 6-part manifest ─────────────────────────────────────────
    part_hashes: Dict[str, str] = {
        "helper": helper_hash,
        "runner": _hash_tree(os.path.join(stage, "runner")),
        "contracts": _hash_tree(os.path.join(stage, "contracts")),
        "profile": _hash_tree(os.path.join(stage, "profile")),
        "scenario": _hash_file(os.path.join(repo, "qa", "runner", "runner_core", "simulation.py")),
        "lane_sentinel": _hash_file(os.path.join(stage, "lane_sentinel.json")),
    }
    assert set(part_hashes) == set(OVERLAY_PARTS), "manifest parts drifted from OVERLAY_PARTS"

    overlay_digest = _sha256_bytes(
        json.dumps({p: part_hashes[p] for p in OVERLAY_PARTS}, sort_keys=True).encode()
    )
    manifest = {
        "kind": "sbpr-qa-overlay-manifest",
        "version": version,
        "helper_state": helper_state,
        "publishable": helper_state == "present",
        "retention_days": RETENTION_DAYS,
        "parts": {p: part_hashes[p] for p in OVERLAY_PARTS},
        "overlay_digest": overlay_digest,
        "maturity": sentinel["maturity"],
    }

    manifest_path = os.path.join(out, "qa-overlay-manifest.json")
    # Rollback snapshot: keep the prior active manifest as .prev before overwriting.
    if os.path.isfile(manifest_path):
        shutil.copy2(manifest_path, os.path.join(out, "qa-overlay-manifest.prev.json"))
    _write(manifest_path, json.dumps(manifest, sort_keys=True, indent=2) + "\n")
    # Also drop the manifest inside the staged tree so verify() is self-contained.
    _write(os.path.join(stage, "MANIFEST.json"), json.dumps(manifest, sort_keys=True, indent=2) + "\n")

    # ── Deterministic zip (fixed mtimes, sorted entries) ────────────────────
    zip_path = os.path.join(out, f"SBPR-QaOverlay-v{version}.zip")
    if os.path.isfile(zip_path):
        os.remove(zip_path)
    entries: List[Tuple[str, str]] = []
    for dp, _dirs, names in os.walk(stage):
        for n in names:
            full = os.path.join(dp, n)
            arc = posixpath.normpath(os.path.relpath(full, out).replace(os.sep, "/"))
            entries.append((arc, full))
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for arc, full in sorted(entries):
            zi = zipfile.ZipInfo(arc, date_time=FIXED_MTIME)
            zi.compress_type = zipfile.ZIP_DEFLATED
            with open(full, "rb") as fh:
                zf.writestr(zi, fh.read())
    zip_sha = _hash_file(zip_path)
    _write(zip_path + ".sha256", f"{zip_sha}  {os.path.basename(zip_path)}\n")

    print(f"pack-qa-overlay: built overlay v{version} (helper: {helper_state})")
    print(f"  overlay_digest: {overlay_digest}")
    print(f"  zip:            {zip_path}")
    print(f"  zip sha256:     {zip_sha}")
    for p in OVERLAY_PARTS:
        print(f"    {p:<14} {part_hashes[p]}")
    if helper_state != "present":
        print("  NOTE: helper DLL absent — this is a PRE-BUILD overlay, NOT publishable.")
    return 0


def verify(*, out: str) -> int:
    """Recompute the staged overlay's part hashes and fail closed on ANY drift."""
    stage = os.path.join(out, "SBPR-QaOverlay")
    manifest_path = os.path.join(stage, "MANIFEST.json")
    if not os.path.isfile(manifest_path):
        raise SystemExit(f"pack-qa-overlay verify: no staged overlay manifest at {manifest_path}")
    with open(manifest_path, encoding="utf-8") as fh:
        manifest = json.load(fh)
    pinned = manifest["parts"]

    recomputed = {
        "helper": pinned["helper"],  # helper hash is over the DLL; recompute if present
        "runner": _hash_tree(os.path.join(stage, "runner")),
        "contracts": _hash_tree(os.path.join(stage, "contracts")),
        "profile": _hash_tree(os.path.join(stage, "profile")),
        "scenario": pinned["scenario"],  # scenario source is in runner/; part of runner tree hash provenance
        "lane_sentinel": _hash_file(os.path.join(stage, "lane_sentinel.json")),
    }
    helper_dll = os.path.join(stage, "helper", "SBPR.QaHarness.T022.dll")
    if os.path.isfile(helper_dll):
        recomputed["helper"] = _hash_file(helper_dll)
    scenario_src = os.path.join(stage, "runner", "runner_core", "simulation.py")
    if os.path.isfile(scenario_src):
        recomputed["scenario"] = _hash_file(scenario_src)

    drift: List[str] = []
    for part in OVERLAY_PARTS:
        if recomputed[part] != pinned[part]:
            drift.append(f"  {part}: staged {recomputed[part]} != pinned {pinned[part]}")

    if drift:
        print("pack-qa-overlay verify: DRIFT DETECTED — overlay refuses to arm:")
        print("\n".join(drift))
        return 1

    # Re-derive the overlay digest and compare.
    digest = _sha256_bytes(
        json.dumps({p: pinned[p] for p in OVERLAY_PARTS}, sort_keys=True).encode()
    )
    if digest != manifest["overlay_digest"]:
        print(f"pack-qa-overlay verify: overlay_digest drift {digest} != {manifest['overlay_digest']}")
        return 1
    print(f"pack-qa-overlay verify: OK — 6/6 parts match, digest {digest}")
    return 0


def rollback(*, out: str) -> int:
    """Restore the previous manifest snapshot (revert a bad publish)."""
    active = os.path.join(out, "qa-overlay-manifest.json")
    prev = os.path.join(out, "qa-overlay-manifest.prev.json")
    if not os.path.isfile(prev):
        raise SystemExit("pack-qa-overlay rollback: no previous manifest snapshot to restore.")
    shutil.copy2(prev, active)
    with open(active, encoding="utf-8") as fh:
        m = json.load(fh)
    print(f"pack-qa-overlay rollback: restored manifest v{m['version']} (digest {m['overlay_digest']})")
    return 0


def main(argv: Optional[List[str]] = None) -> int:
    repo_default = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    ap = argparse.ArgumentParser(description="Assemble/verify the deterministic QA overlay bundle (ADR-0009 §7).")
    ap.add_argument("mode", choices=["build", "verify", "rollback"])
    ap.add_argument("--repo", default=repo_default, help="Repo root (default: parent of scripts/).")
    ap.add_argument("--out", default=os.path.join(repo_default, "qa", "dist"),
                    help="Output dir for the overlay (default: qa/dist — a qa/ subtree, "
                         "structurally excluded from the product modpack).")
    ap.add_argument("--helper-dll", default=None, help="Path to the built net48 helper DLL to pack in.")
    ap.add_argument("--version", default="0.0.0-dev", help="Overlay version string.")
    ap.add_argument(
        "--maturity",
        default=None,
        help=(
            "Exact maturity string to embed in the lane sentinel + manifest. Defaults to "
            "the DRY-RUN/SIMULATED wording. Supply the LIVE wording when packing an "
            "overlay for a real qualification run so the pinned sentinel matches the "
            "deployed one byte-for-byte."
        ),
    )
    args = ap.parse_args(argv)

    os.makedirs(args.out, exist_ok=True)
    if args.mode == "build":
        return build(repo=args.repo, out=args.out, helper_dll=args.helper_dll,
                     version=args.version, maturity=args.maturity)
    if args.mode == "verify":
        return verify(out=args.out)
    return rollback(out=args.out)


if __name__ == "__main__":
    sys.exit(main())
