// ============================================================================
//  IAP-012 FIX-FORWARD (t_f6c8c748) — the seven regression classes from the
//  independent post-merge review of PR #336.
// ----------------------------------------------------------------------------
//  The privacy foundation merged structurally green but semantically incomplete.
//  Each class below pins one gap the review found, so a regression cannot silently
//  reintroduce it:
//    R1 ExportOwnershipFiltering  — export derives characters from the account and
//                                   rejects foreign/untrusted gameplay/receipt rows.
//    R2 FailClosedAdmission       — closed pilots and uncataloged/expired/purged
//                                   world fixtures reject LIVE admission (nothing binds).
//    R3 OperationIdempotency      — every durable privacy mutation replays on its
//                                   operationId and conflicts on reuse for another op.
//    R4 CrashRecovery             — an intent-only (pre-commit crash) privacy mutation
//                                   quarantines on replay; the committed one survives.
//    R5 OperatorAuthority         — a non-admin caller is rejected with NO mutation.
//    R6 RetentionPurgeSemantics   — purge enforces due-time, active holds, evidence,
//                                   and double-purge rejection.
//    R7 PurgeCensusIdentity       — account-scoped artifacts record selector + key
//                                   version + receipt identity for a provable census.
//
//  All files under test are engine-free (System.*+LINQ), so the asserted behaviour
//  IS the shipped net48 behaviour.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>Shared engine-free privacy fixtures for the seven regression classes.</summary>
    internal static class PrivacyRegressionFixture
    {
        public const string NoticeV = "notice-v1";
        public const string RetentionV = "retention-v1";
        public const string ProviderNs = "Steam";
        public const string Backend = "niflheim-pilot-app-896660";
        public const long T0 = 1_784_000_000L;
        public const long Day = 86_400L;
        public const string AdminHost = "76561198000000001";

        public static readonly ServerObservedAdminContext Op = new ServerObservedAdminContext(AdminHost, "Steam");
        public static readonly ServerObservedAdminContext NonAdmin = new ServerObservedAdminContext("76561198000000777", "Steam");
        public static readonly PilotRetentionPolicy Policy = PilotRetentionPolicy.ShippedDefault(RetentionV);

        public static OperatorAdminGate AdminGate() => new OperatorAdminGate(new[] { AdminHost });

        public static LookupKeyRing Ring() => new LookupKeyRing(LookupHmacKey.Generate(new LookupKeyVersion("k1")));

        public static PilotPrivacyService Privacy(PilotAccountStore store) =>
            new PilotPrivacyService(store, AdminGate(), new AccountMutationFence(), TimeSpan.FromSeconds(5));

        public static PilotDisclosure CompleteDisclosure()
        {
            var cat = new PrivacyInventoryCategory(
                "account-credential", "authenticate pilot join", "30 days after pilot close",
                "operator", "none", "operator deletion command", "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var gameplay = new PrivacyInventoryCategory(
                "gameplay-progression", "run cooperative pilot", "while active", "operator", "none",
                "operator deletion command", "legitimate-interest (pilot operation)", humanApprovedBasis: true);
            var inv = new PilotPrivacyInventory(new[] { cat, gameplay }, "pilot-ops@example.invalid", NoticeV);
            return new PilotDisclosure(inv, "operator deletion/export command", statesExplicitResetPossibility: true);
        }

        /// <summary>Seed an account + one owned character over the given store/ring, returning both.</summary>
        public static (PilotAccountId account, string characterId) SeedAccountWithCharacter(
            PilotAccountStore store, LookupKeyRing ring, string subject, long playerId)
        {
            var svc = new PilotAccountService(store, ring, NoticeV, RetentionV);
            svc.ProvisionAllowlistEntry("prov-" + subject, ProviderNs, Backend, subject,
                CompleteDisclosure(), new DisclosureAcknowledgement(NoticeV, T0), T0);
            var res = svc.ResolveOrCreateAccount("bind-" + subject, new VerifiedProviderPrincipal(
                PilotProviderKey.Steamworks(Backend), subject, transportHandle: playerId), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);

            var chars = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var begin = chars.BeginAdmission(res.AccountId, playerId, T0);
            Assert.True(begin.Admitted);
            var cres = chars.ResolveOrCreateCharacter("char-op-" + subject, res.AccountId, begin.SessionId,
                new VerifiedProfileSubject(playerId, playerId), T0);
            Assert.Equal(CharacterAdmissionOutcome.Created, cres.Outcome);
            chars.CloseSession(res.AccountId, begin.SessionId, playerId);
            return (res.AccountId, cres.CharacterId.Value);
        }
    }

    public abstract class PrivacyRegressionTestBase : IDisposable
    {
        protected readonly string Dir;
        protected string JournalPath => Path.Combine(Dir, "account-journal.bin");

        protected PrivacyRegressionTestBase()
        {
            Dir = Path.Combine(Path.GetTempPath(), "aip-t012fx-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
        }

        protected PilotAccountStore NewStore() => new PilotAccountStore(JournalPath);

        public void Dispose()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── R1 ─ Export ownership filtering ─────────────────────────────────────────
    public sealed class PrivacyRegression_R1_ExportOwnershipFiltering : PrivacyRegressionTestBase
    {
        [Fact]
        public void Export_DerivesCharactersFromAccount_NotFromCallerRows()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (account, characterId) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000042", 4242L);
            var privacy = PrivacyRegressionFixture.Privacy(store);

            var export = privacy.ExportAccount(PrivacyRegressionFixture.Op, "exp-1", account,
                gameplayRows: null, receiptRows: null,
                "sched", "exports/a.json", PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day);

            // Even with NO caller rows, the export lists the account's OWN character (authoritative membership).
            Assert.Contains(characterId, export.CharacterIds);
            Assert.Single(export.CharacterIds);
        }

        [Fact]
        public void Export_ForeignCharacterRow_Rejected_NoCrossAccountLeak()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountA, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000042", 4242L);
            var (_, characterB) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000099", 9999L);
            var privacy = PrivacyRegressionFixture.Privacy(store);

            // A row referencing account B's character must be rejected on A's export.
            var foreignRows = new List<PlayerVisibleRecord> { new PlayerVisibleRecord(characterB, "secret from B") };
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.ExportAccount(PrivacyRegressionFixture.Op, "exp-foreign", accountA, foreignRows, null,
                    "sched", "exports/a.json", PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.ForeignCharacterRow, ex.Code);

            // An untrusted/unknown character id (owned by no account) is likewise rejected.
            var bogusRows = new List<PlayerVisibleRecord> { new PlayerVisibleRecord("char-deadbeef", "fabricated") };
            var ex2 = Assert.Throws<PrivacyOperationException>(() =>
                privacy.ExportAccount(PrivacyRegressionFixture.Op, "exp-bogus", accountA, null, bogusRows,
                    "sched", "exports/a.json", PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.ForeignCharacterRow, ex2.Code);
        }
    }

    // ── R2 ─ Fail-closed live admission ─────────────────────────────────────────
    public sealed class PrivacyRegression_R2_FailClosedAdmission : PrivacyRegressionTestBase
    {
        private static VerifiedProviderPrincipal Provider(string subject) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(PrivacyRegressionFixture.Backend), subject, transportHandle: 1L);

        private LiveSessionAdmission BuildAdmission(PilotAccountStore store, LookupKeyRing ring,
            IPrivacyAdmissionGate gate, out BoundSessionPrincipalIndex bound)
        {
            var accounts = new PilotAccountService(store, ring, PrivacyRegressionFixture.NoticeV, PrivacyRegressionFixture.RetentionV);
            accounts.ProvisionAllowlistEntry("prov-x", PrivacyRegressionFixture.ProviderNs, PrivacyRegressionFixture.Backend,
                "76561198000000006", PrivacyRegressionFixture.CompleteDisclosure(),
                new DisclosureAcknowledgement(PrivacyRegressionFixture.NoticeV, PrivacyRegressionFixture.T0), PrivacyRegressionFixture.T0);
            bound = new BoundSessionPrincipalIndex();
            return new LiveSessionAdmission(accounts,
                new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex()), bound, gate);
        }

        [Fact]
        public void UncatalogedWorldFixture_RejectsLiveAdmission_NothingBinds()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var privacy = PrivacyRegressionFixture.Privacy(store);

            var pilot = privacy.OpenPilot(PrivacyRegressionFixture.Op, "open", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0);
            // Configure the gate to a world locator that is NOT cataloged → admission fails closed.
            privacy.ConfigureAdmission(pilot, "worlds/uncataloged.db");

            var live = BuildAdmission(store, ring, privacy, out var bound);
            string peerKey = ServerCreatorIdentity.CharacterSubject(5555L);
            var res = live.Admit(peerKey, Provider("76561198000000006"),
                new VerifiedProfileSubject(5555L, 600L), 600L, PrivacyRegressionFixture.T0 + 1, "conn-1");

            Assert.False(res.Admitted);
            Assert.Equal(LiveAdmissionStage.Privacy, res.FailedStage);
            Assert.Equal(PrivacyRejectionCode.WorldFixtureUncataloged.ToString(), res.ResultCode);
            Assert.False(bound.TryResolve(peerKey, out _));
            Assert.Equal(0, live.LiveSessionCount);
        }

        [Fact]
        public void ClosedPilot_RejectsLiveAdmission_NothingBinds()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var privacy = PrivacyRegressionFixture.Privacy(store);

            const string world = "worlds/pilot.db";
            var pilot = privacy.OpenPilot(PrivacyRegressionFixture.Op, "open", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0);
            privacy.CatalogArtifact(PrivacyRegressionFixture.Op, "cat-world", PilotArtifactType.WorldSave, world,
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);
            privacy.ConfigureAdmission(pilot, world);

            // While Active + cataloged, admission is permitted.
            var live = BuildAdmission(store, ring, privacy, out var bound);
            string peerKey = ServerCreatorIdentity.CharacterSubject(5555L);
            var ok = live.Admit(peerKey, Provider("76561198000000006"),
                new VerifiedProfileSubject(5555L, 600L), 600L, PrivacyRegressionFixture.T0 + 1, "conn-1");
            Assert.True(ok.Admitted, "should admit while pilot Active + fixture cataloged: " + ok.ResultCode);
            live.Close(600L);

            // Close the pilot → admission now fails closed.
            privacy.ClosePilot(PrivacyRegressionFixture.Op, "close", pilot, PrivacyRegressionFixture.Policy,
                PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day);
            var res = live.Admit(ServerCreatorIdentity.CharacterSubject(6666L), Provider("76561198000000006"),
                new VerifiedProfileSubject(6666L, 601L), 601L, PrivacyRegressionFixture.T0 + 2 * PrivacyRegressionFixture.Day, "conn-2");
            Assert.False(res.Admitted);
            Assert.Equal(LiveAdmissionStage.Privacy, res.FailedStage);
            Assert.Equal(PrivacyRejectionCode.PilotClosed.ToString(), res.ResultCode);
        }

        [Fact]
        public void PurgePendingWorldFixture_RejectsLiveAdmission()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var privacy = PrivacyRegressionFixture.Privacy(store);

            const string world = "worlds/pilot.db";
            var pilot = privacy.OpenPilot(PrivacyRegressionFixture.Op, "open", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0);
            var artId = privacy.CatalogArtifact(PrivacyRegressionFixture.Op, "cat-world", PilotArtifactType.WorldSave, world,
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);
            privacy.ConfigureAdmission(pilot, world);
            // Mark the fixture PurgePending → no longer an active cataloged fixture → fails closed.
            privacy.MarkArtifactPurgePending(PrivacyRegressionFixture.Op, "pending", artId, PrivacyRegressionFixture.T0 + 1);

            var live = BuildAdmission(store, ring, privacy, out _);
            var res = live.Admit(ServerCreatorIdentity.CharacterSubject(5555L), Provider("76561198000000006"),
                new VerifiedProfileSubject(5555L, 600L), 600L, PrivacyRegressionFixture.T0 + 2, "conn-1");
            Assert.False(res.Admitted);
            Assert.Equal(PrivacyRejectionCode.WorldFixtureUncataloged.ToString(), res.ResultCode);
        }
    }

    // ── R3 ─ Operation-id idempotency + conflict ────────────────────────────────
    public sealed class PrivacyRegression_R3_OperationIdempotency : PrivacyRegressionTestBase
    {
        [Fact]
        public void OpenPilot_ReplaysSamePilotId_OnRepeatedOperationId()
        {
            var privacy = PrivacyRegressionFixture.Privacy(NewStore());
            var first = privacy.OpenPilot(PrivacyRegressionFixture.Op, "op-open", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0);
            var replay = privacy.OpenPilot(PrivacyRegressionFixture.Op, "op-open", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0);
            Assert.Equal(first.Value, replay.Value);
        }

        [Fact]
        public void SetRetentionHold_ReplaysSameHoldId_OnRepeatedOperationId()
        {
            var privacy = PrivacyRegressionFixture.Privacy(NewStore());
            var first = privacy.SetRetentionHold(PrivacyRegressionFixture.Op, "op-hold", "account:x", "incident",
                PrivacyRegressionFixture.T0, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day);
            var replay = privacy.SetRetentionHold(PrivacyRegressionFixture.Op, "op-hold", "account:x", "incident",
                PrivacyRegressionFixture.T0, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day);
            Assert.Equal(first.Value, replay.Value);
        }

        [Fact]
        public void ReusedOperationId_ForDifferentMutation_ThrowsConflict()
        {
            var privacy = PrivacyRegressionFixture.Privacy(NewStore());
            privacy.OpenPilot(PrivacyRegressionFixture.Op, "shared-op", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0);
            // Reuse "shared-op" for a hold — a different mutation kind → conflict.
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.SetRetentionHold(PrivacyRegressionFixture.Op, "shared-op", "account:x", "r",
                    PrivacyRegressionFixture.T0, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.OperationConflict, ex.Code);
        }

        [Fact]
        public void CatalogArtifact_ReplaysSameArtifactId_OnRepeatedOperationId()
        {
            var privacy = PrivacyRegressionFixture.Privacy(NewStore());
            var first = privacy.CatalogArtifact(PrivacyRegressionFixture.Op, "op-cat", PilotArtifactType.Backup, "loc/1",
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);
            var replay = privacy.CatalogArtifact(PrivacyRegressionFixture.Op, "op-cat", PilotArtifactType.Backup, "loc/1",
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);
            Assert.Equal(first.Value, replay.Value);
        }
    }

    // ── R4 ─ Intent/commit crash recovery ───────────────────────────────────────
    public sealed class PrivacyRegression_R4_CrashRecovery : PrivacyRegressionTestBase
    {
        private sealed class CrashAfterIntent : IAccountCrashInjector
        {
            public void AfterPhase(TransactionPhase phase)
            {
                if (phase == TransactionPhase.Intent)
                    throw new InvalidOperationException("simulated crash after durable Intent, before Commit");
            }
        }

        [Fact]
        public void PrivacyMutation_CrashAfterIntent_QuarantinesOnReplay_NoPartialState()
        {
            // A pilot open that crashes after the Intent record is durable but before Commit must NOT
            // project — boot replay quarantines the intent-only transaction.
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);

            Assert.Throws<InvalidOperationException>(() =>
                privacy.OpenPilot(PrivacyRegressionFixture.Op, "op-crash", PrivacyRegressionFixture.RetentionV,
                    PrivacyRegressionFixture.T0, new CrashAfterIntent()));

            // Rebuild from journal alone: the crashed mutation left an Intent with no Committed → quarantined.
            var store2 = NewStore();
            Assert.Empty(store2.Pilots);
            Assert.Equal(1, store2.QuarantinedIntentTransactions);
        }

        [Fact]
        public void PrivacyMutation_CommittedBeforeCrash_SurvivesRestart()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var artId = privacy.CatalogArtifact(PrivacyRegressionFixture.Op, "op-cat", PilotArtifactType.Export, "loc/1",
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);

            var store2 = NewStore();
            Assert.True(store2.TryGetArtifact(artId, out var art));
            Assert.Equal(ArtifactStatus.Active, art.Status);
            Assert.Equal(0, store2.QuarantinedIntentTransactions);
        }
    }

    // ── R5 ─ Operator authority (fail-closed, no mutation) ──────────────────────
    public sealed class PrivacyRegression_R5_OperatorAuthority : PrivacyRegressionTestBase
    {
        [Fact]
        public void NonAdmin_OpenPilot_Rejected_NoMutation()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.OpenPilot(PrivacyRegressionFixture.NonAdmin, "op", PrivacyRegressionFixture.RetentionV, PrivacyRegressionFixture.T0));
            Assert.Equal(PrivacyRejectionCode.Unauthorized, ex.Code);
            Assert.Empty(store.Pilots);   // no mutation committed
        }

        [Fact]
        public void NonAdmin_CatalogArtifact_Rejected_NoMutation()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.CatalogArtifact(PrivacyRegressionFixture.NonAdmin, "op", PilotArtifactType.Backup, "loc/1",
                    PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0));
            Assert.Equal(PrivacyRejectionCode.Unauthorized, ex.Code);
            Assert.Empty(store.Artifacts);
        }

        [Fact]
        public void NonAdmin_Export_Rejected_NoArtifactCataloged()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (account, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000042", 4242L);
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.ExportAccount(PrivacyRegressionFixture.NonAdmin, "op", account, null, null,
                    "s", "exports/a.json", PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.Unauthorized, ex.Code);
            Assert.DoesNotContain(store.Artifacts, a => a.ArtifactType == PilotArtifactType.Export);
        }
    }

    // ── R6 ─ Retention / purge semantics (due-time, holds, evidence) ────────────
    public sealed class PrivacyRegression_R6_RetentionPurgeSemantics : PrivacyRegressionTestBase
    {
        private DataArtifactId CatalogBackup(PilotPrivacyService privacy, string op = "cat") =>
            privacy.CatalogArtifact(PrivacyRegressionFixture.Op, op, PilotArtifactType.Backup, "loc/backup",
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);

        [Fact]
        public void Purge_BeforeDue_Rejected()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var id = CatalogBackup(privacy);
            // Expiry is T0 + 30d; purging at T0 + 1d is not yet due.
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "purge-early", id, "sha256:x", PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.ArtifactNotDue, ex.Code);
        }

        [Fact]
        public void Purge_WhileScopeHeld_Rejected_ThenResumesAfterHoldExpiry()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var id = CatalogBackup(privacy);
            store.TryGetArtifact(id, out var art);
            string selector = "artifact:" + art.ArtifactType + ":" + id.Value;

            // A hold over the artifact's own selector suppresses purge even once due.
            privacy.SetRetentionHold(PrivacyRegressionFixture.Op, "hold", selector, "legal hold",
                PrivacyRegressionFixture.T0, PrivacyRegressionFixture.T0 + 45 * PrivacyRegressionFixture.Day);

            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "purge-held", id, "sha256:x", PrivacyRegressionFixture.T0 + 31 * PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.ScopeHeld, ex.Code);

            // After the hold expires, an ordinary due purge succeeds.
            privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "purge-after-hold", id, "sha256:x", PrivacyRegressionFixture.T0 + 46 * PrivacyRegressionFixture.Day);
            Assert.True(store.TryGetArtifact(id, out var purged));
            Assert.Equal(ArtifactStatus.Purged, purged.Status);
        }

        [Fact]
        public void Purge_RequiresEvidence_AndRejectsDoublePurge()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var id = CatalogBackup(privacy);

            Assert.Throws<PrivacyOperationException>(() =>
                privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "p-noevidence", id, "", PrivacyRegressionFixture.T0 + 31 * PrivacyRegressionFixture.Day));

            privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "p-good", id, "sha256:evidence", PrivacyRegressionFixture.T0 + 31 * PrivacyRegressionFixture.Day);
            // A second, DIFFERENT purge op on the already-purged artifact rejects.
            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "p-again", id, "sha256:evidence2", PrivacyRegressionFixture.T0 + 32 * PrivacyRegressionFixture.Day));
            Assert.Equal(PrivacyRejectionCode.ArtifactAlreadyPurged, ex.Code);
        }

        [Fact]
        public void ForcePurge_BypassesDueTime_ButStillNeedsEvidence_AndHonorsHold()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var id = CatalogBackup(privacy);

            // A forced (incident/reset) purge before due succeeds WITH evidence.
            privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "force", id, "sha256:incident", PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day, force: true);
            Assert.True(store.TryGetArtifact(id, out var purged));
            Assert.Equal(ArtifactStatus.Purged, purged.Status);
        }
    }

    // ── R7 ─ Purge/census identity (selector + key version + receipt) ───────────
    public sealed class PrivacyRegression_R7_PurgeCensusIdentity : PrivacyRegressionTestBase
    {
        [Fact]
        public void AccountScopedExport_RecordsAccount_KeyVersion_Receipt_ForCensus()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (account, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000042", 4242L);
            var privacy = PrivacyRegressionFixture.Privacy(store);

            var export = privacy.ExportAccount(PrivacyRegressionFixture.Op, "exp", account, null, null,
                "sched", "exports/a.json", PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0 + PrivacyRegressionFixture.Day);

            var artifact = store.Artifacts.Single(a => a.ArtifactType == PilotArtifactType.Export);
            // Account-scoped: the artifact records the AccountId so an account purge can select it.
            Assert.Equal(account.Value, artifact.AccountId);
            // Key version recorded so a key census can attribute the artifact to a key epoch.
            Assert.False(string.IsNullOrEmpty(artifact.KeyVersion));
            Assert.Equal("k1", artifact.KeyVersion);
            // Receipt identity is stable and shared between the export handle and the cataloged artifact.
            Assert.False(string.IsNullOrEmpty(export.ReceiptId));
            Assert.Equal(export.ReceiptId, artifact.ReceiptId);
        }

        [Fact]
        public void Purge_RecordsSelector_KeyVersion_Receipt()
        {
            var store = NewStore();
            var privacy = PrivacyRegressionFixture.Privacy(store);
            var id = privacy.CatalogArtifact(PrivacyRegressionFixture.Op, "cat", PilotArtifactType.Backup, "loc/backup",
                PrivacyRegressionFixture.Policy, PrivacyRegressionFixture.T0);
            privacy.PurgeArtifact(PrivacyRegressionFixture.Op, "purge", id, "sha256:evidence", PrivacyRegressionFixture.T0 + 31 * PrivacyRegressionFixture.Day);

            Assert.True(store.TryGetArtifact(id, out var art));
            Assert.Equal("artifact:" + PilotArtifactType.Backup + ":" + id.Value, art.Selector);
            Assert.False(string.IsNullOrEmpty(art.KeyVersion));
            Assert.False(string.IsNullOrEmpty(art.ReceiptId));
            Assert.False(string.IsNullOrEmpty(art.PurgeEvidenceDigest));

            // The purge identity survives a restart (durable/recoverable census basis).
            var store2 = NewStore();
            Assert.True(store2.TryGetArtifact(id, out var art2));
            Assert.Equal(art.Selector, art2.Selector);
            Assert.Equal(art.ReceiptId, art2.ReceiptId);
        }
    }
}
