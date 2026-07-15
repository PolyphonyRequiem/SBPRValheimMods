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
using SBPR.Niflheim.HomesteadStones.Domain.Content;
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
            var otherView = query.Execute(stone, other,
                BuildAuthority(OtherAccount, OtherChar, RelationshipKind.None));

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

        [Fact]
        public void AT_READMODEL_STONE_ID_RejectsAuthorityForAnotherAccountOrStone()
        {
            var stone = BuildStone();
            var caller = BuildCharacter(OwnerAccount, OwnerChar);
            var wrongAccount = BuildAuthority(OtherAccount, OwnerChar, RelationshipKind.Bond);
            var wrongStone = new AccountStoneAuthorityIndex(
                OwnerAccount,
                StoneId.FromHostZone(World, 99, 99),
                revision: 1,
                activeCharacter: OwnerChar,
                activeKind: RelationshipKind.Bond,
                activeRelationshipId: "rel-wrong-stone",
                activationReceiptId: "receipt:wrong-stone",
                releaseReceiptId: "");

            var query = new GetStoneProgressionView();
            Assert.Throws<System.ArgumentException>(() => query.Execute(stone, caller, wrongAccount));
            Assert.Throws<System.ArgumentException>(() => query.Execute(stone, caller, wrongStone));
        }

        [Fact]
        public void AT_READMODEL_STONE_ID_ReportsExactNodePricesAndRequirements()
        {
            // spec US1/US3 acceptance scenario 1 + contracts.md §"GetStoneProgressionView": the read
            // model must report each node's exact stable identity/version, outcome, first-build status,
            // AP/BP price, Tree/Stone level gates, prior-Offered-Set gate, and other requirements. This
            // proves the projection surfaces the authored registry values verbatim (provisional proof
            // data — Daniel 2026-07-14 — not final balance).
            var stone = BuildStone();
            var caller = BuildCharacter(OwnerAccount, OwnerChar);
            var authority = BuildAuthority(OwnerAccount, OwnerChar, RelationshipKind.Bond);

            var view = new GetStoneProgressionView().Execute(stone, caller, authority);

            // The catalog section mirrors the full 20-node current-build roster in stable order.
            Assert.Equal(20, view.NodeCatalog.Count);

            NodeCatalogEntry Find(string key)
            {
                foreach (var e in view.NodeCatalog)
                    if (e.Node.Key == key) return e;
                throw new Xunit.Sdk.XunitException("missing catalog entry: " + key);
            }

            // Executable personal node: BP=1, AP=1, gates on committed Tree + content + L1 + Attunement + Offered.
            var fieldPrep = Find("FieldPrep");
            Assert.Equal(1, fieldPrep.DevelopmentBpPrice);
            Assert.Equal(1, fieldPrep.PurchaseApPrice);
            Assert.True(fieldPrep.RequiresCommittedTree);
            Assert.True(fieldPrep.RequiresCurrentContentVersion);
            Assert.Equal(1, fieldPrep.MinActiveStoneLevel);
            Assert.Equal(1, fieldPrep.MinTreeLevel);
            Assert.True(fieldPrep.RequiresActiveAttunement);
            Assert.True(fieldPrep.RequiresOfferedStatus);
            Assert.Empty(fieldPrep.PriorOfferedSet);

            // Local node: BP=1 but NO AP price; no Attunement/Offered gate.
            var savor = Find("SavorTheHearth");
            Assert.Equal(1, savor.DevelopmentBpPrice);
            Assert.Null(savor.PurchaseApPrice);
            Assert.False(savor.RequiresActiveAttunement);
            Assert.False(savor.RequiresOfferedStatus);

            // Swift Preparation: L2 gates + the exact 2-node prior-Offered-Set.
            var swift = Find("SwiftPreparation");
            Assert.Equal(2, swift.MinActiveStoneLevel);
            Assert.Equal(2, swift.MinTreeLevel);
            Assert.Equal(2, swift.PriorOfferedSet.Count);
            Assert.Contains(new VersionedId("FieldPrep", 1), swift.PriorOfferedSet);
            Assert.Contains(new VersionedId("IronStomach", 1), swift.PriorOfferedSet);

            // Unavailable node: no price of any kind, no authored gates.
            var watchful = Find("WatchfulCook");
            Assert.Equal(NodeFirstBuildStatus.Unavailable, watchful.Status);
            Assert.Null(watchful.DevelopmentBpPrice);
            Assert.Null(watchful.PurchaseApPrice);

            // The catalog section is still not a client-authoritative ready flag.
            Assert.False(view.HasClientAuthoritativeReadyFlag);
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

    // ========================================================================
    //  Homestead progression — CONTENT REGISTRY roster + mismatch tests
    //  (T005, Tracer 1). Exercises the SHIPPED, engine-free immutable catalog
    //  and its validator (link-compiled from ../src).
    //
    //  Named acceptance closed here:
    //    AT-CONTENT-MISMATCH-REJECT  stale/unknown same-build references reject
    //                                without misbinding to a "closest" definition.
    //    (roster arithmetic invariant: 20 authored = 13 executable + 7 unavailable,
    //     12 executable Level-1 + 1 executable Level-2 (Swift Preparation).)
    // ========================================================================
    public sealed class NiflheimContentRegistryTests
    {
        private static readonly HomesteadProgressionCatalog Catalog = new HomesteadProgressionCatalog();
        private static readonly ContentRegistryValidator Validator = new ContentRegistryValidator(Catalog);
        private static int RegVer => Catalog.ContentRegistryVersion;

        [Fact]
        public void Roster_Asserts_20_Authored_13_Executable_7_Unavailable()
        {
            var r = Validator.CountRoster();
            Assert.Equal(20, r.Authored);
            Assert.Equal(13, r.Executable);
            Assert.Equal(7, r.Unavailable);
            Assert.Equal(20, r.Executable + r.Unavailable);
        }

        [Fact]
        public void Roster_Executable_Is_12_Level1_Plus_1_Level2_SwiftPreparation()
        {
            var r = Validator.CountRoster();
            Assert.Equal(12, r.ExecutableLevel1);
            Assert.Equal(1, r.ExecutableLevel2);

            // The sole executable Level-2 node is Swift Preparation.
            NodeDefinition? soleL2 = null;
            foreach (var n in Catalog.Nodes)
                if (n.IsExecutable && n.TreeLevel == 2) { Assert.Null(soleL2); soleL2 = n; }
            Assert.NotNull(soleL2);
            Assert.Equal("SwiftPreparation", soleL2!.Node.Key);
        }

        [Fact]
        public void Roster_Invariant_Holds_And_NodeKeys_Are_Unique()
        {
            // Drift guard: throws if the authored roster ever diverges from the spec arithmetic.
            Validator.AssertRosterInvariant();

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var n in Catalog.Nodes)
                Assert.True(seen.Add(n.Node.Key), "duplicate node key: " + n.Node.Key);
        }

        [Fact]
        public void Roster_Every_Node_Has_Exact_Stable_Identity_And_Status()
        {
            // Full table-driven drift guard: the EXACT authored 20-row mapping (data-model.md §"Fixed
            // first-build roster"). Each row pins stable node key + version, owning Tree key + version,
            // Tree level, outcome, ownership, first-build status, AND the provisional authored AP/BP
            // price and requirement gates (Daniel design call 2026-07-14). A single edit to the roster
            // that changes any cell fails here — this is the real per-node drift guard (AGENTS.md).
            //
            // Price convention: executable BP=1; executable personal AP=1; Local nodes AP=null; every
            // unavailable node has BP=null and AP=null. Requirement convention: executable nodes gate on
            // committed Tree + current content + Active Stone Level>=level + Tree Level>=level; personal
            // executable nodes additionally require active Attunement + Offered status. Unavailable nodes
            // author no gates. Swift Preparation additionally carries a 2-node prior-Offered-Set.
            var expected = new (string tree, int treeVer, string node, int nodeVer, int level,
                NodeOutcomeType outcome, NodeOwnership ownership, NodeFirstBuildStatus status,
                int? bp, int? ap, bool committedTree, bool currentContent, int minStone, int minTree,
                bool attune, bool offered, int priorSet)[]
            {
                // Cooking
                ("Cooking", 1, "SavorTheHearth", 1, 1, NodeOutcomeType.LocalEffect, NodeOwnership.StoneCultivated, NodeFirstBuildStatus.Executable, 1, null, true, true, 1, 1, false, false, 0),
                ("Cooking", 1, "FieldPrep", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Cooking", 1, "IronStomach", 1, 1, NodeOutcomeType.PermanentEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Cooking", 1, "SwiftPreparation", 1, 2, NodeOutcomeType.CharacterEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 2, 2, true, true, 2),
                ("Cooking", 1, "WatchfulCook", 1, 2, NodeOutcomeType.CharacterEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
                // Crafting
                ("Crafting", 1, "RefinedWorkshop", 1, 1, NodeOutcomeType.LocalEffect, NodeOwnership.StoneCultivated, NodeFirstBuildStatus.Executable, 1, null, true, true, 1, 1, false, false, 0),
                ("Crafting", 1, "Masterwork", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Crafting", 1, "BuiltToLast", 1, 1, NodeOutcomeType.PermanentEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Crafting", 1, "MeasuredCuts", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
                ("Crafting", 1, "ArtisansCounter", 1, 1, NodeOutcomeType.LocalEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
                // Archer
                ("Archer", 1, "PracticeRange", 1, 1, NodeOutcomeType.LocalEffect, NodeOwnership.StoneCultivated, NodeFirstBuildStatus.Executable, 1, null, true, true, 1, 1, false, false, 0),
                ("Archer", 1, "FieldFletchingI", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Archer", 1, "FletchersHabit", 1, 1, NodeOutcomeType.PermanentEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Archer", 1, "SteadyAim", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
                ("Archer", 1, "BowyersLore", 1, 1, NodeOutcomeType.PermanentEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
                // Warrior
                ("Warrior", 1, "TwigTraining", 1, 1, NodeOutcomeType.LocalEffect, NodeOwnership.StoneCultivated, NodeFirstBuildStatus.Executable, 1, null, true, true, 1, 1, false, false, 0),
                ("Warrior", 1, "ReadyHands", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Warrior", 1, "WeaponDiscipline", 1, 1, NodeOutcomeType.PermanentEffect, NodeOwnership.PersonalOffered, NodeFirstBuildStatus.Executable, 1, 1, true, true, 1, 1, true, true, 0),
                ("Warrior", 1, "ShrugItOffI", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
                ("Warrior", 1, "HeavyHands", 1, 1, NodeOutcomeType.CharacterEffect, NodeOwnership.NoneWhileUnavailable, NodeFirstBuildStatus.Unavailable, null, null, false, false, 0, 0, false, false, 0),
            };

            // Exact count and exact ordered mapping — no extra rows, no missing rows, no drift.
            Assert.Equal(20, Catalog.Nodes.Count);
            Assert.Equal(expected.Length, Catalog.Nodes.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var want = expected[i];
                var got = Catalog.Nodes[i];
                Assert.Equal(want.node, got.Node.Key);
                Assert.Equal(want.nodeVer, got.Node.Version);
                Assert.Equal(want.tree, got.Tree.Key);
                Assert.Equal(want.treeVer, got.Tree.Version);
                Assert.Equal(want.level, got.TreeLevel);
                Assert.Equal(want.outcome, got.Outcome);
                Assert.Equal(want.ownership, got.Ownership);
                Assert.Equal(want.status, got.Status);

                // Exact authored prices.
                Assert.Equal(want.bp, got.Pricing.DevelopmentBpPrice);
                Assert.Equal(want.ap, got.Pricing.PurchaseApPrice);

                // Exact authored requirement gates.
                Assert.Equal(want.committedTree, got.Requirements.RequiresCommittedTree);
                Assert.Equal(want.currentContent, got.Requirements.RequiresCurrentContentVersion);
                Assert.Equal(want.minStone, got.Requirements.MinActiveStoneLevel);
                Assert.Equal(want.minTree, got.Requirements.MinTreeLevel);
                Assert.Equal(want.attune, got.Requirements.RequiresActiveAttunement);
                Assert.Equal(want.offered, got.Requirements.RequiresOfferedStatus);
                Assert.Equal(want.priorSet, got.Requirements.PriorOfferedSet.Count);
            }
        }

        [Fact]
        public void Roster_ProvisionalPricing_MatchesDanielDesignCall()
        {
            // Guard the pricing convention as a whole (Daniel 2026-07-14): executable node BP=1;
            // executable personal AP=1; Local nodes have no AP price; unavailable nodes have no price.
            foreach (var n in Catalog.Nodes)
            {
                if (!n.IsExecutable)
                {
                    Assert.Null(n.Pricing.DevelopmentBpPrice);
                    Assert.Null(n.Pricing.PurchaseApPrice);
                    continue;
                }

                Assert.Equal(1, n.Pricing.DevelopmentBpPrice);
                if (n.Ownership == NodeOwnership.StoneCultivated)
                    Assert.Null(n.Pricing.PurchaseApPrice); // Local nodes: BP-only, never AP-purchased.
                else
                    Assert.Equal(1, n.Pricing.PurchaseApPrice); // executable personal nodes: AP=1.
            }
        }

        [Fact]
        public void Roster_SwiftPreparation_PriorOfferedSet_Is_FieldPrep_And_IronStomach()
        {
            NodeDefinition? swift = null;
            foreach (var n in Catalog.Nodes)
                if (n.Node.Key == "SwiftPreparation") swift = n;
            Assert.NotNull(swift);

            var prior = swift!.Requirements.PriorOfferedSet;
            Assert.Equal(2, prior.Count);
            Assert.Contains(new VersionedId("FieldPrep", 1), prior);
            Assert.Contains(new VersionedId("IronStomach", 1), prior);
            // Local Savor the Hearth is NOT part of the personal prior-Offered Set.
            Assert.DoesNotContain(new VersionedId("SavorTheHearth", 1), prior);

            // Swift Preparation's own gates: Cooking Tree Level 2 and Active Stone Level 2.
            Assert.Equal(2, swift.Requirements.MinTreeLevel);
            Assert.Equal(2, swift.Requirements.MinActiveStoneLevel);
        }

        [Fact]
        public void Roster_ExposedNodes_CannotBeMutatedByDowncast()
        {
            // The immutable registry must not be mutable through the exposed Nodes collection. A caller
            // that downcasts to List<T>/ICollection<T> must NOT be able to alter the catalog.
            var nodes = Catalog.Nodes;
            Assert.False(nodes is List<NodeDefinition>, "Nodes must not expose the backing List directly");

            if (nodes is ICollection<NodeDefinition> mutable)
            {
                Assert.True(mutable.IsReadOnly, "exposed collection must be read-only");
                Assert.Throws<System.NotSupportedException>(() => mutable.Clear());
                Assert.Throws<System.NotSupportedException>(() =>
                    mutable.Add(new NodeDefinition(HomesteadProgressionCatalog.CookingTree,
                        new VersionedId("Injected", 1), 1, NodeOutcomeType.LocalEffect,
                        NodeOwnership.StoneCultivated, NodeFirstBuildStatus.Executable,
                        new NodePricing(1, null),
                        new NodeRequirements(true, true, 1, 1, false, false, null), "Injected")));
            }
            if (nodes is IList<NodeDefinition> list)
            {
                Assert.Throws<System.NotSupportedException>(() => list.RemoveAt(0));
            }

            // Catalog still intact after the attempted mutations.
            Assert.Equal(20, Catalog.Nodes.Count);
        }

        // ── AT-CONTENT-MISMATCH-REJECT ────────────────────────────────────────

        [Fact]
        public void AT_CONTENT_MISMATCH_REJECT_KnownReference_Validates()
        {
            var res = Validator.ValidateNodeReference(RegVer,
                HomesteadProgressionCatalog.CookingTree, new VersionedId("FieldPrep", 1));
            Assert.True(res.IsValid);
            Assert.Equal("", res.RejectionCode);
        }

        [Fact]
        public void AT_CONTENT_MISMATCH_REJECT_UnknownNodeKey_Rejects_NoMisbind()
        {
            var res = Validator.ValidateNodeReference(RegVer,
                HomesteadProgressionCatalog.CookingTree, new VersionedId("GhostNode", 1));
            Assert.False(res.IsValid);
            Assert.Equal(ContentMismatchReason.UnknownNodeKey, res.Reason);
            Assert.Equal("ContentVersionMismatch", res.RejectionCode);
            // It must NOT resolve to any real node.
            Assert.Null(Catalog.TryResolveNode(new VersionedId("GhostNode", 1)));
        }

        [Fact]
        public void AT_CONTENT_MISMATCH_REJECT_StaleNodeVersion_Rejects_NoMisbind()
        {
            // FieldPrep exists at v1; a v2 claim is a version mismatch, never a rebind to v1.
            var stale = new VersionedId("FieldPrep", 2);
            var res = Validator.ValidateNodeReference(RegVer,
                HomesteadProgressionCatalog.CookingTree, stale);
            Assert.False(res.IsValid);
            Assert.Equal(ContentMismatchReason.NodeVersionMismatch, res.Reason);
            Assert.True(Catalog.HasNodeKey(stale));          // key known...
            Assert.Null(Catalog.TryResolveNode(stale));       // ...but version does not resolve.
        }

        [Fact]
        public void AT_CONTENT_MISMATCH_REJECT_WrongTree_Rejects()
        {
            // FieldPrep belongs to Cooking; claiming it under Warrior is a tree mismatch.
            var res = Validator.ValidateNodeReference(RegVer,
                HomesteadProgressionCatalog.WarriorTree, new VersionedId("FieldPrep", 1));
            Assert.False(res.IsValid);
            Assert.Equal(ContentMismatchReason.TreeMismatch, res.Reason);
        }

        [Fact]
        public void AT_CONTENT_MISMATCH_REJECT_StaleRegistryVersion_Rejects()
        {
            var res = Validator.ValidateNodeReference(RegVer + 1,
                HomesteadProgressionCatalog.CookingTree, new VersionedId("FieldPrep", 1));
            Assert.False(res.IsValid);
            Assert.Equal(ContentMismatchReason.RegistryVersionMismatch, res.Reason);
            Assert.Equal("ContentVersionMismatch", res.RejectionCode);
        }
    }
}
