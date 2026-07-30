"""Produce + tear down the per-client lane-password file (M6-JOIN3 / B2).

## The gap this closes

The M6-JOIN3 branch shipped a *consumer* — the QA FejdStartup auto-join hook reads a
credential file named by `SBPR_QA_SERVER_PASSWORD_FILE` and sets vanilla
`FejdStartup.ServerPassword` from it so a password-gated lane's handshake auto-submits
the password headless. But NOTHING wrote that file and NOTHING removed it. A consumer of
a credential with no producer and no teardown is worse than no feature: it leaves a live
credential path with no lifecycle, and violates the card's "removed on teardown" rule.

This module is the missing producer. It mirrors `BootstrapProvisioner` discipline exactly:

  * writes the file **mode 0644** in a **0711** directory (known-path cross-uid read,
    without directory listing),
  * writes it **atomically** (temp + fsync-free O_CREAT|O_EXCL-free rename — same as the
    bootstrap doc), refusing to follow a symlink out of the intended directory,
  * derives the password **from the descriptor** (a single source of truth), never invents
    or logs it,
  * tracks every write so `remove`/`remove_all` unlink it on **every** teardown exit path
    (success, failure, timeout, abort, block).

The password VALUE never touches the 0644 launch-env sidecar — only the file PATH rides
there (`SBPR_QA_SERVER_PASSWORD_FILE`), exactly like the bootstrap doc's path rides
the sidecar as `SBPR_QA_T022_BOOTSTRAP`. The value is never placed in a log line or an
exception message by this module.

Engine-free stdlib only. No Valheim/BepInEx/Unity import.
"""
from __future__ import annotations

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


class LanePasswordProvisionError(RuntimeError):
    """A lane-password file could not be produced from the descriptor. Fail closed.

    Note: this error type is deliberately raised with messages that name the client/path
    but NEVER the password value — a credential must not surface in a traceback.
    """


@dataclass(frozen=True)
class ProvisionedLanePassword:
    """A written password file's location + which client it serves (for teardown).

    Deliberately does NOT store the password value — only its path — so the value cannot
    leak through a repr of this record.
    """

    path: str
    actor: str


class LanePasswordProvisioner:
    """Write, verify, and remove per-client lane-password files.

    The password value is read once from `descriptor["lane_password"]` (a run secret,
    alongside the wire secrets) and written to the exact `server_password_file` path each
    client names — which is also the path the launch-env sidecar advertises to the helper
    via `SBPR_QA_SERVER_PASSWORD_FILE`. Every write is tracked so `remove`/`remove_all`
    clears the credential on teardown. A client with no `server_password_file` is skipped
    (an open/no-password lane needs no file), which is a legitimate, tested outcome.
    """

    def __init__(self, *, read_as_uid: Optional[ReadAsUid] = None) -> None:
        self._written: Dict[str, ProvisionedLanePassword] = {}
        self._read_as_uid = read_as_uid

    def provision_from_descriptor(self, descriptor: Mapping[str, Any]) -> List[ProvisionedLanePassword]:
        """Write and consumer-read-verify every declared `server_password_file`.

        The password value is `descriptor["lane_password"]`. If NO client names a
        `server_password_file`, this is a no-op (the lane is open / needs no password) and
        `lane_password` is not required. If any client DOES name one, `lane_password` must be
        present and non-empty — otherwise we fail closed rather than write an empty credential
        the handshake would silently reject.
        """
        clients = descriptor.get("clients")
        if not isinstance(clients, (list, tuple)) or not clients:
            # No clients to serve — nothing to produce. (Bootstrap provisioning fails closed
            # on this; here an absent client list simply means no password files.)
            return []

        targets = [
            (str(c.get("actor", "")), c.get("server_password_file"), c.get("uid"))
            for c in clients
            if c.get("server_password_file")
        ]
        if not targets:
            # Open/no-password lane: no consumer path is armed, so no file is produced.
            return []

        password = descriptor.get("lane_password")
        if password is None or str(password) == "":
            rendered = ", ".join(
                f"client {actor!r} path={path!r} consuming uid {uid!r}"
                for actor, path, uid in targets
            )
            raise LanePasswordProvisionError(
                f"{rendered}: descriptor carries no non-empty 'lane_password'; "
                "refusing to write an empty credential (fail closed)"
            )
        password = str(password)

        # #455: the run id every credential this call writes will be stamped with. A
        # descriptor with no run id is refused rather than stamped with a placeholder:
        # an unattributable credential is one a later sweep must leave behind forever,
        # which is precisely the residue this stamping exists to make clearable.
        run_id = descriptor.get("run_id")
        if not run_id or not isinstance(run_id, str):
            raise LanePasswordProvisionError(
                "descriptor carries no non-empty 'run_id'; refusing to write a "
                "credential no later sweep could attribute to the run that minted it "
                "(fail closed)"
            )

        written: List[ProvisionedLanePassword] = []
        try:
            for actor, path, consumer_uid in targets:
                if consumer_uid is None:
                    raise LanePasswordProvisionError(
                        f"client {actor!r} declares server_password_file={path!r} but no "
                        "consuming uid; readability must be proved as that identity"
                    )
                written.append(
                    self._write_file(
                        str(path), actor, int(consumer_uid), password, run_id
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
            raise LanePasswordProvisionError(str(exc)) from exc
        except Exception:
            self.remove_all()
            raise
        return written

    def _write_file(
        self, path: str, actor: str, consumer_uid: int, password: str, run_id: str
    ) -> ProvisionedLanePassword:
        if not os.path.isabs(path):
            raise LanePasswordProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: server_password_file must be absolute"
            )
        directory = os.path.dirname(path)
        try:
            prepare_credential_directory(directory)
        except OSError as exc:
            raise LanePasswordProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: cannot prepare mode-0711 directory: {exc}"
            ) from exc
        if os.path.islink(path):
            raise LanePasswordProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: refusing symlink destination"
            )
        tmp = f"{path}.tmp.{os.getpid()}"
        # 0644 — the uid-1001 client must read a uid-1000-written per-run credential.
        try:
            fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o644)
            with os.fdopen(fd, "w", encoding="utf-8") as fh:
                # Single line, no trailing metadata: the consumer trims surrounding whitespace.
                fh.write(password)
                fh.write("\n")
            os.replace(tmp, path)
            os.chmod(path, 0o644)
        except Exception as exc:
            cleanup_errors = _cleanup_failed_install(tmp, path)
            # Do NOT include the password in any error surfaced from here.
            if cleanup_errors:
                raise LanePasswordProvisionError(
                    f"credential provision failed for client {actor!r} at {path!r} "
                    f"as consuming uid {consumer_uid}: {type(exc).__name__}: {exc}; "
                    f"cleanup FAILED (credential may remain): {'; '.join(cleanup_errors)}"
                ) from exc
            raise LanePasswordProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: {type(exc).__name__}: {exc}"
            ) from exc
        prov = ProvisionedLanePassword(path=path, actor=actor)
        # Stamp ownership provenance NEXT TO the credential, never inside it: the C#
        # hook reads and trims the WHOLE file as the password, so any metadata within
        # it would become part of the password. The lane password carries NO expiry —
        # that is recorded as None rather than invented, because a lane password's
        # validity ends only with lane teardown and pretending otherwise would assert
        # a bound #455 does not deliver (#457 owns shortening the lane's lifetime).
        try:
            write_provenance(
                CredentialProvenance(
                    run_id=run_id,
                    actor=actor,
                    credential_path=path,
                    minted_unix_ms=int(time.time() * 1000),
                    expiry_unix_ms=None,
                )
            )
        except CredentialProvenanceError as exc:
            # Unwind the credential itself: a credential with no provenance is one a
            # later sweep is obliged to leave behind, so leaving it here would create
            # exactly the permanent residue this stamping exists to prevent.
            _best_effort_unlink(path)
            raise LanePasswordProvisionError(
                f"credential provision failed for client {actor!r} at {path!r} "
                f"as consuming uid {consumer_uid}: {exc}"
            ) from exc
        self._written[path] = prov
        return prov

    def remove(self, path: str) -> None:
        """Remove the credential-bearing password file at `path`. Idempotent.

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
    def written(self) -> List[ProvisionedLanePassword]:
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
