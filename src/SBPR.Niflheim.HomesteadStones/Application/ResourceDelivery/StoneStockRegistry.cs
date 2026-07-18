using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery
{
    // RD-T008 (Tracer 3) — the durable coordinator for the first real Stone Stock vertical slice
    // (spec RD-013/RD-014/RD-017/RD-018; contracts §SelectDonationMenu / §SubmitUpkeepDonation /
    // §Grant/RevokeStockWithdrawalPermission; data-model Aggregates 3 & 4). Named acceptance:
    // AT-RD-013, AT-RD-014, AT-RD-017, AT-RD-018.
    //
    // WHAT THIS OWNS
    //   ONE durable, server-authoritative virtual Stone Stockpile plus the Donation Menu selection and
    //   the canonical delegated-withdrawal permission records — all recovered from one framed,
    //   CRC-checked, fsync'd, append-only journal keyed by operationId, exactly like
    //   AccountStoneParticipationRegistry / StockTransactionHarness. The in-memory projections (menu,
    //   Stock balances, per-grantee permission) are pure functions of replaying the committed events in
    //   journal order, so a restart reconstructs the EXACT state.
    //
    //   * MENU (spec RD-018): owner-role selection of exactly two distinct current options from the
    //     authored candidate pool, or the deterministic authored default pair materialized once when
    //     upkeep is needed before a valid selection. The Level-2 Humble pool is `20 Wood`, `20 Stone`,
    //     `10 Wood + 10 Stone`, default `20 Wood` + `20 Stone`.
    //   * DONATION (spec RD-014): a valid donation resolves the exact authored vector server-side (the
    //     client submits NO quantities), debits the player's server-observed inventory, and credits the
    //     one Stockpile with donation provenance — exactly once. Invalid option, missing items, full
    //     capacity, a current pending generated delivery's priority, or a stale Stock revision all
    //     change nothing.
    //   * PERMISSION (spec RD-017): one canonical `(StoneId, grantee AccountId)` record with
    //     generation/state history and at most one active grant; owner-role grant/revoke, duplicate/
    //     stale-race denial, generation-incrementing regrant, complete revocation, and non-transitive
    //     authority. No expiry in this slice.
    //
    // AUTHORITY: the "active Bond carrying the server-authored owner role", the caller's active
    // relationship, and the qualifying-Connection facts are resolved UPSTREAM and handed in as resolved
    // booleans (this coordinator never reads a relationship graph). It enforces that a menu selection or
    // a grant/revoke without owner-role authority rejects, and that only an active Bond or an active
    // canonical permission authorizes withdrawal.
    //
    // SCOPE: Tracer 3 moves player items into Stock (donation) and owns the permission lifecycle.
    // Generated-delivery deposit and the withdrawal item-move are later tracers; this coordinator models
    // the pending-delivery capacity reservation (so donations correctly reject `PendingDeliveryPriority`)
    // and the withdrawal AUTHORITY predicate that Tracer 5 will gate on, but performs no generated
    // deposit or withdrawal item transfer itself.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, CRC32 — all
    // present in .NET Framework 4.8. Engine-free; link-compiles into the net8 test project.

    public enum StockCommandOutcome
    {
        Applied,
        Replayed,
        OperationConflict
    }

    /// <summary>Provenance kind for one recorded Stockpile mutation (data-model Aggregate 4 §Provenance).</summary>
    public enum StockProvenanceKind
    {
        Donation = 1,
        GeneratedDelivery = 2,
        Withdrawal = 3
    }

    public enum MenuSelectionOutcome
    {
        Applied,
        Replayed,
        OperationConflict,
        OwnerRoleRequired,
        OptionsNotDistinct,
        OptionNotInPool,
        WrongLevel,
        StalePoolVersion,
        StaleMenuRevision,
        AlreadyLocked
    }

    public readonly struct MenuSelectionResult
    {
        public MenuSelectionResult(MenuSelectionOutcome outcome, DonationMenuSelection selection, long menuRevision)
        {
            Outcome = outcome;
            Selection = selection;
            MenuRevision = menuRevision;
        }

        public MenuSelectionOutcome Outcome { get; }
        public DonationMenuSelection Selection { get; }
        public long MenuRevision { get; }

        public bool Accepted => Outcome == MenuSelectionOutcome.Applied || Outcome == MenuSelectionOutcome.Replayed;
    }

    public enum DonationOutcome
    {
        Applied,
        Replayed,
        OperationConflict,
        MenuNotSelected,          // DonationMenuNotSelected — no valid selection and no valid default content
        OptionNotAccepted,        // DonationOptionNotAccepted — option is not one of the two selected
        ItemsMissing,             // DonationItemsMissing — player inventory lacks the complete vector
        CapacityExceeded,         // StoneStockCapacityExceeded — the complete deposit vector cannot fit
        PendingDeliveryPriority,  // PendingDeliveryPriority — capacity is reserved for a pending generated bundle
        StaleStockRevision,       // StaleStockRevision
        StaleInventoryRevision    // player inventory revision no longer current
    }

    public readonly struct DonationResult
    {
        public DonationResult(DonationOutcome outcome, string resultCode, string receiptId,
            long stockRevision, long inventoryRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            ReceiptId = receiptId ?? string.Empty;
            StockRevision = stockRevision;
            InventoryRevision = inventoryRevision;
        }

        public DonationOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public long StockRevision { get; }
        public long InventoryRevision { get; }

        public bool Committed => Outcome == DonationOutcome.Applied || Outcome == DonationOutcome.Replayed;
    }

    public enum PermissionCommandOutcome
    {
        Applied,
        Replayed,
        OperationConflict,
        OwnerRoleRequired,         // caller is not the active owner-role Bond
        AlreadyActive,             // StockPermissionAlreadyActive
        NotActive,                 // revoke against an absent/already-revoked record
        StalePermissionRevision    // expected canonical-permission revision mismatch
    }

    public readonly struct PermissionResult
    {
        public PermissionResult(PermissionCommandOutcome outcome, int generation,
            WithdrawalPermissionState state, long permissionRevision)
        {
            Outcome = outcome;
            Generation = generation;
            State = state;
            PermissionRevision = permissionRevision;
        }

        public PermissionCommandOutcome Outcome { get; }
        public int Generation { get; }
        public WithdrawalPermissionState State { get; }
        public long PermissionRevision { get; }

        public bool Accepted => Outcome == PermissionCommandOutcome.Applied || Outcome == PermissionCommandOutcome.Replayed;
    }

    public sealed class StoneStockRegistry
    {
        private const string RecMenu = "SMNU";
        private const string RecDefault = "SDEF";
        private const string RecDonation = "SDON";
        private const string RecGrant = "SGRT";
        private const string RecRevoke = "SRVK";
        private const string RecPending = "SPND"; // set/clear a pending-delivery capacity reservation (test/adapter seam)

        private readonly string _journalPath;
        private readonly StoneId _stoneId;
        private readonly DonationCandidatePool _pool;
        private readonly CapacityPolicy _capacityPolicy;
        private readonly IReadOnlyDictionary<string, long> _openingStock;
        private readonly IReadOnlyDictionary<string, long> _openingInventory;
        private readonly CapacityPolicy _playerPolicy;

        // Idempotency: committed op binding + recorded terminal result string (verbatim replay).
        private readonly Dictionary<string, string> _committedOps = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _resultByOp = new Dictionary<string, string>(StringComparer.Ordinal);

        // Menu projection.
        private DonationMenuSelection _menu = DonationMenuSelection.None;
        private long _menuRevision;

        // Stock projection: opening + committed donation deltas.
        private readonly Dictionary<string, long> _stock = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _inventory = new Dictionary<string, long>(StringComparer.Ordinal);
        private long _stockRevision;
        private long _inventoryRevision;

        // Provenance/audit: bounded ordered per-operation records (data-model Aggregate 4 §Provenance).
        private readonly List<StockProvenanceEntry> _provenance = new List<StockProvenanceEntry>();

        // Per-grantee canonical permission projection + a shared revision that serializes owner-role changes.
        private readonly Dictionary<string, StockWithdrawalPermission> _permissions =
            new Dictionary<string, StockWithdrawalPermission>(StringComparer.Ordinal);
        private long _permissionRevision;

        // A current pending generated-delivery capacity reservation (contracts §SubmitUpkeepDonation
        // "A current pending generated delivery has first priority on capacity").
        private bool _pendingDelivery;

        public StoneStockRegistry(
            string journalPath,
            StoneId stoneId,
            DonationCandidatePool pool,
            CapacityPolicy? capacityPolicy = null,
            IReadOnlyDictionary<string, long>? openingStock = null,
            IReadOnlyDictionary<string, long>? openingInventory = null,
            CapacityPolicy? playerPolicy = null)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _stoneId = stoneId;
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _capacityPolicy = capacityPolicy ?? CapacityPolicy.Level2Stock;
            _openingStock = openingStock ?? new Dictionary<string, long>();
            _openingInventory = openingInventory ?? new Dictionary<string, long>();
            _playerPolicy = playerPolicy ?? CapacityPolicy.PlayerCarry;
            Rehydrate();
        }

        public string JournalPath => _journalPath;
        public StoneId StoneId => _stoneId;
        public CapacityPolicy CapacityPolicy => _capacityPolicy;

        // ---- Read projections ----

        public DonationMenuSelection CurrentMenu() => _menu;
        public long MenuRevision => _menuRevision;
        public long StockRevision => _stockRevision;
        public long InventoryRevision => _inventoryRevision;
        public bool HasPendingDelivery => _pendingDelivery;

        public IReadOnlyDictionary<string, long> CurrentStock() => CopyLedger(_stock);
        public IReadOnlyDictionary<string, long> CurrentInventory() => CopyLedger(_inventory);
        public IReadOnlyList<StockProvenanceEntry> Provenance => _provenance;

        private static Dictionary<string, long> CopyLedger(IReadOnlyDictionary<string, long> src)
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in src) d[kv.Key] = kv.Value;
            return d;
        }

        public StockWithdrawalPermission PermissionFor(AccountId grantee) =>
            _permissions.TryGetValue(grantee.Value, out var p) ? p : StockWithdrawalPermission.None;
        public long PermissionRevision => _permissionRevision;

        /// <summary>Withdrawal authority predicate (contracts §WithdrawStoneStock: "current Bond or
        /// explicit permission"). Delegation is non-transitive: a grantee's active permission authorizes
        /// ONLY withdrawal, never granting to a third party. An active owner/holder Bond is authorized
        /// directly (resolved upstream).</summary>
        public bool IsWithdrawalAuthorized(AccountId account, bool hasActiveBond) =>
            hasActiveBond || PermissionFor(account).IsActive;

        // ---- Menu selection (contracts §SelectDonationMenu) ----

        public MenuSelectionResult SelectDonationMenu(
            string operationId, int requestedLevel, int requestedPoolVersion,
            string optionAId, int optionAVersion, string optionBId, int optionBVersion,
            bool hasOwnerRoleBond, long expectedMenuRevision)
        {
            string binding = Digest(string.Join("|", new[]
            {
                RecMenu, requestedLevel.ToString(CultureInfo.InvariantCulture),
                requestedPoolVersion.ToString(CultureInfo.InvariantCulture),
                optionAId ?? string.Empty, optionAVersion.ToString(CultureInfo.InvariantCulture),
                optionBId ?? string.Empty, optionBVersion.ToString(CultureInfo.InvariantCulture)
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new MenuSelectionResult(MenuSelectionOutcome.OperationConflict, _menu, _menuRevision);
                var rr = new SnapshotReader(recorded);
                return new MenuSelectionResult(MenuSelectionOutcome.Replayed, _menu, rr.GetLong("menuRev"));
            }

            // Authority + validity are pure-domain gates; a rejection is NOT a receipt-bearing mutation.
            var resolution = DonationMenuSelection.TrySelect(
                _pool, requestedLevel, requestedPoolVersion,
                optionAId ?? string.Empty, optionAVersion, optionBId ?? string.Empty, optionBVersion,
                hasOwnerRoleBond, out var selection);
            if (resolution != DonationSelectionResolution.Accepted)
                return new MenuSelectionResult(MapSelection(resolution), _menu, _menuRevision);

            // Once locked by an owner-role selection, replacement is only via a later level/menu
            // transition (out of this slice) — a fresh selection at the same level rejects.
            if (_menu.IsSelected && _menu.Provenance == DonationMenuProvenance.OwnerRoleSelection)
                return new MenuSelectionResult(MenuSelectionOutcome.AlreadyLocked, _menu, _menuRevision);

            // Optimistic concurrency: concurrent owner-role selections serialize by expected revision.
            if (expectedMenuRevision != _menuRevision)
                return new MenuSelectionResult(MenuSelectionOutcome.StaleMenuRevision, _menu, _menuRevision);

            long newRev = _menuRevision + 1;
            string result = new SnapshotWriter().PutLong("menuRev", newRev).Build();
            AppendMenu(operationId, binding, selection, newRev, isDefault: false, result);
            return new MenuSelectionResult(MenuSelectionOutcome.Applied, _menu, _menuRevision);
        }

        /// <summary>Materialize the authored default pair once, idempotently, when upkeep is needed
        /// before a valid selection exists (contracts §SelectDonationMenu). Authored content, not a
        /// choice, so it needs no owner-role authority. A no-op when a selection already exists.</summary>
        public MenuSelectionResult MaterializeDefaultIfNeeded(string operationId)
        {
            string binding = Digest(RecDefault + "|" + _stoneId.Value + "|" + _pool.StoneLevel.ToString(CultureInfo.InvariantCulture)
                + "|" + _pool.PoolVersion.ToString(CultureInfo.InvariantCulture));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new MenuSelectionResult(MenuSelectionOutcome.OperationConflict, _menu, _menuRevision);
                var rr = new SnapshotReader(recorded);
                return new MenuSelectionResult(MenuSelectionOutcome.Replayed, _menu, rr.GetLong("menuRev"));
            }

            if (_menu.IsSelected)
            {
                // Already selected (owner-role OR a prior default): record an idempotent no-progress
                // replay under this op id so the "materialize once" contract holds across retries.
                string noop = new SnapshotWriter().PutLong("menuRev", _menuRevision).Build();
                AppendMenu(operationId, binding, _menu, _menuRevision, isDefault: true, noop);
                return new MenuSelectionResult(MenuSelectionOutcome.Replayed, _menu, _menuRevision);
            }

            var selection = DonationMenuSelection.MaterializeDefault(_pool);
            long newRev = _menuRevision + 1;
            string result = new SnapshotWriter().PutLong("menuRev", newRev).Build();
            AppendMenu(operationId, binding, selection, newRev, isDefault: true, result);
            return new MenuSelectionResult(MenuSelectionOutcome.Applied, _menu, _menuRevision);
        }

        // ---- Donation (contracts §SubmitUpkeepDonation) ----

        /// <summary>Submit an upkeep donation of one selected option. The server resolves the exact
        /// authored vector (the caller submits no quantities), debits the player's server-observed
        /// inventory, and credits the one Stockpile with donation provenance — exactly once, or changes
        /// nothing. If no valid selection exists yet, the authored default pair materializes first via
        /// <paramref name="defaultMaterializeOperationId"/> (idempotent).</summary>
        public DonationResult SubmitUpkeepDonation(
            string operationId, string defaultMaterializeOperationId,
            string selectedOptionId, int selectedOptionVersion,
            long expectedStockRevision, long expectedInventoryRevision)
        {
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required");

            string binding = Digest(string.Join("|", new[]
            {
                RecDonation, selectedOptionId ?? string.Empty,
                selectedOptionVersion.ToString(CultureInfo.InvariantCulture)
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new DonationResult(DonationOutcome.OperationConflict, "OperationConflict",
                    string.Empty, _stockRevision, _inventoryRevision);
                var rr = new SnapshotReader(recorded);
                return new DonationResult(DonationOutcome.Replayed, "Applied", rr.GetString("receipt"),
                    rr.GetLong("stockRev"), rr.GetLong("invRev"));
            }

            // Materialize the authored default pair before evaluating a donation if no valid selection
            // exists (contracts §SelectDonationMenu). Idempotent by its own op id.
            if (!_menu.IsSelected)
                MaterializeDefaultIfNeeded(defaultMaterializeOperationId);

            if (!_menu.IsSelected)
                return Reject(DonationOutcome.MenuNotSelected, "DonationMenuNotSelected");

            // Only one of the two SELECTED options may be donated (never an arbitrary pool option).
            if (!_menu.TryResolve(selectedOptionId ?? string.Empty, selectedOptionVersion, out var option))
                return Reject(DonationOutcome.OptionNotAccepted, "DonationOptionNotAccepted");

            // A current pending generated delivery has first priority on capacity: later donations reject.
            if (_pendingDelivery)
                return Reject(DonationOutcome.PendingDeliveryPriority, "PendingDeliveryPriority");

            // Optimistic concurrency on both ledgers.
            if (expectedStockRevision != _stockRevision)
                return new DonationResult(DonationOutcome.StaleStockRevision, "StaleStockRevision",
                    string.Empty, _stockRevision, _inventoryRevision);
            if (expectedInventoryRevision != _inventoryRevision)
                return new DonationResult(DonationOutcome.StaleInventoryRevision, "StaleInventoryRevision",
                    string.Empty, _stockRevision, _inventoryRevision);

            var vector = option.Vector;

            // Server-observed player inventory must contain the FULL authored vector.
            if (!Contains(_inventory, vector))
                return Reject(DonationOutcome.ItemsMissing, "DonationItemsMissing");

            // The complete deposit vector must fit the Stockpile capacity policy.
            if (!Fits(_stock, vector, _capacityPolicy))
                return Reject(DonationOutcome.CapacityExceeded, "StoneStockCapacityExceeded");

            long newStockRev = _stockRevision + 1;
            long newInvRev = _inventoryRevision + 1;
            string receipt = ReceiptId(operationId);
            string result = new SnapshotWriter()
                .Put("receipt", receipt)
                .PutLong("stockRev", newStockRev)
                .PutLong("invRev", newInvRev)
                .Build();
            AppendDonation(operationId, binding, option, receipt, newStockRev, newInvRev, result);
            return new DonationResult(DonationOutcome.Applied, "Applied", receipt, _stockRevision, _inventoryRevision);
        }

        /// <summary>Set/clear a pending generated-delivery capacity reservation (adapter/test seam for the
        /// later generated-delivery tracer). While set, donations reject `PendingDeliveryPriority`.</summary>
        public void SetPendingDelivery(string operationId, bool pending)
        {
            string binding = Digest(RecPending + "|" + (pending ? "1" : "0"));
            if (TryReplay(operationId, binding, out var conflict, out _))
            {
                if (conflict) throw new InvalidOperationException("OperationConflict on pending-delivery marker.");
                return;
            }
            AppendPending(operationId, binding, pending);
        }

        // ---- Permission grant/revoke (contracts §Grant/RevokeStockWithdrawalPermission) ----

        public PermissionResult GrantStockWithdrawalPermission(
            string operationId, AccountId grantee, bool hasOwnerRoleBond, long expectedPermissionRevision)
        {
            string binding = Digest(string.Join("|", new[]
            {
                RecGrant, grantee.Value ?? string.Empty
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new PermissionResult(PermissionCommandOutcome.OperationConflict,
                    PermissionFor(grantee).Generation, PermissionFor(grantee).State, _permissionRevision);
                return DecodePermission(PermissionCommandOutcome.Replayed, recorded);
            }

            if (string.IsNullOrEmpty(grantee.Value))
                throw new ArgumentException("grantee required");

            // Only an active Bond carrying the server-authored owner role may grant (non-transitive:
            // a delegated grantee can never grant onward — it never holds the owner role).
            if (!hasOwnerRoleBond)
                return new PermissionResult(PermissionCommandOutcome.OwnerRoleRequired,
                    PermissionFor(grantee).Generation, PermissionFor(grantee).State, _permissionRevision);

            var current = PermissionFor(grantee);
            var transition = current.TryGrant(out var next);
            if (transition == WithdrawalPermissionTransition.AlreadyActive)
                return new PermissionResult(PermissionCommandOutcome.AlreadyActive,
                    current.Generation, current.State, _permissionRevision);

            // Serialize concurrent owner-role changes by expected revision AFTER the domain gate, so a
            // duplicate active grant reports AlreadyActive (not a revision race) regardless of revision.
            if (expectedPermissionRevision != _permissionRevision)
                return new PermissionResult(PermissionCommandOutcome.StalePermissionRevision,
                    current.Generation, current.State, _permissionRevision);

            long newRev = _permissionRevision + 1;
            string result = EncodePermission(next, newRev);
            AppendPermission(RecGrant, operationId, binding, grantee, next, newRev, result);
            return new PermissionResult(PermissionCommandOutcome.Applied, next.Generation, next.State, _permissionRevision);
        }

        public PermissionResult RevokeStockWithdrawalPermission(
            string operationId, AccountId grantee, bool hasOwnerRoleBond, long expectedPermissionRevision)
        {
            string binding = Digest(string.Join("|", new[]
            {
                RecRevoke, grantee.Value ?? string.Empty
            }));
            if (TryReplay(operationId, binding, out var conflict, out var recorded))
            {
                if (conflict) return new PermissionResult(PermissionCommandOutcome.OperationConflict,
                    PermissionFor(grantee).Generation, PermissionFor(grantee).State, _permissionRevision);
                return DecodePermission(PermissionCommandOutcome.Replayed, recorded);
            }

            if (string.IsNullOrEmpty(grantee.Value))
                throw new ArgumentException("grantee required");

            if (!hasOwnerRoleBond)
                return new PermissionResult(PermissionCommandOutcome.OwnerRoleRequired,
                    PermissionFor(grantee).Generation, PermissionFor(grantee).State, _permissionRevision);

            var current = PermissionFor(grantee);
            var transition = current.TryRevoke(out var next);
            if (transition == WithdrawalPermissionTransition.NotActive)
                return new PermissionResult(PermissionCommandOutcome.NotActive,
                    current.Generation, current.State, _permissionRevision);

            if (expectedPermissionRevision != _permissionRevision)
                return new PermissionResult(PermissionCommandOutcome.StalePermissionRevision,
                    current.Generation, current.State, _permissionRevision);

            long newRev = _permissionRevision + 1;
            string result = EncodePermission(next, newRev);
            AppendPermission(RecRevoke, operationId, binding, grantee, next, newRev, result);
            return new PermissionResult(PermissionCommandOutcome.Applied, next.Generation, next.State, _permissionRevision);
        }

        // ---- Provenance ----

        public readonly struct StockProvenanceEntry
        {
            public StockProvenanceEntry(StockProvenanceKind kind, string operationId, string receiptId,
                string optionKey, long stockRevisionAfter)
            {
                Kind = kind;
                OperationId = operationId ?? string.Empty;
                ReceiptId = receiptId ?? string.Empty;
                OptionKey = optionKey ?? string.Empty;
                StockRevisionAfter = stockRevisionAfter;
            }

            public StockProvenanceKind Kind { get; }
            public string OperationId { get; }
            public string ReceiptId { get; }
            public string OptionKey { get; }
            public long StockRevisionAfter { get; }
        }

        // ---- Mapping helpers ----

        private static MenuSelectionOutcome MapSelection(DonationSelectionResolution r)
        {
            switch (r)
            {
                case DonationSelectionResolution.OwnerRoleRequired: return MenuSelectionOutcome.OwnerRoleRequired;
                case DonationSelectionResolution.OptionsNotDistinct: return MenuSelectionOutcome.OptionsNotDistinct;
                case DonationSelectionResolution.OptionNotInPool: return MenuSelectionOutcome.OptionNotInPool;
                case DonationSelectionResolution.WrongLevel: return MenuSelectionOutcome.WrongLevel;
                case DonationSelectionResolution.StalePoolVersion: return MenuSelectionOutcome.StalePoolVersion;
                default: return MenuSelectionOutcome.Applied;
            }
        }

        private DonationResult Reject(DonationOutcome outcome, string code) =>
            new DonationResult(outcome, code, string.Empty, _stockRevision, _inventoryRevision);

        private static bool Contains(IReadOnlyDictionary<string, long> ledger, ItemVector vector)
        {
            foreach (var kv in vector.Items)
            {
                ledger.TryGetValue(kv.Key, out long have);
                if (have < kv.Value) return false;
            }
            return true;
        }

        private static bool Fits(IReadOnlyDictionary<string, long> ledger, ItemVector vector, CapacityPolicy policy)
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in ledger) result[kv.Key] = kv.Value;
            foreach (var kv in vector.Items)
            {
                result.TryGetValue(kv.Key, out long have);
                result[kv.Key] = have + kv.Value;
            }
            long total = 0;
            int kinds = 0;
            foreach (var kv in result)
            {
                if (kv.Value <= 0) continue;
                kinds++;
                total += kv.Value;
                if (kv.Value > policy.MaxPerItem) return false;
            }
            if (kinds > policy.MaxKinds) return false;
            if (total > policy.MaxTotalUnits) return false;
            return true;
        }

        // ---- Journal append + projection application ----

        private void AppendMenu(string operationId, string binding, DonationMenuSelection selection,
            long menuRev, bool isDefault, string result)
        {
            string payload = SerializeMenuPayload(selection);
            Append(SerializeRecord(isDefault ? RecDefault : RecMenu, operationId, binding, menuRev, payload, result));
            ApplyMenu(selection, menuRev);
            _committedOps[operationId] = binding;
            _resultByOp[operationId] = result;
        }

        private void AppendDonation(string operationId, string binding, DonationOption option,
            string receipt, long stockRev, long invRev, string result)
        {
            string payload = new SnapshotWriter()
                .Put("optKey", option.CanonicalKey)
                .Put("vec", SerializeVector(option.Vector))
                .Put("receipt", receipt)
                .Build();
            Append(SerializeRecord(RecDonation, operationId, binding, stockRev, payload, result));
            ApplyDonation(option, operationId, receipt, stockRev, invRev);
            _committedOps[operationId] = binding;
            _resultByOp[operationId] = result;
        }

        private void AppendPermission(string rec, string operationId, string binding, AccountId grantee,
            StockWithdrawalPermission next, long permRev, string result)
        {
            string payload = new SnapshotWriter()
                .Put("grantee", grantee.Value)
                .PutInt("gen", next.Generation)
                .PutInt("state", (int)next.State)
                .Build();
            Append(SerializeRecord(rec, operationId, binding, permRev, payload, result));
            ApplyPermission(grantee, next, permRev);
            _committedOps[operationId] = binding;
            _resultByOp[operationId] = result;
        }

        private void AppendPending(string operationId, string binding, bool pending)
        {
            string payload = new SnapshotWriter().PutBool("pending", pending).Build();
            string result = payload;
            Append(SerializeRecord(RecPending, operationId, binding, 0, payload, result));
            _pendingDelivery = pending;
            _committedOps[operationId] = binding;
            _resultByOp[operationId] = result;
        }

        private void ApplyMenu(DonationMenuSelection selection, long menuRev)
        {
            _menu = selection;
            _menuRevision = menuRev;
        }

        private void ApplyDonation(DonationOption option, string operationId, string receipt, long stockRev, long invRev)
        {
            foreach (var kv in option.Vector.Items)
            {
                _stock.TryGetValue(kv.Key, out long have);
                _stock[kv.Key] = have + kv.Value;
                _inventory.TryGetValue(kv.Key, out long inv);
                long left = inv - kv.Value;
                if (left <= 0) _inventory.Remove(kv.Key);
                else _inventory[kv.Key] = left;
            }
            _stockRevision = stockRev;
            _inventoryRevision = invRev;
            _provenance.Add(new StockProvenanceEntry(StockProvenanceKind.Donation, operationId, receipt,
                option.CanonicalKey, stockRev));
        }

        private void ApplyPermission(AccountId grantee, StockWithdrawalPermission next, long permRev)
        {
            _permissions[grantee.Value] = next;
            _permissionRevision = permRev;
        }

        // ---- Rehydration ----

        private void Rehydrate()
        {
            foreach (var kv in _openingStock) _stock[kv.Key] = kv.Value;
            foreach (var kv in _openingInventory) _inventory[kv.Key] = kv.Value;

            foreach (var line in ReadDurable())
            {
                var rec = ParseRecord(line);
                if (rec == null) continue;
                var r = rec.Value;
                if (_committedOps.ContainsKey(r.OperationId)) continue;
                _committedOps[r.OperationId] = r.Binding;
                _resultByOp[r.OperationId] = r.Result;

                switch (r.Rec)
                {
                    case RecMenu:
                    case RecDefault:
                    {
                        var selection = DeserializeMenuPayload(r.Payload);
                        ApplyMenu(selection, r.Revision);
                        break;
                    }
                    case RecDonation:
                    {
                        var pr = new SnapshotReader(r.Payload);
                        var vector = DeserializeVector(pr.GetString("vec"));
                        var optParts = pr.GetString("optKey").Split('\u0001');
                        var option = new DonationOption(optParts[0],
                            int.Parse(optParts[1], CultureInfo.InvariantCulture), vector);
                        string receipt = pr.GetString("receipt");
                        ApplyDonation(option, r.OperationId, receipt, r.Revision, _inventoryRevision + 1);
                        break;
                    }
                    case RecGrant:
                    case RecRevoke:
                    {
                        var pr = new SnapshotReader(r.Payload);
                        var grantee = new AccountId(pr.GetString("grantee"));
                        var perm = ReconstructPermission(pr.GetInt("gen"), (WithdrawalPermissionState)pr.GetInt("state"));
                        ApplyPermission(grantee, perm, r.Revision);
                        break;
                    }
                    case RecPending:
                    {
                        var pr = new SnapshotReader(r.Payload);
                        _pendingDelivery = pr.GetBool("pending");
                        break;
                    }
                }
            }
        }

        // Reconstruct a permission value from its persisted generation/state. StockWithdrawalPermission
        // is constructed only via transitions, so rehydrate by replaying transitions from None.
        private static StockWithdrawalPermission ReconstructPermission(int generation, WithdrawalPermissionState state)
        {
            var p = StockWithdrawalPermission.None;
            for (int g = 0; g < generation; g++)
            {
                // Each generation is one grant; a revoke between grants is what advanced the generation.
                if (p.IsActive) p.TryRevoke(out p);
                p.TryGrant(out p);
            }
            if (state == WithdrawalPermissionState.Revoked && p.IsActive)
                p.TryRevoke(out p);
            return p;
        }

        // ---- Idempotency ----

        private bool TryReplay(string operationId, string binding, out bool conflict, out string recorded)
        {
            conflict = false;
            recorded = string.Empty;
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required");
            if (_committedOps.TryGetValue(operationId, out var committedBinding))
            {
                if (!string.Equals(committedBinding, binding, StringComparison.Ordinal))
                {
                    conflict = true;
                    return true;
                }
                recorded = _resultByOp.TryGetValue(operationId, out var rec) ? rec : string.Empty;
                return true;
            }
            return false;
        }

        private PermissionResult DecodePermission(PermissionCommandOutcome outcome, string recorded)
        {
            var rr = new SnapshotReader(recorded);
            return new PermissionResult(outcome, rr.GetInt("gen"),
                (WithdrawalPermissionState)rr.GetInt("state"), rr.GetLong("permRev"));
        }

        private static string EncodePermission(StockWithdrawalPermission p, long permRev) =>
            new SnapshotWriter().PutInt("gen", p.Generation).PutInt("state", (int)p.State).PutLong("permRev", permRev).Build();

        // ---- Vector (de)serialization ----

        private static string SerializeVector(ItemVector v)
        {
            var sb = new StringBuilder();
            foreach (var kv in v.Items)
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
            return sb.ToString();
        }

        private static ItemVector DeserializeVector(string s)
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.LastIndexOf('=');
                d[pair.Substring(0, eq)] = long.Parse(pair.Substring(eq + 1), CultureInfo.InvariantCulture);
            }
            return new ItemVector(d);
        }

        private static string SerializeMenuPayload(DonationMenuSelection s)
        {
            var w = new SnapshotWriter()
                .PutInt("selected", s.IsSelected ? 1 : 0)
                .PutInt("level", s.StoneLevel)
                .PutInt("poolVer", s.PoolVersion)
                .PutInt("prov", (int)s.Provenance);
            if (s.IsSelected)
            {
                w.Put("aId", s.OptionA.OptionId).PutInt("aVer", s.OptionA.Version).Put("aVec", SerializeVector(s.OptionA.Vector));
                w.Put("bId", s.OptionB.OptionId).PutInt("bVer", s.OptionB.Version).Put("bVec", SerializeVector(s.OptionB.Vector));
            }
            return w.Build();
        }

        private DonationMenuSelection DeserializeMenuPayload(string payload)
        {
            var r = new SnapshotReader(payload);
            if (r.GetInt("selected") == 0) return DonationMenuSelection.None;
            var a = new DonationOption(r.GetString("aId"), r.GetInt("aVer"), DeserializeVector(r.GetString("aVec")));
            var b = new DonationOption(r.GetString("bId"), r.GetInt("bVer"), DeserializeVector(r.GetString("bVec")));
            // Reconstruct through the pool-independent selection factory: the persisted options ARE the
            // authoritative facts, so build the value directly via the internal reconstruction path.
            return DonationMenuSelection.FromPersisted(
                r.GetInt("level"), r.GetInt("poolVer"), a, b, (DonationMenuProvenance)r.GetInt("prov"));
        }

        // ---- Record framing (pipe-delimited, base64 fields; framed + crc journal) ----

        private struct ParsedRecord
        {
            public string Rec;
            public string OperationId;
            public string Binding;
            public long Revision;
            public string Payload;
            public string Result;
        }

        private static string SerializeRecord(string rec, string operationId, string binding, long revision,
            string payload, string result) =>
            string.Join("|", new[]
            {
                rec, Encode(operationId), Encode(binding),
                revision.ToString(CultureInfo.InvariantCulture),
                Encode(payload), Encode(result)
            });

        private static ParsedRecord? ParseRecord(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 6) return null;
            if (parts[0] != RecMenu && parts[0] != RecDefault && parts[0] != RecDonation
                && parts[0] != RecGrant && parts[0] != RecRevoke && parts[0] != RecPending) return null;
            return new ParsedRecord
            {
                Rec = parts[0],
                OperationId = Decode(parts[1]),
                Binding = Decode(parts[2]),
                Revision = long.Parse(parts[3], CultureInfo.InvariantCulture),
                Payload = Decode(parts[4]),
                Result = Decode(parts[5])
            };
        }

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

        private List<string> ReadDurable()
        {
            var results = new List<string>();
            if (!File.Exists(_journalPath)) return results;
            using (var fs = new FileStream(_journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                long length = fs.Length;
                while (true)
                {
                    long recordStart = fs.Position;
                    if (recordStart + 8 > length) break;
                    int payloadLen = br.ReadInt32();
                    uint crc = br.ReadUInt32();
                    if (payloadLen < 0 || fs.Position + payloadLen > length) break;
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen || Crc32(payload) != crc) break;
                    results.Add(Encoding.UTF8.GetString(payload));
                }
            }
            return results;
        }

        private static string Encode(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));

        private static string Decode(string s) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(s));

        private static string ReceiptId(string operationId) => Digest("receipt|" + operationId);

        private static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static readonly uint[] _crcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (var b in data)
                crc = _crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
