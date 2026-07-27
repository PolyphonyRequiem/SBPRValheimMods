// ============================================================================
//  QA-M4 action/observation adapter seams + labels + product firewall
//  (ADR-0009 §3, §4, §6, PR408 §3.6-§3.10) — M4.
// ----------------------------------------------------------------------------
//  ENGINE-FREE interfaces + inert deterministic fakes for the M3 action/
//  observation primitives the canonical net48 helper wires to vanilla seams.
//  Landing the boundary here (mirroring ControlPlane/GameBindingAdapters.cs)
//  lets the M4 evidence logic be unit-tested headlessly against fakes, exactly
//  like the M1 contract core. NOTHING here registers, listens, patches Harmony,
//  invokes ZRpc, or mutates the game — the real adapters are the net48 slice.
//
//  Each interface method carries an explicit TODO(PR408 §x.y) binding point
//  naming the exact vanilla signature it will bind to — a REFERENCE to the
//  accepted PR #408 map (qa/decomp-map/VANILLA-BINDINGS.md), never a decompiled
//  body (clean-room Chinese wall).
//
//  Also here:
//   * FactSource — the direct-vs-inferred label the runner needs to know whether
//     a fact was DIRECTLY observed from an item (e.g. tooltip text, quality) or
//     INFERRED by the runner correlating receipts. Evidence honesty (§6, T11):
//     a Masterwork stamp is DIRECT only when read off the item's own tooltip.
//   * ProductFirewall — the emission-path invariant that the harness never
//     CLAIMS to have produced product state. A craft receipt records that the
//     helper drove the seam and observed a result; it must never assert the
//     helper minted/signed the stamp (the product issuance seam did). ADR-0009 §4.
//
//  Engine-free: System.* only.
// ============================================================================
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.Evidence
{
    /// <summary>Whether a receipt fact was directly observed off an item or inferred by correlation (ADR-0009 §6).</summary>
    public enum FactSource
    {
        /// <summary>Read directly from the item/world the human would see (tooltip text, quality, world uid). The strong evidence.</summary>
        Direct = 1,
        /// <summary>Derived by the runner correlating multiple receipts (e.g. "transfer preserved" across two clients).</summary>
        Inferred = 2,
    }

    /// <summary>A labeled observed fact: the raw value plus whether it was directly observed or inferred.</summary>
    public sealed class LabeledFact
    {
        public object? Value { get; }
        public FactSource Source { get; }

        public LabeledFact(object? value, FactSource source)
        {
            Value = value;
            Source = source;
        }

        public static LabeledFact Direct(object? value) => new LabeledFact(value, FactSource.Direct);
        public static LabeledFact Inferred(object? value) => new LabeledFact(value, FactSource.Inferred);
    }

    /// <summary>
    /// Client-role bounded action verbs (PR408 §3.6/§3.7/§3.9). Every method is a CONTRACT ONLY; the
    /// canonical net48 helper implements it against the cited vanilla seam behind the single-slot
    /// dispatcher. Client-role only — InventoryGui.instance/Player.m_localPlayer are null on the server.
    /// </summary>
    public interface IActionAdapter
    {
        /// <summary>
        /// Genuine craft through the product issuance seam.
        /// TODO(PR408 §3.6): bind via Harmony/AccessTools to private InventoryGui.OnCraftPressed after
        /// selecting the recipe with SetRecipe(index); let UpdateRecipe->DoCrafting (@1500) run
        /// naturally. Observe RESULT (item present + quality); DoCrafting silently no-ops on unmet reqs.
        /// </summary>
        RedactedReceipt Craft(string recipeName, string station);

        /// <summary>
        /// Upgrade == craft with a non-null upgrade item.
        /// TODO(PR408 §3.6): set InventoryGui.m_craftUpgradeItem to the source item then the same
        /// OnCraftPressed path; DoCrafting computes m_craftUpgradeItem.m_quality + 1. The
        /// source->replacement mapping MUST preserve the continuity key while bumping quality.
        /// </summary>
        RedactedReceipt UpgradeItem(int itemSlot, int targetQuality);

        /// <summary>
        /// Drop into the world for a cross-actor transfer.
        /// TODO(PR408 §3.7): bind Humanoid.DropItem (@767) -> ItemDrop.DropItem (@1646) which Clone()s
        /// the ItemData (m_customData deep-copied @412), preserving the tracked stamp.
        /// </summary>
        RedactedReceipt DropItem(int itemSlot);

        /// <summary>
        /// Receiving-actor pickup (distinct alias).
        /// TODO(PR408 §3.7): resolve nearest world ItemDrop within radius (&lt;= Rmax), then
        /// Humanoid.Pickup(go) (@588). Honor ItemDrop.CanPickup false during the auto-pickup delay by
        /// polling on the dispatcher (no sleeps).
        /// </summary>
        RedactedReceipt PickUpNearest(string itemName, double radius);

        /// <summary>
        /// Controlled degrade — replace/remove an EXISTING allowlisted key on an EXACT throwaway item
        /// only; never add/copy a signature.
        /// TODO(PR408 §3.9): operate on ItemDrop.ItemData.m_customData (@392) in-memory behind
        /// <see cref="TamperPolicy.Validate"/>; the field MUST be in the static allowlist AND the item
        /// MUST be in the run ledger's throwaway set. Never inserts a signature key (T5).
        /// </summary>
        RedactedReceipt TamperField(int itemSlot, string fieldName, TamperOperation operation);
    }

    /// <summary>
    /// Either-role observation verbs (PR408 §3.8/§3.10). Raw facts only — no reflection into verdict
    /// caches (threat T4). Reads only what a player would see (tooltip) or raw field keys.
    /// </summary>
    public interface IObservationAdapter
    {
        /// <summary>TODO(PR408 §3.10): Player.GetInventory() (Humanoid @833) -> enumerate slots via Inventory.GetItem(index) (@447). Client-role, post-spawn.</summary>
        RedactedReceipt ReadInventory();

        /// <summary>TODO(PR408 §3.10): Inventory.GetItem(index) -> emit prefab, quality, present m_customData KEY names only (values redacted).</summary>
        RedactedReceipt ReadItem(int itemSlot);

        /// <summary>
        /// The visible-Workmanship observation seam.
        /// TODO(PR408 §3.8): ItemDrop.ItemData.GetTooltip(stackOverride=-1) (@622) — a PURE string
        /// builder over a STATIC m_stringBuilder. MAIN THREAD ONLY (dispatcher tick). Records the
        /// returned string verbatim as a <see cref="FactSource.Direct"/> fact.
        /// </summary>
        RedactedReceipt ReadTooltip(int itemSlot);

        /// <summary>TODO(PR408 §3.1): ZNet.GetWorldName() (client @1798) — null-guarded.</summary>
        RedactedReceipt ReadWorldName();

        /// <summary>TODO(PR408 §3.1): ZNet.GetWorldUid() (client @1792) — NOT null-guarded; adapter MUST null-check ZNet.instance/World. NOT ZNet.GetUID() (session id).</summary>
        RedactedReceipt ReadWorldUid();
    }

    /// <summary>
    /// Server-channel delivering-peer binding (ADR-0009 §5.1, PR408 §3.4). The server MUST bind the
    /// ACTUAL delivering peer from the inbound rpc and ignore any identity claimed in the envelope.
    /// </summary>
    public interface IPeerBindingAdapter
    {
        /// <summary>
        /// Return the delivering peer uid, or null to reject.
        /// TODO(PR408 §3.4): map inbound ZRpc rpc -> peer via ZNet.GetPeer(rpc) (@729/@820); read
        /// ZRpc.GetSocket() for socket identity; wait for ZNetPeer.IsReady() (m_uid != 0). A mismatch
        /// vs an envelope-claimed peer is a substitution the caller rejects.
        /// </summary>
        long? BindDeliveringPeer(RequestEnvelope envelope);
    }

    /// <summary>
    /// The emission-path invariant that the harness never CLAIMS to have produced product state
    /// (ADR-0009 §4, threat T11). A craft/upgrade receipt records that the helper DROVE the vanilla
    /// seam and OBSERVED a result — it must never assert the helper minted/signed/granted the product
    /// stamp (the product issuance seam did that). This is the honesty complement to
    /// <see cref="ReceiptFirewall.AssertNoProductVerdict"/>.
    /// </summary>
    public static class ProductFirewall
    {
        /// <summary>Observed keys that would falsely claim the HARNESS authored product state — forbidden.</summary>
        public static readonly IReadOnlyList<string> ForbiddenClaimKeys =
            new[] { "minted", "signed", "granted", "issued_by_harness", "stamp_written" };

        /// <summary>
        /// Raise <see cref="HelperVerdictException"/> if a receipt claims the harness produced product
        /// state. The harness may OBSERVE a stamp (via tooltip/field keys); it may never claim it wrote
        /// one.
        /// </summary>
        public static void AssertNoProductStateClaim(RedactedReceipt receipt)
        {
            if (receipt == null) throw new ArgumentNullException(nameof(receipt));
            var lowered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var k in receipt.Observed.Keys) lowered.Add(k.ToLowerInvariant());
            foreach (var forbidden in ForbiddenClaimKeys)
            {
                if (lowered.Contains(forbidden))
                    throw new HelperVerdictException("receipt claims harness produced product state: " + forbidden);
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Inert deterministic fakes — for headless tests of the M4 evidence wiring.
    // These carry NO game dependency and perform NO real action; they only record.
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>A recording <see cref="IPeerBindingAdapter"/> for tests: returns a preset bound peer (or null).</summary>
    public sealed class FakePeerBindingAdapter : IPeerBindingAdapter
    {
        private readonly long? _bound;
        public FakePeerBindingAdapter(long? bound) => _bound = bound;
        public long? BindDeliveringPeer(RequestEnvelope envelope) => _bound;
    }
}
