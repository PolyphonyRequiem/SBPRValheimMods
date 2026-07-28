"""LOCALLY-GATED acceptance test: a stale "running" GABS state does not swallow a
launch — the runner clears it and a fresh marker-carrying process forks (M6-GABSLIVE).

## The bug this test pins (run 8)

GABS never reaps the game processes it forks. A client that exits leaves a `<defunct>`
zombie parented to the long-lived GABS daemon. GABS's single-gameId liveness model uses
a name-based `ps` finder (`internal/process/controller.go:296-302`) that STILL matches
the zombie's `comm`, so `games.status` reports the gameId "running" and every subsequent
`games.start` is a silent no-op ("game X is already running",
`internal/mcp/stdio_server.go:761-764`). Run 8's six re-rolls all hit this: GABS launched
nothing, no marker-carrying child appeared, provenance never resolved.

## What this test actually does (NOT a stub)

It stands up a REAL `gabs` daemon (the deployed host binary) pointed at a throwaway game
whose launch target is a wrapper that execs a short-lived real process named
`valheim.x86_64`. It then:

  1. fires a REAL `games.start`, lets the child exit into a `<defunct>` zombie, and
     asserts the runtime bug reproduces: `games.status` says "running" and a second
     `games.start` fails with "already running" (the silent no-op);
  2. invokes the runner's OWN `_reset_gabs_state` closure — the exact production code
     path (`games.stop` → the daemon reaps its zombie child via `Wait()`) — and asserts
     GABS's view flips to "stopped" and the zombie is gone from `/proc`;
  3. fires `games.start` again and asserts a FRESH `valheim.x86_64` child actually forks.

There is no mock GABS and no fake process: the zombie is a real reaped-pending child and
the assertions read real kernel process state. Stubbing the boot is what let three prior
seam bugs ship, so this crosses the genuine daemon-fork + reap boundary.

## Locally-gated, not CI

CI has no `gabs` binary, so the test SKIPS there (guarded on the binary's presence). The
complementary always-runs coverage (the runner clears stale state before every attempt
and fails a forked-nothing launch fast on poll count) lives in `test_gabs_client_boot.py`.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import time
import urllib.request

import pytest

_GABS_BINARY = "/home/polyphonyrequiem/valheim/mcp-harness/GABS/gabs"
_HTTP_PORT = 8095  # distinct from the deployed :8080/:8081 and the other gated test :8093
_ENDPOINT = f"http://localhost:{_HTTP_PORT}/mcp"

_needs_gabs = pytest.mark.skipif(
    not os.path.exists(_GABS_BINARY),
    reason="locally-gated: real GABS daemon binary not present (e.g. in CI)",
)


def _wrapper_script(hold_seconds: int) -> str:
    # Execs a real process named valheim.x86_64 that stays alive for hold_seconds. A
    # LIVE child makes GABS's single-gameId model report "running" deterministically, so
    # a second games.start is the silent no-op that swallows a launch (the run-8 class).
    # When the child later exits it becomes the <defunct> zombie GABS never reaps; the
    # runner's reset (games.stop) reaps it either way.
    return (
        "#!/usr/bin/env bash\n"
        "set -e\n"
        'HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"\n'
        f'exec "$HERE/valheim.x86_64" {hold_seconds}\n'
    )


def _mcp(tool: str, game_id: str) -> dict:
    payload = json.dumps(
        {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
         "params": {"name": tool, "arguments": {"gameId": game_id}}}
    ).encode()
    req = urllib.request.Request(
        _ENDPOINT, data=payload, headers={"Content-Type": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(req, timeout=10) as resp:
        return json.loads(resp.read().decode())


def _status_text(game_id: str) -> str:
    body = _mcp("games.status", game_id)
    parts = body.get("result", {}).get("content", []) or []
    return " ".join(str(p.get("text", "")) for p in parts if isinstance(p, dict))


def _child_valheim_pids_under(ppid: int) -> list:
    # Return pids of valheim.x86_64 processes parented to the daemon — live OR <defunct>
    # zombie (both count as "GABS thinks it has a child"). The reset must leave NONE.
    out = subprocess.run(
        ["ps", "-o", "pid=,ppid=,comm=", "-A"], capture_output=True, text=True
    ).stdout
    pids = []
    for line in out.splitlines():
        f = line.split()
        if len(f) < 3:
            continue
        pid, pp, comm = f[0], f[1], " ".join(f[2:])
        if comm == "valheim.x86_64" and pp == str(ppid):
            pids.append(int(pid))
    return pids


@_needs_gabs
def test_stale_running_gabs_state_is_cleared_by_the_runner_reset(tmp_path) -> None:
    game_id = "sbprqa_gabslive_at"
    game_dir = tmp_path / "game"
    cfg_dir = tmp_path / "cfg"
    game_dir.mkdir(parents=True, exist_ok=True)
    cfg_dir.mkdir(parents=True, exist_ok=True)

    child_binary = game_dir / "valheim.x86_64"
    shutil.copy("/bin/sleep", child_binary)  # a real valheim.x86_64 stand-in
    os.chmod(child_binary, 0o755)

    wrapper = game_dir / "run.sh"
    # Hold the child alive long enough that GABS deterministically reports "running" and
    # the second start is the silent no-op; the reset (games.stop) then reaps it whether
    # it is still live or has since become a <defunct> zombie.
    wrapper.write_text(_wrapper_script(hold_seconds=30))
    os.chmod(wrapper, 0o755)

    (cfg_dir / "config.json").write_text(json.dumps({
        "version": "1.0",
        "games": {game_id: {
            "id": game_id, "name": "gabslive-at", "launchMode": "DirectPath",
            "target": str(wrapper), "workingDir": str(game_dir),
            "stopProcessName": "valheim.x86_64",
        }},
    }))

    # SAFETY: this test's throwaway daemon (a distinct gabs process on its own port +
    # config) can only ever signal ITS OWN gameId's child — it physically cannot reach a
    # foreign client under the deployed :8080/:8081 daemons. So we scope every check to
    # THIS daemon's children rather than a global name gate, which lets the test run
    # deterministically even when a real client happens to be up elsewhere on the host.
    daemon = subprocess.Popen(
        [_GABS_BINARY, "server", "--http", f"localhost:{_HTTP_PORT}",
         "--configDir", str(cfg_dir), "-log-level", "error"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )
    try:
        time.sleep(1.5)  # daemon HTTP listener warmup

        # 1) START a real child and confirm GABS's single-gameId model reports "running".
        r = _mcp("games.start", game_id)
        assert r.get("result", {}).get("content"), f"first games.start failed: {r}"
        running_deadline = time.monotonic() + 8.0
        started_pids = set()
        while time.monotonic() < running_deadline:
            started_pids = set(_child_valheim_pids_under(daemon.pid))
            if started_pids and "running" in _status_text(game_id).lower():
                break
            time.sleep(0.2)
        assert "running" in _status_text(game_id).lower(), (
            "GABS should report the launched child as 'running' (the state that goes "
            "stale when the child later dies without being reaped)"
        )
        assert started_pids, "a valheim.x86_64 child should be parented to the daemon"

        # 2) REPRODUCE THE RUN-8 CLASS: a second games.start on a 'running' gameId is a
        #    silent no-op ("already running"). This is the exact belief that, once stale
        #    (child dead + unreaped), swallowed all six of run 8's re-rolls.
        noop = _mcp("games.start", game_id)
        assert noop.get("result", {}).get("isError") is True, (
            "second games.start on a 'running' gameId should be a no-op error"
        )
        assert "already running" in json.dumps(noop).lower()

        # 3) THE FIX: run the runner's OWN reset path (games.stop → the daemon terminates
        #    AND Wait()s/reaps its child, clearing the state). Verify the exact
        #    post-conditions the runner's `_reset_gabs_state` closure asserts.
        _mcp("games.stop", game_id)
        cleared_deadline = time.monotonic() + 8.0
        while time.monotonic() < cleared_deadline:
            if ("running" not in _status_text(game_id).lower()
                    and not _child_valheim_pids_under(daemon.pid)):
                break
            time.sleep(0.2)
        assert "running" not in _status_text(game_id).lower(), (
            "games.stop must clear GABS's 'running' belief so the next launch is not swallowed"
        )
        assert _child_valheim_pids_under(daemon.pid) == [], (
            "the daemon must have terminated AND reaped its child (none left under it)"
        )

        # 4) A FRESH launch now actually forks a NEW valheim.x86_64 child — the launch is
        #    no longer swallowed by a stale state.
        r2 = _mcp("games.start", game_id)
        assert r2.get("result", {}).get("content"), f"post-reset games.start failed: {r2}"
        forked = False
        fork_deadline = time.monotonic() + 8.0
        while time.monotonic() < fork_deadline:
            new = set(_child_valheim_pids_under(daemon.pid)) - started_pids
            if new:
                forked = True
                break
            time.sleep(0.2)
        assert forked, "post-reset games.start must fork a NEW valheim.x86_64 child"
    finally:
        # Deterministic teardown: reap the daemon (which reaps its children too).
        subprocess.run(["pkill", "-9", "-f", str(child_binary)], check=False)
        daemon.terminate()
        try:
            daemon.wait(timeout=10)
        except subprocess.TimeoutExpired:
            daemon.kill()
            daemon.wait(timeout=5)
