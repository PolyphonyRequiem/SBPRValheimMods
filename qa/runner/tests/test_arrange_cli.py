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
