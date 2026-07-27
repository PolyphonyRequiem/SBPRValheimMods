"""Shared test fixtures + builders for the T022 FSM suite.

`golden_run()` returns a (transport, context) pair that produces a clean PASS, so
each test can mutate exactly one thing and prove that mutation flips PASS->FAIL.
This is the no-false-PASS harness: the ONLY green path is the fully-correct one.
"""
from __future__ import annotations

import hashlib
import hmac
import json
from typing import Any, Mapping, Optional

from fsm import (  # noqa: E402  (path injected by conftest)
    ActionRequest,
    FakeTransport,
    Manifest,
    Receipt,
    RunContext,
    T022Runner,
)

INTEGRITY_KEY = b"fsm-test-integrity-key"
NONCE = "run-nonce-abc123"


def tag(r: Receipt, key: bytes = INTEGRITY_KEY) -> str:
    body = json.dumps(r.observed, sort_keys=True, separators=(",", ":"))
    msg = (
        f"{r.run_nonce}|{r.request_id}|{r.actor}|{r.conn_gen}|{r.seq}|"
        f"{r.outcome}|{body}"
    ).encode()
    return hmac.new(key, msg, hashlib.sha256).hexdigest()


def receipt(
    request_id: str,
    actor: str,
    verb: str,
    seq: int,
    observed: Mapping[str, Any],
    *,
    conn_gen: int = 1,
    outcome: str = "ok",
    run_nonce: str = NONCE,
    integrity: Optional[str] = None,
    integrity_key: bytes = INTEGRITY_KEY,
) -> Receipt:
    r = Receipt(
        request_id=request_id,
        actor=actor,
        verb=verb,
        seq=seq,
        conn_gen=conn_gen,
        run_nonce=run_nonce,
        outcome=outcome,
        observed=dict(observed),
        integrity=None,
    )
    # Sign after construction so integrity matches the final field values.
    signed = Receipt(
        request_id=r.request_id,
        actor=r.actor,
        verb=r.verb,
        seq=r.seq,
        conn_gen=r.conn_gen,
        run_nonce=r.run_nonce,
        outcome=r.outcome,
        observed=r.observed,
        integrity=integrity if integrity is not None else tag(r, integrity_key),
    )
    return signed


def golden_manifest(**overrides: Any) -> Manifest:
    base = dict(
        world_uid="uid-disposable-xyz",
        world_name="homestead-t009l",
        run_nonce=NONCE,
        expiry=1000,
        artifacts={"helper": "sha-helper", "product": "sha-product"},
        required_artifacts=("helper", "product"),
    )
    base.update(overrides)
    return Manifest(**base)


def golden_context(**overrides: Any) -> RunContext:
    base = dict(
        manifest=golden_manifest(),
        lease_holder="lease-owner-1",
        our_lease_id="lease-owner-1",
        expected_conn_gen={"client_a": 1, "client_b": 1, "server": 1},
        actor_identity={
            "server": "id-server",
            "client_a": "id-primary",
            "client_b": "id-valbot",
        },
    )
    base.update(overrides)
    return RunContext(**base)


# Correct observed primitives per leg — the ONLY values that assert each AT.
GOLDEN_OBSERVED = {
    "req-issue": {"stamp_valid": True},
    "req-upgrade": {"stamp_valid": True},
    "req-transfer": {"verdict": "valid"},
    "req-tamper": {"verdict": "tampered", "line_rendered": False},
}


def golden_transport(**kwargs: Any) -> FakeTransport:
    """A FakeTransport scripted to return the correct receipt for every leg."""
    t = FakeTransport(**kwargs)
    t.on("client_a", "Craft", receipt("req-issue", "client_a", "Craft", 1, GOLDEN_OBSERVED["req-issue"]))
    t.on("client_a", "UpgradeItem", receipt("req-upgrade", "client_a", "UpgradeItem", 2, GOLDEN_OBSERVED["req-upgrade"]))
    t.on("client_b", "ReadItem", receipt("req-transfer", "client_b", "ReadItem", 3, GOLDEN_OBSERVED["req-transfer"]))
    t.on("client_b", "TamperField", receipt("req-tamper", "client_b", "TamperField", 4, GOLDEN_OBSERVED["req-tamper"]))
    return t


def make_runner(transport: FakeTransport, context: Optional[RunContext] = None) -> T022Runner:
    return T022Runner(
        transport,
        context or golden_context(),
        integrity_key=INTEGRITY_KEY,
    )
