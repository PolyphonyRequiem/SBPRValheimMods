---
title: "Trail-Lights family — eternal Beacon + Surtling-Ember Lamp (v3 impl spec)"
status: proposed
purpose: "Architect+spec for the v3 trail-light family deferred in requirements.md §567 / PARKED §v3 / nomap.md §3. Records the decomposition decision (two distinct ETERNAL pieces — a tall far-reaching Beacon and a small Surtling-Ember Lamp — NOT a fueled beacon, because v1's Path Lamp already fills the fueled-standing-light niche), the four open questions that need Daniel's confirm before build, the eternal-fire engine (Assets.GraftTorchFire — the CURRENT cosmetic-fire pattern, correcting the card's stale ConfigureCosmeticFire reference), per-piece prefab/mesh/light/gate specs, the additive ADR-0006 construction, the SpecCheck manifest rows, named acceptance tests, and the v5 lighthouse-promotion forward dependency. status:proposed — buildable only after Daniel confirms the four open questions; an impl card is created on confirm."
---

# Trail-Lights family — eternal Beacon + Surtling-Ember Lamp

This reformats **already-deferred design** into a buildable spec. The sources of truth, read
first and treated as such:

- `docs/design/nomap.md` §3 — the Beacon sketch (Fireplace subclass, tall thin mesh, single
  point light intensity 1.5 / range 12 / no shadows / no smoke; resin-fuelled
  `m_secPerFuel=14400` / `m_maxFuel=4` / `m_fuelItem=resin`).
- `docs/v0.1.0/planning/requirements.md` §A3.8 (line 437) — *"Ember Lamps are NOT in v1 … Ember
  Lamps + Beacons come together later"*; line 567 — *"Beacons, Ember magic — v3"*; line 732 —
  keep these OUT of the v1 player-guide.
- `docs/design/PARKED-2026-06-03.md` §v3 (*"Beacons, Ember magic"*) and §v5 (*"Beacons promote
  to lighthouses"*).
- `docs/design/design-pillars.md` Pillar 1 — *"lamps, beacons (when they ship) — all live on the
  Spade."*

> **Status is `proposed`, not `current`.** The four open questions in §1 are genuine design
> calls reserved for Daniel (the card's acceptance criterion: *"Open questions 1–4 are
> answered/confirmed by Daniel before build"*). §0 records the architect's **recommended**
> decomposition with rationale; §1 surfaces each knob with a lean. On Daniel's confirm this doc
> is revised to `current` and an impl card is cut for `engineer-systems`.

> **Clean-side note (ADR-0001):** every decomp line cited is base game (`assembly_valheim`),
> fair to read and adapt (repo AGENTS.md + 2026-06-09 clarification). Fireplace line numbers are
> from `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`, grepped live this pass.
> Vanilla content facts (Surtling core, Dvergr lantern, braziers) are from the wiki corpus
> `~/valheim/sbpr-corpus/wiki/fandom/`, grepped this pass.

---

## 0. Decomposition decision (recorded with rationale)

The card framed the call as *"either one 'trail beacon' with fuel/eternal variants, or three
distinct pieces (fueled Beacon + eternal Brazier + small Ember Lamp)."* Both framings predate two
facts that reshape the choice:

1. **The fueled-standing-trail-light niche is ALREADY SHIPPED.** v1's **Path Lamp**
   (`piece_sbpr_path_lamp`, 3 Wood + 2 Resin, a `piece_groundtorch_wood` clone scaled 3× tall,
   Meadows, placed via the Spade 'Trail' tab — `Features/Trailhead/Trailhead.cs`) **is** a
   resin-fuelled standing trail light. A v3 "fueled Beacon" would be a reskin of it with a bigger
   light — a new art asset, not a new mechanic.
2. **The v3 family's reason to exist is the ETERNAL behaviour + the Surtling gate.** Surtling core
   *"throbs with inner heat"* (corpus `Surtling_core.md`) and is the material behind every vanilla
   *never-refuelled* light — the Dvergr lantern is *"a light source … only that it has no
   durability"* built with Surtling core ×1 (`Dvergr_lantern.md`). Eternal light = the graduation
   v1's maintenance-bearing fuelled lamps don't offer.

**Recommended decomposition: TWO distinct pieces, both ETERNAL, gated by Surtling core, placed via
the Spade 'Trail' tab:**

| Piece | Prefab | Role | Light (intensity / range) | Form |
|---|---|---|---|---|
| **Beacon** | `piece_sbpr_beacon` | far-reaching navigation landmark; the §v5 lighthouse-promotion seed | **1.5 / 12** (kept verbatim from nomap §3) | tall, thin |
| **Ember Lamp** | `piece_sbpr_ember_lamp` | small path-side eternal ember; the intimate trail light | ~0.8 / ~5 (dimmer/shorter; eyeball-flagged) | low, small |

**Why two, not one, and not three:**

- **Not one piece with variants.** §v5 names *"Beacons promote to lighthouses"* — "Beacon" must
  survive as a distinct, named prefab so the v5 promotion has a concrete anchor. A2.8 lists
  *"Ember Lamps + Beacons"* as two named things shipping together. Collapsing them into one
  variant-toggled piece erases both anchors.
- **Not three.** The card's third piece ("fueled Beacon") is the Path Lamp that already ships
  (point 1 above). Adding it back as a v3 piece is duplication, not differentiation. The card's
  "eternal Brazier" and "Beacon" are the **same object** once the Beacon is made eternal — so they
  merge into one (the Beacon), leaving exactly two distinct pieces.
- **Differentiated by FORM + REACH, not fuel.** Both are eternal; they split on the navigation
  role — a tall far landmark you sight across a valley (Beacon) vs a low close ember that marks the
  trail underfoot (Ember Lamp). This is a legible, non-redundant pair.

This is a **revision of nomap §3's resin-fuelled Beacon**, made deliberately, with the reason
noted (the card permits a noted revision): the fuelled-beacon niche is filled by the v1 Path Lamp,
and an eternal beacon also satisfies the card's own "no add-fuel hover prompt" criterion for free
(§2). The nomap §3 **light** config (intensity 1.5 / range 12 / no shadows / no smoke) is **kept
verbatim** for the Beacon.

---

## 1. Open questions for Daniel (confirm before build)

Each carries the architect's lean. These are the card's "open questions 1–4."

**Q1 — Family split.** Recommend **two distinct eternal pieces** (Beacon + Ember Lamp), per §0.
Confirm, or do you want a single variant-toggled piece, or to keep a separate fuelled Beacon
distinct from the v1 Path Lamp after all?
→ *Lean: two distinct, both eternal.*

**Q2 — Fuelled-vs-eternal progression.** Recommend the v3 family is **eternal-only**; the
fuelled tier is the **v1 Path Lamp** (Meadows). So the progression is *cross-tier* — Path Lamp
(fuelled, Meadows) → Beacon/Ember Lamp (eternal, v3) — not a fuelled-vs-eternal toggle *within*
the v3 family.
→ *Lean: eternal-only v3 family; no intra-family fuel toggle.*

**Q3 — Tier / gate (the real tension).** requirements.md:567 parks these at **v3 (Swamp)**. But
**Surtling core is a Black-Forest-tier material** (Burial Chambers / Surtlings — corpus
`Surtling_core.md`), so a Surtling-core-only gate makes the family *effectively Black-Forest
reachable*, looser than Swamp. Two clean ways to honour "v3/Swamp":
  - **(a) Recommended — split the gate to make the progression a feature, not a leak:** the
    **Ember Lamp** gates on **Surtling core only** (small eternal light, reachable once you hold a
    core — Black-Forest-onward), and the **Beacon** adds an **Iron co-gate** (the Swamp metal —
    same "materials are the gate" pattern the Iron Compass and Sunstone Lens both use), holding the
    far landmark to true v3/Swamp. Result: a small eternal light early, the big eternal landmark at
    tier. This resolves the tension by design.
  - (b) Hold BOTH to Swamp with an Iron co-gate on each.
  - (c) Accept both as Black-Forest-reachable (Surtling-only), and let "v3" mean *release
    sequencing*, not biome gate.
→ *Lean: (a). Confirm the tier story you want; the recipes in §6 are the lever.*

**Q4 — v5 lighthouse-promotion forward dependency.** Recommend **record only** — the Beacon is the
named seed for the v5 *"Beacons promote to lighthouses"* maritime tier; the promotion itself is
out of scope here and built on a separate v5 card (§7). Confirm we only reserve the hook now.
→ *Lean: record the dependency, build nothing.*

---

## 2. The eternal-fire engine (corrects the card's stale reference)

> 🔴 **The card's cited pattern is dead code.** The card says reuse `Features/Cairns/CairnTag.cs`
> `ConfigureCosmeticFire` (`Fireplace.m_infiniteFuel = true` with fuel knobs zeroed). **That method
> no longer exists** — it was the *pre-v0.2.8* clone-then-strip path, retired by ADR-0006. The
> **current** cosmetic-fire pattern, shipped and proven on the Cairn, is:

**`Assets.GraftTorchFire(Transform parent, float localY, float lightIntensity, float lightRange)`**
(`Runtime/Assets.cs:824`). It builds an eternal flame with **no Fireplace at all**:

- Reads the `piece_groundtorch_wood` donor via `ZNetScene.GetPrefab` (fires no Awake), grafts a
  **copy** of three ZNetView-free cosmetic children off its inactive `_enabled` node —
  `fx_Torch_Basic` (flame VFX), `Point light`, `sfx_fire_loop` (crackle) — onto a fresh
  `SBPR_*Fire` GameObject. Instantiating a ZNetView-free subtree wakes no ZDO → nothing to orphan
  (the opposite of the PR #23 trap).
- Strips the two donor traps: `TimedDestruction` (would self-destroy the audio) and any
  `EffectArea` (would grant heat). Keeps one `Light`, sets `intensity`/`range` from the args,
  drops extra lights. No shadows by construction (the torch light casts none); no `SmokeSpawner`
  is copied.
- Returns `null` cleanly on a headless server (no graphics) or a missing donor — the fire is pure
  client art; the piece is still valid without it.

**Why this nails the card's "no add-fuel hover" criterion for free:** the add-fuel prompt comes
from `Fireplace : Hoverable, Interactable` (`assembly_valheim:106277`) whose `Interact` adds
`m_fuelItem` (decomp :106592–106608). **There is no Fireplace on a `GraftTorchFire` piece**, so
there is no `Interact`, no hover prompt, and no fuel state to manage — eternal by construction, not
by setting `m_infiniteFuel=true` on a live Fireplace. This is strictly cleaner than the nomap §3
sketch (which would have inherited the whole Fireplace fuel surface and then had to suppress it).

> Decomp note for the curious: `Fireplace.m_infiniteFuel` (`:106302`), `m_secPerFuel` (`:106300`,
> default 3), `m_maxFuel` (`:106298`, default 10), `m_fuelItem` (`:106336`) all still exist — they
> are just **not the path we take**. We attach no Fireplace, so none of them apply.

The Cairn drives the same helper (`CairnTag.BuildCosmeticFire` → `GraftTorchFire`, then
`ReconcileFire` HP-gates it). Our trail lights call `GraftTorchFire` once at build and **leave the
flame always-lit** (no HP gate — these are eternal lights, not wear indicators).

---

## 3. Beacon spec (`piece_sbpr_beacon`)

**Construction — additive, ADR-0006 (no Fireplace-donor clone):**

1. `Assets.ConstructPieceShell("piece_sbpr_beacon", referenceDonor)` builds the networked skeleton
   from scratch — `Piece` + `WearNTear` + `ZNetView` with fields set explicitly, hit/destroy/place
   `EffectList`s reference-copied off a clean vanilla stone piece (the established
   `ConstructPieceShell` path; the `referenceDonor` is read-only for effects, never instantiated as
   a mutable base).
2. **Body mesh (tall, thin):** a constructed `MeshFilter`/`MeshRenderer` child reading a vanilla
   *pole/torch-shaft* mesh + material as a **blueprint** (e.g. the `piece_groundtorch`/Standing
   iron torch shaft, or `wood_pole2`) via `GetPrefab` — reading an asset is reference, not cloning
   (ADR-0006). Placeholder-quality per the v1/v3 art doctrine (kitbash for playtest; ship art is a
   later polish pass). Exact donor mesh is an impl choice; the load-bearing spec is the silhouette
   = **tall + thin** (nomap §3).
3. **Eternal flame at the top:** `Assets.GraftTorchFire(body.transform, topY, 1.5f, 12f)` — the
   nomap §3 light kept verbatim: **intensity 1.5, range 12, no shadows, no smoke**. Always-lit (no
   HP gate). The `topY` anchor is the constructed body's height.

**Light config (LOCKED from nomap §3, unless Daniel revises):** single point light, intensity
**1.5**, range **12**, no shadows, no smoke particles.

**Fuel model:** **ETERNAL — no Fireplace, no fuel, no add-fuel hover** (§2). *Deliberate revision
of nomap §3's resin-fuel model; reason recorded in §0.*

**Decay:** standard `WearNTear` (destructible by damage / hammer-removable), **no mandatory
time-decay** (unlike Cairns). An eternal flame implies an eternal piece — no maintenance loop.
*(Flag: if Daniel wants Cairn-style decay/maintenance, that's a revision — lean: no decay.)*

**Surtling-core gating:** the eternal flame is *paid for once* by the Surtling core in the build
recipe (§6) — the core is the "inner heat" that never goes out. No runtime core consumption.

**Placement:** `m_category = PieceCategory.Misc` (the Spade's 'Trail' tab — same constraint the
Path Lamp documents: the spade PieceTable declares only Misc, so the piece MUST be Misc to render
there), `m_craftingStation = null` (no bench-proximity to place — Daniel 2026-06-05 *"for the path
light and sign, no bench requirement"*; the Beacon is the same class of Explorer-placed piece).
Added to the spade-only PieceTable in the Trailblazing ODB-wiring pass, like the Path Lamp.

---

## 4. Ember Lamp spec (`piece_sbpr_ember_lamp`)

Same engine as the Beacon, differentiated by **form + reach**:

**Construction:** identical additive path (`ConstructPieceShell` + a constructed body mesh +
`GraftTorchFire`), but:

- **Body mesh (low, small):** a small bowl / stone / brazier-cup silhouette read as a blueprint
  (e.g. a small stone mesh, or the brazier bowl), flame anchored **low to the ground** — the
  "ember" reads at ankle/knee height, not a tall pole.
- **Eternal flame (dimmer, shorter):** `GraftTorchFire(body.transform, emberY, 0.8f, 5f)` —
  **intensity ~0.8, range ~5** (eyeball-flagged consts, same convention as the Cairn's
  `SubTorchLightIntensity = 0.8 / SubTorchLightRange = 4.0`; converge in one joined session, then
  bake). Always-lit.

**Fuel model / decay / placement:** identical to the Beacon — eternal (no Fireplace/fuel/hover),
no mandatory decay, Misc category on the Spade 'Trail' tab, `m_craftingStation = null`.

**Surtling-core gating:** Surtling core ×1 in the recipe (§6) — the core literally *is* the ember.
Per Q3(a), the Ember Lamp gates on **Surtling core only** (no Iron co-gate), making it the
earlier-reachable small eternal light.

---

## 5. Build placement & doctrine (both pieces)

- **Spade 'Trail' tab, not the Hammer** (Pillar 1: *"lamps, beacons … all live on the Spade"*).
  Both pieces are `PieceCategory.Misc` and registered into the spade-only PieceTable in
  `Trailblazing.DoObjectDBWiring`, exactly as the Path Lamp is (`Trailhead.cs` documents the
  Misc-or-it-won't-render constraint).
- **No station-proximity to place** (`m_craftingStation = null`) — Daniel 2026-06-05.
- **Additive construction, ADR-0006** — `ConstructPieceShell` + constructed meshes + grafted
  ZNetView-free flame. **No cloning of a Fireplace-bearing donor and stripping it** (the card's
  explicit constraint; also the whole reason the v3 family is eternal-by-construction).
- **Server-gated** behind `ServerContext.OnSBServer` via the `Registrar` dispatch, like every
  other piece. The flame graft is client-only by construction (`GraftTorchFire` returns null
  headless).

---

## 6. Recipes & SpecCheck manifest impact

`Runtime/SpecCheck.cs` holds the recipe drift manifest (asserted at server boot; AGENTS.md: code +
spec + manifest move together). This feature adds **+2 build-piece rows** (both `Station = null` —
placed via the Spade, no crafting-station output):

| # | Manifest entry | Kind | Resources (proposed — Q3(a)) |
|---|---|---|---|
| 1 | `piece_sbpr_beacon` | build piece | **`SurtlingCore` ×1 + `Iron` ×2 + `Wood` ×5** |
| 2 | `piece_sbpr_ember_lamp` | build piece | **`SurtlingCore` ×1 + `Wood` ×3** |

**Resource prefab-name caveats (must match vanilla internal IDs or SpecCheck flags a null
`m_resItem`) — verified this pass against the corpus `Internal ID` field:**
- `SurtlingCore` — the eternal-heat material (`Surtling_core.md`, *"throbs with inner heat"*;
  Surtlings / Burial Chambers / Bonfires / Dvergr lanterns). The eternal-flame fiction's anchor.
- `Iron` — the Swamp metal (`Iron.md`), the Beacon's tier co-gate per Q3(a) (you can't smelt Iron
  without Sunken-Crypt scrap → Iron in the recipe *is* the Swamp gate, same heuristic as the Iron
  Compass / Sunstone Lens).
- `Wood` — frame.

**Recipe rationale (each material earns its slot):** Beacon = Surtling core (the inner heat that
never dies) + Iron (Swamp-tier frame + the tier gate) + Wood (the pole); Ember Lamp = Surtling core
(the ember itself) + Wood (the cup), no Iron so it's the earlier small eternal light. **These are
the Q3(a) lean — the recipe is the lever Daniel sets in §1.** If Daniel picks Q3(b), add `Iron ×1`
to the Ember Lamp; if Q3(c), drop Iron from the Beacon.

**SpecCheck shape:** both are build-piece rows (like `piece_sbpr_path_lamp`) — `SpecCheck.Run()`
asserts the piece exists and its `m_resources` tuple matches. No item-recipe icon/attack landmines
(those are `ConstructItemShell` item concerns; these are pieces). The manifest count in
`SpecCheck.cs` increments by 2 — keep it in sync in the same PR (AGENTS.md drift rule).

---

## 7. Forward dependency: v5 lighthouse promotion (record only — OUT of scope)

PARKED §v5: *"Sailing tier. Beacons promote to lighthouses."* The **Beacon** (`piece_sbpr_beacon`)
is the named seed for that promotion. **This card builds none of it.** Recorded so the v5 card has
a concrete anchor:

- The promotion likely reads an existing `piece_sbpr_beacon` and upgrades it (taller mast, longer
  light range, maritime placement rules) — a separate v5 (Plains) card, gated on Plains-tier
  materials. Whether it's an in-place upgrade (like the Cairn tier ladder) or a distinct
  `piece_sbpr_lighthouse` is a v5 design call, not decided here.
- **Implication for THIS spec:** keep the Beacon's prefab name and light model stable and
  documented so the v5 promotion has a clean handle. No code hook is added now.

---

## 8. Observable acceptance tests (named, in-game — "logs green ≠ playable")

- **AT-TL-BUILD:** both pieces appear in the Trailblazer's Spade 'Trail' tab and place with no
  bench in range; SpecCheck green for both new rows.
- **AT-TL-ETERNAL:** a placed Beacon and Ember Lamp burn **indefinitely** — no fuel meter, and
  hovering the piece shows **no "add fuel" prompt** (the card's eternal-fire criterion).
- **AT-TL-BEACON-LIGHT:** the Beacon's light reads as a far landmark (intensity 1.5 / range 12),
  visible across open terrain at night; no shadows, no smoke plume.
- **AT-TL-EMBER-LIGHT:** the Ember Lamp reads as a small low ember (dimmer/shorter than the
  Beacon and than a vanilla torch) — a path-side mark, not a landmark.
- **AT-TL-GATE:** the recipes require Surtling core (and Iron on the Beacon, per Q3(a)); a player
  without a core cannot build either.
- **AT-TL-NODECAY:** a placed light still burns after a long absence (no mandatory time-decay),
  unlike a Cairn. *(Only if Daniel confirms the no-decay lean.)*
- **AT-TL-PLAYERGUIDE:** the v1 PLAYER_GUIDE does **not** describe these as buildable now —
  they're a v3 roadmap item (requirements.md:732). The existing v1.1 PLAYER_GUIDE "Ember Lamps /
  Beacons" roadmap bullets are re-pointed to v3.

---

## 9. Doc-drift note (out of scope here; flag for a docs card)

`docs/datasets/PIECES_AND_CRAFTABLES.md` lines 77–78 still describe the **Cairn's** cosmetic fire
via the **stale** `m_infiniteFuel=true` / `ConfigureCosmeticFire` model (*"Fireplace forced
infinite-fuel … see `Features/Cairns/CairnTag.cs` (`ConfigureCosmeticFire`/`ReconcileFire`)"*).
The Cairn's real current path is `BuildCosmeticFire` → `Assets.GraftTorchFire` (no Fireplace at
all — §2). This is a pre-existing dataset doc-drift bug **about the Cairn**, surfaced while
researching this card. It does not block this spec; flag a small docs card to correct the dataset
to the `GraftTorchFire` reality.

---

## 10. Code placement

```
src/SBPR.Trailborne/Features/TrailLights/
  TrailLights.cs   — both piece prefabs (Beacon + Ember Lamp): additive ConstructPieceShell
                     skeletons, constructed body meshes, GraftTorchFire eternal flames, recipes,
                     Spade-table wiring. Mirrors Features/Trailhead/Trailhead.cs (the Path Lamp).
```

Registered via the `Registrar` dispatch (server-gated), wired into the spade PieceTable in the
Trailblazing ODB pass — the same two-phase register-then-wire the Path Lamp uses. `SpecCheck.cs`
manifest +2 rows in the same PR.
