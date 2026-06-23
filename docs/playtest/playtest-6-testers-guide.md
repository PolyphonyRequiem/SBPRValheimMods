---
title: "SBPR Trailborne — Playtest #6 Testers Guide"
status: current
purpose: "Playtest #6 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.34-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #6 Testers Guide

**Build:** SBPR Trailborne 0.2.34 (current `main`, ahead of `v0.2.34-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-22 22:18 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.34-playtest`. The
> **Playtest #6** number is the human-facing testing series — distinct from the
> `vX.Y.Z-playtest` build tags.

---

## 1. Install on your client (one-time per build)

**Easiest — the one-line installer** (copies Valheim to a separate modded folder;
your vanilla install is never touched; bundles BepInEx + Trailborne +
ServerDevcommands and prints the live join code):

- **Windows (PowerShell):**
  ```powershell
  iwr https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.ps1 -UseBasicParsing | iex
  ```
- **Linux / macOS (bash):**
  ```bash
  curl -fsSL https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.sh | bash
  ```

Both verify the modpack SHA256 before installing and write a launcher
(`Play Trailborne` shortcut / `run-trailborne.sh`). Pass `--no-console` (bash) /
`-NoConsole` (PS1) to omit the F5 dev console.

**Manual alternative:** install BepInExPack_Valheim (r2modman or manual), then copy
this build's `BepInEx/plugins/SBPR.Trailborne/` from the release zip into your install.

Either way, launch Valheim and confirm the BepInEx console logs
`Loading [SBPR Trailborne 0.2.34]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

> Build target: **`v0.2.35-playtest`** (SBPR Trailborne 0.2.35) — the **second** build carrying Playtest #6
> (counter HELD; `v0.2.34-playtest` was the first #6 build). Test **local solo** on a fresh client build
> unless an item says otherwise.
>
> **Round 1 (items 1–6)** — Daniel-feedback fixes that merged to `main` and shipped in `v0.2.34-playtest`
> (cut 2026-06-22 08:38 PDT) after Daniel's **Playtest #5** run on the `v0.2.33` client.
>
> **Round 2 (items 7–12)** — fixes from Daniel's **Playtest #6 run on the `v0.2.34` client** (2026-06-22):
> the world-halo trophies were too far to read (#248), the empty-state ring was a flat screen-space circle
> not a 3D pulsing corona (#254), the modal cursor STILL locked on his keyboard+mouse rig — 3rd report (#247),
> the held Local Map rendered as a Hoe (#253), the minimap disc vanished after relog while carried (#251),
> and the Painted Sign re-charged for unchanged slots (#243). These ship in `v0.2.35-playtest`. Counter stays
> at **#6** — these are round-2 refinements of the #6 surfaces, not a new testing series.
>
> _The Portal Seed row (now #13) is carried forward unverified — it shipped no code change, so it is **not**
> archived as a prior surface._

### 🆕 Round 1 — Daniel-feedback fixes from Playtest #5, shipped in `v0.2.34-playtest` (first Playtest #6 build)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Iron Compass equipped-rim → neutral medium grey `#6B6B6B`** | t_540ace8c (#236) | ✅ merged to `main` (`e4c2100`); ships in `v0.2.34`; awaiting Daniel | Worn Iron Compass + an SBPR map surface showing → the equipped bezel **rim now reads as a neutral medium grey ≈ `#6B6B6B`** (RGB 107/107/107), **not** the prior muddy dark brown-grey (`#685F4D`). Root cause: `CIronTint` **multiplies** the warm bronze-baked bezel, so the old `(0.66,0.68,0.72)` constant landed dark; retuned to `(0.677,0.764,1.0)` so tint × base = neutral grey. **AT-COMPASS-RIM-COLOR** — Daniel's GPU eyeball is the accept (an explicit *tune-by-eye against a real iron item* tunable; capped at RGB ≤ 107 by the base's blue channel — a lighter neutral needs the shared base lifted). Unworn bezel (`Color.white`) unchanged. logs-green ≠ playable — closes t_540ace8c. |
| 2 | **Map-surface N-glyph + cardinal ticks lifted in front of the iron bezel** | t_3f7f3a0f (#233) | ✅ merged to `main` (`a6d9527`); ships in `v0.2.34`; awaiting Daniel | Worn compass + map surface → the orbiting **N-glyph + E/S/W cardinal ticks now render in front of the iron bezel band** (on the rim), no longer occluded behind it. Fix gave `_northLayer` its own nested Canvas (`overrideSorting`, `sortingOrder = SortingOrder+1`) so it lifts above the bezel while still riding `+rotZ` for the orbit (surface-relative +1, so disc N=3001 stays below the modal surface=5000). **AT-COMPASS-N-ZORDER** — confirm N + ticks sit **on** the bezel on both the carry-disc **and** the (M) modal; turn N→E→S→W and the N still orbits to true map-north with ticks following. Daniel's eye is the accept. logs-green ≠ playable — closes t_3f7f3a0f. |
| 3 | **Sunstone Lens minimap representation richened — trophies + tint + stars + off-edge rim** | t_aab051ae (#238) | ✅ merged to `main` (`70ab72b`); ships in `v0.2.34`; awaiting Daniel | Equip + solar-charge the Lens, approach hostiles **with a minimap present** (carry-disc or vanilla corner). The minimap detection overlay now matches the HUD ring's richness: **(a)** `MinimapBlipStyle` default flips **Dots→Trophy** (aggro-tinted trophy art; Dots still selectable in Config); **(b)** **star pips** appear above each blip (level-1 hostiles show none), aggro-tinted; **(c)** **off-window threats are clamped to the bezel rim** and drawn smaller instead of dropped (new `BoundedMapMath.ClampToRimPx`); **(d)** the aggro tint rides the trophy. **AT-LENS-MINIMAP-RICH** on the disc **and** the vanilla corner. Supersedes the #5 handoff's *dots / no stars / no rim* representation (spec §5 knob-2 re-locked 2026-06-21). Build 0/0, tests 231/231; render is GPU-only — Daniel's look is the accept. logs-green ≠ playable — closes t_aab051ae. |
| 4 | **Sunstone Lens detection radius 30 m → 50 m** | t_4b9f8889 (#234) | ✅ merged to `main` (`dd680eb`); ships in `v0.2.34`; awaiting Daniel | Equip the Lens; confirm hostile **detection now reaches 50 m** (was 30 m) on **all three** surfaces (HUD head-halo, carry-disc, vanilla-minimap handoff) — single knob `DefaultDetectRadius` 30→50, one sweep feeds all. **AT-LENS-RADIUS-50** — spawn a hostile ~40–45 m out and confirm it's detected (silent at 30 m). The disc inner geometry widened (~48%→~80% of the disc); the iron-compass N (~94 px) vs Sunstone blip zone (~80 px) margin narrows to ~14 px — **flagged: verify the two are still disjoint** (no overlap) by eye. logs-green ≠ playable — closes t_4b9f8889. |
| 5 | **SBPR modal cursor-capture — the real fix (`IsMouseActive` postfix) + inventory-open suppress** | t_f7a5ad53 / t_a1cf35b0 (#237) | ✅ merged to `main` (`142b740`); ships in `v0.2.34`; awaiting Daniel | **The real cursor-capture fix** (supersedes the reverted §2L.7-R). Open each SBPR modal — **Local Map full view (M)**, **Surveyor's Table**, **sign panels**: the cursor is **free to move and click** (pins, swatches) and does **not** snap to screen-centre — **even with a Steam-Input virtual gamepad / drifting stick connected** (root cause: the Input System flipped the active source to Gamepad and re-locked every frame; the fix postfixes `ZInput.IsMouseActive`→true while a modal is open so vanilla's own `UpdateCursor` computes `lockState=None`). On **close**, the cursor re-locks exactly once (no stuck-free cursor). **AT-CURSOR-NOSNAP-ALL-MODALS** + **AT-CURSOR-RELOCK**. Sibling (t_a1cf35b0): the **Inventory hotkey cannot open over an SBPR modal** — **AT-INV-SUPPRESS**. Build 0/0, 226/226. logs-green ≠ playable — closes t_f7a5ad53 + t_a1cf35b0. |
| 6 | **Sunstone Lens standalone ring → world-space eidetic head-halo render** | t_d17d9b58 (#242) | ✅ merged to `main` (`05c53cb`); ships in `v0.2.34`; awaiting Daniel | Major rework — the standalone (**no-minimap**) Sunstone ring is now a **diegetic world-space head-halo of billboarded creature trophies** floating around the player's eye-point, replacing the screen-space camera-relative trophy ring. With **no** minimap present, equip + charge the Lens and approach hostiles: **(a)** trophies float in a tight halo around your head (`Character.GetEyePoint`), rarely occluded by terrain (honest depth, no through-wall material); **(b)** each trophy's radius + scale vary with distance (closer = nearer + bigger); **(c)** trophy-less creatures fall back via a variant→sibling remap (Greyling→Greydwarf …) then a generic threat glyph — a startup `DumpUnmappedCreatures` scan logs any unmapped; **(d)** trophies are **flat billboarded** `m_icons[0]` sprites (vanilla `Billboard`, `m_vertical`), not 3D meshes; **(e)** the faint solar empty-state ring stays screen-space. **AT-LENS-HALO-1..5**. **Supersedes Playtest #5 item 3's camera-relative ring fallback.** Host stays active (#209 invariant; only `_worldContent` toggles). Render is GPU-only — Daniel's in-world look is the accept; large rework, verify per PR #242 / t_d17d9b58 before filing fixes. logs-green ≠ playable — closes t_d17d9b58. |

### 🆕 Round 2 — fixes from Daniel's Playtest #6 run on the `v0.2.34` client, ship in `v0.2.35-playtest`

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 7 | **Sunstone Lens world-halo — FIXED distance + 10 m scale knee (trophies were too far to read)** | t_10bacccf (#248) | ✅ merged to `main` (`21f7a92`); ships in `v0.2.35`; awaiting Daniel | Daniel on `v0.2.34`: *"creatures should be at a FIXED distance from the player but grow in scale from .25 at the far edges to 1.0 when within 10 m. Right now they're seemingly too far from the player to be clearly visible."* The #242 halo used a variable radius **and** scale both ∝ distance (far = pushed away AND shrunk to ~0.12 = invisible). Now: every trophy sits at one **fixed `HaloRadius` (~2.0 m)** from the eye-point — no range push; **scale carries all range info** with a knee: enemy **≤10 m → full scale (1.0× = `HaloScaleMax`)**, enemy at the **50 m detection edge → 0.25×**, linear between. Equip + solar-charge the Lens with **no minimap present**, approach hostiles at varied ranges: **(a)** all trophies sit at the same distance from your face (a true fixed-distance ring), **(b)** a close (<10 m) enemy reads full-size, a far (~50 m) enemy reads ~quarter-size but is **clearly visible** (not pushed away/tiny), **(c)** size shrinks smoothly with distance, **(d)** placement still along the real bearing to each enemy (camera-relative, no north frame). 10 m knee + 0.25 floor + 1.0 ceiling are **Daniel-locked**; `scaleNear` stays the eyeball tunable (live-config). **AT-HALO-FIXED-DIST / AT-HALO-SCALE-KNEE** (21 xUnit cases green; render is GPU-only — Daniel's eye is the accept). logs-green ≠ playable — closes t_10bacccf. |
| 8 | **Sunstone Lens empty-state ring → world-space 3D pulsing sun-corona disc** | t_9d7c3dfe / bug t_2d500d45 (#254) | ✅ merged to `main` (`e155ba8`); ships in `v0.2.35`; awaiting Daniel | Daniel on `v0.2.34`: *"the ring itself is just a screen-space circle, not a 3D slowly pulsing 'sun corona' disc like we discussed."* Replaces the flat screen-space empty-state annulus with a **world-space corona disc** sharing the halo's scene root, breathing on a slow alpha pulse. With the Lens equipped + charged and **no hostiles detected** (empty state): **(a)** the corona renders in **world space** (not a flat screen overlay) — default orientation **GroundPlane**: a flat disc on the ground at the player's feet ("sun on the floor", `Quaternion.Euler(90,0,0)`); the alternate **CameraFacing** (billboarded to the eye) is a live-config flip; **(b)** it **pulses** — alpha breathes slowly between a trough and peak (not static); **(c)** colour is **warm gold** (`CSolarRing` = RGB ≈ **250, 199, 92** — i.e. `0.98, 0.78, 0.36`; a soft amber/yellow, NOT white or red); **(d)** a radial soft-edged glow (inner-fill 0=hoop ↔ 1=filled disc); **(e)** when a threat appears the trophy halo (#248) rides the **same** root — one shared show/hide lifecycle, the **#209 host-pump invariant holds** (world-content toggles, host never deactivates). All knobs (orientation, pulse rate/depth, fill, thickness, radius) are reversible live-config — Daniel converges the look by eye. **AT-CORONA-3D / ORIENT / PULSE / ART / GATED / SUBSTRATE / PUMP** (AT-CORONA-PULSE-MATH 12 cases + build green headless; the look is Daniel's in-game accept). logs-green ≠ playable — closes t_2d500d45 / t_9d7c3dfe. |
| 9 | **SBPR modal cursor-capture — source-independent re-locker (3rd report; the KB+M rig the #237 fix missed)** | t_cad2c6f3 (#247) | ✅ merged to `main` (`14c9390`); ships in `v0.2.35`; awaiting Daniel | **3rd report of this family.** The #237 fix (`MouseActiveForcePatch`) only freed the cursor when an input-source flip drove vanilla's `UpdateCursor` — true on a Steam-Input virtual-gamepad rig (source flips every frame), but on **Daniel's keyboard+mouse box (no controller, Steam Input off)** the source never flips, so the forced flag was never read and the cursor stayed **locked** (mouselook froze = modal detected, but cursor couldn't move to click swatches/pins, and inventory-open is now suppressed = no escape hatch). New fix: a **`ModalCursorDriver`** MonoBehaviour on the `Hud` asserts `lockState=None`+`visible=true` in **both `Update()`** (early enough to beat Unity's pointer center-snap) **and `LateUpdate()`**, every frame any SBPR modal is open, with a one-shot gameplay-lock restore on close — **no dependency on any input source**. Open each SBPR modal — **Local Map full view (M)**, **Surveyor's Table**, **sign paint panel** — on a **plain keyboard+mouse setup**: the cursor is **free to move and click** pins/swatches and does **not** snap to screen-centre; on close it re-locks exactly once (no stuck-free cursor). `SBPR_CursorDiag` (default ON this build) logs the incoming `lockState` every ~30 frames to make the accept ground-truth. **AT-CURSOR-NOSNAP-ALL-MODALS** + **AT-CURSOR-RELOCK**, specifically on a **no-controller** rig. logs-green ≠ playable — closes t_cad2c6f3. |
| 10 | **Local Map held mesh — procedural blank-leather field-map replaces the Hoe donor mesh** | t_64dff55f / parent t_2fb48391 (#253) | ✅ merged to `main` (`e0219ad`); ships in `v0.2.35`; awaiting Daniel | Daniel on `v0.2.34`: the craftable **Local Map** rendered **in-hand as the vanilla Hoe** (long handle + stone blade — a gardening tool, not a map). The item clones the Hoe for valid two-handed held rigging but never replaced the visible mesh. Daniel picked **Option C** (procedural map-appropriate mesh; A=graft table parchment and B=reuse a vanilla held map were both falsified — no separable parchment, no vanilla held-map mesh). Fix authors a fresh **leather field-map sheet** (the repo's first `new Mesh()` — lightly-folded double-sided quads) reading the vanilla **`leatherscraps`** material as a blueprint, installed under the kept `attach` anchor so equip + minimap binding (incl. the #251 relog fix) are untouched. **Equip the Local Map and look at it in-hand (F-key), and drop one on the ground:** it reads as a **flat folded leather sheet/map**, NOT a hoe (handle+blade gone). Disambiguation is **silhouette/value** (flat sheet vs handle+blade), not hue — the leather is grain/value (Daniel is colorblind; no colour cue relied on). The mesh form (size/fold) is a deliberate first form + polish knob — tell me if it wants reshaping. **AT-HELD-MESH** (in-hand + world-drop). Build 0/0; mesh-geometry invariants validated offline; render is Daniel's eye. logs-green ≠ playable — closes the impl half of t_64dff55f (parent bug t_2fb48391). |
| 11 | **Local Map provider binding persists across relog — cold-start carry re-derivation latch** | t_85f45dd7 / bug t_5fc02f00 (#251) | ✅ merged to `main` (`4d9c251`); ships in `v0.2.35`; awaiting Daniel | The SBPR minimap **disc vanished after logout/login** for a **carried-but-unequipped** imprinted Local Map (equipped-at-logout self-healed via vanilla re-equip; carried-unequipped could not — a fresh controller starts each session with no provider and nothing re-derived it on load). Violated the locked **AT-MAP-DURABLE**. Fix: a **one-shot per-session cold-start latch** (`_coldStartResolved`) — once per session, if no provider is bound, re-derive from (a) equipped local map, else (b) the **first carried imprinted** local map in slot order, else null. **The load-bearing invariant (§3.4):** the scan runs **once per session**, NOT "re-derive whenever provider is null" — so an in-session drop→re-pickup still stays unbound. Verify on a GPU client: **AT-PERSIST-CARRY** — imprint a map → unequip but **keep** it → log out and back in → **the disc is there without re-equipping**; **AT-PERSIST-UNBIND-INTACT** (regression) — after relog: drop → disc gone → re-pickup WITHOUT equip → **no disc** → re-equip → disc returns; **AT-PERSIST-MULTI** — two carried imprinted maps → one deterministic disc (slot order), never blank/random; **AT-PERSIST-BLANK** — blank carried map → no disc, no error. No `LocalMap.cs` change, no new customData key, no new Harmony patch, SpecCheck +0. logs-green ≠ playable — closes t_85f45dd7 (bug t_5fc02f00). |
| 12 | **Painted Sign — consume cost per CHANGED slot, not per filled slot** | t_6df12ca8 / bug t_e59a4fd6 (#243) | ✅ merged to `main` (`3c865f4`); ships in `v0.2.35`; awaiting Daniel | Bug: `{Paint this and consume}` charged **1 pigment per FILLED slot**, so opening an already-painted sign, changing **one** slot, and committing re-charged you for the two **unchanged** slots; re-applying identical colours charged for every filled slot. Fix charges only for slots whose new colour **differs** from the stored ZDO colour (Daniel-locked: *"1) disabled. 2) free"*). Verify at a Painted Sign: **AT-1** paint a fresh sign → full cost (1 per non-empty slot); **AT-3 (no-op)** re-open, change nothing, the **Paint button is silently DISABLED** (no message); **AT-6** re-apply the **same** colours → free; change **one** slot → charged for **exactly that one** slot, unchanged slots free; **AT-5 (pure clear)** set a painted slot back to `∅ None` → button **enabled**, commits, reverts that surface to bare wood, **consumes nothing**; **AT-7 (displayed==consumed)** the post-paint message shows exactly what was consumed (the old post-write recompute-reads-0 trap is fixed). The #228 three-slot model + #224 per-renderer MPB tint are untouched. **AT-1..AT-9** (20 xUnit cases green; engine-bound consumption/UX is Daniel's in-game accept). logs-green ≠ playable — closes t_6df12ca8 (bug t_e59a4fd6). |

### 🔁 Carried forward — not yet shipped / not yet verified

Shipped **no** code change in any tag (blocked / verify-only), so it carries into #6 rather than being archived as a #5 surface.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 13 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **Round 2 (items 7–12) are the `src/**/*.cs` changes in the `v0.2.34-playtest..main` window** (the build Daniel
  played → the build carrying his round-2 fixes): **#248** (`21f7a92`, t_10bacccf, item 7), **#254** (`e155ba8`,
  t_9d7c3dfe / t_2d500d45, item 8), **#247** (`14c9390`, t_cad2c6f3, item 9), **#253** (`e0219ad`, t_64dff55f, item 10),
  **#251** (`4d9c251`, t_85f45dd7, item 11), **#243** (`3c865f4`, t_6df12ca8, item 12). All **six** map to a PENDING row
  → `python3 scripts/gen-playtest-guide.py --check` (diffs `v0.2.34-playtest..main`) is **green (0 unledgered)**.
  Docs-only / tooling commits in the same window are EXEMPT: **#246** (`adafcc9`, config bake-down classification,
  `docs:`), **#227** (`a6e7b80`, gen-playtest revert-net tooling, `fix(tooling)` — current main tip).
- **Round 1 (items 1–6)** are the `v0.2.33..v0.2.34` window (already cross-checked at the prior cut): **#236** (item 1),
  **#233** (item 2), **#238** (item 3), **#234** (item 4), **#237** (item 5), **#242** (item 6).
- **Supersession notes:** item 7 (#248 fixed-distance halo) **refines** item 6 (#242 world-space head-halo) — same
  surface, Daniel's round-2 geometry lock; item 8 (#254 corona) **replaces** the flat screen-space empty-state ring with
  a world-space pulsing disc; item 9 (#247 source-independent re-locker) **supersedes** item 5 (#237) on the
  keyboard+mouse rig the #237 fix structurally missed; item 11 (#251 relog-persist) is a **new** durability surface;
  item 12 (#243) **refines** the Painted Sign cost basis (#228 three-slot + #224 MPB tint untouched).

### ⏳ In-flight (will join PENDING when merged)

- **Open PRs** not yet on `main` (become the next-build candidates when merged): **#255** persistent corona aura —
  lens-live cue survives the minimap handoff (t_7416e5b9).
- **Reported-but-unmerged** bugs (blocked/open cards, **no** shipped code yet → not test items until they merge):
  Eikthyr boss pin = yellow square + raw `$enemy_eikthyr` label (t_5c3944cd).
- **Closed without merge** (dropped, not a candidate): **#241** Sunstone Lens pulsing solar aura impl-spec (t_e4a6f559) —
  superseded by the shipped corona #254 / the open #255 aura.

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.34-playtest**: **6**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #6 in the ledger, bumps the counter, and opens the Playtest #7 planning card.
