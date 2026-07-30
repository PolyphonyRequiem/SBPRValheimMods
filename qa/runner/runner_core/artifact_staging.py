"""STAGE — unified artifact staging to every client (T022 ARRANGE spec §4 STAGE, #451).

WHAT THIS EXISTS TO PREVENT (§2 I1)
-----------------------------------
`SBPR.QaHarness.T022` — the mod the entire test depends on — was absent from `client_b`
for the whole twelve-day effort. A `diff` of the two plugin directories returned exactly
one line. A client without the harness boots normally, loads every product mod, opens
its bridge port, and waits at a menu forever, emitting no error. Days were spent
debugging the launch path of a client that could never have armed.

The staging that existed was not merely buggy, it was *structurally* incapable of
noticing:

  * it staged into ONE client only, reading the other client's tree as its source, so
    "the two clients agree" was never a checked fact — it was a hand-`diff` away;
  * its loop was bounded by a literal artifact count, so a fourth manifest entry staged
    NOTHING, silently (§2 I2);
  * it could only REPLACE an existing file, never CREATE a plugin directory, so a
    manifest could not introduce a new artifact at all (§2 I3) — which is precisely
    what blocked staging the harness in the first place.

THE THREE STRUCTURAL RULES HERE
-------------------------------
1. **Every client, one code path.** `stage_all` iterates `manifest.clients` and, within
   each, that client's own `artifacts`. There is no positional `client_a`/`client_b`,
   no "source client", and no per-client branch. Adding a third client is manifest data.
2. **Count-agnostic by construction, not by derivation.** Nothing here counts artifacts.
   The previous fix derived a count from a path list, which still required the manifest's
   line ORDER to match that list. Iterating the parsed structure removes the concept of
   an artifact index entirely, so there is no bound to get wrong.
3. **Absence and drift are DIFFERENT failures.** "Present with the wrong bytes" and
   "not there at all" produce the identical observable — a client at a menu — so the
   postconditions report them under distinct precondition ids. Conflating them is the
   same mistake the credential work had to unwind for "declared but absent" versus
   "written unreadable".

TRANSACTION MODEL, AND ITS HONEST BOUNDARY
------------------------------------------
Staging is planned in full before a single byte is written: every source is resolved,
hashed and checked against its catalogue pin first. A source problem therefore aborts
with nothing touched, which is the common case and is genuinely atomic.

Once writing begins, each entry is staged through a same-directory temp file that is
verified (kind, owner, mode, hash) before an atomic `os.replace`. Bytes displaced by a
replacement are preserved as a sibling `.sbpr-prev` file, so a later failure can put
them back — the previous stager had no undo at all and simply returned mid-loop, leaving
the tree half-new.

**The boundary, stated plainly:** a rollback that must cross a uid boundary is
best-effort, not guaranteed. client_b's tree is uid-1001-owned while the runner is uid
1000, so reverting it requires the same `sudo -n -u #<uid>` seam the credential
readability probes use, and that seam can itself fail. Within a single uid the rollback
is reliable. `StagingRollbackError` names every path that could not be restored rather
than swallowing it: a rollback that fails quietly is worse than one that never ran,
because it leaves a tree nobody knows is mixed.

RELATIONSHIP TO THE OTHER PHASES
--------------------------------
STATIC (#450) already refuses a manifest whose sources are missing or whose pins drift,
and deliberately does NOT fail on a destination that does not exist yet — creating it is
this phase's job. SWEEP (#455) runs BEFORE this phase so staging never writes alongside
residue it is about to invalidate. VERIFY (#456) consumes `assert_postconditions` for
the artifact half of its readiness report rather than reimplementing it.

FILESYSTEM SEAM: every environment contact goes through `StagingFilesystem`. The real
adapter wires stdlib calls; tests wire a temp directory or fakes. Importing or
unit-testing this module touches nothing, and no path here is derived from `~` — a test
that reads a real host path silently SKIPS when the path is absent, which reads as green
while proving nothing.

Engine-free: stdlib only, no product/game import.
"""
from __future__ import annotations

import hashlib
import os
import shutil
import stat
import subprocess
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Sequence, Tuple

from .arrange_manifest import ArrangeManifest, ArtifactRequirement, ClientEntry
from .arrange_static import StaticFailure

# Stable precondition ids for the postconditions. Reported verbatim, so they are part of
# the contract an operator greps for — same discipline as the STATIC S-ids.
#
# Two ids, deliberately, because the two failures need different remedies: a MISSING
# artifact means staging did not happen (or was undone); a DRIFTED one means it happened
# and something else overwrote it. Reporting both as "artifact bad" would leave the
# operator to guess which.
P_ARTIFACT_STAGED = "T1-ARTIFACT-STAGED"
P_ARTIFACT_BYTES = "T2-ARTIFACT-BYTES"
P_ARTIFACT_OWNERSHIP = "T3-ARTIFACT-OWNERSHIP"

ALL_POSTCONDITIONS = (P_ARTIFACT_STAGED, P_ARTIFACT_BYTES, P_ARTIFACT_OWNERSHIP)

# Modes staging establishes. Files are 0644 and parents 0755: readable by the client
# that consumes them, writable by nobody else. Mirrors the credential policy's reasoning
# (paths are public topology; only VALUES are secret) and the existing stager's modes.
ARTIFACT_MODE = 0o644
PARENT_MODE = 0o755

# A parent left 0775/0770 by earlier tooling is benign and may be tightened to 0755.
# Any OTHER unexpected mode is refused rather than "repaired" — silently widening or
# narrowing a mode nobody predicted is how a permissions bug becomes permanent.
HARDENABLE_PARENT_MODES = frozenset({0o775, 0o770})

_STAGE_SUFFIX = ".sbpr-stage"
_PREV_SUFFIX = ".sbpr-prev"


class ArtifactStagingError(RuntimeError):
    """Staging could not proceed or could not complete. Fail closed.

    Raised with a message naming the client actor, the artifact, and the path — the
    three facts an operator needs to act. Never carries file CONTENT.
    """


class StagingRollbackError(ArtifactStagingError):
    """Staging failed AND the rollback could not fully restore the prior state.

    Distinct from `ArtifactStagingError` because the operational meaning differs: a
    plain staging failure leaves a known-good tree, while this one leaves a tree in a
    mixed state that a human must reconcile. It names every path that could not be
    restored.
    """


@dataclass(frozen=True)
class PlannedStage:
    """One artifact's intended landing, resolved but NOT yet written.

    `action` records what staging would do, which is what `--dry-run` prints and what
    the tests assert against:

      create            — no file at the destination yet (may also need its parent made)
      replace           — a file exists with different bytes; it will be displaced
      already-current   — the destination already holds exactly the pinned bytes
    """

    actor: str
    artifact: str
    source_path: str
    dest_path: str
    sha256: str
    action: str
    needs_parent: bool


@dataclass(frozen=True)
class StagedArtifact:
    """One artifact that actually landed, with what had to be done to land it."""

    actor: str
    artifact: str
    dest_path: str
    sha256: str
    action: str
    created_parent: Optional[str] = None
    prev_path: Optional[str] = None


@dataclass
class StagingFilesystem:
    """The injectable filesystem seam. Everything this module touches goes through here.

    Split out rather than calling `os` directly so the whole phase is testable against a
    temp tree, and so the cross-uid variants (which shell out through `sudo`) are a
    swappable implementation detail instead of an untestable branch in the middle of the
    staging loop.
    """

    exists: Callable[[str], bool]
    is_symlink: Callable[[str], bool]
    is_file: Callable[[str], bool]
    is_dir: Callable[[str], bool]
    hash_file: Callable[[str], Optional[str]]
    stat_owner_mode: Callable[[str], Optional[Tuple[int, int]]]
    realpath: Callable[[str], str]
    copy_file: Callable[[str, str, int], None]
    replace: Callable[[str, str], None]
    unlink: Callable[[str], None]
    makedir: Callable[[str, int], None]
    rmdir: Callable[[str], None]
    chmod: Callable[[str, int], None]


def real_staging_filesystem(*, as_uid: Optional[int] = None) -> StagingFilesystem:
    """Wire the real stdlib calls.

    `as_uid` selects the identity that performs MUTATIONS. When it is None or already
    the effective uid, writes happen in-process. Otherwise they are performed through
    `sudo -n -u #<uid>`, so a file staged into another identity's tree lands OWNED by
    that identity.

    That direction is deliberate and is the only one that works. Having uid 1000 write
    into `/home/valbot` and then `chown` the result requires root and dissolves the
    Steam identity isolation the dual-user rig depends on. Reads stay in-process: the
    runner can already read both trees, and a read that fails is reported rather than
    retried as another identity.
    """

    def _hash(path: str) -> Optional[str]:
        try:
            h = hashlib.sha256()
            with open(path, "rb") as fh:
                for chunk in iter(lambda: fh.read(65536), b""):
                    h.update(chunk)
            return h.hexdigest()
        except OSError:
            return None

    def _stat_owner_mode(path: str) -> Optional[Tuple[int, int]]:
        try:
            info = os.stat(path, follow_symlinks=False)
        except OSError:
            return None
        return (info.st_uid, stat.S_IMODE(info.st_mode))

    def _run_as(argv: Sequence[str]) -> None:
        command = list(argv)
        if as_uid is not None and as_uid != os.geteuid():
            command = ["sudo", "-n", "-u", f"#{as_uid}", "--", *command]
        completed = subprocess.run(command, capture_output=True, text=True, check=False)
        if completed.returncode != 0:
            detail = completed.stderr.strip() or f"exited {completed.returncode}"
            raise OSError(detail)

    def _copy_file(source: str, dest: str, mode: int) -> None:
        if as_uid is None or as_uid == os.geteuid():
            shutil.copyfile(source, dest)
            os.chmod(dest, mode)
            return
        # `install` sets mode atomically with the copy, so the file is never briefly
        # world-writable between creation and chmod.
        _run_as(["/usr/bin/install", "-m", format(mode, "03o"), "--", source, dest])

    def _replace(source: str, dest: str) -> None:
        if as_uid is None or as_uid == os.geteuid():
            os.replace(source, dest)
            return
        _run_as(["/bin/mv", "-f", "--", source, dest])

    def _unlink(path: str) -> None:
        if as_uid is None or as_uid == os.geteuid():
            try:
                os.unlink(path)
            except FileNotFoundError:
                return
            return
        _run_as(["/bin/rm", "-f", "--", path])

    def _makedir(path: str, mode: int) -> None:
        if as_uid is None or as_uid == os.geteuid():
            os.mkdir(path, mode)
            os.chmod(path, mode)
            return
        _run_as(["/usr/bin/install", "-d", "-m", format(mode, "03o"), "--", path])

    def _rmdir(path: str) -> None:
        if as_uid is None or as_uid == os.geteuid():
            try:
                os.rmdir(path)
            except OSError:
                return
            return
        _run_as(["/bin/rmdir", "--", path])

    def _chmod(path: str, mode: int) -> None:
        if as_uid is None or as_uid == os.geteuid():
            os.chmod(path, mode)
            return
        _run_as(["/bin/chmod", format(mode, "03o"), "--", path])

    return StagingFilesystem(
        exists=lambda p: os.path.lexists(p),
        is_symlink=os.path.islink,
        is_file=lambda p: os.path.isfile(p) and not os.path.islink(p),
        is_dir=lambda p: os.path.isdir(p) and not os.path.islink(p),
        hash_file=_hash,
        stat_owner_mode=_stat_owner_mode,
        realpath=os.path.realpath,
        copy_file=_copy_file,
        replace=_replace,
        unlink=_unlink,
        makedir=_makedir,
        rmdir=_rmdir,
        chmod=_chmod,
    )


def _mode_is_safe(mode: int) -> bool:
    """True when no group/other write bit is set (the 0o022 test the stager used)."""
    return (mode & 0o022) == 0


def _is_under(path: str, root: str) -> bool:
    """True when `path` is strictly beneath `root`. Both must already be realpaths.

    Strictly: the root itself does not count. Creating or hardening the game root would
    be a much larger action than staging one artifact, and nothing here should be able
    to reach it by accident.
    """
    root = root.rstrip("/")
    return path != root and path.startswith(root + "/")


class ArtifactStager:
    """Stage every manifest artifact to every client, transactionally, then prove it.

    One instance stages one manifest. `plan()` is pure and may be called freely;
    `stage_all()` mutates and is the only method that writes. `assert_postconditions()`
    re-reads from disk and never trusts what `stage_all` believed it did — the whole
    class of bug this phase closes is "the write appeared to work".
    """

    def __init__(
        self,
        *,
        manifest: ArrangeManifest,
        filesystems: Optional[Dict[str, StagingFilesystem]] = None,
        pid: Optional[int] = None,
    ) -> None:
        """`filesystems` maps a client actor to the seam that writes ITS tree.

        Per-actor rather than global because the two clients are written as different
        identities. A missing entry falls back to a real in-process filesystem for that
        client's uid, so production wiring is implicit and tests are explicit.
        """
        self._manifest = manifest
        self._filesystems = dict(filesystems or {})
        self._pid = os.getpid() if pid is None else pid
        self._staged: List[StagedArtifact] = []

    # ---------------------------------------------------------------- filesystem
    def _fs(self, client: ClientEntry) -> StagingFilesystem:
        fs = self._filesystems.get(client.actor)
        if fs is None:
            fs = real_staging_filesystem(as_uid=client.uid)
            self._filesystems[client.actor] = fs
        return fs

    # --------------------------------------------------------------------- plan
    def plan(self) -> List[PlannedStage]:
        """Resolve every client × artifact into an intended action. Writes nothing.

        Iterates the parsed manifest structure directly. There is no artifact count, no
        index, and no bound anywhere in this method — that is what makes the phase
        count-agnostic rather than merely count-correct.

        Raises `ArtifactStagingError` on any source problem, BEFORE any caller could
        have written a byte. A missing or drifted source is not a partial-staging
        situation; it means the plan is unbuildable and the whole run should stop.
        """
        planned: List[PlannedStage] = []
        problems: List[str] = []

        for client in self._manifest.clients:
            fs = self._fs(client)
            for req in client.artifacts:
                artifact = self._manifest.artifacts.get(req.artifact)
                if artifact is None:
                    # S6 already reports this statically; repeated here because plan()
                    # must be safe to call without a prior STATIC run.
                    problems.append(
                        f"client {client.actor!r} requires artifact {req.artifact!r}, "
                        "which is not in the manifest catalogue"
                    )
                    continue

                if not fs.is_file(artifact.source_path):
                    problems.append(
                        f"client {client.actor!r} artifact {req.artifact!r}: source is "
                        f"missing, a symlink, or not a regular file at "
                        f"{artifact.source_path}"
                    )
                    continue

                source_hash = fs.hash_file(artifact.source_path)
                if source_hash is None:
                    problems.append(
                        f"client {client.actor!r} artifact {req.artifact!r}: source at "
                        f"{artifact.source_path} is unreadable"
                    )
                    continue
                if source_hash != artifact.sha256:
                    # The reviewed-source binding. Staging bytes that do not match the
                    # pin would deploy unreviewed code under a reviewed label, which is
                    # strictly worse than staging nothing.
                    problems.append(
                        f"client {client.actor!r} artifact {req.artifact!r}: source at "
                        f"{artifact.source_path} drifted from its pin "
                        f"(expected {artifact.sha256}, got {source_hash})"
                    )
                    continue

                planned.append(
                    self._plan_one(client, req, artifact.source_path, artifact.sha256, fs)
                )

        if problems:
            raise ArtifactStagingError(
                "refusing to stage; nothing was written:\n  - " + "\n  - ".join(problems)
            )
        return planned

    def _plan_one(
        self,
        client: ClientEntry,
        req: ArtifactRequirement,
        source_path: str,
        sha256: str,
        fs: StagingFilesystem,
    ) -> PlannedStage:
        parent = os.path.dirname(req.dest_path)
        needs_parent = not fs.is_dir(parent)

        if fs.is_file(req.dest_path):
            deployed = fs.hash_file(req.dest_path)
            action = "already-current" if deployed == sha256 else "replace"
        else:
            action = "create"

        return PlannedStage(
            actor=client.actor,
            artifact=req.artifact,
            source_path=source_path,
            dest_path=req.dest_path,
            sha256=sha256,
            action=action,
            needs_parent=needs_parent,
        )

    # -------------------------------------------------------------------- stage
    def stage_all(self) -> List[StagedArtifact]:
        """Stage every planned entry. On any failure, roll back and raise.

        Ordering within the batch is manifest order; nothing depends on it, because each
        entry is independent and the whole batch is reverted together on failure.
        """
        planned = self.plan()
        self._staged = []

        try:
            for entry in planned:
                if entry.action == "already-current":
                    # Idempotency: re-running staging must converge, not churn. Still
                    # recorded so callers and --dry-run can see the entry was considered.
                    self._staged.append(
                        StagedArtifact(
                            actor=entry.actor,
                            artifact=entry.artifact,
                            dest_path=entry.dest_path,
                            sha256=entry.sha256,
                            action="already-current",
                        )
                    )
                    continue
                self._staged.append(self._stage_one(entry))
        except Exception as exc:
            rollback_errors = self._rollback()
            if rollback_errors:
                raise StagingRollbackError(
                    f"staging failed ({exc}); ROLLBACK INCOMPLETE — the following paths "
                    "were left in a modified state and need manual reconciliation:\n  - "
                    + "\n  - ".join(rollback_errors)
                ) from exc
            if isinstance(exc, ArtifactStagingError):
                raise
            raise ArtifactStagingError(
                f"staging failed and was rolled back cleanly: {type(exc).__name__}: {exc}"
            ) from exc

        self._commit()
        return list(self._staged)

    def _stage_one(self, entry: PlannedStage) -> StagedArtifact:
        client = self._manifest.client(entry.actor)
        fs = self._fs(client)
        parent = os.path.dirname(entry.dest_path)
        created_parent: Optional[str] = None

        if entry.needs_parent:
            created_parent = self._create_parent(entry, client, parent, fs)
        else:
            self._harden_parent(entry, client, parent, fs)

        self._require_safe_parent(entry, client, parent, fs)
        self._require_safe_destination(entry, client, fs)

        # Guard the destination against escaping the client's game root through a
        # symlinked intermediate directory. S7 checks the DECLARED string prefix and
        # cannot see this; only a realpath after the parent exists can.
        real_root = fs.realpath(client.game_root)
        real_parent = fs.realpath(parent)
        if not _is_under(real_parent, real_root):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": destination resolves to {real_parent!r}, which is outside this "
                f"client's game root {real_root!r} (symlinked intermediate directory)"
            )

        tmp = os.path.join(parent, f".{os.path.basename(entry.dest_path)}{_STAGE_SUFFIX}.{self._pid}")
        if fs.exists(tmp):
            raise ArtifactStagingError(
                self._describe(entry) + f": staging temp {tmp!r} already exists"
            )

        try:
            fs.copy_file(entry.source_path, tmp, ARTIFACT_MODE)
        except OSError as exc:
            fs.unlink(tmp)
            raise ArtifactStagingError(
                self._describe(entry) + f": copy to staging temp failed: {exc}"
            ) from exc

        # Verify the TEMP before it becomes the destination. Checking after the rename
        # would mean a bad file was, however briefly, the live artifact.
        self._verify_temp(entry, client, tmp, fs)

        prev_path: Optional[str] = None
        if entry.action == "replace":
            prev_path = os.path.join(
                parent, f".{os.path.basename(entry.dest_path)}{_PREV_SUFFIX}.{self._pid}"
            )
            try:
                fs.copy_file(entry.dest_path, prev_path, ARTIFACT_MODE)
            except OSError as exc:
                fs.unlink(tmp)
                raise ArtifactStagingError(
                    self._describe(entry)
                    + f": could not preserve the existing bytes for rollback: {exc}"
                ) from exc

        try:
            fs.replace(tmp, entry.dest_path)
        except OSError as exc:
            fs.unlink(tmp)
            if prev_path:
                fs.unlink(prev_path)
            raise ArtifactStagingError(
                self._describe(entry) + f": atomic rename failed: {exc}"
            ) from exc

        return StagedArtifact(
            actor=entry.actor,
            artifact=entry.artifact,
            dest_path=entry.dest_path,
            sha256=entry.sha256,
            action=entry.action,
            created_parent=created_parent,
            prev_path=prev_path,
        )

    def _create_parent(
        self, entry: PlannedStage, client: ClientEntry, parent: str, fs: StagingFilesystem
    ) -> str:
        """Create a missing plugin directory (§2 I3), under narrow fail-closed rules.

        The stager this replaces could only ever REPLACE, so a manifest could not
        introduce a new artifact — which is exactly what blocked staging the harness.
        Creation is allowed, but only in the one shape that cannot be abused:

          * the path must not exist at all (never adopt or "fix" something present),
          * its own parent must already exist as a real, correctly-owned directory,
          * that grandparent must resolve strictly UNDER this client's game root,
          * the result is 0755 and owned by the staging identity, re-checked after.
        """
        if fs.exists(parent):
            # Name the symlink case explicitly. A symlinked plugin directory is the
            # shape that can silently redirect writes outside the game root, so "a
            # symlink is here" is a far more actionable message than "something exists".
            if fs.is_symlink(parent):
                raise ArtifactStagingError(
                    self._describe(entry)
                    + f": refusing to stage into {parent!r}; it is a SYMLINK, which can "
                    "redirect writes outside this client's game root. Replace it with a "
                    "real directory."
                )
            raise ArtifactStagingError(
                self._describe(entry)
                + f": refusing to create {parent!r}; something already exists there and "
                "is not a directory"
            )

        grandparent = os.path.dirname(parent)
        if not fs.is_dir(grandparent):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": cannot create {parent!r}; its parent {grandparent!r} is missing, "
                "a symlink, or not a directory"
            )

        owner_mode = fs.stat_owner_mode(grandparent)
        if owner_mode is None or owner_mode[0] != client.uid:
            got = "unreadable" if owner_mode is None else f"uid {owner_mode[0]}"
            raise ArtifactStagingError(
                self._describe(entry)
                + f": cannot create {parent!r}; its parent {grandparent!r} must be owned "
                f"by this client's uid {client.uid}, got {got}"
            )

        real_root = fs.realpath(client.game_root)
        real_gp = fs.realpath(grandparent)
        if not _is_under(real_gp, real_root):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": cannot create {parent!r}; its parent resolves to {real_gp!r}, which "
                f"is not strictly under this client's game root {real_root!r}"
            )

        try:
            fs.makedir(parent, PARENT_MODE)
        except OSError as exc:
            raise ArtifactStagingError(
                self._describe(entry) + f": could not create {parent!r}: {exc}"
            ) from exc

        created = fs.stat_owner_mode(parent)
        if created is None or created[0] != client.uid or not _mode_is_safe(created[1]):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": created {parent!r} but it is not owned by uid {client.uid} with a "
                "non-group/other-writable mode"
            )
        return parent

    def _harden_parent(
        self, entry: PlannedStage, client: ClientEntry, parent: str, fs: StagingFilesystem
    ) -> None:
        """Tighten a benign 0775/0770 parent left by earlier tooling to 0755.

        Deliberately narrow: any mode outside the allowlist is left exactly as found so
        the explicit guard below produces a diagnostic refusal. Silently "repairing" an
        unexpected mode would hide a real permissions problem.
        """
        owner_mode = fs.stat_owner_mode(parent)
        if owner_mode is None or owner_mode[0] != client.uid:
            return
        mode = owner_mode[1]
        if _mode_is_safe(mode) or mode not in HARDENABLE_PARENT_MODES:
            return
        real_root = fs.realpath(client.game_root)
        real_parent = fs.realpath(parent)
        if not _is_under(real_parent, real_root):
            return
        try:
            fs.chmod(parent, PARENT_MODE)
        except OSError:
            return

    def _require_safe_parent(
        self, entry: PlannedStage, client: ClientEntry, parent: str, fs: StagingFilesystem
    ) -> None:
        if not fs.is_dir(parent):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": parent {parent!r} is missing, a symlink, or not a directory"
            )
        owner_mode = fs.stat_owner_mode(parent)
        if owner_mode is None:
            raise ArtifactStagingError(
                self._describe(entry) + f": parent {parent!r} cannot be stat'd"
            )
        owner, mode = owner_mode
        if owner != client.uid:
            raise ArtifactStagingError(
                self._describe(entry)
                + f": parent {parent!r} is owned by uid {owner}, expected this client's "
                f"uid {client.uid}"
            )
        if not _mode_is_safe(mode):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": parent {parent!r} is group/other-writable ({mode:#06o})"
            )

    def _require_safe_destination(
        self, entry: PlannedStage, client: ClientEntry, fs: StagingFilesystem
    ) -> None:
        """A destination that exists must be a plain, client-owned regular file.

        A symlink destination is refused outright: following it would write through to
        somewhere the manifest never declared, defeating S7's containment.
        """
        if not fs.exists(entry.dest_path):
            return
        if fs.is_symlink(entry.dest_path):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": destination {entry.dest_path!r} is a symlink; refusing to write "
                "through it"
            )
        if not fs.is_file(entry.dest_path):
            raise ArtifactStagingError(
                self._describe(entry)
                + f": destination {entry.dest_path!r} exists but is not a regular file"
            )
        owner_mode = fs.stat_owner_mode(entry.dest_path)
        if owner_mode is None:
            raise ArtifactStagingError(
                self._describe(entry) + f": destination {entry.dest_path!r} cannot be stat'd"
            )
        if owner_mode[0] != client.uid:
            raise ArtifactStagingError(
                self._describe(entry)
                + f": destination {entry.dest_path!r} is owned by uid {owner_mode[0]}, "
                f"expected this client's uid {client.uid}"
            )

    def _verify_temp(
        self, entry: PlannedStage, client: ClientEntry, tmp: str, fs: StagingFilesystem
    ) -> None:
        if not fs.is_file(tmp):
            fs.unlink(tmp)
            raise ArtifactStagingError(
                self._describe(entry) + ": staging temp is not a regular file"
            )
        owner_mode = fs.stat_owner_mode(tmp)
        if owner_mode is None:
            fs.unlink(tmp)
            raise ArtifactStagingError(
                self._describe(entry) + ": staging temp cannot be stat'd"
            )
        owner, mode = owner_mode
        if owner != client.uid:
            fs.unlink(tmp)
            raise ArtifactStagingError(
                self._describe(entry)
                + f": staging temp is owned by uid {owner}, expected {client.uid}"
            )
        if mode != ARTIFACT_MODE:
            fs.unlink(tmp)
            raise ArtifactStagingError(
                self._describe(entry)
                + f": staging temp mode is {mode:#06o}, expected {ARTIFACT_MODE:#06o}"
            )
        got = fs.hash_file(tmp)
        if got != entry.sha256:
            fs.unlink(tmp)
            raise ArtifactStagingError(
                self._describe(entry)
                + f": staging temp hash {got} does not match the pin {entry.sha256}"
            )

    # ----------------------------------------------------------------- rollback
    def _rollback(self) -> List[str]:
        """Undo completed entries in reverse. Returns paths that could NOT be restored.

        Reverse order so a created parent is only removed after the artifact inside it
        is gone. Every step is attempted even when an earlier one fails — a partial
        rollback that stops at the first error strands more than one that continues.
        """
        errors: List[str] = []
        for staged in reversed(self._staged):
            if staged.action == "already-current":
                continue
            client = self._manifest.client(staged.actor)
            fs = self._fs(client)
            try:
                if staged.prev_path is not None:
                    fs.replace(staged.prev_path, staged.dest_path)
                else:
                    fs.unlink(staged.dest_path)
            except OSError as exc:
                errors.append(
                    f"{staged.actor}/{staged.artifact} at {staged.dest_path}: {exc}"
                )
                continue
            if staged.created_parent is not None:
                try:
                    fs.rmdir(staged.created_parent)
                except OSError:
                    # A non-empty created parent means something else put a file there
                    # during the run. Leaving it is correct; it is not ours to delete.
                    pass
        self._staged = []
        return errors

    def _commit(self) -> None:
        """Drop the preserved prior-bytes files once the batch has fully succeeded."""
        for staged in self._staged:
            if staged.prev_path is None:
                continue
            client = self._manifest.client(staged.actor)
            self._fs(client).unlink(staged.prev_path)

    # ----------------------------------------------------------- postconditions
    def assert_postconditions(self) -> List[StaticFailure]:
        """Read every artifact back from disk and assert it. Returns failures, if any.

        Re-reads rather than trusting `stage_all`'s record, because "the write appeared
        to succeed" is precisely the belief this phase exists to stop relying on.

        Returns `StaticFailure` records — the same shape STATIC emits — so VERIFY (#456)
        can fold the artifact half of its readiness report in without a second format.
        Checks do not short-circuit: one call reports EVERY problem, because each
        discovery-by-boot cycle costs ten minutes.
        """
        failures: List[StaticFailure] = []

        for client in self._manifest.clients:
            fs = self._fs(client)
            for req in client.artifacts:
                artifact = self._manifest.artifacts.get(req.artifact)
                if artifact is None:
                    continue  # S6's to report
                failures.extend(self._check_one(client, req, artifact.sha256, fs))
        return failures

    def _check_one(
        self,
        client: ClientEntry,
        req: ArtifactRequirement,
        expected_hash: str,
        fs: StagingFilesystem,
    ) -> List[StaticFailure]:
        failures: List[StaticFailure] = []
        dest = req.dest_path

        if not fs.exists(dest):
            # THE twelve-day failure, caught. An artifact required by the manifest and
            # absent from this client's tree is the exact shape of the harness being on
            # one client and not the other.
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_STAGED,
                    client=client.actor,
                    detail=f"required artifact {req.artifact!r} is ABSENT after staging",
                    expected=f"a staged file at {dest}",
                    actual="no such file",
                    remedy="This client would boot normally, load every other mod, and "
                    "wait at a menu forever with nothing logged. Re-run STAGE and "
                    "check its report; do not launch until this artifact is present.",
                )
            )
            return failures

        if fs.is_symlink(dest) or not fs.is_file(dest):
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_OWNERSHIP,
                    client=client.actor,
                    detail=f"staged artifact {req.artifact!r} is not a plain regular file",
                    expected=f"a regular, non-symlink file at {dest}",
                    actual="symlink or non-file",
                    remedy="Remove the symlink/irregular entry and re-stage. A symlinked "
                    "artifact can point outside this client's game root, which the "
                    "declared-destination check cannot see.",
                )
            )
            return failures

        got = fs.hash_file(dest)
        if got is None:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_BYTES,
                    client=client.actor,
                    detail=f"staged artifact {req.artifact!r} is unreadable",
                    expected=f"readable bytes at {dest}",
                    actual="read failed (permissions or I/O error)",
                    remedy="Drift cannot be ruled out while the file cannot be read. "
                    "Fix permissions and re-run the postcondition check.",
                )
            )
        elif got != expected_hash:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_BYTES,
                    client=client.actor,
                    detail=f"staged artifact {req.artifact!r} does not match its pin",
                    expected=f"sha256 {expected_hash} at {dest}",
                    actual=f"sha256 {got}",
                    remedy="The deployed bytes are not the reviewed bytes; this client "
                    "would run different code from the one the run reports against. "
                    "Re-stage, and find out what wrote it.",
                )
            )

        owner_mode = fs.stat_owner_mode(dest)
        if owner_mode is None:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_OWNERSHIP,
                    client=client.actor,
                    detail=f"staged artifact {req.artifact!r} cannot be stat'd",
                    expected=f"a stat-able file at {dest}",
                    actual="stat failed",
                    remedy="Fix permissions on the containing directory.",
                )
            )
            return failures

        owner, mode = owner_mode
        if owner != client.uid:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_OWNERSHIP,
                    client=client.actor,
                    detail=f"staged artifact {req.artifact!r} is owned by the wrong uid",
                    expected=f"uid {client.uid} (the identity this client runs as)",
                    actual=f"uid {owner}",
                    remedy="The client may be unable to read its own artifact. Re-stage "
                    "as this client's identity rather than writing across the uid "
                    "boundary and adjusting ownership afterwards.",
                )
            )
        if mode != ARTIFACT_MODE:
            failures.append(
                StaticFailure(
                    precondition=P_ARTIFACT_OWNERSHIP,
                    client=client.actor,
                    detail=f"staged artifact {req.artifact!r} has an unexpected mode",
                    expected=f"mode {ARTIFACT_MODE:#06o}",
                    actual=f"mode {mode:#06o}",
                    remedy="Re-stage. A mode outside the policy means something other "
                    "than STAGE wrote this file.",
                )
            )
        return failures

    # -------------------------------------------------------------------- misc
    def _describe(self, entry: PlannedStage) -> str:
        return (
            f"staging failed for client {entry.actor!r} artifact {entry.artifact!r} "
            f"at {entry.dest_path!r}"
        )


def render_plan(planned: Sequence[PlannedStage]) -> str:
    """Human-readable dry-run report, grouped by client.

    Grouped by client deliberately: the failure this phase closes was a per-client
    asymmetry, so the natural reading of the output should make an asymmetry obvious.
    """
    if not planned:
        return "arrange STAGE: nothing to stage (no client declares any artifact)"

    by_actor: Dict[str, List[PlannedStage]] = {}
    for entry in planned:
        by_actor.setdefault(entry.actor, []).append(entry)

    lines = [
        f"arrange STAGE (dry run): {len(planned)} artifact placement(s) over "
        f"{len(by_actor)} client(s)"
    ]
    for actor in sorted(by_actor):
        entries = by_actor[actor]
        lines.append(f"  {actor}: {len(entries)} artifact(s)")
        for entry in entries:
            suffix = " (+create parent directory)" if entry.needs_parent else ""
            lines.append(f"    [{entry.action}] {entry.artifact} -> {entry.dest_path}{suffix}")
    return "\n".join(lines)


def render_postconditions(failures: Sequence[StaticFailure], actors: Sequence[str]) -> str:
    if not failures:
        return (
            f"arrange STAGE postconditions: PASS — every required artifact present with "
            f"matching bytes on {len(actors)} client(s): {', '.join(actors)}"
        )
    head = (
        f"arrange STAGE postconditions: FAIL — {len(failures)} failure(s) over "
        f"client(s) {', '.join(actors)}"
    )
    return "\n".join([head, *(f.render() for f in failures)])
