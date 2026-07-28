// spec-role-split-arm-gate.md §6 — engine-free unit tests for the role-split arm-gate readiness
// decision. The engine-bound sources (ZNetClientReadinessSource / ZNetWorldIdentitySource) touch
// ZNet/Player statics and cannot run headless, so the AND logic is factored into the engine-free
// ClientReadinessDecision and tested here against three injected booleans — mirroring how the
// fixture authority is faked. This proves AC2/AC3/AC5: ready ONLY when client-role AND spawned AND
// a live local player; not-ready when IsServer (SP/host), when the spawn flag is unset, or when the
// local player is gone (spawned-then-destroyed).
using SBPR.QaHarness.T022.Core;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class ClientReadinessDecisionTests
    {
        [Fact]
        public void Ready_onlyWhen_clientRole_and_spawned_and_livePlayer()
        {
            Assert.True(ClientReadinessDecision.Ready(clientRole: true, spawnedFlag: true, livePlayer: true));
        }

        [Fact]
        public void NotReady_whenServerRole_evenIfSpawnedAndLive()
        {
            // AC3: singleplayer/host return IsServer()==true => clientRole is false => never ready,
            // regardless of the spawn signal (OnSpawned DOES fire in SP/host).
            Assert.False(ClientReadinessDecision.Ready(clientRole: false, spawnedFlag: true, livePlayer: true));
        }

        [Fact]
        public void NotReady_whenSpawnFlagUnset()
        {
            // Client role, world-from-net could be present, but no local player has spawned yet.
            Assert.False(ClientReadinessDecision.Ready(clientRole: true, spawnedFlag: false, livePlayer: true));
        }

        [Fact]
        public void NotReady_whenLocalPlayerGone()
        {
            // AC2 defensive live re-read: spawned-then-destroyed player (m_localPlayer cleared).
            Assert.False(ClientReadinessDecision.Ready(clientRole: true, spawnedFlag: true, livePlayer: false));
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        public void NotReady_forEveryPartialCombination(bool clientRole, bool spawnedFlag, bool livePlayer)
        {
            Assert.False(ClientReadinessDecision.Ready(clientRole, spawnedFlag, livePlayer));
        }
    }
}
