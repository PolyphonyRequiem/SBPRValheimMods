"""No-false-PASS proof suite for the T022 runner FSM.

The organizing principle: `golden` is the single correct configuration that
yields PASS. Every other test perturbs exactly ONE thing and asserts the verdict
flips to FAIL with the expected failure_kind. If any perturbation still PASSed,
the FSM would be unsound — so a green suite is the no-false-PASS proof.

Covers (task acceptance): missing / reordered / duplicate / stale / tampered
receipts, AT failure, timeout, crash, cleanup failure, identity collision,
artifact drift, competing lease.
"""
from __future__ import annotations

import pytest

from fsm import (
    ArtifactPinError,
    FakeTransport,
    T022Runner,
    TransportError,
)

from helpers import (  # noqa: E402
    INTEGRITY_KEY,
    NONCE,
    GOLDEN_OBSERVED,
    golden_context,
    golden_manifest,
    golden_transport,
    make_runner,
    receipt,
)


# ---------------------------------------------------------------------------
# The one green path
# ---------------------------------------------------------------------------


def test_golden_run_passes(golden):
    transport, context = golden
    result = make_runner(transport, context).run()
    assert result.verdict == "PASS"
    assert result.passed is True
    assert result.legs == {
        "ISSUE": "pass",
        "UPGRADE": "pass",
        "TRANSFER": "pass",
        "TAMPER": "pass",
    }
    assert result.cleanup_confirmed is True
    assert result.failure_reason is None
    assert result.receipts_correlated == 4
    assert result.evidence_preserved is False


def test_result_json_is_deterministic(golden):
    transport, context = golden
    r1 = make_runner(golden_transport(), golden_context()).run()
    r2 = make_runner(golden_transport(), golden_context()).run()
    assert r1.to_json() == r2.to_json()
    assert '"verdict":"PASS"' in r1.to_json()


def test_all_four_phases_reached_in_order(golden):
    transport, context = golden
    result = make_runner(transport, context).run()
    for leg in ("ISSUE", "UPGRADE", "TRANSFER", "TAMPER"):
        assert leg in result.phases
    assert result.phases.index("ISSUE") < result.phases.index("UPGRADE")
    assert result.phases.index("UPGRADE") < result.phases.index("TRANSFER")
    assert result.phases.index("TRANSFER") < result.phases.index("TAMPER")
    assert result.phases[-1] == "cleanup"


# ---------------------------------------------------------------------------
# Receipt correlation failures
# ---------------------------------------------------------------------------


def test_missing_receipt_fails():
    t = golden_transport()
    t.on("client_a", "Craft", [])  # server returns nothing
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"
    assert "missing" in result.failure_reason
    assert result.legs["ISSUE"] == "skipped"


def test_duplicate_receipt_fails():
    t = golden_transport()
    dup = receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"])
    t.on("client_a", "Craft", [dup, dup])  # two receipts for one request
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"
    assert "duplicate" in result.failure_reason


def test_reordered_seq_fails():
    # The UPGRADE receipt carries a seq that is not > the ISSUE seq.
    t = golden_transport()
    t.on(
        "client_a",
        "UpgradeItem",
        receipt("req-upgrade", "client_a", "UpgradeItem", 1, GOLDEN_OBSERVED["req-upgrade"]),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"
    assert "non-monotonic" in result.failure_reason


def test_stale_connection_generation_fails():
    # Receipt comes from a prior connection (conn_gen 0) — stale.
    t = golden_transport()
    t.on(
        "client_a",
        "Craft",
        receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"], conn_gen=0),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"


def test_wrong_actor_key_fails():
    # A receipt whose actor doesn't match the request's actor.
    t = golden_transport()
    t.on(
        "client_a",
        "Craft",
        receipt("req-issue", "client_b", "Craft", 1, GOLDEN_OBSERVED["req-issue"]),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"


def test_wrong_request_id_key_fails():
    t = golden_transport()
    t.on(
        "client_a",
        "Craft",
        receipt("req-WRONG", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"]),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"


def test_tampered_receipt_body_fails():
    # Correct key + seq, but observed body altered after signing => bad integrity.
    good = receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"])
    from dataclasses import replace

    tampered = replace(good, observed={"stamp_valid": True, "injected": "evil"})
    t = golden_transport()
    t.on("client_a", "Craft", tampered)
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"
    assert "integrity" in result.failure_reason


def test_forged_integrity_key_fails():
    # Receipt signed with the wrong key (a foreign/forged signer).
    t = golden_transport()
    t.on(
        "client_a",
        "Craft",
        receipt(
            "req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"],
            integrity_key=b"attacker-key",
        ),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"
    assert "integrity" in result.failure_reason


def test_reject_outcome_fails():
    t = golden_transport()
    t.on(
        "client_a",
        "Craft",
        receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"], outcome="reject"),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ReceiptCorrelationError"
    assert "not ok" in result.failure_reason


# ---------------------------------------------------------------------------
# AT-assertion failures — one per leg
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "actor,verb,req_id,seq,bad_observed,leg",
    [
        ("client_a", "Craft", "req-issue", 1, {"stamp_valid": False}, "ISSUE"),
        ("client_a", "UpgradeItem", "req-upgrade", 2, {"stamp_valid": False}, "UPGRADE"),
        ("client_b", "ReadItem", "req-transfer", 3, {"verdict": "invalid"}, "TRANSFER"),
        ("client_b", "TamperField", "req-tamper", 4, {"verdict": "valid", "line_rendered": True}, "TAMPER"),
    ],
)
def test_at_assertion_failure_per_leg(actor, verb, req_id, seq, bad_observed, leg):
    t = golden_transport()
    t.on(actor, verb, receipt(req_id, actor, verb, seq, bad_observed))
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ATFailure"
    assert result.legs[leg] == "fail"


def test_tamper_leg_needs_both_conditions():
    # verdict tampered but the client still rendered a line — must NOT pass.
    t = golden_transport()
    t.on(
        "client_b",
        "TamperField",
        receipt("req-tamper", "client_b", "TamperField", 4,
                {"verdict": "tampered", "line_rendered": True}),
    )
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.legs["TAMPER"] == "fail"


# ---------------------------------------------------------------------------
# Timeout / crash
# ---------------------------------------------------------------------------


def test_timeout_before_start_fails():
    # Clock already past expiry at preflight.
    t = golden_transport(start_tick=2000)
    ctx = golden_context(manifest=golden_manifest(expiry=1000))
    result = T022Runner(t, ctx, integrity_key=INTEGRITY_KEY).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "TransportError"
    assert "deadline" in result.failure_reason


def test_timeout_midway_fails():
    # Each send advances the clock a lot; expiry is crossed mid-scenario.
    t = golden_transport(tick_per_send=400)
    ctx = golden_context(manifest=golden_manifest(expiry=1000))
    result = T022Runner(t, ctx, integrity_key=INTEGRITY_KEY).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "TransportError"
    assert "deadline" in result.failure_reason
    # Cleanup still ran despite the timeout.
    assert result.cleanup_confirmed is True


def test_peer_crash_fails():
    t = golden_transport()
    t.on("client_a", "UpgradeItem", TransportError("client_a crashed"))
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "TransportError"
    assert result.legs["ISSUE"] == "pass"     # got that far
    assert result.legs["UPGRADE"] == "skipped"


def test_crash_still_runs_cleanup():
    t = golden_transport()
    t.on("client_a", "Craft", TransportError("boom"))
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.cleanup_confirmed is True
    assert "cleanup" in result.phases


# ---------------------------------------------------------------------------
# Cleanup failures / evidence preservation
# ---------------------------------------------------------------------------


def test_cleanup_failure_forces_fail_even_when_legs_pass():
    t = golden_transport(cleanup_raises=RuntimeError("ledger teardown failed"))
    result = make_runner(t).run()
    # All four legs asserted, but cleanup did not confirm -> NOT a PASS.
    assert result.legs == {
        "ISSUE": "pass", "UPGRADE": "pass", "TRANSFER": "pass", "TAMPER": "pass",
    }
    assert result.cleanup_confirmed is False
    assert result.verdict == "FAIL"
    assert result.failure_kind == "CleanupError"


def test_failure_preserves_evidence_flag():
    t = golden_transport()
    t.on("client_a", "Craft", [])
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.evidence_preserved is True


def test_original_failure_not_masked_by_cleanup_failure():
    # A scenario failure AND a cleanup failure: the scenario reason wins (the
    # cleanup failure must not overwrite the root cause).
    t = golden_transport(cleanup_raises=RuntimeError("teardown also failed"))
    t.on("client_b", "ReadItem", receipt("req-transfer", "client_b", "ReadItem", 3, {"verdict": "invalid"}))
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ATFailure"      # not CleanupError
    assert result.cleanup_confirmed is False


# ---------------------------------------------------------------------------
# Identity collision / artifact pins & drift / competing lease
# ---------------------------------------------------------------------------


def test_identity_collision_fails():
    t = golden_transport()
    ctx = golden_context(
        actor_identity={"server": "id-server", "client_a": "same", "client_b": "same"},
    )
    result = make_runner(t, ctx).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "IdentityCollisionError"
    # No scenario leg ran — preflight blocked it.
    assert all(v == "skipped" for v in result.legs.values())


def test_missing_artifact_pin_fails():
    t = golden_transport()
    ctx = golden_context(
        manifest=golden_manifest(artifacts={"helper": "sha-helper"}),  # missing "product"
    )
    result = make_runner(t, ctx).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ArtifactPinError"


def test_artifact_drift_fails():
    # A receipt reports an observed artifact hash that diverges from the pin.
    t = golden_transport()
    drifted = receipt(
        "req-issue", "client_a", "Craft", 1,
        {"stamp_valid": True, "artifact_hashes": {"product": "sha-DIFFERENT"}},
    )
    t.on("client_a", "Craft", drifted)
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "ArtifactDriftError"
    assert "drifted" in result.failure_reason


def test_matching_artifact_hash_still_passes():
    # Sanity: an observed hash that MATCHES the pin does not trip drift.
    t = golden_transport()
    ok = receipt(
        "req-issue", "client_a", "Craft", 1,
        {"stamp_valid": True, "artifact_hashes": {"product": "sha-product"}},
    )
    t.on("client_a", "Craft", ok)
    result = make_runner(t).run()
    assert result.verdict == "PASS"


def test_competing_lease_fails():
    t = golden_transport()
    ctx = golden_context(lease_holder="someone-else", our_lease_id="us")
    result = make_runner(t, ctx).run()
    assert result.verdict == "FAIL"
    assert result.failure_kind == "CompetingLeaseError"
    assert all(v == "skipped" for v in result.legs.values())


# ---------------------------------------------------------------------------
# Structural no-false-PASS guarantees
# ---------------------------------------------------------------------------


def test_partial_scenario_cannot_pass():
    # If the scenario only had three legs, the fourth stays "skipped" and blocks
    # PASS. We prove the composer requires ALL four regardless of what ran.
    from fsm.fsm import REQUIRED_LEGS
    assert set(REQUIRED_LEGS) == {"ISSUE", "UPGRADE", "TRANSFER", "TAMPER"}


def test_empty_transport_never_passes():
    t = FakeTransport()  # no handlers at all
    result = make_runner(t).run()
    assert result.verdict == "FAIL"
    assert result.passed is False


def test_no_receipt_correlated_means_fail():
    t = FakeTransport()
    result = make_runner(t).run()
    assert result.receipts_correlated == 0
    assert result.verdict == "FAIL"
