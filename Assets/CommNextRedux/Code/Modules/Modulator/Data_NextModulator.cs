// CommNextRedux - the modulator's module data: what band layout the part offers.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Modulator/Data_NextModulator.cs
//
//   Carried over from the legacy: `ModulatorKind` (the `[KSPDefinition]` field), the three
//   band properties (`OmniBand` / `Band` / `SecondaryBand`), the `ModuleType` override, the
//   modulation-kind part-info entry, and the `Copy` override that carries the band selection.
//
//   `OmniBand` / `Band` / `SecondaryBand` are `[KSPState(CopyToSymmetrySet = false)]
//   ModuleProperty<bool|string>`, exactly as in the legacy, with the same defaults
//   (`false` / `NetworkBands.DefaultBand` / `""`) and the same `PAMDisplayControl` sort indices.
//   These are what the band-match gate reads: the port's node-state pass turns them into a
//   `BandsFlags` mask per node (`NetworkEngine`), so a part's band selection is now load-bearing
//   rather than decorative.
//
//   CARRIED OVER IN PHASE 8a - `OnPartBehaviourModuleInit`, and with it the two band dropdowns:
//       SetDropdownData(Band, <every band>), SetDropdownData(SecondaryBand, <none + every band>),
//       SetVisible(OmniBand, false) on a non-omni part, and SetVisible(SecondaryBand, false) on a
//       mono-band part. This is D27, the last P5 deferral, and it is what finally gives a band a
//       way to change without editing a part definition by hand. Every member it calls was
//       resolved on the installed runtime first - see the remarks on the override.
//
//   STILL NOT carried over:
//
//     * The legacy's `Color` per band and `GetIconSprite` - see `NetworkBands.cs`.
//     * The legacy's `RefreshCommNetNode()` call on a band change. The port's graph pass is driven
//       by the game's own 3-second rebuild and re-reads every band selection inside it, exactly as
//       it does for the relay's enable switch, so no explicit invalidation is needed or wanted.
//       Recorded in `Deploy/obj/divergences.md`.
//
//   The legacy's `Copy` copied exactly those three properties and deliberately left
//   `ModulatorKind` alone (it is a definition, not per-part state). This port does the same, and
//   the guard on the cast is the legacy's: a mismatched source is silently ignored.
//
// THE FIELD NAME IS THE WIRE NAME
//   `ModulatorKind` is written by the Lua patches as a JSON key of the same name, and the
//   game's serialiser writes enums as member-name strings (see `ModulatorKind.cs` for the
//   measurement). Renaming the field breaks the patches silently - the patch would add a
//   second, unrecognised key and the value would stay at its default.

using System;
using System.Collections.Generic;
using CommNextRedux.Network.Bands;
using CommNextRedux.UI;
using CommNextRedux.UI.Utils;
using CommNextRedux.Utilities;
using I2.Loc;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP.UI.Binding;

// The field below is called `ModulatorKind` because that name is the serialised key the Lua
// patches write. That makes `ModulatorKind` ambiguous inside this class - it is both the field
// and the type - so the type gets an alias rather than the field getting renamed.
using Kind = CommNextRedux.Modules.Modulator.ModulatorKind;

namespace CommNextRedux.Modules.Modulator
{
    /// <summary>
    /// Serialised data for <see cref="Module_NextModulator"/>: which bands the part can use.
    /// </summary>
    [Serializable]
    public class Data_NextModulator : ModuleData
    {
        /// <summary>The sink for the two lines the context guard writes.</summary>
        /// <remarks>
        /// Named <c>CommNextRedux|Data_NextModulator</c>, the two-part shape
        /// <c>EcsTypeRegistration</c> uses, so the guard's lines are attributable to this class
        /// while still matching the mod's <c>[CommNextRedux</c> grep.
        /// </remarks>
        private static readonly ReduxLib.Logging.ILogger Logger =
            ReduxLib.ReduxLib.GetLogger("CommNextRedux|" + nameof(Data_NextModulator));

        /// <summary>The behaviour module this data belongs to.</summary>
        public override Type ModuleType => typeof(Module_NextModulator);

        /// <summary>
        /// How many bands this part's modulator offers. Set per part by the Lua patches.
        /// </summary>
        /// <remarks>
        /// Serialised as the member name - <c>"MonoBand"</c>, <c>"DualBand"</c>, <c>"OmniBand"</c>.
        /// The stock value is the enum's zero member (<c>MonoBand</c>) until a patch sets it.
        /// </remarks>
        [KSPDefinition]
        public ModulatorKind ModulatorKind;

        /// <summary>
        /// Whether this part's modulator puts its transmitter on every band at once, instead of on
        /// <see cref="Band"/> and <see cref="SecondaryBand"/>. Default <c>false</c>, as in the
        /// legacy.
        /// </summary>
        /// <remarks>
        /// Read by the port's node-state pass: <c>OmniBand</c> credits every band the port knows
        /// with the transmitter's range, and the other two properties are then ignored. The PAM
        /// only offers this property on an omni-band part - that filtering is
        /// <c>SetVisible</c> in <c>OnPartBehaviourModuleInit</c>, which is not ported (see the
        /// file header).
        /// </remarks>
        [KSPState(CopyToSymmetrySet = false)]
        [LocalizedField(LocalizedStrings.OmniBandKey)]
        [PAMDisplayControl(SortIndex = 1)]
        public ModuleProperty<bool> OmniBand = new ModuleProperty<bool>(false);

        /// <summary>
        /// The band this part's transmitter uses when <see cref="OmniBand"/> is off. Defaults to
        /// <see cref="NetworkBands.DefaultBand"/>, as in the legacy - which is why the band gate
        /// removes nothing on a default install.
        /// </summary>
        /// <remarks>
        /// The value is a band CODE, not an index: it is `[KSPState]`, so a save file carries it,
        /// and a code is stable across a band-set change in a way an index is not. The gate
        /// resolves it with <see cref="NetworkBands.GetBandIndex"/>, which returns -1 for a code
        /// this build does not know - a value the gate treats as "this band contributes nothing".
        /// The toString delegate is the legacy's, and it exists because the PAM renders the raw
        /// value.
        /// </remarks>
        [KSPState(CopyToSymmetrySet = false)]
        [LocalizedField(LocalizedStrings.BandKey)]
        [PAMDisplayControl(SortIndex = 2)]
        public ModuleProperty<string> Band =
            new ModuleProperty<string>(NetworkBands.DefaultBand, band => band == null ? string.Empty : band.ToString());

        /// <summary>
        /// The part's second band, or the empty string for a part with only one. Defaults to
        /// <c>""</c>, as in the legacy.
        /// </summary>
        /// <remarks>
        /// Empty is the legacy's "none" sentinel, not a missing value: the gate resolves it to
        /// band index -1 and skips it. A dual-band part is the only kind the legacy offered this
        /// property to.
        /// </remarks>
        [KSPState(CopyToSymmetrySet = false)]
        [LocalizedField(LocalizedStrings.SecondaryBandKey)]
        [PAMDisplayControl(SortIndex = 3)]
        public ModuleProperty<string> SecondaryBand =
            new ModuleProperty<string>(string.Empty, band => band == null ? string.Empty : band.ToString());

        /// <summary>
        /// True when all three band properties carry the <c>ContextKey</c> their PAM rows bind
        /// through. False means this instance's data context was never built.
        /// </summary>
        internal bool HasBandContextKeys
        {
            get { return ModuleDataContextGuard.AllKeyed(OmniBand, Band, SecondaryBand); }
        }

        /// <summary>
        /// Makes sure this instance's data context exists before a band row is handed to the PAM,
        /// and reports whether it does.
        /// </summary>
        /// <param name="phase">Where the caller is, for the guard's log line.</param>
        /// <param name="detail">The caller's provenance string, logged verbatim.</param>
        /// <remarks>
        /// <para>
        /// <b>The L20 fix, and the reason this class owns the entry point.</b> The band properties
        /// are the only context-dependent state this mod has, so the check belongs here rather than
        /// in a shared helper that cannot know which properties matter. See
        /// <see cref="ModuleDataContextGuard"/> for the IL the repair rests on and
        /// <c>Deploy/obj/u06b-fixes.md</c> for the L20 forensic chain.
        /// </para>
        /// <para>
        /// Called from two places, both of which run before any band row is used:
        /// <c>Module_NextModulator.AddDataModules</c> (which is where the game picks the instance
        /// that will actually be used, and therefore where the repair pays for itself) and
        /// <see cref="OnPartBehaviourModuleInit"/> (which is the throwing call site's own belt).
        /// The second call is a no-op whenever the first one succeeded.
        /// </para>
        /// </remarks>
        internal bool EnsureBandContextPrepared(string phase, string detail)
        {
            return ModuleDataContextGuard.EnsurePrepared(this, Logger, phase, detail, Band, SecondaryBand, OmniBand);
        }

        /// <summary>
        /// Fills the PAM's two band dropdowns and hides the rows this part's kind does not offer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>D27, the last P5 deferral.</b> Until this override existed the three band properties
        /// were stored state: the PAM drew them, the gate honoured what they held, and nothing gave
        /// the player a way to change one without editing a part definition by hand.
        /// </para>
        /// <para>
        /// <b>Every member here was resolved on the installed runtime before it was written</b>
        /// (rule 10), because the legacy's shape is SpaceWarp-era and three of the four could have
        /// moved. Measured on <c>$KSP2_ROOT/KSP2_x64_Data/Managed/Assembly-CSharp.dll</c> under a
        /// per-command <c>MONO_PATH</c> prefix, with <c>grep -c 'failed to parse'</c> = 0:
        /// </para>
        /// <list type="bullet">
        /// <item><description><c>KSP.Sim.Definitions.ModuleData.SetVisible(IModuleDataContext, bool)</c>
        /// - method 34608, the overload that takes a property. The <c>string</c> overload beside it
        /// (34606) is not the one this call binds to.</description></item>
        /// <item><description><c>ModuleData.SetDropdownData(IModuleProperty, DropdownItemList)</c> -
        /// method 34616.</description></item>
        /// <item><description><c>KSP.UI.Binding.DropdownItemList</c> is
        /// <c>DictionaryValueList&lt;string, DropdownItem&gt;</c> and its three-field
        /// <c>DropdownItem</c> carries <c>key</c>, <c>text</c> and <c>image</c>; the
        /// <c>Add(string, DropdownItem)</c> the legacy calls is
        /// <c>DictionaryValueList&lt;TKey, TValue&gt;.Add</c>. The live in-repo
        /// <c>mods/OrbitalSurvey</c> port builds one the same way in game.</description></item>
        /// <item><description><c>ModuleProperty&lt;T&gt;</c> implements <c>IModuleDataContext</c>
        /// (interface-implementation table, <c>ModuleProperty`1 implements
        /// KSP.Sim.Definitions.IModuleDataContext</c>), which is what makes the property overload of
        /// <c>SetVisible</c> the one that binds rather than a compile error.</description></item>
        /// </list>
        /// <para>
        /// <b>Two visibility rules, and they are the legacy's exactly.</b> A part whose kind is not
        /// omni has no use for the omni toggle, and a mono-band part has no second band to pick. Note
        /// what is <i>not</i> done here: nothing hides <c>Band</c>/<c>SecondaryBand</c> for an omni
        /// part, because that pair depends on the toggle's value and belongs to
        /// <c>Module_NextModulator</c>, which can see it change. Splitting the definition-driven rows
        /// from the state-driven ones is what keeps the two from fighting when a part is reloaded.
        /// </para>
        /// <para>
        /// <b>The band labels are the legacy's own text, coloured through <c>RTEColor</c></b> -
        /// <c>"X Band"</c> and friends, not a localization key, which is what the legacy passed and
        /// what its CSV never carried a row for. The "none" entry is the exception: it is a real
        /// localized string, and the port looks it up rather than passing the key the legacy passed
        /// (see the divergence note). The empty code is not decoration - it is the same
        /// "no second band" sentinel the gate already resolves to band index -1.
        /// </para>
        /// <para>
        /// <b>THE L20 GUARD (D-L20-1), and why this method is wrapped in a try/catch.</b> The first
        /// run that reached this body (L20, the pin's <c>0x0206</c> build) failed the whole part
        /// load here: <c>SetDropdownData</c> resolves its property through
        /// <c>_propertyContextLookup</c> by <c>IModuleProperty.ContextKey</c> and a <b>null key
        /// throws <c>ArgumentNullException: key</c></b>, which escapes
        /// <c>ObjectAssemblyPart.FinalizeModules</c> - so the probe core never finished loading and
        /// the VAB reported a failure. The key is null because this instance never had a completed
        /// <c>PrepareDataContext</c> pass, and no rule of the game's registration path guarantees
        /// one (see <c>ModuleDataContextGuard</c> and <c>Deploy/obj/u06b-fixes.md</c>).
        /// </para>
        /// <para>
        /// The repair is the guarded <c>EnsureBandContextPrepared</c> call above - a real fix, not a
        /// mask. The <c>try</c>/<c>catch</c> is the belt: a future mistake in these two rows, or in
        /// anything the PAM path grows, becomes one <c>band-rows:</c> line and a pair of unconfigured
        /// dropdowns instead of a part the player cannot place. Nothing in this method is
        /// correctness-critical for the part itself - the band gate reads the stored values, which
        /// are untouched here.
        /// </para>
        /// </remarks>
        public override void OnPartBehaviourModuleInit()
        {
            base.OnPartBehaviourModuleInit();

            // THE L20 GUARD (D-L20-1). SetDropdownData resolves its property through
            // _propertyContextLookup and THROWS ArgumentNullException: key when the property's
            // ContextKey is null, and the exception escapes ObjectAssemblyPart.FinalizeModules - the
            // part never loads and the VAB reports a failure. This instance is the one the module
            // will be used through, so this is the last place the context can be repaired before the
            // first throwing call. Module_NextModulator.AddDataModules normally repairs it earlier;
            // this call is the belt for the paths that do not go through that method.
            if (!EnsureBandContextPrepared(
                    nameof(OnPartBehaviourModuleInit),
                    "origin=" + nameof(Data_NextModulator) + "." + nameof(OnPartBehaviourModuleInit)))
            {
                return;
            }

            // The second half of the guard: nothing below may abort a part load. A failure here is
            // two rows that do not work, not a part that cannot be placed - and the whole point of
            // L19-L21 is that the player gets a placeable probe core with a band dropdown.
            try
            {
                DropdownItemList bandOptions = new DropdownItemList();
                DropdownItemList secondaryBandOptions = new DropdownItemList();

                secondaryBandOptions.Add(string.Empty, new DropdownItem
                {
                    key = string.Empty,
                    text = LocalizationManager.GetTranslation(LocalizedStrings.NoneBand)
                });

                for (int i = 0; i < NetworkBands.All.Length; i++)
                {
                    NetworkBand band = NetworkBands.All[i];
                    string label = band.DisplayName.RTEColor(band.Color);

                    bandOptions.Add(band.Code, new DropdownItem { key = band.Code, text = label });
                    secondaryBandOptions.Add(band.Code, new DropdownItem { key = band.Code, text = label });
                }

                SetDropdownData(Band, bandOptions);
                SetDropdownData(SecondaryBand, secondaryBandOptions);

                if (ModulatorKind != Kind.OmniBand)
                {
                    SetVisible(OmniBand, false);
                }

                if (ModulatorKind == Kind.MonoBand)
                {
                    SetVisible(SecondaryBand, false);
                }
            }
            catch (Exception exception)
            {
                Logger.LogError("band-rows: suppressed an exception while arming the band dropdowns, "
                    + "so the part load could not be aborted by it. The part loads; its band rows "
                    + "stay unconfigured. " + exception);
            }
        }

        /// <summary>
        /// Adds the "Modulation kind: &lt;kind&gt;" row to the part-info window.
        /// </summary>
        /// <remarks>
        /// The legacy added this row for its own module type only, and so does this override -
        /// the guard is what stops a shared delegate list being appended to by every module in
        /// the part. The value is resolved through <c>I2.Loc</c> at render time rather than
        /// captured, so it follows a language change.
        /// </remarks>
        public override List<OABPartData.PartInfoModuleEntry> GetPartInfoEntries(
            Type partBehaviourModuleType,
            List<OABPartData.PartInfoModuleEntry> delegateList)
        {
            if (partBehaviourModuleType != ModuleType) return delegateList;

            delegateList.Add(new OABPartData.PartInfoModuleEntry(
                LocalizationManager.GetTranslation(LocalizedStrings.ModulatorKind),
                s => LocalizationManager.GetTranslation(KindLocalizationKey)));

            return delegateList;
        }

        /// <summary>
        /// Copies the band selection from a symmetry counterpart, as the legacy did.
        /// </summary>
        /// <param name="sourceModuleData">
        /// The data to copy from. Not type-checked by the caller, so a mismatched value is ignored
        /// rather than thrown - the legacy's own guard, kept because a throwing `Copy` would take
        /// the part's initialisation down with it.
        /// </param>
        /// <remarks>
        /// Only the three band properties are copied. `ModulatorKind` is a `[KSPDefinition]` - a
        /// property of the part's definition rather than of this placement - so copying it would
        /// be wrong, and the legacy did not.
        /// </remarks>
        public override void Copy(ModuleData sourceModuleData)
        {
            Data_NextModulator dataModulator = (Data_NextModulator)sourceModuleData;
            if (dataModulator == null) return;

            OmniBand.SetValue(dataModulator.OmniBand.GetValue());
            Band.SetValue(dataModulator.Band.GetValue());
            SecondaryBand.SetValue(dataModulator.SecondaryBand.GetValue());
        }

        /// <summary>The localization key for the part's modulation kind.</summary>
        /// <remarks>
        /// The default arm keeps the legacy's literal <c>"N/A"</c>: an unrecognised value is a
        /// bug, and a translated "N/A" would hide which row was wrong.
        /// </remarks>
        private string KindLocalizationKey
        {
            get
            {
                switch (ModulatorKind)
                {
                    case Kind.MonoBand:
                        return LocalizedStrings.ModulatorKindMonoBand;
                    case Kind.DualBand:
                        return LocalizedStrings.ModulatorKindDualBand;
                    case Kind.OmniBand:
                        return LocalizedStrings.ModulatorKindOmniBand;
                    default:
                        return "N/A";
                }
            }
        }
    }
}
