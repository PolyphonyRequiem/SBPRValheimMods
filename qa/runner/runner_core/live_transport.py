"""Live wire transport for the T022 runner (ADR-0009 §2, §3.2, §5.1) — M6-EXEC.

This is the concrete `fsm.transport.Transport` the M5 docstrings explicitly defer
to: it moves the *same three methods* (`now`, `send`, `cleanup`) the FSM already
depends on onto the real owner-local loopback TCP/JSON control channel exposed by
the merged C# `LoopbackControlServer`. Swapping `FakeTransport` for this changes
NOTHING in the FSM — the Protocol signature is untouched, so the reviewed 32-case
invariant suite still binds.

WIRE CONTRACT (must match the merged C# exactly — do not drift):

  * Framing (`LoopbackFrameParser` / `LoopbackControlServer`): every message is a
    4-byte big-endian UNSIGNED length prefix followed by that many UTF-8 payload
    bytes. A client sends TWO frames per request — first the operator-token frame
    (owner-local bind policy, §5.3), then the request-envelope frame — and reads
    back exactly one receipt frame.
  * Envelope (`EnvelopeCodec.Decode` / `RequestEnvelope`): a flat JSON object
    {nonce, seq, expiry, hmac, role, worldUid, verb, requestId,
    connectionGeneration, args:{...}}. `connectionGeneration` is REQUIRED and
    strictly positive (schema `minimum: 1`); the server rejects a stale
    (pre-reconnect) generation as StaleGeneration even when the HMAC verifies.
  * HMAC (`RequestHmac.CanonicalString`): HMAC-SHA256, lowercase hex, over the
    '\\n'-joined fixed field order {nonce, seq, expiry, role, worldUid, verb,
    requestId, connectionGeneration}. Tampering ANY field (generation included)
    invalidates authentication.
  * Receipt (`EnvelopeCodec.EncodeReceipt`): {requestId, verb, outcome, reason,
    role, worldUid, seq, ts, connectionGeneration, status[, observed]}. The
    receipt echoes the server's CURRENT bound generation; the runner reads it to
    form the next request's `connectionGeneration`, and a reconnect advances it so
    a pre-reconnect envelope refuses.

CONNECTION-GENERATION TRACKING (the M2R binding this satisfies): the transport
carries a per-endpoint `connectionGeneration`, seeds it from the run config, and
advances it from each receipt. A request built against the tracked generation is
accepted; a request replayed after a reconnect (older generation) is rejected by
the server as StaleGeneration — the whole point of pinning the field `minimum: 1`.

MATURITY (M6-EXEC CAPABILITY, NOT PERFORMED): this makes a live in-world run
*possible*; it does not perform one. The transport is exercised in the test suite
against a genuine in-process loopback socket STUB that speaks the identical
framing/envelope/receipt contract — proving the wire path end-to-end with NO
Valheim, NO game I/O, NO deploy. A real two-client cold run is a separate,
operator-authorized execution, never triggered by importing or unit-testing this.

Engine-free: stdlib sockets only. No Valheim/BepInEx/Unity import. The receipt→
FSM-`Receipt` mapping re-signs with the orchestrator's injected `integrity_key`
so the FSM's in-process correlation tag holds; wire authenticity is the C# side's
HMAC + M4 receipt-hash-chain, not this Python tag (which guards in-process only).
"""
from __future__ import annotations

import hashlib
import hmac
import json
import socket
import struct
import time
from dataclasses import dataclass, field
from typing import Any, Dict, List, Mapping, Optional

from fsm.errors import CleanupError, TransportError
from fsm.schema import ActionRequest, Receipt

# The client control channel is Client-role; the server fixture channel is
# Server-role. The FSM's four T022 legs are all Client-role actions (Craft,
# UpgradeItem, ReadItem, TamperField); server fixtures are provisioned outside
# the leg FSM. The transport routes by actor to a per-actor endpoint.
_MAX_FRAME_BYTES = 64 * 1024  # mirror LoopbackFrameParser.MaxPayloadBytes
_HEADER = struct.Struct(">I")  # 4-byte big-endian unsigned length prefix

# Wire outcome vocabularies (receipt.schema.json): the M1 admission decision and
# the M4 mechanical evidence outcome. Only these map to the FSM's "ok"; every
# other value is surfaced verbatim so the FSM fails closed on it (never a PASS).
_OK_WIRE_OUTCOMES = frozenset({"admitted", "OK", "Ok"})


def _canonical_string(
    nonce: str,
    seq: int,
    expiry: int,
    role: str,
    world_uid: int,
    verb: str,
    request_id: str,
    connection_generation: int,
) -> str:
    """Reproduce RequestHmac.CanonicalString byte-for-byte (fixed '\\n' order)."""
    return "\n".join(
        [
            nonce,
            str(seq),
            str(expiry),
            role,
            str(world_uid),
            verb,
            request_id,
            str(connection_generation),
        ]
    )


def compute_hmac(secret: str, canonical: str) -> str:
    """Lowercase-hex HMAC-SHA256 over the canonical string (mirror RequestHmac.Compute)."""
    return hmac.new(secret.encode("utf-8"), canonical.encode("utf-8"), hashlib.sha256).hexdigest()


def encode_frame(payload: str) -> bytes:
    """4-byte BE length prefix + UTF-8 body (mirror LoopbackFrameParser.EncodeFrame)."""
    body = payload.encode("utf-8")
    if len(body) > _MAX_FRAME_BYTES:
        raise TransportError(f"frame payload {len(body)}B exceeds max {_MAX_FRAME_BYTES}B")
    return _HEADER.pack(len(body)) + body


def read_frame(sock: socket.socket, deadline_s: float) -> str:
    """Read exactly one length-prefixed frame or raise TransportError on a short/oversized read."""
    header = _recv_exact(sock, _HEADER.size, deadline_s)
    (declared,) = _HEADER.unpack(header)
    if declared <= 0 or declared > _MAX_FRAME_BYTES:
        raise TransportError(f"receipt frame declares invalid length {declared}")
    body = _recv_exact(sock, declared, deadline_s)
    return body.decode("utf-8")


def _recv_exact(sock: socket.socket, count: int, deadline_s: float) -> bytes:
    chunks: List[bytes] = []
    got = 0
    while got < count:
        remaining = deadline_s - time.monotonic()
        if remaining <= 0:
            raise TransportError("timed out reading control frame")
        sock.settimeout(remaining)
        try:
            chunk = sock.recv(count - got)
        except socket.timeout as exc:
            raise TransportError("timed out reading control frame") from exc
        except OSError as exc:
            raise TransportError(f"socket error reading control frame: {exc}") from exc
        if not chunk:
            raise TransportError("peer closed the control connection mid-frame")
        chunks.append(chunk)
        got += len(chunk)
    return b"".join(chunks)


@dataclass(frozen=True)
class ChannelEndpoint:
    """One helper control endpoint the runner talks to.

    `role` is the wire role token the C# arming gate pinned ("Client" for the two
    GUI clients, "Server" for the fixture channel). `host` is always loopback in a
    real run (the owner-local bind policy refuses any non-127.0.0.1 peer); the test
    stub uses an ephemeral loopback port too.
    """

    host: str
    port: int
    role: str = "Client"


@dataclass
class LiveRunConfig:
    """Per-run wire parameters the runner mints (ADR-0009 §6 — the runner is authority).

    `endpoints` maps an FSM actor ("client_a" / "client_b" / "server") to its helper
    endpoint. `operator_token` is the per-session owner-local bind secret; `hmac_secret`
    signs every envelope. `start_generation` seeds each endpoint's connection generation
    (>=1, schema `minimum: 1`).
    """

    nonce: str
    world_uid: int
    expiry_unix_ms: int
    operator_token: str
    hmac_secret: str
    endpoints: Mapping[str, ChannelEndpoint]
    integrity_key: bytes = b"fsm-fake-integrity-key"
    start_generation: int = 1
    connect_timeout_s: float = 3.0
    request_timeout_s: float = 8.0

    def __post_init__(self) -> None:
        if self.start_generation < 1:
            # The wire schema pins connectionGeneration >= 1; a 0 would silently
            # sidestep the stale-generation defense.
            raise ValueError("start_generation must be >= 1 (schema minimum: 1)")


class LiveReceiptAdapter:
    """Map a merged-helper wire receipt (JSON) into an FSM `Receipt`.

    The FSM sees only `Receipt`. The wire receipt carries the descriptive facts
    (outcome, connectionGeneration, observed); this adapter parses them and stamps
    the in-process integrity tag with the orchestrator's `integrity_key` so the
    FSM's four-part correlation + tamper check hold. Wire authenticity is the C#
    HMAC + M4 receipt-hash-chain; this tag guards only against in-Python tamper.
    """

    def __init__(self, run_nonce: str, integrity_key: bytes) -> None:
        self._nonce = run_nonce
        self._key = integrity_key

    def to_receipt(self, payload: str, actor: str, expected_seq: int) -> Receipt:
        try:
            obj = json.loads(payload)
        except (json.JSONDecodeError, TypeError) as exc:
            raise TransportError(f"unparseable receipt payload: {exc}") from exc
        if not isinstance(obj, dict):
            raise TransportError("receipt payload was not a JSON object")

        wire_outcome = str(obj.get("outcome", ""))
        outcome = "ok" if wire_outcome in _OK_WIRE_OUTCOMES else (wire_outcome or "error")
        observed_raw = obj.get("observed")
        observed: Dict[str, Any] = dict(observed_raw) if isinstance(observed_raw, dict) else {}

        request_id = str(obj.get("requestId", ""))
        verb = str(obj.get("verb", ""))
        # The server echoes its CURRENT generation; the FSM correlates on it, so a
        # stale (pre-reconnect) receipt fails the FSM's conn-gen check too.
        conn_gen = int(obj.get("connectionGeneration", 0))
        # The wire echoes seq; if absent, fall back to the seq we sent.
        seq = int(obj.get("seq", expected_seq))

        base = Receipt(
            request_id=request_id,
            actor=actor,
            verb=verb,
            seq=seq,
            conn_gen=conn_gen,
            run_nonce=self._nonce,
            outcome=outcome,
            observed=observed,
            integrity=None,
        )
        return Receipt(
            request_id=base.request_id,
            actor=base.actor,
            verb=base.verb,
            seq=base.seq,
            conn_gen=base.conn_gen,
            run_nonce=base.run_nonce,
            outcome=base.outcome,
            observed=base.observed,
            integrity=self._sign(base),
        )

    def _sign(self, r: Receipt) -> str:
        body = json.dumps(r.observed, sort_keys=True, separators=(",", ":"))
        msg = (
            f"{r.run_nonce}|{r.request_id}|{r.actor}|{r.conn_gen}|{r.seq}|"
            f"{r.outcome}|{body}"
        ).encode()
        return hmac.new(self._key, msg, hashlib.sha256).hexdigest()


class LiveLoopbackTransport:
    """Concrete `fsm.transport.Transport` over the owner-local loopback channel.

    Satisfies the Protocol exactly — `now()`, `send()`, `cleanup()` — so the FSM
    and its 32-case invariant suite are untouched. One TCP connection per request
    (the C# server is strict single-slot / one-connection-one-request); the socket
    is closed after each receipt. `now()` is a monotonic millisecond tick used only
    for deadline arithmetic, never wall-clock semantics.
    """

    def __init__(self, config: LiveRunConfig) -> None:
        self._cfg = config
        self._adapter = LiveReceiptAdapter(config.nonce, config.integrity_key)
        # Per-actor tracked connection generation (advances from receipts).
        self._generation: Dict[str, int] = {
            actor: config.start_generation for actor in config.endpoints
        }
        self._start_monotonic = time.monotonic()
        self._sockets_opened = 0
        self._closed = False

    # -- Transport protocol -------------------------------------------------
    def now(self) -> int:
        return int((time.monotonic() - self._start_monotonic) * 1000.0)

    def send(self, request: ActionRequest) -> List[Any]:
        if self._closed:
            raise TransportError("transport already cleaned up")
        endpoint = self._cfg.endpoints.get(request.actor)
        if endpoint is None:
            raise TransportError(f"no endpoint configured for actor {request.actor!r}")

        generation = self._generation[request.actor]
        envelope = self._build_envelope(request, endpoint, generation)
        receipt_json = self._round_trip(endpoint, envelope)
        receipt = self._adapter.to_receipt(receipt_json, request.actor, request.seq)

        # Advance the tracked generation from the server's echoed current one, so a
        # subsequent request rides the live generation and a pre-reconnect replay
        # would present a stale one (rejected server-side as StaleGeneration).
        if receipt.conn_gen >= 1:
            self._generation[request.actor] = receipt.conn_gen
        return [receipt]

    def cleanup(self) -> None:
        # There is no persistent socket/fixture to tear down here (one connection
        # per request, closed immediately). Marking closed makes a post-cleanup
        # send fail closed. A live lane/fixture teardown is the operator driver's
        # job (lane_launcher / client_launcher), not the wire transport's.
        if self._closed:
            return
        self._closed = True

    # -- wire ---------------------------------------------------------------
    def _build_envelope(
        self, request: ActionRequest, endpoint: ChannelEndpoint, generation: int
    ) -> str:
        canonical = _canonical_string(
            self._cfg.nonce,
            request.seq,
            self._cfg.expiry_unix_ms,
            endpoint.role,
            self._cfg.world_uid,
            request.verb,
            request.request_id,
            generation,
        )
        envelope = {
            "nonce": self._cfg.nonce,
            "seq": request.seq,
            "expiry": self._cfg.expiry_unix_ms,
            "hmac": compute_hmac(self._cfg.hmac_secret, canonical),
            "role": endpoint.role,
            "worldUid": self._cfg.world_uid,
            "verb": request.verb,
            "requestId": request.request_id,
            "connectionGeneration": generation,
            "args": dict(request.args),
        }
        return json.dumps(envelope, separators=(",", ":"), sort_keys=True)

    def _round_trip(self, endpoint: ChannelEndpoint, envelope_json: str) -> str:
        deadline = time.monotonic() + self._cfg.request_timeout_s
        try:
            sock = socket.create_connection(
                (endpoint.host, endpoint.port), timeout=self._cfg.connect_timeout_s
            )
        except OSError as exc:
            raise TransportError(
                f"cannot connect to helper {endpoint.host}:{endpoint.port}: {exc}"
            ) from exc
        self._sockets_opened += 1
        try:
            sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            # Owner-local bind policy: the token frame FIRST, then the request frame.
            sock.sendall(encode_frame(self._cfg.operator_token))
            sock.sendall(encode_frame(envelope_json))
            return read_frame(sock, deadline)
        finally:
            try:
                sock.close()
            except OSError:
                pass

    # -- introspection (tests/audit) ---------------------------------------
    @property
    def sockets_opened(self) -> int:
        return self._sockets_opened

    def generation_for(self, actor: str) -> int:
        return self._generation.get(actor, 0)
