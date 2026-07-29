"""STATIC arrange checks (T022 ARRANGE spec §4 STATIC, §3 P3/P5/P8).

The checks that can be made before ANYTHING expensive happens: no process is started,
no game is contacted, no file is mutated. Full preflight+compose already runs
sub-second today and has caught two real defects; booting two GPU clients costs ~10
minutes per cycle (§2 I11). So every precondition that CAN be checked statically MUST
be, and it must be cheap enough to run every single time.

WHAT IS CHECKED (§4)
  S1  descriptor well-formed; per-client identity/roots/ports declared
  S2  production ports 2456/2466 absent from EVERY target
  S3  artifact source present; pins match deployed bytes
  S4  lane password policy consistent with client entries
  S5  per-client port sets disjoint                                        [I6]
plus the guards that belong with them:
  S6  every client's required artifacts exist in the catalogue
  S7  per-client destination paths live under that client's own game root
  S8  join target consistency (declared, reaches the lane, names a QA profile)

REPORTING CONTRACT (§3 P3 — the load-bearing part)
Every failure is a `StaticFailure` naming the **precondition**, the **client** it
applies to, and **expected vs actual**. The dominant failure mode of the current
system is silence: a missing plugin, an unreadable credential and a missing
`+connect` all produce the identical observable (a client at a menu), so a check that
merely returns False is worth almost nothing. Checks also do NOT short-circuit — one
invocation reports EVERY problem, because each ~10-minute boot cycle that discovers
one more problem is the exact cost this phase exists to avoid.

NO SYMMETRY ASSUMPTIONS
Not a single check here compares client A against client B other than to assert they
are *disjoint*. Nothing derives a path, uid, port or launcher from a sibling; every
check loops over `manifest.clients` and reports per actor. Adding a third client
changes only the manifest data.

FILESYSTEM SEAM: the only environment contact is `hash_file` / `path_exists`, injected
via `StaticEnvironment`. `real_static_environment()` wires real stdlib reads; the test
suite wires dicts. Importing or unit-testing this module touches nothing.

Engine-free: stdlib only, no product/game import.
"""
from __future__ import annotations

import hashlib
import os
from collections import defaultdict
from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Mapping, Optional, Sequence, Tuple

from .arrange_manifest import (
    PRODUCTION_PORTS,
    ArrangeManifest,
    ArrangeManifestError,
)

# Stable precondition identifiers. These are reported verbatim and are the thing an
# operator greps for, so they are part of the contract.
P_WELL_FORMED = "S1-MANIFEST-WELL-FORMED"
P_PRODUCTION_DENY = "S2-PRODUCTION-PORT-DENY"
P_ARTIFACT_PINS = "S3-ARTIFACT-PINS"
P_LANE_PASSWORD = "S4-LANE-PASSWORD-POLICY"
P_PORTS_DISJOINT = "S5-PORTS-DISJOINT"
P_ARTIFACT_CATALOGUE = "S6-ARTIFACT-CATALOGUE"
P_DEST_UNDER_ROOT = "S7-DEST-UNDER-CLIENT-ROOT"
P_JOIN_TARGET = "S8-JOIN-TARGET"

_GLOBAL = "<manifest>"  # `client` value for a check that is not per-client


@dataclass(frozen=True)
class StaticFailure:
    """One named, actionable precondition failure.

    `precondition` is the stable S-id; `client` is the actor it applies to (or
    `<manifest>` for a whole-manifest fact); `expected` / `actual` are the concrete
    values. `remedy` says what to change. A failure that cannot fill all of these in
    is not specific enough to be worth emitting.
    """

    precondition: str
    client: str
    detail: str
    expected: str
    actual: str
    remedy: str = ""

    def render(self) -> str:
        line = (
            f"[{self.precondition}] client={self.client}: {self.detail}\n"
            f"    expected: {self.expected}\n"
            f"    actual:   {self.actual}"
        )
        if self.remedy:
            line += f"\n    remedy:   {self.remedy}"
        return line


@dataclass(frozen=True)
class StaticReport:
    """The outcome of the static phase. `ok` is True only when NOTHING failed."""

    ok: bool
    failures: Sequence[StaticFailure] = field(default_factory=tuple)
    checked_clients: Sequence[str] = field(default_factory=tuple)
    checked_preconditions: Sequence[str] = field(default_factory=tuple)

    def render(self) -> str:
        if self.ok:
            return (
                "arrange STATIC: PASS — "
                f"{len(self.checked_preconditions)} precondition(s) over "
                f"{len(self.checked_clients)} client(s): {', '.join(self.checked_clients)}"
            )
        head = (
            f"arrange STATIC: FAIL — {len(self.failures)} precondition failure(s) "
            f"over client(s) {', '.join(self.checked_clients)}"
        )
        return "\n".join([head, *(f.render() for f in self.failures)])

    def as_dict(self) -> Dict[str, Any]:
        """Machine-readable form (§3 P7 — readiness is read, not inferred)."""
        return {
            "phase": "static",
            "ok": self.ok,
            "clients": list(self.checked_clients),
            "preconditions": list(self.checked_preconditions),
            "failures": [
                {
                    "precondition": f.precondition,
                    "client": f.client,
                    "detail": f.detail,
                    "expected": f.expected,
                    "actual": f.actual,
                    "remedy": f.remedy,
                }
                for f in self.failures
            ],
        }


@dataclass(frozen=True)
class StaticEnvironment:
    """The injectable filesystem seam. Reads only; never writes, never spawns.

    `path_exists` answers "is there a file here"; `hash_file` returns the sha256 hex
    of its bytes or None when it cannot be read. Returning None rather than raising
    keeps an unreadable deployed copy reportable as a specific failure instead of an
    exception that hides the other nine problems in the same manifest.
    """

    path_exists: Callable[[str], bool]
    hash_file: Callable[[str], Optional[str]]


def real_static_environment() -> StaticEnvironment:
    """Wire the REAL stdlib reads. Still starts no process and mutates nothing."""

    def _hash(path: str) -> Optional[str]:
        try:
            h = hashlib.sha256()
            with open(path, "rb") as fh:
                for chunk in iter(lambda: fh.read(65536), b""):
                    h.update(chunk)
            return h.hexdigest()
        except OSError:
            return None

    return StaticEnvironment(path_exists=os.path.isfile, hash_file=_hash)


# --------------------------------------------------------------------------- #
# S2 — production port deny
# --------------------------------------------------------------------------- #

def _check_production_deny(manifest: ArrangeManifest) -> List[StaticFailure]:
    """2456 (Niflheim) and 2466 (Heistan) hold REAL worlds and may never be a target.

    This guard has demonstrably prevented harm and is preserved verbatim in intent.
    It is applied to EVERY port-shaped value in the manifest — the lane, every client
    port, and every client's join target — not just the lane, because a client port
    set is just as capable of pointing a live process at a production world.
    """
    failures: List[StaticFailure] = []

    if manifest.lane.port in PRODUCTION_PORTS:
        failures.append(
            StaticFailure(
                precondition=P_PRODUCTION_DENY,
                client=_GLOBAL,
                detail=f"lane {manifest.lane.lane_id!r} targets a PRODUCTION port",
                expected=f"a lane port outside the hard deny list {sorted(PRODUCTION_PORTS)}",
                actual=f"lane.port={manifest.lane.port}",
                remedy="Point the disposable lane at a non-production port. These two "
                "ports hold real worlds (Niflheim 2456, Heistan 2466) and are never "
                "a legal QA target.",
            )
        )

    for client in manifest.clients:
        for name, port in sorted(client.ports.items()):
            if port in PRODUCTION_PORTS:
                failures.append(
                    StaticFailure(
                        precondition=P_PRODUCTION_DENY,
                        client=client.actor,
                        detail=f"port {name!r} is a PRODUCTION port",
                        expected=f"a port outside {sorted(PRODUCTION_PORTS)}",
                        actual=f"ports.{name}={port}",
                        remedy=f"Give {client.actor} a different {name} port.",
                    )
                )
        if client.join is not None and client.join.port in PRODUCTION_PORTS:
            failures.append(
                StaticFailure(
                    precondition=P_PRODUCTION_DENY,
                    client=client.actor,
                    detail="join target is a PRODUCTION world",
                    expected=f"a join port outside {sorted(PRODUCTION_PORTS)}",
                    actual=f"join={client.join.host}:{client.join.port}",
                    remedy="Point the client at the disposable lane, never a real world.",
                )
            )
    return failures


# --------------------------------------------------------------------------- #
# S5 — per-client port sets disjoint
# --------------------------------------------------------------------------- #

def _check_ports_disjoint(manifest: ArrangeManifest) -> List[StaticFailure]:
    """No two clients may claim the same port (§2 I6).

    `UnityScriptHost: Failed to bind 127.0.0.1:48210: Address already in use` — a
    hardcoded single-instance port. The loopback control ports were correctly
    per-client; UnityScriptHost and ValBridgeServer were not, so the second client
    silently lost a service. Checking disjointness here costs microseconds; finding it
    by boot costs ten minutes.

    Also reported: a client colliding with the lane's own port.
    """
    failures: List[StaticFailure] = []
    owners: Dict[int, List[Tuple[str, str]]] = defaultdict(list)
    for client in manifest.clients:
        for name, port in sorted(client.ports.items()):
            owners[port].append((client.actor, name))

    for port, claims in sorted(owners.items()):
        if len(claims) > 1:
            # Report against every claimant so no client is implicitly "the right one".
            rendered = ", ".join(f"{actor}.{name}" for actor, name in claims)
            for actor, name in claims:
                others = ", ".join(f"{a}.{n}" for a, n in claims if a != actor)
                failures.append(
                    StaticFailure(
                        precondition=P_PORTS_DISJOINT,
                        client=actor,
                        detail=f"port {port} is claimed by more than one client",
                        expected=f"{actor}.{name} to own port {port} exclusively",
                        actual=f"port {port} claimed by {rendered}",
                        remedy=f"Give {actor} a distinct {name} port; it currently "
                        f"collides with {others}. A colliding service fails to bind "
                        "and the client loses it silently.",
                    )
                )
        if port == manifest.lane.port:
            for actor, name in claims:
                failures.append(
                    StaticFailure(
                        precondition=P_PORTS_DISJOINT,
                        client=actor,
                        detail=f"client port {name!r} collides with the lane's own port",
                        expected=f"{actor}.{name} != lane.port ({manifest.lane.port})",
                        actual=f"{actor}.{name}={port} == lane.port={manifest.lane.port}",
                        remedy=f"Move {actor}.{name} off the lane port.",
                    )
                )
    return failures


# --------------------------------------------------------------------------- #
# S6 / S7 / S3 — artifacts: catalogue, destinations, pins
# --------------------------------------------------------------------------- #

def _check_artifact_catalogue(manifest: ArrangeManifest) -> List[StaticFailure]:
    """Every artifact a client requires must exist in the shared catalogue.

    §2 I1: the mod under test was present on one client and absent on the other for
    the entire effort, and a client without the harness boots normally, loads every
    product mod, and waits at a menu forever. A dangling reference here is that same
    class of silence, caught for free.
    """
    failures: List[StaticFailure] = []
    known = sorted(manifest.artifacts)
    for client in manifest.clients:
        for req in client.artifacts:
            if req.artifact not in manifest.artifacts:
                failures.append(
                    StaticFailure(
                        precondition=P_ARTIFACT_CATALOGUE,
                        client=client.actor,
                        detail=f"requires unknown artifact {req.artifact!r}",
                        expected=f"an artifact named in the catalogue: {known}",
                        actual=f"{req.artifact!r} is not in the catalogue",
                        remedy="Add the artifact to manifest.artifacts, or fix the "
                        "reference. A client that references nothing stages nothing "
                        "and boots to a menu with no error.",
                    )
                )
    return failures


def _check_dest_under_root(manifest: ArrangeManifest) -> List[StaticFailure]:
    """Each client's destination paths must live under THAT client's own game root.

    This is the check that makes the uid/path split safe rather than merely declared:
    a destination under a sibling's root would have one client writing into another's
    tree (across uids, where it either fails with EACCES or — worse — succeeds and
    corrupts the other client's install).
    """
    failures: List[StaticFailure] = []
    for client in manifest.clients:
        root = client.game_root.rstrip("/") + "/"
        for req in client.artifacts:
            if not req.dest_path.startswith(root):
                owner = next(
                    (
                        other.actor
                        for other in manifest.clients
                        if other.actor != client.actor
                        and req.dest_path.startswith(other.game_root.rstrip("/") + "/")
                    ),
                    None,
                )
                extra = (
                    f" It is under {owner}'s game root — one client must never write "
                    "into another's tree (different uids)."
                    if owner
                    else ""
                )
                failures.append(
                    StaticFailure(
                        precondition=P_DEST_UNDER_ROOT,
                        client=client.actor,
                        detail=f"artifact {req.artifact!r} stages outside this client's game root",
                        expected=f"a dest_path under {root}",
                        actual=f"dest_path={req.dest_path}",
                        remedy=f"Move the destination under {client.actor}'s own game "
                        f"root.{extra}",
                    )
                )
    return failures


def _check_artifact_pins(
    manifest: ArrangeManifest, env: StaticEnvironment
) -> List[StaticFailure]:
    """Sources must be present and hash to their pin; deployed copies must match too.

    §2 I8: a stale deployed launcher predating M6-LAUNCHENV was correctly refused by a
    byte-equality guard. That invariant is working as intended and is preserved here,
    generalised over every artifact and every client.

    A destination that does not exist yet is NOT a failure — staging (#452) is what
    creates it, and §2 I3 records that a stager unable to CREATE a new artifact was
    itself a defect. What IS a failure is a destination that exists with the WRONG
    bytes, because that is the stale-deploy case that silently runs old code.

    Source hashes are computed once per catalogue entry, not once per client, so an
    artifact required by N clients costs one read.
    """
    failures: List[StaticFailure] = []
    source_hashes: Dict[str, Optional[str]] = {}

    for name, artifact in sorted(manifest.artifacts.items()):
        if not env.path_exists(artifact.source_path):
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_PINS,
                    client=_GLOBAL,
                    detail=f"artifact {name!r} source is missing",
                    expected=f"a readable file at {artifact.source_path}",
                    actual="no such file",
                    remedy="Build/pack the artifact before arranging. Staging a "
                    "missing source is the silent no-op this phase exists to catch.",
                )
            )
            source_hashes[name] = None
            continue
        got = env.hash_file(artifact.source_path)
        source_hashes[name] = got
        if got is None:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_PINS,
                    client=_GLOBAL,
                    detail=f"artifact {name!r} source is unreadable",
                    expected=f"readable bytes at {artifact.source_path}",
                    actual="read failed (permissions or I/O error)",
                    remedy="Fix the source file's permissions for the arranging user.",
                )
            )
        elif got != artifact.sha256:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_PINS,
                    client=_GLOBAL,
                    detail=f"artifact {name!r} source drifted from its pin",
                    expected=f"sha256 {artifact.sha256} at {artifact.source_path}",
                    actual=f"sha256 {got}",
                    remedy="Either the source was rebuilt without repinning, or the "
                    "pin is stale. Re-pin deliberately; do not relax the check.",
                )
            )

    for client in manifest.clients:
        for req in client.artifacts:
            artifact = manifest.artifacts.get(req.artifact)
            if artifact is None:
                continue  # already reported by S6
            if not env.path_exists(req.dest_path):
                # Not yet staged. PROVISION (#452) creates it; a stager that could
                # only replace and never create was itself a defect (I3).
                continue
            deployed = env.hash_file(req.dest_path)
            if deployed is None:
                failures.append(
                    StaticFailure(
                        precondition=P_ARTIFACT_PINS,
                        client=client.actor,
                        detail=f"deployed artifact {req.artifact!r} is unreadable",
                        expected=f"readable bytes at {req.dest_path}",
                        actual="read failed (permissions or I/O error)",
                        remedy=f"The arranging user cannot read {client.actor}'s "
                        "deployed copy, so drift cannot be ruled out. Fix permissions.",
                    )
                )
            elif deployed != artifact.sha256:
                failures.append(
                    StaticFailure(
                        precondition=P_ARTIFACT_PINS,
                        client=client.actor,
                        detail=f"deployed artifact {req.artifact!r} is STALE",
                        expected=f"sha256 {artifact.sha256} at {req.dest_path}",
                        actual=f"sha256 {deployed}",
                        remedy="The deployed bytes predate the pin; this client would "
                        "run old code and report against the new source. Re-stage it.",
                    )
                )
    return failures


# --------------------------------------------------------------------------- #
# S4 — lane password policy consistency
# --------------------------------------------------------------------------- #

def _check_lane_password(manifest: ArrangeManifest) -> List[StaticFailure]:
    """The lane's declared password policy must match EVERY client entry.

    Preserves the M6-LANEPW guard (`live_preflight.validate_lane_password_consistency`)
    over the new manifest shape, per-client and in both directions. The original
    defect: the t009l lane was password-gated, the descriptor declared no password and
    no client named a password file, the provisioner correctly no-opped as an "open
    lane", the socket connected, vanilla's `RPC_ClientHandshake` took its
    `needPassword=true` branch and waited on `OnPasswordEntered` — a prompt no
    headless client will ever answer. `Player.OnSpawned` never fired and `TryArm` was
    never reached. Every layer behaved as designed; only the data was wrong.

    §2 I4 rides along here: a credential is only useful if the identity that CONSUMES
    it can read it. Credentials were written 0600 by uid 1000 and consumed by uid
    1001. So a client's password credential must declare that client's OWN uid as its
    consumer — checked per client, never assumed equal across clients.
    """
    failures: List[StaticFailure] = []
    requires = manifest.lane.requires_password

    for client in manifest.clients:
        cred = client.server_password_credential
        if requires and cred is None:
            failures.append(
                StaticFailure(
                    precondition=P_LANE_PASSWORD,
                    client=client.actor,
                    detail="password-gated lane, but this client declares no password credential",
                    expected="a `server_password` credential naming a path and consumer_uid",
                    actual="credentials.server_password is absent",
                    remedy="The client would join with no password, connect, then stall "
                    "forever on vanilla's password prompt — the handshake completes the "
                    "socket and hangs until teardown, and the helper never arms. Give "
                    f"{client.actor} a `server_password` credential.",
                )
            )
        elif not requires and cred is not None:
            failures.append(
                StaticFailure(
                    precondition=P_LANE_PASSWORD,
                    client=client.actor,
                    detail="open lane, but this client declares a password credential",
                    expected="no `server_password` credential when lane.requires_password is false",
                    actual=f"credentials.server_password.path={cred.path}",
                    remedy="Either set lane.requires_password true, or drop the "
                    f"credential from {client.actor}. An inconsistent policy means one "
                    "of the two statements is wrong and nothing else will tell you which.",
                )
            )

    # Credential readability is a per-client, per-credential fact (I4).
    for client in manifest.clients:
        for name, cred in sorted(client.credentials.items()):
            if cred.consumer_uid != client.uid:
                failures.append(
                    StaticFailure(
                        precondition=P_LANE_PASSWORD,
                        client=client.actor,
                        detail=f"credential {name!r} is consumed by a different uid than this client runs as",
                        expected=f"consumer_uid == {client.uid} (the uid {client.user!r} runs as)",
                        actual=f"consumer_uid={cred.consumer_uid}",
                        remedy="A credential written for one uid and read by another is "
                        "structurally unreadable in a 0700 directory, and the only "
                        "symptom is a client sitting at a menu. Declare the consuming "
                        "uid to be this client's own.",
                    )
                )

    # Two clients must not share a credential path: a 0600 file can be readable by
    # exactly one uid, so a shared path is unsatisfiable by construction.
    by_path: Dict[str, List[Tuple[str, str]]] = defaultdict(list)
    for client in manifest.clients:
        for name, cred in client.credentials.items():
            by_path[cred.path].append((client.actor, name))
    for path, claims in sorted(by_path.items()):
        uids = {
            c.uid
            for c in manifest.clients
            for n, cr in c.credentials.items()
            if cr.path == path
        }
        if len(claims) > 1 and len(uids) > 1:
            rendered = ", ".join(f"{a}.{n}" for a, n in claims)
            for actor, name in claims:
                failures.append(
                    StaticFailure(
                        precondition=P_LANE_PASSWORD,
                        client=actor,
                        detail=f"credential {name!r} shares a path with a different-uid client",
                        expected=f"a credential path private to {actor}",
                        actual=f"{path} is shared by {rendered} across uids {sorted(uids)}",
                        remedy="A mode-0600 credential is readable by exactly one uid. "
                        "Give each client its own credential path under a directory it "
                        "owns.",
                    )
                )
    return failures


# --------------------------------------------------------------------------- #
# S8 — join target
# --------------------------------------------------------------------------- #

def _check_join_target(manifest: ArrangeManifest) -> List[StaticFailure]:
    """Every client must be told, by a declared mechanism, which server to join.

    §2 I5 — the live blocker. client_a gets `+connect 127.0.0.1:2476` on its command
    line. client_b is launched as `steam -silent -applaunch 892970` with NO arguments
    under an `env -i` scrub, so `m_queuedJoinServer` is never populated. The harness
    patch hooks `ShowCharacterSelection` and drives `OnCharacterStart` — it automates
    *character select only* and relies on `+connect` having already queued the server.
    Without it the client stops at the server-list screen and the trigger never fires.

    Statically checkable, and therefore checked here:
      * the client declares a join target at all;
      * that target is the lane it is supposed to be on;
      * the declared delivery mechanism is compatible with its launcher (a
        `connect_argv` delivery is impossible under a launcher that passes no
        arguments — which is precisely how the gap was created);
      * a joining client names its QA-owned profile, so it can never load a human
        character (the M6-JOIN4 allowlist-of-one).

    Whether the target actually ARRIVES is a VERIFY-phase fact (#455) and is out of
    scope here; declaring an impossible delivery is not.
    """
    failures: List[StaticFailure] = []
    lane = manifest.lane

    # Which launcher kinds can carry `+connect` on a command line at all. Data, not a
    # branch: a new launcher declares its capability by appearing (or not) here.
    ARGV_CAPABLE = {"gabs", "direct_exec"}

    for client in manifest.clients:
        if client.join is None:
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail="no join target declared",
                    expected=f"a join target for lane {lane.lane_id!r} at {lane.host}:{lane.port}",
                    actual="client.join is absent",
                    remedy="A client with no join target boots normally, loads every "
                    "mod, and waits at a menu forever with nothing logged. Declare the "
                    "target and how it is delivered.",
                )
            )
            continue

        if (client.join.host, client.join.port) != (lane.host, lane.port):
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail="join target is not this run's lane",
                    expected=f"{lane.host}:{lane.port} (lane {lane.lane_id!r})",
                    actual=f"{client.join.host}:{client.join.port}",
                    remedy="Point the client at the lane this run actually brings up; "
                    "a client on a different server produces no receipts and no error.",
                )
            )

        if client.join.delivery == "connect_argv" and client.launcher.kind not in ARGV_CAPABLE:
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail="join delivery is impossible under this client's launcher",
                    expected=f"delivery 'connect_argv' requires a launcher in {sorted(ARGV_CAPABLE)}",
                    actual=f"launcher.kind={client.launcher.kind!r} with delivery='connect_argv'",
                    remedy=f"The {client.launcher.kind!r} launcher passes no arguments "
                    "to the game, so `+connect` never reaches it and "
                    "`m_queuedJoinServer` is never populated: the client stops at the "
                    "server-list screen and the character-select hook never fires. Use "
                    "a launcher that forwards argv, or a different delivery mechanism.",
                )
            )

        if not client.qa_profile:
            failures.append(
                StaticFailure(
                    precondition=P_JOIN_TARGET,
                    client=client.actor,
                    detail="joining client names no QA-owned profile",
                    expected="a `qa_profile` naming this client's own QA profile",
                    actual="qa_profile is absent",
                    remedy="A QA join MUST name its own profile (allowlist of one) so "
                    "it can never load a human character. Without it the client boots, "
                    "arms, and then correctly refuses the join — burning a full "
                    "launch/teardown to deliver nothing.",
                )
            )

    # Two clients sharing a QA profile would fight over the same character save.
    profiles: Dict[str, List[str]] = defaultdict(list)
    for client in manifest.clients:
        if client.qa_profile:
            profiles[client.qa_profile].append(client.actor)
    for profile, actors in sorted(profiles.items()):
        if len(actors) > 1:
            for actor in actors:
                failures.append(
                    StaticFailure(
                        precondition=P_JOIN_TARGET,
                        client=actor,
                        detail=f"QA profile {profile!r} is shared with another client",
                        expected=f"a QA profile private to {actor}",
                        actual=f"{profile!r} is claimed by {actors}",
                        remedy="Each client needs its own QA-owned profile; sharing one "
                        "means two clients contend for a single character save.",
                    )
                )
    return failures


# --------------------------------------------------------------------------- #
# Entrypoint
# --------------------------------------------------------------------------- #

ALL_PRECONDITIONS = (
    P_WELL_FORMED,
    P_PRODUCTION_DENY,
    P_ARTIFACT_PINS,
    P_LANE_PASSWORD,
    P_PORTS_DISJOINT,
    P_ARTIFACT_CATALOGUE,
    P_DEST_UNDER_ROOT,
    P_JOIN_TARGET,
)


def arrange_static(
    raw_manifest: object,
    env: Optional[StaticEnvironment] = None,
) -> StaticReport:
    """Run EVERY static arrange check. Starts no process; mutates nothing.

    Accepts the raw parsed JSON (or an already-parsed `ArrangeManifest`). A manifest
    that will not even parse yields a single S1 failure rather than an exception, so
    `arrange --check` has exactly one failure channel and always returns a report.

    Checks do not short-circuit: the report lists every problem found, because the
    whole point of the phase is to avoid discovering problems one 10-minute boot cycle
    at a time.
    """
    if env is None:
        env = real_static_environment()

    if isinstance(raw_manifest, ArrangeManifest):
        manifest: ArrangeManifest = raw_manifest
    else:
        try:
            manifest = ArrangeManifest.parse(raw_manifest)
        except ArrangeManifestError as exc:
            return StaticReport(
                ok=False,
                failures=(
                    StaticFailure(
                        precondition=P_WELL_FORMED,
                        client=_GLOBAL,
                        detail="manifest is not well-formed",
                        expected="a manifest matching the arrange schema "
                        "(kind/version/lane/artifacts/clients)",
                        actual=str(exc),
                        remedy="Fix the named field. Nothing else can be checked until "
                        "the manifest parses.",
                    ),
                ),
                checked_preconditions=(P_WELL_FORMED,),
            )

    failures: List[StaticFailure] = []
    failures.extend(_check_production_deny(manifest))
    failures.extend(_check_ports_disjoint(manifest))
    failures.extend(_check_artifact_catalogue(manifest))
    failures.extend(_check_dest_under_root(manifest))
    failures.extend(_check_artifact_pins(manifest, env))
    failures.extend(_check_lane_password(manifest))
    failures.extend(_check_join_target(manifest))

    return StaticReport(
        ok=not failures,
        failures=tuple(failures),
        checked_clients=tuple(manifest.actors),
        checked_preconditions=ALL_PRECONDITIONS,
    )
