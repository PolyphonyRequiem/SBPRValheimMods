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

def git_code_changes(last_tag, ref):
    if not last_tag:
        return []
    # Read the FULL message (subject + body) per commit, not just --oneline. Card ids
    # often live in the BODY (e.g. signs #228 carried t_6cc9f652 only in its body), so a
    # subject-only scan silently misses them — the exact blind spot that let the v0.2.33
    # ledger drift. %x1f = unit sep (sha|subject|body), %x1e = record sep between commits.
    raw = sh("git", "log", f"{last_tag}..{ref}", "--no-merges",
             "--pretty=format:%h%x1f%s%x1f%b%x1e", "--", "src/**/*.cs")
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
        out.append((sha, subject, f"{subject}\n{body}"))
    return out

def ledger_card_ids(pending):
    return set(re.findall(r"t_[a-f0-9]{8}", pending))

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
    #   (b) it carries a card id that is present in the ledger PENDING.
    # A feat/fix/etc. whose card id is missing from PENDING — or which carries NO card id
    # at all — is flagged. fulltext = subject + body, so a body-only card id is matched
    # (the exact blind spot that let the v0.2.33 ledger drift: signs #228 carried its
    # card id only in the commit body, invisible to the old --oneline scan).
    unledgered = []
    for sha, subject, fulltext in changes:
        if commit_type(subject) in EXEMPT_TYPES:
            continue
        ids = set(re.findall(r"t_[a-f0-9]{8}", fulltext))
        if not (ids & cards_in_ledger):
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
