// Engine-free reflection reader for the vanilla `Version` runtime version string
// (ADR-0009 §5.1; PR #408 VANILLA-BINDINGS.md §3.5) — M6-OBSERVER.
//
// This is the INPUT half of the drift guard. `AssemblyDriftGuard` compares an observed
// version string against the authorized pins; this reader is what actually produces that
// string from the live `Version` type. It was previously inlined in the engine-bound
// GameAdapters.cs and NEVER tested — a latent `Invoke(null, null)` against the 1-parameter
// `GetVersionString(bool includeMercurialHash = false)` accessor threw on every call and was
// silently swallowed, so the guard only ever saw `string.Empty` and every matching MVID
// resolved to GameVersionDrift. Extracted here (System.* + reflection only) so the reader
// link-compiles into the headless xUnit suite and its arity/fallback behavior is pinned.
//
// Vanilla shape (assembly_valheim decompiled.cs:95317):
//     public static string GetVersionString(bool includeMercurialHash = false)
// `MethodInfo.Invoke` does NOT apply C# optional-parameter defaults — those are a
// compile-time binder feature baked into call sites, not the runtime invoke path — so the
// argument MUST be supplied explicitly. We pass `false`: the live log line
// `Valheim version: l-0.221.12 (network version 36)` (decompiled.cs:81765) comes from a
// `GetVersionString()` call taking the default, so `false` is the exact shape whose output
// the pins were derived from. Passing `true` appends a mercurial hash and matches no pin.
using System;
using System.Reflection;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>
    /// Reads the observed runtime version string from the vanilla <c>Version</c> type via
    /// reflection, degrading through a documented fallback chain. Fail-closed: returns
    /// <see cref="string.Empty"/> only when every path is unavailable, and warns before each
    /// fall-through so an empty observation is never silent (a silent empty read at a security
    /// gate cost the entire M6-PIN chain).
    /// </summary>
    public static class GameVersionReader
    {
        /// <summary>
        /// Read the version string from <paramref name="versionType"/> (the live
        /// <c>Version</c> type, or a test stub of the same shape).
        /// </summary>
        /// <param name="versionType">The <c>Version</c> type, or null if it could not be resolved.</param>
        /// <param name="warn">
        /// Optional sink for a single-line Warning message emitted before any fall-through,
        /// carrying the exception type + message. Wired to the BepInEx logger in production;
        /// null in tests.
        /// </param>
        /// <returns>The observed version string, or <see cref="string.Empty"/> if unobtainable.</returns>
        public static string Read(Type? versionType, Action<string>? warn = null)
        {
            if (versionType == null)
            {
                warn?.Invoke("SBPRQA: Version type not resolvable; version observation empty (guard fails closed).");
                return string.Empty;
            }

            // 1. Public static string GetVersionString(...) — the vanilla accessor.
            //    Branch on arity rather than assuming it: a future vanilla patch that changes
            //    the parameter count must degrade to the field fallback, never throw.
            try
            {
                MethodInfo? m = versionType.GetMethod(
                    "GetVersionString", BindingFlags.Public | BindingFlags.Static);
                if (m != null && m.ReturnType == typeof(string))
                {
                    int arity = m.GetParameters().Length;
                    switch (arity)
                    {
                        case 0:
                            if (m.Invoke(null, null) is string s0 && !string.IsNullOrEmpty(s0))
                                return s0;
                            break;
                        case 1:
                            // includeMercurialHash: false — the shape the pins were derived from.
                            if (m.Invoke(null, new object[] { false }) is string s1 && !string.IsNullOrEmpty(s1))
                                return s1;
                            break;
                        default:
                            warn?.Invoke(
                                $"SBPRQA: Version.GetVersionString has unexpected arity {arity}; " +
                                "using CurrentVersion fallback.");
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                warn?.Invoke(
                    $"SBPRQA: Version.GetVersionString invoke failed ({e.GetType().Name}: {e.Message}); " +
                    "using CurrentVersion fallback.");
            }

            // 2. Documented fallback: the CurrentVersion accessor. Vanilla declares it as a
            //    static auto-property (decompiled.cs:95314), whose backing store is a private
            //    field — so try the property getter first, then a public field of the same name
            //    in case the shape ever changes. ToString() yields "0.221.12" (GameVersion.ToString).
            try
            {
                object? current = null;

                PropertyInfo? p = versionType.GetProperty(
                    "CurrentVersion", BindingFlags.Public | BindingFlags.Static);
                if (p != null && p.CanRead)
                    current = p.GetValue(null);

                if (current == null)
                {
                    FieldInfo? f = versionType.GetField(
                        "CurrentVersion", BindingFlags.Public | BindingFlags.Static);
                    if (f != null)
                        current = f.GetValue(null);
                }

                string? str = current?.ToString();
                if (!string.IsNullOrEmpty(str))
                    return str!;
            }
            catch (Exception e)
            {
                warn?.Invoke(
                    $"SBPRQA: Version.CurrentVersion fallback failed ({e.GetType().Name}: {e.Message}); " +
                    "version observation empty (guard fails closed).");
                return string.Empty;
            }

            warn?.Invoke(
                "SBPRQA: no version string obtainable from accessor or CurrentVersion; " +
                "version observation empty (guard fails closed).");
            return string.Empty;
        }
    }
}
