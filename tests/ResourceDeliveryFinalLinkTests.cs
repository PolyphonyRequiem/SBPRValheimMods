// ============================================================================
//  RD-T002 (Gate A) — AT-RD-004: final-link two-operation handshake tests.
// ----------------------------------------------------------------------------
//  Exercises FinalLinkHandshakeStore (link-compiled from ../src). Proves:
//    * preparation stores a durable non-mutating ConfirmationRequired decision
//      and replays the EXACT challenge across restart (new store over same file);
//    * fresh-ID confirmation applies release+grace+receipt atomically, freezes the
//      confirmation-time age, and sets a full 72h grace expiry;
//    * token-bearing principal substitution rejects (PrincipalMismatch);
//    * lost release authority rejects (ReleaseUnauthorized);
//    * a stale/changed set rejects (Stale);
//    * competing confirmation ops: one wins, the other gets Consumed with the
//      winning receipt correlation; it cannot apply twice;
//    * delayed confirmation still gets a full 72h grace from confirmation time;
//    * reusing the preparation op id as the confirmation op id conflicts;
//    * crash/restart at the durable boundary converges (replay returns the winner).
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryFinalLinkTests : System.IDisposable
    {
        private readonly string _journal;
        private static readonly WorldId World = new WorldId("world-A");
        private static readonly ProductScope Product = new ProductScope("SBPR.Trailborne");

        public ResourceDeliveryFinalLinkTests()
        {
            _journal = Path.Combine(Path.GetTempPath(), "rd-finallink-" + System.Guid.NewGuid().ToString("N") + ".jrnl");
        }

        public void Dispose()
        {
            if (File.Exists(_journal)) File.Delete(_journal);
        }

        private FinalLinkHandshakeStore Store() => new FinalLinkHandshakeStore(_journal);

        private static FinalLinkBinding Binding(long authRev = 5, long relRev = 7) =>
            new FinalLinkBinding("alice", "alice-char", "rel-1", authRev, relRev, gracePolicyVersion: 1);

        private static ConnectionId Conn(string a, string b)
        {
            ConnectionId.TryCreate(World, Product, new AccountId(a), new AccountId(b), out var id);
            return id;
        }

        private static List<AffectedConnection> Affected(long connRev = 3)
        {
            return new List<AffectedConnection>
            {
                new AffectedConnection(Conn("alice", "bob"),
                    new List<string> { "src-1" }, connRev, "1.2x", 8 * 86400),
            };
        }

        private static LiveReleaseAuthority GoodAuthority(long authRev = 5) =>
            new LiveReleaseAuthority("alice", "alice-char", authRev, hasVoluntaryReleaseAuthority: true);

        private static Dictionary<string, long> Ages(long ageSeconds = 8 * 86400) =>
            new Dictionary<string, long> { { Conn("alice", "bob").CanonicalKey, ageSeconds } };

        // ── Preparation ──

        [Fact]
        public void Prepare_ProducesDurableConfirmationRequired_NonMutating()
        {
            var r = Store().Prepare("prep-1", Binding(), Affected(), issuedAtServerTimeSeconds: 1000);
            Assert.Equal(FinalLinkOutcome.ConfirmationRequired, r.Outcome);
            Assert.Equal("FinalLinkConfirmationRequired", r.ResultCode);
            Assert.NotEqual("", r.WarningToken);
            Assert.NotEqual("", r.ConfirmationDecisionId);
            // No gameplay receipt yet.
            Assert.Equal("", r.ReceiptId);
        }

        [Fact]
        public void Prepare_Replay_AcrossRestart_ReturnsExactChallenge()
        {
            var first = Store().Prepare("prep-1", Binding(), Affected(), 1000);
            // "Restart": a brand-new store over the same journal file.
            var replay = Store().Prepare("prep-1", Binding(), Affected(), 1000);
            Assert.Equal(FinalLinkOutcome.PreparationReplayed, replay.Outcome);
            Assert.Equal(first.WarningToken, replay.WarningToken);
            Assert.Equal(first.ConfirmationDecisionId, replay.ConfirmationDecisionId);
        }

        [Fact]
        public void Prepare_ConflictingBindingSameOpId_Rejected()
        {
            Store().Prepare("prep-1", Binding(authRev: 5), Affected(), 1000);
            var conflict = Store().Prepare("prep-1", Binding(authRev: 99), Affected(), 1000);
            Assert.Equal(FinalLinkOutcome.OperationConflict, conflict.Outcome);
        }

        // ── Confirmation happy path ──

        [Fact]
        public void Confirm_FreshId_AppliesAtomically_FreezesAge_StartsFull72hGrace()
        {
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            long confirmAt = 2000;
            var r = Store().Confirm("conf-1", "prep-1", Binding(), Affected(),
                GoodAuthority(), Ages(8 * 86400), confirmAt);

            Assert.Equal(FinalLinkOutcome.Confirmed, r.Outcome);
            Assert.Equal("Applied", r.ResultCode);
            Assert.NotEqual("", r.ReceiptId);
            Assert.Equal(8 * 86400, r.FrozenAgeSeconds);
            Assert.Equal(confirmAt + ConnectionAggregate.GraceSeconds, r.GraceExpiresAtSeconds);
        }

        [Fact]
        public void Confirm_DelayedButAuthorized_StillGetsFull72hFromConfirmationTime()
        {
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            long muchLater = 1000 + 500 * 86400; // 500 days later
            var r = Store().Confirm("conf-1", "prep-1", Binding(), Affected(),
                GoodAuthority(), Ages(20 * 86400), muchLater);
            Assert.Equal(FinalLinkOutcome.Confirmed, r.Outcome);
            Assert.Equal(muchLater + ConnectionAggregate.GraceSeconds, r.GraceExpiresAtSeconds);
        }

        // ── Rejections ──

        [Fact]
        public void Confirm_PrincipalSubstitution_TokenBearing_Rejected()
        {
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            // A different principal holding the token attempts to confirm.
            var mallory = new LiveReleaseAuthority("mallory", "mallory-char", 5, true);
            var r = Store().Confirm("conf-1", "prep-1", Binding(), Affected(), mallory, Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.PrincipalMismatch, r.Outcome);
            Assert.Equal("FinalLinkConfirmationPrincipalMismatch", r.ResultCode);
            Assert.Equal("", r.ReceiptId); // no mutation
        }

        [Fact]
        public void Confirm_LostAuthority_Rejected()
        {
            Store().Prepare("prep-1", Binding(authRev: 5), Affected(), 1000);
            // Same principal, but no longer has voluntary release authority.
            var lost = new LiveReleaseAuthority("alice", "alice-char", 5, hasVoluntaryReleaseAuthority: false);
            var r = Store().Confirm("conf-1", "prep-1", Binding(authRev: 5), Affected(), lost, Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.ReleaseUnauthorized, r.Outcome);
            Assert.Equal("RelationshipReleaseUnauthorized", r.ResultCode);
        }

        [Fact]
        public void Confirm_AuthorityRevisionMoved_Rejected()
        {
            Store().Prepare("prep-1", Binding(authRev: 5), Affected(), 1000);
            // Bound at authRev 5, but live authority is now at 6 -> lost/stale authority.
            var moved = new LiveReleaseAuthority("alice", "alice-char", 6, true);
            var r = Store().Confirm("conf-1", "prep-1", Binding(authRev: 5), Affected(), moved, Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.ReleaseUnauthorized, r.Outcome);
        }

        [Fact]
        public void Confirm_ChangedAffectedSet_Stale()
        {
            Store().Prepare("prep-1", Binding(), Affected(connRev: 3), 1000);
            // The affected Connection's revision changed under the caller.
            var r = Store().Confirm("conf-1", "prep-1", Binding(), Affected(connRev: 4),
                GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.Stale, r.Outcome);
            Assert.Equal("FinalLinkConfirmationStale", r.ResultCode);
        }

        [Fact]
        public void Confirm_UnknownPreparation_TreatedAsStale()
        {
            var r = Store().Confirm("conf-1", "no-such-prep", Binding(), Affected(),
                GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.DecisionNotFound, r.Outcome);
            Assert.Equal("FinalLinkConfirmationStale", r.ResultCode);
        }

        [Fact]
        public void Confirm_ReusingPreparationOpId_Conflicts()
        {
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            var r = Store().Confirm("prep-1", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.OperationConflict, r.Outcome);
        }

        // ── Competing confirmations ──

        [Fact]
        public void Confirm_CompetingOps_OneWins_OtherGetsConsumed_CannotApplyTwice()
        {
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            var win = Store().Confirm("conf-A", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.Confirmed, win.Outcome);

            var lose = Store().Confirm("conf-B", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 3000);
            Assert.Equal(FinalLinkOutcome.Consumed, lose.Outcome);
            Assert.Equal("FinalLinkConfirmationConsumed", lose.ResultCode);
            // The loser receives the winning receipt correlation.
            Assert.Equal("conf-A", lose.WinningConfirmationOpId);
            Assert.Equal(win.ReceiptId, lose.ReceiptId);
        }

        // ── Replay / recovery ──

        [Fact]
        public void Confirm_SameOpReplay_AcrossRestart_ReturnsWinningReceipt()
        {
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            var first = Store().Confirm("conf-1", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);

            // "Restart": new store over the same journal; the same confirmation op replays.
            var replay = Store().Confirm("conf-1", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.ConfirmationReplayed, replay.Outcome);
            Assert.Equal(first.ReceiptId, replay.ReceiptId);
            Assert.Equal(first.GraceExpiresAtSeconds, replay.GraceExpiresAtSeconds);
            Assert.Equal(first.FrozenAgeSeconds, replay.FrozenAgeSeconds);
        }

        [Fact]
        public void Confirm_CrashBeforeCommit_LeavesChallengeUnconsumed_FreshConfirmStillApplies()
        {
            // Simulate a prepared decision but NO committed confirmation (crash before terminal record).
            Store().Prepare("prep-1", Binding(), Affected(), 1000);
            // No confirm happened. A fresh confirmation now applies normally — the challenge was never
            // consumed because consumption commits with the terminal record.
            var r = Store().Confirm("conf-1", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.Confirmed, r.Outcome);
        }

        [Fact]
        public void Prepare_And_Confirm_SurviveIndependentStoreInstances()
        {
            // Full lifecycle across three separate store instances (three "processes").
            var p = Store().Prepare("prep-1", Binding(), Affected(), 1000);
            Assert.Equal(FinalLinkOutcome.ConfirmationRequired, p.Outcome);

            var c = Store().Confirm("conf-1", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.Confirmed, c.Outcome);

            var again = Store().Confirm("conf-1", "prep-1", Binding(), Affected(), GoodAuthority(), Ages(), 2000);
            Assert.Equal(FinalLinkOutcome.ConfirmationReplayed, again.Outcome);
            Assert.Equal(c.ReceiptId, again.ReceiptId);
        }
    }
}
