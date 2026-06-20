// ============================================================================
//  EquipableIconTransparencyTests — xUnit asset-quality guard (bug t_b9a111ca).
// ----------------------------------------------------------------------------
//  THE BUG (Daniel playtest, 2026-06-19): "none of our custom equipables show
//  the blue equipped indicator because the backgrounds are solid, not
//  transparent." Vanilla draws the inventory slot's blue "equipped" highlight
//  (InventoryGrid element child "equiped", toggled by m_equiped.SetActive in
//  assembly_valheim) BEHIND the item icon Image. An icon PNG with an opaque
//  background therefore occludes the highlight for every equipable item.
//
//  THE GUARD: this suite asserts that every EQUIPABLE SBPR item icon PNG ships
//  with real transparency (a meaningful fraction of fully-transparent pixels and
//  transparent corners). It is the anti-recurrence guard for the whole class —
//  the next time someone regenerates an equipable icon with an opaque backdrop
//  (the warm_backdrop()/frame() flatten that caused this), CI goes red here.
//
//  WHY A FILE-LEVEL PNG TEST (not a runtime SpecCheck pixel-scan): the icons are
//  loaded into GPU textures at runtime; pixel-reading them server-side is both
//  unreadable-texture-fragile and impossible to verify on the headless build box
//  (the "logs green != playable" boundary). The committed PNG bytes ARE the
//  shippable source of truth, and a pure byte-level decode here is fully
//  verifiable in CI with no Valheim SDK, no GPU, no UnityEngine — exactly the
//  engine-free, structural, low-volatility bar tests/BoundedMapMathTests.cs sets.
//
//  SCOPE — EQUIPABLE items only. Material-type icons (pigments, raw sunstone,
//  cairn marker, portal seed) never show an equipped indicator, so their
//  opacity is harmless and they are deliberately NOT asserted here. The list
//  below is grounded against the items' m_itemType in src/ (Utility / Trinket /
//  Tool / TwoHandedWeapon = equipable; Material = not).
// ============================================================================

using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class EquipableIconTransparencyTests
    {
        // Repo-root-relative path to the shipped item icons (copied verbatim into the
        // plugin folder by scripts/pack-modpack.sh and loaded by Assets.LoadPngAsSprite).
        private static readonly string IconDir =
            Path.Combine(RepoRoot(), "assets", "icons", "items");

        // EQUIPABLE SBPR item icons — each MUST be transparent so the equipped
        // indicator shows. Grounded against m_itemType in src/:
        //   cartographers_kit  → ItemType.Utility        (CartographersKit.cs)
        //   sunstone_lens      → ItemType.Trinket        (SunstoneLens.cs)
        //   iron_compass       → ItemType.Trinket        (IronCompass.cs)
        //   trailblazers_spade → ItemType.Tool (Hoe clone, Trailblazing.cs)
        //   local_map          → ItemType.TwoHandedWeapon (LocalMap.cs)
        public static readonly string[] EquipableIcons =
        {
            "cartographers_kit_v0.1.png",
            "sunstone_lens_v0.1.png",
            "iron_compass_v0.1.png",
            "trailblazers_spade_v0.1.png",
            "local_map_v0.1.png",
        };

        public static System.Collections.Generic.IEnumerable<object[]> EquipableIconCases =>
            EquipableIcons.Select(n => new object[] { n });

        [Theory]
        [MemberData(nameof(EquipableIconCases))]
        public void Equipable_icon_png_has_an_alpha_channel(string fileName)
        {
            var info = DecodePng(Path.Combine(IconDir, fileName));
            Assert.True(info.HasAlphaChannel,
                $"{fileName}: PNG colour type {info.ColorType} has NO alpha channel — it is fully " +
                "opaque, so it occludes the inventory equipped indicator. Equipable icons must be " +
                "RGBA with a transparent background (PNG colour type 6).");
        }

        [Theory]
        [MemberData(nameof(EquipableIconCases))]
        public void Equipable_icon_has_substantial_transparency(string fileName)
        {
            var info = DecodePng(Path.Combine(IconDir, fileName));
            // A real transparent-background icon leaves a large fraction of the frame
            // see-through (the four fixed icons measured 0.44–0.86). Require >=15% so a
            // future tighter-cropped icon still passes, but a fully-opaque backdrop (0%)
            // hard-fails. This is the core anti-recurrence assertion.
            Assert.True(info.TransparentFraction >= 0.15,
                $"{fileName}: only {info.TransparentFraction:P0} of pixels are fully transparent " +
                "(need >=15%). The background is effectively opaque and will hide the blue equipped " +
                "indicator. Regenerate the icon on a TRANSPARENT canvas (see scripts/gen_*_icon*.py / " +
                "scripts/knockout_equipable_icon_bg.py).");
        }

        [Theory]
        [MemberData(nameof(EquipableIconCases))]
        public void Equipable_icon_has_transparent_corners(string fileName)
        {
            var info = DecodePng(Path.Combine(IconDir, fileName));
            // The four corners are the background by construction for every icon in the
            // set (centered subject). All four fully transparent is a cheap, robust proof
            // the backdrop was knocked out rather than left as an opaque square/vignette.
            Assert.True(info.AllCornersTransparent,
                $"{fileName}: not all four corners are fully transparent (alpha = " +
                $"[{string.Join(", ", info.CornerAlphas)}]). An opaque corner means the icon still " +
                "has a solid background box that will occlude the equipped indicator.");
        }

        // ── Minimal, dependency-free PNG decoder ────────────────────────────────
        // System.Drawing is unreliable cross-platform (no libgdiplus on CI), so we
        // decode the PNG ourselves: parse IHDR for colour type/size, inflate the IDAT
        // stream, un-filter scanlines, and read the alpha channel. Supports the 8-bit
        // RGB(A) / grey(A) PNGs this repo's generators emit (no palette, no interlace).
        private readonly struct PngInfo
        {
            public PngInfo(int colorType, bool hasAlpha, double transparentFraction, int[] cornerAlphas)
            {
                ColorType = colorType;
                HasAlphaChannel = hasAlpha;
                TransparentFraction = transparentFraction;
                CornerAlphas = cornerAlphas;
            }
            public int ColorType { get; }
            public bool HasAlphaChannel { get; }
            public double TransparentFraction { get; }
            public int[] CornerAlphas { get; }
            public bool AllCornersTransparent => CornerAlphas.All(a => a == 0);
        }

        private static PngInfo DecodePng(string path)
        {
            Assert.True(File.Exists(path), $"Icon PNG not found on disk: {path}");
            byte[] data = File.ReadAllBytes(path);

            // Signature.
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Assert.True(data.Length > 8 && data.Take(8).SequenceEqual(sig), $"{path}: not a PNG.");

            int width = ReadBE32(data, 16);
            int height = ReadBE32(data, 20);
            int bitDepth = data[24];
            int colorType = data[25];
            int interlace = data[28];

            Assert.True(bitDepth == 8, $"{path}: unsupported bit depth {bitDepth} (test expects 8).");
            Assert.True(interlace == 0, $"{path}: interlaced PNGs unsupported by this test.");

            // Channels per pixel by colour type: 0=grey(1) 2=rgb(3) 4=greyA(2) 6=rgba(4).
            int channels = colorType switch
            {
                0 => 1,
                2 => 3,
                4 => 2,
                6 => 4,
                _ => throw new NotSupportedException($"{path}: PNG colour type {colorType} unsupported."),
            };
            bool hasAlpha = colorType == 4 || colorType == 6;

            if (!hasAlpha)
            {
                // No alpha channel at all → fully opaque by definition.
                return new PngInfo(colorType, false, 0.0, new[] { 255, 255, 255, 255 });
            }

            // Concatenate IDAT chunks, then inflate (skip the 2-byte zlib header).
            byte[] idat = ExtractIdat(data);
            byte[] raw = Inflate(idat);

            int bytesPerPixel = channels; // 8-bit
            int stride = width * bytesPerPixel;
            byte[] image = Unfilter(raw, width, height, bytesPerPixel, stride);

            int alphaIndex = channels - 1; // alpha is the last channel
            long transparent = 0;
            long total = (long)width * height;
            // Sample every pixel (icons are <=1024², trivial).
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    if (image[row + x * bytesPerPixel + alphaIndex] == 0) transparent++;
                }
            }

            int A(int x, int y) => image[y * stride + x * bytesPerPixel + alphaIndex];
            int[] corners = { A(0, 0), A(width - 1, 0), A(0, height - 1), A(width - 1, height - 1) };

            return new PngInfo(colorType, true, (double)transparent / total, corners);
        }

        private static int ReadBE32(byte[] d, int off) =>
            (d[off] << 24) | (d[off + 1] << 16) | (d[off + 2] << 8) | d[off + 3];

        private static byte[] ExtractIdat(byte[] data)
        {
            using var ms = new MemoryStream();
            int p = 8;
            while (p + 8 <= data.Length)
            {
                int len = ReadBE32(data, p);
                string type = System.Text.Encoding.ASCII.GetString(data, p + 4, 4);
                int dataStart = p + 8;
                if (type == "IDAT") ms.Write(data, dataStart, len);
                if (type == "IEND") break;
                p = dataStart + len + 4; // skip data + CRC
            }
            return ms.ToArray();
        }

        private static byte[] Inflate(byte[] zlib)
        {
            // Skip the 2-byte zlib header; DeflateStream wants the raw DEFLATE body.
            using var input = new MemoryStream(zlib, 2, zlib.Length - 2);
            using var deflate = new System.IO.Compression.DeflateStream(
                input, System.IO.Compression.CompressionMode.Decompress);
            using var outp = new MemoryStream();
            deflate.CopyTo(outp);
            return outp.ToArray();
        }

        private static byte[] Unfilter(byte[] raw, int width, int height, int bpp, int stride)
        {
            byte[] outImg = new byte[height * stride];
            byte[] prev = new byte[stride];
            int pos = 0;
            for (int y = 0; y < height; y++)
            {
                int filter = raw[pos++];
                byte[] cur = new byte[stride];
                Array.Copy(raw, pos, cur, 0, stride);
                pos += stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? cur[x - bpp] : 0;
                    int b = prev[x];
                    int c = x >= bpp ? prev[x - bpp] : 0;
                    int val = cur[x];
                    cur[x] = filter switch
                    {
                        0 => (byte)val,
                        1 => (byte)(val + a),
                        2 => (byte)(val + b),
                        3 => (byte)(val + ((a + b) >> 1)),
                        4 => (byte)(val + Paeth(a, b, c)),
                        _ => throw new NotSupportedException($"Unknown PNG filter {filter}."),
                    };
                }
                Array.Copy(cur, 0, outImg, y * stride, stride);
                prev = cur;
            }
            return outImg;
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        private static string RepoRoot()
        {
            // Walk up from the test assembly until we find the repo marker (the icons dir).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "assets", "icons", "items")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate repo root (assets/icons/items) from " + AppContext.BaseDirectory);
        }
    }
}
