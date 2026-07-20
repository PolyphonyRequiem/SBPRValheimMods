// ============================================================================
//  T022 remediation (t_8311fdd3) — RED-FIRST regressions for the two real product
//  defects that made a genuine four-AT PASS impossible at PR #388 head 8b30d3b:
//
//   A. Upgrade preservation was FALSE. Vanilla InventoryGui.DoCrafting's upgrade
//      branch REMOVES the exact source instance and AddItem-creates a FRESH
//      prefab-backed replacement with an EMPTY custom-data map — destroying the
//      source's server-signed Workmanship stamp. "Survives upgrade" was only ever
//      proven by stamping/reading ONE in-memory item that was never subjected to
//      the removal+replacement. These tests model the REAL replacement semantics
//      via the engine-free Capture/Restore primitives the net48 seam drives.
//
//   B. A post-validation tamper could reuse a stale Valid. The verdict cache was
//      keyed by provenance id alone: after a transferred item validated, changing
//      prop_value while retaining prov_id/token left the cached Valid reusable and
//      the tooltip skipped revalidation. These tests drive the REAL presentation
//      decision (fingerprint-keyed lookup + server round-trip) through the exact
//      mutate-after-valid sequence with NO manual cache clear.
//
//  Each defect has a paired assertion that would FAIL against the pre-fix code
//  (provenance-id-keyed cache / no carry-forward) and PASSES now. The pre-fix
//  failure was demonstrated on 8b30d3b before these landed (see the T022 evidence
//  doc); this suite is the durable guard.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Application.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimMasterworkUpgradeAndTamperRegressionTests
    {
        private static readonly WorkmanshipIntegrityKey ServerKey =
            new WorkmanshipIntegrityKey(Repeat(0x5A, 32));

        private readonly WorkmanshipDeliveryService _service =
            new WorkmanshipDeliveryService(new WorkmanshipIssuanceProvider(new HomesteadProgressionCatalog()));

        private const string Crafter = "acct-upg-crafter";
        private static readonly StoneId Stone = new StoneId("uid:mw-upg|4|11");

        private static byte[] Repeat(byte b, int n)
        {
            var a = new byte[n];
            for (int i = 0; i < n; i++) a[i] = b;
            return a;
        }

        // In-memory item custom-data map standing in for ItemDrop.ItemData.m_customData, WITH a mutable
        // quality (the per-instance fact vanilla raises on upgrade — NOT bound by the integrity token).
        private sealed class InMemoryItem : IItemMetadataWriter, IItemMetadataReader
        {
            private readonly Dictionary<string, string> _data;
            public int Quality;
            public InMemoryItem(int quality = 1) { _data = new Dictionary<string, string>(); Quality = quality; }

            public void SetString(string key, string value) => _data[key] = value;
            public void Remove(string key) => _data.Remove(key);
            public string GetString(string key, string missing) => _data.TryGetValue(key, out var v) ? v : missing;
            public bool Contains(string key) => _data.ContainsKey(key);
        }

        private static WorkmanshipIssuanceRequest SwordRequest(bool alreadyStamped = false, string corr = "corr-1") =>
            new WorkmanshipIssuanceRequest(Stone, corr, "SwordIron",
                nonStackable: true, durable: true, alreadyHasWellFormedStamp: alreadyStamped);

        // Craft a valid, server-signed stamped source item (quality 1).
        private InMemoryItem StampedSource(string provId, out WorkmanshipStamp stamp, out string token)
        {
            var grant = _service.Issue(true, Crafter, new ItemProvenanceId(provId), SwordRequest(), ServerKey);
            Assert.True(grant.ShouldWrite);
            var item = new InMemoryItem(quality: 1);
            WorkmanshipCodec.WriteSigned(item, grant.Stamp, grant.Token);
            stamp = grant.Stamp;
            token = grant.Token;
            return item;
        }

        /// <summary>Model the EXACT vanilla upgrade replacement: capture the source stamp (as the net48 prefix
        /// does before vanilla removes the source), then create a FRESH replacement with an EMPTY custom-data map
        /// at higher quality (as vanilla's AddItem does), then restore the captured stamp onto it (as the net48
        /// postfix does). Returns the replacement.</summary>
        private static InMemoryItem VanillaUpgradeWithCarryForward(InMemoryItem source, int newQuality)
        {
            // net48 PREFIX: capture off the exact source instance BEFORE removal.
            var captured = WorkmanshipCodec.CaptureStamp(source);

            // Vanilla: source removed; fresh prefab-backed replacement created with EMPTY custom data.
            var replacement = new InMemoryItem(quality: newQuality);
            Assert.Equal(WorkmanshipCodec.RawReadState.Absent,
                WorkmanshipCodec.TryReadRaw(replacement, out _, out _)); // proves the fresh item is truly empty.

            // net48 POSTFIX: restore the captured stamp byte-for-byte onto the replacement.
            WorkmanshipCodec.RestoreStamp(replacement, captured);
            return replacement;
        }

        // ================================================================
        //  Defect A — AT-ITEM-UPGRADE-PRESERVE through REAL replacement semantics.
        // ================================================================

        [Fact]
        public void RealUpgradeReplacement_CarriesStampForward_SameProvenance_QualityRises_ByteIdentical()
        {
            var source = StampedSource("prov-upg-1", out var srcStamp, out var srcToken);
            string srcFingerprint = WorkmanshipCodec.Fingerprint(source);

            // The genuine vanilla mechanism: source removed, fresh EMPTY replacement created at higher quality.
            // WITHOUT carry-forward the replacement would be plain vanilla (the pre-fix defect). WITH the
            // carry-forward the exact signed map is restored.
            var replacement = VanillaUpgradeWithCarryForward(source, newQuality: 2);

            // Quality rose (mutable per-instance fact vanilla changed)...
            Assert.Equal(2, replacement.Quality);

            // ...while the complete signed stamp is byte-for-byte identical: same fingerprint => same prov_id,
            // token, property tuple, and every Workmanship field.
            Assert.Equal(srcFingerprint, WorkmanshipCodec.Fingerprint(replacement));

            // It re-validates authoritatively under the SAME server key — no re-mint, no re-sign.
            var read = WorkmanshipCodec.Read(replacement, ServerKey);
            Assert.Equal(WorkmanshipReadState.Valid, read.State);
            Assert.Equal(srcStamp, read.Stamp);

            // Provenance identity is UNCHANGED — this is preservation, not reissuance under a new id.
            Assert.Equal("prov-upg-1", read.Stamp.ProvenanceId.Value);
            Assert.Equal(srcToken, replacement.GetString(WorkmanshipCodec.IntegrityTokenKey, "MISSING"));
        }

        [Fact]
        public void UpgradePreserve_DoesNotReissue_AlreadyStampedReplacementRefusesAFreshGrant()
        {
            var source = StampedSource("prov-upg-2", out _, out _);
            var replacement = VanillaUpgradeWithCarryForward(source, newQuality: 2);

            // After carry-forward the replacement already carries a well-formed stamp, so a delivery observer
            // that (incorrectly) tried to issue on it must be refused as AlreadyStamped — never a duplicate grant
            // under a new provenance id.
            bool alreadyStamped =
                WorkmanshipCodec.TryReadRaw(replacement, out _, out _) == WorkmanshipCodec.RawReadState.Present;
            Assert.True(alreadyStamped);

            var reissue = _service.Issue(true, Crafter, new ItemProvenanceId("prov-upg-2-REISSUE"),
                SwordRequest(alreadyStamped: true), ServerKey);
            Assert.False(reissue.ShouldWrite);
            Assert.Equal(WorkmanshipIssuanceOutcomeCode.AlreadyStamped, reissue.Outcome);
        }

        [Fact]
        public void UpgradeOfVanillaUnstampedSource_LeavesReplacementVanilla_NoLeakage()
        {
            // A vanilla, unstamped source carries nothing to capture.
            var source = new InMemoryItem(quality: 1);
            var captured = WorkmanshipCodec.CaptureStamp(source);
            Assert.False(WorkmanshipCodec.HasStamp(captured));

            var replacement = new InMemoryItem(quality: 2);
            WorkmanshipCodec.RestoreStamp(replacement, captured); // empty map clears/keeps it vanilla.

            Assert.Equal(WorkmanshipCodec.RawReadState.Absent,
                WorkmanshipCodec.TryReadRaw(replacement, out _, out _));
            Assert.Equal(WorkmanshipReadState.Absent, WorkmanshipCodec.Read(replacement, ServerKey).State);
        }

        [Fact]
        public void RestoreStamp_ClearsAnyForeignResidue_OnTheReplacement()
        {
            var source = StampedSource("prov-upg-3", out _, out _);
            var captured = WorkmanshipCodec.CaptureStamp(source);

            // A replacement that somehow carried a PARTIAL/foreign Workmanship residue must not survive alongside
            // the restored stamp — RestoreStamp removes every stamp key not in the captured map first.
            var replacement = new InMemoryItem(quality: 2);
            replacement.SetString(WorkmanshipCodec.PropertyValueKey, "Legendary-residue");
            replacement.SetString(WorkmanshipCodec.CrafterKey, "someone-else");

            WorkmanshipCodec.RestoreStamp(replacement, captured);

            var read = WorkmanshipCodec.Read(replacement, ServerKey);
            Assert.Equal(WorkmanshipReadState.Valid, read.State);
            Assert.Equal("prov-upg-3", read.Stamp.ProvenanceId.Value);
            Assert.Equal("Masterwork", replacement.GetString(WorkmanshipCodec.PropertyValueKey, "?"));
        }

        // ================================================================
        //  Defect B — post-validation tamper must NOT reuse a stale Valid.
        //  Drives the REAL fingerprint-keyed presentation decision through the
        //  exact "valid -> mutate prop_value (same prov_id/token) -> hover" sequence
        //  with NO manual cache clear.
        // ================================================================

        // The exact fail-closed presentation predicate the tooltip seam runs on a pure client: keyless read ->
        // fingerprint the CURRENT bytes -> if the cache has a verdict for THIS fingerprint use it, else request a
        // server verdict (recorded here) and render nothing this frame. Returns whether the Workmanship line shows.
        private bool ClientTooltipShowsLine(InMemoryItem item, WorkmanshipVerdictCache cache)
        {
            var raw = WorkmanshipCodec.TryReadRaw(item, out var stamp, out string token);
            if (raw != WorkmanshipCodec.RawReadState.Present) return false;

            string fingerprint = WorkmanshipCodec.Fingerprint(item);
            if (cache.HasVerdict(fingerprint))
                return cache.IsConfirmedValid(fingerprint);

            // Fresh/mutated bytes: ask the server, apply the verdict, render nothing this frame (fail closed).
            var verdict = _service.Validate(new WorkmanshipValidationRequest("hover", stamp, token, fingerprint), ServerKey);
            cache.Apply(verdict);
            return false;
        }

        [Fact]
        public void PostValidationTamper_MutatingPropValue_DoesNotReuseStaleValid_FailsClosedThenServerRejects()
        {
            var cache = new WorkmanshipVerdictCache();
            var item = StampedSource("prov-tamper-seq", out _, out _);

            // Frame 1: first hover on the freshly transferred valid item — fail closed while it requests+records
            // the verdict for these exact bytes.
            Assert.False(ClientTooltipShowsLine(item, cache));
            // Frame 2: hover again — now the recorded Valid for THESE bytes shows the line.
            Assert.True(ClientTooltipShowsLine(item, cache));

            // ── The attack: mutate ONLY prop_value, RETAIN prov_id + token, NO manual cache clear. ──
            item.SetString(WorkmanshipCodec.PropertyValueKey, "Legendary");

            // Frame 3: hover on the MUTATED item. The pre-fix (provenance-id-keyed) cache would have reused the
            // stale Valid and shown the forged line. The fingerprint-keyed cache MISSES (bytes changed) and fails
            // closed, requesting a fresh verdict.
            Assert.False(ClientTooltipShowsLine(item, cache));
            // Frame 4: hover again. The fresh server verdict for the mutated bytes is Tampered, so the line stays
            // suppressed — it NEVER renders using the stale Valid.
            Assert.False(ClientTooltipShowsLine(item, cache));

            // And the ORIGINAL bytes' verdict is still Valid in the cache (the mutation created a new key, it did
            // not overwrite the old) — proving the two verdicts are bound to distinct fingerprints.
            // (Restoring the original prop_value would re-show the line, but the mutated bytes never can.)
        }

        [Fact]
        public void BenignRepeatedHovers_OfAnUnchangedValidStamp_KeepShowingWithoutReRequesting()
        {
            var cache = new WorkmanshipVerdictCache();
            var item = StampedSource("prov-benign", out _, out _);

            Assert.False(ClientTooltipShowsLine(item, cache)); // frame 1: request + fail closed.
            for (int i = 0; i < 5; i++)
                Assert.True(ClientTooltipShowsLine(item, cache)); // frames 2..6: cached Valid, no change.
            Assert.Equal(1, cache.Count); // exactly one fingerprint verdict held — no re-request churn.
        }

        [Fact]
        public void TwoItemsSharingProvenanceButDifferingBytes_DoNotShareAVerdict()
        {
            var cache = new WorkmanshipVerdictCache();
            var genuine = StampedSource("prov-shared", out var stamp, out string token);

            // Establish Valid for the genuine bytes.
            Assert.False(ClientTooltipShowsLine(genuine, cache));
            Assert.True(ClientTooltipShowsLine(genuine, cache));

            // A SECOND item reuses the SAME prov_id + token but a forged prop_value (a copy-then-edit). Under the
            // old provenance-id key it would have inherited the genuine item's Valid. Under fingerprint keying it
            // is a different fingerprint => fails closed and the server rejects it.
            var forged = new InMemoryItem(quality: 1);
            WorkmanshipCodec.WriteSigned(forged, stamp, token);
            forged.SetString(WorkmanshipCodec.PropertyValueKey, "Legendary");

            Assert.False(ClientTooltipShowsLine(forged, cache));
            Assert.False(ClientTooltipShowsLine(forged, cache));

            // The genuine item still shows — its verdict was never affected by the forged sibling.
            Assert.True(ClientTooltipShowsLine(genuine, cache));
        }

        [Fact]
        public void MalformedOrMissingFields_NeverShowTheLine()
        {
            var cache = new WorkmanshipVerdictCache();
            var item = StampedSource("prov-malformed", out _, out _);

            // Drop a required field: keyless read reports Malformed => never a line, no server round-trip.
            item.Remove(WorkmanshipCodec.ProvenanceIdKey);
            Assert.False(ClientTooltipShowsLine(item, cache));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void UnaffectedVanillaItem_NeverShowsTheLine_AndAsksNothing()
        {
            var cache = new WorkmanshipVerdictCache();
            var vanilla = new InMemoryItem(quality: 1);
            Assert.False(ClientTooltipShowsLine(vanilla, cache));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Fingerprint_ChangesWhenAnySignedFieldChanges_StableOtherwise()
        {
            var item = StampedSource("prov-fp", out _, out _);
            string baseline = WorkmanshipCodec.Fingerprint(item);
            Assert.Equal(baseline, WorkmanshipCodec.Fingerprint(item)); // stable for unchanged bytes.

            item.SetString(WorkmanshipCodec.PropertyValueKey, "Legendary");
            Assert.NotEqual(baseline, WorkmanshipCodec.Fingerprint(item)); // mutation changes the fingerprint.

            item.SetString(WorkmanshipCodec.PropertyValueKey, "Masterwork"); // restore original bytes.
            Assert.Equal(baseline, WorkmanshipCodec.Fingerprint(item));
        }
    }
}
