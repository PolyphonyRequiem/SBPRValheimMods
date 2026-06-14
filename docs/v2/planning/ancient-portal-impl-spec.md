---
title: "Portal Seed → Ancient Portal (Black Forest v2) — buildable implementation spec"
status: current
purpose: "Build-ready architect spec for the Portal Seed item + Ancient Portal piece. Converts the locked design (docs/design/pocket-portal.md PR #145, docs/design/ancient-portal-placeholder-art.md PR #146) into one tight section an engineer-systems implementer picks up cold: the two-prefab (item+piece) architecture, the exact vanilla decomp hooks (all line-cited against assembly_valheim), observable named acceptance tests, the Features/ placement, and the SpecCheck manifest rows. Authored by the architect spec-pass (card t_9a5540b2). The design docs are the WHAT; this is the HOW-to-pick-it-up-cold. Impl is the engineer-systems child of this card; Daniel gates the merge."
---

# Portal Seed → Ancient Portal — buildable implementation spec

The design docs ([`pocket-portal.md`](../../design/pocket-portal.md) — the mechanics;
[`ancient-portal-placeholder-art.md`](../../design/ancient-portal-placeholder-art.md) —
the kitbash) are the locked *what*. This doc is the buildable *how*: the two-prefab
architecture, the vanilla hooks carried forward and **re-verified against the decomp**,
observable acceptance criteria, the `Features/` placement, and the SpecCheck manifest
impact. An implementer should be able to build the whole feature from this section
without re-deriving anything.

> **Clean-side note (ADR-0001):** every decomp line cited here is the base game
> (`assembly_valheim`), which is **fair game to read and adapt** (repo AGENTS.md + the
> 2026-06-09 clarification). Line numbers are from
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (this box) and were
> grepped live during this spec-pass — re-confirm against the build assembly if the
> decomp dump drifts. No other mod's code is read or copied.

> **ADR-0006 (load-bearing):** the Ancient Portal prefab is built **additively**
> (`new GameObject()` + `AddComponent`), reading `portal_wood` only as a blueprint via
> `vprefab inspect`. The design doc's older "clone portal_wood" line (`nomap.md` §6)
> **predates ADR-0006 and is FORBIDDEN.** Cloning the ZNetView+EffectArea+GuidePoint
> -bearing `portal_wood` is the exact cairn-soft-lock bug class. Construct, don't clone.

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. This feature adds **+2 entries**
(both new) — one item recipe (the Seed) and one build piece (the Portal), mirroring the
cairn pattern (a marker ITEM whose recipe is checked + a CAIRN PIECE whose build cost is
that marker item):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `SBPR_PortalSeed` | item recipe (amount 1) | AncientSeed ×1, GreydwarfEye ×20, SurtlingCore ×2 | `piece_sbpr_explorers_bench` |
| 2 | `piece_sbpr_ancient_portal` | build piece | `SBPR_PortalSeed` ×1 | (Hammer-placed; `m_craftingStation = null`) |

**Resource prefab-name caveats (must match vanilla internal IDs / SBPR consts, or
SpecCheck flags a NULL `m_resItem`) — all verified this pass against the wiki corpus
`Internal ID` field (`~/valheim/sbpr-corpus/wiki/fandom/`):**
- Ancient seed = vanilla internal id **`AncientSeed`** (verified `Ancient_seed.md`).
- Greydwarf eye = vanilla **`GreydwarfEye`** — already the value of
  `MarkerSigns.EyeResource` (`MarkerSigns.cs:52`); reference that const, **do not** hardcode
  a literal, so the two stay in lockstep.
- Surtling core = vanilla **`SurtlingCore`** (verified `Surtling_core.md`).
- The Portal piece's sole build ingredient is the **SBPR item `SBPR_PortalSeed`** — exactly
  as the cairn pieces consume the SBPR marker item (`SpecCheck.cs:222` —
  `R(markerName, 1)`). SpecCheck resolves it because the Seed item is registered into
  ObjectDB **before** the Portal piece's `DoObjectDBWiring` runs (ordering — see §4).

**The two SpecCheck shapes (gotcha — same as cartography §0):** `SpecCheck.Run()` iterates
`Manifest.Where(s => s.Item != null)` for item recipes and `s.Piece != null` for build
pieces. Row 1 is `Item` only (the Seed recipe, `Station` set, `Amount = 1`); row 2 is
`Piece` only (the Portal, no `Item`, no `Station`). A `RecipeSpec` with both null or both
set is silently skipped — match the shape exactly.

**Asset-renderability (icon-crash guard, C1).** The Seed is an additively-constructed item
(`Assets.ConstructItemShell`), so it pre-seeds `Assets.FallbackIcon`; `SpecCheck.CheckIcon`
will ERROR at boot if the real `SBPR_PortalSeed` icon PNG didn't ship (magenta placeholder).
Ship an icon PNG with the Seed (a placeholder seed/acorn sprite is fine for v1 — flagged for
Daniel's art pass). The Portal **piece** has no item icon to check (pieces use `m_icon` for
the build menu; absent = builds with no menu thumbnail, non-fatal — flagged).

The card that touches `SpecCheck.cs` first should also extend the class's `LOCKED SOURCE`
comment to cite **this doc** alongside the existing v0.1.0/v2 sources. Code + spec +
SpecCheck row move in the **same PR** (spec-first rule).

---

## 1. Architecture — two prefabs, the cairn pattern (read this before coding)

The feature is **two registered prefabs**, exactly mirroring the cairn (a marker ITEM +
a cairn PIECE that consumes it):

1. **`SBPR_PortalSeed`** — an `ItemDrop` (the carried 25 kg item). Crafted at the
   Explorer's Bench. Built additively via `Assets.ConstructItemShell` (the Cartographer's
   Kit / additive-item pattern).
2. **`piece_sbpr_ancient_portal`** — a build `Piece` placed with the **Hammer**, whose
   **build cost is one `SBPR_PortalSeed`**. Built additively via
   `Assets.ConstructPieceShell` + grafted portal/root/leg visuals + a real vanilla
   `TeleportWorld` + our grow-timer `MonoBehaviour`.

**Why this two-prefab split is the whole design, not an implementation detail:**

- **Break → seed falls out for FREE.** Because the piece's `Piece.m_resources` is
  `{ SBPR_PortalSeed ×1, m_recover = true }` (every `Assets.BuildReq` sets
  `m_recover = true` — `Assets.cs:492`), vanilla `WearNTear.Destroy()` (the
  `WearNTear.Destroy` body, `assembly_valheim` ~`:129042`) calls `m_piece.DropResources()`
  on **every** destroy path — deconstruct, creature kill, decay — and
  `Piece.DropResources` (`:116319`) re-spawns the recoverable resources, i.e. **exactly
  one Portal Seed**. No custom `m_onDestroyed` hook, no bespoke drop code. This is the
  literal cairn mechanic (a cairn drops its marker on destroy) and it directly satisfies
  **AT-BREAK-TO-SEED**. (One nuance — `DropResources` halves-to-a-third for
  non-player-built pieces via `IsPlacedByPlayer()` (`:116346`); a player-placed portal is
  player-built so it returns the full ×1. Verify in-game that a creature-killed portal
  still returns the seed and not zero — the `Mathf.Max(1, dropCount/3)` floor means ×1
  stays ×1, but confirm.)
- **The 25 kg / no-bench / Hammer / replant fantasy** is just normal item + piece
  semantics once split this way: the *item* carries the weight and crafts at a bench;
  the *piece* places bench-free off the Hammer and costs that item.

> 🔴 **CRITICAL FINDING — the design docs missed this. The portal will place, grow, and
> teleport-locally but NEVER tag-pair with another unless we register its prefab hash.**
> Vanilla portal connection does **not** scan all `TeleportWorld` components. `ZDOMan`
> only adds a ZDO to its `m_portalObjects` pairing set when
> `Game.instance.PortalPrefabHash.Contains(zdo.GetPrefab())` (`assembly_valheim:64704`,
> `:64769`, `:65052`, `:65199`). `PortalPrefabHash` is a `List<int>` built **once** in
> `Game.Awake()` (`:84078`/`:84088`) from the serialized `Game.m_portalPrefabs` list — a
> from-scratch additive prefab is **not** in it. **The implementer MUST add our prefab's
> stable hash to `Game.instance.PortalPrefabHash` at registration** (a one-line
> `Game.instance.PortalPrefabHash.Add("piece_sbpr_ancient_portal".GetStableHashCode())`,
> guarded for idempotency + null `Game.instance`). Without it, two Ancient Portals with
> the same tag will sit unconnected forever and **AT-REGULAR-PORTAL fails** — and it
> fails *silently* (logs green, never pairs). This is the single highest-risk hook in the
> feature; flag it RED for in-game verification. (Clean-side: `PortalPrefabHash` /
> `m_portalPrefabs` are base-game members, fair to read + use.)

---

## 2. The Portal Seed item (`SBPR_PortalSeed`)

**Lands in:** `Features/Portals/Portals.cs` (a new vertical-slice feature folder —
`RegisterPrefabs` + `DoObjectDBWiring`, wired into `Registrar` per §4). Item + piece live
in the same feature folder (like `Cairns.cs` holds both the marker item and the cairn
piece).

### 2.1 Item construction (ADR-0006 additive)
- Build with **`Assets.ConstructItemShell("SBPR_PortalSeed")`** (the Cartographer's Kit /
  additive-item path — `Assets.cs:1110`). It returns a GameObject carrying ZNetView +
  ZSyncTransform + Rigidbody + item-layer BoxCollider + an `ItemDrop` with a fresh
  `SharedData` and the seeded `FallbackIcon`. **Do NOT clone a vanilla item.**
- Graft a visual mesh child (additive). Placeholder: a small scaled-down `stubbe` stump or
  an `Greydwarf_Root` `default` tendril knot reads as "a gnarled seed/root bulb" — or reuse
  the future Seed icon's source. Visual polish is flagged; any non-script mesh child works
  for v1 (the item just needs to render in-hand/on-ground).

### 2.2 Item SharedData (LOCKED values)
Set on the prefab's `m_itemData.m_shared` (additive items MUST set `m_shared` on the prefab
— `ConstructItemShell` already news it):
- `m_name = "Portal Seed"`, `m_description` = the planting flavor ("Plant with a Hammer;
  it grows into an Ancient Portal over ~15 s. No workbench needed.").
- `m_itemType = ItemType.Material` (= it's a build ingredient / carried item, not a tool or
  weapon). **Rationale:** the Seed is consumed as a `Piece.m_resources` ingredient exactly
  like the cairn marker item, which is a Material. It is NOT placed by being *equipped* — it
  is placed because the **Hammer's** PieceTable contains the Ancient Portal piece whose cost
  is this Material (see §3.4). Do not make the Seed itself a placement tool.
- **`m_weight = 25f`** (the locked 25 kg — Daniel). Set on `m_shared.m_weight`.
- **`m_maxStackSize = 1`** (RESOLVED open-knob #2 — see §6). A 25 kg item that stacks is a
  trap (5× = 125 kg); one-per-slot keeps the "you carry ONE portal" pack-commitment fantasy.
- `m_teleportable` — **leave the vanilla default `true`** (`SharedData.m_teleportable = true`,
  `:57740`). The Seed ITEM may pass through portals (it's not ore); the ore-ban is about
  *other* items in the pack and is enforced portal-side (§3.5), not by making the Seed
  itself non-teleportable. (If Daniel later wants "you can't portal a portal," flip this to
  `false` — note it, don't implement; he didn't ask for it.)
- Ship a real icon PNG (§0 C1) — `Assets.LoadPngAsSprite` over the seeded fallback.

### 2.3 Recipe (LOCKED — SpecCheck row 1)
Crafted at the **Explorer's Bench** (RESOLVED open-knob #1 — §6; the home-bench pre-make
model Daniel leaned toward, and the station every SBPR recipe already uses):
- `m_craftingStation = piece_sbpr_explorers_bench` via
  `RecipeHelpers.FindStation(Trailhead.ExplorersBenchName)` (the existing pattern —
  `Pigments.cs:143`, `Cairns.cs:268`), amount **1**.
- **Resources:** `AncientSeed ×1` + `GreydwarfEye ×20` + `SurtlingCore ×2`, built with
  `Assets.BuildReq(...)`. Reference `MarkerSigns.EyeResource` for the `GreydwarfEye` string.
- 🟡 **Ectoplasm note (DO NOT implement — playtest-contingent):** the design flags Ectoplasm
  (`Ectoplasm`, verified Black-Forest drop) as a possible later substitute for the eyes
  and/or cores. **First build ships the recipe above unchanged.** Leave a one-line code
  comment pointing at this note; do not add a config toggle or alt recipe.

---

## 3. The Ancient Portal piece (`piece_sbpr_ancient_portal`)

**Lands in:** `Features/Portals/Portals.cs` (+ a `Features/Portals/AncientPortalTag.cs`
MonoBehaviour for the grow timer — §3.6). The single biggest build-risk in the feature
(the novel overhead-trigger geometry + the portal-hash registration).

### 3.1 Construction (ADR-0006 additive — hard constraint)
- **`Assets.ConstructPieceShell("piece_sbpr_ancient_portal", donor)`** (`Assets.cs:990`) —
  builds ZNetView + Piece + WearNTear + BoxCollider from scratch and reference-copies
  hit/destroy/place effects off a clean donor. Use a **wood** effect donor (e.g.
  `wood_wall` or the same `portal_wood` read-as-blueprint for its place effect) so it
  sounds like a wooden/organic build, not stone. (The shell defaults
  `WearNTear.MaterialType.Stone` — override to **`Wood`**, see §3.3.)
- **Do NOT `Instantiate(portal_wood)`.** It carries ZNetView + a `PlayerBase` EffectArea
  (the 20 m "rested" sphere) + `GuidePoint` (the Hugin portal-tutorial trigger, same
  component class that mis-greeted the Explorer's Bench) + `portal_destruction` + LODGroup.
  Carrying any of those is the cairn-soft-lock / Explorer's-Bench-GuidePoint bug class.
  Read `portal_wood` only via `vprefab inspect portal_wood` for mesh/material/field values.

### 3.2 Visuals — the placeholder kitbash (from the merged art brief, ADR-0006-clean)
Graft these as **ZNetView-free cosmetic mesh children** via `Assets.GraftVisualSubtree`
(`Assets.cs:923` — the same helper the Surveyor's Table uses to steal the cartographytable
mesh). All three donors are verified script-free steals (art brief, X-rayed via `vprefab`):

| Part | Donor → child | Donor size | Scale to ~3 m | Notes |
|---|---|---|---|---|
| Ring/glow | `portal_wood` → `small_portal` (mesh `Cube.002`, material `portal_small`) | 4.23×3.29 m | ×~0.71 → ~3 m wide | **Rotate 90° to lie flat** (face up). Self-glows via emission map — no light needed. |
| Roots | `Greydwarf_Root` → `default` (980 tris, `GDSpawner_mat`) | 1.75×3.87×4.07 m | scale DOWN to ~3 m tendrils | 2–4 instances weaving up the legs + ring rim. |
| Legs | `stubbe` stump (`cylinder1..._auv`) | 9.8×4.4×6.95 m | scale DOWN hard, thin/tall | 2–3 pillars holding the ring at ~3 m overhead. |

- **Target envelope: ~3 m tall × ~3 m wide** (Daniel, confirmed against the concept render).
  The ring sits at the **top** (~3 m) so the player jumps up into it.
- 🔴 **OMIT** the donor's `PlayerBase` EffectArea, `GuidePoint`, `portal_destruction`,
  LODGroup, and particle stack. `GraftVisualSubtree` copies a *visual subtree* (mesh +
  material), not scripts — but verify the grafted child carries no MonoBehaviour; if the
  chosen donor child does, strip it (`Assets.StripGuidePoints` is the precedent for
  surgically removing an inherited hook).
- Whether the `portal_small` emission reads well lying flat (it's authored to face the
  player vertically) is a flagged in-engine check — fall back to a plain emissive disc if
  it looks wrong horizontal (art brief §"Open / to verify").

### 3.3 Fragility (HP = OPEN — Daniel decides; default vanilla 400 until then)
- Set `m_materialType = WearNTear.MaterialType.Wood` (takes axe/fire damage like the
  organic root structure it is, not the shell's default Stone).
- 🔴 **`WearNTear.m_health` is an OPEN knob — NOT yet decided by Daniel.** The design doc
  says only "more fragile than a regular portal"; it does **not** name a number. (An earlier
  draft of this spec asserted a "~150–200 lean" and locked 175 — that band was **never
  Daniel's**; it was the author's invention and has been retracted, 2026-06-13.) **Default
  to vanilla `m_health = 400f`** (verified `Portal.md`) until Daniel picks a lower value, so
  the build is honest rather than carrying a fabricated number. Whatever Daniel lands on,
  keep code + this line in lockstep. Tunable in playtest.
- **Rain decay: OFF for v1 (CONFIRMED Daniel, 2026-06-13).** Match the vanilla portal
  ("Damaged by Rain? No"). Set `m_noRoofWear = true` like the cairn shell. **Do not**
  implement weather decay. (A prior draft floated "a root structure arguably should decay"
  as if it were a live design thread — that was the author's framing, not Daniel's, and is
  retracted; Daniel confirmed no rain decay.)
- `m_canBeRemoved = true` (deconstructable → returns the seed, §1). The shell already
  sets this.

### 3.4 Placement — the Hammer, no station (LOCKED — RESOLVED open-knob #5, see §6)
- **`piece.m_craftingStation = null`** — NO bench-in-range required to place (the headline
  convenience; matches every bench-free SBPR piece — `Trailhead.cs:127`/`186`,
  `SurveyorsTable.cs:100`).
- **`piece.m_category = Piece.PieceCategory.Misc`** then add to the **Hammer**'s PieceTable
  via `Assets.GetHammerPieceTable()` + `Assets.AddOrReplacePieceByName(portalGo, hammerTable)`
  — the **exact** pattern the Explorer's Bench uses (`Trailhead.cs:153`/`:173`). The Bench
  is the repo's proven precedent for an SBPR piece on the Hammer table (not the Spade).
  Use `AddOrReplacePieceByName` (not a raw add) to avoid the "two benches" duplicate-on-rejoin
  bug (`Trailhead.cs:168-173`).
- 🔴 **Hammer-vs-Spade reconcile (design-pillars.md:33 conflict):** `design-pillars.md`
  Pillar 1 says "pocket portals (when they ship) — all live on the Spade." Daniel's
  2026-06-13 word is **"placed with a regular hammer."** **Daniel's latest word wins:
  Hammer.** This spec updates `design-pillars.md` to carve out the portal as a Hammer
  exception (a deployable settler's convenience, not a trail-marking tool) — see §5. If
  Daniel actually wants it on the Spade, that's a one-line table swap (`GetHammerPieceTable`
  → the spade table add in `Trailblazing.DoObjectDBWiring`); flagged in AT-SEED-FIELD-PLACE.
- **Build cost:** `Piece.m_resources = { Assets.BuildReq("SBPR_PortalSeed", 1) }` — the
  one-seed cost that makes break→seed free (§1). Built in `DoObjectDBWiring` (after the Seed
  is in ObjectDB), exactly like cairns rebuild their marker-cost there (`Cairns.cs`).

### 3.4b Placement surface — SOLID EARTH ONLY, not on structures (CONFIRMED Daniel, 2026-06-13)
- 🔴 **Requirement (Daniel, 2026-06-13, verbatim):** *"it needs to be built on solid earth.
  Not on structures."* The Ancient Portal plants in the ground — you cannot place it on a
  wood floor, stone floor, or any built piece.
- **Implement with `piece.m_groundOnly = true`.** Grounded against the vanilla placement
  validator `Player.UpdatePlacementGhost` / `GetPlacementStatus`
  (`assembly_valheim.decompiled.cs`): at `:18879` `m_groundOnly && heightmap == null →
  PlacementStatus.Invalid` — i.e. the placement point MUST be over terrain (a `Heightmap`),
  which built pieces are not, so this rejects placement on structures exactly as asked.
- **Use `m_groundOnly`, NOT `m_cultivatedGroundOnly`** (`:18883`, that one additionally
  requires hoe-cultivated dirt — too strict; Daniel said solid earth, not tilled soil), and
  NOT `m_groundPiece` (`:18873`/`:18952`, that flag also force-snaps the piece flat to the
  heightmap and enables terrain-clipping — wrong for an overhead root-arch we want sitting
  upright on its legs). `m_groundOnly` is the minimal "must be on terrain" gate without the
  snap/clip side-effects.
- **AT-PLACE-SOLID-EARTH (new — see §7):** placement ghost is valid on dirt/grass/rock
  terrain; placement is REJECTED (red ghost, `Invalid`) when aimed at a wood/stone floor or
  any built piece. Verify in-game on a joined client (logs-green ≠ playable).
- Interaction note: `m_groundOnly` does not by itself constrain slope; if Daniel later wants
  "no steep cliffs," that's a separate `m_notOnTiltingSurface`/`m_groundPiece` call — flag,
  don't pre-add.

### 3.5 TeleportWorld — real vanilla teleport, ore-ban inherited free
Add a real **`TeleportWorld`** component (`AddComponent<TeleportWorld>()`,
`assembly_valheim:122902`). It is the unmodified vanilla teleporter; we keep ALL its
behavior, which is what "otherwise a regular portal" means:
- **Tag-pairing** is identical (`ZDOVars.s_tag`, 10-char tag via the vanilla `TextInput`
  rename on Interact — `:122955`). Two Ancient Portals with the same tag pair — **provided
  the prefab hash is registered (§1 CRITICAL FINDING).**
- **Ore/metal ban inherited free (AT-REGULAR-PORTAL).** `TeleportWorld.Teleport(player)`
  (`:123002`+) calls `player.IsTeleportable()` → `Inventory.IsTeleportable()` (`:57606`),
  which returns false if any carried item has `m_shared.m_teleportable == false` (ore,
  ingots). We do **NOT** set `m_allowAllItems = true`, so the ban holds with zero extra
  code. Leave `m_allowAllItems` default false.
- **NoPortals global key** still honored (`Teleport` checks
  `GlobalKeys.NoPortals` — `:123010`). Correct — we're the convenience portal, not an
  override (that's the future Twisted Portal).

> 🔴 **NRE RISK — serialized child refs `TeleportWorld` dereferences. Wire these or it
> NREs.** Vanilla `portal_wood` has these as Inspector-assigned children; our additive
> build has them null. Two hot paths crash:
> - **`TeleportWorld.Update()`** (every frame, `:122995`) does
>   `m_model.material.SetColor("_EmissionColor", …)`. **`m_model` MUST be assigned** a real
>   `MeshRenderer` (point it at the grafted ring's renderer) or it NREs 60×/s. This alone
>   would spam the log and break the piece.
> - **`UpdatePortal()`** (0.5 s, `:122976`) is guarded by `m_proximityRoot == null` (safe
>   when null) but if you DO set `m_proximityRoot` it then calls `m_target_found.SetActive(…)`
>   — so `m_target_found` (an `EffectFade`) must be non-null whenever `m_proximityRoot` is.
>   Simplest: assign `m_proximityRoot = the ring transform` AND give `m_target_found` a real
>   `EffectFade` child (or a minimal stub), OR leave both null and accept no "target found"
>   glow pulse for v1 (the `m_model` emission still needs wiring regardless).
> - `m_enabled`/activation: the grow timer (§3.6) gates teleport by toggling the
>   `TeleportWorld` component enabled-state (or the trigger collider) — see §3.6.

### 3.6 Grow timer — plant → 15 s → activate (AT-GROW), relog-durable
A custom **`AncientPortalTag : MonoBehaviour`** on the piece (the cairn `CairnTag`
precedent — `Awake` grabs `ZNetView`/`WearNTear`, owner-gated ZDO writes, `InvokeRepeating`
poll):

- **Stamp plant time on placement.** First time the tag wakes with no stamp, owner-write the
  current network time into a ZDO key:
  - `const string ZdoPlantTime = "SBPR_PortalPlantTime"` (LOCK + never rename — a save/wire
    contract like `SBPR_TableName`).
  - Write: `if (!nview.IsOwner()) nview.ClaimOwnership(); zdo.Set(ZdoPlantTime, ZNet.instance.GetTime().Ticks);`
    Use **`ZNet.instance.GetTime().Ticks`** — the persistent network wall-clock vanilla uses
    for pregnancy/spawn/death timers (`assembly_valheim:22836`, `:4035`). Stored as `long`
    Ticks, it **survives relog mid-grow** (AT-GROW relog clause) because it's absolute
    world-time, not session-relative.
- **Poll grow progress.** `InvokeRepeating` at ~0.25 s (the cairn uses 1 Hz; grow wants a
  smoother scale-lerp so 0.25 s or an `Update` lerp is fine):
  - `elapsed = (ZNet.instance.GetTime() - new DateTime(plantTicks)).TotalSeconds`
  - `t = Mathf.Clamp01(elapsed / 15f)`
  - **Scale-lerp:** `transform.localScale = Vector3.Lerp(seedScale, fullScale, t)` where
    `seedScale ≈ fullScale * 0.1` and `fullScale` = the 3×3 m envelope (art brief §grow-fake).
  - **Inert until grown:** while `t < 1`, keep the `TeleportWorld` teleport DISABLED (toggle
    `teleportWorld.enabled = false` and/or disable the trigger collider, §3.7) so it cannot
    teleport mid-grow. At `t >= 1`, enable it ONCE and stop polling (`CancelInvoke`).
- **Owner-authority + ghost guard:** every ZDO read/write guards on a live ZDO
  (`nview?.GetZDO() != null`) so the placement GHOST (no ZDO) is a no-op — the cairn
  discipline (`CairnTag.cs:105`/`:114`).
- The grow is **placeholder** (scale-lerp, no bespoke roots-assembling animation — that's a
  later art pass, art brief §deferred).

### 3.7 The overhead horizontal trigger — the main novel-geometry risk (AT-JUMP-ACTIVATE)
Vanilla activation is **not** an E-press to teleport — it's a trigger volume. A
**`TeleportWorldTrigger` MonoBehaviour** (`assembly_valheim:123135`) sits on a child with a
`BoxCollider { isTrigger = true }`; its `OnTriggerEnter` (`:123142`) calls
`m_teleportWorld.Teleport(player)` when a `Player`-layer collider enters. (The E-press
Interact on `TeleportWorld` only opens the **tag-naming** dialog — `:122955` — not teleport.)

So the design's "jump up into the horizontal ring" = **position + size that trigger collider
horizontally, overhead**:
- Build a child GameObject under the ring, `AddComponent<BoxCollider>` with `isTrigger = true`,
  `AddComponent<TeleportWorldTrigger>` (it `GetComponentInParent<TeleportWorld>()` in its
  Awake — `:123140` — so parent it under the piece root that holds `TeleportWorld`).
- **Placement:** centered on the ring at **~3 m up**, lying flat (the ring plane).
- **Size (the risk):** wide enough to catch a jump-through (~ring footprint, ~2.5–3 m across)
  but with **vertical slack tuned so a jump apex registers and a walk-underneath does NOT.**
  A ~1.8 m player jumping rises ~1 m → head/collider reaches ~2.8–3 m. Make the trigger box
  ~0.6–1.0 m tall centered at the ring height so the apex clips it but standing/walking under
  (head at ~1.8 m) misses. **These exact numbers are not lockable from the desk** — they
  depend on the player capsule height + jump impulse + collider center. **FLAG RED: the
  engineer tunes these on a joined client and Daniel verifies AT-JUMP-ACTIVATE in-game.**
- Gate by grow: keep the trigger collider (or `TeleportWorld.enabled`) OFF until grow
  completes (§3.6) so you can't teleport through a half-grown portal.

---

## 4. Registration + wiring order (Registrar, PatchCheck)

New vertical slice `Features/Portals/`:
- **`Portals.RegisterPrefabs(zns)`** — build + register both prefabs (Seed item via
  `ConstructItemShell` → `RegisterItemInObjectDB`; Portal piece via `ConstructPieceShell` →
  `RegisterPrefabInZNetScene`). **Register the portal prefab hash into
  `Game.instance.PortalPrefabHash`** here or at the first safe point where `Game.instance`
  exists (§1 — guard for null; `Game.Awake` may run before/after our hook depending on phase,
  so do it idempotently and re-assert if needed).
- **`Portals.DoObjectDBWiring(zns)`** — add the Seed recipe (after the Seed item is in ODB),
  rebuild the Portal piece's `m_resources = { BuildReq("SBPR_PortalSeed", 1) }` (now that the
  Seed resolves), and add the Portal piece to the **Hammer** PieceTable
  (`AddOrReplacePieceByName`).
- **Wire into `Registrar`** (`Runtime/Registrar.cs`): add `Portals.RegisterPrefabs` to the
  `RegisterPrefabs` fan-out (after `Trailhead` so the bench exists for the recipe station)
  and `Portals.DoObjectDBWiring` to the `DoObjectDBWiring` fan-out **after `Trailhead`**
  (Hammer table) — mirror how the Bench/Lamp are ordered.
- **`SpecCheck.cs`** — add the two manifest rows (§0). Item row checked by the
  `s.Item != null` loop; piece row by the `s.Piece != null` loop.

> **PatchCheck (the "unregistered patch ships dead" lesson, t_564f695a):** *if* the impl adds
> any Harmony patch (it should NOT need one — TeleportWorld + trigger + ZDO timer are all
> non-Harmony component wiring), every `[HarmonyPatch]` class MUST be handed to
> `harmony.PatchAll(typeof(...))` in `Plugin.Awake()` or PatchCheck ERRORs at boot. The clean
> design here is **patch-free** — flag it if you find yourself reaching for a patch, because
> that's a sign something's being done subtractively.

---

## 5. Cross-doc updates (spec-first rule — move in the SAME PR)

This spec PR also updates (none are code, all are the spec/docs half of spec-first):
- **`docs/v2/planning/index.md`** + **`README.md`** — add this file's row/blurb.
- **`docs/datasets/PIECES_AND_CRAFTABLES.md:317`** — the "Pocket Portal / Twisted Portal
  (Trailborne v3+)" future line: split out the **Portal Seed → Ancient Portal as a v2
  Black-Forest entry** (retheme the name; it's no longer v3+, and no longer "Pocket Portal").
  Add the proper item+piece rows to the dataset's main tables per its format.
- **`docs/design/design-pillars.md:33`** — carve the Hammer exception: pocket/portal pieces
  place via the **Hammer** (Daniel 2026-06-13), NOT the Spade. Note it as a deliberate
  exception to Pillar 1 (a deployable convenience, distinct from trail-marking tools).
- **`docs/design/pocket-portal.md`** — flip status `proposed → specced` and add a one-line
  pointer to this impl spec (don't rewrite the design; just link forward).
- **`docs/design/nomap.md` §6** — the old "Pocket Portal" / "clone portal_wood" note is now
  superseded; add a one-line "→ see ancient-portal-impl-spec.md (additive, not clone)" pointer.
  Do not delete the historical note; annotate it.

These are the doc moves; the engineer's impl PR carries the code + SpecCheck rows. (Per
sbpr-docs-conventions: every folder keeps its README.md narrative + index.md manifest in
sync — update both for `docs/v2/planning/`.)

---

## 6. Open knobs (architect decisions + the ones that are genuinely Daniel's)

The design doc left 5 small knobs. Four are reversible architect tuning calls; **#3 (HP) is
NOT resolved — it's been corrected back to an open Daniel decision** (the earlier "175 / lean
150–200" was an author fabrication, never Daniel's word — retracted 2026-06-13). None block
building.

| # | Knob | Resolution | Justification |
|---|---|---|---|
| 1 | Where the Seed crafts | **Explorer's Bench** | Daniel's lean (pre-make at home, carry out). It's the station every SBPR recipe already uses; zero new surface. The "no bench" promise is about *placing*, not crafting. |
| 2 | Stack size at 25 kg | **`m_maxStackSize = 1`** | 5× = 125 kg defeats the point. One-per-slot is the "you carry ONE portal" pack commitment. |
| 3 | Fragility HP | 🔴 **OPEN — Daniel's call. Build defaults to vanilla 400** | The design doc says only "more fragile than vanilla"; it names **no number**. A prior draft invented a "150–200 lean" and locked 175 — that was never Daniel's and is retracted. Until Daniel picks a value, ship the honest vanilla default (400) rather than a fabricated number. §3.3. |
| 4 | Break→seed scope | **Always returns 1 seed** (every destroy path) | Daniel's lean + it's the *free* behavior of `Piece.DropResources` with a 1-seed recoverable cost (§1). Implementing "deconstruct-only" would mean ADDING a custom `m_onDestroyed` to SUPPRESS drops on death — more code for a worse fantasy. Always-returns is both simpler and the better game. |
| 5 | Hammer vs Spade | **Hammer** (+ reconcile pillars doc) | Daniel's 2026-06-13 explicit word ("regular hammer") overrides `design-pillars.md:33`'s Spade-only line. Spec updates the pillar doc to carve the exception (§5). Spade is a one-line swap if Daniel reverses. |

Plus two confirmed-by-Daniel constraints folded in 2026-06-13: **no rain decay** (§3.3) and
**solid-earth-only placement, not on structures** (`m_groundOnly`, §3.4b). And the
playtest-contingent **Ectoplasm** substitution (recipe eyes/cores) — **not built**, noted
only (§2.3).

---

## 7. Named acceptance tests (the single source of truth for "done")

Observable criteria. **logs-green ≠ playable** — every AT closes only on Daniel placing +
using one in-game on a joined client (repo honesty rule). The engineer reports per-AT status
in the PR handoff; the build PR does NOT self-close these.

- **AT-SEED-CRAFT** — Portal Seed crafts at the Explorer's Bench from `AncientSeed ×1 +
  GreydwarfEye ×20 + SurtlingCore ×2`; the item weighs 25 kg; `m_maxStackSize = 1`. SpecCheck
  row 1 present + green at boot (recipe + icon).
- **AT-SEED-FIELD-PLACE** — the Ancient Portal is placeable with the **Hammer**, with **NO
  crafting station in range** (no "needs workbench" block message). Costs exactly one Portal
  Seed.
- **AT-GROW** — on placement it is inert (cannot teleport) and visibly scale-lerps 0.1→1.0
  over ~15 s, then the portal activates. **Relog mid-grow** (log out at ~7 s, back in):
  it resumes at the correct progress and still completes (ZDO-stamped plant time, not
  session time).
- **AT-GEOMETRY** — the ring is horizontal (lying flat, faces up), ~3 m tall × ~3 m wide, the
  ring at the top of the ~3 m height (overhead). Reads as "roots grown into a portal" (legs +
  root tendrils + glowing ring).
- **AT-JUMP-ACTIVATE** (🔴 main risk) — **jumping up** into the ring teleports; **walking
  underneath** does NOT trigger. Tuned trigger box (§3.7), verified on a joined client.
- **AT-REGULAR-PORTAL** — two Ancient Portals given the **same 10-char tag pair** and teleport
  between each other (requires the §1 `PortalPrefabHash` registration — verify they actually
  CONNECT, not just place). **Ore/metal still blocked**: carrying copper/tin/iron refuses
  teleport with the vanilla message (we never set `m_allowAllItems`).
- **AT-FRAGILE** — durability is the configured `m_health` (🔴 OPEN knob — defaults to vanilla
  400 until Daniel picks a lower value, §3.3/§6); the portal is destroyable by combat /
  deconstruct. (Verify against whatever value is set in code, not a hardcoded number here.)
- **AT-PLACE-SOLID-EARTH** — the placement ghost is **valid on terrain** (dirt/grass/rock) and
  **REJECTED (red `Invalid` ghost) on any built structure** — wood floor, stone floor, any
  piece (`m_groundOnly`, §3.4b). Daniel's "solid earth, not structures." Verify in-game.
- **AT-BREAK-TO-SEED** — destroying it (deconstruct AND creature-kill) drops **exactly one**
  replantable Portal Seed (not its mats, not rubble). The dropped seed **replants into a
  working portal** (full grow → activate → pairs). Confirm a creature-killed portal returns
  ×1 (not 0 via the non-player-built ÷3 path — §1).
- **AT-ADDITIVE** (regression) — the prefab is built additively: no `Instantiate(portal_wood)`,
  no inherited `PlayerBase` EffectArea (no 20 m rested sphere appears around it), no
  `GuidePoint` (Hugin does NOT pop a portal tutorial on placement), no ZNetView-clone
  soft-lock. PatchCheck green; the feature ships **patch-free**.
- **AT-NRE-CLEAN** — no NullReferenceException spam in the log from `TeleportWorld.Update`
  (`m_model` wired) or `UpdatePortal` (`m_proximityRoot`/`m_target_found` consistent). A clean
  boot + place + grow + teleport produces zero portal-related NREs.
- **SpecCheck** — both rows (§0) present and green at boot; recipe-manifest count +2; the Seed
  icon is real (not the fallback). **logs-green ≠ playable** — Daniel's in-game pass is the
  real gate.

### Build gate
`dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` → **0 errors, 0
warnings** (`TreatWarningsAsErrors` ON). PR opened against **`v1`**, `[hold]` for Daniel's
merge — never self-merge.
