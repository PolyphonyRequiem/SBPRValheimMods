// ============================================================================
//  IAP-014 — Independent adversarial verification of the privacy/deletion/purge
//  lifecycle merged via PR #336 (IAP-012) + PR #385 (IAP-013).
// ----------------------------------------------------------------------------
//  These are NOT a re-run of the author's tests. They are reviewer-authored
//  negative fixtures that attack the PASS gate directly:
//
//    (A) An UNCATALOGED world save must reject live admission (fail closed).
//    (B) A "supposedly purged" backup that is actually still Active/Purged in
//        the catalog must NOT survive a terminal FullPilotReset — after the
//        terminal purge, ZERO cataloged artifacts of ANY status remain on disk.
//    (C) Terminal purge with a dense catalog (world/backup/export/log/journal +
//        an already-Purged artifact + a never-expiring ResetAudit) leaves the
//        fixture empty of every artifact class; nothing cataloged can outlive it.
//    (D) No-time-travel: a terminal state cannot be walked back to live.
//
//  Every file under test is engine-free (System.*+LINQ), so the asserted
//  behaviour IS the shipped net48 behaviour.
// ============================================================================

using System;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class PrivacyLifecycle_IndependentAudit : IDisposable
    {
        private readonly string _dir;
        private string JournalPath => Path.Combine(_dir, "account-journal.bin");
        private const long T0 = PrivacyRegressionFixture.T0;
        private const long Day = PrivacyRegressionFixture.Day;
        private static readonly ServerObservedAdminContext Op = PrivacyRegressionFixture.Op;
        private static readonly PilotRetentionPolicy Policy = PrivacyRegressionFixture.Policy;

        public PrivacyLifecycle_IndependentAudit()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-t014-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        private PilotAccountStore NewStore() => new PilotAccountStore(JournalPath);
        private PilotPrivacyService Privacy(PilotAccountStore s) => PrivacyRegressionFixture.Privacy(s);
        private PilotDestructionService Destruction(PilotAccountStore store, AccountMutationFence fence)
        {
            var privacy = new PilotPrivacyService(store, PrivacyRegressionFixture.AdminGate(), fence, TimeSpan.FromSeconds(5));
            return new PilotDestructionService(store, PrivacyRegressionFixture.AdminGate(), fence, privacy, TimeSpan.FromSeconds(5));
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── (A) Negative fixture: an uncataloged world save fails admission closed ──
        [Fact]
        public void NegativeFixture_UncatalogedWorldSave_RejectsAdmission()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            var pilotId = privacy.OpenPilot(Op, "open", "policy-v1", T0);

            // Point admission at a world locator that was NEVER cataloged. This is the
            // "an uncataloged world save" negative fixture the task requires.
            privacy.ConfigureAdmission(pilotId, "world/UNCATALOGED-fixture");

            Assert.Equal(PrivacyRejectionCode.WorldFixtureUncataloged, privacy.EvaluateAdmission(T0 + 1));
            Assert.Throws<PrivacyOperationException>(() =>
                privacy.RequireCatalogedWorldFixture("world/UNCATALOGED-fixture", T0 + 1));

            // Contrast: once the SAME locator is genuinely cataloged as a WorldSave, admission passes.
            privacy.CatalogArtifact(Op, "cat-w", PilotArtifactType.WorldSave, "world/UNCATALOGED-fixture", Policy, T0);
            Assert.Equal(PrivacyRejectionCode.None, privacy.EvaluateAdmission(T0 + 2));
        }

        // ── (A') Negative fixture: a PURGED world save also fails admission closed ──
        [Fact]
        public void NegativeFixture_PurgedWorldSave_RejectsAdmission()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            var pilotId = privacy.OpenPilot(Op, "open", "policy-v1", T0);
            var wid = privacy.CatalogArtifact(Op, "cat-w", PilotArtifactType.WorldSave, "world/live", Policy, T0);
            privacy.ConfigureAdmission(pilotId, "world/live");
            Assert.Equal(PrivacyRejectionCode.None, privacy.EvaluateAdmission(T0 + 1));

            // Force-purge the live world fixture; admission must now fail closed (not Active anymore).
            privacy.PurgeArtifact(Op, "purge-w", wid, "sha256:world-purged", T0 + 2, force: true);
            Assert.Equal(PrivacyRejectionCode.WorldFixtureUncataloged, privacy.EvaluateAdmission(T0 + 3));
        }

        // ── (B) Negative fixture: a "supposedly purged" backup that is actually still
        //        present in the catalog must NOT survive the terminal FullPilotReset. ──
        [Fact]
        public void NegativeFixture_SupposedlyPurgedBackup_DoesNotSurvive_TerminalReset()
        {
            var store = NewStore();
            var privacy = Privacy(store);
            var pilotId = privacy.OpenPilot(Op, "open", "policy-v1", T0);

            // Adversary plants a backup and *claims* it was purged, but it is still cataloged
            // (Active). A terminal reset must leave nothing cataloged regardless of that claim.
            var backup = privacy.CatalogArtifact(Op, "cat-bak", PilotArtifactType.Backup, "bak/ghost", Policy, T0);
            Assert.True(store.TryGetArtifact(backup, out var b0) && b0.Status == ArtifactStatus.Active);

            var fence = new AccountMutationFence();
            var cert = Destruction(store, fence).FullPilotReset(Op, "full-reset", pilotId, "k2", T0 + 5);

            // The certificate must enumerate the backup id as bounded proof it was accounted for.
            Assert.Contains(backup.Value, cert.PurgedArtifactIds);

            // GATE: after the terminal purge, NO cataloged artifact of ANY status remains — live or on disk.
            Assert.Empty(store.Artifacts);
            var reboot = NewStore();
            Assert.Empty(reboot.Artifacts);
            Assert.False(reboot.TryGetArtifact(backup, out _));
            Assert.Equal("k2", reboot.ActiveKeyEpochVersion());
            Assert.Single(reboot.PurgeCertificates);
        }

        // ── (C) The PASS gate proper: terminal purge cannot be reached with ANY cataloged
        //        artifact remaining, even with a dense multi-class catalog. ──
        [Fact]
        public void PassGate_TerminalPurge_LeavesZeroCatalogedArtifacts_AcrossEveryClass()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, characterId) =
                PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000009001", 9001L);
            var privacy = Privacy(store);
            var pilotId = privacy.OpenPilot(Op, "open", "policy-v1", T0);

            // Dense catalog: one of every relevant durable artifact class.
            var world   = privacy.CatalogArtifact(Op, "c-world", PilotArtifactType.WorldSave, "world/1", Policy, T0);
            var backup  = privacy.CatalogArtifact(Op, "c-bak",   PilotArtifactType.Backup, "bak/1", Policy, T0);
            var export  = privacy.CatalogArtifact(Op, "c-exp",   PilotArtifactType.Export, "exp/1", Policy, T0);
            var log     = privacy.CatalogArtifact(Op, "c-log",   PilotArtifactType.SecurityLog, "log/1", Policy, T0);
            var gjournal= privacy.CatalogArtifact(Op, "c-gj",    PilotArtifactType.GameplayJournal, "gj/1", Policy, T0);

            // One artifact already Purged (a real prior purge) and one durable never-expiring proof
            // (ResetAudit created via a scoped reset) — the terminal reset must sweep BOTH away too.
            privacy.PurgeArtifact(Op, "p-log", log, "sha256:log", T0 + 1, force: true);
            var fence = new AccountMutationFence();
            var destruction = Destruction(store, fence);
            destruction.ResetScoped(Op, "scoped", new[] { accountId }, "incompatible-fixture", T0 + 2);
            Assert.Contains(store.Artifacts, a => a.ArtifactType == PilotArtifactType.ResetAudit);

            int before = store.Artifacts.Count;
            Assert.True(before >= 5, "expected a dense pre-reset catalog, saw " + before);

            var cert = destruction.FullPilotReset(Op, "full-reset", pilotId, "k9", T0 + 10);

            // GATE (the whole point of IAP-014): terminal purge is unreachable with any cataloged
            // artifact remaining. Prove absence in the LIVE store (no stale in-memory catalog) AND
            // across reboot (disk truth) for every planted id and in aggregate.
            Assert.Empty(store.Artifacts);
            var reboot = NewStore();
            Assert.Empty(reboot.Artifacts);
            foreach (var id in new[] { world, backup, export, log, gjournal })
                Assert.False(reboot.TryGetArtifact(id, out _), "artifact survived terminal purge: " + id.Value);
            Assert.DoesNotContain(reboot.Artifacts, a => a.ArtifactType == PilotArtifactType.ResetAudit);

            // Bounded proof survives: certificate enumerates the purged ids and rotates the key epoch,
            // and it leaks NO account/character selector.
            Assert.Contains(world.Value, cert.PurgedArtifactIds);
            Assert.Contains(backup.Value, cert.PurgedArtifactIds);
            Assert.Equal("k9", cert.FreshKeyVersion);
            Assert.NotEqual("k9", cert.RetiredKeyVersion);
            Assert.DoesNotContain(accountId.Value, string.Join("~", cert.PurgedArtifactIds));
            Assert.DoesNotContain(characterId, string.Join("~", cert.PurgedArtifactIds));

            // Old bindings can never resolve again: account is physically gone, fresh epoch active.
            Assert.False(reboot.TryGetAccount(accountId, out _));
            Assert.Empty(reboot.Accounts);
            Assert.Equal("k9", reboot.ActiveKeyEpochVersion());
        }

        // ── (D) No-time-travel: terminal states never revive to live. ──
        [Fact]
        public void NoTimeTravel_TerminalDeleted_CannotBeRevivedOrQuarantined()
        {
            var store = NewStore();
            var ring = PrivacyRegressionFixture.Ring();
            var (accountId, _) =
                PrivacyRegressionFixture.SeedAccountWithCharacter(store, ring, "76561198000009002", 9002L);
            var fence = new AccountMutationFence();
            new OperatorAccountService(store, PrivacyRegressionFixture.AdminGate(), fence,
                new PilotSessionRegistry(), TimeSpan.FromSeconds(5)).Delete(Op, accountId, "op-del", T0);
            var destruction = Destruction(store, fence);
            destruction.CompleteAccountDeletion(Op, "op-purge", accountId, "sha256:e", T0 + 31 * Day);

            // Deleted is terminal: quarantining it (a walk-back attempt) must throw.
            Assert.Throws<PrivacyOperationException>(() =>
                destruction.Quarantine(Op, "q-late", accountId, "late", T0 + 40 * Day));
            Assert.True(PilotDestructionService.IsForbiddenRevival(PilotAccountStatus.Deleted, PilotAccountStatus.Active));
        }
    }
}
