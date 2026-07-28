// Client-role arm-time readiness source (spec-role-split-arm-gate.md §2 AC2/AC3/AC5, §4/§5) — CLEAN side.
//
// ATTRIBUTION (finding only, no code ported): keying "a local player is now spawned and in-world"
// off vanilla Player.OnSpawned + Player.m_localPlayer (rather than the server-only ZNet.World) is
// credited to MODSCAN-001 in ~/valheim/refs/jotunn/FINDINGS.md (Jotunn / JotunnLib Team, MIT) as a
// behavioural FINDING. Implementation is our own clean code against vanilla members verified present
// at the pinned game (assembly_valheim @ 0.221.12). The role split uses !ZNet.IsServer() because
// IsClientInstance() does NOT exist at this pin (verified absent — spec §0); if a future pin adds it,
// only this file changes behind the IClientReadinessSource seam.
//
// Reaching the game's own public/observed members (ZNet.IsServer, Player.m_localPlayer) is
// clean-room permitted: the wall is around OTHER mods, not the base game we mod (ADR-0001).
using System;
using SBPR.QaHarness.T022.Core;
using UnityEngine;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Reports arm-time readiness for the CLIENT role: ready only when this is a client instance
    /// joined to a remote server AND a local player has spawned in-world (spec AC2). Concretely,
    /// <see cref="Ready"/> ANDs three observed inputs via the engine-free
    /// <see cref="ClientReadinessDecision"/>:
    /// <list type="number">
    /// <item>role predicate <c>ZNet.instance != null &amp;&amp; !ZNet.instance.IsServer()</c> — the
    /// pinned-game substitute for the absent IsClientInstance(); IsServer() is true for
    /// dedicated AND host AND singleplayer, false only on a joined remote client (spec §0/AC3);</item>
    /// <item>the event-driven <c>PlayerOnSpawnedReadinessPatch._localPlayerSpawned</c> flag;</item>
    /// <item>a live re-read of <c>Player.m_localPlayer != null</c>, guarding a spawned-then-destroyed
    /// player (OnDestroy clears m_localPlayer).</item>
    /// </list>
    /// Fail-closed (spec AC5): any exception, null ZNet.instance, IsServer()==true, unset flag, or
    /// null m_localPlayer yields <c>Ready == false</c>. This source can only ever DELAY arming, never
    /// force it. Observed-only — never mutates game state.
    /// </summary>
    internal sealed class ZNetClientReadinessSource : IClientReadinessSource
    {
        public bool Ready
        {
            get
            {
                try
                {
                    bool clientRole = ZNet.instance != null && !ZNet.instance.IsServer();
                    bool spawnedFlag = PlayerOnSpawnedReadinessPatch._localPlayerSpawned;
                    bool livePlayer = Player.m_localPlayer != null;
                    return ClientReadinessDecision.Ready(clientRole, spawnedFlag, livePlayer);
                }
                catch (Exception)
                {
                    return false; // fail-closed: any read fault => not ready
                }
            }
        }
    }
}
