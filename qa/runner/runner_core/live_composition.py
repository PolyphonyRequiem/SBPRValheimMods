"""Live-run composition entrypoint (ADR-0009 §5, §6, §9) — M6-COMPOSE.

This is the layer the three prior M6 attempts blocked on: the M6-EXEC slice merged
the *pieces* (the live `Transport`, the four operator-driver DI classes, the
fail-closed preflight) but **nothing wired them together into a run**. `--live`
verified the capability and returned. This module supplies the missing top-level
driving loop so `--live`, once its fail-closed preflight UNLOCKS, actually
**executes** a qualification run instead of printing and stopping.

What it does, on an UNLOCK, in order (and tears every one of them down on EVERY
exit path — success, failure, timeout, exception, abort):

  1. `AdminlistGuard.arm()`   — SHA-256 capture the server adminlist before touch.
  2. `LaneLauncher.start()`   — bring up the isolated disposable lane (hard deny of
     the production Niflheim 2456 / Heistan 2466 ports is exercised here).
  3. `DualClientLauncher.launch()` — spawn the two `valheim.x86_64` clients under
     the two licensed Steam identities; refuses any client it did not launch.
  4. `EntitlementSeeder.seed()` — deliver the product `sbpr_master` OFFER→BUY admin
     path (`CmdOffer=1`/`CmdBuy=2`). NEVER mints/signs/grants (threats T3/T5).
  5. Build the concrete `LiveLoopbackTransport`, hand it to the SOLE-authority
     `T022RunOrchestrator` (lease → pins → FSM → evidence → verdict), and drive the
     four T022 legs (ISSUE/UPGRADE/TRANSFER/TAMPER) over the wire.
  6. Compose and return the runner's `RunnerVerdict`.
  7. Teardown: clients stopped, lane stopped, transport closed, adminlist restored
     byte-identically, lease released. Nothing orphaned.

INJECTION SEAM (why this is testable with NO real game): every game-touching action
— process spawn, readiness probe, teardown, running-binary enumeration, admin RPC
delivery, adminlist file I/O, and the transport construction — is injected behind a
small callable on `LiveOperatorEnvironment`. `real_operator_environment()` wires the
REAL `subprocess`/`socket`/`os`/file callables (this is the concrete layer that
genuinely spawns `valheim.x86_64` and delivers `sbpr_master`); the test suite wires
stub callables and asserts the composition actually DROVE (lane launched, both
clients launched, entitlement seeded via OFFER→BUY, all four legs driven, verdict
composed, teardown executed). Importing or unit-testing this module launches
nothing.

MATURITY (M6-COMPOSE, capability NOT performed): this makes a live in-world run
**executable**, not **executed**. Nothing runs in-world here; the four T022 ATs
remain UNOBSERVED. Actually driving a real two-client cold run is the separate
operator-authorized M6 card.

Engine-free stdlib only (subprocess/socket/os live in THIS driver layer, never in
the FSM core). No Valheim/BepInEx/Unity import.
"""
from __future__ import annotations

import os
import subprocess
from dataclasses import dataclass, field
from typing import Any, Callable, List, Mapping, Optional, Sequence

from .live_transport import (
    ChannelEndpoint,
    EntitlementControlChannel,
    EntitlementDeliveryConfig,
    LiveLoopbackTransport,
    LiveRunConfig,
)
from .manifest import ArtifactPinManifest
from .lease import LaneLease
from .launch_env import SidecarWriter
from .bootstrap_provision import BootstrapProvisioner
from .operator_drivers import (
    AdminlistGuard,
    BootRetryPolicy,
    ClientLaunchRequest,
    ClientSpec,
    DualClientLauncher,
    EntitlementSeeder,
    GabsClientBooter,
    HARNESS_INSTANCE_ENV_VAR,
    HarnessInstance,
    LaneLauncher,
    LaneSpec,
    LICENSED_STEAM_IDENTITIES,
    SeedResult,
)
from .orchestrator import RunnerVerdict, T022RunOrchestrator
from .timeouts import PhaseBudget


# --------------------------------------------------------------------------- #
# Operator environment — the injectable game-touching callables.
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class LiveOperatorEnvironment:
    """The concrete game-touching callables the composition drives through.

    A REAL operator run wires `real_operator_environment()` (subprocess/socket/file);
    the test suite wires stubs. The composition itself never imports a game and never
    hard-codes a spawn — it only calls these seams, so the exact same driving logic is
    exercised with or without a live game.
    """

    spawn_lane: Callable[[LaneSpec], object]
    lane_ready: Callable[[object], bool]
    stop_lane: Callable[[object], None]
    spawn_client: Callable[[ClientSpec], object]
    stop_client: Callable[[object], None]
    running_binaries: Callable[[], Sequence[str]]
    deliver_entitlement: Callable[[int], str]
    read_adminlist: Callable[[], bytes]
    write_adminlist: Callable[[bytes], None]
    build_transport: Callable[[LiveRunConfig], Any]
    # M6-LAUNCHENV provisioning seam: emit the per-client arm-bootstrap docs (derived
    # from the descriptor) BEFORE any client launches, and remove the secret-bearing
    # docs on teardown. Defaults are no-ops so a stub env (unit tests) and legacy callers
    # keep working; `real_operator_environment` wires the concrete descriptor-derived
    # provisioner. Keeping this a seam means the composition drives the same provision→
    # launch→teardown order with or without a real game.
    provision_bootstraps: Callable[[], None] = lambda: None
    cleanup_bootstraps: Callable[[], None] = lambda: None
    max_ready_polls: int = 120


@dataclass(frozen=True)
class LiveQualificationPlan:
    """Everything the composition needs to drive ONE live attempt.

    The runner mints the wire parameters (`run_config`) and the operational envelope
    (`lease`, `pins`, budgets, expected generations, actor identities). `run_config`
    carries the same `nonce`/`integrity_key` the orchestrator uses so the in-process
    receipt-correlation tag holds end-to-end.
    """

    lane: LaneSpec
    clients: Sequence[ClientSpec]
    run_config: LiveRunConfig
    lease: LaneLease
    pins: ArtifactPinManifest
    world_uid: str
    world_name: str
    run_nonce: str
    expiry: int
    phase_budget: PhaseBudget
    expected_conn_gen: Mapping[str, int]
    actor_identity: Mapping[str, str]
    integrity_key: bytes
    observed_pin_hashes: Optional[Mapping[str, str]] = None
    expected_receipts: int = 4

    def __post_init__(self) -> None:
        # Fail closed on a malformed plan BEFORE anything spawns. The dual-client
        # invariants (exactly two, exactly the licensed identities) are re-checked
        # by DualClientLauncher, but catching it here keeps us from ever arming the
        # adminlist / launching a lane for a plan that can never launch clients.
        DualClientLauncher.assert_licensed_pair(self.clients)
        LaneLauncher.assert_disposable(self.lane)


@dataclass
class LiveRunReport:
    """Observable record of what the composition actually DROVE.

    This exists so a test can assert on *driving behaviour* (not just a preflight
    message): whether the lane launched, which clients launched, the OFFER→BUY seed
    results, the composed verdict, and that teardown ran. It is descriptive — the
    verdict itself is composed solely by the orchestrator.
    """

    lane_started: bool = False
    clients_launched: List[str] = field(default_factory=list)
    seed_results: List[SeedResult] = field(default_factory=list)
    legs_driven: int = 0
    verdict: Optional[RunnerVerdict] = None
    teardown_completed: bool = False
    teardown_errors: List[str] = field(default_factory=list)
    drive_error: Optional[str] = None

    @property
    def passed(self) -> bool:
        return self.verdict is not None and self.verdict.passed


# --------------------------------------------------------------------------- #
# The composition entrypoint — the piece that did not exist.
# --------------------------------------------------------------------------- #

def run_live_qualification(
    plan: LiveQualificationPlan,
    env: LiveOperatorEnvironment,
) -> LiveRunReport:
    """Instantiate the live transport, construct + wire the four drivers, drive the
    FSM through the orchestrator, and return the runner's verdict.

    Teardown of EVERY started resource runs on EVERY exit path (success, failure,
    timeout, exception, abort): clients, lane, transport, adminlist restore, lease.
    Nothing is left orphaned. Failures during driving still fail closed — the
    orchestrator remains the sole verdict authority and cannot mint a PASS unless
    `fsm_pass ∧ lease_held ∧ pins_verified ∧ receipts_ok` all hold.
    """
    report = LiveRunReport()

    adminlist = AdminlistGuard(env.read_adminlist, env.write_adminlist)
    lane = LaneLauncher(
        env.spawn_lane, env.lane_ready, env.stop_lane, max_ready_polls=env.max_ready_polls
    )
    clients = DualClientLauncher(env.spawn_client, env.stop_client, env.running_binaries)
    seeder = EntitlementSeeder(env.deliver_entitlement)

    adminlist_armed = False
    transport: Any = None

    try:
        # 1. adminlist safety capture BEFORE any change.
        adminlist.arm()
        adminlist_armed = True

        # 2. bring up the isolated disposable lane (production-port deny exercised).
        lane.start(plan.lane)
        report.lane_started = True

        # 2b. provision the per-client arm-bootstrap docs (M6-LAUNCHENV) BEFORE launch.
        #     Emitting them from the descriptor here — rather than relying on hand-authored
        #     files — is what stops a stale doc (wrong helper hash / expired nonce) from
        #     silently blocking arming. The secret-bearing docs are removed in teardown.
        env.provision_bootstraps()

        # 3. launch exactly the two licensed clients.
        launched = clients.launch(plan.clients)
        report.clients_launched = [p.name for p in launched]

        # 4. seed entitlement via the product's OWN OFFER→BUY admin path.
        report.seed_results = seeder.seed()

        # 5. build the live transport and drive the four legs through the SOLE
        #    verdict authority.
        transport = env.build_transport(plan.run_config)
        orchestrator = T022RunOrchestrator(
            transport=transport,
            lease=plan.lease,
            pins=plan.pins,
            world_uid=plan.world_uid,
            world_name=plan.world_name,
            run_nonce=plan.run_nonce,
            expiry=plan.expiry,
            phase_budget=plan.phase_budget,
            expected_conn_gen=plan.expected_conn_gen,
            actor_identity=plan.actor_identity,
            expected_receipts=plan.expected_receipts,
            integrity_key=plan.integrity_key,
            observed_pin_hashes=plan.observed_pin_hashes,
        )
        # 6. compose the verdict.
        verdict = orchestrator.run()
        report.verdict = verdict
        report.legs_driven = verdict.evidence.receipts_correlated
    except Exception as exc:  # noqa: BLE001 — a driving/launch failure never masks into PASS
        # Record the failure (verdict stays None → report.passed is False → no false
        # PASS) and fall through to teardown. We do NOT re-raise: the caller always
        # gets a report with the teardown outcome, never a traceback that could strand
        # a launched client / lane / adminlist change.
        report.drive_error = f"{type(exc).__name__}: {exc}"
    finally:
        # 7. teardown on EVERY exit path. Each step is guarded so one failure never
        #    strands the rest; the lease is released by the orchestrator's own
        #    finally, and again here defensively if we never reached it.
        _safe(report, "clients.teardown", clients.teardown)
        _safe(report, "lane.stop", lane.stop)
        # Remove the secret-bearing bootstrap docs (M6-LAUNCHENV) on every exit path, so
        # a run never leaves an HMAC secret / operator token on disk between runs.
        _safe(report, "cleanup_bootstraps", env.cleanup_bootstraps)
        if transport is not None:
            _safe(report, "transport.cleanup", transport.cleanup)
        if adminlist_armed:
            _safe(report, "adminlist.restore", adminlist.restore)
        _safe(report, "lease.release", plan.lease.release)
        report.teardown_completed = not report.teardown_errors

    return report


def _safe(report: LiveRunReport, label: str, fn: Callable[[], Any]) -> None:
    """Run a teardown step, recording (never swallowing into a PASS) any failure."""
    try:
        fn()
    except Exception as exc:  # noqa: BLE001 — teardown must attempt every step
        report.teardown_errors.append(f"{label}: {exc}")


# --------------------------------------------------------------------------- #
# The REAL operator environment — concrete subprocess/socket/file callables.
# NEVER invoked by import or the test suite; wired only by an authorized operator
# run that actually launches a game. This is the concrete layer the M6-EXEC DI
# classes were waiting for.
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class RealOperatorConfig:
    """Operator-supplied concrete paths/args for a genuine live run.

    Everything here names a real on-disk binary / file / launch identity. A real run
    is the ONLY thing that constructs this; the harness never fabricates it.
    """

    server_binary: str          # dedicated-server binary for the disposable lane
    server_args: Sequence[str]  # lane launch args (world, port, saveinterval, …)
    server_ready_log: str       # path to the server log to poll for the ready marker
    server_ready_marker: str    # e.g. "Game server connected" / "DungeonDB Start"
    client_binary: str          # absolute path to valheim.x86_64
    adminlist_path: str         # server adminlist.txt to guard byte-identically
    # The control channel over which the product `sbpr_master` OFFER→BUY admin command is
    # relayed. Shares the run's owner-local wire (operator token / HMAC secret / nonce /
    # world / expiry) by construction — the seeder rides this, never a new socket. A real
    # run MUST supply it; without it there is no authorized delivery path and the run
    # fails closed (no minting, ever).
    entitlement_delivery: EntitlementDeliveryConfig
    # Boot-retry envelope for the intermittent ValBridge startup-scene wedge. A
    # single-shot launch is unreliable on this box (boot-qa-client.sh re-rolls up to
    # 6× polling readiness every 10s for up to 150s); the booter honours this policy.
    boot_policy: BootRetryPolicy = field(default_factory=BootRetryPolicy)
    # Timeout (seconds) for a single GABS/MCP HTTP request and for the loopback
    # control-port readiness probe. Small — these are localhost round-trips.
    gabs_request_timeout_s: float = 10.0
    control_probe_timeout_s: float = 3.0


def _proc_running_valheim_binaries() -> List[str]:
    """Enumerate absolute paths of currently-running `valheim.x86_64` processes.

    Real /proc scan so `DualClientLauncher` can refuse to touch a user-owned client
    it did not itself launch (fail closed on a foreign binary).
    """
    found: List[str] = []
    proc_root = "/proc"
    try:
        pids = [d for d in os.listdir(proc_root) if d.isdigit()]
    except OSError:
        return found
    for pid in pids:
        exe_link = os.path.join(proc_root, pid, "exe")
        try:
            target = os.readlink(exe_link)
        except OSError:
            continue
        if os.path.basename(target) == "valheim.x86_64":
            found.append(os.path.abspath(target))
    return found


def real_operator_environment(
    config: RealOperatorConfig,
    descriptor: Optional[Mapping[str, Any]] = None,
) -> LiveOperatorEnvironment:
    """Wire the REAL game-touching callables for an authorized operator run.

    Concrete: `subprocess.Popen` for the lane and each `valheim.x86_64` client, an
    explicit log-marker readiness probe (NOT a blind sleep), a /proc enumeration of
    running clients, the `sbpr_master` OFFER→BUY admin delivery over the live control
    channel, and real adminlist file I/O. Constructing this env does NOT start
    anything — only `run_live_qualification` does, and only under an explicit
    operator authorization. This card never invokes it.

    `descriptor` (when supplied by `build_live_run`) is the source the bootstrap-doc
    provisioner derives each client's arm doc from, so the docs cannot drift from the
    wire block. Absent it, provisioning is a no-op (the run then depends on pre-placed
    docs, the legacy behaviour) — but the default live path always supplies it.
    """

    def spawn_lane(spec: LaneSpec) -> subprocess.Popen:
        return subprocess.Popen(
            [config.server_binary, *config.server_args],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

    def lane_ready(handle: object) -> bool:
        # Explicit readiness: the lane is up only when the server log has emitted its
        # ready marker. No blind sleep — LaneLauncher polls this up to its budget.
        try:
            with open(config.server_ready_log, "r", encoding="utf-8", errors="replace") as fh:
                return config.server_ready_marker in fh.read()
        except OSError:
            return False

    def stop_lane(handle: object) -> None:
        _terminate(handle)

    # --- GABS-mediated modded client boot (the attempt-7 fix) --------------------- #
    # A bare `subprocess.Popen([binary_path])` produced a client that never injected
    # BepInEx, never received SBPR_QA_T022_BOOTSTRAP, never `+connect`ed the lane, and
    # therefore never armed or bound its loopback control port. We instead drive the
    # client through its GABS/MCP endpoint (games_start) exactly as boot-qa-client.sh
    # does on this box, publish the bootstrap + identity + harness-provenance env, and
    # poll the helper's loopback control port for armed readiness — re-rolling on the
    # intermittent ValBridge startup wedge. T6: this touches GABS/MCP for boot only; it
    # NEVER acquires the ValBridge/ScriptTools lock and the four AT legs still ride the
    # LiveLoopbackTransport, not USH.
    #
    # TEARDOWN SAFETY (B1): we do NOT `games_kill` the gameId — that would terminate
    # Daniel's OWN Steam Valheim (same gameId "valheim", different binary path). Instead
    # the harness injects a unique per-boot marker (SBPR_QA_HARNESS_INSTANCE) into the
    # launched process env, then identifies the exact PID carrying that marker via /proc,
    # pinning it to the process start-time to defeat PID reuse. Teardown terminates ONLY
    # that recorded PID, re-verifying the marker+start-time immediately before the kill.
    import os as _os
    import signal as _signal
    import socket as _socket
    import time as _time
    import urllib.request as _urlreq
    import json as _json

    # The launch-env sidecar writer (M6-LAUNCHENV). One per run; each client's sidecar is
    # written at the path the descriptor pins for it and removed on teardown so the
    # non-secret arming file never lingers between runs.
    sidecar_writer = SidecarWriter()

    # The bootstrap-doc provisioner (M6-LAUNCHENV). Derives each client's mode-0600 arm
    # doc from the descriptor's wire/pins/lane at run time (before launch) and removes the
    # secret-bearing docs on teardown. When no descriptor is supplied both are no-ops.
    bootstrap_provisioner = BootstrapProvisioner()

    def provision_bootstraps() -> None:
        if descriptor is not None:
            bootstrap_provisioner.provision_from_descriptor(descriptor)

    def cleanup_bootstraps() -> None:
        bootstrap_provisioner.remove_all()

    def _mcp_call(request: ClientLaunchRequest, tool: str) -> None:
        payload = _json.dumps(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "tools/call",
                "params": {"name": tool, "arguments": {"gameId": request.game_id}},
            }
        ).encode()
        req = _urlreq.Request(
            request.gabs_endpoint,
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with _urlreq.urlopen(req, timeout=config.gabs_request_timeout_s):
            pass

    def _gabs_start(request: ClientLaunchRequest) -> None:
        _mcp_call(request, "games_start")

    def _apply_env(request: ClientLaunchRequest) -> None:
        # THE FORK-BOUNDARY FIX (M6-LAUNCHENV). The GABS-launched client is forked by a
        # long-lived GABS daemon over HTTP; it inherits the DAEMON's environment, never
        # this runner process's. So publishing the arming vars into `os.environ` (the old
        # code) delivered them nowhere — proven by t_2a954860, where the launched child's
        # /proc/environ carried only GABP_* and none of the three SBPR vars.
        #
        # Instead, write the vars to the launch-env SIDECAR the client's wrapper reads.
        # The wrapper (`run-trailborne.sh` / valbot controller chain) sources this file
        # just before `exec`ing valheim.x86_64, so the vars land in the CHILD's env across
        # the daemon fork. The sidecar carries only the three NON-SECRET arming vars
        # (a bootstrap-doc PATH, a public SteamID, a random provenance marker); the HMAC
        # secret and operator token live only inside the mode-0600 bootstrap doc, never
        # here. `SidecarWriter.write` fails closed on any non-allowlisted/secret-shaped
        # key. The exact path is the one the descriptor pins for this client (the path its
        # own wrapper resolves from `$HOME`+`$GABS_GAME_ID`), so the two lanes — which
        # launch as different users — each read the sidecar written for them.
        sidecar_writer.write(request.launch_env_path, request.launch_env)

    def _pid_start_ticks(pid: int) -> Optional[int]:
        # Field 22 of /proc/<pid>/stat is the process start time in clock ticks since
        # boot. Reading it and pinning it to the PID defeats PID reuse: a recycled PID
        # held by a DIFFERENT (possibly Daniel-owned) process has a different start-time.
        try:
            with open(f"/proc/{pid}/stat", "r", encoding="utf-8", errors="replace") as fh:
                data = fh.read()
        except OSError:
            return None
        # The comm field (in parens) can contain spaces/parens; split after the last ')'.
        rparen = data.rfind(")")
        if rparen < 0:
            return None
        fields = data[rparen + 2:].split()
        # After comm, field indices: state=0 ... starttime is stat field 22 => index 19.
        if len(fields) <= 19:
            return None
        try:
            return int(fields[19])
        except ValueError:
            return None

    def _pid_marker(pid: int) -> Optional[str]:
        # Read the launched process's own environment to recover the unique harness
        # marker it carries. Absent/unreadable => no proof of harness ownership.
        try:
            with open(f"/proc/{pid}/environ", "rb") as fh:
                raw = fh.read()
        except OSError:
            return None
        for entry in raw.split(b"\x00"):
            if entry.startswith(HARNESS_INSTANCE_ENV_VAR.encode() + b"="):
                return entry.split(b"=", 1)[1].decode("utf-8", errors="replace")
        return None

    def _probe_pid(pid: int) -> Optional[HarnessInstance]:
        # Return the harness provenance of the live process at PID, or None if the PID
        # is gone or carries no harness marker. Used both to resolve the launched client
        # and, immediately before a kill, to re-verify we are terminating OUR process.
        marker = _pid_marker(pid)
        if marker is None:
            return None
        start = _pid_start_ticks(pid)
        if start is None:
            return None
        return HarnessInstance(actor="", marker=marker, pid=pid, start_ticks=start)

    def _resolve_launched(request: ClientLaunchRequest) -> Optional[HarnessInstance]:
        # Find the UNIQUE running valheim.x86_64 whose /proc environ carries THIS boot's
        # marker. Poll briefly — the GABS-launched process takes a moment to appear.
        target_marker = request.launch_env[HARNESS_INSTANCE_ENV_VAR]
        proc_root = "/proc"
        deadline = _time.monotonic() + config.control_probe_timeout_s
        while True:
            matches: List[HarnessInstance] = []
            try:
                pids = [d for d in _os.listdir(proc_root) if d.isdigit()]
            except OSError:
                pids = []
            for pid_s in pids:
                pid = int(pid_s)
                # Only consider actual valheim.x86_64 processes.
                try:
                    exe = _os.readlink(_os.path.join(proc_root, pid_s, "exe"))
                except OSError:
                    continue
                if _os.path.basename(exe) != "valheim.x86_64":
                    continue
                if _pid_marker(pid) != target_marker:
                    continue
                start = _pid_start_ticks(pid)
                if start is None:
                    continue
                matches.append(
                    HarnessInstance(actor=request.actor, marker=target_marker, pid=pid, start_ticks=start)
                )
            if len(matches) == 1:
                return matches[0]
            if len(matches) > 1:
                # Ambiguous provenance — refuse rather than guess which is ours.
                return None
            if _time.monotonic() >= deadline:
                return None
            _time.sleep(0.1)

    def _terminate_instance(instance: HarnessInstance) -> None:
        # Terminate ONLY the exact recorded PID (SIGTERM → wait → SIGKILL). The booter
        # has already re-verified marker+start-time immediately before calling this.
        pid = instance.pid
        try:
            _os.kill(pid, _signal.SIGTERM)
        except OSError:
            return
        for _ in range(30):  # up to ~15s
            if _probe_pid(pid) is None:
                return
            _time.sleep(0.5)
        try:
            _os.kill(pid, _signal.SIGKILL)
        except OSError:
            return

    def _control_ready(request: ClientLaunchRequest) -> bool:
        # Armed-readiness signal: the helper binds its loopback control port only AFTER
        # the full arming AND-gate passes (Plugin.cs:57-62, ControlPlaneComponent). A
        # successful loopback TCP connect is the explicit proof it armed. NOT a sleep.
        try:
            with _socket.create_connection(
                ("127.0.0.1", request.loopback_port),
                timeout=config.control_probe_timeout_s,
            ):
                return True
        except OSError:
            return False

    booter = GabsClientBooter(
        apply_env=_apply_env,
        gabs_start=_gabs_start,
        control_ready=_control_ready,
        resolve_launched=_resolve_launched,
        probe_pid=_probe_pid,
        terminate=_terminate_instance,
        sleep=_time.sleep,
        policy=config.boot_policy,
    )

    def spawn_client(spec: ClientSpec) -> ClientLaunchRequest:
        # Boot to armed readiness through GABS, re-rolling on the ValBridge wedge, and
        # return the live request handle. Raises ClientLaunchError (naming the stage)
        # rather than returning a dead handle if it never arms.
        return booter.boot(spec)

    def stop_client(handle: object) -> None:
        # Provenance-scoped teardown: terminates ONLY the exact PID the harness recorded
        # launching, after re-verifying its marker+start-time (TOCTOU). Fails closed on
        # missing/ambiguous provenance — never a gameId-wide kill, so Daniel's own Steam
        # Valheim can never be collateral.
        try:
            booter.kill(handle)
        finally:
            # Remove this client's launch-env sidecar regardless of kill outcome so the
            # non-secret arming file never lingers between runs. Idempotent + best-effort.
            if isinstance(handle, ClientLaunchRequest):
                sidecar_writer.remove(handle.launch_env_path)

    # The delivering entitlement seam: relay the product OFFER→BUY admin command over
    # the SAME owner-local control transport the four legs ride. Built ONCE here, bound
    # into the environment below. It holds no signing key and mints nothing — it only
    # asks the product to run its own `sbpr_master` path (threats T3/T5).
    entitlement_channel = EntitlementControlChannel(config.entitlement_delivery)

    def deliver_entitlement(discriminator: int) -> str:
        # Invoke the product's OWN authenticated admin path over the control channel. We
        # do NOT construct or sign entitlement — we ask the product to run `sbpr_master`
        # with the fixed discriminator and report back the operator line it emits.
        return entitlement_channel.deliver(discriminator)

    def read_adminlist() -> bytes:
        with open(config.adminlist_path, "rb") as fh:
            return fh.read()

    def write_adminlist(data: bytes) -> None:
        with open(config.adminlist_path, "wb") as fh:
            fh.write(data)

    return LiveOperatorEnvironment(
        spawn_lane=spawn_lane,
        lane_ready=lane_ready,
        stop_lane=stop_lane,
        spawn_client=spawn_client,
        stop_client=stop_client,
        running_binaries=_proc_running_valheim_binaries,
        deliver_entitlement=deliver_entitlement,
        read_adminlist=read_adminlist,
        write_adminlist=write_adminlist,
        build_transport=lambda cfg: LiveLoopbackTransport(cfg),
        provision_bootstraps=provision_bootstraps,
        cleanup_bootstraps=cleanup_bootstraps,
    )


def _terminate(handle: object) -> None:
    """Deterministically stop a spawned process handle (terminate → wait → kill)."""
    proc = handle
    if not isinstance(proc, subprocess.Popen):
        return
    if proc.poll() is not None:
        return
    proc.terminate()
    try:
        proc.wait(timeout=15)
    except subprocess.TimeoutExpired:
        proc.kill()
        proc.wait(timeout=5)


def build_live_run(
    descriptor: Mapping[str, Any],
) -> "tuple[LiveQualificationPlan, LiveOperatorEnvironment]":
    """Build the `(plan, real_operator_environment)` pair from an operator descriptor.

    This is what the `--live` CLI path composes on an UNLOCK: it turns the operator's
    concrete run descriptor (lane/clients/wire/pins/server-binaries) into a fully-wired
    plan plus the REAL subprocess/socket/file operator environment, then hands both to
    `run_live_qualification`. The integrity key is shared by construction between the
    wire transport and the orchestrator so the in-process receipt-correlation tag holds
    end-to-end. Building this does NOT launch anything — only running the returned pair
    through `run_live_qualification` does.
    """
    from .live_transport import ChannelEndpoint

    integrity_key = str(descriptor.get("integrity_key", "sbpr-live-integrity")).encode()

    lane_d = descriptor["lane"]
    lane = LaneSpec(
        lane_id=str(lane_d["lane_id"]),
        world_name=str(lane_d["world_name"]),
        world_uid=int(lane_d["world_uid"]),
        port=int(lane_d["port"]),
    )
    clients = tuple(
        ClientSpec(
            actor=str(c["actor"]),
            steam_id=str(c["steam_id"]),
            binary_path=str(c["binary_path"]),
            # Additive GABS-launch fields. Present in a real live descriptor so the
            # client is booted modded+armed+joined; a bare descriptor (older shape)
            # still parses, and the GABS booter fails closed at build_request time on
            # any missing field rather than silently launching a bare binary.
            gabs_endpoint=(str(c["gabs_endpoint"]) if c.get("gabs_endpoint") is not None else None),
            game_id=str(c.get("game_id", "valheim")),
            bootstrap_path=(str(c["bootstrap_path"]) if c.get("bootstrap_path") is not None else None),
            connect_host=(str(c["connect_host"]) if c.get("connect_host") is not None else None),
            connect_port=(int(c["connect_port"]) if c.get("connect_port") is not None else None),
            loopback_port=(int(c["loopback_port"]) if c.get("loopback_port") is not None else None),
            # Launch-env sidecar path (M6-LAUNCHENV). The descriptor names the exact path
            # this client's wrapper reads (its own launching user's
            # $HOME/.local/share/sbpr-qa/launch-env/<game_id>.env, or the primary-owned
            # cross-user path for the valbot lane). The runner writes the three non-secret
            # arming vars there and the wrapper sources them across the daemon fork.
            launch_env_path=(str(c["launch_env_path"]) if c.get("launch_env_path") is not None else None),
        )
        for c in descriptor["clients"]
    )

    wire = descriptor["wire"]
    endpoints = {
        actor: ChannelEndpoint(host=str(e["host"]), port=int(e["port"]), role=str(e.get("role", "Client")))
        for actor, e in wire["endpoints"].items()
    }
    run_config = LiveRunConfig(
        nonce=str(wire["nonce"]),
        world_uid=int(wire["world_uid"]),
        expiry_unix_ms=int(wire["expiry_unix_ms"]),
        operator_token=str(wire["operator_token"]),
        hmac_secret=str(wire["hmac_secret"]),
        endpoints=endpoints,
        integrity_key=integrity_key,
        start_generation=int(wire.get("start_generation", 1)),
    )

    lease_d = descriptor["lease"]
    lease = LaneLease(lane_id=str(lease_d["lane_id"]), our_id=str(lease_d["our_id"]))
    pins = ArtifactPinManifest(pins={k: str(v) for k, v in descriptor["pins"].items()})
    budget_d = descriptor.get("phase_budget", {"default": 30_000})
    phase_budget = PhaseBudget(
        default=int(budget_d["default"]),
        per_verb={k: int(v) for k, v in budget_d.get("per_verb", {}).items()},
    )

    plan = LiveQualificationPlan(
        lane=lane,
        clients=clients,
        run_config=run_config,
        lease=lease,
        pins=pins,
        world_uid=str(descriptor["world_uid"]),
        world_name=str(descriptor["world_name"]),
        run_nonce=str(wire["nonce"]),
        expiry=int(descriptor["expiry"]),
        phase_budget=phase_budget,
        expected_conn_gen={k: int(v) for k, v in descriptor["expected_conn_gen"].items()},
        actor_identity={k: str(v) for k, v in descriptor["actor_identity"].items()},
        integrity_key=integrity_key,
        observed_pin_hashes=descriptor.get("observed_pin_hashes"),
        expected_receipts=int(descriptor.get("expected_receipts", 4)),
    )

    srv = descriptor["server"]
    # Entitlement delivery rides the SAME owner-local wire (shared token / secret / nonce
    # / world / expiry from `wire`). The operator names the admin control endpoint the
    # product exposes for `sbpr_master` under `wire.entitlement`; it is REQUIRED — without
    # an authorized delivery endpoint there is no relay path and the run fails closed
    # (the harness never mints entitlement).
    ent_d = wire["entitlement"]
    ent_ep = ChannelEndpoint(
        host=str(ent_d["host"]),
        port=int(ent_d["port"]),
        role=str(ent_d.get("role", "Server")),
    )
    entitlement_delivery = EntitlementDeliveryConfig(
        endpoint=ent_ep,
        operator_token=str(wire["operator_token"]),
        hmac_secret=str(wire["hmac_secret"]),
        nonce=str(wire["nonce"]),
        world_uid=int(wire["world_uid"]),
        expiry_unix_ms=int(wire["expiry_unix_ms"]),
        connection_generation=int(ent_d.get("connection_generation", 1)),
    )
    # Boot-retry policy for the intermittent ValBridge startup wedge (additive; the
    # descriptor may override the safe defaults). Named per the launch card.
    boot_d = srv.get("boot_policy", {})
    boot_policy = BootRetryPolicy(
        max_attempts=int(boot_d.get("max_attempts", 6)),
        readiness_timeout_s=float(boot_d.get("readiness_timeout_s", 150.0)),
        poll_interval_s=float(boot_d.get("poll_interval_s", 10.0)),
    )
    env = real_operator_environment(
        RealOperatorConfig(
            server_binary=str(srv["server_binary"]),
            server_args=tuple(str(a) for a in srv.get("server_args", ())),
            server_ready_log=str(srv["server_ready_log"]),
            server_ready_marker=str(srv["server_ready_marker"]),
            client_binary=str(srv["client_binary"]),
            adminlist_path=str(srv["adminlist_path"]),
            entitlement_delivery=entitlement_delivery,
            boot_policy=boot_policy,
            gabs_request_timeout_s=float(srv.get("gabs_request_timeout_s", 10.0)),
            control_probe_timeout_s=float(srv.get("control_probe_timeout_s", 3.0)),
        ),
        descriptor=descriptor,
    )
    return plan, env
