using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Accounts
{
    // IAP-003 Tracer 1 — the framed pilot-account journal + boot-rehydrated indexes (engine-free CLEAN
    // core). This is the durable owner of allowlist/account/credential lifecycle truth for the closed
    // pilot (data-model.md Aggregate 4 "PilotAccountJournal"). It reuses the SHIPPED framed-journal
    // durability discipline (length + CRC32 framing, fsync at durable boundaries, torn-tail truncation,
    // terminal-record-only projection) proven by OperationReceiptStore, applied to account records.
    //
    // TRANSACTION SHAPE (data-model.md invariants): a durable mutation is one Intent record followed by
    // one Committed record. The Committed record carries the COMPLETE logical changes[] for the whole
    // transaction (account+credential creation, re-key supersede+create, allowlist provision/supersede).
    // Projections apply ALL of a committed transaction's changes or NONE — a torn/half-written tail
    // quarantines and never projects, so there is no independently durable partial account.
    //
    // NO RAW SUBJECT is ever written: only SubjectLookupHmac (versioned HMAC) enters a record. Boot
    // replay rebuilds the credential index before admission opens; steady-state lookup is an indexed
    // dictionary read, never a journal scan (AIP-FR-017).
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, Convert.ToBase64.
    // No UnityEngine/Valheim/BepInEx — ships under net48 AND link-compiles under net8.

    public enum AllowlistStatus { Active, Superseded, Revoked, Purged }
    public enum PilotAccountStatus { Active, Disabled, DeletionPending, Deleted }
    public enum CredentialStatus { Active, Revoked, Superseded, Purged }

    // IAP-012 Tracer 4 — pilot lifecycle + artifact-catalog + retention-hold status vocabularies.
    public enum PilotLifecycleStatus { Active, Closing, Purged }
    public enum ArtifactStatus { Active, PurgePending, Purged }
    public enum RetentionHoldStatus { Active, Released }

    /// <summary>The catalogable artifact classes (data-model.md Aggregate 5). Every generation of any of
    /// these that can contain pilot-linked data MUST be cataloged before use/success is acknowledged.</summary>
    public enum PilotArtifactType { AccountJournal, GameplayJournal, WorldSave, SecurityLog, Export, Backup, QuarantineReport, ResetAudit }

    /// <summary>Projected allowlist entry (data-model.md Aggregate 0). No raw subject.</summary>
    public sealed class AllowlistEntryProjection
    {
        public AllowlistEntryId AllowlistEntryId;
        public string ProviderNamespace = string.Empty;
        public string BackendIssuer = string.Empty;
        public SubjectLookupHmac Hmac;
        public AllowlistStatus Status;
        public long Revision;
        public string NoticeVersion = string.Empty;
        public long NoticeAcknowledgedAt;
    }

    /// <summary>Projected account record (data-model.md Aggregate 1). No name/subject/token/HMAC.</summary>
    public sealed class PilotAccountProjection
    {
        public PilotAccountId AccountId;
        public PilotAccountStatus Status;
        public long Revision;
        public readonly List<CredentialBindingId> CredentialBindingIds = new List<CredentialBindingId>();
        public readonly List<PilotCharacterId> CharacterIds = new List<PilotCharacterId>();
        public string NoticeVersion = string.Empty;
        public long NoticeAcknowledgedAt;
        public string RetentionPolicyVersion = string.Empty;
    }

    /// <summary>Projected credential binding (data-model.md Aggregate 2). Raw subject never present.</summary>
    public sealed class CredentialBindingProjection
    {
        public CredentialBindingId CredentialBindingId;
        public AllowlistEntryId AllowlistEntryId;
        public PilotAccountId AccountId;
        public string ProviderNamespace = string.Empty;
        public string BackendIssuer = string.Empty;
        public SubjectLookupHmac Hmac;
        public CredentialStatus Status;
        public long Revision;
    }

    public enum CharacterStatus { Active, Tombstoned, Purged }

    /// <summary>Projected character binding (data-model.md Aggregate 3 "PilotCharacterBinding"). Holds
    /// only the account-scoped profile HMAC — never the raw s_playerID and never the display name
    /// (AIP-FR-010/AIP-FR-012). Introduced by IAP-005 Tracer 2.</summary>
    public sealed class PilotCharacterProjection
    {
        public PilotCharacterId CharacterId;
        public PilotAccountId AccountId;
        public SubjectLookupHmac ProfileHmac;
        public CharacterStatus Status;
        public long Revision;
    }

    /// <summary>Projected pilot lifecycle record (data-model.md Aggregate 5 PilotLifecycleRecord).</summary>
    public sealed class PilotLifecycleProjection
    {
        public PilotId PilotId;
        public PilotLifecycleStatus Status;
        public long Revision;
        public long StartedAt;
        public long EndedAt;        // 0 until ClosePilot
        public long PurgeDueAt;     // 0 until ClosePilot; derived from EndedAt + policy
        public string PolicyVersion = string.Empty;
    }

    /// <summary>Projected cataloged artifact record (data-model.md Aggregate 5 PilotDataArtifactRecord).
    /// The storage locator is operator-only and never exported to players.</summary>
    public sealed class PilotDataArtifactProjection
    {
        public DataArtifactId DataArtifactId;
        public PilotArtifactType ArtifactType;
        public string StorageLocator = string.Empty;
        public long CreatedAt;
        public long ExpiresAt;
        public ArtifactStatus Status;
        public long Revision;
        public string PurgeEvidenceDigest = string.Empty;
    }

    /// <summary>Projected retention hold (data-model.md RetentionHold). Scoped, reasoned, and expiring;
    /// a hold can never target everything by default or omit an expiry.</summary>
    public sealed class RetentionHoldProjection
    {
        public RetentionHoldId RetentionHoldId;
        public string Scope = string.Empty;
        public string Reason = string.Empty;
        public long Revision;
        public long CreatedAt;
        public long ExpiresAt;
        public RetentionHoldStatus Status;
    }

    /// <summary>One version-census line (data-model.md RunLookupKeyVersionCensus). Counts by key version
    /// with NO HMAC exposed (AT-AIP-KEY-VERSION-CENSUS).</summary>
    public sealed class KeyVersionCensus
    {
        private readonly Dictionary<string, int> _allowlist = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _credential = new Dictionary<string, int>(StringComparer.Ordinal);

        public void CountAllowlist(LookupKeyVersion v) => Bump(_allowlist, v.Value);
        public void CountCredential(LookupKeyVersion v) => Bump(_credential, v.Value);

        public int AllowlistCount(LookupKeyVersion v) => Get(_allowlist, v.Value);
        public int CredentialCount(LookupKeyVersion v) => Get(_credential, v.Value);
        public int TotalForVersion(LookupKeyVersion v) => AllowlistCount(v) + CredentialCount(v);

        public IReadOnlyCollection<string> Versions()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in _allowlist.Keys) set.Add(k);
            foreach (var k in _credential.Keys) set.Add(k);
            return set;
        }

        private static void Bump(Dictionary<string, int> d, string k) => d[k] = (d.TryGetValue(k, out int n) ? n : 0) + 1;
        private static int Get(Dictionary<string, int> d, string k) => d.TryGetValue(k, out int n) ? n : 0;
    }

    // ---- Journal change model ----

    /// <summary>One logical delta inside a committed transaction. A transaction's changes[] project
    /// atomically (all or nothing). Encoded field-tagged so an embedded delimiter cannot break framing
    /// (values are base64).</summary>
    public sealed class JournalChange
    {
        public string Kind = string.Empty;                 // acct | cred | allow | acct-status | cred-status | allow-status | acct-add-cred
        private readonly Dictionary<string, string> _fields = new Dictionary<string, string>(StringComparer.Ordinal);

        public JournalChange() { }
        public JournalChange(string kind) { Kind = kind; }

        public JournalChange Set(string key, string value) { _fields[key] = value ?? string.Empty; return this; }
        public string Get(string key) => _fields.TryGetValue(key, out var v) ? v : string.Empty;
        public long GetLong(string key) => long.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
        public IEnumerable<KeyValuePair<string, string>> Fields => _fields;

        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append(B64(Kind));
            foreach (var kv in _fields)
            {
                // Separators ';' and ':' are outside the base64 alphabet (A-Za-z0-9+/=), so a base64
                // field value's '=' padding cannot be mistaken for the key/value separator.
                sb.Append(';').Append(B64(kv.Key)).Append(':').Append(B64(kv.Value));
            }
            return sb.ToString();
        }

        public static JournalChange Decode(string s)
        {
            var parts = s.Split(';');
            var change = new JournalChange(Unb64(parts[0]));
            for (int i = 1; i < parts.Length; i++)
            {
                int sep = parts[i].IndexOf(':');
                if (sep < 0) continue;
                change.Set(Unb64(parts[i].Substring(0, sep)), Unb64(parts[i].Substring(sep + 1)));
            }
            return change;
        }

        private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));
        private static string Unb64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    public enum TransactionPhase { Intent = 1, Committed = 2 }

    /// <summary>One journal record (data-model.md record envelope, Tracer-1 subset).</summary>
    public sealed class JournalRecord
    {
        public string AccountOperationId = string.Empty;
        public string TransactionId = string.Empty;
        public TransactionPhase Phase;
        public string BindingDigest = string.Empty;
        public string PayloadDigest = string.Empty;
        public string ResultCode = string.Empty;
        public long OccurredAt;
        public readonly List<JournalChange> Changes = new List<JournalChange>();

        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append("PAJ|")
              .Append(B64(AccountOperationId)).Append('|')
              .Append(B64(TransactionId)).Append('|')
              .Append(((int)Phase).ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(B64(BindingDigest)).Append('|')
              .Append(B64(PayloadDigest)).Append('|')
              .Append(B64(ResultCode)).Append('|')
              .Append(OccurredAt.ToString(CultureInfo.InvariantCulture)).Append('|')
              .Append(B64(string.Join(",", Changes.Select(c => c.Encode()))));
            return sb.ToString();
        }

        public static JournalRecord? Decode(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 9 || parts[0] != "PAJ") return null;
            var rec = new JournalRecord
            {
                AccountOperationId = Unb64(parts[1]),
                TransactionId = Unb64(parts[2]),
                Phase = (TransactionPhase)int.Parse(parts[3], CultureInfo.InvariantCulture),
                BindingDigest = Unb64(parts[4]),
                PayloadDigest = Unb64(parts[5]),
                ResultCode = Unb64(parts[6]),
                OccurredAt = long.Parse(parts[7], CultureInfo.InvariantCulture),
            };
            string changesBlob = Unb64(parts[8]);
            if (changesBlob.Length > 0)
                foreach (var c in changesBlob.Split(','))
                    rec.Changes.Add(JournalChange.Decode(c));
            return rec;
        }

        private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));
        private static string Unb64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }

    /// <summary>Injects real process death after a durable boundary for torn-tail/recovery tests.</summary>
    public interface IAccountCrashInjector { void AfterPhase(TransactionPhase phase); }
    public sealed class NoAccountCrash : IAccountCrashInjector
    {
        public static readonly NoAccountCrash Instance = new NoAccountCrash();
        public void AfterPhase(TransactionPhase phase) { }
    }

    /// <summary>The durable journal + boot-rehydrated projections/indexes. Construction (== boot) replays
    /// the journal, quarantines any torn tail and any Intent-without-Committed transaction, and builds
    /// the credential/account/allowlist indexes BEFORE the caller opens admission.</summary>
    public sealed class PilotAccountStore
    {
        private readonly string _journalPath;

        private readonly Dictionary<string, PilotAccountProjection> _accounts = new Dictionary<string, PilotAccountProjection>(StringComparer.Ordinal);
        private readonly Dictionary<string, CredentialBindingProjection> _credentials = new Dictionary<string, CredentialBindingProjection>(StringComparer.Ordinal);
        private readonly Dictionary<string, AllowlistEntryProjection> _allowlist = new Dictionary<string, AllowlistEntryProjection>(StringComparer.Ordinal);
        private readonly Dictionary<string, PilotCharacterProjection> _characters = new Dictionary<string, PilotCharacterProjection>(StringComparer.Ordinal);

        // IAP-012 Tracer 4 — pilot lifecycle, artifact catalog, and retention-hold projections.
        private readonly Dictionary<string, PilotLifecycleProjection> _pilots = new Dictionary<string, PilotLifecycleProjection>(StringComparer.Ordinal);
        private readonly Dictionary<string, PilotDataArtifactProjection> _artifacts = new Dictionary<string, PilotDataArtifactProjection>(StringComparer.Ordinal);
        private readonly Dictionary<string, RetentionHoldProjection> _holds = new Dictionary<string, RetentionHoldProjection>(StringComparer.Ordinal);

        // Derived credential lookup index: (providerNs|backendIssuer|keyVersion|hmacHex) -> credentialBindingId (active only).
        private readonly Dictionary<string, string> _credentialIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        // Derived allowlist lookup index: same key shape -> allowlistEntryId (active only).
        private readonly Dictionary<string, string> _allowlistIndex = new Dictionary<string, string>(StringComparer.Ordinal);
        // Derived profile lookup index: (accountId|keyVersion|profileHmacHex) -> characterId (active only).
        // Account-scoped by construction: the profile HMAC input already includes the AccountId, and the
        // index key repeats it, so the same s_playerID under a different account cannot resolve this
        // character (data-model.md Derived index 2 "ProfileLookupIndex"; AT-AIP-CROSS-ACCOUNT-BLOCK).
        private readonly Dictionary<string, string> _profileIndex = new Dictionary<string, string>(StringComparer.Ordinal);

        // Terminal committed transactions by operationId (idempotency): binding+payload digest and result.
        private readonly Dictionary<string, (string binding, string payload, string result)> _committedOps =
            new Dictionary<string, (string, string, string)>(StringComparer.Ordinal);

        public long QuarantinedTailBytes { get; private set; }
        public int QuarantinedIntentTransactions { get; private set; }

        public PilotAccountStore(string journalPath)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            RehydrateFromJournal();
        }

        public string JournalPath => _journalPath;

        // ---- Read projections (bounded, indexed) ----

        public bool TryGetAccount(PilotAccountId id, out PilotAccountProjection account) =>
            _accounts.TryGetValue(id.Value, out account!);

        public bool TryGetCredential(CredentialBindingId id, out CredentialBindingProjection cred) =>
            _credentials.TryGetValue(id.Value, out cred!);

        public bool TryGetAllowlistEntry(AllowlistEntryId id, out AllowlistEntryProjection entry) =>
            _allowlist.TryGetValue(id.Value, out entry!);

        public bool TryGetCharacter(PilotCharacterId id, out PilotCharacterProjection character) =>
            _characters.TryGetValue(id.Value, out character!);

        public int CharacterCount => _characters.Count;

        /// <summary>Indexed, account-scoped profile lookup (NO journal scan). Returns the active
        /// character bound to this account by the given profile HMAC, or false. The account id is part
        /// of both the HMAC input and the index key, so a foreign account's identical s_playerID can
        /// never resolve here (data-model.md Aggregate 3 invariant; AT-AIP-CROSS-ACCOUNT-BLOCK).</summary>
        public bool TryLookupCharacter(PilotAccountId accountId, SubjectLookupHmac profileHmac, out PilotCharacterProjection character)
        {
            character = null!;
            if (_profileIndex.TryGetValue(ProfileIndexKey(accountId, profileHmac), out var id) &&
                _characters.TryGetValue(id, out var c) && c.Status == CharacterStatus.Active)
            {
                character = c;
                return true;
            }
            return false;
        }

        public int AccountCount => _accounts.Count;

        // ---- IAP-012 Tracer 4 read projections (bounded, indexed) ----

        public bool TryGetPilot(PilotId id, out PilotLifecycleProjection pilot) =>
            _pilots.TryGetValue(id.Value, out pilot!);

        public bool TryGetArtifact(DataArtifactId id, out PilotDataArtifactProjection artifact) =>
            _artifacts.TryGetValue(id.Value, out artifact!);

        public bool TryGetRetentionHold(RetentionHoldId id, out RetentionHoldProjection hold) =>
            _holds.TryGetValue(id.Value, out hold!);

        public IReadOnlyCollection<PilotDataArtifactProjection> Artifacts => _artifacts.Values;
        public IReadOnlyCollection<RetentionHoldProjection> RetentionHolds => _holds.Values;
        public IReadOnlyCollection<PilotLifecycleProjection> Pilots => _pilots.Values;

        /// <summary>True when a cataloged, non-purged WorldSave artifact exists at the given storage
        /// locator. Admission fails closed when the active world fixture is NOT cataloged
        /// (AT-AIP-ARTIFACT-CATALOG; contracts §Performance and failure).</summary>
        public bool IsWorldFixtureCataloged(string storageLocator)
        {
            foreach (var a in _artifacts.Values)
                if (a.ArtifactType == PilotArtifactType.WorldSave &&
                    a.Status != ArtifactStatus.Purged &&
                    string.Equals(a.StorageLocator, storageLocator, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>Indexed credential lookup (NO journal scan). Returns the active binding whose HMAC
        /// matches under the given version, or false.</summary>
        public bool TryLookupCredential(SubjectLookupHmac hmac, string providerNs, string backendIssuer, out CredentialBindingProjection cred)
        {
            cred = null!;
            if (_credentialIndex.TryGetValue(IndexKey(providerNs, backendIssuer, hmac), out var id) &&
                _credentials.TryGetValue(id, out var c) && c.Status == CredentialStatus.Active)
            {
                cred = c;
                return true;
            }
            return false;
        }

        /// <summary>Indexed allowlist lookup (NO journal scan). Active entries only.</summary>
        public bool TryLookupAllowlist(SubjectLookupHmac hmac, string providerNs, string backendIssuer, out AllowlistEntryProjection entry)
        {
            entry = null!;
            if (_allowlistIndex.TryGetValue(IndexKey(providerNs, backendIssuer, hmac), out var id) &&
                _allowlist.TryGetValue(id, out var e) && e.Status == AllowlistStatus.Active)
            {
                entry = e;
                return true;
            }
            return false;
        }

        /// <summary>Live version census across allowlist + credential projections (AT-AIP-KEY-VERSION-CENSUS).
        /// Only live (Active/Superseded-not-purged) records that still carry key material count.</summary>
        public KeyVersionCensus RunCensus()
        {
            var census = new KeyVersionCensus();
            foreach (var e in _allowlist.Values)
                if (e.Status == AllowlistStatus.Active) census.CountAllowlist(e.Hmac.KeyVersion);
            foreach (var c in _credentials.Values)
                if (c.Status == CredentialStatus.Active) census.CountCredential(c.Hmac.KeyVersion);
            foreach (var ch in _characters.Values)
                if (ch.Status == CharacterStatus.Active) census.CountCredential(ch.ProfileHmac.KeyVersion);
            return census;
        }

        /// <summary>Whether a previous key version may be retired/rotated away: only when the live census
        /// for that version is zero (AT-AIP-KEY-RETIREMENT-GATE). A nonzero count blocks retirement.</summary>
        public bool MayRetireKeyVersion(LookupKeyVersion version) => RunCensus().TotalForVersion(version) == 0;

        // ---- Durable commit ----

        /// <summary>Look up a committed operation's recorded result for idempotent replay. Returns true
        /// and the recorded (binding,payload,result) if the operationId already committed.</summary>
        public bool TryGetCommittedOp(string operationId, out string binding, out string payload, out string result)
        {
            if (_committedOps.TryGetValue(operationId, out var rec))
            {
                binding = rec.binding; payload = rec.payload; result = rec.result;
                return true;
            }
            binding = payload = result = string.Empty;
            return false;
        }

        /// <summary>Commit one transaction: write Intent then Committed (each fsync'd). Only after the
        /// Committed record is durable do the changes project. A crash between the two leaves an Intent
        /// that boot replay quarantines, so no partial account survives. Returns the terminal record.</summary>
        public JournalRecord Commit(string operationId, string transactionId, string bindingDigest,
            string payloadDigest, string resultCode, long occurredAt, IEnumerable<JournalChange> changes,
            IAccountCrashInjector? crash = null)
        {
            crash = crash ?? NoAccountCrash.Instance;
            var changeList = changes.ToList();

            var intent = new JournalRecord
            {
                AccountOperationId = operationId, TransactionId = transactionId, Phase = TransactionPhase.Intent,
                BindingDigest = bindingDigest, PayloadDigest = payloadDigest, ResultCode = "Intent", OccurredAt = occurredAt,
            };
            intent.Changes.AddRange(changeList);
            Append(intent.Encode());
            crash.AfterPhase(TransactionPhase.Intent);

            var committed = new JournalRecord
            {
                AccountOperationId = operationId, TransactionId = transactionId, Phase = TransactionPhase.Committed,
                BindingDigest = bindingDigest, PayloadDigest = payloadDigest, ResultCode = resultCode, OccurredAt = occurredAt,
            };
            committed.Changes.AddRange(changeList);
            Append(committed.Encode());
            crash.AfterPhase(TransactionPhase.Committed);

            ProjectCommitted(committed);
            _committedOps[operationId] = (bindingDigest, payloadDigest, resultCode);
            return committed;
        }

        // ---- Journal replay / recovery ----

        private void RehydrateFromJournal()
        {
            var records = ReadDurable(out long torn);
            QuarantinedTailBytes = torn;

            // Group by transactionId; a transaction projects only if it has a durable Committed record.
            var committedByTxn = new List<JournalRecord>();
            var intentTxns = new HashSet<string>(StringComparer.Ordinal);
            var committedTxns = new HashSet<string>(StringComparer.Ordinal);

            foreach (var rec in records)
            {
                if (rec.Phase == TransactionPhase.Intent) intentTxns.Add(rec.TransactionId);
                else if (rec.Phase == TransactionPhase.Committed)
                {
                    committedTxns.Add(rec.TransactionId);
                    committedByTxn.Add(rec);
                }
            }

            QuarantinedIntentTransactions = intentTxns.Count(t => !committedTxns.Contains(t));

            foreach (var rec in committedByTxn)
            {
                ProjectCommitted(rec);
                _committedOps[rec.AccountOperationId] = (rec.BindingDigest, rec.PayloadDigest, rec.ResultCode);
            }
        }

        private void ProjectCommitted(JournalRecord rec)
        {
            foreach (var ch in rec.Changes) ApplyChange(ch);
        }

        private void ApplyChange(JournalChange ch)
        {
            switch (ch.Kind)
            {
                case "acct":
                {
                    var acct = new PilotAccountProjection
                    {
                        AccountId = new PilotAccountId(ch.Get("accountId")),
                        Status = ParseAccountStatus(ch.Get("status")),
                        Revision = ch.GetLong("revision"),
                        NoticeVersion = ch.Get("noticeVersion"),
                        NoticeAcknowledgedAt = ch.GetLong("noticeAckAt"),
                        RetentionPolicyVersion = ch.Get("retentionPolicyVersion"),
                    };
                    _accounts[acct.AccountId.Value] = acct;
                    break;
                }
                case "acct-add-cred":
                {
                    if (_accounts.TryGetValue(ch.Get("accountId"), out var acct))
                    {
                        var cid = new CredentialBindingId(ch.Get("credentialBindingId"));
                        if (!acct.CredentialBindingIds.Contains(cid)) acct.CredentialBindingIds.Add(cid);
                        acct.Revision = ch.GetLong("revision");
                    }
                    break;
                }
                case "acct-add-char":
                {
                    if (_accounts.TryGetValue(ch.Get("accountId"), out var acct))
                    {
                        var chid = new PilotCharacterId(ch.Get("characterId"));
                        if (!acct.CharacterIds.Contains(chid)) acct.CharacterIds.Add(chid);
                        acct.Revision = ch.GetLong("revision");
                    }
                    break;
                }
                case "acct-status":
                {
                    if (_accounts.TryGetValue(ch.Get("accountId"), out var acct))
                    {
                        acct.Status = ParseAccountStatus(ch.Get("status"));
                        acct.Revision = ch.GetLong("revision");
                    }
                    break;
                }
                case "cred":
                {
                    var cred = new CredentialBindingProjection
                    {
                        CredentialBindingId = new CredentialBindingId(ch.Get("credentialBindingId")),
                        AllowlistEntryId = new AllowlistEntryId(ch.Get("allowlistEntryId")),
                        AccountId = new PilotAccountId(ch.Get("accountId")),
                        ProviderNamespace = ch.Get("providerNs"),
                        BackendIssuer = ch.Get("backendIssuer"),
                        Hmac = new SubjectLookupHmac(ch.Get("hmac"), new LookupKeyVersion(ch.Get("keyVersion"))),
                        Status = ParseCredentialStatus(ch.Get("status")),
                        Revision = ch.GetLong("revision"),
                    };
                    _credentials[cred.CredentialBindingId.Value] = cred;
                    ReindexCredential(cred);
                    break;
                }
                case "cred-status":
                {
                    if (_credentials.TryGetValue(ch.Get("credentialBindingId"), out var cred))
                    {
                        RemoveCredentialIndex(cred);
                        cred.Status = ParseCredentialStatus(ch.Get("status"));
                        cred.Revision = ch.GetLong("revision");
                        ReindexCredential(cred);
                    }
                    break;
                }
                case "char":
                {
                    var character = new PilotCharacterProjection
                    {
                        CharacterId = new PilotCharacterId(ch.Get("characterId")),
                        AccountId = new PilotAccountId(ch.Get("accountId")),
                        ProfileHmac = new SubjectLookupHmac(ch.Get("hmac"), new LookupKeyVersion(ch.Get("keyVersion"))),
                        Status = ParseCharacterStatus(ch.Get("status")),
                        Revision = ch.GetLong("revision"),
                    };
                    _characters[character.CharacterId.Value] = character;
                    ReindexCharacter(character);
                    break;
                }
                case "char-status":
                {
                    if (_characters.TryGetValue(ch.Get("characterId"), out var character))
                    {
                        RemoveCharacterIndex(character);
                        character.Status = ParseCharacterStatus(ch.Get("status"));
                        character.Revision = ch.GetLong("revision");
                        ReindexCharacter(character);
                    }
                    break;
                }
                case "char-rekey":
                {
                    // In-place profile HMAC re-key: drop the old index key, write the current HMAC/version,
                    // increment revision, RETAIN the same CharacterId (data-model.md Aggregate 3 invariant;
                    // AT-AIP-PROFILE-PREVIOUS-KEY-REKEY). No superseded character record is created.
                    if (_characters.TryGetValue(ch.Get("characterId"), out var character))
                    {
                        RemoveCharacterIndex(character);
                        character.ProfileHmac = new SubjectLookupHmac(ch.Get("hmac"), new LookupKeyVersion(ch.Get("keyVersion")));
                        character.Revision = ch.GetLong("revision");
                        ReindexCharacter(character);
                    }
                    break;
                }
                case "allow":
                {
                    var entry = new AllowlistEntryProjection
                    {
                        AllowlistEntryId = new AllowlistEntryId(ch.Get("allowlistEntryId")),
                        ProviderNamespace = ch.Get("providerNs"),
                        BackendIssuer = ch.Get("backendIssuer"),
                        Hmac = new SubjectLookupHmac(ch.Get("hmac"), new LookupKeyVersion(ch.Get("keyVersion"))),
                        Status = ParseAllowlistStatus(ch.Get("status")),
                        Revision = ch.GetLong("revision"),
                        NoticeVersion = ch.Get("noticeVersion"),
                        NoticeAcknowledgedAt = ch.GetLong("noticeAckAt"),
                    };
                    _allowlist[entry.AllowlistEntryId.Value] = entry;
                    ReindexAllowlist(entry);
                    break;
                }
                case "allow-status":
                {
                    if (_allowlist.TryGetValue(ch.Get("allowlistEntryId"), out var entry))
                    {
                        RemoveAllowlistIndex(entry);
                        entry.Status = ParseAllowlistStatus(ch.Get("status"));
                        entry.Revision = ch.GetLong("revision");
                        if (ch.Get("linkedAllowlistEntryId").Length > 0) { /* linkage note; no-op for tracer 1 */ }
                        ReindexAllowlist(entry);
                    }
                    break;
                }
                // ---- IAP-012 Tracer 4 catalog / lifecycle / hold projections ----
                case "pilot":
                {
                    var pilot = new PilotLifecycleProjection
                    {
                        PilotId = new PilotId(ch.Get("pilotId")),
                        Status = ParsePilotStatus(ch.Get("status")),
                        Revision = ch.GetLong("revision"),
                        StartedAt = ch.GetLong("startedAt"),
                        EndedAt = ch.GetLong("endedAt"),
                        PurgeDueAt = ch.GetLong("purgeDueAt"),
                        PolicyVersion = ch.Get("policyVersion"),
                    };
                    _pilots[pilot.PilotId.Value] = pilot;
                    break;
                }
                case "pilot-status":
                {
                    if (_pilots.TryGetValue(ch.Get("pilotId"), out var pilot))
                    {
                        pilot.Status = ParsePilotStatus(ch.Get("status"));
                        pilot.Revision = ch.GetLong("revision");
                        if (ch.Get("endedAt").Length > 0) pilot.EndedAt = ch.GetLong("endedAt");
                        if (ch.Get("purgeDueAt").Length > 0) pilot.PurgeDueAt = ch.GetLong("purgeDueAt");
                        if (ch.Get("policyVersion").Length > 0) pilot.PolicyVersion = ch.Get("policyVersion");
                    }
                    break;
                }
                case "artifact":
                {
                    var art = new PilotDataArtifactProjection
                    {
                        DataArtifactId = new DataArtifactId(ch.Get("dataArtifactId")),
                        ArtifactType = ParseArtifactType(ch.Get("artifactType")),
                        StorageLocator = ch.Get("storageLocator"),
                        CreatedAt = ch.GetLong("createdAt"),
                        ExpiresAt = ch.GetLong("expiresAt"),
                        Status = ParseArtifactStatus(ch.Get("status")),
                        Revision = ch.GetLong("revision"),
                        PurgeEvidenceDigest = ch.Get("purgeEvidenceDigest"),
                    };
                    _artifacts[art.DataArtifactId.Value] = art;
                    break;
                }
                case "artifact-status":
                {
                    if (_artifacts.TryGetValue(ch.Get("dataArtifactId"), out var art))
                    {
                        art.Status = ParseArtifactStatus(ch.Get("status"));
                        art.Revision = ch.GetLong("revision");
                        if (ch.Get("purgeEvidenceDigest").Length > 0) art.PurgeEvidenceDigest = ch.Get("purgeEvidenceDigest");
                    }
                    break;
                }
                case "hold":
                {
                    var hold = new RetentionHoldProjection
                    {
                        RetentionHoldId = new RetentionHoldId(ch.Get("retentionHoldId")),
                        Scope = ch.Get("scope"),
                        Reason = ch.Get("reason"),
                        Revision = ch.GetLong("revision"),
                        CreatedAt = ch.GetLong("createdAt"),
                        ExpiresAt = ch.GetLong("expiresAt"),
                        Status = ParseHoldStatus(ch.Get("status")),
                    };
                    _holds[hold.RetentionHoldId.Value] = hold;
                    break;
                }
                case "hold-status":
                {
                    if (_holds.TryGetValue(ch.Get("retentionHoldId"), out var hold))
                    {
                        hold.Status = ParseHoldStatus(ch.Get("status"));
                        hold.Revision = ch.GetLong("revision");
                    }
                    break;
                }
            }
        }

        private void ReindexCredential(CredentialBindingProjection cred)
        {
            if (cred.Status == CredentialStatus.Active)
                _credentialIndex[IndexKey(cred.ProviderNamespace, cred.BackendIssuer, cred.Hmac)] = cred.CredentialBindingId.Value;
        }

        private void RemoveCredentialIndex(CredentialBindingProjection cred)
        {
            var key = IndexKey(cred.ProviderNamespace, cred.BackendIssuer, cred.Hmac);
            if (_credentialIndex.TryGetValue(key, out var id) && string.Equals(id, cred.CredentialBindingId.Value, StringComparison.Ordinal))
                _credentialIndex.Remove(key);
        }

        private void ReindexAllowlist(AllowlistEntryProjection entry)
        {
            if (entry.Status == AllowlistStatus.Active)
                _allowlistIndex[IndexKey(entry.ProviderNamespace, entry.BackendIssuer, entry.Hmac)] = entry.AllowlistEntryId.Value;
        }

        private void RemoveAllowlistIndex(AllowlistEntryProjection entry)
        {
            var key = IndexKey(entry.ProviderNamespace, entry.BackendIssuer, entry.Hmac);
            if (_allowlistIndex.TryGetValue(key, out var id) && string.Equals(id, entry.AllowlistEntryId.Value, StringComparison.Ordinal))
                _allowlistIndex.Remove(key);
        }

        private static string IndexKey(string providerNs, string backendIssuer, SubjectLookupHmac hmac) =>
            providerNs + "|" + backendIssuer + "|" + hmac.KeyVersion.Value + "|" + hmac.Hex;

        private void ReindexCharacter(PilotCharacterProjection character)
        {
            if (character.Status == CharacterStatus.Active)
                _profileIndex[ProfileIndexKey(character.AccountId, character.ProfileHmac)] = character.CharacterId.Value;
        }

        private void RemoveCharacterIndex(PilotCharacterProjection character)
        {
            var key = ProfileIndexKey(character.AccountId, character.ProfileHmac);
            if (_profileIndex.TryGetValue(key, out var id) && string.Equals(id, character.CharacterId.Value, StringComparison.Ordinal))
                _profileIndex.Remove(key);
        }

        private static string ProfileIndexKey(PilotAccountId accountId, SubjectLookupHmac profileHmac) =>
            accountId.Value + "|" + profileHmac.KeyVersion.Value + "|" + profileHmac.Hex;

        private static CharacterStatus ParseCharacterStatus(string s) =>
            Enum.TryParse<CharacterStatus>(s, out var v) ? v : CharacterStatus.Active;

        private static PilotAccountStatus ParseAccountStatus(string s) =>
            Enum.TryParse<PilotAccountStatus>(s, out var v) ? v : PilotAccountStatus.Active;
        private static CredentialStatus ParseCredentialStatus(string s) =>
            Enum.TryParse<CredentialStatus>(s, out var v) ? v : CredentialStatus.Active;
        private static AllowlistStatus ParseAllowlistStatus(string s) =>
            Enum.TryParse<AllowlistStatus>(s, out var v) ? v : AllowlistStatus.Active;
        private static PilotLifecycleStatus ParsePilotStatus(string s) =>
            Enum.TryParse<PilotLifecycleStatus>(s, out var v) ? v : PilotLifecycleStatus.Active;
        private static ArtifactStatus ParseArtifactStatus(string s) =>
            Enum.TryParse<ArtifactStatus>(s, out var v) ? v : ArtifactStatus.Active;
        private static PilotArtifactType ParseArtifactType(string s) =>
            Enum.TryParse<PilotArtifactType>(s, out var v) ? v : PilotArtifactType.WorldSave;
        private static RetentionHoldStatus ParseHoldStatus(string s) =>
            Enum.TryParse<RetentionHoldStatus>(s, out var v) ? v : RetentionHoldStatus.Active;

        // ---- Framed append-only journal (shared discipline with OperationReceiptStore) ----

        private void Append(string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            using (var fs = new FileStream(_journalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(payload.Length);
                bw.Write(Crc32(payload));
                bw.Write(payload);
                bw.Flush();
                fs.Flush(true);
            }
        }

        /// <summary>Read only fully-durable, CRC-valid records; a torn tail is reported and ignored.</summary>
        public List<JournalRecord> ReadDurable(out long tornTailBytes)
        {
            tornTailBytes = 0;
            var results = new List<JournalRecord>();
            if (!File.Exists(_journalPath)) return results;

            using (var fs = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                long length = fs.Length;
                while (true)
                {
                    long recordStart = fs.Position;
                    if (recordStart + 8 > length) { tornTailBytes = length - recordStart; break; }
                    int payloadLen = br.ReadInt32();
                    uint crc = br.ReadUInt32();
                    if (payloadLen < 0 || fs.Position + payloadLen > length) { tornTailBytes = length - recordStart; break; }
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen || Crc32(payload) != crc) { tornTailBytes = length - recordStart; break; }
                    var rec = JournalRecord.Decode(Encoding.UTF8.GetString(payload));
                    if (rec != null) results.Add(rec);
                }
            }
            return results;
        }

        // CRC32 (IEEE 802.3), same polynomial/discipline as OperationReceiptStore.
        private static readonly uint[] Crc32Table = BuildCrc32Table();
        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            const uint poly = 0xEDB88320u;
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? poly ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }
        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data) crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        public static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? string.Empty));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }
    }
}
