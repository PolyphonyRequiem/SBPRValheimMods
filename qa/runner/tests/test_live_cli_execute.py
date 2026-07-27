"""M6-COMPOSE CLI coverage — `--live` reaches the executor, does NOT defer (ADR-0009 §6).

The exact gap that blocked three M6 attempts: `--live` with all preconditions satisfied
printed "UNLOCKED but not executed here" and returned. These tests load the hyphenated
runner module and prove:

  * `--live` (valid sentinel + verified overlay pins + a run descriptor) reaches the
    injected executor and reports a composed verdict — NOT the old deferral message;
  * the deferral string is GONE from the runner source (regression guard);
  * fail-closed gating survives: bare `--live`, missing inputs, a drifted overlay pin,
    and a non-disposable sentinel each REFUSE (exit 2) without ever calling the executor;
  * `--dry-run` remains the default and works.

The executor is injected as a stub, so nothing launches a game or mutates a file.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import sys

import pytest

RUNNER_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if RUNNER_DIR not in sys.path:
    sys.path.insert(0, RUNNER_DIR)

RUNNER_PY = os.path.join(RUNNER_DIR, "sbpr-qa-t022.py")


def _load_cli():
    spec = importlib.util.spec_from_file_location("sbpr_qa_t022_cli", RUNNER_PY)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _sha256_file(path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        h.update(fh.read())
    return h.hexdigest()


def _write_overlay(tmp_path, *, lane="disposable", include_prod_deny=True, drift=False):
    """Write a sentinel + a self-consistent overlay manifest that the preflight accepts."""
    from runner_core.live_preflight import OVERLAY_PARTS, _fold_digest

    sentinel = {
        "kind": "sbpr-qa-overlay-lane-sentinel",
        "lane": lane,
    }
    if include_prod_deny:
        sentinel["production_deny"] = {"worlds": ["Niflheim:2456", "Heistan:2466"]}
    sentinel_path = tmp_path / "lane_sentinel.json"
    sentinel_path.write_text(json.dumps(sentinel, sort_keys=True, indent=2) + "\n")

    parts = {p: hashlib.sha256(p.encode()).hexdigest() for p in OVERLAY_PARTS}
    # The runner recomputes lane_sentinel from the file it is handed.
    parts["lane_sentinel"] = _sha256_file(str(sentinel_path))
    digest = _fold_digest(parts)
    if drift:
        digest = "0" * 64  # break the folded digest → preflight refuses
    manifest = {
        "kind": "sbpr-qa-overlay-manifest",
        "parts": parts,
        "overlay_digest": digest,
    }
    manifest_path = tmp_path / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n")
    return str(sentinel_path), str(manifest_path)


class _FakeVerdict:
    def __init__(self, verdict="PASS"):
        self.verdict = verdict
        self.passed = verdict == "PASS"
        self.evidence = type("Ev", (), {
            "legs": {"ISSUE": "pass", "UPGRADE": "pass", "TRANSFER": "pass", "TAMPER": "pass"},
            "lease_held": True, "pins_verified": True, "cleanup_confirmed": True,
            "receipts_correlated": 4, "failure_reason": None, "failure_kind": None,
        })()


class _FakeReport:
    def __init__(self, verdict="PASS"):
        self.verdict = _FakeVerdict(verdict)
        self.teardown_completed = True
        self.teardown_errors = []


# --------------------------------------------------------------------------- #
# The one that matters: --live EXECUTES.
# --------------------------------------------------------------------------- #

def test_live_with_all_preconditions_reaches_executor(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    called = {}

    def stub_runner(descriptor_path):
        called["path"] = descriptor_path
        return _FakeReport("PASS")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=stub_runner,
    )
    out = capsys.readouterr().out
    # The executor WAS invoked with the descriptor — the run actually drove.
    assert called["path"] == str(descriptor)
    assert rc == 0
    assert "LIVE-EXECUTE" in out
    assert "verdict: PASS" in out
    assert "teardown: complete" in out
    # The blocking deferral message must NOT appear on the success path.
    assert "UNLOCKED but NOT executed here" not in out
    assert "did not perform a live qualification" not in out


def test_live_executor_failure_reports_fail_not_defer(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=lambda p: _FakeReport("FAIL"),
    )
    out = capsys.readouterr().out
    assert rc == 1
    assert "verdict: FAIL" in out
    assert "UNLOCKED but NOT executed here" not in out


def test_deferral_string_is_gone_from_runner_source() -> None:
    # Regression guard: the exact string that masked the missing executor for three
    # M6 attempts must not survive anywhere in the runner CLI.
    with open(RUNNER_PY, "r", encoding="utf-8") as fh:
        src = fh.read()
    assert "UNLOCKED but NOT executed here" not in src
    assert "it did not perform a live qualification" not in src


# --------------------------------------------------------------------------- #
# Fail-closed gating survives — executor never reached on a refusal.
# --------------------------------------------------------------------------- #

def _never(_path):
    raise AssertionError("executor must not be reached on a refusal")


def test_bare_live_refuses_without_executing(capsys) -> None:
    cli = _load_cli()
    rc = cli.main(["--live"], live_runner=_never)
    assert rc == 2
    assert "REFUSED" in capsys.readouterr().out


def test_live_missing_overlay_refuses(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, _ = _write_overlay(tmp_path)
    rc = cli.main(["--live", "--lane-sentinel", sentinel], live_runner=_never)
    assert rc == 2


def test_live_drifted_overlay_refuses(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path, drift=True)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=_never,
    )
    assert rc == 2
    assert "REFUSED" in capsys.readouterr().out


def test_live_nondisposable_sentinel_refuses(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path, lane="production")
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=_never,
    )
    assert rc == 2


def test_live_unlocked_without_descriptor_refuses_to_execute(tmp_path, capsys) -> None:
    # Preflight passes but no descriptor: it must NOT fabricate an execution, and must
    # NOT resurrect the old deferral. It refuses (exit 2) and says a descriptor is needed.
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest],
        live_runner=_never,
    )
    out = capsys.readouterr().out
    assert rc == 2
    assert "no --run-descriptor" in out
    assert "UNLOCKED but NOT executed here" not in out


def test_dry_run_still_default_and_passes(capsys) -> None:
    cli = _load_cli()
    rc = cli.main(["--dry-run", "--scenario", "success"])
    assert rc == 0
    assert "verdict: PASS" in capsys.readouterr().out
