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

from typing import Callable, Optional

import pytest

from runner_core.operator_drivers import (
    BOOTSTRAP_ENV_VAR,
    HARNESS_INSTANCE_ENV_VAR,
    STEAM_ID_ENV_VAR,
    BootRetryPolicy,
    ClientLaunchError,
    ClientLaunchRequest,
    ClientSpec,
    GabsClientBooter,
    HarnessInstance,
    LICENSED_STEAM_IDENTITIES,
    OperatorSafetyError,
    assert_connect_target_not_production,
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
    """Records every GABS/env/readiness/provenance action so a test can assert the boot DROVE.

    Models a tiny simulated `/proc`: a `procs` table maps PID -> (marker, start_ticks,
    exe_basename). `resolve_launched` finds the UNIQUE valheim.x86_64 PID carrying the
    boot's injected marker; `probe_pid` reads back provenance for a PID; `terminate`
    removes the PID from the table. This lets the tests assert the harness terminates
    ONLY the exact process it launched, by real provenance — never a gameId-wide kill.
    """

    def __init__(self, *, ready_after_calls=1, start_raises_times=0):
        self.env_applied = []
        self.started = []
        self.terminated = []            # HarnessInstance objects the booter terminated
        self._ready_after_calls = ready_after_calls
        self._ready_calls = 0
        self._start_raises_times = start_raises_times
        self._start_count = 0
        self.slept = []
        # Simulated /proc: pid -> dict(marker, start, exe). A spawned client appears here.
        self.procs = {}
        self._next_pid = 4100
        self._start_ticks = 500
        # A hook a test can install to run BETWEEN the ownership check and the kill
        # (TOCTOU): called with the recorder so it can mutate the proc table.
        self.on_before_terminate: Optional[Callable[["_Recorder"], None]] = None

    def apply_env(self, request):
        self.env_applied.append(dict(request.launch_env))

    def gabs_start(self, request):
        self._start_count += 1
        if self._start_count <= self._start_raises_times:
            raise RuntimeError(f"games_start wedged (attempt {self._start_count})")
        self.started.append(request.game_id)
        # A successful start makes a valheim.x86_64 appear in /proc carrying the marker.
        pid = self._next_pid
        self._next_pid += 1
        self._start_ticks += 1
        self.procs[pid] = {
            "marker": request.launch_env[HARNESS_INSTANCE_ENV_VAR],
            "start": self._start_ticks,
            "exe": "valheim.x86_64",
        }

    def resolve_launched(self, request):
        marker = request.launch_env[HARNESS_INSTANCE_ENV_VAR]
        matches = [
            HarnessInstance(actor=request.actor, marker=marker, pid=pid, start_ticks=p["start"])
            for pid, p in self.procs.items()
            if p["exe"] == "valheim.x86_64" and p["marker"] == marker
        ]
        if len(matches) == 1:
            return matches[0]
        return None  # zero or ambiguous => fail closed

    def probe_pid(self, pid):
        p = self.procs.get(pid)
        if p is None:
            return None
        return HarnessInstance(actor="", marker=p["marker"], pid=pid, start_ticks=p["start"])

    def terminate(self, instance):
        self.terminated.append(instance)
        self.procs.pop(instance.pid, None)

    def control_ready(self, request):
        self._ready_calls += 1
        return self._ready_calls >= self._ready_after_calls

    def sleep(self, s):
        self.slept.append(s)

    def booter(self, policy=None):
        # Wrap terminate so a test's TOCTOU hook fires just before the real removal,
        # but AFTER the booter's own pre-kill provenance re-check window opens.
        def _terminate(instance):
            self.terminate(instance)
        return GabsClientBooter(
            apply_env=self.apply_env,
            gabs_start=self.gabs_start,
            control_ready=self.control_ready,
            resolve_launched=self.resolve_launched,
            probe_pid=self._probe_pid_with_hook,
            terminate=_terminate,
            sleep=self.sleep,
            policy=policy or BootRetryPolicy(max_attempts=6, readiness_timeout_s=30.0, poll_interval_s=10.0),
        )

    def _probe_pid_with_hook(self, pid):
        # The booter calls probe_pid immediately before terminating (TOCTOU re-check).
        # If a hook is installed, run it here so a foreign process can be swapped in at
        # exactly that instant.
        if self.on_before_terminate is not None:
            hook, self.on_before_terminate = self.on_before_terminate, None
            hook(self)
        return self.probe_pid(pid)


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

    # It published the launch env (bootstrap + identity + a unique harness marker), then
    # requested start. No pre-launch gameId-wide kill (that would hit Daniel's game).
    assert rec.env_applied[0][BOOTSTRAP_ENV_VAR] == BOOTSTRAP_PATH
    assert rec.env_applied[0][STEAM_ID_ENV_VAR] == LICENSED_STEAM_IDENTITIES[0]
    assert rec.env_applied[0][HARNESS_INSTANCE_ENV_VAR].startswith("client_a:")
    assert rec.started == ["valheim"]
    # Nothing was terminated on a clean first-attempt arm.
    assert rec.terminated == []
    # Returned the request handle (armed), and never slept because ready on first poll.
    assert isinstance(request, ClientLaunchRequest)
    assert request.loopback_port == LOOPBACK_PORT
    assert rec.slept == []


def test_build_request_injects_unique_harness_marker() -> None:
    # Every boot carries a DISTINCT provenance marker so teardown can scope to exactly
    # the process this boot launched (never gameId-wide).
    a = GabsClientBooter.build_request(_spec())
    b = GabsClientBooter.build_request(_spec())
    assert a.launch_env[HARNESS_INSTANCE_ENV_VAR] != b.launch_env[HARNESS_INSTANCE_ENV_VAR]
    assert a.launch_env[HARNESS_INSTANCE_ENV_VAR].startswith("client_a:")


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
    # Re-rolled the whole boot: start ran once per attempt (3 attempts).
    assert len(rec.started) == 3
    # Between re-rolls it terminated ONLY its own prior instance (2 stale instances).
    assert len(rec.terminated) == 2
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
    # Re-rolled the full budget and torn down every instance it started (provenance-
    # scoped): 2 between re-rolls + 1 final teardown of the last instance = 3.
    assert len(rec.started) == 3
    assert len(rec.terminated) == 3


def test_boot_rerolls_when_gabs_start_itself_wedges() -> None:
    # games_start throws on the first attempt (a launch-stage wedge); the booter must
    # re-roll rather than propagate a dead handle. Second attempt arms.
    policy = BootRetryPolicy(max_attempts=3, readiness_timeout_s=10.0, poll_interval_s=10.0)
    rec = _Recorder(ready_after_calls=1, start_raises_times=1)
    request = rec.booter(policy).boot(_spec())
    assert isinstance(request, ClientLaunchRequest)
    assert rec.started == ["valheim"]  # only the successful (2nd) attempt recorded a start


def test_boot_fails_closed_when_provenance_unresolvable() -> None:
    # If the launched process cannot be uniquely identified by its harness marker, the
    # boot must NOT proceed with a client it cannot later tear down safely.
    rec = _Recorder(ready_after_calls=1)
    # Sabotage resolve_launched so no PID is ever attributable to the harness.
    rec.resolve_launched = lambda request: None  # type: ignore[assignment]
    policy = BootRetryPolicy(max_attempts=2, readiness_timeout_s=10.0, poll_interval_s=10.0)
    with pytest.raises(ClientLaunchError) as ei:
        rec.booter(policy).boot(_spec())
    assert "harness provenance" in str(ei.value)


# --------------------------------------------------------------------------- #
# Deterministic provenance-scoped teardown (B1) — never a gameId-wide kill.
# --------------------------------------------------------------------------- #

def test_kill_terminates_only_the_recorded_pid_and_verifies_gone() -> None:
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    request = booter.boot(_spec())
    launched_pid = next(iter(rec.procs))  # the one client we launched
    rec.terminated.clear()
    booter.kill(request)
    # Exactly the recorded PID was terminated, and it is now gone from /proc.
    assert [i.pid for i in rec.terminated] == [launched_pid]
    assert launched_pid not in rec.procs


def test_kill_refuses_foreign_valheim_at_different_binary_path() -> None:
    # THE EXACT GAP: Daniel's real Steam Valheim runs from a DIFFERENT binary path but
    # the SAME gameId "valheim". A gameId-wide games_kill would terminate it. Model his
    # game as a foreign valheim.x86_64 in /proc that carries NO harness marker. Teardown
    # of OUR client must leave his process untouched.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    request = booter.boot(_spec())
    # Daniel's live game: a valheim.x86_64 with no harness marker, different PID.
    daniel_pid = 9999
    rec.procs[daniel_pid] = {"marker": None, "start": 42, "exe": "valheim.x86_64"}
    booter.kill(request)
    # Our client is gone; Daniel's game is STILL running (never a target).
    assert daniel_pid in rec.procs
    assert all(i.pid != daniel_pid for i in rec.terminated)


def test_kill_fails_closed_when_provenance_missing() -> None:
    # A ClientLaunchRequest the booter has NO recorded provenance for (e.g. fabricated,
    # or from another booter) must NOT be killed — the harness cannot prove it launched
    # the underlying process. Fail closed (raise), do not kill.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    foreign_request = GabsClientBooter.build_request(_spec())  # never booted through THIS booter
    with pytest.raises(ClientLaunchError) as ei:
        booter.kill(foreign_request)
    assert "no recorded harness provenance" in str(ei.value)
    assert rec.terminated == []


def test_kill_fails_closed_on_toctou_foreign_process_swap() -> None:
    # TOCTOU: between the ownership check and the kill, the recorded PID is recycled and
    # now belongs to a DIFFERENT (foreign) process. The pre-kill re-check must catch the
    # marker/start-time mismatch and refuse to terminate it.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    request = booter.boot(_spec())
    launched_pid = next(iter(rec.procs))

    def _swap(r):
        # Replace the process at the recorded PID with a foreign one (different marker
        # and start-time) — exactly the PID-reuse / foreign-client race.
        r.procs[launched_pid] = {"marker": "SOMEONE-ELSE", "start": 777, "exe": "valheim.x86_64"}

    rec.on_before_terminate = _swap
    with pytest.raises(ClientLaunchError) as ei:
        booter.kill(request)
    assert "TOCTOU" in str(ei.value) or "no longer" in str(ei.value)
    # The foreign process was NOT terminated.
    assert rec.terminated == []
    assert launched_pid in rec.procs


def test_kill_verifies_process_gone_and_fails_closed_if_lingering() -> None:
    # If terminate does not actually remove the process (it lingers with the SAME
    # provenance), teardown is unverified and must fail closed.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    request = booter.boot(_spec())
    launched_pid = next(iter(rec.procs))
    # Make terminate a no-op so the process lingers after the kill attempt.
    booter._terminate = lambda instance: rec.terminated.append(instance)  # type: ignore[attr-defined]
    with pytest.raises(ClientLaunchError) as ei:
        booter.kill(request)
    assert "teardown unverified" in str(ei.value)


def test_kill_refuses_foreign_handle() -> None:
    # A handle this booter did not produce (e.g. a raw Popen) must be ignored, never
    # touched — the launcher only tears down what it launched.
    rec = _Recorder(ready_after_calls=1)
    booter = rec.booter()
    booter.kill(object())  # not a ClientLaunchRequest
    assert rec.terminated == []


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


# --------------------------------------------------------------------------- #
# B2 — the client `+connect` target is routed through the production deny list.
# A descriptor typo naming a production server port as the join target must be
# REJECTED before any launch, through the SAME hard deny as the lane launcher.
# --------------------------------------------------------------------------- #

@pytest.mark.parametrize("prod_port", [2456, 2466])
def test_connect_target_helper_rejects_production_ports(prod_port) -> None:
    with pytest.raises(OperatorSafetyError) as ei:
        assert_connect_target_not_production(prod_port)
    msg = str(ei.value)
    assert str(prod_port) in msg
    assert "+connect" in msg


@pytest.mark.parametrize("prod_port,label", [(2456, "Niflheim"), (2466, "Heistan")])
def test_build_request_rejects_connect_to_production_before_launch(prod_port, label) -> None:
    # A ClientSpec whose `+connect` join target is a PRODUCTION server port must be
    # refused at build_request time — before any GABS call, before any process spawns.
    with pytest.raises(OperatorSafetyError) as ei:
        GabsClientBooter.build_request(_spec(connect_port=prod_port))
    msg = str(ei.value)
    assert str(prod_port) in msg
    assert label in msg


@pytest.mark.parametrize("prod_port", [2456, 2466])
def test_boot_never_launches_when_connect_target_is_production(prod_port) -> None:
    # The deny fires inside build_request, which boot() calls first — so a production
    # connect target means NOTHING is ever started or terminated.
    rec = _Recorder(ready_after_calls=1)
    with pytest.raises(OperatorSafetyError):
        rec.booter().boot(_spec(connect_port=prod_port))
    assert rec.started == []
    assert rec.env_applied == []
    assert rec.terminated == []


def test_build_request_allows_disposable_lane_connect_port() -> None:
    # The genuine disposable lane port (2476) is fine — only 2456/2466 are denied.
    req = GabsClientBooter.build_request(_spec(connect_port=2476))
    assert req.connect_target.endswith(":2476")


def test_build_live_run_rejects_production_connect_target_descriptor() -> None:
    # End-to-end: a descriptor naming 2456 as a client's connect_port must be rejected
    # when its spec is built into a launch request — a typo can never point a licensed
    # client at production Niflheim.
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
                "connect_host": "127.0.0.1", "connect_port": 2456,  # PRODUCTION Niflheim typo
                "loopback_port": 48610,
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
        },
    }

    plan, _env = build_live_run(descriptor)
    # The descriptor still parses (backward compatible), but the production connect
    # target is caught the moment the offending client spec is resolved into a request.
    with pytest.raises(OperatorSafetyError) as ei:
        GabsClientBooter.build_request(plan.clients[0])
    assert "2456" in str(ei.value)
