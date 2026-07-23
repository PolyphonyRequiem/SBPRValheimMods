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
using SBPR.QaHarness.T022.Core.Fixtures;

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
        /// Additively construct an allowlisted vanilla station/piece shell for the explicit
        /// <paramref name="category"/> at a bounded offset (ADR-0006:
        /// new GameObject + intended components from a read-only blueprint, never a prefab clone), durably
        /// stamp <paramref name="markerPayload"/> onto its ZDO as part of construction, and return a stable
        /// spawned-instance id recorded in the owned-resource ledger for cleanup. Returns empty on failure
        /// (including a failure to durably stamp the marker — the half-built object is destroyed).
        /// </summary>
        string SpawnPrefab(string prefabName, ResourceCategory category, double posRadius, string markerPayload);

        /// <summary>Grant a bounded quantity of an allowlisted vanilla item, durably stamping
        /// <paramref name="markerPayload"/> onto the spawned drop's ZDO; returns a spawned-instance id for
        /// the ledger (empty on failure, including marker-write failure).</summary>
        string GrantItem(string itemId, long qty, string markerPayload);

        /// <summary>Remove a previously spawned instance (cleanup). True if it existed and was removed.</summary>
        bool Despawn(string spawnedInstanceId);

        /// <summary>
        /// True iff the spawned instance is still live in the world (M3, crash-recovery reconcile).
        /// The owned-resource ledger calls this to reconcile its belief against world truth after a
        /// crash/reload: an instance the seam no longer reports live is treated as gone.
        /// </summary>
        bool IsLiveInstance(string spawnedInstanceId);

        /// <summary>
        /// Enumerate live QA-marked world objects INSIDE the bounded fixture region described by
        /// <paramref name="scope"/> — a pinned spatial/sector query around the deterministic fixture
        /// origin, limited to <see cref="FixtureSeamScope.MaxRadiusMeters"/>, the allowlisted prefab
        /// names, and a hard <see cref="FixtureSeamScope.MaxCandidates"/> cap. This is NOT a whole-world
        /// scan: an object outside the bounded region, of a non-allowlisted prefab, or unmarked is never
        /// returned, so recovery can never adopt or destroy it.
        ///
        /// The result is a TYPED complete/refused outcome. Any binding failure, enumeration exception,
        /// per-candidate read/handle error, or candidate-cap overflow MUST yield
        /// <see cref="SeamDiscoveryOutcome.Refused"/> with ZERO candidates and ZERO world mutation — the
        /// engine-free layer treats a refusal as fail-closed and never adopts a partial list. On success
        /// the outcome is <see cref="SeamDiscoveryOutcome.Complete"/> with the exact in-region candidate
        /// set (possibly empty).
        /// </summary>
        SeamDiscoveryResult DiscoverMarked(FixtureSeamScope scope);
    }

    /// <summary>A live QA-marked world object as reported by the seam: its raw marker payload + handle.</summary>
    public readonly struct MarkedInstanceInfo
    {
        public MarkedInstanceInfo(string markerPayload, string spawnedInstanceId)
        {
            MarkerPayload = markerPayload ?? string.Empty;
            SpawnedInstanceId = spawnedInstanceId ?? string.Empty;
        }

        public string MarkerPayload { get; }
        public string SpawnedInstanceId { get; }
    }

    /// <summary>
    /// The bounded query the engine-free recovery hands the seam so a survivor scan is a PINNED
    /// spatial/sector lookup, never a whole-world walk. It names the allowlisted prefab names the
    /// current plan expects, the maximum fixture radius (meters) the scan may reach from the
    /// deterministic fixture origin, and a hard cap on the number of allowlisted candidates the scan may
    /// inspect before it refuses (overflow ⇒ refuse, never truncate-and-guess).
    /// </summary>
    public readonly struct FixtureSeamScope
    {
        public FixtureSeamScope(IReadOnlyCollection<string> allowedPrefabNames, double maxRadiusMeters, int maxCandidates)
        {
            AllowedPrefabNames = allowedPrefabNames ?? Array.Empty<string>();
            MaxRadiusMeters = maxRadiusMeters;
            MaxCandidates = maxCandidates;
        }

        /// <summary>The allowlisted logical prefab/item names the current plan expects (the ONLY prefabs the scan matches).</summary>
        public IReadOnlyCollection<string> AllowedPrefabNames { get; }

        /// <summary>The maximum radius (meters) from the fixture origin the bounded scan may reach.</summary>
        public double MaxRadiusMeters { get; }

        /// <summary>Hard cap on in-region allowlisted candidates; exceeding it refuses the whole scan (fail-closed).</summary>
        public int MaxCandidates { get; }
    }

    /// <summary>Whether the bounded marker scan produced a complete candidate set or refused (fail-closed).</summary>
    public enum SeamDiscoveryOutcome
    {
        /// <summary>The scan completed; <see cref="SeamDiscoveryResult.Marked"/> is the exact in-region set (possibly empty).</summary>
        Complete = 0,

        /// <summary>The scan could not be completed safely (binding/enumeration/read fault or cap overflow) — refuse, adopt nothing.</summary>
        Refused = 1,
    }

    /// <summary>The typed outcome of a bounded marker scan: complete-with-candidates or refused-with-detail.</summary>
    public sealed class SeamDiscoveryResult
    {
        private SeamDiscoveryResult(SeamDiscoveryOutcome outcome, IReadOnlyList<MarkedInstanceInfo> marked, string detail)
        {
            Outcome = outcome;
            Marked = marked ?? Array.Empty<MarkedInstanceInfo>();
            Detail = detail ?? string.Empty;
        }

        public SeamDiscoveryOutcome Outcome { get; }
        public IReadOnlyList<MarkedInstanceInfo> Marked { get; }
        public string Detail { get; }

        public bool Ok => Outcome == SeamDiscoveryOutcome.Complete;

        public static SeamDiscoveryResult Complete(IReadOnlyList<MarkedInstanceInfo> marked) =>
            new SeamDiscoveryResult(SeamDiscoveryOutcome.Complete, marked, string.Empty);

        public static SeamDiscoveryResult Refused(string detail) =>
            new SeamDiscoveryResult(SeamDiscoveryOutcome.Refused, Array.Empty<MarkedInstanceInfo>(), detail);
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
        private readonly Dictionary<string, string> _spawned = new(StringComparer.Ordinal); // handle -> prefab/item
        private readonly Dictionary<string, string> _markers = new(StringComparer.Ordinal); // handle -> marker payload
        private long _seq;

        public FakeVanillaFixtureSeam(IEnumerable<string>? knownPrefabs = null)
            => _knownPrefabs = new HashSet<string>(knownPrefabs ?? Array.Empty<string>(), StringComparer.Ordinal);

        /// <summary>Live spawned-instance ids not yet despawned (the ledger's view).</summary>
        public IReadOnlyCollection<string> Live => _spawned.Keys;

        /// <summary>Adversarial knob: when true, the next spawn/grant durably-fails to write its marker
        /// and returns empty (the half-built object is discarded) — proves marker-write failure is a
        /// Create failure, not a silent untracked leak.</summary>
        public bool FailMarkerWrite { get; set; }

        public bool PrefabExists(string prefabName) => _knownPrefabs.Contains(prefabName);

        public string SpawnPrefab(string prefabName, ResourceCategory category, double posRadius, string markerPayload) =>
            Stamp("spawn", prefabName, markerPayload);

        public string GrantItem(string itemId, long qty, string markerPayload) => Stamp("item", itemId, markerPayload);

        private string Stamp(string kind, string logical, string markerPayload)
        {
            if (FailMarkerWrite) return string.Empty; // durable marker write failed => Create failed, no object tracked
            string id = kind + "-" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _spawned[id] = logical;
            _markers[id] = markerPayload ?? string.Empty;
            return id;
        }

        public bool Despawn(string spawnedInstanceId)
        {
            _markers.Remove(spawnedInstanceId);
            return _spawned.Remove(spawnedInstanceId);
        }

        public bool IsLiveInstance(string spawnedInstanceId) =>
            spawnedInstanceId != null && _spawned.ContainsKey(spawnedInstanceId);

        public IReadOnlyList<MarkedInstanceInfo> AllMarkedForTest()
        {
            var list = new List<MarkedInstanceInfo>();
            foreach (var kv in _markers)
                if (!string.IsNullOrEmpty(kv.Value)) list.Add(new MarkedInstanceInfo(kv.Value, kv.Key));
            return list;
        }

        /// <summary>Adversarial knob: force the next bounded scan to REFUSE (models a binding/enumeration/
        /// read fault or cap overflow). A refusal must adopt nothing (fail-closed).</summary>
        public bool FailDiscovery { get; set; }

        public SeamDiscoveryResult DiscoverMarked(FixtureSeamScope scope)
        {
            if (FailDiscovery)
                return SeamDiscoveryResult.Refused("injected discovery fault");

            var allowed = scope.AllowedPrefabNames as ICollection<string>;
            var list = new List<MarkedInstanceInfo>();
            foreach (var kv in _markers)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                // Bounded to the scope's allowlisted prefabs (mirrors the real seam's prefab-hash filter):
                // a marked object of a prefab the scope does not name is outside the bounded region here.
                if (allowed != null && allowed.Count > 0 &&
                    _spawned.TryGetValue(kv.Key, out var logical) && !allowed.Contains(logical))
                    continue;
                list.Add(new MarkedInstanceInfo(kv.Value, kv.Key));
            }
            // Hard candidate cap: overflow refuses the whole scan (never truncate-and-guess).
            if (scope.MaxCandidates > 0 && list.Count > scope.MaxCandidates)
                return SeamDiscoveryResult.Refused("candidate-cap-overflow: " + list.Count + " > " + scope.MaxCandidates);
            return SeamDiscoveryResult.Complete(list);
        }

        // ── Test-only adversarial seeding for crash/recovery scenarios ──

        /// <summary>Seed a live object carrying an EXACT marker payload but with NO snapshot recorded
        /// (models a crash-before-snapshot survivor). Returns the handle.</summary>
        public string SeedMarkedSurvivor(string logical, string markerPayload)
        {
            string id = "survivor-" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _spawned[id] = logical;
            _markers[id] = markerPayload ?? string.Empty;
            return id;
        }

        /// <summary>Seed an UNMARKED live object of the same prefab the harness uses (must be preserved
        /// — never discovered, adopted, or destroyed by recovery).</summary>
        public string SeedUnmarked(string logical)
        {
            string id = "unmarked-" + (++_seq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _spawned[id] = logical;
            return id;
        }
    }
}
