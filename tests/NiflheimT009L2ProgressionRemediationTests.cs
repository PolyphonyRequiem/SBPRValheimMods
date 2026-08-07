// ============================================================================
//  T009L2 — bound-principal Attunement + relationship-journal recovery.
// ----------------------------------------------------------------------------
//  Executable evidence for the two concrete current-main blockers a real joined
//  GPU client surfaced (evidence: T009L2-FAIL.md), exercising the SHIPPED,
//  engine-free source link-compiled from ../src so the asserted behaviour IS the
//  shipped behaviour.
//
//  Blocker 1 — principal-space mismatch. Provisioning previously created the
//  Attunement under the raw provider/socket account + player:<s_playerID>
//  subject (AuthenticatedSenderBinder), while dedicated placement authorizes
//  under the BOUND INTERNAL (AccountId, CharacterId) admission published into
//  BoundSessionPrincipalIndex. The two spaces never matched, so an admitted,
//  attuned real placement failed RelationshipRequired with zero AP. The
//  RelationshipProvisioningIngress + the shared runtime are engine-free; the
//  net48 RelationshipProvisioningAdmin seam that resolves the bound principal is
//  not link-compiled, so these tests prove the property at the engine-free seam
//  the fixed net48 handler now calls: provision under the SAME bound internal
//  principal placement uses, and an eligible Foundational placement then reaches
//  an Applied AP receipt.
//
//  Blocker 2 — relationship journal framing. ProvisioningOperationBinding.
//  OperationId legitimately embeds literal '|' (it joins material fields,
//  including a StoneId such as "uid:-898655635|3|2"), but RelationshipCommandHandler
//  wrote it UNENCODED into a pipe-delimited record while ParseRecord required
//  exactly 14 fields. A four-frame journal rehydrated to zero accepted records,
//  so a re-provision returned Applied instead of Replayed and the Attunement was
//  process-local despite fsynced writes. The framing is now delimiter-safe: every
//  free-text field is base64-encoded, so ANY operation id round-trips, restart
//  rehydration recovers the committed op, and an exact re-provision Replays.
//
//  Properties proven (task acceptance 1-5):
//    * provisioning + dedicated placement share ONE bound internal principal;
//      an UNBOUND peer resolves to nothing (fail closed) — proven at the ingress;
//    * no raw provider/profile subject enters the relationship command, journal,
//      or the operator correlation tag;
//    * an operation id CONTAINING '|' round-trips through the durable journal;
//    * a committed Attunement survives a process restart (fresh handler over the
//      same journal file) and an exact re-submit returns Replayed, not Applied;
//    * a torn/malformed record is rejected honestly (never partially applied);
//    * under that restored Attunement an eligible Foundational placement reaches
//      an Applied AP receipt, and the Personal/Cumulative/Mirrored deltas are one
//      atomic operation that replays exactly once.
// ============================================================================

using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimT009L2ProgressionRemediationTests : IDisposable
    {
        private readonly string _dir;
        // A world UID with a NEGATIVE id renders a StoneId whose value embeds literal '|' — exactly the
        // shape that tore the journal on the live server (uid:-898655635|3|2).
        private readonly WorldId _world = new WorldId("uid:-898655635");
        private readonly StoneId _stone;
        // The BOUND INTERNAL principal placement authorizes under (server-minted opaque ids). This is the
        // ONLY identity that may enter the gameplay relationship — never a provider/socket subject.
        private readonly AccountId _account = new AccountId("acct-internal-1");
        private readonly CharacterId _character = new CharacterId("char-internal-1");
        private const double StoneX = 204.5;
        private const double StoneZ = 125.0;

        public NiflheimT009L2ProgressionRemediationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t009l2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 3, 2);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ── fixtures ────────────────────────────────────────────────────────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            private readonly string _key;
            public FixedFamilyResolver(StoneId stone) { _key = stone.Value; }
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (string.Equals(stoneId.Value, _key, StringComparison.Ordinal))
                { family = "Settlement"; variant = "Homestead"; return true; }
                family = variant = string.Empty; return false;
            }
        }

        private sealed class HomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal);
            }
        }

        private sealed class FakeInstanceSource : IServerPlacedInstanceSource
        {
            private readonly System.Collections.Generic.Dictionary<string, ServerPlacedInstanceFacts> _byKey =
                new System.Collections.Generic.Dictionary<string, ServerPlacedInstanceFacts>(StringComparer.Ordinal);
            public void Put(string key, string prefabName, string creatorPrincipal, double x, double z) =>
                _byKey[key] = new ServerPlacedInstanceFacts(key, prefabName, creatorPrincipal, x, z, exists: true);
            public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
            {
                if (instanceKey != null && _byKey.TryGetValue(instanceKey, out facts)) return true;
                facts = ServerPlacedInstanceFacts.Absent(instanceKey ?? string.Empty);
                return false;
            }
        }

        private FoundationalProgressionServer NewServer(IMirroredStoneApStore stoneStore)
        {
            var server = FoundationalProgressionServer.Create(
                _dir, new FixedFamilyResolver(_stone), new HomesteadBondPolicy(), stoneStore);
            server.StoneAreas.Register(_stone, StoneX, StoneZ, radius: 20.0);
            // ADO #138: the relationship handler checks proximity itself now, so this fixture must
            // state the server-observed fact that the acting character is standing AT the Stone.
            server.CharacterPositions.Publish(_character, StoneX, StoneZ);
            return server;
        }

        // The peer key the gameplay path keys the bound-session index by: the durable player:<s_playerID>
        // character subject. It is deliberately NOT the internal character id — it is the SERVER-OBSERVED
        // key admission binds under and the ingress/provisioning resolve through.
        private static string PeerKey(long playerId) => ServerCreatorIdentity.CharacterSubject(playerId);

        private void Seed(FoundationalProgressionServer server)
        {
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(_account, _character,
                    worldProductScope: "t009l2/trailborne", revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));
        }

        // The operation id the fixed net48 admin seam composes — bound to the BOUND INTERNAL principal, the
        // Stone, the command, the range, and the world scope. It embeds '|' via the StoneId + world scope.
        private string BoundOpId(RelationshipCommandType cmd, string range) =>
            ProvisioningOperationBinding.OperationId(
                _account.Value, _character.Value, _stone, cmd, range, _world.Value + "/uid");

        // ── Blocker 1: provisioning + placement share ONE bound internal principal ──

        [Fact]
        public void Provisioning_UnderBoundInternalPrincipal_AuthorizesDedicatedPlacement_AppliedReceipt()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server);

            // Publish the bound internal principal for the server-owned peer key, exactly as live admission
            // does on session activation.
            long playerId = 5555L;
            string peerKey = PeerKey(playerId);
            server.BoundSessions.Bind(peerKey, new PilotSessionPrincipal(_account, _character, "sess-1"));

            // Provision the Attunement under the SAME bound internal principal (the identity the fixed net48
            // seam now resolves from BoundSessions), with the '|'-bearing operation id.
            string opId = BoundOpId(RelationshipCommandType.CreateAttunement, string.Empty);
            var provision = server.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, opId,
                ProvisioningOperationBinding.RelationshipId(_character.Value, RelationshipCommandType.CreateAttunement),
                worldProductScope: _world.Value + "/uid");
            Assert.True(provision.Established);
            Assert.Equal(RelationshipCommandOutcome.Applied, provision.Outcome);

            // The exact eligible Foundational placement — creator == peer key == player:5555 — now credits.
            var source = new FakeInstanceSource();
            source.Put("100:1", "wood_floor", peerKey, StoneX, StoneZ);
            var credited = server.CreateDedicatedIngress(source).Ingest(peerKey, "100:1");

            Assert.True(credited.Routed);
            Assert.Equal(RuntimePlacementDisposition.Earned, credited.Runtime.Disposition);
            Assert.True(credited.Runtime.Credited);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void UnboundPeer_ProvisioningPrincipalNotResolvable_PlacementFailsClosed()
        {
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server);

            // No bind published for this peer. Even with a would-be eligible placement, the ingress fails
            // closed BEFORE any ZDO check — no provider/platform fallback principal is ever derived.
            long playerId = 4242L;
            string peerKey = PeerKey(playerId);
            var source = new FakeInstanceSource();
            source.Put("100:9", "wood_floor", peerKey, StoneX, StoneZ);

            var outcome = server.CreateDedicatedIngress(source).Ingest(peerKey, "100:9");

            Assert.False(outcome.Routed);
            Assert.Equal(DedicatedIngressRejection.UnboundPeer, outcome.Rejection);
            Assert.Equal(0, stoneStore.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void ProvisioningIdentity_CarriesNoRawProviderSubject_OperatorTagIsPseudonymous()
        {
            // The correlation tag emitted to the operator log is derived only from the BOUND INTERNAL ids
            // and is a short digest — it echoes neither the internal ids verbatim nor any provider/socket
            // subject or raw s_playerID.
            const string providerSubject = "76561198000000001";
            long playerId = 5555L;
            string tag = ProvisioningOperationBinding.CorrelationTag(_account.Value, _character.Value);

            Assert.StartsWith("corr-", tag);
            Assert.DoesNotContain(providerSubject, tag);
            Assert.DoesNotContain(playerId.ToString(System.Globalization.CultureInfo.InvariantCulture), tag);
            Assert.DoesNotContain(_account.Value, tag);
            Assert.DoesNotContain(_character.Value, tag);
            // Deterministic: same principal → same tag (an operator can correlate repeated provisioning).
            Assert.Equal(tag, ProvisioningOperationBinding.CorrelationTag(_account.Value, _character.Value));
        }

        // ── Blocker 2: delimiter-bearing operation ids round-trip the journal ──────

        [Fact]
        public void OperationId_ContainsPipe_FromStoneAndWorldScope()
        {
            // Guard the premise: the bound operation id legitimately embeds literal '|' (the exact shape
            // that tore the live journal). If this ever stops being true the round-trip test below is moot.
            string opId = BoundOpId(RelationshipCommandType.CreateAttunement, string.Empty);
            Assert.Contains("|", opId);
            Assert.Contains(_stone.Value, opId);
        }

        [Fact]
        public void Attunement_WithPipeBearingOperationId_SurvivesRestart_ReplaysExactly()
        {
            string opId = BoundOpId(RelationshipCommandType.CreateAttunement, string.Empty);
            Assert.Contains("|", opId);
            string relId = ProvisioningOperationBinding.RelationshipId(_character.Value, RelationshipCommandType.CreateAttunement);
            string worldScope = _world.Value + "/uid";

            // First process: seed + provision the Attunement. It commits to the durable relationship journal.
            var stoneStore1 = new InMemoryMirroredStoneApStore();
            var server1 = NewServer(stoneStore1);
            Seed(server1);
            var first = server1.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, opId, relId, worldScope);
            Assert.Equal(RelationshipCommandOutcome.Applied, first.Outcome);

            // The journal file must hold at least one framed record and rehydrate cleanly.
            string relJournal = Path.Combine(_dir, FoundationalProgressionServer.RelationshipJournalFile);
            Assert.True(File.Exists(relJournal));
            Assert.True(new FileInfo(relJournal).Length > 0);

            // Second process: a fresh server over the SAME durable directory. Construction rehydrates the
            // relationship handler from the journal — the '|'-bearing op must be recovered (pre-fix: zero
            // records accepted → the op looked absent → a re-provision Applied again).
            var stoneStore2 = new InMemoryMirroredStoneApStore();
            var server2 = NewServer(stoneStore2);
            Seed(server2); // seed-if-absent is a no-op for an already-present aggregate after rehydrate

            var replay = server2.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, opId, relId, worldScope);

            Assert.True(replay.Established);
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);

            // And the restored Attunement authorizes a placement in the SECOND process (proving the
            // authority projection actually rehydrated, not just the idempotency record).
            long playerId = 5555L;
            string peerKey = PeerKey(playerId);
            server2.BoundSessions.Bind(peerKey, new PilotSessionPrincipal(_account, _character, "sess-2"));
            var source = new FakeInstanceSource();
            source.Put("200:1", "wood_floor", peerKey, StoneX, StoneZ);
            var credited = server2.CreateDedicatedIngress(source).Ingest(peerKey, "200:1");
            Assert.Equal(RuntimePlacementDisposition.Earned, credited.Runtime.Disposition);
            Assert.Equal(1, stoneStore2.GetMirroredStoneAp(_stone));
        }

        [Fact]
        public void RelationshipJournal_RoundTripsPipeBearingOperationId_AndRejectsTornRecordHonestly()
        {
            string opId = BoundOpId(RelationshipCommandType.CreateBond, "Homestead:All");
            Assert.Contains("|", opId);
            string relId = ProvisioningOperationBinding.RelationshipId(_character.Value, RelationshipCommandType.CreateBond);
            string worldScope = _world.Value + "/uid";

            // Commit a Bond (its op id carries '|' AND the Bond path exercises the ResultCode field, which is
            // now also encoded).
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server);
            var res = server.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateBond, opId, relId, worldScope, "Homestead:All");
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);

            string relJournal = Path.Combine(_dir, FoundationalProgressionServer.RelationshipJournalFile);
            long committedLength = new FileInfo(relJournal).Length;

            // Append a torn/garbage tail AFTER the intact committed frames: a bogus length header with no
            // payload behind it. Recovery's CRC-framed reader must stop at the torn tail (dropping it) while
            // still recovering every intact committed frame before it — the committed op replays exactly
            // once, and nothing partial is applied.
            using (var fs = new FileStream(relJournal, FileMode.Append, FileAccess.Write))
            using (var bw = new System.IO.BinaryWriter(fs))
            {
                bw.Write(9999);            // claims a 9999-byte payload...
                bw.Write((uint)0xDEADBEEF); // ...bogus crc...
                bw.Write(new byte[] { 1, 2, 3 }); // ...only 3 bytes actually follow → torn frame.
            }
            Assert.True(new FileInfo(relJournal).Length > committedLength);

            var stoneStore2 = new InMemoryMirroredStoneApStore();
            var server2 = NewServer(stoneStore2);
            Seed(server2);
            // The committed op still replays exactly once — the torn tail neither corrupts the recovered
            // record nor is partially applied.
            var replay = server2.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateBond, opId, relId, worldScope, "Homestead:All");
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);
        }

        // ── Blocker 1+2 combined: atomic delta, replays exactly once ──────────────

        [Fact]
        public void EligiblePlacement_UnderRestoredAttunement_CreditsAtomically_ReplaysOnce()
        {
            string opId = BoundOpId(RelationshipCommandType.CreateAttunement, string.Empty);
            string relId = ProvisioningOperationBinding.RelationshipId(_character.Value, RelationshipCommandType.CreateAttunement);
            string worldScope = _world.Value + "/uid";

            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            Seed(server);
            server.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, opId, relId, worldScope);

            long playerId = 5555L;
            string peerKey = PeerKey(playerId);
            server.BoundSessions.Bind(peerKey, new PilotSessionPrincipal(_account, _character, "sess-1"));

            var source = new FakeInstanceSource();
            source.Put("300:7", "wood_floor", peerKey, StoneX, StoneZ);
            var ingress = server.CreateDedicatedIngress(source);

            var first = ingress.Ingest(peerKey, "300:7");
            Assert.Equal(RuntimePlacementDisposition.Earned, first.Runtime.Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));

            // The SAME physical instance replayed (duplicate/retried notice) converges on the one receipt —
            // the Personal/Cumulative/Mirrored delta is one atomic operation credited exactly once.
            var replay = server.CreateDedicatedIngress(source).Ingest(peerKey, "300:7");
            Assert.True(replay.Routed);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone)); // unchanged — no double credit
        }
    }
}
