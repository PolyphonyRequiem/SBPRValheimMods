---
title: "Iron Compass — HUD compass overlay, the earned no-map orientation payoff (v3 impl spec)"
status: current
purpose: "Build-ready architect spec for the v3 Swamp-tier Iron Compass: a worn Trinket accessory whose HUD overlay finally grants the cardinal orientation the no-map pillar deliberately withholds — WITHOUT ever touching the local map. Converts the locked design (docs/design/nomap.md §8, requirements.md §566, PARKED §v3) + card t_d35405e3 acceptance criteria into a buildable HOW: the additive ItemDrop, the Hud.Awake postfix that mounts an SBPR_CompassHud UGUI overlay under Hud.instance.m_rootObject, the camera-yaw-driven needle with lag, the pitch→tilt map, and the equip-gate. Corrects the design doc's HaveItem carry-gate to the GetEquippedItems() slot equip-gate the Cartographer's Kit already proves in-repo, and surfaces the card's misread of requirements.md:696 (custom mesh is DEFERRED to v0.2+, not mandated) as an open question for Daniel. Authored by the architect spec-pass (card t_d35405e3); Daniel gates the merge. IMPLEMENTED in card t_ee61472f (2026-06-17) — Daniel's Q1–Q4 answers folded in (recipe Iron×4/Ooze×2/RedPigment×1; sprite HUD, world mesh deferred; lerp lag Config.Bind-tunable; anchor a Config enum default TopCenter, NoMap-safe, extensible to the carry-disc + Eye-of-Odin minimap)."
---

# Iron Compass — HUD compass overlay, the earned no-map orientation payoff

The design ([`nomap.md` §8 "Iron Compass (Swamps)"](../../design/nomap.md)) is the locked
*what*. The kanban card **t_d35405e3** is the locked *acceptance shape*. This doc is the
buildable *how*: the additive `SBPR_IronCompass` Trinket, the `Hud.Awake` postfix that mounts a
`SBPR_CompassHud` UGUI overlay under `Hud.instance.m_rootObject`, the camera-yaw-driven needle
with lag, the pitch→tilt map, the equip-gate, the `Features/` placement, and the SpecCheck
manifest impact.

> **Why this item exists (the load-bearing thesis — do not erode it).** v1 deliberately ships
> "minimap ONLY, freely rotating, **no north indicator**" (`requirements.md:646`), and v2's Local
> Map keeps that disorientation as Daniel-locked intended difficulty (the cartography spec's
> 2026-06-12 re-lock §2H.1, verbatim: *"there is **no** north indicator"*; the held disc's interior
> rotates, the bezel is fixed, nothing points north). The Iron Compass is the **earned tool** that
> finally grants cardinal orientation — and it grants it on a **separate HUD overlay**, *never* by
> adding a north arrow back onto the map. **Putting a north arrow on the local map would delete this
> item's entire reason to exist** and reverse a Daniel-locked difficulty choice. The withheld
> orientation IS the design; the compass is the payoff for earning your way to iron.

> **Clean-side note (ADR-0001):** every decomp line cited here is the base game
> (`assembly_valheim`), which is **fair game to read and adapt** (repo AGENTS.md + the 2026-06-09
> clarification). Line numbers are from
> `/home/polyphonyrequiem/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (this box)
> and were grepped **live during this pass** — re-confirm against the build assembly if the decomp
> drifts. `BugattiBoys/PortalIndicator` (the `nomap.md` precedent for "a HUD overlay anchored to the
> map area") is **reference-only**: its *behaviour* (a screen-space HUD widget that reads world/camera
> state) is reproduced from vanilla primitives only — no third-party code is read or copied. The
> camera-driven needle + pitch-tilt is net-new SBPR fiction.

> **ADR-0006 (load-bearing):** the `SBPR_IronCompass` item is built **additively**
> (`Assets.ConstructItemShell` → `new`'d `SharedData`, set fields), never by `Instantiate`-ing a
> vanilla item prefab and stripping it. The HUD overlay is built additively too — `new GameObject()`
> + `AddComponent<Image>()`/`RectTransform` parented under the existing `Hud.m_rootObject`, reading
> no vanilla prefab as a mutable base.

> ✅ **STATUS: IMPLEMENTED (card t_ee61472f, 2026-06-17) — Daniel gated.** This spec was
> build-ready in its mechanics and blocked on **4 open questions (§1)**. **Daniel answered all
> four on 2026-06-17** (recipe `Iron ×4 + Ooze ×2 + Red Pigment ×1`; sprite HUD with the held
> world-mesh deferred to v0.2+; lerp lag Config.Bind-tunable; anchor a Config **enum** default
> `TopCenter`, NoMap-safe, scaffolded to extend to the carry-disc + the future Eye-of-Odin
> minimap). Those answers (which **supersede the architect defaults below**) are folded into §1
> and the code. The implementation lives in `src/SBPR.Trailborne/Features/Exploration/`
> (`IronCompass.cs`, `SBPR_CompassHud.cs` + the `CompassHudBootstrapPatch`). Build 0/0.
> **logs-green ≠ playable** — the AT-COMPASS-* close on Daniel's in-game playtest.

---

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. This feature adds **+1 entry** (one new
item recipe — the compass is a single craftable Trinket, no build piece):

| # | Manifest entry | Kind | Resources (LOCKED — Daniel Q1, 2026-06-17) | Station |
|---|---|---|---|---|
| 1 | `SBPR_IronCompass` | item recipe (amount 1) | **`Iron ×4, Ooze ×2, Red Pigment ×1`** (`Iron` + `Ooze` vanilla; Red Pigment = `SBPR_InkRed` via `Pigments.PigmentRedName`) | `piece_sbpr_explorers_bench` |

**Resource prefab-name caveats (must match vanilla internal IDs / SBPR consts, or SpecCheck flags a
NULL `m_resItem`) — verified this pass against the wiki corpus `Internal ID` field
(`/home/polyphonyrequiem/valheim/sbpr-corpus/wiki/fandom/`):**
- Iron = vanilla **`Iron`** (verified `Iron.md`) — the Swamp metal, the v3 tier gate. This is the
  one non-negotiable cost: iron-gating is what makes the compass a *Swamp* payoff (`requirements.md:566`).
- FineWood / LeatherScraps = vanilla **`FineWood`** / **`LeatherScraps`** (proposed body/strap mats,
  gated on Q1).
- Station = `Trailhead.ExplorersBenchName` (`piece_sbpr_explorers_bench`), resolved via
  `RecipeHelpers.FindStation` — the **exact** idiom the Cartographer's Kit and Local Map use
  (`CartographersKit.cs:201`, `LocalMap.cs:197`), never a literal.

**The SpecCheck row shape (gotcha — same as the Cartographer's Kit):** this is an `Item`-only row
(recipe, `Station` set, `Amount = 1`) — **no** `Piece`, **no** build-piece row. A `RecipeSpec` with
both Item and Piece null or both set is silently skipped — match the Kit's shape exactly. The compass
is additively constructed (`Assets.ConstructItemShell`), so `SpecCheck.CheckIcon` (C1) **ERRORs at
boot** if the real icon PNG didn't ship — bundle a placeholder compass icon (the `m_icons[0]`
fallback path, `Assets.cs:1246`).

The card that touches `SpecCheck.cs` cites **this doc** alongside the existing sources. Code + spec +
SpecCheck row move in the **same PR** (spec-first rule).

---

## 1. Open questions for Daniel (the card's gate — answer before impl)

> ✅ **ALL FOUR RESOLVED by Daniel 2026-06-17 — these answers SUPERSEDE the architect-proposed
> defaults in the Q-blocks below (kept verbatim as decision history; do not delete — annotate).**
> The implementation (card t_ee61472f) is built to these answers, not the proposed defaults:
> - **Q1 recipe — LOCKED:** `Iron ×4 + Ooze ×2 + Red Pigment ×1` @ Explorer's Bench (NOT the
>   proposed `Iron ×2 / FineWood ×4 / LeatherScraps ×4`). Iron = the Swamp tier gate; Ooze = the
>   Swamp Blob/Oozer drop (wet resin bedding the needle); Red Pigment = `SBPR_InkRed` (the north
>   tip). SpecCheck row 1 + the dataset row carry this exact tuple. (`IronCompass.cs` §3.2.)
> - **Q2 mesh — SPRITES for the HUD; world mesh DEFERRED.** The overlay is 2D UGUI → a sprite is
>   the native tool; v0.1 draws the dial + needle from procedural UGUI primitives (legible, zero
>   art dependency) with `Image` components so an authored sprite drops in later. The held-trinket
>   *world* mesh is deferred to v0.2+ (the `:696` deferral Daniel kept); placeholder item art +
>   the `iron_compass_v0.1.png` icon are fine for v1.
> - **Q3 lag — ENABLED, Config.Bind-tunable.** `IronCompass.NeedleLag` (default 8, range 0.5–30)
>   drives `Mathf.LerpAngle(cur, target, dt * rate)`. Converge the feel in one joined session, then
>   bake into `SBPR_CompassHud.DefaultNeedleLag`.
> - **Q4 anchor — Config.Bind ENUM, default `TopCenter`, NoMap-safe.** `CompassAnchor` ships with
>   `TopCenter` (v1, wired) + `BelowMapDisc` / `OnMapDiscOverlay` (dock to the carry-state Local
>   Map disc, t_7dd54899) + `EyeOfOdinMinimap` (forward-ref) scaffolded from day one — the unwired
>   cases resolve to `TopCenter` with a one-time log until their dock targets exist. Anchor/size/
>   offset are all `Config.Bind`-tunable (`IronCompass` section).

The card body and the design note leave four things genuinely Daniel's to call. As architect I
have a proposed default for each (grounded in precedent), but all four are pacing / art-scope / UX
calls — so this doc **blocks** on them rather than silently picking. Each answer changes a specific,
isolated part of the build (named below), so a late answer is cheap to fold in.

### Q1 — Recipe + exact tier numbers
*(card AC: "gated to Swamp/iron progression")*

The hard constraint is locked: **iron-gated**, crafted at the **Explorer's Bench** (the Trailborne
crafting hub every other exploration tool uses). What's open is the rest of the bill of materials and
the iron count.

- **Architect-proposed default:** `Iron ×2, FineWood ×4, LeatherScraps ×4`, crafted at
  `piece_sbpr_explorers_bench`. Two iron keeps it a real Swamp-tier commitment without being punitive
  for a single QoL accessory; FineWood + LeatherScraps read as "a brass instrument on a leather strap."
- **What it changes in the build:** only the `BuildReq` list and the SpecCheck row-1 resources (§0,
  §3.2). A late answer is a one-line edit. Nothing structural depends on the numbers.

### Q2 — 🔴 Custom mesh art: REQUIRED now, or placeholder-now / mesh-deferred? (the card misread the spec)

**The card body asserts "custom-authored mesh art per `requirements.md:696` (no placeholder
primitive)." That is a misread of the cited line and must be resolved before an impl card is cut.**
`requirements.md:696` actually reads (verified this pass, under the heading **"### Deferred to v0.2+
(after gameplay works)"**):

> *"Custom mesh authoring (if/when v2 brings the Surveyor's Table, Iron Compass, Tents — those have
> genuine geometry needs)"*

So the spec **defers** Iron Compass custom mesh to v0.2+ "after gameplay works" — it does **not**
mandate ship-quality mesh as an acceptance gate for the first build. The locked asset doctrine
(`requirements.md` "Asset generation as needed") is: *"Starbright generates placeholders on demand…
Polish quality is explicitly NOT the bar — 'you can tell what it is' is the bar."*

There is also a **practical reason the held-item mesh barely matters here**: the Iron Compass's entire
function is the **HUD overlay**, which the player stares at — not the third-person item-in-hand model.
A Trinket sits in the accessory slot; its in-world mesh is rarely seen. The art that actually needs to
be legible is the **2D overlay sprite** (the dial + needle), not a 3D compass mesh.

- **Architect-proposed default (matches the locked doctrine):** ship a **FLUX placeholder icon + a
  simple placeholder held mesh** for the first gameplay build (the overlay sprite is the art that
  matters and gets first attention), and keep custom mesh authoring **deferred to v0.2+** exactly as
  `requirements.md:696` says. The acceptance test for art is "you can tell what it is," not "ship
  quality."
- **If Daniel wants custom mesh as a hard gate for v3.0:** that's a legitimate override — but it
  should be a **conscious reversal** of the `:696` deferral, not something an impl card inherits from a
  misread. It would add a custom-mesh-authoring dependency (FLUX/Unity asset pipeline) to the critical
  path and is the single biggest scope lever on this card.
- **What it changes in the build:** the asset deliverables only (placeholder vs. authored mesh). Zero
  code-structure impact — `Assets.ConstructItemShell` + the icon fallback handle either.

### Q3 — Needle-lag feel (the "slight lag" tuning)
*(design note §8: "drive a lerp-toward-target rotation on the needle (the 'slight lag')")*

The needle lerps toward the true heading rather than snapping — the design note calls it "slight lag."
The exact lerp rate is a feel knob that can only be confirmed in-game (the Cairn-banner lesson:
client-only visuals that "shipped wrong in-world TWICE while building 0/0" — `requirements.md:139`).

- **Architect-proposed default:** expose the lerp rate as a **range-clamped `Config.Bind` entry**
  (`SBPR_CompassNeedleLag`, default ≈ `8f` deg/sec-equivalent via `Mathf.Lerp(cur, target, dt * rate)`),
  so Daniel converges the feel in **one** joined session without a recompile cycle — exactly the
  banner-windsock pattern (`requirements.md:139`). Bake the chosen value into the default afterward.
- **What it changes in the build:** one config field + the `Update` lerp call. Isolated.

### Q4 — Overlay anchor + footprint on the HUD
*(design note §8: "positioned bottom-center below the map-icon area")*

The design note says "a small UGUI Image as child of `Hud.instance.m_rootObject`, positioned
bottom-center below the map-icon area." Under the SB server's **default NoMap** there is **no minimap**
(the "map-icon area" the note anchors to may not be present), so the anchor needs a NoMap-safe choice.

- **Architect-proposed default:** anchor to a **fixed screen-space position** (top-center or
  bottom-center) on `Hud.m_rootObject`, **independent of the minimap's presence** — the compass must
  render correctly with NoMap on (the same HUD-overlay-doesn't-depend-on-the-map doctrine the Twisted
  Portal overlay's `AT-NOMAP-SAFE` establishes). Make the anchor + size `Config.Bind`-tunable alongside
  Q3 so Daniel places it in the same joined session.
- **What it changes in the build:** the `RectTransform` anchor/offset constants (config-exposed).
  Isolated; no structural impact.

---
## 2. Architecture — one item + one overlay + one patch, the vertical slice

The Iron Compass is the **lowest-risk item in the whole nomap design** — the design note ranks it
"3. ✅ Iron Compass (pure HUD overlay, no game-state patches)" (`nomap.md:219`). It is three pieces of
new code, no game-state mutation:

| Part | What it is | Construction | Patches |
|---|---|---|---|
| **`SBPR_IronCompass`** | A Trinket accessory ItemDrop (the earned gate) | Additive (`Assets.ConstructItemShell`) | none — it's just an item + recipe |
| **`SBPR_CompassHud`** | A client-only `MonoBehaviour` UGUI overlay (dial + needle) | Additive (`new GameObject` + `AddComponent`) under `Hud.m_rootObject` | none — pure `Update()` reads |
| **`CompassHudBootstrapPatch`** | The one Harmony hook that mounts the overlay | `[HarmonyPatch(typeof(Hud), "Awake")]` postfix | **1 postfix** (so PatchCheck applies — NOT patch-free) |

**Key correction vs. the design note (load-bearing):** the design note (`nomap.md:160`) gates
visibility on `Player.m_localPlayer.GetInventory().HaveItem("SBPR_IronCompass")` — a **carry**-gate
(true if the item is anywhere in your pack). That is **wrong for an accessory**: a Trinket grants its
effect only when **equipped in the accessory slot**, not merely carried. The in-repo precedent is
`CartographersKit.IsWearingKit` (`CartographersKit.cs:233`), which reads the **public**
`Inventory.GetEquippedItems()` (decomp `:57192`), filters on `m_shared.m_itemType == Trinket`, and
matches `m_dropPrefab.name` (clone-suffix-stripped) against the item const. **We use the equip-gate,
not the carry-gate** — see §4.3. (The note's `HaveItem` would let a compass in your backpack work while
unequipped; that breaks the "wear the instrument" fiction and the accessory-slot opportunity cost.)

**No game-state patches.** There is no `Minimap` patch, no map mutation, no ZDO write, no networked
state. The compass reads `GameCamera.instance.transform` and the local player's equipped items, both
client-side, every frame. This is why it is the safest item in the design — it cannot desync, cannot
corrupt a save, and is invisible to other players and to vanilla/other-modded clients (it simply does
nothing when `SBPR_IronCompass` isn't equipped).

---

## 3. The Iron Compass item (`SBPR_IronCompass`)

### 3.1 Item construction (additive — the Cartographer's Kit precedent, verbatim pattern)

Build the item shell additively and set the Trinket slot. The exact idiom is the in-repo
`CartographersKit` / `LocalMap` item path:

```csharp
// In Features/Exploration/IronCompass.cs (new slice — see §6 placement)
public const string CompassName = "SBPR_IronCompass";

public static void RegisterPrefabs(ZNetScene zns)
{
    var go = Assets.ConstructItemShell(CompassName);   // news a fresh ItemDrop + SharedData,
                                                        // seeds m_icons[0] = FallbackIcon (crash-safe),
                                                        // seeds m_attack (kills the per-frame tooltip NRE, t_2dd7c705)
    if (go == null) return;
    var drop   = go.GetComponent<ItemDrop>();
    var shared = drop.m_itemData.m_shared;

    shared.m_name        = "$sbpr_ironcompass";          // localized token (§3.3)
    shared.m_description = "$sbpr_ironcompass_desc";
    shared.m_itemType    = ItemDrop.ItemData.ItemType.Trinket;   // enum 24 — VERIFIED decomp :57652
    shared.m_maxStackSize = 1;
    shared.m_equipDuration = 0.5f;
    shared.m_useDurability = false;                       // the compass does not wear (unlike the Twisted Key)
    // m_icons[0] already a placeholder from ConstructItemShell; the real PNG ships in the bundle.

    zns.m_prefabs.Add(go);   // (mirror the Kit's registration; the helper handles the hash table)
}
```

**Why `m_itemType = Trinket` (enum 24), verified:** the decomp `ItemType` enum (line 57627) ends with
`Trinket = 24` (confirmed this pass — `sed` of lines 57627–57654). The vanilla equip pipeline treats
Trinket as a real equip slot: `VisEquipment.SetTrinketItem` (`:28478`), `SetTrinketEquipped`
(`:29080`), and `Humanoid` world-level gating both branch on `ItemType.Trinket` (`:13825`, `:13992`,
`:20527`). So a Trinket equips into the accessory slot and `GetEquippedItems()` returns it — which is
exactly what the equip-gate (§4.3) reads.

### 3.2 Recipe + station (gated on Q1)

```csharp
public static void DoObjectDBWiring(ZNetScene zns)
{
    var odb = ObjectDB.instance;
    var recipe = ScriptableObject.CreateInstance<Recipe>();
    recipe.name = "Recipe_" + CompassName;
    recipe.m_item = zns.GetPrefab(CompassName).GetComponent<ItemDrop>();
    recipe.m_amount = 1;
    recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);  // the Kit idiom :201
    recipe.m_resources = new[]
    {
        Assets.BuildReq("Iron",          2, "Exploration"),   // the LOCKED Swamp-tier gate
        Assets.BuildReq("FineWood",      4, "Exploration"),   // proposed (Q1)
        Assets.BuildReq("LeatherScraps", 4, "Exploration"),   // proposed (Q1)
    };
    odb.m_recipes.Add(recipe);
}
```

`Iron` is the locked, non-negotiable gate (`requirements.md:566` — "iron is Swamps metal"). The other
two resource rows + the iron count are the Q1 default; only this list changes if Daniel re-tunes.

### 3.3 Localization

Two `$sbpr_*` tokens (`$sbpr_ironcompass`, `$sbpr_ironcompass_desc`) defined in the repo's English
localization file and resolved through `Localization.instance.Localize` (the in-repo idiom —
`SurveyorTableTag.cs:573`, `LocalMapController.cs:315`). The description should name the payoff so the
player understands what they earned: e.g. *"A worn iron compass. Wear it to read true north on a dial
at the edge of your sight — the swamp can't hide the cardinal directions from you anymore."*

---
## 4. The HUD overlay (`SBPR_CompassHud` + `CompassHudBootstrapPatch`)

> ### 🔴 4.0 Render-bug fix (card t_61aff612, 2026-06-19) — the overlay rendered NOTHING when worn
>
> **Symptom:** Daniel equipped the compass in the Trinket slot on v0.2.28 and saw nothing at
> top-center. The mount succeeded and the equip-gate matched — yet the dial never showed.
>
> **Root cause (a dead `Update` pump, NOT the candidate fork the bug card proposed):** the
> `SBPR_CompassHud` MonoBehaviour lives on the host GameObject, and the original `_root` *was*
> that host. The visibility toggle did `_root.gameObject.SetActive(wearing)` — i.e. the component
> deactivated **the GameObject it lives on**. Unity does not call `Update()` on a component whose
> GameObject is inactive, and `Update()` is the *only* code that ever calls `SetVisible(true)`. So
> the closing `SetVisible(false)` in `Build()` froze the overlay inactive **permanently** — the pump
> that would un-hide it had been switched off by the very call that hid it. Total, deterministic
> absence, matching "nothing." (This also explains why the sibling **Sunstone Lens** overlay — same
> self-deactivating-host structure — was dead too: one shared mechanism, not two anchor bugs.)
>
> **Fix:** visibility now toggles a dedicated **content child** (`_content`), never the host. The
> host stays active so its `Update()` keeps pumping; `_content` carries everything visible and is
> what `SetVisible` activates/deactivates. (§4.2, §4.3 updated to match.)
>
> **Two secondary fixes shipped in the same cut:**
> - **Dial sprite:** `Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd")` FAILS to load on
>   Valheim's 0.221.x Unity build (client log: *"The resource UI/Skin/Knob.psd could not be loaded
>   from the resource file!"*), leaving a null sprite. Replaced with a procedurally-generated round
>   disc (`DiscSprite()`, the `SunstoneLensHudOverlay.RingSprite` idiom — zero asset dependency). An
>   authored sprite can still drop into the same `Image` later.
> - **Diagnostic logging (`IronCompass.DebugMount`, default ON for this cut):** the bootstrap mount,
>   the `IsWearingCompass` transitions, and the resolved anchor/size on first show now LogInfo so a
>   fresh client `LogOutput.log` can tell a mount/pump failure apart from an off-screen placement in
>   one playtest. Bake `DefaultDebugMount` to `false` once Daniel confirms the dial renders in-game.

### 4.1 Mounting the overlay — the `Hud.Awake` postfix (the one Harmony patch)

The overlay is attached once, when the HUD comes up, via a postfix on `Hud.Awake`:

```csharp
[HarmonyPatch(typeof(Hud), "Awake")]
internal static class CompassHudBootstrapPatch
{
    private static void Postfix(Hud __instance)
    {
        if (!ServerContext.OnSBServer) return;               // SBPR server-gating doctrine
        if (__instance == null || __instance.m_rootObject == null) return;
        // Idempotent: never double-mount (Hud can re-Awake on scene reload)
        if (__instance.m_rootObject.transform.Find("SBPR_CompassHud") != null) return;

        var host = new GameObject("SBPR_CompassHud", typeof(RectTransform));
        host.transform.SetParent(__instance.m_rootObject.transform, worldPositionStays: false);
        host.AddComponent<SBPR_CompassHud>().Build();        // mounts the dial + needle children
        // (t_61aff612) DebugMount-gated LogInfo: proves the postfix ran + host mounted (vs. the
        // formerly-silent success path that couldn't be confirmed in a client log).
    }
}
```

> ⚠️ **Do NOT deactivate the host (`SBPR_CompassHud`) GameObject to hide the overlay** — it carries
> the `SBPR_CompassHud` MonoBehaviour, and an inactive GameObject gets no `Update()` (the t_61aff612
> render bug). Visibility toggles the `_content` child instead (§4.3).

**Verified hooks:** `Hud` is a `MonoBehaviour` (decomp `:38930`); `Hud.m_rootObject` is a public
`GameObject` (`:38949`); `Hud.instance => m_instance` (`:39259`); `Hud.Awake` is a private void
(`:39270`). Parenting under `m_rootObject` means the overlay inherits the HUD's Canvas + GuiScaler
(the same parent the vanilla health/food/stamina `RectTransform`s live under — `:38989`, `:39006`,
`:39029`), so it scales and shows/hides with the rest of the HUD automatically (when `m_rootObject`
slides to `s_notVisiblePosition` to hide the HUD, the compass goes with it — free correct behaviour).

**MUST be registered in `Plugin.Awake`** via `harmony.PatchAll(typeof(CompassHudBootstrapPatch))` or
**PatchCheck ERRORs at boot** (the "unregistered patch ships dead" lesson, t_564f695a — `PatchCheck.cs`
diffs `[HarmonyPatch]`-attributed classes against the woven set and ERROR-logs any that
`Plugin.Awake()` forgot, `:79`/`:89`). This is the single reason the compass is **not** patch-free.

### 4.2 Building the overlay — additive UGUI (the MapViewer precedent)

`SBPR_CompassHud.Awake` constructs its children additively — the same `new GameObject` + `AddComponent`
UGUI idiom the in-repo `MapViewer` uses (`MapViewer.cs:47` host, `AddComponent` children). Two visual
layers:

- **The dial** (`Image`, the fixed backdrop): a procedurally-generated round disc sprite
  (`DiscSprite()` — the `SunstoneLensHudOverlay.RingSprite` idiom, zero asset-bundle dependency)
  behind the cardinal letters (N/E/S/W), sized ~`128×128` px, anchored per Q4. Does **not** rotate.
  *(t_61aff612: the original `Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd")` does not load
  on Valheim's 0.221.x Unity build; the procedural disc replaces it. An authored sprite can still
  drop into this same `Image` later.)*
- **The needle** (`Image` child, the rotating element): a north-pointing arrow sprite. **This** is the
  element we rotate to heading. Its `RectTransform.localRotation` is driven in `Update` (§4.4).

The dial sprite is procedural (above); the needle uses procedural UGUI quads (placeholder-grade per
Q2). Anchor/size are `Config.Bind`-tunable (Q4). Everything visible parents under a **`_content`
child** of the host (the t_61aff612 visibility container — §4.3), not directly under the host.

### 4.3 Visibility gate — equip, not carry (the corrected gate)

In `Update`, gate the whole overlay's visibility on **wearing** the compass — reading the public
`Inventory.GetEquippedItems()` exactly as `CartographersKit.IsWearingKit` does, retargeted to the
Trinket slot:

```csharp
private static bool IsWearingCompass(Player player)
{
    if (player == null) return false;
    var inv = player.GetInventory();
    if (inv == null) return false;
    foreach (var item in inv.GetEquippedItems())          // PUBLIC API, decomp :57192
    {
        if (item?.m_shared == null) continue;
        if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Trinket) continue;  // SLOT gate
        var drop = item.m_dropPrefab;
        if (drop != null && StripCloneSuffix(drop.name) == IronCompass.CompassName)
            return true;
    }
    return false;
}
// StripCloneSuffix: cut at first '(' or ' ' — the CartographersKit mirror of ItemDrop.GetPrefabName (:58940)
```

`Update` toggles the **`_content` child's** `SetActive(wearing)` (NOT the host — t_61aff612) and
early-returns when not worn, so the needle math only runs while the compass is equipped (zero
per-frame cost otherwise). **This is the §2 correction realized:** the design note's `HaveItem`
carry-gate is replaced by the slot equip-gate the Kit already proves in-repo. (Filtering on the
Trinket slot — not just the prefab name — means a future Trinket can't accidentally satisfy the
check, the exact discipline `CartographersKit.cs:245` documents.)

### 4.4 Driving the needle — camera yaw with lag, pitch → tilt

In `Update`, when worn, read the live camera transform and drive the needle:

```csharp
private void Update()
{
    var player = Player.m_localPlayer;
    bool wearing = IsWearingCompass(player);
    // Toggle the _content CHILD, never the host — the host carries this Update pump (t_61aff612).
    if (_content.activeSelf != wearing) _content.SetActive(wearing);
    if (!wearing || GameCamera.instance == null) return;

    Vector3 euler = GameCamera.instance.transform.eulerAngles;   // VERIFIED: GameCamera :85308, instance :85422

    // --- Yaw → needle rotation, with the "slight lag" (design §8) ---
    // The needle points to WORLD NORTH relative to where the camera faces:
    // as the camera yaw increases (turn right), the world rotates left under you,
    // so the needle rotates by -yaw. Lerp toward the target so it lags slightly.
    float targetZ = -euler.y;
    _needleAngle = Mathf.LerpAngle(_needleAngle, targetZ, Time.deltaTime * _lagRate);  // _lagRate = Q3 config
    float pitchTilt = MapPitchToTilt(euler.x);                   // §4.5
    _needleRect.localRotation = Quaternion.Euler(pitchTilt, 0f, _needleAngle);
}
```

**Verified:** `GameCamera` is the `MonoBehaviour` on the camera GameObject (`:85308`), exposes the
static `instance` (`:85422`), and its `transform` is the live camera transform (it sets
`base.transform.position/rotation` each frame, `:241`–`:246`). `transform.eulerAngles.y` is the camera
yaw (compass heading source); `.x` is the pitch (look up/down). `Mathf.LerpAngle` handles the
0°/360° wrap correctly so the needle never spins the long way around.

### 4.5 Pitch → UI tilt (the "~45° tilt" mapping)

The design note (`nomap.md:162`) wants the dial to tilt as you look up/down — *"map 180° pitch range
to 45° UI tilt."* Camera `eulerAngles.x` wraps (looking up reads as ~350°, not −10°), so unwrap to a
signed −90..+90 range first, then scale:

```csharp
private static float MapPitchToTilt(float rawPitchEuler)
{
    // Unwrap 0..360 → signed -180..180, then clamp to the head's real pitch envelope (~-90..90)
    float signed = rawPitchEuler > 180f ? rawPitchEuler - 360f : rawPitchEuler;
    signed = Mathf.Clamp(signed, -90f, 90f);
    // Map the ~180° pitch span to a ~45° UI tilt (design §8): scale factor 45/90 = 0.5 of the clamp half-range
    return signed * (MaxTiltDegrees / 90f);   // MaxTiltDegrees ≈ 45 (Q4-tunable)
}
```

This gives the dial a subtle 3D-instrument feel — tilt the head up, the dial face tilts away — without
ever exceeding ~45° (legible at all times). The tilt magnitude is a feel knob; fold it into the Q3/Q4
config block.

---
## 5. Registration + wiring order (Registrar, PatchCheck, server-gating)

New file(s) in a new `Features/Exploration/` slice (§6):
- **`IronCompass.RegisterPrefabs(zns)`** — build + register the `SBPR_IronCompass` item (additive
  shell). Wire into `Runtime/Registrar.cs` in the `RegisterPrefabs` fan-out — placement is unconstrained
  (the item depends on no other SBPR prefab), but put it **after `CartographersKit`** to keep the
  exploration-tools grouped (`Registrar.cs:69`).
- **`IronCompass.DoObjectDBWiring(zns)`** — register the item into ObjectDB + add the recipe. Wire into
  the `DoObjectDBWiring` fan-out **after `Trailhead.DoObjectDBWiring`** (the Explorer's Bench station
  must exist for `FindStation` to resolve — same ordering constraint the Kit/Local Map have,
  `Registrar.cs:124`).
- **`SpecCheck.cs`** — add the one new row (§0); extend the `LOCKED SOURCE` comment to cite this doc.
- **One Harmony patch** (`CompassHudBootstrapPatch`, the `Hud.Awake` postfix) MUST be registered in
  `Plugin.Awake` via `harmony.PatchAll(typeof(CompassHudBootstrapPatch))` or **PatchCheck ERRORs at
  boot**. The `SBPR_CompassHud` MonoBehaviour and the item are otherwise **patch-free by construction**
  (component wiring + an additive item) — only the overlay-mount needs Harmony.
- **Server-gating:** the registration fan-out is already gated `if (!ServerContext.OnSBServer) return;`
  via the Registrar; the `Hud.Awake` postfix carries its own `OnSBServer` guard at the top (§4.1). The
  overlay is client-relevant (it draws on the local HUD); the item registration is server+client.

---

## 6. `Features/` placement

The Iron Compass is the first member of a new **`Features/Exploration/`** slice — it doesn't belong
under `Cartography/` (it deliberately does NOT touch the map) and isn't a `Portals/` or `Signs/`
concern. The slice holds:

```
src/SBPR.Trailborne/Features/Exploration/
├── IronCompass.cs              # the item: RegisterPrefabs + DoObjectDBWiring + CompassName const
├── SBPR_CompassHud.cs          # the client-only MonoBehaviour overlay (dial + needle + Update)
└── CompassHudBootstrapPatch.cs # the [HarmonyPatch(Hud,"Awake")] postfix that mounts the overlay
```

This mirrors the vertical-slice convention every other feature follows (one folder, the item + its
behaviour + its patches co-located). Future v3+ exploration tools that are HUD-overlay-shaped (not
map-shaped) land here too.

---

## 7. Named acceptance tests (the single source of truth for "done")

Observable criteria. **logs-green ≠ playable** — every AT closes only on Daniel equipping/using one
in-game on a joined client (repo honesty rule). The engineer reports per-AT status in each PR handoff;
the build PR does NOT self-close these.

- **AT-COMPASS-CRAFT** — `SBPR_IronCompass` crafts at the Explorer's Bench from the recipe-1 materials
  (Iron-gated); it's a **Trinket** (equips into the accessory slot, not a hand/utility slot); SpecCheck
  row 1 green at boot (recipe + icon).
- **AT-COMPASS-EQUIP-GATE** (🔴 the §2/§4.3 correction) — the overlay is visible **only while the
  compass is equipped in the Trinket slot**. Carrying it **unequipped** in the backpack shows **no**
  overlay (proves the equip-gate, not a carry-gate). Unequipping hides the overlay immediately.
- **AT-COMPASS-HEADING** (🔴 the headline, card AC) — with the compass worn, the needle points to
  **true world north**: face north → needle up; turn to face east → needle rotates to read east
  correctly; a full 360° turn sweeps the needle a full 360° with no long-way-around jump (the
  `LerpAngle` wrap).
- **AT-COMPASS-LAG** (design §8) — the needle **lags slightly** behind a fast camera spin and settles
  smoothly (the "slight lag" feel), not an instant snap. Lag rate tunable via `SBPR_CompassNeedleLag`
  in one joined session (Q3).
- **AT-COMPASS-TILT** (design §8) — looking **up/down** tilts the dial face up to ~45° and no more
  (clamped, legible); looking level returns it flat.
- **AT-COMPASS-NOMAP-SAFE** (🔴 the thesis guard) — the overlay renders correctly with the SB server's
  **default NoMap** (no minimap present). It does **not** depend on the minimap being on, and crucially
  it adds **no** north indicator to any map — the Local Map's no-north disorientation (cartography
  §2H.1) is **unchanged** by this feature. The compass is the *separate earned tool*; the map stays
  north-blind.
- **AT-COMPASS-HUD-HIDE** — when the HUD is hidden (e.g. the hide-HUD key, or `Hud.m_rootObject` slid
  to its not-visible position), the compass overlay hides with it (free correct behaviour from parenting
  under `m_rootObject`).
- **AT-COMPASS-VANILLA-ONLY** (clean-room) — no third-party HUD-overlay-mod code is read or copied; all
  hooks are base-game primitives (`Hud`, `GameCamera`, `Inventory.GetEquippedItems`). The
  camera-driven needle + pitch-tilt is net-new SBPR fiction.
- **AT-COMPASS-ART** (Q2-dependent) — *if Daniel keeps the `:696` deferral:* the placeholder dial +
  needle sprite is legible ("you can tell it's a compass," the locked art bar). *If Daniel overrides to
  require custom mesh for v3.0:* the authored mesh ships and is verified in-hand. **This AT's shape is
  set by the Q2 answer.**

---

## 8. Cross-doc updates (spec-first — move in the SAME PR as this spec) + decision log

### Cross-doc updates this spec PR carries (the docs half of spec-first)
- **`docs/v3/planning/index.md`** + **`docs/v3/planning/README.md`** — add this file's row/blurb to the
  v3 planning manifest (a **union** add: the scaffold already carries the Twisted Portal + trail-lights
  rows; this is a trivial add/add, no conflict with their content). This spec PR performs that union.
- **`docs/design/nomap.md` §8** — annotate (do not delete) the historical Iron Compass note with a
  one-line forward pointer: *"→ see `docs/v3/planning/iron-compass-impl-spec.md` (equip-gate corrected
  from `HaveItem`→`GetEquippedItems`; NoMap-safe anchor)"*, and correct the one factual drift the impl
  pass found (the `HaveItem` carry-gate → the Trinket equip-gate, §4.3).
- **`docs/datasets/PIECES_AND_CRAFTABLES.md`** — add the Iron Compass as a proper item dataset row
  (Trinket, v3 Swamp, Iron-gated, HUD-overlay function), per the dataset's format.

### Decision log (what's locked vs. what waits on Daniel)
**Locked by the architect this pass (grounded in precedent — no Daniel input needed):**
- `SBPR_IronCompass` is a **Trinket** (`m_itemType = Trinket`, enum 24 — decomp-verified `:57652`),
  crafted at the **Explorer's Bench**, **Iron-gated** (`requirements.md:566`).
- The overlay mounts via a **`Hud.Awake` postfix** under `Hud.m_rootObject` (decomp-verified
  `:38949`/`:39270`), built **additively** (ADR-0006); the needle is driven from
  `GameCamera.instance.transform.eulerAngles` (decomp-verified `:85422`).
- **Equip-gate, not carry-gate** — `GetEquippedItems()` + Trinket-slot filter + prefab-name match (the
  `CartographersKit.IsWearingKit` precedent, `:233`), correcting the design note's `HaveItem`.
- **NoMap-safe + map-untouched** — the compass NEVER adds a north arrow to any map; the withheld
  map-orientation stays withheld (cartography §2H.1). This is non-negotiable design thesis, not a knob.
- **One Harmony patch, PatchCheck-registered** — the `Hud.Awake` postfix is registered in `Plugin.Awake`
  (the unregistered-patch lesson, t_564f695a). Everything else is patch-free.
- New **`Features/Exploration/`** slice for placement.

**🔴 PROPOSED — waiting on Daniel (the §1 gate):**
- **Q1** — recipe + exact tier numbers (proposed: `Iron ×2, FineWood ×4, LeatherScraps ×4`).
- **Q2** — 🔴 custom mesh REQUIRED now, or placeholder-now / mesh-deferred-to-v0.2+ (the card misread
  `requirements.md:696`, which **defers** Iron Compass mesh — this needs Daniel's conscious call, not an
  inherited misread). Architect default: keep the deferral, ship a placeholder, the overlay sprite is
  the art that matters.
- **Q3** — needle-lag feel (proposed: `Config.Bind SBPR_CompassNeedleLag`, converge in one joined session).
- **Q4** — overlay anchor + footprint on the HUD (proposed: fixed NoMap-safe screen position, config-tunable).

The architect creates the single impl card (assignee = the SBPR engineer-systems profile that built the
Cartographer's Kit + Local Map) via `kanban_create` **once Daniel answers Q1–Q4**, linking it
`parents=[t_d35405e3]`. This card does NOT implement anything.
