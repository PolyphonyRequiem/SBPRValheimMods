// ============================================================================
//  QA-M4 tracked-item identity + continuity (ADR-0009 §10, PR408 §3.6/§3.7) — M4.
// ----------------------------------------------------------------------------
//  ItemFingerprint — a dependency-neutral, hashable identity for a tracked
//  throwaway item, and the continuity/transfer/upgrade decisions the runner
//  correlates receipts on. The SAME logical item retains the SAME track_id
//  across drop->pickup and across an upgrade's source->replacement hop, even
//  though vanilla Clone()s the ItemData (PR408 §3.7, m_customData deep-copied).
//
//  Fields are RAW OBSERVED FACTS ONLY (PR408 §3.10): the harness-minted
//  run-scoped correlation id, the vanilla prefab name, the quality, and the
//  SORTED KEY NAMES present in m_customData (keys only — values are redacted at
//  the receipt boundary, see RedactedReceipt). This is NOT a product signature
//  and carries no entitlement.
//
//  Engine-free: System.* only. Mirrors qa/prebuild-m4/contracts.py ItemFingerprint
//  and evidence.py {assert_fingerprint_continuity, map_upgrade,
//  assert_transfer_preserves} (reviewed t_d5a29850 prebuild).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace SBPR.QaHarness.T022.Core.Evidence
{
    /// <summary>
    /// A hashable identity for a tracked throwaway item. Immutable value object.
    /// <c>CustomKeys</c> is a sorted, de-duplicated, ordinal set of the
    /// <c>m_customData</c> KEY names present (keys only; values never live here).
    /// </summary>
    public sealed class ItemFingerprint : IEquatable<ItemFingerprint>
    {
        /// <summary>Harness-minted run-scoped correlation id (the ledger key). Never a product id.</summary>
        public string TrackId { get; }

        /// <summary>The vanilla item prefab name (ItemDrop.ItemData.m_dropPrefab). PR408 §3.10.</summary>
        public string Prefab { get; }

        /// <summary>ItemDrop.ItemData.m_quality.</summary>
        public int Quality { get; }

        /// <summary>Sorted, ordinal, de-duplicated m_customData KEY names present (keys only).</summary>
        public IReadOnlyList<string> CustomKeys { get; }

        public ItemFingerprint(string trackId, string prefab, int quality, IEnumerable<string>? customKeys = null)
        {
            if (string.IsNullOrEmpty(trackId)) throw new ArgumentException("trackId must be non-empty.", nameof(trackId));
            if (string.IsNullOrEmpty(prefab)) throw new ArgumentException("prefab must be non-empty.", nameof(prefab));
            TrackId = trackId;
            Prefab = prefab;
            Quality = quality;
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (customKeys != null)
            {
                foreach (var k in customKeys)
                {
                    if (!string.IsNullOrEmpty(k)) set.Add(k);
                }
            }
            CustomKeys = set.ToArray();
        }

        /// <summary>
        /// The identity that MUST be stable across a genuine transfer/upgrade hop.
        /// Quality is intentionally excluded (an upgrade legitimately bumps it);
        /// track_id + prefab are the durable identity.
        /// </summary>
        public string ContinuityKey() => TrackId + ":" + Prefab;

        public bool Equals(ItemFingerprint? other)
        {
            if (other is null) return false;
            return string.Equals(TrackId, other.TrackId, StringComparison.Ordinal)
                && string.Equals(Prefab, other.Prefab, StringComparison.Ordinal)
                && Quality == other.Quality
                && CustomKeys.SequenceEqual(other.CustomKeys, StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as ItemFingerprint);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + StringComparer.Ordinal.GetHashCode(TrackId);
                h = h * 31 + StringComparer.Ordinal.GetHashCode(Prefab);
                h = h * 31 + Quality;
                foreach (var k in CustomKeys) h = h * 31 + StringComparer.Ordinal.GetHashCode(k);
                return h;
            }
        }
    }

    /// <summary>
    /// Pure continuity / transfer / upgrade decisions over <see cref="ItemFingerprint"/>.
    /// Every method returns an <see cref="EvidenceReason"/> (None == accept); no world access,
    /// no product state — these are what the runner asserts, never what the helper "passes".
    /// </summary>
    public static class ItemContinuity
    {
        /// <summary>Keys the signature-prefix guard forbids appearing NEW on a hop (never minted harness-side).</summary>
        public static readonly IReadOnlyList<string> SignatureKeyPrefixes =
            new[] { "sbpr_sig", "sbpr_hmac", "sbpr_provenance" };

        internal static bool LooksLikeSignature(string key)
        {
            foreach (var p in SignatureKeyPrefixes)
            {
                if (key.StartsWith(p, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// <see cref="EvidenceReason.None"/> iff <paramref name="after"/> is the SAME logical tracked item
        /// as <paramref name="before"/> across a genuine hop (drop->pickup). Continuity is on the
        /// continuity key (track_id + prefab); quality may change. Every custom-data KEY present before
        /// the hop MUST still be present after (PR408 §3.7 Clone deep-copies m_customData) — a dropped
        /// key means the stamp did not survive, so continuity is broken.
        /// </summary>
        public static EvidenceReason CheckContinuity(ItemFingerprint before, ItemFingerprint after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            if (!string.Equals(before.ContinuityKey(), after.ContinuityKey(), StringComparison.Ordinal))
                return EvidenceReason.ContinuityBroken;
            var afterSet = new HashSet<string>(after.CustomKeys, StringComparer.Ordinal);
            foreach (var k in before.CustomKeys)
            {
                if (!afterSet.Contains(k)) return EvidenceReason.ContinuityBroken;
            }
            return EvidenceReason.None;
        }

        /// <summary>
        /// <see cref="EvidenceReason.None"/> iff a genuine cross-alias transfer preserved the tracked item.
        /// Requires the two actor aliases to be DISTINCT (a self-transfer is not a transfer) and full
        /// fingerprint continuity across drop->pickup.
        /// </summary>
        public static EvidenceReason CheckTransfer(
            string giverAlias, string receiverAlias, ItemFingerprint dropped, ItemFingerprint pickedUp)
        {
            if (string.IsNullOrEmpty(giverAlias)) throw new ArgumentException("giverAlias must be non-empty.", nameof(giverAlias));
            if (string.IsNullOrEmpty(receiverAlias)) throw new ArgumentException("receiverAlias must be non-empty.", nameof(receiverAlias));
            if (string.Equals(giverAlias, receiverAlias, StringComparison.Ordinal))
                return EvidenceReason.SelfTransfer;
            return CheckContinuity(dropped, pickedUp);
        }

        /// <summary>
        /// <see cref="EvidenceReason.None"/> iff <paramref name="replacement"/> is a valid upgrade of
        /// <paramref name="source"/> to <paramref name="targetQuality"/>. Rules (PR408 §3.6 DoCrafting
        /// quality = source.quality + 1):
        ///   * same continuity key (identity preserved),
        ///   * replacement.quality == targetQuality == source.quality + 1,
        ///   * custom-data keys preserved (the Workmanship stamp survives the upgrade),
        ///   * NO new signature-prefixed key appeared (the product issuance seam mints signatures, not us).
        /// </summary>
        public static EvidenceReason CheckUpgrade(
            ItemFingerprint source, ItemFingerprint replacement, int targetQuality)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (!string.Equals(source.ContinuityKey(), replacement.ContinuityKey(), StringComparison.Ordinal))
                return EvidenceReason.ContinuityBroken;
            if (!(replacement.Quality == targetQuality && targetQuality == source.Quality + 1))
                return EvidenceReason.InvalidUpgradeMapping;
            var sourceSet = new HashSet<string>(source.CustomKeys, StringComparer.Ordinal);
            foreach (var k in source.CustomKeys)
            {
                // (superset check) — every source key must survive.
                if (!replacement.CustomKeys.Contains(k)) return EvidenceReason.ContinuityBroken;
            }
            foreach (var k in replacement.CustomKeys)
            {
                if (!sourceSet.Contains(k) && LooksLikeSignature(k))
                    return EvidenceReason.TamperWouldAddSignature;
            }
            return EvidenceReason.None;
        }
    }
}
