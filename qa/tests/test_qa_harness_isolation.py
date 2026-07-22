#!/usr/bin/env python3
"""
QA-harness M0 isolation guard tests (ADR-0009 §1, §7).

Named acceptance gates proven here:
  • AT-QA-NO-PRODUCT-REF          — qa/** and src/SBPR.* are dependency-disjoint,
                                     both directions (via structural project XML
                                     parsing, not a blind grep).
  • AT-QA-MODPACK-EXCLUDES-HARNESS — the modpack (staged tree + zip) contains no QA
                                     harness path/identity/content, resistant to
                                     case-folding, renaming, nested paths, and
                                     path-traversal evasion.

Stdlib `unittest` only — runs in CI with no pip install (`python3 -m unittest`),
and also under pytest. Positive checks run the real guard scripts against the real
repo; adversarial NEGATIVE fixtures build poisoned trees/zips in a tmp dir and
assert the guard DETECTS every evasion variant.
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import unittest
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPTS = os.path.join(REPO, "scripts")
DEP_GUARD = os.path.join(SCRIPTS, "check-qa-dependency-boundary.py")
PACK_GUARD = os.path.join(SCRIPTS, "check-modpack-excludes-harness.py")


def _run(argv: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run(
        [sys.executable, *argv],
        capture_output=True,
        text=True,
    )


def _write(path: str, data: bytes) -> None:
    """Write bytes to `path`, closing the handle deterministically."""
    with open(path, "wb") as fh:
        fh.write(data)


class DependencyBoundaryTests(unittest.TestCase):
    """AT-QA-NO-PRODUCT-REF."""

    def test_real_repo_boundary_holds(self) -> None:
        """The actual repo must pass the bidirectional dependency guard."""
        cp = _run([DEP_GUARD])
        self.assertEqual(cp.returncode, 0, msg=f"guard failed on clean repo:\n{cp.stdout}\n{cp.stderr}")
        self.assertIn("dependency-disjoint", cp.stdout)


class ModpackExclusionPositiveTests(unittest.TestCase):
    """AT-QA-MODPACK-EXCLUDES-HARNESS — clean tree/zip must pass."""

    def test_clean_tree_passes(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            os.makedirs(os.path.join(d, "BepInEx", "plugins", "SBPR.Trailborne"))
            # A legitimate product DLL stand-in with product identity only.
            _write(
                os.path.join(d, "BepInEx", "plugins", "SBPR.Trailborne", "SBPR.Trailborne.dll"),
                b"MZ fake product assembly SBPR.Trailborne\x00",
            )
            cp = _run([PACK_GUARD, "--tree", d])
            self.assertEqual(cp.returncode, 0, msg=cp.stdout + cp.stderr)

    def test_product_readme_mentioning_harness_is_not_flagged(self) -> None:
        """A harmless prose mention of the harness in a product doc must NOT trip."""
        with tempfile.TemporaryDirectory() as d:
            _write(os.path.join(d, "README.md"), b"This modpack never ships the SBPR QA Harness. See ADR-0009.\n")
            cp = _run([PACK_GUARD, "--tree", d])
            self.assertEqual(cp.returncode, 0, msg=cp.stdout + cp.stderr)


class ModpackExclusionAdversarialTests(unittest.TestCase):
    """AT-QA-MODPACK-EXCLUDES-HARNESS — every evasion variant must be DETECTED."""

    def _assert_tree_detected(self, build) -> None:
        with tempfile.TemporaryDirectory() as d:
            build(d)
            cp = _run([PACK_GUARD, "--tree", d])
            self.assertEqual(cp.returncode, 1, msg=f"guard MISSED evasion:\n{cp.stdout}\n{cp.stderr}")

    def test_plain_qa_path(self) -> None:
        def build(d: str) -> None:
            p = os.path.join(d, "BepInEx", "plugins", "qa", "SBPR.QaHarness.T022")
            os.makedirs(p)
            _write(os.path.join(p, "SBPR.QaHarness.T022.dll"), b"MZ\x00")
        self._assert_tree_detected(build)

    def test_renamed_dll_content_signature(self) -> None:
        """DLL renamed to look innocent, but its bytes carry the QA identity."""
        def build(d: str) -> None:
            p = os.path.join(d, "BepInEx", "plugins", "Helper")
            os.makedirs(p)
            # Renamed to 'totally-legit.dll' but contains the assembly token + GUID.
            _write(
                os.path.join(p, "totally-legit.dll"),
                b"MZ\x00...SBPR.QaHarness.T022...net.danielgreen.sbpr.qa.harness.t022...",
            )
        self._assert_tree_detected(build)

    def test_mixed_case_path(self) -> None:
        def build(d: str) -> None:
            p = os.path.join(d, "BepInEx", "plugins", "Sbpr.QAHarness")
            os.makedirs(p)
            _write(os.path.join(p, "x.dll"), b"MZ\x00")
        self._assert_tree_detected(build)

    def test_nested_qa_path(self) -> None:
        def build(d: str) -> None:
            p = os.path.join(d, "a", "b", "c", "qa", "d")
            os.makedirs(p)
            _write(os.path.join(p, "readme.txt"), b"nested")
        self._assert_tree_detected(build)

    def test_zip_path_traversal_entry(self) -> None:
        """A zip entry using ../ traversal that resolves into a qa/ path."""
        with tempfile.TemporaryDirectory() as d:
            zp = os.path.join(d, "modpack.zip")
            with zipfile.ZipFile(zp, "w") as zf:
                zf.writestr("BepInEx/plugins/../../qa/SBPR.QaHarness.T022.dll", b"MZ\x00")
            cp = _run([PACK_GUARD, "--zip", zp])
            self.assertEqual(cp.returncode, 1, msg=cp.stdout + cp.stderr)

    def test_zip_renamed_content_signature(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            zp = os.path.join(d, "modpack.zip")
            with zipfile.ZipFile(zp, "w") as zf:
                zf.writestr(
                    "BepInEx/plugins/Helper/innocent.dll",
                    b"MZ\x00 net.danielgreen.sbpr.qa.harness.t022 \x00",
                )
            cp = _run([PACK_GUARD, "--zip", zp])
            self.assertEqual(cp.returncode, 1, msg=cp.stdout + cp.stderr)


if __name__ == "__main__":
    unittest.main()
