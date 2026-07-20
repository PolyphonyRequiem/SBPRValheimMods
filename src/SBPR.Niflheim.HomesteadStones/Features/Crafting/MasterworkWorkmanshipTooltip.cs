using System;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Application.Crafting;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Features.Progression;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    /// <summary>
    /// T022 remediation — the net48 in-world PRESENTATION seam that makes a Masterwork Workmanship VISIBLE on
    /// a joined client (the "not just host logs" artifact the T022 QA requires) AND drives the keyless
    /// server-validated tamper-degrade. It postfixes the vanilla static <c>ItemDrop.GetTooltip</c> (decomp
    /// :58293) — the single string vanilla builds for every item hover/craft panel — and appends one
    /// deterministic Workmanship line ONLY when the stamp on that exact instance has been CONFIRMED VALID by
    /// the server:
    ///
    ///   * On the authoritative HOST the composed integrity key is present, so the seam validates the stamp
    ///     directly (<see cref="WorkmanshipCodec.Read"/>) — the listen-host path.
    ///   * On a PURE joined CLIENT there is no key. The seam reads the stamp keylessly
    ///     (<see cref="WorkmanshipCodec.TryReadRaw"/>): a well-formed stamp whose provenance id the shared
    ///     <see cref="MasterworkClientState.Verdicts"/> cache already reports Valid renders the line; an
    ///     unconfirmed one requests a server validation (bounded, once per provenance id) and renders NOTHING
    ///     until the verdict arrives. A stamp the server reports Tampered (or a structurally malformed one)
    ///     NEVER renders — it degrades to a plain vanilla tooltip. The key never reaches the client.
    ///
    /// This is a THIN, additive presentation adapter (ADR-0006): it reads one instance's existing custom data
    /// and appends text; it mutates no item, prefab, or shared data. References Valheim (ItemDrop, ZNet) →
    /// net48-only, NOT link-compiled into net8. The codec/verdict-cache it drives are fully unit-tested.
    /// </summary>
    [HarmonyPatch]
    internal static class MasterworkWorkmanshipTooltip
    {
        // The one deterministic visible line. Kept in sync with the issued property (Workmanship=Masterwork).
        private const string WorkmanshipLine = "\n<color=#FFDF7F>Workmanship: Masterwork</color>";

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
        [HarmonyPostfix]
        private static void GetTooltip_Postfix(ItemDrop.ItemData item, ref string __result)
        {
            try
            {
                if (item == null) return;
                if (item.m_customData == null || item.m_customData.Count == 0) return;

                var accessor = new ItemDataMetadataAccessor(item);

                if (ConfirmedValid(accessor))
                    __result = (__result ?? string.Empty) + WorkmanshipLine;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship tooltip postfix threw (ignored): " + ex.Message);
            }
        }

        /// <summary>Whether the Workmanship stamp on this instance is confirmed genuine, by the authoritative
        /// path available. Host: validate directly under the composed key. Client: render only what the server
        /// already confirmed Valid, requesting a verdict once for a not-yet-seen well-formed stamp. Fail closed
        /// everywhere else (absent / malformed / unconfirmed / tampered ⇒ no line).</summary>
        private static bool ConfirmedValid(ItemDataMetadataAccessor accessor)
        {
            // HOST path: the integrity key is armed here, so validate directly.
            var key = MasterworkIssuanceObserver.Armed;
            var znet = ZNet.instance;
            if (key != null && znet != null && znet.IsServer())
                return WorkmanshipCodec.Read(accessor, key).State == WorkmanshipReadState.Valid;

            // PURE CLIENT path: keyless read + server verdict cache, BOUND TO THE COMPLETE SIGNED STAMP.
            var raw = WorkmanshipCodec.TryReadRaw(accessor, out var stamp, out string token);
            if (raw != WorkmanshipCodec.RawReadState.Present) return false;   // absent/malformed ⇒ vanilla.

            // The verdict must be valid ONLY for the exact bytes on the item right now. Key the cache by the
            // complete signed-stamp fingerprint (every signed field + value), NOT the provenance id — so a
            // post-validation tamper that mutates prop_value while retaining prov_id/token changes the
            // fingerprint, misses the cache, and forces a fresh server validation (which rejects it) rather than
            // reusing a stale Valid.
            string fingerprint = WorkmanshipCodec.Fingerprint(accessor);

            var verdicts = MasterworkClientState.Verdicts;
            if (verdicts.HasVerdict(fingerprint))
                return verdicts.IsConfirmedValid(fingerprint);             // confirmed valid for THESE bytes ⇒ line.

            // First time we see this exact stamp fingerprint (fresh, transferred, OR mutated since last verdict):
            // ask the server, render nothing this frame (fail closed until the verdict for these exact bytes
            // lands). Bounded — the cache records the answer per fingerprint so we do not re-ask unchanged bytes.
            MasterworkDedicatedDeliveryObserver.RequestValidation(stamp, token, fingerprint);
            return false;
        }
    }
}
