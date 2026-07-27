"""Deterministic coverage for the M5 T022 runner orchestration layer (ADR-0009 §6).

The FSM's own no-false-PASS core is proven by `test_fsm_no_false_pass.py` (32
cases). This suite proves the RUNNER layer around it:

  * only the fully-correct `success` scenario yields PASS — every scripted failure
    path (each leg fail, missing/duplicate/tampered/stale/reordered receipt, crash,
    per-phase timeout, global deadline, cleanup crash, pin drift, competing lease)
    FAILs (the no-false-PASS contract at the runner level),
  * the runner is the SOLE verdict authority: a held lease + verified pins +
    correlated evidence are ALL required for PASS, and removing any one flips it,
  * the lease is always released (cleanup-safe) on every path,
  * the evidence document is byte-stable and carries the dry-run maturity banner,
  * the per-phase timeout and manifest components fail closed on their own.

Pure stdlib + pytest; no game/network/file I/O.
"""
from __future__ import annotations

import pytest

from runner_core import (
    ArtifactPinManifest,
    EvidenceDocument,
    LaneLease,
    LaneLeaseError,
    PhaseBudget,
    PhaseTimeoutError,
    PhaseTimeoutTransport,
    PinDriftError,
    RunManifestError,
)
from runner_core.evidence import DRY_RUN_MATURITY, REQUIRED_LEGS
from runner_core.manifest import REQUIRED_PARTS
from runner_core import simulation as sim


# --------------------------------------------------------------------------- #
# Scenario matrix — the no-false-PASS contract at the runner level.
# --------------------------------------------------------------------------- #

@pytest.mark.parametrize("name", list(sim.SCENARIOS))
def test_scenario_verdict_matches_expectation(name: str) -> None:
    result = sim.SCENARIOS[name]().run()
    expected_pass = name in sim.PASS_SCENARIOS
    assert result.passed is expected_pass, (
        f"scenario {name!r}: expected {'PASS' if expected_pass else 'FAIL'}, "
        f"got {result.verdict} ({result.evidence.failure_kind}: "
        f"{result.evidence.failure_reason})"
    )


def test_only_success_passes() -> None:
    passing = [n for n in sim.SCENARIOS if sim.SCENARIOS[n]().run().passed]
    assert passing == ["success"], f"unexpected PASS set: {passing}"


def test_success_has_all_legs_and_envelope() -> None:
    ev = sim.scenario_success().run().evidence
    assert ev.verdict == "PASS"
    assert all(ev.legs[leg] == "pass" for leg in REQUIRED_LEGS)
    assert ev.lease_held and ev.pins_verified and ev.cleanup_confirmed
    assert ev.receipts_correlated == 4
    assert ev.evidence_preserved is False  # a PASS is not a preserved failure


# --------------------------------------------------------------------------- #
# The runner is the SOLE verdict authority: FSM-PASS alone is NOT enough.
# --------------------------------------------------------------------------- #

def test_competing_lease_blocks_pass_even_with_good_receipts() -> None:
    """Golden receipts, but the lane is held by someone else -> FAIL."""
    result = sim.scenario_competing_lease().run()
    assert not result.passed
    assert result.evidence.failure_kind == "LaneLeaseError"
    assert result.evidence.lease_held is False


def test_pin_drift_blocks_pass_before_any_leg() -> None:
    result = sim.scenario_pin_drift().run()
    assert not result.passed
    assert result.evidence.failure_kind == "PinDriftError"
    assert result.evidence.pins_verified is False
    # Drift fails closed before legs assert.
    assert all(result.evidence.legs[leg] != "pass" for leg in REQUIRED_LEGS)


def test_cleanup_crash_blocks_pass_despite_all_legs() -> None:
    result = sim.scenario_cleanup_crash().run()
    assert not result.passed
    assert result.evidence.cleanup_confirmed is False
    # All four legs asserted, yet no PASS because cleanup is a precondition.
    assert all(result.evidence.legs[leg] == "pass" for leg in REQUIRED_LEGS)


def test_incomplete_evidence_blocks_pass() -> None:
    """If the runner expects more receipts than correlate, it cannot PASS."""
    orch = sim._orchestrator(sim._golden_transport(), expected_receipts=5)
    result = orch.run()
    assert not result.passed
    assert result.evidence.failure_kind == "IncompleteEvidence"


# --------------------------------------------------------------------------- #
# Lease is always released (cleanup-safe) on every path.
# --------------------------------------------------------------------------- #

@pytest.mark.parametrize("name", list(sim.SCENARIOS))
def test_lease_released_after_every_scenario(name: str) -> None:
    orch = sim.SCENARIOS[name]()
    lease = orch._lease  # the LaneLease instance the scenario wired in
    orch.run()
    assert lease.held_by_us is False


def test_lease_double_acquire_fails_closed() -> None:
    lease = LaneLease(lane_id="l", our_id="me", current_holder="other")
    with pytest.raises(LaneLeaseError):
        lease.acquire()


def test_lease_release_is_idempotent() -> None:
    lease = LaneLease(lane_id="l", our_id="me")
    lease.acquire()
    lease.release()
    lease.release()  # no raise
    assert lease.held_by_us is False


# --------------------------------------------------------------------------- #
# Evidence document: byte-stable + dry-run maturity banner.
# --------------------------------------------------------------------------- #

def test_evidence_is_byte_stable() -> None:
    a = sim.scenario_success().run().evidence
    b = sim.scenario_success().run().evidence
    assert a.to_json() == b.to_json()
    assert a.digest() == b.digest()


def test_evidence_carries_dry_run_maturity() -> None:
    ev = sim.scenario_success().run().evidence
    assert ev.maturity == DRY_RUN_MATURITY
    assert "NOT a live qualification" in ev.maturity


def test_failure_evidence_is_preserved() -> None:
    ev = sim.scenario_issue_fail().run().evidence
    assert ev.evidence_preserved is True
    assert ev.verdict == "FAIL"


# --------------------------------------------------------------------------- #
# Manifest component fails closed on its own.
# --------------------------------------------------------------------------- #

def _good_pins() -> dict:
    return {p: "a" * 64 for p in REQUIRED_PARTS}


def test_manifest_requires_all_six_parts() -> None:
    pins = _good_pins()
    del pins["harmony"]
    with pytest.raises(RunManifestError):
        ArtifactPinManifest(pins=pins)


def test_manifest_rejects_extra_part() -> None:
    pins = _good_pins()
    pins["mystery"] = "b" * 64
    with pytest.raises(RunManifestError):
        ArtifactPinManifest(pins=pins)


def test_manifest_rejects_malformed_hash() -> None:
    pins = _good_pins()
    pins["game"] = "not-a-sha"
    with pytest.raises(RunManifestError):
        ArtifactPinManifest(pins=pins)


def test_manifest_drift_detected() -> None:
    m = ArtifactPinManifest(pins=_good_pins())
    with pytest.raises(PinDriftError):
        m.verify_no_drift({"game": "c" * 64})


def test_manifest_unexpected_observed_artifact_is_drift() -> None:
    m = ArtifactPinManifest(pins=_good_pins())
    with pytest.raises(PinDriftError):
        m.verify_no_drift({"rogue": "d" * 64})


def test_manifest_matching_observed_is_ok() -> None:
    pins = _good_pins()
    m = ArtifactPinManifest(pins=pins)
    m.verify_no_drift({"game": pins["game"]})  # no raise


# --------------------------------------------------------------------------- #
# Per-phase timeout component fails closed on its own.
# --------------------------------------------------------------------------- #

def test_phase_timeout_fires_over_budget() -> None:
    from fsm import FakeTransport
    inner = FakeTransport(tick_per_send=10)
    inner.on("client_a", "Craft", [])  # empty payload list; behaviour is registered
    wrapped = PhaseTimeoutTransport(inner, PhaseBudget(default=3))
    from fsm.schema import ActionRequest
    with pytest.raises(PhaseTimeoutError):
        wrapped.send(ActionRequest("r", "client_a", "Craft", seq=1, conn_gen=1))


def test_phase_timeout_ok_within_budget() -> None:
    from fsm import FakeTransport
    inner = FakeTransport(tick_per_send=2)
    inner.on("client_a", "Craft", [])
    wrapped = PhaseTimeoutTransport(inner, PhaseBudget(default=5))
    from fsm.schema import ActionRequest
    wrapped.send(ActionRequest("r", "client_a", "Craft", seq=1, conn_gen=1))  # no raise
    assert wrapped.charged["r"] == 2


def test_evidence_document_constructs_standalone() -> None:
    ev = EvidenceDocument(verdict="FAIL", run_nonce="n", lane_id="l", lease_holder="h")
    assert ev.legs == {}
    assert ev.passed is False
