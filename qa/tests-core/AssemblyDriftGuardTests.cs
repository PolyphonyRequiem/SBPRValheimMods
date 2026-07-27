// ADR-0009 M2R — AssemblyDriftGuard tests. Proves the live assembly_valheim identity must
// EXACTLY match one of the PR #408-pinned authorized builds (MVID + version + net version);
// anything else — unknown MVID, version drift under a matching MVID, or a null observation —
// fails CLOSED so a stale helper cannot drive a moved seam.
using System;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class AssemblyDriftGuardTests
    {
        private static readonly Guid ClientMvid = Guid.Parse("23db560f-3f87-4454-8fe1-c434da4f936a");
        private static readonly Guid ServerMvid = Guid.Parse("62393fbd-383b-447c-9ae7-7ae16afa654f");

        [Fact]
        public void AuthorizedClientBuild_Passes()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ClientMvid, "0.221.12", 36u));
            Assert.True(r.Ok);
            Assert.Equal("client-trailborne-modded-gui", r.MatchedLabel);
        }

        [Fact]
        public void AuthorizedServerBuild_Passes()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ServerMvid, "0.221.12", 36u));
            Assert.True(r.Ok);
            Assert.Equal("server-dedicated-niflheim-dl", r.MatchedLabel);
        }

        [Fact]
        public void UnknownMvid_FailsClosed()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(Guid.NewGuid(), "0.221.12", 36u));
            Assert.False(r.Ok);
            Assert.Equal("MvidNotAuthorized", r.Reason);
        }

        [Fact]
        public void GameVersionDrift_UnderMatchingMvid_FailsClosed()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ClientMvid, "0.222.0", 36u));
            Assert.False(r.Ok);
            Assert.Equal("GameVersionDrift", r.Reason);
        }

        [Fact]
        public void NetworkVersionDrift_UnderMatchingMvid_FailsClosed()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ServerMvid, "0.221.12", 37u));
            Assert.False(r.Ok);
            Assert.Equal("NetworkVersionDrift", r.Reason);
        }

        [Fact]
        public void NullObservation_FailsClosed()
        {
            var r = AssemblyDriftGuard.Check(null);
            Assert.False(r.Ok);
            Assert.Equal("ObservedAssemblyNull", r.Reason);
        }

        // ── M6-PIN: the Linux dedicated/GUI builds report a platform-prefixed runtime version
        // string (Version.GetVersionString() → "l-0.221.12" on SteamLinux). The same MVID is
        // already authorized; the prefix carries its own explicit ordinal-exact pin.

        [Fact]
        public void LinuxServerBuild_PlatformPrefixedVersion_Passes()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ServerMvid, "l-0.221.12", 36u));
            Assert.True(r.Ok);
            Assert.Equal("server-dedicated-niflheim-dl-linux", r.MatchedLabel);
        }

        [Fact]
        public void LinuxServerBuild_PrefixWithDriftedVersion_FailsGameVersionDrift()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ServerMvid, "l-0.221.13", 36u));
            Assert.False(r.Ok);
            Assert.Equal("GameVersionDrift", r.Reason);
        }

        [Fact]
        public void LinuxServerBuild_PrefixWithDriftedNetVersion_FailsNetworkVersionDrift()
        {
            var r = AssemblyDriftGuard.Check(new ObservedGameAssembly(ServerMvid, "l-0.221.12", 37u));
            Assert.False(r.Ok);
            Assert.Equal("NetworkVersionDrift", r.Reason);
        }
    }
}
