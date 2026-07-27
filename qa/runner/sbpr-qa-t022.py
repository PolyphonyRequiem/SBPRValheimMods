#!/usr/bin/env python3
"""
sbpr-qa-t022 — external QA runner for the T022 Masterwork joined-client scenario.

ADR-0009 §1/§6: the runner is an ENGINE-FREE Python program and the *sole* scenario
state machine + the *sole* PASS/FAIL composer. The BepInEx helper emits dumb
primitive facts; the runner correlates them and decides. It cannot emit PASS without
all four named T022 acceptance tests (ISSUE / UPGRADE / TRANSFER / TAMPER) asserted
AND cleanup confirmed AND the exclusive lane lease held AND the artifact pins
verified.

MATURITY (M6-EXEC, CAPABILITY NOT PERFORMED): live execution is now *implemented* and
opt-in behind `--live`. Merging it makes a live in-world run POSSIBLE; it does not
perform one. The live path is fail-closed: `--live` runs ONLY when an explicit
`--live` flag AND a disposable-lane sentinel (`--lane-sentinel`) AND verified overlay
pins (`--overlay-manifest`) are all present and valid — otherwise it refuses and says
why. `--dry-run` remains the default and fully working: it replays a scripted scenario
through the real orchestrator against the deterministic in-process `FakeTransport` with
NO game I/O, NO network I/O, and NO file mutation. Actually driving a two-client cold
run in-world is a separate operator-authorized action, never triggered by this file's
import or by the test suite.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

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


def main(argv: list[str] | None = None) -> int:
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
        return _run_live_preflight(args)

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


def _run_live_preflight(args: argparse.Namespace) -> int:
    """Fail-closed live-mode gate (M6-EXEC).

    Runs the explicit-opt-in + disposable-sentinel + verified-overlay-pins checks.
    On success it reports that the live path is UNLOCKED — it does NOT launch a lane,
    a client, or a game. Actually driving an in-world run is a separate operator step
    that composes the merged live transport + operator drivers under authorization;
    this runner refuses to conflate "capability verified" with "run performed".
    """
    from runner_core.live_preflight import evaluate_live_preflight

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
    # digest that does not fold is caught. A production integration can extend
    # `observed` with freshly recomputed tree hashes; here we verify self-consistency
    # plus the independently-supplied sentinel.
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
    print("  The live execution path is UNLOCKED but NOT executed here: launching the")
    print("  disposable lane, the two licensed clients, seeding entitlement, and driving")
    print("  the four T022 legs in-world is a separate operator-authorized step. This")
    print("  runner verified the capability; it did not perform a live qualification.")
    return 0


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
