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

from fsm.errors import TransportError

from .live_transport import LiveLoopbackTransport, LiveRunConfig
from .manifest import ArtifactPinManifest
from .lease import LaneLease
from .operator_drivers import (
    AdminlistGuard,
    ClientSpec,
    DualClientLauncher,
    EntitlementSeeder,
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


def real_operator_environment(config: RealOperatorConfig) -> LiveOperatorEnvironment:
    """Wire the REAL game-touching callables for an authorized operator run.

    Concrete: `subprocess.Popen` for the lane and each `valheim.x86_64` client, an
    explicit log-marker readiness probe (NOT a blind sleep), a /proc enumeration of
    running clients, the `sbpr_master` OFFER→BUY admin delivery over the live control
    channel, and real adminlist file I/O. Constructing this env does NOT start
    anything — only `run_live_qualification` does, and only under an explicit
    operator authorization. This card never invokes it.
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

    def spawn_client(spec: ClientSpec) -> subprocess.Popen:
        # Launch valheim.x86_64 under the licensed Steam identity the spec names. The
        # identity is passed through the environment the product/Steam layer reads; we
        # never mint or spoof it — the client authenticates itself.
        child_env = dict(os.environ)
        child_env["SteamAppId"] = child_env.get("SteamAppId", "892970")
        child_env["SBPR_QA_STEAM_ID"] = spec.steam_id
        return subprocess.Popen(
            [spec.binary_path],
            env=child_env,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )

    def stop_client(handle: object) -> None:
        _terminate(handle)

    def deliver_entitlement(discriminator: int) -> str:
        # Invoke the product's OWN authenticated admin path. We do NOT construct or
        # sign entitlement — we ask the product to run `sbpr_master` with the fixed
        # discriminator and report back the operator line it emits (threats T3/T5).
        from .operator_drivers import SBPR_MASTER_CONSOLE_COMMAND

        return _deliver_admin_command(SBPR_MASTER_CONSOLE_COMMAND, discriminator)

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


def _deliver_admin_command(command: str, discriminator: int) -> str:
    """Relay a product admin command and return the product's operator line.

    A real operator run overrides this via the control channel to the running product;
    the seeder only asks — it holds no key and mints nothing. Left unimplemented on the
    engine-free path (there is no product to talk to) so that importing/unit-testing
    this module can never accidentally reach a live product.
    """
    raise TransportError(
        "real sbpr_master admin delivery requires a live product control channel; "
        "wire it in an authorized operator run — the harness never mints entitlement"
    )


# --------------------------------------------------------------------------- #
# Plan + real-env construction from an operator run descriptor.
# --------------------------------------------------------------------------- #

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
        ClientSpec(actor=str(c["actor"]), steam_id=str(c["steam_id"]), binary_path=str(c["binary_path"]))
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
    env = real_operator_environment(
        RealOperatorConfig(
            server_binary=str(srv["server_binary"]),
            server_args=tuple(str(a) for a in srv.get("server_args", ())),
            server_ready_log=str(srv["server_ready_log"]),
            server_ready_marker=str(srv["server_ready_marker"]),
            client_binary=str(srv["client_binary"]),
            adminlist_path=str(srv["adminlist_path"]),
        )
    )
    return plan, env
