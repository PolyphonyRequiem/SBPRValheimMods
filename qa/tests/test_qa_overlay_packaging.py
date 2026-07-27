#!/usr/bin/env python3
"""
QA overlay packaging tests (ADR-0009 §7, §8; M5).

Proves the deterministic QA overlay bundle contract built by
`scripts/pack-qa-overlay.py`:

  • BUILD writes a 6-part SHA-256 manifest + lane sentinel + deterministic zip.
  • REPRODUCIBLE — two builds of identical inputs produce the same overlay_digest.
  • VERIFY passes on a clean staged overlay and FAILS CLOSED on ANY part drift.
  • ROLLBACK restores the previous manifest snapshot.
  • STRUCTURAL EXCLUSION from the product modpack, two independent guarantees:
      (1) the overlay's output lives under `qa/dist` — a `qa/` subtree that the
          production-exclusion guard rejects by normalized-path rule; and
      (2) a present helper DLL carries the QA identity/content signature, so the
          guard catches it even under a renamed path (rename/case/traversal
          resistance is already covered by test_qa_harness_isolation.py).
  • LANE SENTINEL is explicit disposable-lane + hard production deny list.

Stdlib `unittest` only — runs in CI with no pip (`python3 -m unittest`) and under
pytest. Engine-free / dry-run: no game, network, or live deploy.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPTS = os.path.join(REPO, "scripts")
PACKER = os.path.join(SCRIPTS, "pack-qa-overlay.py")
PACK_GUARD = os.path.join(SCRIPTS, "check-modpack-excludes-harness.py")

OVERLAY_PARTS = ("helper", "runner", "contracts", "profile", "scenario", "lane_sentinel")


def _run(argv: list[str]) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, *argv], capture_output=True, text=True)


class OverlayBuildTests(unittest.TestCase):
    def setUp(self) -> None:
        self.out = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.out, ignore_errors=True)

    def _build(self, version: str = "1.0.0", helper_dll: str | None = None):
        argv = [PACKER, "build", "--out", self.out, "--version", version]
        if helper_dll:
            argv += ["--helper-dll", helper_dll]
        cp = _run(argv)
        self.assertEqual(cp.returncode, 0, msg=cp.stdout + cp.stderr)
        return cp

    def _manifest(self) -> dict:
        with open(os.path.join(self.out, "qa-overlay-manifest.json"), encoding="utf-8") as fh:
            return json.load(fh)

    def test_build_writes_six_part_manifest(self) -> None:
        self._build()
        m = self._manifest()
        self.assertEqual(set(m["parts"]), set(OVERLAY_PARTS))
        self.assertIn("overlay_digest", m)
        self.assertEqual(m["helper_state"], "absent")
        self.assertFalse(m["publishable"])  # no DLL => not publishable

    def test_build_is_reproducible(self) -> None:
        a, b = tempfile.mkdtemp(), tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, a, ignore_errors=True)
        self.addCleanup(shutil.rmtree, b, ignore_errors=True)
        _run([PACKER, "build", "--out", a, "--version", "5.5"])
        _run([PACKER, "build", "--out", b, "--version", "5.5"])
        with open(os.path.join(a, "qa-overlay-manifest.json")) as fa, \
             open(os.path.join(b, "qa-overlay-manifest.json")) as fb:
            da = json.load(fa)["overlay_digest"]
            db = json.load(fb)["overlay_digest"]
        self.assertEqual(da, db)

    def test_verify_passes_clean(self) -> None:
        self._build()
        cp = _run([PACKER, "verify", "--out", self.out])
        self.assertEqual(cp.returncode, 0, msg=cp.stdout + cp.stderr)

    def test_verify_fails_closed_on_part_drift(self) -> None:
        self._build()
        # Tamper a bundled part after the manifest was pinned.
        sentinel = os.path.join(self.out, "SBPR-QaOverlay", "lane_sentinel.json")
        with open(sentinel, "w") as fh:
            fh.write('{"tampered": true}\n')
        cp = _run([PACKER, "verify", "--out", self.out])
        self.assertEqual(cp.returncode, 1, msg="verify MISSED drift")
        self.assertIn("DRIFT DETECTED", cp.stdout)

    def test_verify_detects_runner_drift(self) -> None:
        self._build()
        extra = os.path.join(self.out, "SBPR-QaOverlay", "runner", "SNEAKY.py")
        with open(extra, "w") as fh:
            fh.write("# injected after pin\n")
        cp = _run([PACKER, "verify", "--out", self.out])
        self.assertEqual(cp.returncode, 1, msg="verify MISSED runner-tree drift")

    def test_rollback_restores_prev_manifest(self) -> None:
        self._build(version="1.0.0")
        first_digest = self._manifest()["overlay_digest"]
        self._build(version="2.0.0")  # snapshots the v1 manifest as .prev
        cp = _run([PACKER, "rollback", "--out", self.out])
        self.assertEqual(cp.returncode, 0, msg=cp.stdout + cp.stderr)
        restored = self._manifest()
        self.assertEqual(restored["version"], "1.0.0")
        self.assertEqual(restored["overlay_digest"], first_digest)

    def test_lane_sentinel_is_disposable_with_prod_deny(self) -> None:
        self._build()
        with open(os.path.join(self.out, "SBPR-QaOverlay", "lane_sentinel.json")) as fh:
            s = json.load(fh)
        self.assertEqual(s["lane"], "disposable")
        deny = s["production_deny"]["worlds"]
        self.assertIn("Niflheim:2456", deny)
        self.assertIn("Heistan:2466", deny)


class OverlayExclusionTests(unittest.TestCase):
    """The overlay is structurally excluded from the product modpack (ADR-0009 §7)."""

    def test_default_output_is_a_qa_subtree(self) -> None:
        """The packer's default output dir is under qa/ — the guard's path rule
        rejects any qa/ segment, so the overlay can never enter a product artifact."""
        cp = _run([PACKER, "build", "--help"])
        self.assertIn("qa/dist", cp.stdout)
        # And the guard rejects a qa/dist path if it were ever staged into a pack.
        with tempfile.TemporaryDirectory() as d:
            leaked = os.path.join(d, "BepInEx", "plugins", "qa", "dist")
            os.makedirs(leaked)
            with open(os.path.join(leaked, "MANIFEST.json"), "w") as fh:
                fh.write("{}")
            guard = _run([PACK_GUARD, "--tree", d])
            self.assertEqual(guard.returncode, 1, msg="guard MISSED a leaked qa/ overlay path")

    def test_present_helper_dll_caught_by_content_signature(self) -> None:
        """A helper DLL packed into the overlay carries the QA identity signature,
        so if the overlay ever leaked into a modpack the guard catches the DLL even
        renamed. We synthesize a DLL-named file with the real identity bytes (the
        actual net48 DLL is proven by the CI qa-harness content-signature step)."""
        with tempfile.TemporaryDirectory() as src:
            dll = os.path.join(src, "SBPR.QaHarness.T022.dll")
            with open(dll, "wb") as fh:
                fh.write(b"MZ\x00 SBPR.QaHarness.T022 net.danielgreen.sbpr.qa.harness.t022 \x00")
            out = tempfile.mkdtemp()
            self.addCleanup(shutil.rmtree, out, ignore_errors=True)
            cp = _run([PACKER, "build", "--out", out, "--version", "3.3", "--helper-dll", dll])
            self.assertEqual(cp.returncode, 0, msg=cp.stdout + cp.stderr)
            with open(os.path.join(out, "qa-overlay-manifest.json")) as fh:
                m = json.load(fh)
            self.assertEqual(m["helper_state"], "present")
            self.assertTrue(m["publishable"])
            # Now prove: if that overlay's helper DLL leaked into a pack under a
            # renamed path, the content-signature guard still catches it.
            with tempfile.TemporaryDirectory() as pack:
                renamed = os.path.join(pack, "BepInEx", "plugins", "Innocent")
                os.makedirs(renamed)
                shutil.copy(
                    os.path.join(out, "SBPR-QaOverlay", "helper", "SBPR.QaHarness.T022.dll"),
                    os.path.join(renamed, "totally-legit.dll"),
                )
                guard = _run([PACK_GUARD, "--tree", pack])
                self.assertEqual(guard.returncode, 1, msg="guard MISSED the real helper DLL renamed")


if __name__ == "__main__":
    unittest.main()
