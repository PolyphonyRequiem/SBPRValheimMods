using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T009R4 (Blocker 1) — the engine-free, TESTABLE Stone Area registrar.
    //
    // The blocker (adversarial review): FoundationalProgressionServer.StoneAreas started EMPTY and nothing
    // in production ever called StoneAreas.Register(...) — only tests did. An empty membership resolves
    // every placement to OutsideStoneArea, so on a real server NO placement could ever be inside a Stone
    // Area and NOTHING could ever be credited. The Stone Areas must be populated server-authoritatively
    // from the real resident/persisted Stone facts.
    //
    // This registrar reconciles a StoneAreaMembership against the CURRENT set of realized/persisted Stone
    // facts (each: the server-owned StoneId derived from the resident Stone ZDO's world identity + host
    // zone, its world-position center, and the per-Stone Area radius). It is a pure function of those
    // facts, so the net48 layer only has to enumerate resident Stone ZDOs and hand over their facts; every
    // lifecycle branch (add, move, remove, idempotent re-run) is unit-tested here.
    //
    // Explicit lifecycle rules:
    //   * A Stone present in the facts is REGISTERED (or its center/radius UPDATED if it moved between
    //     rerolls of the disposable playtest world) — membership.Register is idempotent per StoneId.
    //   * A Stone previously registered but ABSENT from the facts is UNREGISTERED (its Area no longer
    //     exists — e.g. a stale assignment ZDO was reaped, or the Stone was removed). This never awards or
    //     revokes AP; it only governs which positions resolve inside an Area.
    //   * Reconciliation is a REPLACE-to-match operation, so calling it repeatedly with the same facts is a
    //     no-op — safe to run on startup and on the periodic placement-realization cadence.
    //
    // net48 audit: System + collections + value objects only. Link-compiles into the net8 test project.
    public static class StoneAreaRegistrar
    {
        /// <summary>One server-owned Stone Area fact: the stable StoneId, the world-position center, and the
        /// Area radius. All three are derived server-side from the resident Stone ZDO — never a client claim.</summary>
        public readonly struct StoneAreaFact
        {
            public StoneAreaFact(StoneId stoneId, double x, double z, double radius)
            {
                StoneId = stoneId; X = x; Z = z; Radius = radius;
            }
            public StoneId StoneId { get; }
            public double X { get; }
            public double Z { get; }
            public double Radius { get; }
        }

        /// <summary>The result of one reconciliation, for operator visibility.</summary>
        public readonly struct ReconcileResult
        {
            public ReconcileResult(int registered, int updated, int unregistered, int total)
            {
                Registered = registered; Updated = updated; Unregistered = unregistered; Total = total;
            }
            /// <summary>Stones newly added to the membership.</summary>
            public int Registered { get; }
            /// <summary>Stones already present whose center/radius changed.</summary>
            public int Updated { get; }
            /// <summary>Stones removed because they are no longer resident.</summary>
            public int Unregistered { get; }
            /// <summary>Total Areas after reconciliation.</summary>
            public int Total { get; }

            public override string ToString() =>
                $"[stone-areas] registered={Registered} updated={Updated} unregistered={Unregistered} total={Total}";
        }

        /// <summary>Reconcile <paramref name="membership"/> to exactly the given resident Stone facts. Adds
        /// new Stones, updates moved ones, and removes Stones no longer present. Idempotent: re-running with
        /// the same facts changes nothing. Returns a summary for logging.</summary>
        public static ReconcileResult Reconcile(StoneAreaMembership membership, IEnumerable<StoneAreaFact> facts)
        {
            if (membership == null) throw new ArgumentNullException(nameof(membership));
            if (facts == null) throw new ArgumentNullException(nameof(facts));

            var desired = new Dictionary<string, StoneAreaFact>(StringComparer.Ordinal);
            foreach (var f in facts)
            {
                if (string.IsNullOrEmpty(f.StoneId.Value)) continue;   // an unkeyed Stone has no Area
                desired[f.StoneId.Value] = f;   // last write wins on a duplicate id (stable, deterministic)
            }

            // Snapshot the currently-registered ids so we can remove any that are no longer desired.
            var current = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in membership.RegisteredStoneIds()) current.Add(id);

            int registered = 0, updated = 0, unregistered = 0;

            foreach (var kv in desired)
            {
                var f = kv.Value;
                if (!current.Contains(kv.Key))
                {
                    membership.Register(f.StoneId, f.X, f.Z, f.Radius);
                    registered++;
                }
                else if (!membership.Matches(f.StoneId, f.X, f.Z, f.Radius))
                {
                    membership.Register(f.StoneId, f.X, f.Z, f.Radius);   // re-register replaces center/radius
                    updated++;
                }
            }

            foreach (var id in current)
            {
                if (!desired.ContainsKey(id))
                {
                    membership.Unregister(new StoneId(id));
                    unregistered++;
                }
            }

            return new ReconcileResult(registered, updated, unregistered, membership.Count);
        }
    }
}
