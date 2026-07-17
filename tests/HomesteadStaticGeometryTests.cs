using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// R5 (t_2a8a8aaa) — engine-free static-geometry seat authority.
    ///
    /// These tests exercise the SPIKE-2-proven resolution architecture headless, against the REAL
    /// offline-extracted WoodHouse collider footprints (Fixtures/homestead-static-geometry.json) — the
    /// same geometry SPIKE 1's live Physics.OverlapSphere returned ZERO of on a headless server. No
    /// UnityEngine, no Physics, no Heightmap: proof the seat is derivable from static data alone.
    /// </summary>
    public sealed class HomesteadStaticGeometryTests
    {
        private const string World = "uid:-898655635";      // SPIKE 2 pinned fixture world UID
        private const string Selector = "niflheim-homestead-playtest-v1";

        // Golden C#-format semantic hashes for all 13 ordinary hosts, computed from the checked-in
        // fixture with the exact HomesteadGeometryHash formula. A drift in the shipped AssetBundle (or
        // the extractor) changes a host's geometry and trips exactly one of these — the version-drift
        // guard (INV-5) that forces a selector-version roll instead of silently reseating.
        private static readonly Dictionary<string, string> GoldenHash = new(StringComparer.Ordinal)
        {
            ["WoodHouse1"] = "ABCDC6D6A4D3B5ECC0FFE226489BB967CCC837029CFB41B7219B705C7E849DA3",
            ["WoodHouse2"] = "5C8325CF2BD9A1DFEC3E78DF65D2D3A44E1F43B11DAAD1C40984728387CA6708",
            ["WoodHouse3"] = "4BE3F6CC53E07DFE5B355B4F5511CFFB33CA86FE4127AA26A33138CD1ED356D3",
            ["WoodHouse4"] = "741767C20C84495746751047BCA5E0B62EADA16702AC89C2C85EC833722612AF",
            ["WoodHouse5"] = "AE39400D4320381D737F24766B60ED04E40606266846AC252A0B1650A251977D",
            ["WoodHouse6"] = "7618274A42E9A8E6B7B0D1C4F1373881823DD9A78774FD660FE619260A033E69",
            ["WoodHouse7"] = "977A48F1398C4330219751F3A2DC21F1C8C1534CA5224B7412F9EBE7656C6224",
            ["WoodHouse8"] = "64905D6D985FB32F7FE040F1FD0B8480361ACE99BE60DA008160408DC64EF675",
            ["WoodHouse9"] = "6C97D8842747D370F8B45DF46453FFB40E5A7D8560376A65EBB16E436CDB16D0",
            ["WoodHouse10"] = "760024E3C67E21B9D3D27CF852677432EC68EAB480CA830F06B1BA8A8FA77A40",
            ["WoodHouse11"] = "D985B2B8F3183CBC2F5A88C6F5D77F504576B8DDE3AA5EF057E1D820059AADCF",
            ["WoodHouse12"] = "3B41337E5C7AE72A111D7218F5E7D0914BE4BE94AD5E6BFCF589C6EBCDC09D33",
            ["WoodHouse13"] = "038271632D960F61152DBAE734FA5161EFD6AA1DA129122E9470732D967EA9D1",
        };

        // A permissive height function: seats are clamped to <=6 m where terrain is flat at host-origin Y,
        // so a constant is faithful to the level-radius invariant.
        private static WorldHeightFunction FlatHeight(double y) =>
            (double x, double z, out double h) => { h = y; return true; };

        // ---- Host classification (SPIKE 2 split) ---------------------------------------------

        [Theory]
        [InlineData("WoodHouse1", false)]
        [InlineData("WoodHouse13", false)]
        [InlineData("WoodFarm1", true)]
        [InlineData("WoodVillage1", true)]
        public void Host_classifier_splits_ordinary_from_generator(string prefab, bool isGenerator)
        {
            Assert.Equal(isGenerator, HomesteadHostClassifier.IsGenerator(prefab));
            Assert.Equal(
                isGenerator ? HomesteadHostClass.Generator : HomesteadHostClass.Ordinary,
                HomesteadHostClassifier.Classify(prefab));
        }

        [Fact]
        public void All_thirteen_ordinary_houses_resolve_a_real_seat_where_the_physics_scorer_found_none()
        {
            // SPIKE 1's live Physics.OverlapSphere scorer returned NO VALID SEAT at every host on a
            // headless server. The engine-free static-geometry resolver must find a seat for ALL 13.
            var failures = new List<string>();
            for (var i = 1; i <= 13; i++)
            {
                var prefab = "WoodHouse" + i;
                var geometry = Fixture.Load(prefab);
                var candidate = Host(prefab, i, -i, i * 100.0, -i * 50.0);
                var resolution = HomesteadPlacementResolver.ResolveOrdinary(
                    World, Selector, candidate, geometry, hostYawRadians: 0.0, height: FlatHeight(30.0));
                if (resolution.Status != HomesteadResolutionStatus.Resolved)
                    failures.Add($"{prefab}:{resolution.Status}");
                else
                    Assert.True(resolution.Record!.RadialFromHost <= HomesteadStaticSeatEvaluator.LevelRadius + 1e-9);
            }
            Assert.True(failures.Count == 0, "Hosts with no static-geometry seat: " + string.Join(", ", failures));
        }

        // ---- Real geometry: all 13 houses present, generators empty --------------------------

        [Theory]
        [InlineData("WoodHouse1")]
        [InlineData("WoodHouse2")]
        [InlineData("WoodHouse3")]
        [InlineData("WoodHouse4")]
        [InlineData("WoodHouse5")]
        [InlineData("WoodHouse6")]
        [InlineData("WoodHouse7")]
        [InlineData("WoodHouse8")]
        [InlineData("WoodHouse9")]
        [InlineData("WoodHouse10")]
        [InlineData("WoodHouse11")]
        [InlineData("WoodHouse12")]
        [InlineData("WoodHouse13")]
        public void Every_ordinary_house_has_static_colliders_and_pins_its_semantic_hash(string prefab)
        {
            var geometry = Fixture.Load(prefab);
            Assert.True(geometry.ColliderCount > 0, $"{prefab} must expose static colliders offline.");
            // Recomputed hash matches the extractor-written fixture hash AND the golden pin.
            Assert.Equal(GoldenHash[prefab], geometry.SemanticHash);
            Assert.Equal(GoldenHash[prefab], HomesteadGeometryHash.Compute(geometry.Footprints));
        }

        [Theory]
        [InlineData("WoodFarm1")]
        [InlineData("WoodVillage1")]
        public void Generator_hosts_have_zero_static_colliders(string prefab)
        {
            var geometry = Fixture.Load(prefab);
            Assert.Equal(0, geometry.ColliderCount);
        }

        // ---- Ordinary resolution: real seat, inside level radius, flat Y ---------------------

        [Fact]
        public void Ordinary_host_resolves_a_valid_seat_within_the_level_radius()
        {
            var geometry = Fixture.Load("WoodHouse13");
            var candidate = Host("WoodHouse13", 1, -2, 193.63, 126.33);

            var resolution = HomesteadPlacementResolver.ResolveOrdinary(
                World, Selector, candidate, geometry, hostYawRadians: 0.0, height: FlatHeight(46.873));

            Assert.Equal(HomesteadResolutionStatus.Resolved, resolution.Status);
            var record = resolution.Record!;
            Assert.Equal(HomesteadSeatProvider.StaticGeometry, record.Provider);
            Assert.Equal(46.873, record.SeatY, 3);
            Assert.Equal(geometry.SemanticHash, record.ContentHash);
            // INV-1: seat ring <= level radius so Y is exactly host-origin Y.
            Assert.True(record.RadialFromHost <= HomesteadStaticSeatEvaluator.LevelRadius + 1e-9,
                $"radial {record.RadialFromHost} must be within level radius.");
            // The chosen seat must genuinely clear structure (>= keepout).
            Assert.True(record.Clearance >= HomesteadStaticSeatEvaluator.SeatKeepOut);
        }

        [Fact]
        public void Ordinary_resolution_is_deterministic_pure_function_of_inputs()
        {
            var geometry = Fixture.Load("WoodHouse7");
            var candidate = Host("WoodHouse7", 10, 5, 640.0, 320.0);

            var a = HomesteadPlacementResolver.ResolveOrdinary(World, Selector, candidate, geometry, 0.0, FlatHeight(12.0)).Record!;
            var b = HomesteadPlacementResolver.ResolveOrdinary(World, Selector, candidate, geometry, 0.0, FlatHeight(12.0)).Record!;

            Assert.Equal(a, b);   // INV-3
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(11)]
        [InlineData(15)]
        public void Ordinary_resolution_holds_the_level_radius_invariant_across_16_rotations(int step)
        {
            var geometry = Fixture.Load("WoodHouse9");
            var candidate = Host("WoodHouse9", -30, -13, -1920.0, -832.0);
            var yaw = step * 22.5 * Math.PI / 180.0;   // vanilla m_randomRotation uses 22.5° steps

            var resolution = HomesteadPlacementResolver.ResolveOrdinary(
                World, Selector, candidate, geometry, yaw, FlatHeight(40.0));

            // Whatever the rotation, if a seat is found it MUST obey the <=6 m invariant and real clearance.
            if (resolution.Status == HomesteadResolutionStatus.Resolved)
            {
                Assert.True(resolution.Record!.RadialFromHost <= HomesteadStaticSeatEvaluator.LevelRadius + 1e-9);
                Assert.True(resolution.Record!.Clearance >= HomesteadStaticSeatEvaluator.SeatKeepOut);
            }
        }

        [Fact]
        public void Ordinary_resolution_skips_honestly_when_height_is_unavailable()
        {
            var geometry = Fixture.Load("WoodHouse13");
            var candidate = Host("WoodHouse13", 1, -2, 193.63, 126.33);
            WorldHeightFunction noHeight = (double x, double z, out double h) => { h = 0.0; return false; };

            var resolution = HomesteadPlacementResolver.ResolveOrdinary(World, Selector, candidate, geometry, 0.0, noHeight);

            Assert.Equal(HomesteadResolutionStatus.NoValidSeat, resolution.Status);
            Assert.Null(resolution.Record);
        }

        [Fact]
        public void Ordinary_resolution_fails_geometry_unavailable_on_empty_footprints()
        {
            var empty = new HomesteadHostGeometry("WoodHouse1",
                new List<StaticColliderFootprint>(), HomesteadGeometryHash.Compute(Array.Empty<StaticColliderFootprint>()));
            var candidate = Host("WoodHouse1", 0, 0, 0.0, 0.0);

            var resolution = HomesteadPlacementResolver.ResolveOrdinary(World, Selector, candidate, empty, 0.0, FlatHeight(10.0));

            Assert.Equal(HomesteadResolutionStatus.GeometryUnavailable, resolution.Status);
        }

        // ---- Seat evaluator: analytic clearance + level-radius rejection ---------------------

        [Fact]
        public void Seat_evaluator_rejects_seats_beyond_the_level_radius()
        {
            var geometry = new HomesteadHostGeometry("WoodHouse1",
                new List<StaticColliderFootprint> { new StaticColliderFootprint(0.0, 0.0, 1.0, 1.0) },
                "hash");
            var host = Host("WoodHouse1", 0, 0, 0.0, 0.0);
            var farSeat = new SeatCandidate(0, 7.0, 0.0);   // 7 m > 6 m level radius

            var evaluation = HomesteadStaticSeatEvaluator.Evaluate(host, geometry, 0.0, farSeat);

            Assert.False(evaluation.IsValid);
        }

        [Fact]
        public void Seat_evaluator_computes_true_clearance_from_a_single_box()
        {
            // Box half-extent 1 at local origin; seat 4 m away on +X, no rotation → clearance = 4 - 1 = 3.
            var geometry = new HomesteadHostGeometry("WoodHouse1",
                new List<StaticColliderFootprint> { new StaticColliderFootprint(0.0, 0.0, 1.0, 1.0) },
                "hash");
            var host = Host("WoodHouse1", 0, 0, 100.0, 200.0);
            var seat = new SeatCandidate(0, 104.0, 200.0);

            var evaluation = HomesteadStaticSeatEvaluator.Evaluate(host, geometry, 0.0, seat);

            Assert.True(evaluation.IsValid);
            Assert.Equal(3.0, evaluation.Clearance, 6);
            Assert.Equal(4.0, evaluation.RadialDistance, 6);
        }

        [Fact]
        public void Seat_evaluator_honours_host_rotation_when_mapping_to_local_space()
        {
            // A box offset to local +X=3. Under 90° host yaw, the world +Z direction maps to local +X, so a
            // seat placed at world +Z should sit near the box (low clearance), proving rotation is applied.
            var geometry = new HomesteadHostGeometry("WoodHouse1",
                new List<StaticColliderFootprint> { new StaticColliderFootprint(3.0, 0.0, 0.5, 0.5) },
                "hash");
            var host = Host("WoodHouse1", 0, 0, 0.0, 0.0);
            var yaw90 = Math.PI / 2.0;
            var seatOnWorldZ = new SeatCandidate(0, 0.0, 3.0);

            var rotated = HomesteadStaticSeatEvaluator.Evaluate(host, geometry, yaw90, seatOnWorldZ);
            var unrotated = HomesteadStaticSeatEvaluator.Evaluate(host, geometry, 0.0, seatOnWorldZ);

            // Rotated: seat lands next to the box → small clearance. Unrotated: box is on +X, seat on +Z →
            // farther, larger clearance. They must differ, proving yaw is load-bearing.
            Assert.True(rotated.Clearance < unrotated.Clearance);
        }

        // ---- Generator manifest seam (R6 operational manifest — Blocker 6) -------------------

        [Fact]
        public void Generator_host_requires_a_manifest_and_skips_explicitly_when_absent()
        {
            var candidate = Host("WoodVillage1", -30, -13, -1920.0, -832.0);

            var resolution = HomesteadPlacementResolver.ResolveGeneratorOperational(
                World, Selector, candidate, HomesteadOperationalManifest.Empty);

            Assert.Equal(HomesteadResolutionStatus.ManifestRequired, resolution.Status);
            Assert.Null(resolution.Record);
        }

        [Fact]
        public void Generator_host_accepts_a_matching_operational_manifest_row()
        {
            var candidate = Host("WoodVillage1", -30, -13, -1920.0, -832.0);
            // Zone (-30,-13) center = (-1920,-832); seat within the 32 m zone half-extent.
            var manifest = HomesteadOperationalManifest.Parse(
                "version=1\nworld=" + World + "\nselector=" + Selector + "\nprovider=op-v1\ngeneration=3\n" +
                "row\tWoodVillage1\t-30\t-13\t-1918.0\t-833.0\t41.5\tcontent-abc",
                World, Selector);

            var resolution = HomesteadPlacementResolver.ResolveGeneratorOperational(World, Selector, candidate, manifest);

            Assert.Equal(HomesteadResolutionStatus.Resolved, resolution.Status);
            Assert.Equal(HomesteadSeatProvider.Manifest, resolution.Record!.Provider);
            Assert.Equal(-1918.0, resolution.Record!.SeatX, 6);
            Assert.Equal(41.5, resolution.Record!.SeatY, 6);
            // Provenance stamped onto the record is the manifest DOCUMENT digest, not a per-row hash.
            Assert.Equal(manifest.DocumentDigest, resolution.Record!.ContentHash);
        }

        [Fact]
        public void Operational_manifest_rejects_out_of_zone_and_non_finite_rows()
        {
            var manifest = HomesteadOperationalManifest.Parse(
                "version=1\nworld=" + World + "\nselector=" + Selector + "\nprovider=op-v1\ngeneration=1\n" +
                "row\tWoodVillage1\t-30\t-13\t-1918.0\t-833.0\t41.5\tok-row\n" +      // valid
                "row\tWoodVillage1\t0\t0\t9999.0\t0.0\t10.0\tfar-row\n" +            // out of zone bounds
                "row\tWoodFarm1\t1\t1\tNaN\t0.0\t5.0\tnan-row",                       // non-finite
                World, Selector);

            Assert.Equal(1, manifest.Count);
            Assert.True(manifest.TryGet("WoodVillage1", -30, -13, out _));
            Assert.Equal(2, manifest.RejectedRows.Count);
        }

        [Fact]
        public void Operational_manifest_is_empty_when_world_or_provenance_mismatches()
        {
            // Wrong world → whole document is untrusted (Empty), supplies no seats.
            var wrongWorld = HomesteadOperationalManifest.Parse(
                "version=1\nworld=uid:999\nselector=" + Selector + "\nprovider=op-v1\ngeneration=1\n" +
                "row\tWoodVillage1\t-30\t-13\t-1918.0\t-833.0\t41.5\tr",
                World, Selector);
            Assert.True(wrongWorld.IsEmpty);

            // Missing provider provenance → Empty even if scope keys match.
            var noProvider = HomesteadOperationalManifest.Parse(
                "version=1\nworld=" + World + "\nselector=" + Selector + "\ngeneration=1\n" +
                "row\tWoodVillage1\t-30\t-13\t-1918.0\t-833.0\t41.5\tr",
                World, Selector);
            Assert.True(noProvider.IsEmpty);
        }

        // ---- helpers -------------------------------------------------------------------------

        private static HomesteadCandidate Host(string prefab, int zoneX, int zoneZ, double x, double z) =>
            new HomesteadCandidate(prefab, zoneX, zoneZ, x, z, locationRadius: 10.0);

        private static class Fixture
        {
            private static readonly Lazy<JsonElement> Doc = new(() =>
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "homestead-static-geometry.json");
                using var stream = File.OpenRead(path);
                return JsonDocument.Parse(stream).RootElement.Clone();
            });

            internal static HomesteadHostGeometry Load(string prefab)
            {
                var host = Doc.Value.GetProperty("hosts").GetProperty(prefab);
                var footprints = new List<StaticColliderFootprint>();
                foreach (var c in host.GetProperty("colliders").EnumerateArray())
                {
                    footprints.Add(new StaticColliderFootprint(
                        c.GetProperty("cx").GetDouble(),
                        c.GetProperty("cz").GetDouble(),
                        c.GetProperty("halfX").GetDouble(),
                        c.GetProperty("halfZ").GetDouble()));
                }
                // Compute the semantic hash with the C# formula so the record's ContentHash is self-consistent.
                return new HomesteadHostGeometry(prefab, footprints, HomesteadGeometryHash.Compute(footprints));
            }
        }
    }
}
