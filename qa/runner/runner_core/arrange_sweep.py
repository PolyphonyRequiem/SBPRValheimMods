"""SWEEP — reconcile prior-run residue to declared-absent (T022 ARRANGE §4, #455).

WHAT THIS EXISTS TO PREVENT
---------------------------
Teardown runs only on the runner's graceful exit paths (`live_composition.py:272-289`).
Every time a run was SIGKILLed to stop it burning boot attempts, it left **live
credentials on disk** — observed with expiry ~113 minutes in the future — plus launch
sidecars nothing would ever remove and, before the GABS fix, dead clients.

WHAT THIS PHASE HONESTLY GUARANTEES (and what it does not)
----------------------------------------------------------
The issue originally asked for "credentials cannot outlive the run that minted them,
even when that run is SIGKILLed". That is not achievable in-process and claiming it
would be exactly the "logs green ≠ playable" dishonesty `AGENTS.md` prohibits: SIGKILL
runs no handler, no `atexit`, no `finally`. Three separate mechanisms were being
blurred into one promise. Named separately:

  1. graceful teardown — exists, covers the exit paths the runner actually reaches.
  2. **next-entry sweep — THIS phase.** No credential from a prior run survives into
     the next `arrange`. It does NOT bound the residue window *between* runs.
  3. TTL — exists for bootstrap docs (`wire_mint.py`), enforced at the C# arm gate, so
     an orphaned doc is cryptographically inert past its expiry even unswept.

**The gap this phase does not close:** lane-password files carry no TTL at all. Their
validity ends only with lane teardown. That is the real "~113 minutes" exposure, and
bounding it needs a shorter disposable-lane lifetime — #457's scope, not smuggled in
here. Sweep therefore removes a lane password because its run is OVER, never because
it "expired", and the report says which.

CONVERGENT, NOT REMEMBERED
--------------------------
Sweep is a pure function of the manifest: *reconcile every declared path to absent*.
It is deliberately NOT "delete what I remember writing" — in-process tracking is
precisely what fails when the process that did the remembering was killed. Every
action is "remove if present and provably ours, else record why not"; nothing is ever
"remove, and error if missing". So running sweep over an already-swept tree yields
`ok=True` with every action `already-absent`, and an `as_dict()` identical to the run
before it. That is the idempotency AC, and it is checkable rather than asserted.

Sweep never WRITES a file. It unlinks and it signals; those are the only two mutations
it can perform.

FAIL CLOSED, ALWAYS TOWARD LEAVING THINGS ALONE
-----------------------------------------------
Every ambiguity resolves to "do not touch it, and say so". An unparseable provenance,
a foreign owner uid, a symlink, an unreadable `/proc` — each is `left-alone` or
`refused` with `ok=False`, never a silent skip and never an optimistic delete. `ok`
answers "did the tree reach the state the manifest declares", so a refusal is a
failure of the phase even though it is the correct action.

B1 — THE PROCESS RULE
---------------------
A process is signalled ONLY when its own environment carries this run's harness
marker. There is deliberately **no fallback heuristic**: no cmdline match, no cwd
match, no `pkill -f`, no game-root prefix. All of those match Daniel's own Steam
Valheim, which runs the same binary from a similar root. An unreadable
`/proc/<pid>/environ` is NOT proof of ownership — it fails closed to `left-alone`.
Zombies are report-only: a `<defunct>` child cannot be killed, only reaped by its
parent, and GABS's reaping was fixed upstream (`AGENTS.md`). Sweep issues no GABS call
and does not touch `_reset_gabs_state`.

Engine-free: stdlib only, no product/game import. Every environment contact goes
through the injected seam, so importing or unit-testing this module reads nothing,
signals nothing and spawns nothing.
"""
from __future__ import annotations

import os
import signal as _signal
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Optional, Sequence

from .arrange_manifest import ArrangeManifest, ClientEntry
from .credential_provenance import (
    CredentialProvenance,
    parse_provenance,
    provenance_path,
)
from .proc_provenance import VALHEIM_EXE_BASENAME, marker_run_id

# Stable precondition ids, reported verbatim and grepped by operators — the same
# contract as the STATIC S-ids, the STAGE T-ids and the VERIFY V-ids.
P_CREDENTIALS = "W1-PRIOR-RUN-CREDENTIALS-CLEARED"
P_PROVENANCE = "W2-PROVENANCE-SIDECARS-CLEARED"
P_PROCESSES = "W3-HARNESS-OWNED-CLIENTS-CLEARED"

ALL_CRITERIA = (P_CREDENTIALS, P_PROVENANCE, P_PROCESSES)

# What a single reconciliation actually did. Four outcomes, and the distinction
# between the last two is the whole safety story: `left-alone` means we could not
# prove ownership, `refused` means we proved something is wrong.
OUTCOME_REMOVED = "removed"
OUTCOME_ALREADY_ABSENT = "already-absent"
OUTCOME_LEFT_ALONE = "left-alone"
OUTCOME_REFUSED = "refused"

# Outcomes that mean the tree did NOT reach declared-absent.
_NOT_CONVERGED = frozenset({OUTCOME_LEFT_ALONE, OUTCOME_REFUSED})

RESOURCE_CREDENTIAL = "credential"
RESOURCE_PROVENANCE = "provenance"
RESOURCE_PROCESS = "process"


@dataclass(frozen=True)
class SweepAction:
    """One reconciliation, recorded whether or not anything changed.

    Recorded even for `already-absent` on purpose: the report is the evidence that
    the phase LOOKED at a path, and a report that only lists changes cannot be
    compared run-to-run to demonstrate convergence.
    """

    precondition: str
    client: str
    resource: str
    target: str
    outcome: str
    reason: str

    def render(self) -> str:
        return (
            f"  [{self.precondition}] client={self.client} {self.resource} "
            f"{self.target}\n    outcome:  {self.outcome}\n    reason:   {self.reason}"
        )

    def as_dict(self) -> Dict[str, Any]:
        return {
            "precondition": self.precondition,
            "client": self.client,
            "resource": self.resource,
            "target": self.target,
            "outcome": self.outcome,
            "reason": self.reason,
        }


@dataclass(frozen=True)
class SweepReport:
    """The outcome of the sweep phase. Shape mirrors `StaticReport`/`VerifyReport`.

    Deliberately uniform so #456's readiness report and #457's cutover can absorb it
    as one more section instead of learning a fourth shape.
    """

    ok: bool
    actions: Sequence[SweepAction] = field(default_factory=tuple)
    swept_clients: Sequence[str] = field(default_factory=tuple)
    checked_preconditions: Sequence[str] = field(default_factory=tuple)

    @property
    def unresolved(self) -> Sequence[SweepAction]:
        """Actions that left the tree short of declared-absent."""
        return tuple(a for a in self.actions if a.outcome in _NOT_CONVERGED)

    def render(self) -> str:
        removed = sum(1 for a in self.actions if a.outcome == OUTCOME_REMOVED)
        if self.ok:
            return (
                f"arrange SWEEP: PASS — {removed} item(s) removed, "
                f"{len(self.actions)} reconciliation(s) over "
                f"{len(self.swept_clients)} client(s): {', '.join(self.swept_clients)}"
            )
        unresolved = self.unresolved
        head = (
            f"arrange SWEEP: FAIL — {len(unresolved)} item(s) could not be reconciled "
            f"to declared-absent over client(s) {', '.join(self.swept_clients)} "
            f"({removed} removed)"
        )
        return "\n".join([head, *(a.render() for a in unresolved)])

    def as_dict(self) -> Dict[str, Any]:
        """Machine-readable form. Byte-identical across converged consecutive runs."""
        return {
            "phase": "sweep",
            "ok": self.ok,
            "clients": list(self.swept_clients),
            "preconditions": list(self.checked_preconditions),
            "actions": [a.as_dict() for a in self.actions],
        }


@dataclass(frozen=True)
class SweepEnvironment:
    """The injectable seam. Unlinks and signals; NEVER writes a file.

    `read_text(path)` returns a file's text, or None when it cannot be read.
    `lstat_owner(path)` returns the owning uid WITHOUT following symlinks, or None
        when the path does not exist / cannot be stat'ed.
    `is_symlink(path)` answers whether the path itself is a symlink.
    `unlink(path, as_uid)` removes `path`, ACTING AS `as_uid`, raising OSError on
        failure. The identity is a per-call argument rather than baked in at
        construction because a single sweep spans BOTH identities: uid 1000 cannot
        remove a uid-1001-owned credential from valbot's 0711 home directory (unlink
        is governed by DIRECTORY write permission, which a permissive file mode cannot
        rescue), and uid 1001 has no business removing the runner's own files. #451's
        `real_staging_filesystem(as_uid=...)` supplies the mechanism.
    `list_pids()` returns every PID, or None when the process table could not be
        enumerated at all — which is NOT the same as "no processes".
    `pid_exe(pid)` returns the resolved executable path, or None (gone/zombie/EACCES).
    `pid_marker(pid)` returns the harness instance marker from the process's own
        environment, or None. None means NOT PROVABLY OURS, for both reasons it can
        happen; see `proc_provenance`.
    `pid_start_ticks(pid)` returns `/proc/<pid>/stat` field 22, the anti-PID-reuse pin.
    `signal_pid(pid, sig)` sends `sig`, raising OSError on failure.
    `wait_gone(pid)` blocks briefly for the process to exit, returning True if it did.
    `now_unix_ms()` returns the current wall clock; injected so expiry tests need no
        real clock and cannot flake.

    NO field carries a default (§3 P9). This is the contract #454 established and
    #452/#453, #467 and #473 each re-lost. A defaulted seam fails closed, so it is
    never a security bypass — it is worse in a subtler way: an omitted wiring then
    surfaces as "process table unenumerable" attributed to the CLIENT'S machine,
    emitting the same line a genuine fault would and sending an operator to inspect a
    box that is fine. A caller that cannot enumerate processes says so by passing a
    function returning None, recording that decision at the construction site. The
    contract is enforced structurally (a `dataclasses.fields` assertion plus an AST
    scan of every construction site in the repository), not per-seam, so a future
    merge cannot quietly re-default a seam every current test happens to supply.
    """

    read_text: Callable[[str], Optional[str]]
    lstat_owner: Callable[[str], Optional[int]]
    is_symlink: Callable[[str], bool]
    unlink: Callable[[str, int], None]
    list_pids: Callable[[], Optional[Sequence[int]]]
    pid_exe: Callable[[int], Optional[str]]
    pid_marker: Callable[[int], Optional[str]]
    pid_start_ticks: Callable[[int], Optional[int]]
    signal_pid: Callable[[int, int], None]
    wait_gone: Callable[[int], bool]
    now_unix_ms: Callable[[], int]


def real_sweep_environment() -> SweepEnvironment:
    """Wire the REAL reads, unlinks and signals.

    Mutations are performed as the identity named per call, through #451's
    `real_staging_filesystem(as_uid=...)`, so a credential staged into another
    identity's tree is removed BY that identity. Reads stay in-process: the runner can
    already read both trees, and a read that fails is reported rather than retried as
    somebody else.
    """
    import time as _time

    from .artifact_staging import real_staging_filesystem
    from . import proc_provenance

    fs = real_staging_filesystem()

    def _read_text(path: str) -> Optional[str]:
        try:
            with open(path, "r", encoding="utf-8", errors="replace") as fh:
                return fh.read()
        except OSError:
            return None

    def _lstat_owner(path: str) -> Optional[int]:
        owner_mode = fs.stat_owner_mode(path)
        return None if owner_mode is None else owner_mode[0]

    def _unlink(path: str, as_uid: int) -> None:
        # A fresh filesystem per identity. Cheap (it only closes over `as_uid`) and it
        # keeps the identity an explicit argument at the call site rather than state
        # captured once at construction, which is what made the cross-uid path
        # unreachable in the first place.
        real_staging_filesystem(as_uid=as_uid).unlink(path)

    def _wait_gone(pid: int) -> bool:
        # Up to ~15s, matching the graceful-teardown budget in live_composition.
        for _ in range(30):
            if proc_provenance.read_start_ticks(pid) is None:
                return True
            _time.sleep(0.5)
        return proc_provenance.read_start_ticks(pid) is None

    def _signal_pid(pid: int, sig: int) -> None:
        os.kill(pid, sig)

    return SweepEnvironment(
        read_text=_read_text,
        lstat_owner=_lstat_owner,
        is_symlink=fs.is_symlink,
        unlink=_unlink,
        list_pids=proc_provenance.list_pids,
        pid_exe=proc_provenance.read_exe,
        pid_marker=proc_provenance.read_marker,
        pid_start_ticks=proc_provenance.read_start_ticks,
        signal_pid=_signal_pid,
        wait_gone=_wait_gone,
        now_unix_ms=lambda: int(_time.time() * 1000),
    )


def _decide_credential(
    *,
    provenance: Optional[CredentialProvenance],
    owner_uid: Optional[int],
    arranging_uid: int,
    consumer_uid: int,
    run_id: str,
    now_unix_ms: int,
) -> "tuple[bool, str]":
    """The C2 fail-closed decision table. Returns (may_remove, reason).

    Kept as a pure function of already-gathered facts so the table can be read (and
    tested) as a table, rather than being spread through the I/O loop where an added
    branch could quietly change a `left-alone` into a delete.

    **Two owners are legitimate, not one.** A credential is written either by the
    arranging runner in-process, or INTO the consuming identity's tree as that
    identity through #451's `as_uid` staging — because uid 1000 writing into
    /home/valbot and chowning the result needs root and dissolves the Steam identity
    isolation the dual-user rig depends on. So a uid-1001-owned credential at a path
    the manifest declares with `consumer_uid: 1001` is *ours*, and a check that
    admitted only the arranging uid would refuse every cross-uid credential #455 exists
    to sweep — silently, while reporting a tidy `left-alone`. That defect was invisible
    to every stub test and was caught only by running the phase against a real
    valbot-owned tree. Any uid outside that declared pair is genuinely foreign.
    """
    allowed_owners = {arranging_uid, consumer_uid}
    if owner_uid is not None and owner_uid not in allowed_owners:
        return (
            False,
            f"owned by uid {owner_uid}, which is neither the arranging uid "
            f"{arranging_uid} nor this credential's declared consuming uid "
            f"{consumer_uid}; a file the harness could not have written is not "
            "this run's to remove",
        )
    if provenance is None:
        return (
            False,
            "no readable/parseable ownership provenance sidecar; without it this file "
            "is indistinguishable from one an operator placed deliberately",
        )
    if provenance.run_id == run_id:
        return (
            True,
            f"provenance names THIS run {run_id!r}; removing it makes a repeated "
            "arrange converge instead of colliding with its own residue",
        )
    if provenance.is_expired(now_unix_ms):
        return (
            True,
            f"provenance names prior run {provenance.run_id!r} and expired at "
            f"{provenance.expiry_unix_ms} (now {now_unix_ms})",
        )
    return (
        False,
        f"provenance names a DIFFERENT run {provenance.run_id!r} that has not expired "
        "(no TTL, or still in future); a concurrent run's credential is the lane "
        "lease's business, not the sweeper's",
    )


class _Sweeper:
    """One sweep pass. Holds only the actions accumulated so far."""

    def __init__(
        self, manifest: ArrangeManifest, env: SweepEnvironment, *, arranging_uid: int
    ) -> None:
        self._manifest = manifest
        self._env = env
        self._arranging_uid = arranging_uid
        self._now = env.now_unix_ms()
        self._actions: List[SweepAction] = []

    def _record(
        self, precondition: str, client: str, resource: str, target: str, outcome: str, reason: str
    ) -> None:
        self._actions.append(
            SweepAction(
                precondition=precondition,
                client=client,
                resource=resource,
                target=target,
                outcome=outcome,
                reason=reason,
            )
        )

    # --- credentials + their provenance sidecars ---------------------------------

    def _remove_path(
        self, *, precondition: str, client: str, resource: str, path: str,
        as_uid: int, reason: str
    ) -> bool:
        """Unlink `path` AS `as_uid`, recording the outcome.

        Returns True when the path is now gone. `as_uid` is the file's own owner, so a
        credential staged into another identity's tree is removed by that identity —
        uid 1000 cannot unlink from valbot's 0711 directory at all.
        """
        try:
            self._env.unlink(path, as_uid)
        except FileNotFoundError:
            # Lost a race with something else removing it. The declared end state
            # holds, so this is convergence, not a failure.
            self._record(
                precondition, client, resource, path, OUTCOME_ALREADY_ABSENT,
                "vanished between the decision and the unlink; declared-absent holds",
            )
            return True
        except OSError as exc:
            errno_part = f" errno={exc.errno}" if getattr(exc, "errno", None) else ""
            self._record(
                precondition, client, resource, path, OUTCOME_REFUSED,
                f"unlink failed:{errno_part} {type(exc).__name__}: {exc}",
            )
            return False
        self._record(precondition, client, resource, path, OUTCOME_REMOVED, reason)
        return True

    def _sweep_credential(
        self, client: ClientEntry, path: str, consumer_uid: int
    ) -> None:
        env = self._env
        prov_path = provenance_path(path)

        # A symlink is refused BEFORE anything else and is never followed: unlinking
        # a symlink removes the link, but any check performed through it described a
        # file somewhere else entirely. A credential path that has become a symlink
        # is a fact a human needs to see, not one to clean up silently.
        if env.is_symlink(path):
            self._record(
                P_CREDENTIALS, client.actor, RESOURCE_CREDENTIAL, path, OUTCOME_REFUSED,
                "declared credential path is a SYMLINK; refusing to follow or remove it "
                "— its target is outside anything this manifest declares",
            )
            return

        owner_uid = env.lstat_owner(path)
        if owner_uid is None:
            # Nothing to reconcile. The sidecar may still exist independently, so it
            # is swept below rather than assumed gone with its credential.
            self._record(
                P_CREDENTIALS, client.actor, RESOURCE_CREDENTIAL, path,
                OUTCOME_ALREADY_ABSENT, "no file at the declared credential path",
            )
            self._sweep_provenance(
                client, prov_path, consumer_uid=consumer_uid, credential_removed=True
            )
            return

        provenance = parse_provenance(env.read_text(prov_path))
        may_remove, reason = _decide_credential(
            provenance=provenance,
            owner_uid=owner_uid,
            arranging_uid=self._arranging_uid,
            consumer_uid=consumer_uid,
            run_id=self._manifest.run_id,
            now_unix_ms=self._now,
        )
        if not may_remove:
            self._record(
                P_CREDENTIALS, client.actor, RESOURCE_CREDENTIAL, path,
                OUTCOME_LEFT_ALONE, reason,
            )
            # The sidecar describes a credential we are NOT removing, so it stays too:
            # deleting the provenance of a file we left behind would destroy the only
            # evidence a later sweep could use.
            self._record(
                P_PROVENANCE, client.actor, RESOURCE_PROVENANCE, prov_path,
                OUTCOME_LEFT_ALONE,
                "left with the credential it describes; removing it would destroy the "
                "only ownership evidence a later sweep could use",
            )
            return

        removed = self._remove_path(
            precondition=P_CREDENTIALS,
            client=client.actor,
            resource=RESOURCE_CREDENTIAL,
            path=path,
            # Act as the file's OWN owner. Using the arranging uid here would fail on
            # every credential staged into the consuming identity's tree.
            as_uid=owner_uid,
            reason=reason,
        )
        self._sweep_provenance(
            client, prov_path, consumer_uid=consumer_uid, credential_removed=removed
        )

    def _sweep_provenance(
        self,
        client: ClientEntry,
        prov_path: str,
        *,
        consumer_uid: int,
        credential_removed: bool,
    ) -> None:
        env = self._env
        if env.is_symlink(prov_path):
            self._record(
                P_PROVENANCE, client.actor, RESOURCE_PROVENANCE, prov_path, OUTCOME_REFUSED,
                "provenance sidecar path is a SYMLINK; refusing to follow or remove it",
            )
            return
        prov_owner = env.lstat_owner(prov_path)
        if prov_owner is None:
            self._record(
                P_PROVENANCE, client.actor, RESOURCE_PROVENANCE, prov_path,
                OUTCOME_ALREADY_ABSENT, "no provenance sidecar at the declared path",
            )
            return
        if prov_owner not in (self._arranging_uid, consumer_uid):
            # Same two-owner rule as the credential. A sidecar owned by a third
            # identity is not ours, even when the credential beside it was.
            self._record(
                P_PROVENANCE, client.actor, RESOURCE_PROVENANCE, prov_path,
                OUTCOME_LEFT_ALONE,
                f"owned by uid {prov_owner}, which is neither the arranging uid "
                f"{self._arranging_uid} nor the declared consuming uid {consumer_uid}",
            )
            return
        if not credential_removed:
            self._record(
                P_PROVENANCE, client.actor, RESOURCE_PROVENANCE, prov_path,
                OUTCOME_LEFT_ALONE,
                "the credential it describes could not be removed; the sidecar stays "
                "so the next sweep can still establish ownership",
            )
            return
        self._remove_path(
            precondition=P_PROVENANCE,
            client=client.actor,
            resource=RESOURCE_PROVENANCE,
            path=prov_path,
            as_uid=prov_owner,
            reason="ownership provenance for a credential this sweep removed",
        )

    def sweep_credentials(self) -> None:
        for client in self._manifest.clients:
            for credential in client.credentials.values():
                # `consumer_uid` is per CREDENTIAL, not per client: the manifest
                # declares who must be able to read each one, and that is precisely
                # the second identity legitimately allowed to own it.
                self._sweep_credential(
                    client, credential.path, credential.consumer_uid
                )

    # --- processes (B1) -----------------------------------------------------------

    def sweep_processes(self) -> None:
        """Terminate ONLY processes proven to carry this run's harness marker.

        Attributed to the manifest as a whole rather than to a client: a process's
        provenance is its marker, and the marker names the RUN. Attributing a kill to
        a client would be inventing an attribution the evidence does not support.
        """
        env = self._env
        run_id = self._manifest.run_id
        pids = env.list_pids()
        if pids is None:
            # Degrade to a credential-only sweep and SAY SO. Silently reporting a
            # clean process sweep here would be the exact class of false green this
            # whole phase exists to end.
            self._record(
                P_PROCESSES, "-", RESOURCE_PROCESS, "-", OUTCOME_REFUSED,
                "the process table could not be enumerated; harness-owned client "
                "residue is UNKNOWN, not absent — credentials were still swept",
            )
            return

        considered = 0
        for pid in pids:
            exe = env.pid_exe(pid)
            if exe is None:
                # No readable exe link: a zombie, a vanished PID, or one this uid may
                # not inspect. All three are report-nothing/do-nothing. A zombie in
                # particular is not killable — only its parent can reap it, and GABS's
                # reaping is fixed upstream (AGENTS.md). Sweep issues no GABS call.
                continue
            if os.path.basename(exe) != VALHEIM_EXE_BASENAME:
                continue
            considered += 1
            marker = env.pid_marker(pid)
            if marker is None:
                # B1, THE RULE. No marker => left strictly alone, with NO fallback
                # heuristic. This branch also covers EACCES on a uid-1001 process:
                # an unreadable environ is not proof of ownership. Any cmdline/cwd/
                # game-root fallback here would put Daniel's own Steam Valheim in
                # scope, so there is none.
                self._record(
                    P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_LEFT_ALONE,
                    "no harness marker readable in its environment; not provably "
                    "harness-owned, so it is left strictly alone (B1) — an unreadable "
                    "environ is not proof of ownership",
                )
                continue
            observed_run = marker_run_id(marker)
            if observed_run != run_id:
                self._record(
                    P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_LEFT_ALONE,
                    f"harness marker {marker!r} does not name this run ({run_id!r}); it "
                    "belongs to a concurrent harness run, or predates run-stamped "
                    "markers — either way it is not ours to kill",
                )
                continue
            self._terminate(pid, marker)

        if considered == 0:
            self._record(
                P_PROCESSES, "-", RESOURCE_PROCESS, "-", OUTCOME_ALREADY_ABSENT,
                "no live valheim.x86_64 process present",
            )

    def _terminate(self, pid: int, marker: str) -> None:
        """SIGTERM → wait → SIGKILL, re-verifying identity before EVERY signal."""
        env = self._env
        start_ticks = env.pid_start_ticks(pid)
        if start_ticks is None:
            self._record(
                P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_ALREADY_ABSENT,
                "process exited between reading its marker and pinning its start time",
            )
            return

        for sig, label in ((_signal.SIGTERM, "SIGTERM"), (_signal.SIGKILL, "SIGKILL")):
            # Re-verify IMMEDIATELY before each signal. Between the probe and the
            # signal the PID can be recycled by an unrelated — possibly
            # Daniel-owned — process; a recycled PID has a different start time and a
            # different (or absent) marker, and either mismatch aborts.
            now_marker = env.pid_marker(pid)
            now_ticks = env.pid_start_ticks(pid)
            if now_marker is None or now_ticks is None:
                self._record(
                    P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_REMOVED,
                    f"harness-owned client for run {marker!r} is gone; it exited before "
                    f"{label} was sent",
                )
                return
            if now_marker != marker or now_ticks != start_ticks:
                self._record(
                    P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_LEFT_ALONE,
                    "identity CHANGED between probe and signal (marker "
                    f"{marker!r}->{now_marker!r}, start-ticks {start_ticks}->{now_ticks}); "
                    "the PID was recycled and this is no longer our process — refusing "
                    f"to send {label}",
                )
                return
            try:
                env.signal_pid(pid, sig)
            except OSError as exc:
                self._record(
                    P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_REFUSED,
                    f"{label} failed: {type(exc).__name__}: {exc}",
                )
                return
            if env.wait_gone(pid):
                self._record(
                    P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_REMOVED,
                    f"harness-owned client for run {marker!r} terminated with {label}",
                )
                return

        self._record(
            P_PROCESSES, "-", RESOURCE_PROCESS, f"pid {pid}", OUTCOME_REFUSED,
            f"harness-owned client for run {marker!r} survived SIGTERM and SIGKILL; "
            "it is unkillable from here (uninterruptible sleep, or a zombie whose "
            "parent must reap it) and needs a human",
        )

    def report(self) -> SweepReport:
        actions = tuple(self._actions)
        return SweepReport(
            ok=not any(a.outcome in _NOT_CONVERGED for a in actions),
            actions=actions,
            swept_clients=tuple(self._manifest.actors),
            checked_preconditions=ALL_CRITERIA,
        )


def arrange_sweep(
    manifest: ArrangeManifest, env: SweepEnvironment, *, arranging_uid: int
) -> SweepReport:
    """Run the sweep phase over `manifest`. Unlinks and signals; writes nothing.

    `env` and `arranging_uid` are both explicit and neither is defaulted: the decision
    to remove files and signal processes on THIS machine, as THIS identity, belongs at
    the construction site where a human can see it, not inherited silently by whoever
    imports the module.

    Checks never short-circuit — one invocation reconciles every declared path and
    every candidate process, so an operator sees every problem at once rather than one
    per ten-minute boot cycle (§3 P3).
    """
    sweeper = _Sweeper(manifest, env, arranging_uid=arranging_uid)
    sweeper.sweep_credentials()
    sweeper.sweep_processes()
    return sweeper.report()
