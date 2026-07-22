// ============================================================================
//  QA-M3R real fixture adapter (t_1572d041) — engine-free crash-safe persistence.
// ----------------------------------------------------------------------------
//  LedgerSnapshotStore — the crash-safe DURABLE side of the owned-resource ledger
//  (ADR-0009 §5.4 "no persistence leakage" + the owned-resource ledger crash
//  recovery). M3 shipped a pure SnapshotCodec (snapshot <-> text) and a ledger
//  that can Load/Reconcile; what was missing for the REAL adapter is a store that
//  actually writes that text to disk crash-safely and reads it back on restart.
//
//  Crash-safety contract (why this is more than File.WriteAllText):
//    * ATOMIC PUBLISH. The encoded snapshot is written to a sibling temp file,
//      flushed to the OS (FileStream.Flush(true) => fsync), then File.Replace/
//      Move'd over the live path. A crash mid-write leaves EITHER the previous
//      good snapshot OR the fully-written new one — never a torn half-file that
//      would decode into a partial ledger and mis-drive cleanup.
//    * FAIL-CLOSED READ. A missing file is "no prior run" (Empty). A present but
//      corrupt/truncated file decodes via SnapshotCodec to a typed failure that
//      surfaces as LoadOutcome.Corrupt — the caller must NOT treat corrupt as
//      empty (that would silently orphan a prior run's spawns), it re-drives
//      reconcile against world truth instead.
//    * OWNED-ONLY. The store round-trips exactly the ledger's own entries; it
//      introduces no new ids, so a reloaded ledger can only ever clean up the
//      exact owned ids it created (unrelated-object safety is preserved across
//      restart).
//
//  Engine-free: System.* only (System.IO is durability, not engine). No Unity,
//  no Valheim, no product identity/AP/ownership/signature/verdict. This file
//  link-compiles into the headless xUnit suite unchanged.
// ============================================================================

using System;
using System.IO;
using System.Text;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Why a snapshot load returned no usable ledger, or that it did.</summary>
    public enum SnapshotLoadStatus
    {
        /// <summary>A valid snapshot was loaded.</summary>
        Loaded = 0,

        /// <summary>No snapshot file exists — a fresh run with no prior owned resources.</summary>
        Absent = 1,

        /// <summary>A file exists but could not be read (I/O fault). Fail-closed: caller must not assume empty.</summary>
        IoError = 2,

        /// <summary>A file exists but did not decode (truncated/corrupt). Fail-closed: caller reconciles, never assumes empty.</summary>
        Corrupt = 3,
    }

    /// <summary>The typed outcome of a snapshot load. Carries the snapshot only when <see cref="Status"/> is Loaded.</summary>
    public sealed class SnapshotLoadResult
    {
        private SnapshotLoadResult(SnapshotLoadStatus status, LedgerSnapshot? snapshot, string detail)
        {
            Status = status;
            Snapshot = snapshot;
            Detail = detail ?? string.Empty;
        }

        public SnapshotLoadStatus Status { get; }
        public LedgerSnapshot? Snapshot { get; }
        public string Detail { get; }

        /// <summary>True iff a usable snapshot was loaded.</summary>
        public bool Ok => Status == SnapshotLoadStatus.Loaded && Snapshot != null;

        public static SnapshotLoadResult Loaded(LedgerSnapshot s) => new(SnapshotLoadStatus.Loaded, s, string.Empty);
        public static SnapshotLoadResult Absent() => new(SnapshotLoadStatus.Absent, null, string.Empty);
        public static SnapshotLoadResult IoError(string detail) => new(SnapshotLoadStatus.IoError, null, detail);
        public static SnapshotLoadResult Corrupt(string detail) => new(SnapshotLoadStatus.Corrupt, null, detail);
    }

    /// <summary>
    /// A crash-safe durable store for one owned-resource ledger snapshot. Writes are atomic
    /// (temp + fsync + replace); reads are fail-closed (missing = Absent, unreadable = IoError,
    /// undecodable = Corrupt — never a partial ledger). One store instance owns one snapshot path.
    /// </summary>
    public sealed class LedgerSnapshotStore
    {
        private readonly string _path;
        private readonly string _tempPath;
        private readonly string _backupPath;

        /// <summary>Create a store bound to a durable snapshot path. The parent directory is created on first save.</summary>
        public LedgerSnapshotStore(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("snapshot path must be non-empty", nameof(path));
            _path = path;
            _tempPath = path + ".tmp";
            _backupPath = path + ".bak";
        }

        /// <summary>The durable snapshot path this store owns.</summary>
        public string Path => _path;

        /// <summary>True iff a durable snapshot file currently exists.</summary>
        public bool Exists() => File.Exists(_path);

        /// <summary>
        /// Atomically persist a ledger's snapshot. Encodes via SnapshotCodec, writes to a sibling
        /// temp file, flushes to the OS, then replaces the live file so a crash cannot leave a torn
        /// snapshot. Throws only on a genuine I/O fault the caller should surface (fail-closed) — a
        /// caller that cannot persist must not proceed as if cleanup state were durable.
        /// </summary>
        public void Save(OwnedResourceLedger ledger)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            string text = SnapshotCodec.Encode(ledger.ToSnapshot());

            string? dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // 1. Write the full snapshot to the temp file and fsync it to durable storage.
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
            using (var fs = new FileStream(_tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            // 2. Atomically publish. File.Replace is atomic where supported and keeps a backup; on a
            //    first-write (no live file yet) fall back to a plain move of the fsync'd temp file.
            if (File.Exists(_path))
                File.Replace(_tempPath, _path, _backupPath, ignoreMetadataErrors: true);
            else
                File.Move(_tempPath, _path);
        }

        /// <summary>
        /// Load the durable snapshot. Fail-closed: a missing file is Absent, an unreadable file is
        /// IoError, and a present-but-undecodable file is Corrupt — never a silent empty ledger that
        /// would orphan a prior run's owned resources. The caller reconciles Corrupt/Absent against
        /// world truth rather than assuming nothing is owned.
        /// </summary>
        public SnapshotLoadResult Load()
        {
            if (!File.Exists(_path)) return SnapshotLoadResult.Absent();

            string text;
            try
            {
                text = File.ReadAllText(_path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return SnapshotLoadResult.IoError(ex.GetType().Name + ": " + ex.Message);
            }

            var decode = SnapshotCodec.Decode(text);
            if (!decode.Ok || decode.Snapshot == null)
                return SnapshotLoadResult.Corrupt(decode.Error);

            return SnapshotLoadResult.Loaded(decode.Snapshot);
        }

        /// <summary>
        /// Delete the durable snapshot + any temp/backup siblings (called after a fully-verified
        /// cleanup so the world save carries no harness ledger). Idempotent and best-effort per
        /// sibling; a missing file is success.
        /// </summary>
        public void Delete()
        {
            TryDelete(_path);
            TryDelete(_tempPath);
            TryDelete(_backupPath);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* best effort — a leftover sibling is harmless and re-published next Save */ }
        }
    }
}
