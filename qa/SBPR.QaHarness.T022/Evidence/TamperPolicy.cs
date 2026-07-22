// ============================================================================
//  QA-M4 controlled-tamper policy (ADR-0009 §4 firewall, threat T5, PR408 §3.9) — M4.
// ----------------------------------------------------------------------------
//  TamperPolicy — the static, reviewed-like-code decision that a TamperField
//  primitive may run. Tamper may REPLACE or REMOVE an EXISTING allowlisted key
//  in m_customData on an EXACT tracked THROWAWAY item only, to prove degrade
//  (AT-QA-TAMPER-DEGRADES). It may NEVER add or copy a valid signature field.
//
//  Enforced, fixed-order (ADR-0009 §4 / PR408 §3.9):
//    1. item MUST be a tracked run-scoped throwaway (never a legit item/store),
//    2. operation MUST be replace|remove (an 'add' is not a tamper at all),
//    3. field MUST NOT be a signature key (prefix guard — never add/copy a sig),
//    4. field MUST be in the static tamperable allowlist,
//    5. field MUST already be PRESENT (replace/remove of an EXISTING key only).
//
//  The allowlist + signature prefixes are DATA reviewed like code. They mirror
//  qa/prebuild-m4/contracts.py DEFAULT_TAMPER_FIELD_ALLOWLIST / SIGNATURE_KEY_PREFIXES
//  and evidence.py validate_tamper (reviewed t_d5a29850 prebuild). The canonical
//  net48 helper reaches ItemDrop.ItemData.m_customData (PR408 §3.9) behind THIS gate.
//
//  Engine-free: System.* only.
// ============================================================================
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Evidence
{
    /// <summary>The kind of controlled edit a tamper primitive requests. Only replace/remove are tampers.</summary>
    public enum TamperOperation
    {
        /// <summary>Replace the value of an existing allowlisted key (proves degrade).</summary>
        Replace = 1,
        /// <summary>Remove an existing allowlisted key (proves degrade).</summary>
        Remove = 2,
    }

    /// <summary>
    /// The static tamper firewall (ADR-0009 §4). All decisions are pure and fail-closed; there is
    /// deliberately no code path that ADDS a key, so a signature can never be minted/copied here.
    /// </summary>
    public static class TamperPolicy
    {
        /// <summary>The ONLY m_customData keys a throwaway item may have tampered — reviewed like code.</summary>
        public static readonly IReadOnlyCollection<string> DefaultFieldAllowlist =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "sbpr_workmanship_display",
                "sbpr_workmanship_grade_label",
            };

        /// <summary>
        /// Decide whether a tamper is permitted. Returns <see cref="EvidenceReason.None"/> when allowed,
        /// else the specific fail-closed reason. <paramref name="presentKeys"/> is the exact set of keys
        /// currently on the item; <paramref name="isThrowawayItem"/> is true only for an item in the run's
        /// throwaway ledger set.
        /// </summary>
        public static EvidenceReason Validate(
            string? fieldName,
            IEnumerable<string>? presentKeys,
            bool isThrowawayItem,
            TamperOperation operation,
            IReadOnlyCollection<string>? allowlist = null)
        {
            var allow = allowlist ?? DefaultFieldAllowlist;

            // 1. Never touch anything but a tracked throwaway item.
            if (!isThrowawayItem) return EvidenceReason.TamperItemNotThrowaway;

            // 2. Only replace/remove are tampers. (Add is structurally unrepresentable in
            //    TamperOperation, but guard defensively against an undefined enum value.)
            if (operation != TamperOperation.Replace && operation != TamperOperation.Remove)
                return EvidenceReason.TamperWouldAddSignature;

            if (string.IsNullOrEmpty(fieldName)) return EvidenceReason.TamperFieldNotAllowlisted;

            // 3. Never target a signature-shaped key (prefix guard beyond the literal list).
            if (ItemContinuity.LooksLikeSignature(fieldName!)) return EvidenceReason.TamperWouldAddSignature;

            // 4. Must be an explicitly allowlisted field.
            bool allowed = false;
            foreach (var a in allow)
            {
                if (string.Equals(a, fieldName, StringComparison.Ordinal)) { allowed = true; break; }
            }
            if (!allowed) return EvidenceReason.TamperFieldNotAllowlisted;

            // 5. Must already be present — replace/remove of an EXISTING key only (never an add).
            var present = presentKeys as ICollection<string> ?? new HashSet<string>(presentKeys ?? Array.Empty<string>(), StringComparer.Ordinal);
            bool found = false;
            foreach (var k in present)
            {
                if (string.Equals(k, fieldName, StringComparison.Ordinal)) { found = true; break; }
            }
            if (!found) return EvidenceReason.TamperFieldNotPresent;

            return EvidenceReason.None;
        }
    }
}
