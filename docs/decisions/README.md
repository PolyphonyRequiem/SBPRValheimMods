---
title: Architecture Decision Records
status: living
last_updated: 2026-06-04
---

# Decision Records (ADRs)

Short, append-only records of **load-bearing decisions** — the choices that, if
silently reversed, would break the project's intent or cause rework. They exist
so neither a human nor an AI agent re-litigates or accidentally undoes a settled
call months later.

## When to write an ADR

Write one when a decision:

- constrains future work ("the bench is its own station, NOT the workbench"),
- was expensive to reach (a playtest or a debugging session settled it),
- would look arbitrary later without the reasoning, or
- an agent could plausibly "fix" in the wrong direction.

**Don't** ADR routine choices the spec or code already expresses clearly. ADRs
are for the *why behind the constraint*, not a changelog.

## How

1. Copy [`TEMPLATE.md`](TEMPLATE.md) to `NNNN-kebab-title.md` (next number).
2. Fill it in — keep it short; a screenful is plenty.
3. Set `status: accepted` once Daniel signs off.
4. Never edit an accepted ADR's decision. To change it, write a **new** ADR and
   mark the old one `superseded by ADR-XXXX`. History is the point.

## Relationship to other docs

- **ADRs** = *why* a constraint exists (immutable, append-only).
- **`docs/v0.1.0/planning/requirements.md`** = the *what* of the locked spec.
- **`SpecCheck.cs`** = the machine-enforced *recipe manifest*.
- **`docs/design/`** = durable narrative intent.

See [`index.md`](index.md) for the list of records.
