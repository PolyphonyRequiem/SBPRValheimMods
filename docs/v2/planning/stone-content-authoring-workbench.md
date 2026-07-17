---
status: idea
---

# Stone Content Authoring Workbench (POC)

> **POC — NOT production authority.** The hand-written C# catalogs remain the single source of
> truth for Stone content. This workbench is a *narrow, production-intent vertical slice* that
> proves a declarative-asset + deterministic-generator spine can round-trip the current roster
> without replacing it. Do not migrate runtime authority on the strength of this POC alone; that
> requires the separately approved promotion gate below.

Tool source lives under [`tools/stone-content-workbench/`](../../../tools/stone-content-workbench/README.md).
Planning lineage: this card implements the accepted decision map and implementation plan captured in
the Stone Content Authoring Workbench thread (Daniel-authorized).

## What this POC is

A net8 core deep module plus a thin CLI that:

- loads a canonical JSON asset carrying **all four** catalog sections and the current 20-node roster;
- strictly validates schema shape, semantic invariants, and explicit version-bump policy;
- deterministically **generates** four scratch `*.Data.g.cs` data artifacts;
- proves **behavioral parity** between the asset and the *current production C# catalogs* by
  normalizing both to one semantic snapshot and comparing field-by-field;
- **checks** on-disk generated output for drift;
- compiles the generated scratch output under net8 (a real compilation harness, not byte-equality).

The core (`StoneContent.Workbench.Core`) is I/O-free: it returns result records and never prints,
reads, or writes. The CLI (`StoneContent.Workbench.Cli`) owns all file and console I/O. A local
browser workbench (`StoneContent.Workbench.Web`) — delivered by the UI child card — reuses the same
core deep module for every authoritative decision; see **Local browser workbench** below.

## Local browser workbench (UI vertical slice)

`StoneContent.Workbench.Web` is a **loopback-only** ASP.NET Core host (`--asset <path>
[--scratch <dir>] [--port <n>]`, bound to `127.0.0.1` only) serving a dependency-light static
HTML/CSS/JS workbench. The browser is a **pure presentation layer**: it never reimplements a
validation rule. Four endpoints call the core through a thin `WorkbenchService` adapter:

- `GET  /api/document` — canonical asset text + the SHA-256 baseline hash (stale-write guard);
- `POST /api/validate` — baseline-aware validation (enforces the version-bump policy above);
- `POST /api/generate-preview` — the four generated `*.Data.g.cs` artifacts, blocked when invalid;
- `POST /api/export` — atomically writes the canonical asset + generated artifacts into the
  startup-granted scratch root (temp-sibling write then rename), refusing on a stale baseline,
  any validation error, or a generator failure.

The UI renders all four content sections; **edit controls are enabled for Cooking nodes only** (the
vertical slice). Stable node IDs are read-only; version pins are manually editable so a semantic edit
can be paired with an explicit bump. It provides dirty/reset state, an exact JSON diff, a narrow
generated-C# diff, and diagnostic-to-field navigation. Presentation preserves the accepted dark
mockup and carries meaning through **blue/orange + text/shape** — never red-vs-green, and avoiding
cyan/magenta ambiguity. The host grants exactly one asset root and one scratch root at startup; the
browser can never supply an arbitrary server path.

Acceptance evidence (1440×900 screenshots inspected for clipping/contrast/layout defects) lives under
[`tools/stone-content-workbench/docs/evidence/`](../../../tools/stone-content-workbench/docs/evidence/).

## Current-authority warning

`HomesteadProgressionCatalog`, `FoundationalPieceCatalog`, and `StoneFacetPalette` are executable C#
on `main` and stay authoritative. The workbench reads them through their public interfaces to build
its parity snapshot — no source parsing, no copied second catalog.

## Schema boundary

The canonical asset owns **authored intent only**: root identity (`formatVersion`, `assetId`,
`family`, `variant`), the four explicit human-authored pins (`contentRegistry`, `foundationalCatalog`,
`facetPalette`, `treeTuning`), Foundational tree/catalog identity + ordered members + explicit
exclusions, the Facet palette, the Trees + tuning, and the node roster. It does **not** own derived
counts, lookup dictionaries, helper factories, runtime/aggregate state, rejection/repair policy, or
UI state — those remain hand-written implementation code.

## Version policy (never auto-bumped)

- `displayLabel`-only edit → presentation change, no pin required.
- Node semantic field change → requires **both** that node's version **and** `contentRegistry`.
- Node add/remove/rename and Tree identity/version change → `contentRegistry` (renames are
  remove+add, never silent rebinding).
- Foundational members/exclusions/identity → `foundationalCatalog`.
- Facet ids/categories/candidates → `facetPalette`.
- Tree tuning numbers → `treeTuning`.
- Authoring-file shape change → `formatVersion`.

The tool emits `VERSION_BUMP_REQUIRED` / `VERSION_REGRESSION` diagnostics and blocks generation until
the author edits the pins explicitly. It never bumps a pin on its own.

## T012 honesty (three-axis parity, tuning held)

`TreeTuningCatalog` is a **held review branch** (`wt/t_c7313d0f`); it is **not on current `main`**.
The parity reporter therefore reports **three current-main axes PASS** and the Tree-tuning axis as a
**held-branch reference with current-main parity NOT APPLICABLE**. The POC does not claim four-axis
current-main parity, and it does not merge or depend on T012. When T012 merges, the tuning axis is
re-based to a real current-main comparison.

## Promotion gate: POC → production migration

JSON does not become canonical until a separately approved migration proves **all** of:

- all four catalog axes present on `main` and semantically equal;
- deterministic generation proven;
- generated data compiles under the shipping **net48** project with zero new warnings;
- existing Niflheim tests run against generated artifacts, not hand-written data;
- version-bump classifier has exhaustive tests;
- valid/invalid UI demonstrations pass;
- rollback is an ordinary Git revert with no silent persisted-state reinterpretation;
- Daniel accepts interaction flow and diff quality.

## Spec-and-code rule

Per `AGENTS.md`, any change to a recipe/piece/station/item/mechanic moves the spec and the code in the
same PR. This POC does not change any shipping content; it adds a parallel, non-authoritative
authoring spine and this planning doc together.
