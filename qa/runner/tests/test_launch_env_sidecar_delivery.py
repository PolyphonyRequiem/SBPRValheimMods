"""LOCALLY-GATED acceptance test: env crosses the REAL GABS daemon fork (M6-LAUNCHENV).

## Why this test exists — the seam the two prior bugs shared

`t_2a954860` proved at runtime that the arming env never reached the GABS-launched
client: the launched `valheim.x86_64`'s `/proc/<pid>/environ` carried only `GABP_*`,
none of the three `SBPR_QA_*` vars. It shipped green because every M6-LAUNCH test
**stubbed the boot** — the daemon-fork seam was never crossed by a test. Stubbing the
boot is exactly what let the bug ship, so a stubbed version of this test would be
worthless.

## What this test actually does (NOT a stub)

It stands up a REAL `gabs` daemon (the same binary deployed on this host) pointed at a
throwaway game config whose launch target is a wrapper mirroring `run-trailborne.sh`'s
sidecar contract, then fires a REAL `games.start` over HTTP. GABS forks a real child
process named `valheim.x86_64`; the test reads that child's **actual** `/proc/<pid>/environ`
and asserts it carries all three `SBPR_QA_*` vars — delivered purely by the
`SidecarWriter` + wrapper mechanism, across the genuine daemon fork boundary.

There is no mock GABS, no fake process, no asserted-on request payload. The child is a
real forked process and the assertion reads real kernel-exposed environment bytes.

## Locally-gated, not CI

CI has no `gabs` binary, so the test SKIPS there (guarded on the binary's presence). It
is designed to run on THIS host and the M6-LAUNCHENV handoff reports an actual local
run + result. The complementary in-CI coverage (that the runner's launch request carries
the env values it intends to deliver, and that the sidecar renders/places them) lives in
`test_gabs_client_boot.py` / `test_launch_env.py` and always runs.
"""
from __future__ import annotations

import json
import os
import shutil
import subprocess
import time
import urllib.request

import pytest

from runner_core.launch_env import SidecarWriter
from runner_core.operator_drivers import (
    BOOTSTRAP_ENV_VAR,
    HARNESS_INSTANCE_ENV_VAR,
    STEAM_ID_ENV_VAR,
)

# The deployed GABS daemon binary on this host. Absent in CI → the test skips.
_GABS_BINARY = "/home/polyphonyrequiem/valheim/mcp-harness/GABS/gabs"
_HTTP_PORT = 8093  # a port not used by the deployed :8080/:8081 daemons
_ENDPOINT = f"http://localhost:{_HTTP_PORT}/mcp"

_needs_gabs = pytest.mark.skipif(
    not os.path.exists(_GABS_BINARY),
    reason="locally-gated: real GABS daemon binary not present (e.g. in CI)",
)


def _wrapper_script(game_dir: str) -> str:
    # Mirrors the sidecar contract the deployed run-trailborne.sh carries: derive the
    # sidecar path from $HOME + $GABS_GAME_ID (or an explicit override), source it, then
    # exec the child named valheim.x86_64. This is the exact seam the production wrapper
    # uses; the test proves the seam, not a bespoke shim.
    return (
        "#!/usr/bin/env bash\n"
        "set -e\n"
        'HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"\n'
        'SIDECAR="${SBPR_QA_LAUNCH_ENV_FILE:-$HOME/.local/share/sbpr-qa/launch-env/${GABS_GAME_ID}.env}"\n'
        'if [[ -f "$SIDECAR" ]]; then set -a; . "$SIDECAR"; set +a; fi\n'
        'exec "$HERE/valheim.x86_64" 600\n'
    )


def _mcp_start(game_id: str) -> dict:
    payload = json.dumps(
        {"jsonrpc": "2.0", "id": 1, "method": "tools/call",
         "params": {"name": "games.start", "arguments": {"gameId": game_id}}}
    ).encode()
    req = urllib.request.Request(
        _ENDPOINT, data=payload, headers={"Content-Type": "application/json"}, method="POST"
    )
    with urllib.request.urlopen(req, timeout=10) as resp:
        return json.loads(resp.read().decode())


def _child_environ(child_binary: str, timeout_s: float = 8.0) -> dict:
    """Return the environ dict of the unique forked process running `child_binary`."""
    deadline = time.monotonic() + timeout_s
    target = os.path.realpath(child_binary)
    while time.monotonic() < deadline:
        for pid_s in os.listdir("/proc"):
            if not pid_s.isdigit():
                continue
            try:
                exe = os.path.realpath(os.path.join("/proc", pid_s, "exe"))
            except OSError:
                continue
            if exe != target:
                continue
            try:
                with open(os.path.join("/proc", pid_s, "environ"), "rb") as fh:
                    raw = fh.read()
            except OSError:
                continue
            env = {}
            for entry in raw.split(b"\x00"):
                if b"=" in entry:
                    k, v = entry.split(b"=", 1)
                    env[k.decode("utf-8", "replace")] = v.decode("utf-8", "replace")
            return env
        time.sleep(0.2)
    return {}


@_needs_gabs
def test_launched_child_environ_carries_all_three_sbpr_vars(tmp_path) -> None:
    game_id = "sbprqa_launchenv_at"
    game_dir = tmp_path / "game"
    cfg_dir = tmp_path / "cfg"
    home_dir = tmp_path / "home"
    for d in (game_dir, cfg_dir, home_dir):
        d.mkdir(parents=True, exist_ok=True)

    # A real executable named valheim.x86_64 so /proc/<pid>/exe basename matches the
    # runner's resolver AND this test's finder. `sleep` is a harmless stand-in binary.
    child_binary = game_dir / "valheim.x86_64"
    shutil.copy("/bin/sleep", child_binary)
    os.chmod(child_binary, 0o755)

    wrapper = game_dir / "run-at.sh"
    wrapper.write_text(_wrapper_script(str(game_dir)))
    os.chmod(wrapper, 0o755)

    (cfg_dir / "config.json").write_text(json.dumps({
        "version": "1.0",
        "games": {game_id: {
            "id": game_id, "name": "launchenv-at", "launchMode": "DirectPath",
            "target": str(wrapper), "workingDir": str(game_dir),
            "stopProcessName": "valheim.x86_64",
        }},
    }))

    # THE MECHANISM UNDER TEST: the runner writes the sidecar (the three arming vars) at
    # the exact path the wrapper derives from $HOME + $GABS_GAME_ID. We publish HOME to
    # the daemon so its forked wrapper resolves the same path — mirroring how the real
    # lanes each launch under their own user's HOME.
    launch_env = {
        BOOTSTRAP_ENV_VAR: str(tmp_path / "t022-bootstrap-client_a.json"),
        HARNESS_INSTANCE_ENV_VAR: "client_a:launchenv_at_marker",
        STEAM_ID_ENV_VAR: "76561197965627562",
    }
    sidecar_path = str(home_dir / ".local" / "share" / "sbpr-qa" / "launch-env" / f"{game_id}.env")
    SidecarWriter().write(sidecar_path, launch_env)

    daemon_env = {k: v for k, v in os.environ.items() if not k.startswith("SBPR_QA_")}
    daemon_env["HOME"] = str(home_dir)  # so the forked wrapper's $HOME resolves the sidecar
    # Discriminating guarantee: the daemon's OWN environment carries NONE of the three
    # arming vars. This mirrors the real long-lived daemon (pid 1926, up since before the
    # run) whose env is frozen — the pre-fix code mutated the RUNNER's os.environ, which
    # never reached this frozen daemon or its fork. So the sidecar file is the ONLY
    # channel by which the vars can reach the child; if it fails, the assertion fails.
    assert not any(k.startswith("SBPR_QA_") for k in daemon_env)
    daemon = subprocess.Popen(
        [_GABS_BINARY, "server", "--http", f"localhost:{_HTTP_PORT}",
         "--configDir", str(cfg_dir), "-log-level", "error"],
        env=daemon_env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
    )
    try:
        time.sleep(1.5)  # daemon HTTP listener warmup (a real localhost round-trip follows)
        result = _mcp_start(game_id)
        assert result.get("result", {}).get("content"), f"games.start failed: {result}"

        env = _child_environ(str(child_binary))
        assert env, "no forked valheim.x86_64 child appeared — the daemon did not launch it"

        # THE ASSERTION THE BUG WOULD FAIL: all three arming vars crossed the fork.
        assert env.get(BOOTSTRAP_ENV_VAR) == launch_env[BOOTSTRAP_ENV_VAR]
        assert env.get(HARNESS_INSTANCE_ENV_VAR) == launch_env[HARNESS_INSTANCE_ENV_VAR]
        assert env.get(STEAM_ID_ENV_VAR) == launch_env[STEAM_ID_ENV_VAR]
        # And the GABS bridge vars are still present (we augment, never replace).
        assert env.get("GABS_GAME_ID") == game_id
        assert "GABP_SERVER_PORT" in env
    finally:
        # Deterministic teardown: kill the forked child, then the daemon.
        subprocess.run(["pkill", "-9", "-f", str(child_binary)], check=False)
        daemon.terminate()
        try:
            daemon.wait(timeout=10)
        except subprocess.TimeoutExpired:
            daemon.kill()
            daemon.wait(timeout=5)
