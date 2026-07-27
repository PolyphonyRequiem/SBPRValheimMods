// ============================================================================
//  QA-M4-BIND — live net48 IObservationAdapter + IPeerBindingAdapter
//  (ADR-0009 §4/§6, PR408 §3.1/§3.4/§3.8/§3.10) — engine-bound M4 binding slice.
// ----------------------------------------------------------------------------
//  GameObservationAdapter implements the engine-free IObservationAdapter against
//  the LIVE vanilla read seams the PR #408 map pins. Every game read runs on the
//  helper's own main-thread tick behind the single-slot invoker (tooltip's static
//  StringBuilder is main-thread-only, PR408 §3.8) and emits ONLY raw observed
//  facts (prefab, quality, present custom-data KEY names, product-rendered tooltip
//  text). No reflection into any verdict cache (threat T4); no product-state claim;
//  no PASS/FAIL — verdict composition is the external runner's sole authority.
//
//  GameServerPeerBinding implements IPeerBindingAdapter against the server-channel
//  delivering-peer seam (PR408 §3.4): it binds the ACTUAL delivering peer from the
//  inbound ZRpc via ZNet.GetPeer(rpc) (private → AccessTools) and IGNORES any
//  identity the envelope claims — a mismatch rejects (peer substitution).
//
//  Clean-room (ADR-0001): reads/reflects the base game only; no other-mod source;
//  no decompiled body; no publicized game DLL. MATURITY: compiles against the live
//  assembly members; NO in-world execution is performed or claimed here (M6).
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.Evidence;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Live either-role observation adapter (PR408 §3.8/§3.10/§3.1). Client-role reads (inventory/item/
    /// tooltip) require a spawned local player; world reads work on either role. Every read runs behind
    /// the single-slot main-thread invoker; a Busy/Timeout/absent-world read yields a mechanical
    /// rejection receipt, never a fabricated observation.
    /// </summary>
    internal sealed class GameObservationAdapter : IObservationAdapter
    {
        private readonly IMainThreadInvoker _invoker;
        private readonly IAdapterRequestContextSource _ctx;
        private readonly long _timeoutMs;

        // Verb name constants match the static VerbCatalog (Core.VerbCatalog) so a receipt's Verb is the
        // exact catalog token the runner correlates.
        private const string VReadInventory = "ReadInventory";
        private const string VReadItem = "ReadItem";
        private const string VReadTooltip = "ReadTooltip";
        private const string VReadWorldName = "ReadWorldName";
        private const string VReadWorldUid = "ReadWorldUid";

        public GameObservationAdapter(IMainThreadInvoker invoker, IAdapterRequestContextSource ctx, long timeoutMs = 5000)
        {
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _timeoutMs = timeoutMs > 0 ? timeoutMs : 5000;
        }

        // TODO(PR408 §3.10): Player.GetInventory() (Humanoid @833) -> enumerate slots via Inventory.GetItem.
        public RedactedReceipt ReadInventory()
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VReadInventory, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                Inventory? inv = LocalInventory();
                if (inv == null) { result = GameItemEvidence.Reject(ctx, VReadInventory, ReceiptOutcome.Rejected, EvidenceReason.None); return; }
                var items = inv.GetAllItems();
                var slotFacts = new List<string>();
                int count = items != null ? items.Count : 0;
                for (int i = 0; i < count; i++)
                    slotFacts.Add(GameItemEvidence.PrefabName(items![i]) + "@q" + items[i].m_quality.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var facts = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["item_count"] = count,
                    ["items"] = slotFacts.ToArray(),
                };
                result = GameItemEvidence.FactReceipt(ctx, VReadInventory, ReceiptOutcome.Ok, facts);
            });
            return ShedIfNotRan(outcome, ctx, VReadInventory, result);
        }

        // TODO(PR408 §3.10): Inventory.GetItem(index) -> emit prefab, quality, present m_customData KEY names.
        public RedactedReceipt ReadItem(int itemSlot)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VReadItem, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                ItemDrop.ItemData? item = ItemAtSlot(itemSlot);
                if (item == null) { result = GameItemEvidence.Reject(ctx, VReadItem, ReceiptOutcome.Rejected, EvidenceReason.None); return; }
                result = GameItemEvidence.ObservedReceipt(ctx, VReadItem, ReceiptOutcome.Ok, item);
            });
            return ShedIfNotRan(outcome, ctx, VReadItem, result);
        }

        // TODO(PR408 §3.8): ItemDrop.ItemData.GetTooltip(stackOverride=-1) (@622) — pure string builder over a
        // STATIC m_stringBuilder. MAIN THREAD ONLY (dispatcher tick). Record the returned string verbatim as a
        // FactSource.Direct fact; the runner labels it Direct because it is read off the item's own tooltip.
        public RedactedReceipt ReadTooltip(int itemSlot)
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VReadTooltip, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                ItemDrop.ItemData? item = ItemAtSlot(itemSlot);
                if (item == null) { result = GameItemEvidence.Reject(ctx, VReadTooltip, ReceiptOutcome.Rejected, EvidenceReason.None); return; }
                string tooltip = item.GetTooltip(-1) ?? string.Empty;
                // A visible-Workmanship fact is Direct only when read off the item's own tooltip (§4/§8).
                var extra = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["tooltip_source"] = FactSource.Direct.ToString(),
                };
                result = GameItemEvidence.ObservedReceipt(ctx, VReadTooltip, ReceiptOutcome.Ok, item, tooltipText: tooltip, extraFacts: extra);
            });
            return ShedIfNotRan(outcome, ctx, VReadTooltip, result);
        }

        // TODO(PR408 §3.1): ZNet.GetWorldName() (client @1798) — null-guarded via ZNet.instance/World.
        public RedactedReceipt ReadWorldName()
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VReadWorldName, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                if (ZNet.instance == null || ZNet.World == null)
                { result = GameItemEvidence.Reject(ctx, VReadWorldName, ReceiptOutcome.Rejected, EvidenceReason.None); return; }
                var facts = new Dictionary<string, object?>(StringComparer.Ordinal) { ["world_name"] = ZNet.instance.GetWorldName() ?? string.Empty };
                result = GameItemEvidence.FactReceipt(ctx, VReadWorldName, ReceiptOutcome.Ok, facts);
            });
            return ShedIfNotRan(outcome, ctx, VReadWorldName, result);
        }

        // TODO(PR408 §3.1): ZNet.GetWorldUID() (client @1792) — NOT ZNet.GetUID() (session id); adapter MUST
        // null-check ZNet.instance/World before reading (GetWorldUID is not itself null-guarded, PR408 §3.1).
        public RedactedReceipt ReadWorldUid()
        {
            AdapterRequestContext ctx = _ctx.Current;
            RedactedReceipt result = GameItemEvidence.Reject(ctx, VReadWorldUid, ReceiptOutcome.Timeout, EvidenceReason.None);
            var outcome = _invoker.Run(_timeoutMs, () =>
            {
                if (ZNet.instance == null || ZNet.World == null)
                { result = GameItemEvidence.Reject(ctx, VReadWorldUid, ReceiptOutcome.Rejected, EvidenceReason.None); return; }
                var facts = new Dictionary<string, object?>(StringComparer.Ordinal) { ["world_uid"] = ZNet.instance.GetWorldUID() };
                result = GameItemEvidence.FactReceipt(ctx, VReadWorldUid, ReceiptOutcome.Ok, facts);
            });
            return ShedIfNotRan(outcome, ctx, VReadWorldUid, result);
        }

        // ── shared read helpers (main-thread only) ──────────────────────────

        private static Inventory? LocalInventory()
        {
            Player? p = Player.m_localPlayer;
            return p != null ? p.GetInventory() : null;
        }

        private static ItemDrop.ItemData? ItemAtSlot(int index)
        {
            if (index < 0) return null;
            Inventory? inv = LocalInventory();
            if (inv == null) return null;
            return inv.GetItem(index);
        }

        // Map a shed (Busy/Timeout/Rejected) invoker outcome to a mechanical rejection receipt; on Ran,
        // return the receipt the primitive built.
        private RedactedReceipt ShedIfNotRan(DispatchOutcome outcome, AdapterRequestContext ctx, string verb, RedactedReceipt ran)
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

    /// <summary>
    /// Live server-channel delivering-peer binding (PR408 §3.4, ADR-0009 §5.1). Binds the ACTUAL peer
    /// from the inbound ZRpc and ignores any identity the envelope claims; a mismatch (or an unready
    /// peer) rejects. The inbound rpc for the in-flight call is supplied by the server RPC bridge.
    /// </summary>
    internal sealed class GameServerPeerBinding : IPeerBindingAdapter
    {
        // ZNet.GetPeer(ZRpc) is PRIVATE (verified in assembly_valheim metadata) — resolve reflectively
        // (base game, clean-room permitted). Cached MethodInfo so the bind path allocates no Traverse per call.
        private static readonly MethodInfo? GetPeerByRpc =
            AccessTools.Method(typeof(ZNet), "GetPeer", new[] { typeof(ZRpc) });

        private readonly Func<ZRpc?> _inboundRpc;

        public GameServerPeerBinding(Func<ZRpc?> inboundRpc)
        {
            _inboundRpc = inboundRpc ?? throw new ArgumentNullException(nameof(inboundRpc));
        }

        // TODO(PR408 §3.4): map inbound ZRpc rpc -> peer via ZNet.GetPeer(rpc); wait for ZNetPeer.IsReady()
        // (m_uid != 0). Return the delivering peer uid, or null to reject. A claimed-vs-delivering mismatch
        // is a substitution the caller rejects (the envelope's claimed identity is IGNORED here).
        public long? BindDeliveringPeer(RequestEnvelope envelope)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return null;
            ZRpc? rpc = _inboundRpc();
            if (rpc == null || GetPeerByRpc == null) return null;
            ZNetPeer? peer;
            try { peer = GetPeerByRpc.Invoke(ZNet.instance, new object[] { rpc }) as ZNetPeer; }
            catch (TargetInvocationException) { return null; }
            catch (Exception) { return null; }
            if (peer == null || !peer.IsReady() || peer.m_uid == 0L) return null;
            return peer.m_uid;
        }
    }
}
