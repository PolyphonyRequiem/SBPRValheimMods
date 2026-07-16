# Stone Content Authoring Workbench (POC)

A narrow, **production-intent** vertical slice that proves a declarative content asset plus a
deterministic C# generator can round-trip the current Niflheim Homestead Stone roster **without
replacing** the hand-written C# catalogs. See the planning doc:
[`docs/v2/planning/stone-content-authoring-workbench.md`](../../docs/v2/planning/stone-content-authoring-workbench.md).

> **This is a POC.** The shipping C# catalogs remain authoritative. Generated output goes only to
> explicit scratch directories. Nothing here mutates production content or live-world state.

## Layout

```text
tools/stone-content-workbench/
├── assets/
│   ├── homestead-stone.content.json          # canonical 20-node asset (all four sections/pins)
│   └── homestead-stone.content.schema.json   # Draft 2020-12 schema proposal
├── src/
│   ├── StoneContent.Workbench.Core/          # I/O-free deep module (load/validate/classify/generate/check)
│   └── StoneContent.Workbench.Cli/           # thin CLI adapter (owns all file/console I/O)
└── tests/
    └── StoneContent.Workbench.Tests.csproj   # xUnit: serialization, validation, generation, parity, compile harness
```

The core returns result records and performs no I/O. The CLI owns files and presentation. A browser
`serve` command is **reserved for the UI child card** and is not implemented here.

## Build & test (net8, no Valheim SDK needed)

```bash
unset DOTNET_ROOT
/usr/bin/dotnet test tools/stone-content-workbench/tests/StoneContent.Workbench.Tests.csproj -c Release
```

## CLI

```bash
CLI=tools/stone-content-workbench/src/StoneContent.Workbench.Cli
ASSET=tools/stone-content-workbench/assets/homestead-stone.content.json

# Validate schema + semantics + version policy (exit 0 clean, 1 on errors)
/usr/bin/dotnet run --project $CLI -c Release -- validate "$ASSET"

# Generate the four scratch data artifacts (refuses any src/ path)
/usr/bin/dotnet run --project $CLI -c Release -- generate "$ASSET" --output /tmp/scw-out

# Check on-disk generated output for drift against a fresh generation
/usr/bin/dotnet run --project $CLI -c Release -- check "$ASSET" --generated /tmp/scw-out
```

All commands accept `--json` for machine-readable output. Exit codes: `0` clean, `1`
validation/drift failure, `2` usage error.

## What is proven

- **Deterministic generation** — two generations are byte-identical.
- **Behavioral parity** — three current-`main` axes (`contentRegistry`, `foundationalCatalog`,
  `facetPalette`) PASS against the real production C# catalogs; Tree tuning is a held-branch
  reference (T012 `wt/t_c7313d0f`), reported current-main **NOT APPLICABLE**.
- **Compilation** — the generated scratch output compiles under net8 (a real `dotnet build` harness).
- **Version safety** — the tool never auto-bumps; it blocks generation until pins are edited.
