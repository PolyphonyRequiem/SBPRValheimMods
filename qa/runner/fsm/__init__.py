"""SBPR T022 QA runner — transport-neutral deterministic FSM core.

PARALLEL PREBUILD (QA-M5). This package is a pure-Python, engine-free
state-machine core for the T022 Masterwork joined-client scenario. It performs
NO game I/O, NO network I/O, and NO file mutation. It exists so the canonical
QA-M5 runner card (t_6bb9c7d5) can adopt a proven FSM + verdict composer and
replace the fake transport / fixture receipt schemas with the final M1/M4 wire
contracts *without rewriting the state machine*.

Design authority: ADR-0009 (docs/decisions/0009-qa-harness-separate-fail-closed-mod.md).
The runner is the SOLE scenario state machine and the SOLE PASS/FAIL composer:
the helper emits dumb primitive receipts, the runner correlates them and decides.
It cannot emit PASS without all four named T022 acceptance tests
(ISSUE / UPGRADE / TRANSFER / TAMPER) asserted AND cleanup confirmed.

The adapter seam (`schema.ReceiptAdapter`) keeps receipt/action shapes pluggable
so the final M1/M4 JSON contracts drop in behind the same FSM.
"""
from __future__ import annotations

from .errors import (
    ArtifactDriftError,
    ArtifactPinError,
    CleanupError,
    CompetingLeaseError,
    FsmError,
    IdentityCollisionError,
    ReceiptCorrelationError,
    TransportError,
)
from .fsm import Phase, T022Runner, Verdict
from .result import RunResult
from .schema import (
    ActionRequest,
    Manifest,
    Receipt,
    ReceiptAdapter,
    RunContext,
)
from .transport import FakeTransport, Transport

__all__ = [
    "ActionRequest",
    "ArtifactDriftError",
    "ArtifactPinError",
    "CleanupError",
    "CompetingLeaseError",
    "FakeTransport",
    "FsmError",
    "IdentityCollisionError",
    "Manifest",
    "Phase",
    "Receipt",
    "ReceiptAdapter",
    "ReceiptCorrelationError",
    "RunContext",
    "RunResult",
    "T022Runner",
    "Transport",
    "TransportError",
    "Verdict",
]
