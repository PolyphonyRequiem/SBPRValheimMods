"""GABS-fork launch-env delivery via a wrapper-read sidecar file (M6-LAUNCHENV).

## The seam this closes

`real_operator_environment` used to publish the three arming vars by mutating the
**runner's** `os.environ`, then fire `games_start` over HTTP at a long-lived GABS
daemon. That daemon forks `valheim.x86_64`; the child inherits the **daemon's**
environment, never the runner's. Proven at runtime by `t_2a954860`: the launched
client's `/proc/<pid>/environ` carried only `GABP_TOKEN` + `GABP_SERVER_PORT` — none
of `SBPR_QA_T022_BOOTSTRAP` / `SBPR_QA_HARNESS_INSTANCE` / `SBPR_QA_STEAM_ID`. The
helper never armed and the runner could not find its provenance marker to tear down,
so it orphaned the client.

## The verified mechanism (probed on this host, not assumed)

This GABS build (`1c23db6`) has **no** per-launch env in the `games_start` MCP request
(schema accepts only `gameId` — `internal/mcp/stdio_server.go`) and **no** env field in
the game config (`GameConfig` — `internal/config/games.go`); the controller propagates
only the daemon's `os.Environ()` plus the fixed `GABP_*`/`GABS_*` bridge vars
(`internal/process/controller.go:setupEnvironment`). The configured launch target is a
**wrapper script** (`DirectPath` → `run-trailborne.sh`). The wrapper is therefore the
only seam that can inject per-launch env into the forked child.

The contract: the runner writes a **sidecar env file** at a path the wrapper can derive
from the *only* two facts GABS gives the child — `GABS_GAME_ID` (set by GABS) and `HOME`
(the launching user's home). The wrapper `source`s that file just before `exec`ing the
game binary, so the vars land in the child's environment. Proven by a live probe: a real
GABS daemon forked a child whose `/proc/<pid>/environ` carried all three vars delivered
purely through this sidecar (see `tests/test_launch_env_sidecar_delivery.py`).

## Why a sidecar and not the alternatives

- The three vars are NOT secret: a bootstrap-doc **path**, a public SteamID64, and a
  random provenance marker. The HMAC secret + operator token live only INSIDE the
  bootstrap doc, which stays mode 0600 and is untouched by this mechanism. So the sidecar
  is written 0644 without leaking a credential — and it is removed on teardown regardless.
- Per-launch (a fresh file written before each boot, removed after) keeps values scoped
  to one run, beating a static game-config env that would persist between runs.

Engine-free stdlib only. No Valheim/BepInEx/Unity import.
"""
from __future__ import annotations

import os
import re
from dataclasses import dataclass
from typing import Callable, Dict, List, Mapping, Optional

# Relative to the launching user's HOME. The wrapper computes exactly this path from
# `$HOME` + `$GABS_GAME_ID`; changing it requires changing the wrapper in lockstep.
SIDECAR_SUBDIR = os.path.join(".local", "share", "sbpr-qa", "launch-env")

# Non-secret vars only. A shell-safe identifier: uppercase/underscore, not digit-first.
_KEY_RE = re.compile(r"^[A-Z_][A-Z0-9_]*$")

# The sidecar carries only these keys; anything else is refused so a caller can never
# smuggle a secret (HMAC/operator token) into the 0644 file. The bootstrap DOC (0600)
# is the sole carrier of secrets; the sidecar only names its PATH.
#
# SBPR_QA_CONNECT (M6-JOIN) carries the lane join target as `host:port`. GABS's
# `games_start` accepts no per-launch ARGUMENTS just as it accepts no per-launch env, so
# the `+connect host:port` join argument was never reaching the game process — the client
# booted to the main menu and sat there. This value rides the SAME non-secret sidecar the
# wrapper already sources, and the wrapper turns it into a `+connect <host>:<port>` argv
# fragment just before `exec`. It is non-secret (a LAN host + a disposable-lane port) and,
# like the three arming vars, is scoped to one run and removed on teardown. The value's
# shape is constrained by `render_sidecar` (no whitespace/shell-hostile bytes), so a host
# string can never split into an extra flag once the wrapper prepends `+connect`.
ALLOWED_SIDECAR_KEYS = frozenset(
    {
        "SBPR_QA_T022_BOOTSTRAP",
        "SBPR_QA_HARNESS_INSTANCE",
        "SBPR_QA_STEAM_ID",
        "SBPR_QA_CONNECT",
        # M6-JOIN3: absolute PATH of the mode-0600 lane-password file (non-secret, exactly
        # like SBPR_QA_T022_BOOTSTRAP names the 0600 bootstrap doc). The password VALUE lives
        # only in that 0600 file, never in this 0644 sidecar. The QA FejdStartup auto-join hook
        # reads the file and sets vanilla FejdStartup.ServerPassword so a password-gated lane's
        # handshake auto-submits it instead of parking on the password dialog headless.
        "SBPR_QA_SERVER_PASSWORD_FILE",
    }
)


class LaunchEnvError(RuntimeError):
    """A launch-env sidecar could not be safely rendered/placed. Fail closed."""


def sidecar_relpath(game_id: str) -> str:
    """The sidecar path RELATIVE to the launching user's HOME, for `game_id`.

    This is the single source of truth the wrapper mirrors: `$HOME/<this>`. Keeping it a
    pure function of `game_id` means the wrapper — which knows only `$GABS_GAME_ID` and
    `$HOME` — can resolve the exact same path with no shared state.
    """
    _assert_safe_game_id(game_id)
    return os.path.join(SIDECAR_SUBDIR, f"{game_id}.env")


def sidecar_path(home_dir: str, game_id: str) -> str:
    """Absolute sidecar path under `home_dir` for `game_id`."""
    if not home_dir:
        raise LaunchEnvError("home_dir is required to resolve a sidecar path")
    return os.path.join(home_dir, sidecar_relpath(game_id))


def render_sidecar(env: Mapping[str, str]) -> str:
    """Render a `KEY=value` env file the wrapper can `set -a; . file; set +a`.

    Fails closed on anything that could break the `source` contract or exfiltrate a
    secret: an unknown key, a non-identifier key, or a value carrying a newline/NUL.
    Values are emitted verbatim (no quoting) — every value this harness writes is a
    filesystem path, a numeric SteamID, or a `[A-Za-z0-9:]` marker, none of which need
    quoting; a value with shell-hostile bytes is refused rather than silently mangled.
    """
    lines: List[str] = []
    for key in sorted(env):
        value = env[key]
        if not _KEY_RE.match(key):
            raise LaunchEnvError(f"refusing non-identifier sidecar key {key!r}")
        if key not in ALLOWED_SIDECAR_KEYS:
            raise LaunchEnvError(
                f"refusing sidecar key {key!r}: only the three non-secret arming vars "
                f"{sorted(ALLOWED_SIDECAR_KEYS)} may be written to the 0644 sidecar "
                "(secrets live in the mode-0600 bootstrap doc, never here)"
            )
        if "\n" in value or "\r" in value or "\x00" in value:
            raise LaunchEnvError(
                f"refusing sidecar value for {key!r}: contains a newline/NUL that would "
                "break the wrapper's `source` contract"
            )
        # Reject characters that would let a value inject shell state through an
        # unquoted `KEY=value` assignment sourced by the wrapper. The three real
        # values (path / SteamID64 / actor:hex marker) never contain these.
        if any(c in value for c in " \t\"'`$\\;&|<>()"):
            raise LaunchEnvError(
                f"refusing sidecar value for {key!r}={value!r}: contains a shell-unsafe "
                "character; the arming values are paths/ids/markers and must not"
            )
        lines.append(f"{key}={value}")
    return "".join(f"{line}\n" for line in lines)


@dataclass(frozen=True)
class LaunchEnvSidecar:
    """A written sidecar's location + provenance, recorded so teardown can remove it."""

    path: str
    keys: List[str]


class SidecarWriter:
    """Write/remove launch-env sidecar files at explicit absolute paths.

    Path-based (not home-based) because the two lanes launch as DIFFERENT users: the
    poly lane's wrapper reads `$HOME/<SIDECAR_SUBDIR>/<game_id>.env` under the runner's
    own home, while the valbot lane's wrapper — running as uid 1001, unable to read the
    runner's 0700 home — reads a primary-owned cross-user path. The runner writes each
    client's sidecar at the path that client's wrapper will read (resolved from the
    descriptor), so a single writer serves both without needing write access to another
    user's home.

    Files are written atomically (temp + rename) at mode 0644 (non-secret contents) and
    every write is tracked so `remove`/`remove_all` can clear them on every teardown
    path — a stale sidecar never lingers between runs.
    """

    def __init__(self) -> None:
        self._written: Dict[str, LaunchEnvSidecar] = {}

    def write(self, path: str, env: Mapping[str, str]) -> LaunchEnvSidecar:
        """Render + place the sidecar at absolute `path`. Overwrites any prior file.

        Fails closed via `render_sidecar` on unsafe content, and refuses a relative path
        or a symlink target (never follow a link out of the intended directory).
        """
        if not os.path.isabs(path):
            raise LaunchEnvError(f"sidecar path must be absolute: {path!r}")
        content = render_sidecar(env)
        directory = os.path.dirname(path)
        os.makedirs(directory, mode=0o700, exist_ok=True)
        if os.path.islink(path):
            raise LaunchEnvError(f"refusing to write sidecar over a symlink: {path}")
        tmp = f"{path}.tmp.{os.getpid()}"
        fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o644)
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as fh:
                fh.write(content)
        except Exception:
            _best_effort_unlink(tmp)
            raise
        os.replace(tmp, path)
        os.chmod(path, 0o644)
        sidecar = LaunchEnvSidecar(path=path, keys=sorted(env))
        self._written[path] = sidecar
        return sidecar

    def remove(self, path: str) -> None:
        """Remove the sidecar at `path`. Idempotent; safe on every teardown path."""
        self._written.pop(path, None)
        _best_effort_unlink(path)

    def remove_all(self) -> None:
        for path in list(self._written):
            self.remove(path)

    @property
    def written(self) -> List[LaunchEnvSidecar]:
        return list(self._written.values())


def _best_effort_unlink(path: str) -> None:
    try:
        os.unlink(path)
    except FileNotFoundError:
        return
    except OSError:
        # A sidecar we cannot remove is non-secret and will be overwritten next run;
        # never raise out of teardown for it.
        return


def _assert_safe_game_id(game_id: str) -> None:
    # The game_id becomes a filename component; refuse path separators / traversal so a
    # descriptor value can never redirect the sidecar outside the intended directory.
    if not game_id or not re.match(r"^[A-Za-z0-9._-]+$", game_id) or game_id in (".", ".."):
        raise LaunchEnvError(
            f"unsafe game_id for sidecar path: {game_id!r} (must be [A-Za-z0-9._-])"
        )
