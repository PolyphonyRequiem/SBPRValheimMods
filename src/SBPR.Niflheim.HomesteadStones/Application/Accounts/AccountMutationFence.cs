using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-009 Operator foundation — the per-account mutation fence + drain barrier (engine-free CLEAN core).
    //
    // The spec (edge case "Account deletion races an active session"; contracts DisablePilotAccount /
    // DeletePilotAccount) requires that a disable/delete acquire a PER-ACCOUNT fence, WAIT for any
    // already-committing durable mutation to finish (the drain barrier), then atomically commit the
    // lifecycle change. Gameplay/account mutations for one account and its operator lifecycle mutation
    // therefore serialize through the SAME per-account gate, so a lifecycle change can never interleave
    // with a half-committed gameplay transaction.
    //
    // Recoverability contract (task acceptance "Failed drain leaves a coherent recoverable state"): the
    // fence NEVER mutates durable state itself. It only gates. If the drain barrier cannot be acquired
    // within the bounded wait (an in-flight mutation is stuck), the caller aborts WITHOUT committing, so
    // the account is left exactly as it was — Active and admittable — not stranded in a half-disabled
    // state. The atomic single-transaction commit the caller performs under the lease is what makes the
    // eventual disable/delete crash-safe (torn tails quarantine on replay).
    //
    // net48 audit: System.Threading (SemaphoreSlim), System.Collections.Concurrent. No UnityEngine /
    // Valheim / BepInEx, so it link-compiles into the net8 test project and ships under net48.
    public sealed class AccountMutationFence
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _perAccount =
            new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        private SemaphoreSlim GateFor(string accountId)
        {
            if (string.IsNullOrEmpty(accountId)) throw new ArgumentException("accountId required", nameof(accountId));
            return _perAccount.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>A held fence lease. Disposing it releases the per-account gate so the next mutation
        /// (gameplay or operator) may proceed. Every acquire path returns one of these.</summary>
        public sealed class FenceLease : IDisposable
        {
            private readonly SemaphoreSlim _gate;
            private int _released;

            internal FenceLease(SemaphoreSlim gate) { _gate = gate; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                    _gate.Release();
            }
        }

        /// <summary>Enter a durable mutation for this account (gameplay or account write). Blocks until the
        /// account's gate is free, so an operator disable/delete already holding the fence forces this to
        /// wait — and vice versa. Dispose the returned lease when the commit completes.</summary>
        public FenceLease EnterMutation(string accountId)
        {
            var gate = GateFor(accountId);
            gate.Wait();
            return new FenceLease(gate);
        }

        /// <summary>The operator drain barrier: acquire exclusive control of the account's mutations,
        /// waiting up to <paramref name="drainTimeout"/> for any already-committing mutation to finish.
        /// Returns true and a held lease on success; false (no lease, NO state change) if the barrier
        /// could not be drained in time — the caller MUST then abort without mutating, leaving the account
        /// recoverable. A non-positive timeout means "wait indefinitely".</summary>
        public bool TryAcquireForLifecycle(string accountId, TimeSpan drainTimeout, out FenceLease lease)
        {
            lease = null!;
            var gate = GateFor(accountId);
            bool entered = drainTimeout <= TimeSpan.Zero ? WaitBlocking(gate) : gate.Wait(drainTimeout);
            if (!entered) return false;
            lease = new FenceLease(gate);
            return true;
        }

        private static bool WaitBlocking(SemaphoreSlim gate) { gate.Wait(); return true; }
    }
}
