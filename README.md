# SBPRValheimMods

Server-gated Valheim mods built under the SBPR namespace (Starbright + Polyphony/Requiem).

> **Doctrine.** These mods enrich gameplay on private SBPR servers only.
> Distribution plumbing (BepInEx, Thunderstore) is third-party and used as-is.
> Gameplay behavior is the authors' own work — vanilla Valheim source may be
> read and adapted (it's the game we mod). **Other mods' code is not copied
> directly**; where we reproduce another mod's functionality we use a clean-room
> RE process (separate reviewer + implementer / Chinese wall). No copyrighted
> source (game binaries, decompiled IronGate source, other mods' source) is
> committed here.

## Status

**Active development — Trailborne v0.1.0 playtest.** The first mod (Trailborne)
builds, deploys to the Niflheim server, and is in client playtest. Build + release
are automated via GitHub Actions.

New here? Read in this order:
1. [**PLAYER_GUIDE.md**](PLAYER_GUIDE.md) — what Trailborne is and how it plays.
2. [**CONTRIBUTING.md**](CONTRIBUTING.md) — how to work in this repo (spec-first!).
3. [**AGENTS.md**](AGENTS.md) — condensed operating rules for AI coding agents.

## Trailborne (Family 1 — Nomap / Hardcore Navigation)

"Maps are a luxury, not a right." Trailborne replaces the free minimap with
player-built navigation infrastructure. v0.1.0 ships:

- **Explorer's Bench** — Meadows-tier crafting station (its own station, not the
  vanilla Workbench), gates the Trailborne progression.
- **Trailblazer's Spade** — hand-tool for laying paths and ground ops.
- **Pigments + Painted Signs** — craft inks, place signs, paint them by color.
- **Path Lamps** — standing lights for marking trails.
- **Cairns** — maintained 5-tier stone landmarks with comfort + decay lifecycle.

Durable design intent: [`docs/design/`](docs/design/). The locked spec:
[`docs/v0.1.0/planning/requirements.md`](docs/v0.1.0/planning/requirements.md).

## Architecture

- **`SBPR.Trailborne`** — the mod. BepInEx 5 / HarmonyX, `net48`. Every patch
  tops with a server-gate (`ServerContext.OnSBServer`) so it is a no-op on
  unrelated servers/worlds.
- **Spec-first + drift-checked.** `SBPR.Trailborne/Runtime/SpecCheck.cs` validates
  the live recipe set against the locked spec at server boot and logs ERROR on
  drift. Spec and code change together — see CONTRIBUTING.md.
- **CI/Release** — [`.github/workflows/`](.github/workflows/): build + reflection
  drift-guard on every PR; tag-driven, publish-then-PR releases.

## Build

```bash
scripts/setup.sh        # auto-detect Valheim + write .env   (Windows: scripts/setup.ps1)
scripts/fetch-sdk.sh    # fetch pinned BepInEx pack into .sdk/
dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release
```

Clean build = 0 errors, **0 warnings** (`<TreatWarningsAsErrors>` is ON — any
new warning fails the build). No machine-specific paths are committed; see
[`.env.example`](.env.example).

## Documentation

Contributor + design docs live under [`docs/`](docs/), organized by **semver**.
Every folder carries a `README.md` (human orientation) **and** an `index.md`
(machine-readable manifest) — the convention is in
[`docs/decisions/`](docs/decisions/) (ADRs) and the `sbpr-docs-conventions` skill.
Doc freshness is signalled by a `status:` field in each doc's frontmatter
(`current` / `living` / `historical` / `superseded`). Start at
[`docs/README.md`](docs/README.md).

## Server

**Niflheim** — private SBPR server, the smoke-test target before any release.

## License

MIT. Code only. No IronGate assets, no decompiled source, no game binaries.
