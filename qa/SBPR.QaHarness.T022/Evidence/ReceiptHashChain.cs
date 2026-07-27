// ============================================================================
//  QA-M4 hash-chained receipts + connection-generation cache (ADR-0009 §6, §10) — M4.
// ----------------------------------------------------------------------------
//  Two deferred-M4 hardening primitives ADR-0009 §10 names as REQUIRED before M6:
//
//   1. ReceiptHashChain — a tamper-evident, append-only chain over emitted
//      receipts. Each link's hash covers the previous link's hash plus the
//      receipt's authenticated fields, so a captured evidence bundle cannot have
//      a receipt silently inserted, dropped, or reordered without breaking the
//      chain (AT-QA-RECEIPT-HASH-CHAIN). The chain is BOUNDED: a fixed-width
//      SHA-256 hex per link, and the receipt itself is already byte-bounded by
//      ReceiptFirewall.Redact — the chain adds no unbounded state.
//
//   2. ReceiptCache — idempotency + stale-cache-hostile-order handling bound to
//      a connection generation. A replay of an exact (requestId, seq) returns the
//      CACHED receipt (never a re-execution); once the channel rolls to a newer
//      generation (reconnect), older-generation entries are stale and a lookup
//      against the NEW generation MISSES, so a hostile out-of-order replay from a
//      dead connection cannot resurrect a cached OK.
//
//  Mirrors qa/prebuild-m4/evidence.py {is_stale_generation, ReceiptCache} and
//  extends it with the hash chain the prebuild deferred to canonical M4.
//  Engine-free: System.Security.Cryptography only — no game/BepInEx dependency.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.QaHarness.T022.Core.Evidence
{
    /// <summary>Identifies a control-channel connection AND its generation (ADR-0009 §10 M4).</summary>
    public sealed class ConnectionId
    {
        /// <summary>"loopback" (client channel) | "zrpc" (server channel).</summary>
        public string Channel { get; }

        /// <summary>Monotonic generation; a reconnect bumps it. Anything from an older generation is stale.</summary>
        public long Generation { get; }

        /// <summary>The DELIVERING peer uid on the server channel (PR408 §3.4); null/0 on the client loopback channel.</summary>
        public long? PeerUid { get; }

        public ConnectionId(string channel, long generation, long? peerUid = null)
        {
            if (string.IsNullOrEmpty(channel)) throw new ArgumentException("channel must be non-empty.", nameof(channel));
            Channel = channel;
            Generation = generation;
            PeerUid = peerUid;
        }
    }

    /// <summary>A single tamper-evident link over an emitted receipt.</summary>
    public sealed class ReceiptChainLink
    {
        /// <summary>0-based position in the chain.</summary>
        public long Index { get; }

        /// <summary>The prior link's <see cref="Hash"/> ("" for the genesis link).</summary>
        public string PrevHash { get; }

        /// <summary>Lowercase hex SHA-256 over (prevHash + canonical receipt fields).</summary>
        public string Hash { get; }

        /// <summary>The receipt this link commits to.</summary>
        public RedactedReceipt Receipt { get; }

        public ReceiptChainLink(long index, string prevHash, string hash, RedactedReceipt receipt)
        {
            Index = index;
            PrevHash = prevHash ?? string.Empty;
            Hash = hash;
            Receipt = receipt;
        }
    }

    /// <summary>
    /// Append-only hash chain over emitted receipts (AT-QA-RECEIPT-HASH-CHAIN). NOT thread-safe by
    /// itself — the dispatcher owns a single-slot main-thread queue, so all appends are serialized
    /// there. Every appended receipt is firewalled (<see cref="ReceiptFirewall.Redact"/>) before it is
    /// committed, so a verdict-shaped or oversized receipt can never enter the chain.
    /// </summary>
    public sealed class ReceiptHashChain
    {
        private const string Genesis = "";
        private readonly List<ReceiptChainLink> _links = new List<ReceiptChainLink>();

        /// <summary>The chain links, in append order.</summary>
        public IReadOnlyList<ReceiptChainLink> Links => _links;

        /// <summary>The head hash ("" when empty) — a compact commitment to the whole chain.</summary>
        public string HeadHash => _links.Count == 0 ? Genesis : _links[_links.Count - 1].Hash;

        /// <summary>The canonical, order-stable signing string for a receipt's authenticated fields.</summary>
        public static string CanonicalReceiptString(RedactedReceipt r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var sb = new StringBuilder();
            sb.Append(r.RequestId).Append('\n');
            sb.Append(r.Verb).Append('\n');
            sb.Append(r.Role).Append('\n');
            sb.Append(r.WorldUid.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(r.Nonce).Append('\n');
            sb.Append(r.Seq.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(r.ConnectionGeneration.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(r.TsUnixMs.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(((int)r.Outcome).ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(((int)r.RejectReason).ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        private static string LinkHash(string prevHash, RedactedReceipt r)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(prevHash + "\n" + CanonicalReceiptString(r));
            byte[] hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>
        /// Firewall then append a receipt as a new link; returns the committed link. The receipt is
        /// redacted first, so what the chain commits to is exactly what is emitted.
        /// </summary>
        public ReceiptChainLink Append(RedactedReceipt receipt, int byteBudget = 4096)
        {
            var firewalled = ReceiptFirewall.Redact(receipt, byteBudget);
            string prev = HeadHash;
            string hash = LinkHash(prev, firewalled);
            var link = new ReceiptChainLink(_links.Count, prev, hash, firewalled);
            _links.Add(link);
            return link;
        }

        /// <summary>
        /// Verify the whole chain: every link's PrevHash must equal the prior link's Hash, and every
        /// link's Hash must recompute from (PrevHash + canonical receipt). Returns the 0-based index of
        /// the FIRST broken link, or -1 if the chain is intact. An inserted/dropped/reordered/edited
        /// receipt is detected here.
        /// </summary>
        public static long FindFirstBreak(IReadOnlyList<ReceiptChainLink> links)
        {
            if (links == null) throw new ArgumentNullException(nameof(links));
            string prev = Genesis;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (link.Index != i) return i;
                if (!string.Equals(link.PrevHash, prev, StringComparison.Ordinal)) return i;
                string expect = LinkHash(prev, link.Receipt);
                if (!string.Equals(link.Hash, expect, StringComparison.Ordinal)) return i;
                prev = link.Hash;
            }
            return -1;
        }

        /// <summary>Convenience: true iff the current chain verifies intact.</summary>
        public bool Verify() => FindFirstBreak(_links) < 0;
    }

    /// <summary>
    /// Idempotency + stale-cache-hostile-order handling bound to a connection generation
    /// (ADR-0009 §3.2, §10 M4). Dedup key is (requestId, seq). NOT thread-safe by itself — serialized
    /// by the single-slot dispatcher.
    /// </summary>
    public sealed class ReceiptCache
    {
        private readonly Dictionary<(string, long), RedactedReceipt> _byKey =
            new Dictionary<(string, long), RedactedReceipt>();

        /// <summary>True iff a receipt minted at <paramref name="receiptGeneration"/> is stale vs the current connection.</summary>
        public static bool IsStaleGeneration(ConnectionId current, long receiptGeneration)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            return receiptGeneration < current.Generation;
        }

        /// <summary>Store a receipt under its (requestId, seq) key.</summary>
        public void Put(RedactedReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            _byKey[(receipt.RequestId, receipt.Seq)] = receipt;
        }

        /// <summary>
        /// Return the cached receipt for an exact (requestId, seq) on a STILL-CURRENT generation, else
        /// null (meaning 'execute fresh' — or, for an older-generation hit, refuse to resurrect it).
        /// </summary>
        public RedactedReceipt? Get(string requestId, long seq, ConnectionId current)
        {
            if (requestId == null) throw new ArgumentNullException(nameof(requestId));
            if (!_byKey.TryGetValue((requestId, seq), out var cached)) return null;
            if (IsStaleGeneration(current, cached.ConnectionGeneration)) return null;
            return cached;
        }
    }
}
