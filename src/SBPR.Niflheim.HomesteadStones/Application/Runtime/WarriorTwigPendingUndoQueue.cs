using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T029 remediation — the engine-free, bounded pending queue that closes the ZDO replication race for
    // the Warrior dedicated-server T.W.I.G. gate. Direct analogue of PendingRevalidationQueue (Foundational),
    // specialized to the Warrior ingress outcome.
    //
    // A dedicated client fires its placement notice the instant its local PlacePiece succeeds, but the
    // placed T.W.I.G.'s ZDO transmits to the server LATER (ZDOMan.Update cadence). So a notice usually
    // arrives before the server can resolve the ZDO to gate/undo it. This queue captures the
    // transport-authenticated sender + candidate ZDOID and retries the shared ingress on the pump cadence
    // ONLY until the ZDO resolves (then the ingress decides admit/undo, terminal) or a short deadline
    // expires (dropped; nothing acted on).
    //
    // Invariants (each unit-tested): duplicate notices converge on one entry; a terminal creator-mismatch
    // is dropped without action; an unresolved ZDO past its deadline is dropped without action; the queue
    // is bounded (spam refusal); it is purely in-memory so a restart never re-acts on old loaded pieces.
    //
    // net48 audit: System + collections only. Link-compiles into the net8 test project.
    public sealed class WarriorTwigPendingUndoQueue
    {
        public const int DefaultCapacity = 256;

        private readonly int _capacity;
        private readonly long _deadlineTicks;
        private readonly Dictionary<string, Pending> _byKey;
        private readonly object _gate = new object();

        public WarriorTwigPendingUndoQueue(TimeSpan deadline, int capacity = DefaultCapacity)
        {
            if (deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deadline));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _deadlineTicks = deadline.Ticks;
            _capacity = capacity;
            _byKey = new Dictionary<string, Pending>(StringComparer.Ordinal);
        }

        private readonly struct Pending
        {
            public Pending(string peerKey, string instanceKey, long enqueuedTicks)
            {
                PeerKey = peerKey; InstanceKey = instanceKey; EnqueuedTicks = enqueuedTicks;
            }
            public string PeerKey { get; }
            public string InstanceKey { get; }
            public long EnqueuedTicks { get; }
        }

        public int Count { get { lock (_gate) return _byKey.Count; } }

        public enum EnqueueResult { Enqueued, Converged, RejectedFull, RejectedInvalid }

        /// <summary>Enqueue a transport-authenticated T.W.I.G. placement notice for deferred gating. The
        /// identity is captured now; the gate/undo runs once the ZDO replicates.</summary>
        public EnqueueResult Enqueue(string peerKey, string instanceKey, long nowTicks)
        {
            if (string.IsNullOrEmpty(peerKey) || string.IsNullOrEmpty(instanceKey))
                return EnqueueResult.RejectedInvalid;

            string key = peerKey + "\u0001" + instanceKey;
            lock (_gate)
            {
                if (_byKey.ContainsKey(key)) return EnqueueResult.Converged;
                if (_byKey.Count >= _capacity) return EnqueueResult.RejectedFull;
                _byKey[key] = new Pending(peerKey, instanceKey, nowTicks);
                return EnqueueResult.Enqueued;
            }
        }

        /// <summary>Drive one pump tick. For each pending entry run <paramref name="ingest"/> (the shared
        /// WarriorTwigDedicatedIngress, which re-derives every fact from the server's own ZDO store). An
        /// entry is REMOVED when it resolved (the gate decided — terminal) or its deadline expired; an entry
        /// still awaiting ZDO replication and within deadline is KEPT. Returns the resolved results for the
        /// net48 layer to act on (undo the refused ones) and log.</summary>
        public IReadOnlyList<WarriorTwigIngressResult> Pump(
            long nowTicks, Func<string, string, WarriorTwigIngressResult> ingest)
        {
            if (ingest == null) throw new ArgumentNullException(nameof(ingest));

            List<Pending> snapshot;
            lock (_gate) snapshot = new List<Pending>(_byKey.Values);

            var resolved = new List<WarriorTwigIngressResult>();
            var toRemove = new List<string>();

            foreach (var p in snapshot)
            {
                string key = p.PeerKey + "\u0001" + p.InstanceKey;
                bool expired = (nowTicks - p.EnqueuedTicks) >= _deadlineTicks;

                var result = ingest(p.PeerKey, p.InstanceKey);
                if (result.IsResolved)
                {
                    resolved.Add(result);
                    toRemove.Add(key);
                }
                else if (result.IsAwaitingReplication && !expired)
                {
                    // ZDO not replicated yet, still within deadline — keep polling.
                }
                else
                {
                    // Terminal non-resolution (creator mismatch / malformed) or deadline expired: drop.
                    // No action is taken on a dropped entry.
                    toRemove.Add(key);
                }
            }

            if (toRemove.Count > 0)
            {
                lock (_gate)
                    foreach (var key in toRemove) _byKey.Remove(key);
            }
            return resolved;
        }
    }
}
