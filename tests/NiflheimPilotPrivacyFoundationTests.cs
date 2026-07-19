// ============================================================================
//  IAP-012 — Tracer 4: privacy foundation (export, retention, closure, catalog).
// ----------------------------------------------------------------------------
//  Executable evidence for the IAP-012 named acceptance IDs. These exercise the
//  engine-free CLEAN-side privacy core that ships under net48 in the mod:
//    Domain/Accounts/PilotRetentionPolicy.cs
//    Application/Accounts/PilotPrivacyService.cs
//  over the Tracer-1 Persistence/Accounts/PilotAccountStore.cs (extended with the
//  pilot lifecycle / artifact catalog / retention-hold projections).
//
//  FIX-FORWARD (t_f6c8c748): the privacy service is now operator-gated, idempotent,
//  fenced, receipted, ownership-filtering, and fail-closed on admission. Signatures
//  changed accordingly; this file exercises the corrected API.
//
//  No file under test references UnityEngine/Valheim/BepInEx, so the asserted
//  behaviour IS the shipped behaviour, not a parallel copy.
//
//  Named acceptance (spec §Requirement-to-acceptance):
//    AT-AIP-EXPORT-SAFE                 AT-AIP-RETENTION-CONFIG
//    AT-AIP-RETENTION-INCREASE-RENOTICE AT-AIP-HOLD-EXPIRY
//    AT-AIP-ARTIFACT-CATALOG            AT-AIP-PILOT-CLOSURE-DEADLINE
//    AT-AIP-DISCLOSURE-COMPLETE / AT-AIP-DATA-INVENTORY-BASIS are additionally
//    re-asserted here against the shipped disclosure core for the IAP-012 gate.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPilotPrivacyFoundationTests : IDisposable
    {
        private const string NoticeV = "notice-v1";
        private const string NoticeV2 = "notice-v2";
        private const long T0 = 1_784_000_000L;
        private const long Day = 86_400L;

        private readonly string _dir;

        // A server-observed admin context that the OperatorAdminGate authorizes (the host id is in the list).
        private const string AdminHost = "76561198000000001";
        private static readonly ServerObservedAdminContext Op = new ServerObservedAdminContext(AdminHost, "Steam");
        private static readonly ServerObservedAdminContext NonAdmin = new ServerObservedAdminContext("76561198000000777", "Steam");
        private static OperatorAdminGate AdminGate() => new OperatorAdminGate(new[] { AdminHost });
        private static AccountMutationFence Fence() => new AccountMutationFence();
        private static readonly PilotRetentionPolicy Policy = PilotRetentionPolicy.ShippedDefault("retention-v1");

        public NiflheimPilotPrivacyFoundationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-t012-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string JournalPath => Path.Combine(_dir, "account-journal.bin");

        private PilotAccountStore NewStore() => new PilotAccountStore(JournalPath);
        private PilotPrivacyService NewPrivacy(PilotAccountStore store) =>
            new PilotPrivacyService(store, AdminGate(), Fence(), TimeSpan.FromSeconds(5));

        // ---- Tracer-1 disclosure helpers reused for the IAP-012 export/basis gates ----

        private static PilotDisclosure CompleteDisclosure()
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

        // Seed one Tracer-1 account directly through its foundation service so export/lifecycle tests
        // have a real internal account to operate over.
        private PilotAccountId SeedAccount(PilotAccountStore store, string subject = "76561198000000042")
        {
            var ring = new LookupKeyRing(LookupHmacKey.Generate(new LookupKeyVersion("k1")));
            var svc = new PilotAccountService(store, ring, NoticeV, "retention-v1");
            svc.ProvisionAllowlistEntry("prov-" + subject, "Steam", "niflheim-pilot-app-896660", subject,
                CompleteDisclosure(), new DisclosureAcknowledgement(NoticeV, T0), T0);
            var res = svc.ResolveOrCreateAccount("bind-" + subject, new VerifiedProviderPrincipal(
                PilotProviderKey.Steamworks("niflheim-pilot-app-896660"), subject, transportHandle: 1L), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);
            return res.AccountId;
        }

        // Seed an account AND mint one character bound to it, returning both — export ownership tests need
        // a real owned CharacterId in acct.CharacterIds (the authoritative membership list).
        private (PilotAccountId account, string characterId) SeedAccountWithCharacter(
            PilotAccountStore store, string subject, long playerId, LookupKeyRing ring)
        {
            var svc = new PilotAccountService(store, ring, NoticeV, "retention-v1");
            svc.ProvisionAllowlistEntry("prov-" + subject, "Steam", "niflheim-pilot-app-896660", subject,
                CompleteDisclosure(), new DisclosureAcknowledgement(NoticeV, T0), T0);
            var res = svc.ResolveOrCreateAccount("bind-" + subject, new VerifiedProviderPrincipal(
                PilotProviderKey.Steamworks("niflheim-pilot-app-896660"), subject, transportHandle: playerId), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);

            var chars = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var begin = chars.BeginAdmission(res.AccountId, playerId, T0);
            Assert.True(begin.Admitted);
            var profile = new VerifiedProfileSubject(playerId, playerId);
            var cres = chars.ResolveOrCreateCharacter("char-op-" + subject, res.AccountId, begin.SessionId, profile, T0);
            Assert.Equal(CharacterAdmissionOutcome.Created, cres.Outcome);
            chars.CloseSession(res.AccountId, begin.SessionId, playerId);
            return (res.AccountId, cres.CharacterId.Value);
        }

        // ── AT-AIP-RETENTION-CONFIG ─────────────────────────────────────────────
        [Fact]
        public void AT_AIP_RETENTION_CONFIG_ShippedDefaults_14And30_PositiveBounded_ZeroInvalid()
        {
            var def = PilotRetentionPolicy.ShippedDefault("retention-v1");
            Assert.Equal(14, def.SecurityLogRetentionDays);
            Assert.Equal(30, def.ClosedDataRetentionDays);

            var shorter = new PilotRetentionPolicy("retention-short", 7, 15);
            Assert.Equal(7, shorter.SecurityLogRetentionDays);
            Assert.Equal(15, shorter.ClosedDataRetentionDays);

            Assert.Throws<ArgumentOutOfRangeException>(() => new PilotRetentionPolicy("z", 0, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PilotRetentionPolicy("z", 14, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PilotRetentionPolicy("z", -1, 30));

            Assert.Equal(T0 + 30 * Day, def.ClosedDataPurgeDueAt(T0));
            Assert.Equal(T0 + 14 * Day, def.SecurityLogPurgeDueAt(T0));
        }

        // ── AT-AIP-RETENTION-INCREASE-RENOTICE ──────────────────────────────────
        [Fact]
        public void AT_AIP_RETENTION_INCREASE_RENOTICE_LongerPeriod_RequiresNewNoticeAck_DecreaseAppliesImmediately()
        {
            var current = PilotRetentionPolicy.ShippedDefault("retention-v1");
            var increased = new PilotRetentionPolicy("retention-v2", 30, 60);

            Assert.True(current.IsIncreaseOver(increased));

            var staleAck = new DisclosureAcknowledgement(NoticeV, T0);
            Assert.Equal(RetentionPolicyChangeGate.Decision.RequiresRenotice,
                RetentionPolicyChangeGate.Evaluate(current, increased, staleAck, NoticeV2));

            var freshAck = new DisclosureAcknowledgement(NoticeV2, T0 + 100);
            Assert.Equal(RetentionPolicyChangeGate.Decision.Applies,
                RetentionPolicyChangeGate.Evaluate(current, increased, freshAck, NoticeV2));

            var decreased = new PilotRetentionPolicy("retention-v3", 7, 15);
            Assert.Equal(RetentionPolicyChangeGate.Decision.AppliesImmediately,
                RetentionPolicyChangeGate.Evaluate(current, decreased, staleAck, "retention-v3"));

            var oneSided = new PilotRetentionPolicy("retention-v4", 14, 45);
            Assert.True(current.IsIncreaseOver(oneSided));
            Assert.Equal(RetentionPolicyChangeGate.Decision.RequiresRenotice,
                RetentionPolicyChangeGate.Evaluate(current, oneSided, staleAck, "retention-v4"));
        }

        // ── AT-AIP-HOLD-EXPIRY ──────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_HOLD_EXPIRY_ScopedExpiringHold_SuppressesPurge_UntilExpiry_ThenResumes()
        {
            var store = NewStore();
            var privacy = NewPrivacy(store);

            var acctScope = "account:acct-abc";
            var holdId = privacy.SetRetentionHold(Op, "h1", acctScope, "incident-2026-07 investigation",
                createdAt: T0, expiresAt: T0 + 7 * Day);
            Assert.True(store.TryGetRetentionHold(holdId, out var hold));
            Assert.Equal(RetentionHoldStatus.Active, hold.Status);
            Assert.False(string.IsNullOrEmpty(hold.ReceiptId));   // audit receipt recorded

            Assert.True(privacy.IsScopeHeld(acctScope, now: T0 + 1 * Day));
            Assert.False(privacy.IsScopeHeld(acctScope, now: T0 + 8 * Day));
            Assert.False(privacy.IsScopeHeld("account:other", now: T0 + 1 * Day));

            privacy.ReleaseRetentionHold(Op, "h1-rel", holdId, T0 + 2 * Day);
            Assert.True(store.TryGetRetentionHold(holdId, out var released));
            Assert.Equal(RetentionHoldStatus.Released, released.Status);
            Assert.False(privacy.IsScopeHeld(acctScope, now: T0 + 3 * Day));
        }

        [Fact]
        public void AT_AIP_HOLD_EXPIRY_GlobalOrExpiryless_Hold_Rejected()
        {
            var privacy = NewPrivacy(NewStore());

            Assert.Throws<PrivacyOperationException>(() =>
                privacy.SetRetentionHold(Op, "bad1", "*", "reason", T0, T0 + Day));
            Assert.Throws<PrivacyOperationException>(() =>
                privacy.SetRetentionHold(Op, "bad2", "all", "reason", T0, T0 + Day));

            var ex = Assert.Throws<PrivacyOperationException>(() =>
                privacy.SetRetentionHold(Op, "bad3", "account:x", "reason", T0, T0));
            Assert.Equal(PrivacyRejectionCode.RetentionHoldInvalid, ex.Code);

            Assert.Throws<PrivacyOperationException>(() =>
                privacy.SetRetentionHold(Op, "bad4", "account:x", "", T0, T0 + Day));
        }

        // ── AT-AIP-ARTIFACT-CATALOG ─────────────────────────────────────────────
        [Fact]
        public void AT_AIP_ARTIFACT_CATALOG_UncatalogedWorldFixture_FailsAdmissionClosed_ThenPasses()
        {
            var store = NewStore();
            var privacy = NewPrivacy(store);

            const string worldLocator = "worlds/niflheim-pilot.db";

            var ex = Assert.Throws<PrivacyOperationException>(() => privacy.RequireCatalogedWorldFixture(worldLocator, T0));
            Assert.Equal(PrivacyRejectionCode.WorldFixtureUncataloged, ex.Code);

            var artId = privacy.CatalogArtifact(Op, "cat-world", PilotArtifactType.WorldSave, worldLocator, Policy, createdAt: T0);
            Assert.True(store.TryGetArtifact(artId, out var art));
            Assert.Equal(PilotArtifactType.WorldSave, art.ArtifactType);
            Assert.Equal(T0 + 30 * Day, art.ExpiresAt);   // expiry DERIVED from the closed-data period
            privacy.RequireCatalogedWorldFixture(worldLocator, T0 + Day);   // no throw

            // Purged fixture no longer satisfies the gate (fails closed again). Purge after due.
            privacy.PurgeArtifact(Op, "purge-world", artId, "sha256:evidence-fixture-reset", T0 + 31 * Day);
            Assert.Throws<PrivacyOperationException>(() => privacy.RequireCatalogedWorldFixture(worldLocator, T0 + 32 * Day));
        }

        [Fact]
        public void AT_AIP_ARTIFACT_CATALOG_EveryArtifactClass_EntersPurgeInventory_SufficientForPurgeProof()
        {
            var store = NewStore();
            var privacy = NewPrivacy(store);

            var types = new[]
            {
                PilotArtifactType.WorldSave, PilotArtifactType.AccountJournal, PilotArtifactType.GameplayJournal,
                PilotArtifactType.Export, PilotArtifactType.Backup, PilotArtifactType.SecurityLog,
                PilotArtifactType.QuarantineReport, PilotArtifactType.ResetAudit,
            };
            var ids = new List<DataArtifactId>();
            for (int i = 0; i < types.Length; i++)
                ids.Add(privacy.CatalogArtifact(Op, "cat-" + i, types[i], "loc/" + i, Policy, T0));

            Assert.Equal(types.Length, store.Artifacts.Count);

            // Purge requires an artifact-specific evidence digest; counts alone are refused.
            Assert.Throws<PrivacyOperationException>(() => privacy.PurgeArtifact(Op, "p-noevidence", ids[0], "", T0 + 40 * Day));

            // With evidence, each artifact reaches Purged with its evidence digest + selector + receipt retained.
            for (int i = 0; i < ids.Count; i++)
                privacy.PurgeArtifact(Op, "purge-" + i, ids[i], "sha256:evidence-" + i, T0 + 40 * Day);
            Assert.All(store.Artifacts, a =>
            {
                Assert.Equal(ArtifactStatus.Purged, a.Status);
                Assert.False(string.IsNullOrEmpty(a.PurgeEvidenceDigest));
                Assert.False(string.IsNullOrEmpty(a.Selector));
                Assert.False(string.IsNullOrEmpty(a.ReceiptId));
            });

            // Rebuild from journal alone — the catalog is durable/recoverable.
            var store2 = NewStore();
            Assert.Equal(types.Length, store2.Artifacts.Count);
            Assert.All(store2.Artifacts, a => Assert.Equal(ArtifactStatus.Purged, a.Status));
        }

        // ── AT-AIP-PILOT-CLOSURE-DEADLINE ───────────────────────────────────────
        [Fact]
        public void AT_AIP_PILOT_CLOSURE_DEADLINE_ClosureStampsEndedAndDerivedPurgeDue_RejectsEnrollmentAfter()
        {
            var store = NewStore();
            var privacy = NewPrivacy(store);
            var policy = PilotRetentionPolicy.ShippedDefault("retention-v1");

            var pilotId = privacy.OpenPilot(Op, "open", policy.PolicyVersion, T0);
            Assert.True(privacy.AdmitsEnrollment(pilotId));

            long endedAt = T0 + 5 * Day;
            privacy.ClosePilot(Op, "close", pilotId, policy, endedAt);

            Assert.True(store.TryGetPilot(pilotId, out var pilot));
            Assert.Equal(PilotLifecycleStatus.Closing, pilot.Status);
            Assert.Equal(endedAt, pilot.EndedAt);
            Assert.Equal(endedAt + 30 * Day, pilot.PurgeDueAt);
            Assert.Equal(policy.PolicyVersion, pilot.PolicyVersion);

            Assert.False(privacy.AdmitsEnrollment(pilotId));

            var store2 = NewStore();
            Assert.True(store2.TryGetPilot(pilotId, out var pilot2));
            Assert.Equal(PilotLifecycleStatus.Closing, pilot2.Status);
            Assert.Equal(endedAt + 30 * Day, pilot2.PurgeDueAt);
        }

        // ── AT-AIP-EXPORT-SAFE ──────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_EXPORT_SAFE_PlayerExport_ExcludesSecretsHmacsRawSubjectsUnrelatedAccounts()
        {
            const string subject = "76561198000000042";
            var store = NewStore();
            var privacy = NewPrivacy(store);
            var ring = new LookupKeyRing(LookupHmacKey.Generate(new LookupKeyVersion("k1")));

            var (accountId, characterId) = SeedAccountWithCharacter(store, subject, 4242L, ring);
            // A second, UNRELATED account must never appear in the first account's export.
            var (otherAccountId, _) = SeedAccountWithCharacter(store, "76561198000000099", 9999L, ring);

            var gameplay = new List<PlayerVisibleRecord> { new PlayerVisibleRecord(characterId, "placed foundation stone") };
            var receipts = new List<PlayerVisibleRecord> { new PlayerVisibleRecord(characterId, "op rcpt-xyz applied") };

            var export = privacy.ExportAccount(Op, "exp-1", accountId, gameplay, receipts,
                "closed data purged 30 days after pilot close", "exports/acct-abc.json", Policy, occurredAt: T0 + Day);

            Assert.Equal(accountId.Value, export.AccountId);
            Assert.Equal("Active", export.AccountStatus);
            Assert.Contains(characterId, export.CharacterIds);
            Assert.NotEmpty(export.PlayerVisibleGameplayState);
            Assert.Contains(export.CredentialClasses, c => c.StartsWith("Steam", StringComparison.Ordinal));

            store.TryGetAccount(accountId, out var acct);
            store.TryGetCredential(acct.CredentialBindingIds.First(), out var cred);
            var forbidden = new List<string>
            {
                subject, cred.Hmac.Hex, cred.Hmac.KeyVersion.Value, otherAccountId.Value,
                "steam_ticket_SECRET", "operator-note-private",
            };
            foreach (var rendered in export.AllRenderedValues())
                foreach (var bad in forbidden)
                    Assert.DoesNotContain(bad, rendered ?? string.Empty, StringComparison.Ordinal);

            Assert.Contains(store.Artifacts, a => a.ArtifactType == PilotArtifactType.Export);
        }

        // ── AT-AIP-DISCLOSURE-COMPLETE (re-asserted at the IAP-012 gate) ─────────
        [Fact]
        public void AT_AIP_DISCLOSURE_COMPLETE_ShippedDisclosure_IsComplete_MissingElementFails()
        {
            Assert.True(CompleteDisclosure().IsComplete());

            var cat = new PrivacyInventoryCategory("c", "p", "r", "operator", "none", "del", "basis", true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "ops@x.invalid", NoticeV);
            var noRoute = new PilotDisclosure(inv, "", statesExplicitResetPossibility: true);
            Assert.False(noRoute.IsComplete());
            Assert.Contains("export-deletion-route", noRoute.MissingElements());
        }

        // ── AT-AIP-DATA-INVENTORY-BASIS (re-asserted at the IAP-012 gate) ────────
        [Fact]
        public void AT_AIP_DATA_INVENTORY_BASIS_HumanApprovedBasisRequired_SoftwareNeverSelectsBasis()
        {
            var noBasis = new PrivacyInventoryCategory("c", "p", "r", "operator", "none", "del", "", humanApprovedBasis: false);
            var inv = new PilotPrivacyInventory(new[] { noBasis }, "ops@x.invalid", NoticeV);
            Assert.False(inv.IsValidEnrollmentBasis());
            Assert.Single(inv.CategoriesMissingBasis());

            var empty = new PilotPrivacyInventory(Array.Empty<PrivacyInventoryCategory>(), "ops@x.invalid", NoticeV);
            Assert.False(empty.IsValidEnrollmentBasis());
        }
    }
}
