// Nullable-flow attribute polyfill for the net48 target.
//
// WHY THIS FILE EXISTS (CLEANUP 3/3 follow-up, card t_0234cc42):
// The Assets.cs `return null` optional-returns were converted to the `bool TryX(out …)`
// convention (the audit's named null-as-value fix). The clean Try idiom relies on
// [NotNullWhen(true)] on the `out` parameter so the compiler flow-narrows the result to
// non-null on the `true` branch — without it, every `if (Assets.TryX(out var x)) { x.Foo() }`
// call site would raise CS8602 (deref of a possibly-null reference) under
// <Nullable>enable</Nullable> + <TreatWarningsAsErrors>, defeating the whole point.
//
// .NET Framework 4.8's reference assemblies do NOT ship
// System.Diagnostics.CodeAnalysis.NotNullWhenAttribute (it arrived in the BCL with
// .NET Core 3.0 / netstandard2.1's runtime, but net48 binds against the netstandard2.0
// surface here — verified: a probe build failed CS0246). The Roslyn nullable analyzer,
// however, binds these attributes BY FULLY-QUALIFIED NAME, not by assembly identity — so a
// source-defined copy in the right namespace is honored exactly like the BCL one. This is
// the standard, Microsoft-documented polyfill pattern for down-level nullable annotations.
//
// Guarded so it silently no-ops if this assembly is ever retargeted to a TFM whose BCL
// already provides the type (avoids a duplicate-definition CS0433).
#if !NET5_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>
    /// Specifies that when a method returns <see cref="ReturnValue"/>, the associated
    /// <see langword="out"/> parameter will not be <see langword="null"/> even if the
    /// corresponding type allows it. Polyfill of the BCL attribute for the net48 target;
    /// see this file's header for why a source copy is needed and why it works.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute : Attribute
    {
        /// <summary>Initializes the attribute with the specified return-value condition.</summary>
        /// <param name="returnValue">
        /// The return value condition. If the method returns this value, the associated
        /// parameter will not be <see langword="null"/>.
        /// </param>
        public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

        /// <summary>Gets the return value condition.</summary>
        public bool ReturnValue { get; }
    }
}
#endif
