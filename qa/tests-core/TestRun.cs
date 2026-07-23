// Shared run-context fixture for the M3R crash-safe ownership tests. A single disposable
// world uid + run nonce every ledger.Ensure / executor / provisioner uses, so markers all
// scope to the same run unless a test deliberately builds a foreign one.
using SBPR.QaHarness.T022.Core.Fixtures;

namespace SBPR.QaHarness.T022.Core.Tests
{
    internal static class TestRun
    {
        public const long WorldUid = 9001;
        public const string Nonce = "run-nonce-abc123";

        public static FixtureRunContext Ctx => new FixtureRunContext(WorldUid, Nonce);

        /// <summary>A marker for one owned id under the canonical test run (helper for seeding survivors).</summary>
        public static FixtureOwnershipMarker Marker(string fixtureId, OwnedResourceId id) =>
            FixtureOwnershipMarker.For(Ctx, fixtureId, id);
    }
}
