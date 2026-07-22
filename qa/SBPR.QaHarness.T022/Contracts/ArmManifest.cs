// The immutable per-run arm manifest (ADR-0009 §5.1) and its capability subset.
// Engine-free value objects: the runner mints one of these per run, the helper
// parses it, and the arming gate (ArmingGate.cs) evaluates it against observed world
// facts. Nothing here touches the game.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// Exact world identity (ADR-0009 §5.1): the gate requires BOTH the disposable
    /// world UID and its name to match — name alone is insufficient (spoofable/reusable).
    /// </summary>
    public sealed class WorldIdentity
    {
        public long WorldUid { get; }
        public string WorldName { get; }

        public WorldIdentity(long worldUid, string worldName)
        {
            WorldUid = worldUid;
            WorldName = worldName ?? string.Empty;
        }

        /// <summary>Exact (UID AND name) equality. Ordinal name compare — no culture folding.</summary>
        public bool ExactlyMatches(WorldIdentity other)
            => other != null
               && WorldUid == other.WorldUid
               && string.Equals(WorldName, other.WorldName, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pinned byte-state of the run (ADR-0009 §5.1, §8): product/helper/game/
    /// BepInEx/Harmony/scenario hashes. Drift on ANY of them refuses to arm, so a
    /// stale helper can't silently drive a moved seam. Represented as an ordered set
    /// of (component, sha256) pairs so a missing OR mismatched component both fail.
    /// </summary>
    public sealed class HashManifest
    {
        /// <summary>The components that MUST be pinned for a valid manifest.</summary>
        public static readonly IReadOnlyList<string> RequiredComponents = new[]
        {
            "product", "helper", "game", "bepinex", "harmony", "scenario",
        };

        private readonly Dictionary<string, string> _hashes;

        public HashManifest(IReadOnlyDictionary<string, string> hashes)
        {
            _hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (hashes != null)
            {
                foreach (var kv in hashes) _hashes[kv.Key] = kv.Value;
            }
        }

        /// <summary>True only when every required component is present and non-blank.</summary>
        public bool IsComplete()
        {
            foreach (var c in RequiredComponents)
            {
                if (!_hashes.TryGetValue(c, out var h) || string.IsNullOrWhiteSpace(h)) return false;
            }
            return true;
        }

        /// <summary>
        /// True when every required component's hash exactly equals the observed hash.
        /// A missing observed component, or any mismatch, is drift (returns false).
        /// </summary>
        public bool MatchesObserved(IReadOnlyDictionary<string, string> observed)
        {
            if (observed == null) return false;
            foreach (var c in RequiredComponents)
            {
                if (!_hashes.TryGetValue(c, out var pinned) || string.IsNullOrWhiteSpace(pinned)) return false;
                if (!observed.TryGetValue(c, out var seen) || string.IsNullOrWhiteSpace(seen)) return false;
                if (!string.Equals(pinned, seen, StringComparison.Ordinal)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// The immutable arm manifest the runner mints for one run (ADR-0009 §5.1). All
    /// fields are supplied explicitly; the gate never infers any of them.
    /// </summary>
    public sealed class ArmManifest
    {
        /// <summary>Explicit enable flag. Absent/false => default-disabled, nothing arms.</summary>
        public bool Enabled { get; }

        /// <summary>Explicit process role (Server/Client). Parsed strictly.</summary>
        public string? RoleToken { get; }

        /// <summary>Explicit actor alias (e.g. "primary", "valbot"). Must be non-blank.</summary>
        public string? Actor { get; }

        /// <summary>The disposable world the run is pinned to (UID + name).</summary>
        public WorldIdentity? World { get; }

        /// <summary>Per-run nonce. Absent/empty => fail-closed.</summary>
        public string? Nonce { get; }

        /// <summary>Hard expiry (unix ms). Must be strictly in the future at arm time.</summary>
        public long ExpiryUnixMs { get; }

        /// <summary>The pinned hash manifest (§5.1, §8).</summary>
        public HashManifest? Hashes { get; }

        /// <summary>The capability manifest: exactly which catalog verbs are permitted this run.</summary>
        public IReadOnlyList<string> PermittedVerbs { get; }

        /// <summary>Per-run HMAC secret (shared with the runner). Used for request signing (§3.2).</summary>
        public string? HmacSecret { get; }

        public ArmManifest(
            bool enabled,
            string? roleToken,
            string? actor,
            WorldIdentity? world,
            string? nonce,
            long expiryUnixMs,
            HashManifest? hashes,
            IReadOnlyList<string>? permittedVerbs,
            string? hmacSecret)
        {
            Enabled = enabled;
            RoleToken = roleToken;
            Actor = actor;
            World = world;
            Nonce = nonce;
            ExpiryUnixMs = expiryUnixMs;
            Hashes = hashes;
            PermittedVerbs = permittedVerbs ?? Array.Empty<string>();
            HmacSecret = hmacSecret;
        }
    }
}
