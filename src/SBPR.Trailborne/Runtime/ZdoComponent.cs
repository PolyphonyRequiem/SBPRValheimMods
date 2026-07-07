using UnityEngine;
using SBPR.Trailborne.Core.Zdo;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Base <c>MonoBehaviour</c> for SBPR's ZDO-backed placed pieces (the <c>*Tag</c> family —
    /// arch review §3.1, Model A). Resolves the instance's <c>ZNetView</c> in <see cref="Awake"/>
    /// and offers the ghost-guarded, owner-gated ZDO access every tag used to hand-roll — the
    /// dance the review found copied verbatim across five tags ("Mirrors SignTag.WriteColors'
    /// owner-claim shape", "Shared verbatim …").
    ///
    /// <para><b>How the seam splits.</b> The DECISION rules (ghost = no-op, claim before write,
    /// TryGet over sentinels) live in the engine-free Core (<see cref="ZdoAccess"/>), unit-tested
    /// with a FakeZdo. This shell base only supplies the live handle (<see cref="ZNetViewZdoHandle"/>)
    /// and forwards to that policy — so a subclass calls <c>WriteLong(key, v)</c> / <c>ReadString(key)</c>
    /// and gets the correct discipline for free, while the tag shrinks to JUST its feature state.</para>
    ///
    /// <para><b>Wire-contract (R3).</b> This base moves ACCESS code, never key VALUES. Every
    /// subclass keeps its own <c>const string Zdo*</c> literals and passes them in; a key rename
    /// would orphan every placed instance, so the base never owns a key.</para>
    ///
    /// <para><b>Subclass contract.</b> A subclass that needs its own <c>Awake</c> MUST override
    /// <see cref="OnZdoAwake"/> (called after the base resolves the ZNetView) rather than hiding
    /// <see cref="Awake"/> — so the <c>nview</c> resolution always runs first. <see cref="HasZdo"/>
    /// is the ghost check (false on a placement preview).</para>
    /// </summary>
    public abstract class ZdoComponent : MonoBehaviour
    {
        /// <summary>The resolved ZNetView (null until Awake, or on a prefab with no ZNetView).</summary>
        protected ZNetView? NView { get; private set; }

        /// <summary>A fresh handle over the current ZNetView/ZDO + ownership snapshot. Cheap
        /// (a readonly struct); create one per access so validity/ownership is never stale.</summary>
        protected IZdoHandle Zdo => new ZNetViewZdoHandle(this.NView);

        /// <summary>True when there's a live ZDO (placed instance), false on a GHOST (preview).
        /// The single ghost check subclasses branch on before doing owner-write work.</summary>
        protected bool HasZdo => this.Zdo.IsValid;

        /// <summary>True when the local peer owns this instance's ZDO (may author-write it).</summary>
        protected bool IsOwner => this.Zdo.IsOwner;

        // Sealed so a subclass can't accidentally hide the ZNetView resolution by declaring its
        // own Awake; feature setup goes in OnZdoAwake, which runs AFTER NView is resolved.
        private void Awake()
        {
            this.NView = GetComponent<ZNetView>();
            OnZdoAwake();
        }

        /// <summary>Subclass setup hook, called once from <see cref="Awake"/> after <see cref="NView"/>
        /// is resolved. Default no-op. A ghost (no live ZDO) still calls this — branch on
        /// <see cref="HasZdo"/> for owner-write work.</summary>
        protected virtual void OnZdoAwake() { }

        // ── Ghost-guarded, owner-gated access (delegates to the Core ZdoAccess policy) ─────────

        protected bool WriteInt(string key, int value) => ZdoAccess.Write(this.Zdo, key, value);
        protected bool WriteLong(string key, long value) => ZdoAccess.Write(this.Zdo, key, value);
        protected bool WriteBool(string key, bool value) => ZdoAccess.Write(this.Zdo, key, value);
        protected bool WriteString(string key, string value) => ZdoAccess.Write(this.Zdo, key, value);

        protected int ReadInt(string key, int fallback = 0) => ZdoAccess.ReadInt(this.Zdo, key, fallback);
        protected long ReadLong(string key, long fallback = 0L) => ZdoAccess.ReadLong(this.Zdo, key, fallback);
        protected bool ReadBool(string key, bool fallback = false) => ZdoAccess.ReadBool(this.Zdo, key, fallback);
        protected string ReadString(string key, string fallback = "") => ZdoAccess.ReadString(this.Zdo, key, fallback);

        /// <summary>Sentinel-free read: true + value when live and non-sentinel (the tags'
        /// "0 == unstamped" / "\"\" == unset" convention). Replaces the null-as-value habit.</summary>
        protected bool TryGetLong(string key, out long value, long sentinel = 0L) => ZdoAccess.TryGetLong(this.Zdo, key, out value, sentinel);
        protected bool TryGetInt(string key, out int value, int sentinel = 0) => ZdoAccess.TryGetInt(this.Zdo, key, out value, sentinel);
        protected bool TryGetString(string key, out string value) => ZdoAccess.TryGetString(this.Zdo, key, out value);
    }
}
