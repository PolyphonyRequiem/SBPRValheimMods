// ============================================================================
//  SBPR.CartographyPreview — pure-C# PNG encoder (headless, no Unity Texture)
// ----------------------------------------------------------------------------
//  The dedicated server is headless (graphicsDeviceType == Null). Unity's
//  Texture2D.EncodeToPNG path is unreliable / unwanted there, so we encode the
//  composer's Color32[] to a PNG ourselves: raw RGBA, zlib stored (uncompressed)
//  blocks, manual CRC32 + Adler32. Small (≈33² window upscaled), so the stored
//  encoding's size is irrelevant — correctness + zero engine dependency win.
// ============================================================================

using System;
using System.IO;
using UnityEngine;

namespace SBPR.CartographyPreview
{
    internal static class PngWriter
    {
        /// <summary>
        /// Write an RGBA32 pixel buffer (row-major, top-down) to a PNG file. The composer
        /// produces BOTTOM-UP rows (wy=0 = south), matching the in-game texture; pass
        /// flipY=true to flip to PNG's top-down convention so north is up in the image.
        /// </summary>
        public static void Write(string path, Color32[] pixels, int width, int height, int upscale = 1, bool flipY = true)
        {
            if (upscale < 1) upscale = 1;
            int outW = width * upscale;
            int outH = height * upscale;

            // Build raw image bytes: each row prefixed with filter byte 0 (None).
            int stride = outW * 4;
            byte[] raw = new byte[(stride + 1) * outH];
            for (int oy = 0; oy < outH; oy++)
            {
                int srcY = oy / upscale;
                int readY = flipY ? (height - 1 - srcY) : srcY;
                int rowStart = oy * (stride + 1);
                raw[rowStart] = 0; // filter: None
                for (int ox = 0; ox < outW; ox++)
                {
                    int srcX = ox / upscale;
                    Color32 c = pixels[readY * width + srcX];
                    int p = rowStart + 1 + ox * 4;
                    raw[p + 0] = c.r;
                    raw[p + 1] = c.g;
                    raw[p + 2] = c.b;
                    raw[p + 3] = c.a;
                }
            }

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            // PNG signature.
            fs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);

            // IHDR.
            byte[] ihdr = new byte[13];
            WriteBE(ihdr, 0, outW);
            WriteBE(ihdr, 4, outH);
            ihdr[8] = 8;   // bit depth
            ihdr[9] = 6;   // color type: RGBA
            ihdr[10] = 0;  // compression
            ihdr[11] = 0;  // filter
            ihdr[12] = 0;  // interlace
            WriteChunk(fs, "IHDR", ihdr);

            // IDAT: zlib stream wrapping stored deflate blocks.
            byte[] idat = ZlibStore(raw);
            WriteChunk(fs, "IDAT", idat);

            // IEND.
            WriteChunk(fs, "IEND", Array.Empty<byte>());
        }

        private static void WriteBE(byte[] buf, int off, int v)
        {
            buf[off + 0] = (byte)((v >> 24) & 0xFF);
            buf[off + 1] = (byte)((v >> 16) & 0xFF);
            buf[off + 2] = (byte)((v >> 8) & 0xFF);
            buf[off + 3] = (byte)(v & 0xFF);
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            byte[] len = new byte[4];
            WriteBE(len, 0, data.Length);
            s.Write(len, 0, 4);

            byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            if (data.Length > 0) s.Write(data, 0, data.Length);

            uint crc = Crc32.Compute(typeBytes, data);
            byte[] crcBytes = new byte[4];
            WriteBE(crcBytes, 0, unchecked((int)crc));
            s.Write(crcBytes, 0, 4);
        }

        /// <summary>Wrap raw bytes in a zlib stream using stored (uncompressed) deflate blocks.</summary>
        private static byte[] ZlibStore(byte[] data)
        {
            using var ms = new MemoryStream();
            ms.WriteByte(0x78); // CMF: deflate, 32K window
            ms.WriteByte(0x01); // FLG: no dict, check bits

            int pos = 0;
            const int MaxBlock = 0xFFFF;
            while (pos < data.Length)
            {
                int blockLen = Math.Min(MaxBlock, data.Length - pos);
                bool last = (pos + blockLen) >= data.Length;
                ms.WriteByte((byte)(last ? 1 : 0)); // BFINAL + BTYPE=00 (stored)
                ms.WriteByte((byte)(blockLen & 0xFF));
                ms.WriteByte((byte)((blockLen >> 8) & 0xFF));
                int nlen = (~blockLen) & 0xFFFF;
                ms.WriteByte((byte)(nlen & 0xFF));
                ms.WriteByte((byte)((nlen >> 8) & 0xFF));
                ms.Write(data, pos, blockLen);
                pos += blockLen;
            }

            uint adler = Adler32.Compute(data);
            ms.WriteByte((byte)((adler >> 24) & 0xFF));
            ms.WriteByte((byte)((adler >> 16) & 0xFF));
            ms.WriteByte((byte)((adler >> 8) & 0xFF));
            ms.WriteByte((byte)(adler & 0xFF));
            return ms.ToArray();
        }
    }

    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[n] = c;
            }
            return t;
        }

        public static uint Compute(byte[] a, byte[] b)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte x in a) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
            foreach (byte x in b) c = Table[(c ^ x) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }
    }

    internal static class Adler32
    {
        public static uint Compute(byte[] data)
        {
            const uint Mod = 65521;
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % Mod;
                b = (b + a) % Mod;
            }
            return (b << 16) | a;
        }
    }
}
