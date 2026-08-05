// ============================================================================
//  Shared TEST harness for journal corruption shapes (ADO #129).
// ----------------------------------------------------------------------------
//  Torn-frame corruption — a half-written record from process death — is the
//  hardest durability property in the Homestead progression system, because the
//  journals ARE the save: every projection store is in-memory only and is rebuilt
//  from its journal at server boot. Before ADO #129 that property was exercised for
//  exactly ONE of the seven command handlers (RelationshipCommandHandler). This
//  harness is the shared, parameterised corruption surface the other six now call,
//  so the coverage is symmetric rather than 1-of-7.
//
//  It is deliberately TEST-ONLY. ADO #128 (extracting the shared journal PROTOCOL
//  into production code) is a separate card with an unresolved correlated-blast-radius
//  tradeoff that the architect owns; nothing here touches production. Shared TEST
//  code carries none of that risk.
//
//  The three corruption shapes, and the behaviour each pins (established by ADO #127,
//  unchanged by #129):
//
//    1. TornTail            — raw bytes appended past the last intact frame, shorter
//                             than a frame header. The reader must STOP there. Records
//                             written BEFORE it still replay. Fail-CLOSED at the FRAME
//                             layer: an append-only log with a corrupt length prefix
//                             cannot be resynchronised without guessing at durable data.
//
//    2. CrcInvalidLastFrame — a well-sized frame whose payload no longer matches its
//                             stored CRC (a single flipped byte). Same frame-layer
//                             rule: the read truncates AT that frame, so the corrupt
//                             record is never accepted, and everything before it survives.
//
//    3. WellFramedGarbage   — a structurally PERFECT frame (correct length + correct
//                             CRC) whose CONTENT is malformed (wrong field count / bad
//                             record tag / non-base64 field). Fail-HONEST at the RECORD
//                             layer: rejected individually as null and SKIPPED, so the
//                             records both BEFORE and AFTER it still replay. It does not
//                             poison the file.
//
//  Frame layout, identical across all seven handlers (and asserted so by
//  Harness_frame_layout_matches_the_shipped_writers in the all-seven test file):
//      int32  payloadLength      (little-endian, BinaryWriter)
//      uint32 crc32              (reflected CRC-32, poly 0xEDB88320, init/xorout ~0)
//      byte[] payload            (UTF-8 of the pipe-delimited record text)
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SBPR.Trailborne.Tests
{
    /// <summary>Synthetic corruption of a Homestead progression journal file, for tests that prove a
    /// handler's boot rehydration is fail-closed at the frame layer and fail-honest at the record
    /// layer. Never used by production code (see ADO #128 for why the PROTOCOL is not shared).</summary>
    internal static class JournalCorruptionHarness
    {
        /// <summary>Shape 1 — a half-written frame from process death: raw bytes shorter than the
        /// 8-byte frame header, appended past the last intact frame. Mirrors
        /// NiflheimProgressionRecoveryTests.Recovery_TornTailFromPartialWrite_IsTruncatedNotAccepted.</summary>
        internal static void AppendTornTail(string journalPath)
        {
            using (var fs = new FileStream(journalPath, FileMode.Append, FileAccess.Write))
            {
                var junk = new byte[] { 0x7F, 0x11, 0x22 }; // shorter than an 8-byte header
                fs.Write(junk, 0, junk.Length);
                fs.Flush();
            }
        }

        /// <summary>Shape 1b — a frame header that CLAIMS a payload far longer than the bytes that
        /// actually follow (the classic mid-write kill). Mirrors the torn tail spliced by
        /// NiflheimT009L2ProgressionRemediationTests.</summary>
        internal static void AppendTruncatedFrameHeader(string journalPath)
        {
            using (var fs = new FileStream(journalPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8))
            {
                bw.Write(9999);                     // claims a 9999-byte payload...
                bw.Write((uint)0xDEADBEEF);         // ...bogus crc...
                bw.Write(new byte[] { 1, 2, 3 });   // ...only 3 bytes actually follow.
                bw.Flush();
            }
        }

        /// <summary>Shape 2 — append a frame whose length header and payload are fully present and
        /// correctly sized, but whose stored CRC does NOT match the payload (one flipped byte). This
        /// isolates the CRC check specifically: unlike a torn tail it passes every length bound, so only
        /// the checksum can reject it. The frame is appended AFTER the committed records, so those must
        /// still replay.</summary>
        internal static void AppendCrcInvalidFrame(string journalPath,
            string content = "CRCVICTIM|this frame is correctly sized but its checksum is wrong")
        {
            byte[] payload = Encoding.UTF8.GetBytes(content);
            byte[] corrupted = (byte[])payload.Clone();
            corrupted[0] ^= 0xFF;  // CRC computed over the ORIGINAL, payload written CORRUPTED
            using (var fs = new FileStream(journalPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8))
            {
                bw.Write(corrupted.Length);
                bw.Write(Crc32Of(payload));
                bw.Write(corrupted);
                bw.Flush();
            }
        }

        /// <summary>Shape 2b — flip one byte inside the LAST intact frame's payload so the stored CRC no
        /// longer matches, destroying an already-committed record in place. Returns false when the file
        /// holds no intact frame to corrupt.</summary>
        internal static bool CorruptLastFrameCrc(string journalPath)
        {
            var starts = FrameStarts(journalPath);
            if (starts.Count == 0) return false;
            long payloadStart = starts[starts.Count - 1] + 8; // past length + crc
            using (var fs = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite))
            {
                fs.Position = payloadStart;
                int b = fs.ReadByte();
                if (b < 0) return false;
                fs.Position = payloadStart;
                fs.WriteByte((byte)(b ^ 0xFF));
                fs.Flush();
            }
            return true;
        }

        /// <summary>Shape 3 — splice a structurally PERFECT frame (correct length + correct CRC) whose
        /// CONTENT is garbage. Mirrors
        /// NiflheimProgressionRecoveryTests.OneMalformedRecordDoesNotPreventReplayOfTheOthers.</summary>
        internal static void AppendWellFramedGarbage(string journalPath,
            string content = "REC|not|a|valid|record")
        {
            AppendWellFramedPayload(journalPath, Encoding.UTF8.GetBytes(content));
        }

        /// <summary>Shape 3b — a well-framed record carrying the handler's OWN record tag and the exact
        /// right field count, but with a field that is not valid base64. This is the malformed-CONTENT
        /// case that reaches PAST the field-count guard and lands on the decode guard.</summary>
        internal static void AppendWellFramedNonBase64Record(string journalPath, string recordTag,
            int fieldCount)
        {
            var parts = new string[fieldCount];
            parts[0] = recordTag;
            for (int i = 1; i < fieldCount; i++)
                parts[i] = "!!!not-base64!!!";
            AppendWellFramedPayload(journalPath,
                Encoding.UTF8.GetBytes(string.Join("|", parts)));
        }

        /// <summary>Shape 3c — a well-framed record with the handler's OWN tag and too FEW fields, where
        /// every filler field is simultaneously valid base64 AND a valid integer ("1234" satisfies both),
        /// so it passes every per-field guard the parser applies. That matters: with filler that fails
        /// base64 or int parsing, the parser would bail on the FIRST field and never reach the missing
        /// one, making the field-count guard look redundant. With this filler, deleting the
        /// `parts.Length != N` guard causes ParseRecord to index past the end of the array and throw
        /// IndexOutOfRangeException out of boot rehydration — which is exactly what makes the guard
        /// provably load-bearing under mutation (see scripts/ado129-mutation-evidence.py).</summary>
        internal static void AppendWellFramedShortRecord(string journalPath, string recordTag,
            int fieldCount)
        {
            var parts = new string[fieldCount];
            parts[0] = recordTag;
            for (int i = 1; i < fieldCount; i++)
                parts[i] = "1234";   // valid base64 (4 chars) and a valid int/long
            AppendWellFramedPayload(journalPath,
                Encoding.UTF8.GetBytes(string.Join("|", parts)));
        }

        internal static void AppendWellFramedPayload(string journalPath, byte[] payload)
        {
            using (var fs = new FileStream(journalPath, FileMode.Append, FileAccess.Write))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8))
            {
                bw.Write(payload.Length);
                bw.Write(Crc32Of(payload));
                bw.Write(payload);
                bw.Flush();
            }
        }

        /// <summary>Byte offsets of every intact frame in the journal, in order. Stops at the first
        /// structurally broken frame — the same fail-closed walk the shipped readers perform, so the
        /// harness never invents a frame the production reader would not have accepted.</summary>
        internal static List<long> FrameStarts(string journalPath)
        {
            var starts = new List<long>();
            if (!File.Exists(journalPath)) return starts;
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
                    if (payload.Length != payloadLen || Crc32Of(payload) != crc) break;
                    starts.Add(recordStart);
                }
            }
            return starts;
        }

        /// <summary>Decoded payload text of every intact frame, in order.</summary>
        internal static List<string> ReadIntactFrames(string journalPath)
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
                    if (payload.Length != payloadLen || Crc32Of(payload) != crc) break;
                    results.Add(Encoding.UTF8.GetString(payload));
                }
            }
            return results;
        }

        /// <summary>The journal's frame CRC — the same reflected CRC-32 (poly 0xEDB88320) the seven
        /// shipped writers use, so the harness can splice frames the production reader accepts.</summary>
        internal static uint Crc32Of(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int b = 0; b < 8; b++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
