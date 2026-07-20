using System;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T021 remediation 2 — the isolated-QA Local-node development / personal-node purchase INGRESS.
    //
    // Why this exists: the T021 joined-client rerun (PR #371 FAIL evidence) proved that the accepted
    // progression command handlers wired into LocalProgressionServer + LocalNodeProvisioningDriver +
    // PurchaseCommandHandler had ZERO runtime callers. A Local Effect (Refined Workshop) can only reach
    // Active when its Stone-cultivated node is Developed (committed Tree + developed node) in the
    // authoritative Stone aggregate; nothing at runtime develops it, so the positive effective-Level-3
    // path was structurally unreachable. This ingress is the one missing seam — the SAME shape the
    // T009R3 RelationshipProvisioningIngress uses for relationships:
    //
    //   * It NEVER writes node/commitment/purchase state directly. Every mutation crosses the shipped,
    //     receipt-backed handlers (LocalNodeProvisioningDriver → Facet/Activity/Development; and the
    //     PurchaseCommandHandler for personal Offered nodes) onto their durable journals. Any handler
    //     rejection surfaces verbatim, so a QA run that "provisions" a node has provably crossed the
    //     real authority/revision/idempotency gates.
    //   * The ONLY thing it seeds is the bare, PRE-PROGRESSION Stone envelope (Level-2 Homestead with no
    //     committed Trees and no node development) when the Stone aggregate is absent — the empty owner
    //     row the accepted commands need to exist before they can transition (the handlers reject
    //     StoneNotFound otherwise), exactly like RelationshipProvisioningIngress seeds an absent
    //     character. It is NOT a node-state write and never overwrites an existing Stone: a Stone the
    //     boot journals already rehydrated (a restart) skips seeding, so a developed node survives a
    //     restart via the durable Facet/Development journals, never this seam.
    //
    // Restriction: this ingress is only ever constructed and driven by the net48
    // LocalProgressionProvisioningAdmin seam, which is gated behind an explicit server-owned config flag
    // (default OFF) AND Valheim admin authority. Disabled/absent outside that gate — production behavior
    // fails closed. This is a playtest/isolated-QA provisioning path, never a shipping gameplay command.
    //
    // net48 audit: value objects + the shipped engine-free driver/handlers/stores only. No net5+ surface,
    // no UnityEngine/Valheim, so it link-compiles into the net8 test project and every branch is tested.
    public sealed class LocalProvisioningIngress
    {
        // The bare seed envelope's Homestead Stone level. The shipped Homestead proof Stone is Level 2
        // (matches BareStone in the T016 fixtures + ProgressionStateRepair's reset baseline). A Local
        // node at authored Level 1 (Refined Workshop) is developable and deliverable at this level.
        private const int SeedStoneLevel = 2;
        private const long SeedStoneRevision = 1;

        /// <summary>T022 remediation R4 — the Masterwork personal Character-Effect node + its owning Crafting
        /// Tree (matches HomesteadProgressionCatalog: Crafting / Masterwork v1). Bound here so the ownership
        /// composite names the exact accepted node, never a client-authored id.</summary>
        private static readonly VersionedId MasterworkNode = new VersionedId("Masterwork", 1);
        private static readonly VersionedId MasterworkTree = HomesteadProgressionCatalog.CraftingTree;

        private readonly LocalProgressionServer _server;
        private readonly LocalNodeProvisioningDriver _driver;
        private readonly PurchaseCommandHandler _purchases;
        private readonly HomesteadProgressionCatalog _catalog;

        public LocalProvisioningIngress(
            LocalProgressionServer server,
            PurchaseCommandHandler purchases)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _purchases = purchases ?? throw new ArgumentNullException(nameof(purchases));
            _driver = new LocalNodeProvisioningDriver(server);
            _catalog = server.Catalog;
        }

        /// <summary>Develop one Stone-cultivated Local node to completion for QA using ONLY accepted
        /// commands. <paramref name="governor"/> must already hold an active Homestead:All Governor Bond
        /// (established through the relationship provisioning seam). Seeds the bare Stone envelope if
        /// absent (never overwriting an existing/rehydrated Stone). <paramref name="opPrefix"/> makes
        /// every derived operation id deterministic so an exact re-run replays idempotently through the
        /// handlers. Returns the driver's structured result — on failure it names the accepted-command
        /// step + the handler's verbatim result code so a QA harness asserts the real gate.</summary>
        public LocalProvisioningResult DevelopLocalNode(
            AuthoritativeSubject governor,
            StoneId stoneId,
            VersionedId localNode,
            string opPrefix)
        {
            if (string.IsNullOrEmpty(opPrefix))
                return LocalProvisioningResult.Rejected("MissingOpPrefix", "prefix");
            if (string.IsNullOrEmpty(governor.Account.Value) || string.IsNullOrEmpty(governor.Character.Value))
                return LocalProvisioningResult.Rejected("Unauthenticated", "subject");

            SeedBareStoneIfAbsent(stoneId);

            var result = _driver.Provision(governor, stoneId, localNode, opPrefix);
            return result.IsDeveloped
                ? LocalProvisioningResult.Developed(result)
                : LocalProvisioningResult.Rejected(result.ResultCode, result.FailedStep);
        }

        /// <summary>Purchase one personal Offered node through the accepted PurchaseCommandHandler. The
        /// acting character must hold an active Attunement (the handler enforces it — Bond alone is not
        /// purchase authority). Every content/level/prior-Offered-Set/price/revision/idempotency gate is
        /// the shipped handler's; this ingress only routes a server-derived subject to it. Returns the
        /// handler's verbatim outcome so a QA harness asserts the real gate on reject/replay/conflict.
        /// The Stone must already exist (a developed Stone context) — purchase never seeds one.</summary>
        public LocalProvisioningResult PurchaseNode(
            AuthoritativeSubject buyer,
            StoneId stoneId,
            VersionedId tree,
            VersionedId node,
            VersionedId expectedOfferedSet,
            PurchasePaymentSource paymentPreference,
            string operationId)
        {
            if (string.IsNullOrEmpty(operationId))
                return LocalProvisioningResult.Rejected("MissingOperationId", "op");
            if (string.IsNullOrEmpty(buyer.Account.Value) || string.IsNullOrEmpty(buyer.Character.Value))
                return LocalProvisioningResult.Rejected("Unauthenticated", "subject");

            var connection = new AuthenticatedConnection(buyer.Account.Value, buyer.Character.Value);
            var command = new PurchaseNodeCommand(
                new OperationId(operationId), stoneId, connection, default,
                tree.Key, tree.Version, node.Key, node.Version,
                expectedOfferedSet.IsNone ? string.Empty : expectedOfferedSet.Key,
                expectedOfferedSet.IsNone ? 0 : expectedOfferedSet.Version,
                paymentPreference);

            var result = _purchases.Handle(command);
            return result.Outcome == PurchaseCommandOutcome.Rejected
                ? LocalProvisioningResult.Rejected(result.ResultCode, "purchase")
                : LocalProvisioningResult.Purchased(result);
        }

        /// <summary>T022 remediation R4 — the Masterwork OWNERSHIP provisioning composite: reach an ACTIVE
        /// purchased Masterwork personal node for one QA subject using ONLY accepted, receipt-backed handlers.
        /// It does exactly two accepted-command sequences, in order:
        ///   1. DEVELOP+OFFER Masterwork on the Stone via <see cref="LocalNodeProvisioningDriver.ProvisionOffered"/>
        ///      (commit Crafting Tree → credit BP → ApplyBPToNode to completion), so the personal node is Offered
        ///      and therefore purchasable. Idempotent: an already-Offered node replays without re-development.
        ///   2. PURCHASE Masterwork via the accepted <see cref="PurchaseCommandHandler"/> for the acting buyer,
        ///      which enforces the active-Attunement authority, the Personal-AP/Facet-Credit debit, the
        ///      prior-Offered-Set gate, and one-purchase idempotency. Replay returns the recorded terminal
        ///      result with a single purchase record and a single AP debit.
        /// The <paramref name="governor"/> develops+offers (must hold an active Bond covering Crafting); the
        /// <paramref name="buyer"/> purchases (must hold an active Attunement AND sufficient earned Personal AP —
        /// this seam never mints AP, so an unfunded buyer is rejected InsufficientPersonalAP by the real gate).
        /// In the common single-subject QA case both are the same authenticated principal. Any handler rejection
        /// surfaces verbatim so a QA run proves it crossed the real develop/offer/attunement/purchase gates.
        /// The Stone must already exist (a developed Stone context established by the Local develop seam / bond
        /// placement) — this composite never seeds a Stone.</summary>
        public LocalProvisioningResult OwnMasterwork(
            AuthoritativeSubject governor,
            AuthoritativeSubject buyer,
            StoneId stoneId,
            string opPrefix)
        {
            if (string.IsNullOrEmpty(opPrefix))
                return LocalProvisioningResult.Rejected("MissingOpPrefix", "prefix");
            if (string.IsNullOrEmpty(buyer.Account.Value) || string.IsNullOrEmpty(buyer.Character.Value))
                return LocalProvisioningResult.Rejected("Unauthenticated", "buyer");

            // 1. Develop+offer Masterwork through the accepted commands (Governor authority). An already-Offered
            //    node replays idempotently.
            var offer = OfferMasterwork(governor, stoneId, opPrefix);
            if (!offer.Succeeded)
                return offer;

            // 2. Purchase Masterwork for the acting buyer through the accepted PurchaseCommandHandler. The
            //    handler enforces the active-Attunement authority, the AP debit, and one-purchase idempotency.
            return BuyMasterwork(buyer, stoneId, opPrefix);
        }

        /// <summary>The DEVELOP+OFFER half of Masterwork ownership: develop the Masterwork personal node to
        /// completion (Offered) on the Stone via the accepted commit→BP→ApplyBPToNode commands. Authorized by
        /// the acting <paramref name="governor"/>'s active Bond covering the Crafting Tree. Idempotent: an
        /// already-Offered node replays without re-development. This is the Governor-run console half
        /// (<c>sbpr_master offer</c>) so the reservation model holds — develop needs a Bond, purchase (the other
        /// half) needs an Attunement, and one character cannot hold both at one Stone.</summary>
        public LocalProvisioningResult OfferMasterwork(
            AuthoritativeSubject governor,
            StoneId stoneId,
            string opPrefix)
        {
            if (string.IsNullOrEmpty(opPrefix))
                return LocalProvisioningResult.Rejected("MissingOpPrefix", "prefix");
            if (string.IsNullOrEmpty(governor.Account.Value) || string.IsNullOrEmpty(governor.Character.Value))
                return LocalProvisioningResult.Rejected("Unauthenticated", "governor");

            // Seed the bare Stone envelope if absent (same as the Local develop seam) so the accepted
            // commit/develop commands have a Stone to operate on; never overwrites an existing/rehydrated Stone.
            SeedBareStoneIfAbsent(stoneId);

            var offer = _driver.ProvisionOffered(governor, stoneId, MasterworkNode, opPrefix + "-offer");
            return offer.IsDeveloped
                ? LocalProvisioningResult.Developed(offer)
                : LocalProvisioningResult.Rejected(offer.ResultCode, offer.FailedStep);
        }

        /// <summary>The PURCHASE half of Masterwork ownership: purchase the already-Offered Masterwork node for
        /// the acting <paramref name="buyer"/> through the accepted PurchaseCommandHandler (active-Attunement
        /// authority + Personal-AP debit + one-purchase idempotency). This is the buyer-run console half
        /// (<c>sbpr_master buy</c>). Rejects verbatim if Masterwork is not yet Offered (NodeNotOffered), the
        /// buyer is not Attuned (RelationshipRequired), or is unfunded (InsufficientPersonalAP).</summary>
        public LocalProvisioningResult BuyMasterwork(
            AuthoritativeSubject buyer,
            StoneId stoneId,
            string opPrefix)
        {
            if (string.IsNullOrEmpty(opPrefix))
                return LocalProvisioningResult.Rejected("MissingOpPrefix", "prefix");
            return PurchaseNode(buyer, stoneId, MasterworkTree, MasterworkNode, VersionedId.None,
                PurchasePaymentSource.PersonalAp, opPrefix + "-buy");
        }

        /// <summary>Seed the bare, pre-progression Stone envelope ONLY when the Stone aggregate is absent.
        /// Never overwrites an existing Stone (including one the boot Facet/Development journals already
        /// rehydrated on restart). This is the empty owner row the accepted commands require, not a node-
        /// state write: no committed Trees, no node development, default (Everyone) Settlement policy.</summary>
        private void SeedBareStoneIfAbsent(StoneId stoneId)
        {
            if (_server.Stones.GetStone(stoneId) != null) return;

            _server.Stones.PutStone(new StoneProgressionAggregate(
                stoneId,
                revision: SeedStoneRevision,
                historicalStoneLevel: SeedStoneLevel,
                activeStoneLevel: SeedStoneLevel,
                foundationalTree: _catalog.FoundationalTree,
                foundationalCatalog: _catalog.FoundationalCatalog,
                contentRegistryVersion: _catalog.ContentRegistryVersion,
                createdProvenance: "qa-provision-seed",
                updatedProvenance: "qa-provision-seed",
                mirroredStoneAp: 0,
                lastAppliedReceiptId: "qa-provision-seed",
                committedTrees: null,
                nodeDevelopment: null));
        }
    }

    /// <summary>Structured outcome of an isolated-QA provisioning attempt. On rejection nothing was
    /// committed (the handlers fail closed); it names the accepted-command step and the handler's verbatim
    /// result code so a QA harness proves which real gate blocked it rather than a fabricated success.</summary>
    public readonly struct LocalProvisioningResult
    {
        private LocalProvisioningResult(bool ok, string kind, string resultCode, string step, int steps)
        {
            Succeeded = ok;
            Kind = kind ?? string.Empty;
            ResultCode = resultCode ?? string.Empty;
            Step = step ?? string.Empty;
            Steps = steps;
        }

        /// <summary>True when the accepted handler(s) committed the intended terminal state.</summary>
        public bool Succeeded { get; }

        /// <summary>"Developed" / "Purchased" / "Replayed" / "Rejected" — the terminal shape.</summary>
        public string Kind { get; }

        /// <summary>The handler's verbatim result code (or the pre-command rejection reason).</summary>
        public string ResultCode { get; }

        /// <summary>The accepted-command step that produced this outcome (commit/credit/develop/purchase).</summary>
        public string Step { get; }

        /// <summary>Development steps taken (0 for purchase / rejections).</summary>
        public int Steps { get; }

        internal static LocalProvisioningResult Developed(LocalNodeProvisioningResult r) =>
            new LocalProvisioningResult(true, "Developed", r.ResultCode, "develop", r.Steps);

        internal static LocalProvisioningResult Purchased(PurchaseCommandResult r) =>
            new LocalProvisioningResult(true,
                r.Outcome == PurchaseCommandOutcome.Replayed ? "Replayed" : "Purchased",
                r.ResultCode, "purchase", 0);

        internal static LocalProvisioningResult Rejected(string code, string step) =>
            new LocalProvisioningResult(false, "Rejected", code, step, 0);

        /// <summary>One-line, PII-free operator rendering.</summary>
        public string ToOperatorLine() =>
            Succeeded
                ? $"[local-provisioning] outcome={Kind} result={ResultCode} step={Step} steps={Steps}"
                : $"[local-provisioning] rejected={ResultCode} step={Step}";
    }
}
