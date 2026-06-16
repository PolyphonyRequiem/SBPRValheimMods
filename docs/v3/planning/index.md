# index — docs/v3/planning

| file | purpose |
|------|---------|
| README.md | Human orientation for v3 (Swamp tier) planning |
| index.md | This manifest |
| twisted-portal-impl-spec.md | Build-ready spec for the **Twisted Portal** — a distinct portal class that teleports even where vanilla portals are blocked (`NoPortals`), addressed by player-assigned RUNE NAMES (`sbpr_rune_name` ZDO slot, off `s_tag`), accessed via a food-charged TRINKET key (durability-as-charge, burns per teleport, Pukeberry purge accelerator, the Sunstone Lens energy precedent). Reimplements teleport via `Player.TeleportTo` (omitting the NoPortals gate) rather than inheriting `TeleportWorld`. Catches the multiplayer 300m-sync fork the design doc missed (the Twisted equivalent of the Ancient Portal's PortalPrefabHash gotcha) and routes travel through server-side name-pairing (Model A). Decomposes into impl cards C1–C3. SpecCheck +2 rows. **BLOCKED on Daniel: Q1 coexist/replace, Q2 charge economy, Q3 destination UX (card t_f9cab392).** |
