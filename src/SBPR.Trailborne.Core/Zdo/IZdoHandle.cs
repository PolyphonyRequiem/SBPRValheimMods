namespace SBPR.Trailborne.Core.Zdo
{
    /// <summary>
    /// The engine-free seam over a live ZDO (Valheim's networked, persisted per-instance
    /// key/value store) plus its owning <c>ZNetView</c>'s ownership surface. The Core's ZDO
    /// policy (<see cref="ZdoAccess"/>) and the domain rules are written against THIS, never
    /// against <c>ZNetView</c>/<c>ZDO</c> — so they unit-test against an in-memory
    /// <c>FakeZdo</c> with no engine.
    ///
    /// <para><b>Why a handle, not the ZDO directly.</b> The shell's <c>ZNetViewZdoHandle</c>
    /// adapter folds the two things every tag needs — "is there a live ZDO?" (the ghost guard)
    /// and "who owns it?" (the owner-claim gate) — behind one non-engine interface. No
    /// <c>ZNetView</c> or <c>ZDO</c> type crosses this boundary, so the Core references no
    /// Valheim assembly (enforced by the build: the Core csproj has no game reference).</para>
    ///
    /// <para><b>Method surface</b> mirrors the vanilla <c>ZDO</c> get/set family the tags
    /// actually call (verified against assembly_valheim): <c>GetInt/GetLong/GetBool/GetString</c>
    /// with a caller-supplied default, and the matching owner-side setters. Keys are the tags'
    /// existing <c>const string Zdo*</c> literals — the handle moves ACCESS code, never key
    /// VALUES (wire-contract R3: a key rename orphans every placed instance).</para>
    /// </summary>
    public interface IZdoHandle
    {
        /// <summary>True when this handle wraps a live, valid ZDO. False for a placement GHOST
        /// (preview, no ZDO): every read returns the caller's default and every write is a no-op,
        /// so a ghost is inert by construction.</summary>
        bool IsValid { get; }

        /// <summary>True when the local peer owns this ZDO (may mutate it without a claim).</summary>
        bool IsOwner { get; }

        /// <summary>Claim ownership so a subsequent write is authoritative. No-op if already owner.</summary>
        void ClaimOwnership();

        int GetInt(string key, int fallback = 0);
        long GetLong(string key, long fallback = 0L);
        bool GetBool(string key, bool fallback = false);
        string GetString(string key, string fallback = "");

        void SetInt(string key, int value);
        void SetLong(string key, long value);
        void SetBool(string key, bool value);
        void SetString(string key, string value);
    }
}
