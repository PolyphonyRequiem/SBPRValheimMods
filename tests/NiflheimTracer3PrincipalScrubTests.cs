// ============================================================================
//  IAP-007 Tracer 3 — provider-free gameplay principal + receipts.
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free gameplay-principal migration (link-compiled
//  from ../src): after admission (Tracer 1/2) mints and binds the internal
//  AccountId/CharacterId, every progression receipt/binding/log path consumes that
//  BOUND INTERNAL principal — the raw provider PlatformId and every brute-forceable
//  provider-derived digest are gone.
//
//  Named acceptance covered here:
//    AT-AIP-PRINCIPAL-SCRUB        no raw provider subject / unkeyed provider digest
//                                  survives in any durable gameplay artifact, and the
//                                  gameplay identity types carry no PlatformId member.
//    AT-AIP-RECEIPT-REPLAY         the internal-only receipt binding still replays
//                                  idempotently and preserves creator authority.
//    AT-AIP-HOSTILE-PRINCIPAL      account/character substitution rejects with no
//                                  mutation under the internal principal.
//    AT-AIP-NO-PROVIDER-HOTPATH    ordinary gameplay performs no provider lookup /
//                                  network call: the resolver takes no provider map,
//                                  and the bound-session index is the only identity
//                                  source (no provider fallback).
//    AT-AIP-DEFERRED-SURFACE-ABSENT the deferred provider surfaces (Discord/OIDC/OAuth
//                                  /portal/character-select UI) remain absent from the
//                                  shipped gameplay principal path.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimTracer3PrincipalScrubTests : System.IDisposable
    {
        private readonly string _journalPath;
        private readonly WorldId _world = new WorldId("uid:t3");
        private readonly StoneId _stone;

        // A hostile provider subject the scrub must never let leak into a durable gameplay artifact.
        private const string ProviderSubject = "steam:76561198000000001";

        // The BOUND INTERNAL principal admission minted — opaque, provider-independent (Tracer 1/2).
        private readonly AccountId _account = new AccountId("acct-0123456789abcdef0123456789abcdef");
        private readonly CharacterId _character = new CharacterId("char-fedcba9876543210fedcba9876543210");

        private readonly OperationReceiptStore _receipts;
        private readonly InMemoryMirroredStoneApStore _stoneStore;
        private readonly InMemoryCharacterApStore _characterStore;
        private readonly ProgressionCommandPipeline _pipeline;

        public NiflheimTracer3PrincipalScrubTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(), "niflheim-t3-" + Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 3, 9);
            _stoneStore = new InMemoryMirroredStoneApStore();
            _characterStore = new InMemoryCharacterApStore();
            _receipts = new OperationReceiptStore(_journalPath, _stoneStore, _characterStore);
            var authorizer = new PreconfiguredTestAuthorizer().Allow(_account, _stone);
            _pipeline = new ProgressionCommandPipeline(new PrincipalResolver(), _receipts, authorizer);
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private AuthenticatedConnection BoundConnection() =>
            new AuthenticatedConnection(_account.Value, _character.Value);

        private FoundationalPlacementCommand Command(string operationId, ClaimedPrincipal claim, string evidence = "foundation_wood_floor")
        {
            var adapter = new FoundationalPlacementAdapter();
            var facts = new FoundationalPlacementEvidence(
                new OperationId(operationId), _stone,
                stablePieceId: evidence, pieceInstanceProvenance: "prov-" + evidence,
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1");
            var admission = adapter.Admit(facts, BoundConnection(), claim);
            Assert.True(admission.IsAdmitted);
            return admission.Command;
        }

        // ── AT-AIP-PRINCIPAL-SCRUB ─────────────────────────────────────────────────

        [Fact]
        public void AtAipPrincipalScrub_GameplayIdentityTypes_CarryNoProviderMember()
        {
            // The internal gameplay identity value objects expose no provider/platform member.
            foreach (var t in new[] { typeof(AuthenticatedConnection), typeof(AuthoritativePrincipal), typeof(PilotSessionPrincipal) })
            {
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    var name = p.Name.ToLowerInvariant();
                    Assert.DoesNotContain("platform", name);
                    Assert.DoesNotContain("provider", name);
                    Assert.DoesNotContain("subject", name);
                }
            }
        }

        [Fact]
        public void AtAipPrincipalScrub_DurableReceipt_ContainsNoProviderSubjectOrItsDigest()
        {
            // Commit a real placement under the bound internal principal.
            var result = _pipeline.Handle(Command("op-scrub", new ClaimedPrincipal(_account.Value, _character.Value)));
            Assert.Equal(CommandOutcome.Applied, result.Outcome);

            // The provider subject was never handed to the gameplay path; it must not appear raw in the
            // durable journal, nor as the unkeyed truncated digest of it (the pre-Tracer-3 leak shape).
            string providerDigest = OperationReceiptStore.Digest(ProviderSubject);
            string legacyPrincipalDigest = OperationReceiptStore.Digest(
                _account.Value + "|" + _character.Value + "|" + ProviderSubject);

            Assert.False(PersistedPiiScanner.TryFindForbidden(
                _journalPath, new[] { ProviderSubject, providerDigest, legacyPrincipalDigest }, out var offending),
                "durable gameplay receipt leaked a provider subject/digest: " + offending);

            // Positive control: the scanner is not vacuously passing — a file that genuinely contains the
            // provider subject IS flagged, proving the negative result above is a real absence.
            string seeded = _journalPath + ".seed";
            File.WriteAllText(seeded, "REC|op|" + ProviderSubject + "|leak");
            try
            {
                Assert.True(PersistedPiiScanner.TryFindForbidden(seeded, new[] { ProviderSubject }, out _));
            }
            finally
            {
                if (File.Exists(seeded)) File.Delete(seeded);
            }
        }

        // ── AT-AIP-RECEIPT-REPLAY ──────────────────────────────────────────────────

        [Fact]
        public void AtAipReceiptReplay_InternalOnlyBinding_ReplaysIdempotently()
        {
            var claim = new ClaimedPrincipal(_account.Value, _character.Value);
            var first = _pipeline.Handle(Command("op-replay", claim));
            var second = _pipeline.Handle(Command("op-replay", claim));

            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            Assert.Equal(CommandOutcome.Replayed, second.Outcome);
            Assert.Equal(first.ReceiptId, second.ReceiptId);
            Assert.Equal(1, _stoneStore.GetMirroredStoneAp(_stone));
            Assert.Equal(1, _characterStore.GetPersonalAp(_account, _character, _stone));
        }

        [Fact]
        public void AtAipReceiptReplay_FreshProcessOverSameJournal_ResumesInternalBalances()
        {
            _pipeline.Handle(Command("op-restart", new ClaimedPrincipal(_account.Value, _character.Value)));

            // Fresh process: new stores over the SAME journal rebuild the internal-keyed balances.
            var stone2 = new InMemoryMirroredStoneApStore();
            var char2 = new InMemoryCharacterApStore();
            _ = new OperationReceiptStore(_journalPath, stone2, char2);

            Assert.Equal(1, stone2.GetMirroredStoneAp(_stone));
            Assert.Equal(1, char2.GetPersonalAp(_account, _character, _stone));
            Assert.Equal(1, char2.GetCumulativeAp(_account, _character, _stone));
        }

        // ── AT-AIP-HOSTILE-PRINCIPAL ───────────────────────────────────────────────

        [Fact]
        public void AtAipHostilePrincipal_AccountSubstitution_RejectsWithoutMutation()
        {
            // The bound session is the owner; the payload claims a different internal account.
            var hostile = new ClaimedPrincipal("acct-deadbeefdeadbeefdeadbeefdeadbeef", _character.Value);
            var result = _pipeline.Handle(Command("op-hostile-acct", hostile));

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("PrincipalMismatch", result.ResultCode);
            Assert.Empty(_receipts.DurableOperationIds());
        }

        [Fact]
        public void AtAipHostilePrincipal_UnauthenticatedPeer_RejectsWithoutMutation()
        {
            // No bound internal session (empty account) -> unauthenticated; no provider fallback.
            var adapter = new FoundationalPlacementAdapter();
            var evidence = new FoundationalPlacementEvidence(
                new OperationId("op-unauth"), _stone, "foundation_wood_door", "prov-x", true, true, "v1");
            var admission = adapter.Admit(evidence, new AuthenticatedConnection("", ""),
                new ClaimedPrincipal(_account.Value, _character.Value));
            var result = _pipeline.Handle(admission.Command);

            Assert.Equal(CommandOutcome.Rejected, result.Outcome);
            Assert.Equal("Unauthenticated", result.ResultCode);
            Assert.Empty(_receipts.DurableOperationIds());
        }

        // ── AT-AIP-NO-PROVIDER-HOTPATH ─────────────────────────────────────────────

        [Fact]
        public void AtAipNoProviderHotpath_ResolverTakesNoProviderMap()
        {
            // The gameplay resolver has exactly one public constructor and it takes NO arguments — there
            // is no platform→account lookup function (candidate E) and no candidate-A fallback, so the
            // gameplay path can perform no provider lookup or network call.
            var ctors = typeof(PrincipalResolver).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            Assert.Single(ctors);
            Assert.Empty(ctors[0].GetParameters());
        }

        [Fact]
        public void AtAipNoProviderHotpath_BoundSessionIndexIsTheOnlyIdentitySource_NoProviderFallback()
        {
            var index = new BoundSessionPrincipalIndex();
            var peerKey = "5555"; // a server-owned peer key (durable s_playerID), used only to look up

            // An unbound peer resolves to nothing — the gameplay path fails closed rather than
            // provider-deriving an identity.
            Assert.False(index.TryResolve(peerKey, out _));

            // Admission publishes the minted INTERNAL principal; the observer reads it back with no
            // provider subject involved.
            index.Bind(peerKey, new PilotSessionPrincipal(_account, _character, "sess-1"));
            Assert.True(index.TryResolve(peerKey, out var bound));
            Assert.Equal(_account, bound.Account);
            Assert.Equal(_character, bound.Character);

            // A provider-shaped (empty-account) principal is refused by the index — it never holds a
            // provider identity.
            index.Bind("6666", new PilotSessionPrincipal(new AccountId(""), _character, "sess-2"));
            Assert.False(index.TryResolve("6666", out _));

            // Disconnect clears the binding.
            index.Unbind(peerKey);
            Assert.False(index.TryResolve(peerKey, out _));
        }

        // ── AT-AIP-DEFERRED-SURFACE-ABSENT ─────────────────────────────────────────

        [Fact]
        public void AtAipDeferredSurfaceAbsent_NoDeferredProviderSurfaceInGameplayPrincipalPath()
        {
            // AIP-FR-027: Discord linking, OIDC/OAuth portals, passkeys, email/password auth, recovery
            // factors, cross-provider migration, and a server character-select UI stay absent. The
            // gameplay-principal types must reference none of those concepts.
            string[] forbidden = { "discord", "oauth", "oidc", "passkey", "email", "password",
                                   "recovery", "portal", "characterselect", "migration" };
            foreach (var t in new[]
            {
                typeof(AuthenticatedConnection), typeof(AuthoritativePrincipal),
                typeof(PilotSessionPrincipal), typeof(PrincipalResolver),
                typeof(BoundSessionPrincipalIndex), typeof(FoundationalPlacementObservation)
            })
            {
                var surface = t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                    .Select(m => m.Name.ToLowerInvariant());
                foreach (var member in surface)
                    foreach (var bad in forbidden)
                        Assert.DoesNotContain(bad, member);
            }
        }
    }
}
