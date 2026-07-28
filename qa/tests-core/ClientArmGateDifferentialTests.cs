// spec-role-split-arm-gate.md §2 (AC1/AC2/AC3/AC5) — DIFFERENTIAL acceptance tests for the
// role-split arm gate. These are constructed to FAIL against pre-fix main and PASS on the
// role-split branch, isolating the ONE behavioural change the split makes:
//
//   A joining remote client has ZNet.World != null (the world is rebuilt from network data,
//   spec §5 note *), so the OLD server-shaped gate main ships (arm-ready == worldLoaded) reports
//   READY for that client BEFORE any local player has spawned — it arms prematurely. The NEW
//   client-role readiness source refuses until the local player actually spawns in-world.
//
// The gate-under-test is reached through the single seam `RoleSplitArmGate.ClientArmReady(...)`
// (below). On this (post-fix) branch that seam delegates to the SHIPPED engine-free
// `ClientReadinessDecision.Ready` — the exact AND logic the runtime ZNetClientReadinessSource
// evaluates. The pre-fix main variant of this file (captured as differential evidence in the QA
// report) rebinds the SAME seam to main's ONLY available arm-readiness signal — worldLoaded — so
// `Test1_JoiningClient_preSpawn_notArmReady_despiteWorldFromNet` fails there with a real
// Assert.False failure. Nothing here raises max_attempts or any readiness timeout.
using SBPR.QaHarness.T022.Core;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    /// <summary>
    /// Single arm-readiness seam this differential suite drives, so the SAME test body runs against
    /// both the pre-fix and post-fix trees with only this one binding differing.
    ///
    /// POST-FIX (this branch): client arm-readiness is the role-split decision — ready IFF the client
    /// role predicate holds AND a local player has spawned AND that player is still live. The
    /// server-shaped <paramref name="worldLoaded"/> input is deliberately IGNORED for the client
    /// decision: a joining client's world-from-net must NOT arm it before its player spawns.
    ///
    /// PRE-FIX (main): main has no client source; its arm gate is purely <c>ZNet.World != null</c>.
    /// The main variant of this file binds <see cref="ClientArmReady"/> to <c>worldLoaded</c>, which
    /// makes Test1 fail — that is the differential evidence.
    /// </summary>
    internal static class RoleSplitArmGate
    {
        internal static bool ClientArmReady(bool worldLoaded, bool clientRole, bool spawnedFlag, bool livePlayer)
            => ClientReadinessDecision.Ready(clientRole, spawnedFlag, livePlayer);

        /// <summary>Server-role arm-readiness — unchanged by the split: world loaded (spec AC1).</summary>
        internal static bool ServerArmReady(bool worldLoaded) => worldLoaded;
    }

    public class ClientArmGateDifferentialTests
    {
        // ── Test 1 (THE differential — must FAIL against pre-fix main) ────────────────────────
        // Joining remote client: ZNet.World rebuilt from net (worldLoaded==true), role is client
        // (!IsServer), but no local player has spawned yet (spawnedFlag==false, m_localPlayer==null).
        // Spec §5 row "Client → remote server (pre-spawn)": client src Ready == false ⇒ NOT arm-ready.
        // The old ZNet.World gate would report ready here (worldLoaded==true) — that is exactly the
        // behaviour this test is built to reject, so on main (ClientArmReady==worldLoaded) it fails.
        [Fact]
        public void Test1_JoiningClient_preSpawn_notArmReady_despiteWorldFromNet()
        {
            const bool worldLoaded = true;  // world rebuilt from network on a joining client (spec §5 *)
            const bool clientRole = true;   // !ZNet.IsServer() on a joined remote client
            const bool spawnedFlag = false; // Player.OnSpawned has NOT fired yet
            const bool livePlayer = false;  // Player.m_localPlayer still null pre-spawn

            // Pre-condition sanity: the old server-shaped gate DOES consider this ready (the bug).
            Assert.True(RoleSplitArmGate.ServerArmReady(worldLoaded));

            // The role-split contract: client arm-readiness must be FALSE until the player spawns,
            // regardless of world-from-net. On pre-fix main ClientArmReady==worldLoaded==true, so
            // this assertion fails — the required fail-against-main evidence.
            Assert.False(
                RoleSplitArmGate.ClientArmReady(worldLoaded, clientRole, spawnedFlag, livePlayer),
                "A joining client must not be arm-ready before its local player spawns, even though " +
                "ZNet.World is non-null (rebuilt from net). The old ZNet.World gate arms it prematurely.");
        }

        // ── Test 1b — the same joining client becomes arm-ready ONCE the local player spawns ──
        [Fact]
        public void Test1b_JoiningClient_postSpawn_isArmReady()
        {
            // Spec §5 row "Client → remote server (post-spawn)": client src Ready == true.
            Assert.True(RoleSplitArmGate.ClientArmReady(
                worldLoaded: true, clientRole: true, spawnedFlag: true, livePlayer: true));
        }

        // ── Test 2 — singleplayer / host does NOT arm via the client source (AC3) ─────────────
        // SP/host return IsServer()==true ⇒ clientRole==false ⇒ client source never ready, even
        // though OnSpawned fires in SP/host (spawnedFlag==true) and the player is live.
        [Theory]
        [InlineData(true)]   // world present (SP/host always has a world)
        [InlineData(false)]
        public void Test2_SingleplayerOrHost_neverArmsViaClientSource(bool worldLoaded)
        {
            Assert.False(RoleSplitArmGate.ClientArmReady(
                worldLoaded, clientRole: false, spawnedFlag: true, livePlayer: true));
        }

        // ── Test 3 — server-role arming still gates on ZNet.World, identical to before (AC1) ──
        // The server readiness predicate is unchanged by the split: ready IFF world loaded, and
        // the client-only inputs (role/spawn/live) do not influence it.
        [Theory]
        [InlineData(true, true)]    // world loaded  ⇒ server arm-ready
        [InlineData(false, false)]  // world not loaded ⇒ not ready
        public void Test3_ServerRoleArming_gatesOnWorldLoaded_unchanged(bool worldLoaded, bool expectReady)
        {
            Assert.Equal(expectReady, RoleSplitArmGate.ServerArmReady(worldLoaded));

            // And a dedicated server (IsServer, never spawns a local player) is correctly NOT
            // client-arm-ready — it arms via the server path only (spec §5 row "Dedicated server").
            Assert.False(RoleSplitArmGate.ClientArmReady(
                worldLoaded, clientRole: false, spawnedFlag: false, livePlayer: false));
        }
    }
}
