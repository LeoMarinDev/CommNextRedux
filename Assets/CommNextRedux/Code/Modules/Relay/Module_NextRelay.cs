// CommNextRedux - the relay's behaviour module.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Relay/Module_NextRelay.cs
//
//   Carried over: `PartComponentModuleType`, the `[SerializeField]` data link, and
//   `AddDataModules` - for the same reason as the modulator's, see that file's header.
//
//   NOT carried over: `OnInitialize` / `OnShutdown`, which subscribed and unsubscribed
//   `EnableRelay.OnChangedValue` and refreshed the part's CommNet node on every toggle. That
//   refresh is not cosmetic - it is what makes a relay start or stop carrying traffic the
//   moment the player flips the switch - but the handler needs the resource rule that decides
//   whether an enabled relay *can* relay, and that rule is not ported yet. Subscribing to
//   something that cannot yet change the answer would be worse than not subscribing: it would
//   look wired-up and do nothing.

using System;
using CommNextRedux.Utilities;
using KSP.Sim.Definitions;
using UnityEngine;

namespace CommNextRedux.Modules.Relay
{
    /// <summary>
    /// Marks this part as a relay and (eventually) charges it for relaying.
    /// </summary>
    [DisallowMultipleComponent]
    public class Module_NextRelay : PartBehaviourModule
    {
        /// <summary>The component module that owns this behaviour module.</summary>
        public override Type PartComponentModuleType => typeof(PartComponentModule_NextRelay);

        /// <summary>The module's data, supplied by <see cref="AddDataModules"/>.</summary>
        [SerializeField] protected Data_NextRelay dataRelay;

        /// <summary>Registers <see cref="Data_NextRelay"/> with the module.</summary>
        /// <remarks>
        /// <c>protected</c>, not <c>public</c>: the base declaration on 0.2.8.5 is
        /// <c>PartBehaviourModule.AddDataModules()</c> and it is protected. The legacy source
        /// declared this override <c>public</c>, which the compiler rejects here as CS0507 - a
        /// real API delta, not a style choice. Same signature change as the live OrbitalSurvey
        /// port, which also declares it protected.
        /// </remarks>
        protected override void AddDataModules()
        {
            base.AddDataModules();

            // The same discriminator as the modulator's: did the loader's serialised-field pass wire
            // this `[SerializeField]` field before this method ran, or is this method the instance's
            // only source? See Module_NextModulator.AddDataModules for the full note.
            bool loaderSupplied = dataRelay != null;

            if (dataRelay == null) dataRelay = new Data_NextRelay();
            Data_NextRelay injected = dataRelay;
            Data_NextRelay registered;
            bool added = DataModules.TryAddUnique(injected, out registered);
            dataRelay = registered;

            if (registered == null)
            {
                return;
            }

            // THE SAME REPAIR AS THE MODULATOR'S (U6b sweep, D-L20-1's class). The relay reaches the
            // PAM through the same registration path, so the instance that will really be used is
            // checked and repaired here too. The relay's own code never calls a throwing
            // context API, so the symptom this prevents is a silently unbound EnableRelay row
            // rather than a failed part load. The line only appears when a repair is needed.
            bool registeredKeyedBefore = registered.HasRelayContextKeys;
            registered.EnsureRelayContextPrepared(
                nameof(AddDataModules),
                "origin=" + nameof(Module_NextRelay) + "." + nameof(AddDataModules)
                + " loaderSupplied=" + loaderSupplied
                + " added=" + added
                + " injectedKept=" + ReferenceEquals(injected, registered)
                + " injectedKeyed=" + injected.HasRelayContextKeys
                + " registeredKeyedBefore=" + registeredKeyedBefore);
        }
    }
}
