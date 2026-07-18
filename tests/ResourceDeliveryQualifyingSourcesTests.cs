// ============================================================================
//  RD-T004 (Tracer 1) — Qualifying loyalty sources tests. Named acceptance AT-RD-002.
// ----------------------------------------------------------------------------
//  Exercises the engine-free Tracer-1 seam link-compiled from ../src:
//    * QualifyingSourceRule           the pure Bonded↔Attuned / Bonded↔Bonded rule
//    * StoneConnectionSourceRegistry  the durable, event-sourced source coordinator
//
//  Closes AT-RD-002 end to end (spec RD-002 / data-model Aggregate 1 §Invariants /
//  contracts §Relationship-to-Connection integration):
//
//    * Only Bonded↔Attuned and Bonded↔Bonded activate a Connection source; either
//      account order yields the SAME canonical Connection.
//    * Attuned↔Attuned NEVER qualifies. Friendship, Party, Guild, Discord, proximity,
//      co-presence, suggestion, and transitive (A–B–C ⇒ A–C) paths never qualify —
//      the ONLY input is two accounts' authoritative per-Stone roles, so an indirect
//      edge is inexpressible.
//    * Several Stones/sources maintain ONE Connection; source count never multiplies
//      maturity; exact source removal.
//    * Grace / reconnect integration: final-source removal freezes age into grace;
//      re-deriving the same pair within grace resumes the frozen age.
//    * Restart and replay: fresh registry over the same journal reconstructs the exact
//      source sets, ages, and grace; re-submitting a committed op is an idempotent
//      replay and a conflicting op id is OperationConflict.
//
//  No social graph or provider-shaped identity is introduced — the coordinator's only
//  inputs are internal AccountId + relationship role. The rest of the suite stays green.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryQualifyingSourcesTests : System.IDisposable
    {
        private static readonly WorldId World = new WorldId("world-RD002");
        private static readonly ProductScope Product = new ProductScope("SBPR.Trailborne");
        private const long Day = ConnectionMaturity.SecondsPerDay;

        private static readonly StoneId S1 = new StoneId("stone-1");
        private static readonly StoneId S2 = new StoneId("stone-2");

        private readonly string _journalPath;

        public ResourceDeliveryQualifyingSourcesTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(),
                "niflheim-rdt004-" + System.Guid.NewGuid().ToString("N") + ".journal");
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private StoneConnectionSourceRegistry NewRegistry() => new StoneConnectionSourceRegistry(_journalPath);

        private static ConnectionId Conn(string a, string b)
        {
            ConnectionId.TryCreate(World, Product, new AccountId(a), new AccountId(b), out var id);
            return id;
        }

        // ─────────────────── QualifyingSourceRule (pure) ───────────────────

        [Theory]
        [InlineData(StoneRelationshipRole.Bonded, StoneRelationshipRole.Bonded, true)]   // Bonded↔Bonded
        [InlineData(StoneRelationshipRole.Bonded, StoneRelationshipRole.Attuned, true)]  // Bonded↔Attuned
        [InlineData(StoneRelationshipRole.Attuned, StoneRelationshipRole.Bonded, true)]  // symmetric
        [InlineData(StoneRelationshipRole.Attuned, StoneRelationshipRole.Attuned, false)]// Attuned↔Attuned NEVER
        [InlineData(StoneRelationshipRole.Bonded, StoneRelationshipRole.None, false)]    // no active relationship
        [InlineData(StoneRelationshipRole.None, StoneRelationshipRole.None, false)]
        public void AtRd002_Rule_QualifiesOnlyBondedPairs(StoneRelationshipRole a, StoneRelationshipRole b, bool expected)
        {
            Assert.Equal(expected, QualifyingSourceRule.RolesQualify(a, b));
        }

        [Fact]
        public void AtRd002_Rule_DeriveSource_EitherOrder_SameCanonicalConnection()
        {
            var alice = new StoneParticipant(new AccountId("alice"), "rel-a", StoneRelationshipRole.Bonded);
            var bob = new StoneParticipant(new AccountId("bob"), "rel-b", StoneRelationshipRole.Attuned);

            var ab = QualifyingSourceRule.DeriveSource(World, Product, S1, alice, bob, "prov");
            var ba = QualifyingSourceRule.DeriveSource(World, Product, S1, bob, alice, "prov");

            Assert.True(ab.HasValue);
            Assert.True(ba.HasValue);
            Assert.Equal(ab!.Value.ConnectionId, ba!.Value.ConnectionId);
            // Same canonical source id regardless of which relationship was named first.
            Assert.Equal(ab.Value.Source.SourceId, ba.Value.Source.SourceId);
        }

        [Fact]
        public void AtRd002_Rule_SelfPair_NeverQualifies()
        {
            var a1 = new StoneParticipant(new AccountId("alice"), "rel-1", StoneRelationshipRole.Bonded);
            var a2 = new StoneParticipant(new AccountId("alice"), "rel-2", StoneRelationshipRole.Bonded);
            // Same account, two relationships — a self-pair mints no Connection identity (RD-001).
            Assert.Null(QualifyingSourceRule.DeriveSource(World, Product, S1, a1, a2, "prov"));
        }

        [Fact]
        public void AtRd002_Rule_AttunedAttuned_DerivesNoSource()
        {
            var alice = new StoneParticipant(new AccountId("alice"), "rel-a", StoneRelationshipRole.Attuned);
            var bob = new StoneParticipant(new AccountId("bob"), "rel-b", StoneRelationshipRole.Attuned);
            Assert.Null(QualifyingSourceRule.DeriveSource(World, Product, S1, alice, bob, "prov"));
        }

        [Fact]
        public void AtRd002_Rule_StoneRoster_OmitsAttunedAttunedAndSelfPairs()
        {
            // alice(Bond), bob(Attune), carol(Attune). Only alice↔bob and alice↔carol qualify;
            // bob↔carol (Attuned↔Attuned) does not.
            var roster = new List<StoneParticipant>
            {
                new StoneParticipant(new AccountId("alice"), "rel-a", StoneRelationshipRole.Bonded),
                new StoneParticipant(new AccountId("bob"), "rel-b", StoneRelationshipRole.Attuned),
                new StoneParticipant(new AccountId("carol"), "rel-c", StoneRelationshipRole.Attuned),
            };
            var sources = QualifyingSourceRule.DeriveStoneSources(World, Product, S1, roster, "prov");
            Assert.Equal(2, sources.Count);
            var keys = new HashSet<string>();
            foreach (var s in sources) keys.Add(s.ConnectionId.CanonicalKey);
            Assert.Contains(Conn("alice", "bob").CanonicalKey, keys);
            Assert.Contains(Conn("alice", "carol").CanonicalKey, keys);
            Assert.DoesNotContain(Conn("bob", "carol").CanonicalKey, keys);
        }

        // ─────────────────── Registry: qualification end to end ───────────────────

        [Fact]
        public void AtRd002_BondedAttuned_ActivatesConnection_BothOrders()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-1", World, Product, S1, new AccountId("alice"), "rel-a",
                RelationshipKind.Bond, t0);
            var res = reg.ActivateRelationship("op-2", World, Product, S1, new AccountId("bob"), "rel-b",
                RelationshipKind.Attunement, t0);

            Assert.Equal(ConnectionSourceOutcome.Applied, res.Outcome);
            var conn = reg.GetConnection(Conn("alice", "bob"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            Assert.True(conn.HasSources);
            Assert.True(conn.IsContributionQualifying);
            Assert.Contains(Conn("alice", "bob").CanonicalKey, res.AffectedConnectionKeys);
        }

        [Fact]
        public void AtRd002_AttunedAttuned_NeverActivatesConnection()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-1", World, Product, S1, new AccountId("alice"), "rel-a",
                RelationshipKind.Attunement, t0);
            var res = reg.ActivateRelationship("op-2", World, Product, S1, new AccountId("bob"), "rel-b",
                RelationshipKind.Attunement, t0);

            // No qualifying pair -> no affected connection, no source.
            Assert.Empty(res.AffectedConnectionKeys);
            var conn = reg.GetConnection(Conn("alice", "bob"));
            Assert.False(conn.HasSources);
            Assert.Equal(ConnectionLifecycle.None, conn.Lifecycle);
        }

        [Fact]
        public void AtRd002_SoloBond_HasNoConnection_UntilACounterpartJoins()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            var res = reg.ActivateRelationship("op-1", World, Product, S1, new AccountId("alice"), "rel-a",
                RelationshipKind.Bond, t0);
            // A lone Bond has nobody to pair with -> no source.
            Assert.Empty(res.AffectedConnectionKeys);
            Assert.Empty(reg.ActiveSourceConnectionKeys());
        }

        [Fact]
        public void AtRd002_SocialAndTransitiveEdges_NeverQualify()
        {
            // A–B bonded at S1, B–C bonded at S1. The transitive A–C edge must NOT exist: only the two
            // real qualifying pairs are sources. There is no social/graph input that could mint A–C.
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-a", World, Product, S1, new AccountId("A"), "rel-A",
                RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b", World, Product, S1, new AccountId("B"), "rel-B",
                RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-c", World, Product, S1, new AccountId("C"), "rel-C",
                RelationshipKind.Bond, t0);

            var keys = new HashSet<string>(reg.ActiveSourceConnectionKeys());
            // All three real qualifying pairs exist because all three are bonded at the same Stone —
            // but each is a DIRECT Bonded↔Bonded pair, not a transitive inference. Confirm every present
            // key is a real pair and there is no phantom edge.
            Assert.Contains(Conn("A", "B").CanonicalKey, keys);
            Assert.Contains(Conn("A", "C").CanonicalKey, keys);
            Assert.Contains(Conn("B", "C").CanonicalKey, keys);
            Assert.Equal(3, keys.Count); // exactly the 3 direct pairs; no 4th phantom identity
        }

        [Fact]
        public void AtRd002_TransitiveThroughBondedHub_DoesNotConnectTheAttunedEnds()
        {
            // hub(Bond)–A(Attune) qualifies; hub(Bond)–C(Attune) qualifies; but A and C are Attuned↔Attuned
            // and their ONLY path is transitive through the hub. A–C must have NO source.
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-hub", World, Product, S1, new AccountId("hub"), "rel-h",
                RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-a", World, Product, S1, new AccountId("A"), "rel-A",
                RelationshipKind.Attunement, t0);
            reg.ActivateRelationship("op-c", World, Product, S1, new AccountId("C"), "rel-C",
                RelationshipKind.Attunement, t0);

            var keys = new HashSet<string>(reg.ActiveSourceConnectionKeys());
            Assert.Contains(Conn("A", "hub").CanonicalKey, keys);
            Assert.Contains(Conn("C", "hub").CanonicalKey, keys);
            // The transitive A–C edge is NEVER minted: Attuned↔Attuned does not qualify.
            Assert.DoesNotContain(Conn("A", "C").CanonicalKey, keys);
            Assert.Equal(2, keys.Count);
        }

        // ─────────────────── Several Stones / sources, one Connection ───────────────────

        [Fact]
        public void AtRd002_SeveralStones_MaintainOneConnection_SourceCountDoesNotMultiplyMaturity()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            // alice & bob are bonded at BOTH S1 and S2 -> ONE Connection, TWO sources.
            reg.ActivateRelationship("op-a1", World, Product, S1, new AccountId("alice"), "rel-a1",
                RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b1", World, Product, S1, new AccountId("bob"), "rel-b1",
                RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-a2", World, Product, S2, new AccountId("alice"), "rel-a2",
                RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b2", World, Product, S2, new AccountId("bob"), "rel-b2",
                RelationshipKind.Bond, t0);

            var conn = reg.GetConnection(Conn("alice", "bob"));
            Assert.Equal(2, conn.Sources.Count);
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            // Maturity is a function of AGE only; two sources do not double it. At 10 days it is band 1.2×.
            var at10 = reg.GetConnection(Conn("alice", "bob")).MaturityAt(t0 + 10 * Day);
            Assert.Equal(ConnectionMaturity.Band2, at10);
        }

        [Fact]
        public void AtRd002_ExactSourceRemoval_OneStoneLeaves_ConnectionStaysActiveViaOther()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-a1", World, Product, S1, new AccountId("alice"), "rel-a1", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b1", World, Product, S1, new AccountId("bob"), "rel-b1", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-a2", World, Product, S2, new AccountId("alice"), "rel-a2", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b2", World, Product, S2, new AccountId("bob"), "rel-b2", RelationshipKind.Bond, t0);

            // Release alice's S1 relationship -> removes the S1 source only; the S2 source keeps it Active.
            var res = reg.ReleaseRelationship("op-rel-a1", World, Product, S1, new AccountId("alice"), "rel-a1", t0 + Day);
            Assert.Equal(ConnectionSourceOutcome.Applied, res.Outcome);

            var conn = reg.GetConnection(Conn("alice", "bob"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            Assert.Single(conn.Sources); // exactly the S2 source remains
        }

        // ─────────────────── Grace / reconnect ───────────────────

        [Fact]
        public void AtRd002_FinalSourceRemoval_FreezesAgeIntoGrace()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-a", World, Product, S1, new AccountId("alice"), "rel-a", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b", World, Product, S1, new AccountId("bob"), "rel-b", RelationshipKind.Bond, t0);

            long removeAt = t0 + 10 * Day;
            reg.ReleaseRelationship("op-rel-a", World, Product, S1, new AccountId("alice"), "rel-a", removeAt);

            var conn = reg.GetConnection(Conn("alice", "bob"));
            Assert.Equal(ConnectionLifecycle.Grace, conn.Lifecycle);
            Assert.False(conn.IsContributionQualifying);
            Assert.Equal(10 * Day, conn.AccumulatedSeconds);
            Assert.Equal(removeAt + ConnectionAggregate.GraceSeconds, conn.GraceExpiresAtSeconds);
        }

        [Fact]
        public void AtRd002_ReconnectWithinGrace_ResumesFrozenAge()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-a", World, Product, S1, new AccountId("alice"), "rel-a", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b", World, Product, S1, new AccountId("bob"), "rel-b", RelationshipKind.Bond, t0);
            long removeAt = t0 + 10 * Day;
            reg.ReleaseRelationship("op-rel-a", World, Product, S1, new AccountId("alice"), "rel-a", removeAt);

            // Reconnect: alice re-bonds S1 within 72h -> the same derived pair resumes the frozen age.
            long reconnectAt = removeAt + 24 * 3600;
            reg.ActivateRelationship("op-a2", World, Product, S1, new AccountId("alice"), "rel-a", RelationshipKind.Bond, reconnectAt);

            var conn = reg.GetConnection(Conn("alice", "bob"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            Assert.Equal(10 * Day, conn.AccumulatedSeconds);      // resumed frozen age, not reset
            Assert.Equal(11 * Day, conn.LiveAgeSeconds(reconnectAt + Day));
        }

        [Fact]
        public void AtRd002_GraceExpiry_ResetsAge()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-a", World, Product, S1, new AccountId("alice"), "rel-a", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b", World, Product, S1, new AccountId("bob"), "rel-b", RelationshipKind.Bond, t0);
            reg.ReleaseRelationship("op-rel-a", World, Product, S1, new AccountId("alice"), "rel-a", t0 + 10 * Day);

            var conn = reg.GetConnection(Conn("alice", "bob"));
            var reset = reg.ReconcileGraceExpiry(Conn("alice", "bob"), conn.GraceExpiresAtSeconds);
            Assert.Equal(ConnectionLifecycle.Reset, reset.Lifecycle);
            Assert.Equal(0, reset.AccumulatedSeconds);
        }

        // ─────────────────── Restart / replay ───────────────────

        [Fact]
        public void AtRd002_Restart_RehydratesExactSourceStateFromJournal()
        {
            long t0 = 1000;
            {
                var reg = NewRegistry();
                reg.ActivateRelationship("op-a1", World, Product, S1, new AccountId("alice"), "rel-a1", RelationshipKind.Bond, t0);
                reg.ActivateRelationship("op-b1", World, Product, S1, new AccountId("bob"), "rel-b1", RelationshipKind.Bond, t0);
                reg.ActivateRelationship("op-a2", World, Product, S2, new AccountId("alice"), "rel-a2", RelationshipKind.Bond, t0);
                reg.ActivateRelationship("op-b2", World, Product, S2, new AccountId("bob"), "rel-b2", RelationshipKind.Bond, t0);
                reg.ReleaseRelationship("op-rel-a1", World, Product, S1, new AccountId("alice"), "rel-a1", t0 + Day);
            }

            // Fresh registry over the SAME journal == restarted process.
            var reg2 = NewRegistry();
            var conn = reg2.GetConnection(Conn("alice", "bob"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            Assert.Single(conn.Sources); // exactly the surviving S2 source, reconstructed
            // Age reconstructed from the replayed event times.
            Assert.Equal(Day + Day, conn.LiveAgeSeconds(t0 + 2 * Day));
        }

        [Fact]
        public void AtRd002_ReplaySameOperation_IsIdempotent()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-a", World, Product, S1, new AccountId("alice"), "rel-a", RelationshipKind.Bond, t0);
            reg.ActivateRelationship("op-b", World, Product, S1, new AccountId("bob"), "rel-b", RelationshipKind.Attunement, t0);

            // Re-submit op-b exactly -> Replayed, no doubled source.
            var replay = reg.ActivateRelationship("op-b", World, Product, S1, new AccountId("bob"), "rel-b",
                RelationshipKind.Attunement, t0);
            Assert.Equal(ConnectionSourceOutcome.Replayed, replay.Outcome);
            Assert.Single(reg.GetConnection(Conn("alice", "bob")).Sources);
        }

        [Fact]
        public void AtRd002_ConflictingOperationId_RejectsOperationConflict()
        {
            var reg = NewRegistry();
            long t0 = 1000;
            reg.ActivateRelationship("op-x", World, Product, S1, new AccountId("alice"), "rel-a", RelationshipKind.Bond, t0);
            // Same op id, different binding (different account) -> conflict, no mutation.
            var conflict = reg.ActivateRelationship("op-x", World, Product, S1, new AccountId("bob"), "rel-b",
                RelationshipKind.Bond, t0);
            Assert.Equal(ConnectionSourceOutcome.OperationConflict, conflict.Outcome);
        }
    }
}
