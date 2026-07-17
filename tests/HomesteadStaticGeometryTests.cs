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
            ["WoodHouse1"] = "F7D55B6E270BB43C9D7CBE38F227804A2F799205FAE132064676709F08AC9391",
            ["WoodHouse2"] = "3568AB0F7343C362C650F75867E9FBC7E27F5AD4CF9CFB5A56EEA9FA339151CB",
            ["WoodHouse3"] = "8B30D15DDA6CA06CACE991B8F09B9710501B913195D80A8F77F83FD40C08E948",
            ["WoodHouse4"] = "6361323FE04EE3DA1689E8F00EB39E9B3D3A504A68ADE4ACE2291469553CF882",
            ["WoodHouse5"] = "E94B77E76B7C369527D50669ED5D822E4FBBC749E8EDB88DEDA34C3A6ECA1D8D",
            ["WoodHouse6"] = "279BBF7B77CCC6E2174048CEDE2DD8B217E937ECBFFD25E9DA075BA9C3742248",
            ["WoodHouse7"] = "4CB39C108DB72A4FF89A79B7FC06AC5AB8BA7E19C02E5B13B8788B913BD43B12",
            ["WoodHouse8"] = "A415AC5155A7D5DE86B267D0981065F910C8D9D0AC8C725F83D0461CBEDBB370",
            ["WoodHouse9"] = "2C8A610A6B8CFF6828F5A208A8A4E73E67D8EF087B87547D2A1EA73AC582317B",
            ["WoodHouse10"] = "D92A3430B22E052D9DD02A6FA4B4FD22C57A63763CACE252F0F3F53C24FD3447",
            ["WoodHouse11"] = "928EB24820E127E4F9A89117A1928BF796AC09D8D5871C022DE1DB0427973423",
            ["WoodHouse12"] = "6191D3D36BA0CCA345954F7053CCF39D2F0B9912E7A594CBAE49B4CC75B3E1A4",
            ["WoodHouse13"] = "A6E784F678781FAD82FB349FE6B3E2EAEE60C3518BED029312BACA278DAEE23C",
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

        // ---- Generator manifest seam (Approach C) --------------------------------------------

        [Fact]
        public void Generator_host_requires_a_manifest_and_skips_explicitly_when_absent()
        {
            var candidate = Host("WoodVillage1", -30, -13, -1920.0, -832.0);

            var resolution = HomesteadPlacementResolver.ResolveGenerator(
                World, Selector, candidate, liveContentHash: "content-abc", HomesteadGeneratorManifest.Empty);

            Assert.Equal(HomesteadResolutionStatus.ManifestRequired, resolution.Status);
            Assert.Null(resolution.Record);
        }

        [Fact]
        public void Generator_host_accepts_a_matching_content_valid_manifest_row()
        {
            var candidate = Host("WoodVillage1", -30, -13, -1920.0, -832.0);
            var manifest = new HomesteadGeneratorManifest(new[]
            {
                new HomesteadManifestRow(World, Selector, "WoodVillage1", -30, -13, -1918.0, -833.0, 41.5, "content-abc"),
            });

            var resolution = HomesteadPlacementResolver.ResolveGenerator(World, Selector, candidate, "content-abc", manifest);

            Assert.Equal(HomesteadResolutionStatus.Resolved, resolution.Status);
            Assert.Equal(HomesteadSeatProvider.Manifest, resolution.Record!.Provider);
            Assert.Equal(-1918.0, resolution.Record!.SeatX, 6);
            Assert.Equal(41.5, resolution.Record!.SeatY, 6);
        }

        [Fact]
        public void Generator_host_rejects_a_manifest_row_whose_content_hash_drifted()
        {
            var candidate = Host("WoodVillage1", -30, -13, -1920.0, -832.0);
            var manifest = new HomesteadGeneratorManifest(new[]
            {
                new HomesteadManifestRow(World, Selector, "WoodVillage1", -30, -13, -1918.0, -833.0, 41.5, "content-OLD"),
            });

            var resolution = HomesteadPlacementResolver.ResolveGenerator(World, Selector, candidate, "content-NEW", manifest);

            Assert.Equal(HomesteadResolutionStatus.ManifestRequired, resolution.Status);
        }

        [Fact]
        public void Manifest_rejects_duplicate_keys_at_construction()
        {
            var row = new HomesteadManifestRow(World, Selector, "WoodVillage1", -30, -13, 0, 0, 0, "h");
            var dup = new HomesteadManifestRow(World, Selector, "WoodVillage1", -30, -13, 1, 1, 1, "h2");

            Assert.Throws<ArgumentException>(() => new HomesteadGeneratorManifest(new[] { row, dup }));
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
