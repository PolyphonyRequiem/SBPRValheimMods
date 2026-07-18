// ============================================================================
//  RD-T008 (Tracer 3) — Donation menu + Stone Stock vertical-slice tests.
// ----------------------------------------------------------------------------
//  Exercises the pure DonationMenu / StockWithdrawalPermission domain and the
//  durable StoneStockRegistry coordinator (all link-compiled from ../src). This
//  slice closes:
//
//    AT-RD-013 — Donations read back from ONE durable virtual Stockpile;
//                configured capacity overrides preserve provenance/capacity
//                invariants; restart reconstructs the exact Stock.
//    AT-RD-014 — Valid donation transfers exactly once; invalid option, stale
//                revision, insufficient items, pending priority, or full
//                capacity changes nothing.
//    AT-RD-017 — Owner-role grant, duplicate/stale-race denial, revocation,
//                generation-incrementing regrant, restart, and non-transitive
//                denial prove one canonical permission and complete revocation.
//    AT-RD-018 — Level-2 candidate pool is the three exact recipes; authorized
//                two-option selection/default is stable and either selected
//                option satisfies upkeep; unauthorized role rejects.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryStoneStockTests : IDisposable
    {
        private readonly string _journal;
        private static readonly StoneId Stone = new StoneId("world-1|3|4");

        public ResourceDeliveryStoneStockTests()
        {
            _journal = Path.Combine(Path.GetTempPath(), "rd-stock-t008-" + Guid.NewGuid().ToString("N") + ".jrnl");
        }

        public void Dispose()
        {
            if (File.Exists(_journal)) File.Delete(_journal);
        }

        private StoneStockRegistry NewRegistry(
            Dictionary<string, long>? inventory = null,
            Dictionary<string, long>? stock = null,
            CapacityPolicy? capacity = null,
            DonationCandidatePool? pool = null) =>
            new StoneStockRegistry(
                _journal,
                Stone,
                pool ?? DonationCandidatePool.Level2Humble(),
                capacity,
                stock,
                inventory ?? new Dictionary<string, long> { { "Wood", 100 }, { "Stone", 100 } });

        private static readonly AccountId Grantee = new AccountId("acct-grantee");
        private static readonly AccountId Grantee2 = new AccountId("acct-other");

        // ── AT-RD-018 — candidate pool + selection/default ──────────────────────

        [Fact]
        public void Level2Pool_IsTheThreeExactRecipes_WithTheAuthoredDefaultPair()
        {
            var pool = DonationCandidatePool.Level2Humble();
            Assert.Equal(2, pool.StoneLevel);
            Assert.Equal(3, pool.Options.Count);

            // 20 Wood, 20 Stone, 10 Wood + 10 Stone.
            Assert.True(pool.TryGet("humble-20wood", 1, out var w));
            Assert.Equal(20, w.Vector.Items["Wood"]);
            Assert.Equal(1, w.Vector.KindCount);

            Assert.True(pool.TryGet("humble-20stone", 1, out var s));
            Assert.Equal(20, s.Vector.Items["Stone"]);

            Assert.True(pool.TryGet("humble-10wood10stone", 1, out var m));
            Assert.Equal(10, m.Vector.Items["Wood"]);
            Assert.Equal(10, m.Vector.Items["Stone"]);

            // Default pair = 20 Wood + 20 Stone.
            Assert.Equal("humble-20wood", pool.DefaultA.OptionId);
            Assert.Equal("humble-20stone", pool.DefaultB.OptionId);
        }

        [Fact]
        public void OwnerRoleSelection_OfTwoDistinctOptions_IsStableAndLocks()
        {
            var reg = NewRegistry();
            var r = reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-10wood10stone", 1, hasOwnerRoleBond: true, expectedMenuRevision: 0);
            Assert.Equal(MenuSelectionOutcome.Applied, r.Outcome);
            Assert.True(reg.CurrentMenu().IsSelected);
            Assert.Equal(DonationMenuProvenance.OwnerRoleSelection, reg.CurrentMenu().Provenance);
            Assert.True(reg.CurrentMenu().Includes("humble-20wood", 1));
            Assert.True(reg.CurrentMenu().Includes("humble-10wood10stone", 1));

            // Stable for the level: a second selection at the same level rejects (locked).
            var r2 = reg.SelectDonationMenu("sel-2", 2, 1,
                "humble-20wood", 1, "humble-20stone", 1, hasOwnerRoleBond: true, expectedMenuRevision: 1);
            Assert.Equal(MenuSelectionOutcome.AlreadyLocked, r2.Outcome);
        }

        [Fact]
        public void Selection_WithoutOwnerRole_IsRejected_NoMutation()
        {
            var reg = NewRegistry();
            var r = reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-20stone", 1, hasOwnerRoleBond: false, expectedMenuRevision: 0);
            Assert.Equal(MenuSelectionOutcome.OwnerRoleRequired, r.Outcome);
            Assert.False(reg.CurrentMenu().IsSelected);
            Assert.Equal(0, reg.MenuRevision);
        }

        [Fact]
        public void Selection_OfTwoIdenticalOptions_IsRejected()
        {
            var reg = NewRegistry();
            var r = reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-20wood", 1, hasOwnerRoleBond: true, expectedMenuRevision: 0);
            Assert.Equal(MenuSelectionOutcome.OptionsNotDistinct, r.Outcome);
            Assert.False(reg.CurrentMenu().IsSelected);
        }

        [Fact]
        public void Selection_OfUnknownOption_IsRejected()
        {
            var reg = NewRegistry();
            var r = reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-not-real", 1, hasOwnerRoleBond: true, expectedMenuRevision: 0);
            Assert.Equal(MenuSelectionOutcome.OptionNotInPool, r.Outcome);
        }

        [Fact]
        public void Selection_ConcurrentOwnerRole_SerializeByExpectedRevision()
        {
            var reg = NewRegistry();
            // A select against a stale expected revision rejects with no mutation.
            var r = reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-20stone", 1, hasOwnerRoleBond: true, expectedMenuRevision: 1);
            Assert.Equal(MenuSelectionOutcome.StaleMenuRevision, r.Outcome);
            Assert.False(reg.CurrentMenu().IsSelected);
        }

        [Fact]
        public void Default_MaterializesOnceIdempotently_WhenUpkeepNeededBeforeSelection()
        {
            var reg = NewRegistry();
            var r1 = reg.MaterializeDefaultIfNeeded("def-1");
            Assert.Equal(MenuSelectionOutcome.Applied, r1.Outcome);
            Assert.True(reg.CurrentMenu().IsSelected);
            Assert.Equal(DonationMenuProvenance.AuthoredDefault, reg.CurrentMenu().Provenance);
            // 20 Wood + 20 Stone.
            Assert.True(reg.CurrentMenu().Includes("humble-20wood", 1));
            Assert.True(reg.CurrentMenu().Includes("humble-20stone", 1));

            long rev = reg.MenuRevision;
            // A second materialize (distinct op id) is a no-op: menu unchanged, no new revision.
            var r2 = reg.MaterializeDefaultIfNeeded("def-2");
            Assert.Equal(rev, reg.MenuRevision);

            // Replaying the SAME op id returns the recorded result verbatim.
            var r1b = reg.MaterializeDefaultIfNeeded("def-1");
            Assert.Equal(MenuSelectionOutcome.Replayed, r1b.Outcome);
        }

        [Fact]
        public void EitherSelectedOption_SatisfiesUpkeep()
        {
            var reg = NewRegistry();
            reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-20stone", 1, hasOwnerRoleBond: true, expectedMenuRevision: 0);

            // Option A satisfies upkeep.
            var d1 = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.Applied, d1.Outcome);

            // Option B (the other selected option) also satisfies upkeep.
            var d2 = reg.SubmitUpkeepDonation("don-2", "def-1", "humble-20stone", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.Applied, d2.Outcome);
        }

        // ── AT-RD-013 / AT-RD-014 — donation into one Stockpile ─────────────────

        [Fact]
        public void ValidDonation_TransfersExactlyOnce_IntoOneStockpile()
        {
            var reg = NewRegistry();
            reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-20stone", 1, hasOwnerRoleBond: true, expectedMenuRevision: 0);

            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.Applied, d.Outcome);
            Assert.Equal("Applied", d.ResultCode);

            Assert.Equal(20, reg.CurrentStock()["Wood"]);   // 0 + 20
            Assert.Equal(80, reg.CurrentInventory()["Wood"]); // 100 - 20
            Assert.False(reg.CurrentStock().ContainsKey("Stone")); // untouched

            // Provenance recorded with donation kind.
            Assert.Single(reg.Provenance);
            Assert.Equal(StockProvenanceKind.Donation, reg.Provenance[0].Kind);

            // Replay of the same op id does not transfer again.
            var replay = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1, 0, 0);
            Assert.Equal(DonationOutcome.Replayed, replay.Outcome);
            Assert.Equal(20, reg.CurrentStock()["Wood"]); // still 20
        }

        [Fact]
        public void Donation_WithNoPriorSelection_MaterializesDefaultThenTransfers()
        {
            var reg = NewRegistry();
            // No selection yet; the default (20 Wood + 20 Stone) materializes, so 20 Wood is donatable.
            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.Applied, d.Outcome);
            Assert.Equal(DonationMenuProvenance.AuthoredDefault, reg.CurrentMenu().Provenance);
            Assert.Equal(20, reg.CurrentStock()["Wood"]);
        }

        [Fact]
        public void Donation_OfUnselectedOption_Rejected_NoMutation()
        {
            var reg = NewRegistry();
            // Default pair is 20 Wood + 20 Stone; the mixed option is NOT selected.
            reg.MaterializeDefaultIfNeeded("def-1");
            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-10wood10stone", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.OptionNotAccepted, d.Outcome);
            Assert.Equal("DonationOptionNotAccepted", d.ResultCode);
            Assert.Empty(reg.CurrentStock());
            Assert.Equal(100, reg.CurrentInventory()["Wood"]);
        }

        [Fact]
        public void Donation_WithInsufficientItems_Rejected_NoMutation()
        {
            var reg = NewRegistry(inventory: new Dictionary<string, long> { { "Wood", 5 } });
            reg.MaterializeDefaultIfNeeded("def-1");
            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.ItemsMissing, d.Outcome);
            Assert.Equal("DonationItemsMissing", d.ResultCode);
            Assert.Equal(5, reg.CurrentInventory()["Wood"]);
            Assert.Empty(reg.CurrentStock());
        }

        [Fact]
        public void Donation_ExceedingCapacity_Rejected_NoMutation()
        {
            // Per-item cap of 10 means a 20-Wood deposit cannot fit.
            var reg = NewRegistry(capacity: new CapacityPolicy(16, 1000, 10));
            reg.MaterializeDefaultIfNeeded("def-1");
            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.CapacityExceeded, d.Outcome);
            Assert.Equal("StoneStockCapacityExceeded", d.ResultCode);
            Assert.Empty(reg.CurrentStock());
            Assert.Equal(100, reg.CurrentInventory()["Wood"]);
        }

        [Fact]
        public void Donation_WithStaleStockRevision_Rejected_NoMutation()
        {
            var reg = NewRegistry();
            reg.MaterializeDefaultIfNeeded("def-1");
            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                expectedStockRevision: 999, expectedInventoryRevision: reg.InventoryRevision);
            Assert.Equal(DonationOutcome.StaleStockRevision, d.Outcome);
            Assert.Empty(reg.CurrentStock());
        }

        [Fact]
        public void Donation_WhilePendingDeliveryReserved_Rejects_PendingPriority()
        {
            var reg = NewRegistry();
            reg.MaterializeDefaultIfNeeded("def-1");
            reg.SetPendingDelivery("pend-1", true);
            var d = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.PendingDeliveryPriority, d.Outcome);
            Assert.Equal("PendingDeliveryPriority", d.ResultCode);
            Assert.Empty(reg.CurrentStock());

            // Once the pending delivery clears, the same donation transfers.
            reg.SetPendingDelivery("pend-2", false);
            var d2 = reg.SubmitUpkeepDonation("don-2", "def-1", "humble-20wood", 1,
                reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.Applied, d2.Outcome);
        }

        [Fact]
        public void Stockpile_ReadsBackFromDurableJournal_AfterRestart()
        {
            var reg = NewRegistry();
            reg.SelectDonationMenu("sel-1", 2, 1,
                "humble-20wood", 1, "humble-20stone", 1, hasOwnerRoleBond: true, expectedMenuRevision: 0);
            reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1, reg.StockRevision, reg.InventoryRevision);
            reg.SubmitUpkeepDonation("don-2", "def-1", "humble-20stone", 1, reg.StockRevision, reg.InventoryRevision);

            // Fresh registry over the SAME journal + same opening balances reconstructs the exact state.
            var reborn = NewRegistry();
            Assert.Equal(20, reborn.CurrentStock()["Wood"]);
            Assert.Equal(20, reborn.CurrentStock()["Stone"]);
            Assert.Equal(80, reborn.CurrentInventory()["Wood"]);
            Assert.Equal(80, reborn.CurrentInventory()["Stone"]);
            Assert.True(reborn.CurrentMenu().IsSelected);
            Assert.Equal(reg.StockRevision, reborn.StockRevision);

            // A committed donation op replays (does not double-transfer) after restart.
            var replay = reborn.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1, 0, 0);
            Assert.Equal(DonationOutcome.Replayed, replay.Outcome);
            Assert.Equal(20, reborn.CurrentStock()["Wood"]);
        }

        [Fact]
        public void ConfiguredCapacityOverride_PreservesInvariants()
        {
            // A configured override (non-default caps) still gates deposits by the SAME invariant scan.
            var reg = NewRegistry(capacity: new CapacityPolicy(2, 25, 20));
            reg.MaterializeDefaultIfNeeded("def-1");
            // 20 Wood fits (<=20 per item, <=25 total).
            var d1 = reg.SubmitUpkeepDonation("don-1", "def-1", "humble-20wood", 1, reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.Applied, d1.Outcome);
            // A second 20-Stone deposit would push total to 40 > 25 => rejects, nothing changes.
            var d2 = reg.SubmitUpkeepDonation("don-2", "def-1", "humble-20stone", 1, reg.StockRevision, reg.InventoryRevision);
            Assert.Equal(DonationOutcome.CapacityExceeded, d2.Outcome);
            Assert.Equal(20, reg.CurrentStock()["Wood"]);
            Assert.False(reg.CurrentStock().ContainsKey("Stone"));
        }

        // ── AT-RD-017 — canonical delegated-withdrawal permission ───────────────

        [Fact]
        public void OwnerRoleGrant_CreatesGeneration1_ActivePermission()
        {
            var reg = NewRegistry();
            var g = reg.GrantStockWithdrawalPermission("grant-1", Grantee, hasOwnerRoleBond: true, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.Applied, g.Outcome);
            Assert.Equal(1, g.Generation);
            Assert.Equal(WithdrawalPermissionState.Active, g.State);
            Assert.True(reg.PermissionFor(Grantee).IsActive);
        }

        [Fact]
        public void Grant_WithoutOwnerRole_Rejected()
        {
            var reg = NewRegistry();
            var g = reg.GrantStockWithdrawalPermission("grant-1", Grantee, hasOwnerRoleBond: false, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.OwnerRoleRequired, g.Outcome);
            Assert.False(reg.PermissionFor(Grantee).IsActive);
        }

        [Fact]
        public void DuplicateGrant_AgainstActiveRecord_Rejects_AlreadyActive()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);
            // A NEW grant (distinct op id) against an already-active record rejects — even same payload.
            var dup = reg.GrantStockWithdrawalPermission("grant-2", Grantee, true, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.AlreadyActive, dup.Outcome);
            Assert.Equal(1, reg.PermissionFor(Grantee).Generation); // no fork
        }

        [Fact]
        public void Grant_WithStaleRevision_Rejects_NoMutation()
        {
            var reg = NewRegistry();
            // Advance the permission revision with an unrelated grantee's grant.
            reg.GrantStockWithdrawalPermission("grant-x", Grantee2, true, reg.PermissionRevision);
            // Now a grant to Grantee with a stale expected revision (0) rejects.
            var g = reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, expectedPermissionRevision: 0);
            Assert.Equal(PermissionCommandOutcome.StalePermissionRevision, g.Outcome);
            Assert.False(reg.PermissionFor(Grantee).IsActive);
        }

        [Fact]
        public void Revoke_RemovesAllCurrentDelegatedAuthority()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);
            Assert.True(reg.IsWithdrawalAuthorized(Grantee, hasActiveBond: false));

            var r = reg.RevokeStockWithdrawalPermission("revoke-1", Grantee, true, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.Applied, r.Outcome);
            Assert.Equal(WithdrawalPermissionState.Revoked, reg.PermissionFor(Grantee).State);
            Assert.False(reg.IsWithdrawalAuthorized(Grantee, hasActiveBond: false));
        }

        [Fact]
        public void Revoke_OfInactiveRecord_Rejects_NotActive()
        {
            var reg = NewRegistry();
            var r = reg.RevokeStockWithdrawalPermission("revoke-1", Grantee, true, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.NotActive, r.Outcome);
        }

        [Fact]
        public void RegrantAfterRevocation_IncrementsGeneration()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);
            reg.RevokeStockWithdrawalPermission("revoke-1", Grantee, true, reg.PermissionRevision);
            var regrant = reg.GrantStockWithdrawalPermission("grant-2", Grantee, true, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.Applied, regrant.Outcome);
            Assert.Equal(2, regrant.Generation);
            Assert.Equal(WithdrawalPermissionState.Active, regrant.State);
            Assert.True(reg.PermissionFor(Grantee).IsActive);
        }

        [Fact]
        public void Permission_SurvivesRestart_WithExactGenerationAndState()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);
            reg.RevokeStockWithdrawalPermission("revoke-1", Grantee, true, reg.PermissionRevision);
            reg.GrantStockWithdrawalPermission("grant-2", Grantee, true, reg.PermissionRevision);

            var reborn = NewRegistry();
            var p = reborn.PermissionFor(Grantee);
            Assert.Equal(2, p.Generation);
            Assert.Equal(WithdrawalPermissionState.Active, p.State);
            Assert.True(reborn.IsWithdrawalAuthorized(Grantee, hasActiveBond: false));

            // A committed grant op replays after restart (no third generation).
            var replay = reborn.GrantStockWithdrawalPermission("grant-2", Grantee, true, 0);
            Assert.Equal(PermissionCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(2, reborn.PermissionFor(Grantee).Generation);
        }

        [Fact]
        public void Delegation_IsNonTransitive_AndScopedToWithdrawalOnly()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);

            // The grantee is authorized to WITHDRAW (predicate true) but carries NO owner role, so it
            // cannot itself grant to a third party — a grant with hasOwnerRoleBond:false rejects.
            Assert.True(reg.IsWithdrawalAuthorized(Grantee, hasActiveBond: false));
            var onward = reg.GrantStockWithdrawalPermission("grant-onward", Grantee2, hasOwnerRoleBond: false, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.OwnerRoleRequired, onward.Outcome);
            Assert.False(reg.PermissionFor(Grantee2).IsActive);
        }

        [Fact]
        public void AnotherGranteesPermission_DoesNotImplyAuthority()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);
            // Grantee2 was never granted; it has no authority even though Grantee does.
            Assert.False(reg.IsWithdrawalAuthorized(Grantee2, hasActiveBond: false));
        }

        [Fact]
        public void OperationConflict_WhenGrantOpIdReusedWithDifferentGrantee()
        {
            var reg = NewRegistry();
            reg.GrantStockWithdrawalPermission("grant-1", Grantee, true, reg.PermissionRevision);
            // Same op id, different binding (grantee) => conflict, no mutation.
            var conflict = reg.GrantStockWithdrawalPermission("grant-1", Grantee2, true, reg.PermissionRevision);
            Assert.Equal(PermissionCommandOutcome.OperationConflict, conflict.Outcome);
            Assert.False(reg.PermissionFor(Grantee2).IsActive);
        }
    }
}
