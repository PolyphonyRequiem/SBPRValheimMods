# SBPRValheimMods — Documentation

This is the documentation tree for the SBPR Valheim mods. If you're a player,
you want the root [`PLAYER_GUIDE.md`](../PLAYER_GUIDE.md) instead — this tree is
for contributors and design history.

## How this tree is organized

Documentation is organized by purpose:

- **[`design/`](design/)** — *durable* design intent. The why behind the mods:
  pillars, vision, and the deep investigations (no-map navigation, pin-sharing)
  that shaped the patch surface. These outlive any single release.
- **[`decisions/`](decisions/)** — *Architecture Decision Records (ADRs)*. Short,
  append-only records of load-bearing decisions, so neither a human nor an AI
  agent re-litigates or accidentally undoes a settled call.
- **[`v0.1.0/`](v0.1.0/)** — *version-scoped* working docs for the first release:
  the milestone map, the requirements spec, planning artifacts, and playtest
  scripts. Each future release gets its own semver sibling (`v0.2.0/`, …).
- **[`v1/`](v1/)** — *version-scoped* docs for the v1 implementation line: the
  vertical-slice architecture plan and implementation research notes.
- **[`datasets/`](datasets/)** — *reference data*. Tables of pieces, craftables,
  and recipes that both humans and tooling read.

## Conventions

Every folder in this tree carries **two** index files:

- `README.md` — human orientation (prose; what's here and why).
- `index.md` — machine-readable manifest (a table of every file + one-line purpose).

Other rules:

- **Semver dirs** (`v0.1.0/`), never date stamps. The date a doc was written lives
  inside the doc, not in the path.
- **Freshness via frontmatter `status:`** — content docs declare one of `current`,
  `living`, `historical`, or `superseded` so a reader (human or agent) can tell at
  a glance whether a doc reflects shipped reality. (ADRs use `accepted`/`proposed`.)
- All of this is **machine-enforced** by [`scripts/docs-lint.py`](../scripts/docs-lint.py)
  (CI workflow `.github/workflows/docs.yml`) — broken two-file rule, missing/invalid
  status, or broken relative links fail the check.

The full convention is encoded in the `sbpr-docs-conventions` Hermes skill.

## Map

See [`index.md`](index.md) for the machine-readable manifest of this directory.
