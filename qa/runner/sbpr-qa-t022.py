#!/usr/bin/env python3
"""
sbpr-qa-t022 — external QA runner for the T022 Masterwork joined-client scenario.

ADR-0009 §1/§6: the runner is an ENGINE-FREE Python program and, once complete,
the *sole* scenario state machine and the *sole* PASS/FAIL composer — the helper
emits dumb primitive facts, the runner is the brain. It cannot emit PASS without
all four named T022 acceptance tests asserted and cleanup confirmed.

M0 SCOPE (this file): a SKELETON ONLY. It parses arguments, supports `--dry-run`,
and prints a disabled-notice. It performs NO game I/O, NO network I/O, NO file
mutation, mints no nonce, signs no request, and drives nothing. The scenario state
machine, capability-manifest minting, per-request HMAC, receipt correlation, and
evidence composition all land in M5 (the runner card). Until then this entry point
exists only so the skeleton, packaging, and docs are in place.
"""
from __future__ import annotations

import argparse
import sys


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sbpr-qa-t022",
        description=(
            "External QA runner for the T022 Masterwork joined-client scenario "
            "(ADR-0009). M0 skeleton: no game/network/file side effects."
        ),
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help=(
            "Describe what the runner WOULD do without contacting any game, "
            "server, socket, or file. In M0 this is the only supported mode; "
            "live execution is not implemented until M5 (and a live run is the "
            "separate, operator-authorized M6 card)."
        ),
    )
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)

    print("sbpr-qa-t022: ADR-0009 M0 skeleton — DISABLED.")
    print(
        "  The scenario state machine + PASS/FAIL composer are not implemented "
        "in M0."
    )
    print(
        "  This runner performs no game I/O, no network I/O, and no file "
        "mutation."
    )
    if args.dry_run:
        print("  --dry-run: nothing to simulate yet; the verb catalog lands in M1+.")
    else:
        print("  Live execution is not implemented until M5. Re-run with --dry-run.")

    # Fail-closed: the runner never reports success in M0. A caller/CI that
    # mistakes this skeleton for a working runner gets a non-zero exit, not a
    # silent green.
    return 0 if args.dry_run else 2


if __name__ == "__main__":
    sys.exit(main())
