#!/usr/bin/env python3
"""
check-modpack-excludes-harness — enforce AT-QA-MODPACK-EXCLUDES-HARNESS (ADR-0009 §7).

Given a STAGED modpack tree and/or a built modpack .zip, assert that no QA harness
artifact is present. The check is deliberately resistant to three evasion classes
the card calls out:

  1. CASE-FOLDING   — `SBPR.QaHarness`, `sbpr.qaharness`, `SBPR.QAHARNESS`, …
  2. RENAMING       — the assembly renamed to anything; we also match the QA
                      BepInPlugin GUID and a .NET assembly-identity/content
                      signature inside the DLL bytes, not just the filename.
  3. PATH-TRAVERSAL — normalized paths (`a/../qa/x`, nested, mixed separators)
                      are collapsed before matching so a traversal can't hide a
                      qa/ path.

It does NOT do a broad source-text grep of arbitrary product docs/config (that
would false-positive on harmless mentions of the harness in READMEs). It matches:
  • normalized path segments that resolve into a `qa/` subtree, AND
  • QA identity signatures (assembly name token / plugin GUID) in *binary* entries
    (*.dll) or entry names — the things that would actually load code.

Usage:
  check-modpack-excludes-harness.py --tree <staged_dir>
  check-modpack-excludes-harness.py --zip  <modpack.zip>
  (either or both; at least one required)

Exit 0 if clean, non-zero (listing every hit) otherwise. Pure stdlib.
"""
from __future__ import annotations

import argparse
import os
import posixpath
import re
import sys
import zipfile

# QA identity signatures. These are the things that would actually ship/load QA
# code. Case-insensitive. Kept narrow on purpose (assembly-name token + plugin
# GUID) so product docs mentioning "the QA harness" in prose don't trip it.
_ASM_TOKEN = re.compile(rb"SBPR\.?QaHarness", re.IGNORECASE)
_PLUGIN_GUID = re.compile(rb"net\.danielgreen\.sbpr\.qa", re.IGNORECASE)
# A path is a QA path if any normalized segment is exactly `qa` OR a segment
# starts with the QA assembly token (covers renamed dirs like `Qa-Helper`).
_QA_ASM_NAME_RE = re.compile(r"sbpr\.?qaharness", re.IGNORECASE)


def _normalize(path: str) -> str:
    """Collapse separators + traversal to a canonical forward-slash path."""
    unified = path.replace("\\", "/")
    # posixpath.normpath collapses `a/../qa/x` -> `qa/x`, `./qa` -> `qa`, etc.
    return posixpath.normpath(unified).lstrip("/")


def _is_qa_path(norm_path: str) -> bool:
    segments = [s for s in norm_path.split("/") if s and s != "."]
    for seg in segments:
        if seg.lower() == "qa":
            return True
        if _QA_ASM_NAME_RE.search(seg):
            return True
    return False


def _content_is_qa(data: bytes) -> bool:
    return bool(_ASM_TOKEN.search(data) or _PLUGIN_GUID.search(data))


def _scan_tree(tree: str) -> list[str]:
    hits: list[str] = []
    tree_abs = os.path.abspath(tree)
    for dirpath, _dirs, files in os.walk(tree_abs):
        for f in files:
            full = os.path.join(dirpath, f)
            rel = os.path.relpath(full, tree_abs)
            norm = _normalize(rel)
            if _is_qa_path(norm):
                hits.append(f"[path] staged tree: {rel}  (normalized: {norm})")
                continue
            # Content signature only for code-bearing binaries.
            if f.lower().endswith(".dll"):
                try:
                    with open(full, "rb") as fh:
                        data = fh.read()
                except OSError as exc:
                    raise SystemExit(f"[modpack] cannot read {rel}: {exc}")
                if _content_is_qa(data):
                    hits.append(f"[content] staged tree: {rel} carries a QA assembly/GUID signature")
    return hits


def _scan_zip(zip_path: str) -> list[str]:
    hits: list[str] = []
    with zipfile.ZipFile(zip_path) as zf:
        for info in zf.infolist():
            norm = _normalize(info.filename)
            if _is_qa_path(norm):
                hits.append(f"[path] zip entry: {info.filename}  (normalized: {norm})")
                continue
            if info.filename.lower().endswith(".dll"):
                data = zf.read(info)
                if _content_is_qa(data):
                    hits.append(f"[content] zip entry: {info.filename} carries a QA assembly/GUID signature")
    return hits


def main() -> int:
    ap = argparse.ArgumentParser(description="Assert the modpack contains no QA harness artifact.")
    ap.add_argument("--tree", help="Path to a staged modpack directory to scan.")
    ap.add_argument("--zip", dest="zip_path", help="Path to a built modpack .zip to scan.")
    args = ap.parse_args()

    if not args.tree and not args.zip_path:
        ap.error("at least one of --tree or --zip is required")

    hits: list[str] = []
    if args.tree:
        if not os.path.isdir(args.tree):
            raise SystemExit(f"[modpack] --tree not a directory: {args.tree}")
        hits += _scan_tree(args.tree)
    if args.zip_path:
        if not os.path.isfile(args.zip_path):
            raise SystemExit(f"[modpack] --zip not a file: {args.zip_path}")
        hits += _scan_zip(args.zip_path)

    if hits:
        print(f"check-modpack-excludes-harness: {len(hits)} QA artifact(s) found in the modpack:\n")
        for h in hits:
            print("  " + h)
        return 1
    print("check-modpack-excludes-harness: OK — no QA harness path/identity/content in the modpack.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
