using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    internal sealed class HomesteadCandidate : IEquatable<HomesteadCandidate>
    {
        internal HomesteadCandidate(string prefab, int zoneX, int zoneZ, double x, double z, double locationRadius)
        {
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            ZoneX = zoneX;
            ZoneZ = zoneZ;
            X = x;
            Z = z;
            LocationRadius = locationRadius;
        }

        internal string Prefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal double X { get; }
        internal double Z { get; }
        internal double LocationRadius { get; }

        internal double DistanceSquaredTo(HomesteadCandidate other)
        {
            var dx = X - other.X;
            var dz = Z - other.Z;
            return (dx * dx) + (dz * dz);
        }

        public bool Equals(HomesteadCandidate? other) =>
            other != null && Prefab == other.Prefab && ZoneX == other.ZoneX && ZoneZ == other.ZoneZ &&
            X.Equals(other.X) && Z.Equals(other.Z) && LocationRadius.Equals(other.LocationRadius);

        public override bool Equals(object? obj) => Equals(obj as HomesteadCandidate);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Prefab.GetHashCode();
                hash = (hash * 397) ^ ZoneX;
                hash = (hash * 397) ^ ZoneZ;
                hash = (hash * 397) ^ X.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return (hash * 397) ^ LocationRadius.GetHashCode();
            }
        }
    }

    internal sealed class HomesteadSelectionConfig
    {
        internal HomesteadSelectionConfig(string worldIdentity, string selectorVersion, double minimumDistance, double density)
        {
            if (minimumDistance < 0.0) throw new ArgumentOutOfRangeException(nameof(minimumDistance));
            if (density < 0.0 || density > 1.0) throw new ArgumentOutOfRangeException(nameof(density));
            WorldIdentity = worldIdentity ?? throw new ArgumentNullException(nameof(worldIdentity));
            SelectorVersion = selectorVersion ?? throw new ArgumentNullException(nameof(selectorVersion));
            MinimumDistance = minimumDistance;
            Density = density;
        }

        internal string WorldIdentity { get; }
        internal string SelectorVersion { get; }
        internal double MinimumDistance { get; }
        internal double Density { get; }
    }

    internal sealed class HomesteadSelectionResult
    {
        internal HomesteadSelectionResult(List<HomesteadCandidate> selected, List<string> warnings)
        {
            Selected = selected;
            Warnings = warnings;
        }

        internal List<HomesteadCandidate> Selected { get; }
        internal List<string> Warnings { get; }
    }

    internal static class HomesteadSelector
    {
        internal static HomesteadSelectionResult Select(
            IReadOnlyCollection<HomesteadCandidate> candidates,
            HomesteadSelectionConfig config)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (config == null) throw new ArgumentNullException(nameof(config));

            var byType = candidates
                .GroupBy(candidate => candidate.Prefab, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderBy(candidate => Priority(config, candidate), ByteArrayComparer.Instance)
                                  .ThenBy(candidate => candidate.ZoneX)
                                  .ThenBy(candidate => candidate.ZoneZ)
                                  .ToList(),
                    StringComparer.Ordinal);

            var targets = byType.ToDictionary(
                pair => pair.Key,
                pair => (int)Math.Ceiling(pair.Value.Count * config.Density),
                StringComparer.Ordinal);
            var selected = new List<HomesteadCandidate>();
            var assigned = byType.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
            var nextCandidate = byType.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
            var minimumDistanceSquared = config.MinimumDistance * config.MinimumDistance;
            var maximumTarget = targets.Count == 0 ? 0 : targets.Values.Max();

            // Fair type-local rounds prevent a prefab name or discovery order from monopolizing
            // the hard proximity budget. Within each type, stable SHA-256 priority is authoritative.
            for (var round = 0; round < maximumTarget; round++)
            {
                foreach (var prefab in byType.Keys.OrderBy(key => key, StringComparer.Ordinal))
                {
                    if (assigned[prefab] >= targets[prefab]) continue;
                    var ordered = byType[prefab];
                    while (nextCandidate[prefab] < ordered.Count)
                    {
                        var candidate = ordered[nextCandidate[prefab]++];
                        if (selected.All(existing => candidate.DistanceSquaredTo(existing) >= minimumDistanceSquared))
                        {
                            selected.Add(candidate);
                            assigned[prefab]++;
                            break;
                        }
                    }
                }
            }

            var warnings = targets
                .Where(pair => assigned[pair.Key] < pair.Value)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: assigned {1} of target {2} under {3:0.###} m minimum distance",
                    pair.Key,
                    assigned[pair.Key],
                    pair.Value,
                    config.MinimumDistance))
                .ToList();
            return new HomesteadSelectionResult(selected, warnings);
        }

        internal static byte[] Priority(HomesteadSelectionConfig config, HomesteadCandidate candidate) =>
            StableHash.Bytes(
                config.WorldIdentity,
                config.SelectorVersion,
                candidate.Prefab,
                candidate.ZoneX.ToString(CultureInfo.InvariantCulture),
                candidate.ZoneZ.ToString(CultureInfo.InvariantCulture));
    }

    internal static class HomesteadWorldIdentity
    {
        internal static string FromUid(long worldUid) =>
            "uid:" + worldUid.ToString(CultureInfo.InvariantCulture);
    }

    internal static class HomesteadHostStructure
    {
        internal static bool IsAttributed(
            long creator,
            double pieceX,
            double pieceZ,
            double hostX,
            double hostZ,
            double locationRadius)
        {
            if (creator != 0L) return false;
            var dx = pieceX - hostX;
            var dz = pieceZ - hostZ;
            return (dx * dx) + (dz * dz) <= locationRadius * locationRadius;
        }
    }

    internal readonly struct HomesteadAssignmentMetadata : IEquatable<HomesteadAssignmentMetadata>
    {
        internal HomesteadAssignmentMetadata(string worldIdentity, string selectorVersion, string prefab, int zoneX, int zoneZ)
        {
            WorldIdentity = worldIdentity ?? throw new ArgumentNullException(nameof(worldIdentity));
            SelectorVersion = selectorVersion ?? throw new ArgumentNullException(nameof(selectorVersion));
            Prefab = prefab ?? throw new ArgumentNullException(nameof(prefab));
            ZoneX = zoneX;
            ZoneZ = zoneZ;
        }

        internal string WorldIdentity { get; }
        internal string SelectorVersion { get; }
        internal string Prefab { get; }
        internal int ZoneX { get; }
        internal int ZoneZ { get; }
        internal bool Matches(HomesteadAssignmentMetadata other) => Equals(other);
        public bool Equals(HomesteadAssignmentMetadata other) =>
            WorldIdentity == other.WorldIdentity && SelectorVersion == other.SelectorVersion &&
            Prefab == other.Prefab && ZoneX == other.ZoneX && ZoneZ == other.ZoneZ;
        public override bool Equals(object? obj) => obj is HomesteadAssignmentMetadata other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = WorldIdentity.GetHashCode();
                hash = (hash * 397) ^ SelectorVersion.GetHashCode();
                hash = (hash * 397) ^ Prefab.GetHashCode();
                hash = (hash * 397) ^ ZoneX;
                return (hash * 397) ^ ZoneZ;
            }
        }
    }

    internal readonly struct SeatCandidate : IEquatable<SeatCandidate>
    {
        internal SeatCandidate(int attempt, double x, double z)
        {
            Attempt = attempt;
            X = x;
            Z = z;
        }

        internal int Attempt { get; }
        internal double X { get; }
        internal double Z { get; }

        public bool Equals(SeatCandidate other) => Attempt == other.Attempt && X.Equals(other.X) && Z.Equals(other.Z);
        public override bool Equals(object? obj) => obj is SeatCandidate other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Attempt;
                hash = (hash * 397) ^ X.GetHashCode();
                return (hash * 397) ^ Z.GetHashCode();
            }
        }
    }

    internal readonly struct SeatSelection
    {
        internal SeatSelection(bool hasSeat, SeatCandidate seat, int attemptsEvaluated)
        {
            HasSeat = hasSeat;
            Seat = seat;
            AttemptsEvaluated = attemptsEvaluated;
        }

        internal bool HasSeat { get; }
        internal SeatCandidate Seat { get; }
        internal int AttemptsEvaluated { get; }
    }

    internal readonly struct SeatEvaluation
    {
        internal SeatEvaluation(bool isValid, double clearance, double radialDistance, double hostRadius)
        {
            IsValid = isValid;
            Clearance = clearance;
            RadialDistance = radialDistance;
            HostRadius = hostRadius;
        }

        internal bool IsValid { get; }
        internal double Clearance { get; }
        internal double RadialDistance { get; }
        internal double HostRadius { get; }

        internal double Score
        {
            get
            {
                if (!IsValid || Clearance < 1.75) return double.NegativeInfinity;
                var yardBand = Math.Max(0.0, Math.Min(1.0, 1.0 - (Math.Abs(RadialDistance - (HostRadius + 2.5)) / 5.0)));
                return 100.0 + (Clearance * 4.0) + (yardBand * 8.0) - (Math.Max(0.0, RadialDistance - 12.0) * 2.0);
            }
        }
    }

    internal static class HomesteadSeatGenerator
    {
        private const double CenterGuard = 1.75;
        private const double AreaMargin = 0.92;

        internal static List<SeatCandidate> Generate(
            string worldIdentity,
            string selectorVersion,
            HomesteadCandidate candidate,
            int attemptCount)
        {
            if (attemptCount < 0) throw new ArgumentOutOfRangeException(nameof(attemptCount));
            var seats = new List<SeatCandidate>(attemptCount);
            var radius = candidate.LocationRadius * AreaMargin;
            if (radius < CenterGuard)
                throw new ArgumentOutOfRangeException(nameof(candidate), "Location radius cannot satisfy the center guard after margin.");
            for (var attempt = 0; attempt < attemptCount; attempt++)
            {
                var bytes = StableHash.Bytes(
                    worldIdentity,
                    selectorVersion,
                    "seat",
                    candidate.Prefab,
                    candidate.ZoneX.ToString(CultureInfo.InvariantCulture),
                    candidate.ZoneZ.ToString(CultureInfo.InvariantCulture),
                    attempt.ToString(CultureInfo.InvariantCulture));
                var angle = StableHash.UnitInterval(bytes, 0) * Math.PI * 2.0;
                var radialSample = StableHash.UnitInterval(bytes, 8);
                var distance = CenterGuard + ((radius - CenterGuard) * Math.Sqrt(radialSample));
                seats.Add(new SeatCandidate(
                    attempt,
                    candidate.X + (Math.Cos(angle) * distance),
                    candidate.Z + (Math.Sin(angle) * distance)));
            }
            return seats;
        }

        internal static SeatSelection Choose(IReadOnlyList<SeatCandidate> candidates, Func<SeatCandidate, bool> isValid)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (isValid == null) throw new ArgumentNullException(nameof(isValid));
            for (var index = 0; index < candidates.Count; index++)
            {
                if (isValid(candidates[index]))
                    return new SeatSelection(true, candidates[index], index + 1);
            }
            return new SeatSelection(false, default, candidates.Count);
        }

        internal static SeatSelection ChooseBest(
            IReadOnlyList<SeatCandidate> candidates,
            Func<SeatCandidate, SeatEvaluation> evaluate)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            if (evaluate == null) throw new ArgumentNullException(nameof(evaluate));
            var found = false;
            var best = default(SeatCandidate);
            var bestScore = double.NegativeInfinity;
            for (var index = 0; index < candidates.Count; index++)
            {
                var score = evaluate(candidates[index]).Score;
                if (double.IsNegativeInfinity(score)) continue;
                if (!found || score > bestScore || (score.Equals(bestScore) && candidates[index].Attempt < best.Attempt))
                {
                    found = true;
                    best = candidates[index];
                    bestScore = score;
                }
            }
            return new SeatSelection(found, best, candidates.Count);
        }
    }

    internal static class HomesteadVisualMotion
    {
        internal static VisualMotionSample Sample(double elapsedSeconds)
        {
            var phase = elapsedSeconds % 4.0;
            if (phase < 0.0) phase += 4.0;
            if (phase <= 1.0) return Lerp(phase, 0.0, 0.045, 0.0, 1.5);
            if (phase <= 2.0) return Lerp(phase - 1.0, 0.045, 0.0, 1.5, 0.0);
            if (phase <= 3.0) return Lerp(phase - 2.0, 0.0, -0.035, 0.0, -1.4);
            return Lerp(phase - 3.0, -0.035, 0.0, -1.4, 0.0);
        }

        private static VisualMotionSample Lerp(double amount, double y0, double y1, double yaw0, double yaw1) =>
            new VisualMotionSample(y0 + ((y1 - y0) * amount), yaw0 + ((yaw1 - yaw0) * amount));
    }

    internal readonly struct VisualMotionSample : IEquatable<VisualMotionSample>
    {
        internal VisualMotionSample(double heightOffset, double yawDegrees)
        {
            HeightOffset = heightOffset;
            YawDegrees = yawDegrees;
        }

        internal double HeightOffset { get; }
        internal double YawDegrees { get; }
        public bool Equals(VisualMotionSample other) => HeightOffset.Equals(other.HeightOffset) && YawDegrees.Equals(other.YawDegrees);
        public override bool Equals(object? obj) => obj is VisualMotionSample other && Equals(other);
        public override int GetHashCode() => (HeightOffset.GetHashCode() * 397) ^ YawDegrees.GetHashCode();
    }

    internal static class StableHash
    {
        internal static byte[] Bytes(params string[] parts)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", parts)));
        }

        internal static double UnitInterval(byte[] bytes, int offset)
        {
            ulong value = 0;
            for (var index = 0; index < 8; index++)
                value = (value << 8) | bytes[offset + index];
            return value / 18446744073709551616.0;
        }

        internal static string Hex(params string[] parts) =>
            BitConverter.ToString(Bytes(parts)).Replace("-", string.Empty);
    }

    internal sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static readonly ByteArrayComparer Instance = new ByteArrayComparer();

        public int Compare(byte[]? left, byte[]? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
            {
                var comparison = left[index].CompareTo(right[index]);
                if (comparison != 0) return comparison;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}
