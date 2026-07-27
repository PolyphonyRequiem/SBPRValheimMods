"""Adapter-driven schemas.

These dataclasses are the FSM's INTERNAL contract. They are deliberately small
and transport-neutral. The final M1/M4 JSON wire contracts (qa/contracts/*.json)
are mapped onto these via `ReceiptAdapter` / `ActionRequest.to_wire`, so the
canonical M5 card can swap the fake fixtures for real receipts without touching
the state machine.

Nothing here imports a network, filesystem, or game module.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Callable, Mapping, Optional

# ---------------------------------------------------------------------------
# Correlation identity
# ---------------------------------------------------------------------------
# ADR-0009 §3.2/§6: a receipt is bound to its request by the four-part key
# (run nonce, requestId, actor role, connection generation). The FSM correlates
# strictly on this tuple; any mismatch is a ReceiptCorrelationError, never a
# soft PASS.


@dataclass(frozen=True)
class CorrelationKey:
    run_nonce: str
    request_id: str
    actor: str          # "server" | "client_a" | "client_b"
    conn_gen: int       # connection generation; a reconnect bumps this

    def as_tuple(self) -> tuple[str, str, str, int]:
        return (self.run_nonce, self.request_id, self.actor, self.conn_gen)


# ---------------------------------------------------------------------------
# Requests + receipts
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class ActionRequest:
    """A single bounded verb the runner asks a helper to perform.

    `verb` is opaque to the FSM; the phase logic only cares which phase issued
    it and how the receipt correlates. `seq` is the per-run monotonic sequence
    used to detect reordering/replay.
    """

    request_id: str
    actor: str
    verb: str
    seq: int
    conn_gen: int
    args: Mapping[str, Any] = field(default_factory=dict)

    def key(self, run_nonce: str) -> CorrelationKey:
        return CorrelationKey(run_nonce, self.request_id, self.actor, self.conn_gen)


@dataclass(frozen=True)
class Receipt:
    """A primitive fact emitted by a helper. DESCRIPTIVE ONLY — it never carries
    a product PASS/FAIL verdict (ADR-0009 §6). The FSM derives verdicts; the
    receipt just reports observed primitives.
    """

    request_id: str
    actor: str
    verb: str
    seq: int
    conn_gen: int
    run_nonce: str
    outcome: str                       # "ok" | "reject" | "error"
    observed: Mapping[str, Any] = field(default_factory=dict)
    integrity: Optional[str] = None    # receipt authentication tag (HMAC-like)

    def key(self) -> CorrelationKey:
        return CorrelationKey(self.run_nonce, self.request_id, self.actor, self.conn_gen)


class ReceiptAdapter:
    """Pluggable seam mapping a raw transport payload -> Receipt.

    The fake transport hands the FSM native `Receipt` objects, so the default
    adapter is the identity. The real M1/M4 adapter will parse JSON bytes
    validated against qa/contracts/receipt.schema.json into a `Receipt`. Either
    way the FSM sees only `Receipt`.
    """

    def __init__(self, parse: Optional[Callable[[Any], Receipt]] = None) -> None:
        self._parse = parse

    def to_receipt(self, payload: Any) -> Receipt:
        if isinstance(payload, Receipt):
            return payload
        if self._parse is not None:
            return self._parse(payload)
        raise TypeError(
            "ReceiptAdapter got a non-Receipt payload and no parser was "
            "configured; supply parse= for the real wire contract."
        )


# ---------------------------------------------------------------------------
# Manifest + run context
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class Manifest:
    """Immutable per-run pin set (ADR-0009 §5.1/§8). `artifacts` maps a logical
    artifact name -> its pinned sha256. `required_artifacts` is the set that
    MUST be present, or arming fail-closes with ArtifactPinError.
    """

    world_uid: str
    world_name: str
    run_nonce: str
    expiry: int                        # deadline (monotonic ticks) for the whole run
    artifacts: Mapping[str, str] = field(default_factory=dict)
    required_artifacts: tuple[str, ...] = ()

    def verify_pins(self) -> None:
        missing = [a for a in self.required_artifacts if a not in self.artifacts]
        if missing:
            from .errors import ArtifactPinError

            raise ArtifactPinError(f"manifest missing required artifact pins: {missing}")


@dataclass(frozen=True)
class RunContext:
    """Everything the FSM needs to run one attempt. One attempt only — there is
    no retry loop inside the FSM (ADR-0009 §3.2 one-primitive/one-attempt).
    """

    manifest: Manifest
    lease_holder: str                  # who currently holds the exclusive lane lease
    our_lease_id: str                  # our claim; must equal lease_holder to act
    # actor -> connection generation the runner expects for this attempt.
    expected_conn_gen: Mapping[str, int] = field(default_factory=dict)
    # actor -> bound principal identity. A collision (two actors, one identity)
    # is an IdentityCollisionError.
    actor_identity: Mapping[str, str] = field(default_factory=dict)
