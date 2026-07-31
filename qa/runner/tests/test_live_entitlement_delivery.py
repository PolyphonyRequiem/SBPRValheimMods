"""Real-seam entitlement-delivery coverage (ADR-0009 §5, §6) — M6-SEED.

Three prior M6 cards passed because every test supplied its OWN stub `deliver_entitlement`
callable — green against stubs, dead on the real wire, where `real_operator_environment()`
was wired to a raise-only closure. This suite closes that exact defect class:

  * it drives the callable that `real_operator_environment()` / `build_live_run()`
    ACTUALLY construct (never an injected stub) against a loopback control-server stub
    that speaks the genuine owner-local wire protocol (4-byte framing + operator-token
    bind + `RequestHmac` canonical envelope), and asserts the OFFER (commandType=1) and
    BUY (commandType=2) envelopes are genuinely emitted over the socket carrying the
    product admin verb `sbpr_master`, and that the product's operator line is parsed back;
  * it asserts `real_operator_environment()` NEVER yields a `deliver_entitlement` that
    raises the retired "requires a live product control channel" stub error.

Nothing here launches a game, mutates a file, or mints entitlement — the stub is a pure
in-process socket that echoes the product's operator line; the seeder only ASKS.
"""
from __future__ import annotations

import json
import os
import socket
import sys
import threading
import time

import pytest

RUNNER_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if RUNNER_DIR not in sys.path:
    sys.path.insert(0, RUNNER_DIR)

from fsm.errors import TransportError  # noqa: E402
from runner_core.live_composition import (  # noqa: E402
    RealOperatorConfig,
    build_live_run,
    real_operator_environment,
)
from runner_core.live_transport import (  # noqa: E402
    ChannelEndpoint,
    EntitlementControlChannel,
    EntitlementDeliveryConfig,
    SBPR_MASTER_ADMIN_VERB,
    _canonical_string,
    compute_hmac,
    encode_frame,
    read_frame,
)

OPERATOR_TOKEN = "operator-token-seed"
HMAC_SECRET = "hmac-secret-seed"
NONCE = "seed-run-nonce-0001"
WORLD_UID = 424242
EXPIRY = 32_600_000_000_000


class AdminControlStub:
    """In-process echo of the merged C# loopback control server for the admin channel.

    Wire-faithful: reads the token frame then the request frame, verifies the operator
    token and the canonical HMAC (over the SAME fixed field order the four-leg transport
    uses), and replies with one receipt frame carrying the product's operator line in
    `observed.operator_line`. A wrong token / bad HMAC is rejected, so a test can prove
    the delivery is genuinely authenticated on the wire, not faked.
    """

    def __init__(self, *, accept_any: bool = False) -> None:
        self._accept_any = accept_any
        self._srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._srv.bind(("127.0.0.1", 0))
        self._srv.listen(8)
        self.port = self._srv.getsockname()[1]
        self.requests: list[dict] = []
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()

    def _serve(self) -> None:
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
                    deadline = time.monotonic() + 3.0
                    token = read_frame(conn, deadline)
                    payload = read_frame(conn, deadline)
                except TransportError:
                    continue
                env = json.loads(payload)
                self.requests.append(env)
                conn.sendall(encode_frame(json.dumps(self._decide(token, env))))

    def _decide(self, token: str, env: dict) -> dict:
        if not self._accept_any:
            if token != OPERATOR_TOKEN:
                return self._reject("BadOperatorToken", env)
            canonical = _canonical_string(
                env["nonce"], env["seq"], env["expiry"], env["role"],
                env["worldUid"], env["verb"], env["requestId"], env["connectionGeneration"],
            )
            if compute_hmac(HMAC_SECRET, canonical) != env["hmac"]:
                return self._reject("BadHmac", env)
        # The label is DERIVED from the discriminator, not carried on the wire — the real
        # helper's closed schema permits only `discriminator`. Mirroring the product's own
        # CmdOffer=1 / CmdBuy=2 (MasterworkOwnershipProvisioningAdmin.cs).
        command = {1: "offer", 2: "buy"}.get(
            env.get("args", {}).get("discriminator"), "?"
        )
        return {
            "requestId": env["requestId"],
            "verb": env["verb"],
            "outcome": "OK",
            "reason": "None",
            "role": env["role"],
            "worldUid": env["worldUid"],
            "seq": env["seq"],
            "ts": 1,
            "connectionGeneration": env["connectionGeneration"],
            "observed": {"operator_line": f"sbpr_master {command}: Applied"},
        }

    def _reject(self, reason: str, env: dict) -> dict:
        return {
            "requestId": env.get("requestId", ""),
            "verb": env.get("verb", ""),
            "outcome": "REJECTED",
            "reason": reason,
            "role": env.get("role", "Server"),
            "worldUid": env.get("worldUid", 0),
            "seq": env.get("seq", 0),
            "ts": 1,
            "connectionGeneration": env.get("connectionGeneration", 1),
        }

    def close(self) -> None:
        self._stop.set()
        try:
            self._srv.close()
        except OSError:
            pass
        self._thread.join(timeout=2.0)


def _delivery_config(stub: AdminControlStub) -> EntitlementDeliveryConfig:
    return EntitlementDeliveryConfig(
        endpoint=ChannelEndpoint(host="127.0.0.1", port=stub.port, role="Client"),
        operator_token=OPERATOR_TOKEN,
        hmac_secret=HMAC_SECRET,
        nonce=NONCE,
        world_uid=WORLD_UID,
        expiry_unix_ms=EXPIRY,
    )


def _real_config(stub: AdminControlStub) -> RealOperatorConfig:
    return RealOperatorConfig(
        server_binary="/lane/valheim_server.x86_64",
        server_args=("-name", "qa", "-port", "3456"),
        server_ready_log="/lane/server.log",
        server_ready_marker="Game server connected",
        client_binary="/lane/valheim.x86_64",
        adminlist_path="/lane/adminlist.txt",
        entitlement_delivery=_delivery_config(stub),
    )


def _descriptor(stub: AdminControlStub) -> dict:
    import hashlib
    from runner_core.manifest import REQUIRED_PARTS

    return {
        "integrity_key": "seed-integrity",
        "world_uid": "424242",
        "world_name": "homestead-seed",
        "expiry": 10_000_000,
        "lane": {"lane_id": "seedl", "world_name": "homestead-seed", "world_uid": 1, "port": 3456},
        "clients": [
            {"actor": "client_a", "steam_id": "76561197965627562", "binary_path": "/lane/a/valheim.x86_64"},
            {"actor": "client_b", "steam_id": "76561198671522196", "binary_path": "/lane/b/valheim.x86_64"},
        ],
        "wire": {
            "world_uid": WORLD_UID,
            "endpoints": {
                "client_a": {"host": "127.0.0.1", "port": stub.port, "role": "Client"},
                "client_b": {"host": "127.0.0.1", "port": stub.port, "role": "Client"},
            },
            "entitlement": {"host": "127.0.0.1", "port": stub.port, "role": "Client"},
        },
        "lease": {"lane_id": "seedl", "our_id": "runner-1"},
        "pins": {p: hashlib.sha256(p.encode()).hexdigest() for p in REQUIRED_PARTS},
        "expected_conn_gen": {"client_a": 1, "client_b": 1, "server": 1},
        "actor_identity": {"server": "id-s", "client_a": "id-a", "client_b": "id-b"},
        "server": {
            "server_binary": "/lane/valheim_server.x86_64",
            "server_args": [],
            "server_ready_log": "/lane/server.log",
            "server_ready_marker": "Game server connected",
            "client_binary": "/lane/valheim.x86_64",
            "adminlist_path": "/lane/adminlist.txt",
        },
    }


# --------------------------------------------------------------------------- #
# The delivery seam the REAL environment constructs actually emits OFFER→BUY.
# --------------------------------------------------------------------------- #

def test_real_environment_delivery_emits_offer_then_buy_over_the_wire() -> None:
    stub = AdminControlStub()
    try:
        env = real_operator_environment(_real_config(stub))
        # Drive the REAL callable the environment built (NOT an injected stub).
        offer_line = env.deliver_entitlement(1)
        buy_line = env.deliver_entitlement(2)

        # Both envelopes were genuinely emitted over the socket carrying sbpr_master.
        assert len(stub.requests) == 2
        offer_env, buy_env = stub.requests
        assert offer_env["verb"] == SBPR_MASTER_ADMIN_VERB == "sbpr_master"
        assert buy_env["verb"] == "sbpr_master"
        # The OFFER (1) then BUY (2) discriminators rode in args.discriminator, correct
        # order. The catalog declares `sbpr_master` with EXACTLY this one argument and a
        # CLOSED schema, so the older {command, commandType} pair would be rejected
        # OutOfBoundsArg by the helper's admission gate before reaching the relay.
        assert offer_env["args"] == {"discriminator": 1}
        assert buy_env["args"] == {"discriminator": 2}
        # The product's operator line was parsed back from the receipt.
        assert offer_line == "sbpr_master offer: Applied"
        assert buy_line == "sbpr_master buy: Applied"
    finally:
        stub.close()


def test_build_live_run_wires_the_real_delivering_seam() -> None:
    # build_live_run now MINTS a fresh operator_token/hmac_secret per run, so the
    # stub can no longer match a fixed token — it accepts any authenticated frame
    # and we assert the seam genuinely emitted OFFER over the wire.
    stub = AdminControlStub(accept_any=True)
    try:
        _plan, env = build_live_run(_descriptor(stub))
        # The env from build_live_run carries a REAL delivering callable — driving it
        # emits a genuine sbpr_master envelope and returns the product line.
        line = env.deliver_entitlement(1)
        assert stub.requests[0]["verb"] == "sbpr_master"
        assert stub.requests[0]["args"] == {"discriminator": 1}
        assert line == "sbpr_master offer: Applied"
    finally:
        stub.close()


def test_seeder_run_over_real_channel_yields_both_legs() -> None:
    from runner_core.operator_drivers import EntitlementSeeder

    stub = AdminControlStub()
    try:
        env = real_operator_environment(_real_config(stub))
        results = EntitlementSeeder(env.deliver_entitlement).seed()
        assert [r.discriminator for r in results] == [1, 2]
        assert [r.command for r in results] == ["offer", "buy"]
        assert [s["args"]["discriminator"] for s in stub.requests] == [1, 2]
    finally:
        stub.close()


# --------------------------------------------------------------------------- #
# Regression: the real seam NEVER yields the retired raise-only stub.
# --------------------------------------------------------------------------- #

def test_real_environment_never_yields_raising_stub_callable() -> None:
    stub = AdminControlStub()
    try:
        env = real_operator_environment(_real_config(stub))
        # Driving it against the live stub must NOT raise the old
        # "requires a live product control channel" stub error.
        try:
            env.deliver_entitlement(1)
        except TransportError as exc:  # pragma: no cover - would be the regression
            assert "requires a live product control channel" not in str(exc)
            raise
    finally:
        stub.close()


def test_stub_error_string_is_gone_from_source() -> None:
    # The retired raise-only stub message must not survive anywhere in the composition.
    comp = os.path.join(RUNNER_DIR, "runner_core", "live_composition.py")
    with open(comp, "r", encoding="utf-8") as fh:
        src = fh.read()
    assert "requires a live product control channel" not in src
    assert "def _deliver_admin_command" not in src


# --------------------------------------------------------------------------- #
# The channel is genuinely authenticated on the wire (not a fake pass-through).
# --------------------------------------------------------------------------- #

def test_bad_operator_token_rejected_on_the_wire() -> None:
    stub = AdminControlStub()
    try:
        cfg = EntitlementDeliveryConfig(
            endpoint=ChannelEndpoint(host="127.0.0.1", port=stub.port, role="Client"),
            operator_token="WRONG-TOKEN",
            hmac_secret=HMAC_SECRET,
            nonce=NONCE,
            world_uid=WORLD_UID,
            expiry_unix_ms=EXPIRY,
        )
        with pytest.raises(TransportError):
            EntitlementControlChannel(cfg).deliver(1)
    finally:
        stub.close()


def test_unknown_discriminator_refused_before_send() -> None:
    stub = AdminControlStub()
    try:
        ch = EntitlementControlChannel(_delivery_config(stub))
        with pytest.raises(TransportError):
            ch.deliver(3)  # only OFFER(1)/BUY(2) permitted
        assert stub.requests == []  # refused BEFORE anything hit the wire
    finally:
        stub.close()


def test_entitlement_envelope_matches_the_helper_catalog_schema() -> None:
    """Contract guard: the wire shape must satisfy the helper's CLOSED arg schema.

    This test exists because of a real defect. Historically the runner sent
    ``args={"command": "offer", "commandType": 1}`` to a verb that did not exist in
    ``VerbCatalog`` at all, aimed at a Server-role endpoint that starts no listener.
    Every runner-side test passed anyway, because ``AdminControlStub`` answers ANY
    verb with ANY arguments and never checks catalog membership, role admission, or
    the closed-schema rule. That is textbook green-against-stubs, and it hid the gap
    for twelve days.

    The helper declares ``sbpr_master`` with EXACTLY one argument, ``discriminator``,
    a BoundedInt over the product's real CmdOffer=1 / CmdBuy=2. Admission requires
    every declared arg present and in bounds AND rejects any undeclared arg
    (``RequestAdmission.ArgsInBounds``). So this asserts the envelope the runner
    actually emits would survive the REAL gate, not just the permissive stub.
    """
    stub = AdminControlStub()
    try:
        EntitlementControlChannel(_delivery_config(stub)).deliver(1)
        args = stub.requests[0]["args"]
        # Exactly one key, exactly the declared name — an undeclared extra would be
        # rejected OutOfBoundsArg by the real helper.
        assert set(args) == {"discriminator"}
        # In-bounds and an int, not a string: BoundedInt rejects implicit coercion.
        assert args["discriminator"] in (1, 2)
        assert isinstance(args["discriminator"], int)
        # Client role: sbpr_master is a ClientLoopback verb and the dedicated server
        # starts no host listener, so a Server-role envelope is refused RoleMismatch.
        assert stub.requests[0]["role"] == "Client"
    finally:
        stub.close()
