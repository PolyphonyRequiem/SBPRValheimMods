---
title: QA harness documentation
status: living
last_updated: 2026-07-29
---

# docs/qa — QA harness documentation

Documentation for the external QA harness: the T022 acceptance-test runner, its
arrange phase, and the live two-client rig they drive.

This folder is about **how we test the product**, not about the product itself.
Product behaviour lives under `docs/v0.1.0/` and the version trees; decisions that
constrain either live in `docs/decisions/`.

## What belongs here

- Specifications for QA phases and harness components (arrange, act, assert).
- Implementation notes for shipped harness capabilities, especially where the
  reasoning behind a check is load-bearing and would otherwise be lost.
- Evidence and findings from live QA runs, when they establish a durable fact
  rather than a one-off result.

## What does not

- Per-run logs and transient artifacts — those live outside the repo
  (`~/valheim/qa-artifacts/`), and are referenced by path from a findings doc.
- Product specs and acceptance criteria for game features.
- Anything containing credentials, tokens, or the contents of a lane password
  file. Paths are fine; values never are.

## Conventions

- Every content doc carries frontmatter with a `status:` from the allowed
  vocabulary (`current`, `living`, `historical`, `superseded`, ...). `docs-lint`
  enforces this, along with the two-file rule that keeps this README and
  `index.md` present.
- Prefer stating what was **verified** and how, separately from what is believed.
  The harness's whole reason for existing is that "logs green" is not "playable";
  documentation that blurs the two is worse than none.
