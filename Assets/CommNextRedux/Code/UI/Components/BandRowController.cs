// CommNextRedux - one band of the vessel report: the band's icon, its name and this vessel's range
// on it.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Components/BandRowController.cs (81 lines), MIT. The
//   element names, the `SetBand` call, the range formatting and the tooltip are the legacy's. Three
//   things differ, and the first one is the phase's central deletion:
//
//     1. THE `activate-toggle` IS GONE FROM THIS ROW (D46), because its only consumer is gone from
//        this port. In the legacy, flipping that toggle wrote a GLOBAL selection:
//
//            ConnectionsRenderer.Instance.SelectedBandIndex = bandIndex;   // the legacy
//            MainUIManager.Instance.MapToolbarWindow.UpdateButtonState();
//
//        and the renderer used it to tint and gate band rulers. P7 measured that this port has no
//        global band selection at all: the band a line is drawn in comes from the PER-EDGE band the
//        gate selected (`NetworkEngine.SelectedBandOf`), which is also what the report's connection
//        rows show. There is nothing for the toggle to select, and "a control that visibly does
//        nothing" is the exact failure this port treats as a stub. So the control is DROPPED, not
//        stubbed: it is hidden at construction with one Info line naming the reason, and the row
//        carries no handler for it. It is deliberately left in the markup rather than deleted from
//        `BandRow.uxml` - the UXML stays byte-identical to the legacy's, and if a later phase ever
//        defines a per-band renderer selection this is the one place that has to change.
//
//        The legacy's static `ActivateToggled` event and its finalizer went with the control. That
//        event existed to keep the toggles radio-button-consistent with each other; with no toggles
//        there is nothing to keep consistent, and a finalizer on a pooled UI element is a race with
//        Unity's own teardown rather than a guarantee.
//
//     2. IT CAN SURVIVE A FAILED TEMPLATE. The legacy dereferenced every query result in its
//        constructor, so a missing page or a missing element threw from inside a pooled row's
//        constructor - on a refresh tick, with the window already on screen. Here a row whose
//        elements did not resolve reports `Usable == false` and the pool leaves it out (see
//        `UIToolkitExtensions.PoolChildren`), after one Error line naming what was missing.
//
//     3. THE TOOLTIP IS ATTACHED ONCE AND RE-TEXTED. A pooled row is bound five times a second, and
//        `AddManipulator` appends - so the legacy's per-bind `AddTooltip` shape would grow the
//        element's manipulator list without bound over a session. One manipulator, its
//        `TooltipText` rewritten per bind.
//
//   ONE THING THAT IS *NOT* DIFFERENT ANYMORE: the range. The legacy read
//   `networkNode.BandRanges[bandIndex]`; this port reads `NetworkEngine.BandRangeOf(index,
//   bandIndex)` - P5's per-node band table, the same array the gate tests a pair against, so the
//   number on screen is the number the gate used.

using CommNextRedux.Network.Bands;
using CommNextRedux.UI.Logic;
using CommNextRedux.UI.Tooltip;
using CommNextRedux.UI.Utils;
using CommNext.Unity.Runtime.Controls;
using KSP;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Components
{
    /// <summary>
    /// The controller for one band row: the band, and this vessel's range on it.
    /// </summary>
    /// <remarks>
    /// Built by the report's band list through <c>PoolChildren</c>, which needs a public
    /// parameterless constructor and reuses the row across refreshes - so <see cref="Bind"/> must
    /// write every value it shows and must not assume it is being called once.
    /// </remarks>
    public sealed class BandRowController : UIToolkitElement, IPoolingElement
    {
        /// <summary>The bundle page this row clones, as the bundle names it.</summary>
        /// <remarks>
        /// Lower-case, and read from `Deploy/obj/bundle-audit.log` rather than guessed: bundle
        /// container names are case-sensitive on this platform.
        /// </remarks>
        public const string Container = "assets/commnextredux/ui/components/bandrow.uxml";

        /// <summary>The colour the report tints its range numbers with, the legacy's own.</summary>
        private const string RangeColor = "#E7CA76";

        private static bool _toggleDroppedLogged;

        private Label _nameLabel;
        private Label _rangeLabel;
        private BandIcon _bandIcon;
        private Toggle _activateToggle;
        private TooltipManipulator _bandTooltip;
        private bool _elementsResolved;

        /// <summary>Whether this row can be bound: its template cloned AND its elements resolved.</summary>
        public override bool Usable
        {
            get { return base.Usable && _elementsResolved; }
        }

        /// <summary>Clones the row's page and resolves the elements it binds.</summary>
        public BandRowController() : base(Container)
        {
            if (!_templateLoaded)
            {
                return;
            }

            _nameLabel = _root.Q<Label>("name-label");
            _rangeLabel = _root.Q<Label>("range-label");
            _bandIcon = _root.Q<BandIcon>("band-icon");
            _activateToggle = _root.Q<Toggle>("activate-toggle");

            if (_nameLabel == null || _rangeLabel == null || _bandIcon == null)
            {
                LogError("BandRowController: '" + Container + "' is missing one of the elements this "
                    + "row binds (name-label=" + Describe(_nameLabel) + ", range-label="
                    + Describe(_rangeLabel) + ", band-icon=" + Describe(_bandIcon) + ") - the band "
                    + "list will be short by one row per band the vessel offers rather than showing "
                    + "half a row");
                return;
            }

            // One manipulator for the row's whole life; `Bind` rewrites its text. See the header.
            _bandTooltip = new TooltipManipulator(string.Empty);
            _bandIcon.AddManipulator(_bandTooltip);

            DropActivateToggle();
            _elementsResolved = true;
        }

        /// <summary>Hides the legacy's band-activation toggle and says why, once.</summary>
        /// <remarks>
        /// See the file header. The toggle is hidden rather than removed so the markup and the
        /// legacy stay comparable, and the line is written once per process rather than once per row
        /// - the row is pooled and re-created as the list grows, and a per-row line would be a
        /// five-per-second flood.
        /// </remarks>
        private void DropActivateToggle()
        {
            if (_activateToggle == null)
            {
                return;
            }

            _activateToggle.style.display = DisplayStyle.None;
            _activateToggle.SetValueWithoutNotify(false);

            if (_toggleDroppedLogged)
            {
                return;
            }

            _toggleDroppedLogged = true;
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin != null)
            {
                plugin.LogLine("ui-report: the band rows carry no activate toggle - the legacy's "
                    + "toggle drove a GLOBAL selected band, and this port has no such concept (a map "
                    + "line's band is per-edge, from NetworkEngine.SelectedBandOf). The control is "
                    + "hidden rather than left doing nothing");
            }
        }

        /// <summary>Writes one band and this vessel's range on it.</summary>
        /// <param name="row">The band and its range, both from the engine's own band table.</param>
        /// <remarks>
        /// A band index outside the table is refused rather than indexed: the window skips a range of
        /// zero or less when it builds the list, and a table change between the two reads must not
        /// turn into an exception on a refresh tick.
        /// </remarks>
        public void Bind(BandRowData row)
        {
            if (!Usable)
            {
                return;
            }

            NetworkBand[] all = NetworkBands.All;
            if (row.BandIndex < 0 || row.BandIndex >= all.Length)
            {
                return;
            }

            NetworkBand band = all[row.BandIndex];
            _nameLabel.text = band.DisplayName;
            _rangeLabel.text = Units.PrintSI(row.RangeMeters, Units.SymbolMeters).RTEColor(RangeColor);
            _bandIcon.SetBand(band.Code, band.Color);

            if (_bandTooltip != null)
            {
                _bandTooltip.TooltipText = band.DisplayName;
            }
        }

        /// <summary>A one-word description of a queried element, for a failure line.</summary>
        /// <param name="element">The element, or <c>null</c>.</param>
        /// <returns><c>ok</c> or <c>MISSING</c>.</returns>
        private static string Describe(VisualElement element)
        {
            return element == null ? "MISSING" : "ok";
        }
    }
}
