"""VERIFY — read back everything arrange just established (T022 ARRANGE §4 VERIFY, #456).

WHAT THIS EXISTS TO PREVENT
---------------------------
Twelve days produced zero executed acceptance tests, and not one failure was in the
test logic. Every single one was an arrangement that *looked* done: a missing plugin, a
credential written unreadable by its consumer, a join target that never reached the
game, a port already held by the other client. All four produced the identical
observable — a client sitting at a menu — because **nothing read the arranged state
back**. "Is it arranged?" was answered by inference from process tables and by the
absence of an error message, which is the weakest evidence a system can offer.

This phase converts that inference into a file read (§3 P7). It asserts, per client:

  V1  every required artifact is present with the pinned bytes                  [I1][I8]
  V2  every credential is readable BY ITS CONSUMING UID, tested as that uid     [I4]
  V3  the join target is present in this client's ACTUAL launch path            [I5]
  V4  this client's ports are disjoint from every sibling's AND actually free   [I6]

and emits a machine-readable per-client readiness report (V5).

THE HONESTY RULE THIS PHASE IS BUILT AROUND
-------------------------------------------
"Logs green ≠ playable" (`AGENTS.md`). A report that says READY is a claim that a client
could actually join, so every criterion records **how** it was established, not merely
that it passed. V3 in particular has two rungs of evidence and they are NOT equivalent:

  * `live-argv` — a running process's real `/proc/<pid>/cmdline` carries
    `+connect host:port`. This is proof. It is also only available AFTER launch.
  * `staged-delivery` — the launch-env sidecar on disk carries `SBPR_QA_CONNECT=host:port`
    at the exact path this client's wrapper reads, AND that wrapper demonstrably turns it
    into a `+connect` argv fragment (reusing #453's `inspect_wrapper`). This is strong
    pre-launch evidence and it is what VERIFY can honestly obtain in the phase ordering
    (VERIFY precedes LAUNCH), but it is not the same claim.

The distinction is recorded in the report as `method` and `proven_live`, rather than
being flattened into a boolean. A readiness report that cannot tell an operator which
kind of evidence it holds is exactly the "arranged, probably" this ticket exists to kill.

A PARTIAL ARRANGEMENT IS A HARD, NAMED FAILURE
----------------------------------------------
There is no partial success and no silent proceed. A criterion that cannot be
*established* fails exactly as loudly as one that is established false — an
undeterminable port, an unenumerable process table, an absent launch-env declaration all
fail closed, each naming the precondition, the client, and expected-vs-actual (§3 P3).
A client is READY only when every criterion passed; the run is READY only when every
client is. Checks never short-circuit: one invocation reports every problem, because the
cost this phase exists to avoid is discovering problems one ten-minute boot at a time.

PROOF SEAMS ARE MANDATORY, NOT DEFAULTED (§3 P9)
------------------------------------------------
`VerifyEnvironment` carries NO field with a default, for the reason #454/#467/#473
established and re-established three times over on `StaticEnvironment`: a defaulted
"cannot prove" seam fails closed, so it is never a security hole, but it is a diagnostic
one. An omitted wiring then surfaces as a fault in the *client's machine* — "port state
undeterminable", "process table unenumerable" — sending an operator to inspect a box
that is fine, and emitting the same line a genuine fault would. A caller that cannot
probe ports or enumerate processes says so by passing a function that returns `None`,
recording that choice at the construction site. The contract is enforced structurally
(a `dataclasses.fields` assertion plus an AST scan of every construction site in the
repository), not per-seam, so a future merge cannot quietly re-default a seam that every
current test happens to supply.

REUSE, NOT REIMPLEMENTATION
---------------------------
V1 delegates to `ArtifactStager.assert_postconditions` (#451), which already re-reads
every artifact from disk and reports in the `StaticFailure` shape. V2 delegates to
`credential_access.assert_readable_as_consumer` (#452), which already establishes the
cross-uid read seam — a credential readable by the uid that WROTE it proves nothing, and
there must be exactly one mechanism that knows how to read as another identity. V3
reuses `join_delivery.inspect_wrapper` (#453). This phase adds the reading-back and the
report; it does not add a second opinion about any of them.

Engine-free: stdlib only, no product/game import. Every environment contact goes through
the injected seam, so importing or unit-testing this module reads nothing, binds nothing,
and spawns nothing.
"""
from __future__ import annotations

import errno
import os
import socket
from collections import defaultdict
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple

from .arrange_manifest import ArrangeManifest, ClientEntry
from .arrange_static import StaticFailure
from .credential_access import CredentialReadError, assert_readable_as_consumer
from .join_delivery import CONNECT_VAR, inspect_wrapper

# Stable precondition ids, reported verbatim and grepped by operators — part of the
# contract, exactly like the STATIC S-ids and the STAGE T-ids.
P_ARTIFACTS = "V1-ARTIFACTS-VERIFIED"
P_CREDENTIALS = "V2-CREDENTIAL-READABLE-BY-CONSUMER"
P_JOIN_PATH = "V3-JOIN-IN-LAUNCH-PATH"
P_PORTS = "V4-PORTS-DISJOINT-AND-FREE"

ALL_CRITERIA = (P_ARTIFACTS, P_CREDENTIALS, P_JOIN_PATH, P_PORTS)

# How V3's evidence was obtained. Recorded per client in the readiness report because
# the two are NOT the same claim (see the module docstring).
METHOD_LIVE_ARGV = "live-argv"
METHOD_STAGED_DELIVERY = "staged-delivery"
METHOD_NONE = "unestablished"

# The `+connect` flag vanilla parses into `m_queuedJoinServer`. Mirrored from the
# wrapper contract in `launch_env`/`join_delivery`; if it ever changes, this check must
# change in lockstep or it silently passes.
CONNECT_FLAG = "+connect"


@dataclass(frozen=True)
class LiveProcess:
    """One running process attributed to a client, with its REAL kernel argv.

    `/proc/<pid>/cmdline` is world-readable, unlike `/proc/<pid>/environ` (§2 I7), so
    this is the one cross-uid observation the runner can make about the other client
    without a privilege seam. That is precisely why the join target rides argv.
    """

    pid: int
    argv: Sequence[str]


@dataclass(frozen=True)
class VerifyEnvironment:
    """The injectable seam. Reads and probes only; never writes, never launches.

    `stage_postconditions` re-reads every artifact from disk and returns failures in the
        `StaticFailure` shape (#451's `assert_postconditions`).
    `read_credential_as_uid(path, uid)` opens the path WHILE ACTING AS `uid`, raising on
        failure (#452's cross-uid probe). A credential readable by the uid that wrote it
        proves nothing, so this seam is the whole content of V2.
    `read_text(path)` returns a file's text, or None when it cannot be read.
    `live_processes(client)` returns the processes currently attributable to `client`:
        an empty sequence when the client is not running, or None when the process table
        could not be enumerated at all.
    `port_is_free(host, port)` returns True/False, or None when it could not be
        determined.

    NO field carries a default (§3 P9). Returning None is how a caller says "I cannot
    establish this", and that choice is then visible at the construction site instead of
    being inherited silently and misreported as a fault on the client's machine.
    """

    stage_postconditions: Callable[[ArrangeManifest], Sequence[StaticFailure]]
    read_credential_as_uid: Callable[[str, int], None]
    read_text: Callable[[str], Optional[str]]
    live_processes: Callable[[ClientEntry], Optional[Sequence[LiveProcess]]]
    port_is_free: Callable[[str, int], Optional[bool]]


def real_verify_environment() -> VerifyEnvironment:
    """Wire the REAL reads and probes. Starts no game and mutates no arranged state."""

    def _stage_postconditions(manifest: ArrangeManifest) -> Sequence[StaticFailure]:
        # Imported here rather than at module scope to keep the import graph acyclic:
        # artifact_staging imports StaticFailure from arrange_static, which this module
        # also imports.
        from .artifact_staging import ArtifactStager

        return ArtifactStager(manifest=manifest).assert_postconditions()

    def _read_text(path: str) -> Optional[str]:
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                return fh.read()
        except OSError:
            return None

    def _live_processes(client: ClientEntry) -> Optional[Sequence[LiveProcess]]:
        """Attribute running processes to a client by their real argv[0].

        Deliberately argv-based rather than `/proc/<pid>/exe`-based: `exe` is only
        readable for same-uid processes, so an exe-based finder would report the OTHER
        client as "not running" and silently downgrade V3's evidence to the staged rung
        for the one client that most needs the live proof (§2 I7).
        """
        try:
            entries = os.listdir("/proc")
        except OSError:
            return None

        binary = client.binary_path
        basename = os.path.basename(binary)
        found: List[LiveProcess] = []
        for entry in entries:
            if not entry.isdigit():
                continue
            try:
                with open(f"/proc/{entry}/cmdline", "rb") as fh:
                    raw = fh.read()
            except OSError:
                # A process that exited between listdir and open is not an
                # enumeration failure; a genuinely unreadable table would fail the
                # listdir above.
                continue
            if not raw:
                continue
            argv = [part for part in raw.split(b"\0") if part]
            if not argv:
                continue
            argv0 = argv[0].decode("utf-8", "replace")
            if argv0 != binary and os.path.basename(argv0) != basename:
                continue
            found.append(
                LiveProcess(
                    pid=int(entry),
                    argv=tuple(part.decode("utf-8", "replace") for part in argv),
                )
            )
        return tuple(found)

    def _port_is_free(host: str, port: int) -> Optional[bool]:
        """Bind-probe the port. True = free, False = in use, None = undeterminable.

        A bind is used rather than parsing `/proc/net/tcp` because the question is
        exactly "can the client's listener bind this?", and the listener will itself
        bind. SO_REUSEADDR is deliberately NOT set: it would let the probe succeed
        against a port in TIME_WAIT that a real listener could still take, which is the
        wrong answer in the safe direction only by accident.
        """
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                probe.bind((host, port))
            return True
        except OSError as exc:
            if exc.errno in (errno.EADDRINUSE, errno.EACCES):
                return False
            return None

    return VerifyEnvironment(
        stage_postconditions=_stage_postconditions,
        read_credential_as_uid=lambda path, uid: assert_readable_as_consumer(
            actor="<verify>", path=path, consumer_uid=uid
        ),
        read_text=_read_text,
        live_processes=_live_processes,
        port_is_free=_port_is_free,
    )


# --------------------------------------------------------------------------- #
# Report shapes (§3 P7 — readiness is READ, not inferred)
# --------------------------------------------------------------------------- #

@dataclass(frozen=True)
class CriterionResult:
    """One criterion's outcome for one client, with how it was established.

    `method` and `proven_live` exist so a consumer can tell live proof from pre-launch
    evidence without re-deriving it. Flattening them into `ok` is how "arranged,
    probably" gets reported as "arranged".
    """

    criterion: str
    ok: bool
    evidence: str
    method: str = METHOD_NONE
    proven_live: bool = False

    def as_dict(self) -> Dict[str, Any]:
        return {
            "criterion": self.criterion,
            "ok": self.ok,
            "evidence": self.evidence,
            "method": self.method,
            "proven_live": self.proven_live,
        }


@dataclass(frozen=True)
class ClientReadiness:
    """One client's readiness: every criterion, plus the failures that sank it."""

    actor: str
    ready: bool
    criteria: Sequence[CriterionResult] = field(default_factory=tuple)
    failures: Sequence[StaticFailure] = field(default_factory=tuple)

    def as_dict(self) -> Dict[str, Any]:
        return {
            "client": self.actor,
            "ready": self.ready,
            "criteria": [c.as_dict() for c in self.criteria],
            "failures": [
                {
                    "precondition": f.precondition,
                    "client": f.client,
                    "detail": f.detail,
                    "expected": f.expected,
                    "actual": f.actual,
                    "remedy": f.remedy,
                }
                for f in self.failures
            ],
        }


@dataclass(frozen=True)
class ReadinessReport:
    """The whole VERIFY outcome. `ok` is True only when EVERY client is ready.

    There is deliberately no "mostly ready" and no per-client override: a partial
    arrangement is the single most expensive thing this system can do, so it is a hard
    failure that names which client is missing which thing (§3 P3).
    """

    ok: bool
    clients: Sequence[ClientReadiness] = field(default_factory=tuple)
    criteria: Sequence[str] = ALL_CRITERIA

    @property
    def failures(self) -> Sequence[StaticFailure]:
        return tuple(f for c in self.clients for f in c.failures)

    def client(self, actor: str) -> ClientReadiness:
        for entry in self.clients:
            if entry.actor == actor:
                return entry
        raise KeyError(actor)

    def as_dict(self) -> Dict[str, Any]:
        return {
            "phase": "verify",
            "ok": self.ok,
            "criteria": list(self.criteria),
            "clients": [c.as_dict() for c in self.clients],
            # Present at the top level too so a consumer that only wants "which client
            # is not ready" never has to walk the criteria.
            "not_ready": [c.actor for c in self.clients if not c.ready],
        }

    def render(self) -> str:
        if self.ok:
            head = (
                f"arrange VERIFY: READY — {len(self.criteria)} criteria over "
                f"{len(self.clients)} client(s): "
                f"{', '.join(c.actor for c in self.clients)}"
            )
            lines = [head]
            for entry in self.clients:
                for criterion in entry.criteria:
                    lines.append(
                        f"  [{criterion.criterion}] {entry.actor}: {criterion.evidence} "
                        f"(method={criterion.method}, proven_live={criterion.proven_live})"
                    )
            return "\n".join(lines)

        not_ready = [c.actor for c in self.clients if not c.ready]
        head = (
            f"arrange VERIFY: NOT READY — {len(self.failures)} failure(s); "
            f"client(s) not ready: {', '.join(not_ready) or '<none>'}"
        )
        return "\n".join([head, *(f.render() for f in self.failures)])


# --------------------------------------------------------------------------- #
# V1 — artifacts (delegated to STAGE's postconditions, #451)
# --------------------------------------------------------------------------- #

def _verify_artifacts(
    manifest: ArrangeManifest, env: VerifyEnvironment
) -> Dict[str, List[StaticFailure]]:
    """Re-read every artifact and attribute each failure to its client.

    Delegates rather than reimplements: `assert_postconditions` already re-reads from
    disk, already checks presence, bytes and ownership under distinct ids, and already
    refuses to trust what staging believed it did. A second opinion here would be a
    second thing to keep in sync with the manifest.
    """
    by_actor: Dict[str, List[StaticFailure]] = defaultdict(list)
    for failure in env.stage_postconditions(manifest):
        by_actor[failure.client].append(failure)
    return by_actor


# --------------------------------------------------------------------------- #
# V2 — credentials readable BY THEIR CONSUMER
# --------------------------------------------------------------------------- #

def _verify_credentials(
    client: ClientEntry, env: VerifyEnvironment
) -> Tuple[List[StaticFailure], str]:
    """Open every declared credential WHILE ACTING AS its declared consuming uid.

    §2 I4. The historical defect was `0600` in a `0700` directory, written by uid 1000
    and consumed by uid 1001 — two independent locks, either one sufficient, and the
    only symptom was a client waiting at a menu. The trap when *verifying* it is subtler
    and is the whole point of this criterion: a `stat`, an `access()`, or an `open()`
    performed by the ARRANGING uid proves nothing at all about the consumer. So the read
    is delegated to #452's seam, which performs it as the declared uid, and there is
    exactly one such mechanism in the tree.

    §2 I4 also records that a DANGLING reference is the same defect as an unreadable
    one — #461's live run found `SBPR_QA_SERVER_PASSWORD_FILE` pointing at a
    non-existent path on both clients. `assert_readable_as_consumer` collapses missing
    and permission-denied into one fail-closed error deliberately: either leaves the
    headless client without credentials, and the remedy names both possibilities.
    """
    failures: List[StaticFailure] = []
    checked: List[str] = []

    for name, credential in sorted(client.credentials.items()):
        try:
            env.read_credential_as_uid(credential.path, credential.consumer_uid)
        except (CredentialReadError, OSError, ValueError) as exc:
            failures.append(
                StaticFailure(
                    precondition=P_CREDENTIALS,
                    client=client.actor,
                    detail=f"credential {name!r} is not readable by its consuming uid",
                    expected=(
                        f"{credential.path} readable while acting as uid "
                        f"{credential.consumer_uid} ({client.user})"
                    ),
                    actual=f"{type(exc).__name__}: {exc}",
                    remedy=(
                        "The file is missing, or exists but is locked against the "
                        "identity that consumes it. Both leave this client without a "
                        "credential and both look identical from the outside: it "
                        "connects, vanilla takes its needPassword branch, and the "
                        "handshake waits forever on a prompt no headless client "
                        "answers. Re-run PROVISION (0711 directory, 0644 file) and "
                        "check the path actually exists."
                    ),
                )
            )
            continue
        checked.append(f"{name}@uid{credential.consumer_uid}")

    if not client.credentials:
        # Not a failure by itself — an open lane legitimately needs none, and STATIC's
        # S4 is what reconciles the declaration against `lane.requires_password`. Said
        # explicitly so the report never implies a read happened that did not.
        return failures, "no credentials declared; nothing read"

    return failures, "read as consuming uid: " + ", ".join(checked or ["<none succeeded>"])


# --------------------------------------------------------------------------- #
# V3 — join target present in the ACTUAL launch path
# --------------------------------------------------------------------------- #

def _argv_carries_connect(argv: Sequence[str], target: str) -> bool:
    """True when argv contains `+connect <target>` as adjacent arguments.

    Adjacency matters: vanilla parses `+connect` and takes the NEXT argument. A
    `+connect` whose value was appended as a separate later argument, or a bare target
    with no flag, does not populate `m_queuedJoinServer` — and both would pass a naive
    substring test while producing the original twelve-day symptom.
    """
    for index, item in enumerate(argv[:-1]):
        if item == CONNECT_FLAG and argv[index + 1] == target:
            return True
    return False


def _verify_join_live(
    client: ClientEntry,
    target: str,
    processes: Sequence[LiveProcess],
) -> Tuple[List[StaticFailure], Optional[CriterionResult]]:
    """Assert EVERY live process attributed to this client carries the fragment.

    Every, not any: a second client process running without the join target is exactly
    the failure being verified against, and picking the process that agrees with us
    would be the report telling itself what it wants to hear.
    """
    failures: List[StaticFailure] = []
    for process in processes:
        if _argv_carries_connect(process.argv, target):
            continue
        failures.append(
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail=(
                    f"live process pid {process.pid} carries no {CONNECT_FLAG} "
                    "for this run's lane"
                ),
                expected=f"real argv containing `{CONNECT_FLAG} {target}`",
                actual=f"argv: {' '.join(process.argv)}",
                remedy=(
                    "This process will boot, load every mod, and park at the server "
                    "list with nothing logged — the original blocker. If the launcher "
                    "passes through Steam's %command% wrapper, check the fragment is "
                    'APPENDED after "$@": run_bepinex.sh rotates argv, so a prepended '
                    "fragment is swallowed by Steam's wrapper chain."
                ),
            )
        )
    if failures:
        return failures, None
    pids = ", ".join(str(p.pid) for p in processes)
    return [], CriterionResult(
        criterion=P_JOIN_PATH,
        ok=True,
        evidence=(
            f"`{CONNECT_FLAG} {target}` present in the real kernel argv of live "
            f"pid(s) {pids}"
        ),
        method=METHOD_LIVE_ARGV,
        proven_live=True,
    )


def _verify_join_staged(
    client: ClientEntry,
    target: str,
    env: VerifyEnvironment,
) -> Tuple[List[StaticFailure], Optional[CriterionResult]]:
    """Pre-launch evidence: the sidecar ON DISK plus the wrapper that consumes it.

    VERIFY precedes LAUNCH in the phase ordering, so for the normal run there is no
    process to read. What CAN be read back is the state arrange actually wrote: the
    launch-env sidecar at the exact path this client's wrapper resolves, carrying
    `SBPR_QA_CONNECT=host:port`; and the wrapper itself, which must turn that value into
    a `+connect` argv fragment (#453's `inspect_wrapper`, reused rather than re-parsed).

    Both halves are required, because either alone is the twelve-day failure: a sidecar
    no wrapper reads is discarded silently, and a wrapper with nothing to read delivers
    an empty fragment just as silently.

    This is honest evidence, and it is NOT the live claim. The result records
    `proven_live=False` so no consumer can mistake one for the other.
    """
    launch_env_path = client.launcher.params.get("launch_env_path")
    if not launch_env_path:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="join delivery cannot be verified: no launch-env path declared",
                expected=(
                    "either a live process whose argv can be read, or a "
                    "`launch_env_path` naming the sidecar this client's wrapper reads"
                ),
                actual=(
                    f"no process attributable to {client.binary_path}, and "
                    f"launcher.launch_env_path is absent"
                ),
                remedy=(
                    "With no process and no declared sidecar there is nothing to read "
                    "back, and 'probably arranged' is the state this phase exists to "
                    "abolish. Declare the launch-env path the wrapper resolves, or run "
                    "VERIFY against a launched client."
                ),
            )
        ], None
    launch_env_path = str(launch_env_path)

    sidecar = env.read_text(launch_env_path)
    if sidecar is None:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="launch-env sidecar is missing or unreadable",
                expected=f"a readable sidecar at {launch_env_path} carrying {CONNECT_VAR}",
                actual="no such file, or read failed",
                remedy=(
                    "PROVISION writes this file; the wrapper sources it just before "
                    "exec because the daemon forks the client with the DAEMON's "
                    "environment, not the runner's. Absent, the client receives no join "
                    "target and waits at a menu forever with nothing logged."
                ),
            )
        ], None

    expected_line = f"{CONNECT_VAR}={target}"
    declared = [
        line.strip()
        for line in sidecar.splitlines()
        if line.strip().startswith(f"{CONNECT_VAR}=")
    ]
    if expected_line not in declared:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail=f"sidecar does not carry this run's lane in {CONNECT_VAR}",
                expected=f"a line `{expected_line}` in {launch_env_path}",
                actual=(
                    f"{CONNECT_VAR} lines present: {declared}"
                    if declared
                    else f"no {CONNECT_VAR} line at all"
                ),
                remedy=(
                    "A stale sidecar from a previous run points the client at a lane "
                    "this run never brings up: it connects to nothing, produces no "
                    "receipts, and reports no error. Re-run PROVISION for this run."
                ),
            )
        ], None

    wrapper_path = client.launcher.params.get("wrapper_path")
    if not wrapper_path:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="sidecar carries the target but no wrapper is declared to consume it",
                expected=(
                    "a `wrapper_path` naming the script that turns "
                    f"{CONNECT_VAR} into a `{CONNECT_FLAG}` argv fragment"
                ),
                actual="launcher.wrapper_path is absent",
                remedy=(
                    "An env var is not a join target. Vanilla populates "
                    "`m_queuedJoinServer` from the `+connect` ARGUMENT; the wrapper is "
                    "the only seam that can build it across the fork. Without one "
                    "named, the sidecar is evidence of nothing."
                ),
            )
        ], None
    wrapper_path = str(wrapper_path)

    wrapper_text = env.read_text(wrapper_path)
    if wrapper_text is None:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="declared launch wrapper is missing or unreadable",
                expected=f"a readable wrapper script at {wrapper_path}",
                actual="no such file, or read failed",
                remedy=(
                    "The wrapper is the only seam that can deliver the join target "
                    "across the daemon fork. If it cannot be read, delivery is "
                    "unproven and the run must not spend a ten-minute boot finding out."
                ),
            )
        ], None

    seam = inspect_wrapper(wrapper_text)
    if not (seam.sources_sidecar and seam.builds_connect_args):
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="wrapper does not build a `+connect` fragment from the sidecar",
                expected=(
                    f"{wrapper_path} to source the sidecar and turn {CONNECT_VAR} into "
                    f"a `{CONNECT_FLAG} host:port` fragment"
                ),
                actual=(
                    f"sources_sidecar={seam.sources_sidecar}, "
                    f"builds_connect_args={seam.builds_connect_args}; "
                    f"exec line: {seam.exec_line or '<none found>'}"
                ),
                remedy=(
                    "STATIC's S8 asserts the same seam before launch; if that passed "
                    "and this failed, the wrapper changed underneath the run. Restore "
                    "the fragment construction."
                ),
            )
        ], None

    return [], CriterionResult(
        criterion=P_JOIN_PATH,
        ok=True,
        evidence=(
            f"`{expected_line}` read back from {launch_env_path}; wrapper "
            f"{wrapper_path} builds the `{CONNECT_FLAG}` fragment from it"
        ),
        method=METHOD_STAGED_DELIVERY,
        proven_live=False,
    )


def _verify_join(
    client: ClientEntry, env: VerifyEnvironment
) -> Tuple[List[StaticFailure], CriterionResult]:
    """Establish that the join target is in THIS client's actual launch path.

    Live argv when there is a process to read; the staged delivery chain otherwise.
    An unenumerable process table is a hard failure rather than a silent downgrade to
    the weaker rung: "I could not look" and "I looked and it was fine" must never
    produce the same report line.
    """
    if client.join is None:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="no join target declared",
                expected="a declared join target to verify against",
                actual="client.join is absent",
                remedy=(
                    "STATIC's S8 refuses this before anything boots. Reaching VERIFY "
                    "with no target means the manifest changed after STATIC ran."
                ),
            )
        ], CriterionResult(
            criterion=P_JOIN_PATH, ok=False, evidence="no join target declared"
        )

    target = f"{client.join.host}:{client.join.port}"
    processes = env.live_processes(client)

    if processes is None:
        return [
            StaticFailure(
                precondition=P_JOIN_PATH,
                client=client.actor,
                detail="the process table could not be enumerated",
                expected=(
                    "an enumerable process list, so a live client's real argv can be "
                    "read (or its absence established)"
                ),
                actual="enumeration failed",
                remedy=(
                    "Neither rung of evidence is available: a live client cannot be "
                    "found and its absence cannot be established either, so the staged "
                    "fallback would be asserting delivery for a process that may "
                    "already be running without it. Fix the enumeration seam; do not "
                    "downgrade the claim."
                ),
            )
        ], CriterionResult(
            criterion=P_JOIN_PATH,
            ok=False,
            evidence="process table unenumerable; neither live nor staged evidence valid",
        )

    if processes:
        failures, result = _verify_join_live(client, target, processes)
    else:
        failures, result = _verify_join_staged(client, target, env)

    if result is not None:
        return failures, result
    return failures, CriterionResult(
        criterion=P_JOIN_PATH,
        ok=False,
        evidence=failures[0].detail if failures else "join delivery unestablished",
    )


# --------------------------------------------------------------------------- #
# V4 — ports disjoint AND free
# --------------------------------------------------------------------------- #

def _collect_port_collisions(manifest: ArrangeManifest) -> Dict[int, List[Tuple[str, str]]]:
    owners: Dict[int, List[Tuple[str, str]]] = defaultdict(list)
    for client in manifest.clients:
        for name, port in sorted(client.bound_ports.items()):
            owners[port].append((client.actor, name))
    return {port: claims for port, claims in owners.items() if len(claims) > 1}


def _verify_ports(
    client: ClientEntry,
    collisions: Dict[int, List[Tuple[str, str]]],
    env: VerifyEnvironment,
) -> Tuple[List[StaticFailure], CriterionResult]:
    """Disjoint is re-asserted; FREE is the half only VERIFY can establish.

    §2 I6, confirmed under real concurrency by #461's live run: client_b lost both its
    UnityScriptHost and ValBridgeServer binds while client_a held the ports. It did not
    block the join — the harness rides its own path — but client_b had no GABP bridge,
    so `games_connect` could not drive it, and T022 needs BOTH clients drivable.

    Disjointness is a manifest fact STATIC's S5 already checks; it is re-asserted here
    because VERIFY must not assume STATIC ran against the same manifest it was handed.
    Being FREE is a property of the machine at this instant, and no static check can
    reach it: a port can be disjoint in the manifest and still held by a stale client, a
    developer's own session, or an unrelated service.

    An undeterminable port is a failure, not a pass. VERIFY precedes LAUNCH, so "free"
    is the correct expectation for every declared listener.
    """
    failures: List[StaticFailure] = []
    host = client.join.host if client.join is not None else "127.0.0.1"
    probed: List[str] = []

    for name, port in sorted(client.bound_ports.items()):
        claims = collisions.get(port)
        if claims:
            others = ", ".join(f"{a}.{n}" for a, n in claims if a != client.actor)
            failures.append(
                StaticFailure(
                    precondition=P_PORTS,
                    client=client.actor,
                    detail=f"port {port} ({name}) is claimed by more than one client",
                    expected=f"{client.actor}.{name} to own port {port} exclusively",
                    actual=f"also claimed by {others or '<same client, twice>'}",
                    remedy=(
                        "The second binder fails and the client loses that service "
                        "silently. Under concurrency that costs the GABP bridge, so "
                        "the client cannot be driven at all."
                    ),
                )
            )
            continue

        free = env.port_is_free(host, port)
        if free is None:
            failures.append(
                StaticFailure(
                    precondition=P_PORTS,
                    client=client.actor,
                    detail=f"port {port} ({name}) availability could not be determined",
                    expected=f"a conclusive free/in-use answer for {host}:{port}",
                    actual="probe returned no answer",
                    remedy=(
                        "An undeterminable port is not a free one. Fix the probe seam "
                        "rather than proceeding: a client that cannot bind loses the "
                        "service with no error, which is ten minutes to rediscover."
                    ),
                )
            )
            continue
        if not free:
            failures.append(
                StaticFailure(
                    precondition=P_PORTS,
                    client=client.actor,
                    detail=f"port {port} ({name}) is already in use",
                    expected=f"{host}:{port} free before launch",
                    actual="the bind probe was refused; something already holds it",
                    remedy=(
                        "Sweep prior-run clients (#455) or move the listener. A stale "
                        "holder produces `Failed to bind ...: Address already in use` "
                        "in the client log and nothing anywhere else."
                    ),
                )
            )
            continue
        probed.append(f"{name}={port}")

    if failures:
        return failures, CriterionResult(
            criterion=P_PORTS,
            ok=False,
            evidence=f"{len(failures)} port problem(s) on {client.actor}",
        )
    return [], CriterionResult(
        criterion=P_PORTS,
        ok=True,
        evidence=(
            f"disjoint from every sibling and free at {host}: "
            + (", ".join(probed) if probed else "no listeners declared")
        ),
        method="bind-probe",
        proven_live=True,
    )


# --------------------------------------------------------------------------- #
# Entrypoint
# --------------------------------------------------------------------------- #

def arrange_verify(
    manifest: ArrangeManifest,
    env: VerifyEnvironment,
) -> ReadinessReport:
    """Read back everything arrange established and emit the readiness report.

    `env` is REQUIRED and has no default, for the same reason no field on
    `VerifyEnvironment` does: this phase's entire value is the capabilities it was
    wired with, and a caller that silently inherited a real environment would be
    probing and reading on a machine the caller never decided to touch.

    Nothing here short-circuits and nothing compares one client against another except
    to assert disjointness. Adding a third client is manifest data.
    """
    artifact_failures = _verify_artifacts(manifest, env)
    collisions = _collect_port_collisions(manifest)

    clients: List[ClientReadiness] = []
    for client in manifest.clients:
        failures: List[StaticFailure] = []
        criteria: List[CriterionResult] = []

        staged = list(artifact_failures.get(client.actor, ()))
        failures.extend(staged)
        required = len(client.artifacts)
        criteria.append(
            CriterionResult(
                criterion=P_ARTIFACTS,
                ok=not staged,
                evidence=(
                    f"{required} required artifact(s) re-read from disk and matched "
                    "their manifest pins"
                    if not staged
                    else f"{len(staged)} artifact failure(s) over {required} requirement(s)"
                ),
                method="disk-reread" if not staged else METHOD_NONE,
                proven_live=not staged,
            )
        )

        credential_failures, credential_evidence = _verify_credentials(client, env)
        failures.extend(credential_failures)
        # A client with no declared credentials has nothing to prove, and saying
        # `proven_live=True` there would claim a read that never happened — the exact
        # over-claim this phase's evidence fields exist to prevent. The criterion still
        # passes (S4 is what reconciles an absent credential against the lane policy);
        # it simply does not pretend to be backed by an observation.
        nothing_to_read = not client.credentials
        criteria.append(
            CriterionResult(
                criterion=P_CREDENTIALS,
                ok=not credential_failures,
                evidence=credential_evidence
                if not credential_failures
                else f"{len(credential_failures)} credential(s) unreadable by their consumer",
                method=(
                    METHOD_NONE
                    if credential_failures or nothing_to_read
                    else "read-as-consuming-uid"
                ),
                proven_live=not credential_failures and not nothing_to_read,
            )
        )

        join_failures, join_result = _verify_join(client, env)
        failures.extend(join_failures)
        criteria.append(join_result)

        port_failures, port_result = _verify_ports(client, collisions, env)
        failures.extend(port_failures)
        criteria.append(port_result)

        clients.append(
            ClientReadiness(
                actor=client.actor,
                # READY requires every criterion to have PASSED, not merely to have not
                # failed: a criterion that could not be established leaves `ok` False
                # and therefore blocks readiness, so "unverified" can never read as
                # "arranged".
                ready=all(c.ok for c in criteria) and not failures,
                criteria=tuple(criteria),
                failures=tuple(failures),
            )
        )

    return ReadinessReport(
        ok=bool(clients) and all(c.ready for c in clients),
        clients=tuple(clients),
        criteria=ALL_CRITERIA,
    )
