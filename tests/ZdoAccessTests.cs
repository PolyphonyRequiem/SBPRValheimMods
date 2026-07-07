using System.Collections.Generic;
using SBPR.Trailborne.Core.Zdo;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// In-memory <see cref="IZdoHandle"/> — the TEST ADAPTER across the ZDO seam (the shell's
    /// ZNetViewZdoHandle is the prod adapter). Two real adapters justify the seam. Models the
    /// three facts the policy cares about: is there a live ZDO (ghost vs. placed), who owns it,
    /// and the key/value store. Lets the ZdoAccess policy be exercised with no engine.
    /// </summary>
    internal sealed class FakeZdo : IZdoHandle
    {
        private readonly Dictionary<string, object> store = new Dictionary<string, object>();

        public bool IsValid { get; set; } = true;
        public bool IsOwner { get; set; } = true;
        public int ClaimCount { get; private set; }

        public void ClaimOwnership() { ClaimCount++; IsOwner = true; }

        public int GetInt(string key, int fallback = 0) => store.TryGetValue(key, out var v) && v is int i ? i : fallback;
        public long GetLong(string key, long fallback = 0L) => store.TryGetValue(key, out var v) && v is long l ? l : fallback;
        public bool GetBool(string key, bool fallback = false) => store.TryGetValue(key, out var v) && v is bool b ? b : fallback;
        public string GetString(string key, string fallback = "") => store.TryGetValue(key, out var v) && v is string s ? s : fallback;

        public void SetInt(string key, int value) => store[key] = value;
        public void SetLong(string key, long value) => store[key] = value;
        public void SetBool(string key, bool value) => store[key] = value;
        public void SetString(string key, string value) => store[key] = value;
    }

    /// <summary>
    /// Guards <see cref="ZdoAccess"/> — the engine-free ZDO policy the *Tag family hand-rolls
    /// (arch review §3.1). These assertions used to be possible only by placing a piece in-world
    /// and relogging; now they are unit tests. The three rules under test: ghost = no-op, claim
    /// before write, TryGet replaces sentinels.
    /// </summary>
    public class ZdoAccessTests
    {
        private const string Key = "SBPR_TestKey";

        // ── Rule 1: ghost (invalid handle) = no-op read/write ────────────────────────────────
        [Fact]
        public void GhostHandle_Write_IsNoOp_ReturnsFalse()
        {
            var ghost = new FakeZdo { IsValid = false };
            Assert.False(ZdoAccess.Write(ghost, Key, 42L));
            Assert.Equal(0, ghost.ClaimCount);            // never claimed on a ghost
        }

        [Fact]
        public void GhostHandle_Read_ReturnsFallback()
        {
            var ghost = new FakeZdo { IsValid = false };
            Assert.Equal(99L, ZdoAccess.ReadLong(ghost, Key, 99L));
            Assert.Equal("fb", ZdoAccess.ReadString(ghost, Key, "fb"));
        }

        [Fact]
        public void NullHandle_IsTreatedAsGhost()
        {
            Assert.False(ZdoAccess.Write(null, Key, 1L));
            Assert.Equal(7, ZdoAccess.ReadInt(null, Key, 7));
            Assert.False(ZdoAccess.TryGetLong(null, Key, out var v));
            Assert.Equal(0L, v);
        }

        // ── Rule 2: claim ownership before a write, but only when not already owner ───────────
        [Fact]
        public void Write_WhenNotOwner_ClaimsThenWrites()
        {
            var zdo = new FakeZdo { IsValid = true, IsOwner = false };
            Assert.True(ZdoAccess.Write(zdo, Key, 123L));
            Assert.Equal(1, zdo.ClaimCount);              // claimed exactly once
            Assert.Equal(123L, zdo.GetLong(Key));         // and the write landed
        }

        [Fact]
        public void Write_WhenAlreadyOwner_DoesNotClaimAgain()
        {
            var zdo = new FakeZdo { IsValid = true, IsOwner = true };
            Assert.True(ZdoAccess.Write(zdo, Key, "v"));
            Assert.Equal(0, zdo.ClaimCount);              // already owner → no claim
            Assert.Equal("v", zdo.GetString(Key));
        }

        [Fact]
        public void Write_NullString_StoresEmpty_NotNull()
        {
            var zdo = new FakeZdo();
            Assert.True(ZdoAccess.Write(zdo, Key, (string)null!));
            Assert.Equal("", zdo.GetString(Key));         // null coalesced to "" (matches the tags)
        }

        // ── Rule 3: TryGet replaces the null-as-value sentinel dance ──────────────────────────
        [Fact]
        public void TryGetLong_UnstampedSentinel_IsAbsent()
        {
            var zdo = new FakeZdo();                        // nothing written → GetLong returns 0 (sentinel)
            Assert.False(ZdoAccess.TryGetLong(zdo, Key, out var v));
            Assert.Equal(0L, v);
        }

        [Fact]
        public void TryGetLong_NonSentinelValue_IsPresent()
        {
            var zdo = new FakeZdo();
            ZdoAccess.Write(zdo, Key, 555L);
            Assert.True(ZdoAccess.TryGetLong(zdo, Key, out var v));
            Assert.Equal(555L, v);
        }

        [Fact]
        public void TryGetString_EmptyIsAbsent_NonEmptyIsPresent()
        {
            var zdo = new FakeZdo();
            Assert.False(ZdoAccess.TryGetString(zdo, Key, out var a));
            Assert.Equal("", a);

            ZdoAccess.Write(zdo, Key, "red");
            Assert.True(ZdoAccess.TryGetString(zdo, Key, out var b));
            Assert.Equal("red", b);
        }

        [Fact]
        public void RoundTrip_AllTypes()
        {
            var zdo = new FakeZdo();
            ZdoAccess.Write(zdo, "i", 7);
            ZdoAccess.Write(zdo, "l", 8L);
            ZdoAccess.Write(zdo, "b", true);
            ZdoAccess.Write(zdo, "s", "nine");
            Assert.Equal(7, ZdoAccess.ReadInt(zdo, "i"));
            Assert.Equal(8L, ZdoAccess.ReadLong(zdo, "l"));
            Assert.True(ZdoAccess.ReadBool(zdo, "b"));
            Assert.Equal("nine", ZdoAccess.ReadString(zdo, "s"));
        }
    }
}
