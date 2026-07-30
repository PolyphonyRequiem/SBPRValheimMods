"""STAGE phase tests (#451) — unified artifact staging to every client.

The positive path proves very little here: the failure this phase closes was a client
that staged *successfully* and was still missing the one artifact that mattered. So the
suite is negative-first, and the single most important case is
`TestTheTwelveDayFailure` — the manifest requires the harness on both clients, only one
gets it, and the postconditions must say so by name.

Every filesystem interaction runs against `tmp_path`. Nothing here reads a real host
path or expands `~`: a test that does silently SKIPS when the path is absent, which
reads as green in CI while proving nothing.
"""
from __future__ import annotations

import hashlib
import os
import stat

import pytest

from runner_core.arrange_manifest import ArrangeManifest
from runner_core.artifact_staging import (
    ARTIFACT_MODE,
    P_ARTIFACT_BYTES,
    P_ARTIFACT_OWNERSHIP,
    P_ARTIFACT_STAGED,
    ArtifactStager,
    ArtifactStagingError,
    StagingFilesystem,
    StagingRollbackError,
    real_staging_filesystem,
    render_plan,
)


# --------------------------------------------------------------------------- #
# Fixtures
# --------------------------------------------------------------------------- #

def sha256_of(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write(path, data: bytes, mode: int = 0o644) -> str:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    os.chmod(path, mode)
    return sha256_of(data)


def local_fs() -> StagingFilesystem:
    """The real in-process filesystem, i.e. no uid crossing. Used for every test.

    Cross-uid staging is exercised through a substituted seam rather than real `sudo`:
    a test that shells to `sudo` passes on the rig and skips in CI, which is the exact
    false-green this suite is meant to avoid.
    """
    return real_staging_filesystem(as_uid=None)


def build_manifest(tmp_path, *, artifacts, clients, uid=None):
    """Assemble a manifest whose clients all run as the CURRENT uid.

    Using the real uid keeps the ownership checks meaningful without needing root: the
    stager asserts "owned by this client's uid", and here that is us.
    """
    uid = os.getuid() if uid is None else uid
    return ArrangeManifest.parse(
        {
            "kind": "sbpr-qa-arrange-manifest",
            "version": 2,
            "lane": {
                "lane_id": "t022-test",
                "world_name": "testlane",
                "host": "127.0.0.1",
                "port": 2476,
                "requires_password": False,
            },
            "artifacts": artifacts,
            "clients": [
                {
                    "actor": c["actor"],
                    "uid": uid,
                    "user": "tester",
                    "steam_account": "76561190000000000",
                    "game_root": c["game_root"],
                    "binary_path": c["game_root"] + "/valheim.x86_64",
                    "plugins_dir": c["game_root"] + "/BepInEx/plugins",
                    "launcher": {"kind": "direct_exec"},
                    "ports": {
                        "loopback_control": c["port"],
                        "valbridge_gabp": c["port"] + 100,
                        "unity_script_host": None,
                    },
                    "qa_profile": c["actor"] + "_profile",
                    "join": {"host": "127.0.0.1", "port": 2476, "delivery": "connect_argv"},
                    "artifacts": c["artifacts"],
                    "credentials": {},
                }
                for c in clients
            ],
        }
    )


@pytest.fixture
def rig(tmp_path):
    """Two clients, two artifacts each, sources built and pinned. The healthy baseline."""
    src = tmp_path / "build"
    harness_bytes = b"HARNESS-BYTES-v1"
    product_bytes = b"PRODUCT-BYTES-v1"
    h_harness = write(src / "SBPR.QaHarness.T022.dll", harness_bytes)
    h_product = write(src / "SBPR.Trailborne.dll", product_bytes)

    roots = {}
    clients = []
    for actor, port in (("client_a", 48610), ("client_b", 48611)):
        root = tmp_path / actor
        (root / "BepInEx" / "plugins").mkdir(parents=True)
        roots[actor] = root
        clients.append(
            {
                "actor": actor,
                "game_root": str(root),
                "port": port,
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": str(root / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"),
                    },
                    {
                        "artifact": "SBPR.Trailborne.dll",
                        "dest_path": str(root / "BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll"),
                    },
                ],
            }
        )

    manifest = build_manifest(
        tmp_path,
        artifacts=[
            {
                "name": "SBPR.QaHarness.T022.dll",
                "source_path": str(src / "SBPR.QaHarness.T022.dll"),
                "sha256": h_harness,
            },
            {
                "name": "SBPR.Trailborne.dll",
                "source_path": str(src / "SBPR.Trailborne.dll"),
                "sha256": h_product,
            },
        ],
        clients=clients,
    )
    return {
        "manifest": manifest,
        "roots": roots,
        "src": src,
        "harness_bytes": harness_bytes,
        "product_bytes": product_bytes,
        "h_harness": h_harness,
        "h_product": h_product,
    }


def stager_for(manifest, **kwargs):
    actors = [c.actor for c in manifest.clients]
    return ArtifactStager(
        manifest=manifest,
        filesystems={a: local_fs() for a in actors},
        **kwargs,
    )


# --------------------------------------------------------------------------- #
# THE case this ticket exists for
# --------------------------------------------------------------------------- #

class TestTheTwelveDayFailure:
    """The harness present on one client and absent from the other, caught by name."""

    def test_postconditions_name_the_client_missing_the_harness(self, rig):
        stager = stager_for(rig["manifest"])
        stager.stage_all()

        # Simulate the exact historical state: client_b loses the harness.
        victim = rig["roots"]["client_b"] / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"
        victim.unlink()

        failures = stager.assert_postconditions()
        assert failures, "an absent harness must never pass postconditions"
        absent = [f for f in failures if f.precondition == P_ARTIFACT_STAGED]
        assert len(absent) == 1
        assert absent[0].client == "client_b"
        assert "SBPR.QaHarness.T022.dll" in absent[0].detail
        assert "ABSENT" in absent[0].detail
        # The remedy must state the observable, so nobody debugs the launch path again.
        assert "menu" in absent[0].remedy

    def test_staging_reaches_every_client_without_a_source_client(self, rig):
        """Neither client is the other's source; both are staged from the catalogue."""
        stager_for(rig["manifest"]).stage_all()
        for actor, root in rig["roots"].items():
            harness = root / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"
            assert harness.read_bytes() == rig["harness_bytes"], actor


# --------------------------------------------------------------------------- #
# Count-agnosticism (I2)
# --------------------------------------------------------------------------- #

class TestCountAgnostic:
    @pytest.mark.parametrize("count", [1, 2, 3, 4, 5, 9])
    def test_every_artifact_is_staged_regardless_of_count(self, tmp_path, count):
        """The old loop was bounded by a literal; a fourth entry staged nothing."""
        src = tmp_path / "build"
        artifacts = []
        reqs = []
        root = tmp_path / "client_only"
        (root / "BepInEx" / "plugins").mkdir(parents=True)
        for i in range(count):
            name = f"Artifact{i}.dll"
            digest = write(src / name, f"BYTES-{i}".encode())
            artifacts.append(
                {"name": name, "source_path": str(src / name), "sha256": digest}
            )
            reqs.append(
                {"artifact": name, "dest_path": str(root / f"BepInEx/plugins/{name}")}
            )

        manifest = build_manifest(
            tmp_path,
            artifacts=artifacts,
            clients=[
                {
                    "actor": "client_only",
                    "game_root": str(root),
                    "port": 48610,
                    "artifacts": reqs,
                }
            ],
        )
        staged = stager_for(manifest).stage_all()
        assert len(staged) == count
        for i in range(count):
            assert (root / f"BepInEx/plugins/Artifact{i}.dll").read_bytes() == f"BYTES-{i}".encode()

    def test_module_contains_no_artifact_count_literal(self):
        """Regression guard: count-agnosticism must stay structural, not derived.

        The previous fix derived a count from a path list, which still required manifest
        line ORDER to match. Iterating the parsed structure removes the concept of an
        artifact index; this asserts nobody reintroduces one.
        """
        import runner_core.artifact_staging as mod

        source = open(mod.__file__, "r", encoding="utf-8").read()
        for banned in ("range(3)", "range(4)", "[0:3]", "_deploy_count", "ARTIFACT_COUNT"):
            assert banned not in source, f"reintroduced an artifact bound: {banned}"

    def test_adding_a_third_client_is_data_only(self, rig, tmp_path):
        """A third client stages with no code change — it is another manifest entry."""
        raw_clients = []
        for c in rig["manifest"].clients:
            raw_clients.append(
                {
                    "actor": c.actor,
                    "game_root": c.game_root,
                    "port": c.ports["loopback_control"],
                    "artifacts": [
                        {"artifact": r.artifact, "dest_path": r.dest_path} for r in c.artifacts
                    ],
                }
            )
        root_c = tmp_path / "client_c"
        (root_c / "BepInEx" / "plugins").mkdir(parents=True)
        raw_clients.append(
            {
                "actor": "client_c",
                "game_root": str(root_c),
                "port": 48612,
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": str(root_c / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"),
                    }
                ],
            }
        )
        manifest = build_manifest(
            tmp_path,
            artifacts=[
                {
                    "name": "SBPR.QaHarness.T022.dll",
                    "source_path": str(rig["src"] / "SBPR.QaHarness.T022.dll"),
                    "sha256": rig["h_harness"],
                },
                {
                    "name": "SBPR.Trailborne.dll",
                    "source_path": str(rig["src"] / "SBPR.Trailborne.dll"),
                    "sha256": rig["h_product"],
                },
            ],
            clients=raw_clients,
        )
        stager = stager_for(manifest)
        stager.stage_all()
        assert not stager.assert_postconditions()
        assert (root_c / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll").exists()


# --------------------------------------------------------------------------- #
# Creating a missing plugin directory (I3)
# --------------------------------------------------------------------------- #

class TestCreateMissingDirectory:
    def test_creates_the_plugin_directory_and_stages_into_it(self, rig):
        """The old stager refused a missing parent, so a manifest could add nothing."""
        target_dir = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.QaHarness.T022"
        assert not target_dir.exists()

        staged = stager_for(rig["manifest"]).stage_all()
        assert target_dir.is_dir()
        assert stat.S_IMODE(os.stat(target_dir).st_mode) == 0o755
        created = [s for s in staged if s.created_parent]
        assert created, "creating a new plugin directory must be recorded"

    def test_refuses_when_grandparent_is_missing(self, rig):
        """Creation is one level only; it never materialises a whole tree."""
        import shutil as _shutil

        _shutil.rmtree(rig["roots"]["client_a"] / "BepInEx")
        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "missing" in str(exc.value) or "not a directory" in str(exc.value)

    def test_refuses_when_grandparent_is_a_symlink(self, rig, tmp_path):
        elsewhere = tmp_path / "elsewhere"
        elsewhere.mkdir()
        plugins = rig["roots"]["client_a"] / "BepInEx/plugins"
        plugins.rmdir()
        plugins.symlink_to(elsewhere)

        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "symlink" in str(exc.value) or "not a directory" in str(exc.value)

    def test_refuses_when_destination_escapes_the_game_root(self, rig, tmp_path):
        """A symlinked intermediate can point outside the root; the string check can't see it."""
        outside = tmp_path / "outside"
        outside.mkdir()
        plugins = rig["roots"]["client_a"] / "BepInEx/plugins"
        harness_dir = plugins / "SBPR.QaHarness.T022"
        harness_dir.symlink_to(outside)

        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "outside" in str(exc.value) or "symlink" in str(exc.value)

    def test_never_adopts_an_existing_non_directory_at_the_parent_path(self, rig):
        parent = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.QaHarness.T022"
        parent.write_text("i am a file")
        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "not a directory" in str(exc.value) or "already exists" in str(exc.value)


# --------------------------------------------------------------------------- #
# Transaction / rollback
# --------------------------------------------------------------------------- #

class TestTransaction:
    def test_missing_source_writes_absolutely_nothing(self, rig):
        """Source problems abort in the plan phase, before any byte is written."""
        (rig["src"] / "SBPR.Trailborne.dll").unlink()
        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "nothing was written" in str(exc.value)
        for root in rig["roots"].values():
            assert not (root / "BepInEx/plugins/SBPR.QaHarness.T022").exists()

    def test_drifted_source_is_refused_before_writing(self, rig):
        """Staging bytes that differ from the pin would deploy unreviewed code."""
        (rig["src"] / "SBPR.Trailborne.dll").write_bytes(b"TAMPERED")
        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "drifted from its pin" in str(exc.value)
        for root in rig["roots"].values():
            assert not (root / "BepInEx/plugins/SBPR.Trailborne").exists()

    def test_plan_reports_every_problem_not_just_the_first(self, rig):
        (rig["src"] / "SBPR.Trailborne.dll").unlink()
        (rig["src"] / "SBPR.QaHarness.T022.dll").write_bytes(b"TAMPERED")
        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).plan()
        message = str(exc.value)
        assert "SBPR.Trailborne.dll" in message and "SBPR.QaHarness.T022.dll" in message

    def test_mid_batch_failure_restores_prior_bytes(self, rig):
        """Artifacts already replaced must be put back, not left mixed."""
        # Pre-existing older deployment on client_a.
        old = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll"
        write(old, b"OLD-PRODUCT-BYTES")

        fs_a = local_fs()
        fs_b = local_fs()
        calls = {"n": 0}
        real_replace = fs_b.replace

        def exploding_replace(src, dst):
            calls["n"] += 1
            raise OSError("simulated rename failure on the second client")

        fs_b.replace = exploding_replace

        stager = ArtifactStager(
            manifest=rig["manifest"], filesystems={"client_a": fs_a, "client_b": fs_b}
        )
        with pytest.raises(ArtifactStagingError):
            stager.stage_all()

        # client_a's replaced artifact is back to its original bytes.
        assert old.read_bytes() == b"OLD-PRODUCT-BYTES"
        # client_a's newly created artifact is gone entirely.
        assert not (
            rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"
        ).exists()

    def test_rollback_removes_directories_it_created(self, rig):
        fs_a = local_fs()
        fs_b = local_fs()
        fs_b.replace = lambda s, d: (_ for _ in ()).throw(OSError("boom"))
        stager = ArtifactStager(
            manifest=rig["manifest"], filesystems={"client_a": fs_a, "client_b": fs_b}
        )
        with pytest.raises(ArtifactStagingError):
            stager.stage_all()
        assert not (rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.QaHarness.T022").exists()

    def test_failed_rollback_is_loud_and_names_every_stranded_path(self, rig):
        """A rollback that fails quietly is worse than one that never ran."""
        fs_a = local_fs()
        fs_b = local_fs()

        def fail_replace(src, dst):
            raise OSError("rename failed")

        fs_b.replace = fail_replace
        # client_a's rollback also fails: unlink of the newly created file errors.
        def fail_unlink(path):
            raise OSError("cannot remove")

        fs_a.unlink = fail_unlink

        stager = ArtifactStager(
            manifest=rig["manifest"], filesystems={"client_a": fs_a, "client_b": fs_b}
        )
        with pytest.raises(StagingRollbackError) as exc:
            stager.stage_all()
        message = str(exc.value)
        assert "ROLLBACK INCOMPLETE" in message
        assert "client_a" in message
        assert "manual reconciliation" in message

    def test_successful_staging_leaves_no_temp_or_prev_files(self, rig):
        old = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll"
        write(old, b"OLD-PRODUCT-BYTES")
        stager_for(rig["manifest"]).stage_all()
        for root in rig["roots"].values():
            leftovers = [
                p for p in root.rglob(".*") if ".sbpr-stage" in p.name or ".sbpr-prev" in p.name
            ]
            assert not leftovers, leftovers


# --------------------------------------------------------------------------- #
# Idempotency (converges; #455 owns cross-run sweep)
# --------------------------------------------------------------------------- #

class TestIdempotency:
    def test_staging_twice_converges_and_second_run_is_a_no_op(self, rig):
        stager_for(rig["manifest"]).stage_all()
        second = stager_for(rig["manifest"]).stage_all()
        assert {s.action for s in second} == {"already-current"}
        assert not stager_for(rig["manifest"]).assert_postconditions()

    def test_plan_marks_matching_bytes_as_already_current(self, rig):
        stager_for(rig["manifest"]).stage_all()
        planned = stager_for(rig["manifest"]).plan()
        assert all(p.action == "already-current" for p in planned)


# --------------------------------------------------------------------------- #
# Ownership / mode
# --------------------------------------------------------------------------- #

class TestOwnershipAndMode:
    def test_staged_artifacts_are_0644(self, rig):
        stager_for(rig["manifest"]).stage_all()
        for root in rig["roots"].values():
            path = root / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"
            assert stat.S_IMODE(os.stat(path).st_mode) == ARTIFACT_MODE

    def test_refuses_a_symlinked_destination(self, rig, tmp_path):
        decoy = tmp_path / "decoy.dll"
        decoy.write_bytes(b"decoy")
        dest_dir = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne"
        dest_dir.mkdir(parents=True)
        (dest_dir / "SBPR.Trailborne.dll").symlink_to(decoy)

        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "symlink" in str(exc.value)
        assert decoy.read_bytes() == b"decoy", "must never write through the symlink"

    def test_refuses_a_group_writable_parent(self, rig):
        parent = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne"
        parent.mkdir(parents=True)
        os.chmod(parent, 0o707)  # other-writable, outside the hardenable allowlist
        with pytest.raises(ArtifactStagingError) as exc:
            stager_for(rig["manifest"]).stage_all()
        assert "writable" in str(exc.value)

    def test_hardens_a_benign_0775_parent_to_0755(self, rig):
        parent = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne"
        parent.mkdir(parents=True)
        os.chmod(parent, 0o775)
        stager_for(rig["manifest"]).stage_all()
        assert stat.S_IMODE(os.stat(parent).st_mode) == 0o755

    def test_postconditions_flag_a_wrong_mode(self, rig):
        stager = stager_for(rig["manifest"])
        stager.stage_all()
        path = rig["roots"]["client_b"] / "BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll"
        os.chmod(path, 0o600)
        failures = stager.assert_postconditions()
        modes = [f for f in failures if f.precondition == P_ARTIFACT_OWNERSHIP]
        assert modes and modes[0].client == "client_b"


# --------------------------------------------------------------------------- #
# Postconditions
# --------------------------------------------------------------------------- #

class TestPostconditions:
    def test_clean_stage_passes(self, rig):
        stager = stager_for(rig["manifest"])
        stager.stage_all()
        assert stager.assert_postconditions() == []

    def test_drift_after_staging_is_reported_separately_from_absence(self, rig):
        """Absence and drift are different failures with different remedies."""
        stager = stager_for(rig["manifest"])
        stager.stage_all()
        path = rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll"
        path.write_bytes(b"SOMETHING-ELSE-ENTIRELY")

        failures = stager.assert_postconditions()
        kinds = {f.precondition for f in failures}
        assert P_ARTIFACT_BYTES in kinds
        assert P_ARTIFACT_STAGED not in kinds
        drift = [f for f in failures if f.precondition == P_ARTIFACT_BYTES][0]
        assert drift.client == "client_a"
        assert "sha256" in drift.expected and "sha256" in drift.actual

    def test_reports_every_problem_not_just_the_first(self, rig):
        stager = stager_for(rig["manifest"])
        stager.stage_all()
        (rig["roots"]["client_a"] / "BepInEx/plugins/SBPR.Trailborne/SBPR.Trailborne.dll").unlink()
        (
            rig["roots"]["client_b"] / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll"
        ).unlink()
        failures = stager.assert_postconditions()
        assert {f.client for f in failures} == {"client_a", "client_b"}

    def test_every_failure_names_client_expected_and_actual(self, rig):
        """The reporting contract: a failure that can't fill these in isn't worth emitting."""
        stager = stager_for(rig["manifest"])
        stager.stage_all()
        (rig["roots"]["client_b"] / "BepInEx/plugins/SBPR.QaHarness.T022/SBPR.QaHarness.T022.dll").unlink()
        for f in stager.assert_postconditions():
            assert f.client and f.detail and f.expected and f.actual and f.remedy
            rendered = f.render()
            assert f.client in rendered and "expected:" in rendered


# --------------------------------------------------------------------------- #
# Dry run
# --------------------------------------------------------------------------- #

class TestDryRun:
    def test_plan_writes_nothing(self, rig):
        planned = stager_for(rig["manifest"]).plan()
        assert len(planned) == 4
        for root in rig["roots"].values():
            assert not (root / "BepInEx/plugins/SBPR.QaHarness.T022").exists()

    def test_render_groups_by_client_so_asymmetry_is_visible(self, rig):
        rendered = render_plan(stager_for(rig["manifest"]).plan())
        assert "client_a" in rendered and "client_b" in rendered
        assert "[create]" in rendered
        assert "create parent directory" in rendered
