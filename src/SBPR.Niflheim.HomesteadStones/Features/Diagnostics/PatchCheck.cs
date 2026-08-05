using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace SBPR.Niflheim.HomesteadStones.Features.Diagnostics
{
    /// <summary>
    /// Marks a <c>[HarmonyPatch]</c> class as DELIBERATELY not registered in
    /// <c>Plugin.Awake()</c>, so <see cref="PatchCheck"/> does not report it.
    ///
    /// This exists because silence-by-omission is precisely what produced the three
    /// dead-code defects PatchCheck was built to catch. An intentionally-unregistered
    /// patch class must therefore say so OUT LOUD, in the source, with a reason —
    /// never by being quietly invisible to the guard. The reason is mandatory and is
    /// echoed in the boot log, so an operator reading the log sees the deliberate
    /// omissions alongside the accidental ones and can judge them.
    ///
    /// This is NOT the escape hatch for a config-gated seam. Every config-gated admin
    /// seam in this assembly (RelationshipProvisioningAdmin, SavorProvisioningAdmin,
    /// MasterworkOwnershipProvisioningAdmin, LocalProgressionProvisioningAdmin) IS
    /// unconditionally handed to PatchAll — the gate lives INSIDE the patch, which
    /// checks its server-owned flag at runtime. Those classes weave normally and pass
    /// this guard with no opt-out. Reach for this attribute only when a class must
    /// genuinely never be registered at all.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    internal sealed class DeliberatelyUnregisteredAttribute : Attribute
    {
        public DeliberatelyUnregisteredAttribute(string reason)
        {
            Reason = reason;
        }

        /// <summary>Why this patch class is intentionally never handed to PatchAll.</summary>
        public string Reason { get; }
    }

    /// <summary>
    /// Patch-registration watchdog — ported from <c>SBPR.Trailborne/Runtime/PatchCheck.cs</c>,
    /// which HomesteadStones never received.
    ///
    /// A <c>[HarmonyPatch]</c> class that ships but was never handed to
    /// <c>harmony.PatchAll(typeof(X))</c> in <c>Plugin.Awake()</c> compiles fine, ships in
    /// the DLL, passes its unit tests, and does NOTHING. No build error, no boot warning,
    /// no runtime signal — the feature is simply inert in-world. This guard makes the
    /// server scream the moment such a class boots.
    ///
    /// ── Why this exists: three occurrences, none caught by the two existing guards ──
    ///   1. IAP-015 (live smoke t_48797ca3 at 04efd544) — three operator classes shipped
    ///      unregistered; sbpr_pilotop was absent from Terminal.commands.
    ///   2. T030 Ready Hands, first failure (QA t_2b1e690d) — bound to Humanoid, which
    ///      declares neither target method, so discovery resolved ZERO methods.
    ///   3. T030 Ready Hands, second failure (ADO #125) — correct class, correct Player
    ///      binding, simply absent from Plugin.cs's PatchAll list entirely.
    ///
    /// Neither existing guard covers the general case, and both are correct within their
    /// own scope:
    ///   • <c>OperatorSurfaceConformance.Verify</c> uses exactly this technique but asserts
    ///     three HARDCODED operator roles. Right shape, fixed scope of three.
    ///   • The CI metadata guard (.github/workflows/ci.yml) reflects over assembly_valheim.dll
    ///     and proves the TARGET METHODS still exist on Player. It is structurally blind to
    ///     whether the patch class was ever REGISTERED — it passed green throughout the
    ///     entire period Ready Hands was dead.
    ///
    /// ── How it works, and why the obvious shortcuts do not ──────────────────────────
    ///   1. Enumerate OUR attributed patch classes by reflecting over this assembly. The
    ///      test is "type-level [HarmonyPatch] OR any declared method carries [HarmonyPatch]".
    ///      The method-level prong is load-bearing: a container whose attributes live only on
    ///      its postfix methods is still registered by Plugin.Awake(), and a type-level-only
    ///      scan would false-negative it. GetTypes() returns nested types, so nested patch
    ///      containers are each considered individually, exactly as Awake() registers them.
    ///   2. Walk Harmony's global registry (<see cref="Harmony.GetAllPatchedMethods"/> +
    ///      <see cref="Harmony.GetPatchInfo"/>) and collect the DECLARING TYPES of every woven
    ///      patch method whose owner is this mod. Keying on the patch method's declaring type —
    ///      NOT its target method — is essential: two of our classes may patch the SAME vanilla
    ///      method, so a target-method check (or a coarse "patched-method count >= patch-class
    ///      count") would see that target still owned by the surviving sibling and let a
    ///      forgotten registration pass. That shortcut would NOT have caught ADO #125.
    ///   3. Any attributed class absent from that set is reported at ERROR, by name.
    ///
    /// ── The two failure modes are reported DISTINCTLY ───────────────────────────────
    /// Both produce zero woven methods, but the operator needs to know which:
    ///   • NEVER REGISTERED   — absent from Plugin.cs's PatchAll list (occurrences 1 and 3).
    ///   • RESOLVED 0 TARGETS — registered, but bound to a type declaring no matching method,
    ///     so patch discovery found nothing (occurrence 2, the Humanoid misbind).
    /// We distinguish them by re-running Harmony's own target resolution over the class's
    /// declared attributes: if the class resolves at least one real target method yet wove
    /// nothing, it was never registered; if it resolves no target at all, its binding is wrong.
    ///
    /// Posture: ERROR-log and CONTINUE, matching <c>OperatorSurfaceConformance</c> and
    /// Trailborne's PatchCheck — scream, don't brick. A dead patch class is a serious defect,
    /// but refusing to boot the server over it would turn one inert feature into a total
    /// outage, and the guard itself is reflection over a live registry (the riskier thing to
    /// hard-gate on). Contrast <c>HomesteadRuntimeDriftCheck</c>, which DOES fail closed
    /// because it gates content realization on authored-data integrity.
    /// </summary>
    internal static class PatchCheck
    {
        /// <summary>
        /// Run the registration audit. Call at the END of <c>Plugin.Awake()</c>, after every
        /// <c>PatchAll</c>. Never throws — a guard that takes down Awake is worse than the
        /// defect it hunts.
        /// </summary>
        public static void Run(string ownerId)
        {
            try
            {
                RunCore(ownerId);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones/PatchCheck] Guard threw: " + ex);
            }
        }

        private static void RunCore(string ownerId)
        {
            Assembly self = Assembly.GetExecutingAssembly();

            // (1) Our attributed patch classes (type-level OR method-level [HarmonyPatch]).
            List<Type> patchClasses = SafeGetTypes(self).Where(IsAttributedPatchClass).ToList();

            if (patchClasses.Count == 0)
            {
                // Structural surprise: we always ship patch classes. If reflection found none,
                // the guard itself is suspect — flag it rather than silently "passing".
                Plugin.Log.LogWarning(
                    "[Niflheim/HomesteadStones/PatchCheck] Found 0 [HarmonyPatch] classes in the " +
                    "assembly — guard could not run (unexpected). No registration check performed.");
                return;
            }

            // (2) Declaring types of every woven patch method WE own in the global registry.
            HashSet<Type> wovenByUs = CollectWovenPatchClasses(ownerId);

            // (3) Diff, separating deliberate opt-outs and the two distinct failure modes.
            var neverRegistered = new List<Type>();
            var resolvedNoTarget = new List<Type>();
            var deliberate = new List<Type>();

            foreach (Type type in patchClasses)
            {
                if (wovenByUs.Contains(type)) continue;

                DeliberatelyUnregisteredAttribute? optOut = type
                    .GetCustomAttributes(typeof(DeliberatelyUnregisteredAttribute), false)
                    .Cast<DeliberatelyUnregisteredAttribute>()
                    .FirstOrDefault();

                if (optOut != null)
                {
                    deliberate.Add(type);
                    Plugin.Log.LogInfo(
                        $"[Niflheim/HomesteadStones/PatchCheck] Deliberately unregistered: " +
                        $"{type.FullName} — {optOut.Reason}");
                    continue;
                }

                if (ResolvesAnyTargetMethod(type))
                    neverRegistered.Add(type);
                else
                    resolvedNoTarget.Add(type);
            }

            foreach (Type type in neverRegistered)
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones/PatchCheck] UNREGISTERED PATCH CLASS: {type.FullName} — " +
                    $"its target method(s) resolve fine, but it produced no woven patch owned by {ownerId}. " +
                    $"Did Plugin.Awake() forget harmony.PatchAll(typeof({type.Name}))? " +
                    "This class is DEAD CODE and its feature is inert in-world.");

            foreach (Type type in resolvedNoTarget)
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones/PatchCheck] PATCH CLASS RESOLVES ZERO TARGETS: {type.FullName} — " +
                    "it produced no woven patch AND its [HarmonyPatch] binding resolves no target method. " +
                    "The declaring type in the attribute probably does not declare the named method " +
                    "(the T030 'Humanoid' failure mode). This class is DEAD CODE regardless of registration.");

            int dead = neverRegistered.Count + resolvedNoTarget.Count;
            if (dead == 0)
                Plugin.Log.LogInfo(
                    $"[Niflheim/HomesteadStones/PatchCheck] ✓ All {patchClasses.Count - deliberate.Count} " +
                    $"registrable patch class(es) woven" +
                    (deliberate.Count > 0 ? $" ({deliberate.Count} deliberately unregistered)." : "."));
            else
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones/PatchCheck] ✗ {dead} patch class(es) DEAD out of " +
                    $"{patchClasses.Count} ({neverRegistered.Count} never registered, " +
                    $"{resolvedNoTarget.Count} resolving zero targets). See above.");
        }

        /// <summary>
        /// True if <paramref name="t"/> is one of our Harmony patch containers: it carries a
        /// type-level <c>[HarmonyPatch]</c>, OR any of its declared methods carries one. The
        /// method-level prong catches containers whose attributes live only on their postfixes.
        /// </summary>
        private static bool IsAttributedPatchClass(Type t)
        {
            if (t == null) return false;

            if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                return true;

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
        /// Report whether this class's <c>[HarmonyPatch]</c> attributes name at least one REAL target
        /// method. This is what separates "you forgot to register it" (targets fine, nothing woven) from
        /// "your binding is wrong" (no target exists at all — the Humanoid misbind).
        ///
        /// Harmony's own <c>GetOriginalMethod</c> is <c>internal</c> to HarmonyX, so we resolve through the
        /// public surface instead: merge the type-level and method-level attributes exactly as
        /// <c>PatchClassProcessor</c> does, then look the target up via <see cref="AccessTools"/> using the
        /// merged declaringType / methodName / argumentTypes.
        ///
        /// LIMITS, stated rather than hidden: this understands ordinary methods, constructors, and
        /// property getters/setters — the shapes this assembly actually uses. A patch using an exotic
        /// binding (or a TargetMethod()/TargetMethods() method, which computes its target in code and
        /// cannot be resolved statically) will report "no target found". That mislabels the CAUSE but
        /// never the VERDICT: such a class is only examined at all when it already wove nothing, and it is
        /// dead either way. The distinction is a diagnostic hint, not the finding.
        /// </summary>
        private static bool ResolvesAnyTargetMethod(Type t)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Static | BindingFlags.Instance
                                     | BindingFlags.DeclaredOnly;

            HarmonyMethod? typeLevel = null;
            try
            {
                List<HarmonyMethod> fromType = HarmonyMethodExtensions.GetFromType(t);
                if (fromType != null && fromType.Count > 0)
                    typeLevel = HarmonyMethod.Merge(fromType);
            }
            catch
            {
                // Fall through — the per-method scan below can still resolve a target.
            }

            // A class computing its own target at runtime cannot be resolved statically; treat the
            // presence of such a method as "resolvable" so we never mislabel it as a bad binding.
            foreach (MethodInfo m in t.GetMethods(flags))
            {
                if (m.Name == "TargetMethod" || m.Name == "TargetMethods") return true;
            }

            if (TryResolve(typeLevel)) return true;

            foreach (MethodInfo m in t.GetMethods(flags))
            {
                if (m.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;

                try
                {
                    List<HarmonyMethod> fromMethod = HarmonyMethodExtensions.GetFromMethod(m);
                    if (fromMethod == null || fromMethod.Count == 0) continue;

                    HarmonyMethod merged = HarmonyMethod.Merge(fromMethod);
                    if (typeLevel != null) merged = typeLevel.Merge(merged);
                    if (TryResolve(merged)) return true;
                }
                catch
                {
                    // Unresolvable — keep scanning the remaining methods.
                }
            }

            return false;
        }

        /// <summary>
        /// Resolve one merged <see cref="HarmonyMethod"/> binding to a real target method via the public
        /// <see cref="AccessTools"/> surface. Returns false for an incomplete binding rather than throwing.
        /// </summary>
        private static bool TryResolve(HarmonyMethod? info)
        {
            if (info?.declaringType == null) return false;

            try
            {
                MethodType kind = info.methodType ?? MethodType.Normal;

                switch (kind)
                {
                    case MethodType.Constructor:
                        return AccessTools.Constructor(info.declaringType, info.argumentTypes) != null;

                    case MethodType.StaticConstructor:
                        return AccessTools.Constructor(info.declaringType, info.argumentTypes, searchForStatic: true) != null;

                    case MethodType.Getter:
                        return AccessTools.PropertyGetter(info.declaringType, info.methodName) != null;

                    case MethodType.Setter:
                        return AccessTools.PropertySetter(info.declaringType, info.methodName) != null;

                    default:
                        if (string.IsNullOrEmpty(info.methodName)) return false;
                        return AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Collect the declaring types of every patch method registered under
        /// <paramref name="ownerId"/> across Harmony's global registry. Patches owned by other
        /// mods are ignored.
        /// </summary>
        private static HashSet<Type> CollectWovenPatchClasses(string ownerId)
        {
            var woven = new HashSet<Type>();

            foreach (MethodBase target in Harmony.GetAllPatchedMethods())
            {
                if (target == null) continue;
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null) continue;

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
        /// <c>Assembly.GetTypes()</c> can throw <see cref="ReflectionTypeLoadException"/> if a type
        /// fails to load; salvage the types that did load rather than letting the guard take down Awake.
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
