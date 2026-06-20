# README — docs/v2/planning

Planning artifacts for the Black Forest tier. Same role as
`docs/v0.1.0/planning/` plays for Meadows.

- **`requirements.md`** — the locked v2 cartography requirements, distilled from the
  ratified decisions in [`../../design/cartography-v2.md`](../../design/cartography-v2.md).
  The design doc carries the *why* and the decision history; this carries the locked
  *what* in build-ready form. **All open items closed** (architect spec-pass 2026-06-10).
- **`cartography-impl-spec.md`** — the buildable *how*: one tight section per feature
  (Surveyor's Table, Local Map + viewer, Cartographer's Kit) an implementer picks up
  cold — observable acceptance criteria, exact vanilla decomp hooks, the
  `Features/Cartography/` placement, and each feature's SpecCheck manifest row. This is
  what gates the three implementation cards (filed as children of the spec-pass + the
  UI-fork spike).
- **`marker-signs-impl-spec.md`** — the buildable *how* for **Marker Signs + the
  WorldPin substrate** (design lock:
  [`../../design/marker-signs-worldpin.md`](../../design/marker-signs-worldpin.md)).
  Four additive Spade-placed marker pieces (POI / mining / shelter / portal) that
  pin/unpin themselves on the map via Shift+E with custom marker icons, plus the
  durable destroy-safe WorldPin engine (derive-by-scan reconcile keyed on the sign's
  ZDOID). **This is the pin model the Local Map viewer + Surveyor's Table consume** —
  built once, not forked. SpecCheck delta = **+4 build pieces**.
- **`ancient-portal-impl-spec.md`** — the buildable *how* for **Portal Seed → Ancient
  Portal** (design lock: [`../../design/pocket-portal.md`](../../design/pocket-portal.md) +
  [`../../design/ancient-portal-placeholder-art.md`](../../design/ancient-portal-placeholder-art.md)).
  A two-prefab feature on the **cairn pattern**: a 25 kg **Portal Seed** item (crafted at the
  Explorer's Bench) and a Hammer-placed **Ancient Portal** piece whose build cost is one
  Seed — so break→seed is free vanilla `DropResources`. Additive `TeleportWorld` kitbash
  (horizontal overhead ring, ~15 s scale-lerp grow, jump-up trigger), keeping the vanilla
  ore-ban. Calls out the **`PortalPrefabHash` registration** the design missed (without it
  portals place + grow but never tag-pair). SpecCheck delta = **+2** (1 item recipe + 1
  piece).
- **`map-provider-binding-impl-spec.md`** — the buildable *how* for the **equipped-local-map
  provider binding + carry-state minimap disc** (design lock:
  [`../../design/map-provider-model.md`](../../design/map-provider-model.md) §2/§3.2/§6/§7).
  The provider state machine (equip = provider, survives unequip, unbinds on re-equip /
  leave-inventory / death, most-recent-equipped-still-carried wins) feeding a **circular
  rotate-to-heading minimap disc** that reuses the §2H.1 viewer at minimap scale (inherits the
  #159 edge-bleed clip), gated to nomap-ON so it does not displace the vanilla minimap in
  nomap-off. Refrains card `t_1d1b505b` (issue 5 "carry disc"). One render question — disc
  **centring** (§7 "player-centered" vs §2H.1 "table-centred") — is left **OPEN for Daniel**.
  SpecCheck delta = **+0** (presentation + provider state on existing prefabs).
- **`local-map-mkey-open-impl-spec.md`** — the buildable *how* for the **M-key open gesture**
  (design lock: [`../../design/map-provider-model.md`](../../design/map-provider-model.md) §1,
  Daniel 2026-06-15: "M is the single map key; SBPR owns it"). Moves the bound-local-map OPEN
  from **E (Use) → M**, removes the E-to-open path, swaps the equipped HUD prompt token
  (`$KEY_Use` → `$KEY_Map`), and — the load-bearing piece — makes SBPR **own the M input edge**
  in `nomap=OFF` so our viewer opens *without* vanilla's full map stacking on top. The mechanism
  is a non-skip `Minimap.Update` **consume-prefix**: act on the M edge, then
  `ZInput.ResetButtonStatus("Map")` (vanilla's own input-consume idiom) so vanilla's later read
  sees nothing. Closes **issue 3 / card `t_f9a04fda`** (the impl had drifted — still on E despite
  the DECIDED M model). Supersedes the open-input portions of `cartography-impl-spec.md` §2F/§2G
  and `requirements.md` AT-MAP-EQUIP (§2F's Esc-exit work stands). SpecCheck delta = **+0**
  (input binding + prompt token on an existing controller).
- **`local-map-disc-name-hint-impl-spec.md`** — the buildable *how* for the **name + `[M]`
  open-hint UNDER the minimap disc** (design lock:
  [`../../design/map-provider-model.md`](../../design/map-provider-model.md) §2.1, Daniel
  2026-06-19 v0.2.27 playtest: *"the bottom hint doesn't really work for me… put the name of the
  map under the minimap and the M key hint there"*). **Relocates** the open-hint from the
  floating bottom-centre `SBPR_LocalMapPrompt` canvas (deleted) to a two-line caption **under
  the carry disc** — the bound map's NAME ("Local map for <Table>") above the rebind-correct
  `[<$KEY_Map>]` hint — so it reads as one unit: "[M] opens THIS named local map." **Widens**
  visibility from equipped-only to **disc-visibility** (provider bound + nomap-ON), fixing the
  asymmetry where M opens a carried-but-unequipped map yet the old hint hid. Net-new addendum to
  the locked §2 minimap model; supersedes `local-map-mkey-open-impl-spec.md` §5's prompt
  PLACEMENT (the `$KEY_Map` token + rebind-correctness stand). Closes card **t_338f723b**.
  SpecCheck delta = **+0** (HUD presentation; the name is already stored).

- **`local-map-biome-indicator-impl-spec.md`** — the buildable *how* for the **current-biome
  NAME readout on both cartography surfaces** (design lock:
  [`../../design/map-provider-model.md`](../../design/map-provider-model.md) §2.2, Daniel
  2026-06-19 v0.2.27 playtest: *"Minimap and local map need to have support for biome
  indicators"* → **Path A**, "A and architect"). Adds the player's current-biome name as text:
  on the **disc**, a line in the under-disc caption stack (**name / biome / `[M]` hint**),
  updating on biome change; on the **modal**, a **fixed** current-biome readout under the BARE
  title (cursor-hover is a deferred follow-up — the modal is passive). ONE shared
  `MapSurface.CurrentBiomeNameOrNull()` feeds both surfaces (`Player.GetCurrentBiome()` →
  vanilla `$biome_*` token, with a `Biome.None`/unlocalized guard so no `$biome_*` literal
  leaks — the 2026-06-05 sign-bug lesson). Computed live — **no `SurveyData` wire change**
  (Path A, not Path B colour/legend), no vanilla `m_biomeName*`/`Minimap`/material/root
  mutation (nomap enforced). Net-new addendum to the locked §2 minimap model; extends the
  PR #205 caption infrastructure (**t_26bba85b**). Closes card **t_caf0f1cf**. SpecCheck
  delta = **+0** (HUD presentation; biome is read live, never stored).

As more v2 features (Real Tents, lamp/pigment graduation) get specced, their
requirements land here too.
