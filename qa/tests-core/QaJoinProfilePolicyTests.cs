// M6-JOIN3 / B1 — QA join profile is an ALLOWLIST OF ONE. These engine-free tests prove the
// profile-selection policy makes a human profile structurally unreachable: it selects the
// configured QA name, creates it when absent, and REFUSES (fail closed) rather than fall back
// to any existing (possibly human) profile. The C# hook applies this decision against the real
// vanilla profile list and re-asserts the resolved name before OnCharacterStart fires.
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class QaJoinProfilePolicyTests
    {
        // The known human profiles on this host that must NEVER be selected by a QA run.
        private static readonly string[] HumanProfiles =
        {
            "pololol", "requiem", "polyphony", "polyrequiem", "Developer",
            "devtester", "polyluna", "pololol_backup_auto-20260727161529",
        };

        [Fact]
        public void QaProfilePresent_isSelectedByName()
        {
            var existing = new[] { "pololol", "requiem", "sbpr_qa_join" };
            Assert.Equal(
                QaJoinProfilePolicy.Decision.SelectExisting,
                QaJoinProfilePolicy.Resolve("sbpr_qa_join", existing));
        }

        [Fact]
        public void QaProfileAbsent_createsIt_neverFallsBackToAnExistingProfile()
        {
            // The QA profile is missing but human profiles exist. The ONLY correct outcomes are
            // CREATE the QA profile — NEVER SelectExisting (which would reach a human profile).
            var decision = QaJoinProfilePolicy.Resolve("sbpr_qa_join", HumanProfiles);
            Assert.Equal(QaJoinProfilePolicy.Decision.CreateThenSelect, decision);
            Assert.NotEqual(QaJoinProfilePolicy.Decision.SelectExisting, decision);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NoQaProfileConfigured_failsClosed_evenWhenProfilesExist(string? configured)
        {
            // No QA profile named => refuse the join. A missing config must NEVER select a human
            // profile that happens to be first on disk.
            var decision = QaJoinProfilePolicy.Resolve(configured, HumanProfiles);
            Assert.Equal(QaJoinProfilePolicy.Decision.RefuseNoQaProfileConfigured, decision);
        }

        [Fact]
        public void EveryHumanProfile_isStructurallyUnreachable_underTheFinalGuard()
        {
            // The final belt-and-braces assertion: the resolved profile must be EXACTLY the
            // configured QA name. No human profile name can ever pass it — this is what makes a
            // human character unreachable by exclusion, not by luck.
            const string qa = "sbpr_qa_join";
            foreach (var human in HumanProfiles)
            {
                Assert.False(
                    QaJoinProfilePolicy.ResolvedNameIsQaProfile(qa, human),
                    $"human profile '{human}' must NOT be accepted as the QA profile");
            }
        }

        [Fact]
        public void FinalGuard_acceptsOnlyTheExactConfiguredName()
        {
            Assert.True(QaJoinProfilePolicy.ResolvedNameIsQaProfile("sbpr_qa_join", "sbpr_qa_join"));
            // Trailing whitespace/newline (a sidecar-delivered value) is tolerated on both sides.
            Assert.True(QaJoinProfilePolicy.ResolvedNameIsQaProfile("sbpr_qa_join", "sbpr_qa_join\n"));
            // A near-miss (different case / substring) is rejected — filenames are distinct files.
            Assert.False(QaJoinProfilePolicy.ResolvedNameIsQaProfile("sbpr_qa_join", "SBPR_QA_JOIN"));
            Assert.False(QaJoinProfilePolicy.ResolvedNameIsQaProfile("sbpr_qa_join", "sbpr_qa"));
            Assert.False(QaJoinProfilePolicy.ResolvedNameIsQaProfile("sbpr_qa_join", null));
        }

        [Fact]
        public void IsAllowlistOfOne_notADenylist_soANewHumanProfileCannotLeak()
        {
            // A brand-new human profile Daniel makes tomorrow (not in any denylist) still cannot
            // be selected: it is simply "not the QA name", so Resolve says CREATE (not Select) and
            // the final guard rejects it. This is the property a denylist could never guarantee.
            var withBrandNewHuman = new[] { "sbpr_qa_join_MISSING", "danielsNewCharacter2027" };
            Assert.Equal(
                QaJoinProfilePolicy.Decision.CreateThenSelect,
                QaJoinProfilePolicy.Resolve("sbpr_qa_join", withBrandNewHuman));
            Assert.False(QaJoinProfilePolicy.ResolvedNameIsQaProfile("sbpr_qa_join", "danielsNewCharacter2027"));
        }
    }
}
