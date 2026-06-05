# Client-usability registration fix — implementation notes

Implements the ranked FIX RECOMMENDATION from the T1 gap analysis
(`sbpr-registration-gap.md` §5, companion `jotunn-registration-approach.md`).
Branch `wt/fix-client-registration`; task `t_0387ebb3`.

**Problem recap:** SBPR.Trailborne registers items/recipes/pieces into the
client's own ObjectDB/ZNetScene correctly (that's why the dedicated server's
`SpecCheck` logs `✓ All 15 recipes match`), but a player who joins Niflheim
couldn't actually craft/build the content. The gap is a missing **client-facing
refresh layer**: the player's known-recipe set and the build menu's `PieceTable`
runtime arrays are computed around player spawn / build-mode and were never
re-synced after our registration. "Logs green ≠ playable."

**Clean-room:** T1 cited Jotunn (MIT) only to characterize *vanilla* behavior;
this implementation references vanilla public API names only. No Jotunn (or any
third-party mod-loader) code is copied. The vanilla method/field names used
(`Player.OnSpawned`, `Player.UpdateKnownRecipesList`, `PieceTable.UpdateAvailable`,
`PieceTable.m_availablePieces` / `m_selectedPiece` / `m_lastSelectedPiece`,
`Piece.PieceCategory.Max`) were confirmed against the game assembly's public
metadata surface (`assembly_valheim.dll`), not decompiled IronGate source.

**No save/wire-contract change:** zero string literals (prefab / ZDO / config
names), recipe numbers, comfort values, categories, or station names were
touched. The diff is pure-additive (no existing line removed). SpecCheck is
unmodified and still validates the final recipe set on every boot.

---

## What was hooked, and why

### Fix A — `Player.OnSpawned` postfix → `UpdateKnownRecipesList()`  (root cause #1)

File: `src/SBPR.Trailborne/Runtime/ClientRefreshPatches.cs`
(`Player_OnSpawned_Postfix`).

- A Harmony **postfix** on `Player.OnSpawned(bool)` calls the local player's
  **private** `UpdateKnownRecipesList()` (resolved once via
  `AccessTools.Method(typeof(Player), "UpdateKnownRecipesList")` and invoked
  reflectively, because it is not public).
- Vanilla recomputes a player's known/craftable recipe set at spawn; recipes
  injected into `ObjectDB.m_recipes` after that point stay invisible in the
  crafting panel / hammer menu until the set is re-run. This call is the bridge
  from "recipe is in the DB" → "the player can click it."
- **Guarded** on `Registrar.ContentWired` (a new flag flipped true at the end of
  the single real in-world `DoObjectDBWiring` pass, after `SpecCheck.Run()`), so
  the refresh only fires once our content is actually present. Wrapped in
  try/catch; a missing/renamed vanilla method logs a warning instead of throwing.
- **Priority `Last`** so it runs after any peer mod's own `OnSpawned` postfix.
- **Dedicated-server-safe:** the server has no local `Player`, so this hook is a
  client-only path by construction and never runs server-side.

### Fix B — `PieceTable.UpdateAvailable` prefix + postfix array repair  (root cause #2)

File: same, `PieceTable_UpdateAvailable_Prefix` / `_Postfix`.

- The build menu reads a per-category jagged list `m_availablePieces`
  (`List<List<Piece>>`, **private** → reached via a cached
  `AccessTools.FieldRefAccess`) plus two `Vector2Int[]` selection arrays
  `m_selectedPiece` / `m_lastSelectedPiece` (public). A table whose selection
  arrays are shorter than `m_availablePieces.Count` throws or renders empty when
  the player tabs categories.
- The highest-risk surface is the **from-scratch spade-only `PieceTable`** built
  in `Trailblazing.DoObjectDBWiring` (it sets only `m_pieces` / `m_categories` /
  `m_categoryLabels` / `m_canRemovePieces`, leaving the runtime arrays
  empty/zero-length). The vanilla hammer table is generally pre-sized, but this
  hook repairs it defensively too — one shared patch covers every `PieceTable`.
- **Prefix** grows an already-initialized `m_availablePieces` to
  `Piece.PieceCategory.Max` buckets (the real-category count; the `All`
  pseudo-category at 100 is not a storage bucket). It deliberately **leaves the
  empty-list case to vanilla**, whose own body initializes the buckets — diverting
  that init branch would be riskier than letting it run.
- **Postfix** resizes `m_selectedPiece` / `m_lastSelectedPiece` to the final
  `m_availablePieces.Count` (via `Array.Resize`, which preserves existing entries
  and zero-pads the rest). This is the load-bearing repair for the fresh spade
  table: after vanilla fills the buckets, the selection arrays are sized to match.
- Both halves are wrapped in try/catch and only ever **grow**, never shrink, so a
  correctly-sized vanilla table is a no-op (we never fight vanilla).

### Fix C — explicit Harmony priorities on the registration patches  (aggravating #3)

File: `src/SBPR.Trailborne/Runtime/Registrar.cs`.

- The three registration postfixes (`ZNetScene.Awake`, `ObjectDB.CopyOtherDB`,
  `ObjectDB.Awake`) were bare `[HarmonyPostfix]` with undefined ordering relative
  to vanilla and to peer mods. Each now carries **`[HarmonyPriority(Priority.Last)]`**
  so SBPR's additions + `SpecCheck` observe a fully-settled DB at the end of the
  postfix chain, regardless of modpack load order.
- Priority only orders patches **on the same target method**, so the existing
  cross-method 3-hook race-safety self-heal (whichever of the three fires last,
  once `ObjectDB`+`ZNetScene`+`znetSceneDone` all hold, does the single real
  wiring pass) is **unchanged** — this fix adds ordering, it does not rip out the
  race-safety net.

### Fix D (defensive index rebuild) — NOT implemented

The gap doc lists this as optional hardening ("do only if A–C don't fully
resolve"). Deferred: A+B address the confirmed root causes and D would touch the
item-registration path (`Assets.RegisterItemInObjectDB`) for a low-confidence
edge. If QA still sees missing items after A–C, revisit D as a follow-up.

---

## Wiring

`Plugin.Awake` now calls `harmony.PatchAll(typeof(ClientRefreshPatches))`
alongside the existing `Registrar` / `CairnPatches` registrations.

## Build

`dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` →
**0 errors, 29 warnings** (all pre-existing nullable CS86xx, identical count to
the pre-change baseline; the new file adds none).

## What to verify (handed to the QA card)

1. Join Niflheim as a client; confirm the Explorer's Bench lists the inks /
   spade / cairn markers and they craft.
2. Equip the spade; confirm the path/replant ops appear in its build menu and
   place (this is the from-scratch `PieceTable` that Fix B targets).
3. Confirm the hammer build menu shows bench / lamp / signs / cairns.
4. Boot log: `Player.OnSpawned → UpdateKnownRecipesList()` info line fires after
   spawn; no SBPR-tagged exceptions from `ClientRefreshPatches`.
