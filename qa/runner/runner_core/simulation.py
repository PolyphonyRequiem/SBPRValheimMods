"""Deterministic dry-run simulation scenarios for the T022 runner (ADR-0009 §10).

Every path the runner must handle is scripted here through the engine-free
`FakeTransport` + a golden pin manifest + a golden lease, so the full orchestrator
(lease → pins → FSM → evidence → verdict) is exercised with NO game/network/file
I/O. This is the timing-model / dry-run realization of the M5 coverage requirement
and of `AT-QA-T022-COLD-30MIN` (timing model ONLY — no real 30-minute cold run).

Each `scenario_*` returns a fully-wired `T022RunOrchestrator`; the runner CLI and
the pytest suite both drive them. `SCENARIOS` maps a stable name to a builder so
`sbpr-qa-t022.py --dry-run --scenario <name>` can replay any single path.
"""
from __future__ import annotations

import hashlib
import hmac
import json
from typing import Any, Callable, Dict, Mapping, Optional

from fsm import ActionRequest, FakeTransport, Receipt
from fsm.errors import CleanupError, TransportError

from .lease import LaneLease
from .manifest import REQUIRED_PARTS, ArtifactPinManifest
from .orchestrator import T022RunOrchestrator
from .timeouts import PhaseBudget

INTEGRITY_KEY = b"sim-integrity-key"
NONCE = "sim-run-nonce-0001"
LANE_ID = "disposable-lane-t022"
OUR_LEASE = "runner-sentinel-1"
WORLD_UID = "uid-disposable-sim"
WORLD_NAME = "homestead-sim-t022"
EXPIRY = 10_000


def _pin(seed: str) -> str:
    return hashlib.sha256(seed.encode()).hexdigest()


def golden_pins() -> ArtifactPinManifest:
    return ArtifactPinManifest(pins={part: _pin(f"pin::{part}") for part in REQUIRED_PARTS})


def _tag(r: Receipt, key: bytes = INTEGRITY_KEY) -> str:
    body = json.dumps(r.observed, sort_keys=True, separators=(",", ":"))
    msg = (
        f"{r.run_nonce}|{r.request_id}|{r.actor}|{r.conn_gen}|{r.seq}|"
        f"{r.outcome}|{body}"
    ).encode()
    return hmac.new(key, msg, hashlib.sha256).hexdigest()


def _receipt(
    request_id: str,
    actor: str,
    verb: str,
    seq: int,
    observed: Mapping[str, Any],
    *,
    conn_gen: int = 1,
    outcome: str = "ok",
    integrity: Optional[str] = None,
) -> Receipt:
    base = Receipt(
        request_id=request_id, actor=actor, verb=verb, seq=seq, conn_gen=conn_gen,
        run_nonce=NONCE, outcome=outcome, observed=dict(observed), integrity=None,
    )
    return Receipt(
        request_id=base.request_id, actor=base.actor, verb=base.verb, seq=base.seq,
        conn_gen=base.conn_gen, run_nonce=base.run_nonce, outcome=base.outcome,
        observed=base.observed,
        integrity=integrity if integrity is not None else _tag(base),
    )


# The correct observed primitives per leg — the ONLY values that assert each AT.
GOLDEN_OBSERVED = {
    "req-issue": {"stamp_valid": True},
    "req-upgrade": {"stamp_valid": True},
    "req-transfer": {"verdict": "valid"},
    "req-tamper": {"verdict": "tampered", "line_rendered": False},
}


def _golden_transport(**kwargs: Any) -> FakeTransport:
    t = FakeTransport(**kwargs)
    t.on("client_a", "Craft", _receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"]))
    t.on("client_a", "UpgradeItem", _receipt("req-upgrade", "client_a", "UpgradeItem", 2, GOLDEN_OBSERVED["req-upgrade"]))
    t.on("client_b", "ReadItem", _receipt("req-transfer", "client_b", "ReadItem", 3, GOLDEN_OBSERVED["req-transfer"]))
    t.on("client_b", "TamperField", _receipt("req-tamper", "client_b", "TamperField", 4, GOLDEN_OBSERVED["req-tamper"]))
    return t


def _lease(current_holder: Optional[str] = None) -> LaneLease:
    return LaneLease(lane_id=LANE_ID, our_id=OUR_LEASE, current_holder=current_holder)


def _budget(default: int = 5, per_verb: Optional[Mapping[str, int]] = None) -> PhaseBudget:
    return PhaseBudget(default=default, per_verb=dict(per_verb or {}))


def _orchestrator(
    transport: FakeTransport,
    *,
    lease: Optional[LaneLease] = None,
    pins: Optional[ArtifactPinManifest] = None,
    phase_budget: Optional[PhaseBudget] = None,
    observed_pin_hashes: Optional[Mapping[str, str]] = None,
    expected_receipts: int = 4,
) -> T022RunOrchestrator:
    return T022RunOrchestrator(
        transport=transport,
        lease=lease or _lease(),
        pins=pins or golden_pins(),
        world_uid=WORLD_UID,
        world_name=WORLD_NAME,
        run_nonce=NONCE,
        expiry=EXPIRY,
        phase_budget=phase_budget or _budget(),
        expected_conn_gen={"client_a": 1, "client_b": 1, "server": 1},
        actor_identity={"server": "id-server", "client_a": "id-primary", "client_b": "id-valbot"},
        expected_receipts=expected_receipts,
        integrity_key=INTEGRITY_KEY,
        observed_pin_hashes=observed_pin_hashes,
    )


# --------------------------------------------------------------------------- #
# Scenarios — one per path the runner must cover.
# --------------------------------------------------------------------------- #

def scenario_success() -> T022RunOrchestrator:
    """The single fully-correct path — the ONLY one that yields PASS."""
    return _orchestrator(_golden_transport())


def scenario_issue_fail() -> T022RunOrchestrator:
    """ISSUE leg assertion fails (stamp not valid) -> FAIL, no false PASS."""
    t = _golden_transport()
    t.on("client_a", "Craft", _receipt("req-issue", "client_a", "Craft", 1, {"stamp_valid": False}))
    return _orchestrator(t)


def scenario_upgrade_fail() -> T022RunOrchestrator:
    t = _golden_transport()
    t.on("client_a", "UpgradeItem", _receipt("req-upgrade", "client_a", "UpgradeItem", 2, {"stamp_valid": False}))
    return _orchestrator(t)


def scenario_transfer_fail() -> T022RunOrchestrator:
    t = _golden_transport()
    t.on("client_b", "ReadItem", _receipt("req-transfer", "client_b", "ReadItem", 3, {"verdict": "invalid"}))
    return _orchestrator(t)


def scenario_tamper_fail() -> T022RunOrchestrator:
    """Tamper did NOT degrade (line still rendered) -> FAIL."""
    t = _golden_transport()
    t.on("client_b", "TamperField", _receipt("req-tamper", "client_b", "TamperField", 4, {"verdict": "valid", "line_rendered": True}))
    return _orchestrator(t)


def scenario_missing_receipt() -> T022RunOrchestrator:
    """A leg returns no receipt -> ReceiptCorrelationError -> FAIL."""
    t = _golden_transport()
    t.on("client_b", "TamperField", None)
    return _orchestrator(t)


def scenario_duplicate_receipt() -> T022RunOrchestrator:
    """Two receipts for one request -> duplicate -> FAIL."""
    t = _golden_transport()
    dup = _receipt("req-transfer", "client_b", "ReadItem", 3, GOLDEN_OBSERVED["req-transfer"])
    t.on("client_b", "ReadItem", [dup, dup])
    return _orchestrator(t)


def scenario_tampered_receipt() -> T022RunOrchestrator:
    """A receipt with a broken integrity tag -> FAIL (evidence tamper)."""
    t = _golden_transport()
    t.on("client_b", "ReadItem", _receipt("req-transfer", "client_b", "ReadItem", 3, GOLDEN_OBSERVED["req-transfer"], integrity="deadbeef"))
    return _orchestrator(t)


def scenario_stale_receipt() -> T022RunOrchestrator:
    """A receipt from a prior connection generation -> stale -> FAIL."""
    t = _golden_transport()
    t.on("client_b", "ReadItem", _receipt("req-transfer", "client_b", "ReadItem", 3, GOLDEN_OBSERVED["req-transfer"], conn_gen=99))
    return _orchestrator(t)


def scenario_reordered_receipt() -> T022RunOrchestrator:
    """A non-monotonic sequence -> reorder/replay -> FAIL."""
    t = _golden_transport()
    t.on("client_b", "TamperField", _receipt("req-tamper", "client_b", "TamperField", 1, GOLDEN_OBSERVED["req-tamper"]))
    return _orchestrator(t)


def scenario_crash() -> T022RunOrchestrator:
    """A peer crash mid-run (TransportError) -> FAIL."""
    t = _golden_transport()
    t.on("client_a", "UpgradeItem", TransportError("client_a crashed"))
    return _orchestrator(t)


def scenario_timeout() -> T022RunOrchestrator:
    """A single primitive blows its per-phase budget -> PhaseTimeoutError -> FAIL."""
    # Craft costs 9 ticks against a per-verb budget of 3.
    t = _golden_transport(tick_per_send=9)
    return _orchestrator(t, phase_budget=_budget(default=100, per_verb={"Craft": 3}))


def scenario_global_deadline() -> T022RunOrchestrator:
    """The whole-run expiry passes -> FSM TransportError(deadline) -> FAIL."""
    t = _golden_transport(tick_per_send=4000)  # 4 sends * 4000 > EXPIRY(10000) partway
    return _orchestrator(t, phase_budget=_budget(default=100000))


def scenario_cleanup_crash() -> T022RunOrchestrator:
    """Legs pass but cleanup fails to confirm -> FAIL (cleanup is a PASS precond)."""
    t = _golden_transport(cleanup_raises=CleanupError)
    return _orchestrator(t)


def scenario_pin_drift() -> T022RunOrchestrator:
    """An observed artifact hash diverges from its pin -> FAIL before any leg."""
    observed = {"helper": _pin("DRIFTED-helper-bytes")}
    return _orchestrator(_golden_transport(), observed_pin_hashes=observed)


def scenario_competing_lease() -> T022RunOrchestrator:
    """Another holder owns the lane -> lease acquisition fails -> FAIL."""
    return _orchestrator(_golden_transport(), lease=_lease(current_holder="other-runner"))


SCENARIOS: Dict[str, Callable[[], T022RunOrchestrator]] = {
    "success": scenario_success,
    "issue-fail": scenario_issue_fail,
    "upgrade-fail": scenario_upgrade_fail,
    "transfer-fail": scenario_transfer_fail,
    "tamper-fail": scenario_tamper_fail,
    "missing-receipt": scenario_missing_receipt,
    "duplicate-receipt": scenario_duplicate_receipt,
    "tampered-receipt": scenario_tampered_receipt,
    "stale-receipt": scenario_stale_receipt,
    "reordered-receipt": scenario_reordered_receipt,
    "crash": scenario_crash,
    "timeout": scenario_timeout,
    "global-deadline": scenario_global_deadline,
    "cleanup-crash": scenario_cleanup_crash,
    "pin-drift": scenario_pin_drift,
    "competing-lease": scenario_competing_lease,
}

# The only scenario that is expected to PASS. Everything else must FAIL — this is
# the no-false-PASS contract at the runner (not just FSM) level.
PASS_SCENARIOS = {"success"}
