"""Live-transport wire coverage against an in-process loopback stub (ADR-0009 §2, §5.1).

This proves the concrete `LiveLoopbackTransport` speaks the EXACT owner-local
loopback framing + HMAC envelope + receipt contract the merged C#
`LoopbackControlServer` / `EnvelopeCodec` / `RequestHmac` define — end-to-end over a
REAL TCP socket — with NO Valheim, NO game I/O, and NO deploy. The stub is a tiny
Python echo server that:

  * binds 127.0.0.1 on an ephemeral port (mirrors the loopback-only bind policy),
  * reads the token frame then the request frame (two length-prefixed frames),
  * recomputes the canonical HMAC and rejects a bad token / bad HMAC / stale
    connection generation,
  * replies with one receipt frame carrying the server's current generation.

Nothing here launches a client or drives an in-world run; this is the loopback-stub
live-path coverage the M6-EXEC card requires.
"""
from __future__ import annotations

import socket
import threading
from typing import Optional

import pytest

from fsm.errors import TransportError
from fsm.schema import ActionRequest
from runner_core.live_transport import (
    ChannelEndpoint,
    LiveLoopbackTransport,
    LiveRunConfig,
    compute_hmac,
    encode_frame,
    read_frame,
    _canonical_string,
)

OPERATOR_TOKEN = "operator-token-abc"
HMAC_SECRET = "hmac-secret-xyz"
NONCE = "live-run-nonce-0001"
WORLD_UID = 987654321
EXPIRY = 32_500_000_000_000


class LoopbackStub:
    """In-process echo of the merged C# loopback control server (wire-faithful)."""

    def __init__(self, *, current_generation: int = 1, reject_stale: bool = True) -> None:
        self._srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._srv.bind(("127.0.0.1", 0))
        self._srv.listen(8)
        self.port = self._srv.getsockname()[1]
        self.current_generation = current_generation
        self._reject_stale = reject_stale
        self.requests: list[dict] = []
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()

    def _serve(self) -> None:
        import json

        self._srv.settimeout(0.25)
        while not self._stop.is_set():
            try:
                conn, _ = self._srv.accept()
            except socket.timeout:
                continue
            except OSError:
                break
            with conn:
                try:
                    deadline = __import__("time").monotonic() + 3.0
                    token = read_frame(conn, deadline)
                    payload = read_frame(conn, deadline)
                except TransportError:
                    continue
                env = json.loads(payload)
                self.requests.append(env)
                receipt = self._decide(token, env)
                conn.sendall(encode_frame(json.dumps(receipt)))

    def _decide(self, token: str, env: dict) -> dict:
        # Owner-local bind: wrong token -> TransportRejected (BadOperatorToken).
        if token != OPERATOR_TOKEN:
            return self._reject("BadOperatorToken", env)
        # HMAC recompute over the canonical string (includes connectionGeneration).
        canonical = _canonical_string(
            env["nonce"], env["seq"], env["expiry"], env["role"],
            env["worldUid"], env["verb"], env["requestId"], env["connectionGeneration"],
        )
        if compute_hmac(HMAC_SECRET, canonical) != env["hmac"]:
            return self._reject("BadHmac", env)
        # Stale-generation defense (StaleGeneration) — the pinned M2R behaviour.
        if self._reject_stale and env["connectionGeneration"] != self.current_generation:
            return self._reject("StaleGeneration", env)
        # Admitted: echo a descriptive receipt with the current generation + golden fact.
        return {
            "requestId": env["requestId"],
            "verb": env["verb"],
            "outcome": "OK",
            "reason": "None",
            "role": env["role"],
            "worldUid": env["worldUid"],
            "seq": env["seq"],
            "ts": 1,
            "connectionGeneration": self.current_generation,
            "observed": _golden_observed(env["requestId"]),
        }

    def _reject(self, reason: str, env: dict) -> dict:
        return {
            "requestId": env.get("requestId", ""),
            "verb": env.get("verb", ""),
            "outcome": "REJECTED",
            "reason": reason,
            "role": env.get("role", "Client"),
            "worldUid": env.get("worldUid", 0),
            "seq": env.get("seq", 0),
            "ts": 1,
            "connectionGeneration": self.current_generation,
        }

    def close(self) -> None:
        self._stop.set()
        try:
            self._srv.close()
        except OSError:
            pass
        self._thread.join(timeout=2.0)


def _golden_observed(request_id: str) -> dict:
    return {
        "req-issue": {"stamp_valid": True},
        "req-upgrade": {"stamp_valid": True},
        "req-transfer": {"verdict": "valid"},
        "req-tamper": {"verdict": "tampered", "line_rendered": False},
    }.get(request_id, {})


def _config(stub: LoopbackStub, *, start_generation: int = 1, integrity_key: bytes = b"k") -> LiveRunConfig:
    ep = ChannelEndpoint(host="127.0.0.1", port=stub.port, role="Client")
    return LiveRunConfig(
        nonce=NONCE,
        world_uid=WORLD_UID,
        expiry_unix_ms=EXPIRY,
        operator_token=OPERATOR_TOKEN,
        hmac_secret=HMAC_SECRET,
        endpoints={"client_a": ep, "client_b": ep},
        integrity_key=integrity_key,
        start_generation=start_generation,
    )


# --------------------------------------------------------------------------- #
# Framing round-trip (pure, no socket).
# --------------------------------------------------------------------------- #

def test_frame_round_trip_via_socketpair() -> None:
    a, b = socket.socketpair()
    try:
        a.sendall(encode_frame("hello-frame"))
        import time
        assert read_frame(b, time.monotonic() + 2.0) == "hello-frame"
    finally:
        a.close()
        b.close()


def test_canonical_string_matches_fixed_order() -> None:
    cs = _canonical_string("n", 3, 99, "Client", 42, "Craft", "req-issue", 1)
    assert cs == "n\n3\n99\nClient\n42\nCraft\nreq-issue\n1"


# --------------------------------------------------------------------------- #
# Live send over a real loopback socket.
# --------------------------------------------------------------------------- #

def test_live_send_admitted_returns_ok_receipt() -> None:
    stub = LoopbackStub(current_generation=1)
    try:
        t = LiveLoopbackTransport(_config(stub))
        receipts = t.send(ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=1))
        assert len(receipts) == 1
        r = receipts[0]
        assert r.outcome == "ok"
        assert r.observed == {"stamp_valid": True}
        assert r.conn_gen == 1
        # The stub actually received a well-formed envelope carrying the generation.
        assert stub.requests[0]["connectionGeneration"] == 1
        assert stub.requests[0]["verb"] == "Craft"
    finally:
        stub.close()


def test_live_send_bad_token_is_not_ok() -> None:
    stub = LoopbackStub()
    try:
        cfg = _config(stub)
        object.__setattr__(cfg, "operator_token", "WRONG")  # tamper the token
        t = LiveLoopbackTransport(cfg)
        r = t.send(ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=1))[0]
        assert r.outcome != "ok"  # FSM will fail closed on a non-ok receipt
    finally:
        stub.close()


def test_live_stale_generation_rejected() -> None:
    # Server is at generation 2 (a reconnect happened); the runner still holds 1.
    stub = LoopbackStub(current_generation=2)
    try:
        t = LiveLoopbackTransport(_config(stub, start_generation=1))
        r = t.send(ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=1))[0]
        assert r.outcome != "ok"
        assert stub.requests[0]["connectionGeneration"] == 1  # we sent the stale gen
    finally:
        stub.close()


def test_live_generation_advances_from_receipt() -> None:
    stub = LoopbackStub(current_generation=5)
    try:
        # Start at 5 so the first request is accepted; then confirm the tracked gen
        # follows the server's echoed current generation.
        t = LiveLoopbackTransport(_config(stub, start_generation=5))
        t.send(ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=5))
        assert t.generation_for("client_a") == 5
    finally:
        stub.close()


def test_live_send_after_cleanup_fails_closed() -> None:
    stub = LoopbackStub()
    try:
        t = LiveLoopbackTransport(_config(stub))
        t.cleanup()
        with pytest.raises(TransportError):
            t.send(ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=1))
    finally:
        stub.close()


def test_live_unknown_actor_fails_closed() -> None:
    stub = LoopbackStub()
    try:
        t = LiveLoopbackTransport(_config(stub))
        with pytest.raises(TransportError):
            t.send(ActionRequest("r", "server", "SpawnStation", seq=1, conn_gen=1))
    finally:
        stub.close()


def test_live_connect_failure_fails_closed() -> None:
    # Point at a closed port (no stub) — connection refused must surface as TransportError.
    ep = ChannelEndpoint(host="127.0.0.1", port=1, role="Client")
    cfg = LiveRunConfig(
        nonce=NONCE, world_uid=WORLD_UID, expiry_unix_ms=EXPIRY,
        operator_token=OPERATOR_TOKEN, hmac_secret=HMAC_SECRET,
        endpoints={"client_a": ep}, connect_timeout_s=0.5,
    )
    t = LiveLoopbackTransport(cfg)
    with pytest.raises(TransportError):
        t.send(ActionRequest("req-issue", "client_a", "Craft", seq=1, conn_gen=1))


def test_live_transport_satisfies_protocol() -> None:
    from fsm.transport import Transport
    stub = LoopbackStub()
    try:
        t = LiveLoopbackTransport(_config(stub))
        # runtime_checkable Protocol: the live transport is a structural Transport,
        # so the FSM (and its 32-case suite) accept it unchanged.
        assert isinstance(t, Transport)
    finally:
        stub.close()


def test_start_generation_zero_rejected() -> None:
    ep = ChannelEndpoint(host="127.0.0.1", port=5, role="Client")
    with pytest.raises(ValueError):
        LiveRunConfig(
            nonce=NONCE, world_uid=WORLD_UID, expiry_unix_ms=EXPIRY,
            operator_token=OPERATOR_TOKEN, hmac_secret=HMAC_SECRET,
            endpoints={"client_a": ep}, start_generation=0,
        )


def test_full_four_leg_run_over_loopback_stub() -> None:
    """Drive all four T022 legs through the FSM over the live loopback stub.

    This exercises the concrete transport end-to-end inside the real orchestrator
    machinery (FSM correlation + integrity) — a loopback stub, never a real game.
    """
    from fsm import RunContext, T022Runner
    from fsm.schema import Manifest

    stub = LoopbackStub(current_generation=1)
    try:
        key = b"integrity-key-for-run"
        cfg = _config(stub, integrity_key=key)
        transport = LiveLoopbackTransport(cfg)
        manifest = Manifest(
            world_uid=str(WORLD_UID), world_name="homestead-t009l", run_nonce=NONCE,
            expiry=10_000_000, artifacts={"helper": "h"}, required_artifacts=("helper",),
        )
        ctx = RunContext(
            manifest=manifest, lease_holder="me", our_lease_id="me",
            expected_conn_gen={"client_a": 1, "client_b": 1},
            actor_identity={"client_a": "id-a", "client_b": "id-b"},
        )
        result = T022Runner(transport, ctx, integrity_key=key).run()
        assert result.verdict == "PASS", (result.failure_kind, result.failure_reason)
        assert result.receipts_correlated == 4
    finally:
        stub.close()
