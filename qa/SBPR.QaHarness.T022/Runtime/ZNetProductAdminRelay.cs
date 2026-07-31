// ============================================================================
//  QA-M5-SEED — the product admin RELAY seam (ADR-0009 §4 boundary-critical).
// ----------------------------------------------------------------------------
//  READ THIS BEFORE CHANGING ANYTHING HERE. This is the one file in the harness
//  that sits closest to the ADR-0009 §4 trust boundary, and the distinction it
//  rests on is narrow but real:
//
//    The harness MUST NOT grant entitlement.        ← §4, absolute
//    The harness MAY ask the PRODUCT to run its own admin path, which the
//    product then authorizes or refuses entirely on its own terms.
//
//  This relay does the second and is structurally incapable of the first. It
//  holds no signing key, constructs no entitlement/ownership/AP state, and writes
//  nothing. Its entire payload is a single bounded integer — OFFER(1) or BUY(2) —
//  sent on the SAME per-peer ZRpc the product's own console command sends it on.
//
//  WHY THAT IS SAFE: every authorization decision stays server-side inside the
//  product, and the product re-checks all of it at execution time
//  (MasterworkOwnershipProvisioningAdmin.RPC_Own):
//    * the provisioning seam config gate (default OFF) is re-read at call time;
//    * the acting peer is resolved from the TRANSPORT, never a claimed identity;
//    * the peer must be a server admin;
//    * the peer must resolve to a bound principal;
//    * the Bond (offer) / Attunement + earned AP (buy) preconditions must hold.
//  If any of those fail the product simply refuses and the harness has gained
//  nothing. The harness cannot make a refused grant succeed, which is exactly the
//  property that keeps this on the correct side of the boundary.
//
//  WHY IT IS NOT A CONSOLE RELAY (ADR-0009 §5.2 / §Decision explicitly reject one):
//  the product's `sbpr_master` console command is a thin wrapper that calls
//  ZNet.instance.GetServerRPC().Invoke(RpcOwn, cmd). This relay invokes that SAME
//  RPC directly. It never touches Terminal, ScriptTools, or the console command
//  table, so it takes no main-thread console lock (threat T6) and adds no console
//  command surface. AT-QA-NO-SCRIPTTOOLS-LOCK is unaffected.
//
//  KNOWN LIMITATION (deliberate, and it must stay visible): the product's RPC
//  handler returns void and is fire-and-forget, so this relay CANNOT observe
//  whether the grant applied. It reports only that the invoke was delivered. The
//  SERVER LOG remains the only truth for whether entitlement actually moved — the
//  runner must correlate there. A receipt from this verb is NOT evidence the
//  purchase happened, and nothing here may be read as such.
using System;
using SBPR.QaHarness.T022.Core;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// Invokes the product's own Masterwork ownership admin RPC on the server connection.
    /// Engine-bound; the engine-free seam is <see cref="IProductAdminRelay"/>.
    /// </summary>
    internal sealed class ZNetProductAdminRelay : IProductAdminRelay
    {
        // The product's per-peer ZRpc method name. This string is hashed by ZRpc.Register on
        // the product side (MasterworkOwnershipProvisioningAdmin.RpcOwn) — it is a LOCKED wire
        // identifier, and a rename on either side silently desyncs the session rather than
        // erroring. Pinned verbatim; do not "tidy" it.
        private const string RpcOwn = "SBPR_Niflheim_ProvisionMasterworkOwnership";

        public AdminRelayResult Invoke(long discriminator)
        {
            // Bound to the product's REAL discriminators. The retired QaT022Driver used 0/1
            // against the product's 1/2 — a false-sent defect where the wire looked fine and
            // nothing applied. Refusing out-of-range here is defence in depth on top of the
            // catalog's typed bounds.
            if (discriminator != VerbCatalog.AdminOffer && discriminator != VerbCatalog.AdminBuy)
                return AdminRelayResult.Refused("bad-discriminator");

            try
            {
                var znet = ZNet.instance;
                if (znet == null) return AdminRelayResult.Refused("no-znet");

                // Client-role only: this must be sent by a JOINED client on its server
                // connection, which is what makes the product resolve the acting peer as that
                // client's authenticated identity.
                if (znet.IsServer()) return AdminRelayResult.Refused("server-role");

                var serverRpc = znet.GetServerRPC();
                if (serverRpc == null) return AdminRelayResult.Refused("not-connected");

                serverRpc.Invoke(RpcOwn, (int)discriminator);

                // DELIVERED, not applied. The product handler is void/fire-and-forget; the
                // server log is the only place the outcome is observable. This token must never
                // be read as "entitlement granted".
                return AdminRelayResult.Delivered(
                    discriminator == VerbCatalog.AdminOffer
                        ? "sbpr_master:offer:invoke-delivered"
                        : "sbpr_master:buy:invoke-delivered");
            }
            catch (Exception ex)
            {
                return AdminRelayResult.Refused("relay-fault:" + ex.GetType().Name);
            }
        }
    }
}
