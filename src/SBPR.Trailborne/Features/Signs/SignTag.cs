using UnityEngine;

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
            NeutralizePostFootColliderIfPlaced();
            MigrateLegacyColor();
            ApplyColorsFromZdo();
        }

        /// <summary>
        /// Disable the kitbashed post-foot ground-contact collider
        /// (<see cref="SignPostFootCollider"/>) on a PLACED sign so it can never steal the
        /// Sign's E-to-write / paint raycast — the BOARD stays the sole interact/paint
        /// target (regression guard AT-4, parent spec t_1dc88742).
        ///
        /// That collider exists ONLY to make the placement ghost seat the post FLUSH: the
        /// vanilla build seat drives the lowest enabled, non-trigger collider's AABB to the
        /// ground, and without a foot-level collider the board's crown-lifted interact
        /// collider was the lowest one, burying the 2m post ~3/4 (the bug this fixes). The
        /// seated transform is baked in at placement time, so once we're on a placed
        /// instance the collider has already done its job and is pure liability — disable it.
        ///
        /// CRITICAL — placed-only gate. This MUST NOT run on the placement GHOST: the ghost
        /// has no ZDO (vanilla sets <c>ZNetView.m_forceDisableInit</c> while instantiating
        /// it) and still needs the collider ENABLED to compute the flush seat. We detect
        /// "real placed instance" exactly as the rest of this class does — a live ZDO
        /// (<c>nview.GetZDO() != null</c>); the ghost fails that check and keeps its collider,
        /// so seating is preserved. Idempotent + headless-safe (no-op without a ZNetView).
        /// </summary>
        private void NeutralizePostFootColliderIfPlaced()
        {
            // Ghost (no ZDO) → leave the collider enabled so the post seats flush. Only a
            // truly placed sign reaches the disable path.
            if (nview == null || nview.GetZDO() == null) return;

            foreach (var marker in GetComponentsInChildren<SignPostFootCollider>(includeInactive: true))
            {
                if (marker == null) continue;
                foreach (var col in marker.GetComponents<Collider>())
                {
                    if (col != null) col.enabled = false;
                }
            }
        }

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
