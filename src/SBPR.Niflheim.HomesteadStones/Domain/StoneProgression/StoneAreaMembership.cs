using System;
using System.Collections.Generic;
using System.Globalization;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T009 — engine-free Stone Area membership. The live server observes a placement's world position
    // and must decide, from SERVER-OWNED facts, which Homestead Stone Area (if any) it falls inside —
    // never trusting a client claim of "I'm at Stone X". This pure resolver holds the authoritative set
    // of known Stone centers (each keyed by its stable StoneId + a per-Stone Area radius) and answers
    // "which Stone Area contains (x,z)?". The engine-bound observer builds this from resident Stone ZDOs
    // (the same world-owned identity the T009-era placement pipeline stamps) and asks it per placement.
    //
    // Membership is the nearest containing center within radius; ties resolve by stable StoneId ordinal
    // so the answer is deterministic. A position inside no Area returns false (the observer then submits
    // insideStoneArea=false and the adapter rejects OutsideStoneArea).
    //
    // net48 audit: System + collections + value objects only. Link-compiles into the net8 test project.
    public sealed class StoneAreaMembership
    {
        /// <summary>Default Homestead Stone Area radius (metres). Provisional proof value (design call
        /// 2026-07-15); the per-Stone override on <see cref="Register"/> wins when supplied.</summary>
        public const double DefaultAreaRadius = 20.0;

        private readonly List<StoneArea> _areas = new List<StoneArea>();

        private readonly struct StoneArea
        {
            public StoneArea(StoneId stoneId, double x, double z, double radius)
            {
                StoneId = stoneId; X = x; Z = z; Radius = radius;
            }
            public StoneId StoneId { get; }
            public double X { get; }
            public double Z { get; }
            public double Radius { get; }
        }

        /// <summary>Register (or re-register) a known Stone's Area center. Re-registering the same
        /// StoneId replaces its center/radius (a Stone can move between rerolls of the disposable
        /// playtest world). A non-positive radius falls back to <see cref="DefaultAreaRadius"/>.</summary>
        public void Register(StoneId stoneId, double x, double z, double radius = DefaultAreaRadius)
        {
            double r = radius > 0.0 ? radius : DefaultAreaRadius;
            _areas.RemoveAll(a => a.StoneId.Equals(stoneId));
            _areas.Add(new StoneArea(stoneId, x, z, r));
        }

        public int Count => _areas.Count;

        /// <summary>Remove a Stone's Area from the membership (lifecycle: the Stone is no longer resident).
        /// A no-op when the StoneId was not registered.</summary>
        public void Unregister(StoneId stoneId) => _areas.RemoveAll(a => a.StoneId.Equals(stoneId));

        /// <summary>The stable ids of every currently-registered Stone Area, for reconciliation.</summary>
        public IEnumerable<string> RegisteredStoneIds()
        {
            foreach (var a in _areas) yield return a.StoneId.Value;
        }

        /// <summary>True when the registered Area for <paramref name="stoneId"/> has exactly this center and
        /// radius (used by reconciliation to detect a moved/re-radiused Stone). False when unregistered.</summary>
        public bool Matches(StoneId stoneId, double x, double z, double radius)
        {
            double r = radius > 0.0 ? radius : DefaultAreaRadius;
            foreach (var a in _areas)
            {
                if (!a.StoneId.Equals(stoneId)) continue;
                return a.X.Equals(x) && a.Z.Equals(z) && a.Radius.Equals(r);
            }
            return false;
        }

        /// <summary>Resolve which registered Stone Area contains (x,z). Returns true and the owning
        /// StoneId when the position is within some Area's radius; picks the nearest center on overlap,
        /// tie-broken by stable StoneId ordinal for determinism.</summary>
        public bool TryResolve(double x, double z, out StoneId stoneId)
        {
            stoneId = default;
            bool found = false;
            double bestDistSq = double.PositiveInfinity;
            foreach (var a in _areas)
            {
                double dx = x - a.X, dz = z - a.Z;
                double distSq = (dx * dx) + (dz * dz);
                if (distSq > a.Radius * a.Radius) continue;
                if (!found || distSq < bestDistSq ||
                    (distSq.Equals(bestDistSq) &&
                     string.CompareOrdinal(a.StoneId.Value, stoneId.Value) < 0))
                {
                    found = true;
                    bestDistSq = distSq;
                    stoneId = a.StoneId;
                }
            }
            return found;
        }

        /// <summary>True when (x,z) is inside the given Stone's registered Area specifically (not merely
        /// inside some Area). Used when the placement's owning Stone is already known and the server only
        /// needs to confirm the position is genuinely within THAT Stone's Area.</summary>
        public bool IsInside(StoneId stoneId, double x, double z)
        {
            foreach (var a in _areas)
            {
                if (!a.StoneId.Equals(stoneId)) continue;
                double dx = x - a.X, dz = z - a.Z;
                return (dx * dx) + (dz * dz) <= a.Radius * a.Radius;
            }
            return false;
        }

        public override string ToString() =>
            "StoneAreaMembership(" + _areas.Count.ToString(CultureInfo.InvariantCulture) + " areas)";
    }
}
