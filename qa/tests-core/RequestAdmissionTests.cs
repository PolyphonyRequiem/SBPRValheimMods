// ADR-0009 M1 — per-request ADMISSION acceptance tests.
// Named ATs proven here: BAD-NONCE-REJECT, OUT-OF-MANIFEST-REJECT,
// OUT-OF-BOUNDS-ARG-REJECT, REPLAY-REJECT (+ role/world/expiry/hmac/sequence).
using System.Collections.Generic;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class RequestAdmissionTests
    {
        private static (RequestAdmission, ArmedState) Armed()
        {
            var armed = Fixtures.ArmValidServer(new[] { "SpawnStation", "GrantVanillaMaterials", "Ping" });
            return (new RequestAdmission(armed), armed);
        }

        // ── Happy path ───────────────────────────────────────────────────────
        [Fact]
        public void ValidSignedRequest_Admitted()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "SpawnStation", 1, "req-1", Fixtures.SpawnArgs());
            var d = adm.Admit(env, Fixtures.Now);
            Assert.True(d.Admitted);
            Assert.Equal("SpawnStation", d.Verb!.Name);
        }

        [Fact]
        public void NullEnvelope_Malformed()
        {
            var (adm, _) = Armed();
            Assert.Equal(RejectReason.MalformedEnvelope, adm.Admit(null, Fixtures.Now).Reason);
        }

        // ── BAD-NONCE-REJECT ─────────────────────────────────────────────────
        [Fact]
        public void WrongNonce_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "Ping", 1, "req-n", new Dictionary<string, object?>(),
                nonceOverride: "wrong-nonce");
            Assert.Equal(RejectReason.BadNonce, adm.Admit(env, Fixtures.Now).Reason);
        }

        // ── Role / world mismatch ────────────────────────────────────────────
        [Fact]
        public void WrongRole_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "Ping", 1, "req-r", new Dictionary<string, object?>(),
                roleOverride: "Client");
            Assert.Equal(RejectReason.RoleMismatch, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void WrongWorldUid_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "Ping", 1, "req-w", new Dictionary<string, object?>(),
                worldUidOverride: 42);
            Assert.Equal(RejectReason.RequestWorldMismatch, adm.Admit(env, Fixtures.Now).Reason);
        }

        // ── OUT-OF-MANIFEST-REJECT ───────────────────────────────────────────
        [Fact]
        public void KnownVerbNotInManifest_Rejected()
        {
            // Arm with only Ping; ask for PlaceVanillaPiece (a real Server verb, not permitted).
            var armed = Fixtures.ArmValidServer(new[] { "Ping" });
            var adm = new RequestAdmission(armed);
            var env = Fixtures.SignedRequest(armed, "PlaceVanillaPiece", 1, "req-m",
                new Dictionary<string, object?> { ["prefab"] = "wood_wall", ["posRadius"] = 1.0 });
            Assert.Equal(RejectReason.OutOfManifest, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void UnknownVerb_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "TotallyFakeVerb", 1, "req-u", new Dictionary<string, object?>());
            Assert.Equal(RejectReason.UnknownVerb, adm.Admit(env, Fixtures.Now).Reason);
        }

        // ── OUT-OF-BOUNDS-ARG-REJECT ─────────────────────────────────────────
        [Fact]
        public void QtyAboveMax_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-b", Fixtures.GrantArgs(qty: 9999));
            Assert.Equal(RejectReason.OutOfBoundsArg, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void QtyBelowMin_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-b0", Fixtures.GrantArgs(qty: 0));
            Assert.Equal(RejectReason.OutOfBoundsArg, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void MissingDeclaredArg_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-miss",
                new Dictionary<string, object?> { ["itemId"] = "Wood" }); // qty missing
            Assert.Equal(RejectReason.OutOfBoundsArg, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void UndeclaredExtraArg_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-extra",
                new Dictionary<string, object?> { ["itemId"] = "Wood", ["qty"] = 5L, ["sneaky"] = "x" });
            Assert.Equal(RejectReason.OutOfBoundsArg, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void WrongArgType_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-type",
                new Dictionary<string, object?> { ["itemId"] = "Wood", ["qty"] = "five" });
            Assert.Equal(RejectReason.OutOfBoundsArg, adm.Admit(env, Fixtures.Now).Reason);
        }

        [Fact]
        public void RadiusAboveMax_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "SpawnStation", 1, "req-rad",
                new Dictionary<string, object?> { ["prefab"] = "piece_workbench", ["posRadius"] = 999.0 });
            Assert.Equal(RejectReason.OutOfBoundsArg, adm.Admit(env, Fixtures.Now).Reason);
        }

        // ── Expiry ───────────────────────────────────────────────────────────
        [Fact]
        public void ExpiredRequest_Rejected()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "Ping", 1, "req-exp", new Dictionary<string, object?>(),
                expiry: Fixtures.Now - 1);
            Assert.Equal(RejectReason.RequestExpired, adm.Admit(env, Fixtures.Now).Reason);
        }

        // ── HMAC ─────────────────────────────────────────────────────────────
        [Fact]
        public void TamperedHmac_Rejected()
        {
            var (adm, armed) = Armed();
            var good = Fixtures.SignedRequest(armed, "Ping", 1, "req-h", new Dictionary<string, object?>());
            var tampered = new RequestEnvelope(good.Nonce, good.Seq, good.ExpiryUnixMs,
                "deadbeef" + good.Hmac!.Substring(8), good.Role, good.WorldUid, good.Verb, good.RequestId,
                good.ConnectionGeneration, good.Args);
            Assert.Equal(RejectReason.BadHmac, adm.Admit(tampered, Fixtures.Now).Reason);
        }

        [Fact]
        public void TamperedFieldBreaksHmac_Rejected()
        {
            var (adm, armed) = Armed();
            var good = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-tf", Fixtures.GrantArgs(5));
            // Keep the signature but change the sequence — canonical string no longer matches.
            var tampered = new RequestEnvelope(good.Nonce, 999, good.ExpiryUnixMs, good.Hmac,
                good.Role, good.WorldUid, good.Verb, good.RequestId, good.ConnectionGeneration, good.Args);
            Assert.Equal(RejectReason.BadHmac, adm.Admit(tampered, Fixtures.Now).Reason);
        }

        [Fact]
        public void TamperedGenerationBreaksHmac_Rejected()
        {
            var (adm, armed) = Armed();
            var good = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-tg", Fixtures.GrantArgs(5));
            // Keep the signature but change the connection generation — canonical string no longer matches.
            var tampered = new RequestEnvelope(good.Nonce, good.Seq, good.ExpiryUnixMs, good.Hmac,
                good.Role, good.WorldUid, good.Verb, good.RequestId, good.ConnectionGeneration + 7, good.Args);
            Assert.Equal(RejectReason.BadHmac, adm.Admit(tampered, Fixtures.Now).Reason);
        }

        [Fact]
        public void NonPositiveGeneration_Malformed()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "Ping", 1, "req-g0", new Dictionary<string, object?>(),
                connectionGeneration: 0);
            Assert.Equal(RejectReason.MalformedEnvelope, adm.Admit(env, Fixtures.Now).Reason);
        }

        // ── REPLAY-REJECT ────────────────────────────────────────────────────
        [Fact]
        public void ReplaySameRequestIdAndSeq_ReturnsCachedNotReexecuted()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "SpawnStation", 1, "req-replay", Fixtures.SpawnArgs());
            var first = adm.Admit(env, Fixtures.Now);
            Assert.True(first.Admitted);

            var second = adm.Admit(env, Fixtures.Now);
            Assert.True(second.IsReplay);
            Assert.False(second.Admitted);        // replay does NOT re-admit / re-execute
            Assert.Equal(RejectReason.None, second.Reason); // echoes the original outcome
        }

        [Fact]
        public void ReplayOfRejectedRequest_ReturnsSameReason()
        {
            var (adm, armed) = Armed();
            var env = Fixtures.SignedRequest(armed, "GrantVanillaMaterials", 1, "req-rr", Fixtures.GrantArgs(9999));
            var first = adm.Admit(env, Fixtures.Now);
            Assert.Equal(RejectReason.OutOfBoundsArg, first.Reason);
            var second = adm.Admit(env, Fixtures.Now);
            Assert.True(second.IsReplay);
            Assert.Equal(RejectReason.OutOfBoundsArg, second.Reason);
        }

        [Fact]
        public void SameRequestIdDifferentSeq_Conflict()
        {
            var (adm, armed) = Armed();
            var e1 = Fixtures.SignedRequest(armed, "SpawnStation", 1, "req-c", Fixtures.SpawnArgs());
            Assert.True(adm.Admit(e1, Fixtures.Now).Admitted);
            var e2 = Fixtures.SignedRequest(armed, "SpawnStation", 2, "req-c", Fixtures.SpawnArgs());
            Assert.Equal(RejectReason.SequenceConflict, adm.Admit(e2, Fixtures.Now).Reason);
        }

        // ── Sequence monotonicity ────────────────────────────────────────────
        [Fact]
        public void RewoundSequence_Rejected()
        {
            var (adm, armed) = Armed();
            Assert.True(adm.Admit(Fixtures.SignedRequest(armed, "SpawnStation", 5, "req-s5", Fixtures.SpawnArgs()), Fixtures.Now).Admitted);
            // seq 3 < highest 5 => conflict
            var lower = Fixtures.SignedRequest(armed, "SpawnStation", 3, "req-s3", Fixtures.SpawnArgs());
            Assert.Equal(RejectReason.SequenceConflict, adm.Admit(lower, Fixtures.Now).Reason);
        }

        [Fact]
        public void MonotonicIncreasingSequence_Admitted()
        {
            var (adm, armed) = Armed();
            Assert.True(adm.Admit(Fixtures.SignedRequest(armed, "SpawnStation", 1, "a", Fixtures.SpawnArgs()), Fixtures.Now).Admitted);
            Assert.True(adm.Admit(Fixtures.SignedRequest(armed, "SpawnStation", 2, "b", Fixtures.SpawnArgs()), Fixtures.Now).Admitted);
            Assert.True(adm.Admit(Fixtures.SignedRequest(armed, "Ping", 3, "c", new Dictionary<string, object?>()), Fixtures.Now).Admitted);
        }
    }
}
