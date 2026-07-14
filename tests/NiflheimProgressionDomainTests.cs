// ============================================================================
//  Homestead progression — VERSIONED AGGREGATE + READ-MODEL domain tests
//  (T004, Tracer 1).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free CLEAN-side aggregate envelopes and the
//  Stone progression read model (link-compiled from ../src). These are pure
//  domain round-trip / projection tests — no engine, no journal, no I/O.
//
//  Named acceptance closed here (tasks.md T004 / plan.md Tracer 1):
//    AT-STATE-ROUNDTRIP     every authoritative owner, revision, stable identity,
//                           and provenance field survives serialize -> deserialize.
//    AT-READMODEL-STONE-ID  GetStoneProgressionView returns the correct
//                           world-scoped Homestead identity and a caller-specific
//                           projection.
//    AT-NO-ACTIVE-LEDGER    no independently mutable active-effects ledger exists;
//                           activation is a pure re-derivation from persisted
//                           earned/selected/provenance state.
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Application.Queries;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionDomainTests
    {
        private static readonly WorldId World = new WorldId("uid:world-777");
        private static readonly StoneId Stone = StoneId.FromHostZone(World, 12, -4);
        private static readonly AccountId OwnerAccount = new AccountId("acct-owner");
        private static readonly CharacterId OwnerChar = new CharacterId("char-owner");
        private static readonly AccountId OtherAccount = new AccountId("acct-other");
        private static readonly CharacterId OtherChar = new CharacterId("char-other");

        private static StoneProgressionAggregate BuildStone(
            long revision = 3,
            IReadOnlyList<CommittedTreeRecord>? committed = null,
            IReadOnlyList<NodeDevelopmentRecord>? nodes = null)
        {
            return new StoneProgressionAggregate(
                Stone,
                revision,
                historicalStoneLevel: 2,
                activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1,
                createdProvenance: "receipt:create-1",
                updatedProvenance: "receipt:update-9",
                mirroredStoneAp: 3,
                lastAppliedReceiptId: "receipt:last-9",
                committedTrees: committed,
                nodeDevelopment: nodes);
        }

        private static CharacterProgressionAggregate BuildCharacter(
            AccountId account, CharacterId character,
            IReadOnlyList<NodePurchaseRecord>? purchases = null,
            int personalAp = 3, long revision = 3)
        {
            var stoneRecord = new CharacterStoneRecord(
                Stone,
                personalAp: personalAp,
                cumulativeAp: personalAp,
                personalBp: 5,
                facetCredits: new[] { new FacetCreditRecord("Profession", 2, "receipt:revoke-4") },
                purchases: purchases);
            return new CharacterProgressionAggregate(
                account, character,
                worldProductScope: "world-777/trailborne",
                revision: revision,
                bondSlots: 1,
                attunementSlots: 2,
                lastAppliedReceiptId: "receipt:char-last",
                stoneRecords: new[] { stoneRecord });
        }

        private static AccountStoneAuthorityIndex BuildAuthority(
            AccountId account, CharacterId active, RelationshipKind kind, long revision = 2)
        {
            return new AccountStoneAuthorityIndex(
                account, Stone, revision,
                activeCharacter: active,
                activeKind: kind,
                activeRelationshipId: kind == RelationshipKind.None ? "" : "rel-1",
                activationReceiptId: kind == RelationshipKind.None ? "" : "receipt:activate-1",
                releaseReceiptId: "");
        }

        // ── AT-STATE-ROUNDTRIP ────────────────────────────────────────────────

        [Fact]
        public void AT_STATE_ROUNDTRIP_Stone_PreservesEveryAuthoritativeField()
        {
            var committed = new[]
            {
                new CommittedTreeRecord("Profession", new VersionedId("Cooking", 2),
                    "op-commit-1", "char-owner", treeLevel: 1, cumulativeBpInvested: 40),
            };
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("SavorTheHearth", 1), 10, 10, developed: true, offered: false, "op-dev-1"),
                new NodeDevelopmentRecord(new VersionedId("FieldPrep", 1), 5, 12, developed: false, offered: true, "op-dev-2"),
            };
            var original = BuildStone(revision: 7, committed: committed, nodes: nodes);

            var reloaded = StoneProgressionAggregate.Deserialize(original.Serialize());

            Assert.True(original.StructurallyEquals(reloaded));
            Assert.Equal(original.Serialize(), reloaded.Serialize());
            // Spot-check the load-bearing fields explicitly (identity, revision, provenance, ledger).
            Assert.Equal(Stone, reloaded.StoneId);
            Assert.Equal(7, reloaded.Revision);
            Assert.Equal(2, reloaded.ActiveStoneLevel);
            Assert.Equal(3, reloaded.MirroredStoneAp);
            Assert.Equal("receipt:create-1", reloaded.CreatedProvenance);
            Assert.Equal("receipt:last-9", reloaded.LastAppliedReceiptId);
            Assert.Equal(new VersionedId("Cooking", 2), reloaded.CommittedTrees[0].Tree);
            Assert.Equal(40, reloaded.CommittedTrees[0].CumulativeBpInvested);
            Assert.True(reloaded.NodeDevelopment[0].Developed);
            Assert.True(reloaded.NodeDevelopment[1].Offered);
        }

        [Fact]
        public void AT_STATE_ROUNDTRIP_Character_PreservesBalancesAndProvenance()
        {
            var purchases = new[]
            {
                new NodePurchaseRecord(new VersionedId("Cooking", 2), new VersionedId("FieldPrep", 1),
                    apSource: "PersonalAP", outcomeClass: "CharacterEffect",
                    offeredSet: new VersionedId("Cooking-L1", 1), sourceOperationId: "op-buy-1"),
            };
            var original = BuildCharacter(OwnerAccount, OwnerChar, purchases, personalAp: 6, revision: 5);

            var reloaded = CharacterProgressionAggregate.Deserialize(original.Serialize());

            Assert.True(original.StructurallyEquals(reloaded));
            Assert.Equal(OwnerAccount, reloaded.Account);
            Assert.Equal(OwnerChar, reloaded.Character);
            Assert.Equal(5, reloaded.Revision);
            Assert.Equal(6, reloaded.StoneRecords[0].PersonalAp);
            Assert.Equal(5, reloaded.StoneRecords[0].PersonalBp);
            Assert.Equal("Profession", reloaded.StoneRecords[0].FacetCredits[0].FacetId);
            Assert.Equal(2, reloaded.StoneRecords[0].FacetCredits[0].Amount);
            Assert.Equal(new VersionedId("FieldPrep", 1), reloaded.StoneRecords[0].Purchases[0].Node);
            Assert.Equal("op-buy-1", reloaded.StoneRecords[0].Purchases[0].SourceOperationId);
        }

        [Fact]
        public void AT_STATE_ROUNDTRIP_Authority_PreservesActiveOwnerAndProvenance()
        {
            var original = BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.Bond, revision: 4);

            var reloaded = AccountStoneAuthorityIndex.Deserialize(original.Serialize());

            Assert.True(original.StructurallyEquals(reloaded));
            Assert.Equal(OwnerAccount, reloaded.Account);
            Assert.Equal(Stone, reloaded.StoneId);
            Assert.Equal(4, reloaded.Revision);
            Assert.Equal(OwnerChar, reloaded.ActiveCharacter);
            Assert.Equal(RelationshipKind.Bond, reloaded.ActiveKind);
            Assert.Equal("rel-1", reloaded.ActiveRelationshipId);
            Assert.Equal("receipt:activate-1", reloaded.ActivationReceiptId);
            Assert.False(reloaded.IsVacant);
        }

        // ── AT-READMODEL-STONE-ID ─────────────────────────────────────────────

        [Fact]
        public void AT_READMODEL_STONE_ID_ReturnsWorldScopedIdentity()
        {
            var stone = BuildStone();
            var caller = BuildCharacter(OwnerAccount, OwnerChar);
            var authority = BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.Bond);

            var view = new GetStoneProgressionView().Execute(stone, caller, authority);

            // World-scoped identity is exactly StoneId.FromHostZone(world, zoneX, zoneZ) — not a
            // display name, ZDOID, or minted GUID.
            Assert.Equal(Stone, view.StoneId);
            Assert.Equal(StoneId.FromHostZone(World, 12, -4), view.StoneId);
            Assert.Equal("Settlement", view.Family);
            Assert.Equal("Homestead", view.Variant);
            Assert.Equal(stone.Revision, view.StoneRevision);
            Assert.Equal(2, view.ActiveStoneLevel);
            Assert.False(view.HasClientAuthoritativeReadyFlag);
        }

        [Fact]
        public void AT_READMODEL_STONE_ID_ProjectionIsCallerSpecific()
        {
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("FieldPrep", 1), 12, 12, developed: true, offered: true, "op-dev-2"),
            };
            var stone = BuildStone(nodes: nodes);
            var authority = BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.Bond);

            // Owner bought FieldPrep and has 6 Personal AP; the other account bought nothing, has 1 AP.
            var ownerPurchase = new[]
            {
                new NodePurchaseRecord(new VersionedId("Cooking", 2), new VersionedId("FieldPrep", 1),
                    "PersonalAP", "CharacterEffect", new VersionedId("Cooking-L1", 1), "op-buy-1"),
            };
            var owner = BuildCharacter(OwnerAccount, OwnerChar, ownerPurchase, personalAp: 6);
            var other = BuildCharacter(OtherAccount, OtherChar, purchases: null, personalAp: 1);

            var query = new GetStoneProgressionView();
            var ownerView = query.Execute(stone, owner, authority);
            var otherView = query.Execute(stone, other, authority);

            // Same Stone-identity section...
            Assert.Equal(ownerView.StoneId, otherView.StoneId);
            Assert.Equal(ownerView.StoneRevision, otherView.StoneRevision);

            // ...but caller-specific balances + relationship + node status differ.
            Assert.Equal(6, ownerView.CallerBalances.PersonalAp);
            Assert.Equal(1, otherView.CallerBalances.PersonalAp);
            Assert.Equal(RelationshipKind.Bond, ownerView.CallerRelationship);
            Assert.Equal(RelationshipKind.None, otherView.CallerRelationship);

            Assert.Equal(DerivedNodeState.Active, ownerView.NodeStatuses[0].State);
            Assert.True(ownerView.NodeStatuses[0].Purchased);
            Assert.False(otherView.NodeStatuses[0].Purchased);
            Assert.Equal(DerivedNodeState.Offered, otherView.NodeStatuses[0].State);
        }

        // ── AT-NO-ACTIVE-LEDGER ───────────────────────────────────────────────

        [Fact]
        public void AT_NO_ACTIVE_LEDGER_ActivationIsPureDerivationFromPersistedState()
        {
            // A purchased node. The ONLY persisted facts are: the Stone's node development (offered)
            // and the character's purchase provenance. There is no stored "active" flag anywhere.
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("FieldPrep", 1), 12, 12, developed: true, offered: true, "op-dev-2"),
            };
            var stone = BuildStone(nodes: nodes);
            var purchase = new[]
            {
                new NodePurchaseRecord(new VersionedId("Cooking", 2), new VersionedId("FieldPrep", 1),
                    "PersonalAP", "CharacterEffect", new VersionedId("Cooking-L1", 1), "op-buy-1"),
            };
            var caller = BuildCharacter(OwnerAccount, OwnerChar, purchase);

            // With an ACTIVE relationship the derived effect is Active...
            var active = DerivedActivationView.Derive(stone, caller,
                BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.Attunement));
            Assert.Equal(DerivedNodeState.Active, active.Nodes[0].State);
            Assert.True(active.Nodes[0].Active);

            // ...and with the SAME persisted purchase but NO active relationship it derives Dormant.
            // Nothing was written between the two derivations: the identical aggregates yield opposite
            // activation purely as a function of the authority snapshot. That is the proof that no
            // independently mutable active-effects ledger exists.
            var dormant = DerivedActivationView.Derive(stone, caller,
                BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.None));
            Assert.Equal(DerivedNodeState.Dormant, dormant.Nodes[0].State);
            Assert.False(dormant.Nodes[0].Active);

            // The persisted character aggregate is byte-identical before and after deriving activation
            // (deriving is read-only; it cannot mutate a ledger because there is none to mutate).
            var before = caller.Serialize();
            DerivedActivationView.Derive(stone, caller, BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.Attunement));
            Assert.Equal(before, caller.Serialize());
        }

        [Fact]
        public void AT_NO_ACTIVE_LEDGER_ViewExposesNoPersistenceSurface()
        {
            // Structural guard: the DerivedActivationView type must not offer a Serialize()/persistence
            // path. If someone adds one, this test (and the design intent) should be revisited.
            var t = typeof(DerivedActivationView);
            Assert.Null(t.GetMethod("Serialize"));
            Assert.Null(t.GetMethod("Persist"));
            Assert.Null(t.GetMethod("Save"));
        }
    }
}
