#!/usr/bin/env python3
"""
gen-playtest-guide.py — roll the living playtest ledger + git ground-truth into a
numbered "Playtest #N — Testers Guide".

Source of truth:
  - docs/playtest/playtest-ledger.md  (PENDING items + counters in frontmatter)
  - git log <last_playtest_tag>..<ref> -- 'src/**/*.cs'  (code changes = candidate tests)

Usage:
  scripts/gen-playtest-guide.py [--ref main] [--tag <vX.Y.Z-playtest>] [--write]

Default (no --write): prints the guide to stdout (dry run, safe).
--write: writes docs/playtest/playtest-N-testers-guide.md AND does NOT mutate the
         ledger (archiving/counter-bump is the cron's job, kept separate so a human
         can regenerate a guide without advancing the series).

Reliability stance: this never invents test items. Manual items come from the
ledger PENDING section (human judgment a commit can't capture); auto items come
from git. If the two disagree, BOTH are shown — a code change with no ledger item
is flagged "⚠ no ledger entry" so nothing merges untested silently.
"""
import argparse, re, subprocess, sys, os
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
LEDGER = REPO / "docs/playtest/playtest-ledger.md"

# Single source of truth for "what counts as a gameplay code change". Used by the
# walk, the net-diff set, and the per-commit footprint so all three agree on the
# same file universe (a mismatch here would re-open the count/net discrepancy).
SRC_PATHSPEC = "src/**/*.cs"

def sh(*args):
    return subprocess.run(args, cwd=REPO, capture_output=True, text=True).stdout.strip()

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
    """Return the markdown block under a '## header' up to the next '## '."""
    pat = re.compile(r"^##\s+" + re.escape(header) + r".*?$(.*?)(?=^##\s|\Z)", re.S | re.M)
    m = pat.search(text)
    return m.group(1).strip() if m else ""

def net_changed_src_files(last_tag, ref):
    """src/**/*.cs files whose NET diff across last_tag..ref is non-empty.

    A revert pair (forward commit + a later `revert`) cancels here: a file touched
    only by both halves has identical content at `last_tag` and `ref`, so it never
    appears in this set. This is the mechanism that lets the walk net out reverts
    without parsing commit messages or maintaining an allowlist (card t_0fc06f42).
    """
    raw = sh("git", "diff", "--name-only", last_tag, ref, "--", SRC_PATHSPEC)
    return {ln.strip() for ln in raw.splitlines() if ln.strip()}

def commit_src_files(sha):
    """src/**/*.cs files a single commit touched.

    `-m` expands merge commits (diff against each parent) so a merge that carried a
    src change isn't silently empty; without it `git diff-tree -r` prints nothing
    for merges. We only ever USE this set to DROP a commit when it's both non-empty
    AND fully outside the net-changed set, so an empty result here is treated as
    "can't prove net-zero" and the commit is kept (see git_code_changes).
    """
    raw = sh("git", "diff-tree", "--no-commit-id", "--name-only", "-r", "-m",
             sha, "--", SRC_PATHSPEC)
    return {ln.strip() for ln in raw.splitlines() if ln.strip()}

def git_code_changes(last_tag, ref):
    """Commits touching src/**/*.cs in last_tag..ref, with net-zero commits dropped.

    A commit is kept only if at least one src file it touched still has a non-empty
    NET diff at `ref` (net_changed_src_files). A commit whose every touched src file
    nets to zero — both halves of a revert pair, the most common case — is dropped:
    its net effect on the tree is nil, so there's nothing new for a tester to test
    and it must not inflate the count or trip the "unledgered" guard.

    Conservative by construction: a commit that touches even one genuinely-changed
    file stays counted, so a real change interleaved with a revert is never hidden
    and the guard keeps its value for genuinely-missed changes.
    """
    if not last_tag:
        return []
    # Read the FULL message (subject + body) per commit, not just --oneline. Card ids
    # often live in the BODY (e.g. signs #228 carried t_6cc9f652 only in its body), so a
    # subject-only scan silently misses them — the exact blind spot that let the v0.2.33
    # ledger drift. %x1f = unit sep (sha|subject|body), %x1e = record sep between commits.
    net_files = net_changed_src_files(last_tag, ref)
    raw = sh("git", "log", f"{last_tag}..{ref}", "--no-merges",
             "--pretty=format:%h%x1f%s%x1f%b%x1e", "--", SRC_PATHSPEC)
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
        # Drop a commit only if we can PROVE its net tree effect is zero: it touched
        # at least one src file AND every src file it touched nets to zero at ref
        # (both halves of a revert pair). If the footprint is empty (e.g. a merge
        # commit diff-tree couldn't expand), we can't prove net-zero — keep it, so
        # the guard never hides a change it couldn't reason about.
        footprint = commit_src_files(sha)
        if footprint and not (footprint & net_files):
            continue
        out.append((sha, subject, f"{subject}\n{body}"))
    return out

def ledger_card_ids(pending):
    return set(re.findall(r"t_[a-f0-9]{8}", pending))

def ledger_pr_numbers(pending):
    """PR numbers (#NNN) named anywhere in the PENDING section.

    Used ONLY to rescue a commit that carries NO t_ card id at all — a direct
    `/bug` impl squash-merged with just its own `(#NNN)` and no card (e.g. #264,
    the corona FeetGlow, authored straight from a Discord /bug ticket). Naming
    that PR in a PENDING row is the SAME explicit acknowledgment the card-id path
    provides, so the surface is tracked. PR numbers are unique + monotonic, so a
    genuinely-unledgered FUTURE commit can never coincidentally match a PR number
    already sitting in the ledger. This does NOT loosen id-carrying commits — see
    the cross-check loop: a commit WITH card ids must still match a card id.
    """
    return set(re.findall(r"#(\d+)", pending))

def own_pr_number(subject):
    """The PR number GitHub appends to a squash-merge subject: '… (#264)'.

    Matches the LAST trailing `(#NNN)` only, so a subject that also mentions
    another PR mid-text (e.g. 'supersede #267 (#264)') still resolves to the
    commit's OWN PR, never a referenced one.
    """
    m = re.search(r"\(#(\d+)\)\s*$", subject)
    return m.group(1) if m else None

# Conventional-commit types that introduce NO player-visible test surface, so a src
# change of this type needs no ledger test item. Everything else (feat/fix/perf/refactor)
# is player-visible-by-default and MUST carry a card id present in the ledger PENDING.
EXEMPT_TYPES = {"revert", "chore", "docs", "test", "ci", "build", "style"}

def commit_type(subject):
    m = re.match(r"\s*([a-z]+)(?:\([^)]*\))?!?:", subject)
    return m.group(1) if m else ""

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ref", default="main", help="git ref to diff to (default main)")
    ap.add_argument("--tag", default=None, help="override last_playtest_tag from ledger")
    ap.add_argument("--write", action="store_true", help="write the guide file")
    ap.add_argument("--check", action="store_true",
                    help="exit non-zero if any merged code change is unledgered (CI/ship guard); writes nothing")
    args = ap.parse_args()

    if not LEDGER.exists():
        sys.exit(f"ledger not found: {LEDGER}")
    text = LEDGER.read_text()
    fm = parse_frontmatter(text)
    n = fm.get("playtest_counter", "?")
    last_tag = args.tag or fm.get("last_playtest_tag", "")
    pending = extract_section(text, "PENDING")

    sh("git", "fetch", "origin", args.ref, "--quiet")
    ref = f"origin/{args.ref}" if sh("git", "rev-parse", "--verify", f"origin/{args.ref}") else args.ref
    changes = git_code_changes(last_tag, ref)
    cards_in_ledger = ledger_card_ids(pending)

    # cross-check: a merged src change is "unledgered" (flagged) unless either
    #   (a) it is an EXEMPT commit type (revert/chore/docs/test/ci/build/style) — no
    #       player-visible surface, so no test item is owed; or
    #   (b) it carries a card id that is present in the ledger PENDING; or
    #   (c) it carries NO card id at all AND its own PR number (#NNN) is named in
    #       PENDING — the direct-/bug escape hatch (a commit authored straight from a
    #       Discord /bug ticket squash-merges with just its (#NNN) and no t_ card).
    # A feat/fix/etc. that carries card id(s) NONE of which are in PENDING is still
    # flagged (strict — the PR-number rescue does NOT apply to id-carrying commits, so
    # a real missed surface that happens to mention a ledgered PR can't slip through).
    # fulltext = subject + body, so a body-only card id is matched (the exact blind spot
    # that let the v0.2.33 ledger drift: signs #228 carried its card id only in the body).
    pr_numbers_in_ledger = ledger_pr_numbers(pending)
    unledgered = []
    for sha, subject, fulltext in changes:
        if commit_type(subject) in EXEMPT_TYPES:
            continue
        ids = set(re.findall(r"t_[a-f0-9]{8}", fulltext))
        if ids:
            # id-carrying commit: STRICT — at least one id must be in PENDING.
            if ids & cards_in_ledger:
                continue
        else:
            # no card id at all: rescue iff its OWN PR number is named in PENDING.
            pr = own_pr_number(subject)
            if pr and pr in pr_numbers_in_ledger:
                continue
        unledgered.append((sha, subject, ids))

    # --check: ship/CI guard. Exit non-zero if any merged code change is unledgered,
    # so the release wrapper REFUSES to cut a build with silently-untested surfaces
    # (the exact drift that made the v0.2.33 cut slow — a bump landed without a roll).
    if args.check:
        if unledgered:
            print(f"✗ {len(unledgered)} unledgered code change(s) since {last_tag} "
                  f"(diff vs {args.ref}):", file=sys.stderr)
            for sha, msg, ids in unledgered:
                print(f"    {sha} {msg}  ({', '.join(sorted(ids))})", file=sys.stderr)
            print("  → add a ledger PENDING row (or name the card in the cross-check) "
                  "before shipping.", file=sys.stderr)
            sys.exit(1)
        print(f"✓ ledger clean: all {len(changes)} code change(s) since {last_tag} are ledgered.")
        sys.exit(0)

    # Version is in the csproj <Version> tag (single source of truth per the csproj comment).
    csproj = sh("git", "show", f"{ref}:src/SBPR.Trailborne/SBPR.Trailborne.csproj") or ""
    vm = re.search(r"<Version>\s*([0-9][0-9.]*)\s*</Version>", csproj)
    ver = vm.group(1) if vm else "(see csproj)"

    guide = []
    guide.append(f"""---
title: "SBPR Trailborne — Playtest #{n} Testers Guide"
status: current
purpose: "Playtest #{n} — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: {last_tag}
diff_ref: {args.ref}
---

# SBPR Trailborne — Playtest #{n} Testers Guide

**Build:** SBPR Trailborne {ver} (current `{args.ref}`, ahead of `{last_tag}`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** {sh("date", "+%Y-%m-%d %H:%M %Z")}

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `{last_tag}`. The
> **Playtest #{n}** number is the human-facing testing series — distinct from the
> `vX.Y.Z-playtest` build tags.

---

## 1. Install on your client (one-time per build)

**Easiest — the one-line installer** (copies Valheim to a separate modded folder;
your vanilla install is never touched; bundles BepInEx + Trailborne +
ServerDevcommands and prints the live join code):

- **Windows (PowerShell):**
  ```powershell
  iwr https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.ps1 -UseBasicParsing | iex
  ```
- **Linux / macOS (bash):**
  ```bash
  curl -fsSL https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.sh | bash
  ```

Both verify the modpack SHA256 before installing and write a launcher
(`Play Trailborne` shortcut / `run-trailborne.sh`). Pass `--no-console` (bash) /
`-NoConsole` (PS1) to omit the F5 dev console.

**Manual alternative:** install BepInExPack_Valheim (r2modman or manual), then copy
this build's `BepInEx/plugins/SBPR.Trailborne/` from the release zip into your install.

Either way, launch Valheim and confirm the BepInEx console logs
`Loading [SBPR Trailborne {ver}]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.
""")

    # The manual PENDING block is already human-readable markdown — embed it verbatim,
    # minus its own subheader noise.
    guide.append("### Test items (from the ledger)\n")
    guide.append(pending if pending else "_(ledger PENDING was empty)_")

    guide.append("\n\n## 3. Ground-truth cross-check (auto)\n")
    guide.append(f"Code commits touching `src/**/*.cs` since **{last_tag}**: **{len(changes)}**\n")
    if unledgered:
        guide.append("\n> ⚠️ **These merged code changes have no matching item in the ledger PENDING — "
                     "verify they're covered or add them:**\n")
        for sha, msg, ids in unledgered:
            tag = ', '.join(sorted(ids)) if ids else 'no card id'
            guide.append(f"> - `{sha}` {msg}  ({tag})\n")
    else:
        guide.append("\n✅ Every merged code change maps to a ledger item. No silent-untested changes.\n")

    guide.append("\n## 4. After the playtest\n")
    guide.append(f"""
- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #{n} in the ledger, bumps the counter, and opens the Playtest #{int(n)+1 if str(n).isdigit() else '(N+1)'} planning card.
""")

    out = "\n".join(guide)

    if args.write:
        dest = REPO / f"docs/playtest/playtest-{n}-testers-guide.md"
        dest.write_text(out)
        print(f"✓ wrote {dest}")
        print(f"  ({len(changes)} code changes cross-checked, {len(unledgered)} unledgered)")
    else:
        print(out)
        print(f"\n--- DRY RUN (no file written). {len(changes)} code changes, {len(unledgered)} unledgered. Use --write to emit. ---", file=sys.stderr)

if __name__ == "__main__":
    main()
