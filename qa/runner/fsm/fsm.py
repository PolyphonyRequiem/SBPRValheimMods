"""The deterministic T022 runner state machine.

Phases (ADR-0009 §Decision, §10):

    preflight -> fixture -> ISSUE -> UPGRADE -> TRANSFER -> TAMPER -> evidence -> cleanup

Invariants this FSM enforces (each is proven un-bypassable by the test suite):

  * ONE attempt, ONE deadline. No internal retry. If the transport clock passes
    the manifest expiry at any checkpoint, the run FAILs (timeout).
  * NO FALSE PASS. `verdict == "PASS"` requires all four named legs
    (ISSUE / UPGRADE / TRANSFER / TAMPER) asserted from correlated receipts AND
    cleanup confirmed. Any missing / reordered / duplicate / stale / tampered
    receipt, any AT-assertion failure, any timeout, any crash, any cleanup
    failure, any identity collision, any artifact drift, and any competing lease
    force FAIL.
  * RECEIPT CORRELATION is strict on the four-part key (run nonce, requestId,
    actor, connection generation) PLUS monotonic sequence PLUS an integrity tag.
  * CLEANUP ALWAYS RUNS (finally). A failed run PRESERVES evidence (no cleanup
    of the failure signal) while still tearing down world fixtures.

The FSM depends only on `Transport` + `ReceiptAdapter`; it has no game, network,
or filesystem knowledge. Swapping `FakeTransport` for the real M1 transport and
the identity `ReceiptAdapter` for the M4 wire parser changes nothing here.
"""
from __future__ import annotations

import enum
import hashlib
import hmac
import json
from dataclasses import dataclass
from typing import Any, Callable, List, Mapping, Optional

from .errors import (
    ArtifactDriftError,
    ArtifactPinError,
    CompetingLeaseError,
    IdentityCollisionError,
    ReceiptCorrelationError,
    TransportError,
)
from .result import RunResult
from .schema import (
    ActionRequest,
    Manifest,
    Receipt,
    ReceiptAdapter,
    RunContext,
)
from .transport import Transport


class Phase(enum.Enum):
    PREFLIGHT = "preflight"
    FIXTURE = "fixture"
    ISSUE = "ISSUE"
    UPGRADE = "UPGRADE"
    TRANSFER = "TRANSFER"
    TAMPER = "TAMPER"
    EVIDENCE = "evidence"
    CLEANUP = "cleanup"


class Verdict(enum.Enum):
    PASS = "PASS"
    FAIL = "FAIL"


# The four named T022 acceptance-test legs. PASS requires every one asserted.
REQUIRED_LEGS = ("ISSUE", "UPGRADE", "TRANSFER", "TAMPER")


def _canonical_observed(observed: Mapping[str, Any]) -> str:
    """Byte-stable serialization of an observed body for integrity hashing."""
    return json.dumps(observed, sort_keys=True, separators=(",", ":"))


@dataclass(frozen=True)
class Step:
    """One scenario leg: an action to issue and how to judge its receipt.

    `validator(observed) -> bool` decides whether the observed primitives prove
    the leg. Kept as a plain callable so the M5 card can replace the fixture
    assertions with the final observation contract without FSM changes.
    """

    phase: Phase
    request: ActionRequest
    validator: Callable[[Mapping[str, Any]], bool]


def _default_scenario(nonce: str) -> List[Step]:
    """The canonical T022 four-leg scenario over the fake contract.

    ISSUE   — an active-Masterwork joined craft yields a signed stamp that
              re-validates (observed.stamp_valid is True).
    UPGRADE — the stamp survives a custom-data-preserving upgrade
              (observed.stamp_valid True after upgrade).
    TRANSFER— a receiving client keyless-reads the stamp, server returns Valid
              (observed.verdict == "valid").
    TAMPER  — a hand-edited stamp degrades: server returns Tampered and the
              client renders NO line (observed.verdict == "tampered" and
              observed.line_rendered is False).
    """
    return [
        Step(
            Phase.ISSUE,
            ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=1),
            lambda o: o.get("stamp_valid") is True,
        ),
        Step(
            Phase.UPGRADE,
            ActionRequest("req-upgrade", "client_a", "UpgradeItem", seq=2, conn_gen=1),
            lambda o: o.get("stamp_valid") is True,
        ),
        Step(
            Phase.TRANSFER,
            ActionRequest("req-transfer", "client_b", "ReadItem", seq=3, conn_gen=1),
            lambda o: o.get("verdict") == "valid",
        ),
        Step(
            Phase.TAMPER,
            ActionRequest("req-tamper", "client_b", "TamperField", seq=4, conn_gen=1),
            lambda o: o.get("verdict") == "tampered" and o.get("line_rendered") is False,
        ),
    ]


class T022Runner:
    """Sole scenario state machine + sole PASS/FAIL composer (ADR-0009 §6)."""

    def __init__(
        self,
        transport: Transport,
        context: RunContext,
        *,
        adapter: Optional[ReceiptAdapter] = None,
        integrity_key: bytes = b"fsm-fake-integrity-key",
        scenario: Optional[List[Step]] = None,
    ) -> None:
        self._t = transport
        self._ctx = context
        self._adapter = adapter or ReceiptAdapter()
        self._integrity_key = integrity_key
        self._scenario = scenario if scenario is not None else _default_scenario(
            context.manifest.run_nonce
        )
        # Monotonic sequence guard: the next receipt seq we will accept must be
        # strictly greater than every seq accepted so far.
        self._last_seq = 0
        # Requests already fulfilled — a second receipt for one is a duplicate.
        self._fulfilled: set[str] = set()
        self._correlated = 0

    # -- integrity ---------------------------------------------------------
    def _expected_tag(self, r: Receipt) -> str:
        # The tag binds the correlation key AND the observed body, so any edit to
        # either (key spoof or payload injection) breaks the HMAC.
        body = _canonical_observed(r.observed)
        msg = (
            f"{r.run_nonce}|{r.request_id}|{r.actor}|{r.conn_gen}|{r.seq}|"
            f"{r.outcome}|{body}"
        ).encode()
        return hmac.new(self._integrity_key, msg, hashlib.sha256).hexdigest()

    # -- preflight ---------------------------------------------------------
    def _preflight(self) -> None:
        ctx, m = self._ctx, self._ctx.manifest
        # Exclusive lane lease: we may only act if we hold it.
        if ctx.lease_holder != ctx.our_lease_id:
            raise CompetingLeaseError(
                f"lane lease held by {ctx.lease_holder!r}, not us ({ctx.our_lease_id!r})"
            )
        # Artifact pins present.
        m.verify_pins()
        # Identity: no two actors share a bound principal.
        seen: dict[str, str] = {}
        for actor, ident in ctx.actor_identity.items():
            if ident in seen:
                raise IdentityCollisionError(
                    f"actors {seen[ident]!r} and {actor!r} collide on identity {ident!r}"
                )
            seen[ident] = actor
        self._check_deadline()

    def _check_deadline(self) -> None:
        if self._t.now() > self._ctx.manifest.expiry:
            raise TransportError(
                f"run deadline exceeded: now={self._t.now()} > expiry={self._ctx.manifest.expiry}"
            )

    # -- receipt handling --------------------------------------------------
    def _await_receipt(self, request: ActionRequest) -> Receipt:
        payloads = self._t.send(request)
        self._check_deadline()
        if not payloads:
            raise ReceiptCorrelationError(
                f"no receipt for request {request.request_id!r} (missing)"
            )
        # Exactly one receipt per request in this bounded protocol; more than one
        # is a duplicate/echo we must reject rather than pick from.
        if len(payloads) > 1:
            raise ReceiptCorrelationError(
                f"{len(payloads)} receipts for request {request.request_id!r} (duplicate)"
            )
        receipt = self._adapter.to_receipt(payloads[0])
        self._correlate(request, receipt)
        return receipt

    def _correlate(self, request: ActionRequest, r: Receipt) -> None:
        expected = request.key(self._ctx.manifest.run_nonce)
        # Four-part key must match exactly (run/request/actor/conn-gen).
        if r.key().as_tuple() != expected.as_tuple():
            raise ReceiptCorrelationError(
                f"receipt key {r.key().as_tuple()} != expected {expected.as_tuple()} "
                "(missing/reordered/stale/wrong-connection)"
            )
        # Connection generation must match the attempt's expected generation —
        # a stale receipt from a prior connection is rejected.
        exp_gen = self._ctx.expected_conn_gen.get(request.actor)
        if exp_gen is not None and r.conn_gen != exp_gen:
            raise ReceiptCorrelationError(
                f"stale receipt: actor {request.actor!r} conn_gen {r.conn_gen} "
                f"!= expected {exp_gen}"
            )
        # Duplicate: this request was already fulfilled.
        if request.request_id in self._fulfilled:
            raise ReceiptCorrelationError(
                f"duplicate receipt for already-fulfilled request {request.request_id!r}"
            )
        # Monotonic sequence: strictly increasing, no reorder/replay.
        if r.seq <= self._last_seq:
            raise ReceiptCorrelationError(
                f"non-monotonic seq {r.seq} (last accepted {self._last_seq}) — reordered/replayed"
            )
        # Integrity tag: a tampered receipt body fails the HMAC check.
        if r.integrity != self._expected_tag(r):
            raise ReceiptCorrelationError(
                f"integrity tag mismatch on request {request.request_id!r} — tampered receipt"
            )
        self._last_seq = r.seq
        self._fulfilled.add(request.request_id)
        self._correlated += 1

    # -- artifact drift ----------------------------------------------------
    def _check_artifact_drift(self, r: Receipt) -> None:
        """If a receipt reports an observed artifact hash, it must match the pin."""
        observed = r.observed.get("artifact_hashes")
        if not observed:
            return
        pins = self._ctx.manifest.artifacts
        for name, got in observed.items():
            want = pins.get(name)
            if want is not None and got != want:
                raise ArtifactDriftError(
                    f"artifact {name!r} drifted: observed {got} != pinned {want}"
                )

    # -- main drive --------------------------------------------------------
    def run(self) -> RunResult:
        phases_reached: List[str] = []
        legs: dict[str, str] = {leg: "skipped" for leg in REQUIRED_LEGS}
        failure_reason: Optional[str] = None
        failure_kind: Optional[str] = None
        cleanup_confirmed = False

        try:
            phases_reached.append(Phase.PREFLIGHT.value)
            self._preflight()

            phases_reached.append(Phase.FIXTURE.value)
            self._check_deadline()

            for step in self._scenario:
                phases_reached.append(step.phase.value)
                receipt = self._await_receipt(step.request)
                self._check_artifact_drift(receipt)
                if receipt.outcome != "ok":
                    raise ReceiptCorrelationError(
                        f"{step.phase.value} receipt outcome={receipt.outcome!r} (not ok)"
                    )
                if not step.validator(receipt.observed):
                    legs[step.phase.value] = "fail"
                    raise _ATFailure(
                        f"{step.phase.value} acceptance assertion failed on observed "
                        f"{dict(receipt.observed)!r}"
                    )
                legs[step.phase.value] = "pass"

            phases_reached.append(Phase.EVIDENCE.value)
            self._check_deadline()

        except _ATFailure as exc:
            failure_reason = str(exc)
            failure_kind = "ATFailure"
        except (
            ReceiptCorrelationError,
            TransportError,
            ArtifactDriftError,
            ArtifactPinError,
            IdentityCollisionError,
            CompetingLeaseError,
        ) as exc:
            failure_reason = str(exc)
            failure_kind = type(exc).__name__
        finally:
            # Cleanup ALWAYS runs. It is a PASS precondition.
            phases_reached.append(Phase.CLEANUP.value)
            try:
                self._t.cleanup()
                cleanup_confirmed = True
            except Exception as exc:  # noqa: BLE001 — cleanup must never mask into PASS
                cleanup_confirmed = False
                if failure_reason is None:
                    failure_reason = f"cleanup failed: {exc}"
                    failure_kind = "CleanupError"

        all_legs_pass = all(legs[leg] == "pass" for leg in REQUIRED_LEGS)
        passed = all_legs_pass and cleanup_confirmed and failure_reason is None
        verdict = Verdict.PASS.value if passed else Verdict.FAIL.value

        return RunResult(
            verdict=verdict,
            run_nonce=self._ctx.manifest.run_nonce,
            phases=phases_reached,
            legs=legs,
            cleanup_confirmed=cleanup_confirmed,
            failure_reason=failure_reason,
            failure_kind=failure_kind,
            evidence_preserved=not passed,
            receipts_correlated=self._correlated,
        )


class _ATFailure(Exception):
    """Internal: an acceptance-test leg assertion failed (not an infra error)."""
