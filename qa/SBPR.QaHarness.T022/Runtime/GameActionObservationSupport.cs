// ============================================================================
//  QA-M4-BIND — net48 action/observation adapter support (ADR-0009 §3/§4/§6,
//  PR408 §3.1/§3.2/§3.4/§3.6-§3.10) — the engine-bound M4 binding slice.
// ----------------------------------------------------------------------------
//  This file holds the shared plumbing the two live adapters (GameActionAdapter,
//  GameObservationAdapter) use to touch Valheim safely:
//
//    * IMainThreadInvoker / DispatcherMainThreadInvoker — routes EVERY
//      game-touching primitive through the EXISTING single-slot, timeout-bounded
//      ControlDispatcher (Core.ControlPlane), so one primitive is in flight at a
//      time and a second concurrent call is shed Busy exactly as the reviewed FSM
//      defines. It takes NO game console / ScriptTools / ValBridge lock (ADR-0009
//      §5.2, threat T6; AT-QA-NO-SCRIPTTOOLS-LOCK stays green) — it owns only a
//      reused engine-free dispatcher instance, driven on the helper's own main
//      thread.
//    * IRunItemLedger — the run-scoped authority that says whether a live item is
//      a tracked THROWAWAY and what its harness-minted correlation id (track id)
//      is. The tamper firewall and every fingerprint consult it; the adapter never
//      invents throwaway status or a track id.
//    * AdapterRequestContext / IAdapterRequestContextSource — the receipt identity
//      (requestId/nonce/seq/connectionGeneration/role/worldUid/ts) for the
//      in-flight request, supplied by the dispatcher before a primitive runs. The
//      interface method signatures (Craft(recipe,station) …) carry no envelope, so
//      the ambient context is injected here.
//    * GameItemEvidence — builds engine-free ItemFingerprint / RedactedReceipt
//      values from a LIVE ItemDrop.ItemData using ONLY raw observed facts (prefab,
//      quality, present custom-data KEY names, product-rendered tooltip). Values
//      are redacted to bounded digests by the engine-free ReceiptFirewall; this
//      helper emits primitive facts only and never a PASS/FAIL/verdict or a claim
//      that the harness produced product state (ADR-0009 §6, threat T11).
//
//  Clean-room (ADR-0001): every binding here targets a member pinned in the
//  accepted PR #408 map (qa/decomp-map/VANILLA-BINDINGS.md); no decompiled body is
//  copied. Reaching the base game's public API — and its privates via
//  Harmony/AccessTools — is permitted (the wall is around OTHER mods). No
//  publicized game DLL enters the build.
//
//  MATURITY (truthful): this slice COMPILES against the live assembly_valheim
//  members and is structurally correct, but NO in-world execution is performed or
//  claimed on this card. Live craft/drop/tamper/tooltip qualification is the
//  separate operator-authorized M6 card.
using System;
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Evidence;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>The mechanical outcome of offering a primitive to the single-slot main-thread invoker.</summary>
    internal enum DispatchOutcome
    {
        /// <summary>The primitive ran to completion in the slot.</summary>
        Ran = 0,
        /// <summary>The single slot was already occupied — the primitive was shed (retry later).</summary>
        Busy = 1,
        /// <summary>The primitive could not be admitted before its deadline.</summary>
        Timeout = 2,
        /// <summary>The offer was structurally rejected (malformed request / spent id).</summary>
        Rejected = 3,
    }

    /// <summary>
    /// Runs a game-touching action synchronously in the single execution slot on the main thread,
    /// bounded by a timeout. It shares NO game console / ScriptTools / ValBridge lock (ADR-0009 §5.2).
    /// </summary>
    internal interface IMainThreadInvoker
    {
        /// <summary>Offer <paramref name="work"/> to the slot; run it when admitted, else shed Busy/Timeout.</summary>
        DispatchOutcome Run(long timeoutMs, Action work);
    }

    /// <summary>
    /// The live invoker: routes every primitive through the EXISTING engine-free single-slot
    /// <see cref="ControlDispatcher"/> (maxQueueDepth 0 = strict single-slot). Offer→run→Complete
    /// per primitive; a second concurrent primitive is shed Busy. Called only on the helper's own
    /// main-thread tick; holds no synchronization primitive of its own (T6-safe).
    /// </summary>
    internal sealed class DispatcherMainThreadInvoker : IMainThreadInvoker
    {
        private readonly ControlDispatcher _dispatcher;
        private readonly Func<long> _nowUnixMs;
        private long _seq;

        public DispatcherMainThreadInvoker(ControlDispatcher dispatcher, Func<long> nowUnixMs)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _nowUnixMs = nowUnixMs ?? throw new ArgumentNullException(nameof(nowUnixMs));
        }

        public DispatchOutcome Run(long timeoutMs, Action work)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            long boundedTimeout = timeoutMs > 0 ? timeoutMs : 1;
            long now = _nowUnixMs();
            string id = "adapter-" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);

            var offer = _dispatcher.Offer(id, now, boundedTimeout);
            if (!offer.Accepted)
            {
                return offer.Reason == ControlPlaneReason.Busy || offer.Reason == ControlPlaneReason.QueueFull
                    ? DispatchOutcome.Busy
                    : DispatchOutcome.Rejected;
            }

            try
            {
                // Expire-before-start check: if the (single-threaded) main thread was so far behind
                // that the slot deadline already passed, treat it as a Timeout rather than starting.
                var status = _dispatcher.Poll(_nowUnixMs(), boundedTimeout);
                if (status == null || !string.Equals(status.RequestId, id, StringComparison.Ordinal))
                    return DispatchOutcome.Timeout;
                work();
                return DispatchOutcome.Ran;
            }
            finally
            {
                _dispatcher.Complete(id, _nowUnixMs(), boundedTimeout);
            }
        }
    }

    /// <summary>
    /// Run-scoped authority over which live items are tracked THROWAWAYS and their harness-minted
    /// correlation (track) ids. The tamper firewall and every fingerprint consult this — the adapter
    /// never fabricates throwaway status or a track id (ADR-0009 §4; PR408 §3.9).
    /// </summary>
    internal interface IRunItemLedger
    {
        /// <summary>True only for an item the run explicitly registered as a disposable throwaway.</summary>
        bool IsThrowaway(object itemData);

        /// <summary>The run-scoped correlation id for a tracked item, or empty when untracked. NEVER a product id.</summary>
        string TrackId(object itemData);
    }

    /// <summary>
    /// A reference-keyed in-memory <see cref="IRunItemLedger"/>. The run's fixture layer registers each
    /// throwaway item it grants with a minted correlation id; the adapters read that identity back. This
    /// keeps throwaway/track authority OUT of the adapter (which merely consults it). No product state.
    /// </summary>
    internal sealed class RunItemLedger : IRunItemLedger
    {
        private sealed class Entry { public string TrackId = string.Empty; public bool Throwaway; }

        // Reference identity: the SAME live ItemData object the run granted. A drop→pickup Clone()s the
        // ItemData, so the receiving side registers the picked-up instance under the same track id when
        // it adopts it — that registration is the run layer's job, not the adapter's.
        private readonly Dictionary<object, Entry> _byRef =
            new Dictionary<object, Entry>(ReferenceEqualityComparer.Instance);

        /// <summary>Register a live item as a tracked throwaway under a run-scoped correlation id.</summary>
        public void Register(object itemData, string trackId, bool throwaway)
        {
            if (itemData == null) return;
            _byRef[itemData] = new Entry { TrackId = trackId ?? string.Empty, Throwaway = throwaway };
        }

        public bool IsThrowaway(object itemData)
            => itemData != null && _byRef.TryGetValue(itemData, out var e) && e.Throwaway;

        public string TrackId(object itemData)
            => itemData != null && _byRef.TryGetValue(itemData, out var e) ? e.TrackId : string.Empty;

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            bool IEqualityComparer<object>.Equals(object? x, object? y) => ReferenceEquals(x, y);
            int IEqualityComparer<object>.GetHashCode(object obj)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    /// <summary>The receipt-identity fields for the in-flight request (the interface args carry none).</summary>
    internal readonly struct AdapterRequestContext
    {
        public AdapterRequestContext(
            string requestId, string role, long worldUid, string nonce, long seq,
            long connectionGeneration, long tsUnixMs)
        {
            RequestId = string.IsNullOrEmpty(requestId) ? "unknown" : requestId;
            Role = role ?? string.Empty;
            WorldUid = worldUid;
            Nonce = nonce ?? string.Empty;
            Seq = seq;
            ConnectionGeneration = connectionGeneration;
            TsUnixMs = tsUnixMs;
        }

        public string RequestId { get; }
        public string Role { get; }
        public long WorldUid { get; }
        public string Nonce { get; }
        public long Seq { get; }
        public long ConnectionGeneration { get; }
        public long TsUnixMs { get; }
    }

    /// <summary>Supplies the ambient <see cref="AdapterRequestContext"/> the dispatcher set for the in-flight primitive.</summary>
    internal interface IAdapterRequestContextSource
    {
        AdapterRequestContext Current { get; }
    }

    /// <summary>
    /// An <see cref="IAdapterRequestContextSource"/> that resolves its backing source lazily.
    ///
    /// Needed because the wiring is genuinely circular: the adapters require a context source at
    /// construction, while the executor bridge that OWNS the ambient context requires the adapters
    /// at construction. Deferring the lookup to call time breaks the cycle without either party
    /// holding a half-built reference. The resolution happens on the main-thread pump, after
    /// construction has completed, so the delegate never observes a null bridge in practice; it
    /// fails closed with a placeholder context if it somehow does.
    /// </summary>
    internal sealed class DeferredContextSource : IAdapterRequestContextSource
    {
        private readonly Func<IAdapterRequestContextSource?> _resolve;

        public DeferredContextSource(Func<IAdapterRequestContextSource?> resolve)
        {
            _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        }

        public AdapterRequestContext Current
        {
            get
            {
                var inner = _resolve();
                return inner != null
                    ? inner.Current
                    : new AdapterRequestContext("unresolved", "Client", 0, string.Empty, 0, 0, 0);
            }
        }
    }

    /// <summary>
    /// Builds engine-free <see cref="ItemFingerprint"/> / <see cref="RedactedReceipt"/> values from a LIVE
    /// ItemDrop.ItemData using ONLY raw observed facts (PR408 §3.8/§3.10). No verdict, no product-state
    /// claim, no raw custom-data value — values are digested by the engine-free ReceiptFirewall.
    /// </summary>
    internal static class GameItemEvidence
    {
        /// <summary>The vanilla prefab name behind a live item (m_dropPrefab.name), falling back to the shared name.</summary>
        internal static string PrefabName(ItemDrop.ItemData? item)
        {
            if (item == null) return "unknown";
            if (item.m_dropPrefab != null && !string.IsNullOrEmpty(item.m_dropPrefab.name)) return item.m_dropPrefab.name;
            if (item.m_shared != null && !string.IsNullOrEmpty(item.m_shared.m_name)) return item.m_shared.m_name;
            return "unknown";
        }

        /// <summary>The present custom-data KEY names (keys only — values never leave the game).</summary>
        internal static IReadOnlyList<string> CustomKeyNames(ItemDrop.ItemData? item)
        {
            if (item == null || item.m_customData == null) return Array.Empty<string>();
            return new List<string>(item.m_customData.Keys);
        }

        /// <summary>A tracked-item fingerprint from a live item under a run-scoped track id (raw facts only).</summary>
        internal static ItemFingerprint FingerprintOf(ItemDrop.ItemData? item, string trackId)
        {
            string prefab = PrefabName(item);
            int quality = item != null ? item.m_quality : 0;
            return new ItemFingerprint(
                string.IsNullOrEmpty(trackId) ? "untracked" : trackId, prefab, quality, CustomKeyNames(item));
        }

        /// <summary>
        /// Build a firewalled receipt of an observed live item. <paramref name="tooltipText"/> is the
        /// product-rendered tooltip when this is a tooltip observation, else null. Extra descriptive
        /// facts (e.g. seam-driven markers) may be merged; they are asserted verdict-free + claim-free.
        /// </summary>
        internal static RedactedReceipt ObservedReceipt(
            AdapterRequestContext ctx, string verb, ReceiptOutcome outcome, ItemDrop.ItemData? item,
            string? tooltipText = null, IReadOnlyDictionary<string, object?>? extraFacts = null,
            EvidenceReason rejectReason = EvidenceReason.None)
        {
            Dictionary<string, string>? custom = null;
            if (item != null && item.m_customData != null)
                custom = new Dictionary<string, string>(item.m_customData, StringComparer.Ordinal);

            var facts = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in ReceiptFirewall.ExtractObservedFacts(
                         PrefabName(item), item != null ? item.m_quality : 0, custom, tooltipText))
                facts[kv.Key] = kv.Value;
            if (extraFacts != null)
                foreach (var kv in extraFacts) facts[kv.Key] = kv.Value;

            return Finish(ctx, verb, outcome, facts, rejectReason);
        }

        /// <summary>Build a firewalled receipt from an explicit descriptive-fact map (no live item required).</summary>
        internal static RedactedReceipt FactReceipt(
            AdapterRequestContext ctx, string verb, ReceiptOutcome outcome,
            IReadOnlyDictionary<string, object?>? facts, EvidenceReason rejectReason = EvidenceReason.None)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (facts != null) foreach (var kv in facts) map[kv.Key] = kv.Value;
            return Finish(ctx, verb, outcome, map, rejectReason);
        }

        /// <summary>A rejection receipt carrying only the mechanical reason (no observed product state).</summary>
        internal static RedactedReceipt Reject(
            AdapterRequestContext ctx, string verb, ReceiptOutcome outcome, EvidenceReason rejectReason)
            => Finish(ctx, verb, outcome, new Dictionary<string, object?>(StringComparer.Ordinal), rejectReason);

        private static RedactedReceipt Finish(
            AdapterRequestContext ctx, string verb, ReceiptOutcome outcome,
            Dictionary<string, object?> facts, EvidenceReason rejectReason)
        {
            var receipt = new RedactedReceipt(
                ctx.RequestId, verb, ctx.Role, ctx.WorldUid, ctx.Nonce, ctx.Seq,
                ctx.ConnectionGeneration, ctx.TsUnixMs, outcome, facts, rejectReason);

            // Emission firewall (both directions): no smuggled verdict, no harness-produced-state claim.
            ReceiptFirewall.AssertNoProductVerdict(receipt);
            ProductFirewall.AssertNoProductStateClaim(receipt);
            // Bounded redaction (strips any leaked raw value map; collapses a hostile oversized tooltip).
            return ReceiptFirewall.Redact(receipt);
        }
    }
}
