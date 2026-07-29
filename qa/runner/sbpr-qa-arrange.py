#!/usr/bin/env python3
"""
sbpr-qa-arrange — the arrange phase entrypoint (T022 ARRANGE spec).

THIS TICKET (#450) implements `--check` ONLY: the STATIC phase. It validates the
declarative per-client manifest and every precondition that can be established
without starting a process — in well under a second — and reports each failure with
its precondition, its client, and expected-vs-actual.

The later phases (SWEEP #451, PROVISION #452-#454, VERIFY #455, LAUNCH #456, the
runner cutover #457) are separately owned and are NOT implemented here. Invoking
this program can therefore never start a game, mutate a file, or contact a server —
`--check` reads the manifest and the artifact bytes it names, and nothing else.

Exit codes:
  0  every static precondition passed
  1  at least one precondition failed (the report names each one)
  2  the manifest could not be read at all
"""
from __future__ import annotations

import argparse
import json
import os
import sys

_RUNNER_DIR = os.path.dirname(os.path.abspath(__file__))
if _RUNNER_DIR not in sys.path:
    sys.path.insert(0, _RUNNER_DIR)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sbpr-qa-arrange",
        description=(
            "Arrange-phase entrypoint for the T022 live QA harness. --check runs the "
            "STATIC phase: it validates the per-client manifest and every cheap "
            "precondition without starting any process."
        ),
    )
    parser.add_argument(
        "--manifest",
        metavar="PATH",
        required=True,
        help="Path to the arrange manifest JSON (kind sbpr-qa-arrange-manifest).",
    )
    parser.add_argument(
        "--check",
        action="store_true",
        help=(
            "Run the STATIC checks and report. Starts no process, launches no client, "
            "writes nothing. This is the only mode implemented on this card; the "
            "sweep/provision/verify/launch phases are separately owned."
        ),
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit the static report as machine-readable JSON instead of text.",
    )
    return parser


def main(argv: "list[str] | None" = None) -> int:
    args = build_parser().parse_args(argv)

    from runner_core.arrange_static import arrange_static

    if not args.check:
        print("sbpr-qa-arrange: no mode selected.")
        print("  Use --check to run the STATIC phase (no process is started).")
        print("  The sweep/provision/verify/launch phases are not implemented here.")
        return 2

    try:
        with open(args.manifest, "r", encoding="utf-8") as fh:
            raw = json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"sbpr-qa-arrange: cannot read manifest {args.manifest!r}: {exc}")
        return 2

    report = arrange_static(raw)

    if args.json:
        print(json.dumps(report.as_dict(), indent=2, sort_keys=True))
    else:
        print(report.render())
    return 0 if report.ok else 1


if __name__ == "__main__":
    sys.exit(main())
