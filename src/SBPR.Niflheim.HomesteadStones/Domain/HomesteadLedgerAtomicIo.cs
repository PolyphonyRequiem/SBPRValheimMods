using System;
using System.IO;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Domain
{
    // ============================================================================
    // R6 (Blocker 5) — engine-free atomic, crash-safe ledger IO.
    //
    // Extracted from the net48 HomesteadLedgerStore so the crash-boundary and
    // corruption-recovery contract is UNIT-TESTED headless (the reviewer required
    // crash-boundary and corruption tests, and adapter/storage seams under test).
    //
    // Durability contract:
    //   * WriteAtomic: serialize to a temp on the SAME directory, flush + fsync the
    //     file, then atomically replace the live file via File.Replace (keeping a .bak)
    //     WITHOUT deleting the old one first. A plain rename is used when there is no
    //     live file yet (rename is itself atomic).
    //   * LoadWithRecovery: prefer the live file; if it is missing or corrupt, recover
    //     from a valid temp (crash mid-rename) or the .bak backup; only a genuinely
    //     absent ledger (no live/temp/backup) is an empty ledger. A present-but-corrupt
    //     live file with no valid recovery source FAILS CLOSED (throws) so the caller
    //     never treats corruption as empty history.
    // ============================================================================

    /// <summary>Thrown when the ledger cannot be made durable (write) or a present ledger cannot be read with
    /// no valid recovery source (load). Fail-closed signal for the realization loop.</summary>
    internal sealed class HomesteadLedgerIoException : Exception
    {
        internal HomesteadLedgerIoException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>Engine-free atomic ledger IO over a set of sibling paths (live / temp / backup). Pure
    /// System.IO — no Valheim, no engine — so the crash/corruption contract is fully unit-tested.</summary>
    internal static class HomesteadLedgerAtomicIo
    {
        internal static void WriteAtomic(string livePath, string tempPath, string backupPath, string contents)
        {
            if (livePath == null) throw new ArgumentNullException(nameof(livePath));
            if (tempPath == null) throw new ArgumentNullException(nameof(tempPath));
            if (backupPath == null) throw new ArgumentNullException(nameof(backupPath));

            var directory = Path.GetDirectoryName(livePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);

            // 1) Write full contents to the temp file and fsync it so the bytes are on disk before we make
            //    the temp visible as the live file.
            var bytes = Encoding.UTF8.GetBytes(contents);
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            // 2) Atomically replace the live file WITHOUT a delete-then-move window. File.Replace keeps a
            //    backup of the previous good file; a plain rename is used for the first write.
            if (File.Exists(livePath))
                File.Replace(tempPath, livePath, backupPath, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, livePath);
        }

        /// <summary>Load the ledger for <paramref name="worldIdentity"/> from the live file, recovering from a
        /// valid temp or backup when the live file is missing/corrupt. Throws <see cref="HomesteadLedgerIoException"/>
        /// when a present live file is corrupt and no valid recovery source exists (fail-closed).</summary>
        internal static HomesteadWorldLedger LoadWithRecovery(
            string worldIdentity, string livePath, string tempPath, string backupPath)
        {
            if (TryReadValid(livePath, worldIdentity, out var fromLive)) return fromLive;

            if (File.Exists(livePath))
            {
                if (TryReadValid(tempPath, worldIdentity, out var fromTemp)) return fromTemp;
                if (TryReadValid(backupPath, worldIdentity, out var fromBak)) return fromBak;
                throw new HomesteadLedgerIoException(
                    $"Ledger for world '{worldIdentity}' present but unreadable and no valid temp/backup exists.");
            }

            // No live file. A leftover temp/backup from a crash before the first successful rename still
            // counts as real history — recover it rather than losing provenance.
            if (TryReadValid(tempPath, worldIdentity, out var recoveredTemp)) return recoveredTemp;
            if (TryReadValid(backupPath, worldIdentity, out var recoveredBak)) return recoveredBak;

            var fresh = new HomesteadWorldLedger();
            fresh.SetWorldIdentity(worldIdentity);
            return fresh;
        }

        private static bool TryReadValid(string path, string worldIdentity, out HomesteadWorldLedger ledger)
        {
            ledger = new HomesteadWorldLedger();
            ledger.SetWorldIdentity(worldIdentity);
            try
            {
                if (!File.Exists(path)) return false;
                var serialized = File.ReadAllText(path);
                var parsed = HomesteadWorldLedger.Deserialize(worldIdentity, serialized);
                if (!parsed.IsWellFormed) return false;
                parsed.SetWorldIdentity(worldIdentity);
                ledger = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
