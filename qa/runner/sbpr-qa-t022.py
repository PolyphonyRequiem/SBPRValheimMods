#!/usr/bin/env python3
"""
sbpr-qa-t022 — external QA runner for the T022 Masterwork joined-client scenario.

ADR-0009 §1/§6: the runner is an ENGINE-FREE Python program and the *sole* scenario
state machine + the *sole* PASS/FAIL composer. The BepInEx helper emits dumb
primitive facts; the runner correlates them and decides. It cannot emit PASS without
all four named T022 acceptance tests (ISSUE / UPGRADE / TRANSFER / TAMPER) asserted
AND cleanup confirmed AND the exclusive lane lease held AND the artifact pins
verified.

MATURITY (M6-COMPOSE, CAPABILITY NOT PERFORMED): live execution is now *composed and
invoked* — opt-in behind `--live`. On an UNLOCK the runner constructs the live
transport + the four operator drivers and DRIVES a qualification run through the sole-
authority orchestrator (it no longer prints "unlocked" and returns). Merging it makes a
live in-world run EXECUTABLE; it does not, on this card, run one in-world. The live path
stays fail-closed: `--live` proceeds ONLY when an explicit `--live` flag AND a
disposable-lane sentinel (`--lane-sentinel`) AND verified overlay pins
(`--overlay-manifest`) are all present and valid — otherwise it refuses and says why.
The default CLI executor wires the REAL subprocess/socket/file operator callables; with
no game/product present those fail closed (nothing in-world is fabricated). `--dry-run`
remains the default and fully working: it replays a scripted scenario through the real
orchestrator against the deterministic in-process `FakeTransport` with NO game I/O, NO
network I/O, and NO file mutation. Actually driving a two-client cold run in-world is a
separate operator-authorized action, never triggered by this file's import or by the
test suite.

ARRANGE (M6-CUTOVER / #457 migrate): with `--arrange-manifest`, `--live` arranges the
run through the SINGLE arrange authority before anything launches — the whole chain
(SWEEP -> STATIC -> STAGE -> VERIFY, `runner_core/arrange_cutover.py`) must reach READY
or the run is REFUSED. Previously the runner arranged itself from the run DESCRIPTOR
while the arrange MANIFEST described the same run independently: two descriptions of
one run, either of which can be right while the other is wrong, with a client at a menu
as the only symptom. The gate is fail-closed and is NOT a retry — arranging is a
precondition, and re-launching to paper over a missing one is forbidden. READY is
pre-launch evidence, never proof a client joined: V3 holds by `staged-delivery` here,
because no process yet exists whose real kernel argv could be read.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
from typing import Optional

# Make the sibling engine-free packages (fsm, runner_core) importable whether the
# runner is invoked as `python3 qa/runner/sbpr-qa-t022.py` or from elsewhere.
_RUNNER_DIR = os.path.dirname(os.path.abspath(__file__))
if _RUNNER_DIR not in sys.path:
    sys.path.insert(0, _RUNNER_DIR)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sbpr-qa-t022",
        description=(
            "External QA runner for the T022 Masterwork joined-client scenario "
            "(ADR-0009). Engine-free; the sole scenario state machine + PASS/FAIL "
            "composer. Dry-run only — no game/network/file side effects."
        ),
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help=(
            "Replay a scripted T022 scenario through the real orchestrator against "
            "the deterministic in-process fake transport. No game, server, socket, "
            "or file is contacted. This is the DEFAULT execution mode; a live run "
            "requires the explicit fail-closed --live path below."
        ),
    )
    parser.add_argument(
        "--live",
        action="store_true",
        help=(
            "Opt in to LIVE execution (M6-EXEC). Fail-closed: requires --lane-sentinel "
            "(a disposable-lane sentinel carrying the hard production deny list) AND "
            "--overlay-manifest (a verified overlay pin manifest). Absent either, or on "
            "any drift, live execution is REFUSED. Building this capability does not "
            "perform an in-world run — actually driving one is a separate operator step."
        ),
    )
    parser.add_argument(
        "--lane-sentinel",
        metavar="PATH",
        help="Path to the disposable-lane sentinel JSON (lane_sentinel.json). Required with --live.",
    )
    parser.add_argument(
        "--overlay-manifest",
        metavar="PATH",
        help="Path to the overlay pin manifest JSON (manifest.json). Required with --live.",
    )
    parser.add_argument(
        "--run-descriptor",
        metavar="PATH",
        help=(
            "Path to the operator live-run descriptor JSON (lane/clients/wire/pins/server "
            "binaries). Required with --live to actually EXECUTE: after the fail-closed "
            "preflight UNLOCKS, the runner builds the live transport + the four operator "
            "drivers from this descriptor and drives a qualification run. Omit it and --live "
            "runs the preflight only (capability check), refusing to execute."
        ),
    )
    parser.add_argument(
        "--arrange-manifest",
        metavar="PATH",
        help=(
            "Path to the arrange manifest JSON (kind sbpr-qa-arrange-manifest). With "
            "--live the runner ARRANGES THROUGH IT (#457 migrate): the whole arrange "
            "chain — SWEEP, STATIC, STAGE, VERIFY — runs to READY before any client "
            "launches, and a run that is not READY is refused. This is the single "
            "arrange authority of T022-ARRANGE-SPEC §3 P1; the descriptor describes "
            "the RUN, the manifest describes the ARRANGEMENT."
        ),
    )
    parser.add_argument(
        "--scenario",
        default="success",
        help=(
            "Which dry-run scenario to replay (default: success). Use --list-scenarios "
            "to see them all. Only 'success' yields PASS; every other path FAILs "
            "(the no-false-PASS contract)."
        ),
    )
    parser.add_argument(
        "--list-scenarios",
        action="store_true",
        help="List the available dry-run scenarios and exit.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit the correlated evidence document as JSON (byte-stable).",
    )
    return parser


def main(argv: list[str] | None = None, *, live_runner=None, steam_probe=None) -> int:
    args = build_parser().parse_args(argv)

    # Import here so the module import graph stays clean and errors are actionable.
    from runner_core.simulation import PASS_SCENARIOS, SCENARIOS

    if args.list_scenarios:
        print("sbpr-qa-t022 dry-run scenarios:")
        for name in SCENARIOS:
            marker = "PASS" if name in PASS_SCENARIOS else "FAIL"
            print(f"  {name:<20} expected verdict: {marker}")
        return 0

    if args.live:
        return _run_live(args, live_runner=live_runner, steam_probe=steam_probe)

    if not args.dry_run:
        print("sbpr-qa-t022: no execution mode selected.")
        print("  Use --dry-run (default, deterministic replay; no game/network/file I/O),")
        print("  or --live with --lane-sentinel and --overlay-manifest for the fail-closed")
        print("  live path. Live execution refuses unless every precondition holds.")
        return 2

    if args.scenario not in SCENARIOS:
        print(f"sbpr-qa-t022: unknown scenario {args.scenario!r}.")
        print(f"  Known: {', '.join(SCENARIOS)}")
        return 2

    orchestrator = SCENARIOS[args.scenario]()
    result = orchestrator.run()

    if args.json:
        print(result.evidence.to_json())
    else:
        ev = result.evidence
        print(f"sbpr-qa-t022 [DRY-RUN] scenario={args.scenario}")
        print(f"  verdict: {result.verdict}")
        print(f"  legs:    {ev.legs}")
        print(f"  lease_held={ev.lease_held} pins_verified={ev.pins_verified} "
              f"cleanup_confirmed={ev.cleanup_confirmed} "
              f"receipts_correlated={ev.receipts_correlated}")
        if ev.failure_reason:
            print(f"  failure: [{ev.failure_kind}] {ev.failure_reason}")
        print(f"  maturity: {ev.maturity}")

    # Exit code encodes the verdict for CI: 0 = PASS, 1 = FAIL. A FAIL is the
    # EXPECTED, correct outcome for every non-success scenario, so callers that
    # replay a failure path should not treat exit 1 as a runner defect.
    return 0 if result.passed else 1


def _run_live(args: argparse.Namespace, *, live_runner=None, steam_probe=None) -> int:
    """Fail-closed live-mode gate + EXECUTION (M6-COMPOSE).

    Runs the explicit-opt-in + disposable-sentinel + verified-overlay-pins preflight
    EXACTLY as reviewed. On REFUSE it says why and stops (nothing launched). On UNLOCK
    it no longer prints "unlocked" and returns — it composes the live transport + the
    four operator drivers from the operator run descriptor and DRIVES a qualification
    run through the sole-authority orchestrator, then reports the composed verdict and
    that teardown ran. `live_runner` is an injectable seam `(descriptor_path) -> report`
    so the acceptance suite drives the whole path against stub operator callables with
    no real game; the default wires the REAL subprocess/socket/file operator env.
    """
    from runner_core.live_preflight import evaluate_live_preflight
    from runner_core.steam_preflight import SteamNotReady, require_steam_running

    if not args.lane_sentinel or not args.overlay_manifest:
        print("sbpr-qa-t022: --live REFUSED — missing required inputs.")
        print("  --live requires BOTH --lane-sentinel <path> AND --overlay-manifest <path>.")
        return 2

    sentinel = _load_json(args.lane_sentinel, "lane sentinel")
    manifest = _load_json(args.overlay_manifest, "overlay manifest")
    if sentinel is None or manifest is None:
        return 2

    # Observed part hashes: the manifest records the pinned per-part hashes; the gate
    # re-folds them to the recorded overlay_digest and cross-checks the sentinel pin
    # against the sentinel file actually supplied, so a swapped/tampered sentinel or a
    # digest that does not fold is caught.
    recorded_parts = manifest.get("parts") if isinstance(manifest, dict) else None
    observed = dict(recorded_parts) if isinstance(recorded_parts, dict) else {}
    observed["lane_sentinel"] = _sha256_file(args.lane_sentinel)

    result = evaluate_live_preflight(
        live_requested=bool(args.live),
        sentinel=sentinel,
        manifest=manifest,
        observed_part_hashes=observed,
    )
    if not result.ok:
        print("sbpr-qa-t022: --live REFUSED (fail-closed).")
        print(f"  reason: {result.reason}")
        return 2

    print("sbpr-qa-t022 [LIVE-PREFLIGHT] all fail-closed preconditions PASSED.")
    print(f"  lane: {result.sentinel_lane}  overlay_digest: {result.overlay_digest}")

    # Without an operator run descriptor there is nothing concrete to launch; the
    # preflight verified the capability. This is a capability check, NOT the reviewed
    # success path — surface it explicitly and refuse to fabricate an execution.
    if not args.run_descriptor:
        print("sbpr-qa-t022: --live preflight UNLOCKED but no --run-descriptor supplied.")
        print("  Supply --run-descriptor <path> (lane/clients/wire/pins/server binaries)")
        print("  to EXECUTE the qualification run. Refusing to execute without one.")
        return 2

    # UNLOCKED with a descriptor: EXECUTE. Compose the live transport + the four
    # operator drivers and drive the run through the sole-authority orchestrator.
    #
    # STEAM PRECONDITION (M6-STEAMGATE): the client cannot boot without a RUNNING
    # Steam owned by the user GABS launches it as — with none, it crashes ~6s in with
    # "Steamworks is not initialized" before the scene activates. Verify it here,
    # alongside the other fail-closed preconditions, BEFORE composing drivers or
    # launching anything. The readiness predicate lives in scripts/ensure-steam.sh
    # (live process AND steam.pipe AND a live pidfile) and is invoked via `--check`,
    # so a stale pipe with no process behind it does NOT satisfy it. The target user
    # comes from the descriptor's optional `steam_user`; absent it, the current user
    # (whom the poly GABS daemon launches the primary client as) is checked.
    steam_user = _descriptor_steam_user(args.run_descriptor)
    steam_check = steam_probe if steam_probe is not None else require_steam_running
    try:
        steam = steam_check(steam_user)
    except SteamNotReady as exc:
        print("sbpr-qa-t022: --live REFUSED — Steam precondition failed (fail-closed).")
        print(f"  {exc}")
        return 2
    print(f"sbpr-qa-t022 [LIVE-PREFLIGHT] {steam.message}")

    # ARRANGE (#457 migrate). The run is arranged by the SINGLE arrange authority
    # (§3 P1) before anything launches: SWEEP -> STATIC -> STAGE -> VERIFY, to READY.
    #
    # Why this gate exists at all. The runner previously arranged itself from the run
    # DESCRIPTOR — deriving credentials, sidecars and launch env inside
    # `build_live_run` — while the arrange MANIFEST and its four phases described the
    # same run independently. Two descriptions of one run is the "four mechanisms that
    # do not know about each other" defect (§0) reproduced inside the runner: either
    # can be right while the other is wrong, and the only symptom is a client sitting
    # at a menu, ~10 minutes per diagnosis cycle.
    #
    # It is fail-closed and it is NOT a retry. A run that does not reach READY is
    # refused, naming the phase and the precondition — arranging is a precondition, and
    # re-running a launch to paper over a missing one is exactly what §6 forbids.
    #
    # Opt-in during the migrate step: with no --arrange-manifest the descriptor-derived
    # path still runs, so this commit does not require every caller to move at once.
    # That is the expand-contract discipline, not a permanent second path — the
    # contract step removes the alternative.
    if args.arrange_manifest:
        code = _arrange_before_launch(args.arrange_manifest)
        if code != 0:
            return code

    runner = live_runner if live_runner is not None else _default_live_runner
    print("sbpr-qa-t022 [LIVE-EXECUTE] preflight UNLOCKED — composing drivers and driving the run.")
    report = runner(args.run_descriptor)

    verdict = report.verdict
    if verdict is None:
        print("sbpr-qa-t022 [LIVE-EXECUTE] run did not compose a verdict (driving failed before evidence).")
        if getattr(report, "drive_error", None):
            print(f"  error: {report.drive_error}")
        _print_teardown(report)
        return 1

    ev = verdict.evidence
    print(f"sbpr-qa-t022 [LIVE-EXECUTE] verdict: {verdict.verdict}")
    print(f"  legs:    {ev.legs}")
    print(f"  lease_held={ev.lease_held} pins_verified={ev.pins_verified} "
          f"cleanup_confirmed={ev.cleanup_confirmed} "
          f"receipts_correlated={ev.receipts_correlated}")
    if ev.failure_reason:
        print(f"  failure: [{ev.failure_kind}] {ev.failure_reason}")
    _print_teardown(report)
    return 0 if verdict.passed else 1


def _arrange_before_launch(manifest_path: str) -> int:
    """Arrange the run through the single authority. 0 = READY, non-zero = refused.

    Runs the whole chain (SWEEP -> STATIC -> STAGE -> VERIFY) and returns 0 only when
    every phase passed. On anything else it prints the phase-by-phase report — which
    names the failing precondition, the client, and expected-vs-actual — and refuses,
    so a launch never proceeds on an arrangement nobody stands behind.

    READY IS NOT PROOF OF A JOIN. Because arrange precedes launch, VERIFY's V3
    criterion is satisfied by the `staged-delivery` rung, never `live-argv`: no process
    exists yet whose real kernel argv could be read. The report carries that
    distinction and this function does not collapse it.
    """
    import os as _os

    from runner_core.arrange_cutover import arrange_cutover, real_cutover_environment
    from runner_core.arrange_manifest import ArrangeManifest, ArrangeManifestError

    raw = _load_json(manifest_path, "arrange manifest")
    if raw is None:
        return 2
    try:
        manifest = ArrangeManifest.parse(raw)
    except ArrangeManifestError as exc:
        print(f"sbpr-qa-t022: --live REFUSED — arrange manifest is not well-formed: {exc}")
        return 2

    print("sbpr-qa-t022 [LIVE-ARRANGE] arranging through the single authority (#457).")
    # Explicit, not defaulted (§3 P9): the chain sweeps files, signals processes,
    # writes a filesystem and reads another identity's credentials. Deciding to do that
    # on THIS machine as THIS identity belongs at the construction site.
    report = arrange_cutover(
        manifest, real_cutover_environment(arranging_uid=_os.geteuid())
    )
    print(report.render())
    if not report.ready:
        print("sbpr-qa-t022: --live REFUSED — the run did not reach READY (fail-closed).")
        print("  Arranging is a PRECONDITION. Fix what the report names above; a retry")
        print("  would only re-discover the same missing precondition ten minutes later.")
        return 2
    return 0


def _print_teardown(report) -> None:
    if report.teardown_completed:
        print("  teardown: complete (clients, lane, transport, adminlist restored, lease released).")
    else:
        print(f"  teardown: INCOMPLETE — {report.teardown_errors}")


def _default_live_runner(descriptor_path: str):
    """Default live executor: build the plan + REAL operator env and drive the run.

    Wires the concrete subprocess/socket/file operator callables. With no game/product
    present those fail closed (nothing in-world is fabricated); a genuine operator run
    supplies real binaries. This is invoked ONLY from the `--live` execute path with an
    explicit descriptor — never by import or the test suite.
    """
    from runner_core.live_composition import build_live_run, run_live_qualification

    descriptor = _load_json(descriptor_path, "run descriptor")
    if descriptor is None:
        raise SystemExit(2)
    plan, env = build_live_run(descriptor)
    return run_live_qualification(plan, env)


def _descriptor_steam_user(descriptor_path: Optional[str]):
    """Read the optional `steam_user` from the run descriptor (which user's Steam to
    require). Returns None — meaning "check the current user" — when the descriptor is
    absent, unreadable, or does not name one. A malformed descriptor is NOT swallowed
    into a wrong-user check: None yields the current-user default, and the descriptor
    is re-validated in full by `build_live_run` on the execute path.
    """
    if not descriptor_path:
        return None
    descriptor = _load_json(descriptor_path, "run descriptor")
    if not isinstance(descriptor, dict):
        return None
    user = descriptor.get("steam_user")
    return str(user) if user else None


def _load_json(path: str, label: str):
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"sbpr-qa-t022: --live REFUSED — cannot read {label} {path!r}: {exc}")
        return None


def _sha256_file(path: str) -> str:
    import hashlib

    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


if __name__ == "__main__":
    sys.exit(main())
