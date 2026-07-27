"""GABS-mediated client launch coverage (ADR-0009 §5, §9) — M6-LAUNCH.

The card this suite exists to satisfy: five consecutive layers passed review while
remaining DEAD on the real path, because `spawn_client` returned a bare `Popen` that
could never produce a modded, armed, joined client (no bootstrap env, no `+connect`
join, no loopback control port → the helper never arms → connection-refused → exit 1).

These tests assert the CONSTRUCTED launch request/argv/env actually carries the
launch-critical fields, and that readiness polling RETRIES on a simulated ValBridge
startup wedge and eventually FAILS CLOSED with a named diagnostic — never a hang and
never a dead handle. A test that only asserts "a request object was returned" would be
a blocking review finding, so every assertion here inspects real launch content.

Injected seams only: NO real GABS, NO real socket, NO game, NO sleep.
"""
from __future__ import annotations

import os

import pytest

from runner_core.operator_drivers import (
    BOOTSTRAP_ENV_VAR,
    STEAM_ID_ENV_VAR,
    BootRetryPolicy,
    ClientLaunchError,
    ClientLaunchRequest,
    ClientSpec,
    GabsClientBooter,
    LICENSED_STEAM_IDENTITIES,
)

LANE_HOST = "127.0.0.1"
LANE_PORT = 2476  # the genuine disposable-lane join port (t_c4261da7 world_uid=-898655635)
LOOPBACK_PORT = 48610
GABS_ENDPOINT = "http://localhost:8080/mcp"
BOOTSTRAP_PATH = "/run/sbpr-qa/arm-bootstrap-client_a.json"


def _spec(actor="client_a", **overrides) -> ClientSpec:
    kwargs = dict(
        actor=actor,
        steam_id=LICENSED_STEAM_IDENTITIES[0],
        binary_path="/lane/a/valheim.x86_64",
        gabs_endpoint=GABS_ENDPOINT,
        game_id="valheim",
        bootstrap_path=BOOTSTRAP_PATH,
        connect_host=LANE_HOST,
        connect_port=LANE_PORT,
        loopback_port=LOOPBACK_PORT,
    )
    kwargs.update(overrides)
    return ClientSpec(**kwargs)


class _Recorder:
    """Records every GABS/env/readiness action so a test can assert the boot DROVE."""

    def __init__(self, *, ready_after_calls=1, start_raises_times=0):
        self.env_applied = []
        self.started = []
        self.killed = []
        self._ready_after_calls = ready_after_calls
        self._ready_calls = 0
        self._start_raises_times = start_raises_times
        self._start_count = 0
        self.slept = []
        self.gone = True  # process_gone result (mutable so a test can flip it)

    def apply_env(self, request):
        self.env_applied.append(dict(request.launch_env))

    def gabs_start(self, request):
        self._start_count += 1
        if self._start_count <= self._start_raises_times:
            raise RuntimeError(f"games_start wedged (attempt {self._start_count})")
        self.started.append(request.game_id)

    def gabs_kill(self, request):
        self.killed.append(request.game_id)

    def control_ready(self, request):
        self._ready_calls += 1
        return self._ready_calls >= self._ready_after_calls

    def process_gone(self, request):
        return self.gone

    def sleep(self, s):
        self.slept.append(s)

    def booter(self, policy=None):
        return GabsClientBooter(
            apply_env=self.apply_env,
            gabs_start=self.gabs_start,
            gabs_kill=self.gabs_kill,
            control_ready=self.control_ready,
            process_gone=self.process_gone,
            sleep=self.sleep,
            policy=policy or BootRetryPolicy(max_attempts=6, readiness_timeout_s=30.0, poll_interval_s=10.0),
        )


# --------------------------------------------------------------------------- #
# The constructed launch request actually carries the launch-critical fields.
# --------------------------------------------------------------------------- #

def test_build_request_carries_bootstrap_connect_gabs_and_loopback() -> None:
    request = GabsClientBooter.build_request(_spec())

    # Bootstrap env var — the single field the bare-binary launch dropped. Without it
    # the helper stays DISARMED and never binds its loopback port.
    assert request.launch_env[BOOTSTRAP_ENV_VAR] == BOOTSTRAP_PATH
    assert request.bootstrap_env_value == BOOTSTRAP_PATH
    # Licensed identity env the product/Steam layer reads.
    assert request.launch_env[STEAM_ID_ENV_VAR] == LICENSED_STEAM_IDENTITIES[0]
    # The correct `+connect` join target — port 2476, the genuine lane.
    assert request.connect_target == f"{LANE_HOST}:{LANE_PORT}"
    assert tuple(request.connect_args) == ("+connect", f"{LANE_HOST}:{LANE_PORT}")
    assert str(LANE_PORT) in request.connect_target
    # The actor's GABS endpoint + gameId.
    assert request.gabs_endpoint == GABS_ENDPOINT
    assert request.game_id == "valheim"
    # The helper's loopback control port the readiness poll targets.
    assert request.loopback_port == LOOPBACK_PORT


def test_build_request_fails_closed_when_launch_fields_missing() -> None:
    # A spec with NO gabs/bootstrap/connect/loopback (the old bare shape) must be
    # refused, not silently launched as a bare binary that can never arm.
    bare = ClientSpec(actor="client_a", steam_id=LICENSED_STEAM_IDENTITIES[0], binary_path="/a/valheim.x86_64")
    with pytest.raises(ClientLaunchError) as ei:
        GabsClientBooter.build_request(bare)
    msg = str(ei.value)
    for field in ("gabs_endpoint", "bootstrap_path", "connect_host", "connect_port", "loopback_port"):
        assert field in msg


def test_boot_drives_gabs_env_and_returns_live_handle() -> None:
    rec = _Recorder(ready_after_calls=1)
    request = rec.booter().boot(_spec())

    # It cleared any stale instance, published the launch env, then requested start.
    assert rec.killed == ["valheim"]
    assert rec.env_applied == [{BOOTSTRAP_ENV_VAR: BOOTSTRAP_PATH, STEAM_ID_ENV_VAR: LICENSED_STEAM_IDENTITIES[0]}]
    assert rec.started == ["valheim"]
    # Returned the request handle (armed), and never slept because ready on first poll.
    assert isinstance(request, ClientLaunchRequest)
    assert request.loopback_port == LOOPBACK_PORT
    assert rec.slept == []


# --------------------------------------------------------------------------- #
# Readiness polling retries on a simulated ValBridge wedge, then fails closed.
# --------------------------------------------------------------------------- #

def test_boot_retries_on_valbridge_wedge_then_arms() -> None:
    # The loopback port refuses for two full attempts (the wedge), then the third boot
    # arms. control_ready returns False until the Nth call.
    policy = BootRetryPolicy(max_attempts=6, readiness_timeout_s=20.0, poll_interval_s=10.0)  # 2 polls/attempt
    # 2 polls/attempt * 2 wedged attempts = 4 False, then arm on the 5th poll.
    rec = _Recorder(ready_after_calls=5)
    request = rec.booter(policy).boot(_spec())

    assert isinstance(request, ClientLaunchRequest)
    # Re-rolled the whole boot: games_kill/start ran once per attempt (3 attempts).
    assert len(rec.started) == 3
    assert len(rec.killed) == 3
    # It polled (and slept) rather than blindly sleeping-and-hoping.
    assert rec.slept  # explicit poll interval waits occurred


def test_boot_fails_closed_with_named_diagnostic_when_never_arms() -> None:
    policy = BootRetryPolicy(max_attempts=3, readiness_timeout_s=10.0, poll_interval_s=10.0)  # 1 poll/attempt
    rec = _Recorder(ready_after_calls=9999)  # never ready
    with pytest.raises(ClientLaunchError) as ei:
        rec.booter(policy).boot(_spec())
    msg = str(ei.value)
    # Names the stage + the loopback port + the attempt budget — not a bare hang.
    assert "never reached armed readiness" in msg
    assert str(LOOPBACK_PORT) in msg
    assert "3 boot attempts" in msg
    # Re-rolled the full budget and torn the last instance down (kill each attempt + final).
    assert len(rec.started) == 3
    assert len(rec.killed) == 4  # one per attempt + a final teardown kill


def test_boot_rerolls_when_gabs_start_itself_wedges() -> None:
    # games_start throws on the first attempt (a launch-stage wedge); the booter must
    # re-roll rather than propagate a dead handle. Second attempt arms.
    policy = BootRetryPolicy(max_attempts=3, readiness_timeout_s=10.0, poll_interval_s=10.0)
    rec = _Recorder(ready_after_calls=1, start_raises_times=1)
    request = rec.booter(policy).boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert rec.started == ["valheim"]  # only the successful (2nd) attempt recorded a start


# --------------------------------------------------------------------------- #
# Deterministic GABS teardown with a verified process-gone check.
# --------------------------------------------------------------------------- #

def test_kill_verifies_process_gone() -> None:
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    request = booter.boot(_spec())
    rec.killed.clear()
    booter.kill(request)
    assert rec.killed == ["valheim"]  # games_kill invoked


def test_kill_fails_closed_when_process_lingers() -> None:
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    request = booter.boot(_spec())
    rec.gone = False  # still present after kill
    with pytest.raises(ClientLaunchError) as ei:
        booter.kill(request)
    assert "teardown unverified" in str(ei.value)


def test_kill_refuses_foreign_handle() -> None:
    # A handle this booter did not produce (e.g. a raw Popen) must be ignored, never
    # touched — the launcher only tears down what it launched.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    booter.kill(object())  # not a ClientLaunchRequest
    assert rec.killed == []


# --------------------------------------------------------------------------- #
# Policy validation.
# --------------------------------------------------------------------------- #

def test_boot_policy_rejects_nonsense() -> None:
    with pytest.raises(ValueError):
        BootRetryPolicy(max_attempts=0)
    with pytest.raises(ValueError):
        BootRetryPolicy(readiness_timeout_s=0)
    with pytest.raises(ValueError):
        BootRetryPolicy(poll_interval_s=0)


# --------------------------------------------------------------------------- #
# build_live_run threads the descriptor's GABS-launch fields into the ClientSpecs
# so the REAL environment boots modded+armed+joined clients (not bare binaries).
# --------------------------------------------------------------------------- #

def test_build_live_run_carries_gabs_launch_fields_into_client_specs() -> None:
    import hashlib

    from runner_core.live_composition import build_live_run
    from runner_core.manifest import REQUIRED_PARTS

    descriptor = {
        "integrity_key": "launch-integrity",
        "world_uid": "-898655635",
        "world_name": "homestead-launch",
        "expiry": 10_000_000,
        "lane": {"lane_id": "launchl", "world_name": "homestead-launch", "world_uid": 1, "port": 2476},
        "clients": [
            {
                "actor": "client_a", "steam_id": LICENSED_STEAM_IDENTITIES[0],
                "binary_path": "/lane/a/valheim.x86_64",
                "gabs_endpoint": "http://localhost:8080/mcp", "game_id": "valheim",
                "bootstrap_path": "/run/sbpr-qa/boot-a.json",
                "connect_host": "127.0.0.1", "connect_port": 2476, "loopback_port": 48610,
            },
            {
                "actor": "client_b", "steam_id": LICENSED_STEAM_IDENTITIES[1],
                "binary_path": "/lane/b/valheim.x86_64",
                "gabs_endpoint": "http://localhost:8081/mcp", "game_id": "valheim",
                "bootstrap_path": "/run/sbpr-qa/boot-b.json",
                "connect_host": "127.0.0.1", "connect_port": 2476, "loopback_port": 48611,
            },
        ],
        "wire": {
            "nonce": "launch-nonce", "world_uid": 424242, "expiry_unix_ms": 32_500_000_000_000,
            "operator_token": "tok", "hmac_secret": "sec",
            "endpoints": {
                "client_a": {"host": "127.0.0.1", "port": 5, "role": "Client"},
                "client_b": {"host": "127.0.0.1", "port": 6, "role": "Client"},
            },
            "entitlement": {"host": "127.0.0.1", "port": 7, "role": "Server"},
        },
        "lease": {"lane_id": "launchl", "our_id": "runner-1"},
        "pins": {p: hashlib.sha256(p.encode()).hexdigest() for p in REQUIRED_PARTS},
        "expected_conn_gen": {"client_a": 1, "client_b": 1, "server": 1},
        "actor_identity": {"server": "id-s", "client_a": "id-a", "client_b": "id-b"},
        "server": {
            "server_binary": "/lane/valheim_server.x86_64", "server_args": [],
            "server_ready_log": "/lane/server.log", "server_ready_marker": "Game server connected",
            "client_binary": "/lane/valheim.x86_64", "adminlist_path": "/lane/adminlist.txt",
            "boot_policy": {"max_attempts": 6, "readiness_timeout_s": 150.0, "poll_interval_s": 10.0},
        },
    }

    plan, _env = build_live_run(descriptor)
    # Each client spec resolves into a launch request carrying the launch-critical
    # fields — the exact thing a bare-binary launch dropped.
    reqs = {c.actor: GabsClientBooter.build_request(c) for c in plan.clients}
    assert reqs["client_a"].launch_env[BOOTSTRAP_ENV_VAR] == "/run/sbpr-qa/boot-a.json"
    assert reqs["client_a"].connect_target == "127.0.0.1:2476"
    assert reqs["client_a"].gabs_endpoint == "http://localhost:8080/mcp"
    assert reqs["client_a"].loopback_port == 48610
    assert reqs["client_b"].gabs_endpoint == "http://localhost:8081/mcp"
    assert reqs["client_b"].loopback_port == 48611
    assert reqs["client_b"].launch_env[STEAM_ID_ENV_VAR] == LICENSED_STEAM_IDENTITIES[1]

