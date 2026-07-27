"""The T022 run orchestrator — the SOLE verdict authority (ADR-0009 §6).

This composes the whole run:

  1. acquire the exclusive lane lease (§5.3),
  2. verify the immutable 6-part artifact pins (§5.1/§8) and check drift,
  3. build the FSM `RunContext` threading the lease sentinel + pins into it,
  4. drive the adopted FSM through its 8 phases under per-phase timeout budgets,
  5. compose the correlated evidence document (§6),
  6. ALWAYS release the lease (cleanup-safe),
  7. stamp the FINAL verdict.

The runner is the sole PASS emitter. A PASS requires **every** one of:

  * the FSM returned PASS (all four legs asserted + cleanup confirmed — the
    no-false-PASS core), AND
  * the lease was actually held by us for the run, AND
  * the artifact pins verified (present + no drift), AND
  * the evidence document correlated the expected receipts.

Any missing precondition forces FAIL. The helper/FSM alone cannot mint a PASS; only
this orchestrator can, and only when the full operational envelope holds.

Engine-free / dry-run: the transport is injected. In production the injected
transport is the real M1 loopback/ZRpc transport; in every test/dry-run it is the
deterministic `FakeTransport`. The orchestrator itself never does I/O.
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Mapping, Optional

from fsm import RunContext, T022Runner
from fsm.result import RunResult
from fsm.schema import Manifest

from .evidence import REQUIRED_LEGS, EvidenceDocument
from .lease import LaneLease
from .manifest import ArtifactPinManifest, PinDriftError
from .timeouts import PhaseBudget, PhaseTimeoutTransport


@dataclass(frozen=True)
class RunnerVerdict:
    """The orchestrator's terminal output: the verdict + the evidence document."""

    evidence: EvidenceDocument
    fsm_result: Optional[RunResult]
    verdict: str

    @property
    def passed(self) -> bool:
        return self.verdict == "PASS"


class T022RunOrchestrator:
    """Compose lease + pins + FSM + evidence into one authoritative verdict."""

    def __init__(
        self,
        *,
        transport,
        lease: LaneLease,
        pins: ArtifactPinManifest,
        world_uid: str,
        world_name: str,
        run_nonce: str,
        expiry: int,
        phase_budget: PhaseBudget,
        expected_conn_gen: Mapping[str, int],
        actor_identity: Mapping[str, str],
        expected_receipts: int = len(REQUIRED_LEGS),
        integrity_key: bytes = b"fsm-fake-integrity-key",
        observed_pin_hashes: Optional[Mapping[str, str]] = None,
    ) -> None:
        self._transport = transport
        self._lease = lease
        self._pins = pins
        self._world_uid = world_uid
        self._world_name = world_name
        self._run_nonce = run_nonce
        self._expiry = expiry
        self._phase_budget = phase_budget
        self._expected_conn_gen = dict(expected_conn_gen)
        self._actor_identity = dict(actor_identity)
        self._expected_receipts = expected_receipts
        self._integrity_key = integrity_key
        self._observed_pin_hashes = dict(observed_pin_hashes or {})

    def run(self) -> RunnerVerdict:
        lease_error: Optional[str] = None
        pin_error: Optional[str] = None
        fsm_result: Optional[RunResult] = None
        timed_transport: Optional[PhaseTimeoutTransport] = None

        # -- 1. lease ------------------------------------------------------
        try:
            self._lease.acquire()
        except Exception as exc:  # LaneLeaseError — fail closed, never PASS
            lease_error = f"lease acquisition failed: {exc}"

        try:
            # -- 2. pins (drift check before we drive anything) ------------
            if pin_error is None:
                try:
                    if self._observed_pin_hashes:
                        self._pins.verify_no_drift(self._observed_pin_hashes)
                except PinDriftError as exc:
                    pin_error = f"pin drift: {exc}"

            # Fail closed BEFORE arming: if the lease was not held or the pins
            # drifted, we must not drive the scenario at all (ADR-0009 §5.1 —
            # nothing arms unless every precondition holds).
            if lease_error is None and pin_error is None:
                # -- 3. FSM context (threads the lease sentinel + pins) ----
                manifest = Manifest(
                    world_uid=self._world_uid,
                    world_name=self._world_name,
                    run_nonce=self._run_nonce,
                    expiry=self._expiry,
                    artifacts=self._pins.as_fsm_artifacts(),
                    required_artifacts=tuple(self._pins.as_fsm_artifacts().keys()),
                )
                context = RunContext(
                    manifest=manifest,
                    # If we hold the lease, the FSM sees our id as holder and
                    # proceeds; otherwise it sees the competing/unheld holder and
                    # fails closed.
                    lease_holder=self._lease.effective_holder,
                    our_lease_id=self._lease.our_id,
                    expected_conn_gen=self._expected_conn_gen,
                    actor_identity=self._actor_identity,
                )

                # -- 4. drive the FSM under per-phase timeout budgets ------
                timed_transport = PhaseTimeoutTransport(self._transport, self._phase_budget)
                runner = T022Runner(
                    timed_transport,
                    context,
                    integrity_key=self._integrity_key,
                )
                fsm_result = runner.run()
        finally:
            # -- 6. ALWAYS release the lease (cleanup-safe on every path) --
            self._lease.release()

        # -- 5. compose evidence + 7. stamp final verdict ------------------
        phase_costs = dict(timed_transport.charged) if timed_transport else {}
        lease_held_for_run = lease_error is None
        pins_verified = pin_error is None

        fsm_pass = fsm_result is not None and fsm_result.passed
        receipts = fsm_result.receipts_correlated if fsm_result else 0
        receipts_ok = receipts >= self._expected_receipts

        passed = fsm_pass and lease_held_for_run and pins_verified and receipts_ok
        verdict = "PASS" if passed else "FAIL"

        failure_reason = None
        failure_kind = None
        if not passed:
            # Surface the first, most-fundamental reason the run is not a PASS.
            if lease_error is not None:
                failure_reason, failure_kind = lease_error, "LaneLeaseError"
            elif pin_error is not None:
                failure_reason, failure_kind = pin_error, "PinDriftError"
            elif fsm_result is not None and fsm_result.failure_reason:
                failure_reason = fsm_result.failure_reason
                failure_kind = fsm_result.failure_kind
            elif not receipts_ok:
                failure_reason = (
                    f"only {receipts} receipt(s) correlated; "
                    f"need {self._expected_receipts}"
                )
                failure_kind = "IncompleteEvidence"
            elif not fsm_pass:
                failure_reason = "FSM did not return PASS"
                failure_kind = "FsmFail"

        evidence = EvidenceDocument(
            verdict=verdict,
            run_nonce=self._run_nonce,
            lane_id=self._lease.lane_id,
            lease_holder=self._lease.our_id if lease_held_for_run else self._lease.effective_holder,
            phases=list(fsm_result.phases) if fsm_result else [],
            legs=dict(fsm_result.legs) if fsm_result else {leg: "skipped" for leg in REQUIRED_LEGS},
            cleanup_confirmed=fsm_result.cleanup_confirmed if fsm_result else False,
            lease_held=lease_held_for_run,
            pins_verified=pins_verified,
            receipts_correlated=receipts,
            phase_costs=phase_costs,
            failure_reason=failure_reason,
            failure_kind=failure_kind,
            evidence_preserved=not passed,
        )
        return RunnerVerdict(evidence=evidence, fsm_result=fsm_result, verdict=verdict)
