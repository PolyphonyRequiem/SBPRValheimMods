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
    // server replies Valid/Tampered (WorkmanshipValidationVerdict). This cache records the
    // last verdict per provenance id so the presentation seam (item tooltip) can render a
    // confirmed Workmanship or degrade to vanilla WITHOUT re-asking every frame, and drops
    // the entry on invalidation (relog / teardown) so it fails closed until re-confirmed.
    //
    // Fail-closed semantics: an unknown provenance id (never confirmed) reads as NOT valid,
    // so a stamp the client has not had the server confirm presents as plain vanilla — the
    // client never trusts a stamp on its own word. The cache is bounded by a simple LRU-ish
    // cap so a flood of distinct provenance ids cannot grow it without bound.
    //
    // net48 audit: engine-free (System.* + engine-free domain value objects). Link-compiles
    // into the net8 test project.
    // ============================================================================

    public sealed class WorkmanshipVerdictCache
    {
        /// <summary>Bound on distinct provenance verdicts held at once (spam guard). When exceeded the oldest
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

        /// <summary>Record a server verdict for one provenance id. A later verdict for the same id overwrites
        /// the earlier one (the server is authoritative and may re-decide after a tamper). Empty provenance ids
        /// are ignored.</summary>
        public void Apply(in WorkmanshipValidationVerdict verdict)
        {
            string key = verdict.ProvenanceId.Value;
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

        /// <summary>Whether the server has CONFIRMED this provenance id valid. Fail closed: an id the server
        /// has not confirmed (or confirmed Tampered) reads false, so an unconfirmed stamp presents as vanilla.</summary>
        public bool IsConfirmedValid(ItemProvenanceId provenanceId)
        {
            string key = provenanceId.Value;
            return !string.IsNullOrEmpty(key) && _valid.TryGetValue(key, out bool v) && v;
        }

        /// <summary>Whether the client already holds ANY verdict (valid or tampered) for this id — used by the
        /// presentation seam to avoid re-requesting validation it already has an answer for.</summary>
        public bool HasVerdict(ItemProvenanceId provenanceId)
        {
            string key = provenanceId.Value;
            return !string.IsNullOrEmpty(key) && _valid.ContainsKey(key);
        }

        /// <summary>Drop a single verdict (e.g. after observing a local edit to that instance) so the next
        /// read re-requests confirmation.</summary>
        public void Invalidate(ItemProvenanceId provenanceId)
        {
            string key = provenanceId.Value;
            if (!string.IsNullOrEmpty(key)) _valid.Remove(key);
        }

        /// <summary>Drop every held verdict — on ZNet teardown / disconnect. After this the cache fails closed
        /// for every id until fresh verdicts are applied.</summary>
        public void Clear()
        {
            _valid.Clear();
            _order.Clear();
        }

        public int Count => _valid.Count;
    }
}
