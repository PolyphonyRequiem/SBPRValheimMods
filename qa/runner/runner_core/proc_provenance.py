"""Shared `/proc` provenance reads — the ONE place that decides a process is ours.

WHY THIS EXISTS
---------------
Two code paths need to answer the same question: "is the process at this PID a
harness-owned Valheim client, or is it Daniel's own game?" `live_composition` asks
it when it terminates the client it launched; SWEEP (#455) asks it about processes
no live object remembers, left behind by a run that was SIGKILLed.

Two copies of `/proc` parsing WILL drift, and the failure mode of drift here is not
a wrong report — it is signalling the wrong process. So the parsing lives once, in
one engine-free module, and both callers import it.

THE B1 RULE THIS MODULE ENCODES
-------------------------------
Ownership is proven by ONE fact and one only: the process's own environment carries
`SBPR_QA_HARNESS_INSTANCE`, the per-boot random marker the harness minted. There is
deliberately **no fallback heuristic** — no cmdline match, no cwd match, no
`pkill -f`, no game-root prefix. Every one of those would match Daniel's own Steam
Valheim, which runs the same binary from a similar root with a similar command line.

`read_marker` returning None is therefore terminal, and it is terminal for BOTH
reasons it can happen:

  * the process genuinely carries no marker (not ours), and
  * `/proc/<pid>/environ` could not be read at all (EACCES on a uid-1001 process).

An unreadable environ is **not** proof of ownership. Treating "I could not check" as
"probably mine" is exactly how a sweeper ends up killing the user's game, so the two
cases collapse to the same fail-closed answer here rather than being distinguished by
a caller that might get the distinction wrong.

PID REUSE
---------
A marker alone still is not enough to signal. Between reading the marker and sending
the signal, the PID can be recycled by an unrelated process. Field 22 of
`/proc/<pid>/stat` (start time in clock ticks since boot) pins the identity: a
recycled PID has a different start time. Callers re-read the pair immediately before
each signal and refuse if either moved.

ZOMBIES
-------
A `<defunct>` child exposes no readable `/proc/<pid>/exe`, which is what makes
`is_valheim_exe` a live/zombie discriminator as well as a binary check. Zombies are
report-only everywhere: a zombie cannot be killed, only reaped by its parent, and
GABS's reaping was fixed upstream (`b679943`, `79e1779`, see `AGENTS.md`). Nothing
here attempts a reap.

Engine-free: stdlib only, no product/game import.
"""
from __future__ import annotations

import os
from dataclasses import dataclass
from typing import List, Optional

# Harness-owned provenance marker (B1). A unique per-boot token the harness injects
# into the launched process's environment. Ownership is identified by THIS marker
# (plus the captured PID + process start-time), never by gameId or binary path: a
# gameId-wide `games_kill` would terminate Daniel's own Steam Valheim, which has a
# different binary path but the same gameId "valheim". The marker is provenance the
# harness alone controls, so termination can be scoped to processes it launched.
#
# The name lives HERE rather than in `operator_drivers` because the launcher and the
# sweeper both need it and this is the lower layer. `operator_drivers` re-exports it,
# so existing importers are unaffected.
HARNESS_INSTANCE_ENV_VAR = "SBPR_QA_HARNESS_INSTANCE"

PROC_ROOT = "/proc"

# The only binary basename a sweep will ever consider signalling. Checked against
# the resolved `/proc/<pid>/exe`, not against argv, because argv is attacker- and
# wrapper-controlled while the exe link is the kernel's own answer.
VALHEIM_EXE_BASENAME = "valheim.x86_64"

# Separator inside the marker. The marker is `<run_id>:<actor>:<random>` — three
# fields, so a later sweep can recognise "a client MY run launched" without holding
# the in-process record that a SIGKILL destroys.
MARKER_SEPARATOR = ":"


def mint_marker(run_id: str, actor: str, unique: str) -> str:
    """Compose the per-boot harness marker. THE one authority for its format.

    `unique` makes two clients of the same run distinguishable; `run_id` is what
    makes the marker legible to a *later process* — before #455 the marker was
    `<actor>:<random>`, which only the launching process could interpret, so a run
    that was killed left clients no subsequent sweep could attribute.
    """
    return f"{run_id}{MARKER_SEPARATOR}{actor}{MARKER_SEPARATOR}{unique}"


def marker_run_id(marker: Optional[str]) -> Optional[str]:
    """The run id a marker names, or None when it names none.

    None for an empty marker or one with no separator at all. A pre-#455
    `<actor>:<random>` marker parses to its actor, which will never equal a run id,
    so it is still correctly unattributable — every path here fails closed toward
    leaving the process strictly alone rather than guessing at ownership.
    """
    if not marker:
        return None
    run_id, sep, rest = marker.partition(MARKER_SEPARATOR)
    if not sep or not run_id or not rest:
        return None
    return run_id


@dataclass(frozen=True)
class ProcIdentity:
    """A PID pinned to the two facts that make signalling it safe.

    `marker` is the harness instance marker read from the process's environment;
    `start_ticks` is `/proc/<pid>/stat` field 22. Both are re-read immediately
    before a signal and compared against the recorded pair.
    """

    pid: int
    marker: str
    start_ticks: int


def list_pids(proc_root: str = PROC_ROOT) -> Optional[List[int]]:
    """Every PID currently in `/proc`, or None when the table cannot be enumerated.

    None is distinct from an empty list on purpose: "no processes" and "I could not
    look" have opposite meanings for a sweeper, and conflating them would let an
    unreadable `/proc` report as a clean tree.
    """
    try:
        return sorted(int(d) for d in os.listdir(proc_root) if d.isdigit())
    except OSError:
        return None


def read_exe(pid: int, proc_root: str = PROC_ROOT) -> Optional[str]:
    """The resolved executable path of `pid`, or None (gone / zombie / EACCES)."""
    try:
        return os.readlink(os.path.join(proc_root, str(pid), "exe"))
    except OSError:
        return None


def is_valheim_exe(exe: Optional[str]) -> bool:
    """True only for a live process whose kernel-reported exe IS the game binary."""
    return exe is not None and os.path.basename(exe) == VALHEIM_EXE_BASENAME


def read_marker(pid: int, proc_root: str = PROC_ROOT) -> Optional[str]:
    """The harness instance marker in `pid`'s environment, or None.

    None covers both "no marker" and "environ unreadable". Both mean: NOT PROVABLY
    OURS, so the caller must leave the process strictly alone (B1).
    """
    try:
        with open(os.path.join(proc_root, str(pid), "environ"), "rb") as fh:
            raw = fh.read()
    except OSError:
        return None
    prefix = HARNESS_INSTANCE_ENV_VAR.encode() + b"="
    for entry in raw.split(b"\x00"):
        if entry.startswith(prefix):
            return entry.split(b"=", 1)[1].decode("utf-8", errors="replace")
    return None


def read_start_ticks(pid: int, proc_root: str = PROC_ROOT) -> Optional[int]:
    """Field 22 of `/proc/<pid>/stat` — the anti-PID-reuse pin. None if unreadable."""
    try:
        with open(
            os.path.join(proc_root, str(pid), "stat"), "r", encoding="utf-8", errors="replace"
        ) as fh:
            data = fh.read()
    except OSError:
        return None
    # The comm field is parenthesised and may itself contain spaces and parens, so
    # the split point is the LAST ')', not the first space.
    rparen = data.rfind(")")
    if rparen < 0:
        return None
    fields = data[rparen + 2:].split()
    # After comm: state=index 0 ... starttime is stat field 22 => index 19.
    if len(fields) <= 19:
        return None
    try:
        return int(fields[19])
    except ValueError:
        return None


def probe(pid: int, proc_root: str = PROC_ROOT) -> Optional[ProcIdentity]:
    """Full provenance of `pid`, or None when it cannot be established.

    Returns None if the marker is absent/unreadable OR the start time is unreadable.
    A caller may signal only a PID this returns an identity for, whose marker it
    recognises as its own.
    """
    marker = read_marker(pid, proc_root)
    if marker is None:
        return None
    start = read_start_ticks(pid, proc_root)
    if start is None:
        return None
    return ProcIdentity(pid=pid, marker=marker, start_ticks=start)


def count_live_nonzombie_valheim(proc_root: str = PROC_ROOT) -> int:
    """How many LIVE (non-zombie) Valheim clients are running, harness-owned or not.

    A zombie exposes no readable exe link, so counting resolvable exes structurally
    excludes them. Used as a defence-in-depth gate before any action that could
    conceivably reach a process outside its intended scope.
    """
    pids = list_pids(proc_root)
    if pids is None:
        return 0
    return sum(1 for pid in pids if is_valheim_exe(read_exe(pid, proc_root)))
