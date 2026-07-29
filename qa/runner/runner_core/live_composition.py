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

import json
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
from .lane_password_provision import LanePasswordProvisioner
from .wire_mint import (
    assert_descriptor_carries_no_wire_secrets,
    mint_wire_envelope,
    resolve_ttl_seconds,
)
from .live_preflight import validate_lane_password_consistency
from .operator_drivers import (
    AdminlistGuard,
    BootRetryPolicy,
    ClientLaunchError,
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
    OperatorSafetyError,
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

    The runner mints the wire crypto envelope per run (`build_live_run` calls
    `mint_wire_envelope` once, upstream of both consumers) and assembles the
    operational envelope (`lease`, `pins`, budgets, expected generations, actor
    identities). `run_config` carries the same freshly minted
    `nonce`/`integrity_key` the orchestrator uses so the in-process
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
    # 6× polling readiness every 10s for up to 300s); the booter honours this policy.
    boot_policy: BootRetryPolicy = field(default_factory=BootRetryPolicy)
    # Timeout (seconds) for a single GABS/MCP HTTP request and for the loopback
    # control-port readiness probe. Small — these are localhost round-trips.
    gabs_request_timeout_s: float = 10.0
    control_probe_timeout_s: float = 3.0


# --- Cross-uid harness provenance (M6-PROVENANCE) --------------------------------
# Directory the valbot controller drops its {marker, pid} provenance receipts into.
# Primary-owned, mode 0733: valbot (uid 1001) can CREATE a receipt there but cannot
# enumerate or read the directory back; the runner (uid 1000, the owner) can. This is
# the same already-trusted cross-user seam the controller writes its launch log to.
PROVENANCE_RECEIPT_DIR = (
    "/home/polyphonyrequiem/valheim/mcp-harness/dual-client/runtime-diagnostics"
)


def _pid_start_ticks(pid: int) -> Optional[int]:
    """Field 22 of /proc/<pid>/stat: process start time in clock ticks since boot.

    Pinning this to the PID defeats PID reuse: a recycled PID held by a DIFFERENT
    (possibly Daniel-owned) process has a different start time. World-readable, so
    it works across the uid boundary where `environ` does not.
    """
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


def _pid_exe_basename(pid: int) -> Optional[str]:
    """Basename of /proc/<pid>/exe, or None if unreadable/gone.

    The symlink itself resolves across the uid boundary, unlike `environ`.
    """
    try:
        return os.path.basename(os.readlink(f"/proc/{pid}/exe"))
    except OSError:
        return None


def _receipt_to_instance(doc: object, pid: int, marker: str) -> Optional["HarnessInstance"]:
    """Turn a validated receipt into a HarnessInstance, or None (fail closed).

    Every safety-relevant fact is re-derived from the KERNEL here; the receipt is
    only ever a hint about WHICH pid to look at:
      * the binary at that pid must be valheim.x86_64,
      * start-ticks come from `_pid_start_ticks`, never from the receipt, so PID
        reuse stays defeated.
    """
    if _pid_exe_basename(pid) != "valheim.x86_64":
        return None
    start = _pid_start_ticks(pid)
    if start is None:
        return None
    return HarnessInstance(
        actor=marker.split(":", 1)[0], marker=marker, pid=pid, start_ticks=start
    )


def resolve_via_receipt(
    target_marker: str, receipt_dir: str = PROVENANCE_RECEIPT_DIR
) -> Optional["HarnessInstance"]:
    """Resolve THIS boot's client from the receipt its launching controller attested.

    Fails closed on a missing, malformed, foreign-binary, or STALE receipt — a
    receipt left by a previous run names a previous marker and is rejected here.
    """
    actor_part = target_marker.split(":", 1)[0]
    path = os.path.join(receipt_dir, f"harness-provenance-{actor_part}.json")
    try:
        with open(path, "r", encoding="utf-8") as fh:
            doc = json.load(fh)
    except (OSError, ValueError):
        return None
    if not isinstance(doc, dict) or doc.get("marker") != target_marker:
        return None
    try:
        pid = int(doc["pid"])
    except (KeyError, TypeError, ValueError):
        return None
    return _receipt_to_instance(doc, pid, target_marker)


def probe_pid_via_receipt(
    pid: int, receipt_dir: str = PROVENANCE_RECEIPT_DIR
) -> Optional["HarnessInstance"]:
    """PID-keyed counterpart of `resolve_via_receipt`.

    Used when `/proc/<pid>/environ` is unreadable (the cross-uid client_b case), both
    for the liveness re-probe and for the pre-kill ownership re-verification.
    """
    try:
        names = os.listdir(receipt_dir)
    except OSError:
        return None
    for name in names:
        if not (name.startswith("harness-provenance-") and name.endswith(".json")):
            continue
        try:
            with open(os.path.join(receipt_dir, name), "r", encoding="utf-8") as fh:
                doc = json.load(fh)
        except (OSError, ValueError):
            continue
        if not isinstance(doc, dict):
            continue
        try:
            if int(doc["pid"]) != pid:
                continue
        except (KeyError, TypeError, ValueError):
            continue
        marker = doc.get("marker")
        if not isinstance(marker, str) or not marker:
            continue
        return _receipt_to_instance(doc, pid, marker)
    return None


def remove_provenance_receipts(receipt_dir: str = PROVENANCE_RECEIPT_DIR) -> None:
    """Sweep receipts on teardown.

    They carry no secret (marker + pid only), but a receipt outliving its run is
    stale state. `resolve_via_receipt` already rejects a non-matching marker, so a
    survivor cannot cause a false positive; this keeps the directory honest.
    """
    try:
        names = os.listdir(receipt_dir)
    except OSError:
        return
    for name in names:
        if name.startswith("harness-provenance-") and name.endswith(".json"):
            try:
                os.unlink(os.path.join(receipt_dir, name))
            except OSError:
                pass


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

    # The lane-password provisioner (M6-JOIN3 / B2). Writes each password-gated client's
    # mode-0600 lane-password file from the descriptor's `lane_password` (before launch) and
    # removes the credential-bearing files on teardown. No-op for an open/no-password lane
    # (no client names a `server_password_file`). Same discipline as BootstrapProvisioner.
    lane_password_provisioner = LanePasswordProvisioner()

    def provision_bootstraps() -> None:
        if descriptor is not None:
            bootstrap_provisioner.provision_from_descriptor(descriptor)
            # Produce the lane-password file(s) BEFORE launch so the sidecar-advertised
            # SBPR_QA_SERVER_PASSWORD_FILE path exists when the QA hook reads it. This is the
            # missing producer for the consumer the branch already shipped.
            lane_password_provisioner.provision_from_descriptor(descriptor)

    def cleanup_bootstraps() -> None:
        # Remove BOTH secret-bearing artifact classes on every teardown exit path: the
        # bootstrap docs (HMAC secret + operator token) AND the lane-password files. A best-
        # effort double removal — one failing never strands the other.
        try:
            bootstrap_provisioner.remove_all()
        finally:
            try:
                lane_password_provisioner.remove_all()
            finally:
                remove_provenance_receipts()

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

    def _mcp_status_text(request: ClientLaunchRequest) -> str:
        # Read GABS's own liveness verdict for the gameId. Returns the raw operator text
        # ("... : running ..." / "... : stopped ..."). Used to VERIFY a reset actually
        # cleared the stale "running" belief rather than assuming games.stop worked.
        payload = _json.dumps(
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "tools/call",
                "params": {"name": "games_status", "arguments": {"gameId": request.game_id}},
            }
        ).encode()
        req = _urlreq.Request(
            request.gabs_endpoint,
            data=payload,
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        with _urlreq.urlopen(req, timeout=config.gabs_request_timeout_s) as resp:
            body = _json.loads(resp.read().decode())
        # tools/call result content is a list of {type,text}; concatenate the text.
        parts = body.get("result", {}).get("content", []) or []
        return " ".join(str(p.get("text", "")) for p in parts if isinstance(p, dict))

    def _count_live_nonzombie_valheim() -> int:
        # B1 SAFETY GATE for the reset path. A `<defunct>` zombie exposes NO readable
        # /proc/<pid>/exe (readlink fails) — a LIVE client's exe resolves to a real
        # valheim.x86_64 binary. So counting processes whose exe basename is
        # valheim.x86_64 counts ONLY live, non-zombie clients and structurally excludes
        # the zombies we intend to clear. If this is > 0, a real Valheim (possibly
        # Daniel's own) is up and we must NOT issue any stop — a stale-state clear is
        # only ever performed against a gameId whose only members are dead zombies.
        live = 0
        try:
            pids = [d for d in _os.listdir("/proc") if d.isdigit()]
        except OSError:
            return 0
        for pid_s in pids:
            try:
                exe = _os.readlink(_os.path.join("/proc", pid_s, "exe"))
            except OSError:
                # Zombies (and vanished/permission-denied pids) land here — NOT counted.
                continue
            if _os.path.basename(exe) == "valheim.x86_64":
                live += 1
        return live

    def _reset_gabs_state(request: ClientLaunchRequest) -> None:
        # M6-GABSLIVE: force GABS's single-gameId liveness view to agree with reality
        # before a launch, so a stale "running" belief (a `<defunct>` zombie GABS never
        # reaped) cannot silently swallow the launch. GABS counts a zombie as "running"
        # because its name-based `ps` finder still matches the zombie's `comm`
        # (controller.go:296-302); `games.stop`, being the zombie's PARENT, actually
        # Wait()s and reaps it — empirically verified to clear the state.
        #
        # B1 HARD SAFETY: only ever act when there are ZERO live non-zombie
        # valheim.x86_64 processes. `games.stop` here is scoped by GABS to the tracked
        # gameId, but out of defence-in-depth we refuse to issue ANY stop while a real
        # client is up — so a bug in GABS's scoping could never reach Daniel's own Steam
        # Valheim. If a live client exists we leave GABS untouched and let the normal
        # foreign-binary refusal / no-op detector handle it.
        live = _count_live_nonzombie_valheim()
        if live > 0:
            # A real client is running — do NOT clear anything (never risk Daniel's game).
            return
        # Prefer games.stop (graceful; it reaps the zombie via Wait). If GABS still
        # reports "running" afterwards, escalate to games.kill, then re-verify.
        try:
            _mcp_call(request, "games_stop")
        except OSError:
            pass  # not-running / no-tracked-process is a benign "already clear"
        if "running" in _mcp_status_text(request).lower():
            try:
                _mcp_call(request, "games_kill")
            except OSError:
                pass
        # Verify the clear actually took. If GABS STILL believes it is running with zero
        # live clients, the state is wedged in a way our stop cannot clear — fail loud so
        # the attempt does not proceed into a guaranteed no-op masquerading as a launch.
        final = _mcp_status_text(request).lower()
        if "running" in final and _count_live_nonzombie_valheim() == 0:
            raise ClientLaunchError(
                "GABS still reports the gameId 'running' after games.stop/kill with ZERO "
                f"live non-zombie valheim.x86_64 processes (status: {final!r}) — its stale "
                "liveness state could not be cleared from the runner side; refusing to "
                "launch into a guaranteed silent no-op"
            )

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

    def _pid_marker(pid: int) -> Optional[str]:
        # Read the launched process's own environment to recover the unique harness
        # marker it carries. Absent/unreadable => no proof of harness ownership.
        # NOTE: readable ONLY for a same-uid process (client_a). For client_b, which
        # runs as valbot, this always fails and the caller falls back to the attested
        # provenance receipt — see `resolve_via_receipt` at module scope.
        try:
            with open(f"/proc/{pid}/environ", "rb") as fh:
                raw = fh.read()
        except OSError:
            return None
        for entry in raw.split(b"\x00"):
            if entry.startswith(HARNESS_INSTANCE_ENV_VAR.encode() + b"="):
                return entry.split(b"=", 1)[1].decode("utf-8", errors="replace")
        return None

    # --- Cross-uid harness provenance (M6-PROVENANCE) --------------------------------
    # `_pid_marker` reads /proc/<pid>/environ, which the kernel exposes ONLY to the
    # process owner. client_a runs as the runner's own uid, so that works. client_b runs
    # as valbot (uid 1001) and the read is structurally impossible — the runner burned
    # six boot attempts on a permission error and refused to proceed without a
    # tear-down-able instance, so TRANSFER and TAMPER could never execute.
    #
    # The marker is what the B1 kill guard uses to prove a valheim.x86_64 process is
    # harness-owned rather than Daniel's own game, so it cannot simply be skipped. The
    # valbot controller — which runs AS valbot and knows the PID it just launched —
    # instead drops a receipt {marker, pid} into the primary-owned mode-0733 diagnostics
    # directory it already writes its launch log to.
    #
    # The receipt is a HINT, never an authority. It names a PID; every safety-relevant
    # fact about that PID is then re-established here against the kernel:
    #   * the binary at that PID must be valheim.x86_64 (/proc/<pid>/exe, world-readable),
    #   * start-ticks are read by `_pid_start_ticks` — the SAME parser used for client_a,
    #     never taken from the receipt — so PID reuse remains defeated,
    #   * the recorded marker must equal the marker THIS boot generated.
    # A missing, malformed, or mismatched receipt yields None and the caller fails closed
    # exactly as before. Daniel's own game never has a receipt, so it can never be
    # resolved as harness-owned. The implementations live at MODULE scope
    # (`resolve_via_receipt` / `probe_pid_via_receipt`) so they are directly testable.

    def _probe_pid(pid: int) -> Optional[HarnessInstance]:
        # Return the harness provenance of the live process at PID, or None if the PID
        # is gone or carries no harness marker. Used both to resolve the launched client
        # and, immediately before a kill, to re-verify we are terminating OUR process.
        marker = _pid_marker(pid)
        if marker is None:
            # Cross-uid (client_b/valbot): environ is unreadable, so fall back to the
            # attested receipt for this PID. Without this the liveness re-probe would
            # read a healthy valbot client as "gone" and the pre-kill re-verification
            # could never confirm ownership. Still fails closed when no receipt names
            # this exact PID — including for Daniel's own game, which never has one.
            return probe_pid_via_receipt(pid)
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
            # No environ-readable match. For a cross-uid client (client_b runs as valbot)
            # that is the EXPECTED path, not a failure: the kernel hides that process's
            # environ from us entirely. Consult the receipt the launching controller
            # attested, which re-verifies binary + start-ticks against the kernel.
            via_receipt = resolve_via_receipt(target_marker)
            if via_receipt is not None:
                return via_receipt
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
        reset_gabs_state=_reset_gabs_state,
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

    Fail-closed contract (M6-JOIN4): a client that names a `+connect` join target
    (connect_host+connect_port) MUST also name its QA-owned `qa_profile`. A join client
    that omits it is refused HERE (OperatorSafetyError), before any launch — otherwise
    the client boots, arms, and correctly refuses the join at the C# hook, wasting a full
    launch/teardown. There is NO fallback to an existing profile; the guard is an
    allowlist of one, symmetric with the B2 production-port deny.
    """
    from .live_transport import ChannelEndpoint

    # M6-MINT — mint the crypto envelope ONCE, here, upstream of BOTH consumers
    # (the bootstrap-doc provisioner wired into `real_operator_environment` and the
    # in-process `run_config`/entitlement transport). The descriptor carries only
    # topology; a persisted secret-bearing wire field is refused by name rather
    # than silently overridden. We rebind `descriptor` to a composed copy whose
    # `wire` block merges the durable topology with the freshly minted envelope, so
    # every downstream read below — including `real_operator_environment(
    # descriptor=descriptor)` → `BootstrapProvisioner` — sees ONE identical
    # envelope. This is the load-bearing invariant: mint once, pass down; the docs
    # and the transport authenticate against each other and cannot diverge.
    _topology_wire = descriptor.get("wire")
    if not isinstance(_topology_wire, Mapping):
        raise OperatorSafetyError("descriptor must carry a `wire` topology block")
    assert_descriptor_carries_no_wire_secrets(_topology_wire)
    _minted = mint_wire_envelope(resolve_ttl_seconds(descriptor))
    _composed_wire = {k: v for k, v in _topology_wire.items() if k != "ttl_seconds"}
    _composed_wire.update(_minted)
    descriptor = {**descriptor, "wire": _composed_wire}

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
            # M6-JOIN3 / B1: the QA-owned profile the headless join selects by name (allowlist
            # of one). Absent => the C# hook refuses the join rather than load a human profile.
            qa_profile=(str(c["qa_profile"]) if c.get("qa_profile") is not None else None),
            # M6-JOIN3 / B2: the mode-0600 lane-password file's PATH for a password-gated lane.
            server_password_file=(
                str(c["server_password_file"]) if c.get("server_password_file") is not None else None
            ),
        )
        for c in descriptor["clients"]
    )

    # M6-JOIN4 — fail closed at COMPOSE if a real join client omits its QA profile.
    #
    # The C# auto-join hook already refuses (never loads a human character) when
    # SBPR_QA_PROFILE is absent, and `build_request` deliberately omits the key rather
    # than inventing one — that guard is the last line of defence and must stay. But a
    # descriptor that names a `+connect` join target (connect_host+connect_port: a REAL
    # live launch, not a legacy/unit spec) yet leaves `qa_profile` unset produces a
    # client that boots, arms, and then CORRECTLY refuses the join — burning a full
    # launch/teardown to deliver nothing. That is exactly the silent seam this card was
    # filed on: the deployed descriptor shipped `qa_profile: null`, the runner wrote a
    # sidecar with no SBPR_QA_PROFILE, and every join was refused in under a second.
    #
    # A join client MUST name its own QA-owned profile. Catch the omission HERE, loudly,
    # before anything launches — an allowlist of one, no fallback, symmetric with the B2
    # production-port deny that also fires at spec-build time. A non-join spec (no
    # connect target: legacy/unit shape) is unaffected.
    for spec in clients:
        is_join_client = spec.connect_host is not None and spec.connect_port is not None
        if is_join_client and not spec.qa_profile:
            raise OperatorSafetyError(
                f"client {spec.actor!r} names a +connect join target "
                f"({spec.connect_host}:{spec.connect_port}) but no `qa_profile`; a QA join "
                "MUST name its own QA-owned profile (SBPR_QA_PROFILE) so it can never load a "
                "human character. Refusing at compose rather than launching a client that "
                "would boot only to refuse the join. Add `qa_profile` to this client in the "
                "run descriptor (no fallback to an existing profile is permitted)."
            )

    # M6-LANEPW — fail closed at COMPOSE if the lane's declared password policy does not
    # match the client entries. See live_preflight.validate_lane_password_consistency for
    # the full defect narrative: a password-gated lane whose descriptor names no password
    # produces a client that connects, stalls on vanilla's password prompt, never spawns a
    # player, and therefore never reaches TryArm — a full launch/teardown burned to deliver
    # nothing, misread for a day as a bootstrap-delivery problem. Same shape as the
    # M6-JOIN4 guard above: cheap, descriptor-only, evaluated before anything launches.
    validate_lane_password_consistency(descriptor)

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
        # Post-M6-MINT the MINTED envelope is the sole authority on expiry. Reading a
        # persisted top-level `expiry` here would reintroduce exactly the wall MINT
        # closed: a descriptor-resident timestamp that nothing refreshes, which went
        # 106 minutes stale and made the helper correctly refuse to arm. `wire` at this
        # point is the composed (minted) envelope, so this is always fresh and always
        # agrees with the bootstrap docs + live transport, which share that one envelope.
        expiry=int(wire["expiry_unix_ms"]),
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
        readiness_timeout_s=float(boot_d.get("readiness_timeout_s", 300.0)),
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
