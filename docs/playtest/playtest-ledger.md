---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 6            # next playtest is #6 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.39-playtest
---

# SBPR Trailborne — Playtest Ledger

This is the single source of truth for **what needs to be tested next**. It is
deliberately decoupled from the `vX.Y.Z-playtest` git tags: those are *build*
markers; the **Playtest #N** counter here is the *human-facing* testing series.

## How this stays reliable (read before editing)

1. **Items are added as work merges, not from memory.** Two feeders:
   - **Auto (ground truth):** `scripts/gen-playtest-guide.py` derives candidate items
     from `git log <last_playtest_tag>..main -- 'src/**/*.cs'` — every code change since
     the last playtest is a candidate test item, even if nobody added it by hand.
   - **Manual (judgment):** specific test instructions a commit message can't capture
     (exact steps, what "correct" looks like, edge cases) go in **PENDING** below.
     Kanban workers append here on a review-required handoff; the orchestrator appends
     on merge.
2. **The cron `sbpr-playtest-planner` fires when a new `-playtest` tag lands** → it
   archives PENDING under the shipped Playtest #N, bumps `playtest_counter`, updates
   `last_playtest_tag`, and creates the next-playtest **planning card** on the board.
   Items that did **not** actually ship in the tag (still `todo`/`blocked`/unmerged) are
   **not** archived as shipped — they carry forward into the next PENDING.
3. **Nothing is "tested" until Daniel says so in-game** (logs-green ≠ playable). The
   guide produces a checklist; Daniel's run fills it in.

---

## PENDING — accrues for the next playtest (Playtest #6)

> Build target: **`v0.2.38-playtest`** (SBPR Trailborne 0.2.38) — the **fifth** build carrying Playtest #6
> (counter HELD across the whole #6 line: `v0.2.34` first, `v0.2.35` second, `v0.2.36` third, `v0.2.37` fourth,
> `v0.2.38` fifth). Test **local solo** on a fresh client build unless an item says otherwise.
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
> **Round 3 (item 13)** — fix from Daniel's **Playtest #6 run on the `v0.2.35` client** (2026-06-23): the SBPR
> custom-UI cursor STILL locked on his keyboard+mouse rig — **4th report** of the family — because every prior
> build *wrote* `Cursor.lockState`, but the capture lives **below** managed lockState (SDL relative-mouse on the
> native Linux player). The §2L.18 fix **stops writing lockState** and instead **masquerades as a vanilla GUI**
> (mouse-capture block + `TextInput.IsVisible()`→true) so vanilla itself frees the pointer (#257). Ships in
> `v0.2.36-playtest`. Counter stays at **#6** — round-3 refinement of the same #6 cursor surface (item 5 #237 →
> item 9 #247 → this), not a new testing series. **Daniel confirmed it fixed in-game 2026-06-23** — this build
> ships it to the rest of the testers.
>
> _The Portal Seed row (now #14) is carried forward unverified — it shipped no code change, so it is **not**
> archived as a prior surface._

### 🆕 Round 1 — Daniel-feedback fixes from Playtest #5, shipped in `v0.2.34-playtest` (first Playtest #6 build)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Iron Compass equipped-rim → neutral medium grey `#6B6B6B`** | t_540ace8c (#236) | ✅ merged to `main` (`e4c2100`); ships in `v0.2.34`; awaiting Daniel | Worn Iron Compass + an SBPR map surface showing → the equipped bezel **rim now reads as a neutral medium grey ≈ `#6B6B6B`** (RGB 107/107/107), **not** the prior muddy dark brown-grey (`#685F4D`). Root cause: `CIronTint` **multiplies** the warm bronze-baked bezel, so the old `(0.66,0.68,0.72)` constant landed dark; retuned to `(0.677,0.764,1.0)` so tint × base = neutral grey. **AT-COMPASS-RIM-COLOR** — Daniel's GPU eyeball is the accept (an explicit *tune-by-eye against a real iron item* tunable; capped at RGB ≤ 107 by the base's blue channel — a lighter neutral needs the shared base lifted). Unworn bezel (`Color.white`) unchanged. logs-green ≠ playable — closes t_540ace8c. |
| 2 | **Map-surface N-glyph + cardinal ticks lifted in front of the iron bezel** | t_3f7f3a0f (#233) | ✅ merged to `main` (`a6d9527`); ships in `v0.2.34`; awaiting Daniel | Worn compass + map surface → the orbiting **N-glyph + E/S/W cardinal ticks now render in front of the iron bezel band** (on the rim), no longer occluded behind it. Fix gave `_northLayer` its own nested Canvas (`overrideSorting`, `sortingOrder = SortingOrder+1`) so it lifts above the bezel while still riding `+rotZ` for the orbit (surface-relative +1, so disc N=3001 stays below the modal surface=5000). **AT-COMPASS-N-ZORDER** — confirm N + ticks sit **on** the bezel on both the carry-disc **and** the (M) modal; turn N→E→S→W and the N still orbits to true map-north with ticks following. Daniel's eye is the accept. logs-green ≠ playable — closes t_3f7f3a0f. |
| 3 | **Sunstone Lens minimap representation richened — trophies + tint + stars + off-edge rim** | t_aab051ae (#238) | ✅ merged to `main` (`70ab72b`); ships in `v0.2.34`; awaiting Daniel | Equip + solar-charge the Lens, approach hostiles **with a minimap present** (carry-disc or vanilla corner). The minimap detection overlay now matches the HUD ring's richness: **(a)** `MinimapBlipStyle` default flips **Dots→Trophy** (aggro-tinted trophy art; Dots still selectable in Config); **(b)** **star pips** appear above each blip (level-1 hostiles show none), aggro-tinted; **(c)** **off-window threats are clamped to the bezel rim** and drawn smaller instead of dropped (new `BoundedMapMath.ClampToRimPx`); **(d)** the aggro tint rides the trophy. **AT-LENS-MINIMAP-RICH** on the disc **and** the vanilla corner. Supersedes the #5 handoff's *dots / no stars / no rim* representation (spec §5 knob-2 re-locked 2026-06-21). Build 0/0, tests 231/231; render is GPU-only — Daniel's look is the accept. logs-green ≠ playable — closes t_aab051ae. |
| 4 | **Sunstone Lens detection radius 30 m → 50 m → 70 m** | t_4b9f8889 (#234 + 70 m re-tune) | ✅ 50 m shipped `v0.2.34` (`dd680eb`); **Daniel ACCEPTED in-game 2026-06-24 and re-tuned the standard to 70 m** — 70 m merge lands on `main` now, ships in the **next** build | Equip the Lens; confirm hostile **detection reaches 70 m** (was 50 m, was 30 m) on **all three** surfaces (HUD head-halo, carry-disc, vanilla-minimap handoff) — single knob `DefaultDetectRadius` → 70, one sweep feeds all. **AT-LENS-RADIUS-70** — spawn a hostile ~60–65 m out and confirm it's detected. 🔴 **Geometry crossed a boundary at 70 m:** detection (70 m) now **exceeds** the ~62.5 m visible disc, so the 62.5–70 m band rim-clamps into the bezel (no longer the old "inner ~80 %, disjoint from compass-N by construction" — that margin is **closed**). **Flagged for Daniel's eye:** does a max-range threat blip on the iron-compass N read as cluttered? (tunable via `ThreatRimInset` / blip size / N-glyph priority — not a blocker). logs-green ≠ playable. |
| 5 | **SBPR modal cursor-capture — the real fix (`IsMouseActive` postfix) + inventory-open suppress** | t_f7a5ad53 / t_a1cf35b0 (#237) | ✅ merged to `main` (`142b740`); ships in `v0.2.34`; awaiting Daniel | **The real cursor-capture fix** (supersedes the reverted §2L.7-R). Open each SBPR modal — **Local Map full view (M)**, **Surveyor's Table**, **sign panels**: the cursor is **free to move and click** (pins, swatches) and does **not** snap to screen-centre — **even with a Steam-Input virtual gamepad / drifting stick connected** (root cause: the Input System flipped the active source to Gamepad and re-locked every frame; the fix postfixes `ZInput.IsMouseActive`→true while a modal is open so vanilla's own `UpdateCursor` computes `lockState=None`). On **close**, the cursor re-locks exactly once (no stuck-free cursor). **AT-CURSOR-NOSNAP-ALL-MODALS** + **AT-CURSOR-RELOCK**. Sibling (t_a1cf35b0): the **Inventory hotkey cannot open over an SBPR modal** — **AT-INV-SUPPRESS**. Build 0/0, 226/226. logs-green ≠ playable — closes t_f7a5ad53 + t_a1cf35b0. |
| 6 | **Sunstone Lens standalone ring → world-space eidetic head-halo render** | t_d17d9b58 (#242) | ✅ merged to `main` (`05c53cb`); ships in `v0.2.34`; awaiting Daniel | Major rework — the standalone (**no-minimap**) Sunstone ring is now a **diegetic world-space head-halo of billboarded creature trophies** floating around the player's eye-point, replacing the screen-space camera-relative trophy ring. With **no** minimap present, equip + charge the Lens and approach hostiles: **(a)** trophies float in a tight halo around your head (`Character.GetEyePoint`), rarely occluded by terrain (honest depth, no through-wall material); **(b)** each trophy's radius + scale vary with distance (closer = nearer + bigger); **(c)** trophy-less creatures fall back via a variant→sibling remap (Greyling→Greydwarf …) then a generic threat glyph — a startup `DumpUnmappedCreatures` scan logs any unmapped; **(d)** trophies are **flat billboarded** `m_icons[0]` sprites (vanilla `Billboard`, `m_vertical`), not 3D meshes; **(e)** the faint solar empty-state ring stays screen-space. **AT-LENS-HALO-1..5**. **Supersedes Playtest #5 item 3's camera-relative ring fallback.** Host stays active (#209 invariant; only `_worldContent` toggles). Render is GPU-only — Daniel's in-world look is the accept; large rework, verify per PR #242 / t_d17d9b58 before filing fixes. logs-green ≠ playable — closes t_d17d9b58. |

### 🆕 Round 2 — fixes from Daniel's Playtest #6 run on the `v0.2.34` client, ship in `v0.2.35-playtest`

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 7 | **Sunstone Lens world-halo — FIXED distance + 10 m scale knee (trophies were too far to read)** | t_10bacccf (#248) | ✅ merged to `main` (`21f7a92`); ships in `v0.2.35`; awaiting Daniel | Daniel on `v0.2.34`: *"creatures should be at a FIXED distance from the player but grow in scale from .25 at the far edges to 1.0 when within 10 m. Right now they're seemingly too far from the player to be clearly visible."* The #242 halo used a variable radius **and** scale both ∝ distance (far = pushed away AND shrunk to ~0.12 = invisible). Now: every trophy sits at one **fixed `HaloRadius` (~2.0 m)** from the eye-point — no range push; **scale carries all range info** with a knee: enemy **≤10 m → full scale (1.0× = `HaloScaleMax`)**, enemy at the **70 m detection edge → 0.25×**, linear between. Equip + solar-charge the Lens with **no minimap present**, approach hostiles at varied ranges: **(a)** all trophies sit at the same distance from your face (a true fixed-distance ring), **(b)** a close (<10 m) enemy reads full-size, a far (~70 m) enemy reads ~quarter-size but is **clearly visible** (not pushed away/tiny), **(c)** size shrinks smoothly with distance, **(d)** placement still along the real bearing to each enemy (camera-relative, no north frame). 10 m knee + 0.25 floor + 1.0 ceiling are **Daniel-locked**; `scaleNear` stays the eyeball tunable (live-config). **AT-HALO-FIXED-DIST / AT-HALO-SCALE-KNEE** (21 xUnit cases green; render is GPU-only — Daniel's eye is the accept). logs-green ≠ playable — closes t_10bacccf. |
| 8 | **Sunstone Lens empty-state ring → world-space 3D pulsing sun-corona disc** | t_9d7c3dfe / bug t_2d500d45 (#254) | ✅ merged to `main` (`e155ba8`); ships in `v0.2.35`; awaiting Daniel | Daniel on `v0.2.34`: *"the ring itself is just a screen-space circle, not a 3D slowly pulsing 'sun corona' disc like we discussed."* Replaces the flat screen-space empty-state annulus with a **world-space corona disc** sharing the halo's scene root, breathing on a slow alpha pulse. With the Lens equipped + charged and **no hostiles detected** (empty state): **(a)** the corona renders in **world space** (not a flat screen overlay) — default orientation **GroundPlane**: a flat disc on the ground at the player's feet ("sun on the floor", `Quaternion.Euler(90,0,0)`); the alternate **CameraFacing** (billboarded to the eye) is a live-config flip; **(b)** it **pulses** — alpha breathes slowly between a trough and peak (not static); **(c)** colour is **warm gold** (`CSolarRing` = RGB ≈ **250, 199, 92** — i.e. `0.98, 0.78, 0.36`; a soft amber/yellow, NOT white or red); **(d)** a radial soft-edged glow (inner-fill 0=hoop ↔ 1=filled disc); **(e)** when a threat appears the trophy halo (#248) rides the **same** root — one shared show/hide lifecycle, the **#209 host-pump invariant holds** (world-content toggles, host never deactivates). All knobs (orientation, pulse rate/depth, fill, thickness, radius) are reversible live-config — Daniel converges the look by eye. **AT-CORONA-3D / ORIENT / PULSE / ART / GATED / SUBSTRATE / PUMP** (AT-CORONA-PULSE-MATH 12 cases + build green headless; the look is Daniel's in-game accept). logs-green ≠ playable — closes t_2d500d45 / t_9d7c3dfe. |
| 9 | **SBPR modal cursor-capture — source-independent re-locker (3rd report; the KB+M rig the #237 fix missed)** | t_cad2c6f3 (#247) | ✅ merged to `main` (`14c9390`); ships in `v0.2.35`; awaiting Daniel | **3rd report of this family.** The #237 fix (`MouseActiveForcePatch`) only freed the cursor when an input-source flip drove vanilla's `UpdateCursor` — true on a Steam-Input virtual-gamepad rig (source flips every frame), but on **Daniel's keyboard+mouse box (no controller, Steam Input off)** the source never flips, so the forced flag was never read and the cursor stayed **locked** (mouselook froze = modal detected, but cursor couldn't move to click swatches/pins, and inventory-open is now suppressed = no escape hatch). New fix: a **`ModalCursorDriver`** MonoBehaviour on the `Hud` asserts `lockState=None`+`visible=true` in **both `Update()`** (early enough to beat Unity's pointer center-snap) **and `LateUpdate()`**, every frame any SBPR modal is open, with a one-shot gameplay-lock restore on close — **no dependency on any input source**. Open each SBPR modal — **Local Map full view (M)**, **Surveyor's Table**, **sign paint panel** — on a **plain keyboard+mouse setup**: the cursor is **free to move and click** pins/swatches and does **not** snap to screen-centre; on close it re-locks exactly once (no stuck-free cursor). `SBPR_CursorDiag` (default ON this build) logs the incoming `lockState` every ~30 frames to make the accept ground-truth. **AT-CURSOR-NOSNAP-ALL-MODALS** + **AT-CURSOR-RELOCK**, specifically on a **no-controller** rig. logs-green ≠ playable — closes t_cad2c6f3. |
| 10 | **Local Map held mesh — procedural blank-leather field-map replaces the Hoe donor mesh** | t_64dff55f / parent t_2fb48391 (#253) | ✅ merged to `main` (`e0219ad`); ships in `v0.2.35`; awaiting Daniel | Daniel on `v0.2.34`: the craftable **Local Map** rendered **in-hand as the vanilla Hoe** (long handle + stone blade — a gardening tool, not a map). The item clones the Hoe for valid two-handed held rigging but never replaced the visible mesh. Daniel picked **Option C** (procedural map-appropriate mesh; A=graft table parchment and B=reuse a vanilla held map were both falsified — no separable parchment, no vanilla held-map mesh). Fix authors a fresh **leather field-map sheet** (the repo's first `new Mesh()` — lightly-folded double-sided quads) reading the vanilla **`leatherscraps`** material as a blueprint, installed under the kept `attach` anchor so equip + minimap binding (incl. the #251 relog fix) are untouched. **Equip the Local Map and look at it in-hand (F-key), and drop one on the ground:** it reads as a **flat folded leather sheet/map**, NOT a hoe (handle+blade gone). Disambiguation is **silhouette/value** (flat sheet vs handle+blade), not hue — the leather is grain/value (Daniel is colorblind; no colour cue relied on). The mesh form (size/fold) is a deliberate first form + polish knob — tell me if it wants reshaping. **AT-HELD-MESH** (in-hand + world-drop). Build 0/0; mesh-geometry invariants validated offline; render is Daniel's eye. logs-green ≠ playable — closes the impl half of t_64dff55f (parent bug t_2fb48391). |
| 11 | **Local Map provider binding persists across relog — cold-start carry re-derivation latch** | t_85f45dd7 / bug t_5fc02f00 (#251) | ✅ merged to `main` (`4d9c251`); ships in `v0.2.35`; awaiting Daniel | The SBPR minimap **disc vanished after logout/login** for a **carried-but-unequipped** imprinted Local Map (equipped-at-logout self-healed via vanilla re-equip; carried-unequipped could not — a fresh controller starts each session with no provider and nothing re-derived it on load). Violated the locked **AT-MAP-DURABLE**. Fix: a **one-shot per-session cold-start latch** (`_coldStartResolved`) — once per session, if no provider is bound, re-derive from (a) equipped local map, else (b) the **first carried imprinted** local map in slot order, else null. **The load-bearing invariant (§3.4):** the scan runs **once per session**, NOT "re-derive whenever provider is null" — so an in-session drop→re-pickup still stays unbound. Verify on a GPU client: **AT-PERSIST-CARRY** — imprint a map → unequip but **keep** it → log out and back in → **the disc is there without re-equipping**; **AT-PERSIST-UNBIND-INTACT** (regression) — after relog: drop → disc gone → re-pickup WITHOUT equip → **no disc** → re-equip → disc returns; **AT-PERSIST-MULTI** — two carried imprinted maps → one deterministic disc (slot order), never blank/random; **AT-PERSIST-BLANK** — blank carried map → no disc, no error. No `LocalMap.cs` change, no new customData key, no new Harmony patch, SpecCheck +0. logs-green ≠ playable — closes t_85f45dd7 (bug t_5fc02f00). |
| 12 | **Painted Sign — consume cost per CHANGED slot, not per filled slot** | t_6df12ca8 / bug t_e59a4fd6 (#243) | ✅ merged to `main` (`3c865f4`); ships in `v0.2.35`; awaiting Daniel | Bug: `{Paint this and consume}` charged **1 pigment per FILLED slot**, so opening an already-painted sign, changing **one** slot, and committing re-charged you for the two **unchanged** slots; re-applying identical colours charged for every filled slot. Fix charges only for slots whose new colour **differs** from the stored ZDO colour (Daniel-locked: *"1) disabled. 2) free"*). Verify at a Painted Sign: **AT-1** paint a fresh sign → full cost (1 per non-empty slot); **AT-3 (no-op)** re-open, change nothing, the **Paint button is silently DISABLED** (no message); **AT-6** re-apply the **same** colours → free; change **one** slot → charged for **exactly that one** slot, unchanged slots free; **AT-5 (pure clear)** set a painted slot back to `∅ None` → button **enabled**, commits, reverts that surface to bare wood, **consumes nothing**; **AT-7 (displayed==consumed)** the post-paint message shows exactly what was consumed (the old post-write recompute-reads-0 trap is fixed). The #228 three-slot model + #224 per-renderer MPB tint are untouched. **AT-1..AT-9** (20 xUnit cases green; engine-bound consumption/UX is Daniel's in-game accept). logs-green ≠ playable — closes t_6df12ca8 (bug t_e59a4fd6). |

### 🆕 Round 3 — fix from Daniel's Playtest #6 run on the `v0.2.35` client, ships in `v0.2.36-playtest`

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 13 | **SBPR modal cursor-capture — §2L.18 masquerade-as-vanilla-GUI (4th report; the fix that finally landed)** | t_94cc9713 (#257) | ✅ merged to `main` (`116021a`); ships in `v0.2.36`; **Daniel-confirmed in-game 2026-06-23** | **4th and final report of the cursor-lock family** (issue-7 → t_f7a5ad53 #237 → t_cad2c6f3 #247 → t_94cc9713 #257). The prior **seven** builds (§2L.4…§2L.17) all *wrote* `Cursor.lockState` and all failed on Daniel's Linux rig: his v0.2.35 diagnostic showed `lockState=None` on ~96% of frames with the cursor **still captive** — the capture is **below** managed `Cursor.lockState` (SDL relative-mouse on the native Linux player), unreachable by any C# lockState write. **§2L.18 stops writing lockState entirely** and instead reproduces what a vanilla GUI-open does (the path that empirically frees the pointer): (1) an edge-driven **mouse-capture block** on `GameCamera.LateUpdate` (`m_mouseCapture=false`+`UpdateMouseCapture()` via Traverse, restored on close — mirrors Jotunn's `GUIManager.BlockInput` *behaviour*, no mod code copied, ADR-0001 clean); (2) **`TextInput.IsVisible()` masquerades `true`** while any SBPR modal is open, so every vanilla cursor-free/input-suppress gate fires for our modal as for a real text dialog (NRE-safe, decomp-verified). Open each SBPR modal — **Local Map full view (M)**, **Surveyor's Table**, **sign paint panel** — on a **plain keyboard+mouse rig**: the cursor is **free to move and click** pins/swatches and does **not** snap to screen-centre, and on close it re-locks cleanly. Also test the **same surfaces with a controller / Steam-Input gamepad connected** (the rig the #237/#247 path covered) — must still free + re-lock. `SBPR_CursorDiag` now defaults **off** (don't ship per-frame cursor logging; the fix runs regardless). **AT-CURSOR-NOSNAP-ALL-MODALS** + **AT-CURSOR-RELOCK** on a no-controller rig. Build 0/0, tests 284/284, docs-lint OK. Daniel already confirmed (*"fixed it 🙂"*, map + sign) — this build ships it to the rest of the testers. logs-green ≠ playable — closes t_94cc9713. |

### 🆕 Round 4 — fixes/features from Daniel's Playtest #6 run on the `v0.2.36` client, ship in `v0.2.37-playtest`

> Counter stays at **#6** — these are continued round refinements + new cartography/sunstone surfaces on
> the #6 build line, not a new testing series. `v0.2.37-playtest` is the **fourth** build carrying Playtest #6.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 15 | **Sunstone corona — flat floor-disc → feet-anchored rising vertical glow (FeetGlow default) + trophy ring 2.0→1.0 m, scale +25%** | _(#264, `ticket-diegetic-halo-render`; direct /bug — no card)_ | ✅ merged to `main` (`3489ec6`); ships in `v0.2.37` | Daniel /bug on `v0.2.36`: the corona *"clips through the terrain and seems perfectly flat … prefer a more 'substantive' glow that starts around the feet, full width ~0.5 m from ground, doesn't hard-clip."* Equip + solar-charge the Lens with **no hostiles** (empty state): **(a)** the corona now rises **from your feet** as an upright camera-facing glow (new default `CoronaOrientation.FeetGlow`) — soft at the ground (no hard terrain-clip line), narrow bright core blooming to full width by ~0.5 m, soft dome above; GroundPlane + CameraFacing still live-selectable; **(b)** still **pulses** + warm-gold, #209 host-pump invariant holds. Then approach hostiles: **(c)** the trophy head-halo now sits **tighter — 1.0 m** from your eye-point (was 2.0 m) and trophies are **~25% bigger** (`HaloScaleMax` 0.6→0.75, lifts the whole 10 m-knee curve incl. the 0.25× edge floor). **AT-CORONA-FEET** (rises from feet, full-width ~0.5 m, no hard clip, breathes) + **AT-HALO** ring-1 m/scale. Render is GPU-only — Daniel's eye is the accept. logs-green ≠ playable. |
| 16 | **Sunstone minimap threat-blip +75% (14→24.5 px) + live knob, single-homed metrics** | t_bc017af4 (#260) | ✅ merged to `main` (`10ed143`); ships in `v0.2.37` | Daniel on `v0.2.36` (`ticket-sunstone-minimap-render`): *"minimap icons notably too small, ~75% larger."* Equip + charge the Lens, approach hostiles **with a minimap present** (SBPR carry-disc **and** vanilla corner): threat blips now render **~75% larger (14→24.5 px)** on **both** surfaces, sized from one shared `MinimapThreatMetrics` home (homed in Cartography → can't desync). A **live knob** (`ResolvedMinimapBlipPx`) converges the exact size on a joined client without a rebuild. **AT-MINIMAP-BLIP-75** on disc + corner. (A follow-up commit in the same PR nudged the Daniel-locked default 24.5→24 px.) Render is Daniel's eye. logs-green ≠ playable — closes t_bc017af4. |
| 17 | **Local Map held mesh → flat painted-deer-hide rectangle + build-hammer stance (was still reading as a hoe)** | t_6cc9f652 (#262) | ✅ merged to `main` (`ab30ce4`); ships in `v0.2.37` | Daniel on `v0.2.36`: the v0.2.35/36 folded-leather sheet was unconvincing AND the held **stance** still posed as *"holding a hoe"* (two-handed grip — never touched). Fix: mesh → plain **flat rectangle 0.5 × 0.3 m**, double-sided, **painted on tanned deer hide** (generated PNG, Point-filtered for Valheim pixelation); stance → **`OneHanded`** (the build-**Hammer** hold — pose is driven purely by `m_animationState`, decomp-verified). **Equip the Local Map (look in-hand, F-key) and drop one on the ground:** it reads as a **flat painted deer-hide map**, NOT a hoe (handle+blade gone), and the **held pose is the build-hammer grip**, not the two-handed hoe stance. Disambiguation is silhouette/value (Daniel is colorblind — no hue cue relied on). The mesh form is a first form + polish knob — say if it wants reshaping. **AT-HELD-MESH** + **AT-HELD-STANCE**. Render/pose is Daniel's eye. logs-green ≠ playable — closes t_6cc9f652. |
| 18 | **Sunstone "won't charge in daylight" — charge-path diagnostics (DIAGNOSTIC build, not a fix)** | t_d5949685 (#261) | ✅ merged to `main` (`6904ac2`); ships in `v0.2.37` | The Lens **won't charge even in clear daytime** — Daniel ran the exact clear-sky / open-field / daytime case and the battery still doesn't fill (a real defect, not wet-weather-as-designed). The charge path was **silent**, so this instruments it: refactor `CanRecharge → EvaluateRecharge → RechargeGate` (six named sub-gates + live env name, **behaviour-preserving** — `CanRecharge == EvaluateRecharge(p).All`), a `DrainGate` prefix logging the per-tick charging verdict ~1 Hz, and a HUD overlay probe every worn frame. Default **ON** while the bug is open (mirrors the `DebugMount` precedent). **This changes NO behaviour by design** — it's here to localize the cause. Equip the Lens, try to charge in clear daytime; if it still won't fill, **grab the `LogOutput.log`** — it now names exactly which sub-gate denied the charge (or shows the per-tick prefix never ran on a save-loaded lens). logs-green ≠ playable — feeds the fix for t_d5949685. |
| 19 | **Cartography — vanilla Boss + Hildir pins pulled onto the SBPR local map (icon + localized label + non-deletable)** | t_3d865001 / parent t_5c3944cd (#263) | ✅ merged to `main` (`753affd`); ships in `v0.2.37` | Daniel's lock (2026-06-24): *"Boss and other generated pins such as Hildir's requests should get pulled into our system. Use vanilla pin art. Do not allow these system pins to be deleted."* **Group 1 (Boss/Eikthyr + Hildir1–3).** Before: a vanilla `PinType.Boss` pin reached the SBPR local map as a **solid yellow-orange square** with a **raw `$enemy_eikthyr`** label (the one un-`Localize`d string). Now: vanilla pin **icon** renders (atlas-safe `uvRect` crop), label is **localized** ("Eikthyr", not `$enemy_eikthyr`), and these system pins are **non-deletable** in the Table eraser. On an SBPR local-map surface (carry-disc + Surveyor's Table modal): **(a)** the Eikthyr/boss pin shows the **vanilla boss icon** (no yellow square); **(b)** its label reads as **localized text**; **(c)** Hildir1–3 pins likewise show vanilla art + labels; **(d)** try to **erase** a boss/Hildir pin in the Table → it **cannot be deleted**. **AT-VPIN-BOSS / HILDIR / NONDEL / LABEL.** (Merchant/location pins were Group 2 → item 20.) logs-green ≠ playable — closes t_3d865001 (parent bug t_5c3944cd). |
| 20 | **Cartography — vanilla location/POI auto-icon pins (Haldor, temple, BogWitch, discovered POIs, modded) on both SBPR map surfaces** | t_1dea827c / design t_b5e535b0 (#265) | ✅ merged to `main` (`8231e6b`); ships in `v0.2.37` | **Group 2** follow-up to item 19. Adds the vanilla **auto-icon location set** — Haldor's vendor, StartTemple, Hildir's camp, the BogWitch, discovered POIs, and any **modded** flagged location — to **both** SBPR local-map surfaces (Surveyor's Table modal + carry-disc) as a **live-re-derived, icon-only, non-deletable** layer (built as a sibling of the Sunstone threat-marker layer: live re-derive → transient icon → render without persisting). This is the **same set the vanilla minimap shows** → parity, not new info (Daniel-locked: full auto-icon set / live re-derive / both surfaces). On both surfaces: **(a)** Haldor's vendor icon appears once discovered; **(b)** temple/BogWitch/Hildir-camp/POI icons render with vanilla art; **(c)** they **live-update** as you discover them; **(d)** they're **icon-only** (no labels) and **non-deletable**. **AT-VPIN-LOC-HALDOR / SET / LIVE / NONDEL.** logs-green ≠ playable — closes t_1dea827c. |
| 21 | **Cartography live-update WRITE axis — carried maps update while travelling with the Kit worn (the real fix for issue 5)** | t_9c54d492 / impl-spec t_d46b3398 (#268) | ✅ merged to `main` (`17a1d36`); ships in `v0.2.37` | Daniel's **issue 5**: *"local map(s) data don't update while travelling when the cartographer's tools are equipped."* It **never worked** — nothing wrote a Local Map's blob except `LocalMap.Imprint`; the prior fix (#131) was a docs-only render-overlay since superseded by #266. This is the **real write path**: a new `LiveFieldWrite` per-~2 s throttled tick that, **with the Cartographer's Kit worn**, stamps the Kit-revealed fog into **every** carried, imprinted, in-region Local Map's stored blob (direct-blob-mutation + dirty-check → **zero writes** when standing still / no Kit / re-covering known ground). With the **Kit equipped**, travel across **unrevealed** ground while carrying one or more imprinted Local Maps: **(a)** the carried map's revealed area **grows as you travel** (AT-LIVE-WRITE-1); **(b)** **multiple** carried imprinted maps all update (AT-LIVE-MULTI); **(c)** **without** the Kit, no live write (AT-LIVE-NOKIT); **(d)** out-of-region maps don't update (AT-LIVE-OUTREGION); **(e)** survives relog (AT-LIVE-PERSIST); **(f)** a Surveyor's Table **ingests** a map dropped a few metres off but in the same 64 m cell (AT-INGEST-REBUILD). Full AT-LIVE-* / AT-INGEST-* per PR #268 §9. logs-green ≠ playable — closes t_9c54d492. |

### 🆕 Round 5 — Twisted Portal feature chain + Trailside Camp first piece, ship in `v0.2.38-playtest`

> Counter stays at **#6** — `v0.2.38-playtest` is the **fifth** build carrying Playtest #6. These are **net-new
> feature surfaces** (the Twisted Portal triad + the first Trailside Camp piece), not round-refinements of a prior
> #6 bug — but Daniel has not signed off #6 as a series, so the counter holds on the running #6 line (a roll to #7
> is a one-flag `--roll` decision if he'd rather track these as a fresh series). All four are **net-new player-facing
> surfaces with NO in-game verification yet** — "logs-green ≠ playable" applies to every row.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 22 | **Twisted Portal — core mechanism (distinct class, NoPortals bypass, rune-name pairing)** | t_2b388cd5 (#273) | ✅ merged to `main` (`8441eca`); ships in `v0.2.38` | First in-game test of the Twisted Portal. Build the **Twisted Portal** piece (Spade/Hammer-placed, swamp-tinted ring kitbash from the Ancient Portal). **(a)** `[Use]` opens a vanilla rename box; type a rune name — it persists (stored in the dedicated `sbpr_rune_name` ZDO slot, NOT vanilla `s_tag`). **(b)** Place a **second** Twisted Portal, give it the **same** rune → they pair and teleport to each other (Model A nearest-other-same-rune). **(c)** 🔴 **The headline behaviour — these work where vanilla portals are forbidden:** in a NoPortals-flagged context the Twisted Portal still teleports (the spec §4.4 NoPortals bypass, `:123008` omitted). **(d)** Confirm a vanilla portal and a Twisted Portal do **NOT** cross-link (our hash isn't in `Game.PortalPrefabHash` — AT-NO-VANILLA-PAIR by construction). Boss gate + ore-transport ban are KEPT by default (conservative; each a one-line flip if Daniel wants them off). **AT-CORE-*** per PR #273. logs-green ≠ playable — closes t_2b388cd5. |
| 23 | **Twisted Portal — food-as-fuel cost model (Portal Energy + Bukeperry reserve + Feeling Sick)** | t_6e992a30 (#276) | ✅ merged to `main` (`dfcb90f`); ships in `v0.2.38` | The cost behind a Twisted Portal jump. With food in your belly, jump through a paired Twisted Portal: **(a)** the jump **drains your food/belly** proportional to distance (you **arrive depleted** — AT-ARRIVE-DEPLETED), per the locked `docs/design/twisted-portal-food-charge.md` model (tier = clamp(totalStats/30, 1, 5) rounded to ½, off the **base** stat budget; PE = Σ rangeMin × tier; 1 PE = 1 m). **(b)** If belly can't cover the jump, **Bukeperries** are burned from your inventory as reserve fuel (`ceil(shortfall/30m)` berries — a 30 m berry per 30 PE shortfall). **(c)** A **berry-fuelled** jump applies vanilla **Feeling Sick / `SE_Puke`**. **(d)** Six live `TwistedPortal/*` BepInEx config knobs tune the model without a rebuild. **(e)** Server boot runs `SpecCheck.CheckPortalEnergyManifest` — confirm **no** boot assertion failure naming the §6 baselines. 58 unit cases (AT-PE-MATH) are green offline; the in-game spend/refill/puke loop is Daniel's accept. logs-green ≠ playable — closes t_6e992a30. |
| 24 | **Twisted Portal — through-terrain rune-name overlay (informational, Model A)** | t_e732bd8b (#274) | ✅ merged to `main` (`d3d560d`); ships in `v0.2.38` | The highest-risk UI in the feature — a **visual** check only Daniel's eye can accept. Stand within **~3 m** of a Twisted Portal: floating **world-space rune labels** (+ optional distance) appear over every nearby Twisted Portal, rendered **through terrain** (reads behind hills/walls — ZTest-Always). **(a)** Labels show each portal's rune name; **(b)** they're **informational only** — a read-out, **NOT** a destination picker (Model B stays out of scope); **(c)** they **billboard** to face the camera; **(d)** unnamed portals are skipped; **(e)** the overlay host-pump stays alive for the HUD lifetime (the #209 self-deactivating-host invariant — visibility toggles the world-space field, host never deactivates). Does the through-terrain render read clearly without cluttering? **AT-OVERLAY-*** per PR #274 — render is GPU-only, Daniel's in-game eyeball is the accept. logs-green ≠ playable — closes t_e732bd8b. |
| 25 | **Bear Hide Tent — placeholder piece (SBPR's first custom AssetBundle)** | _(#277, `feat(camp)`; design-thread piece — no card)_ | ✅ merged to `main` (`8520de5`); ships in `v0.2.38` | First piece of the Trailside Camp triad — **VISUAL-ONLY this cut** (no sleep Tag, no mechanic yet). Placeholder art = the vanilla TraderTent mesh, shipped via SBPR's **first custom AssetBundle** (`sbpr_tradertent.unity3d`), hide material built at **runtime** off vanilla LeatherScraps (a bundle-baked material would render magenta). **(a)** With a **Spade** equipped, find the **Bear Hide Tent** in the build menu (Misc category, 'Trail'/Spade placement), Black Forest tier — recipe **PROVISIONAL** (BjornHide ×4 + FineWood ×6 + LeatherScraps ×4). **(b)** 🔴 **The load-bearing render check — the canopy mesh must actually appear (bundle loaded OK) AND the hide must NOT render magenta (runtime material OK):** place it and look — it reads as a trader-tent canopy with legs, hide-coloured, **not** a magenta/missing-shader blob. **(c)** It's cosmetic decor only — no sleep, no buff, nothing to interact with yet. If the canopy is invisible or magenta, the bundle/material path failed — grab `LogOutput.log`. **AT-TENT-RENDER** (canopy renders + hide material reads). logs-green ≠ playable. |

### 🆕 Round 6 — Seer's Stone (v4 Mountains wisp-lens), ships in `v0.2.39-playtest`

> Counter stays at **#6** — `v0.2.39-playtest` is the **sixth** build carrying Playtest #6. The Seer's Stone is a
> **net-new feature surface** (the Mountains-tier signature Explorer item), not a round-refinement of a prior #6 bug.
> It is a **net-new player-facing surface with NO in-game verification yet** — "logs-green ≠ playable" applies, and
> two of its accept points are explicit **eyeball/decision gates** for Daniel (the placeholder glow + magenta icon, and
> the parser-dependency reversal). Built end-to-end across four milestones; all design forks were locked in-thread.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 26 | **Seer's Stone — v4 Mountains wisp-lens (whitelist substrate · item · wisp field · pin-by-look)** | _(#279, `feat(seers-stone)`; #design-thread feature — no card)_ | ✅ merged to `main` (`020b4b2`); ships in `v0.2.39` | SBPR's signature Explorer item, the full feature across four milestones — **all four player surfaces are new, NO in-game verification yet.** Craft the **`SBPR_SeersStone`** at a **Forge** (crystal-gated recipe: **Crystal + Silver + JuteRed**), equip it in the **Utility** slot. **(a) M1 — whitelist substrate:** on first server boot a default `seers_stone_whitelist.yaml` (132 entries: 20 pickable + 9 surface-ore + 103 location) seeds into `BepInEx/config/SBPR.Trailborne/`; it's owner-editable and unlisted/modded prefabs get no marker (ignore-unlisted). **(b) M3 — wisp field (worn):** while the stone is worn, eligible nearby **Pickables/ore** show small **helix-orbiting wisps** (perimeter orbit at bounds+margin + vertical bob, ground-aware), and curated **Locations** show **bigger, greyer markers** — personal/client-only, no networking/ZDO, persisting while the object exists. **(c) M4 — pin-by-look (Alt+E):** look at a resource cluster or location and press **Alt+E** → a camera raycast re-checks eligibility and drops a map pin (`Minimap.AddPin` for Pickables / `DiscoverLocation` for Locations) with merge-dedup so a cluster pins once. **AT-SEERS-WHITELIST-SEED / AT-SEERS-WISP-WORN / AT-SEERS-WISP-CLASSES (small-colored vs bigger-greyer) / AT-SEERS-PIN-BY-LOOK / AT-SEERS-PIN-DEDUP.** 🟡 **Two explicit Daniel gates:** (1) the wisp glow is the raw **`demister_ball`** placeholder effect and the item icon is a **magenta v0.1 fallback** — both are eyeball polish passes, your look decides the final art; (2) the YAML parser was **hand-written engine-free, YamlDotNet dropped** (no shipped lib = no assembly-version collision — your stated concern) — flagged for your explicit yes/no, the wrapper's shaped to drop the real parser back in if you'd rather. Recipe numbers are eyeball, yours to tune. Build 0/0, **451 unit tests green**, render-verified on Prime (wisp glows + helix orbits at 2.75 m measured). logs-green ≠ playable — the glow, icon, and pin feel are your in-game accept. |

### 🔁 Carried forward — not yet shipped / not yet verified

Shipped **no** code change in any tag (blocked / verify-only), so it carries into #6 rather than being archived as a #5 surface.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 14 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **Round 4 (items 15–21) are the `src/**/*.cs` changes in the `v0.2.36-playtest..main` window** (the build Daniel
  played → the build carrying his round-4 fixes + new cartography work): **#260** (`10ed143`, t_bc017af4, item 16),
  **#261** (`6904ac2`, t_d5949685, item 18), **#262** (`ab30ce4`, t_6cc9f652, item 17), **#263** (`753affd`,
  t_3d865001 / parent t_5c3944cd, item 19), **#264** (`3489ec6`, **NO card id** — direct `/bug`
  `ticket-diegetic-halo-render`, item 15), **#265** (`8231e6b`, t_1dea827c / t_b5e535b0, item 20), **#268**
  (`17a1d36`, t_9c54d492 / t_d46b3398, item 21). All **seven** map to a PENDING row → the guard
  (`gen-playtest-guide.py --tag v0.2.36-playtest --check`) is **green (0 unledgered)**. 🔴 **Two reconciliation
  notes:** (a) **#264 carries no `t_` card id** (authored straight from a Discord `/bug`), so the guard's card-id
  matcher could never represent it — `gen-playtest-guide.py` was extended (`fix(tooling)`) to also accept a
  **no-card-id** commit when its **own PR number** is named in PENDING (item 15 names `#264`); id-carrying commits
  stay strict. (b) **#263 was guard-MASKED** — its parent `t_5c3944cd` already sat in the "⏳ In-flight" prose
  (the Eikthyr boss-pin note), so the guard stayed quiet even though #263 merged; promoted to a real row (item 19)
  off an independent `git log v0.2.36-playtest..main -- 'src/**/*.cs'` surface diff (the check-3 trap). **#259**
  (`78540e2`, t_4b9f8889, the 70 m Lens re-tune) is in the same window but was already ledgered as **item 4**.
  Docs/tooling commits in the window are EXEMPT: **#266/#267** (`00b389b`/`37fa8a8`, cartography impl-spec, `docs:`/`design:`),
  **#258** (`4d256eb`, installer pin auto-PR for v0.2.36, `chore(installer)`).
- **Round 3 (item 13) is the `src/**/*.cs` change in the `v0.2.35-playtest..main` window** (the build Daniel
  played → the build carrying his round-3 fix): **#257** (`116021a`, t_94cc9713, item 13). It maps to a PENDING
  row → `python3 scripts/gen-playtest-guide.py --tag v0.2.35-playtest --check` is **green (0 unledgered)**.
  Docs/tooling commits in the same window are EXEMPT: **#256** (`15a2724`, installer pin auto-PR for v0.2.35,
  `chore(installer)`).
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
- **Closed without merge** (dropped, not a candidate): **#241** Sunstone Lens pulsing solar aura impl-spec (t_e4a6f559) —
  superseded by the shipped corona #254 / the open #255 aura.

> _Reconciled at the v0.2.37 cut: the Eikthyr boss-pin surface (t_5c3944cd) **merged** via #263 and is now a real
> PENDING row (item 19) — removed from in-flight so it can't mask the guard next cycle (the check-3 trap)._

---

## ARCHIVE — shipped playtests

### Playtest #5 — shipped v0.2.33-playtest (2026-06-21)

Build tag: `v0.2.33-playtest` (SBPR Trailborne 0.2.33) — the **first build carrying all of items 1–8** (the interim
`v0.2.32` tag lacked the Painted Sign three-slot #228, which merged after it). Guide:
[`playtest-5-testers-guide.md`](playtest-5-testers-guide.md). **Daniel ran #5 on the `v0.2.33` client** (2026-06-21 →
2026-06-22); his feedback generated the **Playtest #6 fix set** (6 PRs, shipped in `v0.2.34-playtest`) — see the current
PENDING. logs-green ≠ playable; the in-game run was the accept.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Minimap DISC content-to-ring margin — closed (§2E.5.7)** | t_12e15162 | #216 (`v0.2.31`) | DISC cartography edge meets the bronze bezel with no transparent annulus; table pins + in-disc marker stay on the exact terrain cell under rotation/zoom; 125 m disc feel unchanged. Full AT-DISC-RING-1..4 → #5 guide item 1. |
| 2 | **Modal / TableEdit in-disc chevron — counter-rotate to screen-up (§2H.2)** | t_423f5bd7 | #215 (`v0.2.31`) | Player chevron stays screen-up (= facing) while the map rotates beneath it; the (M) modal no longer pins the chevron to map-north; edge-arrow + TableEdit regressions hold. Full AT-MODAL-MARKER-1..4 → #5 guide item 2. |
| 3 | **Sunstone Lens → minimap detection handoff (any-minimap rule)** | t_91e86951 | #218 (`v0.2.33`) | With any minimap present the Lens' detection moves onto it (disc blips ride `_pinObjects`; vanilla corner north-up with surviving aggro tint); camera-relative ring is the no-minimap fallback; single-sourced threat state. Full AT-LENS-DISC-* → #5 guide item 3. _(Standalone ring re-locked to a world-space head-halo in #6 item 6; minimap representation richened in #6 item 3.)_ |
| 4 | **Sunstone Lens NOT repairable at any station (`m_canBeReparied=false`)** | t_1afb94cd | #220 (`v0.2.33`) | A sun-depleted Lens shows **no** Repair affordance at the Explorer's Bench / Workbench / Forge; sunlight is the only refill; charge meter + drain model unchanged. AT-LENS-NOREPAIR → #5 guide item 4. |
| 6 | **Painted Sign board + border recolour (MaterialMan/MPB tint)** | t_f3310406 / t_24ad2570 | #224 (`v0.2.33`) | Board + two-tone border visibly recolour via each renderer's own MPB (not just the TMP text); `∅ None` reverts; tints persist a relog; hammer-hover re-asserts. **Superseded by item 8** (three independent slots). Full AT-SIGN-* → #5 guide item 6. |
| 7 | **Iron Compass → minimap north-ring M1 (compass-gated iron bezel + N-glyph)** | t_fb53c9e4 | #230 (`v0.2.33`) | Worn compass + SBPR map surface draws an iron-bezel recolor + N-glyph + cardinal ticks and hides the HUD needle; gated on equip; N orbits to true map-north. Full AT-COMPASS-* → #5 guide item 7. _(Rim colour retuned in #6 item 1; N z-order fixed in #6 item 2.)_ |
| 8 | **Painted Sign — three independent paint slots (letters / board / frame) + stained-wood basis** | t_6cc9f652 | #228 (`v0.2.33`) | Text / Board / Frame each tint exactly one surface independently (e.g. white board / red frame / blue letters); `∅ None` clears each slot; all three persist a relog; legacy `SBPR_SignColor` migrates to the board slot; stained-wood albedo reads true. Full AT-SIGN-3SLOT-* → #5 guide item 8. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #5

- Items **1–2** (#216 / #215) shipped **inside** `v0.2.31` **after** the #4 guide was cut on the v0.2.30 build, so they appear
  in no prior guide and were hand-seeded into #5.
- Items **3–4 / 6 / 7 / 8** are the **7** `src/**/*.cs` changes in `v0.2.31..v0.2.33` (the auto feeder), **0 unledgered**. The
  reverted §2L.7-R cursor pair (t_8b86adb3 / t_12acb9ce) netted to zero (no test item). Installer SHA pins **#219 / #231 / #232**
  are release chores, not surfaces.
- **Build:** `v0.2.33` was the first build with all of 1–8 (interim `v0.2.32` lacked the signs three-slot #228). **Daniel played
  #5 on the v0.2.33 client** (2026-06-21 → -22); his feedback drove the **6-PR Playtest #6 fix set** that shipped under
  `v0.2.34-playtest` (see the current PENDING). logs-green ≠ playable.


### Playtest #4 — shipped v0.2.31-playtest (2026-06-20)

Build tag: `v0.2.31-playtest` (SBPR Trailborne 0.2.31). Guide:
[`playtest-4-testers-guide.md`](playtest-4-testers-guide.md) — the **first guide that correctly
describes the whole v0.2.30 surface set** (cut on the v0.2.30 build at 00:32, after the #3 guide had
been cut on v0.2.29). Playtest #4 actually **shipped under `v0.2.31`** (tagged 14:28): **2** further
`src/**/*.cs` PRs landed in the gap — **#215** (modal chevron counter-rotate, `af4202f`) and **#216**
(minimap-DISC margin, `8601647`), both ~12:50 — **after the #4 guide was cut**, so the #4 guide
describes neither. They carry into **Playtest #5** (hand-seeded, the same pattern by which the six
v0.2.30 surfaces were seeded into #4). Daniel's local-solo run on the v0.2.31 client is the accept —
logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone Lens trophy-RING — first build it draws** | t_b8a19487 + t_d5949685 | #199/#209 (`v0.2.29`/`v0.2.30`) | The #199 render was frozen by a self-deactivating-host Update pump; #209 moved visibility to a `_content` child so `v0.2.30` is the first build the ring draws. Full AT-LENS-RING-1..5/AGGRO/CAMREL checklist → #4 guide item 1. |
| 2 | **Iron Compass HUD needle — first build it draws** | t_61aff612 + t_ee61472f | #208 (`v0.2.30`) | Same dead-pump bug as the lens + an unloadable `Knob.psd` sprite (replaced with a procedural disc); `v0.2.30` is the first build the dial/needle render. Full AT-COMPASS-HEADING/LAG/TILT/EQUIP-GATE/NOMAP-SAFE → #4 guide item 2. |
| 3 | **[M] open-hint + map NAME under the minimap disc** | t_26bba85b | #205 (`v0.2.30`) | Relocated the bottom-centre `[M] Read map` hint under the carry disc, co-located with the map name as one screen-stable unit. Full AT-MAPNAME-UNDER-DISC/MKEY-HINT-COLOCATED/HINT-VISIBILITY/HINT-NO-BOTTOM/CAPTION-NO-ROTATE/MAPNAME-BLANK → #4 guide item 3. |
| 4 | **Biome NAME on both cartography surfaces (Path A)** | t_304076fa | #207 (`v0.2.30`) | Current-biome NAME on the disc caption middle line + a fixed modal readout, one shared `$biome_*`-localized path. Full AT-BIOME-MINIMAP/MODAL/SHARED/CLEAN/NONE-OMIT → #4 guide item 4. |
| 5 | **Modal map content-to-ring margin — zeroed** | t_89d30da3 | #204 (`v0.2.30`) | The §2E.5.6 two-knob reframe (2×survey-radius + re-derived snapped pins) closed the ~22 px MODAL shroud band. Full AT-RING-1..4 → #4 guide item 5. **(NB: the corner DISC margin was a separate surface — fixed later in #216, now Playtest #5 item 1.)** |
| 6 | **Equipable icons — transparent backgrounds** | t_b9a111ca | #201 (`v0.2.30`) | Transparent RGBA canvas so vanilla's blue equipped indicator shows through; Local Map gets a real icon; a CI-gating test guards regressions. Full AT-EQUIP-IND-1/2/3/REGRESSION → #4 guide item 6. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #4

- Items 1–6 are the v0.2.30 surface set the **#4 guide** describes (carried from #3 because the #3
  guide was cut on the v0.2.29 build). All await Daniel's v0.2.31-client run — logs-green ≠ playable.
- **Two PRs landed in the `v0.2.31` tag AFTER the #4 guide was cut (00:32 → tag 14:28):**
  - **PR #216** (squash `8601647`, card t_12e15162) — close the minimap **DISC** content-to-ring
    margin (§2E.5.7; size the cartography rect to the full TargetPx so meshR ≥ holeR on every survey
    size; extracted a `DiscRingGeometry` helper, CI-gated via `DiscRingGeometryTests`). The **second**
    surface of the content-to-ring family (#204 fixed the MODAL only). **Tested under Playtest #5**
    (PENDING item 1). (`v0.2.31`)
  - **PR #215** (squash `af4202f`, card t_423f5bd7) — modal/TableEdit **in-disc chevron counter-rotate
    to screen-up** (§2H.2; generalise the counter-rotation gate from `PlayerCentred` to
    `!_markerOffDisc`). Fixes Daniel's v0.2.30 "main map chevron always faces north." **Tested under
    Playtest #5** (PENDING item 2). (`v0.2.31`)

### Playtest #3 — shipped v0.2.30-playtest (2026-06-19)

Build tag: `v0.2.30-playtest` (SBPR Trailborne 0.2.30). Guide:
[`playtest-3-testers-guide.md`](playtest-3-testers-guide.md) — **note:** that guide was
generated on the **v0.2.29** build (frontmatter `generated_from_tag: v0.2.29`, cut 18:28 as
part of the #2→#3 roll), so it describes only the trophy-ring + Portal-Seed items. Playtest #3
actually **shipped under `v0.2.30`** (tagged 22:37): **5** code commits touching `src/**/*.cs`
landed in the gap (#204/#205/#207/#208/#209) plus the equipable-icon asset PR #201 — none of
which the #3 guide describes. Critically, **#208 (compass) and #209 (lens) are "rendered
NOTHING — dead Update pump" fixes**, so the #3 guide's trophy-ring headline was **dead on the
v0.2.29 build it targeted**; `v0.2.30` is the first build the ring/needle actually draw. The
full, correct checklist for all six v0.2.30 surfaces lives in **Playtest #4** (the first guide
cut on the v0.2.30 build). Daniel's local-solo run on the v0.2.30 client is the accept —
logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone Lens trophy-ring — dead Update pump fixed** | t_d5949685 | #209 (`v0.2.30`) | The #199 trophy-ring render (shipped in v0.2.29) was frozen inactive by a self-deactivating-host `Update` pump — total, deterministic absence whether or not the lens was worn. #209 moves visibility to a `_content` child so the host's `Update` keeps pumping. **`v0.2.30` is the first build the ring renders.** Full checklist (AT-LENS-RING-1..5/AGGRO/CAMREL) → **Playtest #4** PENDING item 1. |
| 2 | **Iron Compass HUD needle — dead Update pump + Knob.psd fixed** | t_61aff612 | #208 (`v0.2.30`) | The compass HUD showed nothing when worn — same self-deactivating-host pump bug as the lens (found + fixed here first). #208 moves visibility to a `_content` child (host stays active) AND replaces the `UI/Skin/Knob.psd` builtin sprite (fails to load on Valheim 0.221.x Unity) with a procedural disc. **`v0.2.30` is the first build the dial/needle render.** Full checklist (AT-COMPASS-HEADING/LAG/TILT/EQUIP-GATE/NOMAP-SAFE) → **Playtest #4** PENDING item 2. |
| 3 | **[M] open-hint + map NAME under the minimap disc** | t_26bba85b | #205 (`v0.2.30`) | Relocated the floating bottom-centre `[M] Read map` hint to sit **under the carry minimap disc**, co-located with the map's name as one screen-stable unit (Daniel v0.2.27: the bottom hint "doesn't really work for me"). Full checklist (AT-MAPNAME-UNDER-DISC/MKEY-HINT-COLOCATED/HINT-VISIBILITY/HINT-NO-BOTTOM/CAPTION-NO-ROTATE/MAPNAME-BLANK) → **Playtest #4** PENDING item 3. |
| 4 | **Biome NAME readout on both cartography surfaces (Path A)** | t_304076fa | #207 (`v0.2.30`) | Current-biome NAME added to both surfaces: disc caption middle line (name / biome / `[M]` hint) + a fixed modal readout under the title cartouche, one shared `CurrentBiomeNameOrNull()` path, `$biome_*`-localized with a leak guard. Full checklist (AT-BIOME-MINIMAP/MODAL/SHARED/CLEAN/NONE-OMIT) → **Playtest #4** PENDING item 4. |
| 5 | **Modal map content-to-ring margin zeroed** | t_89d30da3 | #204 (`v0.2.30`) | The full circular modal map had a ~22 px shroud band between the cartography disc and the bezel ring (Daniel v0.2.27: "no margin at all"). The §2E.5.6 two-knob fix reframes the modal to 2×survey-radius (~2000 m) AND re-derives the snapped table-pin projection through that same span (the landmine — prevents +23.6 px pin drift). Full checklist (AT-RING-1..4) → **Playtest #4** PENDING item 5. |
| 6 | **Equipable icons — transparent backgrounds** | t_b9a111ca | #201 (`v0.2.30`) | Every equipable icon (Kit/Lens/Compass/Spade/Local Map) shipped an opaque background that occluded vanilla's blue "equipped" highlight (it draws behind the icon Image). Generators now build on a transparent RGBA canvas; a knockout script handles the FLUX composites; Local Map gets a real icon (it had none → magenta fallback); a CI-gating test guards regressions. Full checklist (AT-EQUIP-IND-1/2/3/REGRESSION) → **Playtest #4** PENDING item 6. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #3

- **PR #209** (squash `1f3ef3b`, card t_d5949685) — Sunstone Lens detection-ring overlay
  **rendered NOTHING** — dead Update pump fix in `SunstoneLensHudOverlay.cs` (+ `Plugin.cs`):
  visibility moved to a `_content` child so the host's `Update` keeps pumping. First build the
  #199 trophy ring actually draws. **Tested under Playtest #4** (PENDING item 1). (`v0.2.30`)
- **PR #208** (squash `6676080`, card t_61aff612) — Iron Compass HUD overlay **rendered nothing
  when worn** — same dead-pump bug fix in `SBPR_CompassHud.cs` (+ `Plugin.cs`), plus a procedural
  dial sprite replacing the unloadable `UI/Skin/Knob.psd` builtin. First build the dial/needle
  render. **Tested under Playtest #4** (PENDING item 2). (`v0.2.30`)
- **PR #207** (squash `8b2e328`, card t_304076fa) — biome NAME readout on both cartography
  surfaces (`MapCaptionText.cs` + `MapSurface.cs`): disc caption middle line + modal fixed label,
  one shared biome path. **Tested under Playtest #4** (PENDING item 4). (`v0.2.30`)
- **PR #205** (squash `dcd2181`, card t_26bba85b) — relocate `[M]` open-hint + map NAME under the
  minimap disc (`LocalMapController.cs`/`MapSurface.cs`/`MapViewer.cs`/`CartographyViewer.cs`),
  deleting the bottom-centre prompt. **Tested under Playtest #4** (PENDING item 3). (`v0.2.30`)
- **PR #204** (squash `333d1df`, card t_89d30da3) — zero the modal map content-to-ring margin
  (`MapSurface.cs`): §2E.5.6 two-knob reframe (DisplayedSpanMeters 2×radius + WorldToSurfacePxSnapped
  re-derive). **Tested under Playtest #4** (PENDING item 5). (`v0.2.30`)
- **PR #201** (squash `b19f670`, card t_b9a111ca) — equipable icons get transparent backgrounds
  (asset generators + knockout script + new Local Map icon + CI-gating
  `EquipableIconTransparencyTests.cs`) so vanilla's blue equipped indicator shows through. Asset/test
  change, no `src/**/*.cs` gameplay surface. **Tested under Playtest #4** (PENDING item 6). (`v0.2.30`)

### Playtest #2 — shipped v0.2.29-playtest (2026-06-19)

Build tag: `v0.2.29-playtest` (SBPR Trailborne 0.2.29). Guide:
[`playtest-2-testers-guide.md`](playtest-2-testers-guide.md) — **note:** that guide was
generated at the v0.2.28 build (frontmatter `generated_from_tag: v0.2.27`), *before* the
trophy-ring landed, so its Sunstone item is stale (see the ⤳ row). **4** code commits
touching `src/**/*.cs` shipped across the #2 cycle (`v0.2.26`→`v0.2.29`: #192/#196/#197/#199).
Daniel's local-solo run on the v0.2.29 client is the accept — logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Disc player-marker chevron (A′)** | t_efe8b32b | #192 (`v0.2.27`) | Open the local-map **disc**; the player marker is the **vanilla default-map chevron** (not a blue quad), dead-centre, pointing **up = your facing** (NOT north — the disc rotates to heading). Confirm it reads as "you, forward = up" with no north-locked arrow. Logs note `using VANILLA art` vs `chevronFallback` — either is acceptable visually, but flag if it's the procedural fallback. |
| 1a | **Disc render-correctness — real fog cloud + circular clip** | t_ba31ad30 | #192 (`v0.2.27`) + shroud fix #197 (`v0.2.28`) | Open the disc: (a) **no opaque black square** behind it — outside the circle is transparent, the world shows through; (b) the unexplored area inside renders as **vanilla's real fog-of-war cloud** (matching the normal map's unexplored look), NOT a flat dark fill **and NOT the faded "shared-via-table" look** (the #197 G-channel fix — see item 6); (c) the interior is a **continuous disc** — no rotated-square/diamond, no ocean bleeding in at the corners; (d) the bronze bezel ring is visible. GPU-verified on Prime; Daniel's eye on the live client is the final accept. |
| 1b | **Minimap disc fixed tight zoom (125 m)** | t_ba31ad30 | #192 (`v0.2.27`) | The corner disc frames a **tight ~125 m local window** around you (a small portion of the surveyed area), NOT the whole survey — you should read immediate surroundings, like a vanilla minimap. There is **no zoom input** (`,`/`.`/scroll do nothing on the disc by design). To see the whole local map, open the **full map (M)**, which stays at its full-survey scale. Verify: terrain, pins, and the chevron all sit at the same scale (a pin on a landmark lands ON that landmark at the tight zoom). 125 m is a starting value — tell Daniel if it feels too tight/loose. |
| 6 | **Unexplored = full shroud, not the shared-table look** | t_48c23824 | #197 (`v0.2.28`) | Open the local-map disc **and** the full map (M) / Surveyor's Table with a partially-explored survey. The **unexplored** area must render as Valheim's **solid fog-of-war shroud** — the same dark cloud you see on a fresh regular map for terrain nobody has visited — **NOT** the lighter, faded "someone shared this map with you via the cartography table" look it showed in v0.2.27. Explored/surveyed area still shows full bounded cartography (biome/water/relief). Root cause was a one-channel encoding bug: the reveal tagged unexplored as `_FogTex` G=0 (= shared-by-others) instead of G=255 (= nobody-explored). Daniel's eye on the GPU client is the accept (AT-FOG-VANILLA). |
| 7 | **Map-table / full-map cursor is free to click** | t_1f82da71 | #196 (`v0.2.28`) | At the **Surveyor's Table** map and the equipped **Local Map full view (M)**, the mouse cursor is **free to move and click** (e.g. to click pins for removal) — NOT locked to screen-centre. Mouse-look stays frozen while the modal is open (you don't pan the world). On **close**, the cursor re-locks and the game resumes normal mouse-look exactly once (no stuck-free cursor after exit). Fixes Daniel's 06-19 "my mouse is not free to move and click on pins" report; root cause was the cursor-release hooked an empty/inlined vanilla method — re-seated onto `GameCamera.LateUpdate`. |
| ⤳ | **Sunstone Lens trophy-ring** | t_b8a19487 | #199 (`v0.2.29`) | **Render SHIPPED in v0.2.29, ~6 min AFTER the #2 guide was generated at v0.2.28** — so the #2 guide's Sunstone line still describes the retired **placeholder text HUD** (now OFF by default, behind `DebugTextReadout`). The full, correct trophy-ring checklist lives in **Playtest #3** (the first guide cut on the v0.2.29 build). Recorded here so the #2 build's git cross-check stays complete; **tested under #3**. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #2

- **PR #199** (squash `2ed397f`, card t_b8a19487) — Sunstone Lens **trophy-RING** detection
  render in `SunstoneLensHudOverlay.cs` (+ `Plugin.cs`), replacing the bottom-centre text
  placeholder: trophy-per-hostile ring, proximity-scaled, vanilla star pips, aggro-colour
  tint, empty solar ring, threat-glyph fallback. Shipped in the `v0.2.29` tag. **Tested under
  Playtest #3** (the ⤳ row above + #3 PENDING item 1).
- **PR #197** (squash `bf562ec`, card t_48c23824) — unexplored-area full-shroud `_FogTex`
  **G-channel** fix in `MapSurface.BindBoundedReveal` (fogged texels now R=255 **and** G=255
  = nobody-explored → solid shroud, instead of R=255/G=0 = shared-by-others; plus a
  `_SharedFade=0` pin on the cloned material). This is **item 6** above. (`v0.2.28`)
- **PR #196** (squash `f3fc663`, card t_1f82da71) — modal cursor-release re-seated onto the
  live `GameCamera.LateUpdate` seam (the old hook targeted an emptied/inlined vanilla
  method), freeing the cursor at the Surveyor's Table + Local Map full view. This is
  **item 7** above. (`v0.2.28`)
- **PR #192** (commit `678f9aa`, cards t_ba31ad30 + t_efe8b32b) — the MapSurface
  render-correctness fix (real `_FogTex` cloud, geometry circular clip, transparent-outside
  bezel), the disc player-marker chevron, and the 125 m fixed-zoom decouple
  (`MapViewer`/`MapSurface`, new `CircularRawImage.cs`). These are items 1 / 1a / 1b above.
  (`v0.2.27`)

### Playtest #1 — shipped v0.2.26-playtest (2026-06-18)

Build tag: `v0.2.26-playtest` (SBPR Trailborne 0.2.26).
Guide: [`playtest-1-testers-guide.md`](playtest-1-testers-guide.md) · progression
briefing: [`playtest-1-expected-progression.md`](playtest-1-expected-progression.md).
**17** code commits touching `src/**/*.cs` shipped since `v0.2.25-playtest`. Daniel's
local-solo run is the accept — logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone dual-source loot** | t_0445f590 | #183 | Sunstone drops from swamp **surface** chests (~15%) and **Draugr Elite** (~5%). Loot a spread of **freshly-discovered** swamp chests (already-populated chests keep old contents — vanilla populate-once behavior) + kill Draugr Elites; confirm it appears at roughly those rates and is **pickable** (lands in inventory with its icon). **QA data-layer PASS** (t_0aef1243, `docs/v3/research/QA-sunstone-loot-economy.md`): swamp table carries Sunstone w=0.584 → empirically 15.01%/chest over 200k draws of the vanilla sampler; crypt table clean; elite drop 5% flat. Daniel's run closes the observed last mile (logs-green ≠ playable). |
| 2 | **Local Map opens on M, not E** | t_f9a04fda | #181 | Press **M** with the Surveyor's Table map equipped/in-range → local map opens. Pressing **E** (Use) does NOT open it. SBPR owns the M edge. |
| 3 | **Ancient Portal proximity FX aligned** | t_06b7b13c | #180 | Approach an Ancient Portal → the proximity/target-found effect renders **flat, aligned to the ring** (not vertical/offset). |
| 4 | **Iron Compass v3 — Trinket + HUD needle** | t_ee61472f | #171 | Equip Iron Compass in the Trinket slot → a HUD compass needle overlay appears and tracks heading (the no-map orientation payoff). |
| 5 | **Swamp Sunstone Lens trinket** | t_2fd7bc7f | (tier) | Solar-charged monster-detection trinket: charges in daylight, reveals/indicates nearby monsters per spec. **NOTE:** the shipped detection render is a placeholder text HUD — the trophy-ring redesign (t_b8a19487) shipped later in `v0.2.29` #199, tested under Playtest #3. |
| 6 | **Per-pin icon tint + label color** | t_3d7aaa90 | #168 | Place marker pins → per-pin icon tint and label text color apply and persist. |
| 7 | **Ancient Portal walk-up access** | t_ea0072ba | (#162/#169) | Walk up to / through an Ancient Portal cleanly — per-leg colliders, centre drop slab; no pathing block, no getting stuck. |
| 8 | **Local Map provider binding + carry-state disc** | t_7dd54899 | #162 | Carry the map item → minimap renders as the carry-state disc; provider binding correct. |
| 9 | **Map bezel circular clip** | t_d44572f2 | #159 | Local map parchment does NOT bleed past the disc edge — hard circular bezel clip. |
| 10 | **Local Map title format** | t_783672ac | #158 | Map title renders without a race glitch, formatted `Local map for <name>`. |
| 11 | **Re-name a named Surveyor's Table** | (#157) | #157 | `[Use]+Alt` on an already-named Surveyor's Table re-opens the name prompt (§1.6.5). |
| 12 | **Ancient Portal proximity effect wired** | t_e58283d7 | #156 | Vanilla proximity/target-found effect fires on Ancient Portal approach (issue 1). |
| 13 | **Item tooltip NRE fix** | t_2dd7c705 | #154 | Hover SBPR items with custom attacks → no per-frame tooltip NullReferenceException in the log. |

**Also shipped (graduated from in-flight in #1):**

- **Sunstone recipe removal** — t_c27f985e (#186, commit `c7463ac`). The provisional
  Iron×1+Crystal×2 Explorer's Bench craft is **gone**; exploration drops are the sole
  acquisition path. Verify Sunstone is **NOT** craftable and still obtainable via drops.

**Shipped refactor (no dedicated in-game item — behavior-preserving):**

- **CLEANUP 3/3 — null-remediation** — t_0234cc42 (#187, commit `1c5da09`):
  null-as-value → `TryX(out)` across the 6 branch-on-result helpers (Assets.cs + 13
  feature files), build 0/0. No behavior change, so no gameplay test item — covered by
  the structural xUnit suite (#182), not in-game testing. Recorded here so the
  generator's "every code change maps to a ledger item" cross-check stays honest.
