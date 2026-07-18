using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    /// <summary>One Daniel-approved Stone transform authored in a vanilla house prefab's local space.</summary>
    internal readonly struct HomesteadAuthoredSeat
    {
        internal HomesteadAuthoredSeat(string prefab, double localX, double localY, double localZ, double localYawDegrees)
        {
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            LocalX = localX;
            LocalY = localY;
            LocalZ = localZ;
            LocalYawDegrees = localYawDegrees;
        }

        internal string Prefab { get; }
        internal double LocalX { get; }
        internal double LocalY { get; }
        internal double LocalZ { get; }
        internal double LocalYawDegrees { get; }

        internal void ToWorld(
            double hostX, double hostZ, double hostYawDegrees,
            out double worldX, out double worldZ, out double worldYawDegrees)
        {
            var radians = hostYawDegrees * Math.PI / 180.0;
            var sin = Math.Sin(radians);
            var cos = Math.Cos(radians);
            worldX = hostX + (LocalX * cos) + (LocalZ * sin);
            worldZ = hostZ - (LocalX * sin) + (LocalZ * cos);
            worldYawDegrees = NormalizeDegrees(hostYawDegrees + LocalYawDegrees);
        }

        private static double NormalizeDegrees(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }
    }

    /// <summary>
    /// Runtime placement authority: thirteen fixed prefab-local transforms, selected visually by Daniel.
    /// The full collider catalog is build-time validation evidence only and is never loaded by the game.
    /// </summary>
    internal static class HomesteadAuthoredSeatCatalog
    {
        // v2 adds the prefab-child-equivalent vegetation clear around each authored seat.
        internal const string Version = "niflheim-homestead-authored-seats-v2";

        private static readonly IReadOnlyDictionary<string, HomesteadAuthoredSeat> ByPrefab =
            new Dictionary<string, HomesteadAuthoredSeat>(StringComparer.Ordinal)
            {
                ["WoodHouse1"]  = new HomesteadAuthoredSeat("WoodHouse1",  -5.999, 0.0,  0.125,   91.2),
                ["WoodHouse2"]  = new HomesteadAuthoredSeat("WoodHouse2",  -0.062, 0.0,  6.000,  179.4),
                ["WoodHouse3"]  = new HomesteadAuthoredSeat("WoodHouse3",  -4.795, 0.0,  3.607,  127.0),
                ["WoodHouse4"]  = new HomesteadAuthoredSeat("WoodHouse4",   5.267, 0.0,  2.873, -118.6),
                ["WoodHouse5"]  = new HomesteadAuthoredSeat("WoodHouse5",  -5.078, 0.0,  3.196,  122.2),
                ["WoodHouse6"]  = new HomesteadAuthoredSeat("WoodHouse6",   5.671, 0.0,  1.961, -109.1),
                ["WoodHouse7"]  = new HomesteadAuthoredSeat("WoodHouse7",  -0.561, 0.0, -5.974,    5.4),
                ["WoodHouse8"]  = new HomesteadAuthoredSeat("WoodHouse8",  -2.472, 0.0,  0.373,   98.6),
                ["WoodHouse9"]  = new HomesteadAuthoredSeat("WoodHouse9",  -5.078, 0.0,  3.196,  122.2),
                ["WoodHouse10"] = new HomesteadAuthoredSeat("WoodHouse10", -5.207, 0.0,  2.982,  119.8),
                ["WoodHouse11"] = new HomesteadAuthoredSeat("WoodHouse11",  6.000, 0.0,  0.000,  -90.0),
                ["WoodHouse12"] = new HomesteadAuthoredSeat("WoodHouse12",  3.249, 0.0, -5.044,  -32.8),
                ["WoodHouse13"] = new HomesteadAuthoredSeat("WoodHouse13",  2.595, 0.0, -5.410,  -25.6),
            };

        internal static int Count => ByPrefab.Count;
        internal static IEnumerable<string> Prefabs => ByPrefab.Keys.OrderBy(x => x, StringComparer.Ordinal);
        internal static bool TryGet(string prefab, out HomesteadAuthoredSeat seat) => ByPrefab.TryGetValue(prefab, out seat);

        internal static readonly string ContentHash = StableHash.Hex(
            Version,
            string.Join(";", ByPrefab.Values
                .OrderBy(x => x.Prefab, StringComparer.Ordinal)
                .Select(x => string.Format(CultureInfo.InvariantCulture,
                    "{0}:{1:0.000}:{2:0.000}:{3:0.000}:{4:0.0}",
                    x.Prefab, x.LocalX, x.LocalY, x.LocalZ, x.LocalYawDegrees))));
    }
}
