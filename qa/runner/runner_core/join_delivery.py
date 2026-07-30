"""Join-target delivery preflight (#453; T022 ARRANGE §2 I5).

WHAT THIS EXISTS TO PREVENT
---------------------------
A client that is never told which server to join boots normally, loads every mod, and
waits at a menu forever with nothing logged. That was the twelve-day live blocker: for
`client_b`, Steam's AppID path delivered no join target, `m_queuedJoinServer` was never
populated, the client stopped at the server-list screen, and the harness's
character-select hook never fired. One symptom, no error, ~10 minutes per diagnosis.

#449 fixed the delivery. This module makes the fix *checkable*, because a fix nothing
asserts is a fix waiting to be undone. It answers, before anything boots:

    "Is the join target actually going to reach THIS client, through THIS client's own
     launch path?"

THE TWO HALVES OF DELIVERY, AND WHY BOTH ARE CHECKED
----------------------------------------------------
GABS delivers neither per-launch env nor per-launch argv to the forked child, so the
join target crosses the fork in two hops:

  1. The runner writes `SBPR_QA_CONNECT=host:port` into a per-launch **sidecar** env
     file, at the exact path this client's wrapper will read (per-client — the two lanes
     launch as different users and read different paths).
  2. The client's **wrapper** sources that sidecar and turns the value into a
     `+connect host:port` argv fragment immediately before `exec`ing the game.

Hop 1 is manifest data. Hop 2 is a property of a shell script on disk — and it is the
half that silently rots, because the wrapper is a file a human edits. So this module
reads the wrapper and asserts the seam is present.

THE ARGV-ROTATION TRAP (the specific regression #449 warned about)
------------------------------------------------------------------
`run_bepinex.sh` detects Steam's `SteamLaunch` marker and ROTATES argv so the BepInEx
runner is reinserted ahead of the game executable. Consequence, verified offline against
the real script before any GPU launch:

  * arguments APPENDED after `"$@"` ride through the rotation into the game's argv  ✅
  * arguments PREPENDED are consumed as part of Steam's wrapper command             ❌

So for a Steam-launched client the fragment's POSITION is load-bearing. A well-meaning
tidy-up that moves `"${SBPR_QA_CONNECT_ARGS[@]}"` before `"$@"` reintroduces the exact
original symptom — a client parked at the server list with nothing logged — and no test
would catch it. That is checked here, per launcher kind, because it applies to the
Steam path and not to the GABS DirectPath one (which execs the game binary itself and
has no rotation).

WHAT THIS DELIBERATELY DOES NOT DO
----------------------------------
It does not prove the target ARRIVES. Reading the launched process's real
`/proc/<pid>/cmdline` is the only thing that proves that, and it belongs to VERIFY
(#456) because it requires a running process. What this does is refuse to spend ten
minutes booting a client whose delivery path is visibly broken before launch — the
standing rule that structurally unrecoverable conditions are caught at preflight rather
than discovered by a burned launch.

Engine-free: stdlib only, no product/game import. The filesystem seam is injected, so
importing or unit-testing this module reads nothing.
"""
from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Callable, List, Optional, Sequence

from .arrange_manifest import ArrangeManifest, ClientEntry
from .arrange_static import P_JOIN_TARGET, StaticFailure

# The sidecar variable both wrappers read. Mirrored from launch_env.ALLOWED_SIDECAR_KEYS;
# if that name ever changes, this check must change in lockstep or it silently passes.
CONNECT_VAR = "SBPR_QA_CONNECT"

# The argv array both wrappers build from CONNECT_VAR.
CONNECT_ARGS_VAR = "SBPR_QA_CONNECT_ARGS"

# Launcher kinds whose launch chain passes through Steam's `%command%` wrapper, and are
# therefore subject to run_bepinex.sh's SteamLaunch argv rotation. Data, not a branch:
# a new launcher declares its exposure by appearing (or not) here.
ROTATION_EXPOSED_LAUNCHERS = frozenset({"steam_applaunch"})

_EXEC_RE = re.compile(r"^\s*exec\s+(?P<rest>.+)$")


class JoinDeliveryError(RuntimeError):
    """The wrapper could not be read at all. Distinct from 'read it, seam is wrong'."""


@dataclass(frozen=True)
class WrapperSeam:
    """What reading one client's wrapper established.

    `sources_sidecar` — the wrapper reads the sidecar env file at all.
    `builds_connect_args` — it turns CONNECT_VAR into a `+connect` argv fragment.
    `exec_line` — the final exec, kept so a failure can quote what it actually saw.
    `fragment_after_passthrough` — for a rotation-exposed launcher, whether the fragment
        is appended after `"$@"` (True), before it (False), or not determinable (None).
    """

    sources_sidecar: bool
    builds_connect_args: bool
    exec_line: Optional[str]
    fragment_after_passthrough: Optional[bool]


def inspect_wrapper(text: str) -> WrapperSeam:
    """Parse a wrapper script's join-delivery seam. Pure; takes the text, reads nothing.

    Intentionally shallow: this is not a shell parser and must not pretend to be. It
    establishes the three facts that actually distinguish a working seam from the
    twelve-day failure, and reports honestly (None) when it cannot tell.
    """
    sources_sidecar = CONNECT_VAR in text or "LAUNCH_ENV_FILE" in text
    builds_connect_args = CONNECT_ARGS_VAR in text

    exec_line: Optional[str] = None
    for raw in text.splitlines():
        if not _EXEC_RE.match(raw):
            continue
        # Prefer the exec that actually carries the delivery, but fall back to the last
        # exec seen so a restructured wrapper still gets quoted back in the failure.
        if "$@" in raw or CONNECT_ARGS_VAR in raw:
            exec_line = raw.strip()
        elif exec_line is None:
            exec_line = raw.strip()

    fragment_after_passthrough: Optional[bool] = None
    if exec_line and CONNECT_ARGS_VAR in exec_line and '"$@"' in exec_line:
        fragment_after_passthrough = exec_line.index('"$@"') < exec_line.index(
            CONNECT_ARGS_VAR
        )

    return WrapperSeam(
        sources_sidecar=sources_sidecar,
        builds_connect_args=builds_connect_args,
        exec_line=exec_line,
        fragment_after_passthrough=fragment_after_passthrough,
    )


def check_join_delivery(
    manifest: ArrangeManifest,
    read_text: Callable[[str], Optional[str]],
) -> List[StaticFailure]:
    """Assert every client's join target can actually reach it, per client.

    `read_text(path)` returns the file's text, or None when it does not exist / cannot
    be read. Returning None rather than raising keeps an unreadable wrapper reportable
    as a named failure instead of an exception that hides the other clients' problems.

    Nothing here compares one client to another: each client's wrapper is resolved from
    its OWN launcher params and judged against its OWN launcher kind. A client with no
    declared wrapper is skipped (its launcher may deliver argv directly); a client with
    a declared wrapper must have a working seam in it.
    """
    failures: List[StaticFailure] = []

    for client in manifest.clients:
        if client.join is None:
            continue  # S8 already reports a missing join target; don't double-report.

        wrapper_path = client.launcher.params.get("wrapper_path")
        if not wrapper_path:
            continue
        wrapper_path = str(wrapper_path)

        text = read_text(wrapper_path)
        if text is None:
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail="launch wrapper is missing or unreadable",
                    expected=f"a readable wrapper script at {wrapper_path}",
                    actual="no such file, or read failed",
                    remedy="The wrapper is the only seam that can deliver the join "
                    "target across the daemon fork. Without it the client boots, loads "
                    "every mod, and waits at a menu forever with nothing logged.",
                )
            )
            continue

        seam = inspect_wrapper(text)

        if not seam.sources_sidecar:
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail=f"wrapper never reads {CONNECT_VAR}",
                    expected=f"{wrapper_path} to source the launch-env sidecar and read "
                    f"{CONNECT_VAR}",
                    actual=f"no reference to {CONNECT_VAR} or a launch-env file in the wrapper",
                    remedy="The runner writes the join target to a per-launch sidecar "
                    "because the daemon forks the client with the DAEMON's environment, "
                    "not the runner's. A wrapper that never sources it discards the "
                    "target silently — the exact twelve-day blocker.",
                )
            )
            continue

        if not seam.builds_connect_args:
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail=f"wrapper reads {CONNECT_VAR} but builds no `+connect` argument",
                    expected=f"{wrapper_path} to turn {CONNECT_VAR} into a "
                    f"`+connect host:port` fragment ({CONNECT_ARGS_VAR})",
                    actual=f"{CONNECT_ARGS_VAR} is never constructed",
                    remedy="Reading the value is not delivering it. Vanilla populates "
                    "`m_queuedJoinServer` from the `+connect` ARGUMENT; an env var alone "
                    "leaves the client on the server-list screen, where the harness's "
                    "character-select hook never fires.",
                )
            )
            continue

        # The rotation trap — only for launchers that pass through Steam's wrapper chain.
        if client.launcher.kind in ROTATION_EXPOSED_LAUNCHERS:
            if seam.fragment_after_passthrough is False:
                failures.append(
                    StaticFailure(
                        precondition=P_JOIN_TARGET,
                        client=client.actor,
                        detail="`+connect` fragment is PREPENDED and will be swallowed by "
                        "Steam's wrapper rotation",
                        expected=f'{CONNECT_ARGS_VAR} to appear AFTER "$@" in the final exec',
                        actual=f"exec line has it before \"$@\": {seam.exec_line}",
                        remedy="run_bepinex.sh detects Steam's SteamLaunch marker and "
                        "ROTATES argv, reinserting the BepInEx runner ahead of the game. "
                        "Appended args survive that rotation into the game's argv; "
                        "prepended args are consumed as part of Steam's wrapper command "
                        "and never reach the game. Move the fragment after \"$@\". This "
                        "is the single most likely way a future edit silently breaks the "
                        "join, and its symptom is the original one: a client parked at "
                        "the server list with nothing logged.",
                    )
                )
            elif seam.fragment_after_passthrough is None:
                failures.append(
                    StaticFailure(
                        precondition=P_JOIN_TARGET,
                        client=client.actor,
                        detail="cannot determine `+connect` fragment position in a "
                        "rotation-exposed wrapper",
                        expected=f'a final exec containing both "$@" and {CONNECT_ARGS_VAR}, '
                        "so the fragment's position relative to the passthrough is checkable",
                        actual=f"exec line: {seam.exec_line or '<no exec line found>'}",
                        remedy="This launcher passes through Steam's wrapper chain, where "
                        "argv rotation makes the fragment's position load-bearing. If the "
                        "wrapper's exec has been restructured, restate it so the fragment "
                        "is unambiguously appended after \"$@\" — an unverifiable seam here "
                        "fails the same silent way as a broken one.",
                    )
                )

    return failures
