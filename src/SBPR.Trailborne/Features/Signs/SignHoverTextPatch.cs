using HarmonyLib;
using UnityEngine;
using SBPR.Trailborne.Features.MarkerSigns;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Harmony <b>postfix</b> on <c>Sign.GetHoverText</c> that <b>appends</b> a state-aware
    /// pin hint to the crosshair hover text of OUR Marker Signs only (v2 hover-hint card
    /// t_7816c0b0; impl-spec §4A). Vanilla Painted Signs (SignTag, no MarkerSignTag) and
    /// all other signs are untouched.
    ///
    /// Why a postfix on <c>Sign.GetHoverText</c> (decomp-settled, §4A.1/4A.2):
    ///   • <c>Hud.UpdateCrosshair</c> resolves exactly ONE Hoverable via
    ///     <c>GetComponentInParent&lt;Hoverable&gt;()</c> (decomp :39699) and calls
    ///     <c>.GetHoverText()</c> (:39702).
    ///   • <c>Sign : Hoverable</c> (:121412) is added to the marker BEFORE
    ///     <c>MarkerSignTag</c> (<c>MarkerSigns.cs</c>: <c>AddComponent&lt;Sign&gt;()</c> at
    ///     :176, then <c>AddComponent&lt;MarkerSignTag&gt;()</c> at :222), so <c>Sign</c> wins
    ///     the single-Hoverable query and <c>Sign.GetHoverText</c> is the method that fires.
    ///     Route (b) — adding a second <c>Hoverable</c> on the tag — was REJECTED: it can't
    ///     displace the first-added <c>Sign</c> Hoverable. So we augment the method that
    ///     already fires.
    ///
    /// Structure: this postfix is a thin adapter. It reads the three gate booleans from live
    /// Unity/Valheim state and delegates the decision (which AT each gate maps to) to the pure,
    /// Unity-free <see cref="SignHoverHintText.ComputeHintSuffix"/>, then localizes the returned
    /// suffix on the way out. Keeping the decision in a Unity-free function lets the markers-only
    /// and ward gates (AT-MARKER-HINT-5 / -WARD) and the state-flip (AT-MARKER-HINT-1/2/3) be
    /// asserted headlessly while the shipped path compiles the exact same source — no drift.
    ///   • <b>Markers-only (AT-MARKER-HINT-5):</b> <c>GetComponent&lt;MarkerSignTag&gt;()</c> null
    ///     ⇒ <c>isMarker</c> false ⇒ empty suffix, the same tag-gate as <c>SignInteractPatch.cs:44</c>.
    ///   • <b>Ward gate (AT-MARKER-HINT-WARD):</b> mirror vanilla — when the player lacks ward
    ///     access, vanilla <c>Sign.GetHoverText</c> returns early with text only (decomp :121451,
    ///     <c>PrivateArea.CheckAccess(..., flash:false)</c>); we append NOTHING.
    ///   • <b>State-aware + live (AT-MARKER-HINT-1/2/3):</b> the verb flips with
    ///     <c>ReadPinned()</c>, the live <c>SBPR_Pinned</c> ZDO read. Because <c>GetHoverText</c>
    ///     runs every crosshair frame, the wording flips on the next hover frame after a Shift+E
    ///     toggle with zero caching / extra plumbing.
    ///   • <b>Append, never replace (AT-MARKER-HINT-4):</b> <c>__result += …</c>, preserving the
    ///     vanilla typed-text line and the primary <c>[$KEY_Use] $piece_use</c> hint above it.
    ///   • <b>Key tokens (AT-MARKER-HINT-6):</b> the suffix carries the raw <c>$KEY_Use</c> token,
    ///     localized here on the way out (the <c>CairnInteractable.cs:58-65</c> pattern — never a
    ///     custom <c>$piece_*</c> token, which leaked as a literal in the 2026-06-05 sign bug).
    ///   • <b>No-ZDO / ghost path:</b> <c>ReadPinned()</c> returns false with no ZDO
    ///     (<c>MarkerSignTag.cs:123</c>), so a ghost simply reads "Pin to map" — no NRE.
    ///
    /// Clean-room (ADR-0001): patching vanilla <c>Sign.GetHoverText</c> and reading vanilla
    /// <c>PrivateArea</c>/<c>Localization</c> is base-game adaptation — fair game. No third-party
    /// mod code. No new ZDO field, no SpecCheck change, no prefab/registration change (§4A.6).
    /// </summary>
    [HarmonyPatch(typeof(Sign), nameof(Sign.GetHoverText))]
    public static class SignHoverTextPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Sign __instance, ref string __result)
        {
            // Read the three gate booleans from live state, short-circuited to mirror the
            // spec's gate ordering (§4A.3): markers-only first (a plain Painted Sign / any
            // non-marker never touches PrivateArea or ReadPinned — the common-case fast path),
            // then the ward gate, and ReadPinned ONLY when ward access is granted (so the
            // per-frame hover hot path skips the ZDO read on the ward-denied path).
            var marker = __instance.GetComponent<MarkerSignTag>();
            bool isMarker = marker != null;
            bool hasWardAccess = isMarker
                && PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false);
            bool pinned = hasWardAccess && marker!.ReadPinned();

            // Pure decision: empty suffix unless (marker AND ward-access); verb flips with pinned.
            string suffix = SignHoverHintText.ComputeHintSuffix(isMarker, hasWardAccess, pinned);
            if (suffix.Length == 0) return;

            // Localize so $KEY_Use renders the player's actual bound use key (e.g. "E") instead
            // of leaking the literal token. Append — never replace (AT-MARKER-HINT-4).
            __result += Localization.instance != null
                ? Localization.instance.Localize(suffix)
                : suffix;
        }
    }
}
