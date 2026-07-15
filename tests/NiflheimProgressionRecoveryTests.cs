// ============================================================================
//  Homestead progression — Foundational AP RECOVERY tests (T002, Gate A).
// ----------------------------------------------------------------------------
//  Proves the durable-journal recovery contract on the SHIPPED, engine-free
//  OperationReceiptStore + ReceiptRecovery (link-compiled from ../src).
//
//  Named acceptance covered here:
//    AT-P0-REPLAY   retry / reconnect returns the recorded result with no
//                   duplicate state, including a SIMULATED restart after every
//                   one of the four durable boundaries.
//    (recovery report: RECOVERABLE vs QUARANTINE vs CLEAN classification.)
//
//  HONEST SCOPE — what "restart" means here vs. what it does NOT:
//  A crash injector THROWS in-process after the Nth durable boundary, then a
//  brand-new store is constructed over the SAME fsync'd journal path. Because the
//  journal is the only durable truth, that fresh store is behaviourally a
//  restarted process for the purpose of journal replay: re-submitting the same
//  operationId must converge to one terminal result. This is NOT a real OS
//  process kill — the exception unwinds a live process and file handles close
//  normally. Real child-PROCESS death at every durable write was already proven
//  by the T001 Gate-A spike (AT-P0-CRASH-EACH-WRITE, accepted); a real
//  child-process in-world reproduction is scoped to T003, not claimed by this
//  suite.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Recovery;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimProgressionRecoveryTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly StoneId _stone = StoneId.FromHostZone(new WorldId("uid:9"), 3, 3);
        private readonly AuthoritativePrincipal _owner =
            new AuthoritativePrincipal(new AccountId("acct-1"), new CharacterId("char-1"), "plat-1");

        public NiflheimProgressionRecoveryTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(), "niflheim-t002-recovery-" + System.Guid.NewGuid().ToString("N") + ".journal");
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        // Throws after boundary == TargetBoundary to SIMULATE a mid-operation crash (in-process,
        // not a real OS kill — see the honest-scope note in the file header). Recovery is proven by
        // replaying the fsync'd journal in a fresh store.
        private sealed class CrashAfter : ICrashInjector
        {
            private readonly ReceiptBoundary _target;
            public CrashAfter(ReceiptBoundary target) { _target = target; }
            public void AfterBoundary(ReceiptBoundary boundary)
            {
                if (boundary == _target) throw new SimulatedProcessDeath();
            }
        }

        private sealed class SimulatedProcessDeath : System.Exception { }

        private OperationReceiptStore NewStore(out InMemoryMirroredStoneApStore stone, out InMemoryCharacterApStore character)
        {
            // Fresh in-memory aggregates + a fresh store over the SAME on-disk journal == a restarted
            // process for the purpose of journal replay (the journal is the only durable truth carried
            // across the simulated restart). Not a real OS process boundary — that is T001/T003 scope.
            stone = new InMemoryMirroredStoneApStore();
            character = new InMemoryCharacterApStore();
            return new OperationReceiptStore(_journalPath, stone, character);
        }

        [Theory]
        [InlineData(ReceiptBoundary.IntentJournaled)]
        [InlineData(ReceiptBoundary.StoneApplied)]
        [InlineData(ReceiptBoundary.CharacterApplied)]
        [InlineData(ReceiptBoundary.Committed)]
        public void AtP0Replay_SimulatedCrashAfterEveryBoundary_RecoversExactlyOneResult(ReceiptBoundary crashAt)
        {
            const string opId = "op-crash";

            // First "process": submit and simulate a crash right after boundary crashAt.
            var store1 = NewStore(out _, out _);
            Assert.Throws<SimulatedProcessDeath>(() =>
                store1.SubmitFoundationalAp(new OperationId(opId), _stone, _owner, "evi", new CrashAfter(crashAt)));

            // Second "process": fresh store over the same journal, re-submit the same operationId.
            var store2 = NewStore(out var stone2, out var character2);
            var result = store2.SubmitFoundationalAp(new OperationId(opId), _stone, _owner, "evi");

            Assert.Equal(1, result.PersonalAp);
            Assert.Equal(1, result.CumulativeAp);
            Assert.Equal(1, result.MirroredStoneAp);
            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, character2.GetPersonalAp(_owner.Account, _owner.Character, _stone));
            Assert.Equal(1, character2.GetCumulativeAp(_owner.Account, _owner.Character, _stone));

            // Third submit is a pure replay: still exactly one, identical receipt.
            var store3 = NewStore(out _, out _);
            var replay = store3.SubmitFoundationalAp(new OperationId(opId), _stone, _owner, "evi");
            Assert.Equal(ReceiptOutcome.Replayed, replay.Outcome);
            Assert.Equal(result.ReceiptId, replay.ReceiptId);
            Assert.Equal(1, replay.MirroredStoneAp);
        }

        [Fact]
        public void Recovery_TerminalOperation_ReportsRecoverableWithOneBalance()
        {
            var store = NewStore(out _, out _);
            store.SubmitFoundationalAp(new OperationId("op-ok"), _stone, _owner, "evi");

            var recovery = new ReceiptRecovery(store);
            var state = recovery.Inspect("op-ok");
            Assert.Equal(RecoveryStatus.Recoverable, state.Status);
            Assert.Equal(1, state.PersonalAp);
            Assert.Equal(1, state.CumulativeAp);
            Assert.Equal(1, state.MirroredStoneAp);
            Assert.Contains("RECOVERABLE", recovery.BuildReport("op-ok"));
        }

        [Fact]
        public void Recovery_PartialNonTerminalState_ReportsQuarantineNotGuessed()
        {
            // Die before the terminal record is durable -> partial state.
            var store1 = NewStore(out _, out _);
            Assert.Throws<SimulatedProcessDeath>(() =>
                store1.SubmitFoundationalAp(new OperationId("op-partial"), _stone, _owner, "evi",
                    new CrashAfter(ReceiptBoundary.StoneApplied)));

            var recovery = new ReceiptRecovery(NewStore(out _, out _));
            var state = recovery.Inspect("op-partial");
            Assert.Equal(RecoveryStatus.Quarantine, state.Status);
            var report = recovery.BuildReport("op-partial");
            Assert.Contains("QUARANTINE", report);
            Assert.Contains("not auto-guessed", report);
        }

        [Fact]
        public void Recovery_UnknownOperation_ReportsClean()
        {
            var recovery = new ReceiptRecovery(NewStore(out _, out _));
            Assert.Equal(RecoveryStatus.Clean, recovery.Inspect("never-began").Status);
        }

        [Fact]
        public void Recovery_TornTailFromPartialWrite_IsTruncatedNotAccepted()
        {
            var store = NewStore(out _, out _);
            store.SubmitFoundationalAp(new OperationId("op-clean"), _stone, _owner, "evi");

            // Append raw garbage bytes simulating a half-written frame from process death.
            using (var fs = new FileStream(_journalPath, FileMode.Append, FileAccess.Write))
            {
                var junk = new byte[] { 0x7F, 0x11, 0x22 }; // shorter than an 8-byte header
                fs.Write(junk, 0, junk.Length);
                fs.Flush();
            }

            var reopened = NewStore(out _, out _);
            var durable = reopened.ReadDurable(out long tornTail);
            Assert.True(tornTail > 0);
            // The one good operation still recovers cleanly; the torn tail is ignored, not "repaired".
            var recovery = new ReceiptRecovery(reopened);
            Assert.Equal(RecoveryStatus.Recoverable, recovery.Inspect("op-clean").Status);
            Assert.Equal(1, recovery.Inspect("op-clean").MirroredStoneAp);
        }
    }

    // ========================================================================
    //  Homestead progression — QUARANTINE + DISPOSABLE-FIXTURE RESET tests
    //  (T005, Tracer 1). Exercises the SHIPPED, engine-free ProgressionStateRepair
    //  against the versioned aggregates and the immutable current-build catalog.
    //
    //  Named acceptance closed here:
    //    AT-INVARIANT-QUARANTINE   contradictory/unknown state is isolated with a
    //                              reason and never silently repaired or guessed.
    //    AT-UNRELEASED-DATA-RESET  an incompatible unreleased fixture is explicitly
    //                              reset to a clean current-build baseline and the
    //                              derived view is rebuilt.
    // ========================================================================
    public sealed class NiflheimProgressionRepairTests
    {
        private static readonly WorldId World = new WorldId("uid:repair");
        private static readonly StoneId Stone = StoneId.FromHostZone(World, 5, 5);
        private static readonly AccountId Account = new AccountId("acct-r");
        private static readonly CharacterId Character = new CharacterId("char-r");
        private static readonly HomesteadProgressionCatalog Catalog = new HomesteadProgressionCatalog();

        private static StoneProgressionAggregate BuildStone(
            int contentRegistryVersion = HomesteadProgressionCatalog.CurrentContentRegistryVersion,
            int historical = 2, int active = 2, long mirroredAp = 0,
            IReadOnlyList<NodeDevelopmentRecord>? nodes = null,
            IReadOnlyList<CommittedTreeRecord>? committedTrees = null)
        {
            return new StoneProgressionAggregate(
                Stone, revision: 3,
                historicalStoneLevel: historical, activeStoneLevel: active,
                foundationalTree: Catalog.FoundationalTree,
                foundationalCatalog: Catalog.FoundationalCatalog,
                contentRegistryVersion: contentRegistryVersion,
                createdProvenance: "receipt:create", updatedProvenance: "receipt:update",
                mirroredStoneAp: mirroredAp, lastAppliedReceiptId: "receipt:last",
                committedTrees: committedTrees,
                nodeDevelopment: nodes);
        }

        private static CharacterProgressionAggregate BuildCharacter(
            IReadOnlyList<NodePurchaseRecord>? purchases = null,
            int personalAp = 3, int personalBp = 5,
            IReadOnlyList<FacetCreditRecord>? facetCredits = null)
        {
            var sr = new CharacterStoneRecord(Stone, personalAp, personalAp, personalBp,
                facetCredits: facetCredits,
                purchases: purchases);
            return new CharacterProgressionAggregate(Account, Character, "world/prod",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "receipt:c",
                stoneRecords: new[] { sr });
        }

        private static AccountStoneAuthorityIndex BuildAuthority(
            StoneId? stone = null, AccountId? account = null) =>
            new AccountStoneAuthorityIndex(account ?? Account, stone ?? Stone, revision: 2,
                reservations: new[] { new AuthorityReservation(Character, RelationshipKind.Bond, "rel", "receipt:act") },
                lastReleaseReceiptId: "");

        // ── AT-INVARIANT-QUARANTINE ───────────────────────────────────────────

        [Fact]
        public void AT_INVARIANT_QUARANTINE_CleanState_ProducesNoNotices()
        {
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(), BuildCharacter(), BuildAuthority());
            Assert.True(report.IsClean);
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_BadStoneLevel_IsIsolatedWithReason()
        {
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(historical: 2, active: 5), BuildCharacter(), BuildAuthority());
            Assert.False(report.IsClean);
            Assert.True(report.Has(QuarantineReason.StoneLevelInvariant));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_NegativeMirroredAp_IsIsolated()
        {
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(mirroredAp: -1), BuildCharacter(), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.NegativeMirroredAp));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_UnknownDevelopedNode_IsIsolated_NotGuessed()
        {
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("GhostNode", 1), 5, 10, true, false, "op-x"),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(nodes: nodes), BuildCharacter(), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.UnknownNodeDevelopment));
            // The offending record is reported with its stable key, not silently dropped or rebound.
            var notice = report.Notices;
            Assert.Contains(notice, n => n.SubjectId == "GhostNode");
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_LocalNodePurchased_IsIsolated()
        {
            // SavorTheHearth is a Stone-cultivated Local Node; it must never be a personal purchase.
            var purchases = new[]
            {
                new NodePurchaseRecord(HomesteadProgressionCatalog.CookingTree,
                    new VersionedId("SavorTheHearth", 1), "PersonalAP", "LocalEffect",
                    new VersionedId("Cooking-L1", 1), "op-buy"),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(), BuildCharacter(purchases), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.LocalNodePurchased));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_WrongTreePurchase_IsIsolated_NotRebound()
        {
            // FieldPrep belongs to Cooking; a purchase recording it under Warrior is contradictory
            // registry state. It must be isolated with its stable key, never silently rebound.
            var purchases = new[]
            {
                new NodePurchaseRecord(HomesteadProgressionCatalog.WarriorTree,
                    new VersionedId("FieldPrep", 1), "PersonalAP", "CharacterEffect",
                    new VersionedId("Warrior-L1", 1), "op-wrong-tree"),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(), BuildCharacter(purchases), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.WrongTreePurchase));
            Assert.Contains(report.Notices, n => n.SubjectId == "FieldPrep");
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_UnavailableNodeDevelopment_IsIsolated()
        {
            // WatchfulCook is authored but first-build Unavailable; a persisted development record for
            // it is contradictory (unavailable nodes reject development) and is isolated.
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("WatchfulCook", 1), 5, 10, true, false, "op-dev-unavail"),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(nodes: nodes), BuildCharacter(), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.UnavailableNodeDevelopment));
            Assert.Contains(report.Notices, n => n.SubjectId == "WatchfulCook");
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_UnavailableNodePurchased_IsIsolated()
        {
            // WatchfulCook is first-build Unavailable; a persisted purchase for it is contradictory
            // (unavailable nodes reject purchase/Offering) and is isolated.
            var purchases = new[]
            {
                new NodePurchaseRecord(HomesteadProgressionCatalog.CookingTree,
                    new VersionedId("WatchfulCook", 1), "PersonalAP", "CharacterEffect",
                    new VersionedId("Cooking-L2", 1), "op-buy-unavail"),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(), BuildCharacter(purchases), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.UnavailableNodePurchased));
            Assert.Contains(report.Notices, n => n.SubjectId == "WatchfulCook");
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_NegativeBalance_IsIsolated()
        {
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(), BuildCharacter(personalBp: -3), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.NegativeCharacterBalance));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_NegativeCommittedTreeBp_IsIsolated()
        {
            // CumulativeBpInvested is an accumulate-only non-negative ledger; a negative value is
            // corrupt state and is isolated, never repaired.
            var committed = new[]
            {
                new CommittedTreeRecord("Cooking", HomesteadProgressionCatalog.CookingTree,
                    "op-commit", "actor", treeLevel: 1, cumulativeBpInvested: -4),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(committedTrees: committed), BuildCharacter(), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.NegativeLedgerValue));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_NegativeNodeBpProgressOrCost_IsIsolated()
        {
            // Per-node BP progress and cost are non-negative ledgers; either negative is isolated.
            var nodesNegProgress = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("FieldPrep", 1), -1, 1, false, false, "op-neg-prog"),
            };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(nodes: nodesNegProgress), BuildCharacter(), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.NegativeLedgerValue));
            Assert.Contains(report.Notices, n => n.SubjectId == "FieldPrep");

            var nodesNegCost = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("FieldPrep", 1), 0, -1, false, false, "op-neg-cost"),
            };
            var report2 = repair.Scan(BuildStone(nodes: nodesNegCost), BuildCharacter(), BuildAuthority());
            Assert.True(report2.Has(QuarantineReason.NegativeLedgerValue));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_NegativeFacetCredit_IsIsolated()
        {
            // Facet Credit is a non-negative ledger; a negative amount is corrupt state and is isolated.
            var credits = new[] { new FacetCreditRecord("Cooking", -2, "op-revoke") };
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(), BuildCharacter(facetCredits: credits), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.NegativeLedgerValue));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_AuthorityKeyedToAnotherStoneOrAccount_IsIsolated()
        {
            var repair = new ProgressionStateRepair(Catalog);
            var wrongStone = repair.Scan(BuildStone(), BuildCharacter(),
                BuildAuthority(stone: StoneId.FromHostZone(World, 9, 9)));
            Assert.True(wrongStone.Has(QuarantineReason.AuthorityMismatch));

            var wrongAccount = repair.Scan(BuildStone(), BuildCharacter(),
                BuildAuthority(account: new AccountId("acct-other")));
            Assert.True(wrongAccount.Has(QuarantineReason.AuthorityMismatch));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_NegativeAggregateRevision_IsIsolated()
        {
            var repair = new ProgressionStateRepair(Catalog);

            // Stone with a negative revision (corrupt/interrupted envelope).
            var badStone = new StoneProgressionAggregate(
                Stone, revision: -1, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: Catalog.FoundationalTree, foundationalCatalog: Catalog.FoundationalCatalog,
                contentRegistryVersion: HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                createdProvenance: "receipt:create", updatedProvenance: "receipt:update",
                mirroredStoneAp: 0, lastAppliedReceiptId: "receipt:last");
            var stoneReport = repair.Scan(badStone, BuildCharacter(), BuildAuthority());
            Assert.True(stoneReport.Has(QuarantineReason.InvalidRevision));

            // Character with a negative revision.
            var badChar = new CharacterProgressionAggregate(Account, Character, "world/prod",
                revision: -5, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "receipt:c",
                stoneRecords: new[] { new CharacterStoneRecord(Stone, 3, 3, 5) });
            var charReport = repair.Scan(BuildStone(), badChar, BuildAuthority());
            Assert.True(charReport.Has(QuarantineReason.InvalidRevision));

            // Authority with a negative revision.
            var badAuth = new AccountStoneAuthorityIndex(Account, Stone, revision: -2,
                reservations: new[] { new AuthorityReservation(Character, RelationshipKind.Bond, "rel", "receipt:act") },
                lastReleaseReceiptId: "");
            var authReport = repair.Scan(BuildStone(), BuildCharacter(), badAuth);
            Assert.True(authReport.Has(QuarantineReason.InvalidRevision));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_UnsupportedSchemaVersion_IsIsolated()
        {
            var repair = new ProgressionStateRepair(Catalog);

            // A future/unknown Stone schema version is quarantined, never blindly reinterpreted.
            var futureStone = new StoneProgressionAggregate(
                Stone, revision: 3, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: Catalog.FoundationalTree, foundationalCatalog: Catalog.FoundationalCatalog,
                contentRegistryVersion: HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                createdProvenance: "receipt:create", updatedProvenance: "receipt:update",
                mirroredStoneAp: 0, lastAppliedReceiptId: "receipt:last",
                schemaVersion: StoneProgressionAggregate.CurrentSchemaVersion + 1);
            Assert.True(repair.Scan(futureStone, BuildCharacter(), BuildAuthority())
                .Has(QuarantineReason.UnsupportedSchemaVersion));

            var futureChar = new CharacterProgressionAggregate(Account, Character, "world/prod",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "receipt:c",
                stoneRecords: new[] { new CharacterStoneRecord(Stone, 3, 3, 5) },
                schemaVersion: CharacterProgressionAggregate.CurrentSchemaVersion + 1);
            Assert.True(repair.Scan(BuildStone(), futureChar, BuildAuthority())
                .Has(QuarantineReason.UnsupportedSchemaVersion));

            var futureAuth = new AccountStoneAuthorityIndex(Account, Stone, revision: 2,
                reservations: new[] { new AuthorityReservation(Character, RelationshipKind.Bond, "rel", "receipt:act") },
                lastReleaseReceiptId: "",
                schemaVersion: AccountStoneAuthorityIndex.CurrentSchemaVersion + 1);
            Assert.True(repair.Scan(BuildStone(), BuildCharacter(), futureAuth)
                .Has(QuarantineReason.UnsupportedSchemaVersion));
        }

        [Fact]
        public void AT_INVARIANT_QUARANTINE_StoneContentVersionMismatch_IsIsolated()
        {
            // A Stone stamped with a content-registry version other than the current build is an
            // incompatible fixture; Scan reports it (operator then chooses explicit reset).
            var repair = new ProgressionStateRepair(Catalog);
            var report = repair.Scan(BuildStone(contentRegistryVersion: 0), BuildCharacter(), BuildAuthority());
            Assert.True(report.Has(QuarantineReason.ContentVersionMismatch));
        }

        // ── AT-UNRELEASED-DATA-RESET ──────────────────────────────────────────

        [Fact]
        public void AT_UNRELEASED_DATA_RESET_IncompatibleFixture_IsExplicitlyResetAndViewRebuilt()
        {
            // A fixture stamped with an OLDER unreleased content-registry version carrying stale
            // developed nodes. Reset discards the disposable selected/developed state, stamps the
            // current build, and rebuilds the derived view.
            var staleNodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("LegacyNode", 1), 9, 9, true, true, "op-legacy"),
            };
            var stone = BuildStone(contentRegistryVersion: 0, mirroredAp: 42, nodes: staleNodes);
            var character = BuildCharacter();
            var authority = BuildAuthority();

            var repair = new ProgressionStateRepair(Catalog);
            var result = repair.ResetIncompatibleFixture(stone, character, authority, "reset:test");

            Assert.True(result.WasReset);
            Assert.Equal(0, result.ContentRegistryVersionBefore);
            Assert.Equal(HomesteadProgressionCatalog.CurrentContentRegistryVersion, result.ContentRegistryVersionAfter);
            Assert.Equal(HomesteadProgressionCatalog.CurrentContentRegistryVersion, result.Stone.ContentRegistryVersion);

            // Disposable state was explicitly cleared, not migrated.
            Assert.Empty(result.Stone.NodeDevelopment);
            Assert.Empty(result.Stone.CommittedTrees);
            Assert.Equal(0, result.Stone.MirroredStoneAp);
            // Proof levels restored to the preconfigured 2/2 baseline.
            Assert.Equal(2, result.Stone.HistoricalStoneLevel);
            Assert.Equal(2, result.Stone.ActiveStoneLevel);
            // The derived view is rebuilt from the clean baseline (no developed nodes -> no rows).
            Assert.Empty(result.RebuiltView.Nodes);

            // Reset is auditable and the state is now clean under the quarantine scan.
            Assert.True(repair.Scan(result.Stone, result.Character, result.Authority).IsClean);
        }

        [Fact]
        public void AT_UNRELEASED_DATA_RESET_StaleCharacterPurchase_DoesNotSurviveReset()
        {
            // The disposable character state must be reset too: a stale purchase keyed to this Stone
            // must NOT survive a claimed clean reset (research.md §"reset the disposable Homestead/
            // character test state explicitly"). Here the character carries a purchase and non-zero
            // balances on the incompatible-version Stone.
            var stalePurchases = new[]
            {
                new NodePurchaseRecord(HomesteadProgressionCatalog.CookingTree,
                    new VersionedId("FieldPrep", 1), "PersonalAP", "CharacterEffect",
                    new VersionedId("Cooking-L1", 1), "op-stale-buy"),
            };
            var stone = BuildStone(contentRegistryVersion: 0);
            var character = BuildCharacter(stalePurchases, personalAp: 7, personalBp: 9);
            var authority = BuildAuthority();

            var repair = new ProgressionStateRepair(Catalog);
            var result = repair.ResetIncompatibleFixture(stone, character, authority, "reset:test");

            Assert.True(result.WasReset);

            // The character's record for this Stone is rebuilt clean: no purchases, zeroed balances.
            CharacterStoneRecord? rec = null;
            foreach (var sr in result.Character.StoneRecords)
                if (sr.StoneId.Equals(stone.StoneId)) rec = sr;
            Assert.NotNull(rec);
            Assert.Empty(rec!.Purchases);
            Assert.Equal(0, rec.PersonalAp);
            Assert.Equal(0, rec.CumulativeAp);
            Assert.Equal(0, rec.PersonalBp);

            // The authority was released — no stale active relationship survives.
            Assert.True(result.Authority.IsVacant);

            // The rebuilt projection carries no active node from the stale purchase, and the reset
            // state is clean under the quarantine scan.
            Assert.Empty(result.RebuiltView.Nodes);
            Assert.True(repair.Scan(result.Stone, result.Character, result.Authority).IsClean);
        }

        [Fact]
        public void AT_UNRELEASED_DATA_RESET_CompatibleFixture_IsNotDiscarded()
        {
            var nodes = new[]
            {
                new NodeDevelopmentRecord(new VersionedId("FieldPrep", 1), 12, 12, true, true, "op-dev"),
            };
            var stone = BuildStone(nodes: nodes);
            var character = BuildCharacter();
            var authority = BuildAuthority();

            var repair = new ProgressionStateRepair(Catalog);
            var result = repair.ResetIncompatibleFixture(stone, character, authority, "reset:test");

            // Current build == fixture version -> no reset; state preserved and view still rebuilt.
            Assert.False(result.WasReset);
            Assert.Same(stone, result.Stone);
            Assert.Single(result.Stone.NodeDevelopment);
            Assert.Single(result.RebuiltView.Nodes);
        }
    }
}
