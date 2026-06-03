# SBPRValheimMods

Server-gated Valheim mods built under the SBPR namespace (Starbright + Polyphony/Requiem).

> **Doctrine.** These mods are designed to enrich gameplay on private SBPR servers only.
> Distribution plumbing (BepInEx, Thunderstore) is third-party and used as-is.
> All gameplay behavior is a clean-room reimplementation — no IronGate code is reproduced
> in this repository.

## Status

Pre-build. Currently in the **design phase** for the first family.

## Mod families

### Family 1 — Nomap / Hardcore Navigation
"Maps are a luxury, not a right." Each biome unlocks a navigation tier; the cartography
table is rebalanced toward a late-game role; on-demand map *making* becomes a
mid-progression craft; map *viewing* is a Mistlands-tier convenience.

Design doc: [`design/nomap.md`](design/nomap.md)

Nine modules currently in scope:
1. Orienteering Table (Meadows crafting station)
2. Trailblazer's Tools (signage + roadwork hand-tool)
3. Trail signage (Sign + Beacon variants)
4. Traveler's Storage (public + per-player private chest)
5. Traveler's Tent (sleep without setting spawn)
6. Pocket Portal (stackable, one-shot portal piece)
7. Twisted Portal (charged accessory, ignores `NoPortals`, through-terrain rune-name overlay)
8. Map table rebalance (zoom cap, 1000 m visibility, no scroll)
9. Iron Compass (Swamps-tier HUD overlay)
10. Seer's Stone (Alt+E pin-by-look, auto-merges nearby pins)

### Family 2 — Guardian Stones
Reserved. Brief pending.

## Architecture (planned)

- **SBPR.Pact** — shared lib: server gating (`SBPRContext.OnSBServer`), asset bundle
  loader, prefab registration helpers, localization wiring.
- **SBPR.Nomap** — Family 1 mod, depends on Pact.
- Each mod tops every patch with `if (!SBPRContext.OnSBServer) return;` so installs on
  unrelated servers/world are no-ops.

Build pipeline target: BepInEx 5 / Mono Cecil, Harmony 2, Unity asset bundle baked from
a sidecar Unity project (not committed; built artifacts only).

Distribution: Thunderstore once the first mod ships.

## License

MIT. Code only. No IronGate assets, no decompiled source, no game binaries.

## Server

Niflheim — private SBPR server. Smoke-test target before any release.
