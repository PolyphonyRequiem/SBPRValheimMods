#!/usr/bin/env python3
"""
sbpr-qa-t022 — external QA runner for the T022 Masterwork joined-client scenario.

ADR-0009 §1/§6: the runner is an ENGINE-FREE Python program and the *sole* scenario
state machine + the *sole* PASS/FAIL composer. The BepInEx helper emits dumb
primitive facts; the runner correlates them and decides. It cannot emit PASS without
all four named T022 acceptance tests (ISSUE / UPGRADE / TRANSFER / TAMPER) asserted
AND cleanup confirmed AND the exclusive lane lease held AND the artifact pins
verified.

MATURITY (M5, DRY-RUN ONLY): this runner wraps the adopted transport-neutral FSM
(`qa/runner/fsm/`) in the full M5 operational envelope — exclusive lane lease,
immutable 6-part artifact-pin manifest, per-phase timeout budgets, correlated
evidence composition, and final verdict authority (`qa/runner/runner_core/`). It is
still exercised ONLY against the deterministic in-process `FakeTransport`: it
performs NO game I/O, NO network I/O, and NO file mutation, and mints/signs nothing
against a live world. A *live* two-client cold run is the separate, operator-
authorized **M6** card and is never run here. `--dry-run` replays a scripted
scenario through the real orchestrator so every path (success + every failure mode)
is observable without a live world.
"""
from __future__ import annotations

import argparse
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
            "or file is contacted. This is the ONLY supported execution mode; a live "
            "run is the separate operator-authorized M6 card, never run here."
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

    if not args.dry_run:
        print("sbpr-qa-t022: live execution is NOT implemented (M6, operator-only).")
        print("  Re-run with --dry-run to replay a scripted scenario. No game, "
              "network, or file I/O is ever performed by this runner.")
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


if __name__ == "__main__":
    sys.exit(main())
