using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SBPR.Niflheim.ProgressionSpike
{
    // Append-only write-ahead journal (candidate "1" from the Gate-A prep matrix).
    //
    // net48 hard-constraint audit: every API used here exists in .NET Framework 4.8 ->
    //   System.IO.FileStream(.Flush(true)), BinaryReader/Writer, File.Exists, Path,
    //   System.Text.Encoding.UTF8, unchecked CRC over bytes. No net5+-only surface.
    //
    // Record framing (all little-endian, BinaryWriter defaults):
    //   [int32 payloadLen][uint32 crc32(payload)][payload bytes]
    // A record is DURABLE only when its full frame is present AND crc matches. A torn
    // tail (partial frame or bad crc) left by process death is truncated on recovery ->
    // the last fully-durable record wins. This is the canonical crash-each-write answer.
    public sealed class DurableJournal
    {
        private readonly string _path;

        public DurableJournal(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Path { get { return _path; } }

        // Append one record and fsync. FileStream.Flush(true) forces OS buffers to disk
        // (the net48 durability primitive; U-RX1). We open per-append so a killed process
        // never leaves a half-flushed shared handle.
        public void Append(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(payload.Length);
                bw.Write(Crc32(payload));
                bw.Write(payload);
                bw.Flush();
                fs.Flush(true); // fsync-equivalent: the durable boundary
            }
        }

        public void AppendText(string s)
        {
            Append(Encoding.UTF8.GetBytes(s));
        }

        // Read only fully-durable records. A torn tail is ignored (and its byte offset
        // reported) rather than silently accepted or "repaired" with invented data.
        public List<string> ReadDurable(out long tornTailBytes)
        {
            tornTailBytes = 0;
            var results = new List<string>();
            if (!File.Exists(_path)) return results;

            using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs, Encoding.UTF8))
            {
                long length = fs.Length;
                while (true)
                {
                    long recordStart = fs.Position;
                    if (recordStart + 8 > length) { tornTailBytes = length - recordStart; break; }
                    int payloadLen = br.ReadInt32();
                    uint crc = br.ReadUInt32();
                    if (payloadLen < 0 || fs.Position + payloadLen > length)
                    {
                        tornTailBytes = length - recordStart; break;
                    }
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen || Crc32(payload) != crc)
                    {
                        tornTailBytes = length - recordStart; break;
                    }
                    results.Add(Encoding.UTF8.GetString(payload));
                }
            }
            return results;
        }

        public List<string> ReadDurable()
        {
            long t;
            return ReadDurable(out t);
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                table[i] = c;
            }
            return table;
        }

        public static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++)
                crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }
    }
}
