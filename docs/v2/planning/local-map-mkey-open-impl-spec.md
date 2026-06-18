---
title: "Local-map open on M (not E) — buildable implementation spec"
status: current
purpose: "Build-ready architect spec for moving the bound-local-map OPEN gesture from E (Use) to M, removing the E-to-open path, swapping the equipped HUD prompt token, and — the load-bearing part — making SBPR OWN the M input edge in nomap-OFF so our viewer opens WITHOUT vanilla's full map also opening (no double-stack). Converts the 🟢 DECIDED M-key model (map-provider-model.md §1) into one section an engineer-ui implementer picks up cold: the Minimap.Update consume-prefix, the controller open/close routing, the bound-not-just-equipped semantics, the prompt token, and named acceptance tests grounded against LocalMapController.cs + the vanilla decomp at v1 HEAD. Authored by the architect spec-pass (card t_f9a04fda). The design doc is the WHAT; this is the HOW-to-pick-it-up-cold. Build is the engineer-ui child of this card; Daniel gates the merge."
owner: Daniel (design authority); architect (spec capture + grounding)
supersedes_partial:
  - "cartography-impl-spec.md §2G (Use-key open model) — M replaces E entirely"
  - "cartography-impl-spec.md §2F open-input wording (\"opens on the Use key (E)\") — open input is now M"
  - "requirements.md AT-MAP-EQUIP (\"press Use (E)\") — open input is now M"
---

# Local-map open on M (not E) — buildable implementation spec

The merged design doc [`map-provider-model.md`](../../design/map-provider-model.md) §1
(PR #155, **🟢 DECIDED by Daniel 2026-06-15**) is the locked *what*: **M is the single map
key; SBPR owns it.** A bound local map opens on **M**; the **E-to-open path is removed
entirely**; the equipped prompt reads **"[M] …"**. This doc is the buildable *how*: the
input-ownership mechanism that makes M work in `nomap=OFF` without stacking vanilla's map,
the controller edits, the prompt token swap, observable acceptance criteria, and the
`Features/Cartography/` placement. An `engineer-ui` implementer should build the whole
change from this section without re-deriving anything.

> **Why this card exists (drift, not new design).** The M-key model was DECIDED 2026-06-15
> and explicitly supersedes the older Use-key model (cartography-impl-spec §2F/§2G). The impl
> never followed it — Daniel's v0.2.26-dev playtest (2026-06-17, issue 3) found the bound
> local map still opens on **E** with an "[E]" prompt. This is a spec→impl handoff gap: the
> code at `LocalMapController.cs` is still on the §2G Use-key gesture. This spec is the bridge.

> **Clean-side note (ADR-0001):** every vanilla decomp line cited here is the base game
> (`assembly_valheim`), **fair game to read + adapt** (repo AGENTS.md + the 2026-06-09
> clarification). Line numbers were grepped live against
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (and `assembly_utils` for
> ZInput) — re-confirm against the build assembly if the dump drifts. No other mod's code is
> read or copied.

---

## 0. SpecCheck manifest impact (read first)

**None.** This feature changes an input binding + a HUD prompt token + a comment block on an
existing controller — no item recipe, no build piece. `Runtime/SpecCheck.cs` is **untouched**.
(Spec-first still applies: the implementation PR carries the code **and** the supersede
banners on `cartography-impl-spec.md` §2F/§2G **and** the `requirements.md` AT-MAP-EQUIP
amendment **together** — see §8. The spec-pass PR that introduces THIS doc also lands those
cross-doc edits so code and spec never diverge.)

---

## 1. What is DECIDED vs what THIS card specs

The design doc marks each call. Carry these forward verbatim — do **not** reopen 🟢.

**🟢 DECIDED (Daniel — build exactly this; map-provider-model.md §1):**
- **M is the single map key; SBPR owns it** (`:24`, `:48-51`). SBPR owns the M input edge in
  every state row — no vanilla Large map ever opens.
- **A bound local map (currently/most-recently equipped, still in inventory) → M opens it**
  (`:43`). Works even after the map is UNEQUIPPED, as long as it's still bound (still carried).
- **Default (no map bound, no Eye of Odin) → M does nothing** (`:42`), in BOTH nomap-on and
  nomap-off. SBPR fully suppresses vanilla's Large-map toggle.
- **Remove the E-to-open path entirely** (`:46`). The equipped prompt changes from "[E] Open
  map" to **"[M] Open map"**.
- **Eye of Odin (Mistlands, future) → M opens the personal global map** (`:44`). NOT built
  here — out of scope (§2 "out of scope" below). This spec leaves the default-row M-suppression
  in place so that path is a clean future addition, not a rework.

**This card SPECS (the HOW the design doc left to the architect, §9 open q):**
- The exact **clean interception point** for M in nomap-OFF (the design doc points at
  `SetMapMode :47444` but explicitly leaves the mechanism to the architect). Resolved in §3.
- The controller edits that move open/close from `"Use"` → `"Map"` (§4).
- The prompt token swap (§5) and the superseded-comment rewrite (§6).

**Out of scope (do NOT build here):**
- The **Eye-of-Odin global-map-on-M** path (`:44`; future/Mistlands).
- **Minimap-disc rendering** (separate card `t_a39d3e5f`; this card touches the OPEN gesture
  + prompt only, not the disc render).
- Any change to the provider state machine itself (`t_1d1b505b` / map-provider-binding-impl-spec
  shipped it; the open gesture reads `_provider`/`_equippedMap`, it does not redefine them).

---

## 2. Grounding — how vanilla wires M, and why M is HARDER than E (decomp-verified)

The whole reason the open gesture was on E (not M) is a real collision. Here is the exact
vanilla machinery, so the implementer understands what is being intercepted.

**2.1 Vanilla reads M inside `Minimap.Update()`** (decomp `:47021`). The relevant gate:

```
// Minimap.Update (decomp :47074-47099), the "no text input / not paused" branch:
bool flag = (Chat.instance == null || !Chat.instance.HasFocus()) && !Console.IsVisible()
         && !TextInput.IsVisible() && !Menu.IsActive() && !InventoryGui.IsVisible();
if (flag) {
    if (InTextInput()) { /* name-pin escape … */ }
    else if (ZInput.GetButtonDown("Map") || (ZInput.GetButtonDown("JoyMap") && …)) {  // :47085
        switch (m_mode) {
            case MapMode.None:  SetMapMode(MapMode.Small); break;   // :47090
            case MapMode.Small: SetMapMode(MapMode.Large); break;
            case MapMode.Large: SetMapMode(MapMode.Small); break;
        }
    }
}
```

- The **keyboard button is `"Map"`** (gamepad `"JoyMap"`); `"Map"` is the vanilla M key.
  Confirmed a real registered ZInput button (`ZInput.GetButtonDown("Map")`; `GetBoundKeyString`
  resolves it — assembly_utils `:9876-9899`).
- **`SetMapMode` (decomp `:47442`) coerces to `None` ONLY under `Game.m_noMap`** (`:47444`):
  ```
  public void SetMapMode(MapMode mode) {
      if (Game.m_noMap) { mode = MapMode.None; }   // :47444-47446
      …
  }
  ```
  So in **nomap-ON**, M is already dead (mode forced None, `m_smallRoot`/`m_largeRoot` stay off
  `:47457-47458`). In **nomap-OFF**, the `Map` button toggles None→Small→Large and the Large
  root **opens** — this is the surface that stacks on top of our viewer.

**2.2 The collision, stated precisely.** Daniel runs **nomap=OFF** for playtest. If we simply
read `GetButtonDown("Map")` in our controller and open our viewer, vanilla's *own*
`Minimap.Update` reads the **same** edge the same frame and ALSO opens the Large map → both
surfaces stack. (This is exactly the regression the old §2G comment block documents as the
reason the gesture was parked on E.) So binding to M is **not** a one-line `"Use"`→`"Map"`
token swap — we must also **prevent vanilla's `Minimap.Update` from acting on that M press.**

**2.3 The §2F precedent that rules OUT the obvious fix.** §2F already rejected making our
viewer report through `Minimap.IsOpen()` because that predicate is read in ~10 vanilla gates
(build/craft/interact/camera) — wide blast radius. The same logic rules out a skip-original
prefix on `SetMapMode` (many callers: `:47059` death, `:47064` auto-Small, `:48420` pin-center,
`:48537` teleport, plus our own WorldPinReconcile postfix at `WorldPinReconcilePatches.cs:66`).
**We must intercept narrowly — at the single `Map`-button READ, not at the map-mode WRITE.**

---

## 3. The mechanism — SBPR owns the M edge via a `Minimap.Update` consume-PREFIX (LOCK)

**The resolved interception point (closes the design doc's §9 open question): a Harmony
PREFIX on `Minimap.Update` that, when our controller wants the M press, ACTS on it and then
CONSUMES the `Map`/`JoyMap` button edge via `ZInput.ResetButtonStatus(...)` so vanilla's own
body (which runs immediately after, in the same method) reads a cleared edge and never toggles
its map.**

This is non-skip (the prefix returns `true` / void — vanilla's `Update` still runs for pins,
explore, shared-map fade, etc.). It only neutralizes the **one** thing we're claiming: the
`Map`-button edge.

### 3.1 Why a consume-prefix, and why `ResetButtonStatus` (grounded)

- **`ZInput.ResetButtonStatus(name)` is vanilla's OWN input-consume idiom.** Decomp
  `assembly_utils :10058` → `ButtonDef.ResetState()` (`:8129`) clears the press/held/released
  edges for that button:
  ```
  public void ResetState() {
      m_wasPressedDynamic = m_wasPressedFixed = false;
      m_releasedDynamic   = m_releasedFixed   = false;
      m_pressedDynamic    = m_pressedFixed    = false;
      m_heldDynamic       = m_heldFixed       = false;
      … timers = 0;
  }
  ```
  Vanilla uses exactly this to stop one frame's press from being read twice — e.g. InventoryGui
  resets `"Use"`, `"Inventory"`, `"JoyButtonB"` at `:41451-41460`. So **consuming `"Map"` after
  we handle it is the game's own pattern, not a novel hack.** After `ResetButtonStatus("Map")`,
  the very next `ZInput.GetButtonDown("Map")` (vanilla's read at `:47085`) returns false.

- **Why PREFIX on `Minimap.Update`, not the controller's own poll:** ordering must be
  deterministic. The prefix runs **before** vanilla's `Update` body every frame, so the consume
  lands before vanilla's `:47085` read. (The controller's `MonoBehaviour.Update` has no
  guaranteed order vs `Minimap.Update` — relying on Unity script order to win the race is
  fragile. The Harmony prefix makes it exact.)

- **Why NOT skip-original on the whole `Minimap.Update`:** that method also drives
  `UpdateExplore` (the Kit fog write, `:47056`), pin updates, shared-map fade, death→None.
  Skipping it would break all of that. We want vanilla's `Update` to run — minus the M toggle.

- **Why NOT a `SetMapMode` skip-prefix:** §2.3 — too many callers; wide blast radius. The
  consume-prefix touches exactly one input edge.

### 3.2 The patch (engineer builds this — illustrative, not prescriptive line-for-line)

New file `Features/Cartography/MinimapMKeyOwnerPatch.cs` (clean-side; patches base-game
`Minimap` only; registered in `Plugin.Awake()` so `PatchCheck` sees it woven):

```csharp
[HarmonyPatch(typeof(Minimap), "Update")]
public static class MinimapMKeyOwnerPatch
{
    // Runs BEFORE vanilla Minimap.Update each frame. If the M edge is down this frame and
    // a SBPR controller wants to own it, route it to the controller, then CONSUME the edge
    // so vanilla's own GetButtonDown("Map") at decomp :47085 sees nothing → no vanilla map.
    [HarmonyPrefix]
    public static void Prefix(Minimap __instance)
    {
        if (__instance == null) return;
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return; // client+gfx only
        var ctrl = LocalMapController.Instance;
        if (ctrl == null) return;

        // Mirror vanilla's OWN suppression gate (:47074) so we don't steal M while a text
        // field / inventory / console / pause menu / chat is up — those contexts must keep
        // their normal behavior, and our viewer must not open from a typed "m".
        if (!ctrl.MKeyContextAllowsMapToggle()) return;

        bool mEdge = ZInput.GetButtonDown("Map")
                     || (ZInput.GetButtonDown("JoyMap")
                         && (!ZInput.GetButton("JoyLTrigger") || !ZInput.GetButton("JoyLBumper"))
                         && !ZInput.GetButton("JoyAltKeys"));   // vanilla's JoyMap predicate, :47085
        if (!mEdge) return;

        // SBPR owns M in ALL states (design §1). Let the controller decide what it does
        // (open / close / nothing) based on provider+viewer state; then ALWAYS consume so
        // vanilla never toggles its Large map on this press.
        ctrl.HandleMapKeyPressed();

        ZInput.ResetButtonStatus("Map");
        ZInput.ResetButtonStatus("JoyMap");
    }
}
```

**Load-bearing details:**
1. **Consume happens UNCONDITIONALLY once we've claimed the gate** (after
   `MKeyContextAllowsMapToggle`). Even in the default "M does nothing" row (no bound map), we
   still consume — design §1 row 1: *"SBPR fully suppresses vanilla's Large-map toggle … both
   nomap-on AND nomap-off."* If we only consumed when opening, M would open vanilla's map
   whenever no local map is bound. So `HandleMapKeyPressed()` may be a no-op, but the consume is
   not.
2. **`MKeyContextAllowsMapToggle()` mirrors vanilla's `flag` gate** (`:47074`): false when
   `Chat.HasFocus()`, `Console.IsVisible()`, `TextInput.IsVisible()`, `Menu.IsActive()`, or
   `InventoryGui.IsVisible()`. Rationale: don't consume M while typing (so a name-pin field or
   chat still gets its "m"), and don't open our viewer from those contexts. This is the M-side
   analogue of the existing `CanOpenOnUse` modal discipline. **Plus** the SBPR modal check
   (`SignPanelInputBlock.AnyOpen`) so M typed into a sign panel / our own open viewer's text
   doesn't re-trip — except the CLOSE path (see §4.2), which must remain reachable while our
   viewer is up.
3. **Idempotent with the existing `Minimap.Update` POSTFIX** (`WorldPinReconcilePatches.cs:91`):
   different Harmony patch type, different timing (prefix vs postfix), no shared state. They
   compose cleanly — the postfix still does its periodic pin reconcile.

---

## 4. Controller edits — `LocalMapController.cs`

The controller is the state owner; the prefix routes the press into it. Three edits.

### 4.1 REMOVE the E/Use open gesture entirely

Delete the every-frame Use-key open block at `LocalMapController.cs:122-131`:

```csharp
// DELETE THIS WHOLE BLOCK (the §2G Use-key open path):
if (_mapEquipped && _equippedMap != null && !tableViewOwnsViewer
    && (ZInput.GetButtonDown("Use") || ZInput.GetButtonDown("JoyUse")))
{
    bool ourFieldViewOpen = …;
    if (ourFieldViewOpen) CloseFullView("use key toggle");
    else if (CanOpenOnUse(player)) OpenFullView(_equippedMap);
}
```

- **AT-MKEY-NO-E** depends on this deletion: after it, pressing E near nothing does nothing
  map-related (E still interacts with hover objects / Tables — that's vanilla's own
  `Player.Update → Interact`, untouched).
- `CanOpenOnUse(Player)` (`:249-261`) becomes **dead code** once this block is gone. The
  engineer should either delete it or repurpose its body into the new M-key context check
  (§4.3) — its logic (hover-object guard, modal guard) is exactly what the M path also needs,
  minus the hover-object guard (see §4.3 note). Don't leave it dangling (0-warning build:
  an unused private method is a CS0169/IDE0051-class smell; `<TreatWarningsAsErrors>` is ON).

### 4.2 ADD `HandleMapKeyPressed()` — the open/close router the prefix calls

```csharp
/// <summary>
/// SBPR owns the M edge (design §1). Called by MinimapMKeyOwnerPatch.Prefix when M is
/// pressed in a map-toggle-allowed context. Decides what M does from provider + viewer state:
///   • our field viewer already open  → close it (toggle-shut).
///   • a local map is BOUND (provider != null, still carried, even if unequipped) → open it.
///   • otherwise (no bound map)        → nothing (design §1 row 1; the prefix still consumes).
/// </summary>
public void HandleMapKeyPressed()
{
    bool tableOwns = CartographyViewer.IsViewerOpen
                     && CartographyViewer.CurrentMode == MapViewerMode.TableEdit;
    if (tableOwns) return;   // the Table owns the viewer; M doesn't fight it

    bool ourFieldViewOpen = CartographyViewer.IsViewerOpen
                            && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly;
    if (ourFieldViewOpen) { CloseFullView("M key toggle"); return; }

    // OPEN: design §1 row 2 — opens the BOUND map, which survives unequip while carried.
    // Prefer the live equipped instance; fall back to the provider (bound-but-unequipped).
    var toOpen = _equippedMap ?? _provider;
    if (toOpen != null) OpenFullView(toOpen);
    // else: no bound map → M does nothing (row 1). Prefix already consumed the edge.
}
```

- **🔑 Bound-not-just-equipped (design `:43`).** The OLD E-gesture only fired while
  `_mapEquipped`. The design says M opens the bound map **even after unequip, while still in
  inventory.** So the open source is `_equippedMap ?? _provider` — `_provider` is precisely the
  "most-recently-equipped, still-carried" instance the provider state machine already tracks
  (`:147-163`). This is the one **behavioral widening** vs the old gesture, and it's required by
  the spec. **AT-MKEY-OPEN** tests both the equipped and unequipped-but-carried cases.
- **Close path is reachable while our viewer is open.** `HandleMapKeyPressed` is called from the
  prefix whenever the context gate passes; the gate must NOT exclude "our own field viewer is
  open" (otherwise M couldn't close it). The SBPR-modal exclusion in §3.2 detail 2 applies to
  *sign panels* and *text fields*, not to our own read-only field viewer — mirror the existing
  `CanOpenOnUse` comment at `:120-121` ("the CLOSE path is NOT gated … must always be
  dismissible"). **AT-MKEY-CLOSE.**
- `OpenFullView` already messages "[blank map] imprint at a Surveyor's Table" and reads the
  imprinted survey (`:327-357`) — unchanged. M routes into the same open path E used, so the
  blank-map / unreadable-data handling is inherited.

### 4.3 `MKeyContextAllowsMapToggle()` — the context gate the prefix calls

```csharp
/// <summary>
/// True when M should be claimed by SBPR this frame. Mirrors vanilla's own Minimap.Update
/// suppression gate (decomp :47074) PLUS SBPR modal state, so we don't steal M while typing
/// or in a menu. NOTE: unlike CanOpenOnUse, there is NO GetHoverObject() guard — M is not an
/// interaction key, so hovering a door/chest/Table does not suppress M (only the Use key
/// yields to hover-interactions; that was the whole point of moving OFF Use).
/// </summary>
public bool MKeyContextAllowsMapToggle()
{
    // Vanilla's :47074 flag, minus Menu (we WANT M to work in normal play; Menu.IsActive
    // already false then) — keep the typing/console/inventory guards.
    if (Chat.instance != null && Chat.instance.HasFocus()) return false;
    if (Console.IsVisible() || TextInput.IsVisible() || InventoryGui.IsVisible()) return false;
    if (Menu.IsActive()) return false;

    // SBPR modal suppression — but NOT our own field viewer (M must close it, §4.2).
    // A sign panel / paint panel up → M is text/other, don't claim it.
    if (SBPR.Trailborne.Features.Signs.SignPanelInputBlock.AnyOpen
        && !(CartographyViewer.IsViewerOpen
             && CartographyViewer.CurrentMode == MapViewerMode.FieldReadOnly))
        return false;

    return true;
}
```

- **No `GetHoverObject()` guard** — this is the key difference from `CanOpenOnUse` and the
  reason M is collision-free where E wasn't: M is not the interact key, so standing at a Table
  and pressing M opens your bound map without competing with the Table's E-interaction.
  **AT-MKEY-TABLE-COEXIST** (regression): E at a Surveyor's Table still surveys/imprints; M
  there opens the map — they no longer share a key, so the old "Table's Use wins" tension is
  gone by construction.
- **`AnyOpen` exclusion for our own viewer** keeps the close path live (§4.2).

---

## 5. The HUD prompt — `$KEY_Use` → `$KEY_Map`

`UpdateEquippedPrompt` / `EnsurePromptCanvas` (`:274-323`) builds the prompt from
`"[<$KEY_Use>] $piece_readmap"`. Swap the key token to **`$KEY_Map`**:

```csharp
// was:  string raw = "[<color=yellow><b>$KEY_Use</b></color>] $piece_readmap";
string raw = "[<color=yellow><b>$KEY_Map</b></color>] $piece_readmap";
```

**Why `$KEY_Map` is safe (the sign-bug literal-leak lesson — verified, NOT a leak):**
- `$KEY_<button>` is vanilla's generic bound-key token family; the resolver routes
  `KEY_<name>` → `ZInput.GetBoundKeyString("<name>")`, which returns the player's actual bound
  key for any **registered** button (`assembly_utils :9876`). `"Map"` is a registered ZInput
  button (vanilla reads `GetButtonDown("Map")` at `:47085`), so `$KEY_Map` resolves to the
  player's real Map key (e.g. "M") and **stays rebind-correct.**
- Vanilla itself emits the generic family in its own UI: `$KEY_Block`, `$KEY_Jump`,
  `$KEY_BuildMenu`, `$KEY_AltPlace` (decomp `:43340`, `:43370`, `:43453`). `$KEY_Map` is the
  same class as the `$KEY_Use` the prompt already uses successfully — **not** a custom `$piece_*`
  token (those leak as literals; this is a vanilla key token).
- `$piece_readmap` stays (vanilla string "Read map", decomp MapTable `:114046`). Only the key
  token changes.
- **AT-MKEY-PROMPT:** the equipped HUD prompt reads "[M] Read map" (or the player's rebound Map
  key), not "[E] …", and updates if the player rebinds Map.

> Note: the equipped prompt still only shows while a map is **equipped** + the field viewer is
> closed (`:193-197`). The design's "M opens while bound-but-unequipped" (§4.2) does NOT add a
> carried-but-unequipped prompt — that's an intentional scope line (no nag prompt for a map
> you've put away; you still know M opens it). If Daniel wants a carried prompt too, that's a
> follow-up, flagged in §9.

---

## 6. Rewrite the superseded §2G comment block (`:101-121`)

The controller's `:101-121` comment block documents the Use-key rationale — the "🔴 §2G LOCKED
OPEN INPUT — the Use key, NOT the 'Map' button" paragraph and the "Use key never drives
vanilla's Minimap toggle, so our open is collision-free" premise. That whole premise is now
**inverted** by the M-key model. Replace it with a comment that documents the M-ownership model:

- M is the single map key; SBPR owns its edge via the `Minimap.Update` consume-prefix
  (`MinimapMKeyOwnerPatch`, §3).
- The prefix consumes `Map`/`JoyMap` via `ResetButtonStatus` so vanilla's `:47085` read sees
  nothing → no double-stack in nomap-OFF.
- Open routes through `HandleMapKeyPressed()` (§4.2); the context gate is
  `MKeyContextAllowsMapToggle()` (§4.3); bound-not-just-equipped semantics via `_provider`.

**Do not leave the old comment contradicting the spec** — a stale comment that says "we open on
Use because M stacks" directly next to code that opens on M is exactly the spec→impl drift this
card exists to close. The comment is part of the deliverable (AGENTS.md: code and spec change
together).

---

## 7. Acceptance tests (named, observable — logs-green ≠ playable; Daniel's joined-client check is the real accept)

| ID | Setup | Expected |
|---|---|---|
| **AT-MKEY-OPEN-EQUIP** | Bound local map, **equipped**, nothing else open. Press **M**. | The local-map field viewer opens (its 1000 m disc). |
| **AT-MKEY-OPEN-CARRIED** | Equip map A, then **unequip** (still in inventory). Press **M**. | The viewer opens for A — bound-survives-unequip (design `:43`). |
| **AT-MKEY-NO-E** | Bound map equipped, standing in the open (no hover object). Press **E** (Use). | Map does **not** open. (E still interacts with doors/chests/Tables normally.) |
| **AT-MKEY-NO-STACK** | **nomap=OFF** world. Bound map. Press **M**. | ONLY the SBPR viewer opens — vanilla's full/large map does **NOT** also open. No double-stack. |
| **AT-MKEY-DEFAULT-DEAD** | **No** local map bound (none ever equipped / all dropped). Press **M**, nomap=OFF. | Nothing opens — neither our viewer nor vanilla's map. M is inert (design `:42`); SBPR consumed the edge. |
| **AT-MKEY-DEFAULT-DEAD-NOMAPON** | No map bound, nomap=ON. Press **M**. | Nothing opens (already dead under nomap; still inert). |
| **AT-MKEY-PROMPT** | Equip a bound map (field viewer closed). | The HUD prompt reads **"[M] …"** (or the player's rebound Map key), not "[E] …". Rebinding Map updates it. |
| **AT-MKEY-CLOSE** | SBPR field viewer open. Press **M** (and, separately, **Esc**). | Either toggles the viewer shut. (Esc keeps its §2F no-menu-leak behavior.) |
| **AT-MKEY-TABLE-COEXIST** (regression) | At a Surveyor's Table with a bound map. Press **E**, then **M**. | **E** surveys/imprints at the Table (unchanged); **M** opens the bound map. They no longer collide. |
| **AT-MKEY-MODAL-SWALLOW** (regression) | A text field / sign panel / name-pin input open. Type/press **M**. | M does **not** trip a map open; the text field receives "m" / the panel keeps focus. |
| **AT-MKEY-TABLEVIEW-COEXIST** | Surveyor's Table viewer (TableEdit) open. Press **M**. | M does not fight the Table view (HandleMapKeyPressed early-returns on TableEdit). |

> **Build gate (AGENTS.md):** `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c
> Release` → **0 errors, 0 warnings.** `<TreatWarningsAsErrors>` is ON — the dead `CanOpenOnUse`
> (§4.1) MUST be removed or repurposed, or the build fails on the unused-member warning.
> `PatchCheck` must show the new `MinimapMKeyOwnerPatch` woven (register it in `Plugin.Awake()`
> alongside the other cartography patches).

---

## 8. Cross-doc edits this change carries (spec-first — same PR as the impl)

Per AGENTS.md ("spec and code change together"), the **implementation PR** that builds §3-§6
also lands these doc edits so no spec contradicts the shipped behavior. (The spec-pass PR that
introduces THIS doc lands the supersede *banners* below; the impl PR flips any remaining
present-tense "opens on E" prose.)

1. **`cartography-impl-spec.md` §2F + §2G** — add a supersede banner at the top of each: the
   open input is now **M**, not the Use key (E); point to THIS doc. §2F's *menu-suppression*
   and *exit-prompt* work STANDS (Esc still closes cleanly, no menu leak) — only the *open*
   trigger moves E→M. Do not delete §2F/§2G (they're the history); banner them.
2. **`requirements.md` AT-MAP-EQUIP** (`:275-278`) — amend "press **Use (E)**" → "press **M**";
   update the issue-7 parenthetical to point here (issue 3 supersedes the E correction).
3. **`map-provider-model.md` §1** — already DECIDED; no change. Optionally cross-link this impl
   spec from §1 as the realized HOW (nice-to-have, not required).
4. **`docs/v2/planning/index.md` + `README.md`** — add this doc (two-file rule; §10 below).

---

## 9. Open questions for Daniel (route at review — do NOT stamp)

1. **Carried-but-unequipped prompt?** §5 keeps the HUD prompt equipped-only. M still *opens* a
   carried-unequipped bound map (§4.2), but there's no on-screen "[M]" reminder when it's put
   away. Lean: leave it (no nag); add only if Daniel wants discoverability for the put-away case.
2. **M as a hard global suppress vs context-gated?** §3.2 detail 2 gates the consume on
   `MKeyContextAllowsMapToggle` (mirrors vanilla's `:47074`), so M is NOT consumed while typing.
   Confirm that's the intent: the alternative (consume M *always*, even in text fields) would
   make M never type "m" anywhere — almost certainly wrong, but flagging since "SBPR owns M"
   could be read maximally. Lean: context-gated (what's specced).
3. **Eye-of-Odin forward-compat.** The default-row consume (M inert when no map bound) is what
   a future Eye-of-Odin will replace with "open global map." This spec leaves a clean seam
   (`HandleMapKeyPressed`'s else-branch). No action now; noted so the future card knows where to
   hook.

---

## 10. Docs placement (sbpr-docs-conventions)

- This file: `docs/v2/planning/local-map-mkey-open-impl-spec.md` (version-scoped buildable
  spec, same shelf as the sibling cartography specs).
- **Two-file rule:** add a row to `docs/v2/planning/index.md` (manifest) and a bullet to
  `docs/v2/planning/README.md` (narrative). Both updated in this PR.
- SpecCheck: **+0** (§0).

