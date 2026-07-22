// Game-binding adapter STUBS (ADR-0009 §2/§3, PR #408 vanilla binding map) — M2.
//
// The canonical M2 helper (t_e596652b, adopted only after M1 ACCEPT+merge) will need
// to reach the vanilla Valheim seams the PR #408 "VANILLA-BINDINGS.md" map pins:
// world UID/name for the arming gate, main-thread scheduling, delivering-peer binding,
// and additive station/material fixtures. This file declares those seams as ENGINE-FREE
// interfaces plus inert, deterministic fake implementations — built ONLY from the
// accepted PR #408 behavioral DESCRIPTIONS, never from decompiled IronGate source
// (clean-room Chinese wall: this is CLEAN-side code written from a natural-language spec).
//
// Why interfaces + fakes and no game references:
//   • This assembly's engine-bound half (real UnityEngine/Valheim adapters implementing
//     these interfaces) is a LATER, separately-reviewed slice. Landing the boundary now
//     lets the control-plane core (dispatcher/peer-state/frame parser) and the M2 fixture
//     logic be wired and unit-tested headlessly against fakes, exactly like the M1
//     contract core is tested with no Valheim SDK.
//   • Keeping these in SBPR.QaHarness.T022.Core.ControlPlane (System.* only) means the
//     net8 tests-core suite link-compiles them and the net48 helper consumes the same
//     source — one definition, no fork.
//
// NOTHING here registers, listens, patches Harmony, invokes ZRpc, or mutates the game.
// The fakes are pure in-memory records for tests; the real adapters are TODO(M2-canonical).
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>
    /// World identity source (PR #408 §3.1: <c>ZNet.GetWorldUID()</c> / <c>ZNet.GetWorldName()</c>,
    /// backed by <c>World.m_uid</c> = name.GetStableHashCode()+GenerateUID() and <c>World.m_name</c>).
    /// The arming gate consumes UID+name; per the map, <c>ZNet.GetUID()</c> is the SESSION id and
    /// must NOT be used here. Observed-only — never mutated.
    /// </summary>
    public interface IWorldIdentitySource
    {
        /// <summary>True once ZNet has loaded/received a world (World.m_world non-null); facts are unreliable until then.</summary>
        bool WorldLoaded { get; }

        /// <summary>The durable per-world uid (World.m_uid), valid only when <see cref="WorldLoaded"/>.</summary>
        long WorldUid { get; }

        /// <summary>The world display/file name (World.m_name), valid only when <see cref="WorldLoaded"/>.</summary>
        string? WorldName { get; }

        /// <summary>True when running as the authoritative server (ZNet.IsServer()).</summary>
        bool IsServer { get; }
    }

    /// <summary>
    /// Main-thread scheduling seam (PR #408 §3.2: the helper's own <c>MonoBehaviour.Update()</c>
    /// pump — NEVER the shared Terminal/ScriptTools/ValBridge lock, §3.3). The dispatcher's
    /// live pump posts continuations here; the core FSM (ControlDispatcher) is clock-injected
    /// so it needs no scheduler in tests.
    /// </summary>
    public interface IMainThreadScheduler
    {
        /// <summary>Queue an action to run on the next helper Update tick. Must acquire no game console/ScriptTools lock.</summary>
        void Post(Action action);

        /// <summary>Milliseconds since some fixed epoch, read on the main thread — the clock the dispatcher expires deadlines against.</summary>
        long NowUnixMs { get; }
    }

    /// <summary>
    /// Delivering-peer source (PR #408 §3.4: <c>ZNet.OnNewConnection(ZNetPeer)</c>,
    /// <c>ZNetPeer.m_rpc</c>, <c>ZRpc.Register/Invoke</c>). The server binds the ACTUAL
    /// delivering peer here and feeds it to <see cref="DeliveringPeerState"/>. Observed-only:
    /// the adapter reports which peer delivered a call; it does not open a host listener.
    /// </summary>
    public interface IDeliveringPeerSource
    {
        /// <summary>The actual peer id the transport observed for the in-flight ZRpc invocation, or null outside one.</summary>
        string? CurrentDeliveringPeerId { get; }
    }

    /// <summary>
    /// Additive vanilla fixture seam (PR #408 §3.5: <c>ZNetScene.GetPrefab</c>,
    /// <c>ObjectDB.GetItemPrefab</c>, <c>ItemDrop.DropItem</c>, <c>CraftingStation</c>).
    /// ADR-0006 additive-only: read vanilla prefabs as blueprints, spawn via the same
    /// server-authoritative seams the game uses — NEVER clone-and-strip. M2 fixtures call
    /// this to place a station / grant allowlisted materials / place a piece; the real
    /// adapter lands in the canonical M2 slice. Only ALLOWLISTED ids may be requested
    /// (the allowlist is enforced above this seam).
    /// </summary>
    public interface IVanillaFixtureSeam
    {
        /// <summary>True when the named prefab exists in ZNetScene (a known vanilla/allowlisted id).</summary>
        bool PrefabExists(string prefabName);

        /// <summary>
        /// Spawn an allowlisted vanilla station/piece prefab at a bounded offset; returns a
        /// stable spawned-instance id recorded in the owned-resource ledger for cleanup.
        /// </summary>
        string SpawnPrefab(string prefabName, double posRadius);

        /// <summary>Grant a bounded quantity of an allowlisted vanilla item; returns a spawned-instance id for the ledger.</summary>
        string GrantItem(string itemId, long qty);

        /// <summary>Remove a previously spawned instance (cleanup). True if it existed and was removed.</summary>
        bool Despawn(string spawnedInstanceId);

        /// <summary>
        /// True iff the spawned instance is still live in the world (M3, crash-recovery reconcile).
        /// The owned-resource ledger calls this to reconcile its belief against world truth after a
        /// crash/reload: an instance the seam no longer reports live is treated as gone.
        /// </summary>
        bool IsLiveInstance(string spawnedInstanceId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Inert deterministic fakes — for headless tests of the control-plane wiring.
    // These carry NO game dependency and perform NO real spawn; they only record.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>A fixed, in-memory <see cref="IWorldIdentitySource"/> for tests.</summary>
    public sealed class FakeWorldIdentitySource : IWorldIdentitySource
    {
        public bool WorldLoaded { get; set; }
        public long WorldUid { get; set; }
        public string? WorldName { get; set; }
        public bool IsServer { get; set; }
    }

    /// <summary>A manual-clock, immediate-or-deferred <see cref="IMainThreadScheduler"/> for tests.</summary>
    public sealed class FakeMainThreadScheduler : IMainThreadScheduler
    {
        private readonly List<Action> _pending = new();
        public long NowUnixMs { get; set; }

        public void Post(Action action)
        {
            if (action != null) _pending.Add(action);
        }

        /// <summary>Run every queued action in FIFO order (simulates one Update tick draining the post queue).</summary>
        public int Drain()
        {
            int n = 0;
            while (_pending.Count > 0)
            {
                var a = _pending[0];
                _pending.RemoveAt(0);
                a();
                n++;
            }
            return n;
        }

        public void Advance(long deltaMs) => NowUnixMs += deltaMs;
    }

    /// <summary>A settable-current-peer <see cref="IDeliveringPeerSource"/> for tests.</summary>
    public sealed class FakeDeliveringPeerSource : IDeliveringPeerSource
    {
        public string? CurrentDeliveringPeerId { get; set; }
    }

    /// <summary>
    /// An in-memory <see cref="IVanillaFixtureSeam"/> that records spawns instead of touching
    /// the game — lets the owned-resource ledger + cleanup be exercised headlessly.
    /// </summary>
    public sealed class FakeVanillaFixtureSeam : IVanillaFixtureSeam
    {
        private readonly HashSet<string> _knownPrefabs;
        private readonly Dictionary<string, string> _spawned = new(StringComparer.Ordinal);
        private long _seq;

        public FakeVanillaFixtureSeam(IEnumerable<string>? knownPrefabs = null)
            => _knownPrefabs = new HashSet<string>(knownPrefabs ?? Array.Empty<string>(), StringComparer.Ordinal);

        /// <summary>Live spawned-instance ids not yet despawned (the ledger's view).</summary>
        public IReadOnlyCollection<string> Live => _spawned.Keys;

        public bool PrefabExists(string prefabName) => _knownPrefabs.Contains(prefabName);

        public string SpawnPrefab(string prefabName, double posRadius)
        {
            string id = "spawn-" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _spawned[id] = prefabName;
            return id;
        }

        public string GrantItem(string itemId, long qty)
        {
            string id = "item-" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _spawned[id] = itemId;
            return id;
        }

        public bool Despawn(string spawnedInstanceId) => _spawned.Remove(spawnedInstanceId);

        public bool IsLiveInstance(string spawnedInstanceId) =>
            spawnedInstanceId != null && _spawned.ContainsKey(spawnedInstanceId);
    }
}
