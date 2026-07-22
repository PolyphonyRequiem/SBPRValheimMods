#!/usr/bin/env python3
"""
prepare-playtest.py — the ONE explicit, dry-run-by-default Prepare Playtest workflow.

Replaces the deleted polling cron jobs (`sbpr-playtest-planner`,
`sbpr-playtest-ready-watch`) with a single human-invoked command that produces an
exact release-candidate identity, validates the scoped playtest manifest/ledger,
generates a correctly-versioned guide, and ends in ONE decision for a human:
approve or reject cutting the proposed tag.

SAFETY (load-bearing)
---------------------
* Dry run is the DEFAULT. With no flags it MUTATES NOTHING: no tag, no release,
  no installer pin, no ledger bump, no guide file on disk. It only reads git +
  the ledger and prints a verdict (and optionally writes an artifact under a
  scratch dir you name).
* It NEVER creates/pushes a tag or GitHub release. Tag creation is a separate,
  explicit, human-authorized `git tag` step the operator runs AFTER approving.
* `--write-guide <path>` writes the generated guide to a path you pass (for
  review), but still does not touch the living ledger, tags, or releases.

WHAT IT DECIDES
---------------
1. Scope: manifest's ship-project dependency closure (from playtest_scope.py),
   so only THIS mod's src changes are considered — a sibling mod's commits are
   structurally excluded, not filtered by heuristic.
2. Candidate identity: repo, ref, resolved commit SHA + tree, base playtest tag,
   proposed next semantic playtest version/tag, project scope, included changes.
3. Completeness (fail-closed). BLOCKED verdict if ANY of:
     - a scoped, player-facing (non-exempt-type) change is unledgered;
     - the working tree / ref has drifted (dirty or ref mismatch);
     - the base playtest tag is missing or not an ancestor of the ref;
     - the ship csproj <Version> is not greater than the base tag's version
       (an untagged future change must not reuse the released build number);
     - a living guide file already labels these future changes as the OLD build.
4. Human gate: prints exactly one next action (approve → cut tag, or reject).

Exit codes: 0 = READY (a human may cut the proposed tag), 2 = BLOCKED (verdict
lists every blocker), 1 = usage/environment error.
"""
import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import playtest_scope as scope_mod  # noqa: E402

REPO = Path(__file__).resolve().parents[1]

EXEMPT_TYPES = {"revert", "chore", "docs", "test", "ci", "build", "style"}


def sh(*args, check=False):
    r = subprocess.run(args, cwd=REPO, capture_output=True, text=True)
    if check and r.returncode != 0:
        sys.exit(f"prepare-playtest: command failed: {' '.join(args)}\n{r.stderr.strip()}")
    return r.stdout.strip()


def sh_rc(*args):
    r = subprocess.run(args, cwd=REPO, capture_output=True, text=True)
    return r.returncode, r.stdout.strip(), r.stderr.strip()


# ── ledger parsing (shared shape with gen-playtest-guide.py) ─────────────────
def parse_frontmatter(text):
    m = re.match(r"^---\n(.*?)\n---\n", text, re.S)
    fm = {}
    if m:
        for line in m.group(1).splitlines():
            mm = re.match(r"\s*([a-z_]+):\s*(.+?)\s*(?:#.*)?$", line)
            if mm:
                fm[mm.group(1)] = mm.group(2).strip().strip('"')
    return fm


def extract_section(text, header):
    pat = re.compile(r"^##\s+" + re.escape(header) + r".*?$(.*?)(?=^##\s|\Z)", re.S | re.M)
    m = pat.search(text)
    return m.group(1).strip() if m else ""


def commit_type(subject):
    m = re.match(r"\s*([a-z]+)(?:\([^)]*\))?!?:", subject)
    return m.group(1) if m else ""


def own_pr_number(subject):
    m = re.search(r"\(#(\d+)\)\s*$", subject)
    return m.group(1) if m else None


# ── git-scoped change discovery (net-zero-revert aware, project-scoped) ──────
def net_changed_files(base, ref, pathspecs):
    raw = sh("git", "diff", "--name-only", base, ref, "--", *pathspecs)
    return {ln.strip() for ln in raw.splitlines() if ln.strip()}


def commit_files(sha, pathspecs):
    raw = sh("git", "diff-tree", "--no-commit-id", "--name-only", "-r", "-m",
             sha, "--", *pathspecs)
    return {ln.strip() for ln in raw.splitlines() if ln.strip()}


def scoped_commits(base, ref, pathspecs):
    """Commits touching the manifest's scoped src since base..ref, net-zero dropped.

    Same conservative net-zero-revert logic as gen-playtest-guide.py, but the
    pathspecs come from the manifest's dependency closure, so a sibling mod's
    commits never enter this set.
    """
    net = net_changed_files(base, ref, pathspecs)
    raw = sh("git", "log", f"{base}..{ref}", "--no-merges",
             "--pretty=format:%h%x1f%s%x1f%b%x1e", "--", *pathspecs)
    out = []
    for rec in raw.split("\x1e"):
        rec = rec.strip()
        if not rec:
            continue
        parts = rec.split("\x1f")
        sha = parts[0].strip()
        subject = parts[1].strip() if len(parts) > 1 else ""
        body = parts[2].strip() if len(parts) > 2 else ""
        if not sha:
            continue
        fp = commit_files(sha, pathspecs)
        if fp and not (fp & net):
            continue
        out.append((sha, subject, f"{subject}\n{body}"))
    return out


def classify_ledgered(changes, pending):
    """Split scoped changes into (ledgered, exempt, unledgered).

    A change is covered if: exempt commit type; OR carries a card id present in
    PENDING; OR (no card id) its own PR number is named in PENDING. Mirrors the
    established gen-playtest-guide.py cross-check so behaviour is consistent.
    """
    card_ids = set(re.findall(r"t_[a-f0-9]{8}", pending))
    pr_nums = set(re.findall(r"#(\d+)", pending))
    ledgered, exempt, unledgered = [], [], []
    for sha, subject, fulltext in changes:
        if commit_type(subject) in EXEMPT_TYPES:
            exempt.append((sha, subject))
            continue
        ids = set(re.findall(r"t_[a-f0-9]{8}", fulltext))
        if ids:
            if ids & card_ids:
                ledgered.append((sha, subject, sorted(ids)))
                continue
        else:
            pr = own_pr_number(subject)
            if pr and pr in pr_nums:
                ledgered.append((sha, subject, [f"#{pr}"]))
                continue
        unledgered.append((sha, subject, sorted(ids)))
    return ledgered, exempt, unledgered


# ── version helpers ─────────────────────────────────────────────────────────
def parse_semver(v):
    m = re.match(r"^(\d+)\.(\d+)\.(\d+)$", v.strip())
    return tuple(int(x) for x in m.groups()) if m else None


def version_from_tag(tag):
    m = re.search(r"(\d+\.\d+\.\d+)", tag)
    return m.group(1) if m else None


def propose_next_version(base_version):
    t = parse_semver(base_version)
    if not t:
        return None
    return f"{t[0]}.{t[1]}.{t[2] + 1}"


# ── main verdict ────────────────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser(description="Prepare Playtest — dry-run release candidate validator.")
    ap.add_argument("--manifest", default="trailborne", help="manifest key (default trailborne)")
    ap.add_argument("--ref", default="main", help="git ref to evaluate (default main)")
    ap.add_argument("--base-tag", default=None, help="override the base playtest tag (default: ledger last_playtest_tag)")
    ap.add_argument("--no-fetch", action="store_true", help="skip git fetch (offline/deterministic tests)")
    ap.add_argument("--write-guide", default=None, help="write the generated guide to this path (review only; does NOT touch the ledger/tags)")
    ap.add_argument("--json", action="store_true", help="emit the verdict as JSON to stdout")
    args = ap.parse_args()

    scope = scope_mod.get_manifest(args.manifest)
    blockers = []
    notes = []

    if not scope.ledger.exists():
        sys.exit(f"prepare-playtest: ledger not found for manifest '{args.manifest}': {scope.ledger}")
    ledger_text = scope.ledger.read_text()
    fm = parse_frontmatter(ledger_text)
    playtest_n = fm.get("playtest_counter", "?")
    base_tag = args.base_tag or fm.get("last_playtest_tag", "")
    pending = extract_section(ledger_text, "PENDING")

    # Resolve ref → exact commit + tree (fetch unless suppressed).
    if not args.no_fetch:
        sh("git", "fetch", "origin", args.ref, "--quiet")
        sh("git", "fetch", "origin", "--tags", "--quiet")
    # Prefer the remote-tracking branch for a branch-like ref (so `--ref main`
    # evaluates origin/main, the shared truth) — but NOT when offline (--no-fetch)
    # and NOT for HEAD/tags/SHAs, which are meant literally. This keeps the CI
    # preflight (`--ref HEAD --no-fetch`) evaluating the checked-out tag tree.
    ref = args.ref
    if not args.no_fetch and args.ref not in ("HEAD",):
        rc, _, _ = sh_rc("git", "rev-parse", "--verify", f"origin/{args.ref}")
        if rc == 0:
            ref = f"origin/{args.ref}"
    rc, resolved_sha, _ = sh_rc("git", "rev-parse", ref)
    if rc != 0:
        sys.exit(f"prepare-playtest: cannot resolve ref '{args.ref}'.")
    resolved_tree = sh("git", "rev-parse", f"{ref}^{{tree}}")
    repo_url = sh("git", "config", "--get", "remote.origin.url") or "(no origin)"

    # Dirty working tree drift guard (only matters when ref is a local HEAD).
    # `--untracked-files=no` so build/test scratch (e.g. __pycache__) doesn't
    # trip the guard — a candidate is about COMMITTED content, so only tracked
    # modifications constitute meaningful ref drift.
    dirty = sh("git", "status", "--porcelain", "--untracked-files=no")
    if ref == args.ref and dirty:
        blockers.append("working tree is dirty (uncommitted changes) — a candidate must be a clean committed ref.")

    # Base tag existence + ancestry.
    base_version = None
    if not base_tag:
        blockers.append("no base playtest tag (ledger last_playtest_tag empty and no --base-tag).")
    else:
        rc, _, _ = sh_rc("git", "rev-parse", "--verify", f"{base_tag}^{{commit}}")
        if rc != 0:
            blockers.append(f"base playtest tag '{base_tag}' does not exist.")
        else:
            rc, _, _ = sh_rc("git", "merge-base", "--is-ancestor", base_tag, ref)
            if rc != 0:
                blockers.append(f"base tag '{base_tag}' is not an ancestor of {ref} — stale/divergent base.")
            base_version = version_from_tag(base_tag)

    # Version guard: ship csproj <Version> must be > base tag's version, so an
    # untagged future change never reuses the released build number.
    ship_version = scope.version
    proposed_version = None
    proposed_tag = None
    if ship_version is None:
        blockers.append(f"could not read <Version> from ship csproj {scope.ship_csproj.relative_to(REPO).as_posix()}.")
    elif base_version:
        sv = parse_semver(ship_version)
        bv = parse_semver(base_version)
        if sv and bv:
            if sv <= bv:
                proposed_version = propose_next_version(base_version)
                blockers.append(
                    f"ship csproj <Version> {ship_version} is not greater than the released base "
                    f"{base_version} — bump <Version> (e.g. to {proposed_version}) before cutting a playtest tag; "
                    f"an untagged future change must not reuse the released build number.")
            else:
                proposed_version = ship_version
        else:
            blockers.append(f"non-semver version(s): ship={ship_version} base={base_version}.")
    if proposed_version:
        proposed_tag = f"{scope.tag_prefix}{proposed_version}{scope.tag_suffix}"

    # Scoped change discovery + ledger completeness.
    changes = []
    ledgered = exempt = unledgered = []
    if base_tag and base_version is not None:
        changes = scoped_commits(base_tag, ref, scope.pathspecs)
        ledgered, exempt, unledgered = classify_ledgered(changes, pending)
        if unledgered:
            blockers.append(
                f"{len(unledgered)} scoped player-facing change(s) are unledgered — "
                f"add a PENDING row (or name the card/PR) before cutting.")

    # Guide/build-version mismatch guard: an existing guide file for this series
    # must not label these future changes with the OLD released build number.
    guide_path = REPO / scope.guide_template.format(n=playtest_n)
    if guide_path.exists() and base_version and proposed_version and proposed_version != base_version:
        gtext = guide_path.read_text()
        gm = re.search(r"\*\*Build:\*\*\s*SBPR Trailborne\s*([0-9][0-9.]*)", gtext)
        if gm and gm.group(1) == base_version:
            blockers.append(
                f"living guide {guide_path.relative_to(REPO).as_posix()} labels Build {base_version} "
                f"(the released base) but includes post-{base_tag} changes — regenerate against the "
                f"proposed build {proposed_version} before cutting.")

    verdict = "BLOCKED" if blockers else "READY"

    result = {
        "verdict": verdict,
        "manifest": scope.name,
        "repository": repo_url,
        "ref": args.ref,
        "resolved_ref": ref,
        "commit": resolved_sha,
        "tree": resolved_tree,
        "base_tag": base_tag,
        "base_version": base_version,
        "ship_version": ship_version,
        "proposed_version": proposed_version,
        "proposed_tag": proposed_tag,
        "playtest_series": playtest_n,
        "project_scope": scope.project_names(),
        "pathspecs": scope.pathspecs,
        "scoped_changes": len(changes),
        "ledgered": [{"sha": s, "subject": m, "ids": i} for s, m, i in ledgered],
        "exempt": [{"sha": s, "subject": m} for s, m in exempt],
        "unledgered": [{"sha": s, "subject": m, "ids": i} for s, m, i in unledgered],
        "blockers": blockers,
        "generated": datetime.now(timezone.utc).isoformat(),
    }

    # Optional guide write (review only). Delegates to gen-playtest-guide.py's
    # generator would require refactor; we generate a minimal correctly-versioned
    # header + the scoped cross-check here to prove the version-labeling fix.
    if args.write_guide:
        _write_review_guide(Path(args.write_guide), scope, result, pending)
        notes.append(f"wrote review guide → {args.write_guide} (ledger/tags untouched)")

    if args.json:
        print(json.dumps(result, indent=2))
    else:
        _print_human(result, notes)

    sys.exit(0 if verdict == "READY" else 2)


def _write_review_guide(dest, scope, result, pending):
    n = result["playtest_series"]
    ver = result["proposed_version"] or result["ship_version"] or "(see csproj)"
    base = result["base_tag"]
    lines = [
        "---",
        f'title: "SBPR Trailborne — Playtest #{n} Testers Guide (candidate)"',
        "status: candidate",
        f"generated_from_tag: {base}",
        f"candidate_commit: {result['commit']}",
        f"proposed_build: {ver}",
        "---",
        "",
        f"# SBPR Trailborne — Playtest #{n} Testers Guide (candidate)",
        "",
        f"**Build:** SBPR Trailborne {ver} (candidate `{result['ref']}` @ `{result['commit']}`, "
        f"ahead of `{base}`)",
        f"**Proposed tag (NOT yet cut):** `{result['proposed_tag']}`",
        f"**Project scope:** {', '.join(result['project_scope'])}",
        "",
        "> Candidate guide generated by scripts/prepare-playtest.py against the exact",
        f"> commit above. This does NOT bump the ledger or cut a tag. The build number is",
        f"> the PROPOSED next version, never the released base {result['base_version']}.",
        "",
        "## Test items (from the ledger PENDING)",
        "",
        pending if pending else "_(ledger PENDING was empty)_",
        "",
        "## Ground-truth cross-check (scoped)",
        "",
        f"Scoped commits since **{base}** ({', '.join(result['project_scope'])}): "
        f"**{result['scoped_changes']}**",
        "",
    ]
    if result["unledgered"]:
        lines.append("> ⚠️ UNLEDGERED scoped changes (must be resolved before cutting):")
        for u in result["unledgered"]:
            tag = ", ".join(u["ids"]) if u["ids"] else "no card id"
            lines.append(f"> - `{u['sha']}` {u['subject']}  ({tag})")
    else:
        lines.append("✅ Every scoped change maps to a ledger item.")
    dest.write_text("\n".join(lines) + "\n")


def _print_human(r, notes):
    bar = "═" * 70
    print(bar)
    print(f"  PREPARE PLAYTEST — {r['verdict']}   (manifest: {r['manifest']})")
    print(bar)
    print(f"  repository        {r['repository']}")
    print(f"  ref               {r['ref']}  →  {r['resolved_ref']}")
    print(f"  commit            {r['commit']}")
    print(f"  tree              {r['tree']}")
    print(f"  base playtest tag {r['base_tag']}  (version {r['base_version']})")
    print(f"  ship <Version>    {r['ship_version']}")
    print(f"  proposed version  {r['proposed_version']}")
    print(f"  proposed tag      {r['proposed_tag']}   (NOT created)")
    print(f"  playtest series   #{r['playtest_series']}")
    print(f"  project scope     {', '.join(r['project_scope'])}")
    print(f"  scoped changes    {r['scoped_changes']}  "
          f"(ledgered {len(r['ledgered'])}, exempt {len(r['exempt'])}, unledgered {len(r['unledgered'])})")
    if r["unledgered"]:
        print("\n  UNLEDGERED scoped player-facing changes:")
        for u in r["unledgered"]:
            tag = ", ".join(u["ids"]) if u["ids"] else "no card id"
            print(f"    ✗ {u['sha']} {u['subject']}  ({tag})")
    if r["blockers"]:
        print("\n  BLOCKERS:")
        for b in r["blockers"]:
            print(f"    ✗ {b}")
    for nt in notes:
        print(f"\n  note: {nt}")
    print("\n" + "─" * 70)
    if r["verdict"] == "READY":
        print("  NEXT ACTION FOR A HUMAN — approve or reject cutting the proposed tag:")
        print(f"    APPROVE →  git tag {r['proposed_tag']} {r['commit']} && "
              f"git push origin {r['proposed_tag']}")
        print("               (this triggers .github/workflows/release.yml: build → publish →")
        print("                installer-pin PR. Tag creation stays a manual human step.)")
        print("    REJECT  →  do nothing; nothing has been mutated.")
    else:
        print("  NEXT ACTION FOR A HUMAN — REJECT: resolve the blockers above.")
        print("  Nothing was tagged, published, pinned, or written to the living ledger.")
    print("─" * 70)


if __name__ == "__main__":
    main()
