---
title: "Local-map name + [M] open-hint UNDER the minimap disc — buildable implementation spec"
status: current
purpose: "Build-ready architect spec relocating the bound-local-map open hint from a floating bottom-centre screen overlay to a label UNDER the carry minimap disc (top-right), co-located with the map's NAME, so the UI reads as one unit: '[M] opens THIS named local map.' Converts Daniel's 2026-06-19 v0.2.27 playtest refinement (the bottom hint 'doesn't really work for me') into one section an engineer-ui implementer picks up cold. The hint MOVES (the old bottom-centre canvas is deleted), gains the map name above it, and its visibility model widens from equipped-only to disc-visible (provider bound + nomap-ON) so it matches when M actually opens the map. Net-new addendum to the locked map-provider-model.md §2 minimap model; closes that addendum's placement gap and supersedes local-map-mkey-open-impl-spec.md §5's bottom-centre prompt placement. Authored by the architect spec-pass (card t_338f723b). The design lock is the WHAT; this is the HOW-to-pick-it-up-cold. Build is the engineer-ui child of this card; Daniel gates the merge."
owner: Daniel (design authority); architect (spec capture + grounding)
supersedes_partial:
  - "local-map-mkey-open-impl-spec.md §5 — the equipped HUD prompt's PLACEMENT (bottom-centre, equipped-only) is relocated under the disc; the $KEY_Map token + rebind-correctness STAND"
---

# Local-map name + `[M]` open-hint UNDER the minimap disc — buildable implementation spec

The carry minimap disc (top-right, `MapViewer._disc`) renders the bound local map's
survey but shows **no name**, and the `[M] Read map` open-hint floats **bottom-centre**
of the screen, spatially divorced from the disc it acts on. Daniel's 2026-06-19
v0.2.27-playtest refinement: *"the [M] READ MAP display at the bottom doesn't really
work for me… maybe put the name of the map under the minimap and put the M key hint
there to show it will open the named local map?"*

This doc is the buildable *how*: **relocate** the open-hint from the standalone
bottom-centre canvas to a label **under the disc**, **add the map's name** above the
hint as one visual unit, **widen** the visibility model from equipped-only to
disc-visible, and land the design-doc addendum + acceptance tests in the SAME PR
(AGENTS.md spec+code rule). An `engineer-ui` implementer should build the whole change
from §3–§6 without re-deriving anything.

> **Why this card exists (refinement, not defect).** The current bottom-centre hint
> works exactly as the merged M-key spec (`local-map-mkey-open-impl-spec.md` §5) describes
> — `$KEY_Map`+`$piece_readmap`, rebind-correct, shown while equipped. The worker followed
> the spec. Daniel is **refining the UX after feeling it in-game**: the hint should be
> anchored to the thing it acts on (the disc), carry the map's name, and be visible whenever
> M actually works. This is a net-new addendum to the locked `map-provider-model.md` §2
> minimap model, which specified the disc but said nothing about a name label or where the
> open-hint lives.

> **Clean-side note (ADR-0001):** this touches ONLY SBPR's own overlay UI
> (`LocalMapController.cs` prompt canvas, `MapSurface.cs`/`MapViewer.cs` disc surface) and
> reads vanilla `Localization`/`ZInput` tokens (`$KEY_Map`, `$piece_readmap`). No other
> mod's code is read or copied; no vanilla material/root is mutated. Pure HUD presentation.

---

## 0. SpecCheck manifest impact (read first)

**None.** This feature moves a HUD overlay + adds a text label + widens a visibility
predicate on existing controllers — no item recipe, no build piece, no station.
`Runtime/SpecCheck.cs` is **untouched** (+0 rows). Spec-first still applies: the
implementation PR carries the code **and** the `map-provider-model.md` §2 addendum **and**
the `cartography-impl-spec.md` §2H.1 acceptance tests **and** the
`local-map-mkey-open-impl-spec.md` §5 supersede banner **together** (§8).

---

## 1. What is DECIDED vs what THIS card specs

**🟢 DECIDED (Daniel 2026-06-19 playtest — build exactly this):**
- The bound local map's **NAME** renders **under the minimap disc** (top-right).
- The **`[M]` open-hint** renders **with the name, under the disc** — one visual unit
  reading "[M] opens this named local map."
- The hint is **relocated**, not duplicated: the floating bottom-centre element goes away.

**This card SPECS (the HOW + the knobs the playtest report flagged open):**
- The exact relocation mechanism: delete the standalone bottom-centre prompt canvas
  (`LocalMapController.EnsurePromptCanvas`), build the name+hint as a label anchored under
  the disc surface (§3).
- The **visibility model** — disc-visible (provider bound + nomap-ON), NOT equipped-only
  (§3.3, resolves open Q2 with Daniel's eyeball as judge).
- The **name format** under a 200 px disc (§3.4, resolves open Q4).
- The **nomap-OFF** behavior — no SBPR disc there, so no label there (§3.5, resolves Q3).
- The token (`$KEY_Map` + `$piece_readmap`) and rebind-correctness carried forward verbatim
  from `local-map-mkey-open-impl-spec.md` §5 — do NOT reopen (§3.2).

**Out of scope (do NOT build here — coordinate, don't absorb):**
- The disc's render/clip/fog correctness (`§2E.5`, shipped PR #192/#197) — untouched.
- The disc player-marker chevron (`t_efe8b32b` / AT-DISC-MARKER-1, shipped PR #192) — the
  name label lives BELOW the disc, the chevron lives ON the disc face; they don't collide,
  but both ride `MapSurface`/`MapViewer` so the SAME engineer-ui worker owns them (§7).
- Any change to WHAT M does, the provider state machine, or the full-screen modal viewer's
  own title cartouche (that stays the BARE name, §2A.6c — unchanged).

---

## 2. Grounding — the three code facts this builds against (origin/main @ d7dd075)

This is current-as-shipped behaviour to be CHANGED, not a bug to be fixed. Three concrete
facts, file + line, re-anchored against the engineer's base branch:

**2.1 The bottom-centre hint** — `Features/Cartography/LocalMapController.cs`
- `EnsurePromptCanvas()` (`:308`) builds a STANDALONE screen-overlay `Canvas`
  (`SBPR_LocalMapPrompt`, `:312`; `DontDestroyOnLoad`, sortingOrder 4000).
- Text `:337`: `"[<color=yellow><b>$KEY_Map</b></color>] $piece_readmap"` localizes to
  **"[M] Read map"** (the exact string Daniel saw). `$KEY_Map`/`$piece_readmap` are vanilla
  tokens, rebind-correct — **keep them** (the 2026-06-05 sign-bug literal-leak lesson:
  never swap to a custom `$piece_*` literal).
- Anchor: `_promptText.alignment = LowerCenter` (`:327`); `rt.anchorMin/Max = (0.5,0)`
  (`:341-342`); `rt.anchoredPosition = (0, 90)` (`:344`) = **bottom-centre, 90 px up**. This
  is the "display at the bottom" Daniel means.
- Visibility `:182`: `UpdateEquippedPrompt(mapEquipped && !fieldViewOpen)` — **shown ONLY
  while EQUIPPED** and the full view is closed. This is NARROWER than when M works (§2.4).
- Fields to delete with it: `_promptRoot` (`:80`), `_promptText` (`:81`), `_promptShown`
  (`:82`), the `OnDestroy` teardown (`:69`), `UpdateEquippedPrompt` (`:293`),
  `EnsurePromptCanvas` (`:308`), and the `:182` call site.

**2.2 The minimap disc** — `Features/Cartography/MapViewer.cs` + `MapSurface.cs`
- `_disc` config (`MapViewer.cs:73-84`): `ScreenAnchor = (1,1)` ("top-right — vanilla
  minimap's home", `:81`), `DiscTargetPx = 200` (`:36`), `DiscCornerMarginPx = 24` (`:38`),
  `ShowPrompts = false` (`:78`), `PlayerCentred = true` (`:80`).
- The disc DELIBERATELY shows no name today: `BindMinimap` force-clears the title at TWO
  sites — `MapViewer.cs:104` `request.Title = string.Empty; // a minimap shows no cartouche`
  AND `LocalMapController.DriveMinimapDisc :205` `Title = string.Empty`. Daniel wants a name
  LABEL **beneath** the disc, NOT a cartouche on the disc face — so do NOT un-clear Title
  (the modal's `_titleLabel` is a top-centre cartouche on a `ShowPrompts=true` surface; the
  disc has `ShowPrompts=false` and no title element). The new label is a separate element
  (§3.1), not a re-enable of the modal cartouche.
- The disc renders only when `shouldBindDisc` (`LocalMapController.cs:155-158`):
  `_provider != null && Game.m_noMap (nomap-ON) && LocalMap.IsImprinted(_provider) &&
  LocalMap.ReadSurvey(_provider) != null`. **In nomap-OFF there is NO SBPR disc** (vanilla
  minimap owns the corner — `map-provider-model.md` §6). See §3.5.
- The disc's on-screen geometry: the frame is corner-anchored, pulled in from the top-right
  by `CornerMargin + bezelEdge*0.5` where `bezelEdge = TargetPx * sqrt(2) ~= 283 px`
  (`MapSurface.cs:1110,1168-1175`). The caption sits below the bezel's bottom edge (§3.1).

**2.3 The name is already available** — `Features/Cartography/LocalMap.cs`
- `LocalMap.TryGetName(item, out name)` (`:304`) reads the imprinted Table name, **BARE**
  (no display wording). `FormatDisplayName(tableName) => $"Local map for {tableName}"`
  (`:82`) wraps it. The full-screen modal viewer ALREADY uses the BARE name as its cartouche
  title (`LocalMapController.cs:369,377` — `TryGetName` then `Title = mapName`). So the name
  for "under the disc" is a one-call read of existing data — **no new storage, no new key.**
- `TryGetName` returns `false` for a blank / pre-naming-era map. The disc requires
  `IsImprinted`, so a bound disc always has a survey; a name may still be absent on a
  pre-1.6 imprint — handle gracefully (§3.4).

**2.4 The visibility asymmetry (the likely core of "doesn't really work for me").**
M **opens the bound map even when UNEQUIPPED** (still carried) — `HandleMapKeyPressed()`
routing (`LocalMapController.cs:235,250`: `var toOpen = _equippedMap ?? _provider`). But the
bottom hint shows **only while EQUIPPED** (`:182`). So: carry a bound-but-unequipped map,
M still opens it, but no hint tells you so. The disc, by contrast, IS visible whenever the
provider is bound (`shouldBindDisc`, nomap-ON) — a **closer match to when M actually works**.
Relocating the label to track disc-visibility (§3.3) fixes the asymmetry for free: the hint
is present exactly when the disc (and the M-open it advertises) is.

---

## 3. The mechanism — a name+hint caption anchored under the disc surface

Two halves: (a) DELETE the standalone bottom-centre prompt canvas in `LocalMapController`
(§2.1); (b) ADD a two-line caption to the disc's `MapSurface`, positioned just below the
bezel, driven from the same provider state that drives the disc.

### 3.1 Where the caption lives — own element on the disc surface (recommended)

Build the caption as a new child element on the **disc** `MapSurface` (the `PlayerCentred`,
`ShowPrompts=false` instance), NOT on the modal. Two viable homes — the spec recommends (A):

- **(A, recommended) A new `_discCaption` `Text` on the disc's `_root` Canvas**, anchored to
  the disc's top-right `ScreenAnchor` and offset DOWN below the bezel. The disc `MapSurface`
  already corner-anchors its `_frame` (`ComputeFrameAnchoredPos`, `MapSurface.cs:1168`); the
  caption reuses that anchor and sits at `anchoredPosition.y ~= -(CornerMargin + bezelEdge*0.5
  + pad)` (just under the sqrt(2) bezel's bottom), horizontally centred on the disc centre.
  This glues the caption to the disc across resolutions (both ride the `CanvasScaler`
  reference 1920x1080). Gate its construction on a new `MapSurfaceConfig.ShowCaption` flag
  (cleaner — mirrors the existing `ShowPrompts`/`ShowBackdrop` knob pattern,
  `MapSurface.cs:48-64`; the modal sets it false, the disc true).
- **(B, alternative) A caption owned by `LocalMapController`** as a small standalone overlay
  canvas (like the deleted one) but anchored top-right under the disc, driven by the same
  `DriveMinimapDisc`/`UnbindMinimapDisc` transitions (`:192-224`). Simpler (no `MapSurface`
  change) but re-introduces a second canvas that must manually track the disc's screen
  position — brittle if the disc anchor/size ever changes. Take (B) only if a new config flag
  on `MapSurface` proves disruptive.

**Recommendation: (A) with a `ShowCaption` flag.** It keeps the caption's position derived
from the disc's own layout math (one source of truth) and matches the established two-instance
`MapSurfaceConfig` pattern — the disc owns its caption the way the modal owns its cartouche.

### 3.2 The hint text — carry the token forward verbatim (do NOT reopen)

The hint line is the SAME localized string the deleted bottom prompt used:

```csharp
string raw = "[<color=yellow><b>$KEY_Map</b></color>] $piece_readmap";
string hint = Localization.instance != null ? Localization.instance.Localize(raw) : raw;
```

- `$KEY_Map` resolves to the player's bound Map key (e.g. "M"), rebind-correct via
  `ZInput.GetBoundKeyString` (`local-map-mkey-open-impl-spec.md` §5 grounding — verified NOT a
  literal leak; `"Map"` is a registered ZInput button). `$piece_readmap` is "Read map".
- **Do NOT** hardcode "M" and **do NOT** invent a custom `$piece_*` token. The relocation
  changes WHERE the hint is, not WHAT it says. AT-MKEY-HINT-COLOCATED depends on the token
  staying `$KEY_Map`.

### 3.3 Visibility model — disc-visible, NOT equipped-only (resolves open Q2)

The relocated caption tracks **disc visibility** — the same `shouldBindDisc` gate that drives
the disc (`LocalMapController.cs:155-158`: provider bound + nomap-ON + imprinted + survey):
- With the caption on the disc `MapSurface` (route A) it's a child of the disc `_root`, so it
  shows/hides automatically with the disc's `Show()`/`Hide()` — no separate visibility code.
  (Its TEXT still needs a per-bind refresh so the name updates when the provider changes —
  §3.6.)
- This **widens** visibility vs the old equipped-only prompt (`:182`), matching when M
  actually opens the map (§2.4). A bound-but-unequipped carried map now shows the disc AND the
  caption — closing the discoverability gap Daniel hit.

> **Daniel's call (open Q2):** the spec RECOMMENDS disc-visibility (the asymmetry fix). If
> Daniel prefers equipped-only after seeing it in-game, the engineer gates the caption on
> `_mapEquipped` instead — a one-line change. AT-HINT-VISIBILITY is written for the
> recommended disc-visible model and annotated confirm-on-playtest.

### 3.4 Name format under a 200 px disc (resolves open Q4)

The caption is **two lines** — name on top, `[M]` hint below — reading as one unit:

```
Local map for Northern Outpost
[M] Read map
```

- **Name line:** use `LocalMap.FormatDisplayName(bareName)` = `"Local map for <Table>"` (the
  item's inventory wording and Daniel's locked issue-4 format, `LocalMap.cs:82`). It directly
  answers "[M] opens THIS named local map." The modal cartouche uses the BARE name (a big
  top-centre title where "Local map for" is redundant chrome); the under-disc caption is a
  small glanceable label where the full phrase reads as a sentence with the hint. **Use the
  FORMATTED name here, BARE in the modal** — a deliberate split; call it out so a future reader
  doesn't "fix" it to match.
- **Legibility at 200 px:** "Local map for Northern Outpost" at the deleted prompt's 26 px font
  (`:326`) overruns the disc. Size the name line ~18 px and the hint line ~16 px, centred under
  the disc, `HorizontalWrapMode.Overflow` so a long name extends symmetrically rather than
  clipping. Exact px is a **build-calibration knob** (one constant, like `MapRotationSign`) —
  Daniel's eyeball tunes it. Start at name 18 / hint 16.
- **Long names:** do NOT truncate in code (a hard cap hides which map it is). Let it overflow
  centred; a pathological-width name is a follow-up knob (§9), not a launch blocker.
- **Missing name (`TryGetName` false — pre-naming imprint):** render the hint line only, no
  name line. The caption still reads "[M] Read map" under the disc. Never render the literal
  "Local map for " with an empty tail.

### 3.5 nomap-OFF (resolves open Q3)

In nomap-OFF there is **NO SBPR disc** — the vanilla minimap owns the top-right corner
(`map-provider-model.md` §6; `shouldBindDisc` requires `Game.m_noMap`). Therefore **there is
no under-disc caption in nomap-OFF** — it rides the disc's own gate (route A makes this
automatic: no disc `Show()` then no caption). The local map is still M-openable in nomap-OFF
(the provider machine runs in both modes), but the name+hint surface is the disc, and the disc
is nomap-ON-only.

- **Do NOT** attach the caption to vanilla's minimap in nomap-OFF — that's vanilla's surface
  (clean-side: no vanilla root mutation), and nomap-ON is the playtested Trailborne default
  (the primary, and for this build the only, case). nomap-OFF discoverability is a separate
  future concern (§9), not built here.

### 3.6 Refreshing the caption text on provider change

The disc re-renders on the 0.25 s poll via `DriveMinimapDisc -> BindMinimap` (a fresh request
each bind). The caption NAME must refresh when the provider changes (equip A then equip B):
- Add a new `MapViewRequest.Caption` (string, default null) — do NOT reuse `Title` (it's
  force-cleared for minimaps at two sites, §2.2; re-purposing it risks the disc growing a face
  cartouche). `DriveMinimapDisc` sets
  `Caption = LocalMap.TryGetName(provider, out var n) ? LocalMap.FormatDisplayName(n) : null`;
  `MapSurface.Show/Refresh` pushes it into `_discCaption` (name line) above the static
  localized hint line. Controller stays the name authority; surface stays the renderer —
  matching the existing `Title`/`_titleLabel` flow.
- The hint line ("[M] Read map") is STATIC (localized once on build); only the name line is
  per-provider. Leave `Title` exactly as-is so the disc never grows a face cartouche.

---

## 4. Controller + surface edits — the concrete change list

The engineer-ui worker builds this against `origin/main`. Five edits, all clean-side.

### 4.1 DELETE the bottom-centre prompt canvas — `LocalMapController.cs`

Remove the entire standalone prompt apparatus (§2.1): `_promptRoot`/`_promptText`/
`_promptShown` fields (`:80-82`), the `OnDestroy` `Destroy(_promptRoot)` line (`:69`),
`UpdateEquippedPrompt` (`:293-306`), `EnsurePromptCanvas` (`:308-346`), and the `:182` call
site `UpdateEquippedPrompt(mapEquipped && !fieldViewOpen)`. After deletion, `fieldViewOpen`
(`:180-181`) may become unused — if so, delete it too (0-warning build: an unused local is a
CS0219-class smell; `<TreatWarningsAsErrors>` is ON). Grep for any other `_prompt*` reference
before compiling.

### 4.2 ADD a disc caption — `MapSurface.cs` + `MapSurfaceConfig`

- Add `public bool ShowCaption = false;` to `MapSurfaceConfig` (`:48-64`, beside `ShowPrompts`).
- In `MapViewer.Awake` (`:73-84`) set `ShowCaption = true` on the `_disc` config (the modal
  leaves it default false).
- In `EnsureBuilt` (`:1073`), when `_cfg.ShowCaption`, build a `_discCaption` `Text` on `_root`
  (NOT the rotating `_frame`/`_mapContainer` — the caption must NOT spin): top-right anchored,
  `anchoredPosition.y` below the bezel (§3.1), `alignment = UpperCenter`,
  `HorizontalWrapMode.Overflow`, `raycastTarget = false`, font from
  `Signs.VanillaUISkin.Font` (the established idiom, `:1206`). Two text rows: the per-provider
  name line + the static localized hint line (one `Text` with an embedded `\n`, or two stacked
  `Text` children — engineer's call; a single `Text` with `\n` is simplest and keeps them
  glued).
- The static hint line is localized once at build (§3.2). The name line is set in `Render`/a
  new `UpdateCaption()` from `_req.Caption` (§3.6), called from `Render()` (`:177-196`,
  alongside `UpdateTitle()`).

### 4.3 ADD `MapViewRequest.Caption` — `CartographyViewer.cs`

Add `public string? Caption;` to the `MapViewRequest` struct (`:67-88`), documented as
"disc-only: the formatted name shown UNDER the minimap disc caption (FormatDisplayName);
ignored by the modal." Default null. Do NOT touch `Title`.

### 4.4 SET the caption in `DriveMinimapDisc` — `LocalMapController.cs`

In `DriveMinimapDisc` (`:192-214`), on the `BindMinimap` request, set
`Caption = LocalMap.TryGetName(provider, out var capName) ? LocalMap.FormatDisplayName(capName) : null`
(§3.6). Mirror the same in `MapViewer.BindMinimap` ONLY if it would otherwise clear it —
verify `BindMinimap` (`MapViewer.cs:97-106`) does not force `Caption` to null the way it does
`Title`; it must pass `Caption` through untouched (add nothing that clears it).

### 4.5 Build gate (AGENTS.md)

`dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` -> **0 errors, 0
warnings** (`<TreatWarningsAsErrors>` ON; the deleted prompt members MUST leave no dangling
references). No new Harmony patch, so `PatchCheck` is unaffected. SpecCheck +0 (§0).

---

## 5. Acceptance tests (named, observable — logs-green != playable; Daniel's eyeball is the judge)

| ID | Setup | Expected |
|---|---|---|
| **AT-MAPNAME-UNDER-DISC** | nomap-ON. Craft + imprint a Local Map at a named Surveyor's Table; bind it (equip). | The map's name ("Local map for <Table>") renders directly **under the minimap disc** (top-right), legible at the 200 px disc size — NOT a floating bottom-centre element. |
| **AT-MKEY-HINT-COLOCATED** | Same as above, disc visible. | The `[M]` open-hint renders **with the name, under the disc** (one visual unit), using the rebind-correct `$KEY_Map` token. Rebinding Map updates the key shown. No hardcoded "M", no `$piece_*` literal leak. |
| **AT-HINT-VISIBILITY** | Equip a bound map, then **unequip** (still carried), nomap-ON. | The name+hint caption is visible whenever the **disc** is (provider bound + nomap-ON) — including bound-but-unequipped — matching when M actually opens the map. NOT gated to equipped-only. *(Confirm-on-playtest: this is the recommended model, Q2; flip to equipped-only is a one-liner if Daniel prefers.)* |
| **AT-HINT-NO-BOTTOM** | Any bound-map state. | There is **no** `[M] Read map` element at screen bottom-centre anymore — the standalone `SBPR_LocalMapPrompt` canvas is gone. |
| **AT-MAPNAME-BLANK** | Carry/bind a map imprinted before naming existed (no NameKey). | The caption shows the `[M]` hint line only, **no** name line; never the literal "Local map for " with an empty tail. |
| **AT-CAPTION-NOMAP-OFF** | nomap-OFF world, local map equipped. | There is **no** SBPR disc and therefore **no** under-disc caption (vanilla minimap owns the corner). The map is still M-openable; the caption simply doesn't exist in this mode. |
| **AT-CAPTION-NO-ROTATE** (regression) | Disc visible; turn the player so the disc rotates to heading. | The caption (name+hint) is **screen-stable** — it does NOT spin with the disc interior. It's a child of a non-rotating node, not the rotating container. *(As-built: parented to the disc `_frame`, not `_root` as §3.1 recommended — only `_mapContainer` actually rotates, so `_frame` (the bezel's own host) is equally screen-stable and keeps the caption glued to the disc's layout. Same guarantee, cleaner geometry.)* |
| **AT-DISC-INTACT** (regression) | Disc visible. | The disc's render/clip/fog (§2E.5, PR #192/#197) and the player-marker chevron (AT-DISC-MARKER-1, PR #192) are **unchanged** — the caption sits BELOW the bezel and clobbers neither the disc face nor the chevron. |
| **AT-MODAL-TITLE-INTACT** (regression) | Open the full-screen modal (M). | The modal's top-centre cartouche still shows the **BARE** name (§2A.6c) — the caption split (formatted under disc, bare in modal) is intact; the modal is untouched. |
| **AT-REBIND-CORRECT** (regression) | Rebind the Map key to e.g. "N". | The under-disc hint reads "[N] …"; nomap stays enforced (no vanilla material/root mutation). |
| logs-green != playable | — | Daniel's in-game GPU eyeball on the top-right placement + legibility is the real accept. |

> **Build gate:** 0 errors / 0 warnings (§4.5). Added to the playtest ledger for in-game
> verify (local solo or Niflheim), like the chevron card.

---

## 6. Shared-surface coordination (do NOT clobber the disc)

The carry minimap disc is a hot, multi-card surface. This caption sits UNDER the disc; two
sibling elements live ON it:
- **Chevron player-marker** (`t_efe8b32b` / AT-DISC-MARKER-1) — **SHIPPED in PR #192 ->
  v0.2.27**. It's on the disc FACE (`_overlayLayer`, dead-centre); the caption is BELOW the
  bezel on `_root`. Different layers, no collision — but both ride `MapSurface`/`MapViewer`, so
  the **same engineer-ui worker** should hold this card to keep the disc-surface edits coherent.
- **Render-correctness foundation** (`t_ba31ad30` / §2E.5) — **SHIPPED in PR #192 + #197 ->
  v0.2.27/0.2.28**. The disc's backing/clip/fog this caption sits beneath is already correct in
  the build Daniel is playing; this card builds on top of it.

Both foundations are already in `main`, so this card has **no blocking parent** — it lands
directly on `origin/main`, one worktree, same engineer-ui worker who owns the disc surface.

---

## 7. Cross-doc edits this change carries (spec-first — same PR as the impl)

Per AGENTS.md ("spec and code change together"), the **implementation PR** that builds §4
also lands these doc edits so no spec contradicts the shipped behavior. (The spec-pass PR that
introduces THIS doc lands the §2 addendum + the §5 supersede banner; the impl PR flips any
remaining present-tense "bottom-centre prompt" prose.)

1. **`map-provider-model.md` §2** — add a 🟢 DECIDED addendum (Daniel 2026-06-19): the bound
   minimap disc carries its map's NAME + a co-located `[M]` open-hint as a caption UNDER the
   disc; visibility tracks disc-visibility (provider bound + nomap-ON), not equip state. This
   is the net-new line the locked §2 minimap model did not previously specify.
2. **`cartography-impl-spec.md` §2H.1** — add the AT-MAPNAME-UNDER-DISC / AT-MKEY-HINT-COLOCATED
   /AT-HINT-VISIBILITY named acceptance tests beside AT-DISC-MARKER-1 (the disc-overlay AT
   cluster), and a one-line note in the disc-marker/caption prose that the disc now carries an
   under-bezel name+hint caption (screen-stable, below the rotating interior).
3. **`local-map-mkey-open-impl-spec.md` §5** — add a supersede banner: the equipped HUD prompt's
   **PLACEMENT** (bottom-centre, equipped-only) is **relocated under the disc** + widened to
   disc-visibility by THIS doc; the `$KEY_Map` token + rebind-correctness STAND unchanged.
   Resolves that doc's §9 open Q1 (carried-but-unequipped prompt) — the answer is YES, the
   relocated caption shows for a bound carried map (the asymmetry fix, §2.4 here).
4. **`docs/v2/planning/index.md` + `README.md`** — add this doc (two-file rule; §9 below).

---

## 8. Open questions for Daniel (route at review — do NOT stamp)

The card body flagged five; this spec RESOLVES four with explicit recommendations (Daniel's
in-game eyeball is the final judge) and carries them as confirm-on-playtest:

1. **Move vs keep both (Q1)** — 🟢 RESOLVED as **MOVE**: delete the bottom-centre canvas, the
   caption is the only open-hint (§3, §4.1). Confirm it's a move, not a duplicate.
2. **Visibility model (Q2)** — RECOMMEND **disc-visibility** (bound + nomap-ON), the asymmetry
   fix (§3.3). One-liner to flip to equipped-only if Daniel prefers after seeing it.
3. **nomap-OFF (Q3)** — RESOLVED: **no caption in nomap-OFF** (no SBPR disc there); rides the
   disc gate (§3.5). nomap-OFF discoverability deferred (no vanilla-minimap mutation).
4. **Name format (Q4)** — RECOMMEND **FormatDisplayName** ("Local map for <Table>"), name 18 px
   / hint 16 px, overflow-don't-truncate (§3.4). Build-calibration knob; Daniel's eyeball tunes.
5. **Blank/unimprinted provider (Q5)** — RESOLVED: the caption rides the disc's `IsImprinted`
   gate (no disc -> no caption); a bound-but-unnamed map shows the hint line only, no name line
   (§3.4 missing-name).

**New question surfaced (route, do not stamp):**
- **Pathological long name width** — overflow is centred + unbounded (§3.4). If a very long
  Table name reads badly under the disc, a max-width/ellipsis knob is a follow-up, not a launch
  blocker. Flag for Daniel's eyeball.

---

## 9. Docs placement (sbpr-docs-conventions)

- This file: `docs/v2/planning/local-map-disc-name-hint-impl-spec.md` (version-scoped buildable
  spec, same shelf as the sibling cartography specs).
- **Two-file rule:** add a row to `docs/v2/planning/index.md` (manifest) and a bullet to
  `docs/v2/planning/README.md` (narrative). Both updated in the spec-pass PR.
- SpecCheck: **+0** (§0).
