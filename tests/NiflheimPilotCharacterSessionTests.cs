// ============================================================================
//  IAP-005 — Tracer 2: minted characters and single-session admission.
// ----------------------------------------------------------------------------
//  Executable evidence for the 11 named Tracer-2 acceptance IDs. These exercise
//  the engine-free CLEAN-side core (Adapters/Identity/PilotProfileSubject,
//  Domain/Accounts/{PilotCharacterId,SessionId}, the character projections +
//  account-scoped profile index on PilotAccountStore, the ephemeral
//  AccountAdmissionIndex, and PilotCharacterAdmissionService) that ships under
//  net48 in the mod. No file under test references UnityEngine/Valheim/BepInEx,
//  so the asserted behaviour IS the shipped behaviour, not a parallel copy.
//
//  Named acceptance (spec §Requirement-to-acceptance; plan §Tracer 2):
//    AT-AIP-PROFILE-MINT                AT-AIP-PROFILE-RENAME
//    AT-AIP-NAME-NONAUTHORITY           AT-AIP-PROFILE-RECONNECT
//    AT-AIP-PROFILE-PREVIOUS-KEY-REKEY  AT-AIP-CROSS-ACCOUNT-BLOCK
//    AT-AIP-ADMISSION-LEASE-RACE        AT-AIP-ONE-SESSION
//    AT-AIP-STALE-DISCONNECT            AT-AIP-CREATOR-BRIDGE
//    AT-AIP-CHARACTER-MEMBERSHIP-ATOMIC
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
    public sealed class NiflheimPilotCharacterSessionTests : IDisposable
    {
        private const string ProviderNs = "Steam";
        private const string Backend = "niflheim-pilot-app-896660";
        private const string NoticeV = "notice-v1";
        private const string RetentionV = "retention-v1";
        private const long T0 = 1_784_000_000L;

        private readonly string _dir;

        public NiflheimPilotCharacterSessionTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "aip-t005-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ---- helpers ----

        private string JournalPath => Path.Combine(_dir, "account-journal.bin");

        // Deterministic ring so a profile HMAC reproduces across store reboots within one test.
        private static LookupHmacKey FixedKey(string version, byte fill)
        {
            byte[] bytes = new byte[32];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(fill + i);
            return new LookupHmacKey(new LookupKeyVersion(version), bytes);
        }

        private static LookupKeyRing FixedRing(string active = "k1", byte activeFill = 10, string? previous = null, byte prevFill = 40) =>
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

        private PilotAccountService NewAccountService(PilotAccountStore store, LookupKeyRing ring) =>
            new PilotAccountService(store, ring, NoticeV, RetentionV);

        /// <summary>Provision an allowlist entry + first-bind an account, returning the resolved account id.</summary>
        private PilotAccountId ProvisionAndBind(PilotAccountService svc, string subject)
        {
            svc.ProvisionAllowlistEntry("prov-" + subject, ProviderNs, Backend, subject, CompleteDisclosure(), Ack(), T0);
            var res = svc.ResolveOrCreateAccount("bind-" + subject, Principal(subject), T0);
            Assert.True(res.Accepted, "account first-bind should succeed for test setup");
            return res.AccountId;
        }

        /// <summary>Full admission of one profile: begin lease → resolve/mint character → activate.</summary>
        private (PilotCharacterResolution character, SessionId session) AdmitProfile(
            PilotCharacterAdmissionService chr, PilotAccountId account, long playerId, long transportHandle, string opId)
        {
            var begin = chr.BeginAdmission(account, transportHandle, T0);
            Assert.True(begin.Admitted, "admission should be granted: " + begin.RejectionCode);
            var profile = new VerifiedProfileSubject(playerId, transportHandle);
            var res = chr.ResolveOrCreateCharacter(opId, account, begin.SessionId, profile, T0);
            return (res, begin.SessionId);
        }

        // ── AT-AIP-PROFILE-MINT ─────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_PROFILE_MINT_FirstProfile_MintsOneOpaqueAccountScopedCharacter()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());

            var account = ProvisionAndBind(acct, "7656119800000001");
            var (res, _) = AdmitProfile(chr, account, playerId: 42L, transportHandle: 100L, opId: "char-op-1");

            Assert.Equal(CharacterAdmissionOutcome.Created, res.Outcome);
            Assert.StartsWith("char-", res.CharacterId.Value);
            // Opaque, 128-bit tagged id — not the s_playerID.
            Assert.DoesNotContain("42", res.CharacterId.Value);
            Assert.Equal(37, res.CharacterId.Value.Length); // "char-" (5) + 32 hex
            Assert.Equal(1, store.CharacterCount);
            // Account membership updated.
            Assert.True(store.TryGetAccount(account, out var acctProj));
            Assert.Contains(res.CharacterId, acctProj.CharacterIds);
        }

        // ── AT-AIP-CHARACTER-MEMBERSHIP-ATOMIC ──────────────────────────────────
        [Fact]
        public void AT_AIP_CHARACTER_MEMBERSHIP_ATOMIC_CrashAfterIntent_LeavesNoPartialCharacter()
        {
            var ring = FixedRing();
            var account = ProvisionAndBindFreshStore(ring, "7656119800000002", out _);

            // Crash right after the Intent record is durable, before Committed.
            var storeA = new PilotAccountStore(JournalPath);
            var chrA = new PilotCharacterAdmissionService(storeA, ring, new AccountAdmissionIndex());
            var begin = chrA.BeginAdmission(account, transportHandle: 100L, occurredAt: T0);
            var profile = new VerifiedProfileSubject(playerId: 7L, transportHandle: 100L);
            var crash = new CrashAfter(TransactionPhase.Intent);
            Assert.Throws<InjectedCrash>(() =>
                chrA.ResolveOrCreateCharacter("char-op-atomic", account, begin.SessionId, profile, T0, crash));

            // Reboot: the torn Intent-only transaction quarantines; no character survives; account has none.
            var storeB = new PilotAccountStore(JournalPath);
            Assert.Equal(0, storeB.CharacterCount);
            Assert.True(storeB.TryGetAccount(account, out var acctProj));
            Assert.Empty(acctProj.CharacterIds);
            Assert.True(storeB.QuarantinedIntentTransactions >= 1);

            // Retry on the recovered store mints exactly one character.
            var chrB = new PilotCharacterAdmissionService(storeB, ring, new AccountAdmissionIndex());
            var begin2 = chrB.BeginAdmission(account, 100L, T0);
            var res = chrB.ResolveOrCreateCharacter("char-op-atomic-retry", account, begin2.SessionId, profile, T0);
            Assert.Equal(CharacterAdmissionOutcome.Created, res.Outcome);
            Assert.Equal(1, storeB.CharacterCount);
        }

        // ── AT-AIP-PROFILE-RECONNECT ────────────────────────────────────────────
        [Fact]
        public void AT_AIP_PROFILE_RECONNECT_SameProfile_ResolvesSameCharacterAcrossRestart()
        {
            var ring = FixedRing();
            var account = ProvisionAndBindFreshStore(ring, "7656119800000003", out var accountSubject);

            PilotCharacterId minted;
            {
                var store = new PilotAccountStore(JournalPath);
                var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
                var (res, _) = AdmitProfile(chr, account, 55L, 100L, "char-first");
                Assert.Equal(CharacterAdmissionOutcome.Created, res.Outcome);
                minted = res.CharacterId;
            }

            // Fresh process (restart): index rehydrated from journal, no new mint.
            var store2 = new PilotAccountStore(JournalPath);
            var chr2 = new PilotCharacterAdmissionService(store2, ring, new AccountAdmissionIndex());
            var (res2, _) = AdmitProfile(chr2, account, 55L, 200L /* new transport */, "char-reconnect");
            Assert.Equal(CharacterAdmissionOutcome.Resolved, res2.Outcome);
            Assert.Equal(minted, res2.CharacterId);
            Assert.Equal(1, store2.CharacterCount);
            _ = accountSubject;
        }

        // ── AT-AIP-PROFILE-RENAME / AT-AIP-NAME-NONAUTHORITY ─────────────────────
        [Fact]
        public void AT_AIP_PROFILE_RENAME_DisplayNameAndZdoidChange_DoesNotChangeCharacter()
        {
            // The service consumes only s_playerID; there is deliberately no display-name/ZDOID input.
            // Reconnecting with a different transport handle (a new session ZDOID) and no name anywhere
            // still resolves the same character purely from the profile subject — names are non-authority.
            var ring = FixedRing();
            var account = ProvisionAndBindFreshStore(ring, "7656119800000004", out _);
            var store = new PilotAccountStore(JournalPath);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());

            var (first, s1) = AdmitProfile(chr, account, 88L, 100L, "rename-first");
            Assert.Equal(CharacterAdmissionOutcome.Created, first.Outcome);
            Assert.True(chr.CloseSession(account, s1, 100L));

            // "Rename + new session ZDOID" == same s_playerID on a different transport handle.
            var (again, _) = AdmitProfile(chr, account, 88L, 999L, "rename-again");
            Assert.Equal(CharacterAdmissionOutcome.Resolved, again.Outcome);
            Assert.Equal(first.CharacterId, again.CharacterId);
            Assert.Equal(1, store.CharacterCount);
        }

        // ── AT-AIP-CROSS-ACCOUNT-BLOCK ──────────────────────────────────────────
        [Fact]
        public void AT_AIP_CROSS_ACCOUNT_BLOCK_SamePlayerIdUnderAnotherAccount_CannotResolveFirstCharacter()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());

            var accountA = ProvisionAndBind(acct, "7656119800000010");
            var accountB = ProvisionAndBind(acct, "7656119800000011");

            var (charA, _) = AdmitProfile(chr, accountA, playerId: 500L, transportHandle: 100L, opId: "cross-a");
            Assert.Equal(CharacterAdmissionOutcome.Created, charA.Outcome);

            // Account B presents the SAME numeric s_playerID; it must mint its OWN distinct character,
            // never load A's. Direct index lookup under A's HMAC input scoped to B must miss.
            var (charB, _) = AdmitProfile(chr, accountB, playerId: 500L, transportHandle: 200L, opId: "cross-b");
            Assert.Equal(CharacterAdmissionOutcome.Created, charB.Outcome);
            Assert.NotEqual(charA.CharacterId, charB.CharacterId);

            // And the account-scoped index proves A's identical-shaped value cannot resolve B's character.
            var bHmac = ring.ProfileHmacActive(accountB.Value, "500");
            Assert.False(store.TryLookupCharacter(accountA, bHmac, out _));
            Assert.True(store.TryLookupCharacter(accountB, bHmac, out var bChar));
            Assert.Equal(charB.CharacterId, bChar.CharacterId);
        }

        // ── AT-AIP-PROFILE-PREVIOUS-KEY-REKEY ───────────────────────────────────
        [Fact]
        public void AT_AIP_PROFILE_PREVIOUS_KEY_REKEY_PreviousKeyMatch_ReKeysInPlaceSameCharacterId()
        {
            // Mint under k1 alone, then rotate: k2 active, k1 previous. The same profile resolves under
            // the previous key and re-keys in place — same CharacterId, higher revision, no second record.
            var account = ProvisionAndBindFreshStore(FixedRing("k1", 10), "7656119800000005", out _);

            PilotCharacterId minted;
            {
                var ringK1 = FixedRing("k1", 10);
                var store = new PilotAccountStore(JournalPath);
                var chr = new PilotCharacterAdmissionService(store, ringK1, new AccountAdmissionIndex());
                var (res, _) = AdmitProfile(chr, account, 77L, 100L, "rekey-mint");
                minted = res.CharacterId;
            }

            var ringRotated = FixedRing("k2", 20, previous: "k1", prevFill: 10);
            var store2 = new PilotAccountStore(JournalPath);
            var chr2 = new PilotCharacterAdmissionService(store2, ringRotated, new AccountAdmissionIndex());
            var (res2, _) = AdmitProfile(chr2, account, 77L, 200L, "rekey-resolve");

            Assert.Equal(CharacterAdmissionOutcome.Resolved, res2.Outcome);
            Assert.Equal("ResolvedRekeyed", res2.ResultCode);
            Assert.Equal(minted, res2.CharacterId);
            Assert.Equal(1, store2.CharacterCount);

            // The character now lives on the active (k2) version.
            Assert.True(store2.TryGetCharacter(minted, out var proj));
            Assert.Equal("k2", proj.ProfileHmac.KeyVersion.Value);
            Assert.True(res2.CharacterRevision >= 2);

            // Re-key persists across restart: a fresh process resolves under active key with no re-key.
            var store3 = new PilotAccountStore(JournalPath);
            var chr3 = new PilotCharacterAdmissionService(store3, ringRotated, new AccountAdmissionIndex());
            var (res3, _) = AdmitProfile(chr3, account, 77L, 300L, "rekey-after-restart");
            Assert.Equal(CharacterAdmissionOutcome.Resolved, res3.Outcome);
            Assert.Equal("Resolved", res3.ResultCode);
            Assert.Equal(minted, res3.CharacterId);
        }

        // ── AT-AIP-ONE-SESSION ──────────────────────────────────────────────────
        [Fact]
        public void AT_AIP_ONE_SESSION_SecondSiblingProfile_RejectsAsAlreadyConnected_NoCharacterMutation()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000006");

            // Session 1 begins admission and holds the sole lease.
            var begin1 = chr.BeginAdmission(account, transportHandle: 100L, occurredAt: T0);
            Assert.True(begin1.Admitted);

            // Session 2 (a DIFFERENT sibling profile of the same account) tries to begin — rejected
            // BEFORE any character mint.
            var begin2 = chr.BeginAdmission(account, transportHandle: 200L, occurredAt: T0);
            Assert.False(begin2.Admitted);
            Assert.Equal(CharacterRejectionCode.AccountAlreadyConnected, begin2.RejectionCode);
            Assert.Equal(0, store.CharacterCount);

            // Session 1 can still mint its character normally.
            var res = chr.ResolveOrCreateCharacter("one-session-char", account, begin1.SessionId,
                new VerifiedProfileSubject(11L, 100L), T0);
            Assert.Equal(CharacterAdmissionOutcome.Created, res.Outcome);

            // After session 1 closes, session 2 can now admit sequentially.
            Assert.True(chr.CloseSession(account, begin1.SessionId, 100L));
            var begin3 = chr.BeginAdmission(account, transportHandle: 200L, occurredAt: T0);
            Assert.True(begin3.Admitted);
        }

        // ── AT-AIP-ADMISSION-LEASE-RACE ─────────────────────────────────────────
        [Fact]
        public void AT_AIP_ADMISSION_LEASE_RACE_ConcurrentReservations_ExactlyOneWins()
        {
            var index = new AccountAdmissionIndex();
            var account = new PilotAccountId("acct-race");
            const int racers = 32;
            var reserved = new ConcurrentBag<SessionId>();

            Parallel.For(0, racers, i =>
            {
                var sid = OpaqueIdMint.NewSessionId();
                var r = index.TryReserve(account, sid, transportHandle: 1000 + i, admittedAt: T0);
                if (r.Outcome == AdmissionReservationOutcome.Reserved) reserved.Add(sid);
            });

            Assert.Single(reserved);
            Assert.Equal(1, index.ActiveLeaseCount);
        }

        // ── AT-AIP-STALE-DISCONNECT ─────────────────────────────────────────────
        [Fact]
        public void AT_AIP_STALE_DISCONNECT_OldSession_CannotCloseNewerAdmission()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000007");

            // Session 1 admits, mints, then disconnects.
            var begin1 = chr.BeginAdmission(account, transportHandle: 100L, occurredAt: T0);
            var res1 = chr.ResolveOrCreateCharacter("stale-char", account, begin1.SessionId,
                new VerifiedProfileSubject(21L, 100L), T0);
            Assert.True(res1.Accepted);
            Assert.True(chr.CloseSession(account, begin1.SessionId, 100L));

            // Session 2 admits (newer lease, different session + transport).
            var begin2 = chr.BeginAdmission(account, transportHandle: 300L, occurredAt: T0 + 10);
            Assert.True(begin2.Admitted);

            // A STALE disconnect for session 1 arrives late — it must NOT close session 2's lease.
            Assert.False(chr.CloseSession(account, begin1.SessionId, 100L));
            Assert.True(chr.Admission.HasLease(account));

            // The newer session's own close still works.
            Assert.True(chr.CloseSession(account, begin2.SessionId, 300L));
            Assert.False(chr.Admission.HasLease(account));
        }

        // ── AT-AIP-CREATOR-BRIDGE ───────────────────────────────────────────────
        [Fact]
        public void AT_AIP_CREATOR_BRIDGE_MatchingCreator_ResolvesInternalCharacter_MismatchRejects()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000008");

            var (minted, _) = AdmitProfile(chr, account, playerId: 3333L, transportHandle: 100L, opId: "bridge-mint");
            Assert.Equal(CharacterAdmissionOutcome.Created, minted.Outcome);

            var profile = new VerifiedProfileSubject(3333L, 100L);

            // Object created by THIS peer (s_creator == s_playerID) resolves to the internal character.
            var ok = chr.ResolveCreatorCharacter(account, profile, objectCreatorPlayerId: 3333L);
            Assert.True(ok.Accepted);
            Assert.Equal(minted.CharacterId, ok.CharacterId);

            // Object created by a DIFFERENT player rejects — no account resolved from a world object.
            var mismatch = chr.ResolveCreatorCharacter(account, profile, objectCreatorPlayerId: 9999L);
            Assert.False(mismatch.Accepted);
            Assert.Equal(CharacterRejectionCode.CreatorMismatch, mismatch.RejectionCode);
        }

        // ── Guard: zero/invalid profile subject rejects before any mint ──────────
        [Fact]
        public void ProfileSubjectInvalid_ZeroPlayerId_RejectsBeforeMint()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000009");

            var begin = chr.BeginAdmission(account, 100L, T0);
            var res = chr.ResolveOrCreateCharacter("bad-profile", account, begin.SessionId,
                new VerifiedProfileSubject(0L, 100L), T0);
            Assert.False(res.Accepted);
            Assert.Equal(CharacterRejectionCode.ProfileSubjectInvalid, res.RejectionCode);
            Assert.Equal(0, store.CharacterCount);
        }

        // ── Guard: character mint requires the matching pending lease ────────────
        [Fact]
        public void AdmissionLeaseMismatch_WithoutLease_RejectsMint()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000012");

            // No BeginAdmission → the caller holds no lease.
            var res = chr.ResolveOrCreateCharacter("no-lease", account, OpaqueIdMint.NewSessionId(),
                new VerifiedProfileSubject(5L, 100L), T0);
            Assert.False(res.Accepted);
            Assert.Equal(CharacterRejectionCode.AdmissionLeaseMismatch, res.RejectionCode);
        }

        // ── Guard: two sequential DISTINCT sibling profiles → two characters ─────
        [Fact]
        public void SequentialSiblingProfiles_MintTwoDistinctCharacters()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000013");

            var (p1, s1) = AdmitProfile(chr, account, 1001L, 100L, "sib-1");
            Assert.True(chr.CloseSession(account, s1, 100L));
            var (p2, _) = AdmitProfile(chr, account, 1002L, 200L, "sib-2");

            Assert.Equal(CharacterAdmissionOutcome.Created, p1.Outcome);
            Assert.Equal(CharacterAdmissionOutcome.Created, p2.Outcome);
            Assert.NotEqual(p1.CharacterId, p2.CharacterId);
            Assert.Equal(2, store.CharacterCount);
            Assert.True(store.TryGetAccount(account, out var acctProj));
            Assert.Equal(2, acctProj.CharacterIds.Count);
        }

        // ── Guard: character mint replays idempotently on the same operation id ──
        [Fact]
        public void CharacterMint_SameOperationId_ReplaysIdempotently()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000014");

            var begin = chr.BeginAdmission(account, 100L, T0);
            var profile = new VerifiedProfileSubject(64L, 100L);
            var first = chr.ResolveOrCreateCharacter("dup-op", account, begin.SessionId, profile, T0);
            var replay = chr.ResolveOrCreateCharacter("dup-op", account, begin.SessionId, profile, T0);

            Assert.Equal(CharacterAdmissionOutcome.Created, first.Outcome);
            Assert.Equal(CharacterAdmissionOutcome.Resolved, replay.Outcome);
            Assert.Equal("Replayed", replay.ResultCode);
            Assert.Equal(first.CharacterId, replay.CharacterId);
            Assert.Equal(1, store.CharacterCount);
        }

        // ── Guard: raw s_playerID never persisted (profile HMAC only on disk) ────
        [Fact]
        public void ProfileSubject_RawPlayerId_NeverAppearsOnDisk()
        {
            var ring = FixedRing();
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            var chr = new PilotCharacterAdmissionService(store, ring, new AccountAdmissionIndex());
            var account = ProvisionAndBind(acct, "7656119800000015");

            // A distinctive player id unlikely to collide with any minted-id hex or digest.
            const long distinctivePlayer = 987654321987654321L;
            AdmitProfile(chr, account, distinctivePlayer, 100L, "pii-char");

            Assert.False(
                PersistedPiiScanner.JournalContainsForbidden(store, new[] { distinctivePlayer.ToString() }, out var offending),
                "raw s_playerID leaked to the account journal: " + offending);
        }

        // ---- test-only crash injector ----

        private PilotAccountId ProvisionAndBindFreshStore(LookupKeyRing ring, string subject, out string accountSubject)
        {
            var store = new PilotAccountStore(JournalPath);
            var acct = NewAccountService(store, ring);
            accountSubject = subject;
            return ProvisionAndBind(acct, subject);
        }

        private sealed class InjectedCrash : Exception { }

        private sealed class CrashAfter : IAccountCrashInjector
        {
            private readonly TransactionPhase _phase;
            public CrashAfter(TransactionPhase phase) { _phase = phase; }
            public void AfterPhase(TransactionPhase phase)
            {
                if (phase == _phase) throw new InjectedCrash();
            }
        }
    }
}
