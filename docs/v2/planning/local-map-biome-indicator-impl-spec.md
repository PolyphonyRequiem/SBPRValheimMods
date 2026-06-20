---
title: "Biome NAME indicator on both cartography surfaces (minimap disc + local-map modal) — buildable implementation spec"
status: current
purpose: "Build-ready architect spec adding a vanilla-style biome-NAME readout (Path A, Daniel 2026-06-19) to BOTH SBPR cartography surfaces — the carry minimap disc and the full-screen local-map modal. The disc shows the player's current-biome name as a third line in the existing under-disc caption stack (map-name / biome / [M]-hint); the modal shows a fixed current-biome readout under its title cartouche. ONE MapSurface-level implementation feeds both surfaces. Reads vanilla Player.GetCurrentBiome() (already proven in-codebase at SunstoneLens.cs:351) + the vanilla $biome_* localization tokens — zero SurveyData wire change, SpecCheck +0, no new Harmony patch, no vanilla root/material mutation (nomap stays enforced). Net-new addendum to the locked map-provider-model.md §2 minimap model; extends the caption infrastructure landed in PR #205 (t_26bba85b). Authored by the architect spec-pass (card t_caf0f1cf). Daniel gates the merge."
owner: Daniel (design authority); architect (spec capture + grounding)
supersedes_partial: []
---

# Biome NAME indicator on both cartography surfaces — buildable implementation spec

The SBPR cartography surfaces render vanilla's biome **colouring** (inherited from the
cloned map material's `_MainTex`, `MapSurface.TryRenderVanillaShader`) but show **no
biome NAME** of any kind. SBPR's viewer is a standalone uGUI overlay (`MapSurface`), not
vanilla's `m_smallRoot`/`m_largeRoot`, so vanilla's own `m_biomeNameSmall`/
`m_biomeNameLarge` readouts never appear on it (nomap-ON also forces vanilla's roots off).
Daniel, 2026-06-19, v0.2.27 playtest (Niflheim #bugs): *"Minimap and local map need to
have support for biome indicators by the way."*

Daniel's A/B/C path call (2026-06-19): **Path A — the vanilla biome-NAME readout** (NOT
B colour, NOT C legend), on BOTH surfaces, **architect owns the layout**. This doc is the
buildable *how*: add the biome name as a new line on each surface, driven from the same
provider/player state that already drives the surfaces, landing the design-doc addendum +
acceptance tests in the SAME PR (AGENTS.md spec+code rule). An `engineer-ui` implementer
should build the whole change from §3–§6 without re-deriving anything.

> **Why this card exists (enhancement, not defect).** The biome-name affordance is the
> exact vanilla map feature the SBPR standalone overlay drops. This is a net-new addendum
> to the locked `map-provider-model.md` §2 minimap model, which specified the disc + (in
> §2.1) the under-disc name/hint caption but said nothing about a biome readout.

> **Clean-side note (ADR-0001).** This touches ONLY SBPR's own overlay UI
> (`MapSurface.cs` caption + a new modal biome label) and reads base-game
> `Player.GetCurrentBiome()` + vanilla `Localization` `$biome_*` tokens — reading and
> adapting the game we're modding is allowed (AGENTS.md). No other mod's code is read or
> copied; **no vanilla material/root is mutated** (we do NOT drive vanilla's
> `m_biomeName*` or touch `Minimap` — we read `GetCurrentBiome()` and paint our own
> `Text`). Pure HUD presentation.

---

## 0. SpecCheck manifest impact (read first)

**None.** This feature adds a text line to two existing overlay surfaces — no item recipe,
no build piece, no station. `Runtime/SpecCheck.cs` is **untouched** (+0 rows). No new
Harmony patch, so `PatchCheck` is unaffected (+0). **No `SurveyData` wire change** — the
biome name is computed live from `Player.GetCurrentBiome()` at render time, never baked
into the survey artifact (that would be Path B-baked, explicitly NOT chosen — §1). Spec-first
still applies, split the same way the name+hint caption was (spec PR #202 → impl PR #205):
**this spec-pass PR** lands this doc + the `map-provider-model.md` §2.2 addendum + the
`cartography-impl-spec.md` §2H.1 AT cluster (docs-only); the **engineer-ui impl PR** lands
`MapSurface.cs` + the playtest-ledger row (§7). Both Daniel-gated.

---

## 1. What is DECIDED vs what THIS card specs

**🟢 DECIDED (Daniel 2026-06-19 — build exactly this):**
- **Path A** — the biome's **NAME** as text on BOTH surfaces. NOT Path B (more-legible
  colour / discrete fills) and NOT Path C (colour→name legend panel). Daniel's explicit
  one-word call: *"A and architect."*
- The **layout is the architect's** to propose; Daniel's in-game eyeball is the final
  accept on placement (he delegated the pixel layout: *"architect owns it"*).

**This card SPECS (the HOW + the layout calls Daniel delegated):**
- **Disc:** the current-biome name is a **third line in the existing under-disc caption
  stack** introduced by PR #205 — `_discCaption` becomes **name / biome / [M]-hint** (§3.1).
  One coherent text stack, not a fourth label fighting for the same pixels.
- **Modal:** a **fixed current-biome readout** under the modal's title cartouche (§3.2) —
  NOT cursor-hover (that needs new input plumbing the passive modal lacks; deferred, §3.2.1).
- The **data source** — `Player.GetCurrentBiome()` → vanilla `$biome_<name>` token,
  current-biome (player position) for BOTH surfaces (§3.3).
- The **shared mechanism** — ONE biome-name composition in `MapSurface`, fed to both the
  disc caption and the modal label, so there is no divergent second path (§3.4, AT-BIOME-SHARED).
- The **`Biome.None` / unlocalized guard** so a junk `$biome_none` literal never leaks (§3.5).

**Out of scope (do NOT build here):**
- The biome **colour** render (already shipped, PR #192) — untouched unless a future
  Path-B card asks for more-legible fills.
- The disc player-marker chevron (`t_efe8b32b`, shipped) and the name+[M]-hint caption
  (`t_26bba85b` / PR #205, **merged** — this card extends it).
- **Cursor-hover** biome inspection on the modal (vanilla large-map behaviour) — deferred
  follow-up, needs modal input plumbing (§3.2.1). The fixed readout is the shipped default.
- Any `SurveyData` wire-format change, any survey-baked biome layer, any legend panel.

---

## 2. Grounding — the code facts this builds against (origin/main @ dcd2181, post-PR #205)

All file:line anchors are against `origin/main` tip `dcd2181` (PR #205 merged — the
caption infrastructure this card extends is now live on main).

**2.1 The biome data source is already proven in this codebase.**
- `Player.GetCurrentBiome()` returns `Heightmap.Biome` (a single sampled value — vanilla
  decomp `:17190` returns the cached `m_currentBiome` field, NOT a flags-OR). SBPR already
  calls it: `Features/Sunstone/SunstoneLens.cs:351`
  (`if (player.GetCurrentBiome() == Heightmap.Biome.Swamp) return false;`). So the call is
  verified working in our assembly — no new reflection, no new patch.
- **Vanilla's exact token construction** (decomp, base game — fair to read+adapt):
  `Localization.instance.Localize("$biome_" + biome.ToString().ToLower())`
  (`Minimap.UpdateBiome` `:48734`/`:48747`, and the minimap small-path `:46888`). We use
  the **identical** construction — current-biome (player position) for both surfaces.
- **Why current-biome (player pos), not cursor-hover, for both:** vanilla's minimap uses
  `player.GetCurrentBiome()` (current biome, pulse-on-change, `:48743-48750`); vanilla's
  large map uses `ScreenToWorldPoint(cursor) → WorldGenerator.GetBiome()` gated on
  `IsExplored` (`:48729-48740`). The SBPR disc is the minimap analogue; the SBPR modal is a
  **passive read-only** view (no cursor sampling, §2.4), so both take the cheap, gate-free
  current-biome path. Cursor-hover on the modal is the deferred follow-up (§3.2.1).

**2.2 The disc caption is already built and merged — biome is one more line.**
- `MapSurface.cs` (post-#205): `_discCaption` is a **single multi-line rich-text `Text`**
  on the **non-rotating `_frame`** (`BuildCaption` `:1318`), composed in `UpdateCaption`
  (`:982`) from `_req.Caption` (the per-provider NAME line, FormatDisplayName) above the
  static localized hint line (`CaptionHintRaw = "[<color=yellow><b>$KEY_Map</b></color>]
  $piece_readmap"`). Two font sizes via `<size>` tags: `CaptionNameFontPx = 18`,
  `CaptionHintFontPx = 16`, `CaptionGapPx = 10`.
- `UpdateCaption` runs every `Render()` (`:222`), which fires on every disc bind/refresh
  (0.25 s `PollSeconds` poll via `DriveMinimapDisc → BindMinimap`). A `_captionLastText`
  guard skips the redundant `Text.text` set on unchanged re-binds. **A biome line that
  changes as the player walks will repaint correctly through this same guard** — the text
  changes, so the guard lets it through; identical re-binds still skip.
- The caption is screen-stable because `_frame` is the non-rotating host the fixed bezel
  rides; only `_mapContainer` rotates (AT-CAPTION-NO-ROTATE, PR #205). The biome line
  inherits this for free — it is part of the same `_discCaption` `Text`.

**2.3 The name flows through `MapViewRequest.Caption`; biome needs its own channel.**
- `MapViewRequest.Caption` (string?, disc-only, `CartographyViewer.cs:88`) carries the
  FORMATTED name; `BindMinimap` force-clears `Title` but passes `Caption` through untouched
  (`MapViewer.cs:104-108`). `DriveMinimapDisc` (`LocalMapController.cs:178`) sets `Caption =
  FormatDisplayName(name)`.
- **The biome line is NOT a `MapViewRequest` field.** Unlike the name (which is
  provider-state the controller owns and pushes), the current biome is **live per-frame
  player state** that the surface can read itself. Pushing it through `MapViewRequest` would
  mean re-binding the disc every time the biome changes (coupling the controller to player
  position). Instead, `MapSurface.UpdateCaption` reads `Player.m_localPlayer.GetCurrentBiome()`
  **directly at compose time** (§3.1) — the same place it already re-localizes the hint
  line every Render. One read, no new request plumbing, no controller change for the disc.

**2.4 The modal has a title cartouche but is otherwise passive.**
- The modal `MapSurface` (`ShowPrompts=true`) has `_titleLabel` — a top-centre BARE-name
  cartouche (`BuildPrompts` `:1275`, `fontSize=34`, anchored `(0.5,1)` pivot top,
  `anchoredPosition=(0,-40)`), set in `UpdateTitle` (`:960`) from `_req.Title`. It also has
  `_exitPrompt` (bottom-centre, `:1257`).
- The modal refreshes live while open: `LocalMapController.RefreshOpenView` (`:323`) pushes
  a fresh `MapViewRequest` every `Update()` tick while a field map is equipped + open, so a
  fixed biome readout on the modal updates as the player walks (same cadence as the title).
- The modal is **input-passive** in `FieldReadOnly`: its `HandleInput`/tick processes only
  Esc-close; the cursor→world inverse-transform fires ONLY in `TableEdit` (pin removal).
  There is **no per-frame cursor sampling** on the modal — which is why cursor-hover biome
  (vanilla large-map behaviour) is a real new build, not a free ride (§3.2.1).

---

## 3. The mechanism — one biome-name composition, fed to both surfaces

The shared `MapSurface` is the single home for the biome-name logic. A new private helper
`CurrentBiomeNameOrNull()` returns the localized current-biome name (or null when it can't
be resolved). Both surfaces call it from their existing per-Render update path:
- the **disc** appends the biome line into `_discCaption` (the §2.2 caption stack);
- the **modal** writes it into a new `_biomeLabel` under the title cartouche.

This is the AT-BIOME-SHARED guarantee: one method computes the name; there is no second,
divergent biome path.

### 3.1 Disc — biome as the middle line of the caption stack

The disc caption stack becomes three rows (top → bottom):

```
Local map for Northern Outpost   ← name  (§2.2, _req.Caption, 18 px)
Meadows                          ← biome (NEW, this card,      ~16 px)
[M] Read map                     ← hint  (§2.2, static,        16 px)
```

- **Where:** extend `MapSurface.UpdateCaption` (`:982`) to insert the biome line between the
  name line and the hint line in the composed rich-text string. It is the **same
  `_discCaption` `Text`** — no new GameObject, no new anchor math, inherits screen-stability
  and the `_captionLastText` guard for free.
- **Layout order (Daniel-delegated):** name on top (what map), biome in the middle (where
  you are), `[M]` hint on the bottom (how to open). Rationale: the two **identity** lines
  (map name + current biome) read together as "this map, here," with the **action** hint
  anchored last. The biome line uses the hint font size (16 px) so the name line stays the
  visual anchor; biome + hint form a tight lower pair under the name. This is a
  build-calibration choice — Daniel's eyeball tunes order/size; the engineer exposes the
  biome size as a constant beside `CaptionNameFontPx`/`CaptionHintFontPx` (e.g.
  `CaptionBiomeFontPx = 16`) so a flip is one line.
- **Missing biome (null from §3.5):** omit the biome line entirely — the stack falls back to
  name / hint (exactly today's two-line caption). Never render an empty or `$biome_none`
  line.
- **Missing name (existing §3.4 of the name spec):** name line already omitted; with a biome
  the stack reads biome / hint; without, hint only. All combinations degrade cleanly because
  each line is conditionally concatenated.
- **Visibility:** rides the disc caption's existing gate (provider bound + nomap-ON +
  imprinted). No new visibility code — the biome line lives inside `_discCaption`.

### 3.2 Modal — a fixed current-biome readout under the title

The modal shows the player's **current biome** as a fixed label, NOT a cursor-hover readout
(§3.2.1 explains why, and what the deferred hover variant would cost).

- **Where:** a new `_biomeLabel` `Text` built in `BuildPrompts` (`:1251`, the modal-only
  `ShowPrompts` path that builds `_titleLabel`/`_exitPrompt`), parented to the same
  non-rotating `_root` as the title. Anchored top-centre, directly **under** the title
  cartouche: same `(0.5,1)` anchor/pivot as `_titleLabel`, `anchoredPosition` ≈ `(0, -40 -
  titleHeight - gap)` (just below the title's baseline — exact offset is a calibration knob;
  start one title-line down, ~`(0, -84)`). Smaller than the title (`fontSize` ~22 vs the
  title's 34) so the biome reads as a subtitle, not a competing headline.
- **Content:** set in `UpdateTitle` (`:960`, the modal's per-Render text refresh) or a
  sibling `UpdateBiomeLabel()` called alongside it from `Render()`. Text =
  `CurrentBiomeNameOrNull()` (§3.5); `SetActive(false)` when null so a pre-spawn / no-player
  frame shows nothing rather than an empty bar.
- **Why under the title, not a corner:** the modal title is the map's identity ("Local map
  for X"); the biome is "where you are on it." Stacking biome under title keeps one centred
  identity column, mirroring the disc's name-over-biome order (consistent reading on both
  surfaces — AT-BIOME-SHARED is also a *visual* consistency, not just code-sharing).
- **Live update:** rides `RefreshOpenView`'s per-tick refresh (§2.4) — the readout tracks the
  player's biome as they move while the modal is open. Same cadence as the title.

#### 3.2.1 Deferred: cursor-hover biome on the modal (vanilla large-map behaviour)

Vanilla's *large map* shows the biome **under the cursor** (`ScreenToWorldPoint(mouse) →
WorldGenerator.GetBiome`, gated on `IsExplored`). Reproducing that on the SBPR modal is a
**separate, larger build** and is explicitly NOT in this card:
- The modal is input-passive (§2.4) — it has no per-frame cursor→world sampling outside
  `TableEdit`. Hover-biome needs new input plumbing on a table-centred, rotating,
  bezel-clipped surface (the inverse-transform exists for pin-clicks but is only wired in the
  editable mode).
- It also reopens the **explored-gate question** (vanilla gates hover-biome on `IsExplored`;
  SBPR would gate on its own shroud to preserve no-map friction) — a Daniel design call that
  the fixed readout sidesteps entirely (you're always in/explored at your own position).

**This is a reversible default, not a silent down-scope.** If Daniel, after feeling the fixed
readout in-game, wants vanilla-style hover-inspect of *remote* biomes on the modal, that is a
follow-up card (modal cursor-sampling + shroud-gate decision), flagged here so the scope
erosion is explicit, not hidden.

### 3.3 The data source — current biome, both surfaces

Both surfaces read the **player's current biome** (not a cursor/world lookup):

```csharp
var player = Player.m_localPlayer;
Heightmap.Biome biome = player != null ? player.GetCurrentBiome() : Heightmap.Biome.None;
```

- `Player.GetCurrentBiome()` is the proven call (§2.1, `SunstoneLens.cs:351`). It returns the
  cached single-biome value — safe to `.ToString()`.
- **Do NOT** call `WorldGenerator.instance.GetBiome(worldPos)` for the fixed readouts — that's
  the cursor/large-map path, only needed for the deferred hover variant (§3.2.1).
- No `Minimap` access, no vanilla `m_biomeName*` mutation — we read player state and paint our
  own `Text`. Clean-side (§ clean note), nomap stays enforced.

### 3.4 The shared helper — `CurrentBiomeNameOrNull()`

One private method on `MapSurface`, called by both the disc caption (§3.1) and the modal
label (§3.2). This IS the AT-BIOME-SHARED single path:

```csharp
// Localized current-biome name, or null when unresolved (no player / None / unlocalized).
// Vanilla construction: Localize("$biome_" + biome.ToString().ToLower()) (Minimap.UpdateBiome).
private static string? CurrentBiomeNameOrNull()
{
    var player = Player.m_localPlayer;
    if (player == null) return null;
    Heightmap.Biome biome = player.GetCurrentBiome();
    if (biome == Heightmap.Biome.None) return null;     // §3.5 — no $biome_none token

    string token = "$biome_" + biome.ToString().ToLower();
    var loc = Localization.instance;
    if (loc == null) return null;
    string text = loc.Localize(token);
    // §3.5 — unlocalized token comes back as the literal "$biome_xxx"; treat as null.
    return (string.IsNullOrEmpty(text) || text.StartsWith("$biome_")) ? null : text;
}
```

- **Placement:** `MapSurface.cs`, beside `UpdateCaption`/`UpdateTitle`. `static` because it
  reads only global state (`Player.m_localPlayer`, `Localization.instance`) — no instance
  fields. Both the disc instance and the modal instance call the same static method → provably
  one path.
- Returns the **localized display name** ("Meadows", "Black Forest", "Mistlands"), not the
  token — both surfaces render the human-readable string.

### 3.5 The `Biome.None` / unlocalized guard (the literal-leak defense)

Vanilla itself emits `$biome_none` unguarded into a throwaway `TMP_Text` (`:48734`), where a
stale value is harmless. Our caption **recomposes** every Render, so a junk token would show
as a literal — exactly the 2026-06-05 sign-bug class (a `$token` leaking to the player as raw
text). Two guards in `CurrentBiomeNameOrNull` (above):

1. **`Biome.None` → null.** `None=0` has no `$biome_none` token (verified against the vanilla
   English localization: the 9 real biomes — meadows, blackforest, swamp, mountain, plains,
   ashlands, deepnorth, ocean, mistlands — have `$biome_*` tokens; `none` does not). The player
   is in `None` only in edge states (pre-spawn, between zones); show nothing, not a literal.
2. **Unlocalized passthrough → null.** `Localization.Localize` returns the input string
   unchanged when a token is unknown — so an unmapped biome comes back as the literal
   `"$biome_xxx"`. The `StartsWith("$biome_")` check catches it and returns null. This also
   future-proofs against a vanilla enum gaining a value before its token ships.

When the helper returns null, the disc omits the biome line (§3.1) and the modal hides
`_biomeLabel` (§3.2) — never a `$biome_*` literal on screen. **AT-BIOME-CLEAN** depends on
both guards.

> **Verified token set (vanilla English localization):** `$biome_meadows`,
> `$biome_blackforest`, `$biome_swamp`, `$biome_mountain`, `$biome_plains`,
> `$biome_ashlands`, `$biome_deepnorth`, `$biome_ocean`, `$biome_mistlands`. The enum's
> `ToString().ToLower()` produces exactly these stems for the nine real biomes
> (`BlackForest`→`blackforest`, `DeepNorth`→`deepnorth`, `AshLands`→`ashlands`). No custom
> `$piece_*`/`$biome_*` token is authored — we reuse vanilla's, locale-correct.

---

## 4. The concrete change list (engineer-ui builds against `origin/main` @ dcd2181)

Branch off `origin/main` — PR #205's caption infrastructure is **merged**, so this card has
no blocking parent; it lands directly on main, one worktree. Three edits, all clean-side, all
in `MapSurface.cs`. No controller change, no `MapViewRequest` change, no `MapViewer` change.

### 4.1 ADD `CurrentBiomeNameOrNull()` — `MapSurface.cs`

Add the static helper from §3.4 beside `UpdateCaption`/`UpdateTitle`. Add a
`CaptionBiomeFontPx = 16` constant beside `CaptionNameFontPx`/`CaptionHintFontPx` (§3.1).

### 4.2 EXTEND `UpdateCaption()` — disc biome line — `MapSurface.cs`

In `UpdateCaption` (`:982`), compose the biome line between the name line and the hint line.
The current code builds `text` as either `hintLine` (no name) or `name\nhintLine`. Insert the
biome line conditionally so all four combinations work:

```csharp
string? biome = CurrentBiomeNameOrNull();                       // §3.4
string biomeLine = biome != null
    ? "<size=" + (int)CaptionBiomeFontPx + ">" + biome + "</size>\n"
    : "";

string text;
if (string.IsNullOrEmpty(rawName))
    text = biomeLine + hintLine;                                // biome / hint, or just hint
else
{
    string nameLoc = loc != null ? loc.Localize(rawName) : rawName!;
    text = "<size=" + nameSz + ">" + nameLoc + "</size>\n" + biomeLine + hintLine;
}
```

- The `_captionLastText` guard (§2.2) still applies unchanged — when the biome string
  changes (player crosses a biome border), `text` differs, the guard lets the repaint
  through; otherwise it skips. **Note for the engineer:** because the caption text now varies
  with player biome, the guard correctly causes a repaint on biome change even on an otherwise
  identical re-bind — this is desired and costs one `Text.text` set per border crossing
  (~rare), not per poll.
- `BuildCaption`'s `sizeDelta` height (`:1342`, currently `CaptionNameFontPx +
  CaptionHintFontPx + 16f`) must grow to fit the third line: add `CaptionBiomeFontPx` to the
  height so a long biome name doesn't clip vertically. `HorizontalWrapMode.Overflow` already
  handles width.

### 4.3 ADD `_biomeLabel` to the modal — `MapSurface.cs`

- Add a `private Text? _biomeLabel;` field beside `_titleLabel` (`:147`).
- In `BuildPrompts` (`:1251`, after the `_titleLabel` block `:1291`), build `_biomeLabel` on
  `_root`: same font idiom (`Signs.VanillaUISkin.Font ?? Arial`), `fontSize ~22`,
  `alignment = UpperCenter`, `(0.5,1)` anchor/pivot, `anchoredPosition ≈ (0, -84)` (under the
  title), `sizeDelta ≈ (1200, 40)`, `horizontalOverflow/verticalOverflow = Overflow`,
  `raycastTarget = false`, `color` matching the title's warm tint, and `Outline` like the
  caption. Start `SetActive(false)`.
- Add `UpdateBiomeLabel()` (sibling of `UpdateTitle`): if `_biomeLabel == null` return; set
  `string? b = CurrentBiomeNameOrNull(); _biomeLabel.gameObject.SetActive(b != null);` and
  `_biomeLabel.text = b ?? ""`. Call it from `Render()` (`:205`) right after `UpdateTitle()`.
- **Disc safety:** `_biomeLabel` is built only in the `ShowPrompts` path (modal), so the disc
  instance never has one — its biome rides `_discCaption` (§3.1). `UpdateBiomeLabel`'s null
  guard makes the disc call a no-op even though `Render` is shared.

### 4.4 Build gate (AGENTS.md)

`dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` → **0 errors, 0
warnings** (`<TreatWarningsAsErrors>` ON — the nullable `string?` returns and `_biomeLabel?`
field must be null-correct). No new Harmony patch (`PatchCheck` +0). SpecCheck +0 (§0). The
existing structural xUnit suite must stay green (a caption-composition unit test may be
extended to assert the three-line stack + the None/unlocalized → omitted-line behavior).

---

## 5. Acceptance tests (named, observable — logs-green ≠ playable; Daniel's eyeball is the judge)

| ID | Setup | Expected |
|---|---|---|
| **AT-BIOME-MINIMAP** | nomap-ON, bound+imprinted Local Map. Disc visible. Walk across a biome border (e.g. Meadows → Black Forest). | The disc caption shows the player's **current-biome NAME** as the middle line (name / **biome** / `[M]` hint). The biome line **updates** when the player changes biome — "Meadows" becomes "Black Forest" on crossing. Vanilla-style current-biome readout, legible at the 200 px disc. |
| **AT-BIOME-MODAL** | Open the local-map modal (M) while standing in a biome. | A **fixed current-biome readout** renders under the modal's title cartouche ("Local map for X" / **biome**). It tracks the player's biome live while the modal is open (walk → it updates). NOT a cursor-hover readout (that's the deferred §3.2.1 follow-up). |
| **AT-BIOME-SHARED** | Both surfaces visible/used in one session. | The biome name on BOTH surfaces comes from ONE `MapSurface.CurrentBiomeNameOrNull()` — same string, same construction. No divergent second biome path. (Code-level: one static helper; visual-level: both read the same human-readable biome name.) |
| **AT-BIOME-CLEAN** | Any biome incl. edge states (pre-spawn / `Biome.None`); rebind locale. | Uses vanilla `$biome_*` tokens (locale-correct — switch language, the biome name follows). **No raw `$biome_*` literal ever shows** (None → line omitted; unlocalized → omitted, §3.5). nomap stays enforced — no vanilla `m_biomeName*` / `Minimap` / material / root mutation (we read `GetCurrentBiome()` and paint our own `Text`). |
| **AT-BIOME-NONE-OMIT** (regression) | Force a `Biome.None` frame (pre-spawn / world teardown). | The disc caption falls back to name / hint (no empty biome line); the modal hides `_biomeLabel`. Never an empty row or a `$biome_none` literal. |
| **AT-CAPTION-NO-ROTATE** (regression) | Disc visible; turn the player so the disc rotates to heading. | The three-line caption (name / biome / hint) is **screen-stable** — it does NOT spin. It remains a child of the non-rotating `_frame` (inherited from PR #205); the biome line adds no rotation coupling. |
| **AT-CAPTION-NAME-HINT-INTACT** (regression) | Disc visible, bound map with a name. | The PR #205 name line + `[<$KEY_Map>]` hint line are **unchanged** — same text, same rebind-correctness (rebind Map → hint key updates). The biome line is inserted between them without disturbing either. A bound-but-unnamed map shows biome / hint (no empty "Local map for "). |
| **AT-MODAL-TITLE-INTACT** (regression) | Open the modal. | The modal's top-centre cartouche still shows the **BARE** name (the formatted-under-disc / bare-in-modal split, unchanged). The biome label sits **below** the title, doesn't overlap it or the exit prompt. |
| **AT-BIOME-NO-WIRE** (regression) | Inspect a saved/dropped Local Map's `SurveyData`. | No biome layer in the artifact — `SurveyData` wire format **unchanged** (fog + pins only). The biome is computed live from `GetCurrentBiome()`, never baked. (Confirms Path A, not Path B-baked.) |
| **AT-BIOME-MODAL-CURSOR-DEFERRED** (scope marker, not a build test) | — | Cursor-hover biome on the modal is **intentionally absent** in this card (§3.2.1). If Daniel wants it after playtest, it's a follow-up card (modal input plumbing + shroud-gate decision), not a regression here. |
| logs-green ≠ playable | — | Daniel's in-game GPU eyeball on both surfaces (placement, legibility, update-on-border-crossing) is the real accept. |

> **Build gate:** 0 errors / 0 warnings (§4.4). Added to the playtest ledger for in-game
> verify (local solo or Niflheim), like the caption card.

---

## 6. Shared-surface coordination (do NOT clobber the disc / caption)

`MapSurface.cs` is the hot, multi-card surface. This biome line is a **clean extension of
already-merged work** — sequencing is now simple:
- **Caption infrastructure (`t_26bba85b` / PR #205) — MERGED to `origin/main` (`dcd2181`).**
  `_discCaption`, `ShowCaption`, `MapViewRequest.Caption`, `UpdateCaption`/`BuildCaption`
  (parented to `_frame`) are live. The biome line **extends** `UpdateCaption` — it does not
  re-author a caption object. Branch off main; inherit the stack.
- **Disc player-marker chevron (`t_efe8b32b`) — shipped (PR #192).** On the disc FACE
  (`_overlayLayer`); the caption is below the bezel on `_frame`. Different layers, no
  collision.
- **Render-correctness foundation (`t_ba31ad30` / §2E.5) — shipped (PR #192/#197).** The
  biome line is pure text on top; doesn't touch the render path.

Because PR #205 is merged, this card has **no blocking parent** — one engineer-ui worker,
one worktree off `origin/main`, all edits in `MapSurface.cs`. If another in-flight card is
*also* editing `UpdateCaption`/`BuildPrompts` concurrently, the same engineer-ui owner should
sequence them to avoid two workers racing the same methods; otherwise it lands independently.

---

## 7. Cross-doc edits this change carries (spec-first — same PR as the impl)

Per AGENTS.md, the **implementation PR** that builds §4 also lands the doc edits so no spec
contradicts shipped behavior. (This spec-pass PR lands THIS doc + the §2 addendum + the
§2H.1 AT cluster note; the impl PR flips any remaining present-tense prose if needed.)

1. **`map-provider-model.md` §2** — add a 🟢 DECIDED addendum (Daniel 2026-06-19, Path A): the
   bound minimap disc + the local-map modal carry the player's **current-biome NAME** (vanilla
   `$biome_*`, current-biome readout). Disc: a line in the under-disc caption stack (name /
   biome / `[M]` hint). Modal: a fixed readout under the title. Computed live from
   `GetCurrentBiome()` — no survey-wire change. (Net-new line the locked §2 / §2.1 model did
   not specify.) Landed by THIS spec-pass PR.
2. **`cartography-impl-spec.md` §2H.1** — add the AT-BIOME-MINIMAP / AT-BIOME-MODAL /
   AT-BIOME-SHARED / AT-BIOME-CLEAN named tests beside the existing
   AT-MAPNAME-UNDER-DISC caption cluster, with a one-line note that the disc caption now
   carries a biome line between name and hint, and the modal carries a fixed biome readout
   under the title. Landed by THIS spec-pass PR.
3. **`docs/v2/planning/index.md` + `README.md`** — add this doc (two-file rule; §9). Landed by
   THIS spec-pass PR.

> **Why the §2/§2H.1 doc edits land in the SPEC-pass PR (not the impl PR):** the
> name+hint caption precedent (PR #202 spec-pass, PR #205 impl) split the design lock into
> the spec PR and the code into the impl PR. This card follows the same split — the design
> addendum + AT cluster + new spec doc are the spec-pass deliverable (docs-only, no code); the
> engineer-ui impl PR carries `MapSurface.cs` + the playtest-ledger row. Both are
> Daniel-gated.

---

## 8. Open questions for Daniel (route at review — do NOT stamp)

The path fork (A/B/C) is **CLOSED** — Daniel picked A (biome NAME), architect owns layout.
The sub-decisions this spec RESOLVES with recommendations (Daniel's in-game eyeball is the
final judge), carried as confirm-on-playtest:

1. **Modal form (fixed vs cursor-hover)** — RESOLVED as **fixed current-biome readout**
   (§3.2). Free, consistent with the disc, dissolves the explored-gate question. Cursor-hover
   is the deferred follow-up (§3.2.1) — flag, not silent drop. *Confirm fixed reads well
   in-game; if Daniel wants hover-inspect of remote biomes, spawn the follow-up.*
2. **Caption line order** — RECOMMEND **name / biome / `[M]` hint** (§3.1): identity lines
   (name + biome) together, action hint last. Daniel-delegated layout; order + font size are
   one-line knobs. *Confirm on playtest.*
3. **Minimap update cadence** — biome updates **on biome change** (the `_captionLastText`
   guard repaints only when the biome string differs, §2.2/§4.2), matching vanilla's
   change-driven minimap behavior. No pulse animation (vanilla's `SetTrigger("pulse")` drives
   vanilla's own `Animator`; our caption is a plain `Text` — a pulse would be net-new and is
   not requested). *Confirm change-driven (no pulse) is acceptable.*
4. **Modal biome font/placement** — RECOMMEND ~22 px under the title (§3.2). Calibration knob;
   Daniel's eyeball tunes.

**New questions surfaced (route, do not stamp):**
- **Pulse-on-change** — vanilla pulses the minimap biome name on change. Not built (plain
  `Text`, no `Animator`). If Daniel wants the vanilla feel, it's a small follow-up knob.
- **Biome line color** — the biome line inherits the caption's warm tint. If Daniel wants the
  biome to read distinctly (e.g. a per-biome tint, which edges toward Path B), that's a
  separate enhancement, not this card.

---

## 9. Docs placement (sbpr-docs-conventions)

- This file: `docs/v2/planning/local-map-biome-indicator-impl-spec.md` (version-scoped
  buildable spec, same shelf as the sibling cartography specs).
- **Two-file rule:** add a row to `docs/v2/planning/index.md` (manifest) and a bullet to
  `docs/v2/planning/README.md` (narrative). Both updated in this spec-pass PR.
- SpecCheck: **+0** (§0).
