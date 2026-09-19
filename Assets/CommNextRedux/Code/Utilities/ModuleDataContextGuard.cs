// CommNextRedux - the module-data context guard (U6b, D-L20-1).
//
// WHY THIS FILE EXISTS
//   On this pin a `ModuleProperty<T>` is only usable through the PAM once it carries a
//   `ContextKey`: `ModuleData.PrepareDataContext()` walks the module-data object's public instance
//   fields, assigns `ContextKey = <the C# field name>` to every one that is an `IModuleProperty`,
//   and files the property's `DataContext` in `_propertyContextLookup` under that key. Two of the
//   APIs this mod calls then look that dictionary up **by the key**:
//
//     * `ModuleData.SetDropdownData(IModuleProperty, DropdownItemList)`
//       `_propertyContextLookup.TryGetValue(property.ContextKey, out ...)` - a **null key throws
//       `ArgumentNullException: key`**, and the exception escapes the caller. Called from
//       `Data_NextModulator.OnPartBehaviourModuleInit`, that aborts
//       `ObjectAssemblyPart.FinalizeModules` and the part never loads (L20, D-L20-1).
//     * `ModuleData.SetVisible(IModuleDataContext, bool)` - a null key only logs
//       "Cannot change visibility on a NULL PropertyContextKey", so a mistake here is silent.
//       (`ModuleData.AddProperty`, which this mod does not call, throws instead.)
//
//   The pass is guarded by two pieces of state the mod cannot read - a private `_isCached` that
//   short-circuits it and a private `_isDataContextPrepared` - and it is *conditional* at
//   construction time (`ModuleData::.ctor` runs it only when
//   `GameManager.Instance.Game.GlobalGameState.GetState() != 1`). A `ModuleData` instance that
//   reaches a part load without a completed pass therefore has non-null `ModuleProperty` objects
//   whose `ContextKey` is null, and the game's own registration path does not guarantee one:
//   `PartBehaviourModule.InitForDataModules` clears `DataModules` and re-runs `AddDataModules`, so
//   the instance the module finally uses is whichever one the reregistration ends up with.
//
// WHAT THIS DOES ABOUT IT
//   `ModuleData.RebuildDataContext()` is the game's own repair - a public method whose whole body
//   is `_isCached = false; PrepareDataContext();` - and it is the same call the game itself makes
//   after it re-populates module data (`PartBehaviourModule.SetDataModuleValues` calls it on every
//   instance it registers; the PatchManager's `PartModuleLoadPatcher.ApplyOnGameObject` calls it
//   before it injects an authored instance). This guard calls it **only when a property the caller
//   is about to use has no key**, so the healthy path costs one string check per property and
//   writes nothing, and a repair never runs twice for the same instance.
//
//   It is not a substitute for the caller's own guard: a repair that fails still leaves the part
//   loadable, because the throwing call sites are guarded individually. The guard is the belt; this
//   is the braces.
//
// PROVENANCE
//   The L20 forensic chain is recorded in `Deploy/obj/u06b-fixes.md`: the stack
//   (`ModuleData.SetDropdownData` -> `Data_NextModulator.OnPartBehaviourModuleInit` ->
//   `PartBehaviourModule.Init` -> `ObjectAssemblyPart.FinalizeModules`), the `SetDropdownData` IL
//   (the `TryGetValue` on `IModuleProperty::get_ContextKey`), the `PrepareDataContext` IL (the
//   `set_ContextKey(field.Name)` assignment, the `_isCached` early return, the `_isCached = true`
//   tail), `RebuildDataContext`'s 14-byte body, the `ModuleData::.ctor` state guard, and the two
//   working counterexamples (`PartBehaviourModule.SetDataModuleValues`,
//   `VSwift.Modules.Data.Module_PartSwitch`'s explicit `set_ContextKey` pattern). Every member named
//   above was read out of the shipped `Assembly-CSharp.dll` before it was used.

using System.Text;
using KSP.Sim.Definitions;
using ReduxLib.Logging;

namespace CommNextRedux.Utilities
{
    /// <summary>
    /// Repairs a module-data object whose data context was never built, so that a part load cannot
    /// be aborted by a property with no <c>ContextKey</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Internal, static and argument-driven on purpose.</b> A module-data class calls
    /// <see cref="EnsurePrepared"/> with the properties its own PAM code is about to hand to
    /// <c>SetDropdownData</c>/<c>SetVisible</c>; the guard decides from those, so it can never be
    /// "fixed" for a property the caller does not actually use.
    /// </para>
    /// <para>
    /// <b>Never throws.</b> A guard that could throw would be worse than the defect it guards: it
    /// is called from <c>PartBehaviourModule.AddDataModules</c>, which runs inside
    /// <c>ObjectAssemblyPart.FinalizeModules</c>. Every failure mode here is a log line and a
    /// <c>false</c> return.
    /// </para>
    /// <para>
    /// <b>Log level.</b> The repair is <c>Info</c>, not <c>Debug</c>: the default filter drops
    /// <c>Debug</c>, and the point of the line is that a post-launch grep can prove the repair ran
    /// (or did not have to). Every line is prefixed <c>context-guard:</c>.
    /// </para>
    /// </remarks>
    internal static class ModuleDataContextGuard
    {
        /// <summary>The marker every line from this file carries.</summary>
        internal const string Prefix = "context-guard: ";

        /// <summary>
        /// True when every supplied property exists and carries the key its data context is filed
        /// under. An empty set is trivially true - callers pass the properties they are about to use.
        /// </summary>
        internal static bool AllKeyed(params IModuleProperty[] properties)
        {
            for (int i = 0; i < properties.Length; i++)
            {
                IModuleProperty property = properties[i];
                if (property == null || string.IsNullOrEmpty(property.ContextKey))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>A compact, log-safe description of each property's key state.</summary>
        /// <remarks>
        /// Names the assigned key when there is one, so a log reader can tell "unkeyed" (the defect)
        /// from "keyed under a key I did not expect" (a rename). Allocates, so it is only ever
        /// called on the repair path - never per frame.
        /// </remarks>
        internal static string Describe(params IModuleProperty[] properties)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < properties.Length; i++)
            {
                if (i > 0) builder.Append(", ");

                IModuleProperty property = properties[i];
                if (property == null)
                {
                    builder.Append("null");
                }
                else if (string.IsNullOrEmpty(property.ContextKey))
                {
                    builder.Append("unkeyed");
                }
                else
                {
                    builder.Append("keyed:").Append(property.ContextKey);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Ensures <paramref name="data"/> can be used through the PAM for the supplied properties,
        /// repairing the data context when it was never built.
        /// </summary>
        /// <param name="data">The module-data object the caller is about to use. May be null.</param>
        /// <param name="logger">The caller's own logger, so the line carries the caller's name.</param>
        /// <param name="phase">
        /// Where the caller is - <c>nameof(AddDataModules)</c>, <c>nameof(OnPartBehaviourModuleInit)</c>
        /// - so a log reader can tell the two guard call sites apart.
        /// </param>
        /// <param name="detail">
        /// A short caller-built provenance string, logged verbatim. It is where the mod records the
        /// facts the guard cannot observe: which instance was registered, whether the instance it was
        /// handed was kept, and whether that instance was keyed before the repair.
        /// </param>
        /// <param name="properties">The properties the caller is about to hand to the PAM APIs.</param>
        /// <returns>
        /// True when every property is keyed (before or after the repair), false when it is not. A
        /// false return means "do not call the throwing PAM APIs with this object" - it is not an
        /// instruction to fail the part load.
        /// </returns>
        internal static bool EnsurePrepared(ModuleData data, ILogger logger, string phase, string detail,
            params IModuleProperty[] properties)
        {
            if (AllKeyed(properties))
            {
                return true;
            }

            string stateBefore = Describe(properties);

            if (data == null)
            {
                logger.LogError(Prefix + "no data object to repair in phase '" + phase + "' (" + detail
                    + "), state=[" + stateBefore + "] - this module's PAM rows cannot be bound.");
                return false;
            }

            // The game's own repair: _isCached = false, then a full PrepareDataContext() pass, which
            // clears this instance's context and re-derives every ContextKey from its field name.
            data.RebuildDataContext();

            if (AllKeyed(properties))
            {
                logger.LogInfo(Prefix + "repaired in phase '" + phase + "' (" + detail + "), before=["
                    + stateBefore + "] after=[" + Describe(properties)
                    + "] - this module data object had no data context of its own, so "
                    + "RebuildDataContext() was run over it and its PAM rows are bound.");
                return true;
            }

            logger.LogError(Prefix + "could not repair in phase '" + phase + "' (" + detail + "), before=["
                + stateBefore + "] after=[" + Describe(properties)
                + "] - RebuildDataContext() did not key every property, so this module's PAM rows "
                + "cannot be bound. The part still loads: every throwing PAM call site is guarded.");
            return false;
        }
    }
}
