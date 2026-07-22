#!/usr/bin/env python3
"""
check-qa-dependency-boundary — enforce AT-QA-NO-PRODUCT-REF (ADR-0009 §1, §7).

The QA test-helper subsystem (`qa/**`) and the product assemblies (`src/SBPR.*`)
must not depend on each other in EITHER direction:

  (a) No helper/QA project may reference a `src/SBPR.*` product project or DLL.
  (b) No product project may reference / import / include anything under `qa/**`.

This is the structural firewall that keeps the harness out of the product trust
boundary and out of the shipped modpack. It is parsed STRUCTURALLY from the MSBuild
project XML (ProjectReference/Reference/Import/Compile/@Include|@HintPath|@Project),
NOT by a single blind filename grep — so a rename or a sneaky <Import> can't slip a
cross-dependency past the gate.

Exit 0 if the boundary holds, non-zero (listing every violation) otherwise.
Runnable locally and in CI. Pure stdlib; no build required.
"""
from __future__ import annotations

import os
import re
import sys
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
QA_DIR = os.path.join(REPO, "qa")
SRC_DIR = os.path.join(REPO, "src")

# MSBuild attributes that name another project/assembly/file.
_PATH_ATTRS = ("Include", "HintPath", "Project")
# Product-assembly identity: the src/SBPR.* projects and their output DLLs.
_PRODUCT_ASM = re.compile(r"SBPR\.(Trailborne|Trailborne\.Core|Niflheim\.HomesteadStones)\b", re.IGNORECASE)
# Any reference into the qa/ subtree.
_QA_PATH = re.compile(r"(^|[\\/])qa[\\/]", re.IGNORECASE)


def _iter_projects(root: str):
    """Yield every MSBuild project/props file under `root`."""
    for dirpath, _dirs, files in os.walk(root):
        for f in files:
            if f.endswith((".csproj", ".props", ".targets")):
                yield os.path.join(dirpath, f)


def _attr_values(project_path: str):
    """Yield (element_tag, attr_name, attr_value) for path-bearing attributes."""
    try:
        tree = ET.parse(project_path)
    except ET.ParseError as exc:  # malformed project => loud failure, not silent pass
        raise SystemExit(f"[boundary] cannot parse {os.path.relpath(project_path, REPO)}: {exc}")
    for el in tree.iter():
        tag = el.tag.split("}")[-1]  # strip any namespace
        for attr in _PATH_ATTRS:
            val = el.attrib.get(attr)
            if val:
                yield tag, attr, val


def check() -> list[str]:
    violations: list[str] = []

    # (a) qa/** must not reference src/SBPR.* product assemblies.
    if os.path.isdir(QA_DIR):
        for proj in _iter_projects(QA_DIR):
            rel = os.path.relpath(proj, REPO)
            for tag, attr, val in _attr_values(proj):
                # A reference is a product dependency only if it points at src/ or
                # names a product assembly identity via ProjectReference/Reference.
                norm = val.replace("\\", "/")
                names_product = _PRODUCT_ASM.search(val) is not None
                points_at_src = "/src/" in ("/" + norm) or norm.startswith("src/") or "../src/" in norm
                if tag in ("ProjectReference", "Reference") and (names_product or points_at_src):
                    violations.append(
                        f"[AT-QA-NO-PRODUCT-REF] {rel}: <{tag} {attr}=\"{val}\"> "
                        f"references a product assembly (src/SBPR.*)"
                    )
                elif tag == "Import" and points_at_src and "SBPR." in val:
                    violations.append(
                        f"[AT-QA-NO-PRODUCT-REF] {rel}: <Import {attr}=\"{val}\"> imports product build metadata"
                    )

    # (b) src/** product projects must not reference / import / include qa/**.
    if os.path.isdir(SRC_DIR):
        for proj in _iter_projects(SRC_DIR):
            rel = os.path.relpath(proj, REPO)
            for tag, attr, val in _attr_values(proj):
                if _QA_PATH.search(val):
                    violations.append(
                        f"[AT-QA-NO-PRODUCT-REF] {rel}: <{tag} {attr}=\"{val}\"> reaches into qa/**"
                    )

    return violations


def main() -> int:
    violations = check()
    if violations:
        print(f"check-qa-dependency-boundary: {len(violations)} violation(s):\n")
        for v in violations:
            print("  " + v)
        return 1
    print("check-qa-dependency-boundary: OK — qa/** and src/SBPR.* are dependency-disjoint (both directions).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
