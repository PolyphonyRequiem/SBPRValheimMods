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
            MigrateLegacyColor();
            ApplyColorsFromZdo();
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
        /// text never changed). The text widget is re-tinted every apply (even when the
        /// id is unchanged) because the vanilla <c>Sign.UpdateText</c> 2 Hz poll
        /// reassigns <c>m_textWidget.text</c> and can construct/replace the widget after
        /// our first apply; cheap idempotent re-set keeps the colour pinned.
        /// </summary>
        public void ApplyColorsFromZdo()
        {
            // Capture the TMP text widget's ORIGINAL face colour once, before we ever
            // paint it, so clearing the text color can revert the letters to vanilla.
            if (originalTextColor == null)
            {
                var orig = Signs.TryReadTextColor(gameObject);
                if (orig != null) originalTextColor = orig;
            }

            string textColor = ReadTextColor();
            if (textColor != lastAppliedText)
            {
                lastAppliedText = textColor;
                if (!string.IsNullOrEmpty(textColor) && Signs.ColorValues.TryGetValue(textColor, out var tcol))
                    Signs.TintBoard(gameObject, tcol);
                else
                    Signs.RestoreBoard(gameObject); // "remove text color" → vanilla wood board
            }

            // The text TONE drives the TMP letters too (Issue 4b). Re-pin every apply
            // when set (survives the vanilla UpdateText repaint that only reassigns
            // .text); when unset, revert to the captured original text colour.
            if (!string.IsNullOrEmpty(textColor) && Signs.ColorValues.TryGetValue(textColor, out var ttcol))
                Signs.TintText(gameObject, ttcol);
            else if (originalTextColor != null)
                Signs.TintText(gameObject, originalTextColor.Value);

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
