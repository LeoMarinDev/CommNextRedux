// CommNextRedux - the modulator's behaviour module.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Modulator/Module_NextModulator.cs
//
//   Carried over: `PartComponentModuleType`, the `[SerializeField]` data link, and
//   `AddDataModules`.
//
//   CARRIED OVER IN PHASE 8a - the `OnInitialize` / `OnShutdown` pair and the
//   `OmniBand.OnChangedValue` handler, which is the state-driven half of D27: when the player
//   turns the omni toggle on, the `Band` and `SecondaryBand` rows are hidden because an omni
//   transmitter uses neither, and turning it back off restores them. The definition-driven half
//   (a part that never had an omni toggle, a mono-band part with no second band) is
//   `Data_NextModulator.OnPartBehaviourModuleInit`.
//
//   NOT carried over from those handlers: the legacy's
//   `part.partOwner.SimObjectComponent.SimulationObject.Telemetry.RefreshCommNetNode()` call on
//   every change. The port's graph pass runs on the game's own 3-second rebuild and re-reads each
//   part's band selection inside it, in the same way and for the same reason the relay's enable
//   switch is not invalidated either (`Module_NextRelay`'s header). What this handler does is a UI
//   visibility change, which is a real, immediate effect and needs no graph work at all. Recorded
//   in `Deploy/obj/divergences.md`.
//
// WHY AddDataModules MATTERS EVEN THOUGH IT LOOKS LIKE BOILERPLATE
//   The data module is what makes `Data_NextModulator` exist on the part at runtime. The Lua
//   patch writes the *serialised* `ModuleData` entry; this method is what registers the data
//   type with the behaviour module so `PartComponentModule_NextModulator.OnStart` can find it
//   with `TryGetByType`. Omitting it produces a module whose data silently never resolves -
//   the exact failure shape this port is trying to avoid.
//
//   `TryAddUnique` is what keeps a reload from stacking two `Data_NextModulator` entries on the
//   same module: it returns the existing instance through its out parameter when one is already
//   registered, and the field is re-pointed at whatever it returns.

using System;
using KSP.Sim.Definitions;
using UnityEngine;

namespace CommNextRedux.Modules.Modulator
{
    /// <summary>
    /// Modulates this part's signal - in practice, carries the band configuration.
    /// </summary>
    [DisallowMultipleComponent]
    public class Module_NextModulator : PartBehaviourModule
    {
        /// <summary>The component module that owns this behaviour module.</summary>
        public override Type PartComponentModuleType => typeof(PartComponentModule_NextModulator);

        /// <summary>The module's data, supplied by <see cref="AddDataModules"/>.</summary>
        [SerializeField] protected Data_NextModulator dataModulator;

        /// <summary>Registers <see cref="Data_NextModulator"/> with the module.</summary>
        /// <remarks>
        /// <para>
        /// <c>protected</c>, not <c>public</c>: the base declaration on 0.2.8.5 is
        /// <c>PartBehaviourModule.AddDataModules()</c> and it is protected. The legacy source
        /// declared this override <c>public</c>, which the compiler rejects here as CS0507 - a
        /// real API delta, not a style choice. Same signature change as the live OrbitalSurvey
        /// port, which also declares it protected.
        /// </para>
        /// <para>
        /// <b>This method is also the repair point for D-L20-1.</b> <c>TryAddUnique</c> may hand
        /// back an instance other than the one injected into <c>dataModulator</c> - that is its
        /// documented behaviour, and the field is re-pointed at whatever it returns - and the mod
        /// cannot see whether that instance ever had a completed <c>PrepareDataContext</c> pass.
        /// The guard call below records the four facts that discriminate the possibilities
        /// (<c>added</c>, whether the injected instance was kept, and which of the two carried
        /// context keys) and repairs the instance that will actually be used. See
        /// <see cref="ModuleDataContextGuard"/>.
        /// </para>
        /// </remarks>
        protected override void AddDataModules()
        {
            base.AddDataModules();

            // loaderSupplied is the discriminator between the two mechanisms D-L20-1 could have
            // been: the loader's serialised-field pass (`PartModuleLoadPatcher.ApplyOnGameObject`,
            // which sets this `[SerializeField]` field and repairs the data context it hands over)
            // either wired this field or it did not. If it did, the instance below arrived from the
            // loader; if it did not, this method is the only source of the instance and any missing
            // context keys are its own. Logged only on the repair path, like everything else here.
            bool loaderSupplied = dataModulator != null;

            if (dataModulator == null) dataModulator = new Data_NextModulator();
            Data_NextModulator injected = dataModulator;
            Data_NextModulator registered;
            bool added = DataModules.TryAddUnique(injected, out registered);
            dataModulator = registered;

            if (registered == null)
            {
                // TryAddUnique only returns null if it was handed null, which cannot happen above.
                // Guarded anyway: this method runs inside ObjectAssemblyPart.FinalizeModules, and a
                // NullReferenceException here would abort the part load exactly as D-L20-1 did.
                return;
            }

            // THE L20 REPAIR (D-L20-1). This is where the game decides which Data_NextModulator
            // instance the module will actually be used through, and it is not always the instance
            // this method was handed: TryAddUnique returns the already-registered instance through
            // `registered` when one exists, and the field is re-pointed at it. Whether that instance
            // ever had a completed PrepareDataContext pass is not something the mod can see - so the
            // facts are logged (identity, whether the injected instance was kept, and whether either
            // side was keyed) and the instance that will really be used is repaired if it is not.
            // The line only appears when a repair is needed, so the healthy path stays silent.
            bool registeredKeyedBefore = registered.HasBandContextKeys;
            registered.EnsureBandContextPrepared(
                nameof(AddDataModules),
                "origin=" + nameof(Module_NextModulator) + "." + nameof(AddDataModules)
                + " loaderSupplied=" + loaderSupplied
                + " added=" + added
                + " injectedKept=" + ReferenceEquals(injected, registered)
                + " injectedKeyed=" + injected.HasBandContextKeys
                + " registeredKeyedBefore=" + registeredKeyedBefore);
        }

        /// <summary>
        /// Wires the omni toggle, and applies its current value to the two band rows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><c>protected</c>, not <c>public</c> - the second real delta in this pair.</b> The
        /// legacy declared <c>public override void OnInitialize()</c>; on this pin the base member is
        /// protected, and the live in-repo OrbitalSurvey port declares its own override
        /// <c>protected override void OnInitialize()</c>. Declaring it public is CS0507 here, so the
        /// legacy's modifier could not be carried over even though the body could.
        /// </para>
        /// <para>
        /// <b>The initial value is applied, not just observed.</b> A part whose omni toggle is
        /// already on - by a Lua patch, by a symmetry copy, or by a save - must open its Part Action
        /// Menu with the two band rows hidden, and the event only fires on a <i>change</i>. So the
        /// current value is read once here, which is what the legacy did too.
        /// </para>
        /// <para>
        /// <c>OnChangedValue</c> is reachable because <c>ModuleProperty&lt;T&gt;</c> derives from
        /// <c>KSP.Api.CoreTypes.Property&lt;T&gt;</c>, which implements the
        /// <c>KSP.Api.Generic.IProperty&lt;T&gt;</c> that declares the event (interface-implementation
        /// table row 2868; the accessors are <c>add_OnChangedValue</c> / <c>remove_OnChangedValue</c>
        /// on that interface). Verified before use, as rule 10 requires.
        /// </para>
        /// </remarks>
        protected override void OnInitialize()
        {
            base.OnInitialize();

            Data_NextModulator modulator = dataModulator;
            if (modulator == null)
            {
                return;
            }

            modulator.OmniBand.OnChangedValue += OnOmniBandChangedValue;

            if (modulator.OmniBand.GetValue())
            {
                modulator.SetVisible(modulator.Band, false);
                modulator.SetVisible(modulator.SecondaryBand, false);
            }
        }

        /// <summary>Hides or restores the two band rows when the omni toggle changes.</summary>
        /// <param name="isOmniBand">The new value of the omni toggle.</param>
        /// <remarks>
        /// The pair moves together and in the same direction: an omni transmitter credits every
        /// band, so neither <c>Band</c> nor <c>SecondaryBand</c> has any meaning while it is on.
        /// Both calls are the property overload of <c>ModuleData.SetVisible</c> - see
        /// <c>Data_NextModulator</c>'s override for the measurement that pins it.
        /// </remarks>
        private void OnOmniBandChangedValue(bool isOmniBand)
        {
            Data_NextModulator modulator = dataModulator;
            if (modulator == null)
            {
                return;
            }

            modulator.SetVisible(modulator.Band, !isOmniBand);
            modulator.SetVisible(modulator.SecondaryBand, !isOmniBand);
        }

        /// <summary>Detaches the handler the module itself attached.</summary>
        /// <remarks>
        /// The legacy's own unsubscription, kept because the data object can outlive the behaviour
        /// module it was registered against - a part destroyed while its PAM is open would
        /// otherwise leave a handler holding a dead module. <c>protected override</c> for the same
        /// reason <see cref="OnInitialize"/> is.
        /// </remarks>
        protected override void OnShutdown()
        {
            base.OnShutdown();

            Data_NextModulator modulator = dataModulator;
            if (modulator != null)
            {
                modulator.OmniBand.OnChangedValue -= OnOmniBandChangedValue;
            }
        }
    }
}
