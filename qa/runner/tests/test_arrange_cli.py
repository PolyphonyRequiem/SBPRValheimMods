"""`arrange --check` CLI tests (issue #450).

Asserts the entrypoint exists, is STATIC-only, exits with the documented codes, and
that a real on-disk manifest round-trips through it. Nothing here starts a process or
touches a game.
"""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path

import pytest

RUNNER_DIR = Path(__file__).resolve().parent.parent
EXAMPLE = RUNNER_DIR / "examples" / "arrange-manifest.example.json"


def load_cli():
    spec = importlib.util.spec_from_file_location(
        "sbpr_qa_arrange", RUNNER_DIR / "sbpr-qa-arrange.py"
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


@pytest.fixture
def cli():
    return load_cli()


def write(tmp_path, data):
    path = tmp_path / "arrange.json"
    path.write_text(json.dumps(data), encoding="utf-8")
    return str(path)


def minimal(tmp_path):
    """A one-client manifest with real files on disk, so pins genuinely verify."""
    root = tmp_path / "game"
    (root / "BepInEx" / "plugins").mkdir(parents=True)
    src = tmp_path / "build" / "harness.dll"
    src.parent.mkdir(parents=True)
    src.write_bytes(b"harness-bytes")

    import hashlib

    digest = hashlib.sha256(b"harness-bytes").hexdigest()
    return {
        "kind": "sbpr-qa-arrange-manifest",
        "version": 2,
        "lane": {
            "lane_id": "l",
            "world_name": "w",
            "host": "127.0.0.1",
            "port": 2476,
            "requires_password": False,
        },
        "artifacts": [
            {"name": "harness", "source_path": str(src), "sha256": digest}
        ],
        "clients": [
            {
                "actor": "solo",
                "uid": 1000,
                "user": "poly",
                "steam_account": "76561197965627562",
                "game_root": str(root),
                "binary_path": str(root / "valheim.x86_64"),
                "plugins_dir": str(root / "BepInEx" / "plugins"),
                "launcher": {"kind": "direct_exec"},
                "ports": {
                    "loopback_control": 48610,
                    "valbridge_gabp": 49152,
                    "unity_script_host": None,
                },
                "qa_profile": "sbpr_qa_solo",
                "join": {
                    "host": "127.0.0.1",
                    "port": 2476,
                    "delivery": "connect_argv",
                },
                "artifacts": [
                    {
                        "artifact": "harness",
                        "dest_path": str(root / "BepInEx" / "plugins" / "harness.dll"),
                    }
                ],
                "credentials": {},
            }
        ],
    }


class TestArrangeCli:
    def test_passing_manifest_exits_zero(self, cli, tmp_path, capsys):
        rc = cli.main(["--manifest", write(tmp_path, minimal(tmp_path)), "--check"])
        assert rc == 0
        assert "PASS" in capsys.readouterr().out

    def test_failing_manifest_exits_one_and_names_the_precondition(
        self, cli, tmp_path, capsys
    ):
        data = minimal(tmp_path)
        data["clients"][0]["ports"]["loopback_control"] = 2456
        rc = cli.main(["--manifest", write(tmp_path, data), "--check"])
        out = capsys.readouterr().out
        assert rc == 1
        assert "S2-PRODUCTION-PORT-DENY" in out
        assert "solo" in out
        assert "expected:" in out and "actual:" in out

    def test_unreadable_manifest_exits_two(self, cli, tmp_path, capsys):
        rc = cli.main(["--manifest", str(tmp_path / "nope.json"), "--check"])
        assert rc == 2
        assert "cannot read manifest" in capsys.readouterr().out

    def test_no_mode_selected_exits_two(self, cli, tmp_path, capsys):
        rc = cli.main(["--manifest", write(tmp_path, minimal(tmp_path))])
        assert rc == 2
        assert "--check" in capsys.readouterr().out

    def test_json_output_is_machine_readable(self, cli, tmp_path, capsys):
        data = minimal(tmp_path)
        data["clients"][0]["ports"]["loopback_control"] = 2466
        cli.main(["--manifest", write(tmp_path, data), "--check", "--json"])
        payload = json.loads(capsys.readouterr().out)
        assert payload["phase"] == "static"
        assert payload["ok"] is False
        assert payload["clients"] == ["solo"]
        assert payload["failures"][0]["client"] == "solo"

    def test_malformed_json_exits_two(self, cli, tmp_path, capsys):
        path = tmp_path / "bad.json"
        path.write_text("{ not json", encoding="utf-8")
        assert cli.main(["--manifest", str(path), "--check"]) == 2


class TestStageCli:
    """`--stage` CLI surface (issue #451).

    The manifest here declares the CURRENT uid rather than a hardcoded 1000, because
    staging asserts each artifact lands owned by its client's declared uid. Pinning a
    literal uid would make these pass only on a box where the runner happens to be
    uid 1000 — and skip-or-lie everywhere else.
    """

    def staged_manifest(self, tmp_path):
        import os

        data = minimal(tmp_path)
        data["clients"][0]["uid"] = os.getuid()
        # Stage into a plugin subdirectory that does NOT exist yet, so the CLI path
        # exercises directory creation rather than plain replacement.
        root = tmp_path / "game"
        data["clients"][0]["artifacts"][0]["dest_path"] = str(
            root / "BepInEx" / "plugins" / "SBPR.QaHarness.T022" / "harness.dll"
        )
        return data

    def test_dry_run_writes_nothing_and_reports_the_plan(self, cli, tmp_path, capsys):
        data = self.staged_manifest(tmp_path)
        dest = Path(data["clients"][0]["artifacts"][0]["dest_path"])
        rc = cli.main(
            ["--manifest", write(tmp_path, data), "--stage", "--dry-run"]
        )
        out = capsys.readouterr().out
        assert rc == 0
        assert "dry run" in out
        assert "[create]" in out
        assert not dest.exists(), "--dry-run must not write"

    def test_stage_places_the_artifact_and_exits_zero(self, cli, tmp_path, capsys):
        data = self.staged_manifest(tmp_path)
        dest = Path(data["clients"][0]["artifacts"][0]["dest_path"])
        rc = cli.main(["--manifest", write(tmp_path, data), "--stage"])
        out = capsys.readouterr().out
        assert rc == 0, out
        assert dest.read_bytes() == b"harness-bytes"
        assert "postconditions: PASS" in out

    def test_stage_is_idempotent(self, cli, tmp_path, capsys):
        data = self.staged_manifest(tmp_path)
        path = write(tmp_path, data)
        assert cli.main(["--manifest", path, "--stage"]) == 0
        capsys.readouterr()
        assert cli.main(["--manifest", path, "--stage"]) == 0
        assert "PASS" in capsys.readouterr().out

    def test_missing_source_exits_three_with_nothing_written(self, cli, tmp_path, capsys):
        data = self.staged_manifest(tmp_path)
        Path(data["artifacts"][0]["source_path"]).unlink()
        dest = Path(data["clients"][0]["artifacts"][0]["dest_path"])
        rc = cli.main(["--manifest", write(tmp_path, data), "--stage"])
        out = capsys.readouterr().out
        assert rc == 3
        assert "nothing was written" in out
        assert not dest.exists()

    def test_check_gates_stage(self, cli, tmp_path, capsys):
        """A manifest that fails STATIC must never proceed to write bytes."""
        data = self.staged_manifest(tmp_path)
        data["clients"][0]["ports"]["loopback_control"] = 2456  # production deny
        dest = Path(data["clients"][0]["artifacts"][0]["dest_path"])
        rc = cli.main(["--manifest", write(tmp_path, data), "--check", "--stage"])
        out = capsys.readouterr().out
        assert rc == 1
        assert "S2-PRODUCTION-PORT-DENY" in out
        assert not dest.exists(), "STATIC failure must gate staging"

    def test_dry_run_json_is_machine_readable(self, cli, tmp_path, capsys):
        data = self.staged_manifest(tmp_path)
        cli.main(["--manifest", write(tmp_path, data), "--stage", "--dry-run", "--json"])
        payload = json.loads(capsys.readouterr().out)
        assert payload["phase"] == "stage"
        assert payload["dry_run"] is True
        assert payload["placements"][0]["client"] == "solo"
        assert payload["placements"][0]["action"] == "create"

    def test_stage_json_reports_postconditions(self, cli, tmp_path, capsys):
        data = self.staged_manifest(tmp_path)
        cli.main(["--manifest", write(tmp_path, data), "--stage", "--json"])
        payload = json.loads(capsys.readouterr().out)
        assert payload["ok"] is True
        assert payload["failures"] == []
        assert payload["staged"][0]["action"] == "create"

    def test_dry_run_without_stage_is_rejected(self, cli, tmp_path, capsys):
        rc = cli.main(
            ["--manifest", write(tmp_path, minimal(tmp_path)), "--check", "--dry-run"]
        )
        assert rc == 2
        assert "--dry-run applies to --stage" in capsys.readouterr().out

    def test_no_mode_message_names_both_phases(self, cli, tmp_path, capsys):
        cli.main(["--manifest", write(tmp_path, minimal(tmp_path))])
        out = capsys.readouterr().out
        assert "--check" in out and "--stage" in out and "--verify" in out


class TestVerifyCli:
    """`--verify` CLI surface (issue #456).

    These exercise the REAL verify environment against a tmp_path tree and this
    machine's own free ports — no stub, no host rig path, and nothing that can skip.
    The manifest declares the current uid so the credential read is a same-uid read
    that genuinely succeeds; the cross-uid case is #452's seam and is covered there.
    """

    def verify_manifest(self, tmp_path):
        import os
        import socket

        data = minimal(tmp_path)
        data["clients"][0]["uid"] = os.getuid()
        data["clients"][0]["artifacts"][0]["dest_path"] = str(
            tmp_path / "game" / "BepInEx" / "plugins" / "harness.dll"
        )

        # A pair of ports the OS just told us are free, rather than two constants that
        # collide with whatever this machine happens to be running.
        free = []
        holders = [socket.socket(socket.AF_INET, socket.SOCK_STREAM) for _ in range(2)]
        for holder in holders:
            holder.bind(("127.0.0.1", 0))
            free.append(holder.getsockname()[1])
        for holder in holders:
            holder.close()
        data["clients"][0]["ports"]["loopback_control"] = free[0]
        data["clients"][0]["ports"]["valbridge_gabp"] = free[1]

        # The join-delivery chain PROVISION would have left behind.
        sidecar = tmp_path / "launch-env" / "solo.env"
        sidecar.parent.mkdir(parents=True, exist_ok=True)
        sidecar.write_text("SBPR_QA_CONNECT=127.0.0.1:2476\n", encoding="utf-8")
        wrapper = tmp_path / "run-solo.sh"
        wrapper.write_text(
            '. "$SBPR_QA_LAUNCH_ENV_FILE"\n'
            "SBPR_QA_CONNECT_ARGS=(+connect \"$SBPR_QA_CONNECT\")\n"
            'exec "$RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"\n',
            encoding="utf-8",
        )
        data["clients"][0]["launcher"] = {
            "kind": "direct_exec",
            "launch_env_path": str(sidecar),
            "wrapper_path": str(wrapper),
        }
        return data

    def test_verify_after_stage_reports_ready(self, cli, tmp_path, capsys):
        data = self.verify_manifest(tmp_path)
        path = write(tmp_path, data)
        assert cli.main(["--manifest", path, "--stage"]) == 0
        capsys.readouterr()

        rc = cli.main(["--manifest", path, "--verify"])
        out = capsys.readouterr().out
        assert rc == 0, out
        assert "READY" in out and "solo" in out

    def test_unstaged_artifact_makes_verify_exit_one_and_name_the_client(
        self, cli, tmp_path, capsys
    ):
        """The twelve-day failure, caught by a file read instead of a ten-minute boot."""
        data = self.verify_manifest(tmp_path)
        rc = cli.main(["--manifest", write(tmp_path, data), "--verify"])
        out = capsys.readouterr().out

        assert rc == 1
        assert "NOT READY" in out
        assert "solo" in out
        assert "ABSENT" in out

    def test_verify_json_is_machine_readable(self, cli, tmp_path, capsys):
        data = self.verify_manifest(tmp_path)
        path = write(tmp_path, data)
        cli.main(["--manifest", path, "--stage"])
        capsys.readouterr()

        cli.main(["--manifest", path, "--verify", "--json"])
        payload = json.loads(capsys.readouterr().out)
        assert payload["phase"] == "verify"
        assert payload["ok"] is True
        assert payload["clients"][0]["client"] == "solo"
        assert payload["not_ready"] == []
        criteria = {c["criterion"] for c in payload["clients"][0]["criteria"]}
        assert criteria == {
            "V1-ARTIFACTS-VERIFIED",
            "V2-CREDENTIAL-READABLE-BY-CONSUMER",
            "V3-JOIN-IN-LAUNCH-PATH",
            "V4-PORTS-DISJOINT-AND-FREE",
        }

    def test_verify_reports_the_join_rung_it_actually_used(self, cli, tmp_path, capsys):
        """Honesty contract at the CLI boundary: no process ran, so not proven live."""
        data = self.verify_manifest(tmp_path)
        path = write(tmp_path, data)
        cli.main(["--manifest", path, "--stage"])
        capsys.readouterr()

        cli.main(["--manifest", path, "--verify", "--json"])
        payload = json.loads(capsys.readouterr().out)
        join = next(
            c
            for c in payload["clients"][0]["criteria"]
            if c["criterion"] == "V3-JOIN-IN-LAUNCH-PATH"
        )
        assert join["ok"] is True
        assert join["method"] == "staged-delivery"
        assert join["proven_live"] is False

    def test_check_gates_verify(self, cli, tmp_path, capsys):
        data = self.verify_manifest(tmp_path)
        data["clients"][0]["ports"]["loopback_control"] = 2456  # production deny
        rc = cli.main(["--manifest", write(tmp_path, data), "--check", "--verify"])
        out = capsys.readouterr().out
        assert rc == 1
        assert "S2-PRODUCTION-PORT-DENY" in out
        assert "VERIFY" not in out, "STATIC failure must gate VERIFY"

    def test_stage_gates_verify(self, cli, tmp_path, capsys):
        """Verifying a tree staging refused to finish reports against untrusted bytes."""
        data = self.verify_manifest(tmp_path)
        Path(data["artifacts"][0]["source_path"]).unlink()
        rc = cli.main(["--manifest", write(tmp_path, data), "--stage", "--verify"])
        out = capsys.readouterr().out
        assert rc == 3
        assert "VERIFY" not in out

    def test_stage_then_verify_in_one_invocation(self, cli, tmp_path, capsys):
        rc = cli.main(
            ["--manifest", write(tmp_path, self.verify_manifest(tmp_path)), "--stage", "--verify"]
        )
        out = capsys.readouterr().out
        assert rc == 0, out
        assert "postconditions: PASS" in out
        assert "VERIFY: READY" in out

    def test_dry_run_stage_does_not_proceed_to_verify(self, cli, tmp_path, capsys):
        """A dry run stages nothing, so there is nothing yet to read back."""
        rc = cli.main(
            [
                "--manifest",
                write(tmp_path, self.verify_manifest(tmp_path)),
                "--stage",
                "--dry-run",
                "--verify",
            ]
        )
        out = capsys.readouterr().out
        assert rc == 0
        assert "dry run" in out
        assert "VERIFY" not in out

    def test_busy_port_makes_verify_fail(self, cli, tmp_path, capsys):
        """The half no static check can reach: disjoint in data, held on the machine."""
        import socket

        data = self.verify_manifest(tmp_path)
        path = write(tmp_path, data)
        cli.main(["--manifest", path, "--stage"])
        capsys.readouterr()

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as holder:
            holder.bind(("127.0.0.1", data["clients"][0]["ports"]["valbridge_gabp"]))
            holder.listen(1)
            rc = cli.main(["--manifest", path, "--verify"])
        out = capsys.readouterr().out

        assert rc == 1
        assert "V4-PORTS-DISJOINT-AND-FREE" in out
        assert "already in use" in out

    def test_missing_sidecar_makes_verify_fail(self, cli, tmp_path, capsys):
        data = self.verify_manifest(tmp_path)
        path = write(tmp_path, data)
        cli.main(["--manifest", path, "--stage"])
        capsys.readouterr()
        Path(data["clients"][0]["launcher"]["launch_env_path"]).unlink()

        rc = cli.main(["--manifest", path, "--verify"])
        out = capsys.readouterr().out
        assert rc == 1
        assert "V3-JOIN-IN-LAUNCH-PATH" in out

    def test_unreadable_manifest_exits_two(self, cli, tmp_path, capsys):
        rc = cli.main(["--manifest", str(tmp_path / "nope.json"), "--verify"])
        assert rc == 2


class TestShippedExample:
    """The documented example must actually parse under the real schema — a stale
    example is a silent trap for whoever writes the next manifest."""

    def test_example_parses(self):
        from runner_core.arrange_manifest import ArrangeManifest

        manifest = ArrangeManifest.parse(json.loads(EXAMPLE.read_text(encoding="utf-8")))
        assert manifest.actors == ["client_a", "client_b"]

    def test_example_clients_share_nothing(self):
        from runner_core.arrange_manifest import ArrangeManifest

        m = ArrangeManifest.parse(json.loads(EXAMPLE.read_text(encoding="utf-8")))
        a, b = m.clients
        assert a.uid != b.uid
        assert a.user != b.user
        assert a.steam_account != b.steam_account
        assert a.game_root != b.game_root
        assert a.binary_path != b.binary_path
        assert a.launcher.kind != b.launcher.kind
        assert set(a.bound_ports.values()).isdisjoint(b.bound_ports.values())
        assert a.qa_profile != b.qa_profile
        assert a.join is not None and b.join is not None
        assert a.launcher.params != b.launcher.params
        assert (
            a.credentials["server_password"].path
            != b.credentials["server_password"].path
        )

    def test_example_only_fails_on_the_placeholder_pins(self):
        """Every check except the artifact pins should pass; the pins are dummies.

        The example names REAL host paths (artifact sources, launch wrappers) because
        it documents the actual rig. Those files exist only on the QA host, so this
        test must not depend on them: it supplies a stub environment where every named
        file is present and correct. Otherwise the test asserts "this machine is the QA
        box" rather than "the example is well-formed", which is how it broke in CI.

        The wrapper text is the real deployed shape — sources the sidecar, builds the
        fragment, appends it after "$@" for the Steam path — so the join-delivery seam
        (#453) is still genuinely exercised against the example's declared launchers.
        """
        from runner_core.arrange_static import (
            P_ARTIFACT_PINS,
            StaticEnvironment,
            arrange_static,
        )

        wrapper = (
            'SBPR_QA_LAUNCH_ENV_FILE="$HOME/.local/share/sbpr-qa/launch-env/x.env"\n'
            '. "$SBPR_QA_LAUNCH_ENV_FILE"\n'
            "SBPR_QA_CONNECT_ARGS=()\n"
            'if [[ -n "${SBPR_QA_CONNECT:-}" ]]; then\n'
            '  SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT")\n'
            "fi\n"
            'exec "$RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"\n'
        )
        env = StaticEnvironment(
            path_exists=lambda p: True,
            # Any hash: the pins are deliberate placeholders, so S3 is the one
            # precondition this example is expected to fail.
            hash_file=lambda p: "0" * 64,
            read_text=lambda p: wrapper,
            find_named_files=lambda _root, _name: (),
        )
        report = arrange_static(json.loads(EXAMPLE.read_text(encoding="utf-8")), env)
        offenders = {f.precondition for f in report.failures}
        assert offenders <= {P_ARTIFACT_PINS}, report.render()
