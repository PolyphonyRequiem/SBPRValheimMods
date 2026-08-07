// ============================================================================
//  Homestead progression — CRAFTING / BUILT TO LAST tests (T023, US4, Tracer 6).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T023 Crafting node-3 vertical slice (link-compiled
//  from ../src):
//    * Domain/CharacterProgression/DurabilityProvenance.cs — the DurabilityCodec
//      that stamps/reads/validates one FROZEN maximum-durability factor onto an
//      item's custom-data map behind a server-keyed HMAC integrity token, under a
//      key namespace and canonical domain label DISJOINT from Workmanship's.
//    * Adapters/Crafting/DurabilityIssuanceProvider.cs — the pure decision: a
//      DURABLY-acquired Built to Last Permanent Effect issues the configured
//      maximum-durability property on an eligible, not-already-stamped output;
//      and the read side that derives an item's effective maximum durability from
//      ONLY the stamp that exact instance carries.
//
//  Built to Last is a personal PERMANENT Effect (data-model.md §"Fixed first-build
//  roster": Crafting | 1 | Built to Last | Permanent Effect | personal Offered),
//  and per data-model.md §CharacterProgression "Permanent Effects and Progression
//  Keys survive relationship loss and Tree revocation." So — unlike its sibling
//  T022 Masterwork CHARACTER Effect, which dormants the instant the crafter's
//  relationship drops — this provider keys on the character's DURABLE purchase
//  record alone (outcome class PermanentEffect), with no relationship, policy,
//  permission, or Stone-development conjunct, and no second ledger
//  (AT-NO-ACTIVE-LEDGER).
//
//  Named acceptance closed here (tasks.md T023):
//    AT-BUILT-TO-LAST  an acquired Built to Last issues the configured
//                      maximum-durability property on FUTURE eligible outputs —
//                      including after relationship loss, Tree revocation, and a
//                      process restart (contracts.md §Crafting: "acquired Built to
//                      Last supplies the configured maximum-durability property on
//                      future eligible outputs after relationship loss as well");
//                      issuance is idempotent per exact instance; a tampered/
//                      unknown/foreign stamp degrades to vanilla; and NOTHING
//                      retroactively mutates an already-crafted item.
// ============================================================================

using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimBuiltToLastTests
    {
        private readonly WorldId _world = new WorldId("uid:btl-023");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-smith");
        private readonly CharacterId _character = new CharacterId("char-smith");

        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;
        private static readonly VersionedId BuiltToLast = new VersionedId("BuiltToLast", 1);
        private static readonly VersionedId Masterwork = new VersionedId("Masterwork", 1);

        private readonly DurabilityIssuanceProvider _provider = new DurabilityIssuanceProvider();

        // A shared server integrity key. A DIFFERENT key models a foreign server: a stamp minted elsewhere
        // never validates here => degrade to vanilla.
        private static readonly WorkmanshipIntegrityKey Key =
            new WorkmanshipIntegrityKey(Repeat(0x3C, 32));
        private static readonly WorkmanshipIntegrityKey ForeignKey =
            new WorkmanshipIntegrityKey(Repeat(0xC3, 32));

        private const double VanillaMax = 100.0;

        public NiflheimBuiltToLastTests()
        {
            _stone = StoneId.FromHostZone(_world, 7, 3);
        }

        private static byte[] Repeat(byte b, int n)
        {
            var a = new byte[n];
            for (int i = 0; i < n; i++) a[i] = b;
            return a;
        }

        // ── In-memory item custom-data map standing in for ItemDrop.ItemData.m_customData ──
        private sealed class InMemoryItem : IItemMetadataWriter, IItemMetadataReader
        {
            private readonly Dictionary<string, string> _data;
            public InMemoryItem() => _data = new Dictionary<string, string>();
            private InMemoryItem(Dictionary<string, string> data) => _data = data;

            public void SetString(string key, string value) => _data[key] = value;
            public void Remove(string key) => _data.Remove(key);
            public string GetString(string key, string missing) => _data.TryGetValue(key, out var v) ? v : missing;
            public bool Contains(string key) => _data.ContainsKey(key);

            /// <summary>Deep copy — models a clone/transfer that carries the exact custom-data map to a new
            /// ItemData instance (inventory move, container transfer, drop pickup).</summary>
            public InMemoryItem Clone() => new InMemoryItem(new Dictionary<string, string>(_data));
        }

        /// <summary>A character optionally holding the DURABLE Built to Last Permanent-Effect purchase. The
        /// outcome class "PermanentEffect" is exactly what PurchaseNode stamps for a Permanent Effect node
        /// (NodePurchases.OutcomeClassOf → NodeOutcomeType.PermanentEffect).</summary>
        private CharacterProgressionAggregate BuildCharacter(
            bool builtToLastAcquired, bool masterworkAlso = false, string outcomeClass = "PermanentEffect")
        {
            var purchases = new List<NodePurchaseRecord>();
            if (builtToLastAcquired)
                purchases.Add(new NodePurchaseRecord(Crafting, BuiltToLast, "ap:personal",
                    outcomeClass, VersionedId.None, "op-buy-btl"));
            if (masterworkAlso)
                purchases.Add(new NodePurchaseRecord(Crafting, Masterwork, "ap:personal",
                    "CharacterEffect", VersionedId.None, "op-buy-mw"));

            var stoneRecord = new CharacterStoneRecord(_stone, 3, 3, 1,
                purchases.Count > 0 ? purchases.ToArray() : null, null);
            return new CharacterProgressionAggregate(_account, _character,
                "world-scope", 1, 2, 2, "receipt", new[] { stoneRecord });
        }

        private static DurableItemFacts Sword(bool alreadyStamped = false, string id = "prov-btl-1") =>
            new DurableItemFacts("SwordIron", nonStackable: true, durable: true,
                alreadyHasValidDurabilityStamp: alreadyStamped, new ItemProvenanceId(id));

        // ================================================================
        //  AT-BUILT-TO-LAST — issuance on a future eligible output.
        // ================================================================

        [Fact]
        public void AcquiredBuiltToLast_IssuesConfiguredMaxDurabilityProperty_OnEligibleDurableOutput()
        {
            var character = BuildCharacter(builtToLastAcquired: true);

            Assert.True(_provider.IsBuiltToLastAcquired(character));

            var decision = _provider.Decide(character, Sword());

            Assert.Equal(DurabilityIssuanceOutcome.Issue, decision.Outcome);
            Assert.True(decision.ShouldIssue);
            Assert.Equal(DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor, decision.Stamp.Property.Factor);
            Assert.True(decision.Stamp.Property.Improves);
            Assert.Equal(BuiltToLast, decision.Stamp.IssuingNode);
            Assert.Equal("SwordIron", decision.Stamp.ItemType);
            Assert.Equal(_account.Value, decision.Stamp.CrafterAccount);
            Assert.Equal("prov-btl-1", decision.Stamp.ProvenanceId.Value);
        }

        [Fact]
        public void Issuance_IsDeterministic_SameInputsProduceTheSameStamp()
        {
            var character = BuildCharacter(builtToLastAcquired: true);

            var a = _provider.Decide(character, Sword());
            var b = _provider.Decide(character, Sword());

            Assert.Equal(a.Stamp, b.Stamp);
        }

        [Fact]
        public void WithoutBuiltToLast_IssuesNothing()
        {
            var character = BuildCharacter(builtToLastAcquired: false);

            var decision = _provider.Decide(character, Sword());

            Assert.Equal(DurabilityIssuanceOutcome.EffectNotAcquired, decision.Outcome);
            Assert.False(decision.ShouldIssue);
        }

        [Fact]
        public void OnlyPermanentEffectPurchaseCounts_NotACharacterEffectOfTheSameNode()
        {
            // A same-keyed purchase stamped as a Character Effect is NOT a durable Built to Last grant —
            // durability is what makes this node a Permanent Effect.
            var character = BuildCharacter(builtToLastAcquired: true, outcomeClass: "CharacterEffect");

            Assert.False(_provider.IsBuiltToLastAcquired(character));
            Assert.Equal(DurabilityIssuanceOutcome.EffectNotAcquired, _provider.Decide(character, Sword()).Outcome);
        }

        [Fact]
        public void SiblingMasterworkPurchase_DoesNotGrantBuiltToLast()
        {
            var character = BuildCharacter(builtToLastAcquired: false, masterworkAlso: true);
            Assert.False(_provider.IsBuiltToLastAcquired(character));
        }

        // ── Eligibility: exact non-stackable durable outputs only ──────────────

        [Theory]
        [InlineData(true, true, DurabilityIssuanceOutcome.Issue)]        // sword: exact instance, wears
        [InlineData(false, true, DurabilityIssuanceOutcome.IneligibleItem)]  // stackable (shares one ItemData)
        [InlineData(true, false, DurabilityIssuanceOutcome.IneligibleItem)]  // non-durable: nothing to improve
        [InlineData(false, false, DurabilityIssuanceOutcome.IneligibleItem)] // arrows/food/materials
        public void EligibilityMatrix_OnlyNonStackableDurableOutputsReceiveTheProperty(
            bool nonStackable, bool durable, DurabilityIssuanceOutcome expected)
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var facts = new DurableItemFacts("Thing", nonStackable, durable, false, new ItemProvenanceId("p"));

            Assert.Equal(expected, _provider.Decide(character, facts).Outcome);
            Assert.Equal(nonStackable && durable, DurabilityCodec.IsEligible(nonStackable, durable));
        }

        // ── Idempotency: one provenance per exact instance ─────────────────────

        [Fact]
        public void AlreadyStampedInstance_IsANoOp_NeverReIssuesOrOverwrites()
        {
            var character = BuildCharacter(builtToLastAcquired: true);

            var decision = _provider.Decide(character, Sword(alreadyStamped: true));

            Assert.Equal(DurabilityIssuanceOutcome.AlreadyStamped, decision.Outcome);
            Assert.False(decision.ShouldIssue);
        }

        [Fact]
        public void RepeatedIssuanceAgainstOneItem_StampsOnce_AndTheStampIsUnchanged()
        {
            // The realistic replay: a production event fires twice against the SAME instance. The first
            // issuance stamps; the second observes an already-valid stamp and refuses, so the persisted
            // stamp (and the effective durability) is byte-identical afterwards.
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();

            var first = _provider.Decide(character, Sword());
            Assert.True(first.ShouldIssue);
            DurabilityCodec.Stamp(item, first.Stamp, Key);
            string fingerprintAfterFirst = DurabilityCodec.Fingerprint(item);

            bool alreadyValid = DurabilityCodec.Read(item, Key).IsValid;
            Assert.True(alreadyValid);

            var second = _provider.Decide(character, Sword(alreadyStamped: alreadyValid, id: "prov-btl-2"));
            Assert.False(second.ShouldIssue);
            Assert.Equal(DurabilityIssuanceOutcome.AlreadyStamped, second.Outcome);

            Assert.Equal(fingerprintAfterFirst, DurabilityCodec.Fingerprint(item));
            Assert.Equal("prov-btl-1", DurabilityCodec.Read(item, Key).Stamp.ProvenanceId.Value);
        }

        // ── Durability of the EFFECT across relationship loss / revocation / restart ──

        [Fact]
        public void IssuanceSurvivesRelationshipLoss_FutureOutputsStillReceiveTheProperty()
        {
            // contracts.md §Crafting: "acquired Built to Last supplies the configured maximum-durability
            // property on future eligible outputs AFTER RELATIONSHIP LOSS as well." Modelled structurally:
            // the provider takes NO authority index / relationship input at all, so there is no
            // relationship conjunct that could be lost. Releasing a relationship never removes a purchase.
            var character = BuildCharacter(builtToLastAcquired: true);
            Assert.True(_provider.Decide(character, Sword()).ShouldIssue);
        }

        [Fact]
        public void IssuanceSurvivesTreeRevocation()
        {
            // A Permanent Effect survives Tree revocation (data-model.md invariant): the provider keys ONLY
            // on the durable purchase, never on the Stone's current node-development state — it takes no
            // Stone aggregate, so there is no development conjunct to revoke.
            var character = BuildCharacter(builtToLastAcquired: true);
            Assert.True(_provider.IsBuiltToLastAcquired(character));
            Assert.True(_provider.Decide(character, Sword()).ShouldIssue);
        }

        [Fact]
        public void IssuanceSurvivesRestart_RoundTripsThroughSerializedCharacter()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var restored = CharacterProgressionAggregate.Deserialize(character.Serialize());

            Assert.True(_provider.IsBuiltToLastAcquired(restored));
            var decision = _provider.Decide(restored, Sword());
            Assert.True(decision.ShouldIssue);
            Assert.Equal(DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor, decision.Stamp.Property.Factor);
        }

        [Fact]
        public void ResolvingRepeatedly_MutatesNoState()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            string before = character.Serialize();
            for (int i = 0; i < 5; i++) _provider.Decide(character, Sword());
            Assert.Equal(before, character.Serialize());
        }

        // ================================================================
        //  The READ side — effective maximum durability and the
        //  NO-RETROACTIVE-MUTATION invariant.
        // ================================================================

        [Fact]
        public void StampedItem_ResolvesToTheImprovedMaximumDurability()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            Assert.Equal(VanillaMax * DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor,
                DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void ItemCraftedBeforeAcquisition_IsNeverRetroactivelyImproved()
        {
            // THE TRAP THIS CARD EXISTS TO AVOID. An item produced while the crafter did NOT hold Built to
            // Last carries no stamp. Acquiring the effect afterwards must not reach back: the item's
            // effective maximum durability is derived ONLY from its own (absent) stamp, so it stays vanilla
            // forever, with zero writes.
            var before = BuildCharacter(builtToLastAcquired: false);
            var oldItem = new InMemoryItem();

            var refused = _provider.Decide(before, Sword());
            Assert.False(refused.ShouldIssue);                       // nothing was stamped at craft time.
            Assert.Equal(DurabilityReadState.Absent, DurabilityCodec.Read(oldItem, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, oldItem, Key));

            // Now acquire the effect. The pre-existing item is untouched.
            var after = BuildCharacter(builtToLastAcquired: true);
            Assert.True(_provider.Decide(after, Sword(id: "prov-new")).ShouldIssue);   // FUTURE output only.

            Assert.Equal(DurabilityReadState.Absent, DurabilityCodec.Read(oldItem, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, oldItem, Key));
            Assert.False(oldItem.Contains(DurabilityCodec.ProvenanceIdKey));
        }

        [Fact]
        public void RetuningTheConfiguredFactor_DoesNotAlterAlreadyCraftedItems()
        {
            // The factor is FROZEN into the signed stamp at issuance. A later retune (a differently
            // configured provider) issues the new factor onto NEW outputs while an already-stamped
            // instance keeps resolving to exactly the factor it was issued with.
            var character = BuildCharacter(builtToLastAcquired: true);

            var oldItem = new InMemoryItem();
            DurabilityCodec.Stamp(oldItem, _provider.Decide(character, Sword()).Stamp, Key);
            double oldResolved = DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, oldItem, Key);

            var retuned = new DurabilityIssuanceProvider(2.0);
            var newItem = new InMemoryItem();
            DurabilityCodec.Stamp(newItem, retuned.Decide(character, Sword(id: "prov-retuned")).Stamp, Key);

            Assert.Equal(VanillaMax * 2.0, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, newItem, Key));
            Assert.Equal(oldResolved, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, oldItem, Key));
            Assert.Equal(VanillaMax * DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor, oldResolved);
        }

        [Fact]
        public void LosingTheEffectAfterIssuance_DoesNotStripAnAlreadyIssuedItem()
        {
            // The read side takes no character state at all: the improvement is the INSTANCE's durable fact.
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            var lost = BuildCharacter(builtToLastAcquired: false);
            Assert.False(_provider.IsBuiltToLastAcquired(lost));

            Assert.Equal(VanillaMax * DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor,
                DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void ConfiguredFactorBelowVanillaNeutral_IsRejectedAtConstruction()
        {
            // Built to Last IMPROVES durability; it must never be configurable into a nerf.
            Assert.Throws<ArgumentOutOfRangeException>(() => new DurabilityIssuanceProvider(0.9));
        }

        // ── Codec round-trip / transfer / upgrade ──────────────────────────────

        [Fact]
        public void Stamp_RoundTripsExactly_ThroughTheCodec()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var issued = _provider.Decide(character, Sword()).Stamp;
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, issued, Key);

            var read = DurabilityCodec.Read(item, Key);
            Assert.True(read.IsValid);
            Assert.Equal(issued, read.Stamp);
        }

        [Fact]
        public void StampSurvivesTransfer_TheExactCustomDataMapRevalidatesIdentically()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            var transferred = item.Clone();      // inventory move / container transfer / drop→pickup.

            var read = DurabilityCodec.Read(transferred, Key);
            Assert.True(read.IsValid);
            Assert.Equal(VanillaMax * DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor,
                DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, transferred, Key));
        }

        [Fact]
        public void StampSurvivesUpgrade_CaptureRestoreCarriesTheExactSignedBytes()
        {
            // The vanilla upgrade path destroys the source instance and creates a fresh replacement with an
            // EMPTY custom-data map, so preservation is capture→restore of the exact signed bytes — never a
            // re-mint (which would be a NEW provenance identity, i.e. reissuance).
            var character = BuildCharacter(builtToLastAcquired: true);
            var source = new InMemoryItem();
            DurabilityCodec.Stamp(source, _provider.Decide(character, Sword()).Stamp, Key);

            var captured = DurabilityCodec.CaptureStamp(source);
            Assert.True(DurabilityCodec.HasStamp(captured));

            var replacement = new InMemoryItem();          // fresh, empty — exactly what vanilla creates.
            DurabilityCodec.RestoreStamp(replacement, captured);

            var read = DurabilityCodec.Read(replacement, Key);
            Assert.True(read.IsValid);
            Assert.Equal(DurabilityCodec.Read(source, Key).Stamp, read.Stamp);
            Assert.Equal(DurabilityCodec.Fingerprint(source), DurabilityCodec.Fingerprint(replacement));
        }

        [Fact]
        public void RestoringAnEmptyCapture_ClearsAnyDurabilityKeys_FreshReplacementStaysVanilla()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            DurabilityCodec.RestoreStamp(item, new Dictionary<string, string>());

            Assert.Equal(DurabilityReadState.Absent, DurabilityCodec.Read(item, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        // ── Tamper / unknown metadata degrade to vanilla ───────────────────────

        [Fact]
        public void HandEditedFactor_ReadsTampered_AndDegradesToVanilla()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            item.SetString(DurabilityCodec.FactorKey, "99");     // "free" 99x durability — must not be trusted.

            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void ForgedOrMissingToken_ReadsTampered_AndDegradesToVanilla()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            item.SetString(DurabilityCodec.IntegrityTokenKey, new string('0', 64));
            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);

            item.Remove(DurabilityCodec.IntegrityTokenKey);
            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void UnknownSchema_ReadsTampered_AndDegradesToVanilla()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            item.SetString(DurabilityCodec.SchemaKey, "9999");
            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void ForeignServerKey_NeverValidatesHere_DegradesToVanilla()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, ForeignKey);

            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void StampLiftedOntoADifferentItemType_FailsValidation()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            item.SetString(DurabilityCodec.ItemTypeKey, "AxeBronze");   // pasted onto another item type.

            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);
        }

        [Fact]
        public void PartialWrite_ReadsMalformed_AndDegradesToVanilla()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            item.Remove(DurabilityCodec.NodeVersionKey);    // torn write.

            Assert.Equal(DurabilityCodec.RawReadState.Malformed,
                DurabilityCodec.TryReadRaw(item, out _, out _));
            Assert.Equal(DurabilityReadState.Tampered, DurabilityCodec.Read(item, Key).State);
            Assert.Equal(VanillaMax, DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void UnstampedItem_ReadsAbsent_NotTampered()
        {
            var item = new InMemoryItem();
            Assert.Equal(DurabilityCodec.RawReadState.Absent, DurabilityCodec.TryReadRaw(item, out _, out _));
            Assert.Equal(DurabilityReadState.Absent, DurabilityCodec.Read(item, Key).State);
        }

        // ── Domain separation from the sibling Workmanship provenance ──────────

        [Fact]
        public void WorkmanshipAndDurabilityStamps_CoexistOnOneItem_WithoutInterference()
        {
            // The two provenances occupy DISJOINT key namespaces, so a Masterwork item that is also Built to
            // Last carries both and each reads back independently.
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();

            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);
            WorkmanshipCodec.Stamp(item, new WorkmanshipStamp(
                WorkmanshipCodec.SchemaVersion, Masterwork, new ItemProvenanceId("prov-mw"),
                _account.Value, "SwordIron", WorkmanshipIssuanceProvider.MasterworkProperty), Key);

            Assert.True(DurabilityCodec.Read(item, Key).IsValid);
            Assert.True(WorkmanshipCodec.Read(item, Key).IsValid);
            Assert.Equal(VanillaMax * DurabilityIssuanceProvider.ConfiguredMaxDurabilityFactor,
                DurabilityIssuanceProvider.ResolveMaxDurability(VanillaMax, item, Key));
        }

        [Fact]
        public void ADurabilityTokenIsNotAWorkmanshipToken_CrossDomainReplayFails()
        {
            // Same server key, DIFFERENT canonical domain label: the two codecs sign disjoint message spaces,
            // so a token minted for one provenance can never validate the other's fact. Asserted at both
            // levels — the canonical bytes differ, and the actual validation refuses.
            var stamp = new DurabilityStamp(DurabilityCodec.SchemaVersion, BuiltToLast,
                new ItemProvenanceId("p"), "acct", "SwordIron", new DurabilityProperty(1.25));
            string durabilityToken = DurabilityCodec.Sign(stamp, Key);

            var lookalike = new WorkmanshipStamp(WorkmanshipCodec.SchemaVersion, BuiltToLast,
                new ItemProvenanceId("p"), "acct", "SwordIron", new WorkmanshipProperty("Workmanship", "1.25"));

            Assert.NotEqual(DurabilityCodec.Canonical(stamp), WorkmanshipCodec.Canonical(lookalike));
            Assert.NotEqual("workmanship-v1", DurabilityCodec.CanonicalDomain);
            Assert.Equal(WorkmanshipReadState.Tampered,
                WorkmanshipCodec.Validate(lookalike, durabilityToken, Key));
        }

        [Fact]
        public void Fingerprint_ChangesWheneverAnySignedFieldChanges()
        {
            var character = BuildCharacter(builtToLastAcquired: true);
            var item = new InMemoryItem();
            DurabilityCodec.Stamp(item, _provider.Decide(character, Sword()).Stamp, Key);

            string before = DurabilityCodec.Fingerprint(item);
            item.SetString(DurabilityCodec.FactorKey, "99");     // prov_id + token retained.
            Assert.NotEqual(before, DurabilityCodec.Fingerprint(item));
        }

        // ── The pre-derived overload is the same policy ────────────────────────

        [Fact]
        public void BooleanOverload_AgreesWithTheAggregateOverload()
        {
            var acquired = BuildCharacter(builtToLastAcquired: true);
            var not = BuildCharacter(builtToLastAcquired: false);

            Assert.Equal(_provider.Decide(acquired, Sword()).Outcome,
                _provider.Decide(true, _account.Value, Sword()).Outcome);
            Assert.Equal(_provider.Decide(not, Sword()).Outcome,
                _provider.Decide(false, _account.Value, Sword()).Outcome);
            Assert.Equal(_provider.Decide(acquired, Sword()).Stamp,
                _provider.Decide(true, _account.Value, Sword()).Stamp);
        }
    }
}
