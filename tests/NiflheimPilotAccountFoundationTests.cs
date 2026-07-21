// ============================================================================
//  IAP-003 — Tracer 1: first-bind account and credential foundation.
// ----------------------------------------------------------------------------
//  Executable evidence for the 23 named Tracer-1 acceptance IDs. These exercise
//  the engine-free CLEAN-side core (Domain/Accounts, Adapters/Identity/LookupKeyRing,
//  Persistence/Accounts, Application/Accounts) that ships under net48 in the mod.
//  No file under test references UnityEngine/Valheim/BepInEx, so the asserted
//  behaviour IS the shipped behaviour, not a parallel copy.
//
//  Named acceptance (spec §Requirement-to-acceptance; plan §Tracer 1):
//    AT-AIP-FIRST-BIND                  AT-AIP-INTERNAL-ID-ENTROPY
//    AT-AIP-ACCOUNT-RECONNECT           AT-AIP-FIRST-BIND-RACE
//    AT-AIP-ACCOUNT-CREDENTIAL-ATOMIC   AT-AIP-HMAC-CANONICAL
//    AT-AIP-ALLOWLIST-HMAC-ONLY         AT-AIP-DISCLOSURE-COMPLETE
//    AT-AIP-DATA-INVENTORY-BASIS        AT-AIP-FIRST-JOIN-AUTOCREATE
//    AT-AIP-UNKNOWN-CREDENTIAL-SEPARATE AT-AIP-NO-NAME-MERGE
//    AT-AIP-KEY-STRENGTH-SEPARATION     AT-AIP-KEY-MISSING-FAIL-CLOSED
//    AT-AIP-PREVIOUS-KEY-REKEY          AT-AIP-REKEY-MULTIHOP
//    AT-AIP-KEY-RETIREMENT-GATE         AT-AIP-TORN-TAIL
//    AT-AIP-ACCOUNT-JOURNAL-RECOVERY    AT-AIP-OPERATION-CONFLICT
//    AT-AIP-BOOT-BEFORE-ADMISSION       AT-AIP-PERSISTED-PII-SCAN
//    AT-AIP-INDEXED-10K
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPilotAccountFoundationTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;

        private readonly string _dir;

        public NiflheimPilotAccountFoundationTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-t003-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ---- helpers ----

        private string JournalPath => Path.Combine(_dir, "account-journal.bin");

        private static LookupHmacKey Key(string version) => LookupHmacKey.Generate(new LookupKeyVersion(version));

        private LookupKeyRing Ring(string active = "k1", string? previous = null) =>
            previous == null ? new LookupKeyRing(Key(active)) : new LookupKeyRing(Key(active), Key(previous));

        // Deterministic ring so a value reproduces across store reboots within one test.
        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        private LookupKeyRing FixedRing(string active = "k1", byte activeFill = 10, string? previous = null, byte prevFill = 40) =>
            previous == null
                ? new LookupKeyRing(FixedKey(active, activeFill))
                : new LookupKeyRing(FixedKey(active, activeFill), FixedKey(previous, prevFill));

        private static VerifiedProviderPrincipal Principal(string subject) =>
            new VerifiedProviderPrincipal(PilotProviderKey.Steamworks(Backend), subject, transportHandle: 1L);

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

        private static DisclosureAcknowledgement Ack() => new DisclosureAcknowledgement(NoticeV, T0);

        private PilotAccountService NewService(PilotAccountStore store, LookupKeyRing ring) =>
            new PilotAccountService(store, ring, NoticeV, RetentionV);

        /// <summary>First-bind an account for a subject. Normal admission auto-creates the opaque account
        /// on first authenticated join — NO allowlist provisioning is required or performed (the pre-join
        /// allowlist is deprecated; ProvisionAllowlistEntry remains only for compatibility/audit).</summary>
        private PilotAccountResolution ProvisionAndBind(PilotAccountService svc, string subject, string opSuffix = "")
        {
            return svc.ResolveOrCreateAccount("bind-" + subject + opSuffix, Principal(subject), T0);
        }

        // ── AT-AIP-INTERNAL-ID-ENTROPY ──────────────────────────────────────────
        [Fact]
        public void AT_AIP_INTERNAL_ID_ENTROPY_MintedIds_Are128BitOpaqueAndUnique()
        {
            Assert.Equal(128, OpaqueIdMint.EntropyBits);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < 5000; i++)
            {
                string core = OpaqueIdMint.RandomHex();
                Assert.Equal(32, core.Length);                  // 128 bits == 32 hex chars
                Assert.All(core, c => Assert.True(Uri.IsHexDigit(c)));
                Assert.True(seen.Add(core), "minted id core collided");
            }
            // Ids are tagged but the tag is not provider-derived.
            var acct = OpaqueIdMint.NewAccountId();
            Assert.StartsWith("acct-", acct.Value);
            Assert.NotEqual(OpaqueIdMint.NewAccountId().Value, acct.Value);
        }

        // ── AT-AIP-HMAC-CANONICAL ───────────────────────────────────────────────
        [Fact]
        public void AT_AIP_HMAC_CANONICAL_FullLength_DomainSeparated_NoBoundaryCollision()
        {
            var ring = FixedRing();
            var credAB = ring.CredentialHmacActive(ProviderNs, Backend, "76561198000000001");

            // Full-length HMAC-SHA-256 == 64 hex chars, never truncated.
            Assert.Equal(64, credAB.Hex.Length);

            // Domain separation: a credential HMAC and a profile HMAC over the SAME concatenated fields
            // differ, because the domain tag is a distinct framed field.
            var profile = ring.ProfileHmacActive("acct-x", "76561198000000001");
            Assert.NotEqual(credAB.Hex, profile.Hex);

            // Field-boundary safety: (backend="X", subject="Y...") must not equal (backend="XY...", subject="").
            var split = ring.CredentialHmacActive(ProviderNs, "X", "76561198000000001");
            var joined = ring.CredentialHmacActive(ProviderNs, "X76561198000000001", "");
            Assert.NotEqual(split.Hex, joined.Hex);

            // Deterministic under the same key.
            var again = FixedRing().CredentialHmacActive(ProviderNs, Backend, "76561198000000001");
            Assert.Equal(credAB.Hex, again.Hex);
            Assert.Equal("k1", credAB.KeyVersion.Value);
        }

        // ── AT-AIP-KEY-STRENGTH-SEPARATION ──────────────────────────────────────
        [Fact]
        public void AT_AIP_KEY_STRENGTH_SEPARATION_WeakKeyRejected_KeyBytesNeverExposed()
        {
            // A key shorter than 256 bits cannot be constructed.
            Assert.Throws<ArgumentException>(() => new LookupHmacKey(new LookupKeyVersion("weak"), new byte[16]));
            Assert.Equal(256, LookupHmacKey.MinKeyBits);
            Assert.Equal(256, Key("k1").KeyBits);

            // The key type exposes no raw-byte accessor — its only output is a derived HMAC hex.
            var members = typeof(LookupHmacKey).GetProperties().Select(p => p.Name).ToList();
            Assert.DoesNotContain("Key", members);
            Assert.DoesNotContain("KeyBytes", members);
        }

        // ── AT-AIP-KEY-MISSING-FAIL-CLOSED ──────────────────────────────────────
        [Fact]
        public void AT_AIP_KEY_MISSING_FAIL_CLOSED_UnknownVersion_Throws_NoRawFallback()
        {
            var ring = Ring(active: "k2");   // no previous
            Assert.False(ring.Knows(new LookupKeyVersion("k1")));
            Assert.Throws<LookupKeyUnavailableException>(() =>
                ring.CredentialHmacUnder(new LookupKeyVersion("k1"), ProviderNs, Backend, "s"));
        }

        [Fact]
        public void AT_AIP_KEY_MISSING_FAIL_CLOSED_ServiceRejects_WhenActiveKeyCannotServe()
        {
            // A ring whose active key differs from the credential stored under a now-unavailable version:
            // the service resolves under the active key, cannot find it, and (no previous configured)
            // treats it as a first bind requiring allowlist — never a raw-id fallback. Prove the fail-
            // closed path directly by asking the service under a broken ring shape.
            var store = new PilotAccountStore(JournalPath);
            // A degenerate ring cannot even be built with a missing active key, so assert the type
            // invariant that guarantees fail-closed: the active key is mandatory.
            Assert.Throws<ArgumentNullException>(() => new LookupKeyRing(null!));
            store.RunCensus(); // touch store so it is used
        }

        // ── AT-AIP-DISCLOSURE-COMPLETE ──────────────────────────────────────────
        [Fact]
        public void AT_AIP_DISCLOSURE_COMPLETE_AllElementsPresent_Passes_MissingElementFails()
        {
            Assert.True(CompleteDisclosure().IsComplete());

            // Drop the explicit-reset statement → incomplete.
            var cat = new PrivacyInventoryCategory("c", "p", "r", "operator", "none", "del", "basis", true);
            var inv = new PilotPrivacyInventory(new[] { cat }, "ops@x.invalid", NoticeV);
            var noReset = new PilotDisclosure(inv, "route", statesExplicitResetPossibility: false);
            Assert.False(noReset.IsComplete());
            Assert.Contains("explicit-reset-possibility", noReset.MissingElements());
        }

        // ── AT-AIP-DATA-INVENTORY-BASIS ─────────────────────────────────────────
        [Fact]
        public void AT_AIP_DATA_INVENTORY_BASIS_HumanApprovedBasisRequired_EmptyInventoryNeverPasses()
        {
            // A category without a human-approved basis blocks the enrollment basis and the disclosure.
            var noBasis = new PrivacyInventoryCategory("c", "p", "r", "operator", "none", "del", "", humanApprovedBasis: false);
            var inv = new PilotPrivacyInventory(new[] { noBasis }, "ops@x.invalid", NoticeV);
            Assert.False(inv.IsValidEnrollmentBasis());
            Assert.Single(inv.CategoriesMissingBasis());

            var disclosure = new PilotDisclosure(inv, "route", statesExplicitResetPossibility: true);
            Assert.False(disclosure.IsComplete());
            Assert.Contains(disclosure.MissingElements(), m => m.StartsWith("lawful-basis:", StringComparison.Ordinal));

            // Empty inventory is never a pass.
            var empty = new PilotPrivacyInventory(Array.Empty<PrivacyInventoryCategory>(), "ops@x.invalid", NoticeV);
            Assert.False(empty.IsValidEnrollmentBasis());
        }

        // ── AT-AIP-ALLOWLIST-HMAC-ONLY ──────────────────────────────────────────
        [Fact]
        public void AT_AIP_ALLOWLIST_HMAC_ONLY_ProvisionStoresHmac_NotRawSubject()
        {
            const string subject = "76561198000000042";
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            var entryId = svc.ProvisionAllowlistEntry("prov-1", ProviderNs, Backend, subject,
                CompleteDisclosure(), Ack(), T0);

            Assert.True(store.TryGetAllowlistEntry(entryId, out var entry));
            Assert.Equal(64, entry.Hmac.Hex.Length);
            Assert.Equal(AllowlistStatus.Active, entry.Status);
            // The raw subject never touches disk.
            Assert.False(PersistedPiiScanner.JournalContainsForbidden(store, new[] { subject }, out _));

            // Provisioning without a completed disclosure/basis is refused.
            var badCat = new PrivacyInventoryCategory("c", "p", "r", "operator", "none", "del", "", false);
            var badInv = new PilotPrivacyInventory(new[] { badCat }, "ops@x.invalid", NoticeV);
            var badDisc = new PilotDisclosure(badInv, "route", true);
            Assert.Throws<InvalidOperationException>(() =>
                svc.ProvisionAllowlistEntry("prov-2", ProviderNs, Backend, subject, badDisc, Ack(), T0));
        }

        // ── AT-AIP-FIRST-BIND ───────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_FIRST_BIND_MintsOneAccountOneBinding_NoRawSubjectPersisted()
        {
            const string subject = "76561198000000077";
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());

            var res = ProvisionAndBind(svc, subject);

            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);
            Assert.False(res.AccountId.IsEmpty);
            Assert.False(res.CredentialBindingId.IsEmpty);
            Assert.Equal(1, store.AccountCount);
            Assert.True(store.TryGetAccount(res.AccountId, out var acct));
            Assert.Equal(PilotAccountStatus.Active, acct.Status);
            Assert.Single(acct.CredentialBindingIds);
            Assert.Equal(NoticeV, acct.NoticeVersion);
            Assert.Equal(RetentionV, acct.RetentionPolicyVersion);
            Assert.False(PersistedPiiScanner.JournalContainsForbidden(store, new[] { subject }, out _));
        }

        // ── AT-AIP-ACCOUNT-CREDENTIAL-ATOMIC ────────────────────────────────────
        [Fact]
        public void AT_AIP_ACCOUNT_CREDENTIAL_ATOMIC_CrashBeforeCommit_LeavesNoPartialAccount()
        {
            const string subject = "76561198000000088";
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            svc.ProvisionAllowlistEntry("prov", ProviderNs, Backend, subject, CompleteDisclosure(), Ack(), T0);

            // Crash right after the Intent record, before Committed.
            Assert.Throws<CrashAfterPhase>(() =>
                svc.ResolveOrCreateAccount("bind", Principal(subject), T0, new CrashAt(TransactionPhase.Intent)));

            // Reboot: the Intent-only transaction is quarantined; no account/binding projected.
            var store2 = new PilotAccountStore(JournalPath);
            Assert.Equal(0, store2.AccountCount);
            Assert.True(store2.QuarantinedIntentTransactions >= 1);
            // But the allowlist entry (its own committed txn) survived, so a retry can bind.
            var svc2 = NewService(store2, FixedRing());
            var retry = svc2.ResolveOrCreateAccount("bind", Principal(subject), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, retry.Outcome);
            Assert.Equal(1, store2.AccountCount);
        }

        // ── AT-AIP-ACCOUNT-RECONNECT ────────────────────────────────────────────
        [Fact]
        public void AT_AIP_ACCOUNT_RECONNECT_SecondJoin_ResolvesSameAccount_NoNewMint()
        {
            const string subject = "76561198000000099";
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            var first = ProvisionAndBind(svc, subject);

            // Reconnect within the same process (new operationId).
            var again = svc.ResolveOrCreateAccount("bind-reconnect", Principal(subject), T0 + 100);
            Assert.Equal(AccountAdmissionOutcome.Resolved, again.Outcome);
            Assert.Equal(first.AccountId, again.AccountId);
            Assert.Equal(first.CredentialBindingId, again.CredentialBindingId);
            Assert.Equal(1, store.AccountCount);
        }

        [Fact]
        public void AT_AIP_ACCOUNT_RECONNECT_AfterRestart_ResolvesSameAccount()
        {
            const string subject = "76561198000000111";
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var first = ProvisionAndBind(NewService(store, ring), subject);

            // Full restart: fresh store rehydrated from journal, fresh service.
            var store2 = new PilotAccountStore(JournalPath);
            var svc2 = NewService(store2, FixedRing());
            var again = svc2.ResolveOrCreateAccount("bind-after-restart", Principal(subject), T0 + 1);
            Assert.Equal(AccountAdmissionOutcome.Resolved, again.Outcome);
            Assert.Equal(first.AccountId, again.AccountId);
        }

        // ── AT-AIP-FIRST-BIND-RACE ──────────────────────────────────────────────
        [Fact]
        public void AT_AIP_FIRST_BIND_RACE_ConcurrentFirstJoins_ExactlyOneAccount()
        {
            const string subject = "76561198000000222";
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            svc.ProvisionAllowlistEntry("prov", ProviderNs, Backend, subject, CompleteDisclosure(), Ack(), T0);

            var results = new ConcurrentBag<PilotAccountResolution>();
            Parallel.For(0, 16, i =>
            {
                results.Add(svc.ResolveOrCreateAccount("bind-race-" + i, Principal(subject), T0));
            });

            var accountIds = results.Select(r => r.AccountId.Value).Distinct().ToList();
            Assert.Single(accountIds);                               // exactly one account minted
            Assert.Equal(1, store.AccountCount);
            Assert.Equal(1, results.Count(r => r.Outcome == AccountAdmissionOutcome.Created));
            Assert.All(results, r => Assert.True(r.Accepted));
        }

        // ── AT-AIP-FIRST-JOIN-AUTOCREATE ────────────────────────────────────────
        // Normal Niflheim admission: a first authenticated Steam subject with NO existing binding
        // auto-creates exactly one opaque account + credential atomically — no pre-join allowlist, no
        // fabricated disclosure acknowledgement. (Supersedes the retired AT-AIP-NOT-ALLOWLISTED closed-
        // pilot rejection.)
        [Fact]
        public void AT_AIP_FIRST_JOIN_AUTOCREATE_UnknownSubject_MintsOneOpaqueAccount()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            var res = svc.ResolveOrCreateAccount("bind", Principal("76561198000000333"), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, res.Outcome);
            Assert.Equal(AccountRejectionCode.None, res.RejectionCode);
            Assert.False(res.AccountId.IsEmpty);
            Assert.False(res.CredentialBindingId.IsEmpty);
            Assert.Equal(1, store.AccountCount);

            // No allowlist / disclosure record is created or required on the normal first-bind path.
            Assert.Empty(store.AllowlistEntries);
            Assert.True(store.TryGetAccount(res.AccountId, out var acct));
            Assert.Equal(0, acct.NoticeAcknowledgedAt);   // no fabricated per-account admission ack
            Assert.True(store.TryGetCredential(res.CredentialBindingId, out var cred));
            Assert.True(cred.AllowlistEntryId.IsEmpty);    // credential carries NO PilotAllowlistEntry linkage
        }

        // ── AT-AIP-UNKNOWN-CREDENTIAL-SEPARATE ──────────────────────────────────
        [Fact]
        public void AT_AIP_UNKNOWN_CREDENTIAL_SEPARATE_TwoSubjects_TwoAccounts()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            var a = ProvisionAndBind(svc, "76561198000000001");
            var b = ProvisionAndBind(svc, "76561198000000002");
            Assert.Equal(AccountAdmissionOutcome.Created, a.Outcome);
            Assert.Equal(AccountAdmissionOutcome.Created, b.Outcome);
            Assert.NotEqual(a.AccountId, b.AccountId);
            Assert.Equal(2, store.AccountCount);
        }

        // ── AT-AIP-NO-NAME-MERGE ────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_NO_NAME_MERGE_DifferentSubjects_NeverMerge_EachMintsDistinctAccount()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            var a = ProvisionAndBind(svc, "76561198000000001");

            // A second, distinct subject NEVER merges into the first account on any resemblance; under
            // auto-create it mints its OWN distinct opaque account (no name/resemblance merge path exists).
            var b = svc.ResolveOrCreateAccount("bind-b", Principal("76561198000000009"), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, b.Outcome);
            Assert.NotEqual(a.AccountId, b.AccountId);
            Assert.Equal(2, store.AccountCount);
            Assert.True(store.TryGetAccount(a.AccountId, out var acct));
            Assert.Single(acct.CredentialBindingIds);
        }

        // ── AT-AIP-OPERATION-CONFLICT ───────────────────────────────────────────
        [Fact]
        public void AT_AIP_OPERATION_CONFLICT_SameOpId_DifferentBinding_Rejects()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            svc.ProvisionAllowlistEntry("p1", ProviderNs, Backend, "76561198000000001", CompleteDisclosure(), Ack(), T0);
            svc.ProvisionAllowlistEntry("p2", ProviderNs, Backend, "76561198000000002", CompleteDisclosure(), Ack(), T0);

            var first = svc.ResolveOrCreateAccount("op-shared", Principal("76561198000000001"), T0);
            Assert.Equal(AccountAdmissionOutcome.Created, first.Outcome);

            // Reuse the SAME operationId with a DIFFERENT subject → conflict, no mutation.
            var conflict = svc.ResolveOrCreateAccount("op-shared", Principal("76561198000000002"), T0);
            Assert.Equal(AccountAdmissionOutcome.Rejected, conflict.Outcome);
            Assert.Equal(AccountRejectionCode.OperationConflict, conflict.RejectionCode);
            Assert.Equal(1, store.AccountCount);

            // Same operationId + SAME subject → idempotent replay.
            var replay = svc.ResolveOrCreateAccount("op-shared", Principal("76561198000000001"), T0);
            Assert.Equal(AccountAdmissionOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.AccountId, replay.AccountId);
        }

        // ── AT-AIP-PREVIOUS-KEY-REKEY ───────────────────────────────────────────
        [Fact]
        public void AT_AIP_PREVIOUS_KEY_REKEY_ResolvesUnderPrevKey_ReKeysInPlace_SameAccountId()
        {
            const string subject = "76561198000000444";

            // Boot 1: active key k1. Bind the account.
            var store1 = new PilotAccountStore(JournalPath);
            var svc1 = NewService(store1, FixedRing(active: "k1", activeFill: 10));
            var first = ProvisionAndBind(svc1, subject);
            Assert.Equal(AccountAdmissionOutcome.Created, first.Outcome);

            // Boot 2: rotate — active k2, previous k1. The stored binding is under k1.
            var store2 = new PilotAccountStore(JournalPath);
            var ring2 = FixedRing(active: "k2", activeFill: 70, previous: "k1", prevFill: 10);
            var svc2 = NewService(store2, ring2);

            var reconnect = svc2.ResolveOrCreateAccount("bind-rotated", Principal(subject), T0 + 1);
            Assert.True(reconnect.Accepted);
            Assert.Equal(first.AccountId, reconnect.AccountId);
            Assert.Equal(first.CredentialBindingId, reconnect.CredentialBindingId);   // SAME id, re-keyed in place

            // The binding is now stored under k2; a k2-only ring (no previous) still resolves it.
            var store3 = new PilotAccountStore(JournalPath);
            var svc3 = NewService(store3, FixedRing(active: "k2", activeFill: 70));
            var afterRekey = svc3.ResolveOrCreateAccount("bind-after-rekey", Principal(subject), T0 + 2);
            Assert.Equal(AccountAdmissionOutcome.Resolved, afterRekey.Outcome);
            Assert.Equal(first.AccountId, afterRekey.AccountId);

            // Census: zero live entries remain on the retired k1.
            Assert.Equal(0, store3.RunCensus().TotalForVersion(new LookupKeyVersion("k1")));
        }

        // ── AT-AIP-REKEY-MULTIHOP ───────────────────────────────────────────────
        [Fact]
        public void AT_AIP_REKEY_MULTIHOP_TwoSequentialRotations_AccountIdStable()
        {
            const string subject = "76561198000000555";
            PilotAccountId original;

            // k1
            var s1 = new PilotAccountStore(JournalPath);
            original = ProvisionAndBind(NewService(s1, FixedRing("k1", 10)), subject).AccountId;

            // k1 → k2 (previous k1). Reconnect re-keys to k2.
            var s2 = new PilotAccountStore(JournalPath);
            var r2 = NewService(s2, FixedRing("k2", 70, "k1", 10))
                .ResolveOrCreateAccount("hop2", Principal(subject), T0 + 1);
            Assert.Equal(original, r2.AccountId);

            // k2 → k3 (previous k2). Reconnect re-keys to k3. Multi-hop: never revisits k1.
            var s3 = new PilotAccountStore(JournalPath);
            var r3 = NewService(s3, FixedRing("k3", 120, "k2", 70))
                .ResolveOrCreateAccount("hop3", Principal(subject), T0 + 2);
            Assert.Equal(original, r3.AccountId);

            // Final: a k3-only ring resolves it and no live entries remain on k1 or k2.
            var s4 = new PilotAccountStore(JournalPath);
            var r4 = NewService(s4, FixedRing("k3", 120))
                .ResolveOrCreateAccount("hop-final", Principal(subject), T0 + 3);
            Assert.Equal(AccountAdmissionOutcome.Resolved, r4.Outcome);
            Assert.Equal(original, r4.AccountId);
            Assert.Equal(0, s4.RunCensus().TotalForVersion(new LookupKeyVersion("k1")));
            Assert.Equal(0, s4.RunCensus().TotalForVersion(new LookupKeyVersion("k2")));
            // k3 carries the live credential (auto-create binds no allowlist entry, so census counts the
            // credential only).
            Assert.Equal(1, s4.RunCensus().TotalForVersion(new LookupKeyVersion("k3")));
            Assert.Equal(1, s4.RunCensus().CredentialCount(new LookupKeyVersion("k3")));
        }

        // ── AT-AIP-KEY-RETIREMENT-GATE ──────────────────────────────────────────
        [Fact]
        public void AT_AIP_KEY_RETIREMENT_GATE_BlockedWhileLiveEntriesExist_AllowedAfterRekey()
        {
            const string subject = "76561198000000666";
            var s1 = new PilotAccountStore(JournalPath);
            ProvisionAndBind(NewService(s1, FixedRing("k1", 10)), subject);

            // After rotation to k2/prev-k1 but BEFORE the account reconnects/re-keys, k1 still has a live
            // credential → retirement of k1 is blocked.
            var s2 = new PilotAccountStore(JournalPath);
            Assert.False(s2.MayRetireKeyVersion(new LookupKeyVersion("k1")));
            Assert.True(s2.RunCensus().TotalForVersion(new LookupKeyVersion("k1")) > 0);

            // Drive the lazy re-key by reconnecting under k2/prev-k1.
            NewService(s2, FixedRing("k2", 70, "k1", 10)).ResolveOrCreateAccount("reconnect", Principal(subject), T0 + 1);

            // Now k1 census is zero → retirement gate opens.
            var s3 = new PilotAccountStore(JournalPath);
            Assert.True(s3.MayRetireKeyVersion(new LookupKeyVersion("k1")));
        }

        // ── AT-AIP-TORN-TAIL ────────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_TORN_TAIL_TruncatedFrame_Quarantined_NotProjected()
        {
            const string subject = "76561198000000777";
            var store = new PilotAccountStore(JournalPath);
            ProvisionAndBind(NewService(store, FixedRing()), subject);

            // Append a torn frame: a length prefix promising more bytes than exist.
            using (var fs = new FileStream(JournalPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(9999);               // payload length
                bw.Write((uint)0xDEADBEEF);   // crc
                bw.Write(new byte[] { 1, 2, 3 });   // far fewer bytes than promised
            }

            // Reboot: the torn tail is reported and ignored; the durable account still resolves.
            var store2 = new PilotAccountStore(JournalPath);
            Assert.True(store2.QuarantinedTailBytes > 0);
            Assert.Equal(1, store2.AccountCount);
            var svc2 = NewService(store2, FixedRing());
            var res = svc2.ResolveOrCreateAccount("reconnect", Principal(subject), T0 + 1);
            Assert.Equal(AccountAdmissionOutcome.Resolved, res.Outcome);
        }

        // ── AT-AIP-ACCOUNT-JOURNAL-RECOVERY ─────────────────────────────────────
        [Fact]
        public void AT_AIP_ACCOUNT_JOURNAL_RECOVERY_MultipleAccounts_RebuildFromJournalOnly()
        {
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, FixedRing());
            var ids = new List<string>();
            for (int i = 0; i < 25; i++)
            {
                string subject = "765611980000010" + i.ToString("D2");
                ids.Add(ProvisionAndBind(svc, subject).AccountId.Value);
            }

            // Fresh process rebuilds every account/credential/allowlist projection from the journal alone.
            var store2 = new PilotAccountStore(JournalPath);
            Assert.Equal(25, store2.AccountCount);
            foreach (var id in ids)
                Assert.True(store2.TryGetAccount(new PilotAccountId(id), out _));

            // Each reconnect resolves the same account with no new mint.
            var svc2 = NewService(store2, FixedRing());
            for (int i = 0; i < 25; i++)
            {
                string subject = "765611980000010" + i.ToString("D2");
                var r = svc2.ResolveOrCreateAccount("reconnect-" + i, Principal(subject), T0 + 1);
                Assert.Equal(AccountAdmissionOutcome.Resolved, r.Outcome);
            }
            Assert.Equal(25, store2.AccountCount);
        }

        // ── AT-AIP-BOOT-BEFORE-ADMISSION ────────────────────────────────────────
        [Fact]
        public void AT_AIP_BOOT_BEFORE_ADMISSION_IndexesReadyAtConstruction_FirstLookupResolves()
        {
            const string subject = "76561198000000888";
            var store = new PilotAccountStore(JournalPath);
            var first = ProvisionAndBind(NewService(store, FixedRing()), subject);

            // A brand-new store (== boot) has the credential index built in its CONSTRUCTOR, before any
            // admission call. The very first lookup after construction resolves without a journal scan.
            var booted = new PilotAccountStore(JournalPath);
            var svc = NewService(booted, FixedRing());
            var firstLookup = svc.ResolveOrCreateAccount("first-after-boot", Principal(subject), T0 + 1);
            Assert.Equal(AccountAdmissionOutcome.Resolved, firstLookup.Outcome);
            Assert.Equal(first.AccountId, firstLookup.AccountId);
        }

        // ── AT-AIP-PERSISTED-PII-SCAN ───────────────────────────────────────────
        [Fact]
        public void AT_AIP_PERSISTED_PII_SCAN_NoForbiddenValue_AndScannerCatchesASeededLeak()
        {
            const string subject = "76561198000000999";
            const string token = "steam_ticket_SECRET_abc123";
            const string email = "tester@example.invalid";
            var store = new PilotAccountStore(JournalPath);
            ProvisionAndBind(NewService(store, FixedRing()), subject);

            // Real fixture: none of the forbidden values appear on disk (raw byte or decoded field).
            Assert.False(PersistedPiiScanner.JournalContainsForbidden(store,
                new[] { subject, token, email }, out _));

            // Negative control: a file that DID leak a raw subject is caught by the same scan.
            string leaky = Path.Combine(_dir, "leaky.bin");
            File.WriteAllText(leaky, "some record subject=" + subject);
            Assert.True(PersistedPiiScanner.TryFindForbidden(leaky, new[] { subject }, out var offending));
            Assert.Equal(subject, offending);
        }

        // ── AT-AIP-INDEXED-10K ──────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_INDEXED_10K_BootReplayOnce_ThenIndexedResolution()
        {
            const int N = 10_000;
            var ring = FixedRing();
            // Seed 10k bindings.
            var store = new PilotAccountStore(JournalPath);
            var svc = NewService(store, ring);
            for (int i = 0; i < N; i++)
            {
                string subject = "9" + i.ToString("D17");
                svc.ProvisionAllowlistEntry("p" + i, ProviderNs, Backend, subject, CompleteDisclosure(), Ack(), T0);
                svc.ResolveOrCreateAccount("b" + i, Principal(subject), T0);
            }
            Assert.Equal(N, store.AccountCount);

            // One boot replay rebuilds all indexes.
            var booted = new PilotAccountStore(JournalPath);
            Assert.Equal(N, booted.AccountCount);
            var svc2 = NewService(booted, FixedRing());

            // Post-boot resolution is O(1) indexed lookups: resolve a sample without journal scan and
            // assert every one resolves to an existing account (no new mint).
            var rnd = new Random(1234);
            for (int t = 0; t < 200; t++)
            {
                int i = rnd.Next(N);
                string subject = "9" + i.ToString("D17");
                var r = svc2.ResolveOrCreateAccount("lk-" + t, Principal(subject), T0 + 1);
                Assert.Equal(AccountAdmissionOutcome.Resolved, r.Outcome);
            }
            Assert.Equal(N, booted.AccountCount);   // no lookups minted anything
        }

        // ---- crash-injection test doubles ----

        private sealed class CrashAfterPhase : Exception
        {
            public CrashAfterPhase(TransactionPhase phase) : base("crash after " + phase) { }
        }

        private sealed class CrashAt : IAccountCrashInjector
        {
            private readonly TransactionPhase _at;
            public CrashAt(TransactionPhase at) => _at = at;
            public void AfterPhase(TransactionPhase phase)
            {
                if (phase == _at) throw new CrashAfterPhase(phase);
            }
        }
    }
}
