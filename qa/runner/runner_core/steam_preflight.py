"""Steam-running precondition for the T022 live preflight (M6-STEAMGATE).

WHY THIS EXISTS
---------------
The QA client cannot boot when the *target user's* Steam is not running. Valheim's
Steamworks needs a LIVE Steam client owned by the same user; GABS forks the game
directly, so with no Steam up the game crashes ~6s into boot with

    InvalidOperationException: Steamworks is not initialized.
      SteamUtils.IsSteamRunningOnSteamDeck () / ZInput.Load () / SceneLoader.Awake ()

before the scene ever activates. `steam_appid.txt` is necessary but NOT sufficient:
it lets a directly-launched binary identify itself to a RUNNING Steam, it cannot
start one. This was miscategorised for weeks as an "intermittent ValBridge startup
deadlock"; it is deterministic, which is why the boot retry budget never helped.

THE PREDICATE MUST NOT DRIFT
----------------------------
`scripts/ensure-steam.sh` already encodes the correct readiness predicate:

    a live `steam` process owned by the target user
      AND `~/.steam/steam.pipe` exists
      AND `~/.steam/steam.pid` points at a live PID.

A *stale* `steam.pipe` with no process behind it is a real state on this host, and a
naive pipe-presence check passes it. To avoid keeping two copies of that predicate
that can diverge, this module SHELLS OUT to `ensure-steam.sh --check` and branches on
its exit code (0 = ready, 4 = not ready) rather than reimplementing the three
conditions in Python. The one predicate lives in the shell script; Python only calls
it. `--check` starts nothing — it reports readiness only.

WHICH USER
----------
GABS launches the modded `valheim` client via `~/.gabs/config.json` gameId "valheim"
-> `~/.local/share/Trailborne/Valheim-Modded/run-trailborne.sh`, run by the
polyphonyrequiem GABS daemon (uid 1000) — the same user this runner runs as. The
observed crash log is under polyphonyrequiem's `~/.local/share/Trailborne`. So the
primary client's Steam is polyphonyrequiem's. The check therefore targets the current
user by default; a descriptor may override `steam_user` (e.g. "valbot" for the second
lane, which may need a one-time interactive Steam login no script can perform).

Engine-free: stdlib only. No product/game import. This module DECIDES readiness by
invoking the script; it launches no game and mutates no game state.
"""
from __future__ import annotations

import os
import subprocess
from dataclasses import dataclass
from typing import Callable, Optional, Sequence

# Path to the committed predicate script, resolved relative to the repo root
# (qa/runner/runner_core/ -> ../../../scripts/ensure-steam.sh).
_THIS_DIR = os.path.dirname(os.path.abspath(__file__))
ENSURE_STEAM_SCRIPT = os.path.normpath(
    os.path.join(_THIS_DIR, "..", "..", "..", "scripts", "ensure-steam.sh")
)

# ensure-steam.sh exit-code contract (see the script header). We branch only on
# these two; every other code is treated as "not ready" with the reported detail so
# the preflight fails closed rather than proceeding on an ambiguous signal.
EXIT_READY = 0
EXIT_NOT_READY = 4


class SteamNotReady(Exception):
    """The target user's Steam is not running/ready. Carries an actionable message."""


@dataclass(frozen=True)
class SteamProbeResult:
    """The outcome of one `ensure-steam.sh --check` invocation."""

    ready: bool
    target_user: str
    exit_code: int
    detail: str  # combined stdout/stderr from the script, for the operator message

    @property
    def message(self) -> str:
        if self.ready:
            return f"Steam is running and ready for {self.target_user!r}."
        return (
            f"Steam is NOT running for {self.target_user!r} — the QA client will crash "
            f"~6s into boot with 'Steamworks is not initialized' before the scene "
            f"activates. Start Steam for that user (a graphical session is required; "
            f"`scripts/ensure-steam.sh --user {self.target_user}` can do it, and "
            f"`valbot` may need a one-time interactive Steam login), then retry. "
            f"[ensure-steam.sh --check exit {self.exit_code}]"
            + (f"\n{self.detail.strip()}" if self.detail.strip() else "")
        )


def probe_steam_ready(
    target_user: Optional[str] = None,
    *,
    script_path: str = ENSURE_STEAM_SCRIPT,
    runner: Optional[Callable[[Sequence[str]], "subprocess.CompletedProcess"]] = None,
    timeout_s: float = 30.0,
) -> SteamProbeResult:
    """Probe Steam readiness for `target_user` by shelling out to ensure-steam.sh --check.

    `--check` reports readiness and starts NOTHING. Exit 0 => ready, exit 4 => not
    ready; any other exit is treated as not-ready (fail closed) with the script's
    output surfaced. `runner` is an injectable seam `(argv) -> CompletedProcess` so
    the test suite drives every branch with NO real Steam and NO subprocess; the
    default runs the real script. `target_user` defaults to the current user (the
    user GABS launches the primary client as).
    """
    user = target_user or _current_user()
    argv = ["bash", script_path, "--check"]
    if target_user is not None:
        argv += ["--user", target_user]

    run = runner if runner is not None else _default_runner(timeout_s)
    try:
        completed = run(argv)
    except FileNotFoundError as exc:
        # The predicate script is missing — cannot verify, so fail closed.
        return SteamProbeResult(
            ready=False,
            target_user=user,
            exit_code=127,
            detail=f"cannot invoke ensure-steam.sh ({exc}); refusing to assume Steam is up",
        )
    except subprocess.TimeoutExpired:
        return SteamProbeResult(
            ready=False,
            target_user=user,
            exit_code=124,
            detail=f"ensure-steam.sh --check timed out after {timeout_s}s",
        )

    detail = (getattr(completed, "stdout", "") or "") + (getattr(completed, "stderr", "") or "")
    ready = completed.returncode == EXIT_READY
    return SteamProbeResult(
        ready=ready,
        target_user=user,
        exit_code=completed.returncode,
        detail=detail,
    )


def require_steam_running(
    target_user: Optional[str] = None,
    *,
    script_path: str = ENSURE_STEAM_SCRIPT,
    runner: Optional[Callable[[Sequence[str]], "subprocess.CompletedProcess"]] = None,
    timeout_s: float = 30.0,
) -> SteamProbeResult:
    """Fail-closed assertion: raise `SteamNotReady` unless the probe reports ready.

    This is the preflight entry point — it is satisfied ONLY by the full three-part
    predicate in ensure-steam.sh (live process AND pipe AND live pidfile), so a stale
    `steam.pipe` with no process behind it does NOT pass. Returns the probe result on
    success for logging.
    """
    result = probe_steam_ready(
        target_user,
        script_path=script_path,
        runner=runner,
        timeout_s=timeout_s,
    )
    if not result.ready:
        raise SteamNotReady(result.message)
    return result


def _current_user() -> str:
    for key in ("USER", "LOGNAME"):
        val = os.environ.get(key)
        if val:
            return val
    try:
        import getpass

        return getpass.getuser()
    except Exception:  # noqa: BLE001 — last-resort fallback
        return str(os.getuid())


def _default_runner(timeout_s: float) -> Callable[[Sequence[str]], "subprocess.CompletedProcess"]:
    def _run(argv: Sequence[str]) -> "subprocess.CompletedProcess":
        return subprocess.run(
            list(argv),
            capture_output=True,
            text=True,
            timeout=timeout_s,
            check=False,
        )

    return _run
