// CommNextRedux - how many bands a modulator can work at once.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Modulator/ModulatorKind.cs - a three-member
//   enum, member names and order unchanged (the order is not load-bearing; the NAMES are).
//
// WIRE FORMAT - MEASURED, NOT ASSUMED
//   A `[KSPDefinition]` enum field on a stock data class serialises as the member NAME string,
//   not as its ordinal. Evidence is the game's own PatchManager cache, which is written by the
//   game's serialiser and therefore shows the format the deserialiser reads back:
//
//     $ jq -r '.data.serializedPartModules[].ModuleData[].DataObject.trackingMode' \
//          $KSP2_ROOT/pm_cache/<part>   ->  "None" | "Vessel" | "Sun"
//     $ ... rotationMode  -> "YAW"       (Data_Deployable/RotationMode)
//     $ ... WheelState    -> "Active"    (Data_WheelBase)
//
//   All three are enum-typed fields on stock data classes and all three are written as the
//   member name. So the Lua patches in this mod must write `ModulatorKind = "DualBand"`, not
//   `ModulatorKind = 1`. A mismatch is loud rather than silent - Newtonsoft refuses to
//   convert either way - so a wrong guess here would fail the load instead of quietly
//   producing the wrong band, and `PartComponentModule_NextModulator.OnStart` then logs the
//   value it actually read back.
//
//   This is also why the enum is declared with NO explicit numeric values: the ordinals are
//   never on the wire, so pinning them would imply a stability that does not exist.

using System;

namespace CommNextRedux.Modules.Modulator
{
    /// <summary>
    /// Which band slots a modulator offers: one, two, or all of them at once.
    /// </summary>
    /// <remarks>
    /// Legacy: identical members, identical names. The names are the serialised values, so they
    /// are part of the saved-game contract and must not be renamed.
    /// </remarks>
    [Serializable]
    public enum ModulatorKind
    {
        /// <summary>A single fixed band.</summary>
        MonoBand,

        /// <summary>A primary and a secondary band, both selectable.</summary>
        DualBand,

        /// <summary>Every band, with no band selection at all.</summary>
        OmniBand
    }
}
