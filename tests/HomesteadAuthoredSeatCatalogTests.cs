using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SBPR.Niflheim.HomesteadStones.Domain;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class HomesteadAuthoredSeatCatalogTests
    {
        [Fact]
        public void Catalog_contains_exactly_the_thirteen_ordinary_houses()
        {
            Assert.Equal(13, HomesteadAuthoredSeatCatalog.Count);
            Assert.Equal(
                Enumerable.Range(1, 13).Select(i => "WoodHouse" + i).OrderBy(x => x, StringComparer.Ordinal),
                HomesteadAuthoredSeatCatalog.Prefabs);
            Assert.False(HomesteadAuthoredSeatCatalog.TryGet("WoodFarm1", out _));
            Assert.False(HomesteadAuthoredSeatCatalog.TryGet("WoodVillage1", out _));
        }

        [Fact]
        public void Every_authored_seat_is_finite_and_within_the_leveled_six_meter_radius()
        {
            foreach (var prefab in HomesteadAuthoredSeatCatalog.Prefabs)
            {
                Assert.True(HomesteadAuthoredSeatCatalog.TryGet(prefab, out var seat));
                Assert.True(double.IsFinite(seat.LocalX));
                Assert.True(double.IsFinite(seat.LocalY));
                Assert.True(double.IsFinite(seat.LocalZ));
                Assert.True(double.IsFinite(seat.LocalYawDegrees));
                Assert.InRange(Math.Sqrt((seat.LocalX * seat.LocalX) + (seat.LocalZ * seat.LocalZ)), 0.0, 6.001);
            }
        }

        [Fact]
        public void Prefab_local_position_and_orientation_rotate_with_the_host()
        {
            Assert.True(HomesteadAuthoredSeatCatalog.TryGet("WoodHouse11", out var seat));
            seat.ToWorld(100.0, 200.0, 90.0, out var x, out var z, out var yaw);

            Assert.Equal(100.0, x, 3);
            Assert.Equal(194.0, z, 3);
            Assert.Equal(0.0, yaw, 3); // host 90 + local -90
        }

        [Fact]
        public void Content_hash_is_stable_and_nonempty()
        {
            Assert.Equal(64, HomesteadAuthoredSeatCatalog.ContentHash.Length);
            Assert.Matches("^[0-9A-F]{64}$", HomesteadAuthoredSeatCatalog.ContentHash);
        }

        [Fact]
        public void Every_authored_seat_clears_the_build_time_structural_catalog()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "homestead-static-geometry.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var hosts = document.RootElement.GetProperty("hosts");

            foreach (var prefab in HomesteadAuthoredSeatCatalog.Prefabs)
            {
                Assert.True(HomesteadAuthoredSeatCatalog.TryGet(prefab, out var seat));
                var minimum = double.PositiveInfinity;
                foreach (var collider in hosts.GetProperty(prefab).GetProperty("colliders").EnumerateArray())
                {
                    var dx = Math.Max(0.0,
                        Math.Abs(seat.LocalX - collider.GetProperty("cx").GetDouble()) -
                        collider.GetProperty("halfX").GetDouble());
                    var dz = Math.Max(0.0,
                        Math.Abs(seat.LocalZ - collider.GetProperty("cz").GetDouble()) -
                        collider.GetProperty("halfZ").GetDouble());
                    minimum = Math.Min(minimum, Math.Sqrt((dx * dx) + (dz * dz)));
                }

                Assert.True(minimum >= HomesteadStaticSeatEvaluator.SeatKeepOut,
                    $"{prefab} authored seat clearance {minimum:0.000}m is below " +
                    $"{HomesteadStaticSeatEvaluator.SeatKeepOut:0.00}m.");
            }
        }
    }
}
