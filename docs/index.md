# index — docs/

Machine-readable manifest of the documentation tree.

## Subdirectories

| path | kind | purpose |
|------|------|---------|
| design/ | durable | Long-lived design intent: pillars, vision, deep investigations |
| decisions/ | durable | Architecture Decision Records (ADRs) — load-bearing decisions, append-only |
| v0.1.0/ | version-scoped | Specs, planning, milestones, and playtest logs for the v0.1.0 release |
| v1/ | version-scoped | v1 architecture plan + implementation research notes |
| v2/ | version-scoped | Black Forest tier: cartography requirements (Surveyor's Table, Local Maps, Cartographer's Kit) |
| datasets/ | reference | Data tables (pieces, craftables, recipes) for humans and tooling |

## Files in this directory

| file | purpose |
|------|---------|
| README.md | Human orientation for the whole docs tree |
| index.md | This manifest |

## Conventions

- Every folder carries both `README.md` (prose orientation) and `index.md` (this manifest format).
- Version directories use semver (`v0.1.0/`), never date stamps.
- Content docs carry a frontmatter `status:` field for freshness — one of
  `idea`, `current`, `living`, `historical`, `superseded` (ADRs use `accepted`/`proposed`).
  `idea` is the pre-spec capture tier: a feature named but not yet specced (no mechanics
  locked, no impl card, no SpecCheck row) — promote to a real design doc when fleshed out.
- Load-bearing decisions are recorded as ADRs in [`decisions/`](decisions/).
- All of the above is machine-enforced by `scripts/docs-lint.py` (CI: `.github/workflows/docs.yml`).
- The full convention is encoded in the `sbpr-docs-conventions` Hermes skill.
