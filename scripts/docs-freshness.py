#!/usr/bin/env python3
"""
docs-freshness — PROPOSED staleness detector (ADR-0007 §4).

⚠️  STATUS: PROPOSAL, NOT YET WIRED INTO CI. This script implements the
    git-aware freshness check proposed in docs/decisions/0007-docs-evolution-
    strategy.md §4. It is intentionally NOT called from .github/workflows/docs.yml
    yet — per ADR-0007's sequencing, the docs machinery lands only AFTER Daniel
    ratifies the schema. Until then this is a reviewable, runnable artifact:
    run it by hand (`python3 scripts/docs-freshness.py`) to see what it would flag.

What it checks (two staleness signals docs-lint.py does NOT cover):

  1. STAMP-LAG — a content doc whose frontmatter `last_reviewed:` (or, if absent,
     `last_updated:`) is OLDER than the file's last git-commit date. This catches
     the "body edited, date not bumped" class — e.g. PIECES_AND_CRAFTABLES.md
     carried last_updated:2026-06-03 while its body was edited 2026-06-17.

  2. REVIEW-DUE (soft) — a `living`/`current` doc not reviewed in > THRESHOLD_DAYS.
     Surfaces a review queue. NEVER a hard failure; NEVER auto-cuts. Daniel gates
     every deletion.

Exit code: 0 always in --soft mode (default — advisory). With --strict, exits
non-zero on any STAMP-LAG (the hard, mechanical drift) but never on REVIEW-DUE.
A future CI wiring (post-ratification) would call `--strict` so stamp-lag fails
the build while review-due stays advisory.
"""
from __future__ import annotations
import os, re, sys, subprocess, datetime, argparse

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(REPO, "docs")
THRESHOLD_DAYS = 30
STATUS_EXEMPT = {"README.md", "index.md", "TEMPLATE.md"}
# Lifecycle tiers that are intentionally frozen — never review-due.
FROZEN_STATUS = {"historical", "superseded", "template", "accepted", "idea"}


def rel(p: str) -> str:
    return os.path.relpath(p, REPO)


def read(p: str) -> str:
    with open(p, encoding="utf-8") as fh:
        return fh.read()


def frontmatter(text: str) -> dict | None:
    if not text.startswith("---"):
        return None
    end = text.find("\n---", 3)
    if end == -1:
        return None
    fm = {}
    for line in text[3:end].splitlines():
        if ":" in line and not line.startswith(" "):
            k, v = line.split(":", 1)
            fm[k.strip()] = v.strip()
    return fm


def parse_date(val: str | None) -> datetime.date | None:
    if not val:
        return None
    m = re.search(r"(\d{4})-(\d{2})-(\d{2})", val)
    if not m:
        return None
    try:
        return datetime.date(int(m[1]), int(m[2]), int(m[3]))
    except ValueError:
        return None


def git_last_commit_date(path: str) -> datetime.date | None:
    try:
        out = subprocess.run(
            ["git", "-C", REPO, "log", "-1", "--format=%cs", "--", path],
            capture_output=True, text=True, check=True,
        ).stdout.strip()
        return parse_date(out)
    except (subprocess.CalledProcessError, FileNotFoundError):
        return None


def status_token(fm: dict | None) -> str:
    if not fm or "status" not in fm:
        return ""
    raw = fm["status"]
    return raw.split()[0].rstrip("—-").strip().lower() if raw else ""


def main() -> int:
    ap = argparse.ArgumentParser(description="Proposed docs staleness detector (ADR-0007 §4).")
    ap.add_argument("--strict", action="store_true",
                    help="exit non-zero on STAMP-LAG (mechanical drift). review-due stays advisory.")
    args = ap.parse_args()

    today = datetime.date.today()
    stamp_lag: list[str] = []
    review_due: list[str] = []
    no_review_field: list[str] = []

    for dirpath, _, filenames in os.walk(DOCS):
        for f in filenames:
            if not f.endswith(".md") or f in STATUS_EXEMPT:
                continue
            path = os.path.join(dirpath, f)
            fm = frontmatter(read(path))
            reviewed = parse_date(fm.get("last_reviewed") if fm else None)
            updated = parse_date(fm.get("last_updated") if fm else None)
            stamp = reviewed or updated
            committed = git_last_commit_date(path)
            status = status_token(fm)

            # 1. STAMP-LAG — date stamp older than git reality.
            #    Only meaningful for NON-FROZEN docs: a `historical`/`superseded`/
            #    `idea` doc is intentionally not tracking freshness, so a later
            #    mechanical touch (link fix, tree-wide sweep) legitimately
            #    post-dates its stamp. Flagging those is noise. If a frozen doc
            #    gets a *substantive* edit, the fix is to change its status, not
            #    bump a date — a separate concern.
            if stamp and committed and stamp < committed and status not in FROZEN_STATUS:
                stamp_lag.append(
                    f"{rel(path)} — stamp {stamp} < last commit {committed} "
                    f"({'last_reviewed' if reviewed else 'last_updated'} not bumped)")

            # 2. REVIEW-DUE (soft) — living/current docs gone stale.
            if status in ("living", "current"):
                if reviewed is None:
                    no_review_field.append(f"{rel(path)} — no last_reviewed: field")
                elif (today - reviewed).days > THRESHOLD_DAYS:
                    review_due.append(
                        f"{rel(path)} — last_reviewed {reviewed} "
                        f"({(today - reviewed).days}d ago, status:{status})")

    print("docs-freshness (PROPOSED — ADR-0007 §4, not yet CI-wired)\n")
    print(f"  STAMP-LAG (mechanical drift, {len(stamp_lag)}):")
    for s in sorted(stamp_lag):
        print("    ✗ " + s)
    if not stamp_lag:
        print("    (none — all date stamps >= git reality)")

    print(f"\n  REVIEW-DUE (soft, >{THRESHOLD_DAYS}d, {len(review_due)}):")
    for s in sorted(review_due):
        print("    • " + s)
    if not review_due:
        print("    (none)")

    print(f"\n  MISSING last_reviewed: on living/current docs ({len(no_review_field)}):")
    for s in sorted(no_review_field):
        print("    · " + s)
    if not no_review_field:
        print("    (none)")

    if args.strict and stamp_lag:
        print(f"\ndocs-freshness: {len(stamp_lag)} stamp-lag drift(s) — FAIL (--strict).")
        return 1
    print("\ndocs-freshness: advisory pass (review-due/missing never fail the build).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
