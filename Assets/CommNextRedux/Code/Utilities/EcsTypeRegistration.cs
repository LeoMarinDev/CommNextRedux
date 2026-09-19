using System;
using System.Reflection;
using CommNextRedux.Modules.Modulator;
using CommNextRedux.Modules.Relay;
using Unity.Entities;

namespace CommNextRedux.Utilities
{
    /// <summary>
    /// Registers this mod's part-module types with Unity's ECS <see cref="TypeManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this file exists (D-L19-1).</b> On this pin every part module is attached to an ECS
    /// entity. <c>KSP.OAB.ObjectAssemblyPart.EnsureEntity</c> walks the part's
    /// <c>DictionaryValueList&lt;Type, IPartModule&gt;</c>, casts each entry to
    /// <c>KSP.Sim.Definitions.PartBehaviourModule</c> and hands it to
    /// <c>Redux.Ecs.ModuleEntities.Create</c>, whose first act is
    /// <c>EntityManager.AddComponentObject(entity, module)</c>; the sim-side
    /// <c>PartComponentModule</c> reaches the same call through <c>PartComponent.AddModule</c>. That
    /// call builds a <c>ComponentType</c> from the module's <c>System.Type</c>, which resolves
    /// through the <c>TypeManager</c> and throws
    /// <c>"Unknown Type: ... All Entities component types must be registered with the TypeManager"</c>
    /// for anything the manager has not been told about.
    /// </para>
    /// <para>
    /// <b>The game's own module types are registered for it; a mod's are not.</b> Unity's ECS
    /// ILPostProcessor bakes an <c>AssemblyTypeRegistry</c> into <c>Assembly-CSharp</c> during the
    /// player build, which is how the stock modules are known. This assembly is compiled by the
    /// toolchain's <c>csc</c> and gets no such registry, so this mod's four module types have to be
    /// registered by hand at load time - the "component types known only at runtime" path the
    /// exception message itself names.
    /// </para>
    /// <para>
    /// <b>L19's evidence.</b> The Stage-1 build's run carried <b>15</b> <c>Unknown Type:</c> lines and
    /// <b>7</b> failed part loads (<c>Caught Error during loading for part ...</c>), every one
    /// <c>CommNextRedux.Modules.Modulator.Module_NextModulator</c> - the Lua patches add that module to
    /// the stock probe cores, so the first probe core a player picks up in the VAB fails to build its
    /// entity. The stack is verbatim:
    /// <c>TypeManager.GetTypeIndex -&gt; ComponentType..ctor -&gt; EntityManager.AddComponentObject -&gt;
    /// Redux.Ecs.ModuleEntities.Create -&gt; KSP.OAB.ObjectAssemblyPart.EnsureEntity -&gt;
    /// ObjectAssemblyPart.FinalizeModules</c>.
    /// </para>
    /// <para>
    /// <b>Why reflection.</b> <c>TypeManager.TryGetTypeIndex</c> and
    /// <c>GetOrCreateTypeIndexUnsafe</c> are <c>assembly</c>-visibility (internal) on the shipped
    /// <c>Unity.Entities.dll</c>, and this project does not use an assembly publicizer - so the two
    /// members are looked up by name and invoked. <c>GetOrCreateTypeIndex</c>, the member the engine's
    /// own message names, is <b>not</b> usable here: its body calls <c>TryGetTypeIndex</c> and then
    /// either <c>GetOrCreateTypeIndexUnsafe</c> (only for <c>UnityEngine.Object</c>-derived types) or
    /// <c>GetTypeIndex</c>, which re-throws the very same <c>Unknown Type</c> exception for a managed
    /// module class. The <c>Unsafe</c> variant builds the <c>TypeInfo</c> for any type, which is what a
    /// <c>PartBehaviourModule</c> / <c>PartComponentModule</c> pair needs. Both readings are IL
    /// measurements on the installed runtime, and the same shape ships in the in-tree
    /// <c>OrbitalSurvey</c> mirror.
    /// </para>
    /// <para>
    /// <b>Idempotent and argument-free.</b> A type that is already known is reported and left alone,
    /// so a second call (a later loader hook, or another mod's registration) is harmless. Nothing here
    /// reads the config, the logger the loader assigns, or the ECS world: it is called from
    /// <c>OnPreInitialized</c>, before any part module can be touched, and every line is written
    /// through a logger obtained from the static <c>ReduxLib.ReduxLib.GetLogger</c> - never through the
    /// plugin's own field, which the loader may not have assigned yet.
    /// </para>
    /// </remarks>
    internal static class EcsTypeRegistration
    {
        /// <summary>The sink for every line this file writes.</summary>
        /// <remarks>
        /// Named <c>CommNextRedux|EcsTypeRegistration</c>, the same two-part shape the sibling port
        /// uses, so a log reader can tell these four lines apart from the mod's own
        /// <c>[CommNextRedux]</c> family.
        /// </remarks>
        private static readonly ReduxLib.Logging.ILogger Logger =
            ReduxLib.ReduxLib.GetLogger("CommNextRedux|" + nameof(EcsTypeRegistration));

        /// <summary>Selection flags for the two internal members.</summary>
        private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

        // internal static bool TryGetTypeIndex(Type type, out TypeIndex index)
        private static readonly MethodInfo TryGetTypeIndexMethod = typeof(TypeManager).GetMethod(
            "TryGetTypeIndex", StaticNonPublic, null,
            new[] { typeof(Type), typeof(TypeIndex).MakeByRefType() }, null);

        // internal static TypeIndex GetOrCreateTypeIndexUnsafe(Type type)
        private static readonly MethodInfo GetOrCreateTypeIndexUnsafeMethod = typeof(TypeManager).GetMethod(
            "GetOrCreateTypeIndexUnsafe", StaticNonPublic, null, new[] { typeof(Type) }, null);

        /// <summary>
        /// Registers the four module types this mod adds to stock parts.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>All four, declared and inferred together.</b> The modulator pair is the one L19 proved:
        /// both its halves reach <c>AddComponentObject</c>, the view-side
        /// <c>Module_NextModulator</c> through <c>EnsureEntity</c> and the sim-side
        /// <c>PartComponentModule_NextModulator</c> through <c>PartComponent.AddModule</c>. The relay
        /// pair is the same shape on the same call paths and is registered here rather than left for a
        /// relay part to discover - L19 never instantiated one, so the relay half is inferred from the
        /// call paths, not measured. Registering a type the game does not touch costs one entry and one
        /// log line.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> Called from a loader hook: an exception thrown out of it would abort the
        /// rest of <c>OnPreInitialized</c> and could unregister the whole mod, so the body is guarded
        /// and the failure is a log line - the parts would then fail exactly as they did at L19, which
        /// is a diagnosable state, rather than a mod that never loads.
        /// </para>
        /// </remarks>
        internal static void RegisterModuleTypes()
        {
            try
            {
                if (TryGetTypeIndexMethod == null || GetOrCreateTypeIndexUnsafeMethod == null)
                {
                    Logger.LogError("Could not find TypeManager.TryGetTypeIndex / "
                        + "TypeManager.GetOrCreateTypeIndexUnsafe on this runtime. Unity.Entities has "
                        + "changed; parts carrying Module_NextModulator or Module_NextRelay will fail to "
                        + "load with 'Unknown Type ... must be registered with the TypeManager'.");
                    return;
                }

                // No-op when the TypeManager is already up (its first instruction is
                // `if (s_Initialized) return;`), which it normally is by this point.
                TypeManager.Initialize();

                Register(typeof(Module_NextModulator));
                Register(typeof(PartComponentModule_NextModulator));
                Register(typeof(Module_NextRelay));
                Register(typeof(PartComponentModule_NextRelay));
            }
            catch (Exception exception)
            {
                Logger.LogError("ECS type registration failed before it could finish ("
                    + exception.GetType().Name + ": " + exception.Message + "). Parts carrying this "
                    + "mod's modules will fail to load in the VAB and in flight.");
            }
        }

        /// <summary>Registers one type, reporting either outcome and never throwing.</summary>
        /// <param name="type">The module type, view-side or sim-side.</param>
        /// <remarks>
        /// The already-known branch is <c>Info</c>, not <c>Debug</c>: the log level filter drops
        /// <c>Debug</c>, and the point of these lines is that a post-launch grep can prove <b>all four</b>
        /// types were seen without knowing which branch each one took.
        /// </remarks>
        private static void Register(Type type)
        {
            try
            {
                object[] probeArguments = { type, null };
                if ((bool)TryGetTypeIndexMethod.Invoke(null, probeArguments))
                {
                    TypeIndex known = (TypeIndex)probeArguments[1];
                    Logger.LogInfo("'" + type.Name + "' is already registered with the ECS TypeManager "
                        + "(index " + known.Index + ").");
                    return;
                }

                // TypeIndex.ToString() reads DebugTypeName, which is empty in a release player build,
                // so the raw index is logged instead of the (always "null") string form.
                TypeIndex typeIndex = (TypeIndex)GetOrCreateTypeIndexUnsafeMethod.Invoke(
                    null, new object[] { type });

                Logger.LogInfo("Registered '" + type.Name + "' with the ECS TypeManager (index "
                    + typeIndex.Index + ").");
            }
            catch (Exception exception)
            {
                Logger.LogError("Failed to register '" + type.FullName + "' with the ECS TypeManager ("
                    + exception.GetType().Name + ": " + exception.Message + "). Parts carrying this "
                    + "module will fail to load.\n" + exception);
            }
        }
    }
}
