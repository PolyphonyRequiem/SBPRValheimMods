// ADR-0009 M1 — verb catalog + capability-manifest parser unit tests. Proves the
// catalog is a finite, closed, role-partitioned table and the parser accepts only a
// known, role-appropriate, duplicate-free, non-empty SUBSET (never a superset).
using System.Collections.Generic;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class CapabilityManifestTests
    {
        [Fact]
        public void UnknownVerb_IsNotKnown()
        {
            Assert.False(VerbCatalog.IsKnown("Nope"));
            Assert.Null(VerbCatalog.Get("Nope"));
            Assert.False(VerbCatalog.IsKnown(null));
        }

        [Theory]
        [InlineData("SpawnStation", HarnessRole.Server, true)]
        [InlineData("SpawnStation", HarnessRole.Client, false)]
        [InlineData("Craft", HarnessRole.Client, true)]
        [InlineData("Craft", HarnessRole.Server, false)]
        [InlineData("Ping", HarnessRole.Server, true)]
        [InlineData("Ping", HarnessRole.Client, true)]
        [InlineData("ReadTooltip", HarnessRole.Client, true)]
        public void VerbRolePartition_IsEnforced(string verb, HarnessRole role, bool allowed)
        {
            Assert.Equal(allowed, VerbCatalog.Get(verb)!.AllowsRole(role));
        }

        [Fact]
        public void EmptyManifest_Rejected()
        {
            Assert.Equal(RejectReason.EmptyCapability,
                CapabilityManifest.TryParse(new string[0], HarnessRole.Server, out _));
            Assert.Equal(RejectReason.EmptyCapability,
                CapabilityManifest.TryParse(null, HarnessRole.Server, out _));
        }

        [Fact]
        public void DuplicateVerb_Rejected()
        {
            Assert.Equal(RejectReason.MalformedManifest,
                CapabilityManifest.TryParse(new[] { "Ping", "Ping" }, HarnessRole.Server, out _));
        }

        [Fact]
        public void UnknownVerbInManifest_Rejected()
        {
            Assert.Equal(RejectReason.UnknownVerb,
                CapabilityManifest.TryParse(new[] { "Ping", "Ghost" }, HarnessRole.Server, out _));
        }

        [Fact]
        public void RoleInappropriateVerb_Rejected()
        {
            Assert.Equal(RejectReason.RoleMismatch,
                CapabilityManifest.TryParse(new[] { "Craft" }, HarnessRole.Server, out _));
        }

        [Fact]
        public void EmptyStringVerb_Malformed()
        {
            Assert.Equal(RejectReason.MalformedManifest,
                CapabilityManifest.TryParse(new[] { "" }, HarnessRole.Server, out _));
        }

        [Fact]
        public void ValidSubset_Parses()
        {
            var reason = CapabilityManifest.TryParse(
                new[] { "SpawnStation", "GrantVanillaMaterials", "Ping", "ReadInventory" },
                HarnessRole.Server, out var manifest);
            Assert.Equal(RejectReason.None, reason);
            Assert.NotNull(manifest);
            Assert.True(manifest!.Permits("SpawnStation"));
            Assert.False(manifest.Permits("PlaceVanillaPiece")); // known but not permitted this run
        }

        [Fact]
        public void ClientVerbs_ParseUnderClientRole()
        {
            var reason = CapabilityManifest.TryParse(
                new[] { "Craft", "UpgradeItem", "DropItem", "PickUpNearest", "TamperField", "ReadTooltip" },
                HarnessRole.Client, out var manifest);
            Assert.Equal(RejectReason.None, reason);
            Assert.True(manifest!.Permits("TamperField"));
        }
    }

    public class VerbArgBoundsTests
    {
        [Theory]
        [InlineData(1L, true)]
        [InlineData(64L, true)]
        [InlineData(0L, false)]
        [InlineData(65L, false)]
        public void BoundedInt_Bounds(long v, bool ok)
        {
            var arg = new VerbArg("qty", ArgKind.BoundedInt, 1, 64);
            Assert.Equal(ok, arg.IsInBounds(v));
        }

        [Fact]
        public void BoundedInt_RejectsNonIntTypes()
        {
            var arg = new VerbArg("qty", ArgKind.BoundedInt, 1, 64);
            Assert.False(arg.IsInBounds("5"));
            Assert.False(arg.IsInBounds(5.0));
            Assert.False(arg.IsInBounds(null));
        }

        [Theory]
        [InlineData(0.0, true)]
        [InlineData(8.0, true)]
        [InlineData(-0.1, false)]
        [InlineData(8.1, false)]
        public void BoundedDouble_Bounds(double v, bool ok)
        {
            var arg = new VerbArg("radius", ArgKind.BoundedDouble, 0, 8);
            Assert.Equal(ok, arg.IsInBounds(v));
        }

        [Fact]
        public void BoundedDouble_RejectsNaNInfinity()
        {
            var arg = new VerbArg("radius", ArgKind.BoundedDouble, 0, 8);
            Assert.False(arg.IsInBounds(double.NaN));
            Assert.False(arg.IsInBounds(double.PositiveInfinity));
        }

        [Fact]
        public void AllowlistedId_LengthBounded()
        {
            var arg = new VerbArg("prefab", ArgKind.AllowlistedId, 0, 0);
            Assert.True(arg.IsInBounds("piece_workbench"));
            Assert.False(arg.IsInBounds(""));            // empty rejected
            Assert.False(arg.IsInBounds(new string('x', 200))); // unbounded blob rejected
            Assert.False(arg.IsInBounds(42L));           // wrong type
        }

        [Fact]
        public void BoundedString_LengthBounds()
        {
            var arg = new VerbArg("slot", ArgKind.BoundedString, 1, 8);
            Assert.True(arg.IsInBounds("slot3"));
            Assert.False(arg.IsInBounds(""));
            Assert.False(arg.IsInBounds("toolongvalue"));
        }
    }

    public class HmacTests
    {
        [Fact]
        public void SameInputs_SameMac()
        {
            var c = RequestHmac.CanonicalString("n", 1, 2, "Server", 3, "Ping", "r");
            Assert.Equal(RequestHmac.Compute("s", c), RequestHmac.Compute("s", c));
        }

        [Fact]
        public void DifferentSecret_DifferentMac()
        {
            var c = RequestHmac.CanonicalString("n", 1, 2, "Server", 3, "Ping", "r");
            Assert.NotEqual(RequestHmac.Compute("s1", c), RequestHmac.Compute("s2", c));
        }

        [Fact]
        public void Verify_ConstantTimeMatch()
        {
            var c = RequestHmac.CanonicalString("n", 1, 2, "Server", 3, "Ping", "r");
            var mac = RequestHmac.Compute("s", c);
            Assert.True(RequestHmac.Verify(mac, mac));
            Assert.False(RequestHmac.Verify(mac, mac.Substring(0, mac.Length - 1) + "0"));
            Assert.False(RequestHmac.Verify(mac, "short"));
            Assert.False(RequestHmac.Verify(null!, mac));
        }
    }
}
