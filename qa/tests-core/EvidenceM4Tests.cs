// ============================================================================
//  QA-M4 named-AT + adversarial evidence tests (t_3cef643f) — engine-free.
// ----------------------------------------------------------------------------
//  Covers the canonical M4 named acceptance tests + adversarial suite over the
//  SHIPPED, engine-free Evidence core:
//
//    AT-QA-TRANSFER-PRESERVES   : a genuine cross-alias drop->pickup preserves the
//                                 tracked item's identity + custom-data keys.
//    AT-QA-TAMPER-DEGRADES      : tamper replaces/removes an allowlisted key on a
//                                 throwaway item only; never adds/copies a signature.
//    AT-QA-RECEIPT-HASH-CHAIN   : an append-only hash chain over receipts detects any
//                                 inserted/dropped/reordered/edited receipt; the
//                                 connection-generation cache refuses a stale replay.
//    AT-QA-TOOLTIP-OBSERVE      : the tooltip fact is a DIRECT-labeled observed fact;
//                                 observation emits raw facts only (no verdict).
//    (CRAFT-THROUGH-PRODUCT-SEAM / CLEANROOM are proven by the seam contract shape +
//     the product firewall here; the live craft is the operator M6 card.)
//
//  Adversarial: no-second-issuance on upgrade, fingerprint continuity, stale-cache
//  hostile order, token/signature redaction, replay/stale generation, large
//  inventory/frame budget, verdict smuggling, product-state claim.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using SBPR.QaHarness.T022.Core.Evidence;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public sealed class ItemFingerprintContinuityTests
    {
        private static ItemFingerprint Fp(string track, string prefab, int q, params string[] keys)
            => new ItemFingerprint(track, prefab, q, keys);

        // AT-QA-TRANSFER-PRESERVES: distinct aliases + full continuity across drop->pickup.
        [Fact]
        public void Transfer_PreservesTrackedItem_AcrossDistinctAliases()
        {
            var dropped = Fp("t1", "SwordIron", 3, "sbpr_workmanship_display");
            var picked = Fp("t1", "SwordIron", 3, "sbpr_workmanship_display");
            Assert.Equal(EvidenceReason.None, ItemContinuity.CheckTransfer("clientA", "clientB", dropped, picked));
        }

        [Fact]
        public void Transfer_RejectsSelfTransfer()
        {
            var f = Fp("t1", "SwordIron", 3);
            Assert.Equal(EvidenceReason.SelfTransfer, ItemContinuity.CheckTransfer("clientA", "clientA", f, f));
        }

        [Fact]
        public void Transfer_RejectsDroppedStampKey_ContinuityBroken()
        {
            var dropped = Fp("t1", "SwordIron", 3, "sbpr_workmanship_display");
            var picked = Fp("t1", "SwordIron", 3); // stamp key vanished
            Assert.Equal(EvidenceReason.ContinuityBroken, ItemContinuity.CheckTransfer("a", "b", dropped, picked));
        }

        [Fact]
        public void Transfer_RejectsDifferentIdentity()
        {
            var dropped = Fp("t1", "SwordIron", 3);
            var picked = Fp("t2", "SwordIron", 3); // different track id
            Assert.Equal(EvidenceReason.ContinuityBroken, ItemContinuity.CheckTransfer("a", "b", dropped, picked));
        }

        // Fingerprint continuity: quality bump alone does NOT break drop->pickup continuity.
        [Fact]
        public void Continuity_AllowsQualityDifference()
        {
            var before = Fp("t1", "SwordIron", 2, "sbpr_workmanship_display");
            var after = Fp("t1", "SwordIron", 3, "sbpr_workmanship_display");
            Assert.Equal(EvidenceReason.None, ItemContinuity.CheckContinuity(before, after));
        }

        // Upgrade source->replacement mapping: quality +1, identity + keys preserved.
        [Fact]
        public void Upgrade_ValidMapping_Accepted()
        {
            var src = Fp("t1", "SwordIron", 2, "sbpr_workmanship_display");
            var rep = Fp("t1", "SwordIron", 3, "sbpr_workmanship_display");
            Assert.Equal(EvidenceReason.None, ItemContinuity.CheckUpgrade(src, rep, 3));
        }

        // Adversarial: no second issuance on upgrade — a NEW signature-prefixed key appearing on the
        // replacement is refused (the harness must not mint a signature during upgrade).
        [Fact]
        public void Upgrade_RejectsNewSignatureKey_NoSecondIssuance()
        {
            var src = Fp("t1", "SwordIron", 2, "sbpr_workmanship_display");
            var rep = Fp("t1", "SwordIron", 3, "sbpr_workmanship_display", "sbpr_sig_v2");
            Assert.Equal(EvidenceReason.TamperWouldAddSignature, ItemContinuity.CheckUpgrade(src, rep, 3));
        }

        [Fact]
        public void Upgrade_RejectsWrongQualityBump()
        {
            var src = Fp("t1", "SwordIron", 2);
            var rep = Fp("t1", "SwordIron", 4); // +2, not +1
            Assert.Equal(EvidenceReason.InvalidUpgradeMapping, ItemContinuity.CheckUpgrade(src, rep, 4));
        }

        [Fact]
        public void Upgrade_RejectsDroppedStampKey()
        {
            var src = Fp("t1", "SwordIron", 2, "sbpr_workmanship_display");
            var rep = Fp("t1", "SwordIron", 3); // stamp vanished
            Assert.Equal(EvidenceReason.ContinuityBroken, ItemContinuity.CheckUpgrade(src, rep, 3));
        }

        // ItemFingerprint value semantics: custom keys are sorted + de-duped; equality is by value.
        [Fact]
        public void Fingerprint_NormalizesKeys_AndEquality()
        {
            var a = new ItemFingerprint("t1", "P", 1, new[] { "b", "a", "a" });
            var b = new ItemFingerprint("t1", "P", 1, new[] { "a", "b" });
            Assert.Equal(new[] { "a", "b" }, a.CustomKeys);
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }

    public sealed class TamperPolicyTests
    {
        private static readonly string[] Present = { "sbpr_workmanship_display", "sbpr_workmanship_grade_label" };

        // AT-QA-TAMPER-DEGRADES: replace an existing allowlisted key on a throwaway item is permitted.
        [Theory]
        [InlineData(TamperOperation.Replace)]
        [InlineData(TamperOperation.Remove)]
        public void Tamper_AllowsReplaceOrRemove_OnThrowawayAllowlistedPresent(TamperOperation op)
        {
            Assert.Equal(EvidenceReason.None,
                TamperPolicy.Validate("sbpr_workmanship_display", Present, isThrowawayItem: true, op));
        }

        // Never touch a non-throwaway item (a legit item / store).
        [Fact]
        public void Tamper_RejectsNonThrowawayItem()
        {
            Assert.Equal(EvidenceReason.TamperItemNotThrowaway,
                TamperPolicy.Validate("sbpr_workmanship_display", Present, isThrowawayItem: false, TamperOperation.Replace));
        }

        // Never add/copy a signature key (prefix guard beyond the literal allowlist).
        [Theory]
        [InlineData("sbpr_sig_main")]
        [InlineData("sbpr_hmac_key")]
        [InlineData("sbpr_provenance_id")]
        public void Tamper_RejectsSignatureKey(string field)
        {
            var present = Present.Concat(new[] { field }).ToArray();
            Assert.Equal(EvidenceReason.TamperWouldAddSignature,
                TamperPolicy.Validate(field, present, isThrowawayItem: true, TamperOperation.Replace));
        }

        // Field not on the static allowlist is refused.
        [Fact]
        public void Tamper_RejectsNonAllowlistedField()
        {
            var present = new[] { "some_other_key" };
            Assert.Equal(EvidenceReason.TamperFieldNotAllowlisted,
                TamperPolicy.Validate("some_other_key", present, isThrowawayItem: true, TamperOperation.Remove));
        }

        // An allowlisted field that is NOT currently present => replace/remove would be an ADD => refused.
        [Fact]
        public void Tamper_RejectsAbsentField_WouldBeAdd()
        {
            Assert.Equal(EvidenceReason.TamperFieldNotPresent,
                TamperPolicy.Validate("sbpr_workmanship_grade_label", new[] { "sbpr_workmanship_display" },
                    isThrowawayItem: true, TamperOperation.Remove));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Tamper_RejectsEmptyField(string? field)
        {
            Assert.Equal(EvidenceReason.TamperFieldNotAllowlisted,
                TamperPolicy.Validate(field, Present, isThrowawayItem: true, TamperOperation.Replace));
        }

        // There is no 'add' operation representable — the enum has only Replace/Remove, which is the
        // structural guarantee that a signature can never be minted by a tamper.
        [Fact]
        public void TamperOperation_HasNoAddMember()
        {
            var names = System.Enum.GetNames(typeof(TamperOperation));
            Assert.DoesNotContain(names, n => n.ToLowerInvariant().Contains("add"));
            Assert.Equal(new[] { "Remove", "Replace" }, names.OrderBy(n => n).ToArray());
        }
    }

    public sealed class ReceiptFirewallTests
    {
        private static RedactedReceipt Ok(IReadOnlyDictionary<string, object?> observed)
            => new RedactedReceipt("r1", "ReadItem", "Client", 42L, "nonce", 1L, 1L, 1000L, ReceiptOutcome.Ok, observed);

        // AT-QA-TOOLTIP-OBSERVE: extraction emits raw facts (prefab/quality/key names/tooltip), no verdict.
        [Fact]
        public void Observe_EmitsRawFactsOnly()
        {
            var custom = new Dictionary<string, string> { ["sbpr_workmanship_display"] = "Masterwork" };
            var facts = ReceiptFirewall.ExtractObservedFacts("SwordIron", 4, custom, tooltipText: "Workmanship: Masterwork");
            Assert.Equal("SwordIron", facts["prefab"]);
            Assert.Equal(4, facts["quality"]);
            Assert.Equal(new[] { "sbpr_workmanship_display" }, (string[])facts["custom_key_names"]!);
            Assert.Equal("Workmanship: Masterwork", facts["tooltip_text"]);
            // The RAW value never appears; only a bounded digest.
            var digests = (SortedDictionary<string, object?>)facts["custom_value_digests"]!;
            Assert.DoesNotContain("Masterwork", digests["sbpr_workmanship_display"]!.ToString());
            Assert.Contains("len=10", digests["sbpr_workmanship_display"]!.ToString());
        }

        // Token/signature redaction: a raw value map that leaked in is stripped at emission.
        [Fact]
        public void Redact_StripsRawValueMaps()
        {
            var observed = new Dictionary<string, object?>
            {
                ["prefab"] = "P",
                ["custom_values"] = new Dictionary<string, string> { ["sbpr_sig_main"] = "SECRET" },
                ["custom_data"] = "raw",
            };
            var firewalled = ReceiptFirewall.Redact(Ok(observed));
            Assert.False(firewalled.Observed.ContainsKey("custom_values"));
            Assert.False(firewalled.Observed.ContainsKey("custom_data"));
        }

        // Large inventory / frame budget: a hostile giant tooltip is collapsed to a length marker.
        [Fact]
        public void Redact_BoundsOversizedTooltip()
        {
            var big = new string('x', 20000);
            var observed = new Dictionary<string, object?> { ["tooltip_text"] = big };
            var firewalled = ReceiptFirewall.Redact(Ok(observed), byteBudget: 256);
            Assert.Equal("<redacted:len=20000>", firewalled.Observed["tooltip_text"]);
        }

        // Verdict smuggling: a receipt whose observed carries a verdict-shaped key is rejected.
        [Theory]
        [InlineData("pass")]
        [InlineData("PASS")]
        [InlineData("verdict")]
        [InlineData("at_result")]
        public void Firewall_RejectsVerdictKey(string key)
        {
            var observed = new Dictionary<string, object?> { [key] = true };
            Assert.Throws<HelperVerdictException>(() => ReceiptFirewall.AssertNoProductVerdict(Ok(observed)));
        }

        // The mechanical outcome enum has no PASS/FAIL member — structural §6 guarantee.
        [Fact]
        public void ReceiptOutcome_HasNoPassOrFailMember()
        {
            var names = System.Enum.GetNames(typeof(ReceiptOutcome)).Select(n => n.ToLowerInvariant()).ToArray();
            Assert.DoesNotContain("pass", names);
            Assert.DoesNotContain("fail", names);
        }

        // Product firewall: a receipt claiming the HARNESS minted/signed product state is rejected.
        [Theory]
        [InlineData("minted")]
        [InlineData("signed")]
        [InlineData("stamp_written")]
        public void ProductFirewall_RejectsHarnessStateClaim(string key)
        {
            var observed = new Dictionary<string, object?> { [key] = true };
            Assert.Throws<HelperVerdictException>(() => ProductFirewall.AssertNoProductStateClaim(Ok(observed)));
        }

        // Direct-vs-inferred labels: tooltip fact is DIRECT, a correlated conclusion is INFERRED.
        [Fact]
        public void LabeledFact_DistinguishesDirectFromInferred()
        {
            Assert.Equal(FactSource.Direct, LabeledFact.Direct("Workmanship: Masterwork").Source);
            Assert.Equal(FactSource.Inferred, LabeledFact.Inferred("transfer_preserved").Source);
        }
    }

    public sealed class ReceiptHashChainTests
    {
        private static RedactedReceipt R(string id, long seq, long gen)
            => new RedactedReceipt(id, "ReadItem", "Client", 42L, "nonce", seq, gen, 1000L + seq, ReceiptOutcome.Ok);

        // AT-QA-RECEIPT-HASH-CHAIN: an intact chain verifies; head hash commits to the whole chain.
        [Fact]
        public void Chain_AppendsAndVerifies()
        {
            var chain = new ReceiptHashChain();
            Assert.Equal("", chain.HeadHash);
            chain.Append(R("r1", 1, 1));
            chain.Append(R("r2", 2, 1));
            chain.Append(R("r3", 3, 1));
            Assert.True(chain.Verify());
            Assert.NotEqual("", chain.HeadHash);
            // Each link references the prior link's hash.
            Assert.Equal(chain.Links[0].Hash, chain.Links[1].PrevHash);
            Assert.Equal(chain.Links[1].Hash, chain.Links[2].PrevHash);
        }

        // Tamper detection: editing a committed receipt breaks the chain at that link.
        [Fact]
        public void Chain_DetectsEditedReceipt()
        {
            var chain = new ReceiptHashChain();
            chain.Append(R("r1", 1, 1));
            chain.Append(R("r2", 2, 1));
            var links = chain.Links.ToList();
            // Forge link[1] with a different receipt but keep its recorded hash/prevhash.
            var forged = new ReceiptChainLink(links[1].Index, links[1].PrevHash, links[1].Hash, R("r2-EVIL", 2, 1));
            links[1] = forged;
            Assert.Equal(1, ReceiptHashChain.FindFirstBreak(links));
        }

        // Reorder detection: swapping two links breaks the chain.
        [Fact]
        public void Chain_DetectsReorder()
        {
            var chain = new ReceiptHashChain();
            chain.Append(R("r1", 1, 1));
            chain.Append(R("r2", 2, 1));
            chain.Append(R("r3", 3, 1));
            var links = chain.Links.ToList();
            (links[1], links[2]) = (links[2], links[1]);
            Assert.True(ReceiptHashChain.FindFirstBreak(links) >= 0);
        }

        // Drop detection: removing a middle link breaks the chain (prevhash no longer matches).
        [Fact]
        public void Chain_DetectsDroppedLink()
        {
            var chain = new ReceiptHashChain();
            chain.Append(R("r1", 1, 1));
            chain.Append(R("r2", 2, 1));
            chain.Append(R("r3", 3, 1));
            var links = chain.Links.ToList();
            links.RemoveAt(1);
            Assert.True(ReceiptHashChain.FindFirstBreak(links) >= 0);
        }

        // The chain firewalls each receipt — a verdict-shaped receipt cannot enter the chain.
        [Fact]
        public void Chain_RefusesVerdictReceipt()
        {
            var chain = new ReceiptHashChain();
            var bad = new RedactedReceipt("r1", "ReadItem", "Client", 42L, "n", 1L, 1L, 1000L, ReceiptOutcome.Ok,
                new Dictionary<string, object?> { ["verdict"] = "PASS" });
            Assert.Throws<HelperVerdictException>(() => chain.Append(bad));
        }
    }

    public sealed class ReceiptCacheGenerationTests
    {
        private static RedactedReceipt R(string id, long seq, long gen)
            => new RedactedReceipt(id, "ReadItem", "Client", 42L, "nonce", seq, gen, 1000L, ReceiptOutcome.Ok);

        // Replay: an exact (requestId, seq) on the current generation returns the CACHED receipt.
        [Fact]
        public void Cache_ReplayReturnsCachedReceipt()
        {
            var cache = new ReceiptCache();
            var cur = new ConnectionId("loopback", 1);
            cache.Put(R("r1", 1, 1));
            var hit = cache.Get("r1", 1, cur);
            Assert.NotNull(hit);
            Assert.Equal("r1", hit!.RequestId);
        }

        // Stale-cache hostile order: a receipt minted on generation 1 MISSES once the channel rolled to 2.
        [Fact]
        public void Cache_StaleGeneration_Misses()
        {
            var cache = new ReceiptCache();
            cache.Put(R("r1", 1, 1)); // minted on gen 1
            var afterReconnect = new ConnectionId("loopback", 2);
            Assert.Null(cache.Get("r1", 1, afterReconnect));
        }

        [Fact]
        public void Cache_MissForUnknownKey()
        {
            var cache = new ReceiptCache();
            Assert.Null(cache.Get("nope", 9, new ConnectionId("loopback", 1)));
        }

        [Fact]
        public void IsStaleGeneration_Semantics()
        {
            var cur = new ConnectionId("zrpc", 5, peerUid: 77);
            Assert.True(ReceiptCache.IsStaleGeneration(cur, 4));
            Assert.False(ReceiptCache.IsStaleGeneration(cur, 5));
            Assert.False(ReceiptCache.IsStaleGeneration(cur, 6));
        }
    }
}
