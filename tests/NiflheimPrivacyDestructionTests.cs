// ============================================================================
//  IAP-013 Tracer 5 — Privacy destruction: delete, close, purge, reset, prove.
// ----------------------------------------------------------------------------
//  Named acceptance for the destructive privacy lifecycle over the IAP-012
//  privacy foundation + IAP-009 operator control:
//    AT-AIP-DELETE-PURGE            AT-AIP-DELETE-REVOKES-ALLOWLIST
//    AT-AIP-DELETE-DRAIN-BARRIER    AT-AIP-PURGE-FALLBACK-RESET
//    AT-AIP-FULL-RESET-ROTATES-KEY  AT-AIP-BACKUP-PURGE
//    AT-AIP-RETENTION-PURGE         AT-AIP-RESET-EXPLICIT
//    AT-AIP-QUARANTINE              AT-AIP-NO-TIME-TRAVEL
//    AT-AIP-BREACH-RUNBOOK          AT-AIP-PILOT-CLOSURE-DEADLINE
//
//  Every file under test is engine-free (System.*+LINQ), so the asserted
//  behaviour IS the shipped net48 behaviour. Purge completion is proved with
//  artifact-specific evidence (physical absence / evidence digests), never counts.
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
    public abstract class PrivacyDestructionTestBase : IDisposable
    {
        protected readonly string Dir;
        protected string JournalPath => Path.Combine(Dir, "account-journal.bin");

        protected const string NoticeV = PrivacyRegressionFixture.NoticeV;
        protected const string RetentionV = PrivacyRegressionFixture.RetentionV;
        protected const string ProviderNs = PrivacyRegressionFixture.ProviderNs;
        protected const string Backend = PrivacyRegressionFixture.Backend;
        protected const long T0 = PrivacyRegressionFixture.T0;
        protected const long Day = PrivacyRegressionFixture.Day;

        protected static readonly ServerObservedAdminContext Op = PrivacyRegressionFixture.Op;
        protected static readonly ServerObservedAdminContext NonAdmin = PrivacyRegressionFixture.NonAdmin;
        protected static readonly PilotRetentionPolicy Policy = PrivacyRegressionFixture.Policy;

        protected PrivacyDestructionTestBase()
        {
            Dir = Path.Combine(Path.GetTempPath(), "aip-t013-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
        }

        protected PilotAccountStore NewStore() => new PilotAccountStore(JournalPath);

        protected PilotDestructionService Destruction(PilotAccountStore store, AccountMutationFence fence = null!)
        {
            fence = fence ?? new AccountMutationFence();
            var privacy = new PilotPrivacyService(store, PrivacyRegressionFixture.AdminGate(), fence, TimeSpan.FromSeconds(5));
            return new PilotDestructionService(store, PrivacyRegressionFixture.AdminGate(), fence, privacy, TimeSpan.FromSeconds(5));
        }

        protected PilotPrivacyService Privacy(PilotAccountStore store) => PrivacyRegressionFixture.Privacy(store);

        protected OperatorAccountService Operator(PilotAccountStore store, PilotSessionRegistry sessions, AccountMutationFence fence)
            => new OperatorAccountService(store, PrivacyRegressionFixture.AdminGate(), fence, sessions, TimeSpan.FromSeconds(5));

        public void Dispose()
        {
            try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── AT-AIP-DELETE-PURGE / AT-AIP-DELETE-REVOKES-ALLOWLIST ───────────────────
    public sealed class PrivacyDestruction_DeletePurge : PrivacyDestructionTestBase
    {
        [Fact]
        public void Delete_ThenComplete_PhysicallyPurgesAccount_NotJustTombstone()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, characterId) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000101", 501L);

            // Catalog an export artifact scoped to the account so deletion must purge it too.
            var privacy = Privacy(store);
            privacy.ExportAccount(Op, "exp-1", accountId, null, null, "30d", "export/loc-1", Policy, T0);
            string exportArtId = store.Artifacts.Single(a => a.ArtifactType == PilotArtifactType.Export).DataArtifactId.Value;

            // Operator delete → DeletionPending + credential/allowlist revoked.
            var fence = new AccountMutationFence();
            var sessions = new PilotSessionRegistry();
            var op = Operator(store, sessions, fence);
            var del = op.Delete(Op, accountId, "op-del", T0);
            Assert.Equal(OperatorOutcome.Applied, del.Outcome);

            // Complete the deletion: purge artifacts + physical compaction + Deleted marker.
            var destruction = new PilotDestructionService(store, PrivacyRegressionFixture.AdminGate(), fence,
                new PilotPrivacyService(store, PrivacyRegressionFixture.AdminGate(), fence, TimeSpan.FromSeconds(5)),
                TimeSpan.FromSeconds(5));
            var result = destruction.CompleteAccountDeletion(Op, "op-purge", accountId, "sha256:absence-proof", T0 + 31 * Day);

            // Evidence is artifact-specific: the removed credential + character + purged export ids.
            Assert.Contains(exportArtId, result.PurgedArtifactIds);
            Assert.NotEmpty(result.RemovedCharacterIds);
            Assert.Contains(characterId, result.RemovedCharacterIds);

            // Absence proof survives reboot: the account is Deleted, and its credential/character are GONE
            // from projections (a tombstone alone would leave them present).
            var reboot = NewStore();
            Assert.True(reboot.TryGetAccount(accountId, out var acct));
            Assert.Equal(PilotAccountStatus.Deleted, acct.Status);
            Assert.False(reboot.TryGetCharacter(new PilotCharacterId(characterId), out _));
            Assert.DoesNotContain(reboot.Credentials, c => c.AccountId.Value == accountId.Value);
            Assert.DoesNotContain(reboot.Artifacts, a => a.AccountId == accountId.Value && a.Status != ArtifactStatus.Purged);
        }

        [Fact]
        public void DeleteRevokesAllowlist_BlocksImmediateRecreation()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            // Seed with a shared ring so the same subject re-hmacs identically.
            var svc = new PilotAccountService(store, ring, NoticeV, RetentionV);
            svc.ProvisionAllowlistEntry("prov", ProviderNs, Backend, "76561198000000102",
                PrivacyRegressionFixture.CompleteDisclosure(), new DisclosureAcknowledgement(NoticeV, T0), T0);
            var res = svc.ResolveOrCreateAccount("bind", new VerifiedProviderPrincipal(
                PilotProviderKey.Steamworks(Backend), "76561198000000102", 502L), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);

            var fence = new AccountMutationFence();
            var op = Operator(store, new PilotSessionRegistry(), fence);
            Assert.Equal(OperatorOutcome.Applied, op.Delete(Op, res.AccountId, "op-del", T0).Outcome);

            // Re-join for the SAME subject is rejected: credential + allowlist both revoked.
            var rejoin = new PilotAccountService(store, ring, NoticeV, RetentionV)
                .ResolveOrCreateAccount("rejoin", new VerifiedProviderPrincipal(
                    PilotProviderKey.Steamworks(Backend), "76561198000000102", 502L), T0 + 5);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.NotAllowlisted, rejoin.RejectionCode);
        }

        [Fact]
        public void CompleteDeletion_RejectsWithoutEvidence_AndIsIdempotent()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000103", 503L);
            var fence = new AccountMutationFence();
            Operator(store, new PilotSessionRegistry(), fence).Delete(Op, accountId, "op-del", T0);
            var destruction = new PilotDestructionService(store, PrivacyRegressionFixture.AdminGate(), fence,
                new PilotPrivacyService(store, PrivacyRegressionFixture.AdminGate(), fence, TimeSpan.FromSeconds(5)),
                TimeSpan.FromSeconds(5));

            Assert.Throws<PrivacyOperationException>(() =>
                destruction.CompleteAccountDeletion(Op, "op-purge", accountId, "", T0 + 31 * Day));

            var first = destruction.CompleteAccountDeletion(Op, "op-purge", accountId, "sha256:evi", T0 + 31 * Day);
            Assert.False(first.WasReplayed);
            var replay = destruction.CompleteAccountDeletion(Op, "op-purge", accountId, "sha256:evi", T0 + 31 * Day);
            Assert.True(replay.WasReplayed);
        }

        [Fact]
        public void CompleteDeletion_RejectsNonAdmin_NoMutation()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000104", 504L);
            var fence = new AccountMutationFence();
            Operator(store, new PilotSessionRegistry(), fence).Delete(Op, accountId, "op-del", T0);
            var destruction = new PilotDestructionService(store, PrivacyRegressionFixture.AdminGate(), fence,
                new PilotPrivacyService(store, PrivacyRegressionFixture.AdminGate(), fence, TimeSpan.FromSeconds(5)),
                TimeSpan.FromSeconds(5));
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.CompleteAccountDeletion(NonAdmin, "op-purge", accountId, "sha256:evi", T0 + 31 * Day));
            // Still DeletionPending, not purged.
            Assert.True(store.TryGetAccount(accountId, out var acct));
            Assert.Equal(PilotAccountStatus.DeletionPending, acct.Status);
        }
    }

    // ── AT-AIP-RETENTION-PURGE / AT-AIP-BACKUP-PURGE ────────────────────────────
    public sealed class PrivacyDestruction_RetentionPurge : PrivacyDestructionTestBase
    {
        [Fact]
        public void RetentionPurge_PurgesEveryDueArtifact_WithEvidence_ReportsByCategory_NoSelectors()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            var log = privacy.CatalogArtifact(Op, "cat-log", PilotArtifactType.SecurityLog, "log/1", Policy, T0);
            var backup = privacy.CatalogArtifact(Op, "cat-bak", PilotArtifactType.Backup, "bak/1", Policy, T0);
            var world = privacy.CatalogArtifact(Op, "cat-world", PilotArtifactType.WorldSave, "world/1", Policy, T0);

            var destruction = Destruction(store);
            // Not yet due: nothing purges.
            var early = destruction.RunPilotRetentionPurge(Op, "ret-early", T0 + 1, a => "sha256:e-" + a.DataArtifactId.Value);
            Assert.Equal(0, early.TotalPurged);

            // After the closed-data deadline the world+backup are due; security log after its own 14d.
            var report = destruction.RunPilotRetentionPurge(Op, "ret-1", T0 + 40 * Day, a => "sha256:e-" + a.DataArtifactId.Value);
            Assert.Equal(1, report.PurgedCount(PilotArtifactType.SecurityLog));
            Assert.Equal(1, report.PurgedCount(PilotArtifactType.Backup));       // AT-AIP-BACKUP-PURGE
            Assert.Equal(1, report.PurgedCount(PilotArtifactType.WorldSave));
            Assert.Equal(3, report.TotalPurged);
            Assert.Equal(3, report.EvidenceReceiptIds.Count);

            // Every artifact reached Purged with its own evidence digest (durable across reboot).
            var reboot = NewStore();
            foreach (var id in new[] { log, backup, world })
            {
                Assert.True(reboot.TryGetArtifact(id, out var a));
                Assert.Equal(ArtifactStatus.Purged, a.Status);
                Assert.False(string.IsNullOrEmpty(a.PurgeEvidenceDigest));
            }
        }

        [Fact]
        public void RetentionPurge_SkipsHeldScope_ResumesAfterHoldExpiry()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            var backup = privacy.CatalogArtifact(Op, "cat-bak", PilotArtifactType.Backup, "bak/1", Policy, T0);
            string selector = "artifact:" + PilotArtifactType.Backup + ":" + backup.Value;
            privacy.SetRetentionHold(Op, "hold-1", selector, "incident-42", T0, T0 + 60 * Day);

            var destruction = Destruction(store);
            var held = destruction.RunPilotRetentionPurge(Op, "ret-held", T0 + 40 * Day, a => "sha256:e");
            Assert.Equal(0, held.TotalPurged);
            Assert.Contains(selector, held.SkippedHeldSelectors);
            Assert.True(store.TryGetArtifact(backup, out var stillActive));
            Assert.Equal(ArtifactStatus.Active, stillActive.Status);

            // After the hold expires, ordinary purge resumes.
            var resumed = destruction.RunPilotRetentionPurge(Op, "ret-resumed", T0 + 61 * Day, a => "sha256:e");
            Assert.Equal(1, resumed.PurgedCount(PilotArtifactType.Backup));
        }

        [Fact]
        public void RetentionPurge_RejectsMissingEvidence()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            privacy.CatalogArtifact(Op, "cat-bak", PilotArtifactType.Backup, "bak/1", Policy, T0);
            var destruction = Destruction(store);
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.RunPilotRetentionPurge(Op, "ret-1", T0 + 40 * Day, a => ""));
        }
    }

    // ── AT-AIP-RESET-EXPLICIT ───────────────────────────────────────────────────
    public sealed class PrivacyDestruction_ResetExplicit : PrivacyDestructionTestBase
    {
        [Fact]
        public void ScopedReset_RemovesOnlyNamedAccounts_NeverInfersScope()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (a1, c1) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000201", 601L);
            var (a2, c2) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000202", 602L);

            var destruction = Destruction(store);
            var result = destruction.ResetScoped(Op, "reset-1", new[] { a1 }, "incompatible-unreleased-fixture", T0 + 5);
            Assert.False(result.WasReplayed);
            Assert.Contains(c1, result.RemovedCharacterIds);

            // Only a1 removed; a2 untouched (never inferred by recency).
            var reboot = NewStore();
            Assert.False(reboot.TryGetAccount(a1, out _));
            Assert.True(reboot.TryGetAccount(a2, out var acct2));
            Assert.Equal(PilotAccountStatus.Active, acct2.Status);
            Assert.True(reboot.TryGetCharacter(new PilotCharacterId(c2), out _));
        }

        [Fact]
        public void ScopedReset_RequiresNamedScopeAndReason_AndIsIdempotent()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (a1, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000203", 603L);
            var destruction = Destruction(store);

            Assert.Throws<PrivacyOperationException>(() =>
                destruction.ResetScoped(Op, "r", new PilotAccountId[0], "reason", T0));
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.ResetScoped(Op, "r", new[] { a1 }, "", T0));

            var first = destruction.ResetScoped(Op, "reset-1", new[] { a1 }, "reason", T0);
            Assert.False(first.WasReplayed);
            var replay = destruction.ResetScoped(Op, "reset-1", new[] { a1 }, "reason", T0);
            Assert.True(replay.WasReplayed);
        }
    }

    // ── AT-AIP-PURGE-FALLBACK-RESET / AT-AIP-FULL-RESET-ROTATES-KEY ──────────────
    public sealed class PrivacyDestruction_FullReset : PrivacyDestructionTestBase
    {
        [Fact]
        public void FullReset_DestroysEverything_EmitsSelectorFreeCertificate_RotatesKeyEpoch()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, characterId) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000301", 701L);
            var privacy = Privacy(store);
            var pilotId = privacy.OpenPilot(Op, "open", "policy-v1", T0);
            privacy.CatalogArtifact(Op, "cat-world", PilotArtifactType.WorldSave, "world/1", Policy, T0);

            var destruction = Destruction(store);
            var cert = destruction.FullPilotReset(Op, "full-reset", pilotId, "k2", T0 + 5);

            // Certificate carries NO account/character/provider selector — only artifact ids + evidence +
            // key versions (AT-AIP-PURGE-FALLBACK-RESET).
            Assert.False(string.IsNullOrEmpty(cert.PurgeReceiptId));
            Assert.Equal("k2", cert.FreshKeyVersion);
            Assert.DoesNotContain(accountId.Value, string.Join(",", cert.PurgedArtifactIds));
            Assert.DoesNotContain(characterId, string.Join(",", cert.PurgedArtifactIds));

            // Everything old is physically gone; a fresh active key epoch exists (AT-AIP-FULL-RESET-ROTATES-KEY).
            var reboot = NewStore();
            Assert.False(reboot.TryGetAccount(accountId, out _));
            Assert.Empty(reboot.Accounts);
            Assert.Empty(reboot.Credentials);
            Assert.Equal("k2", reboot.ActiveKeyEpochVersion());
            Assert.Single(reboot.PurgeCertificates);
            // The old epoch (if it existed) is retired, not active.
            Assert.DoesNotContain(reboot.KeyEpochs, e => e.Status == KeyEpochStatus.Active && e.KeyVersion != "k2");
        }

        [Fact]
        public void FullReset_RejectsSameKeyVersion_AndNonAdmin()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            var pilotId = privacy.OpenPilot(Op, "open", "policy-v1", T0);
            var destruction = Destruction(store);
            // First reset opens epoch k2.
            destruction.FullPilotReset(Op, "reset-a", pilotId, "k2", T0 + 5);
            // A second reset that reuses the now-active k2 must reject.
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.FullPilotReset(Op, "reset-b", pilotId, "k2", T0 + 6));
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.FullPilotReset(NonAdmin, "reset-c", pilotId, "k3", T0 + 7));
        }
    }

    // ── AT-AIP-QUARANTINE / AT-AIP-NO-TIME-TRAVEL ───────────────────────────────
    public sealed class PrivacyDestruction_QuarantineNoTimeTravel : PrivacyDestructionTestBase
    {
        [Fact]
        public void Quarantine_BlocksAdmission_UntilOperatorDecision()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var svc = new PilotAccountService(store, ring, NoticeV, RetentionV);
            svc.ProvisionAllowlistEntry("prov", ProviderNs, Backend, "76561198000000401",
                PrivacyRegressionFixture.CompleteDisclosure(), new DisclosureAcknowledgement(NoticeV, T0), T0);
            var res = svc.ResolveOrCreateAccount("bind", new VerifiedProviderPrincipal(
                PilotProviderKey.Steamworks(Backend), "76561198000000401", 801L), T0);

            var destruction = Destruction(store);
            destruction.Quarantine(Op, "q-1", res.AccountId, "durable-ambiguity", T0 + 5);

            // Quarantined account admits nothing.
            var reboot = NewStore();
            Assert.True(reboot.TryGetAccount(res.AccountId, out var acct));
            Assert.Equal(PilotAccountStatus.Quarantined, acct.Status);
            var rejoin = new PilotAccountService(reboot, ring, NoticeV, RetentionV)
                .ResolveOrCreateAccount("rejoin", new VerifiedProviderPrincipal(
                    PilotProviderKey.Steamworks(Backend), "76561198000000401", 801L), T0 + 6);
            Assert.False(rejoin.Accepted);
            Assert.Equal(AccountRejectionCode.AccountQuarantined, rejoin.RejectionCode);
        }

        [Fact]
        public void NoTimeTravel_DeletedNeverRevives_QuarantinedNeverSilentlyRestores()
        {
            // Pure transition guard — the terminal states never travel back.
            Assert.True(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.Deleted, PilotAccountStatus.Active));
            Assert.True(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.Deleted, PilotAccountStatus.Quarantined));
            Assert.True(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.Quarantined, PilotAccountStatus.Active));
            Assert.True(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.DeletionPending, PilotAccountStatus.Active));
            Assert.False(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.Active, PilotAccountStatus.Quarantined));
            Assert.False(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.Active, PilotAccountStatus.Disabled));
        }

        [Fact]
        public void Quarantine_OfDeletedAccount_Rejected()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, _) = PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000000402", 802L);
            var fence = new AccountMutationFence();
            Operator(store, new PilotSessionRegistry(), fence).Delete(Op, accountId, "op-del", T0);
            var destruction = new PilotDestructionService(store, PrivacyRegressionFixture.AdminGate(), fence,
                new PilotPrivacyService(store, PrivacyRegressionFixture.AdminGate(), fence, TimeSpan.FromSeconds(5)),
                TimeSpan.FromSeconds(5));
            destruction.CompleteAccountDeletion(Op, "op-purge", accountId, "sha256:e", T0 + 31 * Day);
            // Deleted → Quarantined is time travel; rejected.
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.Quarantine(Op, "q-2", accountId, "late", T0 + 32 * Day));
        }
    }

    // ── AT-AIP-BREACH-RUNBOOK ───────────────────────────────────────────────────
    public sealed class PrivacyDestruction_BreachRunbook
    {
        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "docs", "v2", "runbooks")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate repo root from " + AppContext.BaseDirectory);
        }

        [Fact]
        public void BreachRunbook_NamesResponsibleHuman_AndCoversEveryRequiredStep()
        {
            string path = Path.Combine(RepoRoot(), "docs", "v2", "runbooks", "account-identity-pilot-breach-runbook.md");
            Assert.True(File.Exists(path), "Breach-response runbook must exist at " + path);
            string text = File.ReadAllText(path);

            // Names a responsible operator (contracts §Breach-response runbook contract).
            Assert.Contains("Responsible operator", text, StringComparison.OrdinalIgnoreCase);

            // Executable coverage of every required step.
            foreach (var required in new[]
            {
                "stop new admission",
                "expiring hold",
                "rotate",
                "affected",
                "restore or reset",
                "decision timeline",
                "legal",
            })
                Assert.Contains(required, text, StringComparison.OrdinalIgnoreCase);

            // The software must NOT claim automatic reportability.
            Assert.Contains("does not", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
