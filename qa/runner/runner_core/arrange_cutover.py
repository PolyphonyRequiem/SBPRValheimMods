"""CUTOVER — the single arrange authority the runner calls (T022 ARRANGE §3 P1, #457).

WHAT THIS IS
------------
§3 P1 says: *one script owns arrangement end to end; no other mechanism provisions,
writes credentials, or stages artifacts.* Four phases now exist — STATIC (#450),
SWEEP (#455), STAGE (#451), VERIFY (#456) — but until this module nothing composed
them into a single callable authority, and the live runner still arranged itself from
a **separate** source of truth: `build_live_run` derived credentials, sidecars and
launch env from the run DESCRIPTOR, never consulting the arrange MANIFEST or any of
its four phases.

That is the "four mechanisms that do not know about each other" (§0) reproduced
inside the runner: two independent descriptions of the same run, either of which can
be right while the other is wrong, and the only symptom is a client at a menu.

`arrange_cutover` is the composition. It runs SWEEP → STATIC → STAGE → VERIFY in the
spec's order over ONE manifest and returns ONE report whose `ready` is the run's entry
condition (§4 READY).

WHY EXPAND-CONTRACT AND NOT A SWAP
----------------------------------
Nothing here deletes anything. This module is **additive and opt-in**: the live path
gains the ability to arrange through it, while the descriptor-derived path remains
present and working. That ordering is deliberate — the blast radius spans mechanisms
that do not know about each other, so a simultaneous swap cannot stay green at any
intermediate commit, and "the suite went red for one commit" is how a refactor bug
gets attributed to the wrong change.

ORDERING, AND WHY EACH STEP GATES THE NEXT
------------------------------------------
  SWEEP  first: a stale credential at a declared path IS a present file, so STATIC can
         otherwise pass on bytes this run is about to delete (§4.2 Ordering).
  STATIC next: cheap descriptor/filesystem checks before anything writes (§3 P5), so a
         manifest the run has already refused to trust never gets staged on top of.
  STAGE  next: the only step here that mutates a filesystem.
  VERIFY last: reads back what the previous steps established. Verifying a tree that
         staging refused to finish would report against bytes nobody stands behind.

A step that fails STOPS the chain. The steps that did run are still reported in full —
this phase exists to end the practice of discovering one problem per ten-minute boot
cycle (§3 P3), so an operator gets every finding the run actually reached, and an
explicit record of which phases were never attempted rather than silence about them.

WHAT THIS DOES NOT DO
---------------------
It does not launch a client, and it does not claim any evidence a launch would
produce. In particular VERIFY's V3 `live-argv` rung is **only** available when a
client is already up; in the normal ordering (VERIFY precedes LAUNCH) V3 is satisfied
by the `staged-delivery` rung, which is strong pre-launch evidence and is NOT the same
claim (§4.1). This module propagates that distinction into its own report rather than
flattening it, so a consumer can never read "arranged" as "proven to join".

PROOF SEAMS ARE MANDATORY, NOT DEFAULTED (§3 P9)
------------------------------------------------
No field on `CutoverEnvironment` carries a default, and `arrange_cutover` defaults
neither the environment nor the arranging uid. This is the contract #454 established
and #452/#453, #467 and #473 each re-lost, and it matters MORE here than in any single
phase: this composition sweeps files, signals processes, writes a filesystem and reads
another identity's credentials. The decision to do all of that on THIS machine as THIS
identity must be written where a human reviews it, not inherited by whoever imports the
module. Enforcement is structural, matching #456/#473: a `dataclasses.fields` assertion
plus an AST scan of every construction site in the repository.

Engine-free: stdlib only, no product/game import. Every environment contact goes
through an injected seam, so importing or unit-testing this module reads nothing,
writes nothing, signals nothing and spawns nothing.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Any, Callable, Dict, List, Optional, Sequence

from .arrange_manifest import ArrangeManifest
from .arrange_static import StaticFailure, StaticReport
from .arrange_sweep import SweepReport
from .arrange_verify import ReadinessReport

# Stable phase ids, reported verbatim and grepped by operators — part of the contract,
# exactly like the STATIC S-ids, the STAGE T-ids, the SWEEP W-ids and the VERIFY V-ids.
PHASE_SWEEP = "SWEEP"
PHASE_STATIC = "STATIC"
PHASE_STAGE = "STAGE"
PHASE_VERIFY = "VERIFY"

# The spec's §4 order. SWEEP before STATIC before STAGE before VERIFY; see the module
# docstring for why each gates the next.
CUTOVER_PHASES = (PHASE_SWEEP, PHASE_STATIC, PHASE_STAGE, PHASE_VERIFY)

# Outcome of one phase within the chain.
OUTCOME_PASSED = "passed"
OUTCOME_FAILED = "failed"
# Never attempted, because an earlier phase failed. Deliberately NOT rendered as a
# pass or as a failure: "I did not look" and "I looked and it was fine" must never
# render as the same line (§4.1).
OUTCOME_NOT_REACHED = "not-reached"


@dataclass(frozen=True)
class PhaseOutcome:
    """One phase's result within the chain, with its own report kept intact.

    `report` is the phase's native report object (`StaticReport`, `SweepReport`,
    `ReadinessReport`, or the STAGE failure sequence), NOT a lossy summary. A consumer
    that wants the per-criterion detail reads it directly rather than re-deriving it
    from a flattened boolean — which is the same reason VERIFY records `method` and
    `proven_live` per criterion instead of collapsing them.
    """

    phase: str
    outcome: str
    detail: str
    report: Optional[Any] = None
    failures: Sequence[StaticFailure] = field(default_factory=tuple)

    @property
    def ok(self) -> bool:
        return self.outcome == OUTCOME_PASSED

    def as_dict(self) -> Dict[str, Any]:
        payload: Dict[str, Any] = {
            "phase": self.phase,
            "outcome": self.outcome,
            "detail": self.detail,
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
        if self.report is not None and hasattr(self.report, "as_dict"):
            payload["report"] = self.report.as_dict()
        return payload

    def render(self) -> str:
        return f"  [{self.phase}] {self.outcome}: {self.detail}"


@dataclass(frozen=True)
class CutoverReport:
    """The whole arrange outcome — the run's entry condition (§4 READY, §3 P7).

    `ready` is True only when EVERY phase in the chain passed. There is deliberately
    no "mostly arranged": a partial arrangement is the single most expensive thing
    this system can do (§0), so it is a hard failure naming the phase, the client and
    the precondition.
    """

    ready: bool
    phases: Sequence[PhaseOutcome] = field(default_factory=tuple)
    clients: Sequence[str] = field(default_factory=tuple)
    run_id: str = ""

    @property
    def failures(self) -> Sequence[StaticFailure]:
        """Every named failure across every phase that actually ran."""
        return tuple(f for p in self.phases for f in p.failures)

    def phase(self, name: str) -> PhaseOutcome:
        for entry in self.phases:
            if entry.phase == name:
                return entry
        raise KeyError(name)

    @property
    def readiness(self) -> Optional[ReadinessReport]:
        """VERIFY's per-client readiness report, when VERIFY was reached.

        None when the chain stopped earlier — which is NOT the same as "no client was
        ready", and callers must not read it as such.
        """
        try:
            outcome = self.phase(PHASE_VERIFY)
        except KeyError:
            return None
        report = outcome.report
        return report if isinstance(report, ReadinessReport) else None

    def as_dict(self) -> Dict[str, Any]:
        return {
            "phase": "cutover",
            "ready": self.ready,
            "run_id": self.run_id,
            "clients": list(self.clients),
            "phases": [p.as_dict() for p in self.phases],
            # Present at the top level so a consumer that only wants "what stopped it"
            # never has to walk every phase's report.
            "failed_phases": [p.phase for p in self.phases if p.outcome == OUTCOME_FAILED],
            "not_reached": [
                p.phase for p in self.phases if p.outcome == OUTCOME_NOT_REACHED
            ],
        }

    def render(self) -> str:
        if self.ready:
            head = (
                f"arrange CUTOVER: READY — {len(self.phases)} phase(s) over "
                f"{len(self.clients)} client(s): {', '.join(self.clients)}"
            )
        else:
            stopped = [p.phase for p in self.phases if p.outcome == OUTCOME_FAILED]
            head = (
                f"arrange CUTOVER: NOT READY — stopped at "
                f"{', '.join(stopped) if stopped else 'an unreached phase'} "
                f"over client(s) {', '.join(self.clients)}"
            )
        lines = [head, *(p.render() for p in self.phases)]
        for failure in self.failures:
            lines.append(failure.render())
        return "\n".join(lines)


@dataclass(frozen=True)
class CutoverEnvironment:
    """The injectable seam for the whole chain — one callable per phase.

    Each seam runs ONE phase and returns its native report:

    `sweep(manifest)`  -> `SweepReport`. Unlinks files and signals processes.
    `static(manifest)` -> `StaticReport`. Reads only.
    `stage(manifest)`  -> the sequence of `StaticFailure` staging left unresolved
        (empty means every artifact landed and re-read to its pin). Mutates a
        filesystem.
    `verify(manifest)` -> `ReadinessReport`. Reads and probes only.

    Composing at the PHASE level rather than re-wiring each phase's own seams is
    deliberate: every phase already owns a tested environment with its own mandatory
    proof seams, and re-plumbing them here would create a second place that decides
    how to read another identity's credentials — precisely the "exactly one mechanism"
    rule V2 exists to protect (§4.1 Reuse, not a second opinion).

    NO field carries a default (§3 P9). A caller that cannot perform a phase says so at
    the construction site, where a human reviews it.
    """

    sweep: Callable[[ArrangeManifest], SweepReport]
    static: Callable[[ArrangeManifest], StaticReport]
    stage: Callable[[ArrangeManifest], Sequence[StaticFailure]]
    verify: Callable[[ArrangeManifest], ReadinessReport]


def real_cutover_environment(*, arranging_uid: int) -> CutoverEnvironment:
    """Wire the REAL four phases.

    `arranging_uid` is explicit and undefaulted: SWEEP removes files and signals
    processes as this identity, and inheriting that silently is exactly what §3 P9
    forbids. Constructing this environment performs no phase — only `arrange_cutover`
    does, and only when called.
    """
    from .arrange_static import arrange_static, real_static_environment
    from .arrange_sweep import arrange_sweep, real_sweep_environment
    from .arrange_verify import arrange_verify, real_verify_environment
    from .artifact_staging import ArtifactStager

    def _sweep(manifest: ArrangeManifest) -> SweepReport:
        return arrange_sweep(
            manifest, real_sweep_environment(), arranging_uid=arranging_uid
        )

    def _static(manifest: ArrangeManifest) -> StaticReport:
        return arrange_static(manifest, real_static_environment())

    def _stage(manifest: ArrangeManifest) -> Sequence[StaticFailure]:
        stager = ArtifactStager(manifest=manifest)
        stager.stage_all()
        return stager.assert_postconditions()

    def _verify(manifest: ArrangeManifest) -> ReadinessReport:
        return arrange_verify(manifest, real_verify_environment())

    return CutoverEnvironment(
        sweep=_sweep, static=_static, stage=_stage, verify=_verify
    )


def arrange_cutover(
    manifest: ArrangeManifest,
    env: CutoverEnvironment,
) -> CutoverReport:
    """Run the whole arrange chain over `manifest` and report the entry condition.

    `env` is REQUIRED and has no default, for the same reason no field on
    `CutoverEnvironment` does: this composition sweeps, stages, probes and reads as
    another identity, and a caller that silently inherited a real environment would be
    mutating a machine it never decided to touch.

    A failing phase STOPS the chain — staging on top of a manifest STATIC refused, or
    verifying a tree STAGE could not finish, both report against bytes nobody stands
    behind. Every phase that ran is still reported in full, and every phase that did
    not is recorded as `not-reached` rather than omitted: an operator must be able to
    tell "this passed" from "this was never attempted".

    Raising is not a failure channel. A phase that raises is converted into a named
    `failed` outcome, so this function has exactly ONE outcome shape and a caller never
    has to handle both a report and an exception to learn the same fact.
    """
    outcomes: List[PhaseOutcome] = []

    # Each step is spelled out rather than driven from a heterogeneous table: the four
    # phases return four different report types, and a table would erase that into a
    # union the reader (and the type checker) has to re-narrow at every use.
    steps: Sequence[Callable[[], PhaseOutcome]] = (
        lambda: _interpret_sweep(PHASE_SWEEP, env.sweep(manifest)),
        lambda: _interpret_static(PHASE_STATIC, env.static(manifest)),
        lambda: _interpret_stage(PHASE_STAGE, env.stage(manifest)),
        lambda: _interpret_verify(PHASE_VERIFY, env.verify(manifest)),
    )

    stopped = False
    for name, step in zip(CUTOVER_PHASES, steps):
        if stopped:
            outcomes.append(
                PhaseOutcome(
                    phase=name,
                    outcome=OUTCOME_NOT_REACHED,
                    detail=(
                        "not attempted: an earlier phase failed, and running this one "
                        "would report against state nobody stands behind"
                    ),
                )
            )
            continue
        try:
            outcome = step()
        except Exception as exc:  # noqa: BLE001 — a raising phase is a named failure
            outcomes.append(
                PhaseOutcome(
                    phase=name,
                    outcome=OUTCOME_FAILED,
                    detail=f"{type(exc).__name__}: {exc}",
                )
            )
            stopped = True
            continue
        outcomes.append(outcome)
        if not outcome.ok:
            stopped = True

    return CutoverReport(
        ready=all(o.ok for o in outcomes),
        phases=tuple(outcomes),
        clients=tuple(manifest.actors),
        run_id=manifest.run_id,
    )


# --------------------------------------------------------------------------- #
# Per-phase interpretation. Each keeps the phase's own report intact and adds only
# the pass/fail reading the chain needs — no phase's verdict is recomputed here.
# --------------------------------------------------------------------------- #

def _interpret_sweep(name: str, report: SweepReport) -> PhaseOutcome:
    unresolved = report.unresolved
    if report.ok:
        removed = sum(1 for a in report.actions if a.outcome == "removed")
        detail = (
            f"{removed} item(s) removed, {len(report.actions)} reconciliation(s); "
            "no prior-run residue survives into this run"
        )
    else:
        detail = (
            f"{len(unresolved)} item(s) could not be reconciled to declared-absent; "
            "arranging on top of residue this run cannot account for is refused"
        )
    return PhaseOutcome(
        phase=name,
        outcome=OUTCOME_PASSED if report.ok else OUTCOME_FAILED,
        detail=detail,
        report=report,
    )


def _interpret_static(name: str, report: StaticReport) -> PhaseOutcome:
    if report.ok:
        detail = (
            f"{len(report.checked_preconditions)} precondition(s) passed over "
            f"{len(report.checked_clients)} client(s)"
        )
    else:
        detail = f"{len(report.failures)} precondition failure(s)"
    return PhaseOutcome(
        phase=name,
        outcome=OUTCOME_PASSED if report.ok else OUTCOME_FAILED,
        detail=detail,
        report=report,
        failures=tuple(report.failures),
    )


def _interpret_stage(name: str, failures: Sequence[StaticFailure]) -> PhaseOutcome:
    failures = tuple(failures)
    detail = (
        "every client has every required artifact, hashes re-read and matched"
        if not failures
        else f"{len(failures)} staging postcondition failure(s)"
    )
    return PhaseOutcome(
        phase=name,
        outcome=OUTCOME_PASSED if not failures else OUTCOME_FAILED,
        detail=detail,
        failures=failures,
    )


def _interpret_verify(name: str, report: ReadinessReport) -> PhaseOutcome:
    if report.ok:
        # Name the EVIDENCE, not just the verdict. A readiness report that cannot tell
        # an operator which kind of evidence it holds is the "arranged, probably" this
        # phase exists to abolish (§4.1) — so the summary line carries it too, and a
        # staged-delivery V3 is never summarised as if a launched client had proven it.
        methods = sorted(
            {
                criterion.method
                for client in report.clients
                for criterion in client.criteria
            }
        )
        detail = (
            f"{len(report.clients)} client(s) READY; evidence: {', '.join(methods)}"
        )
    else:
        not_ready = [c.actor for c in report.clients if not c.ready]
        detail = (
            f"{len(not_ready)} client(s) NOT READY: {', '.join(not_ready)}"
            if not_ready
            else "no client was verified"
        )
    return PhaseOutcome(
        phase=name,
        outcome=OUTCOME_PASSED if report.ok else OUTCOME_FAILED,
        detail=detail,
        report=report,
        failures=tuple(report.failures),
    )
