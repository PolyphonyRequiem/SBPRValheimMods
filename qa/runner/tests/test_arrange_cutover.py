"""CUTOVER — the single arrange authority (T022 ARRANGE §3 P1, issue #457).

The chain composes four phases that already have their own suites, so these tests
deliberately do NOT re-test SWEEP/STATIC/STAGE/VERIFY behaviour. They test the four
things the composition itself is responsible for and nothing else can assert:

  1. ORDER — SWEEP before STATIC before STAGE before VERIFY (§4.2 Ordering). Running
     STATIC on residue SWEEP is about to delete, or STAGE on a manifest STATIC refused,
     are the two orderings the spec forbids.
  2. GATING — a failing phase STOPS the chain, and the phases after it are recorded as
     `not-reached`, never omitted and never rendered as passes.
  3. READINESS — `ready` is the conjunction of every phase; there is no partial pass.
  4. HONESTY — the report never upgrades VERIFY's evidence. A `staged-delivery` V3 is
     never summarised as though a launched client had proven the join.

Nothing here launches a client, binds a port, writes a credential or signals a
process: every phase is a stub callable, which is the whole point of the environment
being a mandatory injected seam.
"""
from __future__ import annotations

import ast
import dataclasses
import os

import pytest

from runner_core.arrange_cutover import (  # noqa: E402  (path injected by conftest)
    CUTOVER_PHASES,
    OUTCOME_FAILED,
    OUTCOME_NOT_REACHED,
    OUTCOME_PASSED,
    PHASE_STAGE,
    PHASE_STATIC,
    PHASE_SWEEP,
    PHASE_VERIFY,
    CutoverEnvironment,
    arrange_cutover,
)
from runner_core.arrange_manifest import ArrangeManifest
from runner_core.arrange_static import StaticFailure, StaticReport
from runner_core.arrange_sweep import OUTCOME_REMOVED, SweepAction, SweepReport
from runner_core.arrange_verify import (
    METHOD_STAGED_DELIVERY,
    ClientReadiness,
    CriterionResult,
    P_JOIN_PATH,
    ReadinessReport,
)


# --------------------------------------------------------------------------- #
# Fixtures — a minimal well-formed manifest and a recording environment.
# --------------------------------------------------------------------------- #

def golden_manifest_dict():
    """A two-client manifest with the shape the schema requires.

    Paths are absolute and fictional. Nothing in this module touches a filesystem, so
    they are never resolved — but they must be ABSOLUTE, because the schema refuses a
    relative path (it would resolve against whichever cwd a phase happened to run in).
    """
    return {
        "kind": "sbpr-qa-arrange-manifest",
        "version": 3,
        "run_id": "t022-run-cutover-test",
        "lane": {
            "lane_id": "t022-disposable",
            "world_name": "t022lane",
            "host": "127.0.0.1",
            "port": 2476,
            "requires_password": False,
        },
        "artifacts": [],
        "clients": [
            {
                "actor": "client_a",
                "uid": 1000,
                "user": "polyphonyrequiem",
                "steam_account": "76561197965627562",
                "game_root": "/opt/t022/a",
                "binary_path": "/opt/t022/a/valheim.x86_64",
                "plugins_dir": "/opt/t022/a/BepInEx/plugins",
                "launcher": {"kind": "direct_exec"},
                "ports": {
                    "loopback_control": 48610,
                    "valbridge_gabp": 49152,
                    "unity_script_host": None,
                },
                "artifacts": [],
                "credentials": {},
            },
            {
                "actor": "client_b",
                "uid": 1001,
                "user": "valbot",
                "steam_account": "76561198671522196",
                "game_root": "/opt/t022/b",
                "binary_path": "/opt/t022/b/valheim.x86_64",
                "plugins_dir": "/opt/t022/b/BepInEx/plugins",
                "launcher": {"kind": "direct_exec"},
                "ports": {
                    "loopback_control": 48611,
                    "valbridge_gabp": 49153,
                    "unity_script_host": None,
                },
                "artifacts": [],
                "credentials": {},
            },
        ],
    }


@pytest.fixture
def manifest():
    return ArrangeManifest.parse(golden_manifest_dict())


def failure(precondition="S1-MANIFEST-WELL-FORMED", client="client_a"):
    return StaticFailure(
        precondition=precondition,
        client=client,
        detail="a named precondition failure",
        expected="the precondition to hold",
        actual="it did not",
        remedy="fix the named field",
    )


def passing_sweep(actors):
    return SweepReport(
        ok=True,
        actions=(
            SweepAction(
                precondition="W1-CREDENTIALS-CLEARED",
                client=actors[0],
                resource="credential",
                target="/run/sbpr-qa/a/lane-pw.txt",
                outcome=OUTCOME_REMOVED,
                reason="prior-run credential removed",
            ),
        ),
        swept_clients=tuple(actors),
        checked_preconditions=("W1-CREDENTIALS-CLEARED",),
    )


def passing_static(actors):
    return StaticReport(ok=True, checked_clients=tuple(actors), checked_preconditions=("S1",))


def passing_verify(actors, *, method=METHOD_STAGED_DELIVERY, proven_live=False):
    return ReadinessReport(
        ok=True,
        clients=tuple(
            ClientReadiness(
                actor=actor,
                ready=True,
                criteria=(
                    CriterionResult(
                        criterion=P_JOIN_PATH,
                        ok=True,
                        evidence="sidecar carries the join target and the wrapper reads it",
                        method=method,
                        proven_live=proven_live,
                    ),
                ),
                failures=(),
            )
            for actor in actors
        ),
    )


class RecordingEnvironment:
    """A `CutoverEnvironment` whose four seams record the order they ran in.

    Each seam can be told to fail, so a test can put a failure at any position and
    assert what the chain did afterwards.
    """

    def __init__(self, *, fail=None, raises=None):
        self.calls = []
        self._fail = fail
        self._raises = raises

    def _maybe_raise(self, phase):
        if self._raises == phase:
            raise RuntimeError(f"{phase} blew up")

    def sweep(self, manifest):
        self.calls.append(PHASE_SWEEP)
        self._maybe_raise(PHASE_SWEEP)
        if self._fail == PHASE_SWEEP:
            return SweepReport(
                ok=False,
                actions=(
                    SweepAction(
                        precondition="W1-CREDENTIALS-CLEARED",
                        client="client_a",
                        resource="credential",
                        target="/run/sbpr-qa/a/lane-pw.txt",
                        outcome="left-alone",
                        reason="not provably ours",
                    ),
                ),
                swept_clients=tuple(manifest.actors),
            )
        return passing_sweep(manifest.actors)

    def static(self, manifest):
        self.calls.append(PHASE_STATIC)
        self._maybe_raise(PHASE_STATIC)
        if self._fail == PHASE_STATIC:
            return StaticReport(
                ok=False, failures=(failure(),), checked_clients=tuple(manifest.actors)
            )
        return passing_static(manifest.actors)

    def stage(self, manifest):
        self.calls.append(PHASE_STAGE)
        self._maybe_raise(PHASE_STAGE)
        if self._fail == PHASE_STAGE:
            return (failure("T1-ARTIFACT-STAGED"),)
        return ()

    def verify(self, manifest):
        self.calls.append(PHASE_VERIFY)
        self._maybe_raise(PHASE_VERIFY)
        if self._fail == PHASE_VERIFY:
            return ReadinessReport(
                ok=False,
                clients=(
                    ClientReadiness(
                        actor="client_b",
                        ready=False,
                        criteria=(
                            CriterionResult(
                                criterion=P_JOIN_PATH,
                                ok=False,
                                evidence="no join target in the launch path",
                                method="unestablished",
                                proven_live=False,
                            ),
                        ),
                        failures=(failure(P_JOIN_PATH, "client_b"),),
                    ),
                ),
            )
        return passing_verify(manifest.actors)

    def as_environment(self):
        return CutoverEnvironment(
            sweep=self.sweep, static=self.static, stage=self.stage, verify=self.verify
        )


# --------------------------------------------------------------------------- #
# 1. ORDER
# --------------------------------------------------------------------------- #

class TestPhaseOrdering:
    def test_runs_the_four_phases_in_the_specs_order(self, manifest):
        """SWEEP -> STATIC -> STAGE -> VERIFY (§4.2 Ordering).

        Not an arbitrary preference. SWEEP must precede STATIC because a stale
        credential at a declared path IS a present file, so STATIC can otherwise pass
        on bytes the run is about to delete; and VERIFY must follow STAGE because
        verifying a tree staging refused to finish reports against bytes nobody
        stands behind.
        """
        env = RecordingEnvironment()
        report = arrange_cutover(manifest, env.as_environment())

        assert env.calls == [PHASE_SWEEP, PHASE_STATIC, PHASE_STAGE, PHASE_VERIFY]
        assert env.calls == list(CUTOVER_PHASES)
        assert report.ready

    def test_reports_every_phase_and_the_manifests_clients(self, manifest):
        env = RecordingEnvironment()
        report = arrange_cutover(manifest, env.as_environment())

        assert [p.phase for p in report.phases] == list(CUTOVER_PHASES)
        assert list(report.clients) == ["client_a", "client_b"]
        assert report.run_id == "t022-run-cutover-test"
        assert all(p.outcome == OUTCOME_PASSED for p in report.phases)


# --------------------------------------------------------------------------- #
# 2. GATING — demonstrate the guard FAILING at every position, then passing.
# --------------------------------------------------------------------------- #

class TestAFailingPhaseStopsTheChain:
    """Every phase is demonstrated failing, and each stops everything after it.

    House rule: a guard nobody has watched fail is a guard nobody should trust. The
    positive path is asserted in `TestPhaseOrdering`; these are the negatives.
    """

    @pytest.mark.parametrize(
        "failing,expected_ran",
        [
            (PHASE_SWEEP, [PHASE_SWEEP]),
            (PHASE_STATIC, [PHASE_SWEEP, PHASE_STATIC]),
            (PHASE_STAGE, [PHASE_SWEEP, PHASE_STATIC, PHASE_STAGE]),
            (PHASE_VERIFY, list(CUTOVER_PHASES)),
        ],
    )
    def test_stops_at_the_failing_phase(self, manifest, failing, expected_ran):
        env = RecordingEnvironment(fail=failing)
        report = arrange_cutover(manifest, env.as_environment())

        assert not report.ready
        assert env.calls == expected_ran, (
            f"{failing} failed but the chain continued past it"
        )
        assert report.phase(failing).outcome == OUTCOME_FAILED

    def test_unreached_phases_are_named_not_omitted_and_not_passes(self, manifest):
        """"I did not look" and "I looked and it was fine" must never render alike.

        This is §4.1's undeterminable-is-never-a-pass rule applied to the chain
        itself. Omitting the later phases would let a consumer that counts passes
        conclude the run was fine; rendering them as passes would be worse.
        """
        env = RecordingEnvironment(fail=PHASE_STATIC)
        report = arrange_cutover(manifest, env.as_environment())

        assert report.phase(PHASE_STAGE).outcome == OUTCOME_NOT_REACHED
        assert report.phase(PHASE_VERIFY).outcome == OUTCOME_NOT_REACHED
        assert not report.phase(PHASE_STAGE).ok
        assert not report.phase(PHASE_VERIFY).ok

        payload = report.as_dict()
        assert payload["failed_phases"] == [PHASE_STATIC]
        assert payload["not_reached"] == [PHASE_STAGE, PHASE_VERIFY]

    def test_a_raising_phase_becomes_a_named_failure_not_an_exception(self, manifest):
        """One outcome shape, always.

        A caller must not have to handle BOTH a report and an exception to learn the
        same fact — that is how a failure ends up logged in one path and swallowed in
        the other.
        """
        env = RecordingEnvironment(raises=PHASE_STAGE)
        report = arrange_cutover(manifest, env.as_environment())

        assert not report.ready
        outcome = report.phase(PHASE_STAGE)
        assert outcome.outcome == OUTCOME_FAILED
        assert "RuntimeError" in outcome.detail
        assert report.phase(PHASE_VERIFY).outcome == OUTCOME_NOT_REACHED

    def test_named_failures_survive_into_the_top_level_report(self, manifest):
        """§3 P3: every failure names the precondition, the client, expected-vs-actual."""
        env = RecordingEnvironment(fail=PHASE_VERIFY)
        report = arrange_cutover(manifest, env.as_environment())

        assert [f.precondition for f in report.failures] == [P_JOIN_PATH]
        assert [f.client for f in report.failures] == ["client_b"]

    def test_readiness_is_none_when_verify_was_never_reached(self, manifest):
        """Not the same as "no client was ready", and must not read as such."""
        env = RecordingEnvironment(fail=PHASE_SWEEP)
        report = arrange_cutover(manifest, env.as_environment())

        assert report.readiness is None


# --------------------------------------------------------------------------- #
# 3. READINESS is a conjunction
# --------------------------------------------------------------------------- #

class TestReadinessIsTheRunsEntryCondition:
    def test_ready_only_when_every_phase_passed(self, manifest):
        assert arrange_cutover(manifest, RecordingEnvironment().as_environment()).ready
        for failing in CUTOVER_PHASES:
            report = arrange_cutover(
                manifest, RecordingEnvironment(fail=failing).as_environment()
            )
            assert not report.ready, f"{failing} failed but the run reported READY"

    def test_verify_readiness_report_is_carried_through_intact(self, manifest):
        """The phase's own report, not a lossy summary.

        A consumer that wants per-criterion evidence must be able to read it rather
        than re-derive it from a boolean — the same reason VERIFY records `method`
        and `proven_live` per criterion instead of flattening them.
        """
        report = arrange_cutover(manifest, RecordingEnvironment().as_environment())
        readiness = report.readiness

        assert isinstance(readiness, ReadinessReport)
        assert readiness.ok
        assert [c.actor for c in readiness.clients] == ["client_a", "client_b"]


# --------------------------------------------------------------------------- #
# 4. HONESTY — the composition must not upgrade VERIFY's evidence.
# --------------------------------------------------------------------------- #

class TestTheReportNeverUpgradesTheEvidence:
    def test_a_staged_delivery_join_is_never_summarised_as_live_proof(self, manifest):
        """V3's two rungs are not the same claim (§4.1), and READY does not merge them.

        `staged-delivery` says a sidecar carries the join target and the wrapper reads
        it. `live-argv` says a running process's real kernel argv carries it. Only the
        second is proof, and only a launched client can produce it — so a Phase-A
        arrange must never render a READY line that implies one.
        """
        report = arrange_cutover(manifest, RecordingEnvironment().as_environment())
        rendered = report.render()

        assert report.ready
        assert METHOD_STAGED_DELIVERY in rendered
        assert "live-argv" not in rendered

        readiness = report.readiness
        assert readiness is not None
        for client in readiness.clients:
            for criterion in client.criteria:
                if criterion.criterion == P_JOIN_PATH:
                    assert criterion.method == METHOD_STAGED_DELIVERY
                    assert criterion.proven_live is False

    def test_a_live_argv_join_is_reported_as_such_when_it_genuinely_holds(self, manifest):
        """The converse: the report must not FLATTEN live proof away either.

        Recording the weaker rung when the stronger one holds would be dishonest in
        the other direction, and would make the field useless for telling them apart.
        """
        env = RecordingEnvironment()
        env.verify = lambda m: passing_verify(  # type: ignore[assignment]
            m.actors, method="live-argv", proven_live=True
        )
        report = arrange_cutover(manifest, env.as_environment())

        assert "live-argv" in report.render()

    def test_rendering_a_not_ready_run_names_what_stopped_it(self, manifest):
        env = RecordingEnvironment(fail=PHASE_STAGE)
        rendered = arrange_cutover(manifest, env.as_environment()).render()

        assert "NOT READY" in rendered
        assert PHASE_STAGE in rendered
        assert OUTCOME_NOT_REACHED in rendered


# --------------------------------------------------------------------------- #
# 5. §3 P9 — proof seams are mandatory, enforced structurally.
# --------------------------------------------------------------------------- #

class TestProofSeamsAreMandatory:
    """P9, enforced structurally rather than per-seam.

    This contract has regressed three times already (#454 established it; #452/#453
    re-defaulted both seams; #467 and #473 restored them one at a time). It matters
    more here than in any single phase: this composition sweeps files, signals
    processes, writes a filesystem and reads another identity's credentials.
    """

    def test_arrange_cutover_does_not_default_its_environment(self, manifest):
        with pytest.raises(TypeError):
            arrange_cutover(manifest)  # type: ignore[call-arg]

    def test_real_environment_does_not_default_the_arranging_uid(self):
        """SWEEP removes files and signals processes AS an identity.

        Inheriting that silently is precisely what P9 forbids — the decision to act on
        this machine as this uid must be written where a human reviews it.
        """
        from runner_core.arrange_cutover import real_cutover_environment

        with pytest.raises(TypeError):
            real_cutover_environment()  # type: ignore[call-arg]

    def test_no_environment_field_carries_a_default(self):
        offenders = [
            f.name
            for f in dataclasses.fields(CutoverEnvironment)
            if f.default is not dataclasses.MISSING
            or f.default_factory is not dataclasses.MISSING  # type: ignore[misc]
        ]
        assert not offenders, (
            "CutoverEnvironment proof seams must not be defaulted (P9); "
            f"defaulted field(s): {offenders}"
        )

    def test_an_incomplete_environment_cannot_be_constructed(self):
        with pytest.raises(TypeError) as excinfo:
            CutoverEnvironment(  # arrange-seam-contract-negative
                sweep=lambda m: passing_sweep(m.actors),
                static=lambda m: passing_static(m.actors),
                stage=lambda m: (),
            )
        assert "verify" in str(excinfo.value)

    def test_every_repository_caller_supplies_the_seams(self):
        """No construction of the environment dataclass may omit a seam.

        A TypeError only fires on a code path that actually runs. This asserts the
        contract over every construction site in the repository, so a future merge
        that reintroduces a partial caller fails here rather than ten minutes into a
        GPU boot. AST walk, not a text scan: a paren counter cannot tell a paren in a
        docstring from a real one, and a guard whose job is catching silent
        regressions must not itself be able to fail silently.
        """
        repo = os.path.dirname(os.path.abspath(__file__))
        while not os.path.isfile(os.path.join(repo, "AGENTS.md")):
            parent = os.path.dirname(repo)
            assert parent != repo, "could not locate the repository root (AGENTS.md)"
            repo = parent
        assert os.path.isdir(os.path.join(repo, "qa", "runner")), repo

        target = CutoverEnvironment.__name__
        field_index = {
            f.name: i for i, f in enumerate(dataclasses.fields(CutoverEnvironment))
        }
        seams = tuple(field_index)
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
                        # the exemption is visible at the site rather than encoded as
                        # a path in this scanner.
                        continue
                    constructions += 1
                    missing = [
                        seam
                        for seam in seams
                        if not any(kw.arg == seam for kw in node.keywords)
                        and not any(kw.arg is None for kw in node.keywords)
                        and len(node.args) <= field_index[seam]
                    ]
                    if missing:
                        offenders.append(
                            f"{os.path.relpath(path, repo)}:{node.lineno} "
                            f"(missing: {', '.join(missing)})"
                        )
        # A scanner that silently matched nothing would pass forever.
        assert scanned > 10, f"scanner walked only {scanned} python files from {repo}"
        assert constructions >= 1, (
            f"scanner found only {constructions} construction site(s); it is no longer "
            "seeing the callers it exists to guard"
        )
        assert not offenders, (
            f"{target}(...) constructed without the mandatory proof seam(s) "
            f"{list(seams)} at: {offenders}"
        )
