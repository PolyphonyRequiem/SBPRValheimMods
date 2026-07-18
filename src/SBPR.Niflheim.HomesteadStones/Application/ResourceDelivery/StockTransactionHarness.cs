using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T006 (Gate B) — the DISPOSABLE server-owned player-inventory ↔ Stone Stock transaction
    // harness (plan §"Gate B — Server inventory transaction"; contracts §SubmitUpkeepDonation /
    // §WithdrawStoneStock). Gate B is a SPIKE: it proves the load-bearing transaction seam that
    // donations (Tracer 3) and withdrawals (Tracer 5) will later depend on, WITHOUT implementing
    // the Resource Delivery gameplay loop. No gameplay is enabled by this file.
    //
    // WHAT THIS PROVES (plan Gate B exit)
    //   "The same operation converges to ONE transfer or NO transfer with no duplicated or lost
    //    player or Stone items", across:
    //      * exact AUTHORED vector resolution — a donation never trusts a client quantity; the
    //        server resolves the exact item vector from the authored option (contracts:
    //        "The client does not submit trusted item quantities.");
    //      * full DEBIT/CREDIT FIT — insufficient source items, over-capacity deposit, and a
    //        player inventory that cannot accept the whole withdrawn vector all reject with NO
    //        mutation and NO server trust of a client fit claim;
    //      * STALE REVISIONS — an expected-revision mismatch on either ledger rejects pre-write;
    //      * REPLAY — same operation id + same binding returns the one recorded terminal result;
    //        a conflicting binding under the same id is OperationConflict;
    //      * DISCONNECT / PROCESS DEATH — a crash injected at any debit/credit/commit boundary
    //        converges on reconstruction to exactly one transfer or none.
    //
    // HOW IT MIRRORS THE PROVEN Gate-A MECHANISM
    //   Exactly like OperationReceiptStore / FinalLinkHandshakeStore: the framed, crc-checked,
    //   append-only journal IS the transaction. The player-inventory and Stone-Stock balances are
    //   IDEMPOTENT PROJECTIONS rebuilt from durable, terminal-bearing journal records only. A
    //   partial (non-terminal) operation therefore projects NOTHING — it neither debits nor credits
    //   either ledger — so a crash before the terminal record leaves both inventories exactly as
    //   they were (no transfer), and a crash after it recovers the full debit+credit together on
    //   replay (one transfer). The two ledgers "commit together" because a reader only ever observes
    //   balances derived from a terminal-bearing record; there is no window where one ledger moved
    //   and the other did not.
    //
    //   "Process death" is simulated exactly as in Gate A: an ICrashInjector throws after a chosen
    //   durable boundary, and recovery constructs a FRESH harness over the SAME journal path and the
    //   SAME seeded opening balances.
    //
    // net48 audit: FileStream(.Flush(true)), BinaryReader/Writer, SHA256, Encoding.UTF8, unchecked
    // CRC32 — all present in .NET Framework 4.8. Engine-free (System-only, no UnityEngine/Valheim/
    // BepInEx surface), so it link-compiles into the net8 test project exactly like the Gate-A slice.

    /// <summary>Which direction a Stock transaction moves items. The two directions share one
    /// atomic-recoverable machine but differ in source/destination and fit rules.</summary>
    public enum StockTransferKind
    {
        /// <summary>Donation: debit the authored vector from player inventory, credit Stone Stock
        /// (must fit Stock capacity). Server resolves the exact authored vector.</summary>
        Donation = 1,

        /// <summary>Withdrawal: debit the requested vector from Stone Stock, credit player inventory
        /// (must fit player inventory capacity).</summary>
        Withdrawal = 2
    }

    public enum StockTransferOutcome
    {
        Applied,                  // the transfer committed exactly once
        Replayed,                 // same op id + binding replayed the recorded terminal result
        OperationConflict,        // op id reused with a different binding/payload
        StaleInventoryRevision,   // expected player-inventory revision no longer current
        StaleStockRevision,       // expected Stock revision no longer current
        OptionNotAccepted,        // DonationOptionNotAccepted — unknown/absent authored option
        SourceItemsMissing,       // DonationItemsMissing / StockQuantityUnavailable — source lacks the full vector
        DestinationCannotFit      // StoneStockCapacityExceeded / PlayerInventoryCannotFit — destination cannot accept the full vector
    }

    /// <summary>An immutable stable-item → positive-quantity vector. Ordinally canonicalised so its
    /// digest is stable across process/replay (used for the operation binding). All quantities are
    /// strictly positive; a zero/negative entry is a construction error.</summary>
    public readonly struct ItemVector
    {
        private readonly SortedDictionary<string, long> _items;

        public ItemVector(IReadOnlyDictionary<string, long> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in items)
            {
                if (string.IsNullOrEmpty(kv.Key)) throw new ArgumentException("Item identity must be non-empty.");
                if (kv.Value <= 0) throw new ArgumentException("Item quantity must be strictly positive.");
                _items[kv.Key] = kv.Value;
            }
        }

        public IReadOnlyDictionary<string, long> Items =>
            (IReadOnlyDictionary<string, long>)_items ?? EmptyMap;

        public int KindCount => _items?.Count ?? 0;

        public long TotalUnits
        {
            get
            {
                long t = 0;
                if (_items != null) foreach (var kv in _items) t += kv.Value;
                return t;
            }
        }

        /// <summary>Canonical digest over the sorted (item, qty) pairs. Two vectors with the same
        /// contents in any input order produce the same digest.</summary>
        public string CanonicalDigest()
        {
            var sb = new StringBuilder();
            if (_items != null)
                foreach (var kv in _items)
                    sb.Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
            return sb.ToString();
        }

        private static readonly IReadOnlyDictionary<string, long> EmptyMap = new Dictionary<string, long>();
    }

    /// <summary>Stone Stock / player-inventory capacity policy (data-model Aggregate 4 capacity
    /// policy; provisional Level-2 defaults 16 kinds / 1,000 units / 500 per item). A resulting
    /// destination ledger must satisfy every bound or the whole transfer rejects.</summary>
    public readonly struct CapacityPolicy
    {
        public CapacityPolicy(int maxKinds, long maxTotalUnits, long maxPerItem)
        {
            MaxKinds = maxKinds;
            MaxTotalUnits = maxTotalUnits;
            MaxPerItem = maxPerItem;
        }

        public int MaxKinds { get; }
        public long MaxTotalUnits { get; }
        public long MaxPerItem { get; }

        /// <summary>The provisional Stone-Level-2 Stockpile capacity (data-model Aggregate 4).</summary>
        public static CapacityPolicy Level2Stock => new CapacityPolicy(16, 1000, 500);

        /// <summary>A generous player-carry policy for the harness (player inventory fit is a real
        /// gate but is not the authored Stock policy).</summary>
        public static CapacityPolicy PlayerCarry => new CapacityPolicy(64, 100000, 100000);
    }

    /// <summary>The terminal result of one Stock transaction. On a rejection it is non-mutating and
    /// carries no receipt (contracts: "Any validation or write failure changes none of the state
    /// owners."); on a stale rejection it carries the current revision the caller must refetch.</summary>
    public readonly struct StockTransferResult
    {
        public StockTransferResult(StockTransferOutcome outcome, string resultCode, string receiptId,
            long inventoryRevision, long stockRevision)
        {
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            ReceiptId = receiptId ?? string.Empty;
            InventoryRevision = inventoryRevision;
            StockRevision = stockRevision;
        }

        public StockTransferOutcome Outcome { get; }
        public string ResultCode { get; }
        public string ReceiptId { get; }
        public long InventoryRevision { get; }
        public long StockRevision { get; }

        public bool Committed => Outcome == StockTransferOutcome.Applied || Outcome == StockTransferOutcome.Replayed;
    }

    /// <summary>Injects real process death after the Nth durable boundary of a Stock transaction.
    /// Default never crashes. Mirrors OperationReceiptStore.ICrashInjector.</summary>
    public interface IStockCrashInjector
    {
        void AfterBoundary(StockTransferBoundary boundary);
    }

    public enum StockTransferBoundary
    {
        None = 0,
        IntentJournaled = 1,   // binding + resolved vector recorded
        SourceDebited = 2,     // source-ledger debit delta recorded
        DestinationCredited = 3, // destination-ledger credit delta recorded
        Committed = 4          // terminal result recorded — the atomic boundary
    }

    public sealed class NoStockCrash : IStockCrashInjector
    {
        public static readonly NoStockCrash Instance = new NoStockCrash();
        public void AfterBoundary(StockTransferBoundary boundary) { }
    }

    public sealed class StockTransactionHarness
    {
        private const string Rec = "STX";

        private readonly string _journalPath;
        private readonly CapacityPolicy _stockPolicy;
        private readonly CapacityPolicy _playerPolicy;
        private readonly IReadOnlyDictionary<string, long> _openingInventory;
        private readonly IReadOnlyDictionary<string, long> _openingStock;
        private readonly IReadOnlyDictionary<string, ItemVector> _authoredOptions;

        /// <param name="openingInventory">Durable opening player-inventory balances (the pre-existing
        /// fixture state). Reconstruction across "process death" MUST pass the same opening balances.</param>
        /// <param name="openingStock">Durable opening Stone-Stock balances.</param>
        /// <param name="authoredOptions">Server-authored donation option id → exact item vector. The
        /// server resolves a donation's vector from here; the client never supplies quantities.</param>
        public StockTransactionHarness(
            string journalPath,
            CapacityPolicy stockPolicy,
            CapacityPolicy playerPolicy,
            IReadOnlyDictionary<string, long> openingInventory,
            IReadOnlyDictionary<string, long> openingStock,
            IReadOnlyDictionary<string, ItemVector> authoredOptions)
        {
            _journalPath = journalPath ?? throw new ArgumentNullException(nameof(journalPath));
            _stockPolicy = stockPolicy;
            _playerPolicy = playerPolicy;
            _openingInventory = openingInventory ?? new Dictionary<string, long>();
            _openingStock = openingStock ?? new Dictionary<string, long>();
            _authoredOptions = authoredOptions ?? new Dictionary<string, ItemVector>();
        }

        public string JournalPath => _journalPath;

        // ---- Public balances (projections rebuilt from durable, terminal-bearing records only) ----

        public IReadOnlyDictionary<string, long> CurrentInventory() => ProjectLedger(isInventory: true);
        public IReadOnlyDictionary<string, long> CurrentStock() => ProjectLedger(isInventory: false);

        /// <summary>Committed mutating operations touching the player inventory. Optimistic-concurrency
        /// serialises concurrent transfers; a stale expected value rejects pre-write.</summary>
        public long InventoryRevision() => CommittedMutationCount();

        /// <summary>Committed mutating operations touching the Stone Stock. Every Stock transaction
        /// moves both ledgers, so the two revisions advance together.</summary>
        public long StockRevision() => CommittedMutationCount();

        // ---- Donation: server resolves the exact authored vector, debits player, credits Stock ----

        public StockTransferResult SubmitDonation(
            string operationId,
            string donationOptionId,
            long expectedInventoryRevision,
            long expectedStockRevision,
            IStockCrashInjector? crash = null)
        {
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required");

            // AUTHORED vector resolution: the server maps the option id to the exact item vector.
            // The client supplied NO quantity — an unknown option rejects with no mutation.
            if (donationOptionId == null || !_authoredOptions.TryGetValue(donationOptionId, out var vector))
                return Reject(StockTransferOutcome.OptionNotAccepted, "DonationOptionNotAccepted");

            return Transfer(operationId, StockTransferKind.Donation, donationOptionId, vector,
                expectedInventoryRevision, expectedStockRevision, crash ?? NoStockCrash.Instance);
        }

        // ---- Withdrawal: server validates the requested vector, debits Stock, credits player ----

        public StockTransferResult WithdrawStock(
            string operationId,
            ItemVector requestedVector,
            long expectedStockRevision,
            long expectedInventoryRevision,
            IStockCrashInjector? crash = null)
        {
            if (string.IsNullOrEmpty(operationId)) throw new ArgumentException("operationId required");
            if (requestedVector.KindCount == 0) throw new ArgumentException("A withdrawal must request at least one item.");

            return Transfer(operationId, StockTransferKind.Withdrawal, "withdraw", requestedVector,
                expectedInventoryRevision, expectedStockRevision, crash ?? NoStockCrash.Instance);
        }

        // ---- The one atomic-recoverable transfer machine ----

        private StockTransferResult Transfer(
            string operationId,
            StockTransferKind kind,
            string payloadTag,
            ItemVector vector,
            long expectedInventoryRevision,
            long expectedStockRevision,
            IStockCrashInjector crash)
        {
            string vectorDigest = vector.CanonicalDigest();
            string bindingDigest = Digest(operationId + "|" + (int)kind + "|" + payloadTag + "|" + vectorDigest);

            var view = InspectJournal(operationId);

            // ── Idempotent replay ──
            if (view.HasTerminal)
            {
                if (view.BindingDigest != bindingDigest)
                    return Reject(StockTransferOutcome.OperationConflict, "OperationConflict");
                return new StockTransferResult(StockTransferOutcome.Replayed, "Applied",
                    ReceiptId(operationId), InventoryRevision(), StockRevision());
            }
            if (view.SawAnyRecord && view.BindingDigest != bindingDigest)
                // A partial record under this op id with a DIFFERENT binding is ambiguous — never guess.
                return Reject(StockTransferOutcome.OperationConflict, "OperationConflict");

            // ── Validation (only a brand-new operation is gated; a resumed partial already passed) ──
            if (!view.SawAnyRecord)
            {
                long invRev = InventoryRevision();
                long stkRev = StockRevision();
                if (expectedInventoryRevision != invRev)
                    return new StockTransferResult(StockTransferOutcome.StaleInventoryRevision,
                        "StaleInventoryRevision", string.Empty, invRev, stkRev);
                if (expectedStockRevision != stkRev)
                    return new StockTransferResult(StockTransferOutcome.StaleStockRevision,
                        "StaleStockRevision", string.Empty, invRev, stkRev);

                var inventory = CurrentInventory();
                var stock = CurrentStock();

                if (kind == StockTransferKind.Donation)
                {
                    // Source = player inventory must contain the FULL authored vector.
                    if (!Contains(inventory, vector))
                        return Reject(StockTransferOutcome.SourceItemsMissing, "DonationItemsMissing");
                    // Destination = Stone Stock must accept the whole vector under its capacity policy.
                    if (!Fits(stock, vector, _stockPolicy))
                        return Reject(StockTransferOutcome.DestinationCannotFit, "StoneStockCapacityExceeded");
                }
                else // Withdrawal
                {
                    // Source = Stone Stock must contain the FULL requested vector.
                    if (!Contains(stock, vector))
                        return Reject(StockTransferOutcome.SourceItemsMissing, "StockQuantityUnavailable");
                    // Destination = player inventory must accept the whole vector (server-checked fit;
                    // no trusted client fit claim).
                    if (!Fits(inventory, vector, _playerPolicy))
                        return Reject(StockTransferOutcome.DestinationCannotFit, "PlayerInventoryCannotFit");
                }
            }

            // ── Drive the durable state machine forward from wherever the last crash left us ──
            var phase = view.LastPhase;
            // Signed deltas: donation debits player (−) / credits Stock (+); withdrawal is the mirror.
            var inventoryDelta = kind == StockTransferKind.Donation ? Negate(vector) : Positive(vector);
            var stockDelta = kind == StockTransferKind.Donation ? Positive(vector) : Negate(vector);

            if (phase < StockTransferBoundary.IntentJournaled)
            {
                Append(Serialize(operationId, StockTransferBoundary.IntentJournaled, bindingDigest, vectorDigest, null, null));
                crash.AfterBoundary(StockTransferBoundary.IntentJournaled);
                phase = StockTransferBoundary.IntentJournaled;
            }
            if (phase < StockTransferBoundary.SourceDebited)
            {
                // The debit delta lands on whichever ledger is the SOURCE for this kind.
                var invPart = kind == StockTransferKind.Donation ? inventoryDelta : (IReadOnlyDictionary<string, long>?)null;
                var stkPart = kind == StockTransferKind.Donation ? (IReadOnlyDictionary<string, long>?)null : stockDelta;
                Append(Serialize(operationId, StockTransferBoundary.SourceDebited, bindingDigest, vectorDigest, invPart, stkPart));
                crash.AfterBoundary(StockTransferBoundary.SourceDebited);
                phase = StockTransferBoundary.SourceDebited;
            }
            if (phase < StockTransferBoundary.DestinationCredited)
            {
                var invPart = kind == StockTransferKind.Donation ? (IReadOnlyDictionary<string, long>?)null : inventoryDelta;
                var stkPart = kind == StockTransferKind.Donation ? stockDelta : (IReadOnlyDictionary<string, long>?)null;
                Append(Serialize(operationId, StockTransferBoundary.DestinationCredited, bindingDigest, vectorDigest, invPart, stkPart));
                crash.AfterBoundary(StockTransferBoundary.DestinationCredited);
                phase = StockTransferBoundary.DestinationCredited;
            }
            if (phase < StockTransferBoundary.Committed)
            {
                Append(Serialize(operationId, StockTransferBoundary.Committed, bindingDigest, vectorDigest, null, null));
                crash.AfterBoundary(StockTransferBoundary.Committed);
            }

            return new StockTransferResult(StockTransferOutcome.Applied, "Applied",
                ReceiptId(operationId), InventoryRevision(), StockRevision());
        }

        // ---- Vector arithmetic / fit checks ----

        private static bool Contains(IReadOnlyDictionary<string, long> ledger, ItemVector vector)
        {
            foreach (var kv in vector.Items)
            {
                ledger.TryGetValue(kv.Key, out long have);
                if (have < kv.Value) return false;
            }
            return true;
        }

        /// <summary>Would the destination ledger still satisfy the capacity policy AFTER crediting the
        /// whole vector? Checks distinct kinds, total units, and per-item caps on the resulting state.</summary>
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
                if (kv.Value <= 0) continue; // a zero entry occupies no kind slot
                kinds++;
                total += kv.Value;
                if (kv.Value > policy.MaxPerItem) return false;
            }
            if (kinds > policy.MaxKinds) return false;
            if (total > policy.MaxTotalUnits) return false;
            return true;
        }

        private static Dictionary<string, long> Positive(ItemVector v)
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in v.Items) d[kv.Key] = kv.Value;
            return d;
        }

        private static Dictionary<string, long> Negate(ItemVector v)
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in v.Items) d[kv.Key] = -kv.Value;
            return d;
        }

        // ---- Projections (committed, terminal-bearing records only; deltas deduped per boundary) ----

        private IReadOnlyDictionary<string, long> ProjectLedger(bool isInventory)
        {
            var balances = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var kv in (isInventory ? _openingInventory : _openingStock))
                balances[kv.Key] = kv.Value;

            // Group durable records by op; only fully-committed ops contribute, and each boundary's
            // delta is applied exactly once (deduped by phase) so replay converges to one transfer.
            var byOp = GroupCommitted();
            foreach (var acc in byOp.Values)
            {
                if (!acc.HasTerminal) continue;
                var delta = isInventory ? acc.InventoryDelta : acc.StockDelta;
                foreach (var kv in delta)
                {
                    balances.TryGetValue(kv.Key, out long have);
                    long next = have + kv.Value;
                    if (next == 0) balances.Remove(kv.Key);
                    else balances[kv.Key] = next;
                }
            }
            return balances;
        }

        private long CommittedMutationCount()
        {
            long n = 0;
            foreach (var acc in GroupCommitted().Values)
                if (acc.HasTerminal) n++;
            return n;
        }

        private sealed class OpAccumulator
        {
            public readonly HashSet<StockTransferBoundary> SeenPhases = new HashSet<StockTransferBoundary>();
            public readonly Dictionary<string, long> InventoryDelta = new Dictionary<string, long>(StringComparer.Ordinal);
            public readonly Dictionary<string, long> StockDelta = new Dictionary<string, long>(StringComparer.Ordinal);
            public bool HasTerminal;
        }

        private Dictionary<string, OpAccumulator> GroupCommitted()
        {
            var byOp = new Dictionary<string, OpAccumulator>(StringComparer.Ordinal);
            foreach (var line in ReadDurable())
            {
                var rec = Parse(line);
                if (rec == null) continue;
                if (!byOp.TryGetValue(rec.OperationId, out var acc))
                {
                    acc = new OpAccumulator();
                    byOp[rec.OperationId] = acc;
                }
                if (acc.SeenPhases.Add(rec.Phase))
                {
                    Accumulate(acc.InventoryDelta, rec.InventoryDelta);
                    Accumulate(acc.StockDelta, rec.StockDelta);
                }
                if (rec.Phase == StockTransferBoundary.Committed) acc.HasTerminal = true;
            }
            return byOp;
        }

        private static void Accumulate(Dictionary<string, long> into, IReadOnlyDictionary<string, long>? delta)
        {
            if (delta == null) return;
            foreach (var kv in delta)
            {
                into.TryGetValue(kv.Key, out long have);
                into[kv.Key] = have + kv.Value;
            }
        }

        // ---- Journal inspection ----

        private readonly struct JournalView
        {
            public JournalView(bool sawAnyRecord, bool hasTerminal, StockTransferBoundary lastPhase, string? bindingDigest)
            {
                SawAnyRecord = sawAnyRecord;
                HasTerminal = hasTerminal;
                LastPhase = lastPhase;
                BindingDigest = bindingDigest;
            }

            public bool SawAnyRecord { get; }
            public bool HasTerminal { get; }
            public StockTransferBoundary LastPhase { get; }
            public string? BindingDigest { get; }
        }

        private JournalView InspectJournal(string operationId)
        {
            bool sawAny = false, hasTerminal = false;
            var lastPhase = StockTransferBoundary.None;
            string? binding = null;
            foreach (var line in ReadDurable())
            {
                var rec = Parse(line);
                if (rec == null || rec.OperationId != operationId) continue;
                sawAny = true;
                binding = rec.BindingDigest;
                if (rec.Phase > lastPhase) lastPhase = rec.Phase;
                if (rec.Phase == StockTransferBoundary.Committed) hasTerminal = true;
            }
            return new JournalView(sawAny, hasTerminal, lastPhase, binding);
        }

        private static StockTransferResult Reject(StockTransferOutcome outcome, string code) =>
            new StockTransferResult(outcome, code, string.Empty, 0, 0);

        private static string ReceiptId(string operationId) => Digest("receipt|" + operationId);

        // ---- Record encoding (framed + crc-checked, pipe-delimited with base64 payload fields) ----

        private sealed class RecordData
        {
            public string OperationId = string.Empty;
            public StockTransferBoundary Phase;
            public string BindingDigest = string.Empty;
            public string VectorDigest = string.Empty;
            public IReadOnlyDictionary<string, long>? InventoryDelta;
            public IReadOnlyDictionary<string, long>? StockDelta;
        }

        private static string Serialize(string opId, StockTransferBoundary phase, string binding, string vectorDigest,
            IReadOnlyDictionary<string, long>? inventoryDelta, IReadOnlyDictionary<string, long>? stockDelta) =>
            string.Join("|", new[]
            {
                Rec, Encode(opId), ((int)phase).ToString(CultureInfo.InvariantCulture),
                Encode(binding), Encode(vectorDigest),
                Encode(EncodeDelta(inventoryDelta)), Encode(EncodeDelta(stockDelta))
            });

        private static RecordData? Parse(string line)
        {
            var parts = line.Split('|');
            if (parts.Length != 7 || parts[0] != Rec) return null;
            return new RecordData
            {
                OperationId = Decode(parts[1]),
                Phase = (StockTransferBoundary)int.Parse(parts[2], CultureInfo.InvariantCulture),
                BindingDigest = Decode(parts[3]),
                VectorDigest = Decode(parts[4]),
                InventoryDelta = DecodeDelta(Decode(parts[5])),
                StockDelta = DecodeDelta(Decode(parts[6]))
            };
        }

        private static string EncodeDelta(IReadOnlyDictionary<string, long>? delta)
        {
            if (delta == null || delta.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var kv in delta)
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString(CultureInfo.InvariantCulture)).Append(';');
            return sb.ToString();
        }

        private static IReadOnlyDictionary<string, long>? DecodeDelta(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in s.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.LastIndexOf('=');
                d[pair.Substring(0, eq)] = long.Parse(pair.Substring(eq + 1), CultureInfo.InvariantCulture);
            }
            return d;
        }

        // ---- Append-only, framed + crc-checked journal (mirrors OperationReceiptStore) ----

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
                fs.Flush(true); // fsync-equivalent: the durable boundary
            }
        }

        /// <summary>Read only fully-durable records; a torn tail from process death is ignored.</summary>
        public List<string> ReadDurable()
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

        /// <summary>Durable record count — lets a test assert a crash landed between intended boundaries.</summary>
        public int DurableRecordCount() => ReadDurable().Count;

        private static string Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));
        private static string Decode(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));

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

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc;
        }
    }
}
