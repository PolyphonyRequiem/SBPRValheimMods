using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // T013 — personal node purchase pure transitions (contracts.md §"PurchaseNode"; data-model.md
    // §"Purchase personal node"; spec US3). This is the CHARACTER-side PURE transition over the
    // character aggregate: given the current character + Stone snapshots + the authored content
    // registry, it validates the accepted purchase contract and PRODUCES the next authoritative
    // character state with:
    //   * exactly ONE debit of the permitted balance (Personal AP or matching Facet Credit), and
    //   * exactly ONE appended purchase record carrying the exact Offered-Set/version provenance.
    // It never mutates its input, never journals, never invents a wallet, and never writes a second
    // active-effect ledger — the active/dormant status of the purchase is derived by
    // DerivedActivationView from these persisted facts (AT-NO-ACTIVE-LEDGER).
    //
    // Accepted purchase gates encoded here (contracts.md §"PurchaseNode" Validates):
    //   * node resolves in the current build (NodeNotFound / ContentVersionMismatch), belongs to the
    //     requested Tree (TreeMismatch), and is a personal Offered node (NodeNotOffered covers Local,
    //     unavailable, and not-yet-Offered nodes — spec AT-LOCAL-NOT-OFFERED);
    //   * the owning Tree is committed on the Stone (TreeNotCommitted);
    //   * committed Tree Level and Active Stone Level meet the node's authored level (TreeLevelTooLow /
    //     ActiveStoneLevelTooLow);
    //   * every authored prior-level same-Tree Offered node is already acquired (PriorOfferedSetIncomplete
    //     — Swift Preparation's Field Prep + Iron Stomach; spec AT-TIER-SAME-TREE);
    //   * the node is not already acquired (AlreadyAcquired — unique purchase);
    //   * the caller-supplied expected Offered-Set identity/version matches the derived one
    //     (ContentVersionMismatch);
    //   * the selected balance funds the authored AP price (InsufficientPersonalAP /
    //     InsufficientFacetCredit; Facet Credit must match the committed Tree's Stone Facet).
    //
    // The active-Attunement authority gate (Bond alone is NOT purchase authority) and optimistic
    // concurrency are enforced by the application command layer (PurchaseCommands), mirroring how
    // DevelopmentCommands gates the Bond before the pure TreeDevelopment transition.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + content catalog). No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 test project.

    public enum PurchasePaymentSource
    {
        PersonalAp = 0,
        FacetCredit = 1
    }

    public enum NodePurchaseResult
    {
        Applied = 0,
        NodeNotFound = 1,            // node key not in the current build
        ContentVersionMismatch = 2, // known key wrong version, or expected Offered-Set is stale/unknown
        TreeMismatch = 3,           // node does not belong to the requested Tree
        NodeNotOffered = 4,         // Local, unavailable, or not (yet) Offered on this Stone
        TreeNotCommitted = 5,       // the owning Tree is not committed on this Stone
        TreeLevelTooLow = 6,        // committed Tree Level below the node's required level
        ActiveStoneLevelTooLow = 7, // Active Stone Level below the node's required level
        PriorOfferedSetIncomplete = 8, // prior-level same-Tree personal Offered Nodes not all acquired
        AlreadyAcquired = 9,        // a purchase record for this node already exists
        InsufficientPersonalAP = 10, // Personal AP cannot fund the purchase
        InsufficientFacetCredit = 11 // matching Facet Credit insufficient or wrong Facet
    }

    /// <summary>Result of a pure PurchaseNode transition. On rejection <see cref="NextCharacter"/> is
    /// the UNCHANGED input aggregate (a caller that commits it unconditionally still writes prior
    /// state). On acceptance it carries the debited amount, the payment source actually used, and the
    /// derived Offered-Set provenance persisted with the purchase.</summary>
    public readonly struct NodePurchaseTransition
    {
        private NodePurchaseTransition(NodePurchaseResult result, CharacterProgressionAggregate next,
            int apDebited, PurchasePaymentSource paymentSource, VersionedId offeredSet, string outcomeClass)
        {
            Result = result;
            NextCharacter = next;
            ApDebited = apDebited;
            PaymentSource = paymentSource;
            OfferedSet = offeredSet;
            OutcomeClass = outcomeClass ?? string.Empty;
        }

        public NodePurchaseResult Result { get; }
        public bool Accepted => Result == NodePurchaseResult.Applied;
        public CharacterProgressionAggregate NextCharacter { get; }
        public int ApDebited { get; }
        public PurchasePaymentSource PaymentSource { get; }
        public VersionedId OfferedSet { get; }
        public string OutcomeClass { get; }

        public static NodePurchaseTransition Reject(NodePurchaseResult result,
            CharacterProgressionAggregate character) =>
            new NodePurchaseTransition(result, character, 0, PurchasePaymentSource.PersonalAp,
                VersionedId.None, string.Empty);

        public static NodePurchaseTransition Accept(CharacterProgressionAggregate next, int apDebited,
            PurchasePaymentSource paymentSource, VersionedId offeredSet, string outcomeClass) =>
            new NodePurchaseTransition(NodePurchaseResult.Applied, next, apDebited, paymentSource,
                offeredSet, outcomeClass);
    }

    /// <summary>Pure personal-node purchase + same-Tree Tier Access derivation over the character and
    /// Stone aggregates. Purchase produces the next character (one debit + one purchase record);
    /// Tier Access is a pure read derived from persisted purchases + Tree/Stone caps, NEVER stored.</summary>
    public static class NodePurchases
    {
        /// <summary>Deterministic Offered-Set identity for a Tree at one Tree level under one content
        /// registry version (data-model.md §"OfferedSetId / OfferedSetVersion": exact personal Offered
        /// Nodes for one Tree level/content view). Same-build, stable, and derivable from current
        /// state, so a purchase's persisted provenance can be re-checked without a stored set table.</summary>
        public static VersionedId OfferedSetIdFor(VersionedId tree, int treeLevel, int contentRegistryVersion)
        {
            return new VersionedId(tree.Key + ":L" + treeLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                contentRegistryVersion);
        }

        /// <summary>PurchaseNode (contracts.md). Validates the accepted purchase gates against current
        /// Stone/character snapshots + the authored registry, then produces the next character with the
        /// permitted balance debited once and one purchase record (with exact Offered-Set provenance)
        /// appended. Never mutates its inputs, never journals, never invents a wallet.</summary>
        public static NodePurchaseTransition PurchaseNode(
            CharacterProgressionAggregate character,
            StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog,
            VersionedId tree,
            VersionedId node,
            VersionedId expectedOfferedSet,
            PurchasePaymentSource paymentPreference)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            // Resolve the node against the current build. Unknown key -> NodeNotFound; known key wrong
            // version -> ContentVersionMismatch (never a "closest" rebind).
            var def = catalog.TryResolveNode(node);
            if (def == null)
            {
                return catalog.HasNodeKey(node)
                    ? NodePurchaseTransition.Reject(NodePurchaseResult.ContentVersionMismatch, character)
                    : NodePurchaseTransition.Reject(NodePurchaseResult.NodeNotFound, character);
            }

            // The node must belong to the requested Tree (payload treeId/version must match its owner).
            if (!def.Tree.Equals(tree))
                return NodePurchaseTransition.Reject(NodePurchaseResult.TreeMismatch, character);

            // Only personal Offered nodes are purchasable. Local (Stone-cultivated) and unavailable
            // nodes are never purchasable — they never enter the Offered Set (spec AT-LOCAL-NOT-OFFERED,
            // FR-015/FR-018). Reported as NodeNotOffered so an inspector sees one purchase-refusal code.
            if (!def.IsExecutable || def.Ownership != NodeOwnership.PersonalOffered)
                return NodePurchaseTransition.Reject(NodePurchaseResult.NodeNotOffered, character);

            // The node must actually be Offered on this Stone (developed to completion). An authored
            // personal node that has not yet been developed is not purchasable.
            if (!IsOfferedOnStone(stone, def.Node))
                return NodePurchaseTransition.Reject(NodePurchaseResult.NodeNotOffered, character);

            // The owning Tree must be committed on this Stone.
            int committedIndex = FindCommittedTreeIndex(stone, def.Tree);
            if (committedIndex < 0)
                return NodePurchaseTransition.Reject(NodePurchaseResult.TreeNotCommitted, character);
            var committed = stone.CommittedTrees[committedIndex];

            // Level caps: purchasing a node requires the committed Tree Level AND Active Stone Level to
            // be at least the node's authored level (Swift Preparation needs Level 2).
            if (committed.TreeLevel < def.TreeLevel)
                return NodePurchaseTransition.Reject(NodePurchaseResult.TreeLevelTooLow, character);
            if (stone.ActiveStoneLevel < def.TreeLevel)
                return NodePurchaseTransition.Reject(NodePurchaseResult.ActiveStoneLevelTooLow, character);

            // Prior-Offered-Set gate: every authored prior-level same-Tree personal Offered node must
            // already be acquired by THIS caller (Swift Preparation: Field Prep + Iron Stomach). This
            // is the same-Tree Attunement Tier Access requirement (spec AT-TIER-SAME-TREE); sibling
            // Trees and Local nodes are irrelevant because the authored set names only same-Tree nodes.
            var purchased = PurchasedNodeKeys(character, stone.StoneId);
            foreach (var prior in def.Requirements.PriorOfferedSet)
            {
                if (!purchased.Contains(prior.Key))
                    return NodePurchaseTransition.Reject(NodePurchaseResult.PriorOfferedSetIncomplete, character);
            }

            // Unique purchase: a purchase record for this node already exists (AT-PURCHASE-IDEMPOTENT's
            // conflicting-reuse rejection at the domain layer; replay is handled in the command layer).
            if (purchased.Contains(def.Node.Key))
                return NodePurchaseTransition.Reject(NodePurchaseResult.AlreadyAcquired, character);

            // The caller's expected Offered-Set identity/version must match the derived one for this
            // node's Tree level (stale/unknown expectation -> ContentVersionMismatch). This binds the
            // purchase provenance to the exact same-build Offered view the caller inspected.
            var derivedOfferedSet = OfferedSetIdFor(def.Tree, def.TreeLevel, stone.ContentRegistryVersion);
            if (!expectedOfferedSet.IsNone && !expectedOfferedSet.Equals(derivedOfferedSet))
                return NodePurchaseTransition.Reject(NodePurchaseResult.ContentVersionMismatch, character);

            int price = def.Pricing.PurchaseApPrice ?? 0;

            // Payment: Personal AP or matching Facet Credit (keyed to the committed Tree's Stone Facet).
            // No new wallet is invented — both are existing authoritative balances on the caller's Stone
            // record. Personal AP funds by default; Facet Credit is the authored alternative used to
            // spend credit returned by a prior revocation into the vacated Facet.
            var sr = FindStoneRecord(character, stone.StoneId);
            int personalAp = sr?.PersonalAp ?? 0;

            CharacterProgressionAggregate next;
            if (paymentPreference == PurchasePaymentSource.FacetCredit)
            {
                int facetCredit = FacetCreditAt(sr, committed.FacetId);
                if (facetCredit < price)
                    return NodePurchaseTransition.Reject(NodePurchaseResult.InsufficientFacetCredit, character);
                next = WithPurchase(character, stone.StoneId, sr, def, derivedOfferedSet,
                    PurchasePaymentSource.FacetCredit, committed.FacetId, price);
                return NodePurchaseTransition.Accept(next, price, PurchasePaymentSource.FacetCredit,
                    derivedOfferedSet, OutcomeClassOf(def));
            }

            if (personalAp < price)
                return NodePurchaseTransition.Reject(NodePurchaseResult.InsufficientPersonalAP, character);
            next = WithPurchase(character, stone.StoneId, sr, def, derivedOfferedSet,
                PurchasePaymentSource.PersonalAp, committed.FacetId, price);
            return NodePurchaseTransition.Accept(next, price, PurchasePaymentSource.PersonalAp,
                derivedOfferedSet, OutcomeClassOf(def));
        }

        /// <summary>Same-Tree Attunement Tier Access derived purely from the caller's persisted prior
        /// same-Tree Offered purchases plus the current Tree/Stone caps (spec FR-014, SC-004,
        /// AT-TIER-SAME-TREE). It is NEVER stored as Tier XP: this is a pure function of persisted
        /// facts. Access starts at Tier 1 and advances to Tier T only when EVERY executable personal
        /// Offered node authored at level T-1 in <paramref name="tree"/> is acquired AND the committed
        /// Tree Level and Active Stone Level both reach T. Sibling Trees, Local nodes, and unavailable
        /// nodes neither grant nor block this Tree-specific result.</summary>
        public static int DeriveSameTreeTierAccess(
            CharacterProgressionAggregate character,
            StoneProgressionAggregate stone,
            HomesteadProgressionCatalog catalog,
            VersionedId tree)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));

            int committedIndex = FindCommittedTreeIndex(stone, tree);
            if (committedIndex < 0) return 0; // no commitment -> no derived access
            var committed = stone.CommittedTrees[committedIndex];

            var purchased = PurchasedNodeKeys(character, stone.StoneId);

            int tier = 1;
            while (true)
            {
                int target = tier + 1;
                // Caps first: Tree Level and Active Stone Level must both reach the target tier.
                if (committed.TreeLevel < target || stone.ActiveStoneLevel < target)
                    break;

                // Every executable personal Offered node authored at the PRIOR level (target-1) in this
                // Tree must be acquired. An empty prior level cannot grant a higher tier.
                bool anyPriorNode = false;
                bool allAcquired = true;
                foreach (var def in catalog.Nodes)
                {
                    if (!def.Tree.Equals(tree)) continue;
                    if (!def.IsExecutable || def.Ownership != NodeOwnership.PersonalOffered) continue;
                    if (def.TreeLevel != target - 1) continue;
                    anyPriorNode = true;
                    if (!purchased.Contains(def.Node.Key)) { allAcquired = false; break; }
                }

                if (!anyPriorNode || !allAcquired) break;
                tier = target;
            }
            return tier;
        }

        private static string OutcomeClassOf(NodeDefinition def)
        {
            // Refundable Character Effects vs durable Permanent Effects (data-model.md CharacterProgression:
            // "refundable/durable outcome class"). Used by revocation to decide credit vs survival.
            switch (def.Outcome)
            {
                case NodeOutcomeType.CharacterEffect: return "CharacterEffect";
                case NodeOutcomeType.PermanentEffect: return "PermanentEffect";
                default: return "LocalEffect";
            }
        }

        private static bool IsOfferedOnStone(StoneProgressionAggregate stone, VersionedId node)
        {
            foreach (var dev in stone.NodeDevelopment)
                if (string.Equals(dev.Node.Key, node.Key, StringComparison.Ordinal))
                    return dev.Offered;
            return false;
        }

        private static int FindCommittedTreeIndex(StoneProgressionAggregate stone, VersionedId tree)
        {
            for (int i = 0; i < stone.CommittedTrees.Count; i++)
                if (string.Equals(stone.CommittedTrees[i].Tree.Key, tree.Key, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private static CharacterStoneRecord? FindStoneRecord(CharacterProgressionAggregate character, StoneId stoneId)
        {
            foreach (var sr in character.StoneRecords)
                if (sr.StoneId.Equals(stoneId)) return sr;
            return null;
        }

        private static HashSet<string> PurchasedNodeKeys(CharacterProgressionAggregate character, StoneId stoneId)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId)) continue;
                foreach (var p in sr.Purchases) set.Add(p.Node.Key);
            }
            return set;
        }

        private static int FacetCreditAt(CharacterStoneRecord? sr, string facetId)
        {
            if (sr == null) return 0;
            int total = 0;
            foreach (var fc in sr.FacetCredits)
                if (string.Equals(fc.FacetId, facetId, StringComparison.Ordinal))
                    total += fc.Amount;
            return total;
        }

        /// <summary>Produce the next character with ONE debit (Personal AP or matching Facet Credit)
        /// and ONE appended purchase record on the Stone record for <paramref name="stoneId"/>. Every
        /// other balance/record/field is preserved verbatim; the aggregate revision advances once.</summary>
        private static CharacterProgressionAggregate WithPurchase(
            CharacterProgressionAggregate character, StoneId stoneId, CharacterStoneRecord? existing,
            NodeDefinition def, VersionedId offeredSet, PurchasePaymentSource source, string facetId, int price)
        {
            var newRecords = new List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool found = false;
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId))
                {
                    newRecords.Add(sr);
                    continue;
                }
                found = true;
                newRecords.Add(RewriteWithPurchase(sr, def, offeredSet, source, facetId, price));
            }
            if (!found)
            {
                // No prior record for this Stone: purchase can only reach here when a caller has a
                // record (they hold Attunement + a balance), so this is a defensive fresh-record path.
                var seed = new CharacterStoneRecord(stoneId, 0, 0, 0);
                newRecords.Add(RewriteWithPurchase(seed, def, offeredSet, source, facetId, price));
            }

            return new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope,
                character.Revision + 1, character.BondSlots, character.AttunementSlots,
                character.LastAppliedReceiptId, newRecords, character.SchemaVersion);
        }

        private static CharacterStoneRecord RewriteWithPurchase(
            CharacterStoneRecord sr, NodeDefinition def, VersionedId offeredSet,
            PurchasePaymentSource source, string facetId, int price)
        {
            int newPersonalAp = sr.PersonalAp;
            var newFacetCredits = sr.FacetCredits;

            if (source == PurchasePaymentSource.PersonalAp)
            {
                newPersonalAp = sr.PersonalAp - price;
            }
            else
            {
                newFacetCredits = DebitFacetCredit(sr.FacetCredits, facetId, price);
            }

            var purchases = new List<NodePurchaseRecord>(sr.Purchases.Count + 1);
            foreach (var p in sr.Purchases) purchases.Add(p);
            purchases.Add(new NodePurchaseRecord(def.Tree, def.Node,
                source == PurchasePaymentSource.PersonalAp ? "PersonalAP" : "FacetCredit",
                OutcomeClassOf(def), offeredSet, string.Empty));

            return new CharacterStoneRecord(sr.StoneId, newPersonalAp, sr.CumulativeAp, sr.PersonalBp,
                newFacetCredits, purchases, sr.Relationships, sr.SkillCapChoices);
        }

        private static IReadOnlyList<FacetCreditRecord> DebitFacetCredit(
            IReadOnlyList<FacetCreditRecord> credits, string facetId, int price)
        {
            var result = new List<FacetCreditRecord>(credits.Count);
            int remaining = price;
            foreach (var fc in credits)
            {
                if (remaining > 0 && string.Equals(fc.FacetId, facetId, StringComparison.Ordinal))
                {
                    int take = fc.Amount <= remaining ? fc.Amount : remaining;
                    int left = fc.Amount - take;
                    remaining -= take;
                    if (left > 0)
                        result.Add(new FacetCreditRecord(fc.FacetId, left, fc.SourceProvenance));
                    // fully-consumed credit rows are dropped
                }
                else
                {
                    result.Add(fc);
                }
            }
            return result;
        }
    }
}
