"""LOCALLY-GATED acceptance test: the +connect ARG crosses the REAL GABS daemon fork (M6-JOIN).

## Why this test exists — the seam that kept the client at the main menu

The QA client booted all the way to the **main menu and idled there forever**. The T022
helper only arms once `ZNet.World` loads, so with no join it stayed correctly DISARMED
and no acceptance test could run. Verified on this host, not inferred: a real launched
client's `/proc/<pid>/cmdline` was `valheim.x86_64 -console` — **no `+connect`**.

GABS's `games_start` delivers no per-launch **arguments** exactly as it delivers no
per-launch **env** (proven for M6-LAUNCHENV). `ClientLaunchRequest` built a
`connect_args=("+connect", target)` fragment, but **nothing delivered it** to the game
argv. Validating a field is not the same as delivering it.

## What this test actually does (NOT a stub)

It stands up a REAL `gabs` daemon (the same binary deployed on this host) pointed at a
throwaway game config whose launch target is a wrapper mirroring `run-trailborne.sh`'s
join contract: source the launch-env sidecar, then turn `SBPR_QA_CONNECT=host:port` into
a `+connect <host>:<port>` argv fragment prepended before exec. It fires a REAL
`games.start` over HTTP; GABS forks a real child named `valheim.x86_64`; the test reads
that child's **actual** `/proc/<pid>/cmdline` and asserts it carries `+connect host:port`
— delivered purely by the `SidecarWriter` + wrapper mechanism, across the genuine daemon
fork boundary.

There is no mock GABS, no fake process, no asserted-on request payload. The child is a
real forked process and the assertion reads real kernel-exposed argv bytes. Stubbing the
boot is exactly what let both prior seam bugs (env, then this arg) ship, so a stubbed
version of this test would be worthless.

## Locally-gated, not CI

CI has no `gabs` binary, so the test SKIPS there (guarded on the binary's presence). It
is designed to run on THIS host and the M6-JOIN handoff reports an actual local run +
result. The complementary in-CI coverage (that the launch request carries the connect
target in its sidecar env, and that the sidecar renders/refuses it safely) lives in
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
    CONNECT_TARGET_ENV_VAR,
    HARNESS_INSTANCE_ENV_VAR,
    STEAM_ID_ENV_VAR,
)

# The deployed GABS daemon binary on this host. Absent in CI → the test skips.
_GABS_BINARY = "/home/polyphonyrequiem/valheim/mcp-harness/GABS/gabs"
_HTTP_PORT = 8094  # a port not used by the deployed :8080/:8081 daemons or the env-AT :8093
_ENDPOINT = f"http://localhost:{_HTTP_PORT}/mcp"

_needs_gabs = pytest.mark.skipif(
    not os.path.exists(_GABS_BINARY),
    reason="locally-gated: real GABS daemon binary not present (e.g. in CI)",
)


def _wrapper_script() -> str:
    # Mirrors the deployed run-trailborne.sh join contract: source the sidecar, then turn
    # SBPR_QA_CONNECT into a `+connect host:port` argv fragment prepended before exec.
    # This is the exact seam the production wrapper uses; the test proves the seam.
    return (
        "#!/usr/bin/env bash\n"
        "set -e\n"
        'HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"\n'
        'SIDECAR="${SBPR_QA_LAUNCH_ENV_FILE:-$HOME/.local/share/sbpr-qa/launch-env/${GABS_GAME_ID}.env}"\n'
        'if [[ -f "$SIDECAR" ]]; then set -a; . "$SIDECAR"; set +a; fi\n'
        "SBPR_QA_CONNECT_ARGS=()\n"
        'if [[ -n "${SBPR_QA_CONNECT:-}" ]]; then SBPR_QA_CONNECT_ARGS=(+connect "$SBPR_QA_CONNECT"); fi\n'
        'exec "$HERE/valheim.x86_64" -console "${SBPR_QA_CONNECT_ARGS[@]}" 600\n'
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


def _child_cmdline(child_binary: str, timeout_s: float = 8.0) -> list:
    """Return the argv (list) of the unique forked process running `child_binary`."""
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
                with open(os.path.join("/proc", pid_s, "cmdline"), "rb") as fh:
                    raw = fh.read()
            except OSError:
                continue
            if not raw:
                continue
            return [a.decode("utf-8", "replace") for a in raw.split(b"\x00") if a]
        time.sleep(0.2)
    return []


@_needs_gabs
def test_launched_child_cmdline_carries_connect_arg(tmp_path) -> None:
    game_id = "sbprqa_launcharg_at"
    connect_host = "127.0.0.1"
    connect_port = 2476  # the genuine disposable-lane join port (NOT a production port)
    connect_target = f"{connect_host}:{connect_port}"

    game_dir = tmp_path / "game"
    cfg_dir = tmp_path / "cfg"
    home_dir = tmp_path / "home"
    for d in (game_dir, cfg_dir, home_dir):
        d.mkdir(parents=True, exist_ok=True)

    # A real ELF named valheim.x86_64 that IGNORES its argv and just sleeps, so the
    # +connect/-console flags the wrapper passes don't make it exit (a /bin/sleep stub
    # would reject unknown flags and die before we can read its cmdline). Compiled here
    # because the assertion is on the child's /proc/<pid>/cmdline, which needs a live
    # process that tolerates arbitrary args. Locally-gated already (needs gabs); if no C
    # compiler is present, skip rather than assert on a corpse.
    cc = shutil.which("cc") or shutil.which("gcc")
    if cc is None:
        pytest.skip("locally-gated: no C compiler to build an argv-tolerant child stub")
    child_binary = game_dir / "valheim.x86_64"
    src = game_dir / "stub.c"
    src.write_text("#include <unistd.h>\nint main(void){ for(;;) pause(); return 0; }\n")
    build = subprocess.run(
        [cc, "-x", "c", str(src), "-o", str(child_binary)],
        capture_output=True, text=True,
    )
    assert build.returncode == 0, f"stub compile failed: {build.stderr}"
    os.chmod(child_binary, 0o755)

    wrapper = game_dir / "run-at.sh"
    wrapper.write_text(_wrapper_script())
    os.chmod(wrapper, 0o755)

    (cfg_dir / "config.json").write_text(json.dumps({
        "version": "1.0",
        "games": {game_id: {
            "id": game_id, "name": "launcharg-at", "launchMode": "DirectPath",
            "target": str(wrapper), "workingDir": str(game_dir),
            "stopProcessName": "valheim.x86_64",
        }},
    }))

    # THE MECHANISM UNDER TEST: the runner writes the connect target into the SAME sidecar
    # the wrapper sources; the wrapper turns it into `+connect host:port`. We publish HOME
    # to the daemon so its forked wrapper resolves the same sidecar path.
    launch_env = {
        BOOTSTRAP_ENV_VAR: str(tmp_path / "t022-bootstrap-client_a.json"),
        HARNESS_INSTANCE_ENV_VAR: "client_a:launcharg_at_marker",
        STEAM_ID_ENV_VAR: "76561197965627562",
        CONNECT_TARGET_ENV_VAR: connect_target,
    }
    sidecar_path = str(home_dir / ".local" / "share" / "sbpr-qa" / "launch-env" / f"{game_id}.env")
    SidecarWriter().write(sidecar_path, launch_env)

    daemon_env = {k: v for k, v in os.environ.items() if not k.startswith("SBPR_QA_")}
    daemon_env["HOME"] = str(home_dir)  # so the forked wrapper's $HOME resolves the sidecar
    # Discriminating guarantee: the daemon's OWN environment carries no SBPR_QA_CONNECT, so
    # the sidecar file is the ONLY channel by which the join target can reach the child.
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

        cmdline = _child_cmdline(str(child_binary))
        assert cmdline, "no forked valheim.x86_64 child appeared — the daemon did not launch it"

        # THE ASSERTION THE BUG WOULD FAIL: the `+connect host:port` fragment crossed the
        # fork, as two adjacent argv tokens, delivered purely by the sidecar + wrapper.
        assert "+connect" in cmdline, f"no +connect in child argv: {cmdline!r}"
        i = cmdline.index("+connect")
        assert i + 1 < len(cmdline), f"+connect had no target argument: {cmdline!r}"
        assert cmdline[i + 1] == connect_target, (
            f"+connect target was {cmdline[i + 1]!r}, expected {connect_target!r}: {cmdline!r}"
        )
        # The host:port is a SINGLE argv token — it did not split into an extra flag.
        assert cmdline.count(connect_target) == 1
        # And -console is still present (we augment the launch args, never replace them).
        assert "-console" in cmdline
    finally:
        # Deterministic teardown: kill the forked child, then the daemon.
        subprocess.run(["pkill", "-9", "-f", str(child_binary)], check=False)
        daemon.terminate()
        try:
            daemon.wait(timeout=10)
        except subprocess.TimeoutExpired:
            daemon.kill()
            daemon.wait(timeout=5)
