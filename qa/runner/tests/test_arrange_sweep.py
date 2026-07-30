"""Fault-injection tests for the SWEEP arrange phase (#455).

Engine-free and hermetic: every filesystem and `/proc` contact goes through a stub,
so this suite reads nothing real, removes nothing real, and signals nothing real.

**No test here may touch a real host path.** `$HOME` differs between the profile a
worker runs under and the human's account, so a test that resolved `~` would silently
read the wrong tree — and a test that silently SKIPs reads as green while proving
nothing, which is the exact class of silence the arrange phase exists to end. Every
path below is a fabricated absolute string inside the stub's own dictionary.

The single most important assertion in this file is
`test_unmarked_valheim_process_is_never_signalled`: sweep kills processes, and a
client with no harness marker — including Daniel's own Steam Valheim — must never be
signalled. It asserts on a spy that records EVERY signal, so the claim is "nothing was
sent" rather than "the outcome string looks right".
"""
from __future__ import annotations

import ast
import dataclasses
import json
import os
import signal

import pytest

from runner_core.arrange_manifest import ArrangeManifest
from runner_core.arrange_sweep import (
    OUTCOME_ALREADY_ABSENT,
    OUTCOME_LEFT_ALONE,
    OUTCOME_REFUSED,
    OUTCOME_REMOVED,
    P_CREDENTIALS,
    P_PROCESSES,
    P_PROVENANCE,
    SweepEnvironment,
    arrange_sweep,
)
from runner_core.credential_provenance import (
    PROVENANCE_KIND,
    PROVENANCE_VERSION,
    CredentialProvenance,
    parse_provenance,
    provenance_path,
)
from runner_core.proc_provenance import mint_marker

RUN_ID = "t022-run-current"
OTHER_RUN = "t022-run-other"
ARRANGING_UID = 1000
NOW_MS = 1_000_000_000

CRED_A = "/run/sbpr-qa/a/bootstrap.json"
CRED_B = "/run/sbpr-qa/b/lane-pw.txt"


# --------------------------------------------------------------------------- #
# Manifest + environment stubs
# --------------------------------------------------------------------------- #

def _client(actor: str, uid: int, credentials: dict) -> dict:
    return {
        "actor": actor,
        "uid": uid,
        "user": f"user_{actor}",
        "steam_account": f"steam_{actor}",
        "game_root": f"/lane/{actor}",
        "binary_path": f"/lane/{actor}/valheim.x86_64",
        "plugins_dir": f"/lane/{actor}/BepInEx/plugins",
        "launcher": {"kind": "direct_exec"},
        "ports": {
            "loopback_control": 48610 + uid % 10,
            "valbridge_gabp": 48710 + uid % 10,
            "unity_script_host": None,
        },
        "artifacts": [],
        "credentials": credentials,
    }


def parsed(*, run_id: str = RUN_ID, clients=None) -> ArrangeManifest:
    """A minimal well-formed manifest. Two clients by default, never positional."""
    if clients is None:
        clients = [
            _client("client_a", 1000, {"bootstrap": {"path": CRED_A, "consumer_uid": 1000}}),
            _client("client_b", 1001, {"server_password": {"path": CRED_B, "consumer_uid": 1001}}),
        ]
    return ArrangeManifest.parse(
        {
            "kind": "sbpr-qa-arrange-manifest",
            "version": 3,
            "run_id": run_id,
            "lane": {
                "lane_id": "t022-lane",
                "world_name": "w",
                "host": "127.0.0.1",
                "port": 2476,
                "requires_password": True,
            },
            "artifacts": [],
            "clients": clients,
        }
    )


class FakeWorld:
    """A stub filesystem + `/proc`, and a spy over every mutation.

    `files` maps an absolute path to `(text, owner_uid)`; `symlinks` is the set of
    paths that are themselves symlinks. `procs` maps a PID to
    `(exe, marker, start_ticks)` — `exe=None` models a zombie or a PID whose exe link
    cannot be read, `marker=None` models both "no marker" and "environ unreadable",
    which is deliberate: they are the same fail-closed fact.

    `signals` records EVERY (pid, signal) the sweeper attempted, whether or not it
    succeeded. Assertions about what was NOT signalled read this list.
    """

    def __init__(self, *, files=None, symlinks=(), procs=None, pids_raise=False):
        self.files = dict(files or {})
        self.symlinks = set(symlinks)
        self.procs = dict(procs or {})
        self.pids_raise = pids_raise
        self.signals: "list[tuple[int, int]]" = []
        self.unlinked: "list[str]" = []
        self.unlink_uids: "list[tuple[str, int]]" = []
        self.unlink_errors: "dict[str, OSError]" = {}
        self.survives: "set[int]" = set()

    # --- filesystem -------------------------------------------------------- #
    def read_text(self, path):
        entry = self.files.get(path)
        return None if entry is None else entry[0]

    def lstat_owner(self, path):
        entry = self.files.get(path)
        return None if entry is None else entry[1]

    def is_symlink(self, path):
        return path in self.symlinks

    def unlink(self, path, as_uid):
        # Records the identity, not just the path: the whole point of the seam is
        # WHO performed the unlink, and a stub that dropped that would let the
        # cross-uid regression back in unnoticed.
        self.unlink_uids.append((path, as_uid))
        if path in self.unlink_errors:
            raise self.unlink_errors[path]
        if path not in self.files:
            raise FileNotFoundError(path)
        del self.files[path]
        self.unlinked.append(path)

    # --- /proc ------------------------------------------------------------- #
    def list_pids(self):
        return None if self.pids_raise else sorted(self.procs)

    def pid_exe(self, pid):
        entry = self.procs.get(pid)
        return None if entry is None else entry[0]

    def pid_marker(self, pid):
        entry = self.procs.get(pid)
        return None if entry is None else entry[1]

    def pid_start_ticks(self, pid):
        entry = self.procs.get(pid)
        return None if entry is None else entry[2]

    def signal_pid(self, pid, sig):
        self.signals.append((pid, sig))
        if pid not in self.procs:
            raise ProcessLookupError(pid)
        if sig == signal.SIGKILL or pid not in self.survives:
            del self.procs[pid]

    def wait_gone(self, pid):
        return pid not in self.procs

    def env(self) -> SweepEnvironment:
        return SweepEnvironment(
            read_text=self.read_text,
            lstat_owner=self.lstat_owner,
            is_symlink=self.is_symlink,
            unlink=self.unlink,
            list_pids=self.list_pids,
            pid_exe=self.pid_exe,
            pid_marker=self.pid_marker,
            pid_start_ticks=self.pid_start_ticks,
            signal_pid=self.signal_pid,
            wait_gone=self.wait_gone,
            now_unix_ms=lambda: NOW_MS,
        )


def provenance_text(
    *, run_id=RUN_ID, actor="client_a", path=CRED_A, minted=NOW_MS - 1000, expiry=None
) -> str:
    return json.dumps(
        CredentialProvenance(
            run_id=run_id,
            actor=actor,
            credential_path=path,
            minted_unix_ms=minted,
            expiry_unix_ms=expiry,
        ).as_dict()
    )


def credential_with_provenance(path, *, owner=ARRANGING_UID, **prov):
    """A credential file plus its provenance sidecar, as the stub's `files` entries.

    The sidecar's `credential_path` always matches `path` — a mismatch would be a
    malformed pair no production writer can produce, so tests do not fabricate one.
    """
    prov["path"] = path
    return {
        path: ("secret-bytes", owner),
        provenance_path(path): (provenance_text(**prov), owner),
    }


def sweep(world: FakeWorld, manifest=None):
    return arrange_sweep(
        manifest or parsed(), world.env(), arranging_uid=ARRANGING_UID
    )


def outcomes(report, precondition=None):
    return {
        (a.target, a.outcome)
        for a in report.actions
        if precondition is None or a.precondition == precondition
    }


# --------------------------------------------------------------------------- #
# Convergence — the idempotency acceptance criterion
# --------------------------------------------------------------------------- #

class TestConvergence:
    def test_clean_tree_is_all_already_absent_and_passes(self):
        report = sweep(FakeWorld())
        assert report.ok is True
        assert {a.outcome for a in report.actions} == {OUTCOME_ALREADY_ABSENT}
        # Every declared credential AND its sidecar was looked at, not just the ones
        # that existed: the report is the evidence the phase LOOKED.
        assert (CRED_A, OUTCOME_ALREADY_ABSENT) in outcomes(report)
        assert (provenance_path(CRED_A), OUTCOME_ALREADY_ABSENT) in outcomes(report)

    def test_sweeping_twice_yields_byte_identical_reports(self):
        """The AC: run N+1 over an already-swept tree matches run N exactly.

        Asserted on the full serialised dict rather than on `ok`, because a phase can
        converge on outcome while reporting different actions — and #457 will consume
        this dict, so its stability is part of the contract, not an implementation
        detail.

        The clock ADVANCES between runs. A frozen clock would let a report that
        embedded a timestamp (or any other per-run varying value) compare equal to
        itself, so the guard would pass while the property it exists to prove was
        broken — which is exactly what a deliberate-break rehearsal of this test
        caught. Convergence must mean "the same regardless of when you run it".
        """
        world = FakeWorld(
            files={
                **credential_with_provenance(CRED_A, actor="client_a"),
                **credential_with_provenance(CRED_B, actor="client_b"),
            }
        )
        clock = {"now": NOW_MS}

        def advancing_env():
            clock["now"] += 60_000
            return dataclasses.replace(world.env(), now_unix_ms=lambda: clock["now"])

        first = arrange_sweep(parsed(), advancing_env(), arranging_uid=ARRANGING_UID)
        assert first.ok is True
        assert set(world.unlinked) == {
            CRED_A, provenance_path(CRED_A), CRED_B, provenance_path(CRED_B)
        }

        second = arrange_sweep(parsed(), advancing_env(), arranging_uid=ARRANGING_UID)
        third = arrange_sweep(parsed(), advancing_env(), arranging_uid=ARRANGING_UID)
        assert second.ok is True
        assert json.dumps(second.as_dict(), sort_keys=True) == json.dumps(
            third.as_dict(), sort_keys=True
        )
        assert {a.outcome for a in second.actions} == {OUTCOME_ALREADY_ABSENT}

    def test_three_client_manifest_sweeps_every_client(self):
        """Count-agnostic: no consumer may assume two clients (§3 P2)."""
        cred_c = "/run/sbpr-qa/c/bootstrap.json"
        manifest = parsed(
            clients=[
                _client("client_a", 1000, {"bootstrap": {"path": CRED_A, "consumer_uid": 1000}}),
                _client("client_b", 1001, {"server_password": {"path": CRED_B, "consumer_uid": 1001}}),
                _client("client_c", 1002, {"bootstrap": {"path": cred_c, "consumer_uid": 1002}}),
            ]
        )
        world = FakeWorld(
            files={
                **credential_with_provenance(CRED_A, actor="client_a"),
                **credential_with_provenance(CRED_B, actor="client_b"),
                **credential_with_provenance(cred_c, actor="client_c"),
            }
        )
        report = sweep(world, manifest)
        assert report.ok is True
        assert set(report.swept_clients) == {"client_a", "client_b", "client_c"}
        assert cred_c in world.unlinked


# --------------------------------------------------------------------------- #
# C2 — the fail-closed credential decision table
# --------------------------------------------------------------------------- #

class TestCredentialDecisionTable:
    def test_same_run_id_is_removed_so_a_repeated_arrange_converges(self):
        world = FakeWorld(files=credential_with_provenance(CRED_A, run_id=RUN_ID))
        report = sweep(world)
        assert (CRED_A, OUTCOME_REMOVED) in outcomes(report, P_CREDENTIALS)
        assert report.ok is True

    def test_expired_prior_run_credential_and_sidecar_are_both_removed(self):
        world = FakeWorld(
            files=credential_with_provenance(
                CRED_A, run_id=OTHER_RUN, expiry=NOW_MS - 1
            )
        )
        report = sweep(world)
        assert (CRED_A, OUTCOME_REMOVED) in outcomes(report, P_CREDENTIALS)
        assert (provenance_path(CRED_A), OUTCOME_REMOVED) in outcomes(report, P_PROVENANCE)
        assert world.unlinked == [CRED_A, provenance_path(CRED_A)]
        assert report.ok is True

    def test_foreign_unexpired_credential_is_left_alone_and_fails(self):
        """A concurrent run's live credential is the lane lease's business, not ours."""
        world = FakeWorld(
            files=credential_with_provenance(
                CRED_A, run_id=OTHER_RUN, expiry=NOW_MS + 60_000
            )
        )
        report = sweep(world)
        assert (CRED_A, OUTCOME_LEFT_ALONE) in outcomes(report, P_CREDENTIALS)
        assert world.unlinked == []
        assert report.ok is False
        assert OTHER_RUN in " ".join(a.reason for a in report.unresolved)

    def test_lane_password_without_ttl_from_another_run_is_left_alone(self):
        """No TTL means NOT expired — never 'expired by default'.

        The absent TTL is the residual exposure #455 documents and #457 bounds.
        Treating it as expiry would paper over that gap with a false guarantee, and
        would delete a concurrent run's live lane password.
        """
        world = FakeWorld(
            files=credential_with_provenance(CRED_B, run_id=OTHER_RUN, expiry=None)
        )
        report = sweep(world)
        assert (CRED_B, OUTCOME_LEFT_ALONE) in outcomes(report, P_CREDENTIALS)
        assert world.unlinked == []
        assert report.ok is False

    def test_credential_without_provenance_is_left_alone(self):
        """An operator's file at a declared path is indistinguishable from residue."""
        world = FakeWorld(files={CRED_A: ("operator-placed", ARRANGING_UID)})
        report = sweep(world)
        assert (CRED_A, OUTCOME_LEFT_ALONE) in outcomes(report, P_CREDENTIALS)
        assert world.unlinked == []
        assert report.ok is False

    def test_unparseable_provenance_is_left_alone_not_guessed_at(self):
        world = FakeWorld(
            files={
                CRED_A: ("secret", ARRANGING_UID),
                provenance_path(CRED_A): ("{ this is not json", ARRANGING_UID),
            }
        )
        report = sweep(world)
        assert (CRED_A, OUTCOME_LEFT_ALONE) in outcomes(report, P_CREDENTIALS)
        assert world.unlinked == []
        assert report.ok is False

    def test_foreign_owner_uid_is_left_alone_even_with_our_run_id(self):
        """Mirrors `credential_access`: a file the harness could not have written is not ours.

        1002 is neither the arranging uid (1000) nor client_a's declared consuming uid
        (1000), so it is genuinely foreign. Checked BEFORE provenance on purpose —
        provenance is attacker-writable content at a path we do not own, so it must not
        be able to talk us into a delete.
        """
        world = FakeWorld(
            files=credential_with_provenance(CRED_A, owner=1002, run_id=RUN_ID)
        )
        report = sweep(world)
        assert (CRED_A, OUTCOME_LEFT_ALONE) in outcomes(report, P_CREDENTIALS)
        assert world.unlinked == []
        assert report.ok is False
        assert "1002" in " ".join(a.reason for a in report.unresolved)

    def test_credential_owned_by_its_declared_consumer_uid_is_swept_as_that_uid(self):
        """REGRESSION (found on the real wire, not by any stub): the cross-uid case.

        A credential staged into the consuming identity's tree is owned by THAT uid —
        uid 1000 writing into /home/valbot and chowning the result would need root and
        would dissolve the Steam identity isolation the dual-user rig depends on, so
        #451's `as_uid` staging is the only mechanism that works. Two consequences the
        original implementation got wrong, both invisible here until the phase was run
        against a real valbot-owned directory:

        1. The owner check admitted only the arranging uid, so EVERY uid-1001 credential
           was refused as "foreign" — the exact files #455 exists to sweep, skipped
           silently while the report read as a tidy `left-alone`.
        2. `unlink` was bound to one identity at construction, so even had the decision
           been right, uid 1000 cannot unlink from valbot's 0711 directory at all
           (unlink is governed by DIRECTORY write permission; a permissive file mode
           cannot rescue it).

        So this asserts both the outcome AND the identity that performed it.
        """
        world = FakeWorld(
            files=credential_with_provenance(
                CRED_B, owner=1001, actor="client_b", run_id=RUN_ID
            )
        )
        report = sweep(world)
        assert (CRED_B, OUTCOME_REMOVED) in outcomes(report, P_CREDENTIALS)
        assert (provenance_path(CRED_B), OUTCOME_REMOVED) in outcomes(report, P_PROVENANCE)
        # The unlink was performed AS uid 1001 — the file's own owner — not as the
        # arranging uid, which could not have done it.
        assert (CRED_B, 1001) in world.unlink_uids
        assert (provenance_path(CRED_B), 1001) in world.unlink_uids
        assert report.ok is True

    def test_runner_owned_credential_is_swept_as_the_arranging_uid(self):
        """The other half of the pair: don't act as the consumer when we own the file."""
        world = FakeWorld(files=credential_with_provenance(CRED_A, owner=ARRANGING_UID))
        report = sweep(world)
        assert (CRED_A, OUTCOME_REMOVED) in outcomes(report, P_CREDENTIALS)
        assert (CRED_A, ARRANGING_UID) in world.unlink_uids
        assert report.ok is True

    def test_symlink_credential_is_refused_and_its_target_untouched(self):
        target = "/home/daniel/.config/something-precious"
        world = FakeWorld(
            files={CRED_A: ("link", ARRANGING_UID), target: ("precious", ARRANGING_UID)},
            symlinks=[CRED_A],
        )
        report = sweep(world)
        assert (CRED_A, OUTCOME_REFUSED) in outcomes(report, P_CREDENTIALS)
        assert world.unlinked == []
        assert target in world.files
        assert report.ok is False

    def test_symlink_provenance_sidecar_is_refused(self):
        world = FakeWorld(
            files={
                CRED_A: ("secret", ARRANGING_UID),
                provenance_path(CRED_A): (provenance_text(), ARRANGING_UID),
            },
            symlinks=[provenance_path(CRED_A)],
        )
        report = sweep(world)
        assert (provenance_path(CRED_A), OUTCOME_REFUSED) in outcomes(report, P_PROVENANCE)
        assert report.ok is False

    def test_orphan_sidecar_is_removed_when_its_credential_is_already_gone(self):
        """A sidecar outliving its credential is itself residue."""
        world = FakeWorld(
            files={provenance_path(CRED_A): (provenance_text(), ARRANGING_UID)}
        )
        report = sweep(world)
        assert (CRED_A, OUTCOME_ALREADY_ABSENT) in outcomes(report, P_CREDENTIALS)
        assert (provenance_path(CRED_A), OUTCOME_REMOVED) in outcomes(report, P_PROVENANCE)
        assert report.ok is True

    def test_sidecar_is_kept_when_its_credential_could_not_be_removed(self):
        """Deleting the provenance of a file we left behind destroys the evidence."""
        world = FakeWorld(
            files=credential_with_provenance(
                CRED_A, run_id=OTHER_RUN, expiry=NOW_MS + 60_000
            )
        )
        report = sweep(world)
        assert (provenance_path(CRED_A), OUTCOME_LEFT_ALONE) in outcomes(report, P_PROVENANCE)
        assert provenance_path(CRED_A) in world.files

    def test_unlink_eperm_is_refused_by_path_and_errno_and_other_clients_still_sweep(self):
        """A failure on one client must not abort the sweep of its siblings."""
        world = FakeWorld(
            files={
                **credential_with_provenance(CRED_A, actor="client_a"),
                **credential_with_provenance(CRED_B, actor="client_b"),
            }
        )
        world.unlink_errors[CRED_A] = PermissionError(1, "Operation not permitted")
        report = sweep(world)
        assert (CRED_A, OUTCOME_REFUSED) in outcomes(report, P_CREDENTIALS)
        refusal = next(a for a in report.actions if a.target == CRED_A)
        assert "errno=1" in refusal.reason
        # The sibling client was still swept — no exception escaped.
        assert CRED_B in world.unlinked
        assert report.ok is False

    def test_credential_vanishing_mid_sweep_converges_rather_than_failing(self):
        """Losing a race with another remover still reaches the declared end state."""
        world = FakeWorld(files=credential_with_provenance(CRED_A))
        world.unlink_errors[CRED_A] = FileNotFoundError(2, "No such file")
        report = sweep(world)
        assert (CRED_A, OUTCOME_ALREADY_ABSENT) in outcomes(report, P_CREDENTIALS)
        assert report.ok is True

    def test_provenance_naming_a_wrong_kind_or_version_does_not_parse(self):
        """A shape we do not recognise is refused, never guessed into a deletion."""
        assert parse_provenance(None) is None
        assert parse_provenance("not json") is None
        assert parse_provenance(json.dumps({"kind": "something-else"})) is None
        assert parse_provenance(
            json.dumps({"kind": PROVENANCE_KIND, "version": PROVENANCE_VERSION + 1})
        ) is None
        assert parse_provenance(
            json.dumps(
                {
                    "kind": PROVENANCE_KIND,
                    "version": PROVENANCE_VERSION,
                    "run_id": "",
                    "actor": "a",
                    "credential_path": CRED_A,
                    "minted_unix_ms": 1,
                    "expiry_unix_ms": None,
                }
            )
        ) is None


# --------------------------------------------------------------------------- #
# B1 — marker-only process safety. The highest-stakes tests in this file.
# --------------------------------------------------------------------------- #

class TestProcessSafetyB1:
    def test_unmarked_valheim_process_is_never_signalled(self):
        """THE B1 TEST. Daniel's own Steam Valheim must never be touched.

        Modelled exactly as it appears to a sweeper: a live `valheim.x86_64` whose
        environ yields no marker. Asserted on the SIGNAL SPY — "no signal was sent" —
        rather than on the outcome string, because the outcome could read correctly
        while a signal had still gone out.

        There is deliberately no fallback heuristic for this case: no cmdline match,
        no cwd match, no `pkill -f`, no game-root prefix. Every one of those matches
        this process.
        """
        world = FakeWorld(procs={4242: ("/home/daniel/.steam/valheim.x86_64", None, 99)})
        report = sweep(world)
        assert world.signals == []
        assert (f"pid {4242}", OUTCOME_LEFT_ALONE) in outcomes(report, P_PROCESSES)
        assert 4242 in world.procs
        assert report.ok is False

    def test_unreadable_environ_is_not_proof_of_ownership(self):
        """EACCES on a uid-1001 process is indistinguishable from 'no marker' — by design.

        `pid_marker` returns None for both, and both must fail closed. Treating "I
        could not check" as "probably mine" is how a sweeper kills the user's game.
        """
        world = FakeWorld(procs={7000: ("/lane/b/valheim.x86_64", None, 12)})
        report = sweep(world)
        assert world.signals == []
        assert (f"pid {7000}", OUTCOME_LEFT_ALONE) in outcomes(report, P_PROCESSES)
        assert "not proof of ownership" in " ".join(a.reason for a in report.actions)

    def test_foreign_run_marker_is_never_signalled(self):
        world = FakeWorld(
            procs={5150: ("/lane/a/valheim.x86_64", mint_marker(OTHER_RUN, "client_a", "z"), 7)}
        )
        report = sweep(world)
        assert world.signals == []
        assert (f"pid {5150}", OUTCOME_LEFT_ALONE) in outcomes(report, P_PROCESSES)
        assert report.ok is False

    def test_legacy_marker_without_a_run_id_is_never_signalled(self):
        """A pre-#455 `<actor>:<random>` marker is unattributable, so it is left alone."""
        world = FakeWorld(procs={5151: ("/lane/a/valheim.x86_64", "client_a:deadbeef", 7)})
        report = sweep(world)
        assert world.signals == []
        assert report.ok is False

    def test_owned_client_gets_sigterm_and_is_reported_removed(self):
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6000: ("/lane/a/valheim.x86_64", marker, 5)})
        report = sweep(world)
        assert world.signals == [(6000, signal.SIGTERM)]
        assert (f"pid {6000}", OUTCOME_REMOVED) in outcomes(report, P_PROCESSES)
        assert report.ok is True

    def test_owned_client_surviving_sigterm_escalates_to_sigkill(self):
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6001: ("/lane/a/valheim.x86_64", marker, 5)})
        world.survives.add(6001)
        report = sweep(world)
        assert world.signals == [(6001, signal.SIGTERM), (6001, signal.SIGKILL)]
        assert (f"pid {6001}", OUTCOME_REMOVED) in outcomes(report, P_PROCESSES)

    def test_pid_reuse_between_probe_and_signal_aborts_before_sending(self):
        """TOCTOU: a recycled PID has a different start time, and must not be signalled."""
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6002: ("/lane/a/valheim.x86_64", marker, 5)})

        original_start_ticks = world.pid_start_ticks
        calls = {"n": 0}

        def shifting_start_ticks(pid):
            calls["n"] += 1
            # First read pins the identity; the re-read immediately before the signal
            # sees a DIFFERENT process holding the same PID.
            return original_start_ticks(pid) if calls["n"] == 1 else 987654

        env = dataclasses.replace(world.env(), pid_start_ticks=shifting_start_ticks)
        report = arrange_sweep(parsed(), env, arranging_uid=ARRANGING_UID)
        assert world.signals == []
        assert (f"pid {6002}", OUTCOME_LEFT_ALONE) in outcomes(report, P_PROCESSES)
        assert "recycled" in " ".join(a.reason for a in report.unresolved)
        assert report.ok is False

    def test_marker_changing_between_probe_and_signal_aborts(self):
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6003: ("/lane/a/valheim.x86_64", marker, 5)})
        calls = {"n": 0}

        def shifting_marker(pid):
            calls["n"] += 1
            return marker if calls["n"] == 1 else mint_marker(OTHER_RUN, "x", "y")

        env = dataclasses.replace(world.env(), pid_marker=shifting_marker)
        report = arrange_sweep(parsed(), env, arranging_uid=ARRANGING_UID)
        assert world.signals == []
        assert report.ok is False

    def test_non_valheim_binary_with_an_owned_marker_is_never_signalled(self):
        """Ownership alone is not enough: the exe must be the game binary.

        Checked against the kernel's `/proc/<pid>/exe`, not argv, so a process cannot
        talk the sweeper into signalling it by naming itself convincingly.
        """
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6004: ("/usr/bin/ssh-agent", marker, 5)})
        report = sweep(world)
        assert world.signals == []
        assert report.ok is True  # nothing to reconcile; not a failure

    def test_zombie_is_report_only_with_no_signal_and_no_gabs_call(self):
        """A `<defunct>` child exposes no readable exe and cannot be killed.

        Only its parent can reap it, and GABS's reaping is fixed upstream — so sweep
        neither signals it nor issues any GABS call. There is no GABS seam on
        `SweepEnvironment` at all, which is the structural version of this assertion.
        """
        world = FakeWorld(procs={6005: (None, mint_marker(RUN_ID, "client_a", "z"), 5)})
        report = sweep(world)
        assert world.signals == []
        assert report.ok is True
        assert not any(
            "gabs" in f.name.lower() for f in dataclasses.fields(SweepEnvironment)
        )

    def test_process_exiting_before_the_signal_is_reported_removed(self):
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6006: ("/lane/a/valheim.x86_64", marker, 5)})

        def vanishing_marker(pid):
            world.procs.pop(pid, None)
            return marker

        # First call (the ownership probe) sees it; the pre-signal re-read finds it gone.
        seen = {"n": 0}

        def marker_then_gone(pid):
            seen["n"] += 1
            if seen["n"] == 1:
                return marker
            return vanishing_marker(pid) and None

        env = dataclasses.replace(world.env(), pid_marker=marker_then_gone)
        report = arrange_sweep(parsed(), env, arranging_uid=ARRANGING_UID)
        assert world.signals == []
        assert (f"pid {6006}", OUTCOME_REMOVED) in outcomes(report, P_PROCESSES)

    def test_signal_failure_is_refused_rather_than_raised(self):
        marker = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(procs={6007: ("/lane/a/valheim.x86_64", marker, 5)})

        def refusing_signal(pid, sig):
            world.signals.append((pid, sig))
            raise PermissionError(1, "Operation not permitted")

        env = dataclasses.replace(world.env(), signal_pid=refusing_signal)
        report = arrange_sweep(parsed(), env, arranging_uid=ARRANGING_UID)
        assert (f"pid {6007}", OUTCOME_REFUSED) in outcomes(report, P_PROCESSES)
        assert report.ok is False

    def test_unenumerable_process_table_degrades_loudly_and_still_sweeps_credentials(self):
        """'I could not look' must never be reported as 'nothing was there'."""
        world = FakeWorld(
            files=credential_with_provenance(CRED_A), pids_raise=True
        )
        report = sweep(world)
        assert CRED_A in world.unlinked  # credentials still swept
        assert report.ok is False
        detail = " ".join(a.reason for a in report.unresolved)
        assert "UNKNOWN, not absent" in detail

    def test_mixed_process_table_signals_only_the_owned_client(self):
        """The realistic case: our client, Daniel's game, and a foreign run's client."""
        ours = mint_marker(RUN_ID, "client_a", "abc")
        world = FakeWorld(
            procs={
                100: ("/lane/a/valheim.x86_64", ours, 1),
                200: ("/home/daniel/.steam/valheim.x86_64", None, 2),
                300: ("/lane/x/valheim.x86_64", mint_marker(OTHER_RUN, "c", "d"), 3),
            }
        )
        report = sweep(world)
        assert [pid for pid, _ in world.signals] == [100]
        assert 200 in world.procs and 300 in world.procs
        assert report.ok is False  # the two left-alone processes are unresolved


# --------------------------------------------------------------------------- #
# P9 — the mandatory-seam contract, enforced structurally
# --------------------------------------------------------------------------- #

class TestSweepSeamsAreMandatory:
    """§3 P9 over `SweepEnvironment` — the contract #454/#467/#473 kept losing.

    A defaulted seam fails closed, so it is never a security bypass. It is worse in a
    subtler way: an omitted wiring then surfaces as "process table unenumerable"
    attributed to the CLIENT, sending an operator to inspect a machine that is fine,
    and emitting the same line a genuine fault would. Making construction impossible
    converts a misleading diagnosis into a TypeError at the call site.
    """

    SEAMS = (
        "read_text",
        "lstat_owner",
        "is_symlink",
        "unlink",
        "list_pids",
        "pid_exe",
        "pid_marker",
        "pid_start_ticks",
        "signal_pid",
        "wait_gone",
        "now_unix_ms",
    )

    def test_the_seam_list_matches_the_dataclass(self):
        """Guards the guard: a new seam must be added to the cases below, not skipped."""
        assert tuple(f.name for f in dataclasses.fields(SweepEnvironment)) == self.SEAMS

    @pytest.mark.parametrize("omitted", SEAMS)
    def test_constructing_without_any_seam_raises_type_error(self, omitted):
        kwargs = {
            "read_text": lambda _p: None,
            "lstat_owner": lambda _p: None,
            "is_symlink": lambda _p: False,
            "unlink": lambda _p, _u: None,
            "list_pids": lambda: (),
            "pid_exe": lambda _p: None,
            "pid_marker": lambda _p: None,
            "pid_start_ticks": lambda _p: None,
            "signal_pid": lambda _p, _s: None,
            "wait_gone": lambda _p: True,
            "now_unix_ms": lambda: NOW_MS,
        }
        del kwargs[omitted]
        with pytest.raises(TypeError) as excinfo:
            SweepEnvironment(**kwargs)  # type: ignore[arg-type]  # sweep-seam-contract-negative
        assert omitted in str(excinfo.value)

    def test_no_environment_field_carries_a_default(self):
        """Structural guard: catches a re-defaulted seam the moment it lands.

        The per-seam TypeError cases only fire for seams some caller happens to omit.
        This asserts the contract over the dataclass itself, so a future merge cannot
        quietly re-add a default to a seam every current test supplies — the exact way
        `StaticEnvironment`'s seams were re-defaulted with nothing turning red.
        """
        offenders = [
            f.name
            for f in dataclasses.fields(SweepEnvironment)
            if f.default is not dataclasses.MISSING
            or f.default_factory is not dataclasses.MISSING  # type: ignore[misc]
        ]
        assert not offenders, (
            "SweepEnvironment proof seams must not be defaulted (P9); "
            f"defaulted field(s): {offenders}"
        )

    def test_arrange_sweep_requires_an_explicit_environment_and_uid(self):
        """The phase entrypoint may not default the environment or the identity.

        A defaulted `env=None -> real_sweep_environment()` would let a caller remove
        files and signal processes on a machine it never decided to touch, and the
        decision would be invisible at the call site. For a phase that KILLS
        PROCESSES, that decision must be written down where a human reviews it.
        """
        with pytest.raises(TypeError):
            arrange_sweep(parsed())  # type: ignore[call-arg]  # sweep-seam-contract-negative

    def test_every_repository_caller_supplies_the_seams(self):
        """No construction of the environment dataclass may omit a seam.

        A type error only fires on a code path that actually runs. This asserts the
        contract over every construction site in the repository, including ones a
        given test session never reaches. The scan is an AST walk, deliberately not a
        text/paren scan: a naive brace counter cannot tell a paren inside a string or
        comment from a real one, so it can swallow a trailing region of the file that
        then contains the keyword being looked for — turning the check into a silent
        PASS. A guard whose whole job is catching silent regressions must not itself
        be able to fail silently.
        """
        repo = os.path.dirname(os.path.abspath(__file__))
        while not os.path.isfile(os.path.join(repo, "AGENTS.md")):
            parent = os.path.dirname(repo)
            assert parent != repo, "could not locate the repository root (AGENTS.md)"
            repo = parent
        assert os.path.isdir(os.path.join(repo, "qa", "runner")), repo

        target = SweepEnvironment.__name__
        field_index = {
            f.name: i for i, f in enumerate(dataclasses.fields(SweepEnvironment))
        }
        seams = tuple(field_index)
        offenders = []
        scanned = 0
        constructions = 0
        for dirpath, dirnames, filenames in os.walk(repo):
            dirnames[:] = [
                d
                for d in dirnames
                if d not in {".git", "__pycache__", "obj", "bin", "node_modules"}
                and not d.startswith(".venv")
            ]
            for filename in filenames:
                if not filename.endswith(".py"):
                    continue
                path = os.path.join(dirpath, filename)
                text = open(path, encoding="utf-8", errors="replace").read()
                try:
                    tree = ast.parse(text)
                except SyntaxError:
                    continue
                scanned += 1
                lines = text.splitlines()
                for node in ast.walk(tree):
                    if not isinstance(node, ast.Call):
                        continue
                    func = node.func
                    name = (
                        func.id
                        if isinstance(func, ast.Name)
                        else func.attr
                        if isinstance(func, ast.Attribute)
                        else None
                    )
                    if name != target:
                        continue
                    line = lines[node.lineno - 1]
                    if "sweep-seam-contract-negative" in line:
                        continue
                    constructions += 1
                    missing = [
                        seam
                        for seam in seams
                        if not any(kw.arg == seam for kw in node.keywords)
                        and not any(kw.arg is None for kw in node.keywords)
                        and len(node.args) <= field_index[seam]
                    ]
                    if missing:
                        offenders.append(
                            f"{os.path.relpath(path, repo)}:{node.lineno} "
                            f"(missing: {', '.join(missing)})"
                        )
        assert not offenders, "SweepEnvironment constructed without every seam: " + "; ".join(
            offenders
        )
        # A scanner that silently matched nothing would pass forever. Assert it walked
        # a real tree AND found real constructions.
        assert scanned > 10, f"scanner walked only {scanned} python files from {repo}"
        assert constructions >= 2, (
            f"scanner found only {constructions} construction site(s); it is no longer "
            "seeing the callers it exists to guard"
        )
