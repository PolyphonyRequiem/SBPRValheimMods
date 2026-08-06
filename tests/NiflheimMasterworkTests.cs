// ============================================================================
//  Homestead progression — CRAFTING / MASTERWORK tests (T022, US4, Tracer 6).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T022 Crafting node-2 vertical slice (link-compiled
//  from ../src):
//    * Domain/CharacterProgression/ItemProvenance.cs — the WorkmanshipCodec that
//      stamps/reads/validates one exact-instance Workmanship Property onto an
//      item's custom-data map behind a server-keyed HMAC integrity token, plus
//      the eligibility (non-stackable durable) rule and the ItemProvenanceId.
//    * Adapters/Crafting/WorkmanshipIssuanceProvider.cs — the pure decision: an
//      active Masterwork Character Effect issues one deterministic property on an
//      eligible, not-already-stamped output; dormant/ineligible/duplicate refuse.
//
//  Masterwork is a PERSONAL Character Effect (like T026 Field Fletching I): its
//  active/dormant status is re-derived through the shipped T004 DerivedActivationView
//  (a purchase record for Masterwork@1 at this Stone AND an active relationship to
//  it; no second active-effects ledger — AT-NO-ACTIVE-LEDGER). No Settlement Local
//  policy or build Permission is a conjunct.
//
//  Named acceptance closed here (tasks.md T022):
//    AT-MASTERWORK-ISSUE        active Masterwork issues one deterministic visible
//                               validated Workmanship Property on an eligible
//                               non-stackable durable output; dormant/ineligible
//                               outputs receive nothing.
//    AT-ITEM-UPGRADE-PRESERVE   a valid stamp keeps validating after an upgrade that
//                               preserves the custom-data map (mutable quality/
//                               durability are NOT bound by the integrity token).
//    AT-ITEM-TRANSFER           a valid stamp survives clone/inventory/container
//                               transfer (the exact custom-data map moves with the
//                               instance and re-validates identically).
//    AT-ITEM-TAMPER-DEGRADE     a hand-edited property, a forged/absent token, an
//                               unknown schema, or a partial write all read as
//                               Tampered and DEGRADE TO VANILLA — never a trusted
//                               forged Workmanship.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimMasterworkTests
    {
        private readonly WorldId _world = new WorldId("uid:mw-022");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-crafter");
        private readonly CharacterId _character = new CharacterId("char-crafter");
        private readonly CharacterId _sibling = new CharacterId("char-sibling");

        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;
        private static readonly VersionedId Masterwork = new VersionedId("Masterwork", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly WorkmanshipIssuanceProvider _provider;

        // A shared server integrity key for the stamp/validate tests. A DIFFERENT key models a foreign
        // server (a stamp minted elsewhere never validates here => degrade to vanilla).
        private static readonly WorkmanshipIntegrityKey Key =
            new WorkmanshipIntegrityKey(Repeat(0x5A, 32));
        private static readonly WorkmanshipIntegrityKey ForeignKey =
            new WorkmanshipIntegrityKey(Repeat(0xA5, 32));

        public NiflheimMasterworkTests()
        {
            _stone = StoneId.FromHostZone(_world, 4, 11);
            _provider = new WorkmanshipIssuanceProvider(_catalog);
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

        // ── Aggregate builders (mirror the T026 Field Fletching harness) ──
        private StoneProgressionAggregate BuildStone(bool masterworkDeveloped = true, bool offered = true)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Crafting, "seed-commit-craft", _character.Value, 1, 0)
            };
            var development = new List<NodeDevelopmentRecord>();
            if (masterworkDeveloped)
                development.Add(new NodeDevelopmentRecord(Masterwork, 1, 1, true, offered, "seed-dev-mw"));

            return new StoneProgressionAggregate(_stone, revision: 5, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r",
                committedTrees: committed, nodeDevelopment: development);
        }

        private CharacterProgressionAggregate BuildCharacter(CharacterId character, bool withPurchase)
        {
            NodePurchaseRecord[]? purchases = withPurchase
                ? new[]
                {
                    new NodePurchaseRecord(Crafting, Masterwork, "ap:personal",
                        "CharacterEffect", VersionedId.None, "op-buy-mw")
                }
                : null;
            var stoneRecord = new CharacterStoneRecord(_stone, 3, 3, 1, purchases, null);
            return new CharacterProgressionAggregate(_account, character,
                "world-scope", 1, 2, 2, "receipt", new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BuildAuthority(CharacterId? activeCharacter)
        {
            var idx = AccountStoneAuthorityIndex.Vacant(_account, _stone);
            if (activeCharacter.HasValue)
                idx = idx.WithReservationAdded(
                    new AuthorityReservation(activeCharacter.Value, RelationshipKind.Bond,
                        "rel-mw", "relreceipt:seed"), 1);
            return idx;
        }

        private static ProducedItemFacts Sword(bool alreadyStamped = false, string id = "prov-1") =>
            new ProducedItemFacts("SwordIron", nonStackable: true, durable: true,
                alreadyHasValidWorkmanship: alreadyStamped, new ItemProvenanceId(id));

        // ================================================================
        //  AT-MASTERWORK-ISSUE — active Masterwork issues one deterministic property.
        // ================================================================

        [Fact]
        public void ActiveMasterwork_IssuesOneDeterministicWorkmanshipProperty_OnEligibleDurableOutput()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            Assert.True(_provider.IsMasterworkActive(stone, character, authority));

            var decision = _provider.Decide(stone, character, authority, Sword());

            Assert.Equal(WorkmanshipIssuanceOutcome.Issue, decision.Outcome);
            Assert.True(decision.ShouldIssue);
            Assert.Equal(WorkmanshipIssuanceProvider.MasterworkProperty, decision.Stamp.Property);
            Assert.Equal(Masterwork, decision.Stamp.IssuingNode);
            Assert.Equal("SwordIron", decision.Stamp.ItemType);
            Assert.Equal(_account.Value, decision.Stamp.CrafterAccount);
            Assert.Equal("prov-1", decision.Stamp.ProvenanceId.Value);
        }

        [Fact]
        public void Issuance_IsDeterministic_SameInputsProduceTheSameStamp()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);

            var a = _provider.Decide(stone, character, authority, Sword());
            var b = _provider.Decide(stone, character, authority, Sword());
            Assert.Equal(a.Stamp, b.Stamp);
        }

        [Fact]
        public void DormantMasterwork_NoPurchase_IssuesNothing()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: false);
            var authority = BuildAuthority(_character);

            var decision = _provider.Decide(stone, character, authority, Sword());
            Assert.Equal(WorkmanshipIssuanceOutcome.EffectNotActive, decision.Outcome);
            Assert.False(decision.ShouldIssue);
        }

        [Fact]
        public void DormantMasterwork_NoActiveRelationship_IssuesNothing()
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(null); // relationship released

            var decision = _provider.Decide(stone, character, authority, Sword());
            Assert.Equal(WorkmanshipIssuanceOutcome.EffectNotActive, decision.Outcome);
            Assert.False(decision.ShouldIssue);
        }

        [Fact]
        public void SiblingWithoutOwnPurchase_IssuesNothing_EvenWhenSiblingHoldsRelationship()
        {
            var stone = BuildStone();
            var sibling = BuildCharacter(_sibling, withPurchase: false);
            var authority = BuildAuthority(_sibling);

            var decision = _provider.Decide(stone, sibling, authority, Sword());
            Assert.Equal(WorkmanshipIssuanceOutcome.EffectNotActive, decision.Outcome);
        }

        [Theory]
        [InlineData(false, true)]  // stackable durable (e.g. throwing knife stack) — ineligible
        [InlineData(true, false)]  // non-stackable non-durable — ineligible
        [InlineData(false, false)] // stackable non-durable (arrows, food) — ineligible
        public void IneligibleOutput_IssuesNothing_EvenWhenActive(bool nonStackable, bool durable)
        {
            var item = new ProducedItemFacts("ArrowWood", nonStackable, durable, false, new ItemProvenanceId("p"));
            var decision = _provider.Decide(true, _account.Value, item);
            Assert.Equal(WorkmanshipIssuanceOutcome.IneligibleItem, decision.Outcome);
            Assert.False(decision.ShouldIssue);
        }

        [Fact]
        public void AlreadyStampedOutput_IsIdempotentNoOp()
        {
            var decision = _provider.Decide(true, _account.Value, Sword(alreadyStamped: true));
            Assert.Equal(WorkmanshipIssuanceOutcome.AlreadyStamped, decision.Outcome);
            Assert.False(decision.ShouldIssue);
        }

        [Fact]
        public void EligibilityRule_RequiresBothNonStackableAndDurable()
        {
            Assert.True(WorkmanshipCodec.IsEligible(nonStackable: true, durable: true));
            Assert.False(WorkmanshipCodec.IsEligible(nonStackable: true, durable: false));
            Assert.False(WorkmanshipCodec.IsEligible(nonStackable: false, durable: true));
            Assert.False(WorkmanshipCodec.IsEligible(nonStackable: false, durable: false));
        }

        // ================================================================
        //  Codec round-trip + explicit persistence stamp/read (the exact production code).
        // ================================================================

        private WorkmanshipStamp IssueOnto(InMemoryItem item)
        {
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);
            var decision = _provider.Decide(stone, character, authority, Sword());
            Assert.True(decision.ShouldIssue);
            WorkmanshipCodec.Stamp(item, decision.Stamp, Key);
            return decision.Stamp;
        }

        [Fact]
        public void StampThenRead_RoundTripsAValidWorkmanship()
        {
            var item = new InMemoryItem();
            var issued = IssueOnto(item);

            var read = WorkmanshipCodec.Read(item, Key);
            Assert.Equal(WorkmanshipReadState.Valid, read.State);
            Assert.Equal(issued, read.Stamp);
        }

        [Fact]
        public void UnstampedItem_ReadsAbsent_DegradesToVanilla()
        {
            var read = WorkmanshipCodec.Read(new InMemoryItem(), Key);
            Assert.Equal(WorkmanshipReadState.Absent, read.State);
            Assert.False(read.IsValid);
        }

        // ================================================================
        //  AT-ITEM-UPGRADE-PRESERVE — a valid stamp survives an upgrade that preserves custom data.
        // ================================================================

        [Fact]
        public void ValidStamp_KeepsValidating_AfterAnUpgradeThatPreservesCustomData()
        {
            var item = new InMemoryItem();
            IssueOnto(item);

            // A vanilla upgrade raises quality/durability but does NOT touch m_customData. The integrity
            // token binds only the immutable provenance identity, so it keeps validating unchanged.
            var read = WorkmanshipCodec.Read(item, Key);
            Assert.Equal(WorkmanshipReadState.Valid, read.State);
        }

        // ================================================================
        //  AT-ITEM-TRANSFER — a valid stamp survives clone / inventory / container transfer.
        // ================================================================

        [Fact]
        public void ValidStamp_SurvivesCloneAndTransfer()
        {
            var item = new InMemoryItem();
            var issued = IssueOnto(item);

            // Clone models: inventory move, drop→pickup, container deposit/withdraw — the exact custom-data
            // map rides with the instance to a new ItemData. The transferred item re-validates identically.
            var transferred = item.Clone();
            var read = WorkmanshipCodec.Read(transferred, Key);
            Assert.Equal(WorkmanshipReadState.Valid, read.State);
            Assert.Equal(issued, read.Stamp);
        }

        // ================================================================
        //  AT-ITEM-TAMPER-DEGRADE — forged/edited/unknown metadata degrades to vanilla.
        // ================================================================

        [Fact]
        public void TamperedProperty_DegradesToVanilla()
        {
            var item = new InMemoryItem();
            IssueOnto(item);

            // Hand-edit the visible property value but leave the (now stale) token. Validation fails.
            item.SetString(WorkmanshipCodec.PropertyValueKey, "Legendary");
            var read = WorkmanshipCodec.Read(item, Key);
            Assert.Equal(WorkmanshipReadState.Tampered, read.State);
            Assert.False(read.IsValid);
        }

        [Fact]
        public void ForgedTokenWithoutTheServerKey_DegradesToVanilla()
        {
            var item = new InMemoryItem();
            IssueOnto(item);
            item.SetString(WorkmanshipCodec.IntegrityTokenKey,
                "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
            var read = WorkmanshipCodec.Read(item, Key);
            Assert.Equal(WorkmanshipReadState.Tampered, read.State);
        }

        [Fact]
        public void StampMintedUnderAForeignServerKey_DegradesToVanillaHere()
        {
            var item = new InMemoryItem();
            // Stamp valid under the FOREIGN key...
            var stone = BuildStone();
            var character = BuildCharacter(_character, withPurchase: true);
            var authority = BuildAuthority(_character);
            var decision = _provider.Decide(stone, character, authority, Sword());
            WorkmanshipCodec.Stamp(item, decision.Stamp, ForeignKey);

            // ...reads as Tampered under OUR key: the token cannot be forged across servers.
            Assert.Equal(WorkmanshipReadState.Tampered, WorkmanshipCodec.Read(item, Key).State);
            // ...but still valid under its own minting key (sanity).
            Assert.Equal(WorkmanshipReadState.Valid, WorkmanshipCodec.Read(item, ForeignKey).State);
        }

        [Fact]
        public void LiftedAndPastedStamp_OntoADifferentItemType_DegradesToVanilla()
        {
            var item = new InMemoryItem();
            IssueOnto(item); // itemType SwordIron bound into the token

            // Copy the whole map onto another item, then change ONLY the visible item type. The token was
            // computed over SwordIron, so it no longer matches => tampered.
            var pasted = item.Clone();
            pasted.SetString(WorkmanshipCodec.ItemTypeKey, "ShieldWood");
            Assert.Equal(WorkmanshipReadState.Tampered, WorkmanshipCodec.Read(pasted, Key).State);
        }

        [Fact]
        public void UnknownSchema_DegradesToVanilla()
        {
            var item = new InMemoryItem();
            IssueOnto(item);
            item.SetString(WorkmanshipCodec.SchemaKey, "999");
            Assert.Equal(WorkmanshipReadState.Tampered, WorkmanshipCodec.Read(item, Key).State);
        }

        [Fact]
        public void PartialWriteMissingTheProvenanceIdOrToken_DegradesToVanilla()
        {
            var item = new InMemoryItem();
            IssueOnto(item);
            item.Remove(WorkmanshipCodec.ProvenanceIdKey);
            Assert.Equal(WorkmanshipReadState.Tampered, WorkmanshipCodec.Read(item, Key).State);

            var item2 = new InMemoryItem();
            IssueOnto(item2);
            item2.Remove(WorkmanshipCodec.IntegrityTokenKey);
            Assert.Equal(WorkmanshipReadState.Tampered, WorkmanshipCodec.Read(item2, Key).State);
        }

        [Fact]
        public void IntegrityKey_RejectsAWeakKey()
        {
            Assert.Throws<System.ArgumentException>(() => new WorkmanshipIntegrityKey(Repeat(0x01, 16)));
        }
    }
}
