"""M6-COMPOSE CLI coverage — `--live` reaches the executor, does NOT defer (ADR-0009 §6).

The exact gap that blocked three M6 attempts: `--live` with all preconditions satisfied
printed "UNLOCKED but not executed here" and returned. These tests load the hyphenated
runner module and prove:

  * `--live` (valid sentinel + verified overlay pins + a run descriptor) reaches the
    injected executor and reports a composed verdict — NOT the old deferral message;
  * the deferral string is GONE from the runner source (regression guard);
  * fail-closed gating survives: bare `--live`, missing inputs, a drifted overlay pin,
    and a non-disposable sentinel each REFUSE (exit 2) without ever calling the executor;
  * `--dry-run` remains the default and works.

The executor is injected as a stub, so nothing launches a game or mutates a file.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
import sys

import pytest

RUNNER_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if RUNNER_DIR not in sys.path:
    sys.path.insert(0, RUNNER_DIR)

RUNNER_PY = os.path.join(RUNNER_DIR, "sbpr-qa-t022.py")


def _load_cli():
    spec = importlib.util.spec_from_file_location("sbpr_qa_t022_cli", RUNNER_PY)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def _sha256_file(path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        h.update(fh.read())
    return h.hexdigest()


def _write_overlay(tmp_path, *, lane="disposable", include_prod_deny=True, drift=False):
    """Write a sentinel + a self-consistent overlay manifest that the preflight accepts."""
    from runner_core.live_preflight import OVERLAY_PARTS, _fold_digest

    sentinel = {
        "kind": "sbpr-qa-overlay-lane-sentinel",
        "lane": lane,
    }
    if include_prod_deny:
        sentinel["production_deny"] = {"worlds": ["Niflheim:2456", "Heistan:2466"]}
    sentinel_path = tmp_path / "lane_sentinel.json"
    sentinel_path.write_text(json.dumps(sentinel, sort_keys=True, indent=2) + "\n")

    parts = {p: hashlib.sha256(p.encode()).hexdigest() for p in OVERLAY_PARTS}
    # The runner recomputes lane_sentinel from the file it is handed.
    parts["lane_sentinel"] = _sha256_file(str(sentinel_path))
    digest = _fold_digest(parts)
    if drift:
        digest = "0" * 64  # break the folded digest → preflight refuses
    manifest = {
        "kind": "sbpr-qa-overlay-manifest",
        "parts": parts,
        "overlay_digest": digest,
    }
    manifest_path = tmp_path / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, sort_keys=True, indent=2) + "\n")
    return str(sentinel_path), str(manifest_path)


class _FakeVerdict:
    def __init__(self, verdict="PASS"):
        self.verdict = verdict
        self.passed = verdict == "PASS"
        self.evidence = type("Ev", (), {
            "legs": {"ISSUE": "pass", "UPGRADE": "pass", "TRANSFER": "pass", "TAMPER": "pass"},
            "lease_held": True, "pins_verified": True, "cleanup_confirmed": True,
            "receipts_correlated": 4, "failure_reason": None, "failure_kind": None,
        })()


class _FakeReport:
    def __init__(self, verdict="PASS"):
        self.verdict = _FakeVerdict(verdict)
        self.teardown_completed = True
        self.teardown_errors = []


class _SteamResult:
    """Stand-in for a SteamProbeResult on the ready path."""

    def __init__(self, ready=True, user="polyphonyrequiem"):
        self.ready = ready
        self.target_user = user
        self.message = f"Steam is running and ready for {user!r}."


def _steam_ready(user=None):
    return _SteamResult(ready=True, user=user or "polyphonyrequiem")


def _steam_down(user=None):
    from runner_core.steam_preflight import SteamNotReady

    raise SteamNotReady(
        f"Steam is NOT running for {user or 'polyphonyrequiem'!r} — the QA client will "
        "crash ~6s into boot. [ensure-steam.sh --check exit 4]\n  steam.pipe : present"
    )


# --------------------------------------------------------------------------- #
# The one that matters: --live EXECUTES.
# --------------------------------------------------------------------------- #

def test_live_with_all_preconditions_reaches_executor(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    called = {}

    def stub_runner(descriptor_path):
        called["path"] = descriptor_path
        return _FakeReport("PASS")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=stub_runner,
        steam_probe=_steam_ready,
    )
    out = capsys.readouterr().out
    # The executor WAS invoked with the descriptor — the run actually drove.
    assert called["path"] == str(descriptor)
    assert rc == 0
    assert "LIVE-EXECUTE" in out
    assert "verdict: PASS" in out
    assert "teardown: complete" in out
    # The blocking deferral message must NOT appear on the success path.
    assert "UNLOCKED but NOT executed here" not in out
    assert "did not perform a live qualification" not in out


def test_live_executor_failure_reports_fail_not_defer(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=lambda p: _FakeReport("FAIL"),
        steam_probe=_steam_ready,
    )
    out = capsys.readouterr().out
    assert rc == 1
    assert "verdict: FAIL" in out
    assert "UNLOCKED but NOT executed here" not in out


def test_live_refuses_when_steam_not_running(tmp_path, capsys) -> None:
    # M6-STEAMGATE (A): all overlay/sentinel/descriptor preconditions pass, but the
    # target user's Steam is down. Preflight must fail closed (exit 2) with a clear
    # "Steam not running" message and NEVER reach the executor — a client launched now
    # would crash ~6s in with "Steamworks is not initialized".
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=_never,          # executor must not be reached
        steam_probe=_steam_down,
    )
    out = capsys.readouterr().out
    assert rc == 2
    assert "REFUSED" in out
    assert "Steam" in out
    assert "Steam is NOT running" in out


def test_live_steam_gate_targets_descriptor_steam_user(tmp_path, capsys) -> None:
    # The descriptor's optional `steam_user` selects which user's Steam is required
    # (e.g. "valbot" for the second lane). The gate must probe THAT user, not assume.
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text(json.dumps({"steam_user": "valbot"}))

    seen = {}

    def probe(user=None):
        seen["user"] = user
        return _SteamResult(ready=True, user=user or "polyphonyrequiem")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=lambda p: _FakeReport("PASS"),
        steam_probe=probe,
    )
    assert rc == 0
    assert seen["user"] == "valbot"


def test_deferral_string_is_gone_from_runner_source() -> None:
    # Regression guard: the exact string that masked the missing executor for three
    # M6 attempts must not survive anywhere in the runner CLI.
    with open(RUNNER_PY, "r", encoding="utf-8") as fh:
        src = fh.read()
    assert "UNLOCKED but NOT executed here" not in src
    assert "it did not perform a live qualification" not in src


# --------------------------------------------------------------------------- #
# Fail-closed gating survives — executor never reached on a refusal.
# --------------------------------------------------------------------------- #

def _never(_path):
    raise AssertionError("executor must not be reached on a refusal")


def test_bare_live_refuses_without_executing(capsys) -> None:
    cli = _load_cli()
    rc = cli.main(["--live"], live_runner=_never)
    assert rc == 2
    assert "REFUSED" in capsys.readouterr().out


def test_live_missing_overlay_refuses(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, _ = _write_overlay(tmp_path)
    rc = cli.main(["--live", "--lane-sentinel", sentinel], live_runner=_never)
    assert rc == 2


def test_live_drifted_overlay_refuses(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path, drift=True)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=_never,
    )
    assert rc == 2
    assert "REFUSED" in capsys.readouterr().out


def test_live_nondisposable_sentinel_refuses(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path, lane="production")
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest,
         "--run-descriptor", str(descriptor)],
        live_runner=_never,
    )
    assert rc == 2


def test_live_unlocked_without_descriptor_refuses_to_execute(tmp_path, capsys) -> None:
    # Preflight passes but no descriptor: it must NOT fabricate an execution, and must
    # NOT resurrect the old deferral. It refuses (exit 2) and says a descriptor is needed.
    cli = _load_cli()
    sentinel, manifest = _write_overlay(tmp_path)
    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", manifest],
        live_runner=_never,
    )
    out = capsys.readouterr().out
    assert rc == 2
    assert "no --run-descriptor" in out
    assert "UNLOCKED but NOT executed here" not in out


def test_dry_run_still_default_and_passes(capsys) -> None:
    cli = _load_cli()
    rc = cli.main(["--dry-run", "--scenario", "success"])
    assert rc == 0
    assert "verdict: PASS" in capsys.readouterr().out


# --------------------------------------------------------------------------- #
# ARRANGE gate (#457 migrate): --live arranges through the single authority
# before anything launches, and REFUSES the launch when it is not READY.
# --------------------------------------------------------------------------- #

def _arrange_manifest(tmp_path, *, production_port=False):
    """A real one-client arrange manifest with real files on disk.

    Deliberately NOT a stub of the chain: these tests drive the REAL four phases so
    they prove the launcher is gated by an arrangement that actually happened, not by
    a mock that reported it had. The declared uid is the CURRENT one, so the
    credential/port work is genuine rather than silently skipped.
    """
    import hashlib
    import json as _json
    import os
    import socket

    root = tmp_path / "arrgame"
    (root / "BepInEx" / "plugins").mkdir(parents=True, exist_ok=True)
    src = tmp_path / "arrbuild" / "harness.dll"
    src.parent.mkdir(parents=True, exist_ok=True)
    src.write_bytes(b"harness-bytes")
    digest = hashlib.sha256(b"harness-bytes").hexdigest()

    free = []
    holders = [socket.socket(socket.AF_INET, socket.SOCK_STREAM) for _ in range(2)]
    for holder in holders:
        holder.bind(("127.0.0.1", 0))
        free.append(holder.getsockname()[1])
    for holder in holders:
        holder.close()

    sidecar = tmp_path / "arr-launch-env" / "solo.env"
    sidecar.parent.mkdir(parents=True, exist_ok=True)
    sidecar.write_text("SBPR_QA_CONNECT=127.0.0.1:2476\n", encoding="utf-8")
    wrapper = tmp_path / "arr-run-solo.sh"
    wrapper.write_text(
        '. "$SBPR_QA_LAUNCH_ENV_FILE"\n'
        'SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT")\n'
        'exec "$RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"\n',
        encoding="utf-8",
    )

    data = {
        "kind": "sbpr-qa-arrange-manifest",
        "version": 3,
        "run_id": "t022-run-migrate-test",
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
                "uid": os.getuid(),
                "user": "poly",
                "steam_account": "76561197965627562",
                "game_root": str(root),
                "binary_path": str(root / "valheim.x86_64"),
                "plugins_dir": str(root / "BepInEx" / "plugins"),
                "launcher": {
                    "kind": "direct_exec",
                    "launch_env_path": str(sidecar),
                    "wrapper_path": str(wrapper),
                },
                "ports": {
                    # A production port here is the fail case: 2456 is Niflheim, a
                    # REAL world, and the deny must survive the cutover intact.
                    "loopback_control": 2456 if production_port else free[0],
                    "valbridge_gabp": free[1],
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
    path = tmp_path / "arrange-manifest.json"
    path.write_text(_json.dumps(data), encoding="utf-8")
    return str(path), root / "BepInEx" / "plugins" / "harness.dll"


def test_live_arranges_through_the_single_authority_before_launching(
    tmp_path, capsys
) -> None:
    """§3 P1, executed: the run is arranged by the chain, then it launches.

    Order matters and is asserted: the arrange report must appear BEFORE the executor
    is reached, because arranging after a launch would arrange a client that has
    already booted with whatever was on disk.
    """
    cli = _load_cli()
    sentinel, overlay = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    arrange, staged = _arrange_manifest(tmp_path)

    called = {}

    def stub_runner(descriptor_path):
        # The artifact must ALREADY be staged by the time the launcher runs — proof
        # the arrangement genuinely happened first rather than being reported.
        called["staged_at_launch"] = staged.exists()
        return _FakeReport("PASS")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", overlay,
         "--run-descriptor", str(descriptor), "--arrange-manifest", arrange],
        live_runner=stub_runner,
        steam_probe=_steam_ready,
    )
    out = capsys.readouterr().out

    assert rc == 0, out
    assert called["staged_at_launch"] is True
    assert "LIVE-ARRANGE" in out
    assert "arrange CUTOVER: READY" in out
    assert out.index("LIVE-ARRANGE") < out.index("LIVE-EXECUTE")


def test_live_refuses_to_launch_when_the_run_is_not_ready(tmp_path, capsys) -> None:
    """Fail-closed, and the launcher is never reached.

    A guard that merely logs a complaint and launches anyway is not a guard. The
    executor here raises if invoked, so reaching it turns this test red rather than
    letting an unarranged run through.
    """
    cli = _load_cli()
    sentinel, overlay = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    arrange, staged = _arrange_manifest(tmp_path, production_port=True)

    def _never(_path):
        raise AssertionError("executor reached despite the run not being READY")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", overlay,
         "--run-descriptor", str(descriptor), "--arrange-manifest", arrange],
        live_runner=_never,
        steam_probe=_steam_ready,
    )
    out = capsys.readouterr().out

    assert rc == 2
    assert "NOT READY" in out
    assert "S2-PRODUCTION-PORT-DENY" in out
    assert "LIVE-EXECUTE" not in out
    # And it did not paper over the missing precondition by staging anyway.
    assert not staged.exists()


def test_arrange_refusal_is_not_softened_into_a_retry(tmp_path, capsys) -> None:
    """§6: no retry may substitute for a correct precondition.

    Re-invoking with the same broken manifest must refuse identically, not eventually
    succeed. A gate that yields on the second attempt is a gate that yields.
    """
    cli = _load_cli()
    sentinel, overlay = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    arrange, _staged = _arrange_manifest(tmp_path, production_port=True)

    def _never(_path):
        raise AssertionError("executor reached despite the run not being READY")

    args = ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", overlay,
            "--run-descriptor", str(descriptor), "--arrange-manifest", arrange]
    for _ in range(3):
        assert cli.main(args, live_runner=_never, steam_probe=_steam_ready) == 2
        capsys.readouterr()


def test_live_without_an_arrange_manifest_still_runs_the_legacy_path(
    tmp_path, capsys
) -> None:
    """The migrate step is opt-in: the descriptor-derived path is untouched.

    This is what makes the step independently green. The contract step removes the
    alternative; until then a caller that has not moved must keep working.
    """
    cli = _load_cli()
    sentinel, overlay = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", overlay,
         "--run-descriptor", str(descriptor)],
        live_runner=lambda p: _FakeReport("PASS"),
        steam_probe=_steam_ready,
    )
    out = capsys.readouterr().out
    assert rc == 0
    assert "LIVE-ARRANGE" not in out


def test_unreadable_arrange_manifest_refuses_before_launching(tmp_path, capsys) -> None:
    cli = _load_cli()
    sentinel, overlay = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")

    def _never(_path):
        raise AssertionError("executor reached with an unreadable arrange manifest")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", overlay,
         "--run-descriptor", str(descriptor),
         "--arrange-manifest", str(tmp_path / "nope.json")],
        live_runner=_never,
        steam_probe=_steam_ready,
    )
    assert rc == 2
    assert "cannot read arrange manifest" in capsys.readouterr().out


def test_arrange_gate_runs_after_the_steam_precondition(tmp_path, capsys) -> None:
    """Steam is checked first, and a Steam refusal means nothing is arranged.

    Ordering is not cosmetic: STAGE writes to a client's plugin tree, so arranging
    before knowing the client can even boot would mutate a filesystem for a run that
    was always going to be refused.
    """
    cli = _load_cli()
    sentinel, overlay = _write_overlay(tmp_path)
    descriptor = tmp_path / "descriptor.json"
    descriptor.write_text("{}")
    arrange, staged = _arrange_manifest(tmp_path)

    def _never(_path):
        raise AssertionError("executor reached despite Steam being down")

    rc = cli.main(
        ["--live", "--lane-sentinel", sentinel, "--overlay-manifest", overlay,
         "--run-descriptor", str(descriptor), "--arrange-manifest", arrange],
        live_runner=_never,
        steam_probe=_steam_down,
    )
    out = capsys.readouterr().out
    assert rc == 2
    assert "Steam precondition failed" in out
    assert "LIVE-ARRANGE" not in out
    assert not staged.exists(), "arranged a filesystem for a run Steam had already refused"
