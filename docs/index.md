# index — docs/

Machine-readable manifest of the documentation tree.

## Subdirectories

| path | kind | purpose |
|------|------|---------|
| design/ | durable | Long-lived design intent: pillars, vision, deep investigations |
| v0.1.0/ | version-scoped | Specs, planning, milestones, and playtest logs for the v0.1.0 release |
| datasets/ | reference | Data tables (pieces, craftables, recipes) for humans and tooling |

## Files in this directory

| file | purpose |
|------|---------|
| README.md | Human orientation for the whole docs tree |
| index.md | This manifest |

## Conventions

- Every folder carries both `README.md` (prose orientation) and `index.md` (this manifest format).
- Version directories use semver (`v0.1.0/`), never date stamps.
- Encoded in the `sbpr-docs-conventions` Hermes skill.
