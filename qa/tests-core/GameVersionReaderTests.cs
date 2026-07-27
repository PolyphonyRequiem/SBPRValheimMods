// M6-OBSERVER — GameVersionReader tests. Proves the observer that feeds AssemblyDriftGuard
// actually reads a version string, across accessor arities, with the documented CurrentVersion
// fallback, and fails closed (string.Empty) only when every path is unavailable.
//
// Root cause this suite pins: the shipped observer called `Invoke(null, null)` against the
// 1-parameter vanilla `GetVersionString(bool includeMercurialHash = false)`. MethodInfo.Invoke
// does NOT apply C# optional-parameter defaults, so that call throws TargetParameterCountException,
// was silently swallowed, and the guard only ever saw "". The `OneOptionalParam_*` tests are the
// regression that would have caught it. `NaiveInvokeNull_On1ParamAccessor_Throws` documents the
// exact pre-fix failure mode so the anchor is unambiguous.
using System;
using System.Reflection;
using SBPR.QaHarness.T022.Core.ControlPlane;
using Xunit;

namespace SBPR.QaHarness.T022.Core.Tests
{
    public class GameVersionReaderTests
    {
        // ── Stub Version types mirroring the vanilla shapes ────────────────────────────────

        /// <summary>Vanilla shape: one optional bool param, plus the CurrentVersion accessor.</summary>
        private static class VersionOneOptionalParam
        {
            public static string CurrentVersion => "0.221.12";
            public static string GetVersionString(bool includeMercurialHash = false)
                => includeMercurialHash ? "l-0.221.12\nabc123" : "l-0.221.12";
        }

        /// <summary>Legacy shape: zero-parameter accessor.</summary>
        private static class VersionZeroParam
        {
            public static string CurrentVersion => "0.221.12";
            public static string GetVersionString() => "0.221.12";
        }

        /// <summary>Future/foreign shape: two-parameter accessor → must fall back to the field/prop.</summary>
        private static class VersionTwoParam
        {
            public static string CurrentVersion => "9.9.9";
            public static string GetVersionString(bool a, bool b) => a ? "SHOULD-NOT-BE-CALLED" : "nope-" + b;
        }

        /// <summary>No accessor at all — only the CurrentVersion property is present.</summary>
        private static class VersionFieldOnlyProp
        {
            public static string CurrentVersion => "0.221.12";
        }

        /// <summary>No accessor; CurrentVersion exposed as a public static FIELD (shape-change guard).</summary>
        private static class VersionFieldOnlyField
        {
            public static string CurrentVersion = "0.221.12";
        }

        /// <summary>Neither an accessor nor a CurrentVersion member — both paths unavailable.</summary>
        private static class VersionNothing
        {
        }

        // ── Regression: the 1-optional-param accessor is invoked successfully ───────────────
        // This is the exact live shape (decompiled.cs:95317). Against the pre-fix
        // `Invoke(null, null)` this would throw and yield "" — see NaiveInvokeNull_* below.

        [Fact]
        public void OneOptionalParam_InvokedWithFalse_ReturnsPlatformPrefixedValue()
        {
            string v = GameVersionReader.Read(typeof(VersionOneOptionalParam));
            Assert.Equal("l-0.221.12", v);
        }

        [Fact]
        public void OneOptionalParam_DoesNotAppendMercurialHash()
        {
            // Passing `false` (not `true`) is load-bearing: `true` appends a hash and matches no pin.
            string v = GameVersionReader.Read(typeof(VersionOneOptionalParam));
            Assert.DoesNotContain("\n", v);
            Assert.DoesNotContain("abc123", v);
        }

        /// <summary>
        /// Documents the pre-fix defect directly: the naive `Invoke(null, null)` the shipped
        /// observer used throws TargetParameterCountException against the 1-parameter accessor.
        /// This is why the guard only ever saw string.Empty. Anchors the regression above.
        /// </summary>
        [Fact]
        public void NaiveInvokeNull_On1ParamAccessor_Throws()
        {
            MethodInfo m = typeof(VersionOneOptionalParam)
                .GetMethod("GetVersionString", BindingFlags.Public | BindingFlags.Static)!;
            var ex = Assert.Throws<TargetParameterCountException>(() => m.Invoke(null, null));
            Assert.NotNull(ex);
        }

        // ── Arity coverage ─────────────────────────────────────────────────────────────────

        [Fact]
        public void ZeroParamAccessor_StillWorks()
        {
            string v = GameVersionReader.Read(typeof(VersionZeroParam));
            Assert.Equal("0.221.12", v);
        }

        [Fact]
        public void TwoParamAccessor_FallsBackToField_DoesNotThrow()
        {
            // Unexpected arity must degrade to the CurrentVersion fallback, never throw or
            // invoke the foreign accessor.
            string v = GameVersionReader.Read(typeof(VersionTwoParam));
            Assert.Equal("9.9.9", v);
        }

        // ── Fallback coverage ──────────────────────────────────────────────────────────────

        [Fact]
        public void AccessorMissing_FallsBackToProperty()
        {
            string v = GameVersionReader.Read(typeof(VersionFieldOnlyProp));
            Assert.Equal("0.221.12", v);
        }

        [Fact]
        public void AccessorMissing_FallsBackToStaticField()
        {
            string v = GameVersionReader.Read(typeof(VersionFieldOnlyField));
            Assert.Equal("0.221.12", v);
        }

        // ── Fail-closed coverage ───────────────────────────────────────────────────────────

        [Fact]
        public void BothPathsUnavailable_ReturnsEmpty()
        {
            string v = GameVersionReader.Read(typeof(VersionNothing));
            Assert.Equal(string.Empty, v);
        }

        [Fact]
        public void NullType_ReturnsEmpty()
        {
            string v = GameVersionReader.Read(null);
            Assert.Equal(string.Empty, v);
        }

        [Fact]
        public void EmptyObservation_FailsClosedAtGuard()
        {
            // The end-to-end property: an empty version string must fail the drift guard closed.
            string v = GameVersionReader.Read(typeof(VersionNothing));
            var r = AssemblyDriftGuard.Check(
                new ObservedGameAssembly(
                    Guid.Parse("62393fbd-383b-447c-9ae7-7ae16afa654f"), v, 36u));
            Assert.False(r.Ok);
            Assert.Equal("GameVersionDrift", r.Reason);
        }

        // ── Warn sink: an empty read is never silent ───────────────────────────────────────

        [Fact]
        public void EmptyRead_EmitsWarning()
        {
            string? warned = null;
            GameVersionReader.Read(typeof(VersionNothing), msg => warned = msg);
            Assert.NotNull(warned);
        }

        [Fact]
        public void NullType_EmitsWarning()
        {
            string? warned = null;
            GameVersionReader.Read(null, msg => warned = msg);
            Assert.NotNull(warned);
        }

        [Fact]
        public void TwoParamAccessor_EmitsArityWarning()
        {
            string? warned = null;
            GameVersionReader.Read(typeof(VersionTwoParam), msg => warned = msg);
            Assert.NotNull(warned);
            Assert.Contains("arity", warned!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SuccessfulRead_EmitsNoWarning()
        {
            string? warned = null;
            string v = GameVersionReader.Read(typeof(VersionOneOptionalParam), msg => warned = msg);
            Assert.Equal("l-0.221.12", v);
            Assert.Null(warned);
        }
    }
}
