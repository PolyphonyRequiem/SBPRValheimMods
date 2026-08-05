// ============================================================================
//  ADO #126 — GENERAL patch-registration conformance guard (HomesteadStones).
// ----------------------------------------------------------------------------
//  Closes the CLASS of defect that has now shipped three times in this assembly:
//  a [HarmonyPatch] class that exists, compiles, passes its unit tests, and is
//  never handed to harmony.PatchAll(typeof(X)) in Plugin.Awake() — so it ships
//  as dead code and the feature is silently inert in-world.
//
//    1. IAP-015 (live smoke t_48797ca3 at 04efd544) — three operator classes
//       unregistered; sbpr_pilotop absent from Terminal.commands.
//    2. T030 Ready Hands, first failure (QA t_2b1e690d) — bound to Humanoid,
//       which declares neither target method; discovery resolved ZERO methods.
//    3. T030 Ready Hands, second failure (ADO #125) — correct class, correct
//       Player binding, simply absent from the PatchAll list.
//
//  WHY A SOURCE-CONFORMANCE GUARD, and why a GENERAL one:
//
//  The runtime PatchCheck added alongside this test (Features/Diagnostics/
//  PatchCheck.cs) is the boot-time net. But it only speaks on a live host with a
//  real Harmony registry, so it is NOT deterministic CI coverage — the defect
//  still reaches a server before anyone hears about it. This test is the PR-time
//  half: it catches the same defect before merge, headless, with no Valheim SDK.
//
//  This test project references NO UnityEngine / HarmonyLib / Valheim assemblies,
//  so the net48-only Plugin.cs and the patch seams cannot be link-compiled here
//  and Harmony's weave cannot be exercised. The strongest regression available in
//  a clean-room build is therefore to read the SHIPPED SOURCE and assert every
//  [HarmonyPatch] class appears in Plugin.Awake()'s registration list.
//
//  The established idiom in this repo (OperatorSurfaceRegistrationGuardTests,
//  InventoryOpenSuppressRegistrationGuardTests) writes ONE hand-authored guard
//  per class. That does not scale and is precisely why ADO #125 slipped: nobody
//  wrote the twenty-ninth guard by hand. This test enumerates the patch classes
//  from source instead, so a NEW patch class is covered the moment it is added —
//  no one has to remember to write anything.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class HomesteadPatchRegistrationConformanceTests
    {
        private const string PluginSource = "src/SBPR.Niflheim.HomesteadStones/Plugin.cs";
        private const string SourceRoot   = "src/SBPR.Niflheim.HomesteadStones";

        /// <summary>
        /// Every [HarmonyPatch]-bearing class in the shipped HomesteadStones source must appear in
        /// Plugin.Awake()'s PatchAll list, or carry an explicit [DeliberatelyUnregistered] opt-out.
        /// </summary>
        [Fact]
        public void Every_harmony_patch_class_is_registered_in_plugin_awake()
        {
            var registered = RegisteredTypeNames();
            var patchClasses = DeclaredPatchClasses();

            Assert.True(
                patchClasses.Count > 0,
                "Found 0 [HarmonyPatch] classes in " + SourceRoot + " — the scanner is broken, " +
                "not the codebase. A vacuous guard is worse than no guard.");

            var unregistered = patchClasses
                .Where(pc => !pc.DeliberatelyUnregistered)
                .Where(pc => !registered.Contains(pc.Name))
                .OrderBy(pc => pc.Name)
                .ToList();

            Assert.True(
                unregistered.Count == 0,
                "DEAD PATCH CLASS(ES) — declared with [HarmonyPatch] but never handed to " +
                "harmony.PatchAll(typeof(X)) in Plugin.Awake():\n" +
                string.Join("\n", unregistered.Select(pc => $"  • {pc.Name}   ({pc.RelativePath})")) +
                "\n\nHarmonyX patches ONLY the types explicitly passed to PatchAll — there is no " +
                "assembly-wide auto-scan. An unregistered [HarmonyPatch] class compiles, ships, passes " +
                "its unit tests, and does NOTHING in-world. This is the ADO #125 / IAP-015 defect.\n" +
                "Fix: add the registration to Plugin.Awake(). If the class is INTENTIONALLY never " +
                "registered, mark it [DeliberatelyUnregistered(\"reason\")] — the opt-out must be " +
                "explicit and greppable, because silence-by-omission is what caused these bugs.");
        }

        /// <summary>
        /// The inverse direction: a PatchAll registration naming a type that no longer declares
        /// [HarmonyPatch] is a stale registration — it weaves nothing and misleads the next reader
        /// into believing a seam is armed.
        /// </summary>
        [Fact]
        public void Every_registration_names_a_real_patch_class()
        {
            var registered = RegisteredTypeNames();
            var patchClassNames = DeclaredPatchClasses().Select(pc => pc.Name).ToHashSet(StringComparer.Ordinal);

            var stale = registered
                .Where(r => !patchClassNames.Contains(r))
                .OrderBy(r => r)
                .ToList();

            Assert.True(
                stale.Count == 0,
                "STALE REGISTRATION(S) — Plugin.Awake() calls PatchAll(typeof(X)) for type(s) that " +
                "declare no [HarmonyPatch] attribute anywhere:\n" +
                string.Join("\n", stale.Select(s => "  • " + s)) +
                "\n\nSuch a registration weaves nothing and falsely implies the seam is armed.");
        }

        // ── Source scanning ──────────────────────────────────────────────────────────────────────

        private sealed class PatchClass
        {
            public string Name = "";
            public string RelativePath = "";
            public bool DeliberatelyUnregistered;
        }

        /// <summary>
        /// The set of simple type names registered via PatchAll(typeof(...)) in Plugin.cs. The
        /// namespace qualification is stripped so that Features.Warrior.X and X compare equal;
        /// the type-name scan below already guarantees names are unique within the assembly.
        /// </summary>
        private static HashSet<string> RegisteredTypeNames()
        {
            var full = Path.Combine(RepoRoot(), PluginSource);
            Assert.True(File.Exists(full), "shipped Plugin.cs not found: " + full);

            var src = StripLineComments(File.ReadAllText(full));
            var rx = new Regex(@"PatchAll\s*\(\s*typeof\s*\(\s*([A-Za-z0-9_.]+)\s*\)\s*\)", RegexOptions.Compiled);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in rx.Matches(src))
            {
                var qualified = m.Groups[1].Value;
                var simple = qualified.Contains('.')
                    ? qualified.Substring(qualified.LastIndexOf('.') + 1)
                    : qualified;
                names.Add(simple);
            }
            return names;
        }

        /// <summary>
        /// Every class in the shipped source carrying [HarmonyPatch] at type level OR on any member.
        /// The member-level prong is load-bearing: a container whose attributes live only on its
        /// postfix methods is still registered by Awake(), and a type-level-only scan false-negatives it.
        /// </summary>
        private static List<PatchClass> DeclaredPatchClasses()
        {
            var root = Path.Combine(RepoRoot(), SourceRoot);
            var results = new List<PatchClass>();

            // A class declaration preceded by attribute lines. Captures the attribute block so we can
            // test it for [HarmonyPatch] and [DeliberatelyUnregistered].
            var classRx = new Regex(
                @"((?:^[ \t]*\[[^\]]*\][ \t]*\r?\n)*)" +          // preceding attribute lines
                @"^[ \t]*(?:internal|public|private|protected)?[ \t]*" +
                @"(?:static[ \t]+|sealed[ \t]+|abstract[ \t]+|partial[ \t]+)*" +
                @"class[ \t]+([A-Za-z0-9_]+)",
                RegexOptions.Multiline | RegexOptions.Compiled);

            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                // Skip build output.
                var rel = Path.GetRelativePath(RepoRoot(), file).Replace('\\', '/');
                if (rel.Contains("/bin/") || rel.Contains("/obj/")) continue;

                var raw = File.ReadAllText(file);
                if (!raw.Contains("HarmonyPatch")) continue;   // cheap prefilter
                var src = StripLineComments(raw);

                foreach (Match m in classRx.Matches(src))
                {
                    var attrs = m.Groups[1].Value;
                    var name = m.Groups[2].Value;

                    bool typeLevel = TypeAttrRx.IsMatch(attrs);
                    bool memberLevel = !typeLevel && ClassBodyHasPatchMember(src, m.Index, name);
                    if (!typeLevel && !memberLevel) continue;

                    results.Add(new PatchClass
                    {
                        Name = name,
                        RelativePath = rel,
                        DeliberatelyUnregistered = DeliberateRx.IsMatch(attrs),
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Does the class starting at <paramref name="classIndex"/> carry a member-level
        /// [HarmonyPatch]? Scans the brace-balanced class body, so a sibling class's attributes
        /// cannot leak in. Nested classes are scanned as part of the outer body, which is
        /// deliberately conservative: it can only ever cause the outer container to be CONSIDERED
        /// a patch class, never cause a real one to be missed.
        /// </summary>
        private static bool ClassBodyHasPatchMember(string src, int classIndex, string className)
        {
            int open = src.IndexOf('{', classIndex);
            if (open < 0) return false;

            int depth = 0;
            int i = open;
            for (; i < src.Length; i++)
            {
                if (src[i] == '{') depth++;
                else if (src[i] == '}')
                {
                    depth--;
                    if (depth == 0) break;
                }
            }
            if (i >= src.Length) i = src.Length - 1;

            var body = src.Substring(open, i - open + 1);

            // Require ATTRIBUTE SYNTAX — '[HarmonyPatch' at the start of a line. A substring search
            // would match the text inside a string literal or doc comment: PatchCheck.cs itself
            // mentions "[HarmonyPatch]" in its ERROR messages, and a naive Contains() flagged the
            // guard class as a dead patch class on this test's first run.
            return MemberAttrRx.IsMatch(body);
        }

        // Attribute matchers. All three tolerate a NAMESPACE-QUALIFIED form
        // ([Features.Diagnostics.DeliberatelyUnregistered(...)]) and the optional 'Attribute'
        // suffix, because both are legal C# and a guard that silently ignores a legal spelling
        // is exactly the silence-by-omission failure this whole card exists to end.
        // The trailing (?![A-Za-z0-9_]) word boundary stops 'HarmonyPatchX' matching 'HarmonyPatch'.
        private const string AttrOpen = @"^[ \t]*\[[ \t]*(?:[A-Za-z0-9_]+[ \t]*\.[ \t]*)*";

        private static readonly Regex MemberAttrRx =
            new Regex(AttrOpen + @"Harmony(?:Patch|Prefix|Postfix|Transpiler|Finalizer)(?:Attribute)?(?![A-Za-z0-9_])",
                      RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex TypeAttrRx =
            new Regex(AttrOpen + @"HarmonyPatch(?:Attribute)?(?![A-Za-z0-9_])",
                      RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex DeliberateRx =
            new Regex(AttrOpen + @"DeliberatelyUnregistered(?:Attribute)?(?![A-Za-z0-9_])",
                      RegexOptions.Multiline | RegexOptions.Compiled);

        // Remove // line comments so a commented-out registration or attribute does NOT satisfy
        // (or trip) the guard. Mirrors InventoryOpenSuppressRegistrationGuardTests.
        private static string StripLineComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            foreach (var line in src.Split('\n'))
            {
                int idx = line.IndexOf("//", StringComparison.Ordinal);
                sb.Append(idx >= 0 ? line.Substring(0, idx) : line).Append('\n');
            }
            return sb.ToString();
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "src", "SBPR.Trailborne", "Plugin.cs")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate repo root (src/SBPR.Trailborne/Plugin.cs) from " + AppContext.BaseDirectory);
        }
    }
}
