# SBPRValheimMods — Documentation

This is the documentation tree for the SBPR Valheim mods. If you're a player,
you want the root [`PLAYER_GUIDE.md`](../PLAYER_GUIDE.md) instead — this tree is
for contributors and design history.

## How this tree is organized

Documentation is split three ways:

- **[`design/`](design/)** — *durable* design intent. The why behind the mods:
  pillars, vision, and the deep investigations (no-map navigation, pin-sharing)
  that shaped the patch surface. These outlive any single release.
- **[`v0.1.0/`](v0.1.0/)** — *version-scoped* working docs for the first release:
  the milestone map, the requirements spec, planning artifacts, and playtest
  scripts. Each future release gets its own semver sibling (`v0.2.0/`, …).
- **[`datasets/`](datasets/)** — *reference data*. Tables of pieces, craftables,
  and recipes that both humans and tooling read.

## Conventions

Every folder in this tree carries **two** index files:

- `README.md` — human orientation (prose; what's here and why).
- `index.md` — machine-readable manifest (a table of every file + one-line purpose).

Version directories use **semver** (`v0.1.0/`), never date stamps. The date a doc
was written lives inside the doc, not in the path.

The full convention is encoded in the `sbpr-docs-conventions` Hermes skill.

## Map

See [`index.md`](index.md) for the machine-readable manifest of this directory.
