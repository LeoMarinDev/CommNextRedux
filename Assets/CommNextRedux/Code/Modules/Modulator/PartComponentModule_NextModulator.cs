// CommNextRedux - the modulator's part component module.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Modulator/PartComponentModule_NextModulator.cs
//
//   Carried over: `PartBehaviourModuleType`, the `DataModulator` accessor, the `DataTransmitter`
//   link, and the `OnStart` body - the `TryGetByType<Data_NextModulator>` lookup and the
//   `TryGetModuleData<PartComponentModule_DataTransmitter, Data_Transmitter>` lookup.
//
//   The legacy's `OnStart` had no logging and no failure path beyond a `LogError` when the data
//   was missing. This port adds one `LogInfo` line, deliberately: it is the only place in the
//   whole mod where the value the Lua patch wrote can be read back out of a running part, and
//   the patch that writes it is a claim that cannot be checked any other way at runtime.
//
// WHY THE TRANSMITTER LOOKUP IS AN ASSERTION, NOT A FEATURE
//   Every modulator in this mod is added to a part that already carries a
//   `PartComponentModule_DataTransmitter`: the two "specific antenna" patch jobs target antennas,
//   and the cleanup job's filter is literally "has a transmitter and has no modulator". So a
//   modulator that cannot find its transmitter means a patch filter did not do what it claims.
//   The marker reports that as `transmitter=False` rather than throwing - a missing link is a
//   wiring bug to be seen in the log, not a reason to abort the part's initialisation.

using System;
using System.Globalization;
using KSP.Modules;
using KSP.Sim.impl;
using ReduxLib.Logging;

namespace CommNextRedux.Modules.Modulator
{
    /// <summary>
    /// Owns <see cref="Module_NextModulator"/> on a live part.
    /// </summary>
    public class PartComponentModule_NextModulator : PartComponentModule
    {
        /// <summary>
        /// Named so that every line this mod logs is reachable with a single grep for
        /// <c>[CommNextRedux]</c>. See `Deploy/obj/PORT-PROGRESS.md` for the log contract.
        /// </summary>
        private static readonly ILogger Logger = ReduxLib.ReduxLib.GetLogger("CommNextRedux");

        /// <summary>The behaviour module that owns this component module.</summary>
        public override Type PartBehaviourModuleType => typeof(Module_NextModulator);

        /// <summary>
        /// The transmitter this modulator modulates. Linked by <see cref="OnStart"/>; the patch
        /// filters guarantee it exists.
        /// </summary>
        public Data_Transmitter DataTransmitter { get; private set; }

        private Data_NextModulator _dataModulator;

        /// <summary>The module's data, once <see cref="OnStart"/> has resolved it.</summary>
        public Data_NextModulator DataModulator => _dataModulator;

        /// <summary>
        /// Resolves the data and the companion transmitter, then reports both.
        /// </summary>
        /// <param name="universalTime">
        /// Unused, as in the legacy - the modulator has no time-dependent behaviour.
        /// </param>
        public override void OnStart(double universalTime)
        {
            if (!DataModules.TryGetByType<Data_NextModulator>(out _dataModulator))
            {
                Logger.LogError("Unable to find a Data_NextModulator in the PartComponentModule for "
                                + (Part != null ? Part.PartName : "<no part>"));
                return;
            }

            Data_Transmitter dataTransmitter = null;
            bool hasTransmitter = Part != null
                                  && Part.TryGetModuleData<PartComponentModule_DataTransmitter,
                                      Data_Transmitter>(out dataTransmitter);
            DataTransmitter = dataTransmitter;

            // `range=` is the read-back of the Lua range rebalance: the transmitter's own
            // `CommunicationRange` after the patch manager has finished with the part definition,
            // so a patch that silently matched nothing shows the stock range here rather than a
            // missing line. Resolved member: `KSP.Modules.Data_Transmitter.CommunicationRange`,
            // a `float64` field (monodis --fields on Assembly-CSharp.dll, flist 43755) - not a
            // ModuleProperty, so there is no GetValue() here. Invariant culture because these
            // lines are parsed by scripts.
            Logger.LogInfo("modulator-module part='" + (Part != null ? Part.PartName : "<no part>")
                           + "' kind=" + _dataModulator.ModulatorKind
                           + " transmitter=" + hasTransmitter
                           + " range=" + (hasTransmitter
                               ? dataTransmitter.CommunicationRange.ToString("0.###", CultureInfo.InvariantCulture)
                               : "<none>"));
        }
    }
}
