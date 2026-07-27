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
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Mapping, Optional, Sequence


class OperatorSafetyError(RuntimeError):
    """A hard fail-closed operator guard tripped. Never downgraded to a warning."""


# Hard production deny list (ADR-0009 §5.1). These are SERVER PORTS, denied even if
# an allowlist is misconfigured. A lane may never bind or target either.
PRODUCTION_PORTS = frozenset({2456, 2466})
PRODUCTION_PORT_LABELS = {2456: "Niflheim", 2466: "Heistan"}

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


class GabsClientBooter:
    """Boot one modded, armed, joined client through its GABS endpoint — then poll.

    Replaces the bare `subprocess.Popen([binary_path])` that could never arm. The
    booter, per client:

      1. builds a `ClientLaunchRequest` carrying the bootstrap env var, the identity
         env, the `+connect host:port` join target, the GABS endpoint/gameId, and the
         loopback control port;
      2. drives the launch through the injected seams — `apply_env` (make the launch
         env available to the GABS-launched process), `gabs_kill` (clear any stale
         instance), `gabs_start` (request `games_start`);
      3. polls `control_ready` (does the helper's loopback control port accept a
         connection?) every `poll_interval_s` up to the per-attempt timeout — the
         armed-readiness signal, NOT a blind sleep;
      4. re-rolls the whole boot up to `max_attempts` times to escape the intermittent
         ValBridge startup wedge;
      5. fails closed with a `ClientLaunchError` naming the stage if it never arms —
         never hangs, never returns a dead handle.

    Every game-touching action is an injected callable so the booter is fully
    unit-testable with NO real GABS, NO socket, and NO sleep.
    """

    def __init__(
        self,
        *,
        apply_env: Callable[[ClientLaunchRequest], None],
        gabs_start: Callable[[ClientLaunchRequest], None],
        gabs_kill: Callable[[ClientLaunchRequest], None],
        control_ready: Callable[[ClientLaunchRequest], bool],
        process_gone: Callable[[ClientLaunchRequest], bool],
        sleep: Callable[[float], None],
        policy: Optional[BootRetryPolicy] = None,
    ) -> None:
        self._apply_env = apply_env
        self._gabs_start = gabs_start
        self._gabs_kill = gabs_kill
        self._control_ready = control_ready
        self._process_gone = process_gone
        self._sleep = sleep
        self._policy = policy or BootRetryPolicy()

    @staticmethod
    def build_request(spec: ClientSpec) -> ClientLaunchRequest:
        """Resolve a `ClientSpec` into the concrete launch request. Fail closed on a
        spec missing any field the modded launch requires (that omission is the bug).
        """
        missing = [
            name
            for name, val in (
                ("gabs_endpoint", spec.gabs_endpoint),
                ("bootstrap_path", spec.bootstrap_path),
                ("connect_host", spec.connect_host),
                ("connect_port", spec.connect_port),
                ("loopback_port", spec.loopback_port),
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
        launch_env = {
            BOOTSTRAP_ENV_VAR: str(spec.bootstrap_path),
            STEAM_ID_ENV_VAR: spec.steam_id,
        }
        return ClientLaunchRequest(
            actor=spec.actor,
            gabs_endpoint=str(spec.gabs_endpoint),
            game_id=spec.game_id,
            loopback_port=int(spec.loopback_port),  # type: ignore[arg-type]
            connect_target=connect_target,
            launch_env=launch_env,
            connect_args=("+connect", connect_target),
        )

    def boot(self, spec: ClientSpec) -> ClientLaunchRequest:
        """Boot the client to armed readiness, re-rolling on the ValBridge wedge.

        Returns the `ClientLaunchRequest` (the live handle) once the helper's loopback
        control port accepts connections. Raises `ClientLaunchError` — naming the stage
        — if no attempt arms within the policy. Never returns a dead handle.
        """
        request = self.build_request(spec)
        last_stage = "no attempt ran"
        for attempt in range(1, self._policy.max_attempts + 1):
            # Clear any stale instance, publish the launch env, request games_start.
            try:
                self._gabs_kill(request)
                self._apply_env(request)
                self._gabs_start(request)
            except Exception as exc:  # noqa: BLE001 — surface, then re-roll
                last_stage = f"attempt {attempt}: launch request failed ({exc})"
                continue
            # Explicit armed-readiness poll — never a blind sleep.
            for _ in range(self._policy.polls_per_attempt):
                if self._control_ready(request):
                    return request
                self._sleep(self._policy.poll_interval_s)
            last_stage = (
                f"attempt {attempt}: loopback control port {request.loopback_port} "
                f"never accepted a connection within {self._policy.readiness_timeout_s}s "
                "(helper never armed / ValBridge wedge)"
            )
        # Every attempt wedged: tear the last instance down and fail closed loudly.
        try:
            self._gabs_kill(request)
        except Exception:  # noqa: BLE001 — best-effort teardown before we raise
            pass
        raise ClientLaunchError(
            f"client {spec.actor!r} never reached armed readiness after "
            f"{self._policy.max_attempts} boot attempts; last stage: {last_stage}"
        )

    def kill(self, request: object) -> None:
        """Deterministically tear down a GABS-launched client and verify it is gone.

        Refuses to touch anything but a request THIS booter produced. Idempotent.
        """
        if not isinstance(request, ClientLaunchRequest):
            return
        self._gabs_kill(request)
        if not self._process_gone(request):
            raise ClientLaunchError(
                f"client {request.actor!r} still present after games_kill on "
                f"{request.gabs_endpoint} (gameId={request.game_id!r}); teardown unverified"
            )


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
