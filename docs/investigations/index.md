# index — docs/investigations

Machine-readable manifest of investigation / root-cause write-ups.

| file | status | purpose |
|------|--------|---------|
| README.md | living | What investigations are, when/how to write them |
| 2026-06-06-release-workflow-steamcmd-failure.md | historical | Release workflow fails at SteamCMD; releases were published by hand. Root cause found, fix applied 2026-06-07. |
| 2026-06-07-terrain-placement-ripple-magnitude-spike.md | historical | Placement ground-ripple is a fixed-radius CircleProjector; how to scale it with terrain-op magnitude (Request 1). Fix surface located, not yet built. |

## Conventions

- Filename `YYYY-MM-DD-kebab-summary.md`; newest at the bottom.
- `status: historical` once the dig is closed; `current` while active.
