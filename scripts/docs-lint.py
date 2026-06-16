#!/usr/bin/env python3
"""
docs-lint — enforce SBPR documentation conventions.

Checks (see docs/decisions + the sbpr-docs-conventions skill):
  1. TWO-FILE RULE   — every docs/ subfolder has both README.md and index.md.
  2. STATUS FIELD    — content docs carry a frontmatter `status:` from the
                       allowed vocabulary. (Scaffolding README/index/TEMPLATE
                       are exempt — their role is implicit.) `idea` is the
                       pre-spec capture tier (named, not yet specced); it sits
                       below `proposed` and is promoted once mechanics are locked.
  3. NO BROKEN LINKS — every relative .md link in any doc resolves on disk.

Exit non-zero on any violation. Used by .github/workflows/docs.yml and runnable
locally: `python3 scripts/docs-lint.py`.
"""
from __future__ import annotations
import os, re, sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(REPO, "docs")

ALLOWED_STATUS = {"idea", "current", "living", "historical", "superseded", "template", "accepted", "proposed"}
# Files exempt from the status-field requirement (structural scaffolding).
STATUS_EXEMPT = {"README.md", "index.md"}

errors: list[str] = []

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

# ---- Check 1: two-file rule -------------------------------------------------
for dirpath, dirnames, filenames in os.walk(DOCS):
    mds = [f for f in filenames if f.endswith(".md")]
    if not mds:
        continue
    for required in ("README.md", "index.md"):
        if required not in filenames:
            errors.append(f"[two-file] {rel(dirpath)}/ is missing {required}")

# ---- Checks 2 & 3: status + links ------------------------------------------
LINK = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
all_md = []
for dirpath, _, filenames in os.walk(DOCS):
    for f in filenames:
        if f.endswith(".md"):
            all_md.append(os.path.join(dirpath, f))
# also lint root-level docs
for f in ("README.md", "CONTRIBUTING.md", "AGENTS.md", "PLAYER_GUIDE.md"):
    p = os.path.join(REPO, f)
    if os.path.exists(p):
        all_md.append(p)

for path in all_md:
    text = read(path)
    name = os.path.basename(path)
    in_docs = path.startswith(DOCS + os.sep)

    # Check 2: status field (docs/ content files only; scaffolding exempt)
    if in_docs and name not in STATUS_EXEMPT and name != "TEMPLATE.md":
        fm = frontmatter(text)
        if fm is None or "status" not in fm:
            errors.append(f"[status] {rel(path)} has no frontmatter `status:` field")
        else:
            # status may carry a trailing description; take the first token/word
            first = fm["status"].split()[0].rstrip("—-").strip().lower() if fm["status"] else ""
            if first not in ALLOWED_STATUS:
                errors.append(
                    f"[status] {rel(path)} status '{fm['status']}' not in {sorted(ALLOWED_STATUS)}")

    # Check 3: relative .md links resolve
    for m in LINK.finditer(text):
        target = m.group(1).strip()
        if target.startswith(("http://", "https://", "#", "mailto:")):
            continue
        target = target.split("#", 1)[0]            # drop anchor
        if not target or not target.endswith(".md"):
            continue
        resolved = os.path.normpath(os.path.join(os.path.dirname(path), target))
        if not os.path.exists(resolved):
            errors.append(f"[link] {rel(path)} -> broken link '{target}'")

# ---- Report -----------------------------------------------------------------
if errors:
    print(f"docs-lint: {len(errors)} issue(s):\n")
    for e in sorted(errors):
        print("  " + e)
    sys.exit(1)
print(f"docs-lint: OK — {len(all_md)} docs checked, conventions hold.")
