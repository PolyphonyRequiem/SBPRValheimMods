#!/usr/bin/env python3
"""
sbpr-qa-arrange — the arrange phase entrypoint (T022 ARRANGE spec).

THIS ENTRYPOINT implements two phases:

  `--check`  STATIC — validates the declarative per-client manifest and every
             precondition establishable without starting a process, in well under a
             second, reporting each failure with its precondition, its client, and
             expected-vs-actual. Reads only; writes nothing.

  `--stage`  STAGE  — stages every manifest artifact to EVERY client from the one
             manifest, then reads them all back and asserts hashes. This is the only
             mode that mutates the filesystem. It starts no process and contacts no
             game. `--dry-run` reports exactly what it would do and writes nothing.

STATIC arrived with #450 (manifest + phase), and its guards were hardened by the
merged provisioning issues: #452 (credentials readable by their consuming uid),
#453 (join-target delivery verified at the wrapper), #454 (per-client ports and the
disabled-component proof seam). STAGE is #451.

The remaining phases are separately owned and are NOT implemented here:

    SWEEP     #455  sweep + idempotency
    VERIFY    #456  post-arrange verification + readiness report
    CUTOVER   #457  runner cutover to the new arrange phase (expand-contract)

SWEEP runs BEFORE STAGE in the phase model: clearing prior-run residue first means
staging never writes alongside state it is about to invalidate. Both are idempotent,
so until #455 lands, running `--stage` repeatedly is safe and converges.

Invoking this program can never start a game or contact a server.

See `docs/qa/T022-ARRANGE-SPEC.md` for the phase model these map onto, and
`docs/qa/T022-ARRANGE-STAGING.md` for what STAGE guarantees.

Exit codes:
  0  the selected phase passed
  1  at least one precondition/postcondition failed (the report names each one)
  2  the manifest could not be read at all, or no mode was selected
  3  staging failed and was rolled back; the tree is as it was before
  4  staging failed AND the rollback was incomplete — needs manual reconciliation
"""
from __future__ import annotations

import argparse
import json
import os
import sys

_RUNNER_DIR = os.path.dirname(os.path.abspath(__file__))
if _RUNNER_DIR not in sys.path:
    sys.path.insert(0, _RUNNER_DIR)

EXIT_OK = 0
EXIT_FAILED = 1
EXIT_UNREADABLE = 2
EXIT_ROLLED_BACK = 3
EXIT_ROLLBACK_INCOMPLETE = 4


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sbpr-qa-arrange",
        description=(
            "Arrange-phase entrypoint for the T022 live QA harness. --check runs the "
            "STATIC phase (validates the per-client manifest without starting any "
            "process); --stage runs the STAGE phase (stages every artifact to every "
            "client and asserts the result)."
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
            "writes nothing."
        ),
    )
    parser.add_argument(
        "--stage",
        action="store_true",
        help=(
            "Run the STAGE phase: stage every manifest artifact to every client, then "
            "read them back and assert hashes. Mutates the filesystem; starts no "
            "process. Combine with --dry-run to report without writing."
        ),
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help=(
            "With --stage: resolve and report every intended artifact placement, then "
            "exit without writing anything."
        ),
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit the report as machine-readable JSON instead of text.",
    )
    return parser


def _load_manifest(path: str):
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh), None
    except (OSError, json.JSONDecodeError) as exc:
        return None, f"sbpr-qa-arrange: cannot read manifest {path!r}: {exc}"


def _run_check(raw, as_json: bool) -> int:
    from runner_core.arrange_static import arrange_static

    report = arrange_static(raw)
    if as_json:
        print(json.dumps(report.as_dict(), indent=2, sort_keys=True))
    else:
        print(report.render())
    return EXIT_OK if report.ok else EXIT_FAILED


def _run_stage(raw, *, dry_run: bool, as_json: bool) -> int:
    from runner_core.arrange_manifest import ArrangeManifest, ArrangeManifestError
    from runner_core.artifact_staging import (
        ArtifactStager,
        ArtifactStagingError,
        StagingRollbackError,
        render_plan,
        render_postconditions,
    )

    try:
        manifest = ArrangeManifest.parse(raw)
    except ArrangeManifestError as exc:
        print(f"sbpr-qa-arrange: manifest is not well-formed: {exc}")
        return EXIT_UNREADABLE

    stager = ArtifactStager(manifest=manifest)

    if dry_run:
        try:
            planned = stager.plan()
        except ArtifactStagingError as exc:
            print(str(exc))
            return EXIT_FAILED
        if as_json:
            print(
                json.dumps(
                    {
                        "phase": "stage",
                        "dry_run": True,
                        "placements": [
                            {
                                "client": p.actor,
                                "artifact": p.artifact,
                                "source_path": p.source_path,
                                "dest_path": p.dest_path,
                                "sha256": p.sha256,
                                "action": p.action,
                                "needs_parent": p.needs_parent,
                            }
                            for p in planned
                        ],
                    },
                    indent=2,
                    sort_keys=True,
                )
            )
        else:
            print(render_plan(planned))
        return EXIT_OK

    try:
        staged = stager.stage_all()
    except StagingRollbackError as exc:
        # The tree is in a mixed state. This is the one outcome that needs a human
        # before anything else runs, so it gets its own exit code.
        print(str(exc))
        return EXIT_ROLLBACK_INCOMPLETE
    except ArtifactStagingError as exc:
        print(str(exc))
        return EXIT_ROLLED_BACK

    failures = stager.assert_postconditions()
    actors = manifest.actors

    if as_json:
        print(
            json.dumps(
                {
                    "phase": "stage",
                    "dry_run": False,
                    "ok": not failures,
                    "clients": list(actors),
                    "staged": [
                        {
                            "client": s.actor,
                            "artifact": s.artifact,
                            "dest_path": s.dest_path,
                            "sha256": s.sha256,
                            "action": s.action,
                            "created_parent": s.created_parent,
                        }
                        for s in staged
                    ],
                    "failures": [
                        {
                            "precondition": f.precondition,
                            "client": f.client,
                            "detail": f.detail,
                            "expected": f.expected,
                            "actual": f.actual,
                            "remedy": f.remedy,
                        }
                        for f in failures
                    ],
                },
                indent=2,
                sort_keys=True,
            )
        )
    else:
        print(
            f"arrange STAGE: {len(staged)} artifact placement(s) over "
            f"{len(actors)} client(s)"
        )
        print(render_postconditions(failures, actors))
    return EXIT_OK if not failures else EXIT_FAILED


def main(argv: "list[str] | None" = None) -> int:
    args = build_parser().parse_args(argv)

    if not args.check and not args.stage:
        print("sbpr-qa-arrange: no mode selected.")
        print("  --check  run the STATIC phase (reads only; starts no process).")
        print("  --stage  run the STAGE phase (stages artifacts to every client).")
        print("  The sweep/verify/cutover phases are not implemented here.")
        return EXIT_UNREADABLE

    if args.dry_run and not args.stage:
        print("sbpr-qa-arrange: --dry-run applies to --stage; --check never writes.")
        return EXIT_UNREADABLE

    raw, error = _load_manifest(args.manifest)
    if error is not None:
        print(error)
        return EXIT_UNREADABLE

    if args.check:
        code = _run_check(raw, args.json)
        # STATIC gates STAGE: staging on top of a manifest that failed its
        # preconditions would write bytes the run has already refused to trust.
        if code != EXIT_OK or not args.stage:
            return code

    return _run_stage(raw, dry_run=args.dry_run, as_json=args.json)


if __name__ == "__main__":
    sys.exit(main())
