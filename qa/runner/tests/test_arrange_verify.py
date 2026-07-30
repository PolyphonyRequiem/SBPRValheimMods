"""VERIFY-phase tests (T022 ARRANGE §4 VERIFY, issue #456).

Everything here is engine-free and pure: no process is started, no game is contacted,
no port is bound, no file is written. Every environment contact is a stub, and the one
class that touches the real filesystem does so under `tmp_path`.

Organised around the acceptance criteria, one class each, plus:
  * `TestPartialArrangementIsHardFailure` — the anti-silence contract: a partial
    arrangement names the client and the missing thing and never proceeds.
  * `TestVerifySeamsAreMandatory` — the P9 contract over `VerifyEnvironment`, enforced
    structurally and by an AST scan of every construction site, exactly as #454/#467/#473
    established for `StaticEnvironment`.
  * `TestThirdClientIsDataOnly` — a third client is checked and reported by name with no
    code change.
"""
from __future__ import annotations

import ast
import copy
import dataclasses
import os

import pytest

from runner_core.arrange_manifest import ArrangeManifest, ArrangeManifestError
from runner_core.arrange_static import StaticFailure
from runner_core.arrange_verify import (
    ALL_CRITERIA,
    METHOD_LIVE_ARGV,
    METHOD_STAGED_DELIVERY,
    P_ARTIFACTS,
    P_CREDENTIALS,
    P_JOIN_PATH,
    P_PORTS,
    LiveProcess,
    VerifyEnvironment,
    arrange_verify,
    real_verify_environment,
)
from runner_core.credential_access import CredentialReadError

H_HARNESS = "a" * 64
H_PRODUCT = "b" * 64

WRAPPER_A = "/home/poly/.local/share/Trailborne/Valheim-Modded/run-trailborne.sh"
WRAPPER_B = "/home/valbot/run-valbot-bepinex.sh"
SIDECAR_A = "/home/poly/.local/share/sbpr-qa/launch-env/valheim-qa-a.env"
SIDECAR_B = "/home/valbot/.local/share/sbpr-qa/launch-env/valheim.env"

# The real deployed wrapper shape: sources the sidecar, builds the fragment, appends it
# after "$@" so it survives run_bepinex.sh's SteamLaunch argv rotation.
GOOD_WRAPPER = (
    'SBPR_QA_LAUNCH_ENV_FILE="$HOME/.local/share/sbpr-qa/launch-env/$GABS_GAME_ID.env"\n'
    '. "$SBPR_QA_LAUNCH_ENV_FILE"\n'
    "SBPR_QA_CONNECT_ARGS=()\n"
    'if [[ -n "${SBPR_QA_CONNECT:-}" ]]; then\n'
    '  SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT")\n'
    "fi\n"
    'exec setsid --wait "$RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"\n'
)


def golden_manifest():
    """The REAL asymmetric pair: nothing about client_b is derived from client_a."""
    return {
        "kind": "sbpr-qa-arrange-manifest",
        "version": 2,
        "lane": {
            "lane_id": "t022-disposable",
            "world_name": "t022lane",
            "host": "127.0.0.1",
            "port": 2476,
            "requires_password": True,
        },
        "artifacts": [
            {
                "name": "SBPR.QaHarness.T022.dll",
                "source_path": "/build/out/SBPR.QaHarness.T022.dll",
                "sha256": H_HARNESS,
            },
            {
                "name": "SBPR.Trailborne.dll",
                "source_path": "/build/out/SBPR.Trailborne.dll",
                "sha256": H_PRODUCT,
            },
        ],
        "clients": [
            {
                "actor": "client_a",
                "uid": 1000,
                "user": "polyphonyrequiem",
                "steam_account": "76561197965627562",
                "game_root": "/home/poly/.local/share/Trailborne/Valheim-Modded",
                "binary_path": "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64",
                "plugins_dir": "/home/poly/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins",
                "launcher": {
                    "kind": "gabs",
                    "endpoint": "http://localhost:8080/mcp",
                    "game_id": "valheim-qa-a",
                    "launch_env_path": SIDECAR_A,
                    "wrapper_path": WRAPPER_A,
                },
                "ports": {
                    "loopback_control": 48610,
                    "valbridge_gabp": 49152,
                    "unity_script_host": 48210,
                },
                "qa_profile": "sbpr_qa_a",
                "join": {"host": "127.0.0.1", "port": 2476, "delivery": "connect_argv"},
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": "/home/poly/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins/SBPR.QaHarness.T022.dll",
                    }
                ],
                "credentials": {
                    "server_password": {
                        "path": "/run/sbpr-qa/a/lane-pw.txt",
                        "consumer_uid": 1000,
                    }
                },
            },
            {
                "actor": "client_b",
                "uid": 1001,
                "user": "valbot",
                "steam_account": "76561198671522196",
                "game_root": "/home/valbot/.steam/steam/steamapps/common/Valheim",
                "binary_path": "/home/valbot/.steam/steam/steamapps/common/Valheim/valheim.x86_64",
                "plugins_dir": "/home/valbot/.steam/steam/steamapps/common/Valheim/BepInEx/plugins",
                "launcher": {
                    "kind": "steam_applaunch",
                    "app_id": "892970",
                    "launch_env_path": SIDECAR_B,
                    "wrapper_path": WRAPPER_B,
                },
                "ports": {
                    "loopback_control": 48611,
                    "valbridge_gabp": 49153,
                    "unity_script_host": None,
                },
                "qa_profile": "sbpr_qa_b",
                "join": {"host": "127.0.0.1", "port": 2476, "delivery": "connect_argv"},
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": "/home/valbot/.steam/steam/steamapps/common/Valheim/BepInEx/plugins/SBPR.QaHarness.T022.dll",
                    }
                ],
                "credentials": {
                    "server_password": {
                        "path": "/run/sbpr-qa/b/lane-pw.txt",
                        "consumer_uid": 1001,
                    }
                },
            },
        ],
    }


def parsed(raw=None):
    return ArrangeManifest.parse(raw if raw is not None else golden_manifest())


def good_files():
    """Sidecars and wrappers in the shape PROVISION and #453 leave them."""
    return {
        SIDECAR_A: "SBPR_QA_CONNECT=127.0.0.1:2476\nSBPR_QA_PROFILE=sbpr_qa_a\n",
        SIDECAR_B: "SBPR_QA_CONNECT=127.0.0.1:2476\nSBPR_QA_PROFILE=sbpr_qa_b\n",
        WRAPPER_A: GOOD_WRAPPER,
        WRAPPER_B: GOOD_WRAPPER,
    }


def env(
    *,
    stage_failures=(),
    unreadable_credentials=(),
    files=None,
    processes=None,
    busy_ports=(),
    undeterminable_ports=(),
    processes_unenumerable=False,
    reads=None,
):
    """Build a fully-wired stub environment. Every seam is supplied explicitly.

    `reads` is an optional list the credential seam appends `(path, uid)` to, so a test
    can assert WHICH identity the read was attempted as — the entire content of V2.
    """
    files = good_files() if files is None else files

    def _read_credential(path, uid):
        if reads is not None:
            reads.append((path, uid))
        if path in unreadable_credentials:
            raise CredentialReadError(
                f"credential is missing or unreadable at {path!r} as consuming uid {uid}"
            )

    def _live(client):
        if processes_unenumerable:
            return None
        return tuple((processes or {}).get(client.actor, ()))

    def _port(host, port):
        if port in undeterminable_ports:
            return None
        return port not in busy_ports

    return VerifyEnvironment(
        stage_postconditions=lambda _manifest: tuple(stage_failures),
        read_credential_as_uid=_read_credential,
        read_text=lambda path: files.get(path),
        live_processes=_live,
        port_is_free=_port,
    )


def failures_for(report, criterion, actor):
    return [
        f
        for f in report.client(actor).failures
        if f.precondition == criterion
    ]


def criterion(report, actor, name):
    return next(c for c in report.client(actor).criteria if c.criterion == name)


class TestGoldenIsReady:
    def test_fully_arranged_pair_is_ready(self):
        report = arrange_verify(parsed(), env())
        assert report.ok, report.render()
        assert [c.actor for c in report.clients] == ["client_a", "client_b"]
        assert all(c.ready for c in report.clients)

    def test_every_criterion_is_reported_per_client(self):
        report = arrange_verify(parsed(), env())
        for entry in report.clients:
            assert tuple(c.criterion for c in entry.criteria) == ALL_CRITERIA

    def test_render_names_the_clients_and_says_ready(self):
        rendered = arrange_verify(parsed(), env()).render()
        assert "READY" in rendered
        assert "client_a" in rendered and "client_b" in rendered


class TestArtifactsVerified:
    """AC1 — every client has every required artifact, hashes asserted."""

    def test_missing_artifact_fails_against_its_own_client(self):
        absent = StaticFailure(
            precondition="T1-ARTIFACT-STAGED",
            client="client_b",
            detail="required artifact 'SBPR.QaHarness.T022.dll' is ABSENT after staging",
            expected="a staged file",
            actual="no such file",
        )
        report = arrange_verify(parsed(), env(stage_failures=(absent,)))

        assert not report.ok
        assert report.client("client_b").ready is False
        # THE twelve-day failure: present on one client, absent on the other. The
        # untouched client must still be reported ready, or the report cannot tell an
        # operator WHICH client is missing the thing.
        assert report.client("client_a").ready is True
        assert not criterion(report, "client_b", P_ARTIFACTS).ok
        assert criterion(report, "client_a", P_ARTIFACTS).ok

    def test_drifted_bytes_sink_readiness(self):
        drifted = StaticFailure(
            precondition="T2-ARTIFACT-BYTES",
            client="client_a",
            detail="staged artifact does not match its pin",
            expected=f"sha256 {H_HARNESS}",
            actual="sha256 " + "d" * 64,
        )
        report = arrange_verify(parsed(), env(stage_failures=(drifted,)))
        assert not report.ok
        assert report.client("client_a").ready is False

    def test_artifact_evidence_names_the_requirement_count(self):
        report = arrange_verify(parsed(), env())
        assert "1 required artifact(s)" in criterion(report, "client_a", P_ARTIFACTS).evidence


class TestCredentialReadableByConsumer:
    """AC2 — every credential readable BY ITS CONSUMER, tested as that uid."""

    def test_read_is_attempted_as_the_declared_consuming_uid(self):
        """The whole content of the criterion.

        A credential readable by the uid that WROTE it proves nothing: the historical
        defect was 0600 in a 0700 directory, written by uid 1000 and consumed by uid
        1001. So the assertion is not that a read happened, but that it was attempted
        as 1001 for client_b and 1000 for client_a.
        """
        reads = []
        arrange_verify(parsed(), env(reads=reads))
        assert ("/run/sbpr-qa/a/lane-pw.txt", 1000) in reads
        assert ("/run/sbpr-qa/b/lane-pw.txt", 1001) in reads

    def test_unreadable_credential_names_client_uid_and_path(self):
        report = arrange_verify(
            parsed(), env(unreadable_credentials=("/run/sbpr-qa/b/lane-pw.txt",))
        )
        failure = failures_for(report, P_CREDENTIALS, "client_b")[0]

        assert not report.ok
        assert failure.client == "client_b"
        assert "/run/sbpr-qa/b/lane-pw.txt" in failure.expected
        assert "uid 1001" in failure.expected
        assert "valbot" in failure.expected
        assert failure.remedy

    def test_dangling_credential_is_the_same_defect_as_an_unreadable_one(self):
        """#461 found the password file pointing at a non-existent path on BOTH clients.

        The seam collapses missing and permission-denied into one fail-closed error on
        purpose: either leaves the headless client without a credential, and the remedy
        must send the operator to check both.
        """
        report = arrange_verify(
            parsed(),
            env(
                unreadable_credentials=(
                    "/run/sbpr-qa/a/lane-pw.txt",
                    "/run/sbpr-qa/b/lane-pw.txt",
                )
            ),
        )
        assert not report.ok
        assert not any(c.ready for c in report.clients)
        for actor in ("client_a", "client_b"):
            assert "missing" in failures_for(report, P_CREDENTIALS, actor)[0].remedy

    def test_client_with_no_credentials_says_so_rather_than_implying_a_read(self):
        raw = golden_manifest()
        raw["lane"]["requires_password"] = False
        for client in raw["clients"]:
            client["credentials"] = {}
        report = arrange_verify(ArrangeManifest.parse(raw), env())
        result = criterion(report, "client_a", P_CREDENTIALS)

        assert report.ok
        assert "nothing read" in result.evidence
        # The criterion passes — S4 is what reconciles an absent credential against the
        # lane policy — but it must NOT claim to be backed by an observation that never
        # happened. Caught by running the real CLI, which reported proven_live=True for
        # a client whose own evidence line said nothing was read.
        assert result.proven_live is False
        assert result.method == "unestablished"

    def test_failure_never_names_a_client_that_does_not_exist(self):
        """The report must not embed a placeholder actor inside its own message.

        Caught by the first live cross-uid run: VERIFY emitted an outer line naming
        `client_b` wrapping a nested one naming `<verify>`, a client in no manifest.
        Fail-closed, so never a wrong verdict — but it sends an operator grepping for
        an actor that does not exist. The probe seam is now the raw
        `credential_access.read_as_uid`, and the actor is attached here, where it is
        actually known.
        """
        report = arrange_verify(
            parsed(), env(unreadable_credentials=("/run/sbpr-qa/b/lane-pw.txt",))
        )
        rendered = report.render()
        assert "<verify>" not in rendered
        for failure in report.client("client_b").failures:
            assert "<verify>" not in failure.actual

    def test_failure_actual_is_one_line_not_a_pasted_traceback(self):
        """`actual` is read by a human scanning a multi-client report.

        The real probe raises with the subprocess's whole stderr, traceback included.
        Pasting that verbatim buries the one line that matters under six that don't,
        and does it once per failing credential per client.
        """

        def _read(path, uid):
            raise PermissionError(
                "Traceback (most recent call last):\n"
                '  File "<string>", line 1, in <module>\n'
                "PermissionError: [Errno 13] Permission denied: '/run/sbpr-qa/b/lane-pw.txt'"
            )

        environment = VerifyEnvironment(
            stage_postconditions=lambda _m: (),
            read_credential_as_uid=_read,
            read_text=lambda p: good_files().get(p),
            live_processes=lambda _c: (),
            port_is_free=lambda _h, _p: True,
        )
        failure = failures_for(
            arrange_verify(parsed(), environment), P_CREDENTIALS, "client_a"
        )[0]

        assert "\n" not in failure.actual
        assert "Traceback" not in failure.actual
        # The one line that matters survives.
        assert "Permission denied" in failure.actual
        # ...and the type is named exactly once. The probe's last stderr line already
        # begins `PermissionError:`, so re-prefixing produced
        # `PermissionError: PermissionError: ...` on the live cross-uid run.
        assert failure.actual.count("PermissionError") == 1

    def test_a_read_that_raises_a_plain_oserror_is_still_a_named_failure(self):
        """The seam's contract is 'raises on failure', not 'raises CredentialReadError'."""

        def _read(path, uid):
            raise PermissionError("Permission denied")

        environment = VerifyEnvironment(
            stage_postconditions=lambda _m: (),
            read_credential_as_uid=_read,
            read_text=lambda p: good_files().get(p),
            live_processes=lambda _c: (),
            port_is_free=lambda _h, _p: True,
        )
        report = arrange_verify(parsed(), environment)
        assert not report.ok
        assert "PermissionError" in failures_for(report, P_CREDENTIALS, "client_a")[0].actual


class TestJoinTargetInActualLaunchPath:
    """AC3 — each client's join target is present in its ACTUAL launch path."""

    def test_live_argv_is_the_strong_rung_and_is_marked_proven_live(self):
        processes = {
            "client_a": (
                LiveProcess(
                    pid=4242,
                    argv=(
                        "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64",
                        "+connect",
                        "127.0.0.1:2476",
                    ),
                ),
            )
        }
        report = arrange_verify(parsed(), env(processes=processes))
        result = criterion(report, "client_a", P_JOIN_PATH)

        assert result.ok
        assert result.method == METHOD_LIVE_ARGV
        assert result.proven_live is True
        assert "4242" in result.evidence

    def test_staged_delivery_is_honestly_marked_not_proven_live(self):
        """The distinction the whole phase turns on: evidence is not the live claim."""
        report = arrange_verify(parsed(), env())
        result = criterion(report, "client_b", P_JOIN_PATH)

        assert result.ok
        assert result.method == METHOD_STAGED_DELIVERY
        assert result.proven_live is False

    def test_live_process_without_connect_fails_and_quotes_its_argv(self):
        processes = {
            "client_b": (
                LiveProcess(
                    pid=99,
                    argv=(
                        "/home/valbot/.steam/steam/steamapps/common/Valheim/valheim.x86_64",
                    ),
                ),
            )
        }
        report = arrange_verify(parsed(), env(processes=processes))
        failure = failures_for(report, P_JOIN_PATH, "client_b")[0]

        assert not report.ok
        assert "pid 99" in failure.detail
        assert "+connect 127.0.0.1:2476" in failure.expected
        assert "run_bepinex.sh rotates argv" in failure.remedy

    def test_connect_pointing_at_a_different_lane_fails(self):
        processes = {
            "client_a": (
                LiveProcess(
                    pid=7,
                    argv=(
                        "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64",
                        "+connect",
                        "127.0.0.1:2456",
                    ),
                ),
            )
        }
        report = arrange_verify(parsed(), env(processes=processes))
        assert not report.ok
        assert failures_for(report, P_JOIN_PATH, "client_a")

    def test_non_adjacent_connect_does_not_count(self):
        """Vanilla parses `+connect` and takes the NEXT argument.

        A `+connect` separated from its value populates nothing, and a naive substring
        test would pass it while the client parks at the server list.
        """
        processes = {
            "client_a": (
                LiveProcess(
                    pid=8,
                    argv=(
                        "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64",
                        "+connect",
                        "-console",
                        "127.0.0.1:2476",
                    ),
                ),
            )
        }
        report = arrange_verify(parsed(), env(processes=processes))
        assert not report.ok

    def test_every_live_process_must_carry_it_not_merely_one(self):
        """Picking the process that agrees with us is the report flattering itself."""
        binary = "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64"
        processes = {
            "client_a": (
                LiveProcess(pid=1, argv=(binary, "+connect", "127.0.0.1:2476")),
                LiveProcess(pid=2, argv=(binary,)),
            )
        }
        report = arrange_verify(parsed(), env(processes=processes))
        failures = failures_for(report, P_JOIN_PATH, "client_a")

        assert not report.ok
        assert len(failures) == 1
        assert "pid 2" in failures[0].detail

    def test_missing_sidecar_fails_closed(self):
        files = good_files()
        del files[SIDECAR_B]
        report = arrange_verify(parsed(), env(files=files))
        failure = failures_for(report, P_JOIN_PATH, "client_b")[0]

        assert not report.ok
        assert "sidecar is missing or unreadable" in failure.detail

    def test_stale_sidecar_pointing_at_another_lane_fails(self):
        files = good_files()
        files[SIDECAR_B] = "SBPR_QA_CONNECT=127.0.0.1:9999\n"
        report = arrange_verify(parsed(), env(files=files))
        failure = failures_for(report, P_JOIN_PATH, "client_b")[0]

        assert "does not carry this run's lane" in failure.detail
        assert "127.0.0.1:9999" in failure.actual

    def test_sidecar_with_no_connect_line_says_so_distinctly(self):
        files = good_files()
        files[SIDECAR_A] = "SBPR_QA_PROFILE=sbpr_qa_a\n"
        report = arrange_verify(parsed(), env(files=files))
        assert "no SBPR_QA_CONNECT line at all" in failures_for(
            report, P_JOIN_PATH, "client_a"
        )[0].actual

    def test_wrapper_that_never_builds_the_fragment_fails(self):
        """An env var is not a join target; only the `+connect` ARGUMENT is."""
        files = good_files()
        files[WRAPPER_B] = 'SBPR_QA_CONNECT="x"\nexec "$RUNNER" "$@"\n'
        report = arrange_verify(parsed(), env(files=files))
        failure = failures_for(report, P_JOIN_PATH, "client_b")[0]

        assert "does not build a `+connect` fragment" in failure.detail
        assert "builds_connect_args=False" in failure.actual

    def test_unreadable_wrapper_fails_closed(self):
        files = good_files()
        del files[WRAPPER_A]
        report = arrange_verify(parsed(), env(files=files))
        assert "wrapper is missing or unreadable" in failures_for(
            report, P_JOIN_PATH, "client_a"
        )[0].detail

    def test_no_process_and_no_declared_sidecar_is_a_failure_not_a_pass(self):
        raw = golden_manifest()
        del raw["clients"][0]["launcher"]["launch_env_path"]
        report = arrange_verify(ArrangeManifest.parse(raw), env())
        failure = failures_for(report, P_JOIN_PATH, "client_a")[0]

        assert not report.ok
        assert "no launch-env path declared" in failure.detail

    def test_sidecar_without_a_declared_wrapper_is_evidence_of_nothing(self):
        raw = golden_manifest()
        del raw["clients"][1]["launcher"]["wrapper_path"]
        report = arrange_verify(ArrangeManifest.parse(raw), env())
        failure = failures_for(report, P_JOIN_PATH, "client_b")[0]

        assert "no wrapper is declared to consume it" in failure.detail

    def test_unenumerable_process_table_fails_rather_than_downgrading(self):
        """"I could not look" must never render the same as "I looked and it was fine".

        Falling back to the staged rung here would assert delivery for a process that
        may already be running WITHOUT it — the report would be strictly wrong, not
        merely weaker.
        """
        report = arrange_verify(parsed(), env(processes_unenumerable=True))
        failure = failures_for(report, P_JOIN_PATH, "client_a")[0]

        assert not report.ok
        assert "process table could not be enumerated" in failure.detail
        assert "do not downgrade the claim" in failure.remedy


class TestPortsDisjointAndFree:
    """AC4 — per-client port sets verified disjoint AND free."""

    def test_colliding_ports_are_reported_against_the_claimants(self):
        raw = golden_manifest()
        raw["clients"][1]["ports"]["valbridge_gabp"] = 49152  # client_a's
        report = arrange_verify(ArrangeManifest.parse(raw), env())

        assert not report.ok
        for actor, other in (("client_a", "client_b"), ("client_b", "client_a")):
            failure = failures_for(report, P_PORTS, actor)[0]
            assert "49152" in failure.detail
            assert other in failure.actual

    def test_busy_port_is_a_failure_static_could_never_have_caught(self):
        """Disjoint in the manifest, held by a stale process on the machine."""
        report = arrange_verify(parsed(), env(busy_ports=(49153,)))
        failure = failures_for(report, P_PORTS, "client_b")[0]

        assert not report.ok
        assert "already in use" in failure.detail
        assert "Address already in use" in failure.remedy

    def test_undeterminable_port_is_not_treated_as_free(self):
        report = arrange_verify(parsed(), env(undeterminable_ports=(48610,)))
        failure = failures_for(report, P_PORTS, "client_a")[0]

        assert not report.ok
        assert "could not be determined" in failure.detail
        assert "not a free one" in failure.remedy

    def test_disabled_listener_is_not_probed(self):
        """client_b declares unity_script_host null; a null port binds nothing."""
        probed = []

        environment = VerifyEnvironment(
            stage_postconditions=lambda _m: (),
            read_credential_as_uid=lambda _p, _u: None,
            read_text=lambda p: good_files().get(p),
            live_processes=lambda _c: (),
            port_is_free=lambda host, port: probed.append(port) or True,
        )
        report = arrange_verify(parsed(), environment)

        assert report.ok
        assert 48210 in probed  # client_a's, which IS declared
        assert sorted(probed) == [48210, 48610, 48611, 49152, 49153]

    def test_port_evidence_is_marked_proven_live(self):
        """Unlike the staged join rung, a bind probe IS an observation of the machine."""
        result = criterion(arrange_verify(parsed(), env()), "client_a", P_PORTS)
        assert result.proven_live is True
        assert result.method == "bind-probe"


class TestReadinessReportIsMachineReadable:
    """AC5 — emits a machine-readable readiness report per client."""

    def test_report_dict_carries_every_client_and_criterion(self):
        data = arrange_verify(parsed(), env()).as_dict()

        assert data["phase"] == "verify"
        assert data["ok"] is True
        assert data["criteria"] == list(ALL_CRITERIA)
        assert [c["client"] for c in data["clients"]] == ["client_a", "client_b"]
        for entry in data["clients"]:
            assert [c["criterion"] for c in entry["criteria"]] == list(ALL_CRITERIA)

    def test_report_dict_is_json_serialisable(self):
        import json

        json.loads(json.dumps(arrange_verify(parsed(), env()).as_dict()))

    def test_not_ready_names_the_clients_at_the_top_level(self):
        """"Which client is not ready" must not require walking the criteria."""
        report = arrange_verify(
            parsed(), env(unreadable_credentials=("/run/sbpr-qa/b/lane-pw.txt",))
        )
        assert report.as_dict()["not_ready"] == ["client_b"]

    def test_evidence_distinguishes_live_proof_from_staged_evidence(self):
        """The honesty contract, in machine-readable form.

        A consumer must be able to tell "a running process carries the target" from
        "the files that would deliver it are in place" without re-deriving it.
        """
        processes = {
            "client_a": (
                LiveProcess(
                    pid=11,
                    argv=(
                        "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64",
                        "+connect",
                        "127.0.0.1:2476",
                    ),
                ),
            )
        }
        data = arrange_verify(parsed(), env(processes=processes)).as_dict()
        joins = {
            entry["client"]: next(
                c for c in entry["criteria"] if c["criterion"] == P_JOIN_PATH
            )
            for entry in data["clients"]
        }
        assert joins["client_a"]["proven_live"] is True
        assert joins["client_b"]["proven_live"] is False

    def test_failures_are_reported_in_the_shared_failure_shape(self):
        data = arrange_verify(
            parsed(), env(unreadable_credentials=("/run/sbpr-qa/a/lane-pw.txt",))
        ).as_dict()
        failure = data["clients"][0]["failures"][0]
        assert set(failure) == {
            "precondition",
            "client",
            "detail",
            "expected",
            "actual",
            "remedy",
        }


class TestPartialArrangementIsHardFailure:
    """AC6 — a partial arrangement is a hard, named failure, never a silent proceed."""

    def test_one_bad_client_sinks_the_whole_run(self):
        report = arrange_verify(parsed(), env(busy_ports=(49153,)))
        assert report.ok is False
        assert report.client("client_a").ready is True
        assert report.client("client_b").ready is False

    def test_failure_render_names_precondition_client_and_expected_vs_actual(self):
        rendered = arrange_verify(
            parsed(), env(unreadable_credentials=("/run/sbpr-qa/b/lane-pw.txt",))
        ).render()

        assert "NOT READY" in rendered
        assert P_CREDENTIALS in rendered
        assert "client=client_b" in rendered
        assert "expected:" in rendered and "actual:" in rendered

    def test_checks_do_not_short_circuit(self):
        """One invocation reports EVERY problem; each boot cycle costs ten minutes."""
        absent = StaticFailure(
            precondition="T1-ARTIFACT-STAGED",
            client="client_b",
            detail="artifact absent",
            expected="a staged file",
            actual="no such file",
        )
        files = good_files()
        del files[SIDECAR_B]
        report = arrange_verify(
            parsed(),
            env(
                stage_failures=(absent,),
                unreadable_credentials=("/run/sbpr-qa/b/lane-pw.txt",),
                files=files,
                busy_ports=(49153,),
            ),
        )
        found = {f.precondition for f in report.client("client_b").failures}
        assert found == {"T1-ARTIFACT-STAGED", P_CREDENTIALS, P_JOIN_PATH, P_PORTS}

    def test_a_run_over_no_clients_is_never_ready(self):
        """Vacuous truth is the silent proceed in its purest form.

        `all([])` is True, so a report over zero clients would say READY while proving
        nothing about anything. The manifest layer refuses an empty `clients` list, so
        both guards are asserted: the phase's own (defence in depth, and still valid if
        VERIFY is ever handed a filtered client set) and the manifest's.
        """

        class NoClients:
            clients = ()

        assert arrange_verify(NoClients(), env()).ok is False

        with pytest.raises(ArrangeManifestError, match="non-empty"):
            raw = golden_manifest()
            raw["clients"] = []
            ArrangeManifest.parse(raw)


class TestThirdClientIsDataOnly:
    def test_a_third_client_is_checked_and_named_with_no_code_change(self):
        raw = golden_manifest()
        third = copy.deepcopy(raw["clients"][0])
        third.update(
            {
                "actor": "client_c",
                "uid": 1002,
                "user": "valbot2",
                "steam_account": "76561198000000003",
                "game_root": "/home/valbot2/Valheim",
                "binary_path": "/home/valbot2/Valheim/valheim.x86_64",
                "plugins_dir": "/home/valbot2/Valheim/BepInEx/plugins",
                "qa_profile": "sbpr_qa_c",
                "ports": {
                    "loopback_control": 48612,
                    "valbridge_gabp": 49154,
                    "unity_script_host": None,
                },
                "credentials": {
                    "server_password": {
                        "path": "/run/sbpr-qa/c/lane-pw.txt",
                        "consumer_uid": 1002,
                    }
                },
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": "/home/valbot2/Valheim/BepInEx/plugins/SBPR.QaHarness.T022.dll",
                    }
                ],
            }
        )
        third["launcher"] = {
            "kind": "direct_exec",
            "launch_env_path": SIDECAR_A,
            "wrapper_path": WRAPPER_A,
        }
        raw["clients"].append(third)

        environment = env(
            unreadable_credentials=("/run/sbpr-qa/c/lane-pw.txt",), reads=(reads := [])
        )
        report = arrange_verify(ArrangeManifest.parse(raw), environment)

        assert report.ok is False
        assert report.as_dict()["not_ready"] == ["client_c"]
        assert ("/run/sbpr-qa/c/lane-pw.txt", 1002) in reads


class TestVerifySeamsAreMandatory:
    """§3 P9 over `VerifyEnvironment` — the contract #454/#467/#473 kept losing.

    A defaulted seam fails closed, so it is never a security bypass. It is worse in a
    subtler way: an omitted wiring then surfaces as "port state undeterminable" or
    "process table unenumerable" attributed to the CLIENT, sending an operator to
    inspect a machine that is fine, and emitting the same line a genuine fault would.
    Making construction impossible converts a misleading diagnosis into a TypeError at
    the call site.
    """

    @pytest.mark.parametrize(
        "omitted",
        [
            "stage_postconditions",
            "read_credential_as_uid",
            "read_text",
            "live_processes",
            "port_is_free",
        ],
    )
    def test_constructing_without_any_seam_raises_type_error(self, omitted):
        kwargs = {
            "stage_postconditions": lambda _m: (),
            "read_credential_as_uid": lambda _p, _u: None,
            "read_text": lambda _p: None,
            "live_processes": lambda _c: (),
            "port_is_free": lambda _h, _p: True,
        }
        del kwargs[omitted]
        with pytest.raises(TypeError) as excinfo:
            VerifyEnvironment(**kwargs)  # type: ignore[arg-type]  # verify-seam-contract-negative
        assert omitted in str(excinfo.value)

    def test_no_environment_field_carries_a_default(self):
        """Structural guard: catches a re-defaulted seam the moment it lands.

        The per-seam TypeError tests above only fire for seams some caller happens to
        omit. This asserts the contract over the dataclass itself, so a future merge
        cannot quietly re-add a default to a seam every current test supplies — the
        exact way #452/#453 re-defaulted `StaticEnvironment`'s seams with nothing
        turning red.
        """
        offenders = [
            f.name
            for f in dataclasses.fields(VerifyEnvironment)
            if f.default is not dataclasses.MISSING
            or f.default_factory is not dataclasses.MISSING  # type: ignore[misc]
        ]
        assert not offenders, (
            "VerifyEnvironment proof seams must not be defaulted (P9); "
            f"defaulted field(s): {offenders}"
        )

    def test_arrange_verify_requires_an_explicit_environment(self):
        """The phase entrypoint may not default the environment either.

        A defaulted `env=None -> real_verify_environment()` would let a caller probe
        ports and read another identity's credentials on a machine it never decided to
        touch, and the decision would be invisible at the call site.
        """
        with pytest.raises(TypeError):
            arrange_verify(parsed())  # type: ignore[call-arg]  # verify-seam-contract-negative

    def test_every_repository_caller_supplies_the_seams(self):
        """No construction of the environment dataclass may omit a seam.

        A type error only fires on a code path that actually runs. This asserts the
        contract over every construction site in the repository, including ones a given
        test session never reaches. The scan is an AST walk, deliberately not a
        text/paren scan: a naive brace counter cannot tell a paren inside a string or
        comment from a real one, so it can swallow a trailing region of the file that
        then contains the keyword being looked for — turning the check into a silent
        PASS. A guard whose whole job is catching silent regressions must not itself be
        able to fail silently.
        """
        # Anchor on the repository marker, not a dirname count: this file sits at
        # qa/runner/tests/, so counting levels would land on qa/ and silently skip
        # every caller outside it.
        repo = os.path.dirname(os.path.abspath(__file__))
        while not os.path.isfile(os.path.join(repo, "AGENTS.md")):
            parent = os.path.dirname(repo)
            assert parent != repo, "could not locate the repository root (AGENTS.md)"
            repo = parent
        assert os.path.isdir(os.path.join(repo, "qa", "runner")), repo

        target = VerifyEnvironment.__name__
        # Seam positions come from the dataclass itself rather than a hardcoded arity:
        # a hand-maintained index is correct at N fields and silently wrong at N+1,
        # which is how the equivalent StaticEnvironment scan decayed (#473).
        field_index = {
            f.name: i for i, f in enumerate(dataclasses.fields(VerifyEnvironment))
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
                    if "verify-seam-contract-negative" in line:
                        # The deliberate omissions above, marked inline so the
                        # exemption is visible at the site rather than encoded as a
                        # path in this scanner.
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
        # A scanner that silently matched nothing would pass forever. Assert it walked
        # a real tree AND found real constructions, so a broken root, a filter, or a
        # parser change cannot look like a clean result.
        assert scanned > 10, f"scanner walked only {scanned} python files from {repo}"
        assert constructions >= 3, (
            f"scanner found only {constructions} construction site(s); it is no longer "
            "seeing the callers it exists to guard"
        )
        assert not offenders, (
            f"{target}(...) constructed without the mandatory proof seam(s) "
            f"{list(seams)} at: {offenders}"
        )


class TestRealVerifyEnvironment:
    """The real seam, exercised against tmp_path and this process — never a host path.

    A test that reads a real rig path silently SKIPS when the path is absent, which
    reads as green while proving nothing.
    """

    def test_real_read_text_returns_none_for_a_missing_file(self, tmp_path):
        environment = real_verify_environment()
        assert environment.read_text(str(tmp_path / "nope")) is None

    def test_real_read_text_returns_contents(self, tmp_path):
        path = tmp_path / "sidecar.env"
        path.write_text("SBPR_QA_CONNECT=127.0.0.1:2476\n", encoding="utf-8")
        assert "SBPR_QA_CONNECT" in real_verify_environment().read_text(str(path))

    def test_real_port_probe_reports_a_held_port_as_not_free(self):
        import socket

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as held:
            held.bind(("127.0.0.1", 0))
            held.listen(1)
            port = held.getsockname()[1]
            assert real_verify_environment().port_is_free("127.0.0.1", port) is False

    def test_real_port_probe_reports_an_unheld_port_as_free(self):
        import socket

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            probe.bind(("127.0.0.1", 0))
            port = probe.getsockname()[1]
        assert real_verify_environment().port_is_free("127.0.0.1", port) is True

    def test_real_port_probe_returns_none_when_undeterminable(self):
        """An unroutable bind address is neither free nor in use; say so, don't guess."""
        assert real_verify_environment().port_is_free("203.0.113.1", 48610) is None

    def test_real_process_finder_locates_this_process_by_argv0(self, tmp_path):
        """Proves the finder reads REAL /proc argv, using a client entry pointing at
        this interpreter — no game, no host rig path, no skip."""
        import sys

        raw = golden_manifest()
        raw["clients"][0]["binary_path"] = sys.executable
        manifest = ArrangeManifest.parse(raw)

        found = real_verify_environment().live_processes(manifest.client("client_a"))
        assert found is not None
        assert any(p.pid == os.getpid() for p in found)

    def test_real_process_finder_returns_empty_for_an_unrunning_binary(self, tmp_path):
        raw = golden_manifest()
        raw["clients"][0]["binary_path"] = str(tmp_path / "definitely-not-running")
        manifest = ArrangeManifest.parse(raw)

        assert real_verify_environment().live_processes(manifest.client("client_a")) == ()

    def test_real_credential_probe_reads_as_the_current_uid(self, tmp_path):
        """The same-uid path of the real seam, with no sudo and no rig dependency."""
        path = tmp_path / "lane-pw.txt"
        path.write_text("throwaway\n", encoding="utf-8")
        # Returns None on success, raises on failure.
        assert real_verify_environment().read_credential_as_uid(str(path), os.getuid()) is None

    def test_real_credential_probe_refuses_a_missing_file(self, tmp_path):
        with pytest.raises(OSError):
            real_verify_environment().read_credential_as_uid(
                str(tmp_path / "nope"), os.getuid()
            )


@pytest.mark.crossuid
class TestRealCrossUidCredentialRead:
    """The criterion that cannot be honestly proven same-uid (#452 seam, #456 V2).

    A credential readable by the uid that WROTE it proves nothing — that is the entire
    point of V2, and it is the one thing a same-uid test can never establish. So these
    run the REAL `sudo -n -u #<uid>` probe against a real second identity.

    They FAIL rather than skip when the rig is absent. A skipped test reads as green
    while proving nothing, which is the exact failure mode this phase exists to end;
    if this box has no second QA identity, that is a fact the suite should state out
    loud. Deselect deliberately with `-m 'not crossuid'` when running somewhere the
    dual-user rig does not exist.
    """

    CONSUMER_UID = 1001

    @staticmethod
    def make_traversable(leaf, tmp_path):
        """Make every ancestor of `leaf` traversable by a foreign uid.

        Not incidental test plumbing — this IS I4's "two independent locks, either one
        sufficient". `pytest`'s `tmp_path` sits under a 0700 `/tmp/pytest-of-<user>`
        directory, so a perfectly-moded 0644 credential inside it is still unreachable
        by uid 1001: traversal requires the execute bit on EVERY component of the path,
        not just the last one. That is precisely the defect the credential policy
        encodes (0711 *directory* + 0644 file), and it caught these tests on their
        first real run. PROVISION establishes the same property in production via
        `prepare_credential_directory`.
        """
        current = os.path.dirname(str(leaf))
        stop = os.path.dirname(os.path.dirname(str(tmp_path)))
        while current and current != "/" and current.startswith(stop):
            os.chmod(current, os.stat(current).st_mode | 0o111)
            parent = os.path.dirname(current)
            if parent == current:
                break
            current = parent

    @pytest.fixture
    def consumer_uid(self):
        import pwd
        import subprocess

        try:
            pwd.getpwuid(self.CONSUMER_UID)
        except KeyError:
            pytest.fail(
                f"uid {self.CONSUMER_UID} does not exist on this host; the cross-uid "
                "credential criterion cannot be proven here. Deselect with "
                "-m 'not crossuid' rather than letting it look green."
            )
        probe = subprocess.run(
            ["sudo", "-n", "-u", f"#{self.CONSUMER_UID}", "--", "/usr/bin/true"],
            capture_output=True,
            text=True,
            check=False,
        )
        if probe.returncode != 0:
            pytest.fail(
                f"passwordless sudo to uid {self.CONSUMER_UID} is unavailable "
                f"({probe.stderr.strip()}); V2 cannot be proven on the real wire here."
            )
        return self.CONSUMER_UID

    def test_approved_policy_is_readable_by_the_consuming_uid(
        self, tmp_path, consumer_uid
    ):
        """0711 directory / 0644 file: traversable by known path, not listable."""
        directory = tmp_path / "creds"
        directory.mkdir()
        credential = directory / "lane-pw.txt"
        credential.write_text("throwaway\n", encoding="utf-8")
        os.chmod(credential, 0o644)
        self.make_traversable(credential, tmp_path)
        os.chmod(directory, 0o711)

        # Raises if the foreign uid cannot read it.
        real_verify_environment().read_credential_as_uid(str(credential), consumer_uid)

    def test_historical_defect_fails_closed_for_the_consuming_uid(
        self, tmp_path, consumer_uid
    ):
        """0700 directory / 0600 file — two independent locks, either one sufficient.

        This is the arrangement that cost twelve days, and its only symptom was a
        client waiting at a menu.
        """
        directory = tmp_path / "creds"
        directory.mkdir()
        credential = directory / "lane-pw.txt"
        credential.write_text("throwaway\n", encoding="utf-8")
        # Traversable ancestry, so the ONLY thing denying the foreign uid is the
        # 0700/0600 pair under test — otherwise this would pass for the wrong reason.
        self.make_traversable(credential, tmp_path)
        os.chmod(directory, 0o700)
        os.chmod(credential, 0o600)

        environment = real_verify_environment()
        with pytest.raises(OSError):
            environment.read_credential_as_uid(str(credential), consumer_uid)

        # ...and the SAME file is readable by the uid that wrote it. This is the trap
        # the criterion exists to close: a same-uid read would have reported PASS.
        environment.read_credential_as_uid(str(credential), os.getuid())

    def test_dangling_reference_fails_closed_too(self, tmp_path, consumer_uid):
        """I4: a dangling reference is the same defect as an unreadable one (#461)."""
        target = tmp_path / "never-provisioned.txt"
        self.make_traversable(target, tmp_path)
        with pytest.raises(OSError):
            real_verify_environment().read_credential_as_uid(str(target), consumer_uid)

    def test_verify_phase_end_to_end_against_the_real_consuming_identity(
        self, tmp_path, consumer_uid
    ):
        """V2 through `arrange_verify` itself, not just the seam underneath it."""
        directory = tmp_path / "creds"
        directory.mkdir()
        credential = directory / "lane-pw.txt"
        credential.write_text("throwaway\n", encoding="utf-8")
        os.chmod(credential, 0o644)
        self.make_traversable(credential, tmp_path)
        os.chmod(directory, 0o711)

        raw = golden_manifest()
        raw["clients"][1]["uid"] = consumer_uid
        raw["clients"][1]["credentials"]["server_password"]["path"] = str(credential)
        raw["clients"][1]["credentials"]["server_password"]["consumer_uid"] = consumer_uid

        environment = VerifyEnvironment(
            stage_postconditions=lambda _m: (),
            read_credential_as_uid=real_verify_environment().read_credential_as_uid,
            read_text=lambda p: good_files().get(p),
            live_processes=lambda _c: (),
            port_is_free=lambda _h, _p: True,
        )
        report = arrange_verify(ArrangeManifest.parse(raw), environment)
        result = criterion(report, "client_b", P_CREDENTIALS)

        assert result.ok, report.render()
        assert result.proven_live is True
        assert f"uid{consumer_uid}" in result.evidence

        # Now break it exactly as the historical defect did, and prove VERIFY refuses.
        os.chmod(credential, 0o600)
        os.chmod(directory, 0o700)
        broken = arrange_verify(ArrangeManifest.parse(raw), environment)
        failure = failures_for(broken, P_CREDENTIALS, "client_b")[0]

        assert broken.ok is False
        assert failure.client == "client_b"
        assert f"uid {consumer_uid}" in failure.expected
        assert "<verify>" not in failure.actual
