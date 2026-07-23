using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Crafting
{
    // ============================================================================
    // T022 remediation — the CLIENT-SIDE bounded cache of server Workmanship VERDICTS.
    // A pure joined client holds no integrity key, so it cannot decide on its own whether
    // a stamp it reads is genuine. It reads the stamp keylessly (WorkmanshipCodec.
    // TryReadRaw), asks the server to validate it (WorkmanshipValidationRequest), and the
    // server replies Valid/Tampered (WorkmanshipValidationVerdict).
    //
    // KEYED BY THE COMPLETE SIGNED-STAMP FINGERPRINT — NOT the provenance id. The earlier
    // build keyed a verdict on provenance id alone, which let a post-validation tamper reuse
    // a stale Valid: after a transferred item validated, an attacker could change
    // niflheim.workmanship.prop_value while RETAINING prov_id/token and the cached Valid
    // stayed reusable, so the tooltip rendered the confirmed line over mutated bytes without
    // re-asking the server. Binding the verdict to WorkmanshipCodec.Fingerprint (every signed
    // key AND value, length-framed) closes that: the instant any signed field changes, the
    // fingerprint changes, the cache MISSES, and the presentation seam fails closed and
    // requests a fresh server verdict for the mutated bytes — which the server rejects.
    //
    // Fail-closed semantics: an unknown fingerprint (never confirmed) reads as NOT valid, so
    // a stamp the client has not had the server confirm — OR a stamp whose bytes changed since
    // the last verdict — presents as plain vanilla until re-confirmed. The cache is bounded by
    // a simple insertion-order cap so a flood of distinct fingerprints cannot grow it without
    // bound.
    //
    // net48 audit: engine-free (System.* + the engine-free codec fingerprint). Link-compiles
    // into the net8 test project.
    // ============================================================================

    public sealed class WorkmanshipVerdictCache
    {
        /// <summary>Bound on distinct fingerprint verdicts held at once (spam guard). When exceeded the oldest
        /// inserted entry is evicted; an evicted stamp simply re-fails-closed until re-confirmed.</summary>
        public const int DefaultCapacity = 512;

        private readonly int _capacity;
        private readonly Dictionary<string, bool> _valid = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();

        public WorkmanshipVerdictCache() : this(DefaultCapacity) { }

        public WorkmanshipVerdictCache(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        /// <summary>Record a server verdict for one complete signed-stamp fingerprint. A later verdict for the
        /// same fingerprint overwrites the earlier one. Empty fingerprints are ignored. The verdict carries the
        /// fingerprint of the exact bytes the client validated so a verdict can never be reattached to a
        /// different (e.g. mutated) stamp that merely shares a provenance id.</summary>
        public void Apply(in WorkmanshipValidationVerdict verdict)
        {
            string key = verdict.Fingerprint;
            if (string.IsNullOrEmpty(key)) return;

            if (!_valid.ContainsKey(key))
            {
                _order.Add(key);
                if (_order.Count > _capacity)
                {
                    string evict = _order[0];
                    _order.RemoveAt(0);
                    _valid.Remove(evict);
                }
            }
            _valid[key] = verdict.Valid;
        }

        /// <summary>Whether the server has CONFIRMED this EXACT signed-stamp fingerprint valid. Fail closed: a
        /// fingerprint the server has not confirmed (never seen, confirmed Tampered, or a fingerprint that
        /// changed since the last verdict) reads false, so the stamp presents as vanilla.</summary>
        public bool IsConfirmedValid(string fingerprint)
        {
            return !string.IsNullOrEmpty(fingerprint) && _valid.TryGetValue(fingerprint, out bool v) && v;
        }

        /// <summary>Whether the client already holds ANY verdict (valid or tampered) for this EXACT fingerprint —
        /// used by the presentation seam to avoid re-requesting validation it already has an answer for. A stamp
        /// whose bytes changed produces a new fingerprint and therefore correctly reports no verdict yet.</summary>
        public bool HasVerdict(string fingerprint)
        {
            return !string.IsNullOrEmpty(fingerprint) && _valid.ContainsKey(fingerprint);
        }

        /// <summary>Drop a single verdict by fingerprint (e.g. after observing a local edit) so the next read
        /// re-requests confirmation.</summary>
        public void Invalidate(string fingerprint)
        {
            if (!string.IsNullOrEmpty(fingerprint)) _valid.Remove(fingerprint);
        }

        /// <summary>Drop every held verdict — on ZNet teardown / disconnect. After this the cache fails closed
        /// for every fingerprint until fresh verdicts are applied.</summary>
        public void Clear()
        {
            _valid.Clear();
            _order.Clear();
        }

        public int Count => _valid.Count;
    }
}
