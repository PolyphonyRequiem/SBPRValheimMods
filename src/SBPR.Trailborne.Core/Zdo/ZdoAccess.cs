namespace SBPR.Trailborne.Core.Zdo
{
    /// <summary>
    /// The engine-free ZDO-access POLICY every SBPR <c>*Tag</c> hand-rolls today, hoisted into
    /// one tested place. Encodes the three rules the tags repeat verbatim (the review's §3.1
    /// duplication — "Mirrors SignTag.WriteColors' owner-claim shape", "Shared verbatim …"):
    ///
    /// <list type="number">
    ///   <item><b>Ghost = no-op.</b> A null / invalid handle (placement preview, zone-unloaded)
    ///     reads back the caller's fallback and swallows writes, so a ghost is inert.</item>
    ///   <item><b>Claim before write.</b> A write claims ownership first if the local peer isn't
    ///     already the owner, so the mutation is authoritative.</item>
    ///   <item><b>TryGet over sentinels.</b> A <c>bool + out</c> read replaces the
    ///     <c>return default</c> / <c>return ""</c> / <c>return 0L</c> sentinel habit — the caller
    ///     learns "was there a live value?" instead of guessing from a magic default.</item>
    /// </list>
    ///
    /// <para>Pure functions over <see cref="IZdoHandle"/>: no engine, unit-tested against an
    /// in-memory <c>FakeZdo</c>. Callers pass the ZDO KEY (their own existing <c>const string
    /// Zdo*</c> literal — R3: this policy moves access code, never key values).</para>
    /// </summary>
    public static class ZdoAccess
    {
        // ── Owner-gated writes (ghost = no-op; claim before write). Return true if the write
        //    actually landed (live + now-owned), false on a ghost. ────────────────────────────

        public static bool Write(IZdoHandle? zdo, string key, int value)
        {
            if (zdo == null || !zdo.IsValid) return false;
            if (!zdo.IsOwner) zdo.ClaimOwnership();
            zdo.SetInt(key, value);
            return true;
        }

        public static bool Write(IZdoHandle? zdo, string key, long value)
        {
            if (zdo == null || !zdo.IsValid) return false;
            if (!zdo.IsOwner) zdo.ClaimOwnership();
            zdo.SetLong(key, value);
            return true;
        }

        public static bool Write(IZdoHandle? zdo, string key, bool value)
        {
            if (zdo == null || !zdo.IsValid) return false;
            if (!zdo.IsOwner) zdo.ClaimOwnership();
            zdo.SetBool(key, value);
            return true;
        }

        public static bool Write(IZdoHandle? zdo, string key, string value)
        {
            if (zdo == null || !zdo.IsValid) return false;
            if (!zdo.IsOwner) zdo.ClaimOwnership();
            zdo.SetString(key, value ?? "");
            return true;
        }

        // ── Ghost-safe reads (fallback on a ghost). The plain Read* mirror the tags' current
        //    "return the value or the default" reads. ────────────────────────────────────────

        public static int ReadInt(IZdoHandle? zdo, string key, int fallback = 0)
            => (zdo != null && zdo.IsValid) ? zdo.GetInt(key, fallback) : fallback;

        public static long ReadLong(IZdoHandle? zdo, string key, long fallback = 0L)
            => (zdo != null && zdo.IsValid) ? zdo.GetLong(key, fallback) : fallback;

        public static bool ReadBool(IZdoHandle? zdo, string key, bool fallback = false)
            => (zdo != null && zdo.IsValid) ? zdo.GetBool(key, fallback) : fallback;

        public static string ReadString(IZdoHandle? zdo, string key, string fallback = "")
            => (zdo != null && zdo.IsValid) ? zdo.GetString(key, fallback) : fallback;

        // ── TryGet: bool + out, the sentinel-killer. "Present" means the handle is live AND the
        //    stored value differs from the sentinel default (the tags' own "unstamped == 0L" /
        //    "unset == \"\"" convention). This is the structural null-as-value reduction (§3.1). ──

        /// <summary>True + the stored long when the handle is live and the value is non-sentinel
        /// (!= <paramref name="sentinel"/>, default 0L — matching the tags' "0 == unstamped").</summary>
        public static bool TryGetLong(IZdoHandle? zdo, string key, out long value, long sentinel = 0L)
        {
            if (zdo == null || !zdo.IsValid) { value = sentinel; return false; }
            value = zdo.GetLong(key, sentinel);
            return value != sentinel;
        }

        /// <summary>True + the stored int when live and non-sentinel (!= <paramref name="sentinel"/>, default 0).</summary>
        public static bool TryGetInt(IZdoHandle? zdo, string key, out int value, int sentinel = 0)
        {
            if (zdo == null || !zdo.IsValid) { value = sentinel; return false; }
            value = zdo.GetInt(key, sentinel);
            return value != sentinel;
        }

        /// <summary>True + the stored string when live and non-empty (the tags' "unset == \"\"" convention).</summary>
        public static bool TryGetString(IZdoHandle? zdo, string key, out string value)
        {
            if (zdo == null || !zdo.IsValid) { value = ""; return false; }
            value = zdo.GetString(key, "");
            return !string.IsNullOrEmpty(value);
        }
    }
}
