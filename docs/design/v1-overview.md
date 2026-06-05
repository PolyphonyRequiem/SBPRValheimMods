---
title: "Trailborne v1 — Vision, Features & Asset Needs"
status: living
purpose: Short-form overview of the v1 vision, the feature set, the element roster, and the assets (with variants) still needing real art. Grounded in trailborne-vision.md, design-pillars.md, and PIECES_AND_CRAFTABLES.md.
---

# Trailborne v1 — Vision, Features & Asset Needs

> A short, shareable overview. The canonical sources are
> [`trailborne-vision.md`](trailborne-vision.md) (thesis),
> [`design-pillars.md`](design-pillars.md) (non-negotiable constraints), and
> [`../datasets/PIECES_AND_CRAFTABLES.md`](../datasets/PIECES_AND_CRAFTABLES.md)
> (catalog). This doc summarizes; those govern.

---

## 1. The Vision

**Trailborne reframes Valheim around an Explorer role.** Vanilla hands you a
perfect satellite map after twenty minutes of walking. Trailborne takes that
away — and gives you something better: a world you have to *learn to see*.

On a server running Trailborne, the map you press **M** to view is mostly fog.
Walking no longer reveals terrain. The only way to see the world is to **draw
it** — and to draw it, someone has to go look. That someone is your **Explorer**:
the player who carries the Trailblazer's Tools, mixes pigments, paints signs on
the trails, raises cairns on the ridgelines, and makes the maps the rest of the
group reads.

**Deeper, not harder.** Same world, same bosses, same loot — but seeing it
becomes its own gameplay loop. We are not adding pain; we are adding a place for
a kind of attention Valheim has never quite supported.

**Standalone + server-gated.** Trailborne is a publicly-released Thunderstore mod
any server can install. Its gameplay-altering patches check whether the server
opted into Trailborne behavior before firing, so a player who installs it and
joins a vanilla server sees it sit silently inert — no conflicts. Niflheim is
*one* SBPR server that runs it, not its only home.

### Design pillars (load-bearing)

1. **The Trailblazer's Tools is a peer, not an extension.** The Hammer raises
   walls, the Hoe levels ground — the Tools *paves the world between
   settlements*. It has its own build menu, its own categories, its own pieces.
   Everything an Explorer places — paths, signs, cairns, lamps (and future
   beacons, pocket portals) — lives on the Tools, **never** the Hammer. The Hoe
   and Hammer stay unmodified; we add a peer, we don't replace.
2. **Color semantics are emergent.** Pigments are biome-tiered because their
   *ingredients* are, but the colors carry **no author-assigned meaning**.
   Players decide what red or blue means on their server. No tooltip, hint, or
   guide ever prescribes "red = danger." Pigment names are color names, never
   semantic ones.

---

## 2. The v1 Feature Set

| Feature | What it does |
|---|---|
| **The no-map world** | Minimap defogging is disabled; the M-map is mostly fog. `nomap=ON` → no map at all; `nomap=OFF` → minimap only (no M-key, no north indicator). The Cartography Table is disabled. |
| **The Explorer's Bench** | Meadows-tier crafting station that gates the entire Trailborne progression — every Trailborne item/piece is crafted here. |
| **The Trailblazer's Tools** | The Explorer's signature tool. Holds the Trailborne build menu and lays paths (1.5 / 3 / 5 m widths) + replants grass. Peer of Hoe/Hammer. |
| **Pigments & Painted Signs** | Craft inks from biome ingredients; place plain signposts and paint them by applying ink after placement. Each sign is also a map pin. Text via the vanilla write dialog. |
| **Cairns** | Maintained, comfort-emitting stone landmarks placed along trails. Five tiers, color-bound to a Cairn Marker, with mandatory weather decay + repair/upgrade — a lifecycle rehearsal for the future Guardian Stones mod. |
| **Path Lamps** | Trail-illumination light source — dimmer than a torch, longer fuel, manual ignition — for marking routes after dark. |

---

## 3. The v1 Element Roster

Canonical catalog + recipes live in
[`PIECES_AND_CRAFTABLES.md`](../datasets/PIECES_AND_CRAFTABLES.md); this is the
short version.

### Crafting station

- **Explorer's Bench** — `SBPR_ExplorersBench` · Meadows · built at the vanilla
  Workbench · **10 Wood + 4 Stone + 1 Deer Trophy**. Crafting hub for all of
  Trailborne.

### Pieces (placed in the world via the Trailblazer's Tools menu)

- **Cairn (Tiers I–V)** — `SBPR_Cairn_T1`…`T5` · Meadows · **build T1: 3 Stone +
  1 Resin + 1 Cairn Marker**; **upgrade/repair: 3 Stone + 1 Resin flat per
  tier**. Comfort floor 3/4/5/6/7 by tier; pristine ≥75% (glows), fizzled <75%,
  downgrades <25%, collapses at 0%. Color-bound to its Marker's pigment.
- **Painted Sign** — `piece_sbpr_sign` (single prefab; color is per-instance ZDO
  state) · Meadows · **2 Wood**, placed unpainted. Painted after placement by
  applying an ink; re-applying repaints. Persists + syncs via ZDO. Also a map
  pin.
- **Path Lamp** — `SBPR_PathLamp` · Black Forest (corewood gate) · **Corewood +
  Resin** (quantities TBD). Fueled, dimmer-than-torch trail light.

### Items

- **Trailblazer's Tools** — `SBPR_Item_TrailblazersTools` · Meadows · **5 Wood +
  2 Flint + 2 Leather Hides**. The build-menu tool; lays paths + replants grass.
- **Cairn Marker** — `SBPR_Item_CairnMarker` · Meadows · **2 Leather Scraps + 1
  Finewood + 1 Pigment**. Consumed on cairn placement; its pigment binds the
  cairn's color.
- **Pigments (Red / White / Black / Blue)** — `SBPR_Item_Pigment{Red,White,Black,Blue}`
  · Meadows (R/W/B), Black Forest (Blue) · **1 Raspberry→2 Red**, **1 Bone
  Fragment→2 White**, **1 Coal→2 Black**, **1 Blueberry→2 Blue**. Bind cairn
  color at craft; paint signs after placement.

### Patched vanilla entities

- **Cartography Table** (build + function disabled) · **Minimap** (nomap config)
  · **Vanilla Sign** (`Sign.UseItem` patched for the paint-via-ink flow).

> **Naming note:** "Trailblazer's **Tools**" is the umbrella tool/menu concept
> (per the design pillar); the shipped v1 item is sometimes called the
> "Trailblazer's **Spade**" in code (`SBPR_TrailblazersSpade`). Reconciling this
> is tracked separately — treat the dataset's prefab names as authoritative once
> that lands.

---

## 4. Assets That Need Creating (with Variants)

**Current art reality:** v1 ships on **placeholder icons** and **runtime
kitbashes of vanilla prefabs** — there is no custom-authored 3D art yet. v1's
explicit approach is "kitbash for playtest: playtest-quality mechanics ≠
ship-quality art." This section is the backlog of real art to replace those
stand-ins.

### 4a. Icons (2D) — build-menu / inventory sprites

Six placeholder PNGs exist today under `assets/icons/items/` (`*_v0.1.png`).
They work but are stand-ins.

| Asset | Variants | Count | Current state |
|---|---|---|---|
| Pigment / Ink icons | Red, White, Black, Blue | **4** | placeholder `ink_*_v0.1.png` |
| Cairn Marker icon | one (or ×4 if color-forked — TBD) | **1–4** | placeholder `cairn_marker_v0.1.png`; per-color split is an open question |
| Trailblazer's Tools icon | one | **1** | placeholder `trailblazers_spade_v0.1.png` (also wrongly reused for path-op pieces) |
| Explorer's Bench icon | one | **1** | none authored (kitbash piece icon) |
| Painted Sign icon | one | **1** | none authored |
| Path Lamp icon | one | **1** | none authored |
| Cairn build/upgrade icon(s) | one, or per-tier (TBD) | **1–5** | none authored |
| Path-op icons (spade menu) | 1.5 m / 3 m / 5 m path + 1.5 m / 3 m / 5 m Replant (×3 widths, decided 2026-06-05) | **6** | currently the spade icon placeholder; want distinct per-op icons |

### 4b. 3D Meshes / Prefab Art — currently kitbashed from vanilla

| Asset | Variants | Count | Current kitbash | Real-art note |
|---|---|---|---|---|
| **Cairn** | 5 tiers (stone count grows T1→T5) | **5 visual states** | runtime stack of `rock_low` clones on a `bonfire` base | ⚠️ The bonfire base currently shows *through* (flames visible) — known bug. Real art = a proper stone-cairn mesh per tier, color-tintable runic cap. |
| **Cairn color tint** | Red, White, Black, Blue | **4** | runtime pigment tint on the runic cap | colorway, not a separate mesh |
| **Explorer's Bench** | one | **1** | kitbashed `piece_workbench` | wants antlers from the Deer Trophy integrated *into* the mesh, half-rolled hide-map, bone-needle-in-stone-disk (per `nomap.md` §1) |
| **Painted Sign** | one mesh + 4 paint colorways | **1 + 4** | vanilla `sign`, runtime-tinted | wants a free-standing ~2 m signpost (pole + board); board takes the paint |
| **Path Lamp** | one | **1** | vanilla `groundtorch_wood`, tinted/scaled | wants a 3× taller standing lamp silhouette, pivot raised to ground level |
| **Trailblazer's Tools** | one held-tool mesh | **1** | clone of vanilla Hoe/Hammer | wants its own wanderer's-tool model |
| **Pigment / Ink** | Red, White, Black, Blue (held/dropped item) | **4** | vanilla item mesh | low priority — icon matters more than world mesh |

### 4c. Variant Summary (what "with variants" means here)

- **Pigments / Inks:** ×4 colors → 4 icons (+ 4 item meshes, low priority).
- **Cairn Markers:** ×1 base — **open question** whether color forks the item (→ ×4) or stays metadata (→ ×1).
- **Cairns:** ×5 tier visual states **and** ×4 color tints (tint is a material pass, not 5×4 = 20 separate meshes).
- **Painted Signs:** ×1 mesh + ×4 paint colorways (runtime tint).
- **Path ops (spade menu):** ×3 path widths + ×3 Replant widths (1.5/3/5m, decided 2026-06-05) → 6 distinct op icons.
- **Everything else** (Explorer's Bench, Path Lamp, Trailblazer's Tools): ×1 each.

> **Deferred / future (not v1 art):** Ember Lamps, Beacons (v1.1); Yellow pigment
> (Plains-gated); Map Station, Real Tents (v2); Pocket/Twisted Portals, Iron
> Compass (v3+); Seer's Stone (v4+). Listed so the asset pipeline knows what's
> coming, but **none are v1 deliverables.**
