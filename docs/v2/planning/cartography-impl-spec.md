---
title: "Trailborne v2 (Black Forest) — Cartography buildable implementation spec"
status: current
purpose: "Per-feature, build-ready implementation specs for the v2 cartography features (Surveyor's Table, Local Map + viewer, Cartographer's Kit, and §3.5 the mod-enforced NoMap precondition). Each section gives observable acceptance criteria, the exact vanilla hooks, the feature-folder it lands in, and its SpecCheck manifest row. Authored by the architect spec-pass (card t_4be278de) once all open items locked; §3.5 NoMap enforcement added by card t_8c9abf6f (2026-06-11). Implementers (engineer-systems / engineer-ui) build from THIS doc; requirements.md is the what, this is the how-to-pick-it-up-cold."
---

# Trailborne v2 cartography — buildable implementation spec

The requirements doc ([`requirements.md`](requirements.md)) is the locked *what*. This
doc is the *buildable how*: one tight section per feature an implementer can pick up
cold, with the vanilla hooks carried forward from the design doc
([`../../design/cartography-v2.md`](../../design/cartography-v2.md)), observable
acceptance criteria, the feature-folder placement, and the SpecCheck manifest impact.

> **Clean-side note (ADR-0001):** every vanilla decomp line cited here is the base game
> (`assembly_valheim`), which is fair game to read and adapt. The reference mods
> (`NomapPrinter`, `BetterCartographyTable`) are studied for *approach only* — reproduce
> behavior from vanilla primitives, never copy their code.
>
> **Spike dependency:** the viewer render path (Local Map §2B) is gated on the UI-fork
> spike (`t_e8bbbe48`) whose findings doc lands at
> `docs/v2/investigations/2026-06-10-bounded-map-ui-fork-spike.md`. Where this spec says
> "per the spike," the spike's confirmed `m_pixelSize` and render path are the build
> truth. If the spike returns BLOCKED, the Local Map viewer card re-specs against
> whatever wall it hit — the Table and Kit cards are independent of that risk.

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. Today it carries only the
v0.1.0 Meadows manifest. These three features add **+3 entries** (all new):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `piece_sbpr_surveyors_table` | build piece | Wood-Fine ×10, Bronze ×2, DeerHide ×4, BoneFragments ×8 | (place via Spade menu; `m_craftingStation = null`) |
| 2 | `SBPR_LocalMap` | item recipe (amount 1) | DeerHide ×2, FineWood ×4 | `piece_sbpr_explorers_bench` |
| 3 | `SBPR_CartographersKit` | item recipe (amount 1) | InkRed ×10, InkWhite ×10, InkBlue ×10, InkBlack ×10, FineWood ×4 | `piece_sbpr_explorers_bench` |

**Resource prefab-name caveats (must match vanilla / existing SBPR consts, or SpecCheck
flags a NULL `m_resItem`):**
- Fine Wood = vanilla internal id **`FineWood`** (the existing cairn-marker manifest row
  uses `FineWood`; confirm the Table piece uses the same string).
- Deer Hide = vanilla **`DeerHide`** (verified, wiki Internal ID + decomp item).
- Bone Fragments = vanilla **`BoneFragments`**. Bronze = **`Bronze`**.
- The four pigments are SBPR items whose **wire/prefab names are still the historical
  `SBPR_Ink{Red,White,Blue,Black}`** (see `Pigments.cs:40-43` — the consts say "Pigment"
  but the VALUES are `SBPR_Ink*` and must not change). Reference them via
  `Pigments.PigmentRedName` etc., never a literal.

Each impl card adds **only its own row** in the same PR as its code (spec-first rule:
code + spec + SpecCheck move together). The card that touches `SpecCheck.cs` first should
also update the class's `LOCKED SOURCE` comment to cite `docs/v2/planning/requirements.md`
alongside the v0.1.0 source, and generalize the "v0.1.0 locked manifest" wording.

**Asset-renderability is now part of the watchdog (icon-crash fix, C1).** `SpecCheck.Run()`
additionally asserts that every SBPR item recipe's resolved `ItemDrop` has its real icon
loaded — concretely that `m_icons[0]` is **not** the shared `Assets.FallbackIcon`
placeholder. This closes the "server-green recipes, client-icon-missing" divergence that
hid the Kit no-cost crash: an additive item with no loaded icon shows the magenta fallback
and the server now logs `ICON MISSING` at boot instead of silently shipping a crash. This
is an **asset check, not a recipe row** — the recipe-manifest count is unchanged (the icon
checks are tallied separately in the boot summary line).

> **Drift-watchdog gotcha:** `SpecCheck.Run()` iterates `Manifest.Where(s => s.Piece !=
> null)` for build pieces and `s.Item != null` for item recipes — a new `RecipeSpec`
> with both null (or both set) won't be checked. The Table is `Piece` only; the Local Map
> and Kit are `Item` only. Match that shape.

> **New wire-contract keys (issue 10, Table naming — §1.6/§2A.6).** Two persisted keys are
> added; **neither touches the recipe manifest** (SpecCheck count unchanged), but both are save/wire
> contracts — LOCK on first ship, NEVER rename (renaming orphans named Tables / named maps):
> | Key | Carrier | Type | Meaning |
> |---|---|---|---|
> | `SBPR_TableName` | Surveyor's Table ZDO | string | the Table's player-given name (owner-write) |
> | `sbpr_map_name` | Local Map `m_customData` | string | imprinted Table name stamped on the item |
> Document each in the field-contract comment block of its owning file (`SurveyorTableTag` /
> `LocalMap`), exactly as `sbpr_map_blob` / `SBPR_MarkerType` are documented today.

---

## 1. Surveyor's Table — placed station retaining a shared 1000 m survey

> **IMPL STATUS (2026-06-10, card t_2715661d, engineer-systems):** built additively on
> branch `feat/surveyors-table-t_2715661d`; build 0/0; SpecCheck row 1 added. Code +
> spec + manifest move together (this PR). **Two build-card deviations from the sketch
> below, flagged for review:** (a) §1.1 sketches "one `Switch` (Use → open viewer)"; the
> impl instead implements `Hoverable + Interactable` directly on `SurveyorTableTag` — the
> repo's proven in-production interactable pattern (`CairnInteractable`), avoiding a
> child-collider/`Switch`-delegate wiring layer for the same behaviour. (b) The forked
> VIEWER itself is the downstream card `t_7b616020` (engineer-ui), so this card ships the
> survey DATA + ZDO persistence + contribute/merge + ward gate + pin-removal BACKEND +
> a `CartographyViewer` seam the viewer plugs into; until that card lands, Use records +
> persists the survey and shows a "viewer not installed yet" message (no data lost). The
> windowed-fog cell math (`BoundedMapMath`) is the productionized, executed-proven spike
> seam (`t_e8bbbe48`). **logs-green ≠ playable — Daniel verifies in-game.**

**Lands in:** `Features/Cartography/SurveyorsTable.cs` (registration) + `SurveyorTableTag.cs`

### 1.1 Construction (ADR-0006 additive — hard constraint)
- `new GameObject("piece_sbpr_surveyors_table")` + `AddComponent` of exactly: `Piece`,
  `WearNTear`, `ZNetView`, one `Switch` (Use → open viewer), and a custom
  `SurveyorTableTag : MonoBehaviour`. **Do NOT `Instantiate` the vanilla `maptable`
  prefab** (it carries a `ZNetView`; cloning ZNetView-bearing prefabs is the ADR-0006
  anti-pattern that caused the v0.2.7 ZDO-orphan soft-lock).
- Read vanilla `maptable` (decomp `MapTable` :114014) as a **blueprint only** —
  `vprefab inspect maptable` for mesh/material/`EffectList`/field values; reference-copy,
  never instantiate. `ZNetScene.GetPrefab` fires no `Awake`, so reading is safe.

### 1.2 Placement + recipe (LOCKED)
- Placed via the **Trailblazer's Spade build menu** (Pillar 1 — never the Hammer).
  Register the piece onto the Spade's `PieceTable` the same way Signs / Path Lamp / Cairns
  do (see `Trailblazing.cs` / `Trailhead.cs` for the existing pattern).
- **`piece.m_craftingStation = null`** — NO Explorer's Bench required in range to place.
  This matches every existing Spade-placed SBPR piece (`Signs.cs:270`,
  `Trailhead.cs:186` set it null). Do not set a station.
- **Build cost (Piece.m_resources):** FineWood ×10, Bronze ×2, DeerHide ×4,
  BoneFragments ×8. Black-Forest tier.

### 1.3 Stored data — windowed, shared, cumulative (C5 + the over-provisioning fix)
- Persist the survey **compressed in the Table's ZDO** byte array `ZDOVars.s_data`
  (`Utils.Compress` / `Decompress`) — exactly as vanilla `MapTable` does, so save/load is
  inherited and the format stays interoperable.
- The stored blob is **NOT** vanilla's full-world `GetSharedMapData` 256² array. It is the
  **windowed fog array** (the shared §2C format): the ~32×32 native-resolution window
  around the Table's origin + the bound-origin world coord + the pin list, clipped to the
  Table's own 1000 m disc.
- **Write (any surveyor):** merge the writer's explored cells *that fall inside 1000 m of
  THIS table* + their shareable pins inside the disc into the stored window. Beyond-1000 m
  is dropped (C5 — a Table is a locally-bounded shared survey, not a global map). Owner
  persists via the Table's `ZNetView` (`InvokeRPC` to the owner like vanilla `MapTable`,
  or owner-side write if the interacting player owns the ZDO).
- **Cumulative:** writes OR-merge into the existing window, never overwrite (AT-TABLE-SHARED).

### 1.4 Use → open the viewer with pin editing (D4)
- The Table's `Switch.Interact` opens the **same forked viewer** the Local Map opens
  (§2B), bound to this Table's 1000 m disc — but operating on the Table's **SHARED** blob
  and with **pin REMOVAL enabled**. Field Local-Map view = read-only; Table view = edit.
- Pin removal hook: vanilla `Minimap.RemovePin(PinData)` (decomp :48408) and
  `RemovePin(Vector3 pos, float radius)` (:48366) via `GetClosestPin`. The forked viewer's
  Table mode wires a click → `RemovePin` against the Table's pin list (not the player's
  global `m_pins`). Per-pin-add sharing (C8) is the separate pin-sharing surface; removal
  here operates on the shared record.
- **Ward-gated:** every read/write/remove checks `PrivateArea.CheckAccess` (vanilla
  `MapTable` does this; reproduce it). A Table in a ward is locked to those with access.

### 1.5 Acceptance criteria (observable — close only on Daniel's in-game check)
- **AT-TABLE-SHARED** — two surveyors writing to one Table build a combined record of its
  1000 m disc; fog/pins beyond 1000 m of the Table are not stored.
- **AT-TABLE-PINEDIT** — the Table view permits pin removal on the shared data; the field
  Local-Map view does not.
- **AT-TABLE-PERSIST** — placed Table's recorded fog+pins round-trip across a dedicated-
  server restart (ZDO blob, compressed).
- **AT-TABLE-PLACE** — the Table places from the Spade menu with NO Explorer's Bench in
  range (no `m_craftingStation` block message).
- **AT-TABLE-WARD** — a Table inside a ward is read/write-locked to non-permitted players.
- SpecCheck row 1 present; `[hold]` PR; logs-green ≠ playable.

### 1.6 Table naming + name-gated binding (issue 10, 2026-06-11)

> **Status: NEW DESIGN.** Daniel, v0.2.19-playtest: *"surveyors tables should be required to
> be named prior to binding maps to them. The item name should bear that surveyors table name
> and show the title while the map is open."* Adds a per-instance Table NAME, a naming GATE on
> imprint, name inheritance onto the Local Map item (§2A.6), and a viewer title (§2B.1). Clean-side
> (ADR-0001): vanilla `TextInput`/`TextReceiver` rename dialog + owner-write ZDO are base game.
> **Daniel decision (2026-06-11, comment thread): this card is UNRELATED to the marker-namable
> card (t_62af5802) — do NOT build a shared naming helper and do NOT couple them. Spec this
> standalone.**

> **IMPL STATUS (2026-06-12, card t_41482aa3, engineer-ui):** built on branch
> `feat/table-naming-t_41482aa3` off `v1` (after the §2E/§2F/§2G/§2H viewer overhaul PR #123
> merged, as sequenced). Build 0 warn / 0 err. `SurveyorTableTag` now implements `TextReceiver`,
> carries the `SBPR_TableName` owner-write ZDO (read censored via `CensorShittyWords.FilterUGC`
> like vanilla `Tameable.GetText`), reflects the name in `GetHoverName`/`GetHoverText`, and gates
> `Interact`: an unnamed Table launches the vanilla `TextInput.RequestText(this, "$hud_rename", 32)`
> rename dialog instead of imprinting, and `ImprintCarriedLocalMaps` hard-returns on an empty name
> (the §1.6.4 backstop). Implementer choice taken: **always prompt-to-name an unnamed Table on Use**
> (the spec-sanctioned alternative — keeps the §1.6.2 unnamed hover "[Use] Name this table" literally
> true). Surveying stays un-gated. SpecCheck/manifest unchanged (no recipe rows). **logs-green ≠
> playable — Daniel verifies in-game (AT-TABLENAME-1/2, §1.7).**

**Lands in:** `Features/Cartography/SurveyorTableTag.cs` (name ZDO field + naming dialog + gate).

#### 1.6.1 The Table name — a per-instance owner-write ZDO string
- Add ONE new ZDO field on the Table, following the established wire-contract pattern already
  in the file (the `RpcSurveyData` / `ZDOVars.s_data` discipline) and in `MarkerSignTag`
  (`SBPR_MarkerType`/`SBPR_Pinned`):
  - **`SBPR_TableName`** (string) — the Table's player-given name. Empty/absent = unnamed.
- **Read:** `nview.GetZDO().GetString("SBPR_TableName", "")`. **Write (owner-authoritative):**
  `if (!nview.IsOwner()) nview.ClaimOwnership(); nview.GetZDO().Set("SBPR_TableName", name);`
  — the exact owner-claim shape `MarkerSignTag.WritePinned` uses (grounded against that file),
  NOT a raw `m_nview` poke. Guard every read/write on a live ZDO so the placement GHOST (no ZDO)
  is a no-op.
- **Wire contract — LOCK + NEVER RENAME.** `SBPR_TableName` is a save/wire contract the moment
  one Table is named in a live world; renaming the key orphans every named Table (same rule as
  `SBPR_Ink*` / `s_data`). Document it in the field-contract comment block of `SurveyorTableTag`.

#### 1.6.2 `GetHoverName` reflects the per-instance name
- `SurveyorTableTag.GetHoverName()` (`:72-77`) currently returns the static `piece.m_name`
  ("Surveyor's Table") for every instance. Change it to return the ZDO name when set, falling
  back to `piece.m_name` when absent:
  - named → e.g. `"Northern Outpost (Surveyor's Table)"` (name + base in parens, so the hover
    still reads as a Table — implementer's exact format; Daniel verifies the read);
  - unnamed → the existing `"Surveyor's Table"`.
- The ward-gated `GetHoverText` affordance (`:80-93`) is unchanged except it now shows the named
  hover name (it already calls `GetHoverName()`), and — when unnamed — its `[Use]` line states
  the gate (see 1.6.4): e.g. `"[Use] Name this table"` instead of `"Survey here / review…"`.

#### 1.6.3 The naming dialog — vanilla `TextInput`, no custom UI
- Reuse the **vanilla rename dialog** the game already uses for Tamed animals, Portals, and Signs:
  `TextInput.instance.RequestText(receiver, topic, charLimit)` where `receiver` implements the
  vanilla **`TextReceiver`** interface (`string GetText()` / `void SetText(string)`). Grounded
  against the decomp: `Tameable` (`assembly_valheim:27163`), `Sign` (`:121490`), and `TeleportWorld`
  (`:122967`) all drive renaming exactly this way; `TextInput.RequestText` (`:54895`) queues the
  receiver and shows the panel, and on confirm calls `receiver.SetText(typed)` (`:54888-54891`).
  - **Implementer choice (both clean-side, both fine):** (a) make `SurveyorTableTag` itself
    implement `TextReceiver` — `GetText()` returns the current `SBPR_TableName`, `SetText(name)`
    owner-writes it (1.6.1); or (b) a tiny dedicated receiver object. (a) is simplest and matches
    `Tameable`'s "the component is the receiver" shape. **Do NOT** build a bespoke uGUI text panel
    — the card's `SignPaintPanel`/`MarkerSignPanel` references are heavier surfaces for color/icon
    editing; the vanilla `TextInput` is the right, minimal tool for a single name string.
  - **Topic + char limit:** a plain-English topic string (e.g. `"Name this Surveyor's Table"`,
    NOT a custom `$token` — a custom `$piece_*` token leaks as a literal, the 2026-06-05 sign bug;
    vanilla tokens like `$hud_rename` are fine if a suitable one exists, implementer confirms).
    Char limit ~32 (Tameable uses 10, Sign uses its `m_characterLimit`; 32 gives room for a place
    name). Confirm `TextInput`'s exact members live in the build assembly before wiring.
- **`SetText` runs the censor + persists owner-side**, then refreshes the hover. (Vanilla passes
  rename text through `CensorShittyWords.FilterUGC`; reproduce that on read or write so a named
  Table can't display unfiltered UGC — grounded at `Tameable.GetText` `:27181` and the
  `$item_crafter` censor `:58314`.)

#### 1.6.4 The bind gate — no nameless imprints (AT-TABLENAME-2)

> **⚠️ SUPERSEDED TRIGGER (2026-06-12, issue 6, §2I).** The imprint *trigger* below ("Use the
> Table → ... → `ImprintCarriedLocalMaps` imprints ALL carried maps") is **replaced** by the §2I
> look-at-table + hotbar-number gesture (imprint THE one map in the pressed slot). The **name gate
> itself is unchanged and still binding** — imprint (now via `TryImprintSlot`, §2I.4) still hard-
> refuses while `SBPR_TableName` is empty. Read §2I for the current trigger; the name-gate logic
> here remains the spec of record. Use (E) still launches the rename dialog on an unnamed Table.

- `Interact` (`:97-134`) currently always: ward-gate → `ContributeLocalSurvey` →
  `ImprintCarriedLocalMaps` → open viewer. **Insert a name gate** so a Table with an empty
  `SBPR_TableName` refuses to imprint and instead launches the naming dialog:
  1. Ward gate (unchanged — `PrivateArea.CheckAccess`, denied players never reach naming).
  2. **If `SBPR_TableName` is empty AND the user carries ≥1 blank/imprintable Local Map** (so
     naming is only forced when there's actually a map to bind — an unnamed Table the player just
     wants to *survey at* shouldn't nag): open the naming dialog (1.6.3), show a Center message
     like `"Name this table before binding maps"`, and **return without imprinting** this Interact.
     The next Interact (now named) proceeds to imprint. *(Implementer alternative, equally
     acceptable: always prompt-to-name an unnamed Table on first Use regardless of carried maps —
     Daniel verifies which feels right. The hard requirement is only that imprint NEVER happens
     while the name is empty.)*
  3. Always still allow `ContributeLocalSurvey` (surveying/recording is not name-gated — only
     *binding maps to the item* is; an unnamed Table can still accumulate the shared survey).
  4. Open the viewer as today (with the title — §2B.1).
- **`ImprintCarriedLocalMaps` (`:217-247`) is the hard backstop:** make it read the Table name and
  early-return (no imprint) when the name is empty, regardless of the `Interact` path — so even a
  future caller can't produce a nameless bind. It already no-ops when the survey is empty; add the
  same guard for an empty name. When it DOES imprint, it passes the Table name into
  `LocalMap.Imprint` (§2A.6).

#### 1.6.5 RE-naming an already-named Table (issue 3, 2026-06-15)

> **Status: NEW (spec gap fill).** Daniel, v0.2.25-playtest: *"issue 3: surveyor's table should
> support renaming."* §1.6 specced name-GATING (must name before bind) but had **no RE-name path** —
> once `SBPR_TableName` was non-empty, `Interact` fell straight through to survey + open-viewer with no
> way to change the name. This subsection adds the re-name affordance. Clean-side (ADR-0001): reuses the
> existing vanilla `TextInput`/`TextReceiver` machinery from §1.6.3 — no new surface.

- **Affordance — `[Use]+Alt` (architect call, 2026-06-15).** An already-named Table is re-named with
  the **`alt` modifier + Use** (KBM default Left-Shift + E; gamepad the layout-appropriate alt button).
  `SurveyorTableTag.Interact(Humanoid, bool hold, bool alt)` already receives `alt`; on a **named** Table
  an `alt` Use launches `RequestRename` and returns instead of opening the viewer.
  - **Why (a) over the other candidates:** it has a **direct vanilla precedent** — `Tameable.Interact`
    (decomp `:27075`) does exactly `if (alt) { SetName(); return true; }` for renaming a tamed animal,
    advertised with the same `[$KEY_AltPlace + $KEY_Use] $hud_rename` hover (`:27034`). The Surveyor's
    Table is **not a build piece**, so the `AltPlace` (Shift) modifier carries no competing meaning while
    hovering it — no gesture collision with survey/open or the §2I hotbar-imprint. The hold path stays
    `return false` (candidate (b) rejected — hold gestures are undiscoverable); no in-viewer control
    (candidate (c) rejected — heavier surface for a one-string edit).
- **Placement in `Interact` (order matters — preserves §1.6.4).** The re-name branch sits **after** the
  unnamed name-gate (§1.6.4), so it is reached **only when the name is already non-empty**. The
  unnamed→first-name flow is therefore byte-for-byte unchanged (AT-TABLE-RENAME-NOREGRESS). Survey is
  still contributed first (plain-Use parity), then: unnamed → first-name dialog; named + `alt` →
  re-name dialog; named + plain Use → open viewer (unchanged).
- **The dialog pre-fills the current name.** `TextReceiver.GetText()` already returns the current
  `SBPR_TableName`, so the vanilla rename field opens populated with the existing name — the player
  **edits** it (fix a typo) rather than retyping from blank. `SetText` owner-writes the new name through
  the same censor + `ClaimOwnership` path (§1.6.1); the hover refreshes on the next look-at poll. The
  `RequestRename` Center message is **context-aware**: the §1.6.4 bind-gate nag ("Name this table before
  binding maps") shows ONLY for an unnamed Table; a re-name shows no message (the pre-filled dialog is
  self-explanatory, matching vanilla `Tameable`'s silent alt→rename).
- **Hover advertisement (§1.6.2 pattern, AT-TABLE-RENAME-DISCOVERABLE).** The **named**-table
  `GetHoverText` gains a third line advertising the affordance, using the same gamepad/KBM key-token
  split vanilla derives `alt` from (decomp `:16115`):
  `[$KEY_AltKeys + $KEY_Use] $hud_rename` on the non-classic gamepad layout,
  `[$KEY_AltPlace + $KEY_Use] $hud_rename` otherwise. `$hud_rename` / `$KEY_*` are vanilla tokens (they
  localize; a custom `$piece_*` token would leak as a literal — the 2026-06-05 sign bug). The unnamed
  hover is unchanged ("[Use] Name this table").
- **🔴 Wire contract — re-name changes FUTURE imprints only (AT-TABLE-RENAME-NOMIGRATE).** Re-naming
  writes only the Table's own `SBPR_TableName` ZDO. Already-imprinted Local Maps carry **their own**
  `sbpr_map_name` copy stamped at imprint time (`LocalMap.Imprint` `:230`) — re-naming the Table does
  **not** touch them, and **no migration is performed or wanted** (a map records the Table name AT
  imprint time, by design — same rule §1.6.4 already implies). Subsequent imprints pick up the new name
  via `TryImprintSlot` → `GetTableName()` → `LocalMap.Imprint`. The `SBPR_TableName` key itself is still
  LOCK/never-rename (the key string, not the player value — §0 "NEVER rename" is about the prefab/key,
  not the instance name).

### 1.7 Acceptance criteria — Table naming (issue 10; observable, close only on Daniel's in-game check)

> The feature spans three files (`SurveyorTableTag` §1.6, `LocalMap` §2A.6, `MapViewer`/`CartographyViewer`
> §2B.1). These named tests are the single source of truth for "done"; §2D points here.

- **AT-TABLENAME-1** (named + persists) — A Surveyor's Table can be given a custom name via the
  naming dialog (§1.6.3); the name persists per-instance across relog AND a dedicated-server restart
  (owner-write `SBPR_TableName` ZDO). Its hover name reflects the custom name (§1.6.2).
- **AT-TABLENAME-2** (bind gate) — Attempting to bind/imprint a Local Map at an UNNAMED Table is
  refused (no `sbpr_map_name`/`sbpr_map_blob` written) and the player is prompted to name it first
  (§1.6.4). Naming, then re-Using, imprints normally.
- **AT-TABLENAME-3** (item bears the name) — After naming + imprinting, the Local Map ITEM's name in
  inventory hover bears the Table's name, formatted `Local map for Northern Outpost` (§2A.6/§2A.6c),
  distinguishable from other bound maps in the same pack. Confirmed it is the TITLE, not just a tooltip
  body line. *(Format re-locked by issue 4, 2026-06-15 — the issue-8 `Local Map of "<name>"` and the
  original `Map: <name>` wordings are both superseded; see the AT-MAPNAME-1…5 series in §1.7 / §2A.6c
  for the re-lock's own acceptance tests, including the render-race fix.)*
- **AT-TABLENAME-4** (field-view title) — Opening that Local Map's full view (equip + Map button)
  shows the Table's name as an on-screen title (§2B.1).
- **AT-TABLENAME-5** (Table-view title) — Opening the view at the Table itself (TableEdit mode) also
  shows the Table's name as the title (§2B.1).
- **AT-TABLENAME-6** (standalone, NOT shared with markers) — per Daniel's 2026-06-11 decision, the
  Table naming flow is implemented standalone (vanilla `TextInput`); it does NOT build or depend on a
  shared naming helper with the marker-namable card (t_62af5802). *(Supersedes the card's original
  AT-TABLENAME-6 "consistency with the marker mechanism" — Daniel ruled them unrelated.)*
- **AT-TABLENAME-7** (no orphan) — adding `SBPR_TableName` / `sbpr_map_name` does NOT orphan existing
  placed Tables or already-crafted/imprinted maps: a Table with no name key reads "Surveyor's Table"
  and an imprint-without-name (pre-1.6 map, or the gate disabled) shows the vanilla "Local Map" title.
  The display patches are pure pass-throughs when the key is absent.
- **AT-TABLENAME-8** (patch registered) — every new Harmony patch (the §2A.6b name-display
  Postfix(es)) is handed to `harmony.PatchAll(typeof(...))` in `Plugin.Awake()` and passes `PatchCheck`
  at boot (the t_564f695a "unregistered patch ships dead" lesson). No new patch is needed for §1.6/§2B.1
  (ZDO + `TextInput` + viewer-label are non-Harmony); only §2A.6b adds patch surface.
- **AT-TABLE-RENAME** (§1.6.5, issue 3 — re-name works + persists) — an already-named Table can be
  RE-named via `[Use]+Alt`; the new name persists (owner-write `SBPR_TableName` ZDO), survives relog AND
  a dedicated-server restart, and shows in the hover name (§1.6.2) + as the Table-view title (§2B.1).
- **AT-TABLE-RENAME-DISCOVERABLE** (§1.6.5) — the **named**-table hover advertises the affordance:
  a `[$KEY_AltPlace + $KEY_Use] $hud_rename` line (or the `$KEY_AltKeys` gamepad variant), localized
  (no literal `$`-token leak).
- **AT-TABLE-RENAME-NOMIGRATE** (§1.6.5 wire contract) — re-naming a Table does **not** retroactively
  rename already-imprinted Local Maps (each keeps its imprint-time `sbpr_map_name`); a map imprinted
  AFTER the re-name bears the NEW name. No migration runs.
- **AT-TABLE-RENAME-NOREGRESS** (§1.6.5) — the unnamed→first-name gate (§1.6.4) and the §2I hotbar
  imprint gesture are unchanged: an UNNAMED Table still prompts-to-name on plain Use and still refuses
  imprint; a plain (non-alt) Use on a NAMED Table still opens the viewer without renaming.
- SpecCheck impact: **none** (naming/UI behavior, no recipe rows — §0 manifest count unchanged).
  `[hold]` PR; logs-green ≠ playable — Daniel confirms in-game: name a table, bind a map, see the name
  on the item + as the viewer title. **Issue 3 (re-name): Daniel re-names a table in-game and confirms
  the new name sticks + old imprinted maps keep their names.**

---

## 2. Local Map — two-handed item + bounded forked viewer

> **IMPL STATUS (2026-06-10, card t_cb831069, engineer-ui):** built FULL (not MVP) on
> branch `feat/local-map-viewer-t_cb831069` off `integ/v2-cartography`; build 0/0
> (`TreatWarningsAsErrors` ON); SpecCheck row 2 (`SBPR_LocalMap`) added; spec + code +
> manifest + dataset move together (this PR). Milestones: **M-A** = the item + equip/torch
> patch + binding controller; **M-B** = the forked viewer render (`MapViewer.cs`,
> productionized from the spike); **M-C** = pin-click removal + WorldPins-seam consumption +
> the fork's own open path. Every load-bearing vanilla signature was verified against
> `assembly_valheim.dll` before coding (EquipItem/UnequipItem/GetCurrentBlocker overloads,
> `ItemData.m_customData` round-trip, the nomap `SetMapMode` force, `AnimationState` nesting);
> the windowed-cell projection + its click-inverse are proven by a standalone math harness
> (8/8). **Two design clarifications flagged for review:** (a) §2A.4 says "equip + activate
> → active minimap"; under v1's `nomap` world key vanilla forces `Minimap` to `None` and
> keeps BOTH map roots off (decomp `SetMapMode :975`), so the viewer is a STANDALONE uGUI
> overlay (the spike's design) and equipping BINDS the map while the vanilla **"Map" button**
> (otherwise dead under nomap) ACTIVATES the full view — the fork owning its own open path,
> exactly as §2B requires. (b) The "active minimap shows ONLY its disc" is realized as the
> fork's own bounded full-view overlay, not a re-skin of vanilla's nomap-suppressed minimap
> circle. **logs-green ≠ playable — Daniel verifies the in-game pixel render + equip feel
> (F9/Map-key + in-hand) per AT-MAP-* below; the per-AT status table is in the PR handoff.**

**Lands in:** `Features/Cartography/LocalMap.cs` (item + recipe + `LocalMapItemTag`),
`Features/Cartography/LocalMapEquipPatch.cs` (the torch-exception equip patch),
`Features/Cartography/LocalMapController.cs` + `LocalMapBootstrapPatch.cs` (carry/equip
binding state machine), and `Features/Cartography/MapViewer.cs` (the forked viewer, shared
with the Table via the `CartographyViewer` seam).
**Card:** `t_cb831069` (engineer-ui). **The viewer is the tier's single biggest build-risk.**
**Depends on:** this spec (ItemType + recipe lock) **and** the UI-fork spike (`t_e8bbbe48`).

### 2A — The item, equip behavior, and the torch patch

#### 2A.1 Item + recipe (LOCKED)
- A craftable `ItemDrop` named `SBPR_LocalMap`, **blank when crafted** (no map data).
- **Recipe:** DeerHide ×2 + FineWood ×4, amount 1, **crafted at the Explorer's Bench**
  (`m_craftingStation = piece_sbpr_explorers_bench` via
  `RecipeHelpers.FindStation(Trailhead.ExplorersBenchName)` — the existing pattern in
  `Pigments.cs:143`, `Cairns.cs:268`). NOT crafted at the Surveyor's Table.

#### 2A.2 `ItemType` = `TwoHandedWeapon` (the decisive lock — decomp-grounded)
- Set `m_shared.m_itemType = ItemType.TwoHandedWeapon` (= 14). **Do NOT invent a custom
  enum value, do NOT use `Utility`.**
- **Why not a custom type:** `Humanoid.EquipItem` (decomp :13798–14011) is a closed
  `if / else-if` keyed on `m_itemType`, ending at `Trinket` (:13992) then falling to
  `if (IsItemEquiped(item))` (:14001) with **no default hand-slot branch**. A custom value
  matches nothing → never assigns `m_rightItem`/`m_leftItem` → the item never equips. You'd
  have to patch `EquipItem` to add a whole branch — strictly more surface for no gain.
- **Why `TwoHandedWeapon` is correct:** its branch (:13921–13932) already does the exact
  block-clear discipline the C3 lock demands — `UnequipItem(m_leftItem)` +
  `UnequipItem(m_rightItem)` + `m_hiddenRightItem = null` + `m_hiddenLeftItem = null`,
  then `m_rightItem = item`. True-unequip, never-hide, inherited for free.
  `IsTwoHanded()` (:58050) returns true → all two-handed gating falls out.
- **Suppress combat:** leave `m_shared.m_attack.m_attackAnimation` and
  `m_shared.m_secondaryAttack.m_attackAnimation` **empty** → `HavePrimaryAttack()` /
  `HaveSecondaryAttack()` (:58059/:58064) false → LMB/RMB do nothing, no block
  (AT-MAP-BLOCKCLEAR). Give it a benign animation state (the spike/impl picks a
  non-combat hold pose; fishing-rod-style is the closest vanilla precedent).
- **Activating the map as the minimap is NOT the attack path.** It's a separate
  equip-side hook (see §2B "binding"), so we don't repurpose `m_secondaryAttack`.

#### 2A.3 The torch exception (C12 — ships from the gate, one Harmony patch)
- The bare `TwoHandedWeapon` branch force-unequips the left hand including a Torch. To
  permit a lit map at night, **Harmony-patch `Humanoid.EquipItem`** so that *when the
  item being equipped is `SBPR_LocalMap`* it runs the TwoHandedWeapon eviction but then
  **allows a `Torch` back into `m_leftItem`** — mirror vanilla's torch-beside-one-handed
  special-case (:13846–13850 and the OneHandedWeapon-keeps-torch guard :13882).
- Discipline: shield / left-weapon are still hard-`UnequipItem`'d (never hidden); ONLY a
  `Torch` is allowed back. The block-clear guarantee holds whether or not a torch is up.
- This is the **only** Harmony patch the ItemType decision requires. Keep it scoped to our
  item (guard on the prefab name / a tag component) so it never alters vanilla two-handed
  weapons.

#### 2A.4 Binding durability (D1 / C3)

> **🟢 CARRY PATH NOW RENDERS (2026-06-16, map-provider-binding-impl-spec, card t_7dd54899).**
> §2A.4's "minimap binding is durable while carried" is now realized as an actual rendered
> **circular minimap disc**, not just controller state + a log line. The carry selection is the
> **provider state machine** (map-provider-binding-impl-spec §3): the provider is the
> most-recently-**equipped** still-carried Local Map (instance identity, not "first in
> inventory"); it persists through unequip while `Inventory.ContainsItem` holds and unbinds on
> drop/trade/death. While bound **and nomap is ON** (§5), `LocalMapController` drives
> `CartographyViewer.BindMinimap` → the disc renders the provider's 1000 m survey via a scaled,
> player-centred instance of the §2H.1 viewer (see §2H.1 banner). The old `GetCarriedLocalMap`
> "first carried" probe is **retired**. Read map-provider-binding-impl-spec before touching the
> carry/provider path — it supersedes the "hook inventory-changed; first carried map" wording in
> the bullets below.

> **⚠️ OPEN PATH SUPERSEDED (2026-06-11, issue 7 → §2F).** The §2 IMPL STATUS banner above
> and the last bullet below originally said the Local Map's full view is ACTIVATED by the
> vanilla **"Map" button** ("otherwise dead under nomap"). Daniel's v0.2.19-playtest proves
> that premise FALSE: the playtest world is `nomap=OFF`, where vanilla's M-key map is fully
> alive, so binding our viewer to "Map" stacks both surfaces. **§2F is now the authoritative
> open-input path** — the viewer opens on the **Use key (E)** while equipped, off the "Map"
> button entirely. Read §2F before touching the open trigger.

- **Minimap binding is durable while the item sits in inventory** (equipped or not) and
  **reverts to no-map the instant the item leaves inventory** (dropped/traded/destroyed).
  Hook inventory-changed; when no `SBPR_LocalMap` instance remains, drop the binding.
- **Full-screen view requires the map actively EQUIPPED** (two hands). Carrying it keeps
  the minimap bound; only an equipped map can be opened — **opened by the Use key (E), NOT
  the "Map" button (§2F, issue-7 correction).**

#### 2A.5 Imprint + per-instance storage
- **Imprint** happens at a Surveyor's Table (§1): blank map → a **snapshot** of the
  Table's current windowed survey + the Table's bound-origin world coord. Not a live link.
- Store the windowed fog array + bound-origin coord **on the item instance**. Verify
  `ItemDrop.ItemData.m_customData` (a `Dictionary<string,string>`) exists and round-trips
  trade/drop **on our game version before relying on it** (flagged unknown — decomp it at
  build). **Fallback if absent:** a ZDO-backed "map case" carrier. The blob is the §2C
  windowed format, so one format serves item + Table + viewer.

#### 2A.6 Item name inheritance — the Local Map item bears the Table's name (issue 10, AT-TABLENAME-3)

> **Status: NEW DESIGN.** The imprinted Local Map item's NAME must bear the Table's name so a
> player carrying several bound maps can tell them apart in inventory. Pairs with §1.6.

> **🔒 FORMAT RE-LOCK (issue 4, 2026-06-15, Daniel — supersedes issue 8).** The displayed item name
> is locked to **`Local map for <TableName>`** (lowercase "map", the word "for", the bare Table
> name, NO quotes), superseding the issue-8 `Local Map of "<TableName>"` (2026-06-12) and the older
> v0.2.22 `Map: <name>`. The displayed-name format lives in one place,
> `LocalMap.FormatDisplayName(string)` — see §2A.6c. This is a display-only reskin: storage
> (`sbpr_map_name`, still bare), the imprint path, and SpecCheck are all UNCHANGED. The §2B.1 viewer
> cartouche keeps the BARE name (it does not route through the format helper — see §2A.6c). **Note:
> the §2A.6c re-lock ALSO fixed a render-race bug where the title bound but never painted in-game —
> see §2A.6c.**

> **IMPL STATUS (2026-06-12, card t_41482aa3, engineer-ui):** built; build 0/0.
> **(a)** `LocalMap.Imprint` gained an optional `string tableName` param and stamps the bare
> name into the new `sbpr_map_name` `m_customData` key (LOCK, never rename); `SurveyorTableTag.
> ImprintCarriedLocalMaps` passes `GetTableName()` (only when named). `LocalMap.TryGetName(item,
> out name)` mirrors `TryGetBoundOrigin`. **(b)** Two scoped Harmony postfixes in the new
> `LocalMapNamePatch.cs` (both registered in `Plugin.Awake` → caught by `PatchCheck`, AT-TABLENAME-8):
> `LocalMapTooltipNamePatch` (Postfix on private `InventoryGrid.CreateItemTooltip` → overwrites
> `UITooltip.m_topic`, the title, **verified at build: `m_topic` is the rendered title field in
> `assembly_guiutils`**) and `LocalMapHoverNamePatch` (Postfix on `ItemDrop.GetHoverName` →
> rewrites `__result`, the world-drop hover). **Guard = presence of the `sbpr_map_name` key**
> (chosen over `m_dropPrefab`, which is `[NonSerialized]` and unreliable on loaded items); pure
> pass-through otherwise (AT-TABLENAME-7). **Name format (issue 4 re-lock, 2026-06-15): the
> displayed title is `Local map for <TableName>`** (lowercase "map", word "for", no quotes), produced
> by `LocalMap.FormatDisplayName(name)` — superseded the issue-8 `Local Map of "<name>"` and the
> v0.2.22 `"Map: "` prefix. Stored bare; format applied at display, so it changes without
> re-imprinting. **(c) RENDER-RACE FIX (issue 4, t_783672ac, 2026-06-15):** the inventory-title
> postfix was only *assigning* `tooltip.m_topic`, which never repaints (vanilla's per-frame
> `CreateItemTooltip → Set` clobbers it) — so the title bound at boot but showed bare "Local Map"
> in-game. It now re-issues `tooltip.Set(...)` to force `UpdateTextElements()`. See §2A.6c.
> **Daniel confirms in-game. logs-green ≠ playable — AT-MAPNAME-1/5.**

**⚠️ The card's `m_crafterName` hypothesis is WRONG — corrected here against the decomp.** The
card's open question proposes carrying the per-instance name via `ItemData.m_customData["crafterName"]`
/ the crafter path. That does **not** make the name show as the item's title. Grounded findings
(`assembly_valheim` decomp — clean-side per ADR-0001):

1. **The inventory hover TITLE is `m_shared.m_name`, full stop.** `InventoryGrid.CreateItemTooltip`
   (`:40890`) — the sole inventory-grid hover-title path — calls
   `tooltip.Set(item.m_shared.m_name, item.GetTooltip(), m_tooltipAnchor)` (`:40892`). The world-drop
   hover `ItemDrop.GetHoverName()` (`:58937`) and the equip/craft titles (`:42460`, `:42844`,
   `:138558`) ALL read `m_shared.m_name`. There is **no per-instance display-name field** in vanilla.
2. **`m_crafterName` is NOT the title — it's a separate tooltip BODY line.** `ItemData.GetTooltip`
   appends `"\n$item_crafter: <color=orange>{crafterName}</color>"` (`:58314`) — the "Crafted by Foo"
   line. Stamping the Table name there would show `Crafted by: Northern Outpost` under a still-generic
   "Local Map" title. Wrong surface.
3. **You CANNOT just set `item.m_shared.m_name` either.** `m_shared` is a `[Serializable] SharedData`
   shared **by reference** across all instances of a prefab, and it is **NOT a per-instance field**:
   `ItemData.Clone()` is `MemberwiseClone()` (`:58025-58027`) which copies the `m_shared` *reference*
   (not a deep copy — only `m_customData` is deep-copied, `:58028`). On load, `Inventory.AddItem`
   `Instantiate`s the item from the prefab (`:57499`) and restores ONLY the per-instance fields —
   stack/durability/equipped/quality/variant/crafterID/crafterName/**customData**/worldLevel/pickedUp
   (`:57508-57517`); **`m_name` is never among them** — it always comes from the prefab's shared data.
   So writing `item.m_shared.m_name` would (a) rename EVERY Local Map + the prefab template in the
   live session (shared reference), and (b) not survive anyway (the next spawn reads the prefab's
   name). Hard no.
4. **`m_customData` IS the only per-instance, save-surviving store** — `Clone()` deep-copies it
   (`:58028`), and `Inventory.AddItem` round-trips it through the player-profile ZPackage (`:57515`,
   `m_itemData.m_customData = customData`). The repo already relies on this for `sbpr_map_blob`/`sbpr_map_bound`.

**Locked mechanism (two coupled pieces):**

- **(a) Persist the per-instance name in `m_customData`.** `LocalMap.Imprint` (`LocalMap.cs:199-214`)
  gains a `string tableName` parameter and writes a new key alongside the blob:
  - **`sbpr_map_name`** (`m_customData` key) = the Table's name (already censored at the Table per
    §1.6.3). LOCK + never rename — same wire-contract rule as `sbpr_map_blob`. `SurveyorTableTag.
    ImprintCarriedLocalMaps` passes `GetTableName()` into every `Imprint` call (§1.6.4).
  - Add `LocalMap.TryGetName(item, out string name)` (mirrors `TryGetBoundOrigin`) reading that key.
- **(b) Surface that name as the item's displayed title via a scoped Harmony patch.** Because the
  title is hard-wired to `m_shared.m_name`, the ONLY clean way to show a per-instance name is to
  intercept the name-display seam and substitute our `m_customData` value **for our item only**:
  - **Primary seam (inventory hover, the title Daniel sees):** a Harmony **Postfix on the private
    `InventoryGrid.CreateItemTooltip(ItemDrop.ItemData, UITooltip)`** (`:40890`) — when the item is a
    Local Map (tag/prefab-name guard, the existing `LocalMapItemTag` check) AND carries `sbpr_map_name`,
    rewrite the tooltip's topic/title to that name. Confirm `UITooltip`'s title field at build
    (`UITooltip` lives in `assembly_guiutils`, not in this repo's `assembly_valheim` decomp dump —
    patch its `Set(...)` or set `m_topic`/the title TMP field; the engineer verifies the member).
    *Implementer alternative if `CreateItemTooltip` proves awkward to patch: Postfix
    `ItemDrop.ItemData.GetTooltip` to inject the name as a prominent `m_subtitle`-style first line.
    Lower fidelity (title stays "Local Map") — only if (b)-primary is blocked; Daniel verifies.*
  - **Secondary seam (world-drop + transfer hover, nice-to-have):** Postfix `ItemDrop.GetHoverName()`
    (`:58937`) with the same guard so a dropped bound map names itself on the ground too.
  - **Scope discipline:** every patch guards on the `LocalMapItemTag` (or prefab-name) + presence of
    `sbpr_map_name`, so it is a pure pass-through for every other item — it never touches vanilla
    titles. Register it in `Plugin.Awake()` via `harmony.PatchAll(typeof(...))` and it WILL be caught
    by `PatchCheck` if forgotten (the t_564f695a lesson — an unregistered patch ships dead).
- **Name format (issue 4 — RESOLVED, Daniel re-locked it 2026-06-15, supersedes issue 8):** the
  displayed title is **`Local map for <TableName>`** (lowercase "map", the word "for", no quotes),
  e.g. `Local map for Northern Outpost`. The bare name is stored in `sbpr_map_name` and the format is
  applied at display time by `LocalMap.FormatDisplayName(string)` (so the wording can change without
  re-imprinting). This supersedes the issue-8 `Local Map of "<name>"` and the earlier `"Map: "`
  prefix. See §2A.6c for the locked helper + seams + the render-race fix.
- **Blank maps are unaffected:** a map with no `sbpr_map_name` (never imprinted) shows the vanilla
  "Local Map" title — the patch is a pass-through. (AT-TABLENAME-7 no-orphan.)

#### 2A.6c Item-name FORMAT re-lock — `Local map for <TableName>` (issue 4, 2026-06-15, display-only)

> **Status: LOCKED (Daniel, 2026-06-15).** A pure display-FORMAT change to the §2A.6/§2A.6b item
> name that already ships. The imprinted Local Map's displayed title is now
> **`Local map for <TableName>`** — lowercase "map", the word "for", the bare Table name, **NO
> quotes** — superseding the issue-8 `Local Map of "<TableName>"` (2026-06-12) and the older v0.2.22
> wording `Map: <name>`. **Nothing about storage, imprint, the patched seams, or the patch
> registration changes** for the *format* part — that stays a reskin of the displayed string only.
> The format lives in one place, `LocalMap.FormatDisplayName(string)`.

> **🐞 RENDER-RACE FIX rides on this re-lock (issue 4, t_783672ac, 2026-06-15).** Daniel reported the
> title "still lacks the table name" **in-game** despite issue 8 shipping. Root cause (grounded
> against `assembly_valheim` + `assembly_guiutils` decomp, clean-side per ADR-0001): the
> `LocalMapTooltipNamePatch` postfix only *assigned* `tooltip.m_topic`, which **never repaints**.
> `InventoryGui.Update → UpdateInventory → InventoryGrid.UpdateGui` calls `CreateItemTooltip(item,
> tooltip)` **every GUI frame** for the hovered element, and `CreateItemTooltip` calls
> `tooltip.Set(item.m_shared.m_name, …)`. `UITooltip.Set` is the **only** path that re-renders the
> live tooltip (it calls `UpdateTextElements()` → writes `m_topic` into the TMP "Topic" widget). A
> bare field write after `Set` is overwritten by the next frame's vanilla `Set` before it ever
> paints — so the title bound at boot (`PatchCheck` green) but showed the bare "Local Map" forever.
> **logs-green ≠ playable.** The fix: the postfix now re-issues `tooltip.Set(FormatDisplayName(name),
> tooltip.m_text, m_tooltipAnchor)` so `UpdateTextElements()` actually runs that frame. `Set`
> early-outs when `topic == m_topic && text == m_text`, so our different topic forces the re-render;
> a same-frame guard (`tooltip.m_topic == title → return`) prevents redundant re-issues.

**The single source of the wording.** §2A.6b stored the format as the `LocalMap.NameDisplayPrefix`
const (`"Map: "`) and built the title by concatenation (`prefix + name`). It is now a formatter
method (no quotes in the issue-4 form, but kept as a method so the wording can change in one place):

```csharp
// LocalMap.cs — was: public const string NameDisplayPrefix = "Map: ";
// (issue 8 interim: $"Local Map of \"{tableName}\"" — superseded)
public static string FormatDisplayName(string tableName) => $"Local map for {tableName}";
```

**Both seams call it (otherwise unchanged in format; the TITLE seam changed in MECHANISM — see the
render-race note above).** The two postfixes in `LocalMapNamePatch.cs` format the name through the
helper:

- `LocalMapTooltipNamePatch` (inventory hover title): **re-issues** `tooltip.Set(LocalMap.
  FormatDisplayName(name), tooltip.m_text, __instance.m_tooltipAnchor);` — NOT a bare `m_topic`
  assignment (that never paints; see the render-race fix above).
- `LocalMapHoverNamePatch` (`ItemDrop.GetHoverName` world-drop / transfer hover): `__result =
  LocalMap.FormatDisplayName(name);` — a pure return-value postfix, no render race, unchanged in
  mechanism.

**Explicitly out of scope (do NOT change):**

- **The §2B.1 viewer cartouche title (`MapViewer._titleLabel`)** shows the **BARE** name via
  `MapViewRequest.Title` ← `LocalMap.TryGetName`. It does NOT route through `FormatDisplayName` and is
  left BARE — Daniel said that on-screen title already works and didn't ask to change it. Keep the
  cartouche reading `Northern Outpost`, not `Local map for Northern Outpost`.
- The item DESCRIPTION body is **out of scope** (Daniel, 2026-06-15: "title only, NOT the
  description"). No description seam is added.
- No `sbpr_map_name` / imprint / storage changes; no new Harmony patches; **SpecCheck: no change**
  (display-only, no recipe rows).

**Acceptance tests (AT-MAPNAME-1…5) — supersede the format clause of AT-TABLENAME-3:**

- **AT-MAPNAME-1** — an imprinted map's inventory hover title reads exactly `Local map for Home`
  (table named "Home") — lowercase "map", word "for", no quotes.
- **AT-MAPNAME-2** — BOTH item seams show it: the inventory hover title (`InventoryGrid.
  CreateItemTooltip` → re-issued `Set`) AND the world-drop / transfer hover (`ItemDrop.GetHoverName`).
- **AT-MAPNAME-3** (no orphan) — a blank / pre-1.6 map (no `sbpr_map_name`) still reads the plain
  vanilla `Local Map` (pure pass-through; `FormatDisplayName` is never invoked).
- **AT-MAPNAME-4** — `PatchCheck` green; the §2B.1 cartouche title still shows the BARE name; non-map
  items' titles are untouched.
- **AT-MAPNAME-5** (logs-green ≠ playable, the BUG this card fixes) — Daniel confirms `Local map for
  Home` **actually paints** on the inventory hover in-game (the title repaints every frame instead of
  being clobbered back to bare "Local Map").

#### 2A.7 Tooltip combat-row suppression (issue 7, display-only)

**Problem (Daniel, 2026-06-12 v0.2.22-playtest):** *"issue 7: the map has stats like block, parry
force, etc. what? 😛"* The Local Map's tooltip shows weapon combat stats.

**Root cause (grounded against `assembly_valheim` decomp, clean-side per ADR-0001).** `ItemType =
TwoHandedWeapon` (§2A.2) is the decisive lock for the equip / block-clear / torch discipline and
**must stay**. But that type routes the item through the weapon `case` of the tooltip **body
builder** `ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int)`:
- `AddHandedTip` (runs before the `switch`) appends `$item_twohanded` for any two-handed type — **always**.
- The weapon `case` emits the damage block, `$item_staminause` (`m_attack.m_attackStamina > 0`),
  `AddBlockTooltip` (`$item_parrybonus` from `m_timedBlockBonus > 1`, `$item_parryadrenaline` from
  `m_perfectBlockAdrenaline > 0`, plus the two block rows gated `> 1f`), `$item_knockback`
  (`m_attackForce > 0`), and `$item_backstab` (`m_backstabBonus > 1`).

Zeroing `m_blockPower` / `m_deflectionForce` in `LocalMap.cs` only suppresses the **two block rows**
(both gated `> 1f`); every other weapon field the donor (`Hoe`) carries still leaks. Per-field
zeroing is whack-a-mole — the clean fix is to suppress the **whole** weapon section for our item.

**Locked fix — display-only Harmony Postfix (NOT an `ItemType` change).**
- **Seam:** `LocalMapTooltipCombatStripPatch`, a Postfix on the **public static** overload
  `ItemDrop.ItemData.GetTooltip(ItemData, int, bool, float, int)` (disambiguated by an explicit
  `Type[]` in `[HarmonyPatch]`). The instance `GetTooltip(int)` delegates to this static overload
  and the crafting UI calls it directly, so **one patch covers every surface** — inventory hover,
  crafting hover, and the equip / world-drop hover. This is the tooltip **body** (`item.GetTooltip()`),
  distinct from the **title** seam `LocalMapTooltipNamePatch` hooks (`InventoryGrid.CreateItemTooltip`
  → `m_topic`, §2A.6b).
- **Behavior:** for our item, **rebuild** a clean body — `m_shared.m_description` + a `$item_weight`
  line — and overwrite `ref __result`. Rebuild (not regex-strip): `$item_twohanded` is appended
  *before* the weight line, so post-hoc truncation can't remove it cleanly. Do **not** transiently
  mutate `m_shared.m_itemType` around the original call — `m_shared` is shared **by reference**
  across every Local Map instance + the prefab template (§2A.6b), so mutating it is unsafe.
- **Guard:** `item?.m_dropPrefab?.GetComponent<LocalMapItemTag>() != null` — the **tag**, NOT
  `sbpr_map_name`. The tag catches **both** a blank crafted map AND an imprinted one (the name key
  only exists once imprinted; a blank map would otherwise still leak weapon stats). `m_dropPrefab`
  is reliably set on loaded-from-save items: `Inventory.Load` → `AddItem(name, …, customData, …)` →
  `Instantiate(prefab)` → `ItemDrop.Awake` sets `m_itemData.m_dropPrefab = ObjectDB.GetItemPrefab(name)`
  **unconditionally** (decomp `:58698`), before any ZNetView gating — the same guard the
  equip / binding / table patches already use in-game.
- **Registration / scope:** registered in `Plugin.Awake` beside the name patches so `PatchCheck`
  confirms it wove a method (AT-MAP-TT-6). Pure pass-through for every other item (vanilla tooltips
  byte-identical). **Client-only by nature:** `GetTooltip` dereferences `Player.m_localPlayer` (NPEs
  server-side → never called there); the null-guard short-circuits regardless.

**`ItemType` stays `TwoHandedWeapon`** — equip / block-clear / torch behavior (§2A.2/§2A.3) is
untouched. **SpecCheck / drift manifest: no change** (display-only; no recipe / piece / station delta).

**Acceptance tests:**
- **AT-MAP-TT-1** (inventory hover) — a Local Map's tooltip shows NO combat rows: no
  block / block-force, parry-bonus / parry-adrenaline, damage, knockback, backstab, stamina-use, nor
  the `$item_twohanded` handed line. Description (+ weight) only.
- **AT-MAP-TT-2** (all surfaces) — same clean tooltip in the crafting hover (Explorer's Bench recipe)
  and the equip / world-drop hover — every surface fed by `GetTooltip`.
- **AT-MAP-TT-3** (blank AND imprinted) — both a freshly-crafted blank map and an imprinted map show
  the clean tooltip (guard is `LocalMapItemTag`, not `sbpr_map_name`).
- **AT-MAP-TT-4** (no regression) — two-handed equip + open-on-E + block-clear + torch-exception all
  preserved (`ItemType` unchanged; the fix touches no equip code).
- **AT-MAP-TT-5** (scope / no-orphan) — pure pass-through for every non-map item; verify a real 2H
  weapon (e.g. an axe) still shows its stats.
- **AT-MAP-TT-6** (`PatchCheck`) — the postfix is registered in `Plugin.Awake` and logs it wove a
  method at boot (no dead patch).
- **AT-MAP-TT-7** (logs-green ≠ playable) — Daniel confirms in-game the map tooltip is clean, and
  confirms the final line set (whether to keep the weight line).

### 2B — The forked viewer (productionize the spike's proof)

> **⚠️ RENDER PATH SUPERSEDED (2026-06-11, issue 6 → §2E).** Bullet 1 below originally
> specced the fork to "drive a custom RawImage from OUR windowed array" painted as a
> two-color fog mask. Daniel's v0.2.19-playtest report: that doesn't look/behave like the
> real map. **§2E is now the authoritative render path** — reuse a COPY of vanilla's map
> MATERIAL (the 4-texture shader composite) masked by our fog window. The fork SHELL,
> bounding, fixed zoom, and edge-arrow below are all UNCHANGED and still correct; only the
> per-pixel paint is replaced. Read §2E before touching the render.

> **⚠️ EXIT UX ADDED (2026-06-11, issue 7 → §2F).** §2B specced the viewer's own open/close
> path but never nailed down the *exit* UX. Two gaps closed in **§2F**: (1) Escape closes the
> viewer **and** leaks into vanilla's pause menu the same frame — fixed by gating `Menu.Show`
> through the shared `SignPanelInputBlock` so Escape "just works"; (2) no on-screen exit
> prompt — add a bottom-center "[Esc] Close map" label. Read §2F before touching the viewer's
> input handling or canvas build.
> **⚠️ ORIENTATION SUPERSEDED (2026-06-11, issue 8 → §2H; RE-LOCKED 2026-06-12 → §2H.1).** This
> section's implicit orientation for the **FieldReadOnly (held Local Map)** view is governed by
> **§2H.1** (the 2026-06-12 re-lock), NOT this section and NOT the original §2H. The held Local
> Map is a **fixed-window, TABLE-centred, circular, rotate-to-heading** minimap: the player
> marker moves within a static disc and is hidden + edge-arrowed when outside it; only the
> circular interior rotates (the bezel/frame is fixed); there is **no** north indicator. The
> Surveyor's Table / TableEdit view ALSO rotates-to-heading now (issue #1, Daniel-locked
> 2026-06-12 — no north-up lock anywhere) but keeps its fuller table-centred **square** extent for
> pin-editing visibility, with no north indicator either. Bounding/shroud (1000 m around the table)
> and fixed zoom are UNCHANGED. **Read §2H.1
> before touching the held-map orientation**, and route §2E + §2H.1 to the SAME worker (they
> co-define the same RawImage). *(The superseded §2H "player-centred + free-rotate" model shipped
> in v0.2.22 and was rejected — see the §2H.1 supersession note for why.)*

> **The spike (`t_e8bbbe48`) is the source of truth for the render path.** Build §2B
> against its findings doc — especially the confirmed `m_pixelSize` and which RawImage
> path renders cleanly. Everything below is the spec the spike validates; if the spike
> caveats or blocks any item, this section re-specs to match.

- **It's a FORK of the vanilla map UI**, not a reuse of the live map. Vanilla `Minimap`
  (decomp ~:46485+) builds `m_mapTexture` (:46894) and shows it via `m_mapImageLarge` /
  `m_mapImageSmall` RawImages on `m_largeRoot` / `m_smallRoot` (:46613–46619). The fork
  drives a custom RawImage from OUR windowed array — it does NOT feed our blob to
  `Minimap.AddSharedMapData` (which expects the 256² world array).
- **Hard 1000 m radius**, centered on the **bound origin** (the Surveyor's Table the map
  was imprinted at — NOT the player). Everything beyond 1000 m is permanent shroud and
  never reveals. Pins render only inside the disc.
- **Fixed zoom** on both the minimap circle AND the full-screen view — no scroll-to-zoom.
  One authored scale each.
- **No pinning interface in the field** (Local-Map view is read-only). The same viewer in
  **Table mode** (§1.4) enables pin removal; the mode flag is set by who opened it.
- **Player-outside-the-disc → edge indicator clamped to the 1000 m SHROUD RADIUS**
  (C1-corrected): project the off-disc player position onto the 1000 m circle and draw a
  direction arrow toward the bound Table. This is a **map-space clamp to the disc radius**,
  computed in map coords — **NOT** `Minimap.ClampToScreenEdge` (:34731), which is a
  screen-space ping clamp and the wrong precedent. (The spike sketches this; full polish
  here.)
- Must work / degrade gracefully under v1's map nerf: the fork owns its own open/close on
  the **Use key (E) while equipped (§2F)**, and does not rely on vanilla
  `Minimap.SetMapMode(Large)` or the "Map" button. (Issue-7 correction: the earlier "Map
  button repurposed under nomap" open path is replaced — see §2F.)

#### 2B.1 Viewer title — the Table name shows while the map is open (issue 10, AT-TABLENAME-4/5)

> **Status: NEW DESIGN.** When a map view is open, the Table's name shows as a title on-screen —
> for BOTH the field Local-Map view (the imprinted map's name) and the Table-at-the-Table view.

> **IMPL STATUS (2026-06-12, card t_41482aa3, engineer-ui):** built on the finished §2E/§2F/§2G/§2H
> `MapViewer` canvas (PR #123); build 0/0. Added `string? Title` to `MapViewRequest`. `MapViewer`
> gained a `_titleLabel` (`Text`, bold, parchment-cream — same `VanillaUISkin.Font` as the §2F exit
> prompt) created in `EnsureCanvas` anchored **TOP-centre**, and an `UpdateTitle()` called each
> `Render()` that shows it from `_req.Title` (hidden when empty → AT-TABLENAME-7 no-orphan).
> **Placement contract honoured: title = TOP-centre, §2F exit prompt = BOTTOM-centre — no overlap.**
> Producers: `SurveyorTableTag.Interact` sets `Title = GetTableName()` (Table view); `LocalMapController.
> OpenFullView`/`RefreshOpenView` set `Title = LocalMap.TryGetName(map)` (field view). One mode-agnostic
> code path. **logs-green ≠ playable — AT-TABLENAME-4/5 are F9/in-view checks Daniel confirms.**

- **Thread the name into the viewer via `MapViewRequest`.** Add one field to the request struct
  (`CartographyViewer.cs`, the `MapViewRequest` struct `:67-74`):
  - **`string Title;`** — the display name to show. Producers set it:
    - **Table view** (`SurveyorTableTag.Interact` `:124-132`): `Title = GetTableName()` (§1.6.1).
    - **Field Local-Map view** (`LocalMapController.OpenFullView`/`RefreshOpenView`
      `LocalMap­Controller.cs:152-176`): `Title = LocalMap.TryGetName(map, …) ? name : ""` (§2A.6a).
  - Empty `Title` → render no title element (an unnamed Table's view, or a pre-1.6 imprinted map).
- **Render a title label in the viewer canvas.** `MapViewer.EnsureCanvas` (`MapViewer.cs:463-516`)
  builds the overlay; add a `Text`/`TMP` label anchored **top-center** of the bounded map square
  (above the frame `:489-496`), set from `_req.Title` in `Render()` (`:123-135`). Use the viewer's
  existing dark-Norse palette (`CFrame`/parchment) so it reads as a map cartouche.
- **🔗 Placement coordination with the exit prompt (t_e2cc8183 / §2F, PR #108).** That card adds a
  **bottom-center** `"[Esc] Close map"` exit prompt to the same canvas. **No collision by design:
  title = TOP-center, exit prompt = BOTTOM-center.** This is a hard placement contract — whichever
  of the two lands second must honor it. If PR #108 (§2F) has not merged when THIS card's impl
  starts, the implementer adds only the top-center title and leaves the bottom band for §2F; if it
  has merged, confirm the title sits above the map frame and the prompt below. (Both cards touch
  `MapViewer.cs` — same-file coordination note, mirrors the §2E/§2F dependency already recorded.)
- **One viewer, both modes:** the title element is mode-agnostic — `FieldReadOnly` shows the map's
  imprinted name, `TableEdit` shows the live Table name. No second code path.

### 2C — Fog storage format (the over-provisioning fix, C2-corrected)
- The fog is a **small array windowed to the 1000 m disc at the player auto-map's NATIVE
  pixel resolution** — NOT the full 256² world array, NOT a custom-resolution resample.
- Vanilla world fog: `m_explored` / `m_exploredOthers` are `bool[m_textureSize²]` with
  **`m_textureSize = 256`** (decomp :46692) and **`m_pixelSize = 64f`** (:46694), covering
  the ~16 km world. A 2000 m-diameter disc at 64 m/px ≈ a **~32×32 window** (~1,024 cells)
  — ~1.6 % of the 65,536-cell world array.
- **Resolution = whatever the player's auto-map actually uses — do NOT pick 8 vs 16 m/px**
  (C2 rejected the custom-resolution idea: the map imprints FROM the player's native fog,
  and a custom grid forces a lossy resample every imprint). **Confirm the real
  `m_pixelSize` at build** — the spike does exactly this; the personal auto-map may differ
  from the 64 m/px world-minimap default. Whatever the spike confirms is the build value.
- World→cell windowing uses vanilla `WorldToMapPoint` (:47977) / `WorldToPixel`; copy the
  sub-rectangle of cells around the bound origin out of the live `m_explored` at imprint.
  Stored = the windowed cell range + the bound-origin world coord (+ a resolution tag for
  forward-safety). The forked viewer renders THAT array directly, clipped to the disc.
- The walking-reveal source is vanilla `Minimap.Explore(Vector3, radius)` (:48015) →
  `Explore(x,y)` (:48036) writing `m_explored`; the Kit gate (§3) controls whether that
  write happens at all.

### 2D — Acceptance criteria (spec §6; close only on Daniel's in-game check)
- **AT-MAP-EQUIP** — equip the Local Map + activate → it becomes the active minimap
  showing ONLY its 1000 m disc.
- **AT-MAP-DURABLE** — binding persists while the item sits in inventory; reverts to
  no-map the instant it leaves inventory.
- **AT-MAP-BOUND** — nothing beyond 1000 m of the bound Table reveals; pins beyond 1000 m
  don't render.
- **AT-MAP-FIXEDZOOM** — neither minimap nor full view zooms; the field full view has no
  pinning interface.
- **AT-MAP-EDGEARROW** — player outside the disc → arrow clamped to the 1000 m circle
  pointing at the bound Table (map-space clamp, not screen edge).
- **AT-MAP-STORAGE** — the fog array is windowed to 1000 m at native resolution, not a full
  256² world array, not a resample.
- **AT-MAP-BLOCKCLEAR** — map equipped → RMB/block does nothing (no ghost shield block);
  unequip → weapon + shield return clean.
- **AT-MAP-TORCH** — map + left-hand torch coexist (lit map at night); still can't block or
  attack.
- **AT-TABLEMAP-1…7** (issue 6 correction, 2026-06-11) — the viewer must render vanilla
  cartography (biome/height/forest/water), not a two-color fog mask; see **§2E** for the
  named criteria + the locked route. **(Render route FINAL-LOCKED 2026-06-12 → §2E.3: the
  vanilla styled material IS the render, confirmed in-game on Daniel's GPU client
  v0.2.23-playtest, no toggle. The §2E.1 CPU composite + §2E.2 preview harness are
  SUPERSEDED/removed. See AT-PRUNE-* below.)**
- **AT-PRUNE-1…4** (cleanup, 2026-06-12, confirmed in-game) — the CPU render fallback +
  `sbpr_mapmode` toggle are removed; the vanilla styled material is THE unconditional render.
  **AT-PRUNE-1** held Local Map still renders the parchment look (shader material, bounded to
  the 1000 m disc) with NO console command. **AT-PRUNE-2** `sbpr_mapmode` no longer exists (no
  dead Harmony patch; PatchCheck green at boot). **AT-PRUNE-3** no regression to orientation /
  circular bezel / off-disc marker / pin labels / nomap-intact (the rest of the viewer cluster
  is untouched). **AT-PRUNE-4** build 0/0, docs-lint OK, no orphaned `tools/` project. See
  **§2E.3** for the final locked route. *(Supersedes the §2E.1 AT-RENDER-WATER/BIOME/RELIEF/
  PREVIEW/REGRESSION + AT-PARCHMENT-PREVIEW CPU-composite render tests — the parchment look is
  now proven in-game, not by a CPU-PNG proxy. AT-RENDER-NOMAP-INTACT survives as AT-PRUNE-3's
  nomap-intact clause.)*
- **AT-VIEWEXIT-1…7** (issue 7, 2026-06-11) — the viewer must exit cleanly: Escape closes it
  WITHOUT also opening the pause menu, and a bottom-center "[Esc] Close map" prompt is visible;
  see **§2F** for the named criteria + the locked `Menu.Show`-prefix route.
- **AT-LMAP-OPEN-1…6** (issue 7 correction, 2026-06-11) — the equipped Local Map opens its
  viewer on the **Use key (E)**, not the "Map" button; no double-map stacking; an on-screen
  prompt shows the open key. See **§2G** for the named criteria + the locked input model.
- **AT-TABLENAME-1…8** (issue 10, 2026-06-11) — Table naming + name-gated binding + item-name
  inheritance + viewer title; see **§1.7** for the named criteria. (§2A.6 item-name + §2B.1
  viewer-title are the item/viewer-side halves of that feature.)
- **AT-LMAP-TC-1…6** (issues #2/#3/#4/#9 re-lock, 2026-06-12) — the held Local Map is a
  fixed-window, **TABLE-centred**, circular, rotate-to-heading minimap: no pan (#4); player marker
  at true table-relative position, hidden + edge-arrow when off-disc (#3); fixed bezel, only the
  interior rotates (#2); circular form (#9); no north indicator. The Table view (TableEdit) ALSO
  rotates-to-heading now (issue #1, Daniel-locked 2026-06-12 — no north-up lock anywhere) but keeps
  its fuller **square** extent for pin-editing visibility, with no north indicator either. See
  **§2H.1** for the named criteria + the locked route. *(Supersedes AT-LMAP-ROT-1…5 /
  the player-centred §2H — see the §2H.1 supersession map.)*
- **AT-LMAP-LIVE-1…6** (issue 5, 2026-06-12) — with the Cartographer's Kit worn, travelling
  visibly grows the held map (the FieldReadOnly shroud recedes along the player's path) by OR-ing
  the player's live personal fog over the static imprint snapshot; without the Kit, no passive
  reveal; the snapshot stays static in storage. See **§2I** for the named criteria + the locked
  shroud-source route (rides §2E.1).
- **AT-PIN-LABEL-1…5** (issue #11, 2026-06-12) — pinned marker signs render their **text label**
  (custom `SBPR_PinName`, else the type label) next to the icon on the held Local Map; labels stay
  screen-upright as the map rotates (counter-rotated with the icon), unnamed/empty pins fall back
  cleanly, and the label never blocks the TableEdit click. See **§2K** for the named criteria +
  the locked `Text`-child route. (Rides §2H.1's `CounterRotatePins`.)
- SpecCheck row 2 present; `[hold]` PR; logs-green ≠ playable.

### 2E — Vanilla-cartography render (issue 6 design correction, 2026-06-11)

> **✅ IMPL STATUS (2026-06-11, t_95039708 → branch `impl/tablemap-vanilla-render-t_95039708`).**
> The §2E LOCKED ROUTE is BUILT in `MapViewer.cs`: the two-color `PaintFog` is no longer the
> primary render — `TryRenderVanillaCartography` instantiates a COPY of
> `Minimap.instance.m_mapImageLarge.material`, binds a reveal-all `_FogTex`, drives
> `uvRect`/`_zoom`/`_pixelSize`/`_mapCenter` to frame the bound origin's fog window (the same
> `BoundedMapMath` `WindowSpec` the fog + pins use → aligned by construction), and overlays OUR
> survey fog as a shroud mask. The boxy `CFrame` border is removed (AT-ISSUE1-BORDER). `PaintFog`
> is kept as the mandated graceful-degradation fallback (Minimap not generated yet). Build is
> clean (0 warn / 0 err). **NOT YET PLAYTESTED — the in-client shader micro-spike below could not
> be run by the headless build worker (no GPU; map textures gate on `graphicsDeviceType != Null`,
> decomp `Minimap.Update :47034`). The decomp RE was re-verified line-by-line; the one
> unconfirmable piece is the GPU shader's exact `uvRect`↔`_mapCenter`/`_pixelSize` sampling.
> Daniel's in-game playtest IS the §2E-mandated spike + the merge gate.** If the material can't be
> driven, the calibration constant (`zoom = Size/textureSize`, `_pixelSize = 200/zoom`) is the
> single knob to tune; if it can't be driven at all, the fallback already keeps the viewer
> functional (two-color) rather than blank.

> **🔴 SUPERSEDED (2026-06-12, issue 10 → §2E.1, card t_14c34abe).** The "material-copy"
> render above SHIPPED and is what Daniel saw fail in v0.2.22: a flat "land color" + shroud,
> i.e. the `PaintFog` fallback. Daniel locked a new approach (force-generate / sample vanilla's
> own map data). **The architect decomp-pass below (§2E.1) REFUTES both the shipped approach's
> premise AND the card's stated root cause, and re-locks the render on a CPU-sampled composite
> that needs no GPU shader.** Read §2E.1 before touching the render — it supersedes the
> material-copy LOCKED ROUTE in the rest of §2E (kept below for history).

#### 2E.1 — Render root-cause correction + CPU-composite re-lock (issue 10, 2026-06-12, card t_14c34abe)

> **⛔ SUPERSEDED (2026-06-12) by §2E.3 — historical, kept for the decomp record.** This section
> re-locked the render on a GPU-free CPU composite (`CartographyComposer`) after the §2E material-copy
> route shipped blank in v0.2.22. That composite then became the *fallback* leg of the §2E.3 two-mode
> toggle — and was **REMOVED entirely** once Daniel's v0.2.23-playtest confirmed the vanilla **Shader**
> render looks right on a real GPU client with no toggling. The CPU path insured against a client that
> can't drive the vanilla map shader, which can't see the vanilla map either (a non-scenario). The
> decomp analysis below (textures ARE generated under nomap; `WorldGenerator` is public + deterministic
> on the joining client) remains TRUE and useful context, but **`CartographyComposer.cs` /
> `MapViewer.TryComposeCartography` / `_cartoTex` no longer exist** — the live render is
> `TryRenderVanillaShader` only (§2E.3). The AT-RENDER-* tests below are retired (see §2D / AT-PRUNE-*).

#### 2E.1 — Render root-cause correction + CPU-composite re-lock (ORIGINAL, superseded — see banner above)

> **Status: BUG/DESIGN — ROOT-CAUSE CORRECTION + RE-LOCK.** Supersedes the §2E "reuse a COPY of
> `Minimap.instance.m_mapImageLarge.material`" LOCKED ROUTE. Reported by Daniel, v0.2.22-playtest:
> the Local Map shows a flat land fill + shroud — no water, biomes, or relief. Daniel locked the
> direction (*"force-generate vanilla's map texture even under nomap, then crop to the 1000 m
> window"*) and asked for **Unity preview PNGs messaged to him before ship** (§2E.2). Clean-side:
> reading + adapting vanilla `Minimap`/`WorldGenerator` is base-game, explicitly fair game (ADR-0001
> + repo `AGENTS.md` "Hard constraints"). **SpecCheck impact: none** (render behavior, not a recipe row).

**What Daniel reported (verbatim):** *"the local map doesn't show water, it doesn't show map
features, it's just shroud and 'land color'. I told you to copy the look and feel of the global
map, that clearly didn't happen. Please evaluate in depth. If you need to make a unity based
testing project then so be it. You should be able to preview render exactly what this should look
like in game and message me image captures."*

##### The two competing root-cause claims — and what the decomp actually says

The §2E shipped code assumed the four cartography textures are LIVE under nomap and tried to ride
vanilla's GPU material. The issue-10 card swung the other way: it asserted the textures are **NEVER
generated under nomap**, so `_MainTex == null` forces the `PaintFog` fallback. **Both framings are
wrong on the generation question. Decomp-verified against `~/valheim/sbpr-corpus/subsystems/Minimap.cs`
(line numbers below are that file; the full `assembly_valheim.decompiled.cs` uses a different
numbering but identical logic):**

- **`GenerateWorldMap()` has NO `m_noMap` gate.** `Minimap.Update()` (`Minimap.cs:540-568`) gates
  the bake on exactly four conditions: not headless (`graphicsDeviceType != Null && GetMainCamera()
  != null`, `:552`), `!m_hasGenerated` (`:556`), `WorldGenerator.instance != null` (`:558`), and no
  cached PNG bake on disk (`!TryLoadMinimapTextureData()`, `:562`). **None of them is `m_noMap`.**
- **`m_noMap` only toggles the UI roots, not generation.** `SetMapMode` (`:961-966`):
  `if (Game.m_noMap) mode = MapMode.None;` → `m_largeRoot`/`m_smallRoot` `SetActive(false)`
  (`:976-977`). It never touches the texture bake.
- **Proof it already runs under nomap (no new spike needed):** `UpdateExplore` (`:575`) sits
  *downstream* of the bake block (`:556-568`) in the same `Update()` — you cannot reach `:575`
  without passing the bake. The Cartographer's Kit card (§3 IMPL STATUS) already PROVED in-game that
  `UpdateExplore` runs under v1 nomap (*"personal fog accumulates even under server-side nomap"*).
  Therefore the bake at `:564` necessarily runs too. **The textures ARE generated under nomap.**
- **The bake is pure CPU and deterministic.** `GenerateWorldMap` (`:1639-1682`) loops the 256² grid
  calling `WorldGenerator.instance.GetBiome(wx,wy)` + `GetBiomeHeight(...)`, then writes plain
  `Texture2D`s via `SetPixels`/`Apply`. No GPU. `WorldGenerator.GetBiome`/`GetBiomeHeight` are
  **public** (`assembly_valheim.decompiled.cs:130242/130399`) and deterministic from the world seed.
  `WorldGenerator.Initialize(m_world)` runs on the JOINING CLIENT too — the client reads the server's
  seed off the connect handshake and initializes worldgen (`assembly_valheim.decompiled.cs:67378-67384`,
  the non-server branch). So a client on a dedicated nomap server has `WorldGenerator.instance != null`.

**So why did Daniel see flat fill?** Not "textures don't exist." The failure is **downstream, in
`TryRenderVanillaCartography` itself** — it depends on driving vanilla's **custom GPU map shader**
(the four-texture composite + `_mapCenter`/`_pixelSize`/`_zoom` uniforms) through our own detached
`RawImage` with hand-set uniforms. §2E's own IMPL STATUS flagged this exact piece as unverifiable on
a headless worker and **shipped it blind** — the shader does not composite as hoped on our quad
(wrong/zero output → effectively no main texture or a blank draw → `Render()` falls to `PaintFog`,
the flat two-color fill). **The card's instinct (stop depending on vanilla's render, produce the
composite ourselves) is RIGHT — but for the correct reason: not "the data is missing," but "don't
fight the GPU shader; build the composite on the CPU from data we can read directly."**

##### 🔒 LOCKED ROUTE (re-lock) — sample the composite on the CPU, never the GPU shader

Build OUR own windowed RGBA32 cartography texture by replicating vanilla's *pixel* logic on the CPU,
sampling only **public, deterministic** base-game data. This is the same family of operation §2E
already endorsed (reuse the game we mod), minus the unverifiable GPU dependency.

1. **Keep the entire fork shell + the §2H transform/orientation + the §2F/§2G input model. Replace
   only the cartography-paint step** (`TryRenderVanillaCartography` + `PaintFog`'s role).
2. **Source data — re-sample `WorldGenerator` directly (PRIMARY).** For each window cell, compute its
   world centre (`BoundedMapMath.CellCenterWorldX/Z`, already cell-faithful to vanilla `WorldToPixel`),
   then call `WorldGenerator.instance.GetBiome(wx,wy)` + `GetBiomeHeight(biome,wx,wy,out _)`. Map to
   color by **replicating vanilla's tiny pixel functions** (clean-room-clean — it's our code adapting
   base-game logic):
   - **Biome base color** = `Minimap.GetPixelColor(biome)` (`:1754-1769`): a fixed biome→`Color` table.
     The colors are public `Minimap` fields (`m_meadowsColor` etc., `:237+`); Ocean/unknown = white.
   - **Water** = the height test in `GetMaskColor` (`:1722`): `height < 30f` is ocean
     (`WorldGenerator.c_WaterLevel = 30f`, `assembly_valheim.decompiled.cs:96279`). Render those cells
     as the map's water tone. This is the missing "water" Daniel reported (AT-RENDER-WATER).
   - **Forest/mask stipple** = `GetMaskColor` (`:1719-1752`) per-biome rules (Meadows `InForest`,
     BlackForest always, Plains/Mistlands forest-factor, Ashlands gradient). Optional for v1 of the
     fix — biome + water + relief is the bulk of "looks like the map"; forest stipple is polish.
   - **Relief/height shading** = derive a hillshade from `GetBiomeHeight` (neighbor-delta or the
     vanilla height ramp). Satisfies AT-RENDER-RELIEF.
3. **Why re-sample instead of reading vanilla's baked `m_mapTexture`?** The baked textures exist and
   are CPU-readable (vanilla itself calls `GetPixel`/`GetPixels` on them — `:1636`, `:668`), BUT they
   are **private** (`:301-305`) → reflection, and only populate on a graphical client (gated at
   `:552`). Re-sampling `WorldGenerator` (public, deterministic, no `Minimap` lifecycle dependency)
   is cleaner AND is the **same code path the headless preview harness (§2E.2) runs** — preview and
   in-game render become byte-identical, which is exactly the verification leg Daniel demanded.
   *(Reading the baked textures via reflection is an acceptable fallback if re-sampling proves too
   slow, but the bound 1000 m window is only ~33² = 1089 cells, so the CPU cost is trivial — bake
   ONCE into a cached `Texture2D` at imprint/open, not per-frame.)*
4. **Crop/sample to the window — reuse `BoundedMapMath` (no new math).** The window is the same
   `WindowSpec` (`ComputeWindow`) the fog + pins already use. Walk `wy,wx` over `Size×Size`, compute
   each cell's world centre, sample as above, write into the cartography `Texture2D` (Point filter,
   bottom-up rows = north-up, matching `PaintFog`). The shroud mask (`PaintShroudMask`, our fog window)
   and pin overlay are UNCHANGED and align by construction (one `WindowSpec`). `Point` filter at fixed
   zoom preserves AT-MAP-FIXEDZOOM / AT-TABLEMAP-3.
5. **`SurveyData` wire format UNCHANGED** (answers the card's open-Q2). Cartography is global +
   deterministic from seed and sampled live at render; `SurveyData` still carries ONLY the bool fog
   window + pins → no ZDO contract change, placed Tables don't orphan (AT-TABLEMAP-7 / AT-RENDER
   regression holds).
6. **AT-TABLEMAP-6 / AT-RENDER-NOMAP-INTACT by construction.** We call **`WorldGenerator` sampling
   only** — NOT `Minimap.SetMapMode`, NOT `m_largeRoot.SetActive`, NOT anything that re-enables the
   global map. `Game.m_noMap` and `GlobalKeys.NoMap` are never written. If a `Minimap.ForceRegen()`
   (`:532`, **public**) call is ever used to warm vanilla's cache, note that it too only bakes
   textures and never touches roots/`SetMapMode` — but the PRIMARY route doesn't need it at all,
   since we sample `WorldGenerator` ourselves.
7. **Graceful degradation (keep).** If `WorldGenerator.instance == null` (world not yet initialized —
   shouldn't happen post-join, but guard it), keep `PaintFog` as the two-color fallback so the viewer
   is never blank. `PaintFog` stays in the codebase.

**Net change vs. shipped §2E:** delete the GPU-material-copy path (`_mapMaterial` instantiate,
`_FogTex` reveal override, shader-uniform driving, `uvRect` framing). Replace with a CPU sampler that
fills the existing cartography `RawImage`'s `Texture2D`. The shroud-mask `RawImage`, overlay, title,
exit prompt, and §2H rotate/center are all untouched.

##### 2E.1 acceptance tests (named, observable — close only on Daniel's in-game check + the §2E.2 preview)
- **AT-RENDER-WATER** — water (`height < 30`) renders as a distinct water tone within the disc, not
  the land fill. (The headline defect.)
- **AT-RENDER-BIOME** — biome coloring matches the vanilla map's biome palette (meadows green,
  black-forest, swamp, mountains, plains, etc.) via the `GetPixelColor` table.
- **AT-RENDER-RELIEF** — height/relief shading is visible (hillshade or height ramp from
  `GetBiomeHeight`).
- **AT-RENDER-NOMAP-INTACT** — the global map stays disabled; `m_largeRoot`/`m_smallRoot` never
  re-enable; `Game.m_noMap`/`GlobalKeys.NoMap` untouched (subsumes AT-TABLEMAP-6).
- **AT-RENDER-PREVIEW** — a headless preview PNG (§2E.2) of the intended bounded output is produced
  and signed off by Daniel **before** in-game ship.
- **AT-RENDER-REGRESSION** — `SurveyData` wire unchanged; placed Tables don't orphan; pins + shroud +
  edge arrow still align (AT-TABLEMAP-4/7).
- logs-green ≠ playable — Daniel confirms in-game the local map looks like the real map, bounded.

> **✅ IMPL STATUS (2026-06-12, card t_e0e8c7a9, engineer-ui).** BUILT. The GPU-material-copy path
> (`TryRenderVanillaCartography`, `_mapMaterial`, `_revealTex`, the `_MainTex`/`_FogTex`/`_zoom`/
> `_pixelSize`/`_mapCenter` shader uniforms, `uvRect` framing) is **DELETED** from `MapViewer.cs`.
> The new `CartographyComposer.Compose(IBiomeSampler, palette, window, …)` (new file
> `Features/Cartography/CartographyComposer.cs`) is a pure CPU function: per window cell it samples
> `WorldGenerator.GetBiome`/`GetBiomeHeight` (via `WorldGeneratorSampler.Live`), maps biome→color
> (vanilla `GetPixelColor` table, read live off `Minimap.instance` with literal fallback), renders
> `height < 30 m` as a depth-ramped water tone (AT-RENDER-WATER), and applies a NE hillshade from
> the height field (AT-RENDER-RELIEF). `MapViewer.TryComposeCartography` bakes it ONCE into a cached
> `Texture2D` per Render and overlays the unchanged shroud mask; `PaintFog` stays as the
> `WorldGenerator.instance == null` fallback. `SurveyData` wire is untouched (AT-RENDER-REGRESSION).
> Build 0/0. **The §2E.2 preview PNGs (same composer source) are the AT-RENDER-PREVIEW evidence,
> pending Daniel's sign-off before merge.** Logs-green ≠ playable — Daniel confirms in-game.

#### 2E.2 — Headless preview harness (Daniel-requested: PNG captures before ship)

> **⛔ SUPERSEDED (2026-06-12) by §2E.3 — harness removed.** This preview leg existed to PNG-preview
> the §2E.1 **CPU composite** off-engine before ship (the build box is headless / GPU-less, so the
> *shader* render could never be previewed here anyway — that was the whole reason the toggle existed).
> Once Daniel confirmed the vanilla **Shader** render in-game on his GPU client (v0.2.23-playtest), the
> CPU composite was removed and this harness lost its only subject. **`tools/cartography-preview/`
> (PngWriter.cs, PreviewPlugin.cs, README.md, SBPR.CartographyPreview.csproj) was DELETED** in the §2E.3
> cleanup cut. The parchment look is now proven in-game (the real bar — logs-green/PNG-green was always
> a proxy), so AT-RENDER-PREVIEW / AT-PARCHMENT-PREVIEW are retired (§2D / AT-PRUNE-*). Kept below for
> the historical record of the "preview == ship" approach.

#### 2E.2 — Headless preview harness (ORIGINAL, superseded — see banner above)

> **Status: NEW — verification leg.** This box is a headless dedicated server (no GPU client), which
> is exactly why §2E shipped blind. The fix: make the cartography compositor **GPU-free and
> standalone** (per §2E.1) so it can run off-engine and emit a PNG that previews the in-game look.

- **The whole point of the CPU re-lock (§2E.1) is that the compositor is a pure function:**
  `(worldSeed, boundOrigin, radius) → RGBA32 window texture`, depending only on `WorldGenerator`
  sampling + our color logic. Factor it into a Unity-free (or Unity-light) core so the SAME code runs
  in-game AND in the harness. Preview == ship by construction.
- **Two viable harness routes (implementer + Daniel pick; the engineer prototypes the cheaper one
  first):**
  - **Route P1 — extract-and-replicate (no engine).** Port the color logic + a `WorldGenerator`
    sample into a tiny standalone .NET tool (or reuse the existing `worldgen-spike` tooling at
    `~/valheim/worldgen-spike/`, which already deterministically derives a world from a seed via the
    `.fwl` writer `gen_world.py`). Sample biome/height for a seed+origin window, composite to a PNG
    with `System.Drawing`/`ImageSharp`. **Pro:** runs anywhere headless, fast, no Valheim runtime.
    **Con:** must keep the ported `WorldGenerator` math in sync with vanilla (drift risk — pin it to
    a decomp cite + a golden-seed checksum test).
  - **Route P2 — batchmode capture.** Run the compositor inside a Unity batchmode harness / the game
    in `-batchmode -nographics` and `EncodeToPNG` the generated `Texture2D`. **Pro:** uses the real
    `WorldGenerator` (zero drift). **Con:** heavier to stand up headless; `SetPixels`/`EncodeToPNG`
    work without a GPU (CPU texture ops), but the harness must boot enough of the game to init
    `WorldGenerator` from a seed — the `worldgen-spike` server bootstrap is the proven precedent.
- **Deliverable:** PNG capture(s) of the bounded 1000 m window for a known seed/origin (ideally
  Daniel's playtest world seed), messaged to Daniel for sign-off on AT-RENDER-PREVIEW **before** the
  in-game change is shipped. Include at least one capture spanning a biome boundary + a shoreline so
  water, biome color, and relief are all visible in one frame.
- **This harness is reusable** for every future cartography render change — it converts "logs green"
  into "here's what it looks like," closing the gap that let §2E ship blind.

> **✅ IMPL STATUS (2026-06-12, card t_e0e8c7a9, engineer-ui).** The harness is BUILT and the
> preview PNGs are produced. **Route P1 (port the math headless) was empirically REJECTED, Route
> P2 (real WorldGenerator) built instead — engineer's escalation call per the spec's "fall back to
> P2 if P1 drift proves unfixable" clause.** Concretely:
> - **Why P1 is dead (not just risky):** a standalone .NET probe linking the real `assembly_valheim.dll`
>   confirmed `WorldGenerator.GetBiome`/`GetBiomeHeight` bottom out in `DUtils.PerlinNoise` →
>   **`UnityEngine.Mathf.PerlinNoise`, a Unity NATIVE engine method (ECall)**. Under bare .NET it
>   throws `SecurityException: ECall methods must be packaged into a system module` (and even
>   `World`'s ctor hits `UnityEngine.Random.Range`, another ECall). A faithful P1 would therefore
>   have to reimplement Unity's exact Perlin gradient tables — the precise drift trap this section
>   warns against — so "P1 drift is unfixable" is proven, not assumed.
> - **What P2 is:** a throwaway BepInEx plugin (`tools/cartography-preview/`, NOT shipped) that
>   links the **shipped `CartographyComposer` source** and runs it against the live `WorldGenerator`
>   on a Harmony postfix of `WorldGenerator.Initialize`, inside the dedicated server's Unity runtime
>   (the proven worldgen-spike bootstrap). It writes PNGs with a **pure-C# encoder** (no Unity
>   Texture / GPU) so it works headless. Because it links the *same* composer source, **preview ==
>   ship by construction** — exactly the §2E.2 guarantee.
> - **Result:** 3 PNGs rendered at Daniel's playtest seed (`ForTheWort`, numeric `-756187396`),
>   spanning meadows/black-forest, mountains (relief), and shorelines (water) — water, biome color,
>   and hillshade relief all visible. **Pending Daniel's AT-RENDER-PREVIEW sign-off before the
>   in-game change merges** (the impl PR is blocked review-required with the PNG paths attached).

> **Status: DESIGN CORRECTION (ORIGINAL §2E, 2026-06-11 — SUPERSEDED by §2E.1 above on the render
> route; retained for history).** Supersedes the "paint our own two-color texture" render
> path of §2B bullet 1 / the spike. The fork **SHELL** (own Canvas + open/close path, fixed
> zoom, fixed-radius shroud, pin + player-marker overlay, polar edge-arrow) is UNCHANGED and
> correct — **only the fog-paint step changes.** Reported by Daniel, v0.2.19-playtest, in
> game; applies to BOTH the Surveyor's Table view and the shared field Local-Map view (one
> `MapViewer`). Clean-side (ADR-0001).

**What Daniel reported (verbatim):** *"the surveyor's table does NOT appear to behave like
the regular map. It should be almost identical in behavior to the regular map, just with the
shroud at a fixed RADIUS. and the no zoom alterations."*

**Root cause (grounded).** `MapViewer.PaintFog` (`MapViewer.cs:137-165`) builds a literal
two-color `Texture2D` — `px[i] = fog[i] ? CParchment : CShroud` (`:158-159`, palette `:76-77`).
That is *all* the map shows: no biome color, no height relief, no forest, no water. The fork
was built standalone-from-scratch (PR #101) to avoid riding vanilla's nomap-suppressed
minimap — correct for the OPEN PATH, but the render went too far and discarded vanilla's
cartography wholesale.

**The decisive RE finding (this re-frames the card's "route 1").** There is **NO single
"vanilla map texture" to composite.** The vanilla map is a runtime **GPU shader blend of FOUR
textures**, allocated in `Minimap.Start` (`Minimap.cs:413-442`) and filled by
`GenerateWorldMap` (`:1639-1682`):

| Shader slot | Field | Format | Content (decomp) |
|---|---|---|---|
| `_MainTex`   | `m_mapTexture`        | RGB24 | per-cell biome base color (`GetPixelColor(biome)`, `:1754-1769`) |
| `_MaskTex`   | `m_forestMaskTexture` | RGBA  | forest stipple + ocean/ashlands gradient (`GetMaskColor`, `:1719-1752`) |
| `_HeightTex` | `m_heightTexture`     | RHalf | biome height → drives the hillshade relief |
| `_FogTex`    | `m_fogTexture`        | R8G8  | explored (R) / shared (G) mask (`Explore`, `:1555-1566`) |

…composited by the **custom shader on `m_mapImageLarge.material`** with uniforms `_mapCenter`
(world), `_pixelSize` (`= 200f / zoom`), `_zoom`, `_SharedFade` (set in `CenterMap :1023-1034`
and `Update :628-639`), and the `RawImage.uvRect` windowing the quad (`:1007-1021`). **The
"look of the map" lives in that shader, not in any one texture.** So "composite vanilla's real
map texture" is not a literal operation — the real operation is **reuse the vanilla map
MATERIAL and its four bound source textures.**

**These textures EXIST and are populated under `nomap`** — this answers the card's crux
question. `nomap` only forces `SetMapMode → MapMode.None`, which toggles the UI **roots** off
(`:961-966`). It does **not** gate generation: `Minimap.Update` runs the generation block
(`:556-568`) and `UpdateExplore` **unconditionally every frame, before any mode/`m_noMap`
check** — already VERIFIED by the Cartographer's Kit card (§3 IMPL STATUS: *"personal fog
accumulates even under v1's server-side nomap"*). Therefore `m_mapTexture` /
`m_forestMaskTexture` / `m_heightTexture` are generated, and the **public**
`Minimap.instance.m_mapImageLarge.material` carries all four textures bound (via
`SetTexture` in `Start :435-438`) — readable at runtime, clean-side.

**LOCKED ROUTE — material reuse (the card's route 1, corrected for the no-single-texture
reality). Route 2 — "drive vanilla's renderer in bounded mode" — is REJECTED:** re-enabling
roots risks AT-TABLEMAP-6 and entangles us with the nomap suppression the fork exists to
avoid.

1. **Keep the entire fork shell. Delete only the two-color paint.**
2. **Render through a COPY of the vanilla map material:**
   `var mat = UnityEngine.Object.Instantiate(Minimap.instance.m_mapImageLarge.material);`
   The copy inherits the four texture bindings (shared by reference) + the shader. Apply it
   to the viewer's existing map `RawImage`; set `RawImage.texture = mat.GetTexture("_MainTex")`
   so the RawImage has a valid main texture. Drive **our copy's** `uvRect` +
   `_mapCenter`/`_pixelSize`/`_zoom` to frame the bound origin's 1000 m disc at our single
   fixed scale. We never touch vanilla's own material/roots → **AT-TABLEMAP-6 holds by
   construction.**
3. **Shroud = OUR fog window, NOT vanilla's `_FogTex`.** Composite `SurveyData.Fog`
   (explored-AND-in-disc, already produced by `BoundedMapMath.BuildWindowedFog`) as a
   disc+explored alpha mask OVER the cartography: lit → real map, unlit / beyond-radius →
   opaque shroud. This realizes the ONE deliberate difference (fixed radius). Implementer's
   choice: overlay a mask `RawImage` (simplest, fully decoupled) **or** build a window
   texture and bind it as `_FogTex` on the copy (reuses vanilla's fog-edge fade) — visual
   polish, Daniel verifies.

   > **🔴 SUPERSEDED for the UNEXPLORED appearance (2026-06-17 → §2E.5, cards t_a39d3e5f +
   > t_39324b99).** The "opaque shroud overlay" half of this implementer's-choice was BUILT (the
   > `_shroudImage` flat `CShroudA` fill in the shared `MapSurface`) and Daniel REJECTED its look on
   > first playtest: *"I want the unexplored area to look like it normally does in valheim."* The
   > "build a window texture and bind it as `_FogTex`" option (called "visual polish" here) is now
   > the **required** route — the unexplored area must render as vanilla's real `_FogTex` cloud, NOT
   > a flat opaque fill. The disc+radius CUTOFF geometry of this step still stands; only the
   > unexplored *appearance* is re-locked. See **§2E.5.1 point 1**.
4. **Fixed zoom:** one authored window; no scroll/zoom input (keep `LayoutMapRect`'s
   no-scroll discipline). Disc span = 2000 m ≈ `2000/(m_textureSize*m_pixelSize)` =
   `2000/16384 ≈ 0.122` normalized (`uvRect.width`, × aspect). The matching `_zoom`/
   `_pixelSize` uniform values are **build-calibrated** against the live render (see spike).
   Preserves AT-MAP-FIXEDZOOM.
   > **🔴 SUPERSEDED for the DISC (2026-06-19 → §2E.5.5 point 4).** This single "Disc span =
   > 2000 m" predated the two-scales split. The **modal** still frames the full ~2000 m survey;
   > the **disc** now locks a separate tighter fixed span (`DiscViewSpanMeters = 125 m`) via the
   > `ViewSpanMeters` knob. Both remain fixed-zoom (AT-MAP-FIXEDZOOM holds) — there are simply two
   > authored scales now, not one. See **§2E.5.5 point 4** for the as-built.

**NO SurveyData wire-format change** (answers card open-Q2). The biome/height/forest textures
are **global and deterministic from the world seed** — vanilla regenerates them at `Start`
for the whole ~16 km world (`m_textureSize=256`, `m_pixelSize=64f`, `Minimap.cs:211/213`)
independent of exploration. The viewer **samples them live** at render time using the stored
bound-origin + radius window. `SurveyData` keeps carrying ONLY the bool fog window + pins → no
ZDO contract change, placed Surveyor's Tables do not orphan → **AT-TABLEMAP-7 by
construction.** (Local Map = same engine: live global cartography masked by the item's frozen
snapshot fog window = "the map as it was drawn," now with real terrain. Static terrain +
snapshot shroud is correct.)

**Graceful degradation (mandatory).** If `Minimap.instance == null` or `mat.GetTexture("_MainTex")`
is null at open (generation not yet run), fall back to the current two-color paint rather than
render blank. **Keep `PaintFog` as the fallback path** — do not delete it.

**Mandatory pre-build micro-spike (de-risks the one unverifiable piece — the shader).** The
shader is a GPU asset; its exact `uvRect`-vs-`_mapCenter`/`_pixelSize` sampling semantics
cannot be confirmed from the C# decomp alone. Before the full integration the engineer MUST,
in-client under nomap: instantiate the vanilla map material onto a throwaway RawImage, set
`.texture = mat.GetTexture("_MainTex")`, drive `uvRect`/`_mapCenter`/`_zoom` to a known world
window, and confirm it renders biome cartography (not blank / magenta / clipped). Lock the
calibration constants from that spike — exactly as the original UI-fork spike (t_e8bbbe48)
locked `m_pixelSize`. **If the material cannot be driven this way, BLOCK and re-route — do NOT
silently ship the two-color mask as the shipped behavior.**

**Clean/dirty:** Clean-side (ADR-0001). Reading `Minimap.instance.m_mapImageLarge.material`
plus its bound base-game textures and instantiating a copy is reusing the game we mod at
runtime — the same model as the already-shipped vanilla-UI-sprite/font reuse
(`requirements.md:353`). No decompiled IronGate source is copied into our code; no asset files
are committed; no third-party mod code is touched.

#### 2E acceptance tests (named, observable — close only on Daniel's in-game check)
- **AT-TABLEMAP-1** — the Table map shows the SAME cartographic content as the vanilla map
  for the explored area (biome color + height relief + forest + water), not a two-color mask.
- **AT-TABLEMAP-2** — unexplored cells AND everything beyond the fixed 1000 m radius render as
  opaque shroud (the one intended difference from vanilla).
- **AT-TABLEMAP-3** — no zoom controls; fixed scale (preserves AT-MAP-FIXEDZOOM).
- **AT-TABLEMAP-4** — pins + the polar edge-clamp arrow render at correct positions (preserves
  the existing overlay + `BoundedMapMath.EdgeClampToDisc`).
- **AT-TABLEMAP-5** — open/close, pin display, player marker feel like the vanilla map within
  the bounded disc.
- **AT-TABLEMAP-6** (nomap intact) — the player's world minimap is NOT re-enabled; v1 nomap
  stays in force everywhere except inside our bounded viewer.
- **AT-TABLEMAP-7** (regression) — Local-Map view (same engine) still works; no SurveyData
  wire change, so placed Tables don't orphan.
- logs-green ≠ playable — Daniel confirms in-game it looks/behaves like the real map, bounded.

**Implementation card:** routed to `engineer-ui` (owns `MapViewer.cs`, built it under
t_cb831069), as a child of the issue-6 card. **SpecCheck impact: none** (render behavior, not a
recipe row). Spec + code move together in that PR.

> **🔴 RENDER ROUTE RE-LOCKED — see §2E.1 (issue 10, card t_14c34abe, 2026-06-12).** The
> material-copy route specced in this §2E SHIPPED (PR #123) and FAILED in Daniel's v0.2.22
> playtest (flat land color + shroud = the `PaintFog` fallback). The architect decomp-pass in
> §2E.1 disproves both the shipped premise and the issue-10 card's stated root cause, and
> re-locks the render on a **CPU-sampled `WorldGenerator` composite** (no GPU shader) plus a
> **headless PNG preview harness** (§2E.2) for sign-off before ship. The implementation child
> for this re-lock is the issue-10 render card; route it to the SAME `engineer-ui` worker that
> holds the rest of the viewer cluster (issues #1/#2/#3/#4/#9/#11 — all touch `MapViewer.cs`),
> **render-first** (the CPU composite is the foundation the orientation/overlay fixes ride on).

#### 2E.3 — FINAL LOCKED ROUTE: vanilla styled material is THE render, no toggle (issue 10, confirmed in-game 2026-06-12)

> **✅ LOCKED + SHIPPED-DEFAULT (Daniel, v0.2.23-playtest, 2026-06-12).** The held Local Map renders
> the **REAL vanilla parchment look** — a COPY of vanilla's styled `m_mapImageLarge.material` (the GPU
> map display shader: paper texture + cloud/haze + fog feathering), framed to our bound 1000 m disc,
> with OUR survey fog as the hard shroud mask on top. **Daniel playtested the default Shader render on
> his GPU client and it looked good with NO toggling — he never switched modes.** That empirically
> settles the question this section was opened to hedge: *can vanilla's styled `m_mapImageLarge.material`
> be driven into our bounded `RawImage` on a real GPU client?* **Yes.**
>
> **Decision: the CPU render mode and the `sbpr_mapmode` toggle were REMOVED; Shader is the
> unconditional render.** The CPU composite (§2E.1) was insurance against *"this client can't drive the
> vanilla map shader"* — but a client that can't drive the vanilla map shader **can't see the vanilla
> map either**, so the fallback insured against a non-scenario. Carrying two render paths + a console
> command + a mode enum to hedge a case that can't occur is dead weight, and dead weight in the render
> hot path is a maintenance liability (every future §2H/§2K change had to reason about two paint legs).
>
> **What the render is now (one leg, `MapViewer.TryRenderVanillaShader`):** instantiate a COPY of
> vanilla's styled `m_mapImageLarge.material` (never mutate the live one → nomap intact), assign it to
> the cartography `RawImage`, and frame the bound 1000 m disc by driving `uvRect.center` + `_mapCenter`
> + `_pixelSize` (`200/zoom`) + `_zoom` in **lockstep** (vanilla `CenterMap`, Minimap.cs:1004-1034 — the
> inconsistent transform was the §2E v0.2.22 blank-render bug). Vanilla's native `_FogTex` haze stays
> live inside the disc; OUR survey fog is the hard 1000 m shroud cutoff on top.
>
> **`PaintFog` is the ONLY fallback — and it is NOT a render mode.** It is the never-blank guard for
> the pre-join / `Minimap`-not-generated / GPU-less window (when `TryRenderVanillaShader` returns
> false). On a real GPU client — the only kind that can see the vanilla map at all — the styled material
> is present, so this branch is the steady-state render. The orientation / circular-bezel / pin-label /
> no-North work (§2H.1 etc.) is render-agnostic and unchanged.
>
> **REMOVED in this cut (cleanup card, 2026-06-12):** `CartographyComposer.cs` (the CPU compositor +
> `IBiomeSampler`/`CartographyPalette`/`WorldGeneratorSampler`), `MapRenderMode.cs` (the `MapRenderMode`
> enum + `MapRenderModeState`), `MapModeCommand.cs` (the `sbpr_mapmode` console command + its
> `Terminal.InitTerminal` patch + the `Plugin.Awake` registration), `MapViewer.TryComposeCartography` +
> the cached `_cartoTex`, `MapViewer.RefreshIfOpen` (only the toggle called it), and the throwaway
> `tools/cartography-preview/` harness (it existed only to PNG-preview the CPU composite — orphaned once
> CPU is gone). Build stays 0/0 (TreatWarningsAsErrors). **SpecCheck impact: none** (render behavior, no
> recipe rows). See §2E.1 (CPU composite) + §2E.2 (preview harness) — both now **SUPERSEDED** banners.
>
> **AT index:** AT-PRUNE-1…4 (this cut) replace the CPU-composite render ATs. The parchment look is now
> proven **in-game** (logs-green was never the bar; Daniel's eyes on his GPU client are), so the
> CPU-PNG evidence leg (AT-RENDER-PREVIEW / AT-PARCHMENT-PREVIEW) is retired. See §2D.

### 2E.5 — MapSurface render-correctness re-lock: real `_FogTex` cloud + circular UV clip (first-playtest of the disc, 2026-06-17, cards t_a39d3e5f + t_39324b99)

> **Status: BUG/DESIGN — render-correctness re-lock for the shared `MapSurface`.** First Daniel
> playtest (v0.2.26-dev) of the carry minimap DISC (shipped t_7dd54899, §4.1/§4.2 of
> `map-provider-binding-impl-spec.md`) and the held MODAL viewer surfaced **three render defects**
> that are NOT new design — they are the shipped `MapSurface` drifting from the §2E.3 locked route
> ("vanilla's native `_FogTex` haze stays live inside the disc") into the §2E.4-step-3 "opaque
> shroud overlay" branch. Daniel sharpened the long-standing "wrong visuals" requirement to its
> exact vanilla mechanism, so this section **re-locks** the render and **supersedes §2E.4 step 3's
> implementer's-choice** (opaque-shroud-vs-windowed-`_FogTex`) in favour of the real fog cloud.
> Two cards, ONE fix: the disc (`TargetPx=200`, `t_a39d3e5f`) and the modal (`TargetPx=900`,
> `t_39324b99`) share `MapSurface` by design (`MapSurface.cs:16-18`); this section fixes both,
> parameterized by scale — **do not author a divergent second fix.** Clean-side (ADR-0001): reads
> and adapts vanilla `Minimap` material + `m_fogTexture` + decomp; no third-party mod code.
> **SpecCheck impact: none** (render behaviour, no recipe row). Spec + code move together in the
> implementation PR.

**What Daniel reported (verbatim, 2026-06-17, WITH screenshot
`~/.hermes/kanban/attachments/minimap-multilevel-2026-06-17.webp`):**
- Disc (t_a39d3e5f): *"minimap doesn't work on multiple levels: here's an image 😛"*
- Modal (t_39324b99): *"there are still major rendering issues with the map itself including that it
  has the wrong visuals and it also has gaps around the edges where the square doesn't fit the
  circle, same sort of issue as the minimap."*
- Sharpened "wrong visuals" (both cards): *"I want the unexplored area to look like it normally does
  in valheim on the regular map :("* — the unexplored region must render with vanilla's **fog-of-war
  cloud**, NOT a flat dark fill.

> **⚠️ "doesn't work on multiple levels" — scope note for Daniel at review.** The architect reads
> "multiple levels" as **the several distinct defects below** (the screenshot supports this:
> black-square backing + mostly-black interior + diamond geometry are three separate render faults
> in one image). It is NOT read as elevation/altitude/floors (the bounded survey is a flat 2-D
> top-down window; there is no vertical-level concept in the cartography). **Confirm at review** if
> "levels" meant something about height; if so this section's scope expands.

#### 2E.5.0 — The three defects, decomp-grounded

The shipped steady-state render is `MapSurface.TryRenderVanillaShader` (`MapSurface.cs:188-245`) —
NOT `PaintFog` (the 2-colour fallback at `:444-475`, taken only when the vanilla styled material is
unavailable). The screenshot **proves the shader path is live**: it shows real biome content (forest
green, ocean blue, a tan path), which the flat `PaintFog` palette (`CParchment`/`CShroud`) cannot
produce. So all three defects live in the shader path + its overlays, not the fallback.

| # | Defect (observed) | Root cause (grounded) |
|---|---|---|
| 1 | **Opaque black SQUARE backing** — the disc sits on a solid black panel filling its bounding box; only the bezel ring should show, outside the circle transparent. | `EnsureBezelTexture` (`MapSurface.cs:831-873`) fills every texel beyond `ringOuterR` with `coverage=1` (opaque) tinted `cornerShroud=(0.04,0.035,0.03)` ≈ black (`:855,:865-868`). On the **modal** (`ShowBackdrop=true`, `:901-910`) that opaque square reads as the intended dim backdrop. On the **HUD disc** (`ShowBackdrop=false`) there is no backdrop, so the bezel's own opaque corner-fill **is** the black square Daniel sees. The bezel was authored as "ring + shroud one contiguous opaque cover" (a modal assumption); the disc needs the area **outside the ring to be transparent**, not opaque. |
| 2 | **Mostly-black interior** — only a thin strip of terrain is lit; the rest inside the circle is opaque black, not vanilla fog. | The shroud overlay `_shroudImage` (`PaintShroudMask*`, `:302-368`) paints every unexplored/out-of-disc texel `CShroudA=(10,9,8,255)` — a **flat near-opaque RGBA fill laid ON TOP of the cartography**, occluding it. This is the §2E.4-step-3 "opaque shroud" branch. It is exactly the flat fill Daniel rejected: it hides BOTH unexplored terrain AND vanilla's real `_FogTex` cloud. The disc also resamples this mask at `DiscShroudTexN=128` (`:293`) against the table-anchored survey — correct as *geometry*, wrong as *appearance* (opaque, not cloud). |
| 3 | **Diamond / 45°-rotated geometry** — the lit content forms a rotated square inscribed in the circle; black at the four diagonal corners; ocean wedges bleed along the diamond edges. | The §2H.1 **geometric guarantee** (`:1802-1807`: "the visible disc is the square's inscribed circle, invariant under rotation; no empty corners ever appear") holds ONLY if the cartography square is uniformly valid to its inscribed circle. It is not: `TryRenderVanillaShader` frames `_MainTex` with a `uvRect` window (`:232-236`) **and** drives `_zoom`/`_pixelSize`/`_mapCenter` shader uniforms (`:239-241`) — **two transforms that must agree** (vanilla drives them in lockstep in `CenterMap`, decomp `Minimap.cs:1004-1034`). When they disagree at disc scale (or the window over-/under-shoots), the genuinely-sampled region is a smaller square than the rect; the rotating interior then shows that inner square as a **diamond**, with the rect's true corners (and the gap between the inner square and the inscribed circle) showing unexplored ocean / black. The diamond is the **falsification of the §2H.1 corner guarantee** — empty corners DID appear. |

> **The unifying root cause.** Defects 2 and 3 are the same mistake from two angles: the surface is
> **not reusing vanilla's real cartography compositing** — it samples `_MainTex` through a
> hand-driven window and then **overrides the reveal** with its own opaque flat shroud instead of
> letting vanilla's `_FogTex` shader render the cloud. Defect 1 is the bezel built for the modal's
> opaque-backdrop world bleeding onto the no-backdrop disc. All three are "we approximated the map
> instead of reusing it" — the issue-#10 scope-erosion arc, now at disc scale.

#### 2E.5.1 — The re-lock (what the render MUST do)

**1. Reuse vanilla's real `_FogTex` as the unexplored cloud — DELETE the opaque flat shroud overlay
(defect 2; supersedes §2E.4 step 3).** The fog-of-war cloud Daniel wants is a **GPU-shader product**
of vanilla's map material, not a colour we paint. Decomp (`Minimap.cs`, clean-side):

| Shader slot | Field | Format | Content |
|---|---|---|---|
| `_MainTex` | `m_mapTexture` | RGB24 | full-world biome base colour (`:435`) |
| `_MaskTex` | `m_forestMaskTexture` | RGBA | forest stipple + water gradient (`:436`) |
| `_HeightTex`| `m_heightTexture` | RHalf | height → hillshade relief (`:437`) |
| `_FogTex` | `m_fogTexture` | R8G8 | **reveal mask**: `R=0` explored / `R=255` fogged (`:438`; `Reset` fills 255 `:495-498`; `Explore` sets `pixel.r=0` `:1561-1563`) |

The cloned material (`_shaderMat`, `MapSurface.cs:202`) already inherits all four bindings by
reference, so vanilla's live `_FogTex` cloud is **already available** — the surface just **throws it
away** by laying its own opaque `_shroudImage` on top. The fix:
   - **Bind a windowed reveal as `_FogTex` on the clone**, derived from `SurveyData.Fog` in vanilla's
     R8G8 convention (lit → `R=0`, unexplored/out-of-disc → `R=255`), at the SAME UV window as
     `_MainTex` so reveal and biome align. This lets vanilla's shader composite the **real cloud**
     for the unexplored area (AT-FOG-VANILLA) while the bounded survey still controls *what is
     revealed* (the 1000 m disc). The bounded-survey window is the *reveal authority*; vanilla's
     shader is the *look authority*.
   - **Retire the opaque `_shroudImage` RGBA fill** as the appearance layer. Its geometry job
     (table-anchored vs player-centred reveal, §4.2/R1) moves into **building the windowed
     `_FogTex`** instead of an opaque overlay. The hard 1000 m radius cutoff (beyond the bound =
     full shroud) is expressed as `R=255` in that windowed fog, which vanilla then renders as solid
     cloud — matching the regular map's unexplored look by construction.
   - **`CShroud`/`CShroudA` flat colours are no longer the unexplored appearance.** They remain only
     inside `PaintFog` (the GPU-less never-blank fallback, unchanged).

> **Decomp caveat the implementer MUST spike (the one unverifiable piece — the GPU shader).** Whether
> the cloned material samples `_FogTex` in **full-texture UV space** (so a windowed reveal must be
> written into a 256²-aligned sub-rect) vs the **`uvRect`-windowed space** (so a small Size²/N²
> reveal aligns 1:1 with the framed `_MainTex`) cannot be confirmed from C# decomp alone — it is
> shader-internal. The §2E.4 pre-build micro-spike discipline applies: on a GPU client, drive a
> throwaway RawImage with the cloned material, write a known windowed `_FogTex`, and confirm the
> cloud lands where expected before integrating. **If the windowed `_FogTex` cannot be made to
> register with `_MainTex`, fall back to feeding vanilla's live full-world `_FogTex` directly and
> doing the hard 1000 m cutoff as a separate circular alpha clip (§2E.5.1 point 3) — but do NOT
> revert to the opaque flat shroud.** (This is the same headless-can't-judge-the-shader limit that
> made §2E ship blind; Daniel's GPU client is the verification leg — §2E.5.3.)

**2. Make the `uvRect` window and the shader uniforms agree so the cartography fills its rect
(defect 3).** The diamond is a framing-transform disagreement. Vanilla frames the large map by
driving `uvRect.center=(mx,my)`, `uvRect.width/height=zoom`, AND `_zoom`/`_pixelSize=200/zoom`/
`_mapCenter` **together** from one `CenterMap` call (decomp `Minimap.cs:1004-1034`). The surface must
do the same so the genuinely-sampled biome region **fills the whole square rect** (out to its
corners), making the §2H.1 inscribed-circle guarantee true again:
   - Re-derive `uvCx/uvCy/zoom` (`MapSurface.cs:221-236`) and the `_zoom`/`_pixelSize`/`_mapCenter`
     uniforms (`:239-241`) from a **single** framing computation, not two independently-derived
     paths, so they cannot drift. The window must cover the **full square rect to its corners**
     (radius = half-edge·√2 of valid content), so that after the circular clip (point 3) the
     inscribed circle is uniformly valid — no diamond, no corner gaps.
   - This is `TargetPx`-agnostic: the disc (200) and modal (900) use the same framing math at their
     scale. The modal's "square doesn't fit the circle" edge gaps (t_39324b99 defect 2) and the
     disc's diamond are the **same** framing/clip fault at two scales.

**3. Circular clip that is TRANSPARENT outside the disc — split the bezel's ring from its backing
(defect 1 + the modal corner gaps).** The visible disc must be a clean circle on a **transparent**
field (game world shows through outside it), with the bezel ring drawn ON the circle's edge — NOT an
opaque square panel. The #159 hard alpha-clip must clip the cartography to the circle *without*
painting the outside opaque:
   - **Decouple "ring appearance" from "corner coverage."** `EnsureBezelTexture` currently makes the
     ring + everything-beyond ONE contiguous **opaque** cover (`:854-868`, `cornerShroud` α=1
     everywhere past `ringOuterR`). For the disc that opaque beyond-ring region is the black square.
     Re-lock: **inside `holeR` transparent (cartography shows); a bronze ring band `holeR→ringOuterR`;
     beyond `ringOuterR` α=0 (TRANSPARENT)** — the world shows through outside the disc. The
     cartography itself must be clipped to the circle so it does not draw in the corners (either a
     circular alpha mask on the map RawImage, or the bezel's opaque ring + an inner circular clip on
     the interior — implementer picks; the constraint is *clipped AND outside-transparent*).
   - **Modal vs disc differ ONLY by the backdrop, which already exists** (`ShowBackdrop`,
     `MapSurface.cs:48-49,:901-910`). The modal keeps its dim full-screen backdrop (drawn as a
     *separate* layer, `:903-906`) so the modal still reads as "the whole view." The disc keeps
     `ShowBackdrop=false` and now correctly shows transparent corners. **The bezel itself must be
     transparent-outside on BOTH** — the modal's "outside the disc is dark" comes from its backdrop
     layer, not from an opaque bezel. This removes the disc's black square AND closes the modal's
     corner gaps in one change (AT-DISC-CLIP = AT-MODAL-CLIP).
   - Preserve the #159 fix: the transparent disc stays inset `BezelInsetFrac` inside the inscribed
     circle (`:85`) and the AA stays analytic-to-screen-px (`:864-866`) so no parchment slivers past
     the straight tangents (issue-6 regression guard).

#### 2E.5.2 — What does NOT change (scope fence)
- **Reveal geometry / R1 disc centring** (player-centred camera + table-anchored shroud, §4.2/R1)
  is unchanged as *geometry* — it moves from "build an opaque mask" to "build the windowed
  `_FogTex` reveal," same world→texel mapping (`SampleLitAt`, `MapSurface.cs:271-288`;
  `PaintShroudMaskPlayerCentred`, `:333-368`).
- **§2H.1 orientation** (fixed bezel, interior-only rotation, no-North, rotate-to-heading) is
  unchanged. This section corrects the §2H.1 corner *guarantee*'s **precondition** (the cartography
  must actually fill the square), not the rotation model.
- **The player marker art** (the magnifier-style quad) is a **separate card** — `t_e880a36d`
  (disc player-marker → vanilla arrow or hide). It rides `_overlayLayer` ABOVE the cartography/
  shroud (`MapSurface.cs:949-955`); do NOT fold it into the render fix (different layer, different
  card) — but the SAME engineer should hold both so the overlay and render changes don't clobber.
- **`SurveyData` wire format** unchanged (cartography sampled live; reveal derived from the existing
  bool fog window). No ZDO contract change.
- **nomap intact:** never mutate vanilla's live material/roots/`m_fogTexture`; only read them and
  drive OUR clone. `Game.m_noMap` gate on the disc bind (§4.2/§5) is untouched.

#### 2E.5.3 — Acceptance tests (named, observable — eyeball-judged on Daniel's GPU client)
- **AT-DISC-CLIP** — outside the disc circle is **TRANSPARENT** (the game world shows through); there
  is **no opaque black square** backing. Only the bezel ring + the circular cartography render.
- **AT-MODAL-CLIP** — the modal map's square corners are fully covered: no gaps where the square
  pokes past the circle; the modal reads as a clean disc on its dim backdrop. (Same code path as
  AT-DISC-CLIP at `TargetPx=900`.)
- **AT-FOG-VANILLA** — the **unexplored** area (inside the disc, not yet surveyed) renders with
  **vanilla's fog-of-war cloud** (the real `_FogTex` composited by the real map shader), visually
  matching the regular Valheim map's unexplored look — **NOT** a flat dark `CShroud`/`CShroudA` fill.
  Daniel's eye on a GPU client is the judge. This supersedes the generic "parchment look" wording and
  §2E.4 step 3's opaque-shroud option.
- **AT-DISC-FILL** — the in-circle area is a **continuous disc** of bounded cartography (biome/water/
  relief from the real shader, clipped to the 1000 m survey), **NOT a rotated-square/diamond** sample
  and **no ocean bleeding in at the corners**. The §2H.1 inscribed-circle guarantee holds: rotating
  the interior never uncovers a corner.
- **AT-DISC-SHROUD** — explored area renders lit; only genuinely-unexplored / beyond-1000 m area is
  clouded. The lit region matches what was actually surveyed (not a thin diamond sliver). If a
  starting survey is genuinely sparse, sparse-but-correct is acceptable — the test is that the
  geometry is a disc with a real reveal, not that a fixed fraction is lit.
- **AT-DISC-SHARED** — ONE code path in `MapSurface` produces both the corrected disc (`TargetPx=200`)
  and the corrected modal (`TargetPx=900`); no divergent second renderer (`MapSurface.cs:16-18`).
- **Regression AT-DISC-BEZEL-159** — the #159 hard circular bezel clip still holds: no parchment
  bleed past the disc edge / straight-tangent slivers; the inset + analytic-AA edge are preserved.
- **Regression AT-DISC-NOMAP / §2H.1** — nomap stays enforced (no vanilla material/root mutation);
  rotate-to-heading + interior-only rotation + no-North are unchanged.
- **Logs-green ≠ playable** — the GPU shader cannot be judged on the headless build box. Daniel's
  joined-client eyeball is the real accept (AT-FOG-VANILLA / AT-DISC-FILL / AT-DISC-CLIP especially).
  Separate "can't VERIFY headlessly" (get Daniel's client in the loop) from "can't BUILD it" (it is
  buildable — reuse of an existing material + texture + a windowed reveal); do NOT let the CI box's
  blindness erode AT-FOG-VANILLA back to a flat fill.

#### 2E.5.4 — Routing
ONE `engineer-ui` worker, ONE worktree off `v1`, holding the whole `MapSurface` render-correctness
change (disc t_a39d3e5f + modal t_39324b99) **plus** the disc player-marker card `t_e880a36d`
(separate layer, same file — co-located to avoid collisions). Render-correctness first (it is the
foundation); the marker art rides on top. Do NOT parallel-dispatch on `MapSurface.cs` (the v0.2.20
collision lesson). Open a PR; Daniel gates; the merge bar is Daniel's in-game GPU eyeball on
AT-FOG-VANILLA / AT-DISC-FILL / AT-DISC-CLIP, not a green build.

#### 2E.5.5 — AS-BUILT (impl card t_ba31ad30, 2026-06-19)

The render-correctness fix landed on branch `fix/mapsurface-render-correctness-t_ba31ad30` (off
`main` — `v1` was promoted into `main` via PR #163, so the §2E.5.4 "off v1" routing is stale; build
line is `main`). Three implementation decisions resolved the spec's implementer's-choice points:

1. **Defect 2 reveal — FULL-WORLD `_FogTex`, not a windowed sub-rect (§2E.5.1's documented fallback,
   chosen deliberately).** `MapSurface.BindBoundedReveal(survey, textureSize)` allocates a
   `textureSize²` (256²) R8G8-convention reveal, fills it fully fogged on **both** channels
   (`R=255` **and** `G=255` — the COMPLETE vanilla `Reset` convention, `Minimap.Reset` decomp `:46976`
   fills `(255,255,255,255)`), then clears `R=0` on exactly the lit cells of the table-anchored survey
   window, mapped back to their absolute source-cell position (the inverse of `SurveyData.CaptureWindow`).
   It binds via `_shaderMat.SetTexture("_FogTex", _revealTex)`. Because vanilla's cloned material samples
   `_FogTex` in FULL-texture UV space paired with the full-world `_MainTex`, a 256² reveal registers 1:1
   with the biome **by construction** — sidestepping the windowed-registration spike the spec flagged as
   the one unverifiable-headless risk. The opaque `_shroudImage` RGBA overlay + `PaintShroudMask*` +
   `SampleLitAt` are **deleted**; the unexplored area is now vanilla's real shader-composited cloud.
   The reveal is absolute-world-space, so table-centred and player-centred surfaces share it with no
   resample (R1 falls out for free; the old `DiscShroudTexN=128` disc resample is gone).

   > **🔴 G-CHANNEL CORRECTION (2026-06-19 → card t_48c23824, PR after #192).** The first as-built of
   > this point filled fogged texels as `R=255, G=0` ("mirror Reset's R=255") — but that is INCOMPLETE:
   > vanilla `_FogTex` is two-channel, **R = `m_explored`** (self; `Explore` zeroes R, decomp `:48043`)
   > and **G = `m_exploredOthers`** (shared via the cartography table / other players; `ExploreOthers`
   > zeroes G, `:48091`). `R=255, G=0` is therefore vanilla's encoding for *"someone else explored this
   > and shared it with you"* — so the map shader rendered the **faded shared-map look**, NOT the full
   > fog-of-war shroud. Daniel's v0.2.27-playtest report: *"the unexplored parts … show like … someone
   > else has already explored and is sharing the map with you via the cartography table, but I was
   > expecting the 'full shroud' look."* Fix: fill fogged as `R=255, G=255` (genuinely-unexplored =
   > nobody-explored = vanilla Reset on both channels → solid shroud). `cleared` is `R=0, G=255` (the
   > exact vanilla "self-explored only" state — `Explore` leaves G at its Reset 255). Belt-and-suspenders:
   > the cloned material now pins `_SharedFade = 0` in `TryRenderVanillaShader` (vanilla drives it live
   > from `m_showSharedMapData`, `:47106-47121`) so no shared-data fade can bleed into the bounded view.
   > This is the genuine satisfaction of **AT-FOG-VANILLA** (the merge of #192 landed the `_FogTex` path
   > but on the wrong channel; this lands the correct unexplored *appearance*).

2. **Defect 3 clip — GEOMETRY fan, not a uGUI stencil/mask.** New `CircularRawImage : RawImage`
   (`Features/Cartography/CircularRawImage.cs`) overrides `OnPopulateMesh` to tessellate the rect into a
   128-segment inscribed-disc triangle fan that honours `uvRect` per-vertex. The four corners carry no
   geometry → emit no fragments → are transparent regardless of the bound material. This is the
   material-agnostic choice the spec left open ("circular alpha mask OR bezel ring + inner clip —
   implementer picks"): the cloned vanilla map shader does **not** honour a uGUI `RectMask2D`/stencil, so
   a mask-based clip would be silently ignored at disc scale; the fan makes the §2H.1 inscribed-circle
   guarantee true **by construction** (a disc silhouette is rotation-invariant). The `cartography`
   GameObject now `AddComponent<CircularRawImage>()` instead of `RawImage`. Defect-3 framing root cause
   was concrete: the shipped `_mapCenter` was `(x, z, 0, 0)` — Z shoved into the Y slot and world-Z
   **zeroed** — while vanilla `CenterMap` passes raw world `(x, y, z)` (decomp `:1027`); now fixed to
   `(frameCenter.x, frameCenter.y, frameCenter.z, 0)`, re-agreeing the uvRect and uniform framings.

3. **Defect 1 bezel — alpha BAND + a minimum-absolute ring floor (regression caught headless).**
   `EnsureBezelTexture` alpha is now `clamp01(inner − outer)`: `α=0` inside `holeR`, opaque bronze
   `holeR→ringOuterR`, `α=0` beyond (no `cornerShroud` opaque fill). A headless geometry harness
   (radial-alpha sweep + fan corner-coverage + reveal centroid mapping, all PASS) surfaced that the
   pure `10/900` ring fraction gives the 200 px disc only a ~2.2 px thread — which, now that outside is
   correctly transparent, was the *only* disc edge and read as weak. Added `BezelRingMinPx = 4.5f`
   (`ringPx = Max(TargetPx·BezelRingFrac, BezelRingMinPx)`); the 900 px modal's 10 px ring exceeds the
   floor so its playtested look is byte-preserved. Final ring **weight** is Daniel's GPU-eyeball call
   (one-line bump).

4. **Two fixed zoom SCALES — the disc and the modal are decoupled (Daniel 2026-06-19).** AT-MAP-FIXEDZOOM
   ("neither minimap nor full view zooms; one authored scale each") is now realised as two *different*
   fixed scales sharing one render path, per Daniel's lock: *"the minimap should NOT support zoom… full
   local map locks zoom at 'show full local map' scale. Minimap shows a small portion; if you want to see
   the whole thing, use the whole map."* Implemented with a single `MapSurface.ViewSpanMeters` knob:
   `0` = "frame the whole survey" (the modal's behaviour — `DisplayedSpanMeters` returns `survey.Size *
   pixelSize ≈ 2112 m`, byte-identical to the prior single-scale build; **🔴 RE-LOCKED 2026-06-19 →
   §2E.5.6:** the modal `0` branch now frames `2 × survey radius ≈ 2000 m`, NOT 2112 m, so the surveyed
   disc meets the bezel ring — see §2E.5.6 for the content-to-ring fix + the snapped-pin landmine it
   exposes); `>0` = a fixed metre span (the
   disc). `MapViewer` sets the disc's `DiscViewSpanMeters = 125 m` (Daniel: *"use 125 m by default, we can
   adjust from there"* — a hair tighter than vanilla's small-minimap `m_smallZoom=0.01 ≈ 164 m`). **One
   source of truth prevents pin drift:** `DisplayedSpanMeters(survey)` feeds BOTH the shader framing
   (`zoom = span / (textureSize·pixelSize)`, replacing the old `size/textureSize` that pinned the disc to
   the 1000 m survey window) AND `WorldToSurfacePx` (the continuous pin/marker projection) AND the
   `TryRemovePinAtCursor` inverse — so terrain, pins, and the player marker frame at the exact same scale
   and cannot desync when the disc tightens. `WorldToSurfacePxSnapped` (modal table-cell pins) stays on
   `survey.Size` since the modal's span IS the survey. The 1000 m survey CAPTURE (§4.1 grid-anchored
   exploration invariant) is **untouched** — `ViewSpanMeters` only changes how far out the disc camera
   frames the already-captured data; clamped so it can never frame more than was surveyed. Headless math
   harness confirms: modal 2112 m / disc 125 m, all three projections share the span, modal byte-identical.
   The *feel* of 125 m is Daniel's GPU-eyeball tune (one-line constant). This supersedes §2E.5.1 point 4 /
   §2E.4 step 4's single "Disc span = 2000 m" wording, which predated the two-scales split.

**Headless boundary (unchanged from §2E.5.3):** the harness verifies the GEOMETRY (alpha band, fan
silhouette, reveal world-mapping) on the CI/iGPU box; the GPU SHADER APPEARANCE — that vanilla's fog
cloud actually composites through the overridden `_FogTex` — is still Daniel's RTX/Prime accept on
AT-FOG-VANILLA / AT-DISC-FILL / AT-DISC-CLIP. Build is clean (0/0); the 27-test `BoundedMapMath` suite
still passes (the reveal reuses its windowing math).


#### 2E.5.6 — Modal content-to-ring margin: frame the surveyed disc, not the over-provisioned window (Daniel playtest, 2026-06-19, card t_252f808d)

> **🟢 NEW VISUAL CRITERION (Daniel, v0.2.27-playtest #bugs):** *"the margins between the map and
> the ring are significant, I'd like there to be no margin at all."* §2E.5.5 point 4 pinned the modal
> displayed-span to the **over-provisioned window** (`Size×pixelSize = 2112 m`), but the survey
> content is disc-clipped at `RadiusMeters = 1000 m` (a 2000 m surveyed diameter). The 112 m gap
> between framed-square and surveyed-disc shows on screen as a shroud/fog annulus between the
> cartography edge and the bronze bezel ring. The spec never pinned a *content-meets-ring* relation,
> so this is a **missing visual acceptance criterion** added here alongside the fix — not a worker
> violation of an existing one.

**The geometry (modal, `TargetPx=900`, grounded in the build's `main` == tag `v0.2.27-playtest`
`4c6b18e`; line refs are that code):**

There are **three** radii on the modal, not two — the triage framing of "two constants" missed the
mesh disc:

| radius | px | source |
|---|---|---|
| surveyed-content edge | **421.9** | `R × edge/displayedSpan = 1000 × 891/2112` |
| bezel transparent hole `holeR` | **444.0** | `TargetPx·0.5 − TargetPx·BezelInsetFrac` (`MapSurface.cs:1020-1023`) |
| `CircularRawImage` mesh-disc edge `meshR` | **445.5** | `edge·0.5 = (Size·upscale)·0.5 = 891·0.5` (the fan's inscribed circle) |

The bezel hole (444) and the mesh silhouette (445.5) already coincide (post-#159, by design). The
**content** is the outlier at 421.9 — a **~22 px shroud annulus** that persists even over a fully
explored interior, because the survey is clipped at 1000 m while the frame shows 2112 m. (`edge` is
`Size·upscale = 33·27 = 891` and is **span-independent** — reframing changes the px/metre, not the
rect size.)

**LOCKED approach — (A) frame the modal to the surveyed-disc diameter.** When `ViewSpanMeters <= 0`
(the modal branch), `DisplayedSpanMeters` returns `2 × effective survey radius` instead of
`Size × pixelSize`. Effective radius = `_req.RadiusMeters > 0 ? _req.RadiusMeters : survey.RadiusMeters`
(the same fallback `RebuildOverlay` already uses, `MapSurface.cs:510`) — **do not hard-code 1000**, read
the survey's radius so a future radius change can't silently re-open the gap. At 2000 m the surveyed
disc maps to `1000 × 891/2000 = 445.5 px` ≈ the 444 px hole → **margin ≈ 0** (a ~1.5 px overdraw of
content under the ring's inner edge, which the ring covers — *not* a bleed past `ringOuterR`). The
over-provisioned corner cells (beyond the 1000 m disc, always shroud + always bezel-clipped) simply
stop being framed; nothing of value leaves the view. (A) is the right call; (B) — shrinking `holeR` to
the content — is rejected: it would pull the ring inward off the canvas edge *and* leave the surveyed
disc's far cells (which DO render between 422 and 444 px) as a fog crescent *outside* the new ring,
re-opening the #159 / issue-6 edge-bleed class. (A) grows content to the ring; (B) shrinks the ring
into the content and strands a crescent. Grow, don't shrink.

> **🔴 LANDMINE — (A) is NOT the "clean single-knob" the triage card claimed; it is a coordinated
> TWO-knob change.** §2E.5.5 point 4 asserts "one source of truth prevents pin drift," but that holds
> only for the *continuous* projection (`WorldToSurfacePx`, `:415`) and the cursor inverse
> (`TryRemovePinAtCursor`, `:970`), which both call `DisplayedSpanMeters`. The **cell-snapped** table-pin
> projection `WorldToSurfacePxSnapped` (`:430-453`) does **NOT** — it computes `cell = edge / Size`
> (`:449`), hard-wired to the 2112 m grid, on the explicit assumption (point 4: *"the modal's span IS
> the survey"*) that reframe **breaks**. Leave it untouched and **table pins drift outward up to
> ~+23.6 px at the disc edge** (verified: 250 m→+5.9, 500 m→+11.8, 1000 m→+23.6) — pins float off the
> terrain they annotate. The snapped path MUST be re-derived to project through the *same* displayed
> span: snap world→cell as today (banker's-rounded `WorldToCellX/Y`, preserving byte-faithful cell
> annotation), convert the snapped cell back to its world-centre offset, then project that offset
> through `DisplayedSpanMeters` exactly like `WorldToSurfacePx` — so terrain, snapped pins, continuous
> pins, the player marker, and the cursor inverse all frame at one span and cannot desync. This is the
> real "single source of truth" the original note assumed but the snapped path silently escaped.

**Scope fence — DISC is untouched by construction.** The corner minimap disc sets
`ViewSpanMeters = DiscViewSpanMeters = 125 m > 0` (`MapViewer.cs:46,83`), so it takes the
`> 0` branch of `DisplayedSpanMeters` (`MapSurface.cs:313-314`) and never reaches the modal's
`<= 0` reframe. AT-RING-3 holds without a disc-specific guard — but because `MapSurface` is the
**shared** builder for both surfaces, the change MUST be verified on BOTH (the disc must not be
collaterally re-zoomed). Single-owner this fix; do not split disc/modal.

**Acceptance tests (named, observable — eyeball-judged on Daniel's GPU client; logs-green ≠ playable):**
- **AT-RING-1** *(Daniel is the judge)* — with the full modal map open over a **fully-surveyed**
  area, the surveyed cartography disc's edge **meets the inside of the bronze bezel ring** — no
  visible shroud/fog band between content and ring.
- **AT-RING-2** *(regression — #159 hard clip)* — cartography must **not bleed past** the ring /
  outside the disc when the interior rotates to heading. (A) grows the framed content to the ring
  edge; it must not overgrow into an out-of-disc crescent (don't re-introduce issue-6 edge-bleed).
- **AT-RING-3** *(regression — disc unaffected)* — the corner minimap disc (125 m view) keeps its
  current framing; the change is scoped to the modal (`ViewSpanMeters<=0`) branch only.
- **AT-RING-4** *(regression — table pins track terrain)* — after the reframe, table-view pins and
  the in-disc player marker still land on the exact terrain cell they annotate (the
  `WorldToSurfacePxSnapped` desync is fixed, not shipped). Eyeball: place a pin on a known
  feature, confirm it stays glued under rotation/zoom-scale.

**Headless boundary:** the per-pixel margin arithmetic above is verifiable on the build box (pure
geometry, no shader); the *appearance* — that the content now visually kisses the ring with no fog
band — is Daniel's GPU-client accept on AT-RING-1. The exact target is eyeball-judged: the ~1.5 px
content-under-ring overdraw and the `BezelInsetFrac` residual are within tuning tolerance, adjustable
by a one-line constant if Daniel wants the content pulled a hair tighter or looser.


### 2F — Viewer exit UX: suppress the Escape→menu leak + show an exit prompt (issue 7, 2026-06-11)

> **🔴 OPEN-INPUT NOTE SUPERSEDED (2026-06-17, issue 3, card t_f9a04fda).** Where §2F refers to
> the viewer *opening* on the **Use key (E)** (the "open-input path" wording), that is REPLACED
> by the M-key model — open is now **M** (see §2G banner +
> [`local-map-mkey-open-impl-spec.md`](local-map-mkey-open-impl-spec.md)). **§2F's actual subject
> — the Escape→menu-leak suppression (`Menu.Show` prefix) and the bottom-centre exit prompt —
> STANDS unchanged:** Esc still closes our viewer cleanly with no pause-menu pop, and `[Esc]`
> stays a hardcoded literal (Escape is not a rebindable ZInput button). Only the *open* trigger
> moved E→M; the *exit* UX is untouched.

> **✅ IMPL STATUS (2026-06-11, t_23b950ee → branch `feat/local-map-viewer-overhaul-t_23b950ee`).**
> The §2F LOCKED route is BUILT. Defect 1 (Escape opens the pause menu too): a new
> `SignPanelInputBlock.MenuOpenSuppressPatch` — a `[HarmonyPatch(typeof(Menu), "Show", new Type[0])]`
> skip-original PREFIX (`return !AnyOpen`) — stops `Menu.Show()` from opening the pause menu while
> any SBPR modal is up. Registered in `Plugin.Awake()` right after the other three
> `SignPanelInputBlock.*` containers, so `PatchCheck` sees it woven (AT-VIEWEXIT-7). Because
> `AnyOpen` already covers both sign panels + the viewer, it fixes the identical leak on
> MarkerSignPanel / SignPaintPanel in the same stroke (AT-VIEWEXIT-5). Self-clearing (next Escape
> after close → `AnyOpen` false → pass-through → menu opens normally, AT-VIEWEXIT-3); server-safe
> (`AnyOpen` false on a dedicated server). Defect 2 (no exit prompt): a bottom-centre `Text` label
> built in `MapViewer.EnsureCanvas` (parented to `_root`, toggles with the overlay), mode-aware via
> `UpdateExitPrompt` — `[Esc] Close map` in FieldReadOnly, `+ [Left-click] Remove pin` in TableEdit.
> Literal `[Esc]` (NOT a `$KEY_` token — Escape is hardcoded `KeyCode.Escape`, never a rebindable
> ZInput button). Wears `VanillaUISkin.Font` (degrades to Arial). The viewer's own `Close()` on
> Escape is kept (the half that works). `Menu.Show()` verified as a single parameterless public
> instance method on `class Menu` (decomp :45762; the `Show(bool)` at :43050 is `JoinCode`, a
> different class). Build 0/0. **NOT YET PLAYTESTED — Daniel confirms in-game: Escape closes cleanly
> with no menu pop, exit prompt visible, next Escape opens the menu normally.**

> **Status: BUG + small UX gap.** Reported by Daniel, v0.2.19-playtest, in game. Closes a gap
> §2B left open (the viewer owns its open/close path but the *exit UX* — menu suppression +
> discoverability — was never nailed down). The fork **SHELL, render (§2E), bounding, zoom,
> overlay are all UNCHANGED** — this adds an input-gate hook + one UI label. Applies to BOTH the
> Surveyor's Table view (TableEdit) and the shared field Local-Map view (FieldReadOnly) — one
> `MapViewer`. Clean-side (ADR-0001). **SpecCheck impact: none** (input/UI, no recipe row).

**What Daniel reported (verbatim):** *"issue 7 no clear mechanism to exit the surveyor's table
map viewing mode. escape does exit, but also pulls up the game menu. There should be a prompt at
the bottom for how to exit, or escape should 'just work' without opening the game menu."*

#### 2F.1 Two defects, and a correction to the card's premise

**Defect 1 — Escape closes the viewer AND opens the vanilla pause menu the same frame.**
`MapViewer.Update` (`MapViewer.cs:387-399`) does `if (Input.GetKeyDown(KeyCode.Escape)) Close();`.
The viewer closes — but the *same* Escape keypress also reaches vanilla's menu handler that
frame, so the pause menu opens too.

The vanilla gate, grounded (decompiled `assembly_valheim.dll`, `Menu.Update`):

```
// Menu.Update, the "menu not already visible" branch (decomp):
bool flag = !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Console.IsVisible()
         && !TextInput.IsVisible() && !ZNet.instance.InPasswordDialog()
         && !ZNet.instance.InConnectingScreen() && !StoreGui.IsVisible()
         && !Hud.IsPieceSelectionVisible() && !UnifiedPopup.IsVisible()
         && !PlayerCustomizaton.IsBarberGuiVisible() && !Hud.InRadial();
if ((ZInput.GetKeyDown(KeyCode.Escape) || /* JoyMenu… */) && flag && !Chat.instance.m_wasFocused)
    Show();
```

Vanilla opens the pause menu on Escape **only when `flag` is true** — i.e. when *no* recognized
modal UI is up. Our viewer is a standalone uGUI overlay that satisfies **none** of those
predicates, so from `Menu`'s view nothing is open → `flag` stays true → Escape does double duty.

> **⚠️ The card's premise is half-wrong — corrected here (verified on `SignPanelInputBlock.cs`).**
> The card states the viewer *"does NOT route through `SignPanelInputBlock` or any equivalent."*
> **It already does.** `SignPanelInputBlock.AnyOpen` (`:41-44`) reads
> `SignPaintPanel.IsOpen || MarkerSignPanel.IsOpen || CartographyViewer.IsViewerOpen` — the
> viewer was wired in at build (card t_cb831069). All three `SignPanelInputBlock` patches
> (`Player.TakeInput`, `PlayerController.TakeInput`, `GameCamera.UpdateMouseCapture` — `:46-81`)
> already fire while the viewer is open. So the viewer is NOT missing character-input blocking,
> camera-look freeze, or cursor release — those work. **The single real gap is that none of those
> three seams touch the Escape→`Menu.Show` path.** That gate is what leaks.
>
> **⚠️ 2026-06-17 correction (see §2L, card t_f7a6db7a): the "cursor release — those work" clause
> above is FALSE on the shipped v0.2.26-dev build.** The wiring is real (the patch is registered and
> keyed on the viewer), but the seam it postfixes — `GameCamera.UpdateMouseCapture` — was emptied by
> vanilla in a Unity-Input-System update (IL-confirmed: `IL_0000: ret`), so the cursor release is a
> no-op against the live lock owner. Character-input blocking and camera-look freeze DO still work
> (they ride the live `Player.TakeInput` / `PlayerController.TakeInput` seams). Only the cursor half
> is dead. §2L re-seats it on a live seam.
>
> **Second correction: `MarkerSignPanel`/`SignPaintPanel` are NOT a working reference to copy —
> they have the *identical* leak.** Both also raw-poll `Input.GetKeyDown(KeyCode.Escape)` in their
> own `Update` (`MarkerSignPanel.cs:96`, `SignPaintPanel.cs:144`) and both route through the same
> `AnyOpen` that does *not* suppress `Menu.Show`. The reason the leak wasn't reported on the sign
> panels is incidental (they're smaller, dismissed faster, less obviously "modal"). The fix below
> closes the leak for **all three SBPR modal UIs at once** via the shared helper — which is also
> why AT-VIEWEXIT-5 is "fix the panels too," not "make the viewer match the panels."

**Defect 2 — no on-screen exit prompt exists.** Nothing in `EnsureCanvas`/`Render`
(`MapViewer.cs:463-516`) builds an instructional label. The overlay shows map + pins + player
marker but never tells the player how to leave.

#### 2F.2 Fix Defect 1 — suppress `Menu.Show` while any SBPR modal UI is open (Daniel's route a)

Daniel's preferred outcome is *"Escape just works"* — the viewer closes and the menu does NOT
open. Realize it by making the **shared** `SignPanelInputBlock` also gate the one seam it
currently misses: vanilla's pause-menu open.

**Seam = `Menu.Show()` (Harmony Prefix, skip-original when `AnyOpen`).** Grounded choice:

- `Menu.Show()` is a **single parameterless public instance method** (decomp `Menu.cs:212`); its
  **only internal caller is the Escape/JoyMenu gate** in `Menu.Update` (decomp `:366`). Prefixing
  it to early-return (skip original) while `SignPanelInputBlock.AnyOpen` is true cleanly prevents
  the pause menu from opening on the same Escape that closes our viewer — **without** consuming the
  keystroke globally or touching any other input path.
- **Why a `Menu.Show` prefix and NOT the `Minimap.IsOpen()` predicate (rejected route):** making
  our viewer report through `Minimap.IsOpen()` would satisfy `flag`, but `Minimap.IsOpen()` is
  referenced in **~10 vanilla gates** (build placement, crafting, interact, camera, attach-point —
  verified by grep over the decompiled assembly). Hooking it to return true while our overlay is up
  would silently alter all of them (e.g. suppress build/craft input) → wide, surprising blast
  radius. `Menu.Show` has exactly one caller and one effect. **Lock: `Menu.Show` prefix.**
- **Scope is self-clearing (AT-VIEWEXIT-3).** The gate keys on `AnyOpen`, which is false the moment
  the viewer/panel closes. The very Escape that closes the viewer is swallowed for the menu *that
  frame*; the *next* Escape (viewer now closed → `AnyOpen` false → prefix passes through) opens the
  menu normally. We never permanently eat Escape.
- **Unify, don't fork.** Add the prefix as a **fourth nested patch container inside
  `SignPanelInputBlock`** (e.g. `MenuOpenSuppressPatch`), gated on the same `AnyOpen`. This is the
  "one shared SBPR modal-input path" the card recommends — and because `AnyOpen` already includes
  all three surfaces, it fixes the viewer **and** the sibling `MarkerSignPanel`/`SignPaintPanel`
  Escape→menu leak in the same stroke (AT-VIEWEXIT-5), with zero new per-surface code.

**Registration (load-bearing — the PatchCheck lesson).** Each nested `[HarmonyPatch]` container
must be handed to `harmony.PatchAll(typeof(...))` individually in `Plugin.Awake()` — exactly as the
existing three `SignPanelInputBlock.*` containers are (`Plugin.cs:258-260`). A new nested patch that
is authored but never registered compiles, ships, and silently does nothing; `Runtime/PatchCheck.cs`
will ERROR-log it at boot, but the engineer must add the `PatchAll` line so it's actually woven.

**Server-safe by construction.** `AnyOpen` is false on a dedicated server (no local Player → no
panel/viewer ever opens), so the prefix is pure pass-through there — same inertness discipline as the
existing three patches (`SignPanelInputBlock.cs:30-33`).

**Keep the viewer's own `Close()` on Escape** (`MapViewer.cs:395-398`) — that's the half that
works. The new prefix only stops the *menu* from also opening. (Equivalent for the panels'
`Hide()`.) Belt-and-suspenders note for the implementer: the `Menu.Show` prefix is the load-bearing
fix; do not *also* try to consume the key via `Input`/`ZInput` reset — one clean seam, not two.

#### 2F.3 Fix Defect 2 — exit prompt label in the viewer canvas

- Add a bottom-center `Text` label to the viewer's canvas (built once in `EnsureCanvas`, parented
  to `_root` so it toggles with the overlay), e.g. **"[Esc] Close map"**.
- **Prompt key token — literal `[Esc]`, NOT a `$KEY_` bound-key token (corrects the card's open
  question).** The card recommends a bound-key token "for rebind-correctness, consistent with
  t_7816c0b0." **That is wrong for THIS key:** Escape in vanilla is a **hardcoded
  `KeyCode.Escape`** (23 call-sites across the decompiled assembly, including the `Menu` gate
  itself) — it is **never** registered as a rebindable `ZInput` button, so there is no `$KEY_`
  token that resolves to it and no rebind to stay correct against. A `$KEY_…` here would leak as a
  literal unresolved token (the exact 2026-06-05 bug `CairnInteractable.cs:58-65` documents). Use
  the literal `[Esc]`. The `$KEY_` token idiom remains correct for **bindable** actions (e.g. the
  `$KEY_Use` interact prompt on `SurveyorTableTag.cs:92`).
- **TableEdit mode — surface the pin-removal affordance too.** In TableEdit the viewer already
  does left-click-removes-pin (`MapViewer.cs:404`, gated on `_req.Mode == TableEdit && PinEditor != null`).
  Extend the prompt line in that mode only, e.g. **"[Esc] Close map    [Left-click] Remove pin"**.
  FieldReadOnly mode shows just the close hint (no pin editing there). The click verb is a bindable
  action — if a token is used for it, `$KEY_Use`-style localization via `Localization.instance.Localize`
  is the correct idiom (mirror `CairnInteractable.cs:58-65`); plain "[Left-click]" is also acceptable
  since left-click for UI interaction isn't a Trailborne-rebound action.
- **Skin/degrade like the panels.** Reuse the shared `VanillaUISkin.Font` and a flat-color fallback
  (same discipline as `MarkerSignPanel`/`SignPaintPanel`), so the label wears the native look and
  degrades gracefully if the skin donor is absent. Visual polish (placement, size, drop-shadow) is
  Daniel's in-game call.

#### 2F.4 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-VIEWEXIT-1** — With the Surveyor's Table viewer open, Escape CLOSES the viewer and does
  **NOT** open the pause menu.
- **AT-VIEWEXIT-2** — A clear exit prompt is visible while the viewer is open (bottom-center,
  e.g. "[Esc] Close map").
- **AT-VIEWEXIT-3** — After Escape closes the viewer, a **subsequent** Escape opens the pause menu
  normally (suppression is scoped to while-a-modal-is-open; Escape is never permanently eaten).
- **AT-VIEWEXIT-4** — Same clean exit for the field **Local-Map** viewer (shared `MapViewer`
  engine), both prompt and menu-suppression.
- **AT-VIEWEXIT-5** (consistency) — `MarkerSignPanel`'s Escape (`:96`) and `SignPaintPanel`'s
  Escape (`:144`) likewise no longer leak the pause menu — fixed in the same pass via the shared
  `SignPanelInputBlock` gate (they share the identical pre-fix leak; this is a fix, not a
  match-the-reference).
- **AT-VIEWEXIT-6** (no regression) — The viewer's own inputs still work while open: TableEdit
  left-click pin removal (`:404`), pin display, player marker, edge-arrow. Menu suppression must
  not block the viewer's interactions. The new `Menu.Show` prefix must NOT suppress the pause menu
  during normal play when no SBPR modal is open (`AnyOpen` false → pass-through).
- **AT-VIEWEXIT-7** (registration) — `PatchCheck` reports the new nested patch container as
  registered at boot (no UNREGISTERED PATCH CLASS error) — i.e. `Plugin.Awake()` actually
  `PatchAll`'d it.
- logs-green ≠ playable — Daniel confirms in-game: Escape closes cleanly with no menu pop, and the
  exit prompt is visible.

#### 2F.5 Routing + dependency note

- **Clean-side → `engineer-ui`** (owns `MapViewer.cs` + the sign panels + `SignPanelInputBlock`).
  Hooking `Menu.Show` / vanilla input gates is base-game (ADR-0001, fair game); no third-party mod
  code.
- **Lands in:** `Features/Signs/SignPanelInputBlock.cs` (new nested `Menu.Show` prefix container),
  `Plugin.cs` (its `PatchAll` registration), `Features/Cartography/MapViewer.cs` (exit-prompt
  label in `EnsureCanvas` + mode-aware text). No `SurveyData`/wire change.
- **Shares `MapViewer.cs` with the issue-6 render card (§2E).** Both edit the viewer. They are
  **separable** (this touches `EnsureCanvas`'s UI build + an input patch; §2E touches `PaintFog`/the
  render material) but if both run concurrently they will both modify `MapViewer.cs`. **Sequence
  recommendation:** land §2E (render) first or assign **both to the same `engineer-ui` worker** so
  the exit-prompt label and the material-reuse render land without a merge conflict on the same
  file. Note the dependency on the implementation card.
### 2L — Cursor stays locked at the Surveyor's Table map: the cursor-release seam was emptied by vanilla (issue 7, 2026-06-17, card t_f7a6db7a)

> **✅ IMPLEMENTED 2026-06-19 (card t_1f82da71, the impl child of t_f7a6db7a).** The §2L.4 fix shipped:
> `SignPanelInputBlock.MouseCapturePatch` (dead postfix on the emptied `GameCamera.UpdateMouseCapture`)
> was replaced by `SignPanelInputBlock.CursorPumpPatch`, a postfix on the LIVE `GameCamera.LateUpdate`
> seam (§2L.6 **option 2**) that, while `AnyOpen`, re-asserts `Cursor.lockState=None`/`visible=true`
> every frame and, on the `AnyOpen` true→false edge (one `_wasOpen` edge-detector), restores
> `Locked`/`visible=false` exactly once (§2L.4b). `Plugin.cs` registration re-pointed at
> `CursorPumpPatch`; `AnyOpen`, both `TakeInput` postfixes, and the `Menu.Show` prefix are UNCHANGED.
> Type kept named `SignPanelInputBlock` (rename to `ModalUiSession` declined as pure churn — §2L.5
> permits this; a class-doc note records that it is the shared modal guard). Build: 0 errors, 0
> warnings. Closes on Daniel's in-game check of AT-TABLE-FIELD-CURSOR / AT-TABLE-CURSOR-FREE /
> AT-TABLE-PIN-REMOVE / AT-TABLE-RESTORE / AT-SIGN-CURSOR-REGRESSION (logs-green ≠ playable).

> **Build seen:** v0.2.26-dev (Daniel, 2026-06-17 in-game playtest). Daniel: *"issue 7, at the
> map table, my mouse is not free to move and click on pins to remove."*
>
> **Grounding for this whole section** was done against the LIVE managed assembly Daniel runs
> (`assembly_valheim.dll` / `assembly_utils.dll`, m_playerVersion 43, decompiled with ilspycmd) and
> the in-repo source at `origin/main`. Every vanilla claim below cites IL or decompiled source, not
> memory. Where this section disagrees with the card body or with §2F.1, this section is the
> corrected, verified account.

#### 2L.1 The card's premise is wrong in BOTH directions — what is actually true

The card states the bug is *"a MISSING mechanism — NOTHING frees the cursor or blocks gameplay
input… ZERO hits in Cartography."* That is the result of grepping **only** `Features/Cartography/`.
The cursor-free + input-block mechanism is **not** in that folder — it lives cross-feature in
`Features/Signs/SignPanelInputBlock.cs`, and it **already names the viewer**:

```csharp
// SignPanelInputBlock.cs:55-58  (committed 4aa0ef1, in v0.2.25-playtest — verified ancestor)
internal static bool AnyOpen =>
       SignPaintPanel.IsOpen
    || MarkerSignPanel.IsOpen
    || SBPR.Trailborne.Features.Cartography.CartographyViewer.IsViewerOpen;   // ← viewer already wired
```

All four `SignPanelInputBlock` patch containers are registered in `Plugin.Awake()`
(`Plugin.cs:298-307`). So the mechanism exists, is wired to the TableEdit viewer, and ships.
**But §2F.1's counter-claim is ALSO wrong:** it asserts *"the viewer is NOT missing… cursor
release — those work."* They do **not** work, for the reason §2F never checked. The truth is a
third thing neither the card nor §2F states:

> **🔴 ROOT CAUSE: the cursor-release patch targets a vanilla method that is now EMPTY. Vanilla
> moved cursor management out of `GameCamera.UpdateMouseCapture` in a Unity-Input-System update.
> Our postfix still runs every frame, but the method it postfixes no longer touches the cursor, so
> setting `Cursor.lockState = None` there is a no-op against the live lock owner.**

This is a **stale-seam regression in the BASE GAME**, not a missing SBPR mechanism. The SBPR code
is the same code that worked on an older Valheim; the seam rotted under it.

#### 2L.2 The regression, proven by old-vs-new assembly diff (the load-bearing evidence)

**Our patch (`SignPanelInputBlock.cs:85-95`)** postfixes `GameCamera.UpdateMouseCapture`:

```csharp
[HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
public static class MouseCapturePatch {
    [HarmonyPostfix] private static void Postfix() {
        if (!AnyOpen) return;
        Cursor.lockState = CursorLockMode.None;   // free the cursor each frame while a modal is open
        Cursor.visible = true;
    }
}
```

**OLD Valheim** (RandyKnapp reference assembly, in `sbpr-corpus`): `UpdateMouseCapture` WAS the
per-frame cursor manager — it actively locked the cursor during gameplay:

```csharp
// OLD GameCamera.UpdateMouseCapture (decompiled):
private void UpdateMouseCapture() {
    if (Input.GetKey(LeftAlt) && Input.GetKeyDown(...)) m_mouseCapture = !m_mouseCapture;
    if (m_mouseCapture && !InventoryGui.IsVisible() && !TextInput.IsVisible() && !Menu.IsVisible()
        && !Minimap.IsOpen() && !StoreGui.IsVisible() && !Hud.IsPieceSelectionVisible() && ...) {
        Cursor.lockState = CursorLockMode.Locked;   // ← the gameplay lock used to live HERE
        Cursor.visible = false;
    } else {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = ZInput.IsMouseActive();
    }
}
```

In that era a postfix forcing `None`/`visible=true` after this body was the **correct and
sufficient** seam: the only writer ran, then we overrode it. That is the world `MouseCapturePatch`
was authored for.

**CURRENT Valheim** (Daniel's build, IL-verified): the same method is **empty**:

```
// ilspycmd --ilcode -t GameCamera assembly_valheim.dll
.method public hidebysig instance void UpdateMouseCapture () cil managed {
    // Code size: 1 (0x1)
    IL_0000: ret            // ← empty body. Still CALLED every LateUpdate (GameCamera.LateUpdate:161),
}                           //   so our postfix still fires — but it overrides NOTHING.
```

A whole-assembly scan for the runtime cursor writers (`set_lockState` across
`assembly_valheim.dll` + `assembly_utils.dll` + `assembly_guiutils.dll`) finds the gameplay lock is
**no longer in any managed method**:

| `Cursor.lockState =` write site | Class | Runs during gameplay? |
|---|---|---|
| `Menu.UpdateCursor` | `Menu` (pause menu) | No — only while the pause menu is up |
| `FejdStartup.UpdateCursor` | `FejdStartup` (start screen) | No — main-menu only |
| `TestSceneCharacter` Mouse1 toggle | test scene | No — not shipped gameplay |

The build also now ships `Unity.InputSystem.dll` + `Unity.InputSystem.ForUI.dll`. The cursor
capture/lock during play migrated into the new Input System plumbing (below the managed-Valheim
layer we patch), and `GameCamera.m_mouseCapture` is now a vestigial field (set true in `Awake`,
never read for locking). **Net effect:** our postfix sets `lockState=None` on a dead method; the
Input-System layer re-asserts the lock the same frame → the cursor never actually frees. Exactly
Daniel's report.

**Why the "[Left-click] Remove pin" prompt is visible but unusable (the apparent paradox,
resolved).** The prompt renders only when `_req.Mode == TableEdit` and the modal is active
(`MapSurface.cs:747`). Daniel sees it → the viewer **is** open → `IsViewerOpen`/`AnyOpen` **are**
true → the `TakeInput` gates **are** firing. So the camera-freeze half works; it's specifically the
cursor unlock that's dead. The click handler (`MapSurface.cs:777`) is reachable in principle, but
with the cursor still center-locked there is no free pointer to aim it.

#### 2L.3 What ALREADY works (do not "fix" these — verified live)

Two of the three things the card lumps together as missing are present and correct in the current
assembly. Touching them would be churn:

- **AT-TABLE-NO-LOOK (camera doesn't turn) — WORKS.** `PlayerController.LateUpdate:235` still gates
  mouse-look: `if (!TakeInput(look:true) || InInventoryEtc()) { m_character.SetMouseLook(Vector2.zero); return; }`.
  Our `PlayerControllerTakeInputPatch` forces `TakeInput→false` while `AnyOpen`, so the camera is
  already frozen at the table. (`PlayerController.TakeInput(bool)` exists at decomp line 198 — the
  seam is live, not stale.)
- **Character-input block (no weapon swing / build / move) — WORKS.** `Player.TakeInput()` (the
  parameterless override, decomp line 2469) is forced false by our `TakeInputPatch` while
  `AnyOpen`. Left-click can't swing a weapon because character input is gated. (The card's
  AT-TABLE-NO-LOOK "left-click does not swing a weapon" clause is therefore already satisfied —
  it's the *aiming* that's broken, via the cursor, not the swing-suppression.)

The ONLY broken seam is the cursor unlock. This narrows the fix dramatically versus the card's
"build the whole mechanism" framing.

#### 2L.4 🔒 LOCKED ROUTE — re-seat cursor release on a LIVE seam, set imperatively, with a restore

The fix is to stop depending on a vanilla method that no longer manages the cursor, and instead own
the cursor state ourselves across the modal session. Two coupled changes:

**(a) Drive the cursor IMPERATIVELY on the open/close edges, not by postfixing a (dead) per-frame
vanilla method.** When any SBPR modal opens, set `Cursor.lockState = None; Cursor.visible = true`.
When the last one closes, restore the cursor to gameplay (`Cursor.lockState = Locked;
Cursor.visible = false`). Because the new Input System re-asserts the lock, a one-shot set on the
open edge is not enough on its own — so **also keep a per-frame re-assert while `AnyOpen`**, but
hang it off a seam that is *guaranteed to run* and is NOT the emptied `UpdateMouseCapture`. The
robust anchor is a tiny driver the mod already controls:

- **Anchor = a per-frame tick the mod owns** (the `MapViewer` MonoBehaviour already has an
  `Update()` at `MapViewer.cs:104`; the sign panels each have their own `Update`). Add a single
  shared call — `ModalUiSession.PumpCursor()` — that, while `AnyOpen`, re-asserts
  `lockState=None`/`visible=true` every frame in **LateUpdate ordering after** the Input System has
  run. A dedicated `MonoBehaviour` with a `LateUpdate` (one global instance, created in
  `Plugin.Awake` alongside the viewer host) is the cleanest guaranteed anchor and removes the
  dependency on any specific vanilla method body. **Engineer chooses** between (i) a dedicated
  `LateUpdate` pump and (ii) re-pointing the existing Harmony postfix at a vanilla method that is
  *non-empty and runs every frame in LateUpdate* (e.g. postfix `GameCamera.LateUpdate` itself,
  which is live — see §2L.6 seam options). The acceptance is behavioral (AT-TABLE-CURSOR-FREE), not
  which anchor.

**(b) RESTORE on close (the missing half — even the old code half-relied on vanilla to re-lock).**
Today nothing restores `lockState=Locked` when the modal closes; the old design got away with it
because vanilla's `UpdateMouseCapture` re-locked the next frame. Now that vanilla's gameplay
re-lock is in the Input System and our pump stops the moment `AnyOpen` goes false, the cursor would
be left in whatever state the Input System chooses — observably fine in most cases, but **the spec
requires an explicit restore** so AT-TABLE-RESTORE is deterministic and not luck. On the
`AnyOpen: true→false` edge, set `Cursor.lockState = Locked; Cursor.visible = false` once.

**Do NOT** try to make the cursor free by reporting through `Minimap.IsOpen()` or any vanilla
predicate — same wide-blast-radius reason §2F.2 rejected it for the menu gate (≈10 vanilla gates
read `Minimap.IsOpen`). Own the cursor directly.

#### 2L.5 Extraction shape — `ModalUiSession` (the card's "shared modal uGUI guard," done right)

The card asks for extraction, not copy-paste, so the cursor-free + input-block stop drifting per
surface. Correct — and the current `SignPanelInputBlock` is *already* the de-facto shared guard
(its `AnyOpen` covers all three SBPR modals). The extraction is therefore **a focused promotion,
not a rewrite**:

- **Rename the concept to its real scope.** `SignPanelInputBlock` is misnamed now that it gates the
  map viewer too. Promote it (or wrap it) as **`Features/Common/ModalUiSession`** — a single static
  guard exposing:
  - `static bool AnyOpen` — the existing OR of the three live `IsOpen` probes (keep the
    un-latchable `_root.activeSelf`/`IsViewerOpen` discipline from `SignPanelInputBlock.cs:42-58`;
    **the disc must NOT contribute** — it stays `IsMinimapBound`, a passive HUD element that must
    not free the cursor).
  - the existing `Player.TakeInput` + `PlayerController.TakeInput` postfixes (UNCHANGED — they
    work),
  - the `Menu.Show` suppress prefix (UNCHANGED — §2F),
  - **replacing** the dead `MouseCapturePatch` with the live cursor pump + open/close restore from
    §2L.4.
  - One edge-detector (`_wasOpen`) so the restore fires exactly once on close.
- **Keep it one file, one `AnyOpen`, one registration block.** Adding a fourth contributor later
  still just ORs another `IsOpen`. This is the "modal uGUI session guard: open → free cursor + block
  input; close → restore" the card describes, realized by editing the file that already is that
  guard rather than spawning a parallel helper the viewer would have to also call.
- **Naming/relocation is the engineer's call** — renaming `SignPanelInputBlock` touches
  `Plugin.cs` registration lines and is pure churn risk; an acceptable lighter option is to **leave
  the type name** and only (i) swap the cursor patch for the live pump+restore and (ii) add a
  class-doc note that it is the shared modal guard, not sign-specific. The behavioral ATs don't care
  about the name. **Lock: one shared guard, cursor seam re-seated on a live anchor, explicit restore
  on close. Name/location at engineer discretion.**

#### 2L.6 Seam options for the live cursor pump (engineer picks one; all verified live)

The implementer must NOT re-use `GameCamera.UpdateMouseCapture` (empty, IL-confirmed). Verified-live
alternatives, in preference order:

1. **Dedicated `LateUpdate` pump (recommended).** A one-instance `MonoBehaviour` created in
   `Plugin.Awake`. Its `LateUpdate` runs after `PlayerController.LateUpdate`/`GameCamera.LateUpdate`
   in the same frame; while `AnyOpen` it sets `lockState=None`/`visible=true`. Zero Harmony, no
   dependency on any vanilla method body — immune to the next time vanilla reshuffles input code.
2. **Postfix `GameCamera.LateUpdate`** (non-empty, runs every frame, calls the empty
   `UpdateMouseCapture` itself at `:161`). Re-point the existing patch up one level. Lower-risk than
   option 1 in terms of lifecycle (no new GameObject) but couples us to `GameCamera.LateUpdate`
   staying the camera's per-frame entry.
3. **Postfix `PlayerController.LateUpdate`** — also live every frame. Equivalent to (2); pick
   whichever the engineer finds least surprising.

All three are vanilla base-game seams (ADR-0001 clean-side: reading/patching base game is fair
game; verified against `assembly_valheim.dll` metadata — no third-party mod code). The choice is an
implementation detail; **AT-TABLE-CURSOR-FREE + AT-TABLE-RESTORE are the contract.**

#### 2L.7 Scope — TableEdit vs FieldReadOnly (resolves the card's open question)

The card asks whether the FieldReadOnly (carry/equipped) full view also needs the cursor free.
**Answer: it falls out for free and should NOT be specially gated.** `AnyOpen` keys on
`IsViewerOpen` = the modal being active, regardless of mode — so the same cursor pump frees the
cursor for BOTH the TableEdit modal and the FieldReadOnly modal. That is correct and desirable:
even the read-only field map is a full-screen modal you Escape out of, and a free cursor there is
consistent (and harmless — there are no clickable pins to remove, so a free cursor simply does
nothing extra). The passive **minimap disc** is the one surface that must stay cursor-locked — and
it already is, because it contributes via `IsMinimapBound`, not `IsViewerOpen`/`AnyOpen`. No
mode-specific cursor branching is needed; **one pump, gated on `AnyOpen`, covers it.**

#### 2L.8 The sibling sign panels have the IDENTICAL dead seam (fix once, fix all three)

Because the cursor release is the SHARED `MouseCapturePatch` keyed on the shared `AnyOpen`, the
sign panels (`SignPaintPanel`, `MarkerSignPanel`) have the **same** broken cursor-free on this
build — their cursor-free playtest box was never confirmed (`docs/v0.1.0/v0.1.0-PLAYTEST.md:49` is
still unchecked). Re-seating the cursor seam in the shared guard fixes the cursor for all three SBPR
modals in one stroke — exactly the AT-VIEWEXIT-5 pattern §2F used for the Escape leak. This is a
**fix-all-three**, not a make-the-viewer-match-the-panels (the panels are not a working reference
here — they share the regression).

#### 2L.9 Files touched + clean/dirty

- **Clean-side → `engineer-ui`** (owns `SignPanelInputBlock`/the shared modal guard + the sign
  panels + `MapViewer`). All seams are base-game (`GameCamera`/`PlayerController` LateUpdate,
  `Cursor`) — ADR-0001 fair game, verified against `assembly_valheim.dll` metadata. No third-party
  mod code, no `SurveyData`/wire change, no recipe/SpecCheck/manifest impact.
- **Lands in:** `Features/Signs/SignPanelInputBlock.cs` (replace `MouseCapturePatch` with the live
  cursor pump + open/close restore; keep `AnyOpen`, the two `TakeInput` patches, and the
  `Menu.Show` prefix unchanged), `Plugin.cs` (registration delta only if the pump is a new
  MonoBehaviour/patch container — register it exactly as the existing four containers are, or
  PatchCheck will ERROR at boot per the t_564f695a unregistered-patch lesson), and optionally a new
  `Features/Common/ModalUiSession.cs` if the engineer promotes the type.
- **Shares `SignPanelInputBlock.cs` with §2F** (the `Menu.Show` exit-leak fix). If both land
  concurrently they edit the same file — **same-worker or sequence** them (assign both to
  `engineer-ui`), same discipline §2F.5 notes for `MapViewer.cs`.
- **Build:** `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` → 0 errors,
  **0 warnings** (`<TreatWarningsAsErrors>` is on).

#### 2L.10 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-TABLE-CURSOR-FREE** — Opening the Surveyor's Table map (TableEdit) shows a free, visible
  cursor that moves independently of the camera. (The fix: cursor pump on a LIVE seam, not the empty
  `UpdateMouseCapture`.)
- **AT-TABLE-NO-LOOK** — While the table map is open, moving the mouse does NOT turn the camera and
  left-click does NOT swing/use a weapon. *(Already passing on the current build via the live
  `TakeInput` gates — this AT is a regression guard, confirming the cursor fix didn't disturb the
  working camera/character block.)*
- **AT-TABLE-PIN-REMOVE** — With the cursor now free, hovering a pin and left-clicking removes it
  (the existing `TryRemovePinAtCursor` path at `MapSurface.cs:777` is finally reachable with a
  real pointer).
- **AT-TABLE-RESTORE** — Closing the map (Esc) restores normal cursor lock + camera look + player
  input, with **no** stuck cursor and no stuck input-block. (The explicit `AnyOpen:true→false`
  restore, §2L.4b — deterministic, not relying on vanilla to re-lock.)
- **AT-TABLE-FIELD-CURSOR** — The FieldReadOnly (equipped Local Map) full view ALSO shows a free
  cursor while open and restores on close (same `AnyOpen` pump; §2L.7). The passive minimap **disc**
  does NOT free the cursor (it never trips `AnyOpen`).
- **AT-SIGN-CURSOR-REGRESSION** — Re-verify the Painted Sign + Marker Sign panels: cursor is free
  while open and restored on close (they share the re-seated seam; this finally confirms
  `v0.1.0-PLAYTEST.md:49`, which the dead seam left unverified).
- **AT-CURSOR-PATCHCHECK** (registration) — `Runtime/PatchCheck.cs` reports no UNREGISTERED PATCH
  CLASS at boot; if the pump is a new patch/Mono container it is actually woven in `Plugin.Awake`.
- **logs-green ≠ playable** — Daniel's joined-client check is the real accept: at a real Table, the
  mouse moves freely, hovers a pin, left-clicks, the pin is removed, Esc closes cleanly and
  mouse-look returns.

#### 2L.11 Spec hygiene — corrects §2F.1's cursor claim

§2F.1 (2026-06-11) asserts the viewer "is NOT missing… cursor release — those work." That was
correct *reasoning* about the wiring (the patch IS registered and IS keyed on the viewer) but an
**unverified claim about the vanilla seam**, which had already been emptied by the time Daniel
tested. This section supersedes that clause: the wiring is present, but the cursor seam is dead on
the shipped build. The camera-freeze and character-input halves of §2F.1's claim stand (verified
live in §2L.3); only the cursor-release half is corrected. No other part of §2F (the Escape→menu
suppression, the exit prompt) is affected — those target different, live seams.

### 2G — Local Map open input (issue 7 design correction, 2026-06-11)

> **🔴 SUPERSEDED (2026-06-17, issue 3, card t_f9a04fda) — the open input is now M, not E.**
> The entire §2G Use-key (E) open model below is REPLACED by the 🟢 DECIDED M-key model
> (`map-provider-model.md` §1, Daniel 2026-06-15): **M opens the bound local map; the E-to-open
> path is removed entirely; the prompt reads "[M] …".** The buildable HOW — including how SBPR
> owns the M edge in nomap-OFF without stacking vanilla's map (a `Minimap.Update` consume-prefix)
> — is **[`local-map-mkey-open-impl-spec.md`](local-map-mkey-open-impl-spec.md)**. §2G is kept
> below as history (the reasoning for why the gesture was *temporarily* on E); do not build from
> it. Daniel's v0.2.26-dev playtest found the impl still on E — that's the drift this supersede
> closes.

> **✅ IMPL STATUS (2026-06-11, t_23b950ee → branch `feat/local-map-viewer-overhaul-t_23b950ee`).**
> The §2G LOCKED open input is BUILT in `LocalMapController.cs`: the `GetButtonDown("Map")` open
> edge is replaced by `GetButtonDown("Use") || GetButtonDown("JoyUse")`, gated through
> `CanOpenOnUse(player)` — opens ONLY on an idle Use press (`player.GetHoverObject() == null`, so a
> hovered Table/door/chest interaction wins — AT-LMAP-TABLE-COEXIST) and is suppressed while any
> modal is up (`TextInput.IsVisible()` / `InventoryGui.IsVisible()` / `SignPanelInputBlock.AnyOpen`).
> The toggle-CLOSE path is intentionally ungated so the open field viewer is always dismissible
> with Use. The equipped HUD prompt (`UpdateEquippedPrompt` → a client-only bottom-centre overlay)
> shows `[<$KEY_Use>] $piece_readmap` (vanilla tokens, localized — rebind-correct, mirrors the
> MapTable :114046 idiom) only while equipped + the field viewer is closed (mutually exclusive with
> §2F's "[Esc] Close map"). The inverted-premise comments in `LocalMapController.cs` + the
> `MapViewer.cs` Escape comment are corrected. The "just a hoe" LMB/RMB click-suppression is
> untouched (intended — AT-LMAP-OPEN-5). No `Minimap`-clamp nerf smuggled in (§2G.3 deferred —
> Daniel's separate call). Build 0/0. **NOT YET PLAYTESTED — Daniel confirms in-game: Use opens, no
> double-map, hover-interaction wins, prompt visible.** Vanilla APIs verified vs the decomp
> (`ZInput.GetButtonDown("Use")` :16116, `Player.GetHoverObject()` :14699, `$piece_readmap` :114046).

> **⚠️ FOLLOW-UP (2026-06-12, issue 6, §2I).** Daniel's v0.2.22 playtest found this §2G open
> gesture **intermittently dies** after imprinting at the Table or pinning a marker sign (recovers
> after re-using the Table). Root cause: the open-suppression gate (`CanOpenOnUse` →
> `SignPanelInputBlock.AnyOpen`) can latch on a stale `CartographyViewer.IsViewerOpen` because the
> viewer tracked open-state in a side bool that desyncs from its canvas. Fix + the imprint-trigger
> redesign (look-at-table + hotbar#) are specced in **§2I** — read it alongside this section.

> **Status: DESIGN CORRECTION.** Supersedes the "the vanilla **'Map' button** activates the
> full view (dead under nomap, so the fork repurposes it)" open path asserted in the §2 IMPL
> STATUS banner, §2A.4, §2B, and the code comments at `LocalMapController.cs:79-86` /
> `MapViewer.cs:391-394` / `LocalMapController.cs:14-16`. The fork SHELL, binding state machine,
> imprint, torch exception, and render (§2E) are all UNCHANGED and correct — **only the OPEN
> TRIGGER changes.** Reported by Daniel, v0.2.19-playtest, in game. Clean-side (ADR-0001).

**What Daniel reported (verbatim):** *"local map doesn't seem usable with left or right click.
Just appears to be a hoe :P. M pulls up the local map on top of the global map which is weird."*

**The decisive RE finding — the card's premise was inverted.** The whole "Map button is dead
under nomap, repurpose it" design rests on a false reading of vanilla. Verified against the
decomp (`assembly_valheim`, `Minimap.cs`):

- `Minimap.SetMapMode(MapMode mode)` (`:961-999`) clamps `mode = MapMode.None` **only when
  `Game.m_noMap == true`** (`:963-966`), which sets BOTH `m_largeRoot.SetActive(false)` AND
  `m_smallRoot.SetActive(false)` (`:976-977`). So `Game.m_noMap` does not "kill only the M
  map" — **it kills the minimap circle too.**
- `Minimap.Update` (`:604`) fires the `ZInput.GetButtonDown("Map")` → `SetMapMode` toggle
  inside the modal gate at `:593` (`!Chat.HasFocus && !Console.IsVisible && !TextInput.IsVisible
  && !Menu.IsActive && !InventoryGui.IsVisible`).
- **Therefore the "Map" button is dead *only* when `Game.m_noMap == true* — and in that world
  there is no minimap either.** Daniel sees a minimap circle AND a global map opening on M.
  That combination is **only possible when `Game.m_noMap == false` (`nomap=OFF`)**, where
  vanilla's M-key Large map is fully alive.

**Two coupled defects fall out of that one inverted premise:**

1. **Open-trigger collision (the "M opens local-map-on-top-of-global-map" defect).**
   `LocalMapController.cs:86` opens our viewer on `ZInput.GetButtonDown("Map")`. Under
   `nomap=OFF` that SAME press also drives vanilla's `Minimap.Update` Small→Large toggle →
   both surfaces appear. The fork was wired for a world (`nomap=ON`) the playtest isn't using.

2. **The v1 "no M-key full map" baseline was specced but never built.** The locked v1 baseline
   (`PARKED-2026-06-03.md:20`, `requirements.md:10`, `design/cartography-v2.md:21-26`) is:
   `nomap=ON` → no map at all; `nomap=OFF` (default) → **minimap only, no M-key full map**. But
   "minimap-only-without-the-M-map" is **not a vanilla state** — vanilla gives you both or
   neither (per the decomp above). Delivering it requires an SBPR patch that clamps vanilla's
   Large map. **No such patch exists in `src/`** (audited: the only `Minimap` prefixes are the
   Cartographer's Kit `UpdateExplore` gate and the equip patch; the rest are reconcile
   postfixes). So under `nomap=OFF` vanilla's full M-map leaks — exactly what Daniel sees.

**The "just a hoe" half is INTENDED and stays.** The click-suppression (`LocalMap.cs:112-121`
empties `m_attack`/`m_secondaryAttack` → `HavePrimaryAttack`/`HaveSecondaryAttack` false) is
**correct** (AT-MAP-BLOCKCLEAR). The item is not supposed to attack. The fix is to give it a
working, discoverable OPEN action — not to restore clicks.

#### 2G.1 LOCKED open input — the Use key (E) while equipped

> **⚠️ OPEN INPUT SUPERSEDED (2026-06-17, issue 3 → `local-map-mkey-open-impl-spec.md`, card
> t_f9a04fda).** The "Use key (E)" lock below is HISTORY. The open gesture moved to **M** — SBPR
> now owns the M (Map) input edge via a consume-prefix on `Minimap.Update` (no double-stack in
> nomap=OFF), the E-to-open path was removed, and the equipped prompt reads "[M] …". Route (b)
> ("gate vanilla's M") — rejected here — was in fact the realized design, done cleanly via the
> consume-prefix idiom rather than a `SetMapMode` skip. The §2F menu-suppression / Esc-close work
> below STANDS. Read this section as the rationale that was superseded, not current behavior.

**Route (a) from the card is locked. Routes (b) "gate vanilla's M" and (c) "a new bind" are
rejected** (rationale below).

- **Open gesture:** while a Local Map is **equipped** (two hands), pressing the **Use key**
  (`ZInput.GetButtonDown("Use")` / `"JoyUse"` — the same input vanilla routes to
  `Player.Interact`, decomp `Player.cs:806`) opens the bounded viewer in `FieldReadOnly` mode.
  This is the SAME open gesture the Surveyor's Table uses (`Switch.Interact`/Use → open viewer,
  §1.4) — one consistent "Use to read the map" model across both surfaces.
- **Collision-free by construction:** the Use key is NOT the "Map" button, so pressing it never
  drives vanilla's `Minimap.Update` map toggle. Vanilla's M continues to do whatever it does in
  the world's nomap config, independently; our viewer never rides it. (AT-LMAP-OPEN-2/3.)
- **Toggle + close:** while the viewer is open, **Use** (or the §2-exit card t_e2cc8183's
  Escape handling) closes it. The controller already self-closes on unequip and when the map
  leaves inventory — keep those. Replace the `GetButtonDown("Map")` edge at
  `LocalMapController.cs:86` with a `GetButtonDown("Use")` edge under the same
  `_mapEquipped && _equippedMap != null && !tableViewOwnsViewer` guard.

**Use-key interaction discipline (the one real hazard — must be handled):**

- **Do not let one Use press both open the viewer AND interact with a hovered world object.**
  Vanilla `Player.Update` (`:806`) sends Use → `Interact(m_hovering, …)` when something is
  hovered. The Local-Map open path must only fire when the press is NOT being consumed by a
  world interaction — i.e. gate our open on **`Player.m_localPlayer.GetHoverObject() == null`**
  (public accessor, decomp `Player.cs:4055`) so standing in front of a door/Table/chest and
  pressing E still interacts with it, and only opens the map on an otherwise-idle Use press.
  This mirrors how a Local Map at a Surveyor's Table should still **Use the Table** (survey +
  imprint), not pop the field viewer — the Table is the hovered object, so our open suppresses
  and the Table's `Interact` wins. (AT-LMAP-OPEN-3, AT-LMAP-TABLE-COEXIST.)
- **Suppress while a modal SBPR UI / text input is up.** Reuse the existing
  `SignPanelInputBlock.AnyOpen` check (it already covers `CartographyViewer.IsViewerOpen`) plus
  the vanilla `TextInput.IsVisible()` / `InventoryGui.IsVisible()` guards, so opening text
  fields or the inventory doesn't trip a map-open. (The controller's `Update` runs every frame;
  keep its existing graphics-client guard.)
- **Implementer's-choice mechanism, equivalent outcomes:** either (i) keep reading `ZInput`
  directly in `LocalMapController.Update` with the hover-null guard above (smallest change,
  matches the current controller shape), or (ii) route through a `Humanoid.UseItem` /
  Interact-side hook on the equipped item. (i) is recommended — it's the minimal delta to the
  existing polled controller and avoids a new Harmony surface. Whichever is chosen, the
  hover-null + modal-suppress discipline is mandatory.

**Why not (b) "keep Map, gate vanilla's M":** it requires a reliable Harmony clamp on vanilla's
map-open under `nomap=OFF` — which is the very "no M-key full map" nerf that was specced but
never built (defect 2). That's a **separate, larger design decision** (see §2G.3) about whether
v1's intended nerf ships at all; do NOT smuggle it in through the Local Map's open path. Moving
off "Map" entirely makes the Local Map correct **regardless of how that nerf question lands.**

**Why not (c) "a new dedicated bind":** a brand-new keybind is undiscoverable without a rebind
UI and duplicates the "Use to read" affordance the Table already establishes. The Use key is
the consistent, already-bound, prompt-backed gesture.

#### 2G.2 The equipped prompt (so it doesn't read as an inert hoe)

- While a Local Map is **equipped**, show an on-screen hint: **`[<$KEY_Use>] $piece_readmap`**
  (or plain "Open map") rendered through `Localization.instance.Localize` so the bound-key
  token resolves to the player's actual key (e.g. "E") — the **same rebind-correct pattern**
  `SurveyorTableTag.GetHoverText` / `CairnInteractable` use (`$KEY_Use` localizes; a CUSTOM
  `$piece_*` token would leak as a literal — the 2026-06-05 sign-bug lesson). `$piece_readmap`
  is a **vanilla** token (decomp `MapTable.cs:34`), so it localizes; if a fuller string is
  wanted, keep the `$KEY_Use` token and put the rest in plain English.
- **Placement:** a HUD hint while the map is equipped (not a hover-text — the map is a held
  item, not a hovered piece). Bottom-center is consistent with the viewer's own exit prompt
  (t_e2cc8183 adds "[Esc] Close map" while the viewer is open); the equipped-prompt and the
  open-viewer-prompt are mutually exclusive (one says how to open, the other how to close).
  Implementer picks the exact HUD surface; the bound-key token + equipped-only visibility are
  the locked requirements. (AT-LMAP-OPEN-4.)
- **Coordinate with t_7816c0b0 / t_e2cc8183** on token wording so all three cartography prompts
  read consistently.

#### 2G.3 The deferred question — does v1's "no M-key full map" nerf actually ship? (NOT this card)

This card makes the Local Map correct on `nomap=OFF` **without** depending on the nerf. But the
playtest exposed that **vanilla's full M-map is currently reachable**, which contradicts the v1
baseline's "no M-key full map." Two coherent end-states, and **Daniel must call it** (it is a
gameplay-pillar decision, not an implementation detail):

- **(I) Ship the nerf:** add the missing SBPR patch that clamps vanilla `Minimap.SetMapMode`
  Large→Small (or gates the `:604` toggle) under `nomap=OFF`, so M only ever opens the minimap,
  never the full world map. This is what the locked v1 baseline says should already be true. The
  Local Map (Use-key) is unaffected either way.
- **(II) Drop/relax the nerf:** accept that under `nomap=OFF` players have vanilla's full M-map,
  and the Local Map is an *additional* bounded artifact. Re-word the v1 baseline + requirements
  to match reality.

**This card does NOT implement either** — it routes the Local Map off "Map" so it's correct in
both. If Daniel wants (I), that's a **separate card** (clean-side; a `Minimap.SetMapMode` clamp,
the same hook the WorldPin reconcile already postfixes). Flagged here, surfaced as the card's
open question, NOT silently chosen.

#### 2G.4 Files touched + clean/dirty

- **`LocalMapController.cs`** — replace the `GetButtonDown("Map")` open edge (`:86`) with a
  `GetButtonDown("Use")` edge guarded by `GetHoverObject() == null` + the modal-suppress check;
  fix the false-premise comments (`:14-16`, `:79-86`).
- **`MapViewer.cs`** — the `:391-394` comment asserting "vanilla Minimap's M/ESC handling, which
  is dead under nomap" is the same false premise; correct it. (The Escape close itself is the
  t_e2cc8183 exit card's surface — coordinate; this card only fixes the OPEN trigger + the
  comment.)
- **`LocalMap.cs`** — no code change to the item; the click-suppression stays (intended). Add
  the equipped prompt via the controller or a small HUD hook (implementer's choice per §2G.2).
- **Clean-side (ADR-0001):** reading `ZInput`, `Player.GetHoverObject()`, vanilla `$KEY_Use` /
  `$piece_readmap` tokens, and the vanilla `Minimap` decomp is all base-game. No third-party mod
  code. No SpecCheck impact (input/UI behavior, not a recipe row).

#### 2G.5 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-LMAP-OPEN-1** — with the Local Map equipped, pressing **Use (E)** on an otherwise-idle
  press opens its bounded viewer. It WORKS and is discoverable.
- **AT-LMAP-OPEN-2** — opening the Local Map viewer does NOT also open vanilla's map; no
  double-map stacking on any single input.
- **AT-LMAP-OPEN-3** — pressing **M** (vanilla's map, in whatever state the world's nomap config
  leaves it) does NOT open our viewer; pressing **Use** while hovering a world object interacts
  with that object (door/chest/Table), NOT the map. The two input paths are non-colliding.
- **AT-LMAP-OPEN-4** — an on-screen prompt (bound-key token, e.g. "[E] Open map") is visible
  while the Local Map is equipped and not-yet-open, so it never reads as an inert hoe.
- **AT-LMAP-OPEN-5** (intended behavior preserved) — LMB/RMB still do no attack/block
  (AT-MAP-BLOCKCLEAR holds; click-suppression is correct and unchanged).
- **AT-LMAP-OPEN-6** — closing the viewer is clean (pairs with t_e2cc8183: Escape closes without
  leaking the game menu; Use also toggles it shut). Same `MapViewer` engine.
- **AT-LMAP-TABLE-COEXIST** — standing at a Surveyor's Table with a Local Map equipped and
  pressing Use **surveys + imprints at the Table** (Table is the hovered object), and does NOT
  pop the field viewer over the Table view.
- logs-green ≠ playable — Daniel confirms in-game: equipped map opens on Use, no global-map
  overlap, prompt visible.

**Shared-file coordination (viewer-UX cluster).** This card (open), **t_e2cc8183** (Table-viewer
Escape exit + prompt), and **t_c90f4d8c**/§2E (vanilla-cartography render) all touch
`MapViewer.cs` + the viewer input model. They are one coherent input/UX problem. **Routing
recommendation:** sequence them onto ONE engineer-ui worker (or strictly serialize the PRs) so
they don't conflict on `MapViewer.cs`. Suggested order: §2E render (largest, already specced via
PR #107) → t_e2cc8183 exit → this open-input card, OR fold all three into a single viewer-UX
implementation card. The architect (this card) flags the coupling; the merge sequencing is
Daniel's call at review.

**Implementation card:** to be routed to `engineer-ui` (owns `MapViewer.cs` +
`LocalMapController.cs`), as a child of this card on approve. **SpecCheck impact: none.** Spec +
code move together in that PR.

---

### 2H — Free-rotate the held Local Map (issue 8 design correction, 2026-06-11)

> **🔴 SUPERSEDED (2026-06-12, issues #2/#3/#4/#9 → §2H.1, card t_05e702ee).** The
> **player-centred minimap** model locked in this section (P2: view recentres on the player each
> frame, marker pinned at dead centre, off-disc arrow points at the off-screen table) SHIPPED in
> v0.2.22 and is what Daniel rejected across four playtest bugs at once: #4 (the map pans to
> follow the player — that pan IS the player-centring offset), #3 (the blue square renders
> outside the disc — the centred marker never hides), #2 (the whole box/frame rotates, not just
> the interior), and #9 (it's a rotating *square*, not the discussed fixed-bezel *circular*
> minimap). Daniel re-locked the orientation model on 2026-06-12 (verbatim quotes in §2H.1).
> **Read §2H.1 before touching the held-map orientation — it supersedes the player-centred
> LOCKED ROUTE in the rest of §2H (kept below for history).** The §2H *rotation* intent
> (free-rotate to heading, no north indicator) and the §2H/§2E coordination (one `engineer-ui`
> worker owns the whole transform model) both STAND; only the *centring* model inverts.

#### 2H.1 — Orientation-model re-lock: fixed-window, TABLE-centred, circular (issues #2/#3/#4/#9, 2026-06-12, card t_05e702ee)

> **🟢 VIEWER NOW INSTANCED AT TWO SCALES (2026-06-16, map-provider-binding-impl-spec, card
> t_7dd54899).** This §2H.1 circular viewer is now factored into a shared surface (`MapSurface`)
> built ONCE and instanced twice: (a) the **full-screen MODAL** full view — table-centred,
> backdrop + Esc/title prompts, the playtested §2H.1 behaviour **unchanged**; and (b) a small,
> corner-anchored, passive **carry minimap DISC** that reuses the SAME layer tree + #159 hard
> alpha-clip bezel (bezel inset/ring now parameterized by **fraction-of-target-edge**, so the
> ~200 px disc scales the clip down instead of inheriting 900 px-absolute insets). One renderer,
> two configs — never a parallel circular implementation.
>
> **🔴 R1 — the DISC centring DIVERGES from §2H.1's table-centred lock (Daniel, 2026-06-16, card
> t_1d1b505b thread).** §2H.1 mechanic 1 (table-centred, no pan) and mechanic 2 (off-disc
> edge-arrow) remain the lock **for the full-view MODAL**. The **disc** does NOT inherit them:
> Daniel resolved R1 as **player-centred camera + table-anchored shroud**. Concretely on the disc:
> the player marker is pinned **dead-centre** and the survey **scrolls under** it (player-centred —
> reads like a real minimap); BUT the revealed area stays **anchored to the imprinted 1000 m bound**
> (table-anchored shroud), so walking toward the survey edge makes shroud **creep in** from that
> side and leaving the disc entirely goes **all shroud**; and the **edge-arrow is REMOVED** for the
> disc (a player-centred camera can't fall off the window, so §2H.1 mechanic 2 has no meaning
> there). This is NOT a return to the rejected v0.2.22 player-centred model — that one let the
> *shroud* follow the player (infinite fog); here the shroud stays a finite, earned, table-anchored
> survey. Implemented as: player-centred shader reframe (`_mapCenter` = player) + a player-centred
> shroud mask resampled against the table-anchored survey + dead-centre marker, all inside the
> circular bezel. The modal's §2H.1 path below is byte-unchanged.

> **Status: BUG/DESIGN — ORIENTATION-MODEL RE-LOCK.** Supersedes §2H's "P2 player-centred
> minimap" LOCKED ROUTE. Resolves four v0.2.22-playtest bugs that are **one model**, not four
> fixes: #2 (frame rotates), #3 (player square renders outside the disc), #4 (map pans to follow
> the player), #9 (square, not circular). Reported by Daniel 2026-06-12; the model below is
> Daniel-locked via two coordination comments on card `t_05e702ee` (2026-06-12). Clean-side
> (ADR-0001): reading + adapting vanilla camera/heading + our own uGUI transforms is base-game.
> **SpecCheck impact: none** (transform/presentation behaviour, not a recipe row). Spec + code
> move together in the implementation PR.

**What Daniel locked (verbatim, card `t_05e702ee` comments, 2026-06-12):**
- *"M1 B but always centered on the table not the player."* — the held Local Map is **M1**
  (player marker moves within a fixed window) with **B** (rotates to heading), **centred on the
  table's survey origin, not the player.**
- *"the player is supposed to be disoriented by lack of an understanding of North. The whole
  purpose of the compass in the swamp is to assist with reading the map."* — the absence of a
  North reference is **intended difficulty**, not a bug to hedge. **No north-up mode, no north
  arrow, no compass rose, no orienting aid of any kind** on the local map. A future swamp-tier
  compass item is the designed tool that earns the player that help.

The held Local Map (**FieldReadOnly** view) is a **FIXED-WINDOW, TABLE-CENTRED, CIRCULAR,
rotate-to-heading minimap.** Point by point:

1. **TABLE-centred, fixed window — NO pan (resolves #4).** The 1000 m window is **static**: it is
   centred on the bound Table's survey origin (`BoundOrigin`) and does **not** slide to follow
   the player. This is already the default projection — §2B/§2E put `BoundOrigin`'s cell at rect
   centre. The bug is the deliberate player-centring offset `ApplyFieldOrientation` adds in field
   mode: **`_mapRect.anchoredPosition = -playerAnchor`** (current `MapViewer.cs:685-690`). **Delete
   that offset** (set `Vector2.zero`, same as `TableEdit`); the view then reads like a paper map
   nailed to the table. AT-MAP-BOUND (what is *revealed*) is unchanged — only what is *centred*
   changes.

2. **Player marker = TRUE table-relative position; hidden + edge-arrow when outside the disc
   (resolves #3).** The blue marker sits at the player's real offset from the table and travels
   as the player walks (M1). When the player leaves the 1000 m radius, the in-disc dot is
   **hidden** and an **edge arrow** is shown instead, clamped to the disc edge and pointing
   **outward toward the player's real bearing** ("you are that way, past the shroud").
   **This is exactly the existing `UpdatePlayerMarkerTableCentred` behaviour** (`MapViewer.cs:616-659`:
   `EdgeClampToDisc` → in-disc dot, or outside → outward arrow). The #3 fix is therefore a
   **routing change, not new math**: in `UpdatePlayerMarker` (`:574-588`), route `FieldReadOnly`
   to `UpdatePlayerMarkerTableCentred`, **not** `UpdatePlayerMarkerFieldCentred` (`:596-609`,
   which pins the marker at dead centre and never hides — the defect). Retire the now-dead
   player-centred apparatus: `UpdatePlayerMarkerFieldCentred`, the `_staticOverlay` layer
   (`:118`), and `UpdateTableArrow` (`:731-779`). **`UpdateTableArrow` is incoherent under this
   model** — it points at an *off-screen* table, but the table is now always at screen centre, so
   there is nothing to point at. Daniel's #3 phrasing "table-arrow shown instead" was written
   under the superseded player-centred assumption; his re-lock comment says "edge-arrow when
   outside," which is the player-direction edge arrow specified here (= AT-MAP-EDGEARROW, already
   built).

3. **CIRCULAR form with a FIXED bezel — only the interior rotates (resolves #9 + #2).** The held
   view is a **circle**, not a square. Split the layer tree into a **non-rotating frame** and a
   **rotating interior**:
   - **Non-rotating parent** (screen-aligned, never rotates): a **circular clip mask** (the disc)
     + a **fixed circular bezel** image on top + the already-static title (§2B.1) and exit prompt
     (§2F). This is the "box" Daniel wants to stop spinning (#2).
   - **Rotating interior** (`_mapContainer`, today's rotation node): the §2E.1 cartography
     `RawImage` + the shroud-mask `RawImage` + the pin/marker overlay. **Only this rotates.**
   - **Geometric guarantee (resolves the #2 AT "no clipping artifacts at the window edge"):** the
     square cartography texture is the bounding box of the 1000 m disc, so the visible disc is the
     square's **inscribed circle** — which is invariant under rotation about centre. Rotating the
     square never uncovers the disc (the four corner triangles outside the disc are shroud-opaque
     anyway and are clipped away by the circular mask). No empty corners ever appear. Clip radius =
     disc radius in pixels = half the square's pixel side.

   > **⚠️ PRECONDITION (added 2026-06-17 → §2E.5, card t_a39d3e5f).** This guarantee holds ONLY if the
   > cartography square is **uniformly valid to its inscribed circle** — i.e. the render actually
   > fills the square with bounded cartography. The first disc playtest FALSIFIED it: the
   > shader-sampled content filled a SMALLER square than the rect (a `uvRect`-vs-shader-uniform
   > framing disagreement), so the rotating interior showed that inner square as a **diamond** with
   > black/ocean corners — "empty corners" did appear. The fix (the framing must fill the rect to its
   > corners) is **§2E.5.1 point 2**; the guarantee's *geometry* is correct, its *precondition* was
   > unmet.

   > **✅ IMPL UPDATE — issue 6 edge-bleed fix (2026-06-15, t_d44572f2, engineer-ui).** The
   > circular clip is realized by the **fixed bezel's opaque alpha cover**, not a uGUI `Mask`: the
   > rotating interior renders with the vanilla map *shader* (no stencil pass), which a `Mask`
   > cannot clip. The original bezel made the transparent disc *coincident* with the square's
   > inscribed circle ("clip radius = half the square's pixel side") — ZERO margin — and built the
   > edge as a low-res (512²) **Bilinear** step. Upscaled ~2.5× on screen, that step smeared into a
   > 2–3 px partial-alpha seam straddling the square's four straight tangents (12/6/9/3 o'clock), so
   > parchment bled past the bezel as straight-edged slivers (top + left in the playtest evidence,
   > `docs/v2/playtest-evidence/2026-06-15/issue6-map-edge-bleed.jpeg`). **Fix:** (a) inset the
   > transparent disc `BezelDiscInsetPx` (6 px) INSIDE the inscribed circle so the straight tangents
   > always sit under opaque cover with margin; (b) build the bezel at 1024² with **analytic** AA
   > (mapped to exact SCREEN px) so the alpha reaches full opacity well inside the square edge — no
   > sub-pixel seam; (c) make the bronze ring + shroud ONE contiguous opaque cover (no thin isolated
   > band a future upscale could thin). The visible disc shrinks 6 px (imperceptible); rotation,
   > shroud, bezel ring, and corner-coverage (#2/#9) are unchanged. `EnsureBezelTexture` now takes
   > the on-screen bezel edge; the dead `DiscClipFraction` const was removed. Build 0/0. **NOT YET
   > PLAYTESTED — the headless worker has no GPU; Daniel's in-game playtest of the v0.2.25 held local
   > map is the merge gate.**

4. **Rotates to heading, pivoting on the TABLE (Daniel's "B").** Keep the existing rotation of the
   interior: `_mapContainer.localRotation = Euler(0, 0, MapRotationSign * cameraYaw)`
   (`MapViewer.cs:696-700`), driven per-frame from `Update` (§2H b6, unchanged). With the #4 pan
   removed, the container pivots about its own centre = the **table** point = screen centre.
   **Documented geometric consequence (intended, NOT a bug):** because the player marker is
   off-centre (it's at the player's offset from the table), rotating the disc about the table
   makes the marker **orbit** screen-centre as the player turns in place far from the table — the
   player is not at the pivot. This is the disorientation Daniel explicitly wants (point 5).
   - **`MapRotationSign` (`:89`, currently `+1f`) stays the single build-calibration knob** for
     rotation *sense* — Daniel tunes it in-game if the map turns the wrong way (same discipline as
     §2H). **Do NOT hardcode-and-forget; do NOT expose any *other* flag.**
   - **NO north-up alternative.** STRIKE the superseded §2H "north-up + facing-arrow fallback":
     Daniel reversed it — disorientation is the design. There is no north-up mode to expose, so
     **drop that flag entirely.**

5. **NO north reference of any kind (Daniel-locked).** Do not add a north-up mode, a compass rose,
   a North arrow, a fixed-North bezel mark, or any orienting aid to the held Local Map. Reading the
   spinning table-centred disc IS the intended challenge of the no-map exploration loop until a
   future **swamp-tier compass** item ships. (Consistent with the v1 lock:
   `docs/v0.1.0/planning/requirements.md:57` / `:646` — "minimap ONLY, freely rotating, **no north
   indicator**".) The player marker stays a **featureless square/dot with no facing pip** — a
   facing indicator is itself an orientation aid and is out of scope here; the marker's *position*
   rides the rotation, and (like the pins, §2H b4) its icon counter-rotates so it never appears to
   spin (`CounterRotatePins`).

6. **Surveyor's Table (TableEdit) view ALSO rotates-to-heading — table-centred (issue #1, Daniel
   re-locked 2026-06-12, REVERSES the earlier "north-up" line).** Issue #1's candidate-A (placement
   ghost `Piece.m_canRotate`) was decomp-falsified (it defaults true); the real issue is
   candidate-B — the Table *map view* was north-locked — and Daniel wants it **CHANGED, not
   wontfix**. A north-locked table view was a **free, reliable North reference** any time the player
   stood at a table, which defeats the no-North design pillar (the swamp Iron Compass is the *earned*
   orientation tool). So the table view now rotates-to-heading exactly like the held map, closing
   the free-North hole. Concretely: `ApplyFieldOrientation` STOPS hard-resetting
   `_mapContainer.localRotation = identity` in TableEdit and applies the same `MapRotationSign *
   cameraYaw` rotation. The table is table-centred and the player stands at it (≈ centre), so the
   rotation is clean about centre (no orbit issue). **What stays table-specific:** the TableEdit view
   keeps its **fuller square extent** (no circular clip) for pin-editing visibility — a circular clip
   can hide edge pins you're trying to manage — and keeps left-click pin removal; only its
   *orientation* changes. **No north indicator on the table view either** (same no-North rule). Switch
   on the existing `MapViewerMode` flag: both modes rotate-to-heading; `FieldReadOnly` adds the
   fixed circular bezel + the marker hide/edge-arrow, `TableEdit` stays square + keeps pin editing.

**Net change vs. shipped §2H.** This is a **simplification**, not added complexity: delete the
player-centring offset (#4), delete `UpdatePlayerMarkerFieldCentred` + `_staticOverlay` +
`UpdateTableArrow` and route field mode through the existing `UpdatePlayerMarkerTableCentred`
(#3), and split a fixed circular bezel/clip parent off the rotating `_mapContainer` (#2 + #9). The
§2E.1 CPU-composite render, the shroud mask, pins (ride rotation + counter-rotate icons), the
§2F exit prompt, the §2G open input, AT-MAP-BOUND, and AT-MAP-FIXEDZOOM are all untouched.

##### 2H.1 acceptance tests (named, observable — close only on Daniel's in-game check)
- **AT-LMAP-TC-1 (issue #4)** — equipping/opening the held Local Map shows a **table-centred,
  fixed** window: the map does **not** pan or slide to follow the player; the bound Table sits at
  screen centre.
- **AT-LMAP-TC-2 (issue #3)** — inside the 1000 m disc the player marker renders at its **true
  position relative to the table** and moves as the player walks; **outside** the disc the in-disc
  square is **hidden** and a single **edge arrow**, clamped to the disc edge, points outward toward
  the player's real bearing. The player square never renders beyond the disc.
- **AT-LMAP-TC-3 (issues #9 + #2)** — the held view is a **circle** with a **fixed, screen-aligned
  bezel/frame**; only the interior content (cartography + shroud + pins + marker) rotates. No part
  of the frame/box rotates, and no clipping artifact appears at the disc edge as the interior spins.
- **AT-LMAP-TC-4 (issue #2 rotation / Daniel's "B")** — turning the player rotates the interior to
  heading about the **table** pivot; a player turning in place away from the table sees their marker
  **orbit** screen-centre (intended). The rotation **sense** is correct after `MapRotationSign`
  calibration.
- **AT-LMAP-TC-5 (Daniel disorientation lock)** — there is **no** north indicator, compass rose,
  north-up mode, or any orienting aid anywhere on the held Local Map.
- **AT-LMAP-TC-6 (no regression)** — AT-MAP-BOUND (1000 m reveal), AT-MAP-FIXEDZOOM, the §2E.1
  CPU-composite render, pin position+icon-upright behaviour, the §2F exit prompt, and the §2G open
  input are unchanged. The Surveyor's Table (TableEdit) view stays table-centred + **square** +
  keeps left-click pin removal — but now **rotates-to-heading** like the held map (issue #1), with
  **no** north indicator.
- **AT-TABLEVIEW-ROT-1 (issue #1)** — opening the Surveyor's Table view and turning the player
  rotates the table map to heading (it is **no longer north-locked**); there is **no** North
  indicator/compass rose on the table view; left-click pin removal still works while rotated.
- **AT-DISC-MARKER-1 (A′ player-marker art, card t_efe8b32b, 2026-06-19)** — the carry-disc player
  marker is a **chevron "you are here" glyph**, NOT a bare flat blue quad: it reads as a player
  arrowhead dead-centre on the disc. The glyph is **screen-stable pointing up = the player's facing**
  (the disc rotates to heading, so "up" is always *forward*, never a fixed-North arrow — this does NOT
  violate AT-LMAP-TC-5: it is a player-orientation glyph, not an orienting compass aid). Art source is
  vanilla's own player-marker texture (`Minimap.m_smallMarker`'s child graphic, blueprint-read,
  ADR-0006-clean); if that can't be resolved the marker falls back to a procedurally-drawn upward
  chevron so it is **never blank** (the headless-verified fallback — apex up, V-notch base, dark
  outline on transparent). On the table-centred modal the in-disc marker uses the same glyph; the
  off-disc edge-arrow keeps its distinct orange directional recolour.
- logs-green ≠ playable — Daniel confirms in-game.

**Supersession map (old §2H ATs → this section).** AT-LMAP-ROT-1 (free-rotate) → restated in
AT-LMAP-TC-4. **AT-LMAP-ROT-2 (player pinned at centre, world rotates under it) → SUPERSEDED**
(the player is no longer centred; the table is). AT-LMAP-ROT-3 (pins ride rotation, icons upright)
→ retained, now also covers the marker (AT-LMAP-TC-3/-4). AT-LMAP-ROT-4 (off-disc arrow) →
**re-pointed**: the arrow points at the off-disc *player* (table is centred), per AT-LMAP-TC-2.
AT-LMAP-ROT-5 (no zoom/bound regression) → AT-LMAP-TC-6.

**Implementation routing.** One `engineer-ui` worker owns `MapViewer.cs`; route #2/#3/#4/#9 as a
**single** impl card (a child of the #3 card `t_05e702ee`) on a worktree — they are one transform
model and would collide if split (the v0.2.20 `MapViewer.cs` lesson). Sequence after the §2E.1
render impl lands (same file). #11 (pin labels) rides the same rotating overlay but is a separate
label-rendering change, not orientation. **SpecCheck impact: none.** Spec + code move together.

> **✅ IMPL STATUS (2026-06-11, t_23b950ee → branch `feat/local-map-viewer-overhaul-t_23b950ee`).**
> The §2H LOCKED ROUTE (P2 player-centred minimap, route-1 transform rotation) is BUILT in
> `MapViewer.cs` on top of §2E. A new `_mapContainer` pivot node wraps the §2E
> cartography/shroud/pins as one rigid unit; in `FieldReadOnly` `ApplyFieldOrientation` offsets
> that unit by `-WorldToMapRectUnclamped(player)` (player → screen centre) and rotates the
> container by `MapRotationSign * cameraYaw` each FRAME (driven from `Update`, not the 0.25 s
> `Refresh` — §2H b6). The player marker is a static square moved to a never-rotated
> `_staticOverlay` at dead centre (Daniel-locked: no facing indicator); pins counter-rotate their
> icons to stay upright (`CounterRotatePins`); the off-disc indicator (`UpdateTableArrow`) points
> at the bound Table from the rotating container. `TableEdit` resets rotation+offset to identity,
> so the Surveyor's Table view is byte-for-byte the pre-§2H north-up table-centred behaviour.
> Build 0/0. **NOT YET PLAYTESTED — the rotation SENSE (`MapRotationSign`, first guess `+1f`) and
> camera-vs-body-yaw choice are BUILD-CALIBRATED in-client per §2H b2; Daniel's playtest tunes the
> one constant if the map turns the wrong way.** Implementation note: player-centring is realized
> as a rigid TRANSFORM offset of the whole §2E unit (not by re-driving the shader `_mapCenter` to
> the player) — the survey fog/shroud is table-anchored, so a transform offset keeps cartography +
> shroud + pins aligned by construction; re-framing only the shader would desync the shroud mask.

> **Status: DESIGN CORRECTION + UNDER-SPECIFIED-POINT RESOLUTION.** Reported by Daniel,
> v0.2.19-playtest: *"local map does not rotate freely but rather is north fixed."* The fork
> SHELL, bounding, fixed zoom, the §2E cartography render, pins, and the edge arrow are all
> UNCHANGED in PURPOSE — this section adds **orientation** (the map turns with the player so
> forward = up) and resolves a point §2B/§2E left implicit: **what sits at the centre of the
> held view.** Clean-side (ADR-0001). Applies to the **held Local Map (FieldReadOnly) view
> only** — the Surveyor's Table (TableEdit) view stays north-up (see "Table view" below).

**Two factual corrections up front (decomp-verified — do NOT carry the card's framing forward).**
The bug card and Daniel's scoping comment both assume vanilla offers a "free-rotate mode" and
that the in-disc player marker already sits at screen centre. Both are false in the current build:

1. **Vanilla's minimap is NORTH-UP; it does NOT free-rotate, and there is no north-lock/free
   toggle.** Grounded: `Minimap.CenterMap` (`Minimap.cs:1002-1039`) only *pans* the map —
   `uvRect.center`/`m_mapImageLarge.uvRect` + the `_mapCenter`/`_pixelSize`/`_zoom` shader
   uniforms (`:1023-1034`). The map **surface is never rotated.** The complete uniform set the
   vanilla shader is driven by is `_MainTex`/`_MaskTex`/`_HeightTex`/`_FogTex` + `_zoom`/
   `_pixelSize`/`_mapCenter`/`_SharedFade` (`:435-442`, `:628-639`, `:1023-1034`) — **there is no
   rotation uniform.** The only thing that rotates is the **marker**: `UpdatePlayerMarker` sets
   `m_smallMarker.rotation = Quaternion.Euler(0, 0, -eulerAngles.y)` from
   `Utils.GetMainCamera().transform.rotation` (`:958`, `:1412-1416`). So "match vanilla" and
   "free-rotate" are **opposite** for orientation. **There is no vanilla rotation idiom to copy
   — free-rotate is SBPR behaviour we BUILD.** (The "freely rotating" phrasing in the v1 docs —
   `docs/v0.1.0/planning/requirements.md:57`, `cartography-v2.md:23` — is SBPR *design intent*,
   not a description of vanilla. Free-rotate is consistent with that intent; north-up was the
   drift.)

2. **The held view is centred on the bound TABLE, not the player** (`CartographyViewer.cs:70`
   `BoundOrigin = Table position`; `MapViewer.WorldToMapRect :237-239` puts the bound origin's
   cell at rect centre; `LocalMapController :152` feeds the table origin). So the in-disc player
   marker (`MapViewer.cs:343-345`) renders at the player's **offset from the table**, NOT at
   centre — it only coincides with centre when you stand on the table. **Consequence:** Daniel's
   two requirements — *"static square at centre"* AND *"only the map root rotates by -heading,
   keep the marker as-is"* — are **mutually exclusive under the current table-centred projection.**
   Rotating the root about its centre (the table) makes the offset player marker **orbit** screen
   centre as you turn in place; it does not pin it there. Pinning the square at centre **requires
   centring the view on the player.** This is the real crux the card glossed.

**The locked routing decision (route 1 — rotate the transform, NOT the projection/shader).** Bake
heading into a **transform rotation of the displayed map quad + overlay**, never into
`WorldToMapRect` math (route 2, REJECTED) and never into the shader (impossible — no rotation
uniform). Route 2 would also collide head-on with §2E, which is replacing the per-pixel render.
Rotation is render-agnostic: you rotate the `RawImage`'s `RectTransform` (the §2E material rides
its own quad's UVs, so rotating the quad rotates the composited cartography correctly) plus the
overlay layer as one unit.

**RECOMMENDED construction — P2 "player-centred minimap" (matches Daniel's stated visual; primary
spec).** The held Local Map behaves like the personal minimap it is meant to *become* (D1):

1. **Centre the FIELD view on the player.** Each frame in `FieldReadOnly` mode, drive the view so
   the player's world position maps to rect centre — for §2E, set the material `_mapCenter` (and
   `uvRect` window) to the **player** position; for the overlay, project world→rect relative to
   the player. The **bound/shroud stays 1000 m around the TABLE** (AT-MAP-BOUND unchanged — only
   what's *centred* changes, not what's *revealed*). The explored disc therefore sits OFF-centre
   inside the fixed circular shroud vignette and slides as the player walks — the normal "you are
   here, world around you" minimap look.
2. **Rotate the map quad + overlay container by the camera yaw** so camera-forward points up.
   Reference convention: vanilla rotates its marker by `-cameraYaw`; the *map* therefore rotates
   by the **opposite** sense. **Exact sign + camera-yaw-vs-body-yaw is BUILD-CALIBRATED in-client**
   (same discipline that locked `m_pixelSize` in the spike — confirm against the live render, do
   not ship an unverified sign). Heading source: `Utils.GetMainCamera().transform.eulerAngles.y`
   (the member vanilla's own marker uses), unless the calibration shows body-yaw reads better.
3. **Player marker: static featureless square at dead centre** (`MapViewer.cs:343-345` kept at
   `Quaternion.identity` — Daniel's comment, honoured). Forward = up is carried by the rotating
   world under it (AT-LMAP-ROT-2). No facing indicator on the marker (Daniel, 2026-06-11).
4. **Pins ride the rotation for POSITION, counter-rotate their ICON for readability.** Parent pins
   to the rotating container so they stay world-anchored as it spins; then set each pin's own
   `localRotation` to **-containerRotation** so the icon sprite stays screen-upright (never
   upside-down). *(Originally this fork had no pin text. **Now corrected (issue #11 → §2K,
   2026-06-12):** pin **labels** were added in #124's wake; they are rendered as a `Text` **child**
   of each pin GameObject, so the SAME `CounterRotatePins` that rights the icon also rights the
   label — no extra rotation code. See §2K.)* (AT-LMAP-ROT-3.)
5. **Edge arrow points at the TABLE when the table is off-view.** Under player-centring, when the
   player is outside the 1000 m disc the player is at centre and the **table** is the off-screen
   target — clamp a direction arrow toward the bound origin at the view edge. This is *more*
   faithful to AT-MAP-EDGEARROW's wording ("arrow… pointing at the bound Table") than the current
   table-centred clamp. Re-express `BoundedMapMath.EdgeClampToDisc` for the player→table bearing;
   because the arrow is a child of the rotating container, its clamp angle composes with the
   container rotation automatically (AT-LMAP-ROT-4).
6. **Drive rotation + recentre per-frame in `MapViewer.Update()`** (field mode), NOT on the 0.25 s
   survey `Refresh` — at 4 Hz rotation would visibly stutter. The viewer already has an `Update()`
   (Escape/click); add the heading read + recenter there, gated to `FieldReadOnly`.

**Table view (TableEdit) stays NORTH-UP and table-centred** (resolves open-Q3). A Surveyor's
Table is a static placed object with no heading; you stand at it and read. Switch on the existing
`MapViewerMode` flag: `FieldReadOnly` → player-centred + free-rotate; `TableEdit` → north-up +
table-centred (today's behaviour, unchanged). This keeps the Table view a stable shared-editing
surface and confines all rotation to the held map.

**Free-rotate only — NO toggle** (resolves open-Q "north-up/free toggle like vanilla?"). There is
no vanilla toggle to match (correction 1). Daniel asked for free-rotate; v1 intent is "freely
rotating, no north indicator." Ship free-rotate; do not build a north-lock option.

**Cheaper FALLBACK — P1 "table-centred spin" (documented, NOT recommended).** Keep the current
table-centred projection (and §2E unchanged) and rotate the container by heading about its centre
(the table). Delivers forward = up with a near-one-line change and zero §2E coupling, BUT the
player marker **orbits** centre instead of staying pinned there — which **fails Daniel's stated
AT-LMAP-ROT-2.** Listed only so the cost delta is explicit: if Daniel decides an orbiting marker
on a table-centred map is acceptable, P1 is materially cheaper. **Architect's lean: P2.**

**Interaction with §2E (mandatory coordination).** §2H and §2E both define the centring +
projection of the **same** `MapViewer` RawImage, and §2E is not yet built. **Route both to the
SAME `engineer-ui` worker** (the viewer-UX cluster note in all four viewer cards). Concretely: P2
makes the §2E field render **player-centred** (`_mapCenter` → player) while the Table render stays
table-centred — a small, clean delta to §2E, but only coherent if one worker holds both. If they
are split, they will conflict on `MapViewer.cs` centring. Sequence §2E first (it establishes the
material render), then §2H adds centring-mode + rotation on top.

**Graceful degradation.** If `Utils.GetMainCamera()` is null (no camera yet), skip the rotation
for that frame (leave last orientation) rather than throw — the map must never blank on a missing
camera.

**Clean/dirty:** Clean-side (ADR-0001). Reading `Utils.GetMainCamera().transform` /
`Player.m_localPlayer.transform` heading and applying a uGUI transform rotation is base-game read +
our own UI. No decompiled IronGate source copied; no third-party mod code.

#### 2H acceptance tests (named, observable — close only on Daniel's in-game check)
- **AT-LMAP-ROT-1** — turning the player rotates the held Local Map so the player's forward
  direction is up (free-rotate); the map is no longer north-fixed.
- **AT-LMAP-ROT-2** — the player marker sits fixed at the centre of the held view as a static
  square; the world rotates underneath it (player-centred; satisfied by P2, not P1).
- **AT-LMAP-ROT-3** — pins stay anchored to their world positions as the map rotates (they ride
  the rotation) and their icons remain screen-upright (counter-rotated), never upside-down.
- **AT-LMAP-ROT-4** — the fixed 1000 m shroud + the off-disc edge arrow read correctly under
  rotation; when the player is outside the disc the arrow points toward the bound Table in the
  rotated frame.
- **AT-LMAP-ROT-5** (no regression) — fixed zoom (AT-MAP-FIXEDZOOM) and the bounded-disc reveal
  (AT-MAP-BOUND) are unchanged; only orientation + view-centring of the held map change. The
  Surveyor's Table (TableEdit) view stays north-up + table-centred.
- logs-green ≠ playable — Daniel confirms in-game the held map rotates with heading.

**Implementation card:** routed to `engineer-ui` (owns `MapViewer.cs`), as a child of THIS
card and **coordinated with / sequenced after the §2E implementation child** (same worker).
**SpecCheck impact: none** (transform/render behaviour, not a recipe row). Spec + code move
together in that PR.

---

### 2I — Held map updates live while travelling with the Kit (issue 5, 2026-06-12)

> **Status: BUG/DESIGN — ROOT-CAUSE LOCATED + SEMANTICS RESOLVED + RENDER RE-LOCK.** Reported by
> Daniel, v0.2.22-playtest. Resolves a behaviour the §2E.1 render model (issue 10, PR #129) made
> *visible-by-omission*: the held Local Map's shroud is built from the **frozen imprint snapshot**
> only, so wearing the Cartographer's Kit and walking never grows what the field viewer shows.
> The fork SHELL, the §2E.1 cartography composite, §2F/§2G input, §2H.1 orientation, the Kit gate
> (§3), and the `SurveyData` wire format are all UNCHANGED. This section adds **one fog source** to
> the FieldReadOnly shroud — the player's LIVE personal fog, OR'd over the static snapshot —
> mirroring the snapshot∪live PIN union the viewer already does. Clean-side (ADR-0001).
> **SpecCheck impact: none** (render behaviour, not a recipe row).

**What Daniel reported (verbatim):** *"issue 5: local map(s) data don't update while travelling
when the cartographer's tools are equipped."*

#### 2I.1 The two survey surfaces — which one Daniel expects to grow (disambiguation)

The bug card flagged two distinct survey surfaces and asked which Daniel expects to update while
walking. The decomp + the shipped code settle it:

- **(a) The player's PERSONAL fog** — vanilla `Minimap.m_explored` (`bool[m_textureSize²]`),
  written by `Explore(player.position, m_exploreRadius)` every `m_exploreInterval` (2 s) inside
  `UpdateExplore` (decomp `Minimap.cs:1524-1532`/`:1534-1566`). The Cartographer's Kit GATES exactly
  this write (§3.2 — Prefix on `UpdateExplore` returns `false` with no Kit). **With the Kit worn,
  `m_explored` demonstrably grows as you walk** (proven in-game by the §3 IMPL STATUS: *"personal fog
  accumulates even under v1's server-side nomap"*; and `SurveyorTableTag.ContributeLocalSurvey`
  `:288-328` already reads this live array successfully).
- **(b) The TABLE's shared ZDO survey** — the imprinted 1000 m snapshot a Local Map carries
  (`LocalMap.ReadSurvey` → `m_customData["sbpr_map_blob"]`). This is **static by design and by
  hard lock**: requirements §2 *"Imprint = a snapshot of the Table's current survey at imprint time
  (NOT a live link — a map 'as it was when drawn')."* It MUST NOT mutate while carried (it can be
  handed to another player; mutating it would break that contract and AT-MAP regression).

**Resolution (architect's locked reading — flagged for Daniel's ratification): semantics (a),
realized as a live OVERLAY, not a mutation of (b).** The held FieldReadOnly view composites the
player's LIVE personal fog (Kit-gated, disc-clipped to the bound Table's 1000 m disc) OR'd OVER the
frozen imprint snapshot. So a bound Local Map shows *"everything the Table knew when I drew it, PLUS
everywhere I've personally walked in this disc since (while wearing the Kit)."* That is the minimal,
lock-consistent reading of *"travelling grows the map"*:

- The imprinted snapshot stays static in storage (requirement §2 honoured — handing the map to
  another player still gives them the frozen snapshot; the live overlay is each reader's OWN
  `m_explored`, read at render time, never persisted).
- The bound Table survey is **NOT** auto-resynced from the field — re-syncing the shared ZDO from
  afar is the deliberate **Use-at-the-Table** action, which already merges your fog into the Table
  (`ContributeLocalSurvey`, runs on every Table Use before it opens the Table view). The field map
  must not silently owner-write a distant Table's ZDO.
- It is **structurally identical to how PINS already work**: `RebuildOverlay` (`MapViewer.cs:418-454`)
  already unions the snapshot pins (`survey.Pins`) with the LIVE `WorldPins.CollectInDiscPins(origin,
  radius)`. Issue 5 extends that same snapshot∪live dual-source pattern from pins to fog. One model,
  not a new one.

> **If Daniel wants a different surface to grow** (e.g. ALSO re-sync the bound Table survey live, or
> have the field map mutate its own stored snapshot), say so on review and this section re-specs.
> The architect's lean — and the only reading that touches no locked contract — is the live overlay
> above.

#### 2I.2 Located root cause (grounded — the §2E.1 model made it a one-layer gap)

The §2E.1 render (issue 10, PR #129 `60ba21e`) split the viewer into two independent layers:

| Layer | Source | Currentness |
|---|---|---|
| **Cartography** (biome/water/relief) | sampled live from `WorldGenerator` (or, in the shipped material-copy build, vanilla's map textures) | always fully current — deterministic from seed, independent of exploration |
| **Shroud mask** | `SurveyData.Fog` — the lit/unlit cells | **as supplied in `_req.Survey`** |

`PaintShroudMask(survey)` (`MapViewer.cs:309-340`) makes `fog[i] ? transparent : opaque-shroud`. In
**FieldReadOnly** the `survey` is `LocalMap.ReadSurvey(map)` (`LocalMapController.OpenFullView :266`
/ `RefreshOpenView :289`) — the **frozen imprint snapshot**. `RefreshOpenView` re-reads that SAME
frozen blob on the 0.25 s poll, so the shroud never changes. **There is no code path feeding the
player's live `m_explored` into the held viewer's shroud.** Therefore, even with the Kit working
perfectly and `m_explored` growing every 2 s, the held map's shroud can never recede while walking.
That — not a broken Kit — is the bug. The cartography layer is irrelevant to the defect (it's always
fully drawn underneath); the shroud is the only thing gating visibility, and its only input is the
static snapshot.

**Corollary:** issue 5 is a **pure shroud-source enrichment**, riding directly on §2E.1. No
cartography change, no `SurveyData` wire change, no Kit change.

#### 2I.3 🔒 LOCKED ROUTE — OR the live personal-fog window into the FieldReadOnly shroud

In **FieldReadOnly mode only**, before building the shroud mask, merge the player's live personal
fog into a RENDER-TIME COPY of the survey fog (never mutate the stored `SurveyData`):

1. **Read live personal fog.** `Minimap.instance.m_explored` via the SAME reflected-field idiom
   `SurveyorTableTag.ReadExplored` already uses (`:463-468` — `GetField("m_explored",
   Instance|NonPublic)`, cached `FieldInfo`). Reuse/extract that helper; do not hand-roll a second
   reflection path. Clean-side: `m_explored` is a stable base-game field (ADR-0001).
2. **Window it to the SAME `WindowSpec` the snapshot uses** — `BoundedMapMath.BuildWindowedFog`
   (the fog half of `SurveyData.CaptureWindow`) keyed on the **survey's `OriginX/OriginZ`** (the
   bound Table), `RadiusMeters`, `PixelSize`, `TextureSize` — **NOT** the player position. Under the
   §2H.1 re-lock the held view is **TABLE-centred** (the bound origin sits at rect centre; the
   player marker moves within the fixed window), so the live-fog window is table-anchored exactly
   like the snapshot fog, the §2E.1 cartography, and the pins — one `WindowSpec`, aligned by
   construction, no offset subtlety. (Do NOT re-window on the player position: that would desync the
   live fog from the static cartography/snapshot beneath it.)
3. **OR-merge into a copy.** `mergedFog[i] = snapshot.Fog[i] || liveWindow[i]`. Build a throwaway
   `bool[]` (or a scratch `SurveyData` clone) for the mask; the stored snapshot is untouched
   (requirement §2 static-snapshot lock; AT-LMAP-LIVE-5).
4. **Disc-clip is inherited.** `BuildWindowedFog` already clips to the 1000 m disc, so live cells
   beyond the bound disc never light up — walking OUTSIDE the disc reveals nothing on this map
   (AT-MAP-BOUND holds; the §2H.1 edge arrow then points outward toward the player's real bearing).
   Pins likewise stay disc-bound.
5. **Feed `mergedFog` to `PaintShroudMask`** (and to the overlay's disc test). The always-present
   §2E.1 cartography shows through every newly-lit cell → the map visibly fills in as you travel.
6. **Re-evaluate every Render().** The shroud merge must recompute each `Render()` (not cache once),
   so the live window reflects the current `m_explored`. The cadence already exists:
   `LocalMapController` calls `RefreshOpenView` → `CartographyViewer.Refresh` → `Render()` every
   0.25 s while a field map is open (`LocalMapController.cs:156-158`) — 4 Hz, far finer than the 2 s
   explore interval, so the reveal is smooth and needs NO new timer. (The §2H.1 per-frame `Update`
   drives rotation; the fog merge can ride either the 0.25 s `Refresh` or the per-frame `Update` —
   implementer's choice; 0.25 s is sufficient and cheaper.)
7. **Cost is trivial.** The window is ~33×33 ≈ 1089 cells; re-windowing reads only that disc
   sub-rectangle of `m_explored`, not the whole array. Negligible at 4 Hz.

**Kit gate holds by construction (AT-LMAP-LIVE-2).** The live overlay's only new cells come from
`m_explored`, which the §3 Kit Prefix only lets grow WHILE THE KIT IS WORN. With the Kit off,
`m_explored` does not change, so the live window contributes nothing new and the held map shows only
the static snapshot — i.e. *"without the Kit, no passive reveal"* falls out automatically; no extra
gate code in the viewer.

**TableEdit (Surveyor's Table view) is UNCHANGED — no live overlay there.** The Table view reads the
shared ZDO survey, and `ContributeLocalSurvey` ALREADY merges the user's live fog into it on the
same Use that opens it — so the Table view is fresh by that path, not by a render-time overlay.
Adding a personal-fog overlay to the shared-editing surface would muddy its shared semantics. The
overlay is **FieldReadOnly-only**, symmetric with §2H (FieldReadOnly rotates/centres; TableEdit stays
static north-up). Switch on the existing `MapViewerMode` flag.

**`SurveyData` wire format UNCHANGED** (no ZDO contract change → placed Tables / imprinted maps don't
orphan, AT-LMAP-LIVE-6). The live fog is a render-time read of `Minimap.m_explored`, never serialized.

#### 2I.4 Investigation precondition (verify before closing — logs-green ≠ playable)

The card asks to confirm the Kit actually engages under enforced nomap (the v0.2.19 Kit had a
separate no-cost crash). This is **already addressed** — the icon-crash was fixed (§3.1 / C1
`FallbackIcon`) and the §3 gate verified `UpdateExplore` fires under nomap — but it is the
PRECONDITION for issue 5 being observable. Daniel verifies in one pass: craft + equip the Kit, walk,
confirm (1) the Kit is craftable/equippable with no crash, AND (2) the held map's shroud now recedes
along your path. If the shroud still doesn't move with a confirmed-equipped Kit, the live-fog read
(step 1) — not the snapshot — is the suspect.

#### 2I.5 Files touched + clean/dirty

- **`MapViewer.cs`** — in `Render()`/`TryRenderVanillaCartography`/`PaintShroudMask`, when
  `_req.Mode == FieldReadOnly`, build the snapshot∪live merged fog (steps 1-5) and paint the shroud
  + run the overlay disc test from it. Extract the reflected `m_explored` read into a shared helper
  (or reuse `SurveyorTableTag`'s). No change to TableEdit, the cartography composite, the §2H.1
  transform/orientation, or `SurveyData`.
- **(optional) a shared fog-read helper** — if `SurveyorTableTag.ReadExplored` is made reusable
  (e.g. a small `static bool[]? MinimapFog.ReadExplored()` in `BoundedMapMath` or a new tiny helper),
  both the Table contribute path and the viewer overlay read `m_explored` through one cached
  `FieldInfo`. Keeps the reflection idiom single-sourced.
- **Clean-side (ADR-0001):** reading `Minimap.m_explored` + windowing with `BoundedMapMath` is
  base-game read + our own math. No decompiled IronGate source committed; no third-party mod code
  (the reference cartography mods are NOT consulted for this — the snapshot∪live pattern is taken
  from THIS repo's own pin overlay).

#### 2I.6 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-LMAP-LIVE-1** — With the Kit worn and a bound Local Map equipped (field view open), walking
  visibly grows the revealed area on the held map (the shroud recedes along your path), in real time
  as you travel.
- **AT-LMAP-LIVE-2** — WITHOUT the Kit, walking reveals NOTHING new on the held map (the §3 gate
  still holds; the held shroud shows only the static imprint snapshot).
- **AT-LMAP-LIVE-3** — The reveal is bounded: walking OUTSIDE the bound Table's 1000 m disc adds
  nothing to the map (AT-MAP-BOUND intact); the §2H.1 edge arrow still points outward toward the
  player's real bearing.
- **AT-LMAP-LIVE-4** — The live reveal aligns with the cartography and pins under §2H.1
  rotate-to-heading + table-centred framing (the live-fog window is table-anchored to the same
  `WindowSpec`); no offset/tearing between the newly-lit cells and the terrain beneath them.
- **AT-LMAP-LIVE-5** (snapshot static-in-storage) — The imprinted snapshot is NOT mutated: re-reading
  the same map after walking (e.g. relog, or hand it to another player) shows that player's own
  live fog over the ORIGINAL snapshot, not your accumulated walk baked into the item. `SurveyData`
  wire unchanged.
- **AT-LMAP-LIVE-6** (no regression) — Table (TableEdit) view unchanged; placed Tables / imprinted
  maps don't orphan (no wire change); the §2E.1 cartography, §2F exit, §2G open, §2H.1 orientation all
  still behave.
- logs-green ≠ playable — Daniel confirms in-game: Kit on + walk → the held map fills in as he
  travels; Kit off → it doesn't.

#### 2I.7 Routing

- **Clean-side → `engineer-ui`** (owns `MapViewer.cs` + `LocalMapController.cs` + the whole viewer
  cluster). This is a small, self-contained shroud-source change on top of the §2E.1 render — route
  it to the SAME worker that holds the viewer cluster, **after** the §2E.1 CPU-composite render child
  lands (the live overlay paints into that composite's shroud layer; sequencing it first would build
  against a render that's being replaced).
- **SpecCheck impact: none** (render behaviour, no recipe row). Spec + code move together in the PR.


### 2I — E-to-open reliability + imprint redesign to look-at-table + hotbar# (issue 6, 2026-06-12)

> **Status: BUG-FIX + DESIGN CORRECTION.** Two coupled changes to the §2G open model and the
> §1.6.4 imprint trigger, reported together by Daniel (v0.2.22-playtest, in game). **Part A** is a
> bug: the §2G "[E] open map" intermittently dies and only recovers after re-using the Table.
> **Part B** is the locked enhancement: replace the auto/Use imprint with an explicit
> *look-at-the-Table + press the hotbar number of the Local Map you want to imprint* gesture.
> They are coupled because the present auto-imprint-on-Use is one of the paths that can leave the
> open-input gate latched (Part A), and Part B removes that ambiguity by construction. Clean-side
> (ADR-0001): everything below reads/patches the base game only (`ZInput`, `Player.UseHotbarItem`,
> `Inventory.GetItemAt`, `Player.GetHoverObject`) — verified against the `assembly_valheim` decomp.
> **SpecCheck/recipe manifest impact: none** (input + interaction behaviour, no recipe rows).

> **✅ IMPL STATUS (2026-06-12, card t_5b070785, engineer-ui).** BUILT on branch
> `feat/eopen-reliable-hotbar-imprint-t_5b070785` off `v1` (after the viewer-cluster PR #139
> landed — the MapViewer.cs collision this card's SEQUENCING warned about is resolved). Build
> 0 errors / 0 warnings. One PR, both parts (they share MapViewer.cs / SurveyorTableTag.cs).
> **Part A** — `MapViewer.IsOpen` is now `_root != null && _root.activeSelf` (§2I.2 mechanism
> (i)); the `_open` side-bool is DELETED and `Open`/`Refresh`/`Close`/`Update`/`RefreshIfOpen`
> re-gate on the canvas. *(Note: `RefreshIfOpen` was itself removed in the later §2E.3 cleanup cut —
> its only caller was the deleted `sbpr_mapmode` toggle. `Open`/`Refresh`/`Close`/`Update` still
> re-gate on the canvas.)* `SignPanelInputBlock.AnyOpen` documents the §2I.2 liveness invariant
> (mechanism (ii)) — all three contributors now derive from live `activeSelf`, so none can latch
> the §2G E-open gate. No watchdog/timer added; the §2G modal-suppress is untouched. **Part B** —
> new `SurveyorTableHotbarImprintPatch` (Harmony prefix on `Player.UseHotbarItem(int)`, Type[]
> overload-pinned, registered in `Plugin.Awake` before `PatchCheck.Run` → PatchCheck-verified):
> local-player + hovered-`SurveyorTableTag` gate → `GetItemAt(index-1,0)` → `TryImprintSlot`,
> `return !handled` consumes a handled press. `SurveyorTableTag.Interact` no longer imprints (the
> `ImprintCarriedLocalMaps` call is removed; the method is retired); new
> `TryImprintSlot(ItemDrop.ItemData?)` reuses the name/ward/empty-survey backstops one slot at a
> time with the §2I.4 Center-message refusals; the named-Table `GetHoverText` gains the
> `[1-8] Imprint that Local Map` line. **Verified: build 0/0 + DLL contains the new type + the
> patch is registered.** NOT yet verified in-game — the §2I.6 acceptance tests
> (AT-LMAP-OPEN-RELIABLE-1..3, AT-IMPRINT-HOTBAR-1..6) are Daniel's playtest gate (logs-green ≠
> playable); in particular Part A reliability and Part B refusal feedback need a live client.

**What Daniel reported (verbatim):** *"issue 6: for some reason, the 'press E to open the map'
stopped working. Either after copying from the map station, or after pinning a marker sign.
Seemed to start working again after using the table again. We should make copy to a map require
you to look at the table and press the number associated with the hotkey bar map you want to
imprint upon."*

#### 2I.1 PART A — root cause: the §2G open-input gate is a multi-flag latch (grounded)

The §2G open fires from `LocalMapController.Update` (`LocalMapController.cs:112-121`) only when
**both** of these hold:

1. `!tableViewOwnsViewer` — where `tableViewOwnsViewer = CartographyViewer.IsViewerOpen &&
   CurrentMode == TableEdit` (`:88-89`); and
2. `CanOpenOnUse(player)` returns true (`:119`), which is **false** whenever any of
   (`GetHoverObject() != null`) `||` `TextInput.IsVisible()` `||` `InventoryGui.IsVisible()` `||`
   **`SignPanelInputBlock.AnyOpen`** (`:177-189`).

`SignPanelInputBlock.AnyOpen` (`SignPanelInputBlock.cs:41-44`) is the OR of **three independent
modal flags**:

```
AnyOpen = SignPaintPanel.IsOpen
        || MarkerSignPanel.IsOpen
        || CartographyViewer.IsViewerOpen
```

**If any one of those flags is stuck `true`, E-to-open silently stays dead** — and so does the
"[E] Open map" prompt's usefulness, because the field viewer never opens. That single fact
explains both of Daniel's triggers and the "recovers after using the Table" tell:

- **"After copying from the map station" (imprint at the Table):** the Table opens the viewer in
  `TableEdit` (`SurveyorTableTag.Interact:177-186`). While that view is up,
  `CartographyViewer.IsViewerOpen` is true → `AnyOpen` true **and** `tableViewOwnsViewer` true →
  E-open is correctly suppressed. The defect is in the **teardown**: `MapViewer` tracks open-state
  in a **standalone bool `_open`** (`MapViewer.cs:131-132`), flipped in `Open()`/`Close()`. Every
  *other* SBPR modal derives `IsOpen` from `_root.activeSelf` (`MarkerSignPanel.cs:38`,
  `SignPaintPanel.cs:55`), which **cannot desync** from the actual GameObject state. The viewer's
  bool **can**: any close path that deactivates the root (or any frame where `Close()` is skipped
  while the canvas is hidden, e.g. a scene/route change, an exception thrown out of `Render()`
  after `_open=true` on `:142-143`, or an Escape consumed by a different handler) leaves `_open`
  reading `true` while nothing is on screen. The result: `IsViewerOpen` stays latched → `AnyOpen`
  stays latched → E-open is dead. **Re-using the Table** calls `CartographyViewer.Open()` →
  `MapViewer.Open()` which re-asserts `_open=true` + `_root.SetActive(true)`, and the subsequent
  Escape close runs `Close()` cleanly → `_open=false`. That is *exactly* the "started working
  again after using the table again" recovery.
- **"After pinning a marker sign":** primary **E** on a marker opens `MarkerSignPanel`
  (`SignInteractPatch.cs:79`). `MarkerSignPanel.IsOpen` keys on `_root.activeSelf`, and its only
  dismiss paths (Escape / Close button / destroyed-sign) all route through `Hide()` which
  `SetActive(false)` (`MarkerSignPanel.cs:129-139`). This flag is **structurally sound** — but it
  feeds the same `AnyOpen`. If the panel is closed by any path that does **not** run `Hide()`
  (e.g. the host GameObject deactivated by a scene transition while the panel was open, or the
  *Shift+E fast-pin* path interacting with panel lifecycle), `activeSelf` could read stale.
  Daniel's "after pinning a marker sign" maps to the **Shift+E** fast-pin gesture
  (`SignInteractPatch.cs:47-69`), which does NOT open the panel at all — so the suspicion here is
  weaker than the viewer-bool path. **(OPEN — route to RE/engineer:** reproduce whether
  `MarkerSignPanel.IsOpen` can read `true` after a Shift+E pin with no panel ever shown; the most
  likely real culprit for *both* triggers is the viewer `_open` latch, with the marker path a
  secondary contributor or a red herring. The fix below hardens **all three** flags so the spec
  is correct regardless of which trigger reproduces.)

**Why now (regression window):** both PR #123 (the new §2G Use-key open + the standalone-bool
`MapViewer`) and PR #126 (table-naming, which re-routes `Interact` and threads a title into the
viewer) **landed in v0.2.22** — the exact build Daniel reports. The latch surface is new code.

#### 2I.2 PART A — the fix: make viewer open-state authoritative, and self-heal the gate

Two locked requirements; an implementer's-choice on mechanism within them.

1. **`MapViewer.IsOpen` MUST track the actual canvas, not a side bool.** Derive it from the root,
   the same discipline the sign panels use:
   ```csharp
   public bool IsOpen => _root != null && _root.activeSelf;
   ```
   and drop the `_open` field (or keep it strictly as a cache that is *never* the source of truth).
   This makes `CartographyViewer.IsViewerOpen` un-latchable: if the overlay isn't on screen, the
   gate reads closed. (Same change kills the `Refresh()`/`Update()` `if (!_open) return` early-outs
   cleanly — gate them on `IsOpen`.)
2. **The §2G open-suppression gate MUST be self-healing — never trust a modal flag that has no
   visible surface.** Even with (1), defend in depth: in `CanOpenOnUse` (and/or the
   `SignPanelInputBlock.AnyOpen` getter), a flag should only suppress when its owning surface is
   *actually* displayed. The marker/paint panels already meet this (they key on `activeSelf`); the
   requirement is that **no SBPR modal contributes to `AnyOpen` via a bool that can outlive its
   GameObject.** Audit all three; convert any side-bool to an `activeSelf`/instance-liveness check.

**Implementer's-choice mechanism, equivalent outcomes (pick the minimal one that satisfies 1+2):**
- (i) The §2I.1-named change: `MapViewer.IsOpen => _root.activeSelf`, delete `_open`. Smallest
  delta; fixes the one demonstrated latch. **Recommended.**
- (ii) Additionally harden `SignPanelInputBlock.AnyOpen` to re-derive each contributor from its
  panel's live `activeSelf` (belt-and-suspenders; covers a future side-bool regression).

No new Harmony surface is required for Part A. **Do NOT** "fix" this by adding a watchdog that
force-closes the viewer on a timer, or by removing the modal-suppress from §2G (that suppress is
correct — a real open panel must eat the Use press); the bug is a *stale* flag, not the gate logic.

#### 2I.3 PART B — locked imprint trigger: look at the Table + press the target map's hotbar number

**Replaces** the §1.6.4 "Use the Table → contribute + imprint ALL carried Local Maps" trigger with
an explicit, disambiguated gesture (Daniel's locked enhancement):

> **While looking at (hovering) a named Surveyor's Table, press the hotbar number key (1–8) of the
> Local Map slot you want to imprint. That one map — and only that one — is imprinted with the
> Table's current survey.**

This removes the auto-imprint-on-Use ambiguity (which map got the survey when you carried
several?) and decouples imprint from the viewer-open press that fed Part A.

**The input seam (decomp-verified — clean-side):** vanilla `Player.Update` reads
`ZInput.GetButtonDown("Hotbar1".."Hotbar8")` and calls `Player.UseHotbarItem(n)`
(`Player.cs` decomp `:888-919`); `UseHotbarItem(int index)` resolves the item via
`m_inventory.GetItemAt(index - 1, 0)` and `UseItem(...)`s it (`:2471-2478`). The whole hotbar
block runs only when `TakeInput()` is true (`:781`, gate at `:2461` — false while any vanilla
modal/menu/chat is up). **A Harmony prefix on `Player.UseHotbarItem(int)`** is therefore the exact,
collision-free capture point:

```
[HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]   // (int index)
prefix(Player __instance, int index):
    if __instance != Player.m_localPlayer: return true            // only the local player
    table = HoveredSurveyorTable(__instance)                      // GetHoverObject()→GetComponentInParent<SurveyorTableTag>()
    if table == null: return true                                 // not looking at a Table → vanilla hotbar use
    item = __instance.GetInventory().GetItemAt(index - 1, 0)      // SAME slot vanilla would use (row 0 = hotbar)
    handled = table.TryImprintSlot(item)                          // §2I.4 refusal-aware imprint of THIS map
    return !handled                                               // handled → skip vanilla UseItem (don't "equip" the map); else fall through
```

- **`HoveredSurveyorTable`** reuses the §2G hover idiom: `player.GetHoverObject()` (public accessor,
  decomp `Player.cs:4055`) → `GetComponentInParent<SurveyorTableTag>()`. Looking at the Table is the
  gate; standing near it is not enough (consistent with how vanilla Use targets the hovered piece).
- **Slot mapping is vanilla-faithful:** hotbar number `n` → `GetItemAt(n-1, 0)` — row 0 is the
  hotbar, so "press 3" imprints whatever sits in hotbar slot 3, exactly the item vanilla's
  `UseHotbarItem(3)` would have actioned. No custom slot math.
- **Why prefix `UseHotbarItem` and not raw `GetButtonDown("HotbarN")`:** the single method covers
  all 8 keys + the gamepad radial's hotbar use, runs already inside vanilla's `TakeInput` gate, and
  lets us *consume* the press (return false) so the map isn't also "used"/equipped by vanilla in the
  same frame. Reading `ZInput` directly in the controller would duplicate the 8-key plumbing and
  miss the consume.
- **Coexistence with §2G:** the Local Map open gesture is **Use (E) while equipped**; the imprint
  gesture is **a hotbar number while hovering the Table**. Different inputs, different preconditions
  — no collision. Surveying-by-Use on the Table is unchanged (see §2I.4).

#### 2I.4 PART B — Table interaction split, refusals, and feedback

The Table's `Interact` (Use/E) and the new hotbar-imprint split responsibilities cleanly:

- **Use (E) on the Table — unchanged contribute + name-gate + open viewer**, MINUS the imprint
  step. `SurveyorTableTag.Interact` keeps: ward gate → `ContributeLocalSurvey` (survey/record is
  NOT name-gated, §1.6.4.3) → if unnamed, launch the rename dialog and return (§1.6.4) → open the
  TableEdit viewer with the title (§2B.1). **Remove the `ImprintCarriedLocalMaps(user)` call** from
  the Use path — imprint no longer rides Use.
- **Hotbar number while hovering the Table — the new imprint** (`TryImprintSlot(item)`), with these
  refusals (each gives Center-message feedback; no silent no-op):
  - **Table unnamed** → refuse: `"Name this table before binding maps"` (preserves the §1.6.4 bind
    gate — `ImprintCarriedLocalMaps`'s empty-name backstop is now enforced per-slot here). Imprint
    never happens while the name is empty (the hard requirement, unchanged).
  - **Ward access denied** → refuse with the vanilla `$piece_noaccess` (re-check
    `PrivateArea.CheckAccess` — never trust the gesture to have gated).
  - **Slot empty / not a Local Map** → refuse: `"Hold a Local Map in that slot to imprint it"`.
    (Guard with the existing `LocalMapItemTag`/prefab-name check — the same `IsLocalMap` idiom.)
  - **Table has no survey yet** → refuse: `"This table has nothing surveyed yet"` (mirrors the
    existing `shared.IsEmpty` no-op, now surfaced as feedback).
  - **Success** → imprint THAT ONE slot's map via `LocalMap.Imprint(item, shared, origin,
    GetTableName())` (snapshot + bound-origin + name, §2A.5/§2A.6, unchanged), and confirm:
    `"Local Map imprinted: <table name>"`. Consume the press (return false from the prefix) so the
    map is not also equipped/used by vanilla.
- **`ImprintCarriedLocalMaps` is retired as a Use-path step** but its per-map core (the name +
  empty-survey backstops + the `LocalMap.Imprint` call) is **reused by `TryImprintSlot`** for a
  single item. Keep the hard backstops; just drive them one slot at a time.
- **Hover affordance (so the gesture is discoverable):** extend the Table's named-state
  `GetHoverText` (`SurveyorTableTag.cs:117-125`) to add a line like
  `"[1-8] Imprint that Local Map"` (plain English; the bracketed digits are literal, not a
  `$KEY_*` token — the hotbar keys are not a single rebindable Trailborne action). The unnamed-Table
  hover keeps its `"[Use] Name this table"` line (binding still blocked until named).

#### 2I.5 Files touched + clean/dirty

- **`MapViewer.cs`** — `IsOpen => _root != null && _root.activeSelf`; remove/neutralize the `_open`
  side bool; re-gate `Refresh`/`Update` early-outs on `IsOpen` (Part A fix 1).
- **`SignPanelInputBlock.cs`** — (if mechanism (ii)) re-derive each `AnyOpen` contributor from its
  panel's live state so no side-bool can latch the gate (Part A fix 2). No change to the three
  Harmony patch bodies.
- **`SurveyorTableTag.cs`** — remove the `ImprintCarriedLocalMaps(user)` call from the `Interact`
  Use path; add `TryImprintSlot(ItemDrop.ItemData? item)` (the refusal-aware single-map imprint,
  §2I.4) reusing the existing name/ward/empty-survey backstops; add the `[1-8] Imprint` hover line.
- **`SurveyorTableHotbarImprintPatch.cs`** (NEW) — the `Player.UseHotbarItem(int)` prefix (§2I.3).
  Register it in `Plugin.Awake()` via `harmony.PatchAll(typeof(...))` so **`PatchCheck` catches it
  at boot** (the t_564f695a "unregistered patch ships dead" lesson — mandatory).
- **`LocalMapController.cs` / `CanOpenOnUse`** — no logic change required if Part A fix 1 lands
  (the gate becomes correct once `IsViewerOpen` can't latch); optionally tighten per fix 2.
- **Clean-side (ADR-0001):** `ZInput`, `Player.UseHotbarItem`, `Player.GetHoverObject`,
  `Inventory.GetItemAt`, `PrivateArea.CheckAccess`, vanilla `$piece_noaccess` token — all base
  game. No third-party mod code. **No SpecCheck/recipe-manifest impact** (input/interaction, not a
  recipe row). Spec + code move together in the impl PR.

#### 2I.6 Acceptance criteria (named, observable — close only on Daniel's in-game check)

- **AT-LMAP-OPEN-RELIABLE-1 (Part A — imprint trigger)** — After imprinting at a Surveyor's Table
  (§2I.3 hotbar gesture) and walking away, equipping the Local Map and pressing **Use (E)** opens
  the field viewer **every time** — no dead-E state, no need to re-use the Table to "wake it up".
- **AT-LMAP-OPEN-RELIABLE-2 (Part A — marker-sign trigger)** — After pinning/unpinning a marker
  sign (both the **E** panel path and the **Shift+E** fast path) and closing any panel, **Use (E)**
  on an equipped Local Map opens the field viewer reliably. The §2G modal-suppress still correctly
  blocks E-open *while* a panel is genuinely on screen.
- **AT-LMAP-OPEN-RELIABLE-3 (no false latch)** — `CartographyViewer.IsViewerOpen` reads `false`
  whenever the viewer overlay is not visible on screen (it is derived from the canvas, not a side
  bool). Verifiable via a one-line debug log on the gate, or by the absence of any dead-E episode
  across a play session that opens/closes the Table view, the field view, and both sign panels
  repeatedly.
- **AT-IMPRINT-HOTBAR-1 (Part B — the gesture)** — Looking at a **named** Table and pressing the
  hotbar number of a slot holding a **blank Local Map** imprints THAT map (it now reads as bound:
  bears the Table name + opens to the Table's survey), and **only** that map — other carried blank
  maps stay blank.
- **AT-IMPRINT-HOTBAR-2 (Part B — wrong slot refused)** — Pressing a hotbar number for a slot that
  is empty or holds a non-Local-Map item, while looking at the Table, is **safely refused** with a
  Center message and changes nothing (the slot's item is NOT consumed/used/equipped).
- **AT-IMPRINT-HOTBAR-3 (Part B — name gate preserved)** — The same gesture at an **unnamed** Table
  is refused with `"Name this table before binding maps"`; no `sbpr_map_blob`/`sbpr_map_name` is
  written (the §1.6.4 bind gate holds, now enforced at the per-slot imprint).
- **AT-IMPRINT-HOTBAR-4 (Part B — Use no longer imprints)** — Using (E) the named Table
  contributes the survey + opens the TableEdit view but **does not** imprint any carried map; only
  the §2I.3 hotbar gesture imprints. (Confirms the auto-imprint ambiguity that fed Part A is gone.)
- **AT-IMPRINT-HOTBAR-5 (discoverable)** — A named Table's hover text shows the `[1-8] Imprint that
  Local Map` affordance; an unnamed Table still shows `[Use] Name this table`.
- **AT-IMPRINT-HOTBAR-6 (no vanilla collision)** — While NOT looking at a Surveyor's Table, hotbar
  number keys behave **exactly** as vanilla (use/equip the slot item). The imprint behaviour only
  triggers while the Table is hovered.
- logs-green ≠ playable — **Daniel confirms in-game** (Part A reliability across all triggers +
  Part B gesture, refusals, and the Use-no-longer-imprints split).

**Implementation routing.** One `engineer-ui` worker owns the cartography UI surface
(`MapViewer.cs` / `SurveyorTableTag.cs` / the new hotbar patch). Route §2I as a **single** impl
card (child of THIS spec card) — Part A and Part B touch the same files and the same interaction
model and would collide if split (the v0.2.20 `MapViewer.cs` lesson). **Sequence after** the
in-flight §2H orientation card (`t_05e702ee`) and the §2E.1 render card (`t_14c34abe`) land, since
both are mid-flight in `MapViewer.cs`. **SpecCheck impact: none.** Spec + code move together.

> **Note — supersedes the §1.6.4 / §2A.5 imprint *trigger*, not the imprint *mechanism*.** The
> snapshot format, bound-origin, name-stamp, and all per-instance storage (§2A.5/§2A.6) are
> UNCHANGED. Only *what fires the imprint* moves from "Use the Table (imprints all carried maps)"
> to "hover the Table + press the target map's hotbar number (imprints that one)". The §1.6.4 name
> gate and the `LocalMap.Imprint` backstops are preserved, now enforced per-slot in
> `TryImprintSlot`.

---
### 2K — Pin labels on the held Local Map (issue #11, 2026-06-12, card t_424f38be)

> **Status: BUG — MISSING RENDER LEG.** Reported by Daniel 2026-06-12 (v0.2.22-playtest):
> *"issue 11: markers pin labels don't appear on the local map."* Pinned marker signs show their
> **icon** on the bounded viewer but not their **text label** — the name set via the namable-markers
> feature (`SBPR_PinName`, shipped #124, §7). The label data reaches the viewer; there is simply no
> render path for it. This rides the same rotating pin overlay as §2H.1 but is a **separate
> label-rendering change, not orientation** (the §2H.1 routing note already carved #11 out).
> Clean-side (ADR-0001): our own uGUI text on our own overlay; vanilla read only. **SpecCheck
> impact: none** (render/presentation, no recipe row). Spec + code move together in the impl PR.

#### 2K.1 Located root cause (grounded — verified against `origin/v1`)

The label string is carried end-to-end **into** the viewer and then dropped at the last step:

1. **Data is present.** `SurveyPin.Name` (`Features/Cartography/SurveyData.cs:37`) holds the
   resolved label. The live-scan source populates it via the centralized resolver:
   `WorldPins.CollectInDiscPins` builds `new SurveyPin(label, …)` where
   `label = ResolveLabel(zdo.GetString(SBPR_PinName), def)` (`Features/MarkerSigns/WorldPins.cs:262-263`).
   `ResolveLabel` (`WorldPins.cs:363-367`) is: custom name if non-blank → else the marker type's
   `PinLabel` (`MarkerSigns.cs:71`, e.g. "Mining"/"Portal"/"Shelter"/"Point of Interest") → else
   `"Marker"`. Imprinted **snapshot** pins (`survey.Pins`) carry whatever name was saved on the
   vanilla pin (may be empty). So every marker-sign pin arrives with a non-empty `Name`; generic
   snapshot pins may arrive with an empty one.
2. **Render drops it.** `MapViewer.SpawnPinMarker(pin, anchored)`
   (`Features/Cartography/MapViewer.cs:529-548`) creates a `GameObject("pin")`, adds a single
   `RawImage` for the icon, sizes/positions it — and **never reads `pin.Name`.** No `Text` is ever
   created for a pin. Labels exist in data with no render leg in the bounded viewer. **That is the
   whole bug.**

#### 2K.2 🔒 LOCKED ROUTE — add a `Text` CHILD to the existing pin GameObject

Augment `SpawnPinMarker` to parent a `Text` label **under the same `go`** that holds the icon. The
GameObject is already the unit that pins, positions, counter-rotates, and clears — so a child
inherits all four for free. Concretely, after the icon is built (`MapViewer.cs:529-548`):

```
// after: _pinObjects.Add(go);  (the Text is a CHILD of go, NOT a sibling on _overlayLayer)
if (!string.IsNullOrWhiteSpace(pin.Name))
{
    var labelGo = new GameObject("pinLabel");
    labelGo.transform.SetParent(go.transform, false);     // child of the pin → rides + counter-rotates with it
    var txt = labelGo.AddComponent<Text>();
    txt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
               ?? Resources.GetBuiltinResource<Font>("Arial.ttf");   // SAME font as §2B.1 title / §2F exit prompt
    txt.fontSize = PinLabelFontPx;                         // new const ~14 (annotation scale, below the 26/34 prompt/title)
    txt.alignment = TextAnchor.UpperCenter;                // sits centred BELOW the icon
    txt.color = new Color(1f, 0.95f, 0.8f, 0.97f);         // parchment-cream, matches title/exit prompt
    txt.horizontalOverflow = HorizontalWrapMode.Overflow;  // single line, no mid-word clip
    txt.verticalOverflow   = VerticalWrapMode.Overflow;
    txt.raycastTarget = false;                             // never eat the TableEdit left-click-remove ray
    txt.text = pin.Name;
    var lrt = txt.rectTransform;
    lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
    lrt.pivot = new Vector2(0.5f, 1f);                     // top-centre pivot → grows downward
    lrt.anchoredPosition = new Vector2(0f, -(PinIconPx * 0.5f + 2f)); // just under the icon (PinIconPx=22, :72)
    // legibility over the §2E.1 composite (Outline precedent: MarkerSignPanel.cs:467-469):
    var outline = labelGo.AddComponent<Outline>();
    outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
    outline.effectDistance = new Vector2(1.5f, -1.5f);
}
```

**Why a CHILD and not a sibling — the load-bearing design point.** §2H.1 keeps pin **icons**
screen-upright under map rotation via `CounterRotatePins` (`MapViewer.cs:717-722`), which sets each
pin **GameObject's** `localRotation = -containerRotation`. A label parented to that GameObject
therefore counter-rotates **with the icon, automatically** — its world rotation is
`container(+Z) · go(-Z) = identity` (screen-upright) and its local `(0, -Y)` offset resolves to a
fixed screen-space position **below** the icon at every heading. **No new counter-rotation code is
needed, and `CounterRotatePins` does not change.** In the `TableEdit` (Surveyor's Table) view, the
interior never rotates (`ApplyFieldOrientation` early-returns at identity), so the label is upright
there too with no special-casing. `ClearPinObjects` (`MapViewer.cs:809-813`) destroys `go`, which
takes the child label with it — lifecycle is automatic; `_pinObjects` is unchanged.

**Both views get labels by construction.** `SpawnPinMarker` is shared by `FieldReadOnly` and
`TableEdit`, so labels render in the held map AND the table-editing surface. This is desirable —
the table is the pin-management surface (left-click remove), where reading names matters most —
and it costs nothing extra. `raycastTarget = false` guarantees the label never intercepts the
TableEdit removal click.

**Add one const** beside `PinIconPx` (`MapViewer.cs:72`): `private const float PinLabelFontPx = 14f;`.

#### 2K.3 Unnamed pins (resolves the card's third AT) — one rule, no branching

The single guard `if (!string.IsNullOrWhiteSpace(pin.Name))` delivers BOTH required behaviours,
because `ResolveLabel` already did the fallback upstream:

- An **unnamed marker sign** arrives with `Name` = its type label ("Mining", "Portal", …), so it
  renders that type label. (Daniel's card allows "type label **or** no label"; type label is
  chosen because it is free — already in the data — and a typed marker reads better as "Mining"
  than blank.)
- A **genuinely empty** name (a generic snapshot pin with no name) renders **no** label — no empty
  box, no stray outline. This also satisfies the §7 blank-name contract (AT-MARKER-NAME-5): a
  blank name never paints an empty label.

> **Clutter is an in-game calibration knob, NOT a v1 mechanism.** Many unnamed markers would each
> show a type label; vanilla allows pin-name overlap and does not de-clutter, and our disc is
> bounded, so v1 **matches vanilla: render every non-empty label, allow overlap.** If Daniel finds
> the type-label-on-every-unnamed-marker noisy in-game, the cheap follow-up is to gate
> *type-label-only* labels (i.e. require a custom name) behind a config flag defaulting to show —
> **flagged, not built.** Do not add label collision-avoidance / LOD for this fix.

#### 2K.4 Files touched + clean/dirty

- `src/SBPR.Trailborne/Features/Cartography/MapViewer.cs` — augment `SpawnPinMarker` (one block) +
  one new `PinLabelFontPx` const. No change to `CounterRotatePins`, `ClearPinObjects`, the pin
  collection, or the projection math. **Clean-side** (ADR-0001): our own `Text` on our own overlay,
  reusing the in-repo `VanillaUISkin.Font` + `Outline` patterns. Vanilla read only — see the cite
  below; no decompiled IronGate source copied, no third-party mod code.
- `docs/v2/planning/cartography-impl-spec.md` — this §2K + the §2H b4 correction + the §2D pointer
  (spec + code move together, AGENTS.md).

> **Vanilla reference (ADR-0001 fair-game read, NOT copied).** Vanilla renders pin names from a
> separate `TMP_Text` prefab (`Minimap.CreateMapNamePin`, decomp `:47226`) gated on map zoom
> (`pin.m_name.Length > 0 && m_largeZoom < m_showNamesZoom`, decomp `:47863`). Two deliberate
> deviations for our bounded viewer: (a) our viewer is **fixed-zoom** (AT-MAP-FIXEDZOOM), so the
> zoom gate is N/A — labels are simply always-on for named pins; (b) we use legacy
> `UnityEngine.UI.Text` (not `TMP_Text`) to match the viewer's existing title/exit-prompt text
> stack (`MapViewer.cs:1059-1094`) and the shared `VanillaUISkin.Font`, rather than introducing a
> second text pipeline. Behaviour adapted from the game we mod; implementation is our own.

#### 2K.5 Acceptance tests (named, observable — close only on Daniel's in-game check)

- **AT-PIN-LABEL-1 (card AT 1)** — a **named** pinned marker sign shows its label text positioned
  next to (just below) its icon on the held Local Map.
- **AT-PIN-LABEL-2 (card AT 2 / #2 counter-rotate)** — as the held map rotates to heading, pin
  labels stay **screen-upright and legible** (never upside-down, never mirrored), riding the same
  counter-rotation as the icons; the label stays anchored just below its icon at every heading.
- **AT-PIN-LABEL-3 (card AT 3)** — an **unnamed** marker sign falls back to its **type label**
  ("Mining"/"Portal"/"Shelter"/"Point of Interest") cleanly; a pin with a genuinely empty name
  shows **no** label and **no** empty box/outline.
- **AT-PIN-LABEL-4 (legibility)** — labels are readable over the §2E.1 composite (biome/water/
  relief) via the dark outline + parchment fill; they reuse the viewer's existing font + tint so
  they read as part of the same map UI as the title/exit prompt.
- **AT-PIN-LABEL-5 (no regression / table view)** — pin **icon** position + icon-upright behaviour
  (AT-LMAP-TC-3/-4) is unchanged; the label renders in BOTH the held map and the Surveyor's Table
  (TableEdit) view, and `raycastTarget=false` means the label never blocks the TableEdit
  left-click-remove gesture.
- logs-green ≠ playable — Daniel confirms in-game that named marker pins show their labels and stay
  readable as the map turns.

#### 2K.6 Routing

- **Clean-side → `engineer-ui`** (owns `MapViewer.cs` + the viewer cluster). This is a small,
  self-contained addition to `SpawnPinMarker` on top of §2E.1 (render) + §2H.1 (the rotating pin
  overlay it rides). Route it to the SAME worker holding the viewer cluster, **after** §2H.1 lands
  (the label rides the counter-rotation `CounterRotatePins` provides). Folded into the combined
  viewer-cluster impl child as STEP 3 (render → orientation → labels).
- **SpecCheck impact: none.** Spec + code move together in the PR.


## 3. Cartographer's Kit — Utility-slot accessory that gates auto-mapping

> **IMPL STATUS (2026-06-10, card t_65fcfe5c, engineer-systems):** built additively on
> branch `feat/cartographers-kit-t_65fcfe5c` off `integ/v2-cartography`; build 0/0
> (`TreatWarningsAsErrors` ON); SpecCheck row 3 added; code + spec + manifest + dataset
> move together (this PR). **Construction:** new `Assets.ConstructItemShell` (the ADR-0006
> item analogue of `ConstructPieceShell`) builds the networked item skeleton (ZNetView +
> ZSyncTransform + Rigidbody + collider + ItemDrop with a FRESH `SharedData`) from scratch —
> it does NOT clone a vanilla item (the pre-ADR Pigments/cairn-marker pattern). World-drop
> mesh grafted off the vanilla `LeatherScraps` blueprint. **The gate is exactly §3.2's hook:**
> a Harmony **Prefix on `Minimap.UpdateExplore(float, Player)`** returns `false` (skips the
> fog write) unless the local player wears the Kit. **Spike claim VERIFIED against the decomp:**
> `Minimap.Update` calls `UpdateExplore` *unconditionally* every frame (`:47056`), BEFORE any
> map-mode/`Game.m_noMap` check, so personal fog accumulates even under v1's server-side nomap
> config — gating `UpdateExplore` is the correct, sufficient boundary. **One detection
> deviation flagged for review:** `Player.m_utilityItem` is `protected`, so the Kit is detected
> via the PUBLIC `Inventory.GetEquippedItems()` + `m_dropPrefab` name (the same pair vanilla
> uses at `VisEquipment` wiring, `:14158`) rather than reading `m_utilityItem` — same intended
> boundary, public API. **Coupling note for the viewer card (t_cb831069):** this gate controls
> whether vanilla writes `m_explored` at all; the viewer READS that same `m_explored` window.
> One fog-write model — the Kit is the write-gate, the viewer is the reader; not forked.
> **logs-green ≠ playable — Daniel verifies AT-KIT-* in-game.**

**Lands in:** `Features/Cartography/CartographersKit.cs` (item + recipe + the gate patch).
**Card:** `t_c871efec` (engineer-systems). Smaller / lower-risk than the viewer.
**Depends on:** this spec (recipe already locked; confirm the gate hook).

### 3.1 Item + recipe (LOCKED)
- An equippable `ItemDrop` named `SBPR_CartographersKit`, **`m_shared.m_itemType =
  ItemType.Utility`** (= 18, decomp :57646) — the SAME slot as Megingjord / Wishbone,
  written to the player's dedicated `m_utilityItem` (`EquipItem` Utility branch :13983).
  Coexists with any weapon / shield / map; never a hand item.
- **Recipe (LOCKED, C11):** InkRed ×10 + InkWhite ×10 + InkBlue ×10 + InkBlack ×10 +
  FineWood ×4, amount 1, crafted at the Explorer's Bench. Reference pigments via
  `Pigments.Pigment{Red,White,Blue,Black}Name` (values are `SBPR_Ink*`).
- **NO discovery-flag system (C10).** It's a normal recipe surfaced the vanilla way
  (`IsKnownMaterial` — appears once the player has encountered its ingredients). The
  40-pigment cost IS the gate. Do **not** build any "discovered all 4 pigments" tracking.
- **Loadable icon is a HARD requirement, not cosmetic.** The Kit is the only
  additively-constructed item (`Assets.ConstructItemShell`, fresh `SharedData` → empty
  default `m_icons`). Vanilla `ItemDrop.GetIcon()` (`ItemDrop.cs:623-625`) indexes
  `m_icons[m_variant]` with no bounds guard, so an empty `m_icons` throws
  `IndexOutOfRangeException` in the crafting panel on selection and aborts the cost repaint
  (the "no cost, inherits the previous selection" symptom). `ConstructItemShell` MUST
  guarantee a non-empty `m_icons` via a shared fallback sprite (`Assets.FallbackIcon`, a
  code-generated magenta placeholder — no disk dependency) so a missing icon degrades to a
  visible placeholder, never a crash. The Kit ships `cartographers_kit_v0.1.png`; if it
  fails to load, the item shows the magenta fallback and SpecCheck logs `ICON MISSING` at
  server boot.

### 3.2 The auto-mapping gate (the whole point)
- **With the Kit in the Utility slot, walking reveals fog; without it, ZERO passive
  reveal.** Gate the vanilla walking-reveal behind "is `SBPR_CartographersKit` the player's
  equipped `m_utilityItem`?"
- **Exact hook (confirmed):** Harmony-patch **`Minimap.UpdateExplore(float dt, Player
  player)`** (decomp :48005) — this is the per-interval driver that calls
  `Explore(player.transform.position, m_exploreRadius)` (:48011) every `m_exploreInterval`.
  A **Prefix returning `false` when the local player has no Kit equipped** cleanly no-ops
  the fog write for that tick (nothing reveals) while leaving everything else untouched.
  - Prefer patching `UpdateExplore` over `Explore(Vector3,float)` (:48015): `UpdateExplore`
    is the single gated entry point; `Explore` is also reachable from
    `ExploreOthers`/shared-data merges (:48823 path) which we do NOT want to gate (reading
    a Table's shared fog must still work without the Kit). Gating `UpdateExplore` targets
    *only* the personal walking-reveal — exactly the intended boundary.
  - Guard the patch on `player == Player.m_localPlayer` and a null-safe `m_utilityItem`
    name check (or a tag component on our item) so it only affects the local walking-reveal.
- **v1 nomap interaction:** under v1 the M-key map is gone but personal fog still
  accumulates; the gate makes that accumulation Kit-dependent. Confirm in-game that with no
  Kit, walking adds nothing to the personal auto-map (and therefore nothing imprintable at
  a Table); with the Kit, it accumulates normally.

> **🔗 Coupling with issue 5 (§2I) — the Kit is the WRITE-gate; the held viewer is the READER.**
> This gate controls whether `m_explored` grows while walking. Issue 5 (§2I) is the other half:
> the held FieldReadOnly viewer must READ that growing `m_explored` (live, OR'd over the static
> imprint snapshot) so the held map visibly fills in as you travel. The Kit working (this section,
> AT-KIT-GATE) is the PRECONDITION for §2I being observable; §2I is what surfaces the Kit's effect
> on the held map. No change to this gate is needed for §2I — they share one fog-write model (the
> §3 IMPL STATUS coupling note already records this).

### 3.3 Acceptance criteria (spec §6; close only on Daniel's in-game check)
- **AT-KIT-ICON** — On a CLIENT, selecting the Kit in the Explorer's Bench renders an icon
  and its full cost panel (10× each pigment R/W/B/K + 4 FineWood) with **no exception** in
  `LogOutput.log` at selection time. Deleting the Kit's icon PNG and rebooting yields the
  magenta placeholder icon + an intact cost panel (never a panel crash) AND a
  `[Trailborne/SpecCheck] ICON MISSING` ERROR at server boot. Restoring the PNG → green.
- **AT-KIT-GATE** — Kit worn → walking reveals fog; Kit absent → walking reveals ZERO fog.
- **AT-KIT-RECIPE** — crafts from 10×(R/W/B/K) + 4 FineWood at the Explorer's Bench,
  surfaced as a normal recipe (no discovery flag).
- **AT-KIT-COEXIST** — sits in the Utility slot alongside weapon / shield / Local Map with
  no slot collision.
- SpecCheck row 3 present; `[hold]` PR; logs-green ≠ playable.

---

## 3.5 NoMap enforcement — the mod disables the global map by default (the tier's enforced precondition)

> **Status: NEW FEATURE + premise correction (card t_8c9abf6f, architect spec-pass 2026-06-11).**
> This is the precondition the entire cartography tier was built assuming but **nothing
> enforced**. The tier's whole premise — *no global map → earn bounded local maps* — is only
> true if `Game.m_noMap` is actually on. On a fresh/local world it is NOT (the key isn't set
> until a host runs `nomap` by hand), so the forked viewer competes with a free full-world
> map. **This feature makes the mod own its own premise: it sets `GlobalKeys.NoMap`
> server-side by default, built as a LIFTABLE gate** so a future Mistlands advancement can
> re-enable the global map. Clean-side (ADR-0001): setting a vanilla global key via
> `ZoneSystem` is base-game; no third-party mod code.

> **Daniel's framing correction (2026-06-11, on the card):** the original report said "the
> global map works on Niflheim." That was a misread — Daniel was playtesting on a **local
> world**, not Niflheim; Niflheim itself already has NoMap set. This *strengthens* the
> feature: the local-world case IS the evidence for mod-owned enforcement. A per-world,
> set-by-hand premise is exactly the silent fragility this removes. **The lesson:** an
> unenforced premise (the stale "hardcore = no map" belief) silently shipped false for the
> whole tier — never again leave the tier's precondition to a server-config assumption.

**Lands in:** `Features/Cartography/NoMapEnforcer.cs` (a new server-side Harmony patch class) +
its `PatchCheck`-visible registration in `Plugin.cs`. **No new prefab, no item, no recipe.**
**Card:** route the impl to `engineer-systems` (server-side global-key code; smaller/lower-risk
than the viewer). **SpecCheck impact: NONE** — this is global-key behaviour, not a recipe row.

### 3.5.0 The mechanism (re-verified against the decomp — `assembly_valheim.decompiled.cs`)

> ⚠️ **Re-grounding note for the implementer.** Every line number below was re-checked
> against the local decomp on 2026-06-11 (the card's cited `:96455` etc. are from an
> older dump; the *behaviour* matches but the *line numbers* differ — verify names against
> `assembly_valheim.dll` metadata, never trust a line number cold). Six facts the original
> card framing did NOT surface but that **decide the hook design** are called out as ⭐.

`Game.m_noMap` is driven SOLELY by the `GlobalKeys.NoMap` global key (plus a per-player
client pref, irrelevant to us):

- `Game.UpdateNoMap()` (`:85133`): `m_noMap = (ZoneSystem.instance &&
  ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoMap)) || (per-player "mapenabled_<name>"
  pref == 0)`, then `Minimap.instance.SetMapMode(m_noMap ? MapMode.None : MapMode.Small)`.
  So **set the key → `m_noMap` true → the global map UI is forced off.** ✅ as the card says.
- `GlobalKeys` enum (`:85203`): `NoMap` is index **26**; `NonServerOption` is index **32**.
  This ordering is load-bearing for persistence (see ⭐3).

⭐**1 — `SetGlobalKey` is a ROUTED RPC, not a direct write.**
`ZoneSystem.SetGlobalKey(GlobalKeys)` → `SetGlobalKey(string)` (`:98480`) →
`ZRoutedRpc.instance.InvokeRoutedRPC("SetGlobalKey", name)`. The no-target overload
(`:70673`) routes to `GetServerPeerID()` — i.e. **the call is always sent to the server**,
from wherever it's invoked. The actual mutation happens in the server-side handler
`RPC_SetGlobalKey(sender, name)` (`:98539`): if the key isn't already present it calls
`GlobalKeyAdd(name)` then `SendGlobalKeys(ZRoutedRpc.Everybody)`. **Consequence:** calling
`SetGlobalKey` is correct and idempotent (the handler's own `!Contains` guard makes a repeat
a no-op + no re-broadcast), but it is *asynchronous* — it does not mutate state inline; it
posts an RPC the server processes. Do not assume `GetGlobalKey(NoMap)` flips true on the next
line. This is why the enforcement hook must run **server-side** (so the RPC is local) and must
be **idempotent on every world-load**, not a one-shot fire-and-forget.

⭐**2 — the RPC handler is registered SERVER-ONLY.**
`ZoneSystem.Start()` (`:96426`) registers `RPC_SetGlobalKey` / `RPC_RemoveGlobalKey` **only
inside `if (ZNet.instance.IsServer())`** (`:96434`). A pure client never handles these. So
enforcement MUST be a server-side action. (A connected client *can* call `SetGlobalKey` — it
routes to the server — but our design sets it server-authoritatively at world load, exactly
like the `nomap` console command does under its own `ZNet.instance.IsServer()` guard,
`:37350`.)

⭐**3 — NoMap PERSISTS automatically (two independent save paths), because idx 26 <
`NonServerOption` (32).**
- `GlobalKeyAdd` (`:96472`) adds NoMap to `ZNet.World.m_startingGlobalKeys` when the key's
  enum `< NonServerOption` (`:96477`, `:96495-96498`). `m_startingGlobalKeys` is serialized to
  the world **`.fwl` meta** (`World.SaveWorldMetaData`, `:95780-95784`).
- `ZoneSystem.SaveASync` (`:96703`) also writes the live `m_globalKeys` set to the world
  **`.db`**, filtering OUT keys with enum `< NonServerOption` (`:96713-96717`) — i.e. the
  `.db` path saves the boss/event keys, the `.fwl` path saves the world-modifier keys.
  **NoMap (idx 26 < 32) rides the `.fwl`/`m_startingGlobalKeys` path.** Either way, once set,
  vanilla restores it on the next boot with no action from us → **AT-NOMAP-3 holds by
  construction.** The implication for our hook: we are *enforcing an invariant*, not
  *persisting state* — vanilla persists it; we just guarantee it's present.

⭐**4 — a freshly-joined client inherits the state with ZERO client-side mod action.**
On the server, `ZoneSystem.OnNewPeer(peerID)` (`:96593`) calls `SendGlobalKeys(peerID)` for
every connecting peer (`:96595-96599`). The client's `RPC_GlobalKeys` handler (`:96462`)
clears + rebuilds its key set from the server's, then (via `GlobalKeyAdd` → `UpdateWorldRates`
→ `UpdateNoMap`) flips `m_noMap` and forces `SetMapMode(None)`. **So a server-set NoMap takes
effect on all clients automatically — the cartography fork needs no client-side enforcement.**
This is why the feature is purely server-side. → **AT-NOMAP-2 holds by construction.**

⭐**5 — liftability is FREE and symmetric, BUT a custom-named latch key would NOT persist.**
`RemoveGlobalKey(NoMap)` (`:98548`) routes the same way → server `RPC_RemoveGlobalKey`
(`:98558`) → `GlobalKeyRemove` + `SendGlobalKeys(Everybody)` → every client re-runs
`UpdateNoMap` and the global map comes back. So the future Mistlands trigger is a single
`RemoveGlobalKey(GlobalKeys.NoMap)` server-side call — a clean flip, no code rip-out. **But
note for the gate design:** `GetKeyValue` (`:96544`) resolves any name that is NOT a member of
the `GlobalKeys` enum to `gk = NonServerOption` (`:96558-96560`), and `GlobalKeyAdd` then does
NOT add it to `m_globalKeysEnums` and does NOT persist it to `m_startingGlobalKeys` (the
`< NonServerOption` guard fails). **Therefore a custom `"SBPR_MistlandsReached"` global key is
the WRONG durable latch** — it wouldn't be queryable via `GetGlobalKey(GlobalKeys)` and
wouldn't survive a restart. The liftability signal must be either (a) a real vanilla
`GlobalKeys` enum member, or (b) NoMap's own presence/absence (see §3.5.2).

⭐**6 — the `WorldSetup` WIPE hazard (the hook-timing trap).**
`ZNet.LoadWorld()` ends by calling `WorldSetup()` (`:68198`, `:68222`) →
`ZoneSystem.SetStartingGlobalKeys()` (`:98441`), which **clears all 32 world-modifier keys and
re-adds only the persisted `m_startingGlobalKeys`** (`:98443-98467`). If our enforcement runs
*before* `WorldSetup`, a `SetGlobalKey(NoMap)` RPC could be processed and then wiped by the
rebuild on the very first boot of a world that didn't already have it. **Therefore the
enforcement hook must fire AFTER `WorldSetup` has run** — i.e. after the existing v1
`LegacyTerrainOpZdoCleanup` postfix point (which is a `[HarmonyPostfix]` on `ZNet.LoadWorld`,
already proven server-only because `LoadWorld` is reached only from `ServerLoadWorld` under
`if (m_isServer)`, `:66811-66823`). Because NoMap then lands in `m_startingGlobalKeys` and is
re-applied by every subsequent `SetStartingGlobalKeys`, only the FIRST boot needs the nudge;
later boots find it already set and our idempotent guard no-ops.

### 3.5.1 The hook (RESOLVES open question 1: cleanest server-side enforce-on-load point)

**Postfix on `ZNet.LoadWorld`** (the SAME vanilla method `LegacyTerrainOpZdoCleanup` already
postfixes — a proven server-only, once-per-boot, post-`WorldSetup` seam). Do NOT use
`ZoneSystem.Start` — that runs before the world DB / starting-keys are loaded and before the
server RPC handlers may be wired, and it would race ⭐6's wipe.

```
[HarmonyPatch(typeof(ZNet), "LoadWorld")]
public static class NoMapEnforcer
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]   // after vanilla body + WorldSetup; order-independent vs the legacy-ZDO sweep
    public static void Postfix()
    {
        try
        {
            if (!ServerContext.OnSBServer) return;            // mod's own gate (see Registrar pattern)
            var zs = ZoneSystem.instance;
            if (zs == null) return;                            // defensive; LoadWorld implies it exists
            if (!ShouldEnforceNoMap(zs)) return;               // the LIFTABLE gate (§3.5.2)
            if (zs.GetGlobalKey(GlobalKeys.NoMap)) {           // already set → idempotent no-op, no re-broadcast
                Plugin.Log.LogInfo("[Trailborne/NoMap] NoMap already set; mod holds the global-map disable.");
                return;
            }
            zs.SetGlobalKey(GlobalKeys.NoMap);                 // routed RPC → server handler → SendGlobalKeys(Everybody)
            Plugin.Log.LogWarning("[Trailborne/NoMap] Global map DISABLED by default "
                + "(set GlobalKeys.NoMap server-side). Liftable at the Mistlands tier advancement (future). "
                + "The cartography tier is now the only map. (card t_8c9abf6f)");
        }
        catch (System.Exception e)
        {
            // Fail LOUD but non-fatal: a thrown enforcer must not take down world load.
            Plugin.Log.LogError($"[Trailborne/NoMap] enforce-on-load threw (non-fatal): {e}");
        }
    }
}
```

- **Server-only by construction** (`LoadWorld` only runs server-side) AND explicitly gated on
  `ServerContext.OnSBServer` — same belt-and-braces as every other SBPR registration.
- **Idempotent** — the `GetGlobalKey(NoMap)` check + the handler's own `!Contains` guard mean a
  repeat boot, a second LoadWorld, or a hand-set `nomap` all collapse to a no-op.
- **The loud boot-log line is mandatory** (RESOLVES open question 3's "honesty" half): the
  lesson of this bug is that a silent, unenforced premise shipped false. The mod must SAY, at
  every boot, that it holds NoMap — so the state is never again a silent assumption. Grep
  target: `[Trailborne/NoMap]`.

### 3.5.2 The LIFTABLE gate (RESOLVES open question 2: the Mistlands signal)

The disable MUST be conditional so a future Mistlands advancement can lift it cleanly. Build
the gate **now**; the Mistlands *trigger* is out of scope (future card). Concretely, factor the
condition behind one method:

```
// Returns false once the world has advanced past the point where the global map is re-granted.
// TODAY this is always true (Mistlands re-enable is future scope); the SEAM is what this card ships.
internal static bool ShouldEnforceNoMap(ZoneSystem zs)
{
    // FUTURE (Mistlands tier card): return false when the Mistlands-reached signal is present,
    // e.g.  if (zs.GetGlobalKey(GlobalKeys.NoMap_LiftedByMistlands_or_a_real_progression_key)) return false;
    // The lift itself is then a single server-side RemoveGlobalKey(GlobalKeys.NoMap) at the
    // advancement, and this guard stops re-asserting it on the next boot.
    return true;
}
```

**The latch signal — architect's recommendation (the Mistlands TRIGGER card finalizes it):**
do NOT invent a custom-named global key for "Mistlands reached" — per ⭐5 a non-enum key is
neither enum-queryable nor persisted, so it can't be a durable latch. Two grounded options the
future card chooses between:

1. **NoMap's own absence as the latch (simplest, recommended).** When the Mistlands trigger
   fires it calls `RemoveGlobalKey(GlobalKeys.NoMap)` once. Because the removal persists
   (it drops out of `m_startingGlobalKeys`), the key stays absent across restarts. The gate
   becomes: *"if a player/world has reached Mistlands, don't re-assert."* The cleanest read of
   "reached Mistlands" that is **server-side and persisted** is a real vanilla progression
   key — see option 2 — OR a small SBPR ZDO/world-data flag the Mistlands card owns. Until that
   card exists, `ShouldEnforceNoMap` returns true and re-asserts NoMap every boot, which is the
   safe default (the map stays disabled).
2. **Tie to a real vanilla `GlobalKeys` progression member** if one cleanly denotes Mistlands
   entry. The enum (`:85203`) carries boss-defeat keys (`defeated_*`) but NOT a "reached
   biome" key, so there is no perfect vanilla "entered Mistlands" global key — the Mistlands
   card will most likely mint its own persisted SBPR world flag. **This is explicitly the
   future card's call;** THIS card only ships the `ShouldEnforceNoMap` seam + the
   always-enforce default so lifting later is a one-method flip, not a code rip-out.

> **Architect note routed to the future Mistlands card:** the cleanest progression signal is
> NOT a global key at all — it's whatever durable per-world state the Mistlands-advancement
> feature already needs. File the Mistlands re-enable as: (a) detect the advancement, (b)
> `RemoveGlobalKey(GlobalKeys.NoMap)` once, (c) flip `ShouldEnforceNoMap` to read that same
> advancement flag. Do not hardcode an unconditional permanent NoMap, and do not mint a custom
> *global key* as the latch (⭐5).

### 3.5.3 Config posture (RESOLVES open question 3: escape hatch)

Daniel's directive is "this mod should just disable it" → **default ON, enforced.** Add ONE
optional BepInEx config escape hatch, defaulting to enforced, so a future server operator (or a
debug session) can opt out without a recompile — mirroring the existing `Plugin.cs` config
pattern (`Config.Bind`):

- `Config.Bind("Cartography", "SBPR_EnforceNoMap", true, "When true (default), the mod disables
  the vanilla global map by setting GlobalKeys.NoMap server-side at world load — the cartography
  tier's enforced precondition. Set false ONLY to let the vanilla global map coexist (debug /
  non-cartography server). The Mistlands tier advancement lifts NoMap independently of this
  flag.")`.
- `ShouldEnforceNoMap` checks this flag first: `if (!Plugin.EnforceNoMap.Value) return false;`.
- **The loud boot-log line fires either way** — if the flag is false, log that the mod is
  *deliberately NOT* holding NoMap (so a "why does the map work?" question is answered in the
  log, not re-debugged in-game). This is the honesty rule: the state is never silent.

### 3.5.4 Scope discipline (no over-reach)

- The hook sets **ONLY** `GlobalKeys.NoMap`. It must NOT touch any other global key, world
  modifier, or the hardcore death-penalty/combat keys. (`SetGlobalKey(GlobalKeys.NoMap)` is a
  single-key add; the handler's `GlobalKeyAdd` only mutates that one key.) → **AT-NOMAP-6.**
- It does NOT remove or alter a NoMap the operator set by hand — if it's already there, no-op.
- It does NOT touch the per-player client `nomap` pref (`mapenabled_<name>`); that orthogonal
  toggle is out of scope (card "Scope/out").
- The Local Map M-key collision card (`t_91182d97`) is made moot by this (no global map to
  collide with), but per the decision pinned there it is STILL implemented defensively — the
  forked viewer opens on its own input regardless of whether the global map exists. This card
  does not change that.

### 3.5.5 Acceptance criteria (named, observable — close only on Daniel's in-game check)

- **AT-NOMAP-1** — On a world with the mod (fresh/local world included), the vanilla global map
  (M) is disabled **by default** — `Game.m_noMap` is true, pressing M opens no global map UI.
  No host has to run `nomap` by hand.
- **AT-NOMAP-2** — A freshly-joined client inherits the disabled state automatically (the server
  pushes the key on connect via `SendGlobalKeys`); no client-side mod action and no client
  config needed.
- **AT-NOMAP-3** — The state persists across a dedicated-server restart (NoMap rides
  `m_startingGlobalKeys` → the world `.fwl`); the mod re-asserts idempotently on every boot
  regardless.
- **AT-NOMAP-4 (liftable)** — The disable is gated behind `ShouldEnforceNoMap` so a future
  Mistlands advancement can `RemoveGlobalKey(GlobalKeys.NoMap)` and restore the global map.
  Verified now by a manual/test toggle of the gate condition (or the `SBPR_EnforceNoMap=false`
  config) restoring the map. **The Mistlands trigger itself is future scope; the LIFTABILITY
  seam must exist now.**
- **AT-NOMAP-5 (no regression)** — The cartography tier (Surveyor's Table / Local Map viewer /
  Cartographer's Kit) still works with NoMap enforced — it was built for exactly this state;
  confirm no regression now that the premise is actually true (the viewer still opens, the
  bounded disc still renders, the Kit still gates fog).
- **AT-NOMAP-6 (no over-reach)** — The mod sets ONLY the NoMap key; the hardcore death-penalty
  and every other world modifier / global key are unchanged (diff the global-key set before/after
  boot — only NoMap is added).
- **AT-NOMAP-BOOTLOG** — Server boot logs a single, greppable `[Trailborne/NoMap]` line stating
  the mod set or already-holds NoMap (or, with the config off, that it is deliberately NOT
  holding it). The premise is never silent again.
- logs-green ≠ playable: Daniel confirms in-game that M opens nothing and the cartography tier is
  the only map.

### 3.5.6 PatchCheck + boot wiring

- Register `NoMapEnforcer` in `Plugin.Awake()` via `harmony.PatchAll(typeof(NoMapEnforcer))`
  **alongside the other cartography patches** (next to `CartographersKit.UpdateExploreGate`).
  The `PatchCheck` watchdog (`Runtime/PatchCheck.cs`) will scream at boot if it's attributed but
  unregistered — so adding the `[HarmonyPatch]` class without the `PatchAll` line fails loudly
  (this is the exact dead-patch class the watchdog exists to catch). The `✓ All N patch classes
  registered` count rises by 1.
- **SpecCheck: untouched** — no recipe/piece row. Do not bump the recipe manifest count.

---

## 4. Build order, cross-feature seams, and the shared format

- **Build order (lowest → highest risk):** Cartographer's Kit (§3, smallest — one gate
  patch + a Utility item) → Surveyor's Table (§1, re-gated `MapTable` loop on public APIs)
  → Local Map item + equip (§2A) → **the forked viewer (§2B), the one high-risk item**, and
  it is gated on the spike. The three impl cards are children of THIS spec + the spike.
- **The shared windowed-fog blob (§2C) is the seam** between all three: the Kit writes the
  player's native fog, the Table stores a windowed merge of it, the Local Map snapshots the
  Table's window, and the viewer renders that window. One format, defined once in
  `Features/Cartography/` — agree on it before §1 and §2 diverge.
- **The viewer (§2B / `MapViewer.cs`) is shared** by the Local Map (read-only field mode)
  and the Surveyor's Table (pin-removal Table mode). Build it once with a mode flag; don't
  fork two viewers.
- **All clean-side / ADR-0006:** additive construction, vanilla read as blueprint, reference
  mods studied not copied, no decompiled IronGate source committed.
- **Spec-first:** each impl card moves its `requirements.md` cross-check + its `SpecCheck.cs`
  manifest row + its code in the same PR. A card is done when code, spec, and the SpecCheck
  manifest agree — and (logs-green ≠ playable) only when Daniel verifies it in-game.

## 5. Naming reference (prefab / id strings — agree before building)
| Thing | Prefab / id (proposed; confirm at build) | Type |
|---|---|---|
| Surveyor's Table | `piece_sbpr_surveyors_table` | build piece |
| Local Map | `SBPR_LocalMap` | `ItemDrop`, `TwoHandedWeapon` |
| Cartographer's Kit | `SBPR_CartographersKit` | `ItemDrop`, `Utility` |
| Pigments (ingredient) | `SBPR_Ink{Red,White,Blue,Black}` (existing) | `ItemDrop` |

> Prefab-name strings are save/wire contracts the moment a piece/item is placed or
> crafted in a live world. Lock these three names in the first impl PR that registers each,
> and never rename them after (renaming orphans every placed/crafted instance — the same
> reason `Pigments` kept `SBPR_Ink*`).
