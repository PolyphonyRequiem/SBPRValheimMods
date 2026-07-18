using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-005 Tracer 2 — character selection + one-session admission (engine-free CLEAN core).
    //
    // This is the second vertical slice on top of the Tracer-1 account foundation. It turns a
    // server-observed authenticated Valheim profile fact (nonzero s_playerID) into a server-MINTED,
    // account-scoped opaque CharacterId, reserves exactly one admission per account before minting, and
    // bridges vanilla creator evidence to the internal character. The provider/profile subjects never
    // become durable identity (spec closed-pilot decisions #4/#5; AIP-FR-009..013).
    //
    // Ordering discipline (data-model.md "Begin account admission" / "Resolve or create pilot character"):
    //   1. account resolved (Tracer 1) → 2. BeginAdmission reserves the sole PendingAdmission lease →
    //   3. ResolveOrCreateCharacter (only the lease holder may mint) → 4. ActivateSession promotes to
    //   Active. A second sibling-profile connection rejects at step 2 BEFORE any character mutation.
    //
    // net48 audit: System.* + generics only. No UnityEngine/Valheim/BepInEx — link-compiles under net8
    // and ships under net48 exactly like the Tracer-1 account service.

    public enum CharacterAdmissionOutcome { Created, Resolved, Rejected }

    /// <summary>Stable rejection subset for character/session admission (contracts.md "Stable rejection
    /// vocabulary"). Disjoint from the account-level codes so a caller can tell which stage failed.</summary>
    public enum CharacterRejectionCode
    {
        None = 0,
        AccountAlreadyConnected,
        AdmissionLeaseMismatch,
        ProfileSubjectInvalid,
        AccountDisabled,
        AccountDeletionPending,
        AccountDeleted,
        LookupKeyUnavailable,
        AccountNotFound,
        CharacterNotOwned,
        CreatorMismatch,
        OperationConflict
    }

    /// <summary>Result of a character resolve/mint attempt.</summary>
    public sealed class PilotCharacterResolution
    {
        public CharacterAdmissionOutcome Outcome { get; }
        public CharacterRejectionCode RejectionCode { get; }
        public PilotCharacterId CharacterId { get; }
        public long CharacterRevision { get; }
        public string ResultCode { get; }

        public PilotCharacterResolution(CharacterAdmissionOutcome outcome, CharacterRejectionCode rejection,
            PilotCharacterId characterId, long characterRevision, string resultCode)
        {
            Outcome = outcome;
            RejectionCode = rejection;
            CharacterId = characterId;
            CharacterRevision = characterRevision;
            ResultCode = resultCode;
        }

        public bool Accepted => Outcome != CharacterAdmissionOutcome.Rejected;

        internal static PilotCharacterResolution Reject(CharacterRejectionCode code) =>
            new PilotCharacterResolution(CharacterAdmissionOutcome.Rejected, code, default, 0, code.ToString());
    }

    /// <summary>Result of a BeginAdmission attempt.</summary>
    public sealed class PilotAdmissionResult
    {
        public bool Admitted { get; }
        public CharacterRejectionCode RejectionCode { get; }
        public SessionId SessionId { get; }

        private PilotAdmissionResult(bool admitted, CharacterRejectionCode code, SessionId sessionId)
        {
            Admitted = admitted;
            RejectionCode = code;
            SessionId = sessionId;
        }

        internal static PilotAdmissionResult Ok(SessionId sessionId) =>
            new PilotAdmissionResult(true, CharacterRejectionCode.None, sessionId);
        internal static PilotAdmissionResult Reject(CharacterRejectionCode code) =>
            new PilotAdmissionResult(false, code, default);
    }

    /// <summary>The character selection + single-session admission service. Composes over the same
    /// boot-rehydrated <see cref="PilotAccountStore"/> and <see cref="LookupKeyRing"/> the Tracer-1
    /// account service uses, plus the ephemeral <see cref="AccountAdmissionIndex"/>.</summary>
    public sealed class PilotCharacterAdmissionService
    {
        private readonly PilotAccountStore _store;
        private readonly LookupKeyRing _keyRing;
        private readonly AccountAdmissionIndex _admission;

        // Serializes the resolve→commit critical section for character minting, mirroring the account
        // service's admission gate so a re-key never races a concurrent mint of the same character.
        private readonly object _mintGate = new object();

        public PilotCharacterAdmissionService(PilotAccountStore store, LookupKeyRing keyRing, AccountAdmissionIndex admission)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
            _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        }

        public AccountAdmissionIndex Admission => _admission;

        // ---- Begin account admission (contracts.md BeginPilotAdmission) ----

        /// <summary>Reserve the account's sole PendingAdmission lease immediately after account
        /// resolution and BEFORE any profile lookup or character mint. A second connection (even a
        /// different sibling profile of the same account) rejects as AccountAlreadyConnected. The
        /// returned SessionId is server-minted; the same session retrying is idempotent.</summary>
        public PilotAdmissionResult BeginAdmission(PilotAccountId accountId, long transportHandle, long occurredAt)
        {
            // Reject admission to a non-admissible account up front (disabled/deletion-pending/deleted).
            if (!_store.TryGetAccount(accountId, out var acct))
                return PilotAdmissionResult.Reject(CharacterRejectionCode.AccountNotFound);
            switch (acct.Status)
            {
                case PilotAccountStatus.Disabled: return PilotAdmissionResult.Reject(CharacterRejectionCode.AccountDisabled);
                case PilotAccountStatus.DeletionPending: return PilotAdmissionResult.Reject(CharacterRejectionCode.AccountDeletionPending);
                case PilotAccountStatus.Deleted: return PilotAdmissionResult.Reject(CharacterRejectionCode.AccountDeleted);
            }

            var sessionId = OpaqueIdMint.NewSessionId();
            var reservation = _admission.TryReserve(accountId, sessionId, transportHandle, occurredAt);
            if (reservation.Outcome == AdmissionReservationOutcome.AlreadyConnected)
                return PilotAdmissionResult.Reject(CharacterRejectionCode.AccountAlreadyConnected);
            return PilotAdmissionResult.Ok(sessionId);
        }

        /// <summary>Re-enter an existing admission for the same session (idempotent). Rarely needed
        /// directly; BeginAdmission mints a fresh session each call, so a caller that wants to reuse a
        /// session passes the prior SessionId here.</summary>
        public PilotAdmissionResult ReenterAdmission(PilotAccountId accountId, SessionId sessionId, long transportHandle, long occurredAt)
        {
            var reservation = _admission.TryReserve(accountId, sessionId, transportHandle, occurredAt);
            if (reservation.Outcome == AdmissionReservationOutcome.AlreadyConnected)
                return PilotAdmissionResult.Reject(CharacterRejectionCode.AccountAlreadyConnected);
            return PilotAdmissionResult.Ok(sessionId);
        }

        // ---- Resolve or create pilot character (contracts.md ResolveOrCreatePilotCharacter) ----

        /// <summary>Resolve the account/profile character, or mint one CharacterId + binding atomically.
        /// Requires the caller to hold the account's matching pending lease, a nonzero server-observed
        /// s_playerID, and an admissible account. A previous-key profile match revises that same binding
        /// in place under the active key WITHOUT changing CharacterId.</summary>
        public PilotCharacterResolution ResolveOrCreateCharacter(
            string operationId, PilotAccountId accountId, SessionId sessionId, VerifiedProfileSubject profile,
            long occurredAt, IAccountCrashInjector? crash = null)
        {
            if (!profile.IsResolved)
                return PilotCharacterResolution.Reject(CharacterRejectionCode.ProfileSubjectInvalid);

            // The caller must hold the account's matching pending lease (transport tied to the profile).
            if (!_admission.TryGetHeldLease(accountId, sessionId, profile.TransportHandle, out _))
                return PilotCharacterResolution.Reject(CharacterRejectionCode.AdmissionLeaseMismatch);

            lock (_mintGate)
            {
                return ResolveOrCreateCharacterLocked(operationId, accountId, profile, occurredAt, crash);
            }
        }

        private PilotCharacterResolution ResolveOrCreateCharacterLocked(
            string operationId, PilotAccountId accountId, VerifiedProfileSubject profile,
            long occurredAt, IAccountCrashInjector? crash)
        {
            if (!_store.TryGetAccount(accountId, out var acct))
                return PilotCharacterResolution.Reject(CharacterRejectionCode.AccountNotFound);
            switch (acct.Status)
            {
                case PilotAccountStatus.Disabled: return PilotCharacterResolution.Reject(CharacterRejectionCode.AccountDisabled);
                case PilotAccountStatus.DeletionPending: return PilotCharacterResolution.Reject(CharacterRejectionCode.AccountDeletionPending);
                case PilotAccountStatus.Deleted: return PilotCharacterResolution.Reject(CharacterRejectionCode.AccountDeleted);
            }

            // Idempotent replay of a committed character mint under this operation id.
            if (_store.TryGetCommittedOp(operationId, out var recBinding, out _, out var recResult))
            {
                if (recResult.StartsWith("character:", StringComparison.Ordinal))
                {
                    var charId = new PilotCharacterId(recResult.Substring("character:".Length));
                    string expect = ProfileBindingFor(accountId, profile);
                    if (!string.Equals(recBinding, expect, StringComparison.Ordinal))
                        return PilotCharacterResolution.Reject(CharacterRejectionCode.OperationConflict);
                    long rev = _store.TryGetCharacter(charId, out var c) ? c.Revision : 0;
                    return new PilotCharacterResolution(CharacterAdmissionOutcome.Resolved, CharacterRejectionCode.None, charId, rev, "Replayed");
                }
                return PilotCharacterResolution.Reject(CharacterRejectionCode.OperationConflict);
            }

            SubjectLookupHmac activeHmac;
            try { activeHmac = _keyRing.ProfileHmacActive(accountId.Value, profile.CanonicalPlayerId); }
            catch (LookupKeyUnavailableException) { return PilotCharacterResolution.Reject(CharacterRejectionCode.LookupKeyUnavailable); }

            // 1) Existing active character under the active key resolves directly.
            if (_store.TryLookupCharacter(accountId, activeHmac, out var existing))
                return new PilotCharacterResolution(CharacterAdmissionOutcome.Resolved, CharacterRejectionCode.None,
                    existing.CharacterId, existing.Revision, "Resolved");

            // 2) Existing character under the configured previous key → resolve + lazily re-key in place.
            if (_keyRing.HasPrevious)
            {
                var prevHmac = _keyRing.ProfileHmacUnder(_keyRing.PreviousVersion, accountId.Value, profile.CanonicalPlayerId);
                if (_store.TryLookupCharacter(accountId, prevHmac, out var prevChar))
                {
                    ReKeyCharacterInPlace(operationId + "#rekey", prevChar, activeHmac, occurredAt, crash);
                    long rev2 = _store.TryGetCharacter(prevChar.CharacterId, out var c2) ? c2.Revision : prevChar.Revision + 1;
                    return new PilotCharacterResolution(CharacterAdmissionOutcome.Resolved, CharacterRejectionCode.None,
                        prevChar.CharacterId, rev2, "ResolvedRekeyed");
                }
            }

            // 3) No binding → mint one CharacterId + binding atomically and add account membership.
            var newCharId = OpaqueIdMint.NewCharacterId();
            var changes = new List<JournalChange>
            {
                new JournalChange("char")
                    .Set("characterId", newCharId.Value)
                    .Set("accountId", accountId.Value)
                    .Set("hmac", activeHmac.Hex)
                    .Set("keyVersion", activeHmac.KeyVersion.Value)
                    .Set("status", CharacterStatus.Active.ToString())
                    .Set("revision", "1"),
                new JournalChange("acct-add-char")
                    .Set("accountId", accountId.Value)
                    .Set("characterId", newCharId.Value)
                    .Set("revision", (acct.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            };

            string binding = ProfileBindingFor(accountId, profile);
            _store.Commit(operationId, "txn-char-" + newCharId.Value, binding, PilotAccountStore.Digest(operationId),
                "character:" + newCharId.Value, occurredAt, changes, crash);

            long finalRev = _store.TryGetCharacter(newCharId, out var created) ? created.Revision : 1;
            return new PilotCharacterResolution(CharacterAdmissionOutcome.Created, CharacterRejectionCode.None,
                newCharId, finalRev, "Created");
        }

        /// <summary>Re-key one character binding in place under the active key: drop the previous index
        /// key, write the current HMAC/version, increment revision, RETAIN the same CharacterId
        /// (AT-AIP-PROFILE-PREVIOUS-KEY-REKEY). No superseded character record is created.</summary>
        private void ReKeyCharacterInPlace(string operationId, PilotCharacterProjection character,
            SubjectLookupHmac activeHmac, long occurredAt, IAccountCrashInjector? crash)
        {
            if (_store.TryGetCommittedOp(operationId, out _, out _, out _)) return; // idempotent

            var change = new JournalChange("char-rekey")
                .Set("characterId", character.CharacterId.Value)
                .Set("hmac", activeHmac.Hex)
                .Set("keyVersion", activeHmac.KeyVersion.Value)
                .Set("revision", (character.Revision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));

            _store.Commit(operationId, "txn-char-rekey-" + character.CharacterId.Value,
                PilotAccountStore.Digest("char-rekey|" + character.CharacterId.Value + "|" + activeHmac.Hex),
                PilotAccountStore.Digest(operationId), "char-rekey:" + character.CharacterId.Value, occurredAt,
                new[] { change }, crash);
        }

        // ---- Activate / close session (contracts.md ActivatePilotSession / ClosePilotSession) ----

        /// <summary>Promote the account's matching pending lease to Active, stamping the resolved
        /// character. Rejects if the lease does not match or the character is not owned by the
        /// account.</summary>
        public CharacterRejectionCode ActivateSession(PilotAccountId accountId, SessionId sessionId, long transportHandle, PilotCharacterId characterId)
        {
            if (!_store.TryGetCharacter(characterId, out var character) || !character.AccountId.Equals(accountId))
                return CharacterRejectionCode.CharacterNotOwned;
            if (!_store.TryGetAccount(accountId, out var acct))
                return CharacterRejectionCode.AccountNotFound;
            if (acct.Status != PilotAccountStatus.Active)
                return acct.Status == PilotAccountStatus.Disabled ? CharacterRejectionCode.AccountDisabled
                    : acct.Status == PilotAccountStatus.DeletionPending ? CharacterRejectionCode.AccountDeletionPending
                    : CharacterRejectionCode.AccountDeleted;
            if (!_admission.TryActivate(accountId, sessionId, transportHandle, characterId))
                return CharacterRejectionCode.AdmissionLeaseMismatch;
            return CharacterRejectionCode.None;
        }

        /// <summary>Close a session by removing ONLY a lease whose account, session, and transport all
        /// match. A stale disconnect cannot close a newer admission/session
        /// (AT-AIP-STALE-DISCONNECT). Returns true iff a lease was actually removed.</summary>
        public bool CloseSession(PilotAccountId accountId, SessionId sessionId, long transportHandle) =>
            _admission.TryRelease(accountId, sessionId, transportHandle);

        // ---- Creator evidence bridge (contracts.md "Creator evidence bridge", AIP-FR-011) ----

        /// <summary>Resolve a placed object's creator to the internal CharacterId. The world object's
        /// server-owned <paramref name="objectCreatorPlayerId"/> (s_creator) is compared to the
        /// authenticated peer's server-observed <paramref name="profile"/> s_playerID; only on a match
        /// does the verified profile fact resolve through the account-scoped profile index to a
        /// CharacterId. No world object resolves an account directly, and no raw s_playerID becomes a
        /// durable CharacterId. Rejects CreatorMismatch when the object was not created by this peer.</summary>
        public PilotCharacterResolution ResolveCreatorCharacter(PilotAccountId accountId, VerifiedProfileSubject profile, long objectCreatorPlayerId)
        {
            if (!profile.IsResolved)
                return PilotCharacterResolution.Reject(CharacterRejectionCode.ProfileSubjectInvalid);

            // Step 2 (contracts bridge): compare s_creator to the authenticated peer's s_playerID in the
            // existing creator fact space. A mismatch means this peer did not create the object.
            if (objectCreatorPlayerId != profile.PlayerId)
                return PilotCharacterResolution.Reject(CharacterRejectionCode.CreatorMismatch);

            SubjectLookupHmac activeHmac;
            try { activeHmac = _keyRing.ProfileHmacActive(accountId.Value, profile.CanonicalPlayerId); }
            catch (LookupKeyUnavailableException) { return PilotCharacterResolution.Reject(CharacterRejectionCode.LookupKeyUnavailable); }

            // Step 3 (contracts bridge): resolve the verified profile fact through the profile index.
            if (_store.TryLookupCharacter(accountId, activeHmac, out var character))
                return new PilotCharacterResolution(CharacterAdmissionOutcome.Resolved, CharacterRejectionCode.None,
                    character.CharacterId, character.Revision, "CreatorResolved");

            if (_keyRing.HasPrevious)
            {
                var prevHmac = _keyRing.ProfileHmacUnder(_keyRing.PreviousVersion, accountId.Value, profile.CanonicalPlayerId);
                if (_store.TryLookupCharacter(accountId, prevHmac, out var prevChar))
                    return new PilotCharacterResolution(CharacterAdmissionOutcome.Resolved, CharacterRejectionCode.None,
                        prevChar.CharacterId, prevChar.Revision, "CreatorResolved");
            }

            // Creator matched the peer, but no character binding exists for this account/profile yet.
            return PilotCharacterResolution.Reject(CharacterRejectionCode.CharacterNotOwned);
        }

        private string ProfileBindingFor(PilotAccountId accountId, VerifiedProfileSubject profile)
        {
            var hmac = _keyRing.ProfileHmacActive(accountId.Value, profile.CanonicalPlayerId);
            return PilotAccountStore.Digest("profile|" + accountId.Value + "|" + hmac.KeyVersion.Value + "|" + hmac.Hex);
        }
    }
}
