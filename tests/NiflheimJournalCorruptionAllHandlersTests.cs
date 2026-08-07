// ============================================================================
//  AT-TORN-FRAME-ALL-SEVEN (ADO #129) — journal corruption coverage for every one
//  of the seven Homestead progression command handlers.
// ----------------------------------------------------------------------------
//  WHY THIS FILE EXISTS
//
//  The journals ARE the save. Every progression projection store (character
//  aggregate, Stone aggregate, account-Stone authority index) is in-memory only and
//  is rebuilt from its handler's append-only journal at server boot. A handler that
//  mishandles a torn frame therefore does not degrade — it permanently loses a
//  player's purchases, Tree commitments, BP, Weapon-Discipline choice, or Settlement
//  Local policy.
//
//  Before this card that property was exercised for exactly ONE of the seven
//  handlers (RelationshipCommandHandler). That is the SAME 1-of-7 shape that produced
//  ADO #127: the delimiter fix landed on RelationshipCommandHandler and missed six
//  siblings. The lone handler with the corruption test was the lone handler that had
//  the fix. This file closes that fix-inheritance gap.
//
//  THE CONTRACT (established by ADO #127, NOT changed here)
//
//    1. Fail-CLOSED at the FRAME layer. A torn tail or a CRC-invalid frame truncates
//       the read AT that point. Records written BEFORE it still replay; records
//       written AFTER it are unreachable. An append-only log with a corrupt length
//       prefix cannot be resynchronised without guessing at durable data, so it is
//       not resynchronised. THE UNREACHABILITY IS ASSERTED, not merely tolerated —
//       it is the observable difference between a CRC check that runs and one that
//       does not, and asserting it is what makes these tests bite (see below).
//    2. Fail-HONEST at the RECORD layer. A structurally perfect frame (right length,
//       right CRC) whose CONTENT is malformed — wrong field count, bad record tag,
//       non-base64 field — is rejected individually as null and SKIPPED. Records
//       both BEFORE and AFTER it still replay. It does not poison the file.
//    3. No partial application. A rejected record never half-applies its projection.
//    4. No throw. Rehydration completes on every corruption shape.
//
//  MUTATION EVIDENCE — WHY THE ASSERTIONS ARE SHAPED THIS WAY
//
//  An earlier draft of this file appended every corruption PAST the last committed
//  record and asserted only "the prior record survives". That passed — and kept
//  passing when the CRC check and the field-count guard were deleted from all six
//  production handlers. It was vacuous. Corruption after the last record is
//  invisible: with the CRC guard gone the reader merely hands the garbage payload to
//  ParseRecord, which rejects it anyway, so the observable outcome is identical.
//
//  Each shape below is therefore positioned so that removing the guard it targets
//  CHANGES AN ASSERTED OUTCOME:
//    * CorruptionShape.CrcInvalidFrame splices the bad frame BETWEEN two committed
//      records and asserts the SECOND is unreachable. Delete `Crc32(payload) != crc`
//      and the second record becomes readable -> RED.
//    * CorruptionShape.WellFramedShortRecord splices a well-framed record with the
//      handler's own tag and too FEW fields. Delete the `parts.Length != N` guard and
//      ParseRecord indexes past the end -> IndexOutOfRangeException escapes boot -> RED.
//  Verified per handler by scripts/ado129-mutation-evidence.py.
//
//  SCOPE (ADO #129, deliberate)
//
//  This card adds TESTS. The shared corruption surface lives in
//  JournalCorruptionHarness.cs — shared TEST code, which carries none of the
//  correlated-blast-radius risk that makes extracting the shared PROTOCOL (ADO #128,
//  still undecided) a real architectural tradeoff. No production behaviour is changed
//  by this file, and none needed changing: all six previously-uncovered handlers were
//  found already correct.
//
//  WHAT THIS PROVES AND WHAT IT DOES NOT
//
//  These are unit-level assertions about rehydration over SYNTHETICALLY corrupted
//  journal files. They do NOT prove that a real mid-write process kill on a live
//  dedicated server produces byte patterns identical to these, nor that the live boot
//  path behaves identically end to end. Logs green is not playable.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>The corruption shapes every handler is driven through. Ordered by which layer they
    /// exercise: the first three are FRAME-layer (fail-closed), the last two RECORD-layer
    /// (fail-honest).</summary>
    public enum CorruptionShape
    {
        /// <summary>Raw bytes shorter than a frame header, appended past the last intact frame — the
        /// classic half-written tail from process death.</summary>
        TornTail,

        /// <summary>A frame header claiming a payload far longer than the bytes that follow.</summary>
        TruncatedFrameHeader,

        /// <summary>A correctly-sized frame whose stored CRC does not match its payload, spliced
        /// BETWEEN two committed records. Everything after it must become unreachable.</summary>
        CrcInvalidFrame,

        /// <summary>A structurally perfect frame whose payload is not a record of this handler at all
        /// (foreign tag, arbitrary content), spliced between two committed records.</summary>
        WellFramedGarbage,

        /// <summary>A structurally perfect frame carrying this handler's OWN record tag but too few
        /// fields — the shape that reaches the field-count guard specifically.</summary>
        WellFramedShortRecord
    }

    public sealed class NiflheimJournalCorruptionAllHandlersTests : System.IDisposable
    {
        // ── Shared world/identity fixture ──
        private readonly WorldId _world = new WorldId("uid:corrupt-129");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-gov");
        private readonly CharacterId _governor = new CharacterId("char-gov");
        private readonly AccountId _accountAtt = new AccountId("acct-att");
        private readonly CharacterId _attuned = new CharacterId("char-att");

        private const string BondRelId = "rel-bond-gov";
        private const string AttRelId = "rel-att";

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId Warrior = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);
        private static readonly VersionedId IronStomach = new VersionedId("IronStomach", 1);
        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1);
        private static readonly VersionedId WeaponDiscipline = new VersionedId("WeaponDiscipline", 1);

        private readonly List<string> _journals = new List<string>();

        public NiflheimJournalCorruptionAllHandlersTests()
        {
            _stone = StoneId.FromHostZone(_world, 6, -2);
        }

        public void Dispose()
        {
            foreach (var p in _journals)
                if (File.Exists(p)) File.Delete(p);
        }

        private string TempJournal(string tag)
        {
            string p = Path.Combine(Path.GetTempPath(),
                "niflheim-ado129-" + tag + "-" + Guid.NewGuid().ToString("N") + ".journal");
            _journals.Add(p);
            return p;
        }

        /// <summary>True when this shape truncates the read at the corruption point (frame layer), so a
        /// record committed AFTER it must be unreachable on boot. False for record-layer shapes, where
        /// the record after the corruption must still replay.</summary>
        private static bool IsFrameLayer(CorruptionShape shape)
            => shape == CorruptionShape.TornTail
               || shape == CorruptionShape.TruncatedFrameHeader
               || shape == CorruptionShape.CrcInvalidFrame;

        private static void Apply(CorruptionShape shape, string journal, string recordTag, int fieldCount)
        {
            switch (shape)
            {
                case CorruptionShape.TornTail:
                    JournalCorruptionHarness.AppendTornTail(journal);
                    break;
                case CorruptionShape.TruncatedFrameHeader:
                    JournalCorruptionHarness.AppendTruncatedFrameHeader(journal);
                    break;
                case CorruptionShape.CrcInvalidFrame:
                    JournalCorruptionHarness.AppendCrcInvalidFrame(journal);
                    break;
                case CorruptionShape.WellFramedGarbage:
                    JournalCorruptionHarness.AppendWellFramedGarbage(journal);
                    break;
                case CorruptionShape.WellFramedShortRecord:
                    // One field SHORT of what the handler requires: same tag, valid base64, wrong count.
                    JournalCorruptionHarness.AppendWellFramedShortRecord(journal, recordTag, fieldCount - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(shape));
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  Harness self-checks — the harness must speak the SHIPPED frame format,
        //  otherwise every test below would be corrupting nothing and passing vacuously.
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void Harness_frame_layout_matches_the_shipped_writers()
        {
            var env = NewLocalPolicyHandler(out var journal, NewStoneStore());
            Assert.Equal(LocalPolicyCommandOutcome.Applied,
                env.Handle(LocalPolicyCmd("op-frame", LocalBeneficiaryMode.Attuned)).Outcome);

            var frames = JournalCorruptionHarness.ReadIntactFrames(journal);
            Assert.NotEmpty(frames);
            foreach (var f in frames)
                Assert.StartsWith("LOCALPOLICYREC|", f, StringComparison.Ordinal);
            Assert.Equal(frames.Count, JournalCorruptionHarness.FrameStarts(journal).Count);
        }

        [Fact]
        public void Harness_corruption_shapes_actually_change_what_the_reader_can_see()
        {
            // Torn tail: intact frame count UNCHANGED (the tail is dropped) but the file grew — proving
            // the tail was really written and then correctly ignored, not silently never written.
            var h = NewLocalPolicyHandler(out var journal, NewStoneStore());
            h.Handle(LocalPolicyCmd("op-a", LocalBeneficiaryMode.Attuned));
            int before = JournalCorruptionHarness.FrameStarts(journal).Count;
            long lenBefore = new FileInfo(journal).Length;
            JournalCorruptionHarness.AppendTornTail(journal);
            Assert.True(new FileInfo(journal).Length > lenBefore);
            Assert.Equal(before, JournalCorruptionHarness.FrameStarts(journal).Count);

            // A CRC-invalid frame is NOT readable as a frame: the walk stops at it.
            JournalCorruptionHarness.AppendCrcInvalidFrame(journal);
            Assert.Equal(before, JournalCorruptionHarness.FrameStarts(journal).Count);

            // Well-framed garbage IS readable as a frame (it is structurally perfect) — the record
            // layer, not the frame layer, is what must reject it.
            var h2 = NewLocalPolicyHandler(out var journal2, NewStoneStore());
            h2.Handle(LocalPolicyCmd("op-b", LocalBeneficiaryMode.Attuned));
            int before2 = JournalCorruptionHarness.FrameStarts(journal2).Count;
            JournalCorruptionHarness.AppendWellFramedGarbage(journal2);
            Assert.Equal(before2 + 1, JournalCorruptionHarness.FrameStarts(journal2).Count);

            // ...and so is a well-framed SHORT record carrying the handler's own tag.
            JournalCorruptionHarness.AppendWellFramedShortRecord(journal2, "LOCALPOLICYREC", 10);
            Assert.Equal(before2 + 2, JournalCorruptionHarness.FrameStarts(journal2).Count);
        }

        // ════════════════════════════════════════════════════════════════════
        //  1/7 — LocalPolicyCommandHandler   (LOCALPOLICYREC, 11 fields)
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void LocalPolicy_journal_corruption(CorruptionShape shape)
        {
            string journal = TempJournal("localpolicy");

            // BEFORE: one committed policy change.
            var h1 = new LocalPolicyCommandHandler(journal, new PrincipalResolver(), NewStoneStore(),
                NewOwnerAuthority());
            var applied = h1.Handle(LocalPolicyCmd("op-before", LocalBeneficiaryMode.Private,
                new[] { _accountAtt.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, applied.Outcome);

            Apply(shape, journal, "LOCALPOLICYREC", 11);

            // AFTER: a further committed policy change written past the corruption.
            var midStore = NewStoneStore();
            var h2 = new LocalPolicyCommandHandler(journal, new PrincipalResolver(), midStore, NewOwnerAuthority());
            Assert.Equal(LocalPolicyCommandOutcome.Applied,
                h2.Handle(LocalPolicyCmd("op-after", LocalBeneficiaryMode.Attuned)).Outcome);

            // BOOT over the corrupted file — AT-TORN-FRAME-NO-THROW.
            var fresh = NewStoneStore();
            var booted = new LocalPolicyCommandHandler(journal, new PrincipalResolver(), fresh, NewOwnerAuthority());
            var recovered = fresh.GetStone(_stone)!;

            // AT-TORN-FRAME-PRIOR-RECORDS-SURVIVE: the op committed BEFORE the corruption rehydrated
            // into identical state, and replays as itself rather than re-applying.
            Assert.Equal(LocalPolicyCommandOutcome.Replayed,
                booted.Handle(LocalPolicyCmd("op-before", LocalBeneficiaryMode.Private,
                    new[] { _accountAtt.Value })).Outcome);

            if (IsFrameLayer(shape))
            {
                // Fail-CLOSED: the read truncated at the corruption, so the AFTER op is unreachable.
                // Its projection is absent and re-submitting it applies fresh rather than replaying.
                Assert.Equal(LocalBeneficiaryMode.Private, recovered.LocalPolicy.Mode);
                Assert.Equal(applied.PolicyRevision, recovered.LocalPolicy.Revision);
                Assert.Contains(_accountAtt.Value, recovered.LocalPolicy.AllowlistAccounts);
                Assert.Equal(LocalPolicyCommandOutcome.Applied,
                    booted.Handle(LocalPolicyCmd("op-after", LocalBeneficiaryMode.Attuned)).Outcome);
            }
            else
            {
                // Fail-HONEST: the malformed record is skipped individually; the AFTER op still replays.
                Assert.Equal(LocalBeneficiaryMode.Attuned, recovered.LocalPolicy.Mode);
                Assert.Equal(LocalPolicyCommandOutcome.Replayed,
                    booted.Handle(LocalPolicyCmd("op-after", LocalBeneficiaryMode.Attuned)).Outcome);
            }
        }

        private LocalPolicyCommandHandler NewLocalPolicyHandler(out string journal,
            InMemoryStoneAggregateStore stones)
        {
            journal = TempJournal("localpolicy");
            return new LocalPolicyCommandHandler(journal, new PrincipalResolver(), stones, NewOwnerAuthority());
        }

        private StubOwnerAuthority NewOwnerAuthority() => new StubOwnerAuthority(_account, _governor, _stone);

        private SetSettlementLocalPolicyCommand LocalPolicyCmd(string op, LocalBeneficiaryMode mode,
            IReadOnlyList<string>? allow = null)
            => new SetSettlementLocalPolicyCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(_account.Value, _governor.Value), default, mode, allow, null, null);

        // ════════════════════════════════════════════════════════════════════
        //  2/7 — FacetCommandHandler   (FACETREC, 12 fields)
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void Facet_journal_corruption(CorruptionShape shape)
        {
            string journal = TempJournal("facet");
            var chars = NewCharacterStore();
            var authority = NewAuthorityStore();

            // BEFORE: Cooking committed into the Profession Facet.
            var h1 = new FacetCommandHandler(journal, new PrincipalResolver(), NewEmptyFacetStoneStore(),
                chars, authority, new StubGovernorAuthorityPolicy());
            Assert.Equal(FacetCommandOutcome.Applied,
                h1.Handle(FacetCmd("op-before", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking")).Outcome);

            Apply(shape, journal, "FACETREC", 12);

            // AFTER: Warrior committed into the Martial Facet, past the corruption.
            var h2 = new FacetCommandHandler(journal, new PrincipalResolver(), NewEmptyFacetStoneStore(),
                chars, authority, new StubGovernorAuthorityPolicy());
            Assert.Equal(FacetCommandOutcome.Applied,
                h2.Handle(FacetCmd("op-after", HomesteadProgressionCatalog.MartialFacetId, "Warrior")).Outcome);

            var fresh = NewEmptyFacetStoneStore();
            var booted = new FacetCommandHandler(journal, new PrincipalResolver(), fresh, chars, authority,
                new StubGovernorAuthorityPolicy());

            Assert.Equal("Cooking", CommittedTree(fresh, HomesteadProgressionCatalog.ProfessionFacetId)?.Tree.Key);
            Assert.Equal(FacetCommandOutcome.Replayed,
                booted.Handle(FacetCmd("op-before", HomesteadProgressionCatalog.ProfessionFacetId, "Cooking")).Outcome);

            if (IsFrameLayer(shape))
            {
                Assert.Null(CommittedTree(fresh, HomesteadProgressionCatalog.MartialFacetId));
                Assert.Equal(FacetCommandOutcome.Applied,
                    booted.Handle(FacetCmd("op-after", HomesteadProgressionCatalog.MartialFacetId, "Warrior")).Outcome);
            }
            else
            {
                Assert.Equal("Warrior", CommittedTree(fresh, HomesteadProgressionCatalog.MartialFacetId)?.Tree.Key);
                Assert.Equal(FacetCommandOutcome.Replayed,
                    booted.Handle(FacetCmd("op-after", HomesteadProgressionCatalog.MartialFacetId, "Warrior")).Outcome);
            }
        }

        private CommittedTreeRecord? CommittedTree(InMemoryStoneAggregateStore store, string facetId)
        {
            foreach (var c in store.GetStone(_stone)!.CommittedTrees)
                if (c.FacetId == facetId) return c;
            return null;
        }

        private CommitTreeToFacetCommand FacetCmd(string op, string facetId, string treeKey)
            => new CommitTreeToFacetCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(_account.Value, _governor.Value), default,
                facetId, treeKey, 1, StoneFacetPalette.CurrentPaletteVersion, null);

        // ════════════════════════════════════════════════════════════════════
        //  3/7 — ActivityCommandHandler   (ACTIVITYREC, 13 fields)
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void Activity_journal_corruption(CorruptionShape shape)
        {
            string journal = TempJournal("activity");
            var stones = NewStoneStore();
            var authority = NewAuthorityStore();

            var h1 = new ActivityCommandHandler(journal, new PrincipalResolver(), stones, NewCharacterStore(),
                authority, new StubDevelopmentAuthority());
            Assert.Equal(ActivityCommandOutcome.Applied, h1.Handle(ActivityCmd("op-before", 5)).Outcome);

            Apply(shape, journal, "ACTIVITYREC", 13);

            var h2 = new ActivityCommandHandler(journal, new PrincipalResolver(), stones, NewCharacterStore(),
                authority, new StubDevelopmentAuthority());
            Assert.Equal(ActivityCommandOutcome.Applied, h2.Handle(ActivityCmd("op-after", 3)).Outcome);

            var freshChars = NewCharacterStore();
            var booted = new ActivityCommandHandler(journal, new PrincipalResolver(), stones, freshChars,
                authority, new StubDevelopmentAuthority());

            int bp = BondPower.BalanceAt(freshChars.GetCharacter(_account, _governor)!, _stone);

            // The pre-corruption credit is intact and idempotent — checked BEFORE any re-apply below.
            Assert.Equal(ActivityCommandOutcome.Replayed, booted.Handle(ActivityCmd("op-before", 5)).Outcome);
            Assert.Equal(bp, BondPower.BalanceAt(freshChars.GetCharacter(_account, _governor)!, _stone));

            if (IsFrameLayer(shape))
            {
                Assert.Equal(5, bp);   // only the pre-corruption credit replayed
                Assert.Equal(ActivityCommandOutcome.Applied, booted.Handle(ActivityCmd("op-after", 3)).Outcome);
            }
            else
            {
                Assert.Equal(8, bp);   // both credits replayed across the skipped malformed record
                Assert.Equal(ActivityCommandOutcome.Replayed, booted.Handle(ActivityCmd("op-after", 3)).Outcome);
                Assert.Equal(8, BondPower.BalanceAt(freshChars.GetCharacter(_account, _governor)!, _stone));
            }
        }

        private RecordAlignedActivityCommand ActivityCmd(string op, int award)
        {
            var adapter = new AlignedActivityAdapter();
            var evidence = new AlignedActivityEvidence(new OperationId(op), _stone,
                "activity.cook", 1, "CookedMeal", Cooking, award, serverAttributed: true);
            var admission = adapter.Admit(evidence,
                new AuthenticatedConnection(_account.Value, _governor.Value), default);
            Assert.True(admission.IsAdmitted);
            return admission.Command;
        }

        // ════════════════════════════════════════════════════════════════════
        //  4/7 — DevelopmentCommandHandler   (DEVELOPREC, 19 fields)
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void Development_journal_corruption(CorruptionShape shape)
        {
            string activityJournal = TempJournal("dev-activity");
            string developJournal = TempJournal("develop");
            var stones = NewStoneStore();
            var chars = NewCharacterStore();
            var authority = NewAuthorityStore();

            var activity = new ActivityCommandHandler(activityJournal, new PrincipalResolver(), stones, chars,
                authority, new StubDevelopmentAuthority());
            Assert.Equal(ActivityCommandOutcome.Applied, activity.Handle(ActivityCmd("op-credit", 10)).Outcome);

            var develop = NewDevelopHandler(developJournal, stones, chars, authority);
            Assert.Equal(DevelopmentCommandOutcome.Applied,
                develop.Handle(DevelopCmd("op-before", Cooking, FieldPrep, 1)).Outcome);

            Apply(shape, developJournal, "DEVELOPREC", 19);

            Assert.Equal(DevelopmentCommandOutcome.Applied,
                develop.Handle(DevelopCmd("op-after", Cooking, Savor, 1)).Outcome);

            var freshStones = NewStoneStore();
            var freshChars = NewCharacterStore();
            var bootedActivity = new ActivityCommandHandler(activityJournal, new PrincipalResolver(),
                freshStones, freshChars, authority, new StubDevelopmentAuthority());
            var booted = NewDevelopHandler(developJournal, freshStones, freshChars, authority);

            Assert.Equal(1, DevProgress(freshStones, FieldPrep));
            Assert.Equal(DevelopmentCommandOutcome.Replayed,
                booted.Handle(DevelopCmd("op-before", Cooking, FieldPrep, 1)).Outcome);

            if (IsFrameLayer(shape))
            {
                Assert.Equal(0, DevProgress(freshStones, Savor));
                Assert.Equal(DevelopmentCommandOutcome.Applied,
                    booted.Handle(DevelopCmd("op-after", Cooking, Savor, 1)).Outcome);
            }
            else
            {
                Assert.Equal(1, DevProgress(freshStones, Savor));
                Assert.Equal(DevelopmentCommandOutcome.Replayed,
                    booted.Handle(DevelopCmd("op-after", Cooking, Savor, 1)).Outcome);
            }
            _ = bootedActivity;
        }

        private int DevProgress(InMemoryStoneAggregateStore store, VersionedId node)
        {
            foreach (var n in store.GetStone(_stone)!.NodeDevelopment)
                if (n.Node.Key == node.Key) return n.BpProgress;
            return 0;
        }

        private DevelopmentCommandHandler NewDevelopHandler(string journal,
            InMemoryStoneAggregateStore stones, InMemoryCharacterAggregateStore chars,
            InMemoryAccountStoneAuthorityStore authority)
            => new DevelopmentCommandHandler(journal, new PrincipalResolver(), stones, chars, authority,
                new StubDevelopmentAuthority(), new HomesteadProgressionCatalog(), null);

        private ApplyBPToNodeCommand DevelopCmd(string op, VersionedId tree, VersionedId node, int amount)
            => new ApplyBPToNodeCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(_account.Value, _governor.Value), default,
                tree.Key, tree.Version, node.Key, node.Version, amount);

        // ════════════════════════════════════════════════════════════════════
        //  5/7 — PurchaseCommandHandler   (PURCHASEREC, 15 fields)
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void Purchase_journal_corruption(CorruptionShape shape)
        {
            var env = new PurchaseEnv(this);
            env.OfferBothCookingL1();

            Assert.Equal(PurchaseCommandOutcome.Applied,
                env.Purchase.Handle(env.PurchaseCmd("op-before", FieldPrep)).Outcome);

            Apply(shape, env.PurchaseJournal, "PURCHASEREC", 15);

            Assert.Equal(PurchaseCommandOutcome.Applied,
                env.Purchase.Handle(env.PurchaseCmd("op-after", IronStomach)).Outcome);

            var freshChars = NewCharacterStore();
            var booted = new PurchaseCommandHandler(env.PurchaseJournal, new PrincipalResolver(),
                env.Stones, freshChars, env.Authority, new HomesteadProgressionCatalog());

            Assert.Equal(1, PurchaseCount(freshChars, FieldPrep));
            Assert.Equal(PurchaseCommandOutcome.Replayed,
                booted.Handle(env.PurchaseCmd("op-before", FieldPrep)).Outcome);

            if (IsFrameLayer(shape))
            {
                Assert.Equal(0, PurchaseCount(freshChars, IronStomach));
                Assert.Equal(9, PersonalAp(freshChars));    // exactly one 1-AP debit replayed
                Assert.Equal(PurchaseCommandOutcome.Applied,
                    booted.Handle(env.PurchaseCmd("op-after", IronStomach)).Outcome);
            }
            else
            {
                Assert.Equal(1, PurchaseCount(freshChars, IronStomach));
                Assert.Equal(PurchaseCommandOutcome.Replayed,
                    booted.Handle(env.PurchaseCmd("op-after", IronStomach)).Outcome);
            }
        }

        private int PurchaseCount(InMemoryCharacterAggregateStore chars, VersionedId node)
        {
            int n = 0;
            foreach (var sr in chars.GetCharacter(_accountAtt, _attuned)!.StoneRecords)
                if (sr.StoneId.Equals(_stone))
                    foreach (var p in sr.Purchases)
                        if (p.Node.Key == node.Key) n++;
            return n;
        }

        private int PersonalAp(InMemoryCharacterAggregateStore chars)
        {
            foreach (var sr in chars.GetCharacter(_accountAtt, _attuned)!.StoneRecords)
                if (sr.StoneId.Equals(_stone)) return sr.PersonalAp;
            return 0;
        }

        // ════════════════════════════════════════════════════════════════════
        //  6/7 — WeaponDisciplineCommandHandler   (WEAPDISCREC, 14 fields)
        //  Lives in PurchaseCommands.cs alongside PurchaseCommandHandler and is the
        //  handler most easily missed when a fix is applied "to the purchase file".
        //  Its record is a PERMANENT, once-only player choice: there is no second
        //  chance to re-make it, so losing the record is unrecoverable data loss.
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void WeaponDiscipline_journal_corruption(CorruptionShape shape)
        {
            var env = new PurchaseEnv(this);
            env.OfferAndPurchaseWeaponDiscipline();

            string choiceJournal = TempJournal("choice");
            var provider = new SkillCapProvider();
            var pick = provider.Choices[0];

            var choice = new WeaponDisciplineCommandHandler(choiceJournal, new PrincipalResolver(),
                env.Stones, env.Characters, env.Authority, new SkillCapProvider());
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                choice.Handle(env.ChooseCmd("op-before", pick.ChoiceId)).Outcome);

            Apply(shape, choiceJournal, "WEAPDISCREC", 14);

            // BOOT over the corrupted file: purchase journal first, then the choice journal.
            var freshChars = NewCharacterStore();
            var rehydratedPurchase = new PurchaseCommandHandler(env.PurchaseJournal, new PrincipalResolver(),
                env.Stones, freshChars, env.Authority, new HomesteadProgressionCatalog());
            var booted = new WeaponDisciplineCommandHandler(choiceJournal, new PrincipalResolver(),
                env.Stones, freshChars, env.Authority, new SkillCapProvider());

            // The PERMANENT choice written BEFORE the corruption survived, exactly once, at full effect.
            var c = freshChars.GetCharacter(_accountAtt, _attuned)!;
            int choices = 0;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone)) choices += sr.SkillCapChoices.Count;
            Assert.Equal(1, choices);
            Assert.Equal(Math.Max(SkillCapProvider.VanillaBaselineCap, pick.CapValue),
                new SkillCapProvider().EffectiveCap(c, _stone, pick.TargetSkill));

            Assert.Equal(WeaponDisciplineCommandOutcome.Replayed,
                booted.Handle(env.ChooseCmd("op-before", pick.ChoiceId)).Outcome);
            _ = rehydratedPurchase;
        }

        // ════════════════════════════════════════════════════════════════════
        //  7/7 — RelationshipCommandHandler   (RELREC, 14/16 fields)
        //  ALREADY covered before this card (NiflheimRelationshipLifecycleTests +
        //  NiflheimT009L2ProgressionRemediationTests). Restated here through the SAME
        //  shared harness so all seven are asserted identically and a future regression
        //  cannot hide behind a differently-shaped older test.
        // ════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(CorruptionShape.TornTail)]
        [InlineData(CorruptionShape.TruncatedFrameHeader)]
        [InlineData(CorruptionShape.CrcInvalidFrame)]
        [InlineData(CorruptionShape.WellFramedGarbage)]
        [InlineData(CorruptionShape.WellFramedShortRecord)]
        public void Relationship_journal_corruption(CorruptionShape shape)
        {
            string journal = TempJournal("relationship");
            var families = new StubFamilyResolver();
            families.Set(_stone, "Settlement", "Homestead");

            var chars = NewBareCharacterStore();
            var authority = new InMemoryAccountStoneAuthorityStore();
            var h1 = new RelationshipCommandHandler(journal, new PrincipalResolver(), chars, authority,
                families, new StubBondAuthorityPolicy());
            Assert.Equal(RelationshipCommandOutcome.Applied,
                h1.Handle(BondCmd("op-before", "rel-bond-129")).Outcome);

            // RELREC carries 16 fields on the RD-T004 path; the legacy 14-field shape is also accepted,
            // so the short-record shape must be one short of the SMALLER accepted count to be malformed.
            Apply(shape, journal, "RELREC", 14);

            // AFTER: an Attunement on the OTHER account, past the corruption.
            var chars2 = NewBareCharacterStore();
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            var h2 = new RelationshipCommandHandler(journal, new PrincipalResolver(), chars2, authority2,
                families, new StubBondAuthorityPolicy());
            Assert.Equal(RelationshipCommandOutcome.Applied,
                h2.Handle(AttuneCmd("op-after", "rel-att-129")).Outcome);

            var freshChars = NewBareCharacterStore();
            var freshAuthority = new InMemoryAccountStoneAuthorityStore();
            var booted = new RelationshipCommandHandler(journal, new PrincipalResolver(), freshChars,
                freshAuthority, families, new StubBondAuthorityPolicy());

            var idx = freshAuthority.GetAuthority(_account, _stone);
            Assert.False(idx.IsVacant);
            var res = Assert.Single(idx.Reservations);
            Assert.Equal(RelationshipKind.Bond, res.Kind);
            Assert.Equal("rel-bond-129", res.RelationshipId);
            Assert.Equal(RelationshipCommandOutcome.Replayed,
                booted.Handle(BondCmd("op-before", "rel-bond-129")).Outcome);

            var attIdx = freshAuthority.GetAuthority(_accountAtt, _stone);
            if (IsFrameLayer(shape))
            {
                Assert.True(attIdx.IsVacant);
                Assert.Equal(RelationshipCommandOutcome.Applied,
                    booted.Handle(AttuneCmd("op-after", "rel-att-129")).Outcome);
            }
            else
            {
                Assert.False(attIdx.IsVacant);
                Assert.Equal(RelationshipCommandOutcome.Replayed,
                    booted.Handle(AttuneCmd("op-after", "rel-att-129")).Outcome);
            }
        }

        private RelationshipCommand BondCmd(string op, string relId)
            => new RelationshipCommand(new OperationId(op), RelationshipCommandType.CreateBond, _stone,
                new AuthenticatedConnection(_account.Value, _governor.Value), default,
                relId, responsibilityRange: "Homestead:All", ownerGovernorRole: "Owner");

        private RelationshipCommand AttuneCmd(string op, string relId)
            => new RelationshipCommand(new OperationId(op), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_accountAtt.Value, _attuned.Value), default, relId);

        // ════════════════════════════════════════════════════════════════════
        //  Fixtures
        // ════════════════════════════════════════════════════════════════════

        private InMemoryStoneAggregateStore NewStoneStore()
        {
            var stones = new InMemoryStoneAggregateStore();
            stones.PutStone(new StoneProgressionAggregate(_stone, revision: 10,
                historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: new[]
                {
                    new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                        Cooking, "seed-commit-cook", _governor.Value, 1, 0),
                    new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                        Warrior, "seed-commit-war", _governor.Value, 1, 0),
                },
                nodeDevelopment: null));
            return stones;
        }

        /// <summary>A Stone store with EMPTY Facets, for the Facet handler (which must find the Facet
        /// unoccupied before it can commit into it).</summary>
        private InMemoryStoneAggregateStore NewEmptyFacetStoneStore()
        {
            var stones = new InMemoryStoneAggregateStore();
            stones.PutStone(new StoneProgressionAggregate(_stone, revision: 10,
                historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: null));
            return stones;
        }

        private InMemoryCharacterAggregateStore NewCharacterStore()
        {
            var chars = new InMemoryCharacterAggregateStore();
            chars.PutCharacter(BuildGovernor(_account, _governor, personalBp: 0));
            chars.PutCharacter(BuildAttuned(_accountAtt, _attuned, personalAp: 10));
            return chars;
        }

        /// <summary>Characters with NO relationships — RelationshipCommandHandler must create them
        /// itself, so seeding one would make the committed op unrepresentative.</summary>
        private InMemoryCharacterAggregateStore NewBareCharacterStore()
        {
            var chars = new InMemoryCharacterAggregateStore();
            chars.PutCharacter(BuildBareCharacter(_account, _governor));
            chars.PutCharacter(BuildBareCharacter(_accountAtt, _attuned));
            return chars;
        }

        private InMemoryAccountStoneAuthorityStore NewAuthorityStore()
        {
            var authority = new InMemoryAccountStoneAuthorityStore();
            authority.ApplyAuthorityProjection("seed-bond",
                AccountStoneAuthorityIndex.Vacant(_account, _stone).WithReservationAdded(
                    new AuthorityReservation(_governor, RelationshipKind.Bond, BondRelId, "relreceipt:seed-bond"), 1));
            authority.ApplyAuthorityProjection("seed-att",
                AccountStoneAuthorityIndex.Vacant(_accountAtt, _stone).WithReservationAdded(
                    new AuthorityReservation(_attuned, RelationshipKind.Attunement, AttRelId, "relreceipt:seed-att"), 1));
            return authority;
        }

        private CharacterProgressionAggregate BuildGovernor(AccountId account, CharacterId character, int personalBp)
        {
            var bond = new RelationshipRecord(BondRelId, RelationshipKind.Bond, RelationshipStatus.Active,
                "Homestead:All", "Governor", "relreceipt:seed-bond", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, 0, 0, personalBp,
                purchases: null, relationships: new[] { bond });
            return new CharacterProgressionAggregate(account, character, "corrupt-129/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private CharacterProgressionAggregate BuildAttuned(AccountId account, CharacterId character, int personalAp)
        {
            var att = new RelationshipRecord(AttRelId, RelationshipKind.Attunement, RelationshipStatus.Active,
                string.Empty, string.Empty, "relreceipt:seed-att", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, personalAp, personalAp, 0,
                purchases: null, relationships: new[] { att });
            return new CharacterProgressionAggregate(account, character, "corrupt-129/trailborne",
                revision: 1, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private CharacterProgressionAggregate BuildBareCharacter(AccountId account, CharacterId character)
        {
            var stoneRecord = new CharacterStoneRecord(_stone, 0, 0, 0,
                purchases: null);
            return new CharacterProgressionAggregate(account, character, "corrupt-129/trailborne",
                revision: 0, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        /// <summary>The purchase / weapon-discipline pipeline: BP credit -> development to Offered ->
        /// purchase. Assembled once so both the Purchase and WeaponDiscipline cases share it.</summary>
        private sealed class PurchaseEnv
        {
            private readonly NiflheimJournalCorruptionAllHandlersTests _t;

            internal readonly string ActivityJournal;
            internal readonly string DevelopJournal;
            internal readonly string PurchaseJournal;
            internal readonly InMemoryStoneAggregateStore Stones;
            internal readonly InMemoryCharacterAggregateStore Characters;
            internal readonly InMemoryAccountStoneAuthorityStore Authority;
            internal readonly ActivityCommandHandler Activity;
            internal readonly DevelopmentCommandHandler Develop;
            internal readonly PurchaseCommandHandler Purchase;

            internal PurchaseEnv(NiflheimJournalCorruptionAllHandlersTests t)
            {
                _t = t;
                ActivityJournal = t.TempJournal("pe-activity");
                DevelopJournal = t.TempJournal("pe-develop");
                PurchaseJournal = t.TempJournal("pe-purchase");
                Stones = t.NewStoneStore();
                Characters = t.NewCharacterStore();
                Authority = t.NewAuthorityStore();

                Activity = new ActivityCommandHandler(ActivityJournal, new PrincipalResolver(), Stones,
                    Characters, Authority, new StubDevelopmentAuthority());
                Develop = new DevelopmentCommandHandler(DevelopJournal, new PrincipalResolver(), Stones,
                    Characters, Authority, new StubDevelopmentAuthority(),
                    new HomesteadProgressionCatalog(), null);
                Purchase = new PurchaseCommandHandler(PurchaseJournal, new PrincipalResolver(), Stones,
                    Characters, Authority, new HomesteadProgressionCatalog());
            }

            internal void CreditBp(int amount, string op, VersionedId tree)
            {
                var adapter = new AlignedActivityAdapter();
                var evidence = new AlignedActivityEvidence(new OperationId(op), _t._stone,
                    "activity.any", 1, "CookedMeal", tree, amount, serverAttributed: true);
                var admission = adapter.Admit(evidence,
                    new AuthenticatedConnection(_t._account.Value, _t._governor.Value), default);
                Assert.True(admission.IsAdmitted);
                Assert.Equal(ActivityCommandOutcome.Applied, Activity.Handle(admission.Command).Outcome);
            }

            /// <summary>The successive-unlock cost curve escalates per already-developed node, so apply
            /// 1 BP per op until the node completes.</summary>
            internal DevelopmentCommandResult DevelopToComplete(string op, VersionedId tree, VersionedId node)
            {
                DevelopmentCommandResult last = default;
                for (int i = 0; i < 16; i++)
                {
                    last = Develop.Handle(new ApplyBPToNodeCommand(new OperationId(op + "-" + i), _t._stone,
                        new AuthenticatedConnection(_t._account.Value, _t._governor.Value), default,
                        tree.Key, tree.Version, node.Key, node.Version, 1));
                    Assert.Equal(DevelopmentCommandOutcome.Applied, last.Outcome);
                    if (last.NodeCompleted) break;
                }
                return last;
            }

            internal void OfferBothCookingL1()
            {
                CreditBp(10, "op-credit", Cooking);
                Assert.True(DevelopToComplete("op-dev-fieldprep", Cooking, FieldPrep).NodeOffered);
                Assert.True(DevelopToComplete("op-dev-ironstomach", Cooking, IronStomach).NodeOffered);
            }

            internal void OfferAndPurchaseWeaponDiscipline()
            {
                CreditBp(10, "op-credit-war", Warrior);
                Assert.True(DevelopToComplete("op-dev-weapdisc", Warrior, WeaponDiscipline).NodeOffered);
                Assert.Equal(PurchaseCommandOutcome.Applied,
                    Purchase.Handle(PurchaseCmd("op-buy-weapdisc", WeaponDiscipline, Warrior)).Outcome);
            }

            internal PurchaseNodeCommand PurchaseCmd(string op, VersionedId node, VersionedId? tree = null)
            {
                var t = tree ?? Cooking;
                return new PurchaseNodeCommand(new OperationId(op), _t._stone,
                    new AuthenticatedConnection(_t._accountAtt.Value, _t._attuned.Value), default,
                    t.Key, t.Version, node.Key, node.Version,
                    string.Empty, 0, PurchasePaymentSource.PersonalAp);
            }

            internal ChooseWeaponDisciplineSkillCommand ChooseCmd(string op, string choiceId)
                => new ChooseWeaponDisciplineSkillCommand(new OperationId(op), _t._stone,
                    new AuthenticatedConnection(_t._accountAtt.Value, _t._attuned.Value), default,
                    WeaponDiscipline.Key, WeaponDiscipline.Version, choiceId,
                    SkillCapProvider.CurrentCatalogVersion, null, null);
        }

        // ── Test doubles (mirrors of the ones in the per-handler fixtures) ──

        private sealed class StubDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree)
                => string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
                   && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
                   && !tree.IsNone;
        }

        private sealed class StubGovernorAuthorityPolicy : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category)
                => string.Equals(responsibilityRange, "Homestead:All", StringComparison.Ordinal)
                   && string.Equals(ownerGovernorRole, "Governor", StringComparison.Ordinal)
                   && category != FacetCategory.None;
        }

        private sealed class StubOwnerAuthority : IHomesteadOwnerAuthority
        {
            private readonly AccountId _owner;
            private readonly CharacterId _ownerChar;
            private readonly StoneId _stone;

            internal StubOwnerAuthority(AccountId owner, CharacterId ownerChar, StoneId stone)
            {
                _owner = owner; _ownerChar = ownerChar; _stone = stone;
            }

            public bool IsOwner(AuthoritativePrincipal principal, StoneId stoneId)
                => stoneId.Equals(_stone) && principal.Account.Equals(_owner)
                   && principal.Character.Equals(_ownerChar);
        }

        private sealed class StubFamilyResolver : IStoneFamilyResolver
        {
            private readonly Dictionary<string, string[]> _map =
                new Dictionary<string, string[]>(StringComparer.Ordinal);

            internal void Set(StoneId stone, string family, string variant)
                => _map[stone.Value] = new[] { family, variant };

            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (_map.TryGetValue(stoneId.Value, out var v)) { family = v[0]; variant = v[1]; return true; }
                family = variant = string.Empty;
                return false;
            }
        }

        private sealed class StubBondAuthorityPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = string.Empty;
                grantedRole = string.Empty;
                if (!string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal))
                    return false;
                grantedRange = requestedResponsibilityRange;
                grantedRole = "Governor";
                return true;
            }
        }
    }
}
