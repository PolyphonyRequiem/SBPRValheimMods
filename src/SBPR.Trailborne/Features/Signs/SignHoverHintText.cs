namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Pure (Unity-free) decision table for the Marker Sign hover postfix
    /// (<see cref="SignHoverTextPatch"/>, impl-spec §4A). Deliberately references no
    /// UnityEngine / HarmonyLib / Valheim type so the full gate + wording logic can be
    /// exercised headless — this is the "focused test on the postfix's append logic with a
    /// stubbed <c>ReadPinned()</c>" the card calls for (the <c>pinned</c> argument <em>is</em>
    /// the stub; <c>isMarker</c>/<c>hasWardAccess</c> stub the two Unity-bound gates).
    ///
    /// The postfix (<see cref="SignHoverTextPatch"/>) is a thin adapter: it evaluates the three
    /// gate booleans from live Unity/Valheim state (the <c>MarkerSignTag</c> component, vanilla
    /// <c>PrivateArea.CheckAccess</c>, and the marker's live <c>ReadPinned()</c> ZDO read), hands
    /// them here, and localizes the returned suffix on the way out. Both the shipped patch and
    /// the self-test compile THIS source, so the asserted gates + wording can never drift from
    /// what ships. Runtime behaviour is identical to the inline patch shape in impl-spec §4A.3.
    /// </summary>
    public static class SignHoverHintText
    {
        // Verb wording is LOCKED (impl-spec §4A.4) to match MarkerSignPanel's button labels.
        // Do not invent a third phrasing.
        internal const string VerbPinned   = "Unpin from map";
        internal const string VerbUnpinned = "Pin to map";

        /// <summary>
        /// The raw, pre-localization hint suffix to append to a marker sign's hover text — or
        /// <see cref="string.Empty"/> when no hint should be shown. The full §4A decision table:
        /// <list type="bullet">
        ///   <item><b>AT-MARKER-HINT-5 (markers-only):</b> <paramref name="isMarker"/> false
        ///     (a plain Painted Sign / any non-marker) ⇒ empty ⇒ no "Pin"/"Unpin" substring.</item>
        ///   <item><b>AT-MARKER-HINT-WARD:</b> <paramref name="hasWardAccess"/> false (player lacks
        ///     ward access, the vanilla early-return path) ⇒ empty ⇒ no pin affordance offered.</item>
        ///   <item><b>AT-MARKER-HINT-1/2/3 (state-aware, live):</b> the verb flips with
        ///     <paramref name="pinned"/> — "Pin to map" when not pinned, "Unpin from map" when
        ///     pinned. Live for free because the postfix re-runs per crosshair frame.</item>
        ///   <item><b>AT-MARKER-HINT-6 (key tokens):</b> emits the raw <c>$KEY_Use</c> token
        ///     (localized later to the player's bound use key) behind a literal "Shift" modifier
        ///     (the shipped <c>CairnInteractable.cs:56</c> precedent). Never a hardcoded "E".</item>
        /// </list>
        /// The leading newline keeps the pin line on its own row beneath the vanilla typed-text +
        /// <c>[Use]</c> line, which the postfix preserves by appending (AT-MARKER-HINT-4).
        /// </summary>
        public static string ComputeHintSuffix(bool isMarker, bool hasWardAccess, bool pinned)
        {
            if (!isMarker) return string.Empty;       // AT-MARKER-HINT-5 — markers-only
            if (!hasWardAccess) return string.Empty;  // AT-MARKER-HINT-WARD — mirror vanilla gate

            string verb = pinned ? VerbPinned : VerbUnpinned;
            return $"\n[<color=yellow><b>Shift+$KEY_Use</b></color>] {verb}";
        }
    }
}
