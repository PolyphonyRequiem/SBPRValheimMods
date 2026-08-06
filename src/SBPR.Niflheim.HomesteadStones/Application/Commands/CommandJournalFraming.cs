using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Application.Commands
{
    /// <summary>
    /// The receipt-backed command journal's DURABLE FRAMING LAYER (ADO #128).
    ///
    /// Every command handler in this directory writes its progression history to its own
    /// append-only journal using an identical on-disk frame:
    ///
    ///     [int32 payloadLength][uint32 crc32(payload)][payload bytes (UTF-8)]
    ///
    /// This type owns that frame and nothing above it. It was extracted verbatim from six
    /// handlers (Purchase, Development, Relationship, Facet, LocalPolicy, Activity) whose
    /// copies were byte-for-byte identical after comment stripping — the extraction is
    /// behaviour-preserving by construction, not by re-derivation.
    ///
    /// WHY THIS EXISTS (locality, not tidiness). Six independent copies of a durability
    /// protocol are six places a fix must land. That is not hypothetical: ADO #127 was a
    /// total-data-loss delimiter bug fixed in RelationshipCommands and missed in the other
    /// six, and ADO #126 was the same shape one level up. A defect in CRC, fsync ordering,
    /// or torn-frame rejection must now be fixed once.
    ///
    /// WHAT DELIBERATELY STAYS OUT (the correlation-risk boundary, ADO #128's adversarial case):
    ///   * Each handler still owns its OWN journal file. Shared code, INDEPENDENT durable
    ///     state. Corrupting one handler's journal must never affect another's rehydration.
    ///     Do not add a shared-file or shared-stream API to this type.
    ///   * The record layout (field set, count, and tag such as LOCALPOLICYREC) stays in the
    ///     handler. Those genuinely differ per handler and a shared record shape would couple
    ///     unrelated domains.
    ///   * Replay/conflict detection, the domain transition, and the authority policy stay in
    ///     the handler behind their existing interfaces.
    ///
    /// The delimiter-safety invariant from ADO #127 lives one level up in each handler's
    /// Record/ParseRecord: every free-text field is base64-encoded via <see cref="Encode"/>
    /// before entering the pipe-delimited record, so the field count is a reliable structural
    /// check for ANY operation id. This layer is delimiter-agnostic: it moves opaque bytes.
    /// </summary>
    internal static class CommandJournalFraming
    {
        /// <summary>
        /// Appends one CRC-guarded frame to <paramref name="journalPath"/> and fsyncs it.
        ///
        /// The frame is written length-first so a reader can detect a torn tail structurally,
        /// and the CRC is written before the payload so a partially-flushed payload fails
        /// verification rather than being accepted. <c>fs.Flush(true)</c> forces the OS write
        /// barrier: the journal IS the save, so a boundary that returns has durably landed.
        /// </summary>
        internal static void Append(string journalPath, string text)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            using (var fs = new FileStream(journalPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(payload.Length);
                bw.Write(Crc32(payload));
                bw.Write(payload);
                bw.Flush();
                fs.Flush(true);
            }
        }

        /// <summary>
        /// Reads every INTACT frame from <paramref name="journalPath"/>, in append order.
        ///
        /// Stops at the first frame that fails any structural or CRC check and returns what
        /// preceded it. That truncate-at-first-damage rule is deliberate and load-bearing: a
        /// crash mid-append leaves a torn tail, and everything before the tear is known-good
        /// history while everything at or after it is untrustworthy. Frames are never
        /// partially applied, and damage is never silently skipped over to reach later frames.
        ///
        /// Returns an empty list when the journal does not yet exist (a first-boot handler).
        /// </summary>
        internal static List<string> ReadDurable(string journalPath)
        {
            var results = new List<string>();
            if (!File.Exists(journalPath)) return results;
            using (var fs = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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

        /// <summary>
        /// Base64-encodes a free-text field so it cannot contain the '|' record delimiter.
        /// A null is encoded as empty rather than throwing — an absent field is a legal value.
        /// </summary>
        internal static string Encode(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));

        /// <summary>
        /// Inverse of <see cref="Encode"/>. Throws <see cref="FormatException"/> on a
        /// malformed field; callers catch that and reject the whole frame honestly as null.
        /// </summary>
        internal static string Decode(string s) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(s));

        /// <summary>
        /// The 8-byte (16 hex char) truncated SHA-256 used for binding digests, payload
        /// digests, and receipts. Truncation is intentional: these are collision-resistant
        /// correlation handles for replay detection, not security tokens.
        /// </summary>
        internal static string Digest(string s)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

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

        /// <summary>Standard CRC-32 (reflected, polynomial 0xEDB88320) over the frame payload.</summary>
        internal static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
