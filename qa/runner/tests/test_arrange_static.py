"""Static-arrange tests (T022 ARRANGE §4 STATIC, issue #450).

Everything here is engine-free and pure: no process is started, no game is contacted,
no file is written. The filesystem seam is a dict.

The suite is organised around the two structural claims of the card:
  * `TestNoSymmetryAssumption` — no check assumes same-uid, same-path or
    same-launcher, and a fully asymmetric pair passes.
  * `TestThirdClientIsDataOnly` — adding a third client is a data change: the same
    unmodified code checks it and reports against it by name.
plus one class per precondition, each asserting the failure NAMES the precondition,
the client, and expected-vs-actual (the anti-silence contract).
"""
from __future__ import annotations

import ast
import copy
import os

import pytest

from runner_core.arrange_manifest import (
    ArrangeManifest,
    ArrangeManifestError,
    PRODUCTION_PORTS,
)
from runner_core.arrange_static import (
    P_ARTIFACT_CATALOGUE,
    P_ARTIFACT_PINS,
    P_DEST_UNDER_ROOT,
    P_DISABLED_COMPONENTS,
    P_JOIN_TARGET,
    P_LANE_PASSWORD,
    P_PORTS_DISJOINT,
    P_PRODUCTION_DENY,
    P_WELL_FORMED,
    StaticEnvironment,
    arrange_static,
    real_static_environment,
)

H_HARNESS = "a" * 64
H_PRODUCT = "b" * 64
H_STALE = "c" * 64


def fs(paths):
    """Build a StaticEnvironment from a {path: sha256|None} dict.

    A path mapped to None exists but cannot be read (permission case); a path absent
    from the dict does not exist.
    """

    return StaticEnvironment(
        path_exists=lambda p: p in paths,
        hash_file=lambda p: paths.get(p),
        # This module's cases are about hashes and plugin trees, not wrapper text.
        # The seam is mandatory (#467), so the stub is explicit: no wrapper is
        # readable here, and the join-delivery check reports that by name.
        read_text=lambda _p: None,
        find_named_files=lambda root, name: tuple(
            sorted(
                p
                for p in paths
                if p.startswith(root.rstrip("/") + "/") and p.rsplit("/", 1)[-1] == name
            )
        ),
    )


def golden_manifest():
    """A manifest describing the REAL asymmetric pair.

    client_a: uid 1000, GABS launcher, `+connect` argv, its own game root.
    client_b: uid 1001, Steam -applaunch under env -i, sidecar join delivery, a
              completely different root, binary, ports and credential paths.
    Nothing about b is derived from a.
    """
    return {
        "kind": "sbpr-qa-arrange-manifest",
        "version": 2,
        "lane": {
            "lane_id": "t022-disposable",
            "world_name": "t022lane",
            "host": "127.0.0.1",
            "port": 2476,
            "requires_password": True,
        },
        "artifacts": [
            {
                "name": "SBPR.QaHarness.T022.dll",
                "source_path": "/build/out/SBPR.QaHarness.T022.dll",
                "sha256": H_HARNESS,
            },
            {
                "name": "SBPR.Trailborne.dll",
                "source_path": "/build/out/SBPR.Trailborne.dll",
                "sha256": H_PRODUCT,
            },
        ],
        "clients": [
            {
                "actor": "client_a",
                "uid": 1000,
                "user": "polyphonyrequiem",
                "steam_account": "76561197965627562",
                "game_root": "/home/poly/.local/share/Trailborne/Valheim-Modded",
                "binary_path": "/home/poly/.local/share/Trailborne/Valheim-Modded/valheim.x86_64",
                "plugins_dir": "/home/poly/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins",
                "launcher": {
                    "kind": "gabs",
                    "endpoint": "http://localhost:8080/mcp",
                    "game_id": "valheim-qa-a",
                },
                "ports": {
                    "loopback_control": 48610,
                    "valbridge_gabp": 49152,
                    "unity_script_host": 48210,
                },
                "qa_profile": "sbpr_qa_a",
                "join": {"host": "127.0.0.1", "port": 2476, "delivery": "connect_argv"},
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": "/home/poly/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins/SBPR.QaHarness.T022.dll",
                    },
                    {
                        "artifact": "SBPR.Trailborne.dll",
                        "dest_path": "/home/poly/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins/SBPR.Trailborne.dll",
                    },
                ],
                "credentials": {
                    "server_password": {
                        "path": "/run/sbpr-qa/a/lane-pw.txt",
                        "consumer_uid": 1000,
                    }
                },
            },
            {
                "actor": "client_b",
                "uid": 1001,
                "user": "valbot",
                "steam_account": "76561198671522196",
                "game_root": "/home/valbot/.steam/steam/steamapps/common/Valheim",
                "binary_path": "/home/valbot/.steam/steam/steamapps/common/Valheim/valheim.x86_64",
                "plugins_dir": "/home/valbot/.steam/steam/steamapps/common/Valheim/BepInEx/plugins",
                "launcher": {
                    "kind": "steam_applaunch",
                    "app_id": "892970",
                    "launch_env_path": "/home/valbot/.local/share/sbpr-qa/launch-env/valheim.env",
                },
                "ports": {
                    "loopback_control": 48611,
                    "valbridge_gabp": 49153,
                    "unity_script_host": None,
                },
                "qa_profile": "sbpr_qa_b",
                "join": {
                    "host": "127.0.0.1",
                    "port": 2476,
                    "delivery": "launch_env_sidecar",
                },
                "artifacts": [
                    {
                        "artifact": "SBPR.QaHarness.T022.dll",
                        "dest_path": "/home/valbot/.steam/steam/steamapps/common/Valheim/BepInEx/plugins/SBPR.QaHarness.T022.dll",
                    },
                    {
                        "artifact": "SBPR.Trailborne.dll",
                        "dest_path": "/home/valbot/.steam/steam/steamapps/common/Valheim/BepInEx/plugins/SBPR.Trailborne.dll",
                    },
                ],
                "credentials": {
                    "server_password": {
                        "path": "/run/sbpr-qa/b/lane-pw.txt",
                        "consumer_uid": 1001,
                    }
                },
            },
        ],
    }


def golden_fs(manifest=None):
    """A filesystem where every source is present and correctly pinned, and nothing
    is deployed yet (the normal pre-staging state)."""
    return fs(
        {
            "/build/out/SBPR.QaHarness.T022.dll": H_HARNESS,
            "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
        }
    )


def failures_for(report, precondition, client=None):
    return [
        f
        for f in report.failures
        if f.precondition == precondition and (client is None or f.client == client)
    ]


# --------------------------------------------------------------------------- #

class TestGolden:
    def test_asymmetric_pair_passes(self):
        report = arrange_static(golden_manifest(), golden_fs())
        assert report.ok, report.render()
        assert list(report.checked_clients) == ["client_a", "client_b"]

    def test_pass_report_renders_without_failures(self):
        report = arrange_static(golden_manifest(), golden_fs())
        assert "PASS" in report.render()
        assert report.as_dict()["failures"] == []

    def test_deployed_and_matching_still_passes(self):
        m = golden_manifest()
        paths = {
            "/build/out/SBPR.QaHarness.T022.dll": H_HARNESS,
            "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
        }
        for c in m["clients"]:
            for a in c["artifacts"]:
                paths[a["dest_path"]] = (
                    H_HARNESS if "QaHarness" in a["artifact"] else H_PRODUCT
                )
        assert arrange_static(m, fs(paths)).ok

    def test_static_starts_no_process_and_writes_nothing(self):
        """The ONLY environment contact is the three read seams; assert they are all
        that is ever called by recording every path touched."""
        touched = []
        env = StaticEnvironment(
            path_exists=lambda p: touched.append(("exists", p)) or (p.startswith("/build/")),
            hash_file=lambda p: touched.append(("hash", p))
            or (H_HARNESS if "QaHarness" in p else H_PRODUCT),
            read_text=lambda _p: None,
            find_named_files=lambda root, name: touched.append(("find", f"{root}/{name}"))
            or (),
        )
        arrange_static(golden_manifest(), env)
        assert touched, "static phase must actually inspect the declared artifacts"
        assert all(kind in ("exists", "hash", "find") for kind, _ in touched)


class TestNoSymmetryAssumption:
    """Not a single check may assume same-uid, same-path, or same-launcher."""

    def test_different_uids_are_fine(self):
        m = golden_manifest()
        assert m["clients"][0]["uid"] != m["clients"][1]["uid"]
        assert arrange_static(m, golden_fs()).ok

    def test_different_launchers_are_fine(self):
        m = golden_manifest()
        assert m["clients"][0]["launcher"]["kind"] != m["clients"][1]["launcher"]["kind"]
        assert arrange_static(m, golden_fs()).ok

    def test_different_roots_binaries_and_credential_paths_are_fine(self):
        m = golden_manifest()
        a, b = m["clients"]
        assert a["game_root"] != b["game_root"]
        assert a["binary_path"] != b["binary_path"]
        assert (
            a["credentials"]["server_password"]["path"]
            != b["credentials"]["server_password"]["path"]
        )
        assert arrange_static(m, golden_fs()).ok

    def test_different_join_delivery_mechanisms_are_fine(self):
        m = golden_manifest()
        assert m["clients"][0]["join"]["delivery"] != m["clients"][1]["join"]["delivery"]
        assert arrange_static(m, golden_fs()).ok

    def test_client_order_does_not_change_the_verdict(self):
        m = golden_manifest()
        swapped = copy.deepcopy(m)
        swapped["clients"].reverse()
        assert arrange_static(m, golden_fs()).ok
        assert arrange_static(swapped, golden_fs()).ok

    def test_a_failure_on_one_client_does_not_implicate_the_other(self):
        m = golden_manifest()
        m["clients"][1]["ports"]["loopback_control"] = 2456  # production port
        report = arrange_static(m, golden_fs())
        assert not report.ok
        assert failures_for(report, P_PRODUCTION_DENY, "client_b")
        assert not failures_for(report, P_PRODUCTION_DENY, "client_a")


class TestThirdClientIsDataOnly:
    """Adding a third client must be a data change, not a code change."""

    @staticmethod
    def _third():
        return {
            "actor": "client_c",
            "uid": 1002,
            "user": "valbot2",
            "steam_account": "76561198000000003",
            "game_root": "/srv/qa/c/Valheim",
            "binary_path": "/srv/qa/c/Valheim/valheim.x86_64",
            "plugins_dir": "/srv/qa/c/Valheim/BepInEx/plugins",
            "launcher": {"kind": "direct_exec"},
            "ports": {
                "loopback_control": 48612,
                "valbridge_gabp": 49154,
                "unity_script_host": None,
            },
            "qa_profile": "sbpr_qa_c",
            "join": {"host": "127.0.0.1", "port": 2476, "delivery": "connect_argv"},
            "artifacts": [
                {
                    "artifact": "SBPR.QaHarness.T022.dll",
                    "dest_path": "/srv/qa/c/Valheim/BepInEx/plugins/SBPR.QaHarness.T022.dll",
                }
            ],
            "credentials": {
                "server_password": {"path": "/run/sbpr-qa/c/lane-pw.txt", "consumer_uid": 1002}
            },
        }

    def test_three_clients_pass_with_no_code_change(self):
        m = golden_manifest()
        m["clients"].append(self._third())
        report = arrange_static(m, golden_fs())
        assert report.ok, report.render()
        assert list(report.checked_clients) == ["client_a", "client_b", "client_c"]

    def test_third_client_is_checked_by_name(self):
        m = golden_manifest()
        third = self._third()
        third["ports"]["loopback_control"] = 48610  # collides with client_a
        m["clients"].append(third)
        report = arrange_static(m, golden_fs())
        assert not report.ok
        assert failures_for(report, P_PORTS_DISJOINT, "client_c")
        assert failures_for(report, P_PORTS_DISJOINT, "client_a")

    def test_third_clients_missing_harness_is_reported(self):
        """§I1: the mod under test must be present on EVERY client. A third client
        that references an artifact nobody declared is caught."""
        m = golden_manifest()
        third = self._third()
        third["artifacts"][0]["artifact"] = "SBPR.NotInCatalogue.dll"
        m["clients"].append(third)
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_ARTIFACT_CATALOGUE, "client_c")


class TestFailuresAreSpecific:
    """§3 P3 — every failure names precondition + client + expected/actual."""

    def test_every_failure_carries_all_four_fields(self):
        m = golden_manifest()
        m["clients"][0]["ports"]["loopback_control"] = 2466
        m["clients"][1]["join"]["port"] = 9999
        del m["clients"][1]["credentials"]["server_password"]
        report = arrange_static(m, fs({}))
        assert report.failures
        for f in report.failures:
            assert f.precondition
            assert f.client
            assert f.detail
            assert f.expected
            assert f.actual
            assert f.remedy, f"failure {f.precondition} gives no remedy"

    def test_checks_do_not_short_circuit(self):
        """One invocation reports EVERY problem — the whole point is to avoid
        discovering them one 10-minute boot cycle at a time."""
        m = golden_manifest()
        m["clients"][0]["ports"]["loopback_control"] = 2456
        m["clients"][1]["join"]["port"] = 1234
        m["lane"]["requires_password"] = False
        report = arrange_static(m, fs({}))
        kinds = {f.precondition for f in report.failures}
        assert P_PRODUCTION_DENY in kinds
        assert P_JOIN_TARGET in kinds
        assert P_LANE_PASSWORD in kinds
        assert P_ARTIFACT_PINS in kinds

    def test_rendered_report_contains_the_client_and_both_values(self):
        m = golden_manifest()
        m["clients"][1]["ports"]["loopback_control"] = 48610
        text = arrange_static(m, golden_fs()).render()
        assert "client_b" in text
        assert "expected:" in text and "actual:" in text
        assert "48610" in text

    def test_report_is_machine_readable(self):
        m = golden_manifest()
        m["clients"][0]["ports"]["x"] = 2456
        d = arrange_static(m, golden_fs()).as_dict()
        assert d["phase"] == "static" and d["ok"] is False
        entry = next(f for f in d["failures"] if f["precondition"] == P_PRODUCTION_DENY)
        assert entry["client"] == "client_a"
        assert "2456" in entry["actual"]


class TestProductionDeny:
    """Preserved guard: 2456/2466 hold REAL worlds and may never be a target."""

    @pytest.mark.parametrize("port", sorted(PRODUCTION_PORTS))
    def test_lane_may_not_target_production(self, port):
        m = golden_manifest()
        m["lane"]["port"] = port
        for c in m["clients"]:
            c["join"]["port"] = port
        report = arrange_static(m, golden_fs())
        assert not report.ok
        assert failures_for(report, P_PRODUCTION_DENY, "<manifest>")

    @pytest.mark.parametrize("port", sorted(PRODUCTION_PORTS))
    def test_no_client_port_may_be_production(self, port):
        m = golden_manifest()
        m["clients"][1]["ports"]["unity_script_host"] = port
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_PRODUCTION_DENY, "client_b")

    @pytest.mark.parametrize("port", sorted(PRODUCTION_PORTS))
    def test_no_client_may_join_production(self, port):
        m = golden_manifest()
        m["clients"][0]["join"]["port"] = port
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_PRODUCTION_DENY, "client_a")


class TestPortsDisjoint:
    """§I6 — a hardcoded single-instance port silently cost the second client a
    service (`Failed to bind 127.0.0.1:48210: Address already in use`)."""

    def test_colliding_client_ports_are_refused(self):
        m = golden_manifest()
        m["clients"][1]["ports"]["valbridge_gabp"] = 49152
        report = arrange_static(m, golden_fs())
        assert not report.ok
        assert failures_for(report, P_PORTS_DISJOINT, "client_a")
        assert failures_for(report, P_PORTS_DISJOINT, "client_b")

    def test_collision_names_both_claimants(self):
        m = golden_manifest()
        m["clients"][1]["ports"]["valbridge_gabp"] = 49152
        f = failures_for(arrange_static(m, golden_fs()), P_PORTS_DISJOINT)[0]
        assert "client_a" in f.actual and "client_b" in f.actual

    def test_client_port_may_not_collide_with_the_lane(self):
        m = golden_manifest()
        m["clients"][0]["ports"]["loopback_control"] = m["lane"]["port"]
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_PORTS_DISJOINT, "client_a")

    def test_two_listeners_on_one_client_may_not_share_a_port(self):
        m = golden_manifest()
        m["clients"][0]["ports"]["unity_script_host"] = 49152
        failures = failures_for(
            arrange_static(m, golden_fs()), P_PORTS_DISJOINT, "client_a"
        )
        assert len(failures) == 2
        assert all("more than one listener" in f.detail for f in failures)
        assert all("client_a." in f.remedy for f in failures)

    def test_same_port_name_on_different_clients_is_fine_when_values_differ(self):
        m = golden_manifest()
        assert (
            m["clients"][0]["ports"]["loopback_control"]
            != m["clients"][1]["ports"]["loopback_control"]
        )
        assert arrange_static(m, golden_fs()).ok

    def test_disabled_component_claims_no_port(self):
        m = golden_manifest()
        assert m["clients"][1]["ports"]["unity_script_host"] is None
        assert arrange_static(m, golden_fs()).ok


class TestDisabledComponents:
    @pytest.mark.parametrize(
        "relative",
        [
            "UnityScriptHost/UnityScriptHost.dll",
            "UnityScriptHost.dll",
            "renamed-folder/UnityScriptHost.dll",
        ],
    )
    def test_disabled_unity_script_host_must_not_be_deployed(self, relative):
        m = golden_manifest()
        plugin = m["clients"][1]["plugins_dir"] + "/" + relative
        paths = {
            "/build/out/SBPR.QaHarness.T022.dll": H_HARNESS,
            "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
            plugin: H_STALE,
        }
        report = arrange_static(m, fs(paths))
        failure = failures_for(report, P_DISABLED_COMPONENTS, "client_b")[0]
        assert "declared disabled" in failure.detail
        assert plugin in failure.actual

    def test_unreadable_plugin_tree_fails_closed(self):
        m = golden_manifest()
        env = StaticEnvironment(
            path_exists=lambda p: p.startswith("/build/out/"),
            hash_file=lambda p: H_HARNESS if "QaHarness" in p else H_PRODUCT,
            read_text=lambda _p: None,
            find_named_files=lambda _root, _name: None,
        )
        report = arrange_static(m, env)
        failure = failures_for(report, P_DISABLED_COMPONENTS, "client_b")[0]
        assert "unreadable" in failure.detail

    def test_enabled_unity_script_host_may_be_deployed(self):
        m = golden_manifest()
        plugin = (
            m["clients"][0]["plugins_dir"]
            + "/UnityScriptHost/UnityScriptHost.dll"
        )
        paths = {
            "/build/out/SBPR.QaHarness.T022.dll": H_HARNESS,
            "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
            plugin: H_STALE,
        }
        assert arrange_static(m, fs(paths)).ok


class TestEnumerationSeamIsMandatory:
    """`find_named_files` has no default, so an incomplete caller cannot be built.

    The regression this locks (#467): overlapping merges restored a
    `lambda _root, _name: None` default that #454's review had deliberately removed.
    The default failed CLOSED, so it was never a security bypass — it was worse in a
    subtler way. An omitted seam produced an ordinary S9 "plugin tree missing or
    unreadable" failure against the CLIENT, pointing an operator at filesystem
    permissions on a machine whose filesystem was fine, when the actual fault was a
    caller that never wired the proof. Making it mandatory converts a misleading
    runtime diagnosis into an impossible construction.
    """

    def test_constructing_without_find_named_files_raises_type_error(self):
        with pytest.raises(TypeError) as excinfo:
            StaticEnvironment(  # type: ignore[call-arg]  # arrange-seam-contract-negative
                path_exists=lambda p: False,
                hash_file=lambda p: None,
            )
        assert "find_named_files" in str(excinfo.value)

    def test_read_text_is_mandatory_on_the_same_grounds(self):
        """`read_text` is mandatory too (#467) — the asymmetry was removed.

        `3cba781` originally kept `read_text` optional, reasoning that "unreadable
        wrapper" is a self-describing S8 result. That reasoning does not survive
        contact with the actual failure mode: an omitted `read_text` is
        indistinguishable from a wrapper that genuinely could not be read, which is
        the same misleading-diagnosis defect that made `find_named_files` mandatory.
        `read_text` also carries the #453 join-delivery proof, so an unwired seam
        silently skips a check that exists to prevent a burned ten-minute boot.
        """
        with pytest.raises(TypeError) as excinfo:
            StaticEnvironment(  # type: ignore[call-arg]  # arrange-seam-contract-negative
                path_exists=lambda p: False,
                hash_file=lambda p: None,
                find_named_files=lambda _root, _name: (),
            )
        assert "read_text" in str(excinfo.value)

    def test_every_repository_caller_supplies_the_seam(self):
        """No construction of the environment dataclass may omit the seam.

        A type error only fires on a code path that actually runs. This asserts the
        contract over every construction site in the repository, including ones a
        given test session never reaches, so a future merge that reintroduces a
        two-field caller fails here rather than ten minutes into a GPU boot.

        The scan is an AST walk, deliberately not a text/paren scan. A naive brace
        counter cannot tell a paren inside a string or comment from a real one, so an
        unbalanced paren in a docstring makes it swallow an arbitrary trailing region
        of the file — which can then contain the keyword being looked for and turn the
        check into a silent PASS. A guard whose whole job is catching silent
        regressions must not itself be able to fail silently, so the parser decides
        what a call is.
        """
        # Anchor on the repository root by walking up to its marker, NOT by counting
        # `dirname` levels: this file sits at qa/runner/tests/, so three dirnames
        # lands on `qa/` and would silently skip every caller outside it — a scanner
        # that cannot see the thing it guards is worse than no scanner.
        repo = os.path.dirname(os.path.abspath(__file__))
        while not os.path.isfile(os.path.join(repo, "AGENTS.md")):
            parent = os.path.dirname(repo)
            assert parent != repo, "could not locate the repository root (AGENTS.md)"
            repo = parent
        assert os.path.isdir(os.path.join(repo, "qa", "runner")), repo

        target = StaticEnvironment.__name__
        # Both proof seams are mandatory (#467), so the scan guards both. Keeping the
        # list derived from one place means adding a fifth seam later cannot leave the
        # scanner silently guarding a subset.
        seams = ("read_text", "find_named_files")
        # Positional index of each seam in the dataclass field order. A construction
        # supplies a seam either by keyword or by having enough positional args to
        # reach its index. Hardcoding "3 or more args" was correct when the dataclass
        # had three fields and became silently wrong at four.
        seam_positions = {"read_text": 3, "find_named_files": 4}
        offenders = []
        scanned = 0
        constructions = 0
        for dirpath, dirnames, filenames in os.walk(repo):
            dirnames[:] = [
                d
                for d in dirnames
                if d not in {".git", "__pycache__", "obj", "bin", "node_modules"}
                and not d.startswith(".venv")
            ]
            for filename in filenames:
                if not filename.endswith(".py"):
                    continue
                path = os.path.join(dirpath, filename)
                text = open(path, encoding="utf-8", errors="replace").read()
                try:
                    tree = ast.parse(text)
                except SyntaxError:
                    continue
                scanned += 1
                lines = text.splitlines()
                for node in ast.walk(tree):
                    if not isinstance(node, ast.Call):
                        continue
                    func = node.func
                    name = (
                        func.id
                        if isinstance(func, ast.Name)
                        else func.attr
                        if isinstance(func, ast.Attribute)
                        else None
                    )
                    if name != target:
                        continue
                    line = lines[node.lineno - 1]
                    if "arrange-seam-contract-negative" in line:
                        # The one deliberate omission: the negative test above, which
                        # asserts the incomplete construction raises. Marked inline so
                        # the exemption is visible at the site rather than encoded as a
                        # path in this scanner.
                        continue
                    constructions += 1
                    supplied_kwargs = {kw.arg for kw in node.keywords}
                    for seam in seams:
                        # Keyword, or enough positional args to reach the seam's index.
                        supplied = seam in supplied_kwargs or (
                            len(node.args) >= seam_positions[seam]
                        )
                        if not supplied:
                            offenders.append(
                                f"{os.path.relpath(path, repo)}:{node.lineno} "
                                f"(missing {seam})"
                            )
        # A scanner that silently matched nothing would pass forever. Assert it walked
        # a real tree AND found real constructions, so a broken root, a filter, or a
        # parser change cannot look like a clean result.
        assert scanned > 10, f"scanner walked only {scanned} python files from {repo}"
        assert constructions >= 8, (
            f"scanner found only {constructions} construction site(s); it is no longer "
            "seeing the callers it exists to guard"
        )
        assert not offenders, (
            f"{target}(...) constructed without a mandatory proof seam "
            f"(guarding {list(seams)}) at: {offenders}"
        )


class TestRealDisabledComponentEnumeration:
    @staticmethod
    def failures_for_root(root):
        m = golden_manifest()
        m["clients"][1]["plugins_dir"] = str(root)
        report = arrange_static(m, real_static_environment())
        return failures_for(report, P_DISABLED_COMPONENTS, "client_b")

    def test_missing_declared_plugin_tree_fails_closed(self, tmp_path):
        failures = self.failures_for_root(tmp_path / "missing-plugins")
        assert len(failures) == 1
        assert "unreadable" in failures[0].detail

    def test_non_directory_plugin_tree_fails_closed(self, tmp_path):
        not_a_directory = tmp_path / "plugins"
        not_a_directory.write_bytes(b"not-a-directory")

        failures = self.failures_for_root(not_a_directory)
        assert len(failures) == 1
        assert "missing or unreadable" in failures[0].detail

    def test_nested_unity_script_host_is_found(self, tmp_path):
        plugins = tmp_path / "plugins"
        nested = plugins / "nested"
        nested.mkdir(parents=True)
        dll = nested / "UnityScriptHost.dll"
        dll.write_bytes(b"not-a-real-assembly")

        failures = self.failures_for_root(plugins)
        assert len(failures) == 1
        assert str(dll) in failures[0].actual

    def test_unity_script_host_through_directory_symlink_is_found(self, tmp_path):
        plugins = tmp_path / "plugins"
        outside = tmp_path / "outside"
        plugins.mkdir()
        outside.mkdir()
        dll = outside / "UnityScriptHost.dll"
        dll.write_bytes(b"not-a-real-assembly")
        (plugins / "linked").symlink_to(outside, target_is_directory=True)

        failures = self.failures_for_root(plugins)
        assert len(failures) == 1
        assert str(plugins / "linked" / "UnityScriptHost.dll") in failures[0].actual

    def test_symlinked_unity_script_host_file_is_found(self, tmp_path):
        plugins = tmp_path / "plugins"
        plugins.mkdir()
        outside_dll = tmp_path / "outside.dll"
        outside_dll.write_bytes(b"not-a-real-assembly")
        dll = plugins / "UnityScriptHost.dll"
        dll.symlink_to(outside_dll)

        failures = self.failures_for_root(plugins)
        assert len(failures) == 1
        assert str(dll) in failures[0].actual

    def test_multiple_unity_script_host_files_are_all_reported(self, tmp_path):
        plugins = tmp_path / "plugins"
        first = plugins / "first" / "UnityScriptHost.dll"
        second = plugins / "second" / "UnityScriptHost.dll"
        first.parent.mkdir(parents=True)
        second.parent.mkdir(parents=True)
        first.write_bytes(b"first")
        second.write_bytes(b"second")

        failures = self.failures_for_root(plugins)
        assert len(failures) == 1
        assert str(first) in failures[0].actual
        assert str(second) in failures[0].actual

    @pytest.mark.skipif(
        hasattr(os, "geteuid") and os.geteuid() == 0,
        reason="root can enumerate mode-000 directories",
    )
    def test_unreadable_subtree_fails_closed(self, tmp_path):
        plugins = tmp_path / "plugins"
        blocked = plugins / "blocked"
        blocked.mkdir(parents=True)
        blocked.chmod(0)
        try:
            failures = self.failures_for_root(plugins)
        finally:
            blocked.chmod(0o700)

        assert len(failures) == 1
        assert "missing or unreadable" in failures[0].detail

    def test_scandir_error_fails_closed(self, tmp_path, monkeypatch):
        plugins = tmp_path / "plugins"
        plugins.mkdir()

        def fail_scandir(_path):
            raise OSError("injected traversal failure")

        monkeypatch.setattr(os, "scandir", fail_scandir)
        failures = self.failures_for_root(plugins)

        assert len(failures) == 1
        assert "missing or unreadable" in failures[0].detail

    def test_directory_symlink_cycle_terminates(self, tmp_path):
        plugins = tmp_path / "plugins"
        plugins.mkdir()
        (plugins / "cycle").symlink_to(plugins, target_is_directory=True)

        assert self.failures_for_root(plugins) == []


class TestArtifactPins:
    """§I8 — a stale deployed launcher was correctly refused by byte-equality. Keep it."""

    def test_missing_source_is_named(self):
        m = golden_manifest()
        report = arrange_static(m, fs({"/build/out/SBPR.Trailborne.dll": H_PRODUCT}))
        f = failures_for(report, P_ARTIFACT_PINS)[0]
        assert "SBPR.QaHarness.T022.dll" in f.detail
        assert "/build/out/SBPR.QaHarness.T022.dll" in f.expected

    def test_drifted_source_is_named_with_both_hashes(self):
        m = golden_manifest()
        report = arrange_static(
            m,
            fs(
                {
                    "/build/out/SBPR.QaHarness.T022.dll": H_STALE,
                    "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
                }
            ),
        )
        f = failures_for(report, P_ARTIFACT_PINS)[0]
        assert H_HARNESS in f.expected and H_STALE in f.actual

    def test_unreadable_source_is_reported_not_raised(self):
        m = golden_manifest()
        report = arrange_static(
            m,
            fs(
                {
                    "/build/out/SBPR.QaHarness.T022.dll": None,
                    "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
                }
            ),
        )
        assert failures_for(report, P_ARTIFACT_PINS)

    def test_stale_deployed_copy_is_refused_per_client(self):
        m = golden_manifest()
        dest = m["clients"][1]["artifacts"][0]["dest_path"]
        report = arrange_static(
            m,
            fs(
                {
                    "/build/out/SBPR.QaHarness.T022.dll": H_HARNESS,
                    "/build/out/SBPR.Trailborne.dll": H_PRODUCT,
                    dest: H_STALE,
                }
            ),
        )
        f = failures_for(report, P_ARTIFACT_PINS, "client_b")[0]
        assert "STALE" in f.detail
        assert H_HARNESS in f.expected and H_STALE in f.actual

    def test_not_yet_deployed_is_not_a_failure(self):
        """§I3 — the stager must be able to CREATE, not only replace. An absent
        destination is PROVISION's job, not a static failure."""
        assert arrange_static(golden_manifest(), golden_fs()).ok

    def test_source_is_hashed_once_per_catalogue_entry(self):
        hashed = []
        env = StaticEnvironment(
            path_exists=lambda p: p.startswith("/build/"),
            hash_file=lambda p: hashed.append(p)
            or (H_HARNESS if "QaHarness" in p else H_PRODUCT),
            read_text=lambda _p: None,
            find_named_files=lambda _root, _name: (),
        )
        arrange_static(golden_manifest(), env)
        assert hashed.count("/build/out/SBPR.QaHarness.T022.dll") == 1


class TestArtifactDestinations:
    def test_dest_outside_own_game_root_is_refused(self):
        m = golden_manifest()
        m["clients"][0]["artifacts"][0]["dest_path"] = "/tmp/plugins/SBPR.QaHarness.T022.dll"
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_DEST_UNDER_ROOT, "client_a")

    def test_dest_under_a_sibling_root_says_so(self):
        m = golden_manifest()
        m["clients"][0]["artifacts"][0]["dest_path"] = m["clients"][1]["artifacts"][0][
            "dest_path"
        ]
        f = failures_for(arrange_static(m, golden_fs()), P_DEST_UNDER_ROOT, "client_a")[0]
        assert "client_b" in f.remedy


class TestLanePasswordPolicy:
    """M6-LANEPW, preserved: an unstated password policy stalls the handshake forever."""

    def test_gated_lane_requires_every_client_to_declare_a_credential(self):
        m = golden_manifest()
        del m["clients"][1]["credentials"]["server_password"]
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_LANE_PASSWORD, "client_b")
        assert not failures_for(report, P_LANE_PASSWORD, "client_a")

    def test_open_lane_refuses_a_declared_credential(self):
        m = golden_manifest()
        m["lane"]["requires_password"] = False
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_LANE_PASSWORD, "client_a")
        assert failures_for(report, P_LANE_PASSWORD, "client_b")

    def test_open_lane_with_no_credentials_passes(self):
        m = golden_manifest()
        m["lane"]["requires_password"] = False
        for c in m["clients"]:
            c["credentials"] = {}
        assert arrange_static(m, golden_fs()).ok

    def test_requires_password_is_never_inferred(self):
        m = golden_manifest()
        del m["lane"]["requires_password"]
        report = arrange_static(m, golden_fs())
        assert not report.ok
        assert failures_for(report, P_WELL_FORMED)


class TestCredentialConsumerUid:
    """§I4 — written 0600 by uid 1000, consumed by uid 1001: structurally impossible,
    and the only symptom was a client sitting at a menu."""

    def test_credential_consumed_by_a_foreign_uid_is_refused(self):
        m = golden_manifest()
        m["clients"][1]["credentials"]["server_password"]["consumer_uid"] = 1000
        f = failures_for(arrange_static(m, golden_fs()), P_LANE_PASSWORD, "client_b")[0]
        assert "1001" in f.expected and "1000" in f.actual

    def test_shared_credential_path_across_uids_is_refused(self):
        m = golden_manifest()
        shared = "/run/sbpr-qa/lane-pw.txt"
        m["clients"][0]["credentials"]["server_password"]["path"] = shared
        m["clients"][1]["credentials"]["server_password"]["path"] = shared
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_LANE_PASSWORD, "client_a")
        assert failures_for(report, P_LANE_PASSWORD, "client_b")


class TestJoinTarget:
    """§I5 — the live blocker: client_b is launched with no arguments under env -i,
    so `m_queuedJoinServer` is never populated and it stops at the server list."""

    def test_missing_join_target_is_refused(self):
        m = golden_manifest()
        del m["clients"][1]["join"]
        f = failures_for(arrange_static(m, golden_fs()), P_JOIN_TARGET, "client_b")[0]
        assert "no join target" in f.detail

    def test_join_target_must_be_this_runs_lane(self):
        m = golden_manifest()
        m["clients"][0]["join"]["port"] = 2477
        f = failures_for(arrange_static(m, golden_fs()), P_JOIN_TARGET, "client_a")[0]
        assert "2476" in f.expected and "2477" in f.actual

    def test_connect_argv_is_legal_under_the_steam_launcher(self):
        """REGRESSION (#449). An earlier revision refused `connect_argv` under
        `steam_applaunch`, reasoning that Steam passes no arguments. A live run
        disproved it: the Steam `%command%` wrapper appends the fragment after `"$@"`,
        the real kernel argv was `valheim.x86_64 +connect 127.0.0.1:2476`, and the lane
        server logged client_b's own SteamID connecting and spawning in-world.

        That check was therefore refusing the ONLY configuration proven to work. A
        fail-closed guard that blocks the working path is worse than no guard. STATIC
        does not guess at launcher capability; whether argv reaches the game is VERIFY's
        job (#456), which can read the launched process's actual argv."""
        m = golden_manifest()
        m["clients"][1]["join"]["delivery"] = "connect_argv"
        assert m["clients"][1]["launcher"]["kind"] == "steam_applaunch"
        report = arrange_static(m, golden_fs())
        assert report.ok, report.render()

    def test_no_delivery_mechanism_is_refused_by_launcher_kind(self):
        """Every known delivery must be legal under every known launcher: STATIC has no
        visibility into a wrapper's internals and must not invent a constraint."""
        from runner_core.arrange_manifest import JOIN_DELIVERY_KINDS, LAUNCHER_KINDS

        base = golden_manifest()
        for kind in sorted(LAUNCHER_KINDS):
            for delivery in sorted(JOIN_DELIVERY_KINDS):
                m = copy.deepcopy(base)
                params = {
                    "gabs": {"endpoint": "http://x/mcp", "game_id": "g"},
                    "steam_applaunch": {"app_id": "892970"},
                    "direct_exec": {},
                }[kind]
                m["clients"][1]["launcher"] = dict(kind=kind, **params)
                m["clients"][1]["join"]["delivery"] = delivery
                report = arrange_static(m, golden_fs())
                assert report.ok, f"{kind}/{delivery} wrongly refused: {report.render()}"

    def test_sidecar_delivery_under_steam_launcher_is_accepted(self):
        assert arrange_static(golden_manifest(), golden_fs()).ok

    def test_joining_client_must_name_a_qa_profile(self):
        m = golden_manifest()
        del m["clients"][0]["qa_profile"]
        assert failures_for(arrange_static(m, golden_fs()), P_JOIN_TARGET, "client_a")

    def test_two_clients_may_not_share_a_qa_profile(self):
        m = golden_manifest()
        m["clients"][1]["qa_profile"] = m["clients"][0]["qa_profile"]
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_JOIN_TARGET, "client_a")
        assert failures_for(report, P_JOIN_TARGET, "client_b")


class TestManifestWellFormedness:
    def test_unparseable_manifest_yields_one_named_failure_not_an_exception(self):
        report = arrange_static({"kind": "wrong"}, golden_fs())
        assert not report.ok
        assert [f.precondition for f in report.failures] == [P_WELL_FORMED]

    def test_garbage_input_is_reported(self):
        report = arrange_static("not a manifest", golden_fs())
        assert not report.ok and failures_for(report, P_WELL_FORMED)

    def test_already_parsed_manifest_is_accepted(self):
        manifest = ArrangeManifest.parse(golden_manifest())
        assert arrange_static(manifest, golden_fs()).ok

    @pytest.mark.parametrize(
        "field", ["uid", "user", "steam_account", "game_root", "binary_path", "plugins_dir"]
    )
    def test_every_identity_field_is_required(self, field):
        m = golden_manifest()
        del m["clients"][1][field]
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_WELL_FORMED)
        assert field in report.failures[0].actual

    def test_duplicate_actor_is_refused(self):
        m = golden_manifest()
        m["clients"][1]["actor"] = "client_a"
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_WELL_FORMED)
        assert "duplicate actor" in report.failures[0].actual

    def test_unknown_launcher_kind_is_refused_and_lists_the_known_ones(self):
        m = golden_manifest()
        m["clients"][0]["launcher"] = {"kind": "telepathy"}
        report = arrange_static(m, golden_fs())
        assert "telepathy" in report.failures[0].actual

    def test_launcher_missing_a_required_parameter_is_refused(self):
        m = golden_manifest()
        del m["clients"][0]["launcher"]["game_id"]
        report = arrange_static(m, golden_fs())
        assert "game_id" in report.failures[0].actual

    def test_unknown_launcher_parameter_is_refused_not_ignored(self):
        m = golden_manifest()
        m["clients"][0]["launcher"]["typoed_param"] = "x"
        report = arrange_static(m, golden_fs())
        assert "typoed_param" in report.failures[0].actual

    def test_relative_paths_are_refused(self):
        m = golden_manifest()
        m["clients"][0]["game_root"] = "relative/root"
        report = arrange_static(m, golden_fs())
        assert "ABSOLUTE" in report.failures[0].actual

    def test_unknown_join_delivery_is_refused(self):
        m = golden_manifest()
        m["clients"][0]["join"]["delivery"] = "hope"
        report = arrange_static(m, golden_fs())
        assert "hope" in report.failures[0].actual

    def test_bad_artifact_pin_is_refused(self):
        m = golden_manifest()
        m["artifacts"][0]["sha256"] = "not-a-hash"
        report = arrange_static(m, golden_fs())
        assert "sha256" in report.failures[0].actual

    def test_empty_client_list_is_refused(self):
        m = golden_manifest()
        m["clients"] = []
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_WELL_FORMED)

    @pytest.mark.parametrize("version", [1, 99])
    def test_wrong_version_is_refused(self, version):
        m = golden_manifest()
        m["version"] = version
        report = arrange_static(m, golden_fs())
        assert str(version) in report.failures[0].actual

    def test_boolean_port_is_refused(self):
        m = golden_manifest()
        m["clients"][0]["ports"]["loopback_control"] = True
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_WELL_FORMED)

    @pytest.mark.parametrize(
        "resource", ["loopback_control", "valbridge_gabp", "unity_script_host"]
    )
    def test_every_known_port_resource_is_required(self, resource):
        m = golden_manifest()
        del m["clients"][1]["ports"][resource]
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_WELL_FORMED)
        assert resource in report.failures[0].actual

    @pytest.mark.parametrize("resource", ["loopback_control", "valbridge_gabp"])
    def test_required_t022_listener_cannot_be_disabled(self, resource):
        m = golden_manifest()
        m["clients"][1]["ports"][resource] = None
        report = arrange_static(m, golden_fs())
        assert failures_for(report, P_WELL_FORMED)
        assert "null/disabled is not allowed" in report.failures[0].actual

    def test_out_of_range_port_is_refused(self):
        m = golden_manifest()
        m["clients"][0]["ports"]["loopback_control"] = 99999
        report = arrange_static(m, golden_fs())
        assert "65535" in report.failures[0].actual


class TestManifestModel:
    def test_client_lookup_by_actor(self):
        manifest = ArrangeManifest.parse(golden_manifest())
        assert manifest.client("client_b").uid == 1001
        with pytest.raises(KeyError):
            manifest.client("client_z")

    def test_actors_preserve_declaration_order(self):
        assert ArrangeManifest.parse(golden_manifest()).actors == ["client_a", "client_b"]

    def test_credential_mode_is_fixed_to_cross_uid_policy(self):
        m = golden_manifest()
        m["clients"][0]["credentials"]["server_password"]["mode"] = "0640"
        with pytest.raises(ArrangeManifestError, match="0644"):
            ArrangeManifest.parse(m)

    def test_credential_mode_defaults_to_0644(self):
        m = golden_manifest()
        assert ArrangeManifest.parse(m).client("client_a").credentials[
            "server_password"
        ].mode == 0o644

    def test_parse_raises_on_shape_errors(self):
        with pytest.raises(ArrangeManifestError):
            ArrangeManifest.parse({"kind": "sbpr-qa-arrange-manifest"})

    def test_duplicate_artifact_name_is_refused(self):
        m = golden_manifest()
        m["artifacts"].append(dict(m["artifacts"][0]))
        with pytest.raises(ArrangeManifestError, match="duplicate artifact"):
            ArrangeManifest.parse(m)


class TestStaticEnvironmentStructuralContract:
    """Structural guards that no single call shape can provide (#467).

    The call-shape negatives live in `TestEnumerationSeamIsMandatory` above; these
    two assert properties of the dataclass itself, so a seam re-defaulted in future
    is caught even if no explicit test happens to omit it.
    """

    def test_no_static_environment_field_carries_a_default(self):
        """Assert on the dataclass fields, not on one call shape.

        A future merge could re-add a default to a seam this suite happens not to
        omit in any explicit case. Reading the field metadata catches that directly.
        This is the guard that would have caught the #452/#453 re-defaulting
        regression at the moment it landed rather than three merges later.
        """
        import dataclasses

        for field in dataclasses.fields(StaticEnvironment):
            assert field.default is dataclasses.MISSING, (
                f"StaticEnvironment.{field.name} has a default; every proof seam must "
                "be mandatory so omitted wiring is impossible to construct (#467)"
            )
            assert field.default_factory is dataclasses.MISSING, (
                f"StaticEnvironment.{field.name} has a default_factory; see #467"
            )

    def test_real_static_environment_supplies_every_seam(self):
        env = real_static_environment()
        import dataclasses

        for field in dataclasses.fields(StaticEnvironment):
            assert callable(getattr(env, field.name)), (
                f"real_static_environment() left {field.name} unwired"
            )
