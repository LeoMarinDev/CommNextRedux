// CommNextRedux - the localization keys the module code and the Lua patches resolve.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/LocalizedStrings.cs
//
//   The legacy class held two kinds of member: I2 `LocalizedString` statics (used as
//   translatable values) and a handful of `const string` keys (used where a compile-time
//   constant is required, e.g. attribute arguments). That split is a legacy accident, not a
//   design: a `const string` works in BOTH places, because `I2.Loc.LocalizedString` has an
//   implicit conversion to `string` (measured: `Assembly-CSharp.dll`, mlist 12145,
//   `default string op_Implicit (valuetype I2.Loc.LocalizedString s)`).
//
//   So this port keeps only the `const string` form and treats every key as a key. Where a
//   translated value is actually needed the call site asks for it explicitly with
//   `I2.Loc.LocalizationManager.GetTranslation(...)` (measured: `Assembly-CSharp.dll`,
//   mlist 11886/11887 - the two `GetTranslation` overloads).
//
// SCOPE - PHASES 4, 5, 8a AND 8b
//   Only the keys the port's types and Lua patches actually reference. Phase 5 added the three
//   band-property labels (`OmniBandKey`, `BandKey`, `SecondaryBandKey`) that the modulator's
//   `[LocalizedField]` attributes name. Phase 8a added the eight the map toolbar uses - the three
//   connection modes, the three ruler modes, and the two buttons' own tooltips. Phase 8b added the
//   sixteen the vessel report renders: the two row labels (`RangeLabelKey`, `DistanceLabelKey`), the
//   power pair (`NoPower`, `TooltipNoPowerCurrentVessel`), the two dropdown labels (`FilterLabel`,
//   `SortLabel`), the three filter choices and four sort choices, the two direction tags, and the
//   control source's own name (`KSCCommNet`).
//
//   Three of the legacy's keys were deliberately NOT carried over, and each one has an argument in
//   `Deploy/obj/divergences.md`: `Occluded`/`OccludedBy` (the report has no occlusion column - this
//   engine publishes occlusion as per-body COUNTS, never per link, D44/D47), and
//   `ActionRequiresMapView` (the report closes with the map, so there is no reachable state that
//   could show it, D50). Declaring a key nothing can render is dead code, and a CSV row with no
//   reader is worse - it looks translated.
//
// THE CSV IS THE OTHER HALF
//   `Copied/localizations/commnext_localizations.csv` ships the translated rows for every key
//   named here. A key with no row renders as the key itself, so the two files must stay in
//   lockstep - see `Deploy/obj/divergences.md` for the header-order trap this port fixes.

namespace CommNextRedux.UI
{
    /// <summary>
    /// Localization keys for the CommNext module surface, as compile-time constants.
    /// </summary>
    public static class LocalizedStrings
    {
        /// <summary>The relay module's boolean "is this relay enabled" property label.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.EnableRelayKey</c>, same key, unchanged.</remarks>
        public const string EnableRelayKey = "CommNext/UI/EnableRelay";

        /// <summary>One-line description of what a relay does, shown as a part-info entry.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.RelayDescription</c>, same key, unchanged.</remarks>
        public const string RelayDescription = "PartModules/NextRelay/RelayDescription";

        /// <summary>The "Modulation kind" row label for the modulator's part-info entry.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.ModulatorKind</c>. The name collides with the
        /// <see cref="CommNextRedux.Modules.Modulator.ModulatorKind"/> enum, which is exactly
        /// how the legacy read too - the class-qualified form is unambiguous.
        /// </remarks>
        public const string ModulatorKind = "PartModules/NextModulator/Kind";

        /// <summary>The "Mono-band" value for the modulation-kind row.</summary>
        public const string ModulatorKindMonoBand = "PartModules/NextModulator/KindMonoBand";

        /// <summary>The "Dual-band" value for the modulation-kind row.</summary>
        public const string ModulatorKindDualBand = "PartModules/NextModulator/KindDualBand";

        /// <summary>The "Omni-band" value for the modulation-kind row.</summary>
        public const string ModulatorKindOmniBand = "PartModules/NextModulator/KindOmniBand";

        /// <summary>The label for the modulator's <c>OmniBand</c> property.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.OmniBandKey</c>, same key, unchanged.</remarks>
        public const string OmniBandKey = "CommNext/UI/OmniBand";

        /// <summary>The label for the modulator's <c>Band</c> property.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.BandKey</c>, same key, unchanged - and the key still lives
        /// under <c>PartModules/NextRelay/</c> in the CSV, because renaming it would orphan the
        /// translations the legacy shipped. The legacy's own naming, kept deliberately.
        /// </remarks>
        public const string BandKey = "PartModules/NextRelay/Band";

        /// <summary>The label for the modulator's <c>SecondaryBand</c> property.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.SecondaryBandKey</c>, same key, unchanged, and under the
        /// same `PartModules/NextRelay/` prefix as <see cref="BandKey"/> for the same reason.
        /// </remarks>
        public const string SecondaryBandKey = "PartModules/NextRelay/SecondaryBand";

        /// <summary>
        /// The modulator's second-band dropdown entry for "no second band".
        /// </summary>
        /// <remarks>
        /// <para>
        /// Legacy: <c>LocalizedStrings.NoneBand</c>, same key and same text (<c>None</c>). It is the
        /// text of the <c>""</c> entry <c>Data_NextModulator.OnPartBehaviourModuleInit</c> adds to
        /// the secondary-band dropdown, which is the only way the player can put a dual-band part
        /// back to a single band.
        /// </para>
        /// <para>
        /// <b>One deliberate difference from the legacy.</b> The legacy assigned the
        /// <c>LocalizedString</c> itself to <c>DropdownItem.text</c> - which is a <c>string</c>
        /// field, so it stored the <i>key</i> through I2's implicit conversion - while every other
        /// entry in the same list was a display name. This port passes
        /// <c>LocalizationManager.GetTranslation(...)</c> like the rest of its localization call
        /// sites, so the row shows a translated word. Recorded in
        /// <c>Deploy/obj/divergences.md</c>.
        /// </para>
        /// </remarks>
        public const string NoneBand = "CommNext/UI/NoneBand";

        // ---------------------------------------------------------------------------------------
        // The UI half - added by P8a, the phase that landed the map toolbar.
        //
        // The legacy's UI class carried these as `public static LocalizedString` fields; here they
        // are `const string` like the rest of this file (the legacy split was accidental - see the
        // header). The keys are byte-identical to the legacy's, because the CSV rows ship the
        // translations the legacy collected and a renamed key would orphan them.
        //
        // The mode keys are the toolbar's own tooltip text: `UpdateButtonState` writes the CURRENT
        // mode into a button's tooltip every time the window is shown, so hovering a mode button
        // says what the map is doing right now rather than what the button would do. The two
        // *Tooltip keys are a button's text before the first show - the legacy used a hardcoded
        // English "All active connections" for the lines button, which is the English text of
        // `ConnectionsDisplayModeLines`, so the port uses that key and gets the translation the
        // legacy's own CSV already carries (D40).
        // ---------------------------------------------------------------------------------------

        /// <summary>Tooltip for the lines button when the connections are not being drawn.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.ConnectionsDisplayModeNone</c>, same key.</remarks>
        public const string ConnectionsDisplayModeNone = "CommNext/UI/ConnectionsDisplayModeNone";

        /// <summary>Tooltip for the lines button when every connection is drawn.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.ConnectionsDisplayModeLines</c>, same key - and it is also
        /// the value P8a gives the button before its first state refresh (see the block comment).
        /// </remarks>
        public const string ConnectionsDisplayModeLines = "CommNext/UI/ConnectionsDisplayModeLines";

        /// <summary>Tooltip for the lines button when only the active vessel's path is drawn.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.ConnectionsDisplayModeActive</c>, same key.</remarks>
        public const string ConnectionsDisplayModeActive = "CommNext/UI/ConnectionsDisplayModeActive";

        /// <summary>Tooltip for the rulers button when no ruler is drawn.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.RulersDisplayModeNone</c>, same key.</remarks>
        public const string RulersDisplayModeNone = "CommNext/UI/RulersDisplayModeNone";

        /// <summary>Tooltip for the rulers button when only relays get a ruler.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.RulersDisplayModeRelays</c>, same key.</remarks>
        public const string RulersDisplayModeRelays = "CommNext/UI/RulersDisplayModeRelays";

        /// <summary>Tooltip for the rulers button when every node gets a ruler.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.RulersDisplayModeAll</c>, same key.</remarks>
        public const string RulersDisplayModeAll = "CommNext/UI/RulersDisplayModeAll";

        /// <summary>The rulers button's tooltip before its first state refresh.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.RulersTooltip</c>, same key. The legacy replaced it with the
        /// current mode on the first `UpdateButtonState`, and so does this port.
        /// </remarks>
        public const string RulersTooltip = "CommNext/UI/RulersTooltip";

        /// <summary>The vessel report button's tooltip, which never changes.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.VesselReportTooltip</c>, same key. The button itself is
        /// P8b's; its tooltip and its `toggled` tint are wired here so the toolbar is complete.
        /// </remarks>
        public const string VesselReportTooltip = "CommNext/UI/VesselReportTooltip";

        // ---------------------------------------------------------------------------------------
        // The vessel report - added by P8b, the phase that landed the third window.
        //
        // The same contract as the block above: `const string` keys, byte-identical to the legacy's
        // (the CSV rows ship the translations the legacy collected, and a renamed key would orphan
        // them), resolved at the call site through `Localize.Text`.
        //
        // The two `{0}` keys are I2 parameter rows: `Localize.Text` forwards the arguments to
        // `LocalizationManager.GetTranslation(term, params)`, which substitutes them into the
        // translated text - so a language that orders the number differently gets its own order.
        // ---------------------------------------------------------------------------------------

        /// <summary>The header's range row: <c>Range {0}</c>, the reported vessel's own range.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.RangeLabelKey</c>, same key. Used with the SI-formatted range
        /// as <c>{0}</c>.
        /// </remarks>
        public const string RangeLabelKey = "CommNext/UI/RangeLabel";

        /// <summary>One link's distance row: <c>Distance {0}</c>.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.DistanceLabelKey</c>, same key.</remarks>
        public const string DistanceLabelKey = "CommNext/UI/DistanceLabel";

        /// <summary>The no-power icon's tooltip when the OTHER end is the one without power.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.NoPower</c>, same key. The window header uses it too, where
        /// the reported vessel is the one without power.
        /// </remarks>
        public const string NoPower = "CommNext/UI/NoPower";

        /// <summary>The no-power icon's tooltip when THIS vessel is the one without power.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.TooltipNoPowerCurrentVessel</c>, same key.</remarks>
        public const string TooltipNoPowerCurrentVessel = "CommNext/UI/TooltipNoPowerCurrentVessel";

        /// <summary>The filter dropdown's tooltip.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.FilterLabel</c>, same key.</remarks>
        public const string FilterLabel = "CommNext/UI/FilterLabel";

        /// <summary>The sort dropdown's tooltip.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.SortLabel</c>, same key.</remarks>
        public const string SortLabel = "CommNext/UI/SortLabel";

        /// <summary>The filter choice that lists every link the vessel is an endpoint of.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.FilterAll</c>, same key and the legacy's own default. See
        /// <c>ConnectionsQuery</c>'s header for why the legacy's other three filter choices are not
        /// in this port.
        /// </remarks>
        public const string FilterAll = "CommNext/UI/FilterAll";

        /// <summary>The filter choice that lists only the links this vessel is the source of.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.ConnectionOutbound</c> - the key the legacy used for the
        /// connection row's "Out" direction tag, reused here as the filter's own label because its
        /// English text ("Outbound") is exactly what the filter means and its row already carries 12
        /// translations. No new key, no untranslated English.
        /// </remarks>
        public const string ConnectionOutbound = "CommNext/UI/ConnectionOutbound";

        /// <summary>The filter choice that lists only this vessel's own inbound link.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.ConnectionInbound</c>, reused for the same reason as
        /// <see cref="ConnectionOutbound"/>.
        /// </remarks>
        public const string ConnectionInbound = "CommNext/UI/ConnectionInbound";

        /// <summary>The sort choice that orders the links by distance.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.SortByDistance</c>, same key.</remarks>
        public const string SortByDistance = "CommNext/UI/SortByDistance";

        /// <summary>The sort choice that orders the links by the band the gate selected.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.SortByBand</c>, same key.</remarks>
        public const string SortByBand = "CommNext/UI/SortByBand";

        /// <summary>The sort choice that orders the links by signal strength.</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.SortBySignalStrength</c>, same key.</remarks>
        public const string SortBySignalStrength = "CommNext/UI/SortBySignalStrength";

        /// <summary>The sort choice that orders the links by the other end's name.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.SortByName</c>, same key - and the one row in this file whose
        /// translations are NOT a verbatim copy: the legacy's own row is shifted by one cell from its
        /// Korean column onward (its Korean cell holds the Polish word and its tail repeats Chinese).
        /// Every language is placed where it belongs here and the Korean cell is left EMPTY rather
        /// than filled with an invented word; I2 falls back to the English term for an empty cell.
        /// Recorded in <c>Deploy/obj/divergences.md</c>.
        /// </remarks>
        public const string SortByName = "CommNext/UI/SortByName";

        /// <summary>The direction tag on a link this vessel feeds: "In".</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.InDirection</c>, same key.</remarks>
        public const string InDirection = "CommNext/UI/In";

        /// <summary>The direction tag on a link this vessel receives from: "Out".</summary>
        /// <remarks>Legacy: <c>LocalizedStrings.OutDirection</c>, same key.</remarks>
        public const string OutDirection = "CommNext/UI/Out";

        /// <summary>The control source's own name, where a vessel name would otherwise go.</summary>
        /// <remarks>
        /// Legacy: <c>LocalizedStrings.KSCCommNet</c>, same key. The control source is the KSC: it is
        /// not a vessel and has no sim-object name, so its node is named by its role. The key's own
        /// prefix is <c>CommNext/Simulation/</c>, not <c>CommNext/UI/</c> - the legacy's, kept.
        /// </remarks>
        public const string KSCCommNet = "CommNext/Simulation/KSCCommNet";
    }
}
