// ============================================================================
//  T022 privacy fix — operator-contact acceptance policy tests.
// ----------------------------------------------------------------------------
//  The privacy disclosure presented before a real QA subject is provisioned MUST
//  name a routable operator contact. These deterministic tests prove that an
//  absent, malformed, `.invalid`, or documented-placeholder contact is refused
//  (fail-closed BEFORE any subject prompt/write), and that a real configured
//  contact is accepted and normalized for the notice. No raw subject appears here
//  at all — the contact is disclosure metadata, not a secret.
//
//  Named acceptance: AT-T022-CONTACT-REQUIRED, AT-T022-CONTACT-PLACEHOLDER-REJECT,
//  AT-T022-CONTACT-ROUTABLE-ACCEPT.
// ============================================================================
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimOperatorContactPolicyTests
    {
        // ---- fail-closed: absent ----

        [Theory] // AT-T022-CONTACT-REQUIRED
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        public void AbsentContactIsRefused(string? contact)
        {
            var r = OperatorContactPolicy.Validate(contact);
            Assert.False(r.IsAcceptable);
            Assert.Equal(OperatorContactPolicy.CodeAbsent, r.RejectionCode);
            Assert.Equal(string.Empty, r.NormalizedContact);
        }

        // ---- fail-closed: the exact placeholder the PR shipped, plus its family ----

        [Theory] // AT-T022-CONTACT-PLACEHOLDER-REJECT
        [InlineData("pilot-ops@example.invalid")]   // the exact hard-coded value the audit found
        [InlineData("ops@example.invalid")]
        [InlineData("anything@foo.invalid")]
        [InlineData("ops@example.com")]
        [InlineData("ops@example.org")]
        [InlineData("ops@sub.example.test")]
        [InlineData("ops@localhost")]
        [InlineData("ops@host.localhost")]
        [InlineData("changeme")]
        [InlineData("TODO")]
        [InlineData("none")]
        [InlineData("placeholder")]
        [InlineData("https://example.com/contact")]
        [InlineData("mailto:ops@example.invalid")]
        public void NonRoutablePlaceholderIsRefused(string contact)
        {
            var r = OperatorContactPolicy.Validate(contact);
            Assert.False(r.IsAcceptable);
            Assert.Equal(OperatorContactPolicy.CodePlaceholder, r.RejectionCode);
            Assert.Equal(string.Empty, r.NormalizedContact);
        }

        // ---- fail-closed: malformed ----

        [Theory] // AT-T022-CONTACT-REQUIRED (malformed shape)
        [InlineData("not-an-email")]        // no '@', no scheme, no dot-host
        [InlineData("@nohostpart.com")]     // empty local part
        [InlineData("local@")]              // empty domain
        [InlineData("a@b@c.com")]           // two '@'
        [InlineData("ops@\u00A0nbsp.com")]  // NBSP inside → malformed
        [InlineData("ops@trailingdot.")]    // domain ends with dot
        [InlineData("ftp://ops.example.org")] // unsupported scheme, not email shape
        public void MalformedContactIsRefused(string contact)
        {
            var r = OperatorContactPolicy.Validate(contact);
            Assert.False(r.IsAcceptable);
            Assert.NotEqual(string.Empty, r.RejectionCode);
        }

        // ---- accept: a real, routable configured contact ----

        [Theory] // AT-T022-CONTACT-ROUTABLE-ACCEPT
        [InlineData("qa-ops@niflheim-pilot.dev")]
        [InlineData("daniel+t009l@polyphonyrequiem.dev")]
        [InlineData("mailto:qa-ops@niflheim-pilot.dev")]
        [InlineData("https://niflheim-pilot.dev/contact")]
        public void RoutableConfiguredContactIsAccepted(string contact)
        {
            var r = OperatorContactPolicy.Validate(contact);
            Assert.True(r.IsAcceptable, "expected acceptable: " + contact + " code=" + r.RejectionCode);
            Assert.Equal(string.Empty, r.RejectionCode);
            Assert.Equal(contact.Trim(), r.NormalizedContact);
        }

        [Fact] // trims surrounding whitespace for the printed notice
        public void AcceptedContactIsTrimmed()
        {
            var r = OperatorContactPolicy.Validate("  qa-ops@niflheim-pilot.dev  ");
            Assert.True(r.IsAcceptable);
            Assert.Equal("qa-ops@niflheim-pilot.dev", r.NormalizedContact);
        }

        [Fact] // the disclosure built from a real contact is complete; the placeholder one is NOT
        public void DisclosureCompletenessTracksContactRoutability()
        {
            var cat = new PrivacyInventoryCategory("account-credential", "authenticate pilot join",
                "30 days after pilot close", "operator", "none", "operator deletion command",
                "legitimate-interest (pilot operation)", humanApprovedBasis: true);

            // A routable contact yields a complete disclosure (all mandatory elements present).
            var good = new PilotDisclosure(
                new PilotPrivacyInventory(new[] { cat }, "qa-ops@niflheim-pilot.dev", "notice-v1"),
                "operator deletion/export command", statesExplicitResetPossibility: true);
            Assert.True(good.IsComplete());

            // The policy is what stops the placeholder ever reaching PilotDisclosure — prove the guard fires.
            Assert.False(OperatorContactPolicy.Validate("pilot-ops@example.invalid").IsAcceptable);
        }
    }
}
