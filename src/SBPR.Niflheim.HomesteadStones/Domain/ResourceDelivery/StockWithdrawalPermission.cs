using System;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T008 (Tracer 3) — the pure canonical delegated-withdrawal permission record (spec RD-017,
    // contracts §GrantStockWithdrawalPermission / §RevokeStockWithdrawalPermission, data-model
    // Aggregate 4 §Delegation). Named acceptance: AT-RD-017.
    //
    // WHAT THIS FILE OWNS (PURE — no I/O, no engine surface)
    //   One canonical current record per (StoneId, grantee AccountId): its generation, its current
    //   Active/Revoked state, and the exact grant/revoke transition rules. The durable coordinator
    //   (StoneStockRegistry) owns persistence, idempotency, and the expected-revision serialization;
    //   this aggregate owns the invariants:
    //     * at most ONE active generation (data-model Aggregate 4 invariant);
    //     * a grant against an already-active record — INCLUDING the same payload — rejects
    //       `StockPermissionAlreadyActive` (contracts §GrantStockWithdrawalPermission);
    //     * a first grant creates generation 1; a regrant AFTER revocation increments generation and
    //       reactivates (contracts: "create generation 1 when absent, or increment generation and
    //       reactivate after prior revocation");
    //     * revocation of an active record deactivates it (removing all current delegated authority for
    //       that grantee); revoking an absent/already-revoked record is a no-op reject;
    //     * delegation is explicit and NON-TRANSITIVE — this record grants only Stock withdrawal, never
    //       Bond/Attunement/node-development/menu-selection/build/AP/BP or the right to re-delegate.
    //     * no expiry in this slice.
    //
    // The "active Bond carrying the server-authored owner role" that may grant/revoke is resolved
    // UPSTREAM and enforced by the coordinator; this aggregate is authority-agnostic state + transitions.
    //
    // net48 audit: only System. Engine-free — link-compiles into the net8 test project.

    /// <summary>Current lifecycle state of a canonical delegated-withdrawal permission.</summary>
    public enum WithdrawalPermissionState
    {
        /// <summary>No canonical record has ever been created for this grantee (generation 0).</summary>
        Absent = 0,
        Active = 1,
        Revoked = 2
    }

    /// <summary>Why a grant/revoke transition rejected. <see cref="Accepted"/> is the only accepting
    /// outcome; every rejection leaves the record unchanged.</summary>
    public enum WithdrawalPermissionTransition
    {
        Accepted = 0,
        AlreadyActive = 1,       // StockPermissionAlreadyActive — grant against an active record
        NotActive = 2            // revoke against an absent/already-revoked record
    }

    /// <summary>One canonical current delegated-withdrawal permission record for a
    /// <c>(StoneId, grantee AccountId)</c> pair (data-model <c>WithdrawalPermissionId</c>). Immutable
    /// value: every transition returns a NEW record, so replay/rehydration reconstructs the exact
    /// generation and state. Generation carries history: it starts at 0 (Absent), becomes 1 on first
    /// grant, and increments on every regrant after a revocation.</summary>
    public readonly struct StockWithdrawalPermission : IEquatable<StockWithdrawalPermission>
    {
        private StockWithdrawalPermission(int generation, WithdrawalPermissionState state)
        {
            Generation = generation;
            State = state;
        }

        /// <summary>Current generation. 0 while Absent; >=1 once granted. A regrant after revocation
        /// increments it, so an earlier-generation UI token can be detected as stale downstream.</summary>
        public int Generation { get; }

        public WithdrawalPermissionState State { get; }

        public bool IsActive => State == WithdrawalPermissionState.Active;

        /// <summary>The canonical starting record before any grant: generation 0, Absent.</summary>
        public static readonly StockWithdrawalPermission None =
            new StockWithdrawalPermission(0, WithdrawalPermissionState.Absent);

        /// <summary>Grant delegated withdrawal (contracts §GrantStockWithdrawalPermission). Creates
        /// generation 1 when absent, or increments generation and reactivates after a prior revocation.
        /// A grant against an already-active record rejects <see cref="WithdrawalPermissionTransition.AlreadyActive"/>
        /// — one canonical record never forks into two active generations.</summary>
        public WithdrawalPermissionTransition TryGrant(out StockWithdrawalPermission next)
        {
            if (State == WithdrawalPermissionState.Active)
            {
                next = this;
                return WithdrawalPermissionTransition.AlreadyActive;
            }
            // Absent -> generation 1; Revoked -> generation+1. Reactivate either way.
            next = new StockWithdrawalPermission(Generation + 1, WithdrawalPermissionState.Active);
            return WithdrawalPermissionTransition.Accepted;
        }

        /// <summary>Revoke delegated withdrawal (contracts §RevokeStockWithdrawalPermission). Deactivates
        /// the one active generation, removing all current delegated authority for the grantee. Revoking
        /// an absent/already-revoked record rejects <see cref="WithdrawalPermissionTransition.NotActive"/>.
        /// The generation is preserved so a later regrant increments from it.</summary>
        public WithdrawalPermissionTransition TryRevoke(out StockWithdrawalPermission next)
        {
            if (State != WithdrawalPermissionState.Active)
            {
                next = this;
                return WithdrawalPermissionTransition.NotActive;
            }
            next = new StockWithdrawalPermission(Generation, WithdrawalPermissionState.Revoked);
            return WithdrawalPermissionTransition.Accepted;
        }

        public bool Equals(StockWithdrawalPermission other) =>
            Generation == other.Generation && State == other.State;
        public override bool Equals(object? obj) => obj is StockWithdrawalPermission other && Equals(other);
        public override int GetHashCode() => unchecked(Generation * 397 ^ (int)State);
        public override string ToString() =>
            "WithdrawalPermission(gen=" + Generation + ", " + State + ")";
    }
}
