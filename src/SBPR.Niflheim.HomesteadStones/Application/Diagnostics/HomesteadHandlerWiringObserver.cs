using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;

namespace SBPR.Niflheim.HomesteadStones.Application.Diagnostics
{
    // ADO #123 — the honest wiring observer for the operator shape report.
    //
    // "Wired" in this card means COMPOSED INTO THE LIVE RUNTIME, not "the type exists". A type can
    // compile, ship, pass its unit tests, and have ZERO runtime callers — that is exactly the class of
    // defect this repo has shipped repeatedly (the unregistered patch classes; the PurchaseCommandHandler
    // that the T021 investigation found had no runtime composition at all). So this observer reads the
    // ACTUAL composed instances off the two shipped composition roots and reports what it finds.
    //
    // It NEVER infers. A root the caller did not supply produces NOT CHECKED for every handler that root
    // owns, never a green. That asymmetry is deliberate: the report is allowed to admit a blind spot, and
    // is not allowed to guess a pass.
    //
    // net48 audit: System.Collections.Generic + the two shipped engine-free composition roots. No
    // UnityEngine / Valheim / BepInEx, so it link-compiles into the net8 test project.
    public static class HomesteadHandlerWiringObserver
    {
        public const string Relationship = "RelationshipCommandHandler";
        public const string Activity = "ActivityCommandHandler";
        public const string Development = "DevelopmentCommandHandler";
        public const string Facet = "FacetCommandHandler";
        public const string LocalPolicy = "LocalPolicyCommandHandler";
        public const string Purchase = "PurchaseCommandHandler";
        public const string WeaponDiscipline = "WeaponDisciplineCommandHandler";

        /// <summary>Observe the seven shipped command handlers against the live composition roots.
        ///
        /// <paramref name="foundational"/> owns RelationshipCommandHandler.
        /// <paramref name="local"/> owns Activity / Development / Facet / LocalPolicy.
        /// <paramref name="provisioningIngress"/> is the ON-DEMAND ingress that composes
        /// PurchaseCommandHandler — production only builds one inside the config-flag + Valheim-admin
        /// gated seam, so a null here honestly reports that no purchase handler is currently composed.
        ///
        /// A null root yields NOT CHECKED (not "absent") for the handlers it owns.</summary>
        public static IReadOnlyList<HandlerWiring> Observe(
            FoundationalProgressionServer? foundational,
            LocalProgressionServer? local,
            LocalProvisioningIngress? provisioningIngress = null)
        {
            var observed = new List<HandlerWiring>();

            observed.Add(foundational == null
                ? new HandlerWiring(Relationship, WiringState.NotChecked,
                    "no FoundationalProgressionServer supplied; this report did not look.")
                : new HandlerWiring(Relationship,
                    foundational.Relationships != null ? WiringState.Composed : WiringState.NotComposed,
                    "composed by FoundationalProgressionServer.Create; rehydrates the relationship journal at construction."));

            if (local == null)
            {
                const string notLookedAt = "no LocalProgressionServer supplied; this report did not look.";
                observed.Add(new HandlerWiring(Activity, WiringState.NotChecked, notLookedAt));
                observed.Add(new HandlerWiring(Development, WiringState.NotChecked, notLookedAt));
                observed.Add(new HandlerWiring(Facet, WiringState.NotChecked, notLookedAt));
                observed.Add(new HandlerWiring(LocalPolicy, WiringState.NotChecked, notLookedAt));
            }
            else
            {
                const string note = "composed by LocalProgressionServer.Create; rehydrates its durable journal at construction.";
                observed.Add(new HandlerWiring(Activity,
                    local.Activities != null ? WiringState.Composed : WiringState.NotComposed, note));
                observed.Add(new HandlerWiring(Development,
                    local.Development != null ? WiringState.Composed : WiringState.NotComposed, note));
                observed.Add(new HandlerWiring(Facet,
                    local.Facets != null ? WiringState.Composed : WiringState.NotComposed, note));
                observed.Add(new HandlerWiring(LocalPolicy,
                    local.LocalPolicy != null ? WiringState.Composed : WiringState.NotComposed, note));
            }

            // PurchaseCommandHandler is not held by either root: LocalProgressionServer constructs one
            // per call to CreateLocalProvisioningIngress(), which production reaches ONLY through the
            // config-flag + Valheim-admin gated isolated-QA seam. So "no ingress" is a real, reportable
            // fact about this build's shape, not a blind spot.
            if (provisioningIngress != null)
                observed.Add(new HandlerWiring(Purchase, WiringState.Composed,
                    "composed via LocalProgressionServer.CreateLocalProvisioningIngress()."));
            else if (local != null)
                observed.Add(new HandlerWiring(Purchase, WiringState.NotComposed,
                    "no live instance: it is built on demand by LocalProgressionServer.CreateLocalProvisioningIngress(), "
                    + "which production reaches only through the config-gated, admin-only provisioning seam."));
            else
                observed.Add(new HandlerWiring(Purchase, WiringState.NotChecked,
                    "no LocalProgressionServer or ingress supplied; this report did not look."));

            // WeaponDisciplineCommandHandler ships in PurchaseCommands.cs and is exercised by its unit
            // tests, but NO composition root constructs one. Reporting that plainly is the point of this
            // card: a type that exists is not a type that is wired.
            observed.Add(new HandlerWiring(WeaponDiscipline, WiringState.NotComposed,
                "type ships and is unit-tested, but no composition root constructs one — it has no live runtime caller."));

            return observed;
        }
    }
}
