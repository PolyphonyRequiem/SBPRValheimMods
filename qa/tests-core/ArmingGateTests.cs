// ADR-0009 M1 — fail-closed ARMING gate acceptance tests.
// Named ATs proven here: AT-QA-DISABLED-BY-DEFAULT, PROD-WORLD-REJECT,
// EXACT-WORLD-UID (+ the rest of the AND-composed gate, each isolated).
using System.Collections.Generic;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ArmingGateTests
    {
        // ── AT-QA-DISABLED-BY-DEFAULT ────────────────────────────────────────
        [Fact]
        public void NullManifest_IsDisabledByDefault()
        {
            var d = ArmingGate.Evaluate(null, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.False(d.Armed);
            Assert.Equal(RejectReason.DisabledByDefault, d.Reason);
        }

        [Fact]
        public void EnabledFalse_IsDisabledByDefault()
        {
            var m = new ArmManifest(false, "Server", "primary", Fixtures.DisposableWorld(),
                Fixtures.Nonce, Fixtures.ArmExpiry, Fixtures.HashManifest(),
                new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.DisabledByDefault, d.Reason);
        }

        // ── Happy path ───────────────────────────────────────────────────────
        [Fact]
        public void FullyValidManifest_Arms()
        {
            var d = ArmingGate.Evaluate(Fixtures.ValidServerManifest(), Fixtures.DisposableWorld(),
                Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.True(d.Armed);
            Assert.NotNull(d.State);
            Assert.Equal(HarnessRole.Server, d.State!.Role);
            Assert.Equal("primary", d.State.Actor);
        }

        // ── Role / actor ─────────────────────────────────────────────────────
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("server")]   // wrong case — strict
        [InlineData("Governor")] // not a role
        public void BadRoleToken_Rejected(string? token)
        {
            var m = new ArmManifest(true, token, "primary", Fixtures.DisposableWorld(),
                Fixtures.Nonce, Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.UnknownRole, d.Reason);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MissingActor_Rejected(string? actor)
        {
            var m = new ArmManifest(true, "Server", actor, Fixtures.DisposableWorld(),
                Fixtures.Nonce, Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.MissingActor, d.Reason);
        }

        // ── AT-QA-EXACT-WORLD-UID: name matches, UID does not ────────────────
        [Fact]
        public void WorldNameMatches_UidDoesNot_Rejected()
        {
            var observed = new WorldIdentity(Fixtures.DisposableUid + 1, Fixtures.DisposableName);
            var policy = new WorldPolicy(new[] { observed }); // allowlist the observed one so we isolate the UID axis
            var d = ArmingGate.Evaluate(Fixtures.ValidServerManifest(), observed,
                Fixtures.ValidHashes(), policy, Fixtures.Now);
            Assert.Equal(RejectReason.WorldUidMismatch, d.Reason);
        }

        [Fact]
        public void WorldUidMatches_NameDoesNot_Rejected()
        {
            var observed = new WorldIdentity(Fixtures.DisposableUid, "different-name");
            var policy = new WorldPolicy(new[] { observed });
            var d = ArmingGate.Evaluate(Fixtures.ValidServerManifest(), observed,
                Fixtures.ValidHashes(), policy, Fixtures.Now);
            Assert.Equal(RejectReason.WorldNameMismatch, d.Reason);
        }

        // ── PROD-WORLD-REJECT: hard deny even if allowlisted/misconfigured ───
        [Theory]
        [InlineData(2456, "sbpr-qa-disposable-t022")] // production UID
        [InlineData(2466, "sbpr-qa-disposable-t022")] // production UID
        public void ProductionUid_HardDenied_EvenIfAllowlisted(long uid, string name)
        {
            var prod = new WorldIdentity(uid, name);
            // Deliberately misconfigure the allowlist to INCLUDE production.
            var policy = new WorldPolicy(new[] { prod });
            var m = new ArmManifest(true, "Server", "primary", prod, Fixtures.Nonce,
                Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, prod, Fixtures.ValidHashes(), policy, Fixtures.Now);
            Assert.Equal(RejectReason.ProductionWorldDenied, d.Reason);
        }

        [Theory]
        [InlineData("Niflheim-Main")]
        [InlineData("heistan-prod")]
        [InlineData("some-PROD-world")]
        public void ProductionNameMarker_HardDenied(string name)
        {
            var prod = new WorldIdentity(55555, name);
            var policy = new WorldPolicy(new[] { prod });
            var m = new ArmManifest(true, "Server", "primary", prod, Fixtures.Nonce,
                Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, prod, Fixtures.ValidHashes(), policy, Fixtures.Now);
            Assert.Equal(RejectReason.ProductionWorldDenied, d.Reason);
        }

        [Fact]
        public void WorldNotAllowlisted_Rejected()
        {
            var observed = new WorldIdentity(12345, "unknown-world");
            var m = new ArmManifest(true, "Server", "primary", observed, Fixtures.Nonce,
                Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            // Empty allowlist.
            var d = ArmingGate.Evaluate(m, observed, Fixtures.ValidHashes(), new WorldPolicy(null), Fixtures.Now);
            Assert.Equal(RejectReason.WorldNotAllowlisted, d.Reason);
        }

        // ── Hash manifest drift ──────────────────────────────────────────────
        [Fact]
        public void IncompleteHashManifest_Rejected()
        {
            var incomplete = new HashManifest(new Dictionary<string, string> { ["product"] = "p1" });
            var m = new ArmManifest(true, "Server", "primary", Fixtures.DisposableWorld(),
                Fixtures.Nonce, Fixtures.ArmExpiry, incomplete, new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.MalformedManifest, d.Reason);
        }

        [Fact]
        public void HashDriftOnAnyComponent_Rejected()
        {
            var observed = Fixtures.ValidHashes();
            observed["harmony"] = "DRIFTED";
            var d = ArmingGate.Evaluate(Fixtures.ValidServerManifest(), Fixtures.DisposableWorld(),
                observed, Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.HashManifestDrift, d.Reason);
        }

        [Fact]
        public void MissingObservedHashComponent_IsDrift()
        {
            var observed = Fixtures.ValidHashes();
            observed.Remove("scenario");
            var d = ArmingGate.Evaluate(Fixtures.ValidServerManifest(), Fixtures.DisposableWorld(),
                observed, Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.HashManifestDrift, d.Reason);
        }

        // ── Nonce / expiry / hmac / capability ───────────────────────────────
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void MissingNonce_Rejected(string? nonce)
        {
            var m = new ArmManifest(true, "Server", "primary", Fixtures.DisposableWorld(),
                nonce, Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.MissingNonce, d.Reason);
        }

        [Fact]
        public void ExpiryAtOrBeforeNow_Rejected()
        {
            var m = new ArmManifest(true, "Server", "primary", Fixtures.DisposableWorld(),
                Fixtures.Nonce, Fixtures.Now, Fixtures.HashManifest(), new[] { "Ping" }, Fixtures.Secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.Expired, d.Reason);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void MissingHmacSecret_Rejected(string? secret)
        {
            var m = new ArmManifest(true, "Server", "primary", Fixtures.DisposableWorld(),
                Fixtures.Nonce, Fixtures.ArmExpiry, Fixtures.HashManifest(), new[] { "Ping" }, secret);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.MalformedManifest, d.Reason);
        }

        [Fact]
        public void EmptyCapability_Rejected()
        {
            var m = Fixtures.ValidServerManifest(new string[0]);
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.EmptyCapability, d.Reason);
        }

        [Fact]
        public void CapabilityWithUnknownVerb_Rejected()
        {
            var m = Fixtures.ValidServerManifest(new[] { "Ping", "NotARealVerb" });
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.UnknownVerb, d.Reason);
        }

        [Fact]
        public void CapabilityWithRoleInappropriateVerb_Rejected()
        {
            // Craft is a Client verb; a Server arm cannot list it.
            var m = Fixtures.ValidServerManifest(new[] { "Craft" });
            var d = ArmingGate.Evaluate(m, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            Assert.Equal(RejectReason.RoleMismatch, d.Reason);
        }
    }
}
