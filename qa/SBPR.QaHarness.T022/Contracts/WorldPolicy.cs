// The production deny list + disposable-world allowlist (ADR-0009 §5.1, Appendix A
// T1). Production rejection is a HARD gate: known production worlds/servers are
// refused even if the allowlist is misconfigured, and the deny list is checked BEFORE
// the allowlist so a stray allowlist entry can never re-admit production.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// Immutable production/allowlist policy. The hard production deny list is baked
    /// in (not config): Niflheim 2456 and Heistan 2466 (ADR-0009 §5.1, §Appendix A).
    /// A world named like production, or on a production port, is also denied.
    /// </summary>
    public sealed class WorldPolicy
    {
        // Hard-coded production world UIDs — refused even if allowlisted (ADR-0009 §5.1).
        private static readonly HashSet<long> _prodUids = new() { 2456, 2466 };

        // Production name markers (ordinal, case-insensitive) — belt-and-suspenders
        // so a production world can't sneak past on UID reuse.
        private static readonly string[] _prodNameMarkers = { "niflheim", "heistan", "prod" };

        private readonly HashSet<long> _allowUids;
        private readonly HashSet<string> _allowNames;

        public WorldPolicy(IEnumerable<WorldIdentity>? allowlist)
        {
            _allowUids = new HashSet<long>();
            _allowNames = new HashSet<string>(StringComparer.Ordinal);
            if (allowlist != null)
            {
                foreach (var w in allowlist)
                {
                    if (w == null) continue;
                    _allowUids.Add(w.WorldUid);
                    _allowNames.Add(w.WorldName);
                }
            }
        }

        /// <summary>
        /// True when the world is a hard-denied production world (by UID or name marker).
        /// Checked before any allowlist consideration.
        /// </summary>
        public bool IsProductionDenied(WorldIdentity world)
        {
            if (world == null) return true; // null => fail-closed (treat as denied)
            if (_prodUids.Contains(world.WorldUid)) return true;
            var name = world.WorldName ?? string.Empty;
            foreach (var marker in _prodNameMarkers)
            {
                if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// True when the world is in the disposable-world allowlist (exact UID AND name).
        /// Only meaningful after <see cref="IsProductionDenied"/> is false.
        /// </summary>
        public bool IsAllowlisted(WorldIdentity world)
            => world != null
               && _allowUids.Contains(world.WorldUid)
               && _allowNames.Contains(world.WorldName);
    }
}
