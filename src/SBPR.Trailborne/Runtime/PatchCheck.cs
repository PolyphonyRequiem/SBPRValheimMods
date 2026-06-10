using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SBPR.Trailborne.Runtime
{
    /// <summary>
    /// Patch-registration watchdog — the sibling of <see cref="SpecCheck"/>.
    ///
    /// SpecCheck screams when a recipe drifts from the locked spec; PatchCheck
    /// screams when a <c>[HarmonyPatch]</c> class ships but was never handed to
    /// <c>harmony.PatchAll(typeof(X))</c> in <c>Plugin.Awake()</c>. Such a class
    /// compiles fine, ships in the DLL, and does NOTHING — no build error, no
    /// boot warning, no runtime signal. It just silently doesn't patch.
    ///
    /// This is the meta-bug fix for the 2026-06-09 underwater-cairn gate (card
    /// t_564f695a): <c>CairnPlacementGatePatch</c> was authored, compiled, and
    /// shipped in the v0.2.10 DLL, but <c>Plugin.Awake()</c> never registered it,
    /// so the elevation gate was dead on arrival. CI is blind to this (it
    /// compiles); the only thing that caught it was Daniel hitting the bug
    /// in-game weeks later. Now the server screams the moment any forgotten
    /// registration boots.
    ///
    /// Runs at the END of <c>Plugin.Awake()</c>, after every <c>PatchAll</c> call.
    /// ERROR-logs and continues (does NOT hard-fail boot) — same visibility tier
    /// and same "scream, don't brick" philosophy as SpecCheck.
    ///
    /// ── How it works (and why the obvious shortcuts don't) ───────────────────
    /// 1. Enumerate OUR attributed patch classes by reflecting over this
    ///    assembly. The test is "type-level [HarmonyPatch] OR any declared method
    ///    carries [HarmonyPatch]" — the method-level prong is load-bearing:
    ///    <c>Registrar</c> has NO type-level attribute, only method-level ones,
    ///    yet <c>Plugin.Awake()</c> registers it. A type-level-only scan would
    ///    false-negative it. <c>GetTypes()</c> returns nested types, so the three
    ///    <c>SignPanelInputBlock.*</c> nested patch containers each appear (and
    ///    pass) individually, exactly as Plugin.Awake() registers them; their
    ///    non-patch outer container has neither prong and is correctly skipped.
    /// 2. Walk Harmony's global registry (<see cref="Harmony.GetAllPatchedMethods"/>
    ///    + <see cref="Harmony.GetPatchInfo"/>) and collect the set of
    ///    DECLARING TYPES of every woven patch method whose <c>owner == ModId</c>.
    ///    Keying on the patch method's declaring type — NOT its target method —
    ///    is essential: <c>PlacementMarkerRadiusPatch</c> and
    ///    <c>CairnPlacementGatePatch</c> patch the SAME vanilla method
    ///    (<c>Player.UpdatePlacementGhost</c>). A target-method check (or a coarse
    ///    "patched-method count ≥ patch-class count" check) would see that method
    ///    still owned by the surviving sibling and let a forgotten registration
    ///    pass — i.e. it would NOT have caught the very bug this guard exists for.
    /// 3. Any attributed class absent from that declaring-type set is reported at
    ///    ERROR, naming the class. As a bonus this also catches a class that WAS
    ///    registered but whose target failed to resolve (it produces no woven
    ///    method either), under the same message.
    /// </summary>
    internal static class PatchCheck
    {
        public static void Run()
        {
            Assembly self = Assembly.GetExecutingAssembly();

            // (1) Our attributed patch classes (type-level OR method-level [HarmonyPatch]).
            List<Type> patchClasses = SafeGetTypes(self).Where(IsAttributedPatchClass).ToList();

            if (patchClasses.Count == 0)
            {
                // Structural surprise: we always ship patch classes. If reflection
                // found none, the guard itself is suspect — flag it rather than
                // silently passing.
                Plugin.Log.LogWarning(
                    "[Trailborne/PatchCheck] Found 0 [HarmonyPatch] classes in the assembly — " +
                    "guard could not run (unexpected). No registration check performed.");
                return;
            }

            // (2) Declaring types of every woven patch method WE own in the global registry.
            HashSet<Type> wovenByUs = CollectWovenPatchClasses(Plugin.ModId);

            // (3) Diff: attributed-but-not-woven => forgotten PatchAll (or target unresolved).
            int missing = 0;
            foreach (Type type in patchClasses)
            {
                if (wovenByUs.Contains(type)) continue;

                missing++;
                Plugin.Log.LogError(
                    $"[Trailborne/PatchCheck] UNREGISTERED PATCH CLASS: {type.FullName} — " +
                    $"has [HarmonyPatch] but produced no woven method owned by {Plugin.ModId}. " +
                    $"Did Plugin.Awake() forget harmony.PatchAll(typeof({type.Name}))? " +
                    "(Or its target method failed to resolve.)");
            }

            if (missing == 0)
                Plugin.Log.LogInfo(
                    $"[Trailborne/PatchCheck] ✓ All {patchClasses.Count} patch classes registered.");
            else
                Plugin.Log.LogError(
                    $"[Trailborne/PatchCheck] ✗ {missing} attributed patch class(es) not woven " +
                    $"out of {patchClasses.Count}. See above.");
        }

        /// <summary>
        /// True if <paramref name="t"/> is one of our Harmony patch containers:
        /// it carries a type-level <c>[HarmonyPatch]</c>, OR any of its declared
        /// methods carries one. The method-level prong catches <c>Registrar</c>,
        /// whose attributes live only on its postfix methods.
        /// </summary>
        private static bool IsAttributedPatchClass(Type t)
        {
            if (t == null) return false;

            // Type-level [HarmonyPatch] (e.g. SignInteractPatch, the nested
            // SignPanelInputBlock.* containers, CairnPlacementGatePatch, …).
            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                return true;

            // Method-level [HarmonyPatch] (e.g. Registrar's private postfixes).
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static | BindingFlags.Instance
                                     | BindingFlags.DeclaredOnly;
            foreach (MethodInfo m in t.GetMethods(flags))
            {
                if (m.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Collect the declaring types of every patch method registered under
        /// <paramref name="ownerId"/> across Harmony's global registry. Patches
        /// owned by other mods are ignored.
        /// </summary>
        private static HashSet<Type> CollectWovenPatchClasses(string ownerId)
        {
            var woven = new HashSet<Type>();

            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                if (target == null) continue;
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;

                // Every patch flavour HarmonyX tracks for this target.
                ReadOnlyCollection<Patch>[] buckets =
                {
                    info.Prefixes, info.Postfixes, info.Transpilers,
                    info.Finalizers, info.ILManipulators,
                };

                foreach (ReadOnlyCollection<Patch> bucket in buckets)
                {
                    if (bucket == null) continue;
                    foreach (Patch p in bucket)
                    {
                        if (p == null || p.owner != ownerId) continue;
                        Type? declaring = p.PatchMethod?.DeclaringType;
                        if (declaring != null)
                            woven.Add(declaring);
                    }
                }
            }

            return woven;
        }

        /// <summary>
        /// <c>Assembly.GetTypes()</c> can throw <see cref="ReflectionTypeLoadException"/>
        /// if a type fails to load; salvage the types that did load rather than
        /// letting the guard take down Awake.
        /// </summary>
        private static Type[] SafeGetTypes(Assembly asm)
        {
            try
            {
                return asm.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).Select(t => t!).ToArray();
            }
        }
    }
}
