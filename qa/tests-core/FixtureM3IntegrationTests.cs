// ============================================================================
//  QA-M3 canonical named-AT + adversarial tests (t_4db82cc0) — engine-free.
// ----------------------------------------------------------------------------
//  Covers the canonical M3 named acceptance tests over the SHIPPED, re-homed
//  fixture core + the M3-only seam adapter, vanilla manifest, and execution-time
//  authority recheck:
//
//    AT-QA-FIXTURE-VANILLA-ONLY : only ordinary vanilla scaffolding is representable;
//                                 product ids (SBPR_ prefix / denylist) are refused at
//                                 the manifest AND again at the world seam boundary.
//    AT-QA-CLEANUP-NO-LEAK      : every created object is destroyed through the seam;
//                                 an unrelated pre-existing object is never touched.
//
//  Adversarial: product-prefab rejection (manifest + seam), non-additive clone
//  rejection (the seam exposes no clone path), bounds overflow via the real manifest
//  bounds, peer-substitution / stale-generation / non-admin / non-server / world-not-
//  loaded recheck rejection (each performs NO world side effect), partial failure,
//  crash reconcile through the seam, and unrelated-object preservation.
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using SBPR.QaHarness.T022.Core.ControlPlane;
using SBPR.QaHarness.T022.Core.Fixtures;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    // ── A recording IServerAuthoritySource for the execution-time recheck tests. ──
    internal sealed class FakeAuthority : IServerAuthoritySource
    {
        public bool IsServer { get; set; } = true;
        public bool WorldLoaded { get; set; } = true;
        public HashSet<string> Admins { get; } = new HashSet<string>(System.StringComparer.Ordinal);
        public bool IsAdmin(string deliveringPeerId) => deliveringPeerId != null && Admins.Contains(deliveringPeerId);
    }

    public sealed class VanillaFixtureManifestTests
    {
        // AT-QA-FIXTURE-VANILLA-ONLY — the manifest allowlist only carries ordinary vanilla ids,
        // each resolving to a NON-product category.
        [Fact]
        public void Manifest_AllowlistIsVanillaOnly_NonProductCategories()
        {
            var allow = VanillaFixtureManifest.BuildAllowlist();
            Assert.True(allow.Count > 0);
            foreach (var id in allow.LogicalIds)
            {
                Assert.False(VanillaFixtureManifest.IsProductId(id), "allowlisted id must not be a product id: " + id);
                Assert.True(allow.TryGetCategory(id, out var cat));
                // Every category is one of the three ordinary vanilla scaffolding kinds.
                Assert.Contains(cat, new[] { ResourceCategory.Material, ResourceCategory.Station, ResourceCategory.PlacementAnchor });
            }
        }

        [Theory]
        [InlineData("SBPR_Masterwork")]      // product prefab prefix
        [InlineData("sbpr_workmanship")]     // case-folded prefix evasion
        [InlineData("Masterwork")]           // denylisted product artifact
        [InlineData("attunement")]           // denylisted, case-folded
        [InlineData("")]                     // empty => fail closed
        [InlineData(null)]                   // null  => fail closed
        public void Manifest_RejectsProductIds(string? id)
        {
            Assert.True(VanillaFixtureManifest.IsProductId(id));
        }

        [Theory]
        [InlineData("Wood")]
        [InlineData("piece_workbench")]
        [InlineData("forge")]
        [InlineData("FixtureAnchor")]
        public void Manifest_AcceptsOrdinaryVanillaIds(string id)
        {
            Assert.False(VanillaFixtureManifest.IsProductId(id));
        }

        // A plan naming a product id is rejected as UnknownLogicalId (it is never on the vanilla
        // allowlist), so the validator refuses to expand it — product state is unrepresentable.
        [Fact]
        public void Validator_RejectsProductId_AsUnknown()
        {
            var allow = VanillaFixtureManifest.BuildAllowlist();
            var plan = new FixturePlan("fx", new[] { new ResourceSpec("SBPR_Masterwork", 1, 1.0) });
            var r = FixturePlanValidator.Validate(plan, allow, VanillaFixtureManifest.Bounds);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.UnknownLogicalId, r.Reason);
        }

        // Real manifest bounds: a count past MaxCountPerResource is refused before any world effect.
        [Fact]
        public void Validator_EnforcesRealManifestBounds()
        {
            var allow = VanillaFixtureManifest.BuildAllowlist();
            var bounds = VanillaFixtureManifest.Bounds;
            var plan = new FixturePlan("fx", new[] { new ResourceSpec("Wood", (int)(bounds.MaxCountPerResource + 1), 1.0) });
            var r = FixturePlanValidator.Validate(plan, allow, bounds);
            Assert.False(r.Accepted);
            Assert.Equal(PlanRejectionReason.CountOverflow, r.Reason);
        }
    }

    public sealed class SeamFixtureWorldTests
    {
        private static FakeVanillaFixtureSeam Seam(params string[] known) => new FakeVanillaFixtureSeam(known);

        private static ValidatedFixturePlan ValidPlan(string fixtureId, params (string id, int count, double radius)[] specs)
        {
            var allow = VanillaFixtureManifest.BuildAllowlist();
            var plan = new FixturePlan(fixtureId, specs.Select(s => new ResourceSpec(s.id, s.count, s.radius)));
            var r = FixturePlanValidator.Validate(plan, allow, VanillaFixtureManifest.Bounds);
            Assert.True(r.Accepted, r.Reason.ToString());
            return r.Plan!;
        }

        // AT-QA-CLEANUP-NO-LEAK (through the real seam adapter): ensure creates through the seam,
        // cleanup despawns every created instance, and the seam ends empty.
        [Fact]
        public void EnsureThenCleanup_NoLeak_ThroughSeam()
        {
            var seam = Seam("piece_workbench", "Wood");
            var world = new SeamFixtureWorld(seam);
            var ledger = OwnedResourceLedger.ForPlan(ValidPlan("fx", ("piece_workbench", 1, 2.0), ("Wood", 3, 1.0)));

            var ensured = ledger.Ensure(world);
            Assert.Equal(4, ensured.Created);
            Assert.True(ensured.FullySatisfied);
            Assert.Equal(4, seam.Live.Count);

            var cleaned = ledger.Cleanup(world);
            Assert.True(cleaned.FullyCleaned);
            Assert.Empty(seam.Live);
        }

        // AT-QA-CLEANUP-NO-LEAK (unrelated preservation): an object the ledger did not create has
        // no handle in the ledger, so cleanup cannot reach it — it survives.
        [Fact]
        public void Cleanup_PreservesUnrelatedSeamObject()
        {
            var seam = Seam("piece_workbench");
            // Independently spawn an object NOT owned by the ledger.
            string unrelated = seam.SpawnPrefab("piece_workbench", 1.0);
            var world = new SeamFixtureWorld(seam);
            var ledger = OwnedResourceLedger.ForPlan(ValidPlan("fx", ("piece_workbench", 1, 2.0)));

            ledger.Ensure(world);
            ledger.Cleanup(world);

            // The ledger's own object is gone; the unrelated one is untouched.
            Assert.True(seam.IsLiveInstance(unrelated));
            Assert.Single(seam.Live);
        }

        // Product-id rejection at the WORLD boundary (defence in depth): even if a product id were
        // somehow handed to the adapter (bypassing the validator), Create fails closed.
        [Fact]
        public void Seam_RejectsProductId_AtWorldBoundary()
        {
            var seam = Seam("SBPR_Masterwork"); // pretend the prefab even "exists"
            var world = new SeamFixtureWorld(seam);
            var id = new OwnedResourceId("fx", "SBPR_Masterwork", 0);
            var op = world.Create(id, ResourceCategory.Station, "SBPR_Masterwork", 2.0);
            Assert.False(op.Ok);
            Assert.Contains("product-id-refused", op.FailureReason);
            Assert.Empty(seam.Live); // nothing was spawned
        }

        // Non-additive clone rejection: the seam exposes ONLY additive Spawn/Grant — there is no
        // clone-and-strip method reachable from the adapter, so a subtractive base cannot be built.
        [Fact]
        public void Seam_ExposesNoNonAdditiveClonePath()
        {
            var methods = typeof(IVanillaFixtureSeam).GetMethods().Select(m => m.Name).ToArray();
            // Only additive/observe/remove operations exist; no Clone/Instantiate/Strip/Mutate.
            Assert.Contains("SpawnPrefab", methods);
            Assert.Contains("GrantItem", methods);
            Assert.DoesNotContain(methods, m =>
                m.Contains("Clone") || m.Contains("Instantiate") || m.Contains("Strip") || m.Contains("Mutate"));
        }

        // Live drift guard: a prefab the seam does not know is refused (unknown-prefab), no spawn.
        [Fact]
        public void Seam_RejectsUnknownPrefab()
        {
            var seam = Seam(/* nothing known */);
            var world = new SeamFixtureWorld(seam);
            var id = new OwnedResourceId("fx", "piece_workbench", 0);
            var op = world.Create(id, ResourceCategory.Station, "piece_workbench", 2.0);
            Assert.False(op.Ok);
            Assert.Contains("unknown-prefab", op.FailureReason);
        }

        // Partial failure through the seam: an injected create failure marks the entry Failed and a
        // later ensure retries only the missing tail (idempotency).
        [Fact]
        public void Seam_PartialFailure_ThenRetry()
        {
            var seam = new FailingSeam("piece_workbench", "Wood");
            var world = new SeamFixtureWorld(seam);
            var ledger = OwnedResourceLedger.ForPlan(ValidPlan("fx", ("Wood", 2, 1.0)));

            seam.FailGrant = true;
            var first = ledger.Ensure(world);
            Assert.Equal(0, first.Created);
            Assert.Equal(2, first.Failed);
            Assert.False(first.FullySatisfied);

            seam.FailGrant = false;
            var second = ledger.Ensure(world);
            Assert.Equal(2, second.Created);
            Assert.True(second.FullySatisfied);
        }

        // Crash reconcile through the seam: a created instance that vanished (world wipe) is
        // downgraded so ensure re-creates it — no leak, no double-create.
        [Fact]
        public void Seam_CrashReconcile_ReCreatesVanished()
        {
            var seam = Seam("piece_workbench");
            var world = new SeamFixtureWorld(seam);
            var ledger = OwnedResourceLedger.ForPlan(ValidPlan("fx", ("piece_workbench", 2, 2.0)));
            ledger.Ensure(world);
            Assert.Equal(2, seam.Live.Count);

            // Simulate a crash: the world loses the objects (despawn all live).
            foreach (var h in seam.Live.ToArray()) seam.Despawn(h);

            int downgraded = ledger.ReconcileWithWorld(world);
            Assert.Equal(2, downgraded);
            var re = ledger.Ensure(world);
            Assert.Equal(2, re.Created);
            Assert.Equal(2, seam.Live.Count);
        }
    }

    // A seam that can be told to fail its grant, for the partial-failure path.
    internal sealed class FailingSeam : IVanillaFixtureSeam
    {
        private readonly HashSet<string> _known;
        private readonly Dictionary<string, string> _live = new(System.StringComparer.Ordinal);
        private long _seq;
        public bool FailGrant { get; set; }
        public bool FailSpawn { get; set; }

        public FailingSeam(params string[] known) => _known = new HashSet<string>(known, System.StringComparer.Ordinal);
        public IReadOnlyCollection<string> Live => _live.Keys;
        public bool PrefabExists(string prefabName) => _known.Contains(prefabName);

        public string SpawnPrefab(string prefabName, double posRadius)
        {
            if (FailSpawn) throw new System.InvalidOperationException("injected spawn failure");
            string id = "spawn-" + (++_seq);
            _live[id] = prefabName;
            return id;
        }

        public string GrantItem(string itemId, long qty)
        {
            if (FailGrant) throw new System.InvalidOperationException("injected grant failure");
            string id = "item-" + (++_seq);
            _live[id] = itemId;
            return id;
        }

        public bool Despawn(string spawnedInstanceId) => _live.Remove(spawnedInstanceId);
        public bool IsLiveInstance(string spawnedInstanceId) => _live.ContainsKey(spawnedInstanceId);
    }

    public sealed class FixtureAuthorityRecheckTests
    {
        private static (FixtureProvisioner prov, OwnedResourceLedger ledger, FakeVanillaFixtureSeam seam)
            Setup(FakeAuthority auth, DeliveringPeerState peers)
        {
            var seam = new FakeVanillaFixtureSeam(new[] { "piece_workbench" });
            var world = new SeamFixtureWorld(seam);
            var allow = VanillaFixtureManifest.BuildAllowlist();
            var plan = new FixturePlan("fx", new[] { new ResourceSpec("piece_workbench", 1, 2.0) });
            var validated = FixturePlanValidator.Validate(plan, allow, VanillaFixtureManifest.Bounds).Plan!;
            var ledger = OwnedResourceLedger.ForPlan(validated);
            return (new FixtureProvisioner(auth, peers, world), ledger, seam);
        }

        // Happy path: server + world-loaded + bound admin peer on current generation -> ensure runs.
        [Fact]
        public void Recheck_Accepts_ServerAdminBoundPeer()
        {
            var auth = new FakeAuthority();
            auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var (prov, ledger, seam) = Setup(auth, peers);

            var res = prov.Ensure(ledger, "owner", bind.Generation);
            Assert.NotNull(res);
            Assert.True(prov.LastDecision.Ok);
            Assert.Single(seam.Live);
        }

        // AT-QA-REMOTE / peer-substitution: a different delivering peer is refused, NO world effect.
        [Fact]
        public void Recheck_RejectsPeerSubstitution_NoWorldEffect()
        {
            var auth = new FakeAuthority();
            auth.Admins.Add("owner");
            auth.Admins.Add("intruder");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var (prov, ledger, seam) = Setup(auth, peers);

            var res = prov.Ensure(ledger, "intruder", bind.Generation);
            Assert.Null(res);
            Assert.Equal(FixtureAuthorityReason.PeerRejected, prov.LastDecision.Reason);
            Assert.Empty(seam.Live);
        }

        // Stale generation (post-reconnect replay) is refused, NO world effect.
        [Fact]
        public void Recheck_RejectsStaleGeneration_NoWorldEffect()
        {
            var auth = new FakeAuthority();
            auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            peers.Bind("owner");   // gen 1
            peers.Bind("owner");   // reconnect: gen 2
            var (prov, ledger, seam) = Setup(auth, peers);

            var res = prov.Ensure(ledger, "owner", 1); // stale
            Assert.Null(res);
            Assert.Equal(FixtureAuthorityReason.PeerRejected, prov.LastDecision.Reason);
            Assert.Empty(seam.Live);
        }

        // Admin revoked between arm and execution: the RE-READ refuses, NO world effect.
        [Fact]
        public void Recheck_RejectsNonAdmin_NoWorldEffect()
        {
            var auth = new FakeAuthority(); // owner is NOT admin
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var (prov, ledger, seam) = Setup(auth, peers);

            var res = prov.Ensure(ledger, "owner", bind.Generation);
            Assert.Null(res);
            Assert.Equal(FixtureAuthorityReason.NotAdmin, prov.LastDecision.Reason);
            Assert.Empty(seam.Live);
        }

        // Not the authoritative server: refused, NO world effect.
        [Fact]
        public void Recheck_RejectsNonServer_NoWorldEffect()
        {
            var auth = new FakeAuthority { IsServer = false };
            auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var (prov, ledger, seam) = Setup(auth, peers);

            var res = prov.Ensure(ledger, "owner", bind.Generation);
            Assert.Null(res);
            Assert.Equal(FixtureAuthorityReason.NotServerRole, prov.LastDecision.Reason);
            Assert.Empty(seam.Live);
        }

        // World not loaded yet: refused, NO world effect.
        [Fact]
        public void Recheck_RejectsWorldNotLoaded_NoWorldEffect()
        {
            var auth = new FakeAuthority { WorldLoaded = false };
            auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var (prov, ledger, seam) = Setup(auth, peers);

            var res = prov.Ensure(ledger, "owner", bind.Generation);
            Assert.Null(res);
            Assert.Equal(FixtureAuthorityReason.WorldNotLoaded, prov.LastDecision.Reason);
            Assert.Empty(seam.Live);
        }

        // Cleanup is gated the same way: a peer that loses authority cannot force cleanup either.
        [Fact]
        public void Recheck_GatesCleanup_Too()
        {
            var auth = new FakeAuthority();
            auth.Admins.Add("owner");
            var peers = new DeliveringPeerState();
            var bind = peers.Bind("owner");
            var (prov, ledger, seam) = Setup(auth, peers);

            // Create legitimately.
            Assert.NotNull(prov.Ensure(ledger, "owner", bind.Generation));
            Assert.Single(seam.Live);

            // Now the peer loses admin; cleanup is refused and the object remains owned.
            auth.Admins.Clear();
            var res = prov.Cleanup(ledger, "owner", bind.Generation);
            Assert.Null(res);
            Assert.Equal(FixtureAuthorityReason.NotAdmin, prov.LastDecision.Reason);
            Assert.Single(seam.Live);

            // Restore admin and cleanup succeeds with no leak.
            auth.Admins.Add("owner");
            var ok = prov.Cleanup(ledger, "owner", bind.Generation);
            Assert.NotNull(ok);
            Assert.True(ok!.FullyCleaned);
            Assert.Empty(seam.Live);
        }
    }
}
