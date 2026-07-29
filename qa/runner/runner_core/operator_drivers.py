"""Operator launch/seed drivers for a live T022 run (ADR-0009 §5, §9) — M6-EXEC.

These are the operator-only drivers the runner's live path composes. They build
the *capability* to drive a live in-world T022 run; they do NOT run one on import
or under the test suite. Every guard is a HARD, TESTED fail-closed check, not a
convention:

  * `LaneLauncher` — bring up the isolated disposable lane. Refuses to target the
    production Niflheim (2456) / Heistan (2466) ports; readiness is confirmed by an
    explicit health/log signal, never a blind sleep.
  * `DualClientLauncher` — launch two Valheim clients under the two distinct
    licensed Steam identities. Refuses to touch any `valheim.x86_64` it did not
    itself launch (fail closed). Deterministic teardown of everything it started,
    on every exit path.
  * `EntitlementSeeder` — drive the product OFFER→BUY admin path via the product's
    own `sbpr_master offer|buy` (discriminators CmdOffer=1 / CmdBuy=2). NEVER mints,
    signs, or grants entitlement (threats T3/T5) — it only invokes the product's
    authenticated admin RPC and reports the operator line the product emits.
  * `AdminlistGuard` — capture SHA-256 before any adminlist change, restore
    byte-identically at teardown, verify the hash matches, and surface a LOUD
    failure if it does not.

DESIGN: process spawning + admin RPC delivery are injected behind small callables
(`spawn`, `deliver`) so the drivers are fully unit-testable with NO real Valheim
and NO game I/O. A real operator run wires the real spawn/deliver; the tests wire
fakes. Importing or unit-testing this module launches nothing.

Engine-free: stdlib only. No Valheim/BepInEx/Unity import, no product reference.
"""
from __future__ import annotations

import hashlib
import os
import uuid
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Mapping, Optional, Sequence


class OperatorSafetyError(RuntimeError):
    """A hard fail-closed operator guard tripped. Never downgraded to a warning."""


# Hard production deny list (ADR-0009 §5.1). These are SERVER PORTS, denied even if
# an allowlist is misconfigured. A lane may never bind or target either.
PRODUCTION_PORTS = frozenset({2456, 2466})
PRODUCTION_PORT_LABELS = {2456: "Niflheim", 2466: "Heistan"}


def assert_connect_target_not_production(port: int) -> None:
    """Hard deny: a client `+connect` target may NEVER be a production server port.

    The client join target (`+connect host:port`) is a launch surface distinct from
    the lane bind port `LaneLauncher.assert_disposable` guards. A descriptor typo that
    named 2456/2466 here would point a licensed client at the live Niflheim/Heistan
    server — so the SAME hard production deny is applied to the connect target, before
    any launch, as a tested guard (ADR-0009 §5.1). Fail closed, never a convention.
    """
    if port in PRODUCTION_PORTS:
        label = PRODUCTION_PORT_LABELS.get(port, "production")
        raise OperatorSafetyError(
            f"refusing to +connect a client to production {label} port {port}: "
            "the QA client joins the disposable lane ONLY (ADR-0009 §5.1 hard deny)"
        )

# The two distinct licensed Steam identities proven simultaneously in t_e3aa60f4.
# Fixed, non-secret public SteamID64s; a live run must present exactly these two.
LICENSED_STEAM_IDENTITIES = ("76561197965627562", "76561198671522196")

# Product admin-path discriminators (MasterworkOwnershipProvisioningAdmin.cs:72-73).
# The retired QaT022Driver sent 0/1 (offer dropped, buy invoked OFFER); the correct
# values are 1/2. The seeder pins these so the off-by-one cannot recur.
CMD_OFFER = 1
CMD_BUY = 2
SBPR_MASTER_CONSOLE_COMMAND = "sbpr_master"


# --------------------------------------------------------------------------- #
# Lane launcher
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class LaneSpec:
    """The disposable lane to bring up. `port` MUST NOT be a production port."""

    lane_id: str
    world_name: str
    world_uid: int
    port: int


@dataclass
class LaunchedProcess:
    """A handle to something a driver started, so it can be deterministically stopped."""

    name: str
    handle: object  # opaque process handle from the injected spawn callable
    stopped: bool = False


class LaneLauncher:
    """Bring up the isolated disposable lane, fail-closed on any production target.

    `spawn(spec) -> handle` starts the lane process; `is_ready(handle) -> bool`
    reports an explicit readiness signal (a health probe / log marker the caller
    supplies), polled up to `max_ready_polls` times. There is NO blind sleep: the
    lane is considered up only when `is_ready` returns True, and startup fails
    closed (with teardown) if it never does within the poll budget.
    """

    def __init__(
        self,
        spawn: Callable[[LaneSpec], object],
        is_ready: Callable[[object], bool],
        stop: Callable[[object], None],
        *,
        max_ready_polls: int = 120,
    ) -> None:
        self._spawn = spawn
        self._is_ready = is_ready
        self._stop = stop
        self._max_ready_polls = max_ready_polls
        self._process: Optional[LaunchedProcess] = None

    @staticmethod
    def assert_disposable(spec: LaneSpec) -> None:
        """Hard guard: refuse a production port. Tested, not a convention."""
        if spec.port in PRODUCTION_PORTS:
            label = PRODUCTION_PORT_LABELS.get(spec.port, "production")
            raise OperatorSafetyError(
                f"refusing to target production {label} port {spec.port}: "
                "the QA lane is disposable-world ONLY (ADR-0009 §5.1 hard deny)"
            )

    def start(self, spec: LaneSpec) -> LaunchedProcess:
        self.assert_disposable(spec)
        if self._process is not None:
            raise OperatorSafetyError("lane already started")
        handle = self._spawn(spec)
        proc = LaunchedProcess(name=f"lane:{spec.lane_id}", handle=handle)
        self._process = proc
        # Explicit readiness — never a blind sleep.
        for _ in range(self._max_ready_polls):
            if self._is_ready(handle):
                return proc
        # Never became ready: tear down and fail closed.
        self.stop()
        raise OperatorSafetyError(
            f"lane {spec.lane_id!r} never signalled readiness within "
            f"{self._max_ready_polls} polls; torn down"
        )

    def stop(self) -> None:
        proc = self._process
        if proc is None or proc.stopped:
            return
        try:
            self._stop(proc.handle)
        finally:
            proc.stopped = True

    @property
    def running(self) -> bool:
        return self._process is not None and not self._process.stopped


# --------------------------------------------------------------------------- #
# Dual-client launcher
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class ClientSpec:
    """One Valheim GUI client to launch under a specific licensed identity.

    The first three fields are the original launch identity and are REQUIRED and
    unchanged (existing callers keep working). The remaining fields are ADDITIVE and
    describe the GABS-mediated modded-launch path this box actually requires: a bare
    `subprocess.Popen([binary_path])` produces a client that never injects BepInEx,
    never receives `SBPR_QA_T022_BOOTSTRAP`, never joins the lane, and therefore never
    arms or binds its loopback control port (attempt-7 block, `live_composition.py`
    launch seam). When these are supplied the client is booted through its GABS/MCP
    endpoint with the bootstrap doc + `+connect` join target + a readiness poll on the
    helper's loopback control port. When they are absent (unit tests, legacy callers)
    the spec is still valid — only the REAL GABS booter consults them.
    """

    actor: str            # "client_a" / "client_b"
    steam_id: str         # must be one of LICENSED_STEAM_IDENTITIES
    binary_path: str      # absolute path to the valheim.x86_64 this run OWNS
    # --- additive GABS-launch fields (optional; only the real booter reads them) ---
    gabs_endpoint: Optional[str] = None   # e.g. "http://localhost:8080/mcp"
    game_id: str = "valheim"              # GABS gameId for games_start/games_kill
    bootstrap_path: Optional[str] = None  # abs path the runner wrote the arm-bootstrap JSON to
    connect_host: Optional[str] = None    # lane host to `+connect` to
    connect_port: Optional[int] = None    # lane join port (the disposable lane, e.g. 2476)
    loopback_port: Optional[int] = None   # the helper's loopback control port to poll for armed-readiness
    # Absolute path of the launch-env SIDECAR this client's wrapper reads. The GABS
    # daemon forks the client with the daemon's env, NOT the runner's — so the arming
    # vars cannot be delivered by mutating the runner's `os.environ`. Instead the runner
    # writes them to this sidecar file and the launch wrapper (`run-trailborne.sh` and
    # the valbot controller chain) sources it just before `exec`ing the game. Each
    # client's wrapper reads a path derived from ITS launching user's `$HOME` +
    # `$GABS_GAME_ID`; the descriptor names that exact path here so the runner writes
    # where the wrapper will read (the two lanes launch as different users). Absent on
    # legacy/unit specs; the real booter requires it (fail closed at build_request).
    launch_env_path: Optional[str] = None
    # --- M6-JOIN3 additive fields ------------------------------------------------- #
    # The single QA-owned profile name the headless auto-join may select (B1). Non-secret.
    # Delivered to the client as SBPR_QA_PROFILE via the launch-env sidecar. When present the
    # QA FejdStartup hook selects THIS profile by name (allowlist of one) — creating it if
    # absent, refusing the join otherwise — so a human character can never be loaded. Absent
    # on legacy/unit specs; a real live descriptor always names it.
    qa_profile: Optional[str] = None
    # Absolute path of the mode-0600 lane-password file this client's wrapper/helper reads
    # (B2). Non-secret PATH only (the password VALUE lives inside the 0600 file the
    # LanePasswordProvisioner writes). Delivered as SBPR_QA_SERVER_PASSWORD_FILE via the
    # sidecar. Absent for an open/no-password lane.
    server_password_file: Optional[str] = None


class DualClientLauncher:
    """Launch exactly two Valheim clients under the two licensed identities.

    Fail-closed invariants (all tested):
      * exactly two clients, presenting exactly the two licensed identities;
      * refuses to touch a `valheim.x86_64` it did not itself launch — the caller
        supplies `running_binaries()` (the set of already-running valheim binary
        paths); if a requested binary is already running, launch refuses;
      * deterministic teardown of everything it started, on every exit path,
        including partial-launch failure.
    """

    def __init__(
        self,
        spawn: Callable[[ClientSpec], object],
        stop: Callable[[object], None],
        running_binaries: Callable[[], Sequence[str]],
    ) -> None:
        self._spawn = spawn
        self._stop = stop
        self._running_binaries = running_binaries
        self._launched: List[LaunchedProcess] = []

    @staticmethod
    def assert_licensed_pair(specs: Sequence[ClientSpec]) -> None:
        if len(specs) != 2:
            raise OperatorSafetyError(
                f"expected exactly 2 clients, got {len(specs)}"
            )
        ids = tuple(s.steam_id for s in specs)
        if set(ids) != set(LICENSED_STEAM_IDENTITIES):
            raise OperatorSafetyError(
                f"clients must present exactly the two licensed identities "
                f"{LICENSED_STEAM_IDENTITIES}; got {ids}"
            )
        if len(set(ids)) != 2:
            raise OperatorSafetyError("the two clients must use DISTINCT identities")

    def launch(self, specs: Sequence[ClientSpec]) -> List[LaunchedProcess]:
        self.assert_licensed_pair(specs)
        if self._launched:
            raise OperatorSafetyError("clients already launched")

        # Refuse to touch a binary we did not launch: if any requested binary is
        # already running, we must not co-opt it. Fail closed BEFORE spawning.
        already = set(os.path.abspath(p) for p in self._running_binaries())
        for spec in specs:
            target = os.path.abspath(spec.binary_path)
            if target in already:
                raise OperatorSafetyError(
                    f"refusing to touch already-running valheim binary {target!r} "
                    "that this launcher did not start (fail closed)"
                )

        try:
            for spec in specs:
                handle = self._spawn(spec)
                self._launched.append(
                    LaunchedProcess(name=f"client:{spec.actor}", handle=handle)
                )
        except Exception:
            # Partial launch: tear down whatever we started, then re-raise.
            self.teardown()
            raise
        return list(self._launched)

    def teardown(self) -> None:
        """Stop everything we started, on every exit path. Idempotent."""
        for proc in self._launched:
            if proc.stopped:
                continue
            try:
                self._stop(proc.handle)
            finally:
                proc.stopped = True

    @property
    def launched(self) -> List[LaunchedProcess]:
        return list(self._launched)


# --------------------------------------------------------------------------- #
# GABS-mediated modded-client boot (bootstrap + join + armed-readiness poll)
# --------------------------------------------------------------------------- #

class ClientLaunchError(OperatorSafetyError):
    """A GABS-mediated client boot never reached armed readiness. Fail closed.

    Carries which stage did not become ready so the diagnostic names the failure
    (never a silent hang, never a dead handle passed off as a live client).
    """


@dataclass(frozen=True)
class BootRetryPolicy:
    """Retry-with-readiness-poll envelope for the known ValBridge startup wedge.

    A single-shot launch is NOT reliable on this box: the ValBridge/ScriptTools
    startup-scene activation deadlock is intermittent (`boot-qa-client.sh` escapes it
    by re-rolling the boot). So a boot is `max_attempts` re-rolls, each polling an
    explicit readiness signal every `poll_interval_s` up to `readiness_timeout_s`.
    There is NO blind sleep-and-hope: readiness is an explicit probe.
    """

    max_attempts: int = 6
    readiness_timeout_s: float = 150.0
    poll_interval_s: float = 10.0

    def __post_init__(self) -> None:
        if self.max_attempts < 1:
            raise ValueError("max_attempts must be >= 1")
        if self.readiness_timeout_s <= 0 or self.poll_interval_s <= 0:
            raise ValueError("readiness_timeout_s and poll_interval_s must be > 0")

    @property
    def polls_per_attempt(self) -> int:
        # At least one poll per attempt even for a tiny timeout.
        return max(1, int(self.readiness_timeout_s // self.poll_interval_s))


@dataclass(frozen=True)
class ClientLaunchRequest:
    """The fully-resolved launch request for ONE GABS-mediated modded client.

    This is the object the acceptance test inspects: it must actually carry the
    bootstrap env var, the correct `+connect` join target, the actor's GABS
    endpoint + gameId, and the helper's loopback control port. A launch that omits
    any of these produces a client that never arms — precisely the attempt-7 defect.
    Built by `GabsClientBooter.build_request`; never fabricated in a test.
    """

    actor: str
    gabs_endpoint: str
    game_id: str
    loopback_port: int
    connect_target: str                    # "host:port" the client `+connect`s to
    launch_env: Mapping[str, str]          # env the launched process must inherit
    connect_args: Sequence[str]            # the `+connect host:port` argv fragment
    launch_env_path: str                   # abs path of the sidecar the wrapper sources

    @property
    def bootstrap_env_value(self) -> Optional[str]:
        return self.launch_env.get(BOOTSTRAP_ENV_VAR)


# The env var the QA helper reads for its arm-bootstrap doc path (mirrors the C#
# `Plugin.BootstrapEnvVar`). Absent from the launched process => the helper stays
# DISARMED and never binds its loopback control port. This is the single most
# important field the bare-binary launch was dropping.
BOOTSTRAP_ENV_VAR = "SBPR_QA_T022_BOOTSTRAP"
# The identity env the product/Steam layer reads to select the licensed account.
STEAM_ID_ENV_VAR = "SBPR_QA_STEAM_ID"
# Harness-owned provenance marker (B1). A unique per-boot token the harness injects
# into the launched process's environment. Teardown identifies an instance the harness
# ITSELF launched by this marker (plus the captured PID + process start-time), NOT by
# gameId or binary path. A gameId-wide `games_kill` would terminate Daniel's own Steam
# Valheim (different binary path, same gameId "valheim"); this marker is provenance the
# harness alone controls, so teardown can be scoped to a single harness-launched process.
HARNESS_INSTANCE_ENV_VAR = "SBPR_QA_HARNESS_INSTANCE"
# The join target the wrapper turns into a `+connect host:port` launch ARGUMENT (M6-JOIN).
# GABS's games_start delivers no per-launch argv (just as it delivers no per-launch env),
# so this `host:port` rides the same non-secret launch-env sidecar the wrapper already
# sources; the wrapper prepends `+connect` and passes it to the game binary. Absent this,
# the client boots to the main menu and never joins the lane world.
CONNECT_TARGET_ENV_VAR = "SBPR_QA_CONNECT"
# M6-JOIN3 / B1: the single QA-owned profile name the headless auto-join may select. The QA
# FejdStartup hook selects THIS profile by name (allowlist of one), creating it if absent and
# refusing the join otherwise — so a human character (pololol.fch etc.) is structurally
# unreachable by a QA run. Non-secret (a character filename); rides the same sidecar.
QA_PROFILE_ENV_VAR = "SBPR_QA_PROFILE"
# M6-JOIN3 / B2: absolute PATH of the mode-0600 lane-password file the QA hook reads to set
# vanilla FejdStartup.ServerPassword for a password-gated lane. Non-secret PATH only — the
# password VALUE lives inside the 0600 file (written by LanePasswordProvisioner), never here.
SERVER_PASSWORD_FILE_ENV_VAR = "SBPR_QA_SERVER_PASSWORD_FILE"


@dataclass(frozen=True)
class HarnessInstance:
    """Provenance of ONE process the harness itself launched.

    Teardown terminates ONLY a process whose provenance matches a recorded
    `HarnessInstance` — identified by the unique `marker` the harness injected into the
    process environment, keyed to the captured `pid`, and pinned to the process
    `start_ticks` (kernel start time) so a REUSED pid held by a different (possibly
    Daniel-owned) process is never mistaken for ours. All three must match before a
    kill; a mismatch or a missing instance fails closed (block, do not kill).
    """

    actor: str
    marker: str        # the unique HARNESS_INSTANCE_ENV_VAR value the harness injected
    pid: int
    start_ticks: int   # process start time (defeats PID reuse)


class GabsClientBooter:
    """Boot one modded, armed, joined client through its GABS endpoint — then poll.

    Replaces the bare `subprocess.Popen([binary_path])` that could never arm. The
    booter, per client:

      1. builds a `ClientLaunchRequest` carrying the bootstrap env var, the identity
         env, a unique HARNESS provenance marker, the `+connect host:port` join target,
         the GABS endpoint/gameId, and the loopback control port;
      2. drives the launch through the injected seams — `apply_env` (make the launch
         env available to the GABS-launched process), `gabs_start` (request
         `games_start`) — then `resolve_launched` to capture the PID + start-time of
         the process carrying THIS boot's unique marker (harness-owned provenance);
      3. polls `control_ready` (does the helper's loopback control port accept a
         connection?) every `poll_interval_s` up to the per-attempt timeout — the
         armed-readiness signal, NOT a blind sleep;
      4. re-rolls the whole boot up to `max_attempts` times to escape the intermittent
         ValBridge startup wedge; between re-rolls it terminates ONLY its OWN prior
         provenance-recorded instance (never a gameId-wide kill);
      5. fails closed with a `ClientLaunchError` naming the stage if it never arms —
         never hangs, never returns a dead handle.

    TEARDOWN SAFETY (B1): the booter NEVER issues a gameId-wide `games_kill` — that
    would terminate Daniel's own Steam Valheim (same gameId, different binary path). It
    terminates ONLY the exact PID it recorded at spawn, and only after re-verifying —
    immediately before the kill (TOCTOU) — that the live process at that PID still
    carries our unique marker AND the same start-time. Missing/ambiguous provenance =>
    fail closed (block, do not kill).

    Every game-touching action is an injected callable so the booter is fully
    unit-testable with NO real GABS, NO socket, and NO sleep.
    """

    def __init__(
        self,
        *,
        apply_env: Callable[[ClientLaunchRequest], None],
        gabs_start: Callable[[ClientLaunchRequest], None],
        control_ready: Callable[[ClientLaunchRequest], bool],
        resolve_launched: Callable[[ClientLaunchRequest], Optional[HarnessInstance]],
        probe_pid: Callable[[int], Optional[HarnessInstance]],
        terminate: Callable[[HarnessInstance], None],
        sleep: Callable[[float], None],
        policy: Optional[BootRetryPolicy] = None,
        reset_gabs_state: Optional[Callable[[ClientLaunchRequest], None]] = None,
    ) -> None:
        self._apply_env = apply_env
        self._gabs_start = gabs_start
        self._control_ready = control_ready
        self._resolve_launched = resolve_launched
        self._probe_pid = probe_pid
        self._terminate = terminate
        self._sleep = sleep
        self._policy = policy or BootRetryPolicy()
        # M6-GABSLIVE: force GABS's single-gameId liveness view to match reality BEFORE
        # every launch attempt. GABS never reaps the game processes it forks (proven:
        # `internal/process/controller.go:296-302` counts a `<defunct>` zombie as "running"
        # because its name-based `ps` finder still matches the zombie's `comm`). A stale
        # "running" belief then makes `games.start` a silent no-op ("game X is already
        # running", `internal/mcp/stdio_server.go:761-764`). This seam calls `games.stop`
        # (which, being the child's PARENT, actually `Wait()`s and reaps the zombie —
        # empirically verified) so a stale belief can never swallow a launch. Injected so
        # the booter stays unit-testable with no real GABS; None => no reset (legacy/unit).
        self._reset_gabs_state = reset_gabs_state
        # Provenance registry: maps a returned request handle to the instance the
        # harness recorded launching it. kill() refuses any handle absent from here.
        self._instances: Dict[int, HarnessInstance] = {}

    @staticmethod
    def build_request(spec: ClientSpec) -> ClientLaunchRequest:
        """Resolve a `ClientSpec` into the concrete launch request. Fail closed on a
        spec missing any field the modded launch requires (that omission is the bug).

        Also hard-denies a `+connect` target on a production server port (B2): a
        descriptor typo naming Niflheim 2456 / Heistan 2466 as the join target is
        rejected HERE, before any launch, through the same production deny used by the
        lane launcher / preflight — so a client can never be pointed at production.
        """
        # B2 hard deny FIRST: if a connect_port is present, a production server port is
        # rejected before anything else — a production typo must never be masked by some
        # OTHER field being absent. Only when connect_port itself is missing do we fall
        # through to the missing-field report.
        if spec.connect_port is not None:
            assert_connect_target_not_production(int(spec.connect_port))
        missing = [
            name
            for name, val in (
                ("gabs_endpoint", spec.gabs_endpoint),
                ("bootstrap_path", spec.bootstrap_path),
                ("connect_host", spec.connect_host),
                ("connect_port", spec.connect_port),
                ("loopback_port", spec.loopback_port),
                ("launch_env_path", spec.launch_env_path),
            )
            if val is None
        ]
        if missing:
            raise ClientLaunchError(
                f"client {spec.actor!r} cannot be GABS-launched: missing required "
                f"launch fields {missing}; a bare-binary launch would never arm "
                "(bootstrap/join/loopback all absent)"
            )
        # mypy/readers: the None-guard above proves these are set.
        connect_target = f"{spec.connect_host}:{spec.connect_port}"
        # Unique per-boot provenance marker the harness injects and later matches on.
        marker = f"{spec.actor}:{uuid.uuid4().hex}"
        launch_env = {
            BOOTSTRAP_ENV_VAR: str(spec.bootstrap_path),
            STEAM_ID_ENV_VAR: spec.steam_id,
            HARNESS_INSTANCE_ENV_VAR: marker,
            # The join target rides the SAME non-secret sidecar the wrapper sources; the
            # wrapper turns it into `+connect host:port`. This is the argument-half fix
            # (M6-JOIN) mirroring the env-half fix (M6-LAUNCHENV): GABS delivers neither
            # per-launch env NOR per-launch argv, so both cross the fork via the sidecar.
            CONNECT_TARGET_ENV_VAR: connect_target,
        }
        # M6-JOIN3 / B1: name the single QA-owned profile the headless join may select. Only
        # added when the descriptor supplied it — the C# hook fails closed (refuses the join)
        # if SBPR_QA_PROFILE is absent, so a run that forgot it never loads a human character.
        if spec.qa_profile:
            launch_env[QA_PROFILE_ENV_VAR] = str(spec.qa_profile)
        # M6-JOIN3 / B2: name the mode-0600 lane-password file's PATH (the value lives in that
        # file, never here). Only added for a password-gated lane that named the file.
        if spec.server_password_file:
            launch_env[SERVER_PASSWORD_FILE_ENV_VAR] = str(spec.server_password_file)
        return ClientLaunchRequest(
            actor=spec.actor,
            gabs_endpoint=str(spec.gabs_endpoint),
            game_id=spec.game_id,
            loopback_port=int(spec.loopback_port),  # type: ignore[arg-type]
            connect_target=connect_target,
            launch_env=launch_env,
            connect_args=("+connect", connect_target),
            launch_env_path=str(spec.launch_env_path),
        )

    def boot(self, spec: ClientSpec) -> ClientLaunchRequest:
        """Boot the client to armed readiness, re-rolling on the ValBridge wedge.

        Returns the `ClientLaunchRequest` (the live handle) once the helper's loopback
        control port accepts connections AND the harness has captured provenance (PID +
        start-time) of the process carrying this boot's unique marker. Raises
        `ClientLaunchError` — naming the stage — if no attempt arms within the policy.
        Never returns a dead handle, and never a handle it cannot safely tear down.
        """
        request = self.build_request(spec)
        last_stage = "no attempt ran"
        last_instance: Optional[HarnessInstance] = None
        for attempt in range(1, self._policy.max_attempts + 1):
            # Re-roll cleanup: terminate ONLY our own prior recorded instance (never a
            # gameId-wide kill). First attempt has no prior instance.
            if last_instance is not None:
                self._terminate_owned_best_effort(last_instance)
                last_instance = None
            # M6-GABSLIVE: force GABS's view to match reality BEFORE the launch. GABS
            # never reaps its forked children, so a `<defunct>` zombie from a prior run
            # leaves its single-gameId liveness model stuck on "running" — and the next
            # `games.start` is a silent no-op. Clearing the stale state (games.stop, which
            # reaps the zombie as its parent) before EVERY attempt means a stale belief can
            # never swallow this launch. Best-effort: a reset failure is surfaced in the
            # stage string but does not itself abort — the no-op detector below is the hard
            # gate that catches a launch that forked nothing.
            if self._reset_gabs_state is not None:
                try:
                    self._reset_gabs_state(request)
                except Exception as exc:  # noqa: BLE001 — surface, continue to launch
                    last_stage = f"attempt {attempt}: GABS state reset failed ({exc})"
            try:
                self._apply_env(request)
                self._gabs_start(request)
            except Exception as exc:  # noqa: BLE001 — surface, then re-roll
                last_stage = f"attempt {attempt}: launch request failed ({exc})"
                continue
            # Capture harness-owned provenance: the process carrying THIS boot's marker.
            #
            # NO-OP DETECTION (M6-GABSLIVE): `games.start` can return SUCCESS yet fork
            # nothing — the stale-"running" silent no-op that burned all six re-rolls of
            # run 8. `resolve_launched` polls only `control_probe_timeout_s` (a few
            # seconds) for a process carrying our unique marker; when GABS forked nothing,
            # NO such process ever appears and this returns None FAST (bounded by that
            # short probe, asserted on poll count in the tests, not the readiness budget).
            # We surface it by name — "GABS reported success but forked nothing" — instead
            # of silently re-rolling into the same wall, and re-roll (the reset at the top
            # of the next attempt clears the stale state that caused it).
            instance = self._resolve_launched(request)
            if instance is None:
                last_stage = (
                    f"attempt {attempt}: GABS reported games.start success but forked "
                    f"nothing — no process carrying marker "
                    f"{request.launch_env[HARNESS_INSTANCE_ENV_VAR]!r} appeared within the "
                    "provenance probe window (stale 'running' no-op, or ambiguous "
                    "provenance) — refusing to proceed without a tear-down-able instance "
                    "(no harness provenance established)"
                )
                continue
            last_instance = instance
            # Explicit armed-readiness poll — never a blind sleep.
            #
            # LIVENESS (M6-STEAMGATE): a launched client can DIE during this window
            # rather than merely take its time to arm — the deterministic
            # `Steamworks is not initialized` crash exits ~6s into boot. Polling a
            # corpse for the full readiness_timeout_s is pure waste (6 attempts ×
            # 150s of polling a dead process was exactly the observed failure). So
            # between readiness polls we re-probe the recorded instance's PID: if the
            # process is GONE (or the PID is now a foreign/reused process), we abandon
            # THIS attempt immediately and re-roll, surfacing a crash-on-boot in
            # seconds instead of minutes. This is a READ of live state only; it does
            # not touch `_terminate_owned`'s TOCTOU re-check, and "process gone" here
            # is a clean abandon-and-re-roll path, never an error.
            wedged = False
            for _ in range(self._policy.polls_per_attempt):
                if self._control_ready(request):
                    self._instances[id(request)] = instance
                    return request
                current = self._probe_pid(instance.pid)
                if (
                    current is None
                    or current.marker != instance.marker
                    or current.start_ticks != instance.start_ticks
                ):
                    # The client we launched is gone (crash-on-boot) or the PID is now
                    # a different process. Do not keep polling a dead attempt — abandon
                    # it now and re-roll (the top-of-loop cleanup treats an already-gone
                    # instance as a clean, verified teardown).
                    last_stage = (
                        f"attempt {attempt}: PROCESS DIED — launched client (PID "
                        f"{instance.pid}) exited before arming; abandoned immediately "
                        "rather than polling the readiness budget. The process is GONE, "
                        "so the helper never got the chance to arm. Read the client's "
                        "Player.log / BepInEx LogOutput.log for the exit cause (a "
                        "deterministic ~6s exit is typically `Steamworks is not "
                        "initialized`). NOTE: this is the process-died path ONLY — if "
                        "the client stayed alive and merely never armed, the run fails "
                        "on the NEVER-ARMED stage below instead, which is a different "
                        "defect class with different causes."
                    )
                    wedged = True
                    break
                self._sleep(self._policy.poll_interval_s)
            if wedged:
                continue
            last_stage = (
                f"attempt {attempt}: NEVER ARMED — the launched client is STILL ALIVE "
                f"(PID {instance.pid}) but loopback control port {request.loopback_port} "
                f"never accepted a connection within {self._policy.readiness_timeout_s}s. "
                "The process did NOT crash; it ran and failed to reach TryArm. Do not "
                "read this as a boot/Steam problem. Likely causes, in order: the join "
                "handshake never completed (e.g. a password-gated lane the client "
                "supplied no password for — vanilla waits on a prompt no headless client "
                "answers, so Player.OnSpawned never fires and the arm deferrer spins), a "
                "gate inside TryArm refused (grep the client's BepInEx LogOutput.log for "
                "'staying DISARMED' — every refusal names its reason), or a ValBridge "
                "wedge. NOTE: the banner 'SBPR.QaHarness.T022 — DISARMED' at plugin load "
                "is an UNCONDITIONAL header logged before the bootstrap is even read; it "
                "is NOT a verdict and does not mean the bootstrap was missing."
            )
        # Every attempt wedged: tear down our LAST recorded instance (provenance-scoped,
        # never gameId-wide) and fail closed loudly.
        if last_instance is not None:
            self._terminate_owned_best_effort(last_instance)
        raise ClientLaunchError(
            f"client {spec.actor!r} never reached armed readiness after "
            f"{self._policy.max_attempts} boot attempts; last stage: {last_stage}"
        )

    def kill(self, request: object) -> None:
        """Deterministically tear down a harness-launched client and verify it is gone.

        Refuses to touch anything but a request THIS booter launched AND recorded
        provenance for. Fails CLOSED (raises) on ambiguous provenance rather than
        killing the wrong process; verifies process-gone after terminating. Never
        issues a gameId-wide kill, so Daniel's own Steam Valheim can never be a target.
        Idempotent for an already-gone instance.
        """
        if not isinstance(request, ClientLaunchRequest):
            # A handle this booter did not produce (e.g. a raw Popen). Never touch it.
            return
        instance = self._instances.get(id(request))
        if instance is None:
            # No recorded provenance for this handle => the harness cannot prove it
            # launched this process. Fail closed: do NOT kill.
            raise ClientLaunchError(
                f"client {request.actor!r} has no recorded harness provenance; refusing "
                "to terminate a process the harness cannot prove it launched (fail closed)"
            )
        self._terminate_owned(instance, verify_gone=True)
        # Instance torn down: drop it so a second kill is a no-op.
        self._instances.pop(id(request), None)

    def _terminate_owned(self, instance: HarnessInstance, *, verify_gone: bool) -> None:
        """Terminate ONLY the recorded harness instance, with a TOCTOU re-check.

        Immediately before the kill, re-probe the live process at the recorded PID and
        confirm it STILL carries our marker AND the same start-time. If the PID is now
        held by a different process (PID reuse / a foreign client that appeared between
        the ownership check and the kill), refuse — Daniel's game is never collateral.
        """
        current = self._probe_pid(instance.pid)
        if current is None:
            # Already gone — nothing to terminate. Teardown is (vacuously) verified.
            return
        if current.marker != instance.marker or current.start_ticks != instance.start_ticks:
            # PID reuse / foreign process now at this PID. Refuse to kill it.
            raise ClientLaunchError(
                f"refusing to terminate PID {instance.pid}: the live process no longer "
                f"matches recorded harness provenance (marker/start-time mismatch) — a "
                f"foreign or reused-PID process, NOT the client {instance.actor!r} we "
                "launched (TOCTOU fail-closed)"
            )
        self._terminate(instance)
        if verify_gone:
            after = self._probe_pid(instance.pid)
            if after is not None and after.marker == instance.marker and after.start_ticks == instance.start_ticks:
                raise ClientLaunchError(
                    f"client {instance.actor!r} (PID {instance.pid}) still present after "
                    "terminate; teardown unverified"
                )

    def _terminate_owned_best_effort(self, instance: HarnessInstance) -> None:
        """Provenance-scoped teardown that never raises (used in boot re-roll cleanup)."""
        try:
            self._terminate_owned(instance, verify_gone=False)
        except Exception:  # noqa: BLE001 — best-effort between re-rolls / before raising
            pass


# --------------------------------------------------------------------------- #
# Authorized entitlement seeding (OFFER -> BUY via the product admin path)
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class SeedResult:
    """The operator line the product emitted for one admin command. Descriptive only."""

    command: str          # "offer" | "buy"
    discriminator: int    # CMD_OFFER | CMD_BUY
    operator_line: str    # verbatim product output; the harness asserts nothing here


class EntitlementSeeder:
    """Drive the product OFFER→BUY admin path — the harness NEVER mints entitlement.

    `deliver(discriminator) -> operator_line` invokes the product's OWN authenticated
    admin RPC (`sbpr_master`, discriminator CmdOffer=1 / CmdBuy=2) and returns the
    product's operator line. The seeder holds NO signing key, constructs NO
    entitlement, and has NO code path that grants ownership — it only asks the
    product to run its own path (threats T3/T5). The offer is run by the Governor
    client, the buy by the attuned buyer client (the reservation model forbids one
    character holding both at one Stone), so `deliver` is bound to the correct
    caller by the operator wiring, not by this class.
    """

    def __init__(self, deliver: Callable[[int], str]) -> None:
        self._deliver = deliver

    def offer(self) -> SeedResult:
        line = self._deliver(CMD_OFFER)
        return SeedResult(command="offer", discriminator=CMD_OFFER, operator_line=line)

    def buy(self) -> SeedResult:
        line = self._deliver(CMD_BUY)
        return SeedResult(command="buy", discriminator=CMD_BUY, operator_line=line)

    def seed(self) -> List[SeedResult]:
        """Run the authorized OFFER→BUY sequence in order. Never in reverse."""
        return [self.offer(), self.buy()]


# --------------------------------------------------------------------------- #
# Adminlist safety
# --------------------------------------------------------------------------- #

class AdminlistGuard:
    """Capture / restore / verify the server adminlist byte-identically.

    On `arm()` it records the SHA-256 of the current adminlist bytes. On
    `restore()` it writes the captured bytes back and re-hashes to confirm a
    byte-identical restore; a mismatch raises `OperatorSafetyError` (a LOUD
    failure, never swallowed). File I/O is injected (`read_bytes` / `write_bytes`)
    so the guard is testable without a real adminlist file.
    """

    def __init__(
        self,
        read_bytes: Callable[[], bytes],
        write_bytes: Callable[[bytes], None],
    ) -> None:
        self._read = read_bytes
        self._write = write_bytes
        self._original: Optional[bytes] = None
        self._original_sha: Optional[str] = None

    @staticmethod
    def _sha256(data: bytes) -> str:
        return hashlib.sha256(data).hexdigest()

    def arm(self) -> str:
        """Capture the current adminlist + its SHA-256. Returns the captured hash."""
        if self._original is not None:
            raise OperatorSafetyError("adminlist guard already armed")
        data = self._read()
        self._original = bytes(data)
        self._original_sha = self._sha256(self._original)
        return self._original_sha

    @property
    def original_sha256(self) -> Optional[str]:
        return self._original_sha

    def restore(self) -> str:
        """Restore the captured bytes and verify the hash matches. Loud on mismatch."""
        if self._original is None or self._original_sha is None:
            raise OperatorSafetyError("adminlist guard restore called before arm")
        self._write(self._original)
        after = self._read()
        after_sha = self._sha256(after)
        if after_sha != self._original_sha:
            raise OperatorSafetyError(
                "ADMINLIST RESTORE MISMATCH: expected sha256 "
                f"{self._original_sha}, got {after_sha} after restore — "
                "the adminlist was NOT restored byte-identically (loud failure)"
            )
        return after_sha
