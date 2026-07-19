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
        ///      which enforces the active-Attunement authority, the Personal-AP debit, the
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

        /// <summary>Provision full personal-node OWNERSHIP (developed + purchased) for one QA subject using
        /// ONLY accepted, receipt-backed handlers — the missing runtime seam a joined-client OWNER in-world
        /// proof structurally depends on (T027 Fletcher's Habit R2 verdict / T026 Field Fletching I). It is
        /// the personal-purchase sibling of <see cref="DevelopLocalNode"/>: where that reaches a Stone-owned
        /// developed Local node, this reaches a personal <c>NodePurchaseRecord</c>, the only durable truth by
        /// which <c>ProjectileRecoveryProvider.OwnsFletchersHabit</c> (and any personal Permanent/Character
        /// Effect) returns owned.
        ///
        /// The sequence crosses the SAME accepted handlers a real session would, in the order the spec's state
        /// machine requires, on ONE server-derived subject:
        ///   1. Establish a Governor Bond (RelationshipCommandHandler.CreateBond) — cultivation authority.
        ///   2. Develop the personal node to Offered through the accepted Facet→BP→development handlers
        ///      (LocalNodeProvisioningDriver.ProvisionOffered).
        ///   3. RELEASE the Bond (ReleaseRelationship). A single character cannot ACTIVELY hold both a Bond
        ///      and an Attunement to one Stone (the authority index is sibling/self exclusive), and the
        ///      accepted purchase gate requires an active Attunement — so the Governor Bond that developed
        ///      the node is released first. Ownership is unaffected: the node stays Offered (Stone-owned) and
        ///      the buyer holds no purchase yet.
        ///   4. Establish an Attunement (CreateAttunement) — purchase authority.
        ///   5. Purchase the node through the accepted PurchaseCommandHandler (every content/level/prior-
        ///      Offered-Set/price/authority/idempotency gate is the handler's).
        ///
        /// The one thing this seam SEEDS is the bare Stone envelope (like <see cref="DevelopLocalNode"/>) and,
        /// before the purchase, the buyer's authored Personal AP price on their character record — the empty
        /// funded owner row the accepted purchase debit needs to exist (no runtime handler credits aggregate
        /// Personal AP; this is the purchase analogue of seeding the bare Stone the develop handlers need). It
        /// is NOT a purchase-state write: the debit + the single durable <c>NodePurchaseRecord</c> are still
        /// produced by the accepted handler, and any handler rejection surfaces verbatim so a QA run that
        /// "owns" a node has provably crossed the real authority/price/idempotency gates.
        ///
        /// <paramref name="opPrefix"/> makes every derived operation id deterministic so an exact re-run
        /// replays idempotently through the accepted handlers. Returns the terminal purchase outcome (or the
        /// first failing step's verbatim result code).</summary>
        public LocalProvisioningResult ProvisionPersonalNodeOwnership(
            AuthoritativeSubject subject,
            StoneId stoneId,
            VersionedId tree,
            VersionedId node,
            string opPrefix,
            string worldProductScope)
        {
            if (string.IsNullOrEmpty(opPrefix))
                return LocalProvisioningResult.Rejected("MissingOpPrefix", "prefix");
            if (string.IsNullOrEmpty(subject.Account.Value) || string.IsNullOrEmpty(subject.Character.Value))
                return LocalProvisioningResult.Rejected("Unauthenticated", "subject");

            var def = _catalog.TryResolveNode(node);
            if (def == null)
                return LocalProvisioningResult.Rejected("NodeNotFound", "resolve");
            if (def.Ownership != NodeOwnership.PersonalOffered)
                return LocalProvisioningResult.Rejected("NotAPersonalNode", "resolve");

            SeedBareStoneIfAbsent(stoneId);

            // 1. Governor Bond — cultivation authority for the develop steps. (Seeds the character
            //    aggregate if absent, exactly like the relationship provisioning seam.)
            string bondOp = opPrefix + "-bond";
            string bondRel = opPrefix + "-rel-bond";
            var bond = ProvisionRelationship(subject, stoneId, RelationshipCommandType.CreateBond,
                bondOp, bondRel, worldProductScope, "Homestead:All");
            if (bond.Outcome == RelationshipCommandOutcome.Rejected)
                return LocalProvisioningResult.Rejected(bond.ResultCode, "bond");

            // 2. Develop the personal node to Offered through accepted commands only.
            var develop = _driver.ProvisionOffered(subject, stoneId, node, opPrefix + "-dev");
            if (!develop.IsDeveloped)
                return LocalProvisioningResult.Rejected(develop.ResultCode, develop.FailedStep);

            // 3. Release the Bond — a character cannot actively hold Bond AND Attunement to one Stone, and
            //    purchase requires an active Attunement. Ownership is unaffected (node stays Offered).
            string releaseOp = opPrefix + "-release";
            var release = ProvisionRelationshipRelease(subject, stoneId, releaseOp, bondRel, worldProductScope);
            if (release.Outcome == RelationshipCommandOutcome.Rejected)
                return LocalProvisioningResult.Rejected(release.ResultCode, "release");

            // 4. Attunement — purchase authority.
            string attOp = opPrefix + "-attune";
            string attRel = opPrefix + "-rel-att";
            var attune = ProvisionRelationship(subject, stoneId, RelationshipCommandType.CreateAttunement,
                attOp, attRel, worldProductScope, string.Empty);
            if (attune.Outcome == RelationshipCommandOutcome.Rejected)
                return LocalProvisioningResult.Rejected(attune.ResultCode, "attune");

            // 5. Fund the authored Personal AP price (the empty funded owner row the accepted purchase debit
            //    needs — no runtime handler credits aggregate Personal AP), then purchase through the handler.
            int price = def.Pricing.PurchaseApPrice ?? 0;
            FundPersonalApIfNeeded(subject, stoneId, price);

            return PurchaseNode(subject, stoneId, tree, node, VersionedId.None,
                PurchasePaymentSource.PersonalAp, opPrefix + "-purchase");
        }

        private RelationshipCommandResult ProvisionRelationship(
            AuthoritativeSubject subject, StoneId stoneId, RelationshipCommandType commandType,
            string operationId, string relationshipId, string worldProductScope, string requestedRange)
        {
            // Seed the character aggregate ONLY when absent — never overwrite existing progression. This is
            // the empty owner row the accepted relationship handler requires (it rejects CharacterNotFound
            // otherwise), exactly like RelationshipProvisioningIngress.
            if (_server.Characters.GetCharacter(subject.Account, subject.Character) == null)
            {
                _server.Characters.PutCharacter(new CharacterProgressionAggregate(
                    subject.Account, subject.Character,
                    worldProductScope: worldProductScope ?? string.Empty, revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "qa-personal-node-provision",
                    stoneRecords: new[] { new CharacterStoneRecord(stoneId, 0, 0, 0, null, null) }));
            }

            var connection = new AuthenticatedConnection(subject.Account.Value, subject.Character.Value);
            return _server.Relationships.Handle(new RelationshipCommand(
                new OperationId(operationId), commandType, stoneId, connection, default,
                relationshipId, responsibilityRange: requestedRange ?? string.Empty));
        }

        private RelationshipCommandResult ProvisionRelationshipRelease(
            AuthoritativeSubject subject, StoneId stoneId, string operationId, string relationshipId,
            string worldProductScope)
        {
            var connection = new AuthenticatedConnection(subject.Account.Value, subject.Character.Value);
            return _server.Relationships.Handle(new RelationshipCommand(
                new OperationId(operationId), RelationshipCommandType.ReleaseRelationship, stoneId,
                connection, default, relationshipId));
        }

        /// <summary>Seed the acting subject's authored Personal AP price onto their character record ONLY when
        /// the current balance cannot fund it — the empty funded owner row the accepted purchase debit needs.
        /// No runtime handler credits aggregate Personal AP (Foundational AP lands in a separate receipt sink,
        /// BP is the only aggregate-credited balance), so this is the purchase analogue of seeding the bare
        /// Stone the develop handlers require. It NEVER writes purchase/relationship state and preserves every
        /// other balance/record verbatim; the debit itself is still the accepted handler's.</summary>
        private void FundPersonalApIfNeeded(AuthoritativeSubject subject, StoneId stoneId, int price)
        {
            if (price <= 0) return;
            var character = _server.Characters.GetCharacter(subject.Account, subject.Character);
            if (character == null) return;

            var newStoneRecords = new System.Collections.Generic.List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool found = false;
            foreach (var sr in character.StoneRecords)
            {
                if (sr.StoneId.Equals(stoneId))
                {
                    found = true;
                    if (sr.PersonalAp >= price) return; // already funded; do not overwrite/inflate.
                    newStoneRecords.Add(new CharacterStoneRecord(sr.StoneId, price, sr.CumulativeAp,
                        sr.PersonalBp, sr.Purchases, sr.Relationships, sr.SkillCapChoices));
                }
                else
                {
                    newStoneRecords.Add(sr);
                }
            }
            if (!found)
                newStoneRecords.Add(new CharacterStoneRecord(stoneId, price, 0, 0, null, null, null));

            _server.Characters.PutCharacter(new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope,
                character.Revision + 1, character.BondSlots, character.AttunementSlots,
                character.LastAppliedReceiptId, newStoneRecords, character.SchemaVersion));
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
