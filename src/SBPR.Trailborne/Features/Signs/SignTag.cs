using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Tag attached to the single Painted Sign clone. Carries the sign's TWO-TONE
    /// painted state as ZDO-backed runtime state (NOT baked into the prefab):
    /// a board/text color (<see cref="Signs.ZdoTextColor"/>) and a separate border
    /// color (<see cref="Signs.ZdoBorderColor"/>). Re-applies both tints on spawn so
    /// they persist across reloads + sync to joined clients (mirrors CairnTag).
    ///
    /// Each color identity is one of Signs.Colors ("red"/"white"/"blue"/"black") or
    /// the empty string for "that slot unset". Owner-only writes via ZNetView.
    ///
    /// Legacy migration: a sign painted under the retired single-color apply-ink
    /// model stored its color in <see cref="Signs.ZdoColor"/> (SBPR_SignColor). On
    /// first spawn we fold any such legacy value into the new text-color field (one
    /// way, idempotent) so old saves keep their color under the two-tone model.
    /// </summary>
    public class SignTag : MonoBehaviour
    {
        private ZNetView? nview;

        // Sentinels: nothing applied yet (force first tint even for the unpainted "").
        private string? lastAppliedText   = null;
        private string? lastAppliedBorder = null;

        // The TMP text widget's ORIGINAL (unpainted) face colour, captured once the
        // first time we touch it, so clearing the text color ("remove text color")
        // can revert the letters to vanilla instead of leaving them stuck on the last
        // paint. Nullable: null = not yet captured.
        private Color? originalTextColor = null;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            SignGeometry.NeutralizeFootColliderIfPlaced(this, nview);
            MigrateLegacyColor();
            ApplyColorsFromZdo();
        }

        // Post-foot collider neutralize (the placed-only disable that keeps the BOARD the
        // sole interact/paint target — regression guard AT-4) is now the SHARED
        // SignGeometry.NeutralizeFootColliderIfPlaced (card t_cc093d04), called from Awake.
        // Behaviour unchanged: ghost (no ZDO) keeps the foot collider enabled so the post
        // seats flush; only a placed instance (live ZDO) disables it. Shared verbatim with
        // MarkerSignTag so the two features can never diverge on this.

        /// <summary>Current board/text color id, or "" if unset.</summary>
        public string ReadTextColor()
        {
            if (nview == null || nview.GetZDO() == null) return "";
            return nview.GetZDO().GetString(Signs.ZdoTextColor, "");
        }

        /// <summary>Current border color id, or "" if unset.</summary>
        public string ReadBorderColor()
        {
            if (nview == null || nview.GetZDO() == null) return "";
            return nview.GetZDO().GetString(Signs.ZdoBorderColor, "");
        }

        /// <summary>
        /// Owner-write BOTH tones at once and re-tint immediately. Pass "" for a slot
        /// to leave it unpainted (border is optional). Returns false if the ZDO isn't
        /// ready (e.g. ghost/uninitialised) so the caller can avoid consuming pigments.
        /// </summary>
        public bool WriteColors(string textColor, string borderColor)
        {
            if (nview == null || nview.GetZDO() == null) return false;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            nview.GetZDO().Set(Signs.ZdoTextColor,   textColor ?? "");
            nview.GetZDO().Set(Signs.ZdoBorderColor, borderColor ?? "");
            ApplyColorsFromZdo();
            return true;
        }

        /// <summary>
        /// Read both ZDO colors and tint board + text + border to match. Empty/unknown
        /// colors leave that element in its vanilla (unpainted wood / default text)
        /// material. Idempotent: skips re-applying an element whose color hasn't changed
        /// since the last apply.
        ///
        /// The TEXT tone drives BOTH the board mesh (<see cref="Signs.TintBoard"/>) AND
        /// the TMP text widget (<see cref="Signs.TintText"/>): §A2.6 "Set Text Color"
        /// colours the written letters, not just the plank (Issue 4b — previously the
        /// text never changed). The text-letter re-pin is delegated to
        /// <see cref="ReapplyTextTint"/> (the single source of truth for the letter
        /// colour). NOTE: this per-APPLY re-pin alone does NOT survive the vanilla
        /// <c>Sign.UpdateText</c> ~2 Hz poll — apply only fires on spawn (Awake) and
        /// paint (WriteColors), and the poll reconstructs/rewrites <c>m_textWidget</c>
        /// AFTER our apply, dropping the colour. The <see cref="SignTextRetintPatch"/>
        /// postfix is what keeps the letters pinned across polls (it calls
        /// <see cref="ReapplyTextTint"/> on the poll's cadence).
        /// </summary>
        public void ApplyColorsFromZdo()
        {
            string textColor = ReadTextColor();
            if (textColor != lastAppliedText)
            {
                lastAppliedText = textColor;
                if (!string.IsNullOrEmpty(textColor) && Signs.ColorValues.TryGetValue(textColor, out var tcol))
                    Signs.TintBoard(gameObject, tcol);
                else
                    Signs.RestoreBoard(gameObject); // "remove text color" → vanilla wood board
            }

            // The text TONE drives the TMP letters too (Issue 4b). Delegated to
            // ReapplyTextTint() — the SINGLE source of truth for the letter colour — so
            // the spawn/paint apply and the Sign.UpdateText-poll postfix can never drift.
            ReapplyTextTint();

            string borderColor = ReadBorderColor();
            if (borderColor != lastAppliedBorder)
            {
                lastAppliedBorder = borderColor;
                if (!string.IsNullOrEmpty(borderColor) && Signs.ColorValues.TryGetValue(borderColor, out var bcol))
                    Signs.TintBorder(gameObject, bcol);
                else
                    Signs.RestoreBorder(gameObject); // "remove border color" → vanilla wood border
            }
        }

        /// <summary>
        /// Re-pin ONLY the TMP letter tint from ZDO. Called from
        /// <see cref="ApplyColorsFromZdo"/> (spawn/paint) AND from the
        /// <see cref="SignTextRetintPatch"/> postfix on <c>Sign.UpdateText</c>, so the
        /// letters' colour survives the vanilla text poll that reconstructs/rewrites
        /// <c>m_textWidget</c> after our paint-time apply. Deliberately does NOT re-walk
        /// the board/border renderers (only the cheap TMP widget set) — keeping it safe
        /// to call on the poll's ~2 Hz cadence. Cheap: TintText's faceColor set early-outs
        /// when the colour is unchanged. Reverts to the captured original colour when the
        /// text slot is unset ("remove text color"). The original (unpainted) face colour
        /// is captured once here, lazily, before we ever paint it.
        /// </summary>
        public void ReapplyTextTint()
        {
            // Capture the TMP text widget's ORIGINAL face colour once, before we ever
            // paint it, so clearing the text color can revert the letters to vanilla.
            if (originalTextColor == null)
            {
                var orig = Signs.TryReadTextColor(gameObject);
                if (orig != null) originalTextColor = orig;
            }

            string textColor = ReadTextColor();
            if (!string.IsNullOrEmpty(textColor) && Signs.ColorValues.TryGetValue(textColor, out var c))
                Signs.TintText(gameObject, c);
            else if (originalTextColor != null)
                Signs.TintText(gameObject, originalTextColor.Value);
        }

        /// <summary>
        /// UNCONDITIONALLY re-pin the BOARD + BORDER mesh tint from ZDO — the single
        /// source of truth for the plank/frame colour, the mesh-layer analogue of
        /// <see cref="ReapplyTextTint"/>. Unlike <see cref="ApplyColorsFromZdo"/> this does
        /// NOT consult the <c>lastApplied*</c> change-detection sentinels: it always re-walks
        /// the renderers and re-writes the per-renderer <c>_Color</c> MPB from the current ZDO
        /// state. That is exactly what the highlight-recovery path needs — after the hammer
        /// support-overlay clears, the renderer's MPB has been wiped but the ZDO + sentinels
        /// are unchanged, so a sentinel-gated re-apply would skip the re-tint and leave the
        /// board stuck on plain wood (bug t_f3310406, AT-SIGN-HIGHLIGHT-REASSERT).
        ///
        /// Does NOT touch the TMP text (that rides the <c>Sign.UpdateText</c> poll via
        /// <see cref="ReapplyTextTint"/>) — the hammer highlight only clobbers the MaterialMan
        /// mesh layer, never the Canvas-renderer letters. Headless-safe (the tint helpers
        /// no-op without renderers). Leaves the sentinels untouched (it re-asserts current
        /// state, it does not change it).
        /// </summary>
        public void ReapplyMeshTint()
        {
            string textColor = ReadTextColor();
            if (!string.IsNullOrEmpty(textColor) && Signs.ColorValues.TryGetValue(textColor, out var tcol))
                Signs.TintBoard(gameObject, tcol);
            else
                Signs.RestoreBoard(gameObject);

            string borderColor = ReadBorderColor();
            if (!string.IsNullOrEmpty(borderColor) && Signs.ColorValues.TryGetValue(borderColor, out var bcol))
                Signs.TintBorder(gameObject, bcol);
            else
                Signs.RestoreBorder(gameObject);
        }

        // Delay (seconds) from the LAST hammer-highlight frame to our mesh re-assert.
        // Vanilla WearNTear fires ResetHighlight 0.2s after the last Highlight; MaterialMan
        // clears our _Color on its next Update (~1 frame later). We re-assert at 0.3s — a
        // ~0.1s margin past that clear so we re-write AFTER MaterialMan's wipe, not before
        // (re-writing before would just be wiped again). CancelInvoke-rearm on every
        // Highlight means this fires once, ~0.3s after the player stops hovering.
        private const float MeshReassertDelay = 0.3f;

        /// <summary>
        /// Schedule a one-shot mesh re-assert <see cref="MeshReassertDelay"/> seconds out,
        /// cancelling any already-pending one. Called from <see cref="SignMeshRetintPatch"/>
        /// on every <c>WearNTear.Highlight</c> tick while the player hovers our sign with the
        /// Hammer, so the re-assert lands just after the support-overlay clears.
        /// </summary>
        public void ScheduleMeshReassert()
        {
            CancelInvoke(nameof(MeshReassertInvoke));
            Invoke(nameof(MeshReassertInvoke), MeshReassertDelay);
        }

        // Invoke target (Unity Invoke needs a no-arg instance method by name).
        private void MeshReassertInvoke() => ReapplyMeshTint();

        /// <summary>
        /// One-way migration of a pre-two-tone save: if the legacy single-color field
        /// (SBPR_SignColor) holds a value and the new text-color field is still empty,
        /// copy it into the text-color field (owner-write) and clear the legacy field
        /// so this runs at most once. No-op on clients (owner-gated) and on signs that
        /// were never painted under the old model.
        /// </summary>
        private void MigrateLegacyColor()
        {
            if (nview == null || nview.GetZDO() == null) return;
            var zdo = nview.GetZDO();
            string legacy = zdo.GetString(Signs.ZdoColor, "");
            if (string.IsNullOrEmpty(legacy)) return;
            string current = zdo.GetString(Signs.ZdoTextColor, "");
            if (!string.IsNullOrEmpty(current)) return; // already migrated / set
            if (!nview.IsOwner()) return;               // only the owner mutates the ZDO

            zdo.Set(Signs.ZdoTextColor, legacy);
            zdo.Set(Signs.ZdoColor, ""); // consume the legacy field so we don't re-migrate
            Plugin.Log.LogInfo($"[Trailborne/M1] Migrated legacy sign color '{legacy}' → SBPR_SignTextColor.");
        }
    }
}
