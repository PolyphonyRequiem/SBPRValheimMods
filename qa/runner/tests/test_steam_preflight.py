"""Steam-running precondition tests (M6-STEAMGATE, defect A).

The QA client cannot boot without a RUNNING Steam owned by the user GABS launches it
as; with none it crashes ~6s into boot with `Steamworks is not initialized`. These
tests drive `steam_preflight` through an injected `runner` seam so NO real Steam and
NO subprocess are touched — every branch of the exit-code contract is exercised.

The predicate itself (live process AND steam.pipe AND a live pidfile) lives in the
committed `scripts/ensure-steam.sh`; Python shells out to `--check` and branches on
its exit code. The critical case: a STALE `steam.pipe` with no live process behind it
must NOT be treated as ready (that is the real host state that a naive pipe-presence
check would wrongly pass). We prove that here by modelling the script's exit-4-with-
"steam.pipe : present" output and asserting the preflight still fails closed.
"""
from __future__ import annotations

import subprocess
from dataclasses import dataclass

import pytest

from runner_core.steam_preflight import (
    EXIT_NOT_READY,
    EXIT_READY,
    SteamNotReady,
    SteamProbeResult,
    probe_steam_ready,
    require_steam_running,
)


@dataclass
class _FakeCompleted:
    """Stand-in for subprocess.CompletedProcess: the fields steam_preflight reads."""

    returncode: int
    stdout: str = ""
    stderr: str = ""


def _runner(returncode: int, stdout: str = "", stderr: str = ""):
    """Build an injectable runner that records argv and returns a canned result."""
    calls = []

    def run(argv):
        calls.append(list(argv))
        return _FakeCompleted(returncode=returncode, stdout=stdout, stderr=stderr)

    run.calls = calls  # type: ignore[attr-defined]
    return run


# --------------------------------------------------------------------------- #
# Ready path.
# --------------------------------------------------------------------------- #

def test_probe_reports_ready_on_exit_zero() -> None:
    run = _runner(EXIT_READY, stdout="[steam] OK Steam already running and ready\n")
    result = probe_steam_ready("polyphonyrequiem", runner=run)
    assert result.ready is True
    assert result.exit_code == 0
    assert result.target_user == "polyphonyrequiem"
    # It invoked ensure-steam.sh --check --user <user>; --check starts nothing.
    argv = run.calls[0]  # type: ignore[attr-defined]
    assert argv[0] == "bash"
    assert argv[1].endswith("scripts/ensure-steam.sh")
    assert "--check" in argv
    assert argv[-2:] == ["--user", "polyphonyrequiem"]


def test_require_steam_running_returns_result_when_ready() -> None:
    run = _runner(EXIT_READY)
    result = require_steam_running("polyphonyrequiem", runner=run)
    assert isinstance(result, SteamProbeResult)
    assert result.ready is True


# --------------------------------------------------------------------------- #
# THE STALE-PIPE FALSE POSITIVE. A stale steam.pipe with no live process behind it
# is a real state on this host. The script's own predicate rejects it (exit 4 while
# reporting "steam.pipe : present"); the preflight must fail closed, NOT be fooled by
# the pipe being present.
# --------------------------------------------------------------------------- #

def test_stale_pipe_with_no_process_is_not_ready() -> None:
    # Faithfully model ensure-steam.sh --check output for the stale-pipe host state:
    # exit 4 (NOT ready) even though steam.pipe is present, because no live steam
    # process / live pidfile backs it.
    stale_output = (
        "[steam] !! Steam NOT ready for polyphonyrequiem\n"
        "  user       : polyphonyrequiem (/home/polyphonyrequiem)\n"
        "  steam pids : <none>\n"
        "  steam.pipe : present\n"
    )
    run = _runner(EXIT_NOT_READY, stderr=stale_output)
    result = probe_steam_ready("polyphonyrequiem", runner=run)
    # Pipe present, but NOT ready — the three-part predicate in the script rejected it.
    assert result.ready is False
    assert result.exit_code == EXIT_NOT_READY
    assert "steam.pipe : present" in result.detail

    # And the fail-closed assertion raises with an actionable message.
    with pytest.raises(SteamNotReady) as ei:
        require_steam_running("polyphonyrequiem", runner=run)
    msg = str(ei.value)
    assert "Steam is NOT running" in msg
    assert "polyphonyrequiem" in msg
    # It surfaces the script's own state report so the operator sees the stale pipe.
    assert "steam.pipe : present" in msg


# --------------------------------------------------------------------------- #
# Not-ready and fail-closed-on-ambiguity paths.
# --------------------------------------------------------------------------- #

def test_require_raises_on_not_ready() -> None:
    run = _runner(EXIT_NOT_READY, stderr="[steam] !! Steam NOT ready\n")
    with pytest.raises(SteamNotReady):
        require_steam_running("valbot", runner=run)


def test_unknown_exit_code_fails_closed() -> None:
    # Any non-0/4 exit (e.g. 2 = no X display, 3 = no install) is treated as NOT ready
    # so the preflight never proceeds on an ambiguous signal.
    run = _runner(2, stderr="[steam] FATAL no X display found\n")
    result = probe_steam_ready("polyphonyrequiem", runner=run)
    assert result.ready is False
    assert result.exit_code == 2
    with pytest.raises(SteamNotReady):
        require_steam_running("polyphonyrequiem", runner=run)


def test_missing_script_fails_closed() -> None:
    def run(argv):
        raise FileNotFoundError("bash: ensure-steam.sh: No such file")

    result = probe_steam_ready("polyphonyrequiem", runner=run)
    assert result.ready is False
    assert result.exit_code == 127
    assert "refusing to assume Steam is up" in result.detail


def test_timeout_fails_closed() -> None:
    def run(argv):
        raise subprocess.TimeoutExpired(cmd=argv, timeout=30.0)

    result = probe_steam_ready("polyphonyrequiem", runner=run, timeout_s=30.0)
    assert result.ready is False
    assert result.exit_code == 124
    assert "timed out" in result.detail


# --------------------------------------------------------------------------- #
# Target-user wiring: absent an explicit user, the CURRENT user is checked (the user
# GABS launches the primary client as) and no --user flag is passed.
# --------------------------------------------------------------------------- #

def test_default_targets_current_user_without_user_flag(monkeypatch) -> None:
    monkeypatch.setenv("USER", "polyphonyrequiem")
    run = _runner(EXIT_READY)
    result = probe_steam_ready(runner=run)
    assert result.target_user == "polyphonyrequiem"
    argv = run.calls[0]  # type: ignore[attr-defined]
    assert "--user" not in argv  # current-user check needs no --user
    assert "--check" in argv


def test_explicit_valbot_user_is_passed_through() -> None:
    run = _runner(EXIT_NOT_READY)
    result = probe_steam_ready("valbot", runner=run)
    assert result.target_user == "valbot"
    argv = run.calls[0]  # type: ignore[attr-defined]
    assert argv[-2:] == ["--user", "valbot"]
    # The not-ready message flags that valbot may need a one-time interactive login.
    assert "valbot" in result.message
