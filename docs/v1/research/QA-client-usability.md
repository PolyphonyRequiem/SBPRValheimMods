---
title: "QA — client usability verification (client-refresh fix, commit 25f1df6)"
status: current
purpose: PASS/FAIL evidence that the T2 client-refresh layer makes Trailborne content genuinely craftable/buildable on a joined client — not merely log-green. Verified-vs-reasoned split is explicit.
task: t_f96db65b
parent: t_0387ebb3
verified_against: origin/v1 @ 376156c (contains fix commit 25f1df6, merged PR #10)
---

# QA — client usability verification

**Scope.** This card exists because "SpecCheck passed" was mistaken for "playable"
once already (the T1 gap). It verifies the merged client-refresh fix
(`ClientRefreshPatches.cs`, commit `25f1df6`, PR #10 → `v1`) actually makes the
content reachable from a player's UI on a *connected client*, and states
precisely **what was verified live vs. reasoned from the code path**.

**Honesty boundary (load-bearing).** A dedicated server has no local `Player` and
never opens a build menu, so the two client-only surfaces (the player's
known-recipe set and a `PieceTable`'s lazily-built runtime arrays) cannot be
exercised headless. I therefore prove the content reaches the **data layer a
client builds its UI from** (ObjectDB recipes + the Hammer/spade
`PieceTable.m_pieces` lists — which the client constructs locally because SBPR is
a client-installed mod, `ServerContext.OnSBServer => true`), verify the patched
vanilla member names resolve in the shipped assembly, and trace the client-only
last mile. The last mile (UpdateKnownRecipesList re-scan + UpdateAvailable array
sizing on a real client) is **reasoned, not observed** — see "Remaining
client-only risks" at the bottom.

## How this was verified

- **Build.** Clean worktree at `origin/v1` (`376156c`, contains `25f1df6`) built
  with `dotnet build -c Release` → **0 errors, 29 known nullable warnings**
  (baseline count; the fix adds none). DLL md5 `564f10c69d563b111434c9d2cde7b08c`
  deployed to Niflheim `config/bepinex/plugins/SBPR.Trailborne/` and confirmed as
  the loaded artifact. (The DLL previously deployed on the box was a *different*
  feature-branch build — `5779d1b…` — so this QA rebuilt and redeployed the merged
  v1 to verify the right thing.)
- **Live server.** Niflheim (`lloesche/valheim-server`, world `niflheim`) restarted;
  boot logs grepped after world load + `Game server connected`.
- **Data-layer probe.** A throwaway read-only companion BepInEx plugin
  (`SBPR.QADiag`, clean-room: vanilla public API only, ordered
  `[HarmonyAfter("net.danielgreen.sbpr.trailborne")]`) dumped the settled
  ObjectDB recipe set + Hammer/spade `PieceTable.m_pieces` after SBPR's wiring.
  It was an instrument only and has been **removed from both config and runtime
  data dirs** — final boot confirmed `0` QADiag lines.
- **Metadata probe.** A `MetadataLoadContext` reflection pass over the shipped
  `assembly_valheim.dll` confirmed every vanilla member the fix patches exists
  with the expected shape (signatures only — no decompiled IronGate source read).

---

## PASS / FAIL checklist

### ✅ 1. Clean boot — zero SBPR warnings/errors; SpecCheck green  → **PASS**

Final clean boot (23:45, DLL `564f10c`, only SBPR.Trailborne loaded):

```
[Info :BepInEx] Loading [SBPR Trailborne 0.1.0]
[Info :SBPR Trailborne] [Trailborne] Awake — SBPR Trailborne 0.1.0 booting (… OnSBServer=True)
[Info :SBPR Trailborne] [Trailborne] Harmony patches applied (DebugCairnDamage=True).
[Info :SBPR Trailborne] [Trailborne] ZNetScene registration complete.
[Info :SBPR Trailborne] [Trailborne/M1] Pigments ObjectDB wiring complete (4 ink items + recipes).
[Info :SBPR Trailborne] [Trailborne/M1] Signs ObjectDB wiring complete (sign recipes + hammer pieces).
[Info :SBPR Trailborne] [Trailborne/M2] M2 ObjectDB wiring complete (4 marker + 4 cairn + 5-tier ladder …).
[Info :SBPR Trailborne] [Trailborne] ObjectDB wiring complete (items + recipes + hammer pieces).
[Info :SBPR Trailborne] [Trailborne/SpecCheck] ✓ All 15 recipes match the v0.1.0 spec manifest.
06/04/2026 23:45:46: Game server connected
```

- SBPR-tagged exceptions/errors this boot: **0**.
- One bare `ArgumentNullException` appears each boot — stack is
  `ShieldDomeImageEffect.Awake() → Material..ctor(null shader)` (module GUID
  `<62393fbd…>` = vanilla). It is **pre-existing vanilla headless noise** (a camera
  post-process effect with no shader pipeline under `-nographics`); it fired 16×
  across boots back to Jun 3 20:40, independent of SBPR and of this redeploy.
  **Not an SBPR defect.**

### ✅ 2. The new client-refresh hook fires in the join path  → **PASS (mechanism verified; client-only fire reasoned)**

The hook is **Fix A** — a `Player.OnSpawned(bool)` Harmony postfix
(`ClientRefreshPatches.Player_OnSpawned_Postfix`, `[HarmonyPriority(Priority.Last)]`)
that invokes the player's private `UpdateKnownRecipesList()`, guarded on
`Registrar.ContentWired`.

- **Patched members resolve in the shipped assembly** (metadata probe vs
  `assembly_valheim.dll`):
  - `Player.OnSpawned` — public instance, params `[Boolean]` → Harmony target valid.
  - `Player.UpdateKnownRecipesList()` — **private**, instance, 0 params, returns
    `Void` → exactly what `AccessTools.Method(typeof(Player),"UpdateKnownRecipesList")`
    resolves and `Invoke(__instance, null)` calls. **Confirms Fix A will NOT
    silently no-op** (the load-bearing reflection risk).
- **Why it fires on the client join path:** `Player.OnSpawned` runs when the local
  player avatar spawns into the world, which on a multiplayer join happens *after*
  the client has received the world and run its own SBPR registration (so
  `Registrar.ContentWired` is true by then). The postfix then re-runs the vanilla
  known-recipe scan over `ObjectDB.m_recipes`, which now contains our recipes.
- **Why it is NOT observed server-side (correct):** a dedicated server has no local
  `Player`, so `Player.OnSpawned` never fires there. Grep of the *entire* container
  log history → **0** occurrences of the Fix A info line. That is the *expected*
  proof of server-safety, and is why this criterion's actual fire is **reasoned for
  the client, not observed headless** (no remote client joined during the QA window;
  handshake grep = none).

### ✅ 3. The 8 craftables (spade + 4 inks + 4 cairn markers) reach the Explorer's Bench  → **PASS (data layer verified live)**

> Note: the card says "8 craftables"; the actual count is **9** item recipes
> (spade + 4 inks + 4 cairn markers). SpecCheck's "15" = 9 item recipes + 2
> non-station build pieces (bench, lamp) + 4 cairn build pieces. Documented here
> so the count discrepancy isn't mistaken for a missing item.

Live QADiag dump of `ObjectDB.m_recipes` after wiring — **9/9 present, every one
stationed at `piece_sbpr_explorers_bench`, costs spec-correct:**

```
OK SBPR_TrailblazersSpade  yield=1  station=piece_sbpr_explorers_bench  cost=[Woodx5,Flintx2,LeatherScrapsx2]
OK SBPR_InkRed     yield=2  station=piece_sbpr_explorers_bench  cost=[Raspberryx1]
OK SBPR_InkWhite   yield=2  station=piece_sbpr_explorers_bench  cost=[BoneFragmentsx1]
OK SBPR_InkBlue    yield=2  station=piece_sbpr_explorers_bench  cost=[Blueberriesx1]
OK SBPR_InkBlack   yield=2  station=piece_sbpr_explorers_bench  cost=[Coalx1]
OK SBPR_CairnMarker_red    yield=1  station=piece_sbpr_explorers_bench  cost=[LeatherScrapsx2,FineWoodx1,SBPR_InkRedx1]
OK SBPR_CairnMarker_white  yield=1  station=piece_sbpr_explorers_bench  cost=[LeatherScrapsx2,FineWoodx1,SBPR_InkWhitex1]
OK SBPR_CairnMarker_blue   yield=1  station=piece_sbpr_explorers_bench  cost=[LeatherScrapsx2,FineWoodx1,SBPR_InkBluex1]
OK SBPR_CairnMarker_black  yield=1  station=piece_sbpr_explorers_bench  cost=[LeatherScrapsx2,FineWoodx1,SBPR_InkBlackx1]
item-recipe result: 9/9 present in ObjectDB
```

- **Mechanism (recipe → visible at bench on a client):** the recipes are in the
  client's `ObjectDB.m_recipes` (it runs the same registration locally; SBPR is a
  client-installed mod). The crafting panel filters known recipes by their
  `m_craftingStation`; standing at the Explorer's Bench surfaces exactly these 9.
  Fix A's `UpdateKnownRecipesList()` re-scan is what moves them from "in the DB" to
  "known/clickable" after spawn. Recipe presence + correct station = **verified
  live**; the post-spawn known-set refresh = **reasoned** (see criterion 2 + risks).

### ✅ 4. The 10 hammer pieces land in the CLIENT hammer PieceTable  → **PASS (data layer verified live)**

Live QADiag dump of the Hammer's `m_buildPieces.m_pieces` (the list the build menu
renders) — **10/10 present:**

```
OK piece_sbpr_explorers_bench
OK piece_sbpr_path_lamp
OK piece_sbpr_sign_red / _white / _blue / _black
OK piece_sbpr_cairn_red / _white / _blue / _black
hammer-piece result: 10/10 in Hammer.m_pieces (table total m_pieces=323)
```

- **Mechanism:** `Trailhead/Signs/Cairns.DoObjectDBWiring` resolve the Hammer's
  PieceTable via `ObjectDB.GetItemPrefab("Hammer").…m_buildPieces` and `m_pieces.Add`
  each prefab. The client runs this same path locally, so its hammer table contains
  the same 10. This is the **exact surface the T1 gap was about** ("logs green ≠
  playable") and it is now **verified at the data layer**, not assumed.
- **Spade-only PieceTable (Fix B's highest-risk surface) — also verified:** the
  from-scratch table built in `Trailblazing.DoObjectDBWiring` holds **4/4** spade
  ops (`piece_sbpr_path_narrow/standard/wide`, `piece_sbpr_replant_wide`). Server-side
  its lazy runtime arrays read `m_availablePieces.Count=0` (no build menu opens
  headless) — **expected**; these are populated client-side by
  `PieceTable.UpdateAvailable`, which is precisely what Fix B sizes
  (`m_selectedPiece`/`m_lastSelectedPiece` → `m_availablePieces.Count`,
  `Piece.PieceCategory.Max=8` buckets). All of `m_availablePieces (List<List<Piece>>)`,
  `m_selectedPiece`/`m_lastSelectedPiece (Vector2Int[])`, and `PieceCategory.Max`
  confirmed present in the shipped assembly by the metadata probe.

---

## Verified vs. reasoned — explicit ledger

| Claim | Status | Evidence |
|---|---|---|
| Plugin loads, `OnSBServer=True`, SpecCheck ✓ 15/15, 0 SBPR errors | **VERIFIED LIVE** | boot log, this QA's clean redeploy |
| `ArgumentNullException` is vanilla, not SBPR | **VERIFIED** | stack = `ShieldDomeImageEffect`, 16× pre-dating redeploy |
| 9 item recipes in client ObjectDB, correct station + costs | **VERIFIED LIVE** | QADiag dump (9/9) |
| 10 hammer pieces in client Hammer `m_pieces` | **VERIFIED LIVE** | QADiag dump (10/10, table=323) |
| 4 spade ops in from-scratch spade PieceTable | **VERIFIED LIVE** | QADiag dump (4/4) |
| Fix A's patched members exist / won't silently no-op | **VERIFIED** | metadata probe vs `assembly_valheim.dll` (19/19) |
| Fix B's patched fields/enum exist with expected types | **VERIFIED** | metadata probe (`m_availablePieces` `List<List<Piece>>`, etc.) |
| Player.OnSpawned refresh actually *fires & makes recipes clickable* on a real client | **REASONED** | server has no local Player; no client joined this window |
| UpdateAvailable array sizing prevents empty/throwing build menu on a real client | **REASONED** | runtime arrays only build client-side on menu open |
| Crafting panel actually displays + lets you craft after spawn | **REASONED** | requires a live windowed client |

## Remaining client-only risks I could NOT prove headless

These are the gaps a real Windows client join would close. None are evidence of a
defect — they are the inherent limit of headless QA, listed so "playable" is never
claimed beyond what was checked:

1. **Fix A live fire + visible effect.** I proved `UpdateKnownRecipesList()`
   resolves and that the recipes are in ObjectDB, but I did not observe the
   `Player.OnSpawned` postfix run on a real client nor watch the 9 recipes become
   clickable in the crafting panel. Requires a windowed client join.
2. **Fix B under a real build-menu open.** The spade table's runtime arrays
   (`m_availablePieces`/`m_selectedPiece`/`m_lastSelectedPiece`) are only built when
   a client opens the build menu. I verified the *inputs* (m_pieces 4/4, field types,
   `PieceCategory.Max=8`) and the patch logic, but did not watch a client tab the
   "Trail" category without an empty menu or an `IndexOutOfRange`.
3. **Actual craft/place consumption.** Whether clicking a recipe consumes the
   spec'd ingredients and yields the item, and whether placing a hammer/spade piece
   succeeds and deducts cost, is unobserved. SpecCheck guarantees the requirement
   *data*; it cannot guarantee the *client interaction*.
4. **Icons / names render.** PNG icons load from the plugin folder client-side;
   missing icons degrade to blank squares but don't block crafting. Unverified
   visually.
5. **Cairn E-gesture (repair/upgrade) + comfort floor + Shift+E debug damage.** These
   `CairnInteractable` / `SE_Rested` paths are client-input-driven and out of scope
   for this registration-usability card; flagged for an in-world playtest pass.
6. **Multi-mod load order.** Verified clean with only SBPR loaded. `Priority.Last`
   + `HarmonyAfter` are designed for peer-mod coexistence but a real modpack was not
   tested.

## Recommended next step

A single **windowed-client smoke session** (join Niflheim, stand at Explorer's
Bench, craft one ink + the spade, open hammer + spade build menus, place a sign and
a cairn) closes risks 1–5 in ~5 minutes. The headless evidence above makes that a
confirmation pass, not a discovery pass — every data-layer prerequisite for
"playable" is verified present; what remains is watching the client UI honor it.

**Verdict: PASS at the data + assembly layer for all four registration-usability
criteria. "Playable" is verified as far as a dedicated server + assembly metadata
can prove it; the client-input last mile is reasoned and itemized above, not
claimed.**
