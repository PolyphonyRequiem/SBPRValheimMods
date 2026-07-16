using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009R4 (Blocker 5) — the engine-free, TESTABLE bounded pending-revalidation queue that closes the
    // ZDO replication race.
    //
    // The race (adversarial review): a joined dedicated-server client fires its placement NOTICE the
    // instant its local PlacePiece succeeds, but the placed piece's ZDO transmits to the server LATER, on
    // the ZDOMan.Update replication cadence. So the notice frequently arrives BEFORE the authoritative ZDO
    // is resolvable in the server's own store, and an immediate ingest fails NoSuchInstance permanently —
    // the piece is never credited even though it is a legitimate placement.
    //
    // The fix: the transport-bound notice handler does NOT credit inline. It captures the
    // TRANSPORT-AUTHENTICATED sender identity (account + stable character subject, derived from the real
    // ZRpc/ZNetPeer, never the payload) together with the physical ZDOID, and enqueues a pending entry.
    // A pump (driven by the net48 layer on the ZDOMan.Update cadence) retries revalidation ONLY until the
    // authoritative ZDO appears or a short configured deadline expires; then it runs the full shared
    // revalidation exactly once via the same DedicatedPlacementIngress.
    //
    // Invariants this type guarantees (each unit-tested):
    //   * DEDUP / converge — duplicate notices for one physical instance (client resend, reconnect) collapse
    //     onto ONE pending entry keyed by (sender character subject + ZDOID), so retries converge on a
    //     single receipt. The already-idempotent receipt op-id is the second line of defence.
    //   * TIMEOUT writes no credit — an entry whose ZDO never resolves before its deadline is dropped with
    //     no ingest, so a fabricated / griefing key earns nothing and cannot linger.
    //   * BOUNDED against spam — the queue has a hard capacity; once full, further NEW keys are refused
    //     (an attacker cannot exhaust memory by flooding notices). Existing keys still update/converge.
    //   * RESTART never scans / awards — the queue is purely in-memory and never persisted, so a server
    //     restart starts empty and never re-credits old loaded pieces (the notice-driven distinction).
    //
    // net48 audit: System + collections + value objects only. No net5+ surface, no UnityEngine/Valheim,
    // so it link-compiles into the net8 test project and every branch is unit-tested with a fake clock.
    public sealed class PendingRevalidationQueue
    {
        /// <summary>Default bounded capacity. A live playtest server sees a handful of concurrent
        /// in-flight placements; this is generous headroom while still refusing a flood.</summary>
        public const int DefaultCapacity = 256;

        private readonly int _capacity;
        private readonly long _deadlineTicks;
        private readonly Dictionary<string, Pending> _byKey;
        private readonly object _gate = new object();

        /// <param name="deadline">How long to keep polling for an authoritative ZDO before dropping the
        /// entry with no credit. Bounded; must be positive.</param>
        /// <param name="capacity">Hard cap on distinct pending keys (spam bound). Must be positive.</param>
        public PendingRevalidationQueue(TimeSpan deadline, int capacity = DefaultCapacity)
        {
            if (deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deadline));
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _deadlineTicks = deadline.Ticks;
            _capacity = capacity;
            _byKey = new Dictionary<string, Pending>(StringComparer.Ordinal);
        }

        private readonly struct Pending
        {
            public Pending(string account, string character, string instanceKey, long enqueuedTicks)
            {
                Account = account; Character = character; InstanceKey = instanceKey; EnqueuedTicks = enqueuedTicks;
            }
            public string Account { get; }
            public string Character { get; }
            public string InstanceKey { get; }
            public long EnqueuedTicks { get; }
        }

        /// <summary>Number of entries currently awaiting revalidation.</summary>
        public int Count { get { lock (_gate) return _byKey.Count; } }

        /// <summary>Outcome of an enqueue attempt.</summary>
        public enum EnqueueResult
        {
            /// <summary>A new pending entry was accepted.</summary>
            Enqueued,
            /// <summary>A pending entry for this (character, instance) already existed — converged, no dup.</summary>
            Converged,
            /// <summary>The queue is at capacity and this is a new key — refused (spam bound).</summary>
            RejectedFull,
            /// <summary>The sender identity or instance key was empty — refused (unbindable).</summary>
            RejectedInvalid
        }

        /// <summary>Enqueue a transport-authenticated placement notice for later revalidation. The identity
        /// is captured NOW from the authenticated peer; the ingest is deferred until the ZDO replicates.
        /// Duplicate notices for the same (character subject, instance) converge on the one entry.</summary>
        public EnqueueResult Enqueue(string senderAccount, string senderCharacter, string instanceKey, long nowTicks)
        {
            if (string.IsNullOrEmpty(senderAccount) || string.IsNullOrEmpty(senderCharacter) ||
                string.IsNullOrEmpty(instanceKey))
                return EnqueueResult.RejectedInvalid;

            string key = senderCharacter + "\u0001" + instanceKey;
            lock (_gate)
            {
                if (_byKey.ContainsKey(key))
                    return EnqueueResult.Converged;   // duplicate/replayed notice — do NOT reset the deadline
                if (_byKey.Count >= _capacity)
                    return EnqueueResult.RejectedFull;

                _byKey[key] = new Pending(senderAccount, senderCharacter, instanceKey, nowTicks);
                return EnqueueResult.Enqueued;
            }
        }

        /// <summary>Drive one pump tick. For each pending entry, attempt revalidation via
        /// <paramref name="ingest"/> (which independently re-derives every credit-bearing fact from the
        /// server's own ZDO store — the shared DedicatedPlacementIngress). An entry is REMOVED when it is
        /// routed (the ZDO resolved and the shared runtime decided — Earned / Replayed / rejected are all
        /// terminal) OR its deadline has expired (dropped with no credit). An entry whose ZDO is still
        /// absent (NoSuchInstance) and whose deadline has NOT expired is KEPT for the next tick. Returns
        /// the resolved outcomes for operator logging.</summary>
        public IReadOnlyList<DedicatedIngressOutcome> Pump(
            long nowTicks, Func<string, string, string, DedicatedIngressOutcome> ingest)
        {
            if (ingest == null) throw new ArgumentNullException(nameof(ingest));

            List<Pending> snapshot;
            lock (_gate) snapshot = new List<Pending>(_byKey.Values);

            var resolved = new List<DedicatedIngressOutcome>();
            var toRemove = new List<string>();

            foreach (var p in snapshot)
            {
                string key = p.Character + "\u0001" + p.InstanceKey;
                bool expired = (nowTicks - p.EnqueuedTicks) >= _deadlineTicks;

                var outcome = ingest(p.Account, p.Character, p.InstanceKey);
                if (outcome.Routed)
                {
                    // The ZDO resolved and the shared runtime ran its single full revalidation. Terminal.
                    resolved.Add(outcome);
                    toRemove.Add(key);
                }
                else if (outcome.Rejection == DedicatedIngressRejection.NoSuchInstance && !expired)
                {
                    // ZDO has not replicated yet and we still have deadline budget — keep polling.
                }
                else
                {
                    // Either a terminal pre-runtime rejection (MissingInstanceKey / CreatorMismatch — the
                    // authenticated identity does not own the resolved ZDO) or the deadline expired while
                    // the ZDO never appeared. Drop with no credit; a timeout writes nothing.
                    if (outcome.Rejection != DedicatedIngressRejection.NoSuchInstance)
                        resolved.Add(outcome);
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
