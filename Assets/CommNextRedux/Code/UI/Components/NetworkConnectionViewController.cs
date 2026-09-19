// CommNextRedux - one link of the vessel report: the other end, how strong the link is, which band
// it uses, which way round it is, and the two things a player can do with it.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Components/NetworkConnectionViewController.cs (146
//   lines), MIT. The element names, the class toggles, the tooltips, the distance formatting, the
//   signal-strength call, the band icon and the two row actions are the legacy's. Five things
//   differ, and the first two are forced by the data this port's engine has:
//
//     1. THE BAND COMES FROM THE EDGE, NOT FROM THE PAIR'S JOB RECORD (D46's other half). The
//        legacy read `connection.SelectedBand`, which its network job filled in per directed pair.
//        This port's equivalent - and the value the map line is actually coloured with - is
//        `NetworkEngine.SelectedBandOf(target)` for the edge, carried on the row as
//        `ConnectionRow.BandIndex`. `-1` means the gate selected no band for that edge (the master
//        switch off, a vanilla capture, or a source), which is a normal answer: the band icon is
//        hidden and the map line takes the renderer's own fallback colour. The legacy's
//        `HasMatchingBand == false` branch and its `IsBandMissingRange` / `IsBandNotAvailable`
//        warning texts are NOT ported: they came from per-pair band verdicts this engine does not
//        publish, and on the rows this report can show - tree edges, which the gate accepted by
//        definition - both are vacuously false. See `ConnectionsQuery.cs`'s header for the whole
//        argument.
//
//     2. THE OCCLUSION COLUMN IS DROPPED (D47). The legacy appended `Occluded by <body>` to the
//        details label from `connection.OccludingBody`. This engine publishes occlusion as COUNTS
//        (`OccludedPairCount`, `OccludedByBody(bodyIndex)` per body) and never per link, so there is
//        no member to read - and a tree edge passed the occlusion test by construction, which is why
//        it is in the tree. Nothing is stubbed and nothing is guessed: the column is gone, and the
//        details label carries the distance.
//
//     3. THE CONNECTION ICON HAS NO TINT WRITE. The legacy tinted it green for connected and red for
//        not (`connection.IsConnected ? ActiveColor : InactiveColor`). Every row this report can
//        build IS connected (it is a tree edge), so the tint was a constant being rewritten five
//        times a second; the sheet's `.relay__icon` / `.antenna__icon` classes carry the colour
//        instead, and the relay/antenna class toggle - which is real data - is kept.
//
//     4. THE `Control` BUTTON IS HIDDEN ON THE CONTROL SOURCE'S ROW (D47), AND THE ACTION ITSELF NOW
//        GOES THROUGH A PUBLIC ROUTE (F64/D49). The legacy's row called `Map3DFocusItem.ControlVessel()`
//        on the other end's map item - which is PRIVATE on this pin (measured by reflection: the type's
//        declared members include `private Void ControlVessel()`), so it is CS1061 here and could only
//        ever have compiled against a publicized assembly. The row now delegates to
//        `VesselReportWindowController.ControlVesselOnMap`, which uses the game's own public
//        `ViewController.SetActiveVehicle(owner)` behind its `CanObserverLeaveTheActiveVessel()` veto.
//        The button is still hidden on the control source's row: the KSC is not a vessel, and asking
//        the game to make it the active vehicle is a request with no meaning.
//
//     5. IT CAN SURVIVE A FAILED TEMPLATE, and its three tooltips are attached once and re-texted -
//        both for the reasons `BandRowController`'s header gives.
//
// THE DIRECTION TAG IS ALWAYS SHOWN
//   The legacy wrote `_directionTag.style.display = connection.IsActive ? Flex : None`. This port's
//   rows are the vessel's own tree edges, so the direction is always meaningful and the tag is
//   always drawn - the legacy's `IsActive` was precisely "this directed edge is in the tree", which
//   is the only kind of row this report builds.

using System.Globalization;
using CommNext.Unity.Runtime.Controls;
using CommNextRedux.Network.Bands;
using CommNextRedux.UI.Logic;
using CommNextRedux.UI.Tooltip;
using CommNextRedux.UI.Utils;
using KSP;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Components
{
    /// <summary>
    /// The controller for one connection row: the other end, the link's state, and the two actions.
    /// </summary>
    /// <remarks>
    /// Pooled by the report's connection list, so <see cref="Bind"/> rewrites every visible value on
    /// every call. The only state it holds between binds is what the actions need: the row itself and
    /// the window that owns the map actions.
    /// </remarks>
    public sealed class NetworkConnectionViewController : UIToolkitElement, IPoolingElement
    {
        /// <summary>The bundle page this row clones, as the bundle names it.</summary>
        public const string Container =
            "assets/commnextredux/ui/components/networkconnectionview.uxml";

        /// <summary>The colour the report tints its distance with, the legacy's own.</summary>
        private const string DistanceColor = "#E7CA76";

        private static bool _controlSourceLogged;

        private VisualElement _connectionIcon;
        private VisualElement _directionTag;
        private Label _nameLabel;
        private Label _detailsLabel;
        private Label _directionLabel;
        private BandIcon _bandIcon;
        private VisualElement _powerIcon;
        private SignalStrengthIcon _signalStrengthIcon;
        private Button _controlButton;
        private VisualElement _rowContainer;

        private TooltipManipulator _bandTooltip;
        private TooltipManipulator _powerTooltip;
        private TooltipManipulator _signalStrengthTooltip;

        private VesselReportWindowController _owner;
        private ConnectionRow _row;
        private bool _elementsResolved;

        /// <summary>Whether this row can be bound: its template cloned AND its elements resolved.</summary>
        public override bool Usable
        {
            get { return base.Usable && _elementsResolved; }
        }

        /// <summary>Clones the row's page, resolves its elements and wires its three actions.</summary>
        public NetworkConnectionViewController() : base(Container)
        {
            if (!_templateLoaded)
            {
                return;
            }

            _connectionIcon = _root.Q<VisualElement>("connection-icon");
            _directionTag = _root.Q<VisualElement>("direction-tag");
            _nameLabel = _root.Q<Label>("name-label");
            _detailsLabel = _root.Q<Label>("details-label");
            _directionLabel = _root.Q<Label>("direction-label");
            _bandIcon = _root.Q<BandIcon>("band-icon");
            _powerIcon = _root.Q<VisualElement>("power-icon");
            _signalStrengthIcon = _root.Q<SignalStrengthIcon>("signal-strength-icon");
            _controlButton = _root.Q<Button>("control-button");
            _rowContainer = _root.Q<VisualElement>("row__container");

            bool resolved = _connectionIcon != null && _directionTag != null && _nameLabel != null
                && _detailsLabel != null && _directionLabel != null && _bandIcon != null
                && _powerIcon != null && _signalStrengthIcon != null && _controlButton != null
                && _rowContainer != null;

            if (!resolved)
            {
                LogError("NetworkConnectionViewController: '" + Container + "' is missing one of the "
                    + "elements this row binds (connection-icon=" + Describe(_connectionIcon)
                    + ", direction-tag=" + Describe(_directionTag) + ", name-label="
                    + Describe(_nameLabel) + ", details-label=" + Describe(_detailsLabel)
                    + ", direction-label=" + Describe(_directionLabel) + ", band-icon="
                    + Describe(_bandIcon) + ", power-icon=" + Describe(_powerIcon)
                    + ", signal-strength-icon=" + Describe(_signalStrengthIcon) + ", control-button="
                    + Describe(_controlButton) + ", row__container=" + Describe(_rowContainer)
                    + ") - the connection list will be short rather than showing half a row");
                return;
            }

            // One manipulator each, re-texted per bind: `AddManipulator` appends, and a pooled row is
            // bound five times a second (see `BandRowController`'s header).
            _bandTooltip = new TooltipManipulator(string.Empty);
            _bandIcon.AddManipulator(_bandTooltip);
            _powerTooltip = new TooltipManipulator(string.Empty);
            _powerIcon.AddManipulator(_powerTooltip);
            _signalStrengthTooltip = new TooltipManipulator(string.Empty);
            _signalStrengthIcon.AddManipulator(_signalStrengthTooltip);

            // The row itself is the "focus on the map" target, the button the "take control" one -
            // the legacy's own split, and the reason the button also appears on hover only (the
            // sheet's `.row__container .row__button--on-hover` rule).
            _rowContainer.AddManipulator(new Clickable(OnRowClicked));
            _controlButton.clicked += OnControlClicked;

            _elementsResolved = true;
        }

        /// <summary>Writes one link into this row.</summary>
        /// <param name="owner">The report window, which owns the two map actions.</param>
        /// <param name="row">The link, as the engine reports it.</param>
        public void Bind(VesselReportWindowController owner, ConnectionRow row)
        {
            if (!Usable)
            {
                return;
            }

            _owner = owner;
            _row = row;

            // 1. The other end and which way the link runs. The direction tag's two classes are the
            //    sheet's colour-and-rotation opposites, so the class IS the arrow's direction.
            _nameLabel.text = row.OtherName;
            _directionLabel.text = Localize.Text(row.VesselIsSource
                ? LocalizedStrings.OutDirection
                : LocalizedStrings.InDirection);
            _directionTag.style.display = DisplayStyle.Flex;
            _directionTag.ToggleClassesIf(row.VesselIsSource,
                new[] { "direction__tag--outbound" },
                new[] { "direction__tag--inbound" });

            // 2. Relay or plain antenna: real data about the other end, and the sheet's two icons.
            _connectionIcon.ToggleClassesIf(row.OtherIsRelay,
                new[] { "relay__icon" },
                new[] { "antenna__icon" });

            // 3. Power. The icon is shown when either end is starved, and its two classes say whose
            //    fault it is: at full strength when the other end is the one that cannot transmit,
            //    faded when this vessel is.
            _powerIcon.style.display = row.IsPowered ? DisplayStyle.None : DisplayStyle.Flex;
            _powerIcon.ToggleClassesIf(row.VesselHasEnoughResources,
                new[] { "icon--no-power" },
                new[] { "icon--no-power-active" });
            if (_powerTooltip != null)
            {
                _powerTooltip.TooltipText = Localize.Text(row.VesselHasEnoughResources
                    ? LocalizedStrings.NoPower
                    : LocalizedStrings.TooltipNoPowerCurrentVessel);
            }

            // 4. The band, when the gate selected one for this edge. No band is not an error - see
            //    the header - so the icon simply is not there.
            if (row.HasBand && row.BandIndex < NetworkBands.All.Length)
            {
                NetworkBand band = NetworkBands.All[row.BandIndex];
                _bandIcon.style.display = DisplayStyle.Flex;
                _bandIcon.SetBand(band.Code, band.Color);
                if (_bandTooltip != null)
                {
                    _bandTooltip.TooltipText = band.DisplayName;
                }
            }
            else
            {
                _bandIcon.style.display = DisplayStyle.None;
            }

            // 5. Signal strength: the game's own curve over the two ends' ranges, and the same number
            //    the map's line length represents.
            _signalStrengthIcon.SetStrengthPercentage(row.SignalStrength);
            if (_signalStrengthTooltip != null)
            {
                // Invariant, deliberately: a tooltip number is read, not parsed, and the player's
                // culture would otherwise decide whether "42%" gains a space between sessions.
                _signalStrengthTooltip.TooltipText =
                    row.SignalStrength.ToString("P0", CultureInfo.InvariantCulture);
            }

            // 6. The details line: the distance, in the game's own SI formatting.
            _detailsLabel.text = Localize.Text(LocalizedStrings.DistanceLabelKey,
                "<color=" + DistanceColor + ">"
                + Units.PrintSI(row.DistanceMeters, Units.SymbolMeters) + "</color>");

            // 7. The control button, hidden on the control source's own row (see the header).
            if (row.OtherIsControlSource)
            {
                _controlButton.style.display = DisplayStyle.None;
                if (!_controlSourceLogged)
                {
                    _controlSourceLogged = true;
                    CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
                    if (plugin != null)
                    {
                        plugin.LogLine("ui-report: the control source's row has no Control button - "
                            + "the KSC is not a vessel, so there is nothing to take control of; "
                            + "every other row reaches ViewController.SetActiveVehicle through "
                            + "ControlVesselOnMap (F64/D49)");
                    }
                }
            }
            else
            {
                // Hand the decision back to the sheet. `.row__container .row__button--on-hover`
                // keeps this button hidden and `.row__container:hover .row__button--on-hover`
                // shows it on hover - and an INLINE declaration beats both, so writing
                // `DisplayStyle.Flex` here would leave every row carrying a permanently visible
                // Control button (the donor never writes this property at all - D52). The
                // `StyleKeyword.Null` form clears the inline value instead of setting one; the
                // library does the same thing at UitkForKsp2's own `AppShell.cs:263`.
                _controlButton.style.display = StyleKeyword.Null;
            }
        }

        /// <summary>Focuses the other end's map item.</summary>
        private void OnRowClicked()
        {
            if (_owner != null)
            {
                _owner.FocusOnMap(_row.OtherOwner, _row.OtherIsControlSource);
            }
        }

        /// <summary>Takes control of the other end, when it is a vessel.</summary>
        private void OnControlClicked()
        {
            if (_owner != null && !_row.OtherIsControlSource)
            {
                _owner.ControlVesselOnMap(_row.OtherOwner, false);
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
