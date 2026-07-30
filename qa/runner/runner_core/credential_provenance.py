"""Ownership provenance for per-run credentials — the `.sbprqa` sidecar (#455).

THE PROBLEM THIS SOLVES
-----------------------
A sweeper that runs on `arrange` entry finds a file at a path the manifest declares.
It must decide whether to delete it. Before this module there was **no fact on disk**
that distinguished "residue from the run we SIGKILLed an hour ago" from "a file an
operator deliberately placed at that path". A sweeper without that fact can only
guess, and a guessing sweeper that deletes credentials is worse than no sweeper.

So every credential the runner writes gets a companion provenance file naming the run
that minted it and when it expires. Sweep keys on THAT, never on the credential's
own bytes, and removes the pair. No provenance ⇒ not provably ours ⇒ left strictly
alone (`refused`/`left-alone`, never deleted).

WHY A SIDECAR AND NOT AN EMBEDDED FIELD
---------------------------------------
For the lane password the answer is forced: the C# hook reads and trims the WHOLE
file as the password (`lane_password_provision.py:167`), so any metadata inside it
would become part of the password.

For the bootstrap doc a field would work — `ArmBootstrapParser.Parse` reads named
keys and ignores unknown members, so an added `sbprQaRun` object is inert (verified
by reading `qa/SBPR.QaHarness.T022/ControlPlane/ArmBootstrapParser.cs`). We use the
sidecar there too, deliberately: one mechanism means the sweeper has one code path
and one failure mode rather than two, and it keeps the credential formats — one of
which crosses a language boundary into a fail-closed arming gate — unchanged by a
cleanup feature. The cost is one extra small file per credential; the benefit is that
adding a third credential kind requires no new sweep logic.

`SBPR_QA_HARNESS_INSTANCE` already stamps launch-env sidecars (`operator_drivers.py`),
so those need no companion file — the marker IS the provenance.

DISCIPLINE
----------
Provenance files are written with the same atomic + symlink-refusing + 0644 policy as
the credentials they describe, into the same 0711 directory. They carry NO secret:
run id, actor, the credential's own path, minted/expiry timestamps. A sidecar that
leaked the credential would double the exposure this ticket exists to reduce.

Engine-free: stdlib only, no product/game import.
"""
from __future__ import annotations

import json
import os
from dataclasses import dataclass
from typing import Any, Mapping, Optional

# Suffix appended to the credential path. Deliberately NOT a dotfile and not a
# separate directory: keeping it adjacent means a human listing the credential
# directory sees the provenance next to the thing it describes.
PROVENANCE_SUFFIX = ".sbprqa"

# Written into every file so a future reader can refuse a shape it does not know
# rather than misparse it into a deletion decision.
PROVENANCE_KIND = "sbpr-qa-credential-provenance"
PROVENANCE_VERSION = 1


class CredentialProvenanceError(RuntimeError):
    """A provenance file could not be written. Never raised for a READ."""


def provenance_path(credential_path: str) -> str:
    """The provenance companion path for `credential_path`."""
    return f"{credential_path}{PROVENANCE_SUFFIX}"


@dataclass(frozen=True)
class CredentialProvenance:
    """Who minted a credential, when, and when it stops being interesting.

    `expiry_unix_ms` is Optional and that is load-bearing rather than lazy typing:
    bootstrap docs carry a real TTL from the wire envelope, and lane-password files
    carry **none at all** — their validity ends only with lane teardown. Recording
    None rather than inventing an expiry keeps the sweeper honest: a lane password
    is removed because it belongs to a run that is over, never because it "expired",
    and the report says so.
    """

    run_id: str
    actor: str
    credential_path: str
    minted_unix_ms: int
    expiry_unix_ms: Optional[int] = None

    def as_dict(self) -> "dict[str, Any]":
        return {
            "kind": PROVENANCE_KIND,
            "version": PROVENANCE_VERSION,
            "run_id": self.run_id,
            "actor": self.actor,
            "credential_path": self.credential_path,
            "minted_unix_ms": self.minted_unix_ms,
            "expiry_unix_ms": self.expiry_unix_ms,
        }

    def is_expired(self, now_unix_ms: int) -> bool:
        """True only when a real TTL exists AND has passed.

        No TTL means never expired — NOT 'expired by default'. The absence of an
        expiry is the residual exposure #455 documents and #457 bounds; treating it
        as expiry would paper over the gap with a false guarantee.
        """
        return self.expiry_unix_ms is not None and now_unix_ms >= self.expiry_unix_ms


def parse_provenance(text: Optional[str]) -> Optional[CredentialProvenance]:
    """Parse provenance text, or None when it is absent/unreadable/unrecognised.

    Never raises. Every failure collapses to None, which the sweeper treats as
    "not provably ours" and therefore leaves alone. A parser that threw would abort
    a sweep partway and leave the tree in a state neither run understands.
    """
    if text is None:
        return None
    try:
        raw = json.loads(text)
    except (ValueError, TypeError):
        return None
    if not isinstance(raw, Mapping):
        return None
    if raw.get("kind") != PROVENANCE_KIND:
        return None
    if raw.get("version") != PROVENANCE_VERSION:
        return None
    run_id = raw.get("run_id")
    actor = raw.get("actor")
    credential_path = raw.get("credential_path")
    minted = raw.get("minted_unix_ms")
    expiry = raw.get("expiry_unix_ms")
    if not isinstance(run_id, str) or not run_id:
        return None
    if not isinstance(actor, str):
        return None
    if not isinstance(credential_path, str) or not credential_path:
        return None
    if isinstance(minted, bool) or not isinstance(minted, int):
        return None
    if expiry is not None and (isinstance(expiry, bool) or not isinstance(expiry, int)):
        return None
    return CredentialProvenance(
        run_id=run_id,
        actor=actor,
        credential_path=credential_path,
        minted_unix_ms=minted,
        expiry_unix_ms=expiry,
    )


def write_provenance(provenance: CredentialProvenance) -> str:
    """Atomically write the companion file for a credential. Returns its path.

    Same policy as the credential itself: refuse a symlink destination, write to a
    temp file in the same directory, `os.replace` into place, mode 0644 so the
    consuming uid can read it. Failure raises — a credential whose provenance could
    not be recorded is one a future sweep will refuse to clean, so the caller must
    know now rather than discover it as residue later.
    """
    path = provenance_path(provenance.credential_path)
    if not os.path.isabs(path):
        raise CredentialProvenanceError(
            f"credential provenance path must be absolute, got {path!r}"
        )
    if os.path.islink(path):
        raise CredentialProvenanceError(
            f"refusing symlink destination for credential provenance: {path!r}"
        )
    tmp = f"{path}.tmp.{os.getpid()}"
    try:
        fd = os.open(tmp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o644)
        with os.fdopen(fd, "w", encoding="utf-8") as fh:
            json.dump(provenance.as_dict(), fh, indent=2, sort_keys=True)
            fh.write("\n")
        os.replace(tmp, path)
        os.chmod(path, 0o644)
    except OSError as exc:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise CredentialProvenanceError(
            f"cannot write credential provenance at {path!r}: {type(exc).__name__}: {exc}"
        ) from exc
    return path
