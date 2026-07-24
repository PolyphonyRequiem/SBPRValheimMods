---
title: "SBPR Trailborne — Playtest #7 Testers Guide"
status: current
purpose: "Playtest #7 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.40-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #7 Testers Guide

**Build:** SBPR Trailborne 0.2.41 (current `main`, ahead of `v0.2.40-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-07-02 00:23 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.40-playtest`. The
> **Playtest #7** number is the human-facing testing series — distinct from the
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
`Loading [SBPR Trailborne 0.2.41]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

> Build target: **next `-playtest` tag** (SBPR Trailborne 0.2.41+; the first #7 build). Test **local solo** on a
> fresh client build unless an item says otherwise. These are the `src/**/*.cs` changes merged to `main`
> **after** `v0.2.40-playtest` (the tag that shipped all of Playtest #6) — Daniel's Niflheim-feedback rework of
> the look-to-aim Twisted Portal + the Seer's Stone interaction re-lock + two cartography/wisp fixes. **NONE are
> in-game verified yet** — "logs-green ≠ playable" applies to every row.
>
> **Twisted Portal — Model A → look-to-aim (items 29, 31, 32):** Daniel's `v0.2.40` feedback retired the
> Model A nearest-same-rune pairing + overhead jump-through trigger (spec supersession #288). The new model:
> stand on a Twisted Portal, **aim the crosshair** at any destination portal (selectable through terrain via its
> label), **tap [Use]/E to travel**; hold-E still renames. Shipped in three layers — L1 core aim-pick+commit
> (#289), L2 server-authoritative long-range candidate set over RPC (#291, the multiplayer-correct reach past the
> active-zone window), L3 overlay selection-highlight + read-only food-impact preview (#292). Test as one
> connected surface.
>
> **Seer's Stone — pin-by-USE re-lock + wisp clustering (items 30, 34):** Daniel's `v0.2.40` re-lock made the
> wisp a **vanilla interactable** — walk up, press **[E]** to pin, wisp dims to confirm; the buggy Alt+E
> raycast path (#279 item 26, #6) is **retired** (#290). Separately, wisps now cluster **one-per-patch** instead
> of one-per-Pickable so a berry patch shows a single wisp/pin (#286).
>
> **Cartography (item 33):** boss/Hildir pins now **live-derive onto the holder's own map** (#287), not just via
> a frozen Surveyor's-Table survey — the missing capture path Daniel hit on `v0.2.40` (a boss discovered but
> never surveyed didn't show).

### 🆕 Round 1 — post-v0.2.40 `main` changes (Twisted Portal look-to-aim rework + Seer's Stone re-lock), ship in the first Playtest #7 build

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 29 | **Twisted Portal — look-to-aim travel (L1: aim-pick destination + tap-E commit; retire Model A pairing + jump-through trigger)** | t_f4d0d5e1 (#289) | ✅ merged to `main` (`e3eb529`); ships in first #7 build | 🔴 **Major model change — the whole Twisted Portal travel loop is new.** Stand ON a Twisted Portal, **aim the crosshair** at another Twisted Portal (even one behind a hill — aim at its through-terrain label), **tap [Use]/E → you travel there.** The retired paths must be gone: **(a)** no more Model A "nearest-same-rune auto-pair," **(b)** no overhead jump-through trigger — walking/falling through the ring does NOT teleport; only tap-E commits. **(c)** **hold-E** still opens the rune-rename box (tap vs hold fork — they must not double-fire). **(d)** The teleport core is UNCHANGED: NoPortals bypass still works, boss-gate + ore-ban still KEPT, food-as-fuel debit still applies. **(e)** A back-to-back commit inside the 2 s cooldown spends **no** food/berries (AT-COOLDOWN-REFUND). Aim cone default 35° (`TwistedPortal/AimConeDegrees`, live). **AT-AIM-* / AT-COOLDOWN-REFUND** per PR #289 (12 AimPickMath unit cases green; the in-game aim+commit feel is Daniel's accept). logs-green ≠ playable — closes t_f4d0d5e1. |
| 30 | **Seer's Stone — pin-by-USE: wisp is a vanilla [E] interactable, dim-on-pin (retires the Alt+E raycast)** | t_d3768adf (#290) | ✅ merged to `main` (`570f25b`); ships in first #7 build | Daniel's 2026-06-27 re-lock, **superseding the #6 pin-by-look (Alt+E) path (item 26).** Wear the Seer's Stone, walk up to a wisp (within ~5 m), **press [Use]/E** → a map pin drops **and the wisp visibly dims** to confirm. **(a)** The wisp shows a localized **`[E] <name>`** hover prompt (no raw token leak); **(b)** after pinning it reads a muted "pinned" hover and the glow dims (light intensity/range + particle alpha down); **(c)** a wisp that dedups into an existing pin ("merged") still gives the **same** pinned feedback (E never reads as a no-op); **(d)** 🔴 **the retired path is GONE** — Alt+E no longer pins (the raycast-to-source postfix is deleted). Built on the vanilla hover pipeline (world-root re-parent so FindHoverObject sees it, `piece_nonsolid` layer, ~1.25 m trigger). **AT-WISP-E-PROMPT / AT-WISP-E-PIN / AT-DIM-ON-PIN** per PR #290. logs-green ≠ playable — closes t_d3768adf. |
| 31 | **Twisted Portal — server-authoritative long-range candidate set (L2, RPC): aim/travel to portals past the active-zone window** | t_ccb454f8 (#291) | ✅ merged to `main` (`8713617`); ships in first #7 build | **Multiplayer-correctness for the look-to-aim picker — test on the dedicated server (joined client), not just SP.** On a dedicated server a client only syncs the ~64–128 m active-zone window, so before this a destination portal past it was **un-aimable and un-travelable**. Now a client→server→client routed RPC asks the server (which holds every world ZDO) for the within-range Twisted Portals and unions them with the always-current local window. **AT-PICK-LONGRANGE:** on a joined dedicated-server world, place portal B **>128 m away in a different sector**, stand on portal A, aim at B's through-terrain label, tap-E → **you travel** (before: nothing to aim at). **(a)** Near portals still resolve instantly (local-window union); **(b)** SP / hosted-game falls back to the local walk (no RPC needed) and is unregressed. Boot-verified: directory RPCs registered on the Niflheim server, PatchCheck + SpecCheck green. **AT-PICK-LONGRANGE / fallback** per PR #291 (12 TwistedDirectoryModel unit cases green). logs-green ≠ playable — the >128 m joined-client travel is Daniel's accept. closes t_ccb454f8. |
| 32 | **Twisted Portal — look-to-aim overlay: aimed-label highlight + food-impact preview (L3)** | t_d9ea1b2c (#292) | ✅ merged to `main` (`5ce4489`); ships in first #7 build | Turns the through-terrain overlay from a read-out into the **interactive aim surface**. Stand on a Twisted Portal and sweep the crosshair across nearby portal labels: **(a) AT-AIM-HIGHLIGHT** — the label you're aimed at **highlights** (brighter + slightly bigger) and the highlight **tracks** as you sweep; it is provably the portal tap-E travels to (matched by ZDO id, not proximity). Highlight is **luminance + size, not hue** (colorblind-safe). **(b) AT-FOOD-PREVIEW** — under the aimed label a **read-only food cost** preview shows the jump's belly range + an in-range / need-N-berries / too-far verdict; it **spends nothing** (debit only on tap-E commit), and the previewed distance == the committed distance (preview matches outcome). 3 live knobs (`HighlightAimed`, `ShowFoodPreview`, `HighlightScaleBump`). Builds on #289/#291 seams (no re-architecture). **AT-AIM-HIGHLIGHT / AT-FOOD-PREVIEW** per PR #292 (11 TwistedPortalPreviewText cases green; the rendered highlight + preview are Daniel's in-game eyeball). logs-green ≠ playable — closes t_d9ea1b2c. |
| 33 | **Cartography — boss/Hildir pins live-derive onto the holder's own map (§2N)** | t_2110193e (#287) | ✅ merged to `main` (`5b2e8f6`); ships in first #7 build | Daniel on `v0.2.40`: a boss discovered (boss-stone used) but **not surveyed since** never showed on the SBPR local map, and re-using the boss stone said "already pinned" yet couldn't surface it. The #6 boss-pin work (item 19) captured boss/Hildir pins **only** via a frozen Surveyor's-Table survey; this adds the **missing live capture** — `SystemPins.Collect` reads the live `Minimap.m_pins`, filters `m_save && IsSystemPin` (Boss/Hildir1–3), and renders them on the holder's own map right alongside the frozen survey (dedup so a pin that's both frozen+live renders once). Verify on an SBPR local-map surface (carry-disc + Surveyor's Table modal): **(a)** kill/discover a boss (or use its stone) **without** surveying at a Table → the boss pin **still appears** on your carried map; **(b)** it inherits the #6 vanilla icon + localized label + non-deletable behavior (item 19 untouched); **(c)** a boss that's both frozen (surveyed) and live shows **once**, not doubled. Live-derived, persists nothing (WireVersion unchanged). **AT-§2N** per PR #287. logs-green ≠ playable — closes t_2110193e. |
| 34 | **Seer's Stone — wisps cluster one-per-patch, not one-per-Pickable** | t_9e6a0654 (#286) | ✅ merged to `main` (`c2ec36d`); ships in first #7 build | Daniel-relevant polish on the #6 wisp field (item 26): a berry patch (N same-prefab RaspberryBush Pickables placed close together) spawned **N wisps** instead of one. Spec locks the wisp as the spawn-time **group aggregate** (one wisp per patch → pinning it pins the whole patch as one pin), but the aggregate was never implemented. Fix: `PickableClustering` groups eligible Pickables by (prefab-name × XZ proximity within R=15 m, the same abundance-radius the pin-site uses) into single-linkage components and spawns **one wisp per cluster** at the centroid with the patch's aggregate bounds. Wear the stone near a dense same-berry patch: **(a)** you see **one** wisp over the whole patch, not one per bush; **(b)** a lone isolated bush is unchanged (singleton cluster → the prior 2 m single-bush behavior); **(c)** two different berry types close together stay **separate** wisps (per-prefab clustering); **(d)** pinning the patch wisp (via the item-30 [E] path) drops **one** pin. Locations are unaffected (already one wisp each). **AT-CLUSTER-*** per PR #286 (12 headless cases green). logs-green ≠ playable — closes t_9e6a0654. |

### 🆕 Round 2 — Trailside Camp triad + cleanup + the engine-free Core seam (P0/P1/P2), ship in the first Playtest #7 build

> The rest of the post-`v0.2.40` `main` changes: the Trailside Camp triad completion (bedroll + camp fire + tent
> collider fit), two Daniel-greenlit cleanup commits, and the three-slice **engine-free Core seam** (P0/P1/P2).
> The Core slices are **behaviour-preserving refactors** — their accept is a **boot/smoke confirm** (client loads
> clean with the new second DLL, server boots with SpecCheck green, an Ancient Portal still plants+grows+resumes),
> not a new player mechanic. "logs-green ≠ playable" still applies to every row — a green build that fails to load
> the new `SBPR.Trailborne.Core.dll` is a broken client.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 35 | **Trailside Camp — Bedroll + Camp Fire (finish the triad: night-skip sleep + heat-keeping fire)** | t_439f2351 (#345) | ✅ merged to `main` (`81d504e`); ships in first #7 build | Both **Spade-placed Black Forest** pieces on the **'Trail'** build tab. **Bedroll:** lie down at night → skip to morning **WITHOUT overwriting your home respawn point** (a no-spawn night skip); free **SE_Rested** comfort rides the wake; the exposure gate still needs **underRoof** (open sky refused) but drops the 0.8-cover clause; 🔴 a **vanilla** bed still sets spawn as normal (AT-BEDROLL-VANILLA). **Camp Fire:** light it → it **emits heat** (warm status nearby; toggles with lit state — AT-BEDROLL-NOFIRE), burns **Wood**, ordinary rain behavior. **AT-BEDROLL-* / AT-CAMPFIRE-*** per PR #345. logs-green ≠ playable — closes t_439f2351 (defects 2,3). |
| 36 | **Bear Hide Tent — walk-under shelter collider fit** | t_c96a2ea2 (#341) | ✅ merged to `main` (`4bbe862`); ships in first #7 build | Daniel on `v0.2.40`: *"the collision mesh has no relationship to the tent mesh / I can't find a spot to get shelter."* Fix seats an **open-sided walk-under MeshCollider** to the measured canopy mesh (non-leaky → still counts as **underRoof**). Place a **Bear Hide Tent** (Spade, 'Trail' tab, BF): **(a)** walk **under** the canopy and stand inside — no invisible wall filling the footprint; **(b)** standing under it grants **shelter/underRoof** (works with the item-35 bedroll beneath it); **(c)** the canopy visual **coincides** with collision (no ~4.7 m offset). **AT-TENT-COLLIDER-FIT** per PR #341. logs-green ≠ playable — closes t_c96a2ea2. |
| 37 | **Cairns — BannerDiagnostic default OFF in shipped builds (perf, behaviour-preserving)** | t_0027b296 (#342) | ✅ merged to `main` (`2d67e0e`); ships in first #7 build | Flips the per-frame cairn-banner diagnostic probe **off by default** in release. Verify a **cairn with a banner** looks/behaves **exactly as before** (no visible change) and the log **no longer** shows the per-frame banner-diagnostic spew. Chiefly a **no-visible-regression** check. The `sbpr_bannerdiag` command + `SBPR_BannerDiagnostic` toggle remain. logs-green ≠ playable. |
| 38 | **Signs — remove dead PinTypes/PinTypeForColor/ColorForPigment helpers (dead-code removal)** | t_0027b296 (#340) | ✅ merged to `main` (`918707a`); ships in first #7 build | Removes three sign helpers with **zero call sites** (the live `PigmentForColor` is kept). Wire-contract safe (no ZDO keys / prefab names / config keys touched). Chiefly a **no-regression** check: **Painted Signs still paint + tint + persist exactly as before**. logs-green ≠ playable. |
| 39 | **Core seam P0 — engine-free `SBPR.Trailborne.Core` project (additive; ships a 2nd DLL)** | direct push — ledgered by SHA `adbabf5` | ✅ merged to `main` (`adbabf5`); ships in first #7 build | Additive, zero behaviour change: a new engine-free Core project with `BoundedMapMath` moved into it (byte-identical). 🔴 **Load-bearing:** the modpack now ships a **second DLL** (`SBPR.Trailborne.Core.dll`). Verify: launch this build → the client **loads clean**, logs `Loading [SBPR Trailborne 0.2.41]` + `Harmony patches applied.` with **no `FileNotFoundException`** for the Core DLL, and the local-map / cartography surfaces still render. logs-green ≠ playable. |
| 40 | **Core seam P1 — ZDO-component seam + migrate AncientPortalTag (behaviour-preserving)** | direct push — ledgered by SHA `4413841` | ✅ merged to `main` (`4413841`); ships in first #7 build | `AncientPortalTag` now rides the new `ZdoComponent` base + extracted `PortalGrow`; **`SBPR_PortalPlantTime` ZDO key UNCHANGED**. Ancient Portal smoke: **(a)** plant one → inert, **scale-lerps ~15 s**, activates **once**; **(b)** **relog mid-grow** → **resumes correctly** (absolute world-time). 🔴 If it fails to grow, double-activates, or restarts its timer on relog, the seam regressed. logs-green ≠ playable. |
| 41 | **Core seam P2 (sliced) — recipe registry retires the SpecCheck↔wiring drift (boot-consistency)** | direct push — ledgered by SHA `89962ba` | ✅ merged to `main` (`89962ba`); ships in first #7 build | `SpecCheck.Manifest` is now **projected from** the engine-free `ContentRegistry` (identical row-set: 10 item recipes + 7 pieces, same identifiers; no wire-string minted). Boot smoke: start a server → **SpecCheck logs the SAME "checks All N recipes match" line as before** (no boot assertion failure). No player mechanic changed. logs-green ≠ playable. |

### 🔁 Carried forward — not yet shipped / not yet verified

Shipped **no** code change in any tag (blocked / verify-only), so it carries into #6 rather than being archived as a #5 surface.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 14 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **Round 1 (items 29–34) are the first six `src/**/*.cs` changes in the `v0.2.40-playtest..main` window**:
  **#286** (`c2ec36d`, t_9e6a0654, item 34), **#287** (`5b2e8f6`,
  t_2110193e, item 33), **#289** (`e3eb529`, t_f4d0d5e1, item 29), **#290** (`570f25b`, t_d3768adf, item 30),
  **#291** (`8713617`, t_ccb454f8, item 31), **#292** (`5ce4489`, t_d9ea1b2c, item 32).
- **Round 2 (items 35–41) are the remaining seven scoped changes** in the same window:
  **#345** (`81d504e`, t_439f2351, item 35), **#341** (`4bbe862`, t_c96a2ea2, item 36), **#342** (`2d67e0e`,
  t_0027b296, item 37), **#340** (`918707a`, t_0027b296, item 38), and the three **direct-to-`main`** `feat(core)`
  commits **`adbabf5`** (P0, item 39), **`4413841`** (P1, item 40), **`89962ba`** (P2, item 41) — the last three
  carry no card id / no `(#NNN)` / no GitHub PR, so they are ledgered by the **SHA-rescue** (each row names the
  commit's own SHA). All 13 non-exempt changes map to a PENDING row → `python3 scripts/prepare-playtest.py
  --ref main` reports **0 unledgered**. **EXEMPT:** **`da11e6b`** (`docs(sunstone):`, docs-only).
- **Supersession notes:** item 29 (#289 look-to-aim) **replaces** the #6 Twisted Portal core's Model A
  nearest-same-rune pairing + jump-through trigger (item 22, #273) — same feature, Daniel's `v0.2.40` model
  re-lock; items 31/32 (#291/#292) **extend** the same look-to-aim surface (long-range reach + interactive
  overlay). Item 30 (#290 pin-by-USE) **supersedes** the #6 Seer's Stone pin-by-look Alt+E path (item 26, #279);
  item 34 (#286 wisp clustering) **refines** the same #6 wisp field. Item 33 (#287 live-derive) **extends** the
  #6 boss-pin capture (item 19, #263) with the missing live path. The Twisted Portal food-model (item 23, #276)
  + through-terrain label render fix (item 28, #284) are untouched by this rework — they remain #6 surfaces
  archived under Playtest #6, and the #7 look-to-aim loop rides the same food-debit + overlay they established.

### ⏳ In-flight (will join PENDING when merged)

- _(none currently — no open `src/**/*.cs` PRs against `main`.)_

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.40-playtest**: **13** (Round 1: 6, Round 2: 7)


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #7 in the ledger, bumps the counter, and opens the Playtest #8 planning card.
