---
title: Investigations
status: living
last_updated: 2026-06-07
---

# Investigations

Dated post-mortems and root-cause write-ups — the "we hit a confusing failure,
here's what actually happened" record. An investigation captures the *evidence
trail* (what we observed, what we ruled out, the real cause, and the proposed or
applied fix) so neither a human nor an AI agent re-runs the same dead-end debug
months later.

## When to write one

Write an investigation when:

- a failure cost real debugging time and the cause was non-obvious,
- the root cause is worth preserving even after the immediate fix lands, or
- a fix is proposed but gated (so the reasoning survives until it's applied).

## Conventions

- Filename: `YYYY-MM-DD-kebab-summary.md`.
- Frontmatter `status:` from the allowed vocabulary — typically `historical`
  for a closed post-mortem, `current` for an active/ongoing dig.
- Append-only in spirit: supersede a stale finding with a new file rather than
  rewriting history. Record the fix outcome inline when it lands.
