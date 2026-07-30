"""Declarative per-client arrange manifest (T022 ARRANGE spec §1 A1, §3 P2).

WHY THIS EXISTS
---------------
The arrange phase is today four independent mechanisms that do not know about each
other (overlay packer, valbot artifact manifest + isolation lib, the runner's
credential writers, two different launchers). Every one of them was written for ONE
client and had a second bolted on, so the uid split is rediscovered painfully at each
layer in turn. Its dominant failure mode is SILENCE: a missing plugin, an unreadable
credential and a missing join target all produce the *identical* observable — a client
sitting at a menu with nothing logged.

This module is the single declarative description of *every* client: identity (unix
uid/user + Steam account), game root, launch mechanism, port set, required artifacts,
credential paths, and join target. It is a pure data model — parsing it starts no
process, reads no file, and contacts no game. `arrange_static` performs the cheap
checks over it; the later arrange phases (stage #451, sweep #455, verify #456, and the
runner cutover #457) consume the SAME object.

THE TWO STRUCTURAL RULES
------------------------
1. **Nothing is assumed symmetric.** uid, user, game root, binary, launcher kind,
   ports, credential paths and join delivery are ALL per-client fields with no
   defaults inherited from a sibling. There is no "the client" anywhere in this file.
2. **Adding a third client is a data change.** Every consumer iterates
   `manifest.clients`; there is no positional `client_a` / `client_b` anywhere, and
   launcher variation is expressed as a `kind` + parameter dict validated against the
   data-driven `LAUNCHER_KINDS` table rather than as a code branch. Adding a launcher
   flavour is likewise a data change.

Engine-free: stdlib only, no product/game import, no I/O.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any, Dict, List, Mapping, Optional, Sequence, Tuple

MANIFEST_KIND = "sbpr-qa-arrange-manifest"
# 3 (#455): `run_id` became a required top-level field. The bump is what makes an
# older manifest a NAMED refusal at :485 rather than a manifest that parses and then
# sweeps with an empty run identity — which would match nothing and silently leave
# every credential behind while reporting a clean sweep.
MANIFEST_VERSION = 3

# Hard production deny list (§3 P8 / B2). These hold REAL worlds — Niflheim 2456 and
# Heistan 2466 — and may never appear as a target anywhere in an arrange manifest.
# Mirrors live_preflight.PRODUCTION_PORTS and operator_drivers.PRODUCTION_PORTS.
PRODUCTION_PORTS = frozenset({2456, 2466})

# Every T022 client binds the QA helper and ValBridgeServer listeners. UnityScriptHost
# is different: the AT legs never use it, so a client may explicitly disable it with
# JSON null rather than allocating another needless port. Requiring all three names
# closes the loophole where an undeclared plugin still bound its compiled-in default.
REQUIRED_PORT_RESOURCES = frozenset(
    {"loopback_control", "valbridge_gabp", "unity_script_host"}
)
DISABLEABLE_PORT_RESOURCES = frozenset({"unity_script_host"})

_SHA256_RE = re.compile(r"^[0-9a-f]{64}$")

# Data-driven launcher table. A client declares `launcher: {kind, ...params}`; the
# kind must appear here and carry its required parameters. Adding a launch mechanism
# (a third client launched some other way) is an entry in THIS TABLE plus manifest
# data — not a new branch in any consumer. Nothing here executes a launch; these are
# the fields the later launch path — brought under the arrange phase by the runner
# cutover (#457) — will read.
#
# `wrapper_path` (optional on every kind) names the launch wrapper script that actually
# execs the game. GABS delivers neither per-launch env nor per-launch argv to the forked
# child, so the wrapper is the ONLY seam that can carry the join target across the fork.
# Naming it here lets the join-delivery preflight (#453) read the script and assert the
# seam is present BEFORE a ten-minute boot discovers it isn't. Optional because a
# launcher that execs the binary directly has no wrapper to check.
LAUNCHER_KINDS: Mapping[str, Mapping[str, Tuple[str, ...]]] = {
    # GABS/MCP mediated start (client_a today): games_start against a gameId.
    "gabs": {
        "required": ("endpoint", "game_id"),
        "optional": ("launch_env_path", "wrapper_path"),
    },
    # Steam `-applaunch <app_id>` under a scrubbed env (client_b today). The join
    # target CANNOT ride the command line here, which is exactly why `join` is a
    # first-class per-client field with its own declared delivery mechanism.
    "steam_applaunch": {
        "required": ("app_id",),
        "optional": ("systemd_unit", "steam_binary", "launch_env_path", "wrapper_path"),
    },
    # Plain exec of the binary (no Steam shim, no daemon). Useful for a third
    # client and for tests.
    "direct_exec": {
        "required": (),
        "optional": ("launch_env_path", "wrapper_path"),
    },
}

# How a client is told which server to join (§2 I5). client_a receives `+connect` on
# its command line; client_b is launched with no arguments and an `env -i` scrub, so
# it needs a different mechanism. Declaring the mechanism per client means the gap is
# visible in data instead of being an unstated assumption in a launcher script.
JOIN_DELIVERY_KINDS = frozenset({"connect_argv", "launch_env_sidecar", "harness_driven"})


class ArrangeManifestError(ValueError):
    """The manifest is structurally invalid. Message names the offending field.

    Raised only for shape errors (wrong type, missing key, unknown enum value) —
    i.e. things that make the manifest unreadable. Semantic preconditions (port
    denies, pin drift, password policy) are `arrange_static`'s job and are reported
    as named failures rather than raised, so a single run reports EVERY problem.
    """


def _require_mapping(value: Any, where: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise ArrangeManifestError(f"{where}: expected a JSON object, got {type(value).__name__}")
    return value


def _require_str(container: Mapping[str, Any], key: str, where: str) -> str:
    if key not in container:
        raise ArrangeManifestError(f"{where}: missing required field {key!r}")
    value = container[key]
    if not isinstance(value, str) or not value:
        raise ArrangeManifestError(f"{where}.{key}: expected a non-empty string, got {value!r}")
    return value


def _require_int(container: Mapping[str, Any], key: str, where: str) -> int:
    if key not in container:
        raise ArrangeManifestError(f"{where}: missing required field {key!r}")
    value = container[key]
    # bool is an int subclass; a boolean here is always a mistake.
    if isinstance(value, bool) or not isinstance(value, int):
        raise ArrangeManifestError(f"{where}.{key}: expected an integer, got {value!r}")
    return value


def _require_bool(container: Mapping[str, Any], key: str, where: str) -> bool:
    if key not in container:
        raise ArrangeManifestError(
            f"{where}: missing required field {key!r} (fail closed: it is never inferred)"
        )
    value = container[key]
    if not isinstance(value, bool):
        raise ArrangeManifestError(f"{where}.{key}: expected a boolean, got {value!r}")
    return value


def _require_abs_path(container: Mapping[str, Any], key: str, where: str) -> str:
    value = _require_str(container, key, where)
    if not value.startswith("/"):
        raise ArrangeManifestError(
            f"{where}.{key}: expected an ABSOLUTE path, got {value!r} "
            "(relative paths resolve against whichever cwd the phase happens to run in, "
            "which differs per client and per launcher)"
        )
    return value


@dataclass(frozen=True)
class Artifact:
    """One deployable file in the shared catalogue, pinned by content hash.

    `name` is the reference key clients use. `source_path` is where the packer/build
    left the authoritative bytes. `sha256` is the pin: the source must hash to it and
    any already-deployed copy must too (§2 I8 — a stale deployed launcher was
    correctly refused by exactly this guard; keep it).
    """

    name: str
    source_path: str
    sha256: str

    @staticmethod
    def parse(raw: Any, where: str) -> "Artifact":
        data = _require_mapping(raw, where)
        name = _require_str(data, "name", where)
        source_path = _require_abs_path(data, "source_path", f"{where}[{name}]")
        digest = _require_str(data, "sha256", f"{where}[{name}]")
        if not _SHA256_RE.match(digest):
            raise ArrangeManifestError(
                f"{where}[{name}].sha256: expected a lowercase 64-hex sha256, got {digest!r}"
            )
        return Artifact(name=name, source_path=source_path, sha256=digest)


@dataclass(frozen=True)
class ArtifactRequirement:
    """A client's requirement that catalogue artifact `artifact` land at `dest_path`.

    Both halves are per-client: two clients requiring the same artifact will as a rule
    put it at DIFFERENT absolute paths, because their game roots differ. Nothing here
    derives one client's destination from another's (§2 I1 — the harness plugin was
    present on one client and absent on the other for the entire effort, and the only
    thing that ever noticed was a manual `diff`).
    """

    artifact: str
    dest_path: str

    @staticmethod
    def parse(raw: Any, where: str) -> "ArtifactRequirement":
        data = _require_mapping(raw, where)
        artifact = _require_str(data, "artifact", where)
        dest_path = _require_abs_path(data, "dest_path", f"{where}[{artifact}]")
        return ArtifactRequirement(artifact=artifact, dest_path=dest_path)


@dataclass(frozen=True)
class Credential:
    """A per-run credential file: where it goes, who must be able to READ it.

    `consumer_uid` is the load-bearing field (§2 I4). Credentials were written 0600 in
    a 0700 directory by uid 1000 and consumed by uid 1001 — structurally impossible,
    and the only symptom was a client waiting at a menu. Declaring the consuming uid
    per credential makes that a checkable fact instead of an assumption.
    """

    name: str
    path: str
    consumer_uid: int
    mode: int = 0o644

    @staticmethod
    def parse(name: str, raw: Any, where: str) -> "Credential":
        data = _require_mapping(raw, f"{where}[{name}]")
        path = _require_abs_path(data, "path", f"{where}[{name}]")
        consumer_uid = _require_int(data, "consumer_uid", f"{where}[{name}]")
        mode = data.get("mode", 0o644)
        if isinstance(mode, str):
            try:
                mode = int(mode, 8)
            except ValueError:
                raise ArrangeManifestError(
                    f"{where}[{name}].mode: expected an octal string like '0600', got {mode!r}"
                )
        if isinstance(mode, bool) or not isinstance(mode, int):
            raise ArrangeManifestError(f"{where}[{name}].mode: expected an integer, got {mode!r}")
        if mode != 0o644:
            raise ArrangeManifestError(
                f"{where}[{name}].mode: expected exactly '0644' for the approved "
                f"cross-uid throwaway-credential policy, got {mode:#06o}"
            )
        return Credential(name=name, path=path, consumer_uid=consumer_uid, mode=mode)


@dataclass(frozen=True)
class Launcher:
    """How ONE client is started. `kind` selects from the data-driven table above."""

    kind: str
    params: Mapping[str, Any] = field(default_factory=dict)

    @staticmethod
    def parse(raw: Any, where: str) -> "Launcher":
        data = _require_mapping(raw, where)
        kind = _require_str(data, "kind", where)
        if kind not in LAUNCHER_KINDS:
            raise ArrangeManifestError(
                f"{where}.kind: unknown launcher kind {kind!r}; known kinds are "
                f"{sorted(LAUNCHER_KINDS)}. Adding a launch mechanism is an entry in "
                "LAUNCHER_KINDS plus manifest data, never a branch in a consumer."
            )
        spec = LAUNCHER_KINDS[kind]
        params = {k: v for k, v in data.items() if k != "kind"}
        missing = [p for p in spec["required"] if p not in params]
        if missing:
            raise ArrangeManifestError(
                f"{where}: launcher kind {kind!r} requires parameter(s) {missing} "
                f"(required: {list(spec['required'])}, optional: {list(spec['optional'])})"
            )
        allowed = set(spec["required"]) | set(spec["optional"])
        unknown = sorted(set(params) - allowed)
        if unknown:
            raise ArrangeManifestError(
                f"{where}: launcher kind {kind!r} does not accept parameter(s) {unknown} "
                f"(allowed: {sorted(allowed)}). An unrecognised parameter is silently "
                "ignored by every launcher, which is precisely the failure mode this "
                "manifest exists to remove."
            )
        return Launcher(kind=kind, params=params)


@dataclass(frozen=True)
class JoinTarget:
    """Which server this client joins, and HOW it is told (§2 I5).

    The join target reaching the game is a separate fact from the target existing.
    client_a gets `+connect host:port` on its command line; client_b is launched with
    no arguments under `env -i`, so `m_queuedJoinServer` is never populated and it
    stops at the server-list screen, where the harness's character-select hook never
    fires. Declaring `delivery` per client makes "how does THIS client learn the
    target" an explicit, inspectable field.
    """

    host: str
    port: int
    delivery: str

    @staticmethod
    def parse(raw: Any, where: str) -> "JoinTarget":
        data = _require_mapping(raw, where)
        host = _require_str(data, "host", where)
        port = _require_int(data, "port", where)
        delivery = _require_str(data, "delivery", where)
        if delivery not in JOIN_DELIVERY_KINDS:
            raise ArrangeManifestError(
                f"{where}.delivery: unknown join delivery {delivery!r}; known: "
                f"{sorted(JOIN_DELIVERY_KINDS)}"
            )
        return JoinTarget(host=host, port=port, delivery=delivery)


@dataclass(frozen=True)
class ClientEntry:
    """EVERY per-client fact, with nothing inherited from a sibling client.

    identity ......... `actor` (stable id), `uid` + `user` (unix), `steam_account`
    filesystem ....... `game_root`, `binary_path`, `plugins_dir`
    launch ........... `launcher` (kind + params), `join`
    resources ........ `ports` (named -> port; per-client, checked disjoint)
    provisioning ..... `artifacts` (catalogue refs + per-client destinations)
    credentials ...... `credentials` (path + consuming uid per credential)
    """

    actor: str
    uid: int
    user: str
    steam_account: str
    game_root: str
    binary_path: str
    plugins_dir: str
    launcher: Launcher
    ports: Mapping[str, Optional[int]]
    artifacts: Sequence[ArtifactRequirement]
    credentials: Mapping[str, Credential]
    join: Optional[JoinTarget] = None
    qa_profile: Optional[str] = None

    @property
    def bound_ports(self) -> Mapping[str, int]:
        """The listeners this client will actually bind (disabled resources omitted)."""
        return {name: port for name, port in self.ports.items() if port is not None}

    @property
    def server_password_credential(self) -> Optional[Credential]:
        """The lane-password credential, if this client declares one."""
        return self.credentials.get("server_password")

    @staticmethod
    def parse(raw: Any, index: int) -> "ClientEntry":
        data = _require_mapping(raw, f"clients[{index}]")
        actor = _require_str(data, "actor", f"clients[{index}]")
        where = f"client {actor!r}"

        if "ports" not in data:
            raise ArrangeManifestError(
                f"{where}: missing required field 'ports' (every bound resource must be "
                "declared per client; UnityScriptHost may be explicitly null/disabled)"
            )
        ports_raw = data["ports"]
        ports_map = _require_mapping(ports_raw, f"{where}.ports")
        missing_ports = sorted(REQUIRED_PORT_RESOURCES - set(ports_map))
        if missing_ports:
            raise ArrangeManifestError(
                f"{where}.ports: missing required resource declaration(s) {missing_ports}; "
                "declare loopback_control and valbridge_gabp as integer ports, and declare "
                "unity_script_host as an integer or null to disable it"
            )
        ports: Dict[str, Optional[int]] = {}
        for pname, pval in ports_map.items():
            pname = str(pname)
            if pval is None:
                if pname not in DISABLEABLE_PORT_RESOURCES:
                    raise ArrangeManifestError(
                        f"{where}.ports.{pname}: null/disabled is not allowed; this listener "
                        "is required for T022 and must declare an integer port"
                    )
                ports[pname] = None
                continue
            if isinstance(pval, bool) or not isinstance(pval, int):
                raise ArrangeManifestError(
                    f"{where}.ports.{pname}: expected an integer port, got {pval!r}"
                )
            if not (1 <= pval <= 65535):
                raise ArrangeManifestError(
                    f"{where}.ports.{pname}: port {pval} is outside 1-65535"
                )
            ports[pname] = pval

        artifacts_raw = data.get("artifacts", [])
        if not isinstance(artifacts_raw, (list, tuple)):
            raise ArrangeManifestError(f"{where}.artifacts: expected a list")
        artifacts = [
            ArtifactRequirement.parse(a, f"{where}.artifacts") for a in artifacts_raw
        ]

        creds_raw = _require_mapping(data.get("credentials", {}), f"{where}.credentials")
        credentials = {
            str(name): Credential.parse(str(name), c, f"{where}.credentials")
            for name, c in creds_raw.items()
        }

        join_raw = data.get("join")
        join = JoinTarget.parse(join_raw, f"{where}.join") if join_raw is not None else None

        qa_profile = data.get("qa_profile")
        if qa_profile is not None and (not isinstance(qa_profile, str) or not qa_profile):
            raise ArrangeManifestError(
                f"{where}.qa_profile: expected a non-empty string or absent, got {qa_profile!r}"
            )

        return ClientEntry(
            actor=actor,
            uid=_require_int(data, "uid", where),
            user=_require_str(data, "user", where),
            steam_account=_require_str(data, "steam_account", where),
            game_root=_require_abs_path(data, "game_root", where),
            binary_path=_require_abs_path(data, "binary_path", where),
            plugins_dir=_require_abs_path(data, "plugins_dir", where),
            launcher=Launcher.parse(data.get("launcher"), f"{where}.launcher"),
            ports=ports,
            artifacts=tuple(artifacts),
            credentials=credentials,
            join=join,
            qa_profile=qa_profile,
        )


@dataclass(frozen=True)
class LaneEntry:
    """The disposable lane every client joins.

    `requires_password` is REQUIRED and never inferred (M6-LANEPW): the t009l lane was
    password-gated while the descriptor declared nothing, so the provisioner correctly
    no-opped as an "open lane", the client connected, vanilla took its
    `needPassword=true` branch, and the handshake waited forever on a prompt no
    headless client answers. Every layer behaved as designed; only the data was wrong,
    and nothing checked it.
    """

    lane_id: str
    world_name: str
    host: str
    port: int
    requires_password: bool

    @staticmethod
    def parse(raw: Any) -> "LaneEntry":
        data = _require_mapping(raw, "lane")
        return LaneEntry(
            lane_id=_require_str(data, "lane_id", "lane"),
            world_name=_require_str(data, "world_name", "lane"),
            host=_require_str(data, "host", "lane"),
            port=_require_int(data, "port", "lane"),
            requires_password=_require_bool(data, "requires_password", "lane"),
        )


@dataclass(frozen=True)
class ArrangeManifest:
    """The whole arrange manifest: one lane, one artifact catalogue, N clients.

    `run_id` identifies THIS run and is required (#455). It is the fact that lets a
    later sweep tell its own residue from a file an operator placed deliberately: the
    runner stamps it into a provenance sidecar beside every credential it writes, and
    the sweeper matches on it. Without it, a sweeper at a declared path can only
    guess, and a guessing sweeper that deletes credentials is worse than none.
    """

    lane: LaneEntry
    artifacts: Mapping[str, Artifact]
    clients: Sequence[ClientEntry]
    run_id: str
    version: int = MANIFEST_VERSION

    def client(self, actor: str) -> ClientEntry:
        for c in self.clients:
            if c.actor == actor:
                return c
        raise KeyError(actor)

    @property
    def actors(self) -> List[str]:
        return [c.actor for c in self.clients]

    @staticmethod
    def parse(raw: Any) -> "ArrangeManifest":
        """Parse + shape-validate. Reads no file, starts no process."""
        data = _require_mapping(raw, "manifest")
        kind = data.get("kind")
        if kind != MANIFEST_KIND:
            raise ArrangeManifestError(
                f"manifest.kind: expected {MANIFEST_KIND!r}, got {kind!r}"
            )
        version = _require_int(data, "version", "manifest")
        if version != MANIFEST_VERSION:
            raise ArrangeManifestError(
                f"manifest.version: expected {MANIFEST_VERSION}, got {version} "
                "(this runner does not understand that schema revision)"
            )

        artifacts_raw = data.get("artifacts", [])
        if not isinstance(artifacts_raw, (list, tuple)):
            raise ArrangeManifestError("manifest.artifacts: expected a list")
        artifacts: Dict[str, Artifact] = {}
        for a in artifacts_raw:
            art = Artifact.parse(a, "manifest.artifacts")
            if art.name in artifacts:
                raise ArrangeManifestError(
                    f"manifest.artifacts: duplicate artifact name {art.name!r}"
                )
            artifacts[art.name] = art

        clients_raw = data.get("clients")
        if not isinstance(clients_raw, (list, tuple)) or not clients_raw:
            raise ArrangeManifestError("manifest.clients: expected a non-empty list")
        clients = [ClientEntry.parse(c, i) for i, c in enumerate(clients_raw)]
        seen: Dict[str, int] = {}
        for i, c in enumerate(clients):
            if c.actor in seen:
                raise ArrangeManifestError(
                    f"manifest.clients: duplicate actor {c.actor!r} at index {i} "
                    f"(already at index {seen[c.actor]}); actors are the identity key "
                    "every phase reports against and must be unique"
                )
            seen[c.actor] = i

        return ArrangeManifest(
            lane=LaneEntry.parse(data.get("lane")),
            artifacts=artifacts,
            clients=tuple(clients),
            run_id=_require_str(data, "run_id", "manifest"),
            version=version,
        )
