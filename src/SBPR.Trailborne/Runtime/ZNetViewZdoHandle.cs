using UnityEngine;
using SBPR.Trailborne.Core.Zdo;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Shell adapter that satisfies the Core's <see cref="IZdoHandle"/> seam with a live Valheim
    /// <c>ZNetView</c> + its <c>ZDO</c>. This is the PROD adapter across the ZDO seam (the test
    /// suite's in-memory FakeZdo is the test adapter — two real adapters justify the seam).
    ///
    /// <para>Folds the two engine facts every tag needs behind the interface: the ghost guard
    /// (<see cref="IsValid"/> = live <c>ZNetView</c> with a non-null <c>ZDO</c>) and ownership
    /// (<see cref="IsOwner"/> / <see cref="ClaimOwnership"/>). All get/set calls forward to the
    /// live <c>ZDO</c> by the caller's string key — the same keys the tags already use (R3: the
    /// adapter moves ACCESS through the seam, never key VALUES). No <c>ZNetView</c>/<c>ZDO</c>
    /// type crosses into the Core; only this shell type touches them.</para>
    ///
    /// <para>Struct (not class): one is created per access from a resolved <c>ZNetView</c>, so a
    /// value type avoids a per-read heap allocation on the tag hot paths (e.g. the portal's 10 Hz
    /// grow poll). It caches the resolved <c>ZDO</c> at construction; callers create a fresh handle
    /// when they need a fresh ownership/validity snapshot (cheap).</para>
    /// </summary>
    internal readonly struct ZNetViewZdoHandle : IZdoHandle
    {
        private readonly ZNetView? nview;
        private readonly ZDO? zdo;

        internal ZNetViewZdoHandle(ZNetView? nview)
        {
            this.nview = nview;
            this.zdo = nview != null ? nview.GetZDO() : null;
        }

        public bool IsValid => this.nview != null && this.zdo != null;

        public bool IsOwner => this.IsValid && this.nview!.IsOwner();

        public void ClaimOwnership()
        {
            if (this.IsValid && !this.nview!.IsOwner()) this.nview.ClaimOwnership();
        }

        public int GetInt(string key, int fallback = 0) => this.IsValid ? this.zdo!.GetInt(key, fallback) : fallback;
        public long GetLong(string key, long fallback = 0L) => this.IsValid ? this.zdo!.GetLong(key, fallback) : fallback;
        public bool GetBool(string key, bool fallback = false) => this.IsValid ? this.zdo!.GetBool(key, fallback) : fallback;
        public string GetString(string key, string fallback = "") => this.IsValid ? this.zdo!.GetString(key, fallback) : fallback;

        public void SetInt(string key, int value) { if (this.IsValid) this.zdo!.Set(key, value); }
        public void SetLong(string key, long value) { if (this.IsValid) this.zdo!.Set(key, value); }
        public void SetBool(string key, bool value) { if (this.IsValid) this.zdo!.Set(key, value); }
        public void SetString(string key, string value) { if (this.IsValid) this.zdo!.Set(key, value ?? ""); }
    }
}
