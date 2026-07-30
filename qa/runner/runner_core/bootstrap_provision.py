"""Derive + emit the per-client arm-bootstrap docs from the run descriptor (M6-LAUNCHENV).

## The gap this closes

`t_2a954860` found that `t022-bootstrap-client_{a,b}.json` were **pre-provisioned
operator inputs the runner never wrote** on the non-test live path — so a human had to
hand-author them, copying values out of the descriptor's wire/pins/lane by hand. Hand-
authored docs go stale silently: the ones on disk pinned helper `8436e740` against a
deployed `135f6029` and had already expired, which is exactly why the run could not arm.

This module makes the provisioning path **emit** each client's bootstrap doc from the
single source of truth — the run descriptor — so the doc can never drift from the wire
block it is supposed to match. Nothing here is fabricated: every value is copied from a
descriptor field (`wire`, `pins`, `lane`, and the client's own `role`/`verbs`).

## The bootstrap doc shape (mirrors `ArmBootstrapParser.Parse`)

    {enabled, role, actor, worldUid, worldName, nonce, expiry, hmacSecret,
     operatorToken, loopbackPort, verbs:"A,B,C",
     hashes:{product,helper,game,bepinex,harmony,scenario}}

The doc carries the HMAC secret and operator token, so it is written **mode 0644** in a
**0711** directory and removed on teardown. This permits a known-path read by the
uid-1001 client without making the directory listable. (The launch-env sidecar carries only the doc's
PATH plus two non-secret ids and is 0644; see `launch_env.py`.)

Engine-free stdlib only. No Valheim/BepInEx/Unity import.
"""
from __future__ import annotations

import json
import os
import time
from dataclasses import dataclass
from typing import Any, Dict, List, Mapping, Optional

from .credential_access import (
    CredentialReadError,
    ReadAsUid,
    assert_readable_as_consumer,
    prepare_credential_directory,
)
from .credential_provenance import (
    CredentialProvenance,
    CredentialProvenanceError,
    provenance_path,
    write_provenance,
)
from .manifest import REQUIRED_PARTS


class BootstrapProvisionError(RuntimeError):
    """A bootstrap doc could not be derived from the descriptor. Fail closed."""


@dataclass(frozen=True)
class ProvisionedBootstrap:
    """A written bootstrap doc's location + provenance, recorded so teardown can remove it."""

    path: str
    actor: str


def build_bootstrap_doc(
    *,
    role: str,
    actor: str,
    wire: Mapping[str, Any],
    pins: Mapping[str, str],
    lane: Mapping[str, Any],
    verbs: str,
    loopback_port: int,
) -> Dict[str, Any]:
    """Assemble ONE client's bootstrap doc dict from descriptor fields only.

    Every value is copied from the descriptor: `worldUid`/`worldName` from the lane,
    the crypto envelope (`nonce`/`expiry`/`hmacSecret`/`operatorToken`) from the wire,
    and the six hashes from the pins. Nothing is invented. Fails closed on a missing
    required field so a half-formed descriptor can never produce a doc that silently
    won't arm.
    """
    for key in ("nonce", "expiry_unix_ms", "hmac_secret", "operator_token"):
        if key not in wire:
            raise BootstrapProvisionError(f"descriptor wire missing {key!r}")
    for key in ("world_uid", "world_name"):
        if key not in lane:
            raise BootstrapProvisionError(f"descriptor lane missing {key!r}")
    missing_pins = [p for p in REQUIRED_PARTS if p not in pins]
    if missing_pins:
        raise BootstrapProvisionError(f"descriptor pins missing part(s): {missing_pins}")
    if not verbs:
        raise BootstrapProvisionError(f"client {actor!r} has no verbs to permit")

    return {
        "enabled": 1,
        "role": role,
        "actor": actor,
        "worldUid": int(lane["world_uid"]),
        "worldName": str(lane["world_name"]),
        "nonce": str(wire["nonce"]),
        "expiry": int(wire["expiry_unix_ms"]),
        "hmacSecret": str(wire["hmac_secret"]),
        "operatorToken": str(wire["operator_token"]),
        "loopbackPort": int(loopback_port),
        "verbs": str(verbs),
        "hashes": {part: str(pins[part]) for part in REQUIRED_PARTS},
    }


class BootstrapProvisioner:
    """Write, consumer-read-verify, and remove per-client bootstrap docs.

    Each doc is written mode 0644 (it carries per-run, short-TTL credentials) at the
    exact `bootstrap_path` the descriptor names for that client — which is also the path
    the launch-env sidecar advertises to the helper via `SBPR_QA_T022_BOOTSTRAP`. Every
    write is tracked so `remove`/`remove_all` clears the secret-bearing docs on teardown.
    """

    def __init__(self, *, read_as_uid: Optional[ReadAsUid] = None) -> None:
        self._written: Dict[str, ProvisionedBootstrap] = {}
        self._read_as_uid = read_as_uid

    def provision_from_descriptor(self, descriptor: Mapping[str, Any]) -> List[ProvisionedBootstrap]:
        """Derive + write a bootstrap doc for every client in the descriptor.

        Reads `descriptor["clients"]` for each client's `actor`, `role` (default
        "Client"), `verbs`, `loopback_port`, and `bootstrap_path`, and the shared
        `wire`/`pins`/`lane`. Returns the written docs. Fails closed on any client
        missing `bootstrap_path` (nowhere to write) or `verbs` (nothing to permit).
        """
        wire = descriptor.get("wire")
        pins = descriptor.get("pins")
        lane = descriptor.get("lane")
        if not isinstance(wire, Mapping) or not isinstance(pins, Mapping) or not isinstance(lane, Mapping):
            raise BootstrapProvisionError("descriptor must carry wire+pins+lane to provision bootstraps")
        clients = descriptor.get("clients")
        if not isinstance(clients, (list, tuple)) or not clients:
            raise BootstrapProvisionError("descriptor has no clients to provision")

        # #455: the run id every doc this call writes is stamped with, and the TTL the
        # wire already mints. Refused rather than defaulted: an unattributable
        # credential is one a later sweep must leave behind forever.
        run_id = descriptor.get("run_id")
        if not run_id or not isinstance(run_id, str):
            raise BootstrapProvisionError(
                "descriptor carries no non-empty 'run_id'; refusing to write a "
                "credential no later sweep could attribute to the run that minted it "
                "(fail closed)"
            )
        # The TTL the wire already minted. Validated here rather than indexed blindly:
        # `build_bootstrap_doc` names this field in a fail-closed check, and reaching
        # past it with a raw index would turn that named error into a bare KeyError.
        expiry_raw = wire.get("expiry_unix_ms")
        if isinstance(expiry_raw, bool) or not isinstance(expiry_raw, int):
            raise BootstrapProvisionError(
                f"descriptor wire 'expiry_unix_ms' must be an integer, got {expiry_raw!r}"
            )
        expiry_unix_ms = int(expiry_raw)

        written: List[ProvisionedBootstrap] = []
        try:
            for c in clients:
                actor = str(c.get("actor", ""))
                path = c.get("bootstrap_path")
                if not path:
                    raise BootstrapProvisionError(
                        f"client {actor!r} has no bootstrap_path; cannot provision its arm doc"
                    )
                consumer_uid = c.get("uid")
                if consumer_uid is None:
                    raise BootstrapProvisionError(
                        f"client {actor!r} has no consuming uid for bootstrap path "
                        f"{path!r}; readability must be proved as the identity that "
                        "launches this client"
                    )
                verbs = str(c.get("verbs", ""))
                role = str(c.get("role", "Client"))
                loopback = c.get("loopback_port")
                if loopback is None:
                    raise BootstrapProvisionError(
                        f"client {actor!r} at bootstrap path {path!r} as consuming uid "
                        f"{consumer_uid} has no loopback_port"
                    )
                doc = build_bootstrap_doc(
                    role=role,
                    actor=actor,
                    wire=wire,
                    pins=pins,
                    lane=lane,
                    verbs=verbs,
                    loopback_port=int(loopback),
                )
                written.append(
                    self._write_doc(
                        str(path), actor, int(consumer_uid), doc, run_id, expiry_unix_ms
                    )
                )
                assert_readable_as_consumer(
                    actor=actor,
                    path=str(path),
                    consumer_uid=int(consumer_uid),
                    read_as_uid=self._read_as_uid,
                )
        except CredentialReadError as exc:
            self.remove_all()
            raise BootstrapProvisionError(str(exc)) from exc
        except Exception:
            self.remove_all()
            raise
        return written

    def _write_doc(
        self,
        path: str,
        actor: str,
        consumer_uid: int,
        doc: Mapping[str, Any],
        run_id: str,
        expiry_unix_ms: int,
    ) -> ProvisionedBootstrap:
        if not os.path.isabs(path):
            raise BootstrapProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: bootstrap_path must be absolute"
            )
        directory = os.path.dirname(path)
        try:
            prepare_credential_directory(directory)
        except OSError as exc:
            raise BootstrapProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: cannot prepare mode-0711 directory: {exc}"
            ) from exc
        if os.path.islink(path):
            raise BootstrapProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: refusing symlink destination"
            )
        content = json.dumps(doc, indent=2, sort_keys=False)
        tmp = f"{path}.tmp.{os.getpid()}"
        # 0644 — the uid-1001 client must read a uid-1000-written per-run document.
        try:
            fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o644)
            with os.fdopen(fd, "w", encoding="utf-8") as fh:
                fh.write(content)
                fh.write("\n")
            os.replace(tmp, path)
            os.chmod(path, 0o644)
        except Exception as exc:
            cleanup_errors = _cleanup_failed_install(tmp, path)
            if cleanup_errors:
                raise BootstrapProvisionError(
                    f"credential provision failed for client {actor!r} at {path!r} "
                    f"as consuming uid {consumer_uid}: {type(exc).__name__}: {exc}; "
                    f"cleanup FAILED (credential may remain): {'; '.join(cleanup_errors)}"
                ) from exc
            raise BootstrapProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: {type(exc).__name__}: {exc}"
            ) from exc
        prov = ProvisionedBootstrap(path=path, actor=actor)
        # Stamp ownership provenance beside the doc (#455). Unlike the lane password
        # this one carries a REAL expiry — the wire's TTL, the same value the C# arm
        # gate enforces — so a sweeper can distinguish "residue of a prior run" from
        # "still-valid credential of a concurrent run" without consulting anything but
        # the sidecar. A sidecar rather than an added `sbprQaRun` member inside the doc:
        # `ArmBootstrapParser.Parse` does ignore unknown members (verified), so a field
        # would work, but one mechanism for both credential kinds means the sweeper has
        # one code path, and a cleanup feature never alters a format that crosses into a
        # fail-closed arming gate.
        try:
            write_provenance(
                CredentialProvenance(
                    run_id=run_id,
                    actor=actor,
                    credential_path=path,
                    minted_unix_ms=int(time.time() * 1000),
                    expiry_unix_ms=expiry_unix_ms,
                )
            )
        except CredentialProvenanceError as exc:
            # Unwind the doc: a secret-bearing credential with no provenance is one a
            # later sweep is obliged to leave behind, which is the exact residue this
            # stamping exists to prevent.
            _best_effort_unlink(path)
            raise BootstrapProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: {exc}"
            ) from exc
        self._written[path] = prov
        return prov

    def remove(self, path: str) -> None:
        """Remove the secret-bearing bootstrap doc at `path`. Idempotent.

        Removes its ownership-provenance sidecar too: a sidecar outliving the
        credential it describes is itself residue, and it would make a later sweep
        report an ownership decision about a file that no longer exists.
        """
        self._written.pop(path, None)
        _best_effort_unlink(path)
        _best_effort_unlink(provenance_path(path))

    def remove_all(self) -> None:
        for path in list(self._written):
            self.remove(path)

    @property
    def written(self) -> List[ProvisionedBootstrap]:
        return list(self._written.values())


def _best_effort_unlink(path: str) -> None:
    try:
        os.unlink(path)
    except FileNotFoundError:
        return
    except OSError:
        return


def _cleanup_failed_install(*paths: str) -> List[str]:
    """Clean a failed install and report every path that could not be removed."""
    errors: List[str] = []
    for path in paths:
        try:
            os.unlink(path)
        except FileNotFoundError:
            continue
        except OSError as exc:
            errors.append(f"{path!r}: {type(exc).__name__}: {exc}")
    return errors
