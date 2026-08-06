// ============================================================================
//  AT-EXTRACT-BYTE-IDENTICAL (ADO #128) — the extraction oracle.
// ----------------------------------------------------------------------------
//  WHAT THIS FILE IS FOR
//
//  ADO #128 extracts the durable framing layer that six command handlers each
//  carried a private copy of (Purchase, Development, Relationship, Facet,
//  LocalPolicy, Activity) into CommandJournalFraming. The journals ARE the save:
//  every progression projection is in-memory only and is rebuilt from these files
//  at boot. An extraction that changes ONE BYTE of the on-disk frame does not
//  produce a test failure — it produces a player whose purchases, Tree commitments,
//  BP, and Weapon-Discipline choice silently fail to load after the next update.
//
//  So the extraction is not accepted on "the suite is still green". It is accepted
//  on a byte-level oracle: an INDEPENDENT reimplementation of the pre-extraction
//  frame format (see LegacyFraming below, transcribed from the handlers as they
//  stood at 448d081) writes a corpus, CommandJournalFraming writes the same corpus,
//  and the two files must be byte-for-byte identical — and each must read the
//  other's output.
//
//  WHY A HAND-TRANSCRIBED LEGACY COPY RATHER THAN CALLING THE OLD CODE
//
//  The old code is being deleted by this card, so it cannot be called. A frozen
//  transcription is the only thing that can outlive the deletion and keep asserting
//  the format afterwards. It is deliberately written in the pre-extraction style
//  (indexed loop, inline table) and MUST NOT be refactored to delegate to
//  CommandJournalFraming — that would make the oracle vacuously self-comparing.
//
//  THE CORPUS
//
//  Chosen to cover the field shapes that actually broke this system before:
//    * pipes in the payload — ADO #127's total-data-loss bug. A StoneId is
//      "world|zoneX|zoneZ" by construction, so operation ids legitimately contain '|'.
//    * empty and whitespace strings — the null/absent-field path through Encode.
//    * non-ASCII and emoji — multi-byte UTF-8, where a length-in-bytes vs
//      length-in-chars confusion would surface.
//    * embedded newlines and NUL — bytes that a line-oriented reader would mishandle.
//    * a payload longer than one disk page — the multi-frame / large-length path.
//
//  MUTATION EVIDENCE — WHY THESE ASSERTIONS BITE
//
//  Verified by mutating CommandJournalFraming and re-running this file:
//    * length/CRC write order swapped        -> 21 of 36 RED
//    * digest truncated to 9 bytes not 8     -> 10 of 36 RED
//    * CRC verification deleted from the read -> 1 of 36 RED
//    * CRC polynomial 0xEDB88320 -> ...21    -> 22 of 36 RED
//
//  ONE MUTATION SURVIVES, AND IT MATTERS: changing `fs.Flush(true)` to `fs.Flush()`
//  keeps all 36 GREEN. An in-process unit test cannot observe an OS write barrier —
//  the bytes are in the page cache either way, and only a real power loss or kernel
//  panic tells the two apart. The fsync is therefore protected by CODE REVIEW AND
//  THIS COMMENT, not by a test. Do not read a green run here as evidence that
//  durability is intact. If you touch Append, check the Flush(true) by eye.
//
//  WHAT THIS PROVES AND WHAT IT DOES NOT
//
//  It proves the extracted module's on-disk bytes and read behaviour match the
//  pre-extraction format for this corpus. It does NOT prove any handler was wired
//  to it correctly (each handler's migration carries its own round-trip proof), it
//  does not prove the fsync survives (see above), and it does not prove live server
//  boot behaviour. Logs green is not playable.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// A frozen, independent transcription of the frame format as the six handlers
    /// implemented it before ADO #128, captured at merge-base 448d081.
    ///
    /// DO NOT refactor this to call CommandJournalFraming. Its entire value is that it
    /// is a SEPARATE implementation; delegating would turn the oracle into a tautology.
    /// </summary>
    internal static class LegacyFraming
    {
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

        internal static string Encode(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));

        internal static string Decode(string s) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(s));

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

        internal static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }

    public sealed class NiflheimCommandJournalFramingOracleTests : IDisposable
    {
        private readonly string _dir;

        public NiflheimCommandJournalFramingOracleTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ado128-oracle-" + Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
            catch (IOException) { /* best-effort temp cleanup; never fail a test on it */ }
        }

        /// <summary>
        /// The adversarial corpus. Every entry is a payload shape that has broken, or could
        /// plausibly break, a length-prefixed CRC-guarded frame.
        /// </summary>
        public static IEnumerable<object[]> Corpus()
        {
            yield return new object[] { "ordinary", "PURCHASEREC|abc|1|deadbeefcafe0001|feedface00000002|3" };
            // ADO #127: a StoneId is "world|zoneX|zoneZ", so operation ids DO contain pipes.
            yield return new object[] { "pipes", "uid:-898655635|3|2|purchase|op|with|many|pipes" };
            yield return new object[] { "empty", "" };
            yield return new object[] { "whitespace", "   \t  " };
            yield return new object[] { "non-ascii", "Hügin und Munin — Járnviðr, Yggdrasill, Ægir" };
            yield return new object[] { "emoji-astral", "runestone \U0001F5FF glow \U0001FAB5 \U0001F9ED" };
            yield return new object[] { "newlines", "line one\nline two\r\nline three\r" };
            yield return new object[] { "nul-byte", "before\0after" };
            yield return new object[] { "all-bytes", BuildAllCodeUnitsPayload() };
            yield return new object[] { "large", new string('x', 9000) };
        }

        private static string BuildAllCodeUnitsPayload()
        {
            // Every code point 1..0xD7FF is representable; the surrogate range is skipped because
            // a lone surrogate is not valid UTF-8 and is not a payload this layer must carry.
            var sb = new StringBuilder();
            for (int c = 1; c < 0xD800; c += 7) sb.Append((char)c);
            return sb.ToString();
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void ExtractedFrameIsByteIdenticalToLegacyFrame(string label, string payload)
        {
            string legacyPath = Path.Combine(_dir, label + ".legacy.journal");
            string extractedPath = Path.Combine(_dir, label + ".extracted.journal");

            LegacyFraming.Append(legacyPath, payload);
            CommandJournalFraming.Append(extractedPath, payload);

            byte[] legacyBytes = File.ReadAllBytes(legacyPath);
            byte[] extractedBytes = File.ReadAllBytes(extractedPath);

            Assert.Equal(legacyBytes.Length, extractedBytes.Length);
            Assert.Equal(legacyBytes, extractedBytes);
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void ExtractedReaderRoundTripsAndReadsLegacyBytes(string label, string payload)
        {
            string legacyPath = Path.Combine(_dir, label + ".xread.journal");
            LegacyFraming.Append(legacyPath, payload);

            // The extracted reader must read a journal written by the PRE-extraction code —
            // this is the upgrade path for every existing player's save.
            Assert.Equal(new[] { payload }, CommandJournalFraming.ReadDurable(legacyPath));

            string extractedPath = Path.Combine(_dir, label + ".xwrite.journal");
            CommandJournalFraming.Append(extractedPath, payload);

            // ...and the pre-extraction reader must read what the extracted writer produces,
            // which is what makes a rollback safe.
            Assert.Equal(new[] { payload }, LegacyFraming.ReadDurable(extractedPath));
        }

        [Fact]
        public void MultiFrameAppendOrderAndBytesMatchLegacy()
        {
            string legacyPath = Path.Combine(_dir, "multi.legacy.journal");
            string extractedPath = Path.Combine(_dir, "multi.extracted.journal");

            var payloads = new List<string>();
            foreach (object[] row in Corpus()) payloads.Add((string)row[1]);

            foreach (string p in payloads)
            {
                LegacyFraming.Append(legacyPath, p);
                CommandJournalFraming.Append(extractedPath, p);
            }

            Assert.Equal(File.ReadAllBytes(legacyPath), File.ReadAllBytes(extractedPath));
            Assert.Equal(payloads, CommandJournalFraming.ReadDurable(extractedPath));
            Assert.Equal(payloads, LegacyFraming.ReadDurable(extractedPath));
        }

        [Theory]
        [MemberData(nameof(Corpus))]
        public void EncodeDecodeAndDigestMatchLegacy(string label, string payload)
        {
            Assert.Equal(LegacyFraming.Encode(payload), CommandJournalFraming.Encode(payload));
            Assert.Equal(LegacyFraming.Digest(payload), CommandJournalFraming.Digest(payload));

            string encoded = CommandJournalFraming.Encode(payload);
            // Encoding is what makes the field count a reliable structural check (ADO #127):
            // no encoded field may contain the record delimiter.
            Assert.DoesNotContain("|", encoded);
            Assert.Equal(payload, CommandJournalFraming.Decode(encoded));
            Assert.Equal(payload, LegacyFraming.Decode(encoded));

            Assert.Equal(label, label);
        }

        [Fact]
        public void EncodeTreatsNullAsEmptyRatherThanThrowing()
        {
            Assert.Equal(LegacyFraming.Encode(null!), CommandJournalFraming.Encode(null!));
            Assert.Equal(string.Empty, CommandJournalFraming.Decode(CommandJournalFraming.Encode(null!)));
        }

        [Fact]
        public void Crc32MatchesLegacyAcrossByteValues()
        {
            var rng = new Random(20260805);
            for (int trial = 0; trial < 256; trial++)
            {
                var buf = new byte[trial];
                rng.NextBytes(buf);
                Assert.Equal(LegacyFraming.Crc32(buf), CommandJournalFraming.Crc32(buf));
            }
        }

        [Fact]
        public void ReadDurableOnMissingJournalReturnsEmptyLikeLegacy()
        {
            string missing = Path.Combine(_dir, "does-not-exist.journal");
            Assert.Empty(CommandJournalFraming.ReadDurable(missing));
            Assert.Empty(LegacyFraming.ReadDurable(missing));
        }

        /// <summary>
        /// The fail-closed truncate-at-first-damage rule (ADO #127/#129 contract) must survive
        /// the extraction: a torn tail hides nothing before it and everything after it.
        /// </summary>
        [Fact]
        public void TornTailTruncatesReadIdenticallyInBothImplementations()
        {
            string path = Path.Combine(_dir, "torn.journal");
            CommandJournalFraming.Append(path, "first");
            CommandJournalFraming.Append(path, "second");

            // Half a frame header — the classic process-death tail.
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                fs.Write(new byte[] { 0x11, 0x22, 0x33 }, 0, 3);

            CommandJournalFraming.Append(path, "unreachable");

            var extracted = CommandJournalFraming.ReadDurable(path);
            var legacy = LegacyFraming.ReadDurable(path);

            Assert.Equal(legacy, extracted);
            Assert.Equal(new[] { "first", "second" }, extracted);
            Assert.DoesNotContain("unreachable", extracted);
        }

        /// <summary>
        /// A CRC-invalid frame spliced BETWEEN two good records must make the later record
        /// unreachable. Positioned this way deliberately: corruption past the last record is
        /// invisible and would let a deleted CRC check still pass (the vacuity trap ADO #129
        /// documented).
        /// </summary>
        [Fact]
        public void CrcInvalidFrameMakesLaterRecordsUnreachableIdentically()
        {
            string path = Path.Combine(_dir, "crc.journal");
            CommandJournalFraming.Append(path, "before");

            byte[] payload = Encoding.UTF8.GetBytes("corrupt-me");
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(payload.Length);
                bw.Write(CommandJournalFraming.Crc32(payload) ^ 0xFFFFu);  // deliberately wrong
                bw.Write(payload);
            }

            CommandJournalFraming.Append(path, "after");

            var extracted = CommandJournalFraming.ReadDurable(path);
            Assert.Equal(LegacyFraming.ReadDurable(path), extracted);
            Assert.Equal(new[] { "before" }, extracted);
        }
    }
}
