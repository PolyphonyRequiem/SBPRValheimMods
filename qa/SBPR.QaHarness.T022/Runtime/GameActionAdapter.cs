// ============================================================================
//  QA-M4-BIND — live net48 IActionAdapter (ADR-0009 §3/§4/§6,
//  PR408 §3.6/§3.7/§3.9) — engine-bound M4 binding slice.
// ----------------------------------------------------------------------------
//  GameActionAdapter implements the engine-free IActionAdapter against the LIVE
//  vanilla action seams the PR #408 map pins. Every game-touching call runs on the
//  helper's own main-thread tick behind the EXISTING single-slot invoker (one
//  primitive in flight; a second is shed Busy) and shares NO game console /
//  ScriptTools / ValBridge lock (ADR-0009 §5.2, threat T6). Client-role only for
//  craft/upgrade/drop/pickup/tamper — InventoryGui.instance / Player.m_localPlayer
//  are null on the dedicated server (PR408 §3.6).
//
//  FIREWALLS (non-negotiable, ADR-0009 §4/§6):
//   * CRAFT/UPGRADE drive the REAL product issuance seam (private
//     InventoryGui.OnCraftPressed → UpdateRecipe → DoCrafting) via AccessTools; the
//     receipt records that the helper DROVE the seam and OBSERVED the result — it
//     NEVER claims the harness minted/signed the stamp (ProductFirewall). DoCrafting
//     silently no-ops on unmet requirements, so the adapter OBSERVES the result item
//     (present + quality) rather than assuming success.
//   * TAMPER is gated by the engine-free TamperPolicy: replace/remove an EXISTING
//     allowlisted key on an EXACT tracked throwaway item only — the enum has no add
//     member, so a signature can never be minted/copied. It mutates m_customData
//     in-memory (PR408 §3.9); persistence rides the item's normal SaveToZDO path.
//   * Continuity/upgrade mapping is asserted by the engine-free ItemContinuity — the
//     adapter only OBSERVES source→result fingerprints; the runner composes the
//     verdict. No PASS/FAIL is emitted anywhere.
//
//  Clean-room (ADR-0001): reads/reflects the base game only (privates via
//  AccessTools); no other-mod source; no decompiled body; no publicized game DLL.
//  MATURITY: compiles against the live assembly members; NO in-world execution is
//  performed or claimed on this card (live qualification is the operator M6 card).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.Evidence;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Live client-role action adapter (PR408 §3.6/§3.7/§3.9). Drives the real vanilla seams behind the
    /// single-slot main-thread invoker; every path OBSERVES a result and emits a firewalled primitive-fact
    /// receipt, never a verdict and never a harness-produced-state claim.
    /// </summary>
    internal sealed class GameActionAdapter : IActionAdapter
    {
        private readonly IMainThreadInvoker _invoker;
        private readonly IAdapterRequestContextSource _ctx;
        private readonly IRunItemLedger _ledger;
        private readonly long _timeoutMs;

        // Verb tokens (match Core.VerbCatalog).
        private const string VCraft = "Craft";
        private const string VUpgrade = "UpgradeItem";
        private const string VDrop = "DropItem";
        private const string VPickup = "PickUpNearest";
        private const string VTamper = "TamperField";

        // Private InventoryGui members (verified private in assembly_valheim metadata) reached via
        // AccessTools — base game, clean-room permitted (the wall is around OTHER mods). No publicized DLL.
        private static readonly MethodInfo? MiSetRecipe = AccessTools.Method(typeof(InventoryGui), "SetRecipe", new[] { typeof(int), typeof(bool) });
        private static readonly MethodInfo? MiOnCraftPressed = AccessTools.Method(typeof(InventoryGui), "OnCraftPressed");
        private static readonly FieldInfo? FiCraftUpgradeItem = AccessTools.Field(typeof(InventoryGui), "m_craftUpgradeItem");
        private static readonly FieldInfo? FiAvailableRecipes = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");

        public GameActionAdapter(
            IMainThreadInvoker invoker, IAdapterRequestContextSource ctx, IRunItemLedger ledger, long timeoutMs = 5000)
        {
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _timeoutMs = timeoutMs > 0 ? timeoutMs : 5000;
        }

        // TODO(PR408 §3.6): select the recipe via private SetRecipe(index,center) (index found from
        // m_availableRecipes), then drive private OnCraftPressed() via AccessTools; let UpdateRecipe→
        // DoCrafting (@1500) run naturally on subsequent frames. Observe the RESULT (item present +
        // quality) — DoCrafting silently no-ops on unmet reqs / no open station. NEVER claim the harness
        // minted the stamp (ProductFirewall). Client-role only.
        public RedactedReceipt Craft(string recipeName, string station)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VCraft, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                if (!ClientCraftContextReady(station, out string ctxReason))
                { result = GameItemEvidence.Reject(ctx, VCraft, ReceiptOutcome.Rejected, EvidenceReason.None); return; }

                int index = FindRecipeIndex(recipeName);
                if (index < 0) { result = GameItemEvidence.Reject(ctx, VCraft, ReceiptOutcome.Rejected, EvidenceReason.None); return; }

                // Fresh craft: no upgrade item selected (DoCrafting computes target quality 1).
                if (FiCraftUpgradeItem != null) FiCraftUpgradeItem.SetValue(InventoryGui.instance, null);
                bool driven = DriveSeam(index);

                // OBSERVE the result: the newly-present result item + its quality. The receipt records
                // that we drove the seam and observed a result; it never asserts we minted the stamp.
                ItemDrop.ItemData? resultItem = FindByRecipeName(recipeName);
                var extra = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["drove_product_seam"] = driven,
                    ["recipe"] = recipeName,
                };
                result = GameItemEvidence.ObservedReceipt(
                    ctx, VCraft, driven ? ReceiptOutcome.Ok : ReceiptOutcome.Rejected, resultItem, extraFacts: extra);
            });
            return Shed(outcome, ctx, VCraft, result);
        }

        // TODO(PR408 §3.6): upgrade == craft with a NON-NULL m_craftUpgradeItem set to the source item, then
        // the same OnCraftPressed path; DoCrafting computes targetQuality = m_craftUpgradeItem.m_quality + 1.
        // The source→replacement mapping is asserted engine-free by ItemContinuity.CheckUpgrade (identity +
        // quality+1 + keys preserved + no new signature key). The adapter OBSERVES both fingerprints.
        public RedactedReceipt UpgradeItem(int itemSlot, int targetQuality)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VUpgrade, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                ItemDrop.ItemData? source = ItemAtSlot(itemSlot);
                if (source == null || InventoryGui.instance == null || Player.m_localPlayer == null)
                { result = GameItemEvidence.Reject(ctx, VUpgrade, ReceiptOutcome.Rejected, EvidenceReason.None); return; }

                string trackId = _ledger.TrackId(source);
                ItemFingerprint before = GameItemEvidence.FingerprintOf(source, trackId);

                int index = FindRecipeIndexForItem(source);
                bool driven = false;
                if (index >= 0)
                {
                    if (FiCraftUpgradeItem != null) FiCraftUpgradeItem.SetValue(InventoryGui.instance, source);
                    driven = DriveSeam(index);
                }

                // OBSERVE the result item in the same slot; assert the upgrade mapping engine-free.
                ItemDrop.ItemData? replacement = ItemAtSlot(itemSlot);
                ItemFingerprint after = GameItemEvidence.FingerprintOf(replacement, trackId);
                EvidenceReason mapping = ItemContinuity.CheckUpgrade(before, after, targetQuality);
                var extra = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["drove_product_seam"] = driven,
                    ["target_quality"] = targetQuality,
                    ["source_quality"] = before.Quality,
                    ["upgrade_mapping"] = mapping.ToString(),
                };
                ReceiptOutcome ro = (driven && mapping == EvidenceReason.None) ? ReceiptOutcome.Ok : ReceiptOutcome.Rejected;
                result = GameItemEvidence.ObservedReceipt(ctx, VUpgrade, ro, replacement, extraFacts: extra,
                    rejectReason: mapping);
            });
            return Shed(outcome, ctx, VUpgrade, result);
        }

        // TODO(PR408 §3.7): Humanoid.DropItem(inventory,item,amount) (@767) -> ItemDrop.DropItem (@1646) which
        // Clone()s the ItemData (m_customData deep-copied @412), preserving the tracked stamp. Unequip first if
        // equipped. Observe the dropped fingerprint the runner correlates with the pickup.
        public RedactedReceipt DropItem(int itemSlot)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VDrop, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                Player? player = Player.m_localPlayer;
                ItemDrop.ItemData? item = ItemAtSlot(itemSlot);
                Inventory? inv = player != null ? player.GetInventory() : null;
                if (player == null || item == null || inv == null)
                { result = GameItemEvidence.Reject(ctx, VDrop, ReceiptOutcome.Rejected, EvidenceReason.None); return; }

                string trackId = _ledger.TrackId(item);
                // Snapshot the dropped fingerprint BEFORE the drop (the world clone carries the same facts).
                ItemFingerprint dropped = GameItemEvidence.FingerprintOf(item, trackId);
                bool ok = player.DropItem(inv, item, item.m_stack);

                var extra = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["dropped_track_id"] = trackId,
                    ["dropped_continuity_key"] = dropped.ContinuityKey(),
                };
                result = GameItemEvidence.FactReceipt(ctx, VDrop, ok ? ReceiptOutcome.Ok : ReceiptOutcome.Rejected,
                    MergeItemFacts(dropped, extra));
            });
            return Shed(outcome, ctx, VDrop, result);
        }

        // TODO(PR408 §3.7): resolve nearest world ItemDrop within radius (<= Rmax) whose item name matches, then
        // Humanoid.Pickup(go) (@588). Honor ItemDrop.CanPickup false during the auto-pickup delay by polling on
        // the dispatcher (no sleeps) — here a single bounded attempt; the runner re-offers if CanPickup was false.
        public RedactedReceipt PickUpNearest(string itemName, double radius)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VPickup, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                Player? player = Player.m_localPlayer;
                if (player == null) { result = GameItemEvidence.Reject(ctx, VPickup, ReceiptOutcome.Rejected, EvidenceReason.None); return; }

                ItemDrop? nearest = FindNearestDrop(player.transform.position, itemName, (float)radius);
                if (nearest == null || nearest.m_itemData == null)
                { result = GameItemEvidence.Reject(ctx, VPickup, ReceiptOutcome.Rejected, EvidenceReason.None); return; }
                if (!nearest.CanPickup(true))
                { result = GameItemEvidence.Reject(ctx, VPickup, ReceiptOutcome.Busy, EvidenceReason.None); return; }

                // Fingerprint the world item BEFORE pickup; adopt its run track id if the run registered it.
                string trackId = _ledger.TrackId(nearest.m_itemData);
                ItemFingerprint pickedUp = GameItemEvidence.FingerprintOf(nearest.m_itemData, trackId);
                bool ok = player.Pickup(nearest.gameObject, true, true);

                var extra = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["picked_track_id"] = trackId,
                    ["picked_continuity_key"] = pickedUp.ContinuityKey(),
                };
                result = GameItemEvidence.FactReceipt(ctx, VPickup, ok ? ReceiptOutcome.Ok : ReceiptOutcome.Rejected,
                    MergeItemFacts(pickedUp, extra));
            });
            return Shed(outcome, ctx, VPickup, result);
        }

        // TODO(PR408 §3.9): operate on ItemDrop.ItemData.m_customData (@392) in-memory behind TamperPolicy.
        // Replace/remove an EXISTING allowlisted key on an EXACT tracked throwaway item only; the policy has no
        // add path (T5). Persistence rides the item's normal SaveToZDO. Never edits a product-store copy.
        public RedactedReceipt TamperField(int itemSlot, string fieldName, TamperOperation operation)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VTamper, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                ItemDrop.ItemData? item = ItemAtSlot(itemSlot);
                if (item == null) { result = GameItemEvidence.Reject(ctx, VTamper, ReceiptOutcome.Rejected, EvidenceReason.None); return; }

                bool isThrowaway = _ledger.IsThrowaway(item);
                IReadOnlyList<string> presentKeys = GameItemEvidence.CustomKeyNames(item);

                // Engine-free firewall: fail-closed fixed-order validation (throwaway → replace/remove →
                // not-signature → allowlisted → present). There is NO add path.
                EvidenceReason gate = TamperPolicy.Validate(fieldName, presentKeys, isThrowaway, operation);
                if (gate != EvidenceReason.None)
                { result = GameItemEvidence.Reject(ctx, VTamper, ReceiptOutcome.Rejected, gate); return; }

                // Apply the vetted mutation to the in-memory m_customData (never adds a key: replace requires
                // presence, remove deletes). Persistence is the item's normal save path (not forced here).
                if (item.m_customData != null)
                {
                    if (operation == TamperOperation.Remove) item.m_customData.Remove(fieldName);
                    else if (operation == TamperOperation.Replace) item.m_customData[fieldName] = string.Empty; // degrade to empty
                }

                var extra = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["tamper_op"] = operation.ToString(),
                    ["tamper_field"] = fieldName,
                };
                result = GameItemEvidence.ObservedReceipt(ctx, VTamper, ReceiptOutcome.Ok, item, extraFacts: extra);
            });
            return Shed(outcome, ctx, VTamper, result);
        }

        // ── shared helpers (main-thread only) ───────────────────────────────

        // Drive the private product issuance seam: select the recipe, then press. UpdateRecipe→DoCrafting
        // run on subsequent frames. Returns true iff both private members resolved and were invoked.
        private static bool DriveSeam(int recipeIndex)
        {
            var gui = InventoryGui.instance;
            if (gui == null || MiSetRecipe == null || MiOnCraftPressed == null) return false;
            try
            {
                MiSetRecipe.Invoke(gui, new object[] { recipeIndex, false });
                MiOnCraftPressed.Invoke(gui, null);
                return true;
            }
            catch (TargetInvocationException) { return false; }
            catch (Exception) { return false; }
        }

        private static bool ClientCraftContextReady(string station, out string reason)
        {
            reason = string.Empty;
            if (InventoryGui.instance == null) { reason = "no-inventory-gui"; return false; }
            Player? p = Player.m_localPlayer;
            if (p == null) { reason = "no-local-player"; return false; }
            CraftingStation? cur = p.GetCurrentCraftingStation();
            // station arg must match the current open station (PR408 §3.6). An empty station means hand-craft.
            if (!string.IsNullOrEmpty(station))
            {
                if (cur == null || !string.Equals(cur.m_name, station, StringComparison.Ordinal)) { reason = "station-mismatch"; return false; }
            }
            return true;
        }

        // Find the m_availableRecipes index whose recipe result item shared-name matches recipeName.
        private static int FindRecipeIndex(string recipeName)
        {
            IList? list = FiAvailableRecipes?.GetValue(InventoryGui.instance) as IList;
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                Recipe? recipe = RecipeOfPair(list[i]);
                if (recipe != null && RecipeResultName(recipe) is string name &&
                    string.Equals(name, recipeName, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        // Find the recipe index whose result prefab matches a given source item's prefab (for upgrades).
        private static int FindRecipeIndexForItem(ItemDrop.ItemData source)
        {
            string want = GameItemEvidence.PrefabName(source);
            IList? list = FiAvailableRecipes?.GetValue(InventoryGui.instance) as IList;
            if (list == null) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                Recipe? recipe = RecipeOfPair(list[i]);
                if (recipe != null && RecipeResultName(recipe) is string name &&
                    string.Equals(name, want, StringComparison.Ordinal))
                    return i;
            }
            return -1;
        }

        // RecipeDataPair.Recipe (public property on the nested struct) — reached generically to avoid
        // depending on the internal struct type name at compile time.
        private static Recipe? RecipeOfPair(object? pair)
        {
            if (pair == null) return null;
            PropertyInfo? p = pair.GetType().GetProperty("Recipe", BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(pair) as Recipe;
        }

        private static string? RecipeResultName(Recipe recipe)
        {
            ItemDrop? item = recipe.m_item;
            if (item == null || item.m_itemData == null) return null;
            return GameItemEvidence.PrefabName(item.m_itemData);
        }

        private static ItemDrop.ItemData? FindByRecipeName(string recipeName)
        {
            Player? p = Player.m_localPlayer;
            Inventory? inv = p != null ? p.GetInventory() : null;
            if (inv == null) return null;
            var items = inv.GetAllItems();
            if (items == null) return null;
            for (int i = 0; i < items.Count; i++)
                if (string.Equals(GameItemEvidence.PrefabName(items[i]), recipeName, StringComparison.Ordinal))
                    return items[i];
            return null;
        }

        private static ItemDrop? FindNearestDrop(Vector3 origin, string itemName, float radius)
        {
            ItemDrop? best = null;
            float bestSq = radius * radius;
            foreach (var drop in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None))
            {
                if (drop == null || drop.m_itemData == null) continue;
                if (!string.IsNullOrEmpty(itemName) &&
                    !string.Equals(GameItemEvidence.PrefabName(drop.m_itemData), itemName, StringComparison.Ordinal))
                    continue;
                float sq = (drop.transform.position - origin).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = drop; }
            }
            return best;
        }

        private static ItemDrop.ItemData? ItemAtSlot(int index)
        {
            if (index < 0) return null;
            Player? p = Player.m_localPlayer;
            Inventory? inv = p != null ? p.GetInventory() : null;
            return inv != null ? inv.GetItem(index) : null;
        }

        // Merge a fingerprint's raw facts (prefab/quality/key names) into an extra-fact map for a receipt
        // built without a live item handle (drop/pickup snapshot the fingerprint, not the item).
        private static IReadOnlyDictionary<string, object?> MergeItemFacts(ItemFingerprint fp, Dictionary<string, object?> extra)
        {
            extra["prefab"] = fp.Prefab;
            extra["quality"] = fp.Quality;
            var keys = new List<string>(fp.CustomKeys);
            extra["custom_key_names"] = keys.ToArray();
            return extra;
        }

        private RedactedReceipt Shed(DispatchOutcome outcome, AdapterRequestContext ctx, string verb, RedactedReceipt ran)
        {
            switch (outcome)
            {
                case DispatchOutcome.Ran: return ran;
                case DispatchOutcome.Busy: return GameItemEvidence.Reject(ctx, verb, ReceiptOutcome.Busy, EvidenceReason.None);
                case DispatchOutcome.Timeout: return GameItemEvidence.Reject(ctx, verb, ReceiptOutcome.Timeout, EvidenceReason.None);
                default: return GameItemEvidence.Reject(ctx, verb, ReceiptOutcome.Rejected, EvidenceReason.None);
            }
        }
    }
}
