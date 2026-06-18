# index — docs/v2/planning

| file | purpose |
|------|---------|
| README.md | Human orientation for v2 planning |
| index.md | This manifest |
| requirements.md | Locked v2 cartography requirements (Surveyor's Table, Local Maps, Cartographer's Kit) |
| cartography-impl-spec.md | Per-feature buildable implementation spec — acceptance criteria, vanilla hooks, feature-folder placement, SpecCheck rows (gates the 3 impl cards) |
| marker-signs-impl-spec.md | Per-feature buildable spec for Marker Signs + WorldPin — 4 additive marker pieces, Shift+E gesture, derive-by-scan reconcile, SpecCheck +4 rows (the pin substrate the cartography viewer consumes) |
| ancient-portal-impl-spec.md | Per-feature buildable spec for Portal Seed → Ancient Portal — two-prefab cairn-pattern (Seed item + Hammer-placed portal piece), additive TeleportWorld kitbash, the PortalPrefabHash pairing gotcha, 15s grow timer, overhead jump-trigger, 11 named ATs, SpecCheck +2 rows |
| map-provider-binding-impl-spec.md | Per-feature buildable spec for the equipped-local-map PROVIDER BINDING state machine (most-recent-equipped wins; unbinds on re-equip / leave-inventory / death) + the carry-state circular minimap disc that reuses the §2H.1 viewer at minimap scale (inherits #159 clip), nomap-on gated, with the disc-centring question (§7 'player-centered' vs §2H.1 'table-centred') flagged OPEN for Daniel. SpecCheck +0 rows |
| local-map-mkey-open-impl-spec.md | Per-feature buildable spec for moving the bound-local-map OPEN gesture from E (Use) → M, removing the E-to-open path, swapping the HUD prompt token ($KEY_Use → $KEY_Map), and — the load-bearing part — SBPR OWNING the M edge in nomap-OFF via a `Minimap.Update` consume-prefix (`ResetButtonStatus`) so our viewer opens without vanilla's full map stacking. Realizes the 🟢 DECIDED M-key model (map-provider-model.md §1); supersedes cartography-impl-spec §2F/§2G open-input + requirements AT-MAP-EQUIP. Closes issue 3 / card t_f9a04fda. 11 named AT-MKEY-* tests. SpecCheck +0 rows |
