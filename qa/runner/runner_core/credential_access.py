"""Shared credential filesystem policy for T022 credential provisioning (#452).

Credential paths are public topology; credential values are per-run throwaways. The
containing directory is mode 0711 so a consumer that already knows its path can traverse
it without being able to list neighbouring credentials. Files are mode 0644 because the
runner (uid 1000) writes credentials consumed by both uid 1000 and uid 1001.

This module also provides the VERIFY seam used to prove readability by opening the file
under the declared consuming uid. Missing and permission-denied files intentionally share
one fail-closed error: either condition leaves the headless client without credentials.
"""
from __future__ import annotations

import os
import stat
import subprocess
from typing import Callable, Optional


class CredentialReadError(RuntimeError):
    """A declared credential could not be read as its consuming uid."""


ReadAsUid = Callable[[str, int], None]


def prepare_credential_directory(directory: str) -> None:
    """Create or repair a runner-owned, non-symlink credential directory to 0711."""
    os.makedirs(directory, mode=0o711, exist_ok=True)
    info = os.lstat(directory)
    if stat.S_ISLNK(info.st_mode):
        raise OSError(f"credential directory must not be a symlink: {directory}")
    if info.st_uid != os.geteuid():
        raise OSError(
            f"credential directory must be owned by arranging uid {os.geteuid()}, "
            f"got uid {info.st_uid}: {directory}"
        )
    os.chmod(directory, 0o711)


def _read_as_uid(path: str, uid: int) -> None:
    """Open and read one byte under `uid`, using passwordless sudo cross-uid."""
    if uid < 0:
        raise ValueError(f"consumer uid must be non-negative, got {uid}")
    probe = "import sys; open(sys.argv[1], 'rb').read(1)"
    # `uv run` may place sys.executable under a runner-owned cache path the foreign
    # uid cannot traverse. The host interpreter is stable and world-executable.
    command = ["/usr/bin/python3", "-c", probe, path]
    if uid != os.geteuid():
        command = ["sudo", "-n", "-u", f"#{uid}", "--", *command]
    completed = subprocess.run(command, capture_output=True, text=True, check=False)
    if completed.returncode != 0:
        detail = completed.stderr.strip() or f"read probe exited {completed.returncode}"
        raise PermissionError(detail)


def assert_readable_as_consumer(
    *,
    actor: str,
    path: str,
    consumer_uid: int,
    read_as_uid: Optional[ReadAsUid] = None,
) -> None:
    """Fail closed unless `path` can be opened while acting as `consumer_uid`."""
    reader = _read_as_uid if read_as_uid is None else read_as_uid
    try:
        reader(path, consumer_uid)
    except (OSError, ValueError) as exc:
        raise CredentialReadError(
            f"credential for client {actor!r} is missing or unreadable at {path!r} "
            f"as consuming uid {consumer_uid}: {type(exc).__name__}: {exc}"
        ) from exc
