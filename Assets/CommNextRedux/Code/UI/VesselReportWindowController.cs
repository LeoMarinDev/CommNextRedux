// CommNextRedux - the vessel report: every CommNet link the active vessel is an endpoint of, one
// row each, with the vessel's band table underneath.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/VesselReportWindowController.cs (221 lines), MIT. The
//   element names, the query wiring, the two lists, the refresh cadence, the focus button, the
//   close button and the alignment-to-the-toolbar idea are the legacy's. What differs, and why:
//
//     1. THE DATA IS THIS PORT'S ENGINE, NOT THE LEGACY'S NETWORK MANAGER (D46). The legacy asked
//        `NetworkManager.Instance.Nodes` for a `NetworkNode`, then asked it for `BandRanges` and its
//        own `NetworkConnection` objects. None of that exists here: the engine publishes a flat node
//        array (`Snapshot(index)`), a spanning tree (`PredecessorOf`, `SelectedBandOf`) and a
//        per-node band table (`BandRangeOf`). So this window builds its rows from the tree:
//
//            outbound: every node whose PredecessorOf(c) == vesselIndex
//            inbound:  the vessel's own PredecessorOf(vesselIndex), when it has one
//
//        which is the legacy's `IsActive` edge set - a tree edge, by definition, in both ports. The
//        band for a row is `SelectedBandOf(the edge's TARGET)`, the per-edge band the relay/band gate
//        accepted that pair on (the engine documents it as the legacy's `SelectedBand` equivalent),
//        and the distance is `sqrt(CostOf(target))` - the game's own `ConnectionGraph` edge metric,
//        not a second measurement of the same two positions.
//
//     2. THERE IS NO `SelectedBandIndex`, AND THE LEGACY'S PER-VESSEL REPORT SEAM IS GONE WITH IT.
//        The legacy's `Vessel` property wrote
//        `ConnectionsRenderer.Instance.ReportVessel = _vessel` on every change, so the renderer could
//        tint its lines from a globally selected band. This port's renderer has no such input and the
//        engine has no such concept (P7's carried note #3); the band a line is drawn in is per-edge.
//        The report therefore has NO setter at all: it reports on the ACTIVE vessel, re-resolved on
//        every refresh through `ViewController.TryGetActiveSimVessel` - the same route the renderer's
//        active mode and the probe already use. That also makes the window survive a save load with
//        nothing to invalidate: it holds no `VesselComponent` between refreshes, only ids read fresh
//        (rule 6 - key by the identity string captured at registration, never by object reference).
//
//     3. THE THREE COLUMNS THIS ENGINE CANNOT FILL ARE DROPPED, NOT STUBBED (D44, D47):
//        - the filter dropdown lists the three questions the engine can answer per LINK
//          (All / Outbound / Inbound); the legacy's `InRange` and `Connected` filters are gone
//          because "in range" and "in line of sight" exist here only as PASS COUNTS;
//        - the occlusion suffix (`Occluded by <body>`) is gone for the same reason;
//        - the band rows carry no activate toggle, because the global band selection it wrote to
//          does not exist in this port.
//        Each is argued in its own file's header (`ConnectionsQuery.cs`, `BandRowController.cs`).
//
//     4. THE WINDOW'S MOVEMENT AND RESIZE ARE THE GAME'S (D48). `MoveOptions` carries
//        `IsMovingEnabled` and `CheckScreenBounds`; `ResizeOptions` carries `IsResizingEnabled`,
//        `CheckScreenBounds` and a minimum size - both pinned-set members, and both documented in the
//        pinned XML. The legacy's own drag manipulator is NOT ported (it read and wrote
//        `transform.position`, which is CSS `translate` and would silently offset the window by its
//        drag history), and neither is the legacy's `StopMouseEventsPropagation()` call. P8b answered
//        that with `WindowOptions.BlockGameInput`, and **P9.4 turned it back off** (D62, F84): the
//        option holds all eight of KSP2's input locks for as long as the pointer merely HOVERS this
//        window, which kills the map camera and WASD - and the one case it was added for (a wheel
//        over the list must not zoom the map) is now answered by the P9.3 layer fix. **P9.5 (D65)
//        answers the suppression itself, in `UI.Utils.PanelInputBlocker`:** while the pointer rests on
//        this window's rectangle, the map view's five mouse camera actions are held down at ACTION
//        granularity through the same public `InputManager.SetInputLock`, so rotation, the
//        mouse-driven pan and the wheel zoom are suppressed while WASD, ESC, quicksave/load and the
//        time-warp keys keep working. The option's own comment below carries all four measurements.
//
//     5. IT IS A MAP WINDOW, AND IT SAYS SO. The legacy's every action began with
//        `MapViewHelper.IsInMapViewOrNotify()`. Here the window is CLOSED when the map goes away
//        (`CommNextUIManager.SyncMapView`), so the impossible case cannot be reached by leaving the
//        map - and the two map actions still re-check `EventListener.IsInMapView` before touching a
//        map item, so a torn-down session cannot be reached through a stale window either. The
//        legacy's notification text ("Action is enabled only in Map View") was therefore not ported
//        and got no CSV row: there is no reachable state that could show it (D50).
//
//     6. THE TWO MAP ACTIONS GO THROUGH PUBLIC ROUTES, BECAUSE THE LEGACY'S ARE PRIVATE ON THIS PIN
//        (F64/D49). The legacy focused a marker with `Map3DFocusItem.FocusSimObject()` and took
//        control of a vessel with `Map3DFocusItem.ControlVessel()`. Both members exist in the
//        installed runtime and BOTH ARE PRIVATE - measured by reflection over
//        `$KSP2_ROOT/KSP2_x64_Data/Managed/Assembly-CSharp.dll`, which reports
//        `private Void FocusSimObject()` / `private Void ControlVessel()` among the type's 33 declared
//        members (all five public ones that matter are named in `RequestFocus`). The legacy could
//        call them because it was compiled against a PUBLICIZED copy of the game assembly; this port
//        compiles against the shipped one, so those calls are CS1061 here and must not return. The
//        replacements are the game's own public routes, not a workaround:
//
//          focus   -> publish `MapRequestFocusMessage { MapItem = item.AssociatedMapItem }`, the
//                     documented "requests the map view to focus on a specific ⟨MapItem⟩" message,
//                     for any marker (vessel or KSC). `MapFocusChangedMessage` is observed back, so
//                     the request's fate is a grep and not a guess.
//          control -> `ViewController.SetActiveVehicle(owner)`, behind the game's own
//                     `CanObserverLeaveTheActiveVessel()` veto.
//
//        Neither is a stub: both call the game's own machinery for the same job, and the log names
//        which route ran.
//
//     6. THE BAND ROW'S RANGE IS THE ENGINE'S OWN. The legacy read `networkNode.BandRanges[bandIndex]`
//        (P5's table, its own); this port reads `NetworkEngine.BandRangeOf(index, bandIndex)`, the
//        same array the band gate tests a pair against - so the number on screen is the number the
//        gate used. The header's range is `Snapshot(index).MaxRange`, deliberately the SAME value the
//        signal column divides by, rather than a second read of
//        `TelemetryComponent.CommNetRangeMeters` that could disagree with the rows below it.
//
// WHAT THIS FILE DOES NOT DO
//   It does not create the window (`CommNextUIManager` owns that, and the creation ORDER is the
//   window layer's z-order - the report is created between the toolbar and the tooltip, which stays
//   last so it still draws on top). It does not own the stylesheet (`EnsureStylesAttached`), and it
//   does not decide when it is shown: the toolbar's third button toggles it, and the map messages
//   close it.

using System;
using System.Collections.Generic;
using System.Globalization;
using CommNext.Unity.Runtime.Controls; // SortDirectionButton, the report's own sort-order control
using CommNextRedux.Network;
using CommNextRedux.Network.Bands;
using CommNextRedux.UI.Components;
using CommNextRedux.UI.Logic;
using CommNextRedux.UI.Screen;
using CommNextRedux.UI.Tooltip;
using CommNextRedux.UI.Utils;
using KSP;
using KSP.Game;
using KSP.Map;
using KSP.Messages;
using KSP.Sim.impl;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI
{
    /// <summary>
    /// The vessel report window: the active vessel's links, its band table, and the map actions.
    /// </summary>
    /// <remarks>
    /// A <c>MonoBehaviour</c> on the window's own GameObject, like the toolbar and the tooltip, so
    /// the window layer stays one object graph owned by <see cref="CommNextUIManager"/>. Its
    /// <c>Update</c> is its own: the refresh is this window's business, and it runs only while the
    /// window is actually on screen.
    /// </remarks>
    public sealed class VesselReportWindowController : MonoBehaviour
    {
        /// <summary>The window UitkForKsp2 is asked for. One instance, for the process.</summary>
        /// <remarks>
        /// The id is the port's own (the legacy's was <c>CommNext_VesselReportWindow</c>); a window id
        /// must be unique across the install, and this mod's ids are all prefixed with its own name.
        /// </remarks>
        public static WindowOptions WindowOptions = new WindowOptions
        {
            WindowId = "CommNextRedux_VesselReportWindow",
            Parent = null,
            IsHidingEnabled = true,
            DisableGameInputForTextFields = true,

            // P8b enabled this for ONE case: a wheel over the connection list had to scroll the list
            // rather than zoom the map, and the legacy had bought that by disabling the game's camera
            // zoom action. **P9.4 turned it off (D62, F84). P9.5 keeps it off and supersedes D62 (D65),
            // because the player's L17 verdict was that the behaviour was RIGHT and the option was the
            // wrong instrument for it: the dead zone over this window was the window-scoped
            // suppression doing its job, but the suppression was definition-wide and lethal to WASD,
            // ESC and the time-warp keys.**
            //
            // What the option actually does (IL, on the shipped `UitkForKsp2.dll`): attach
            // `UitkForKsp2.API.Manipulator.GameInputBlockManipulator` to this window's root, whose
            // `CheckPointerState()` acquires the lock on `_isPointerOver || _isPointerDown` - i.e. on
            // HOVER, not on interaction - and whose `AcquireLock()` calls
            // `Extensions.SetGameInputDisabled(this, true)`, which calls
            // `ReduxInputManager.SetUitkInputLocks()` - `Assembly-CSharp`, one `SetInputLock(lock,
            // false)` call per lock, and the eight it pushes are `GlobalInputDisabled`,
            // `FlightInputDisabled`, `EVAInputDisabled`, `OABInputDisabled`, `MapViewInputDisabled`,
            // `RDInputDisabled`, `KSCInputDisabled` and `AudioInputDisabled`. Every one of those
            // `InputManager.SetInputLock` calls disables a whole definition, so holding
            // `MapViewInputDisabled` + `GlobalInputDisabled` for as long as the pointer sits anywhere
            // over this window is what killed map-camera rotation AND WASD in the player's report (F84)
            // - and `GlobalInputDisabled` is what would take ESC, quicksave/load and the time-warp
            // keys with it, which the player's D65 spec forbids outright.
            //
            // The suppression the player wants lives in `UI.Utils.PanelInputBlocker` instead: it drives
            // the same public `InputManager.SetInputLock`, but its `InputLockDefinition.InputLock`
            // entries carry an `InputID`, so the lock lands on the FIVE named actions of
            // `MapViewInputDefinition` - `mousePosition`, `mouseSecondary`, `mouseTertiary`,
            // `cameraRotate`, `cameraZoom` - and on no other action of any definition. `cameraMoveXY`
            // (WASD) is deliberately left enabled, and nothing outside that definition is touched at
            // all. The region is pointer-scoped: the library's lock also holds while a button is DOWN,
            // which is what stretched L16's dead zone past the window, and that latch is omitted here.
            //
            // The wheel case this option was added for is covered twice over: P9.3's layer fix (D60)
            // puts this panel's `selectableGameObject` on layer 5, which the map's own zoom paths test
            // through `KSP.UI.UIRaycaster.IsAnyUIHovered` (`UniverseCameraManager.<Start>d__79::
            // OnCameraZoom`, `KSP.Input.MapViewInputDefinition::OnCameraZoom`,
            // `KSP.Map.MapCameraInputHandler::ShouldBlockCameraZoom`), and `cameraZoom` is one of the
            // five actions the guard holds down while the pointer is over this window anyway.
            //
            // The release path is a second reason not to keep the option (measured, not inferred):
            // `ReduxInputManager.RestoreUitkInputLocks()` re-enables the `InputDefinition`s from the
            // snapshot `SetUitkInputLocks` took, and never pops the eight `SetInputLock` entries it
            // pushed. Removing this mod's only caller leaves that asymmetry unexercised, which is the
            // robust choice; nothing in this port pushes those locks.
            BlockGameInput = false,

            MoveOptions = new MoveOptions
            {
                IsMovingEnabled = true,
                CheckScreenBounds = true
            },

            // The legacy had no resize options of its own - the sheet's grip element was the only
            // hint that the window could be resized. This pin HAS the member, and the window system
            // owns the grip, so the window is resizable by its lower-right corner with a floor that
            // keeps the two lists usable.
            ResizeOptions = new ResizeOptions
            {
                IsResizingEnabled = true,
                CheckScreenBounds = true,
                MinWidth = 360f,
                MinHeight = 220f
            }
        };

        /// <summary>
        /// How often the open window re-reads the engine. The legacy's 0.2 s, unchanged.
        /// </summary>
        /// <remarks>
        /// The engine's own pass runs on the game's three-second timer, so this is already finer than
        /// the data it reads; it exists so a row's power state and a vessel's name follow a change
        /// without the window looking frozen.
        /// </remarks>
        private const float RefreshSeconds = 0.2f;

        private PanelRenderer _renderer;
        private VisualElement _root;
        private VisualElement _connectionsTarget;
        private Label _nameLabel;
        private Label _rangeLabel;
        private VisualElement _powerIcon;
        private TooltipManipulator _powerTooltip;
        private ScrollView _connectionsList;
        private VisualElement _bandsList;
        private Button _focusButton;
        private DropdownField _filterDropdown;
        private TooltipManipulator _filterTooltip;
        private DropdownField _sortDropdown;
        private TooltipManipulator _sortTooltip;
        private SortDirectionButton _sortDirectionButton;

        private readonly ConnectionsQuery _query = new ConnectionsQuery();

        /// <summary>Every link the vessel is an endpoint of, before the filter. Reused, not rebuilt.</summary>
        private readonly List<ConnectionRow> _links = new List<ConnectionRow>();

        /// <summary>The rows the filter let through, in the sort's order. What the list shows.</summary>
        private readonly List<ConnectionRow> _shown = new List<ConnectionRow>();

        /// <summary>The vessel's bands with a positive range, in band order.</summary>
        private readonly List<BandRowData> _bandRows = new List<BandRowData>();

        private Action<string> _log;
        private Action<string> _warn;
        private Action<string> _error;

        private bool _bound;
        private bool _isWindowOpen;
        private bool _styleProofRun;
        private bool _refreshFailureLogged;
        private float _refreshTimer;

        /// <summary>
        /// Whether the one-shot name-elision probe has fired. U6g.
        /// </summary>
        /// <remarks>
        /// Set on the first refresh whose vessel name is WIDER than the name label's laid-out box,
        /// which is exactly the condition the U6g fix exists for. Bounded on purpose: the window
        /// refreshes five times a second while it is open, so a per-refresh line would bury the log,
        /// and a probe that fires only on the long-name case is positive evidence without the noise.
        /// </remarks>
        private bool _nameLayoutProbeLogged;

        /// <summary>
        /// Whether the one-shot connection-row overflow probe has fired. U6i.
        /// </summary>
        /// <remarks>
        /// The row's counterpart of <see cref="_nameLayoutProbeLogged"/>, with the same bound: it
        /// fires the first time a connection row's name is wider than the label holding it and never
        /// again, so a session whose vessels all have short names writes nothing and the line can
        /// never become per-refresh noise. Which it is: once per session, not once per name.
        /// </remarks>
        private bool _rowOverflowProbeLogged;

        /// <summary>
        /// Whether the layout audit has run for this size (U6h), and the size it last ran at.
        /// </summary>
        /// <remarks>
        /// The audit is the acceptance gate for the four minimum-size layout defects: it is the
        /// instrument that turns "the window looks right" into a number the log carries. One line per
        /// settled size - a drag-resize changes the geometry on every frame, so the audit waits for
        /// <see cref="LayoutAuditSettleFrames"/> frames without a change before it reads anything, and
        /// then remembers the size so a re-layout at the same size (a font load, a scrollbar, a
        /// pooled-row rebuild) cannot fire it again. <c>(-1, -1)</c> is "never audited", the same
        /// sentinel shape the counts above use.
        /// </remarks>
        private Vector2 _layoutAuditSize = new Vector2(-1f, -1f);

        /// <summary>Armed by a geometry change; the frame countdown until the size is considered settled.</summary>
        private bool _layoutAuditPending;
        private int _layoutAuditFrames;
        private Vector2 _layoutAuditPendingSize;

        /// <summary>
        /// How many frames without a geometry change count as "the size has settled".
        /// </summary>
        /// <remarks>
        /// Frames, not seconds: <c>Time.deltaTime</c> is time-scaled, so a player who pauses the game
        /// and then resizes the window would leave a seconds-based audit waiting forever. An
        /// <c>Update()</c> tick runs every frame regardless of the time scale, so 15 frames settle
        /// the audit in ~0.25 s at 60 fps and in the same wall time at any frame rate the panel runs.
        /// </remarks>
        private const int LayoutAuditSettleFrames = 15;

        /// <summary>The message center this window's focus observer is subscribed to.</summary>
        /// <remarks>
        /// Held so the observer can be re-armed when the game replaces the center (a save load does
        /// that, and the old center's subscriptions die with it - the same fact the plugin's own
        /// <c>EventListener</c> documents). Compared by reference, never by value.
        /// </remarks>
        private MessageCenter _observedCenter;

        /// <summary>The last counts written to the log, so the counts line is one per real change.</summary>
        private int _loggedLinks = -1;
        private int _loggedShown = -1;
        private int _loggedBands = -1;
        private string _loggedVesselName;

        /// <summary>The toolbar's position history, for the one-shot default position.</summary>
        private bool _positionLogged;

        /// <summary>The report's own root element, or <c>null</c> before a successful bind.</summary>
        public VisualElement Root
        {
            get { return _root; }
        }

        /// <summary>The window's position, in the reference-resolution units the library uses.</summary>
        /// <remarks>
        /// Reads and writes <c>left</c>/<c>top</c>, never <c>transform.position</c>, for the reason
        /// the toolbar's own <c>Position</c> gives (AGENTS.md 9). Nothing in P8b writes it; it exists
        /// so a later phase's persistence has the same seam on every window.
        /// </remarks>
        public Vector2 Position
        {
            get
            {
                if (_root == null)
                {
                    return Vector2.zero;
                }

                return new Vector2(_root.resolvedStyle.left, _root.resolvedStyle.top);
            }
            set
            {
                if (_root == null)
                {
                    return;
                }

                _root.style.left = value.x;
                _root.style.top = value.y;
            }
        }

        /// <summary>Whether the report is on screen.</summary>
        /// <remarks>
        /// <para>
        /// <b>The getter reads two facts, not one.</b> This mod's flag says what the player asked for;
        /// the root's resolved <c>display</c> says what the window layer is actually doing. They
        /// diverge for exactly one reason - the library's own hide key (F2) writes the same
        /// <c>display</c> property this class writes - and a toolbar button tinted "open" for a hidden
        /// window is exactly the silent disagreement D39 exists to forbid. So the answer is the
        /// conjunction.
        /// </para>
        /// <para>
        /// <b>The setter's no-op branch is not quite a no-op.</b> Clicking the button while the player
        /// has hidden the UI must show the window again, and <c>_isWindowOpen</c> is already true in
        /// that case - so a <c>true</c> write with the display at <c>None</c> re-shows it and says so
        /// once. Measured against the pinned documentation: <c>Extensions.Hide</c>/<c>Show</c> are
        /// documented as setting the display style, which is the same property F2's hiding uses.
        /// </para>
        /// </remarks>
        public bool IsWindowOpen
        {
            get
            {
                if (!_isWindowOpen || _root == null)
                {
                    return false;
                }

                return _root.resolvedStyle.display != DisplayStyle.None;
            }
            set
            {
                if (_root == null)
                {
                    Write(_warn, "ui-report: the window is not bound, so it cannot be shown or hidden "
                        + "(the bind failed earlier in this launch - see the error above)");
                    return;
                }

                if (_isWindowOpen == value)
                {
                    if (value && _root.resolvedStyle.display == DisplayStyle.None)
                    {
                        _root.style.display = DisplayStyle.Flex;
                        Write(_log, "ui-report: shown again - the window was hidden by the game's own "
                            + "hide key (F2), which writes the same display property this window writes");
                        _refreshTimer = RefreshSeconds;
                        Refresh();
                    }

                    return;
                }

                _isWindowOpen = value;
                _root.style.display = _isWindowOpen ? DisplayStyle.Flex : DisplayStyle.None;

                // The toolbar's tint is re-read from this window's state on EVERY transition, not only
                // on the click that opened it: this window also closes itself (the close button, the
                // map going away, the active vessel leaving the CommNet), and a button left tinted
                // "open" over a closed window is the silent disagreement D39 exists to forbid. The
                // call is the toolbar's own public state refresh and reads this property back, so it
                // cannot recurse.
                MapToolbarWindowController toolbar = CommNextUIManager.Toolbar;
                if (toolbar != null)
                {
                    toolbar.UpdateButtonState();
                }

                if (_isWindowOpen)
                {
                    Write(_log, "ui-report: opened - reporting on the active vessel; the link list is "
                        + "rebuilt every " + RefreshSeconds.ToString("0.#") + " s while this window is "
                        + "open (the engine's own pass runs on the game's timer, so this is already "
                        + "finer than the data)");

                    // Refresh immediately, so the open is one line plus one counts line rather than a
                    // blank window for up to one refresh interval.
                    _refreshTimer = 0f;
                    Refresh();
                }
                else
                {
                    Write(_log, "ui-report: closed");
                }
            }
        }

        /// <summary>
        /// Binds the window: resolves the root, wires the controls and attaches the stylesheet.
        /// </summary>
        /// <param name="renderer">The <c>PanelRenderer</c> <c>Window.Create</c> returned.</param>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink.</param>
        /// <param name="error">Error sink.</param>
        /// <remarks>
        /// Called by <c>CommNextUIManager</c> immediately after the component is added. Idempotent:
        /// the manager's call and the <c>OnEnable</c> fallback can both arrive, and only the first one
        /// that sees a live renderer binds.
        /// </remarks>
        public void Initialize(PanelRenderer renderer, Action<string> log, Action<string> warn,
            Action<string> error)
        {
            _log = log;
            _warn = warn;
            _error = error;

            if (renderer != null)
            {
                _renderer = renderer;
            }

            Bind();
        }

        /// <summary>
        /// The fallback bind path, for a component enabled without <see cref="Initialize"/>.
        /// </summary>
        /// <remarks>
        /// Unity calls this from <c>AddComponent</c>, i.e. BEFORE the manager calls
        /// <see cref="Initialize"/> - so on the normal path this runs with no sinks and returns. Kept
        /// because a re-enable after a disable would otherwise leave a live window permanently
        /// unbound, and the failure mode of that is a blank window with no log line at all.
        /// </remarks>
        private void OnEnable()
        {
            if (_log == null)
            {
                return;
            }

            Bind();
        }

        /// <summary>The one-time bind, guarded so a second enable cannot double-subscribe.</summary>
        private void Bind()
        {
            if (_bound || _renderer == null)
            {
                return;
            }

            // GetWindowRoot resolves the window's content root out of the clone's template container
            // (`Extensions::GetWindowRoot` -> `Window::ResolveWindowRoot`), so this is the element the
            // 0.2.8.5 shape addressed as `rootVisualElement[0]` - the `[0]` index is NOT ported.
            VisualElement root = UitkForKsp2.API.Extensions.GetWindowRoot(_renderer);
            if (root == null)
            {
                Write(_error, "ui-report: Extensions.GetWindowRoot(PanelRenderer) returned null - the "
                    + "window has not built its visual tree, so the report cannot be wired up and will "
                    + "never appear. This is a Window.Create failure, not a markup failure");
                return;
            }

            if (root.childCount == 0)
            {
                // The blank-window trap, named: a VisualTreeAsset that a different Unity generation
                // serialised clones with ZERO children, and every query then returns null with no
                // exception anywhere. A bundle built by the wrong editor is the usual cause.
                Write(_error, "ui-report: the window root has no children - the cloned "
                    + "VisualTreeAsset is EMPTY, so every element query below will return null. This is "
                    + "the version-mismatch symptom (bundle built by a different Unity than the one "
                    + "loading it); rebuild the UI bundle with 6000.5.8f1");
                return;
            }

            _root = root;
            _bound = true;

            // The sheet first, so the proof below runs against a tree the sheet is attached to, and a
            // failed attach is exactly one warning rather than one per check.
            CommNextUIManager.EnsureStylesAttached(_root, _log, _warn);

            _nameLabel = _root.Q<Label>("name-label");
            _rangeLabel = _root.Q<Label>("range-label");
            _powerIcon = _root.Q<VisualElement>("power-icon");
            _connectionsList = _root.Q<ScrollView>("connections-list");
            _bandsList = _root.Q<VisualElement>("bands-list");
            _focusButton = _root.Q<Button>("focus-button");
            _filterDropdown = _root.Q<DropdownField>("filter-dropdown");
            _sortDropdown = _root.Q<DropdownField>("sort-dropdown");
            _sortDirectionButton = _root.Q<SortDirectionButton>("sort-direction-button");
            Button closeButton = _root.Q<Button>("close-button");

            if (_nameLabel == null || _rangeLabel == null || _powerIcon == null
                || _connectionsList == null || _bandsList == null || _focusButton == null
                || _filterDropdown == null || _sortDropdown == null || _sortDirectionButton == null
                || closeButton == null)
            {
                Write(_error, "ui-report: the window template is missing one of the elements this "
                    + "controller binds (name-label=" + Describe(_nameLabel) + ", range-label="
                    + Describe(_rangeLabel) + ", power-icon=" + Describe(_powerIcon)
                    + ", connections-list=" + Describe(_connectionsList) + ", bands-list="
                    + Describe(_bandsList) + ", focus-button=" + Describe(_focusButton)
                    + ", filter-dropdown=" + Describe(_filterDropdown) + ", sort-dropdown="
                    + Describe(_sortDropdown) + ", sort-direction-button="
                    + Describe(_sortDirectionButton) + ", close-button=" + Describe(closeButton)
                    + ") - the elements that are present still work, but the report is incomplete");
            }

            if (closeButton != null)
            {
                closeButton.clicked += () => IsWindowOpen = false;
            }

            if (_focusButton != null)
            {
                _focusButton.clicked += FocusReportedVessel;
            }

            if (_powerIcon != null)
            {
                // One manipulator for the window's whole life, like the row controllers'.
                _powerTooltip = new TooltipManipulator(Localize.Text(LocalizedStrings.NoPower));
                _powerIcon.AddManipulator(_powerTooltip);
            }

            if (_filterDropdown != null)
            {
                _filterTooltip = new TooltipManipulator(Localize.Text(LocalizedStrings.FilterLabel));
                _filterDropdown.AddManipulator(_filterTooltip);
                _query.BindFilter(_filterDropdown, _warn);
            }

            if (_sortDropdown != null)
            {
                _sortTooltip = new TooltipManipulator(Localize.Text(LocalizedStrings.SortLabel));
                _sortDropdown.AddManipulator(_sortTooltip);
                _query.BindSort(_sortDropdown, _warn);
            }

            _query.BindDirection(_sortDirectionButton);
            _query.Changed += SafeRefresh;

            // The ScrollView's rows must be parented into its CONTENT container. Adding them to the
            // ScrollView element itself puts them beside the viewport instead of inside it, which
            // looks exactly like "the list is empty" while every count in the log is correct.
            _connectionsTarget = _connectionsList == null ? null : _connectionsList.contentContainer;
            if (_connectionsList != null && _connectionsTarget == null)
            {
                _connectionsTarget = _connectionsList;
                Write(_warn, "ui-report: the connections ScrollView has no content container, so the "
                    + "rows are parented to the ScrollView itself - they will be laid out beside its "
                    + "viewport rather than inside it, and the list may look empty or short even though "
                    + "the row count below is correct");
            }

            // The proof is one-shot and needs a layout pass: at this instant the window is hidden
            // (display: none), so its rect is empty and every resolved value is a default. The event
            // fires on the first real layout - i.e. the first time the player opens the report.
            _root.RegisterCallback<GeometryChangedEvent>(OnFirstGeometryChanged);

            // U6h: the layout audit's trigger - a separate callback because its lifetime is the
            // opposite of the proof's. The proof is one-shot; the audit must run once per settled
            // size for as long as the window exists, which is what the minimum-size defects need
            // (the player resizes the window, and the audit re-reads the rows it moved).
            _root.RegisterCallback<GeometryChangedEvent>(OnWindowGeometryChanged);

            // Hidden until the toolbar's button says otherwise. Set last, so the bind has finished
            // before the first IsWindowOpen write can log a transition.
            _isWindowOpen = false;
            _root.style.display = DisplayStyle.None;

            // Applied now, and re-applied by the library when the element is first laid out. The
            // position is the only thing the sheet cannot style, and the library's own drag takes
            // over from the first layout on.
            _root.SetDefaultPosition(DefaultPosition);

            Write(_log, "ui-report: bound - root='" + _root.name + "' (childCount=" + root.childCount
                + "), name-label=" + Describe(_nameLabel) + " range-label=" + Describe(_rangeLabel)
                + " connections-list=" + Describe(_connectionsList) + " bands-list="
                + Describe(_bandsList) + " filter=" + Describe(_filterDropdown) + " sort="
                + Describe(_sortDropdown) + " direction=" + Describe(_sortDirectionButton)
                + ", styles=" + CommNextUIManager.SheetRoute + ", panel " + UIScreenUtils.Describe());
        }

        /// <summary>
        /// Where the window sits before the player moves it: under the toolbar, right edges aligned.
        /// </summary>
        /// <param name="size">The element's own size, as the library passes it.</param>
        /// <returns>The position, in panel coordinates.</returns>
        /// <remarks>
        /// The legacy's own arithmetic, generalised: it wrote
        /// <c>toolbar.transform.position + (toolbar.Width - 400, toolbar.Height + 10)</c>, where
        /// <c>400</c> was its hard-coded guess at this window's width. The callback hands us the real
        /// width, so the same intent is exact. The toolbar's position is read from the LIVE element
        /// rather than recomputed, so the two windows cannot drift apart after the player has dragged
        /// the toolbar; when the toolbar is not bound (a launch where the window layer came up
        /// partially) the toolbar's own reference position is used as a fallback, which is what the
        /// legacy's constant amounted to.
        /// </remarks>
        private Vector2 DefaultPosition(Vector2 size)
        {
            float panelWidth = UIScreenUtils.PanelWidth(_root);
            Vector2 fallback = UIScreenUtils.ToolbarDefaultPosition(size, panelWidth);

            MapToolbarWindowController toolbar = CommNextUIManager.Toolbar;
            VisualElement toolbarRoot = toolbar == null ? null : toolbar.Root;
            if (toolbarRoot == null)
            {
                return fallback;
            }

            float left = toolbarRoot.resolvedStyle.left;
            float top = toolbarRoot.resolvedStyle.top;
            float width = toolbarRoot.resolvedStyle.width;
            float height = toolbarRoot.resolvedStyle.height;

            if (float.IsNaN(left) || float.IsNaN(top) || float.IsNaN(width) || float.IsNaN(height)
                || width <= 0f)
            {
                return fallback;
            }

            // A small inset from the panel's own edge is the only clamp performed here; the library's
            // `CheckScreenBounds` owns keeping the window inside the screen once the player drags it.
            float x = Mathf.Max(left + width - size.x, 8f);
            float y = Mathf.Max(top + height + 10f, 8f);

            if (!_positionLogged)
            {
                _positionLogged = true;
                Write(_log, "ui-report: placed under the toolbar at left=" + x.ToString("0.#")
                    + " top=" + y.ToString("0.#") + " (toolbar " + width.ToString("0.#") + "x"
                    + height.ToString("0.#") + " at left=" + left.ToString("0.#") + " top="
                    + top.ToString("0.#") + ")");
            }

            return new Vector2(x, y);
        }

        /// <summary>
        /// Runs the stylesheet proof once, on the first layout pass with a real size.
        /// </summary>
        /// <param name="evt">The geometry change.</param>
        /// <remarks>
        /// The same one-shot proof the toolbar runs, on its own tag: a sheet in a list is not evidence
        /// that any rule is in effect, and the report's rows are styled entirely by the sheet (the
        /// images, the colours, the direction tag).
        /// </remarks>
        private void OnFirstGeometryChanged(GeometryChangedEvent evt)
        {
            if (_styleProofRun)
            {
                return;
            }

            // A hidden window lays out at zero: `display: none` means no geometry, and running the
            // proof there would read four defaults and call the sheet missing.
            if (evt.newRect.width <= 0f || evt.newRect.height <= 0f)
            {
                return;
            }

            _styleProofRun = true;
            _root.UnregisterCallback<GeometryChangedEvent>(OnFirstGeometryChanged);

            // P9.4 (F84) measured the rectangle the removed library lock used to cover; P9.5's guard
            // (D65) covers the same rectangle through `PanelInputBlocker`, so this line is still the
            // number to check the guarded region against - it is the region, not a symptom of it.
            //
            // P8b's `BlockGameInput` engaged its lock while the pointer was merely over this element
            // (`GameInputBlockManipulator.CheckPointerState`: `_isPointerOver || _isPointerDown` ->
            // `AcquireLock`), and `IsPointerOverTarget` tests exactly `worldBound.Contains(pointer)`
            // plus "the pick under the pointer is this element or a descendant" - so `worldBound` IS
            // the dead zone the player described, and it is the number to check that description
            // against.
            //
            // It is logged here, on the first real layout, rather than beside the window's creation in
            // `CommNextUIManager`: the root is `display: none` until the window is first opened, so at
            // creation this rectangle does not exist yet and `worldBound` would be identically zero -
            // a measurement that says nothing. `_root` is the element the library's manipulator
            // targets (`Window.ResolveWindowRoot` resolves to the UXML's own root, the element the bind
            // line names as `root='root'`), and `UIScreenUtils.Describe()` prints `scale=1` on that
            // same bind line, so these panel coordinates are screen pixels.
            Rect deadZone = _root.worldBound;
            Write(_log, "ui: map-input - vessel report window root worldBound=(x="
                + deadZone.x.ToString("0.#") + ", y=" + deadZone.y.ToString("0.#")
                + ", w=" + deadZone.width.ToString("0.#") + ", h=" + deadZone.height.ToString("0.#")
                + ") - this is the rectangle the removed input lock used to cover; measured once, at "
                + "the report's FIRST real layout, because the root is display:none until the window "
                + "is first opened");

            // The toolbar's four checks do NOT describe this page (F65): the report's header row is
            // only *named* `toolbar` - it has none of the sheet's `.toolbar` class - and there is no
            // `lines-button` here, so `Prove` would read four defaults and report a permanent false
            // RED on a correctly styled page. `ProveReport` reads four values the sheet alone supplies
            // on this page, and keeps its own statics so the toolbar's verdict is never overwritten.
            bool proved = UIStyleSheetProof.ProveReport(_root, "ui-styles-report", _log, _warn);

            Write(proved ? _log : _warn, CommNextUIManager.Why(proved) + " [" + _root.name + " "
                + evt.newRect.width.ToString("0.#") + "x" + evt.newRect.height.ToString("0.#")
                + " at left=" + Position.x.ToString("0.#") + " top=" + Position.y.ToString("0.#")
                + "]");
        }

        /// <summary>
        /// Arms the layout audit on a geometry change (U6h), one audit per settled size.
        /// </summary>
        /// <param name="evt">The geometry change, as UI Toolkit reports it.</param>
        /// <remarks>
        /// The whole debounce is three fields and one early-out, and it is here rather than in
        /// <see cref="Update"/> because a geometry change during a drag arrives several times per
        /// frame: the countdown is restarted by each one, so only the final size of a drag reaches the
        /// audit. Zero-sized events are the window being hidden (`display: none` lays out at zero) -
        /// skipped, because auditing them would read zeros and claim the rows collapsed.
        /// </remarks>
        private void OnWindowGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt.newRect.width <= 0f || evt.newRect.height <= 0f)
            {
                return;
            }

            Vector2 size = new Vector2(evt.newRect.width, evt.newRect.height);

            // Same size as the last audit: a re-layout at an unchanged size (a pooled row rebuilt, a
            // scrollbar arriving, a font loading) is not a size the player set, and auditing it would
            // print the same line again.
            if (Mathf.Abs(size.x - _layoutAuditSize.x) < 0.5f
                && Mathf.Abs(size.y - _layoutAuditSize.y) < 0.5f)
            {
                return;
            }

            _layoutAuditPending = true;
            _layoutAuditFrames = 0;
            _layoutAuditPendingSize = size;
        }

        /// <summary>Runs the armed audit once the size has stopped changing.</summary>
        private void RunLayoutAuditWhenSettled()
        {
            if (!_layoutAuditPending)
            {
                return;
            }

            _layoutAuditFrames++;
            if (_layoutAuditFrames < LayoutAuditSettleFrames)
            {
                return;
            }

            _layoutAuditPending = false;
            _layoutAuditSize = _layoutAuditPendingSize;
            AuditLayout();
        }

        /// <summary>
        /// Reads every row the minimum-size defects touch and prints one line plus one verdict (U6h).
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the acceptance gate for the layout half of the report, and it is a READ of the
        /// settled layout, not a re-run of the layout engine: `worldBound` is the element's rect after
        /// layout, in panel coordinates, and every rect below is translated into the root's own frame
        /// so one line can be compared against another (a nested element's `layout` is relative to its
        /// parent, which is useless for "does this escape the window"). The verdict does the same
        /// translation before it compares, against the root's LOCAL rect - U6i's fix, because the
        /// world-space bound it used to compare against reported every child as an escape; the note in
        /// the method carries the numbers.
        /// </para>
        /// <para>
        /// The element set is bounded and named, deliberately: the rows the player's four defects name
        /// (`toolbar`, `vessel-row`, `filter-row`, the two lists), the labels inside the header, and
        /// the two buttons whose intersection is defect 2. The pooled children of the two lists are not
        /// walked - there can be any number of them, they are rebuilt every 0.2 s, and a row that
        /// escapes ITS list is that list's own overflow, not a window-boundary defect.
        /// </para>
        /// <para>
        /// The tolerance is 0.5 px: UI Toolkit rounds layout to whole pixels in places, and a
        /// sub-pixel "escape" is not a visible one. If a row really does leave the root, the offending
        /// element is named on the verdict line and the verdict is written with <c>warn</c>, so a run
        /// that fails this gate is visible in the log without grepping for a number. Defect 2 is
        /// checked with <c>Rect.Overlaps</c> and reported with the measured gap, so "does not
        /// intersect" comes with the number that proves it.
        /// </para>
        /// </remarks>
        private void AuditLayout()
        {
            Rect rootWorld = _root.worldBound;

            // U6i: THE TWO FRAMES ARE NOT INTERCHANGEABLE, and this line is the fix for a verdict that
            // was meaningless while its rects were right. `worldBound` is in PANEL coordinates and the
            // root usually does not sit at the panel's origin (L25 measured (1089, 69)); `RowText` and
            // `RectOf` both translate every row into the ROOT's own frame by subtracting that origin.
            // The verdict below used to compare those root-local rects against the WORLD-space
            // `rootWorld`, so with the window at x=1089 a row at local x=9 read as "escapes left" by
            // 1080 px - every child at every size - while, the other way round, a genuine right or
            // bottom escape was hidden until it exceeded a world-space bound ~1089 or ~69 px away.
            // The comparison is therefore made against the root's LOCAL rect, whose origin is (0, 0)
            // by construction: same space as every rect it is handed. The rects were never the
            // problem (they are the numbers that proved the four U6h fixes) and are unchanged.
            Rect rootLocal = new Rect(0f, 0f, rootWorld.width, rootWorld.height);

            // The audited set: a parent and a name per row. The three header children are read from
            // the header row itself, because `name-label` and `range-label` also exist in every pooled
            // band row and connection row - an unscoped `Q` would be reading "whoever is first in the
            // tree", which is the header today and would silently become wrong the day a list is
            // parented above it.
            VisualElement header = _root.Q("vessel-row");
            VisualElement titlebar = _root.Q("toolbar");
            VisualElement[] parents = { _root, _root, _root, _root, _root, header, header, header,
                titlebar };
            string[] audited = { "toolbar", "vessel-row", "filter-row", "connections-list",
                "bands-list", "name-label", "range-label", "focus-button", "close-button" };

            string rows = string.Empty;
            for (int i = 0; i < audited.Length; i++)
            {
                rows += (i == 0 ? string.Empty : " ") + audited[i] + "="
                    + RowText(parents[i], audited[i], rootWorld);
            }

            Write(_log, "ui-report: layout-audit - root layout=" + RectText(_root.layout)
                + " worldBound=" + RectText(rootWorld) + " rootLocal=" + RectText(rootLocal)
                + " resolvedStyle="
                + N(_root.resolvedStyle.width) + "x" + N(_root.resolvedStyle.height)
                + "; rows (root-local frame, the frame the verdict compares against): "
                + rows);

            // The two verdicts the acceptance gate is made of: nothing escapes the root, and the
            // header's Focus button is clear of the title bar's close button.
            string escaped = null;
            for (int i = 0; i < audited.Length; i++)
            {
                Rect rect = RectOf(parents[i], audited[i]);
                if (rect.width <= 0f && rect.height <= 0f)
                {
                    continue;
                }

                string side = EscapedSide(rect, rootLocal);
                if (side != null)
                {
                    escaped = escaped == null ? audited[i] + " escapes " + side
                        : escaped + ", " + audited[i] + " escapes " + side;
                }
            }

            Rect focus = RectOf(header, "focus-button");
            Rect close = RectOf(titlebar, "close-button");
            bool intersect = focus.width > 0f && close.width > 0f && focus.Overlaps(close);

            // The measured separation, so a near miss is a number rather than a verdict: the two
            // buttons are expected to be in different ROWS (the close button in the title bar, Focus
            // under it), so the vertical gap is the one that matters and is reported first.
            string gap;
            if (intersect)
            {
                gap = "the overlap is " + N(Mathf.Min(focus.yMax, close.yMax)
                    - Mathf.Max(focus.yMin, close.yMin)) + " px tall";
            }
            else if (focus.yMin >= close.yMax)
            {
                gap = "the Focus button's top is " + N(focus.yMin - close.yMax)
                    + " px below the close button's bottom";
            }
            else if (close.yMin >= focus.yMax)
            {
                gap = "the close button sits " + N(close.yMin - focus.yMax)
                    + " px below the Focus button";
            }
            else
            {
                gap = "they are clear by "
                    + N(Mathf.Max(focus.xMin - close.xMax, close.xMin - focus.xMax))
                    + " px horizontally (different columns)";
            }

            Write(escaped == null ? _log : _warn, "ui-report: layout-audit verdict: "
                + (escaped == null
                    ? "no audited child escapes the root (0.5 px tolerance, root-local frame "
                        + RectText(rootLocal) + ")"
                    : "NOT CLEAN - " + escaped)
                + "; focus-button and close-button " + (intersect ? "INTERSECT" : "do not intersect")
                + " - " + gap + " (focus x " + N(focus.xMin) + ".." + N(focus.xMax) + " y "
                + N(focus.yMin) + ".." + N(focus.yMax) + ")");
        }

        /// <summary>The named element's rect after layout, in the root's frame, or a zero rect.</summary>
        private Rect RectOf(VisualElement parent, string elementName)
        {
            VisualElement element = parent == null ? null : parent.Q(elementName);
            if (element == null || _root == null)
            {
                return new Rect();
            }

            Rect world = element.worldBound;
            Rect rootWorld = _root.worldBound;
            return new Rect(world.x - rootWorld.x, world.y - rootWorld.y, world.width, world.height);
        }

        /// <summary>One named row, as `name=(x, y, w, h)` in the root's frame, or `name=absent`.</summary>
        private string RowText(VisualElement parent, string elementName, Rect rootWorld)
        {
            VisualElement element = parent == null ? null : parent.Q(elementName);
            if (element == null)
            {
                return "absent";
            }

            Rect world = element.worldBound;
            return "(" + N(world.x - rootWorld.x) + ", " + N(world.y - rootWorld.y)
                + ", " + N(world.width) + ", " + N(world.height) + ")";
        }

        /// <summary>
        /// Which side of the root's rect the element crosses (0.5 px tolerance), or <c>null</c>.
        /// </summary>
        private static string EscapedSide(Rect rect, Rect root)
        {
            const float tolerance = 0.5f;
            if (rect.xMin < root.xMin - tolerance)
            {
                return "left";
            }

            if (rect.xMax > root.xMax + tolerance)
            {
                return "right";
            }

            if (rect.yMin < root.yMin - tolerance)
            {
                return "top";
            }

            if (rect.yMax > root.yMax + tolerance)
            {
                return "bottom";
            }

            return null;
        }

        /// <summary>A rect as `(x, y, w, h)`, one decimal, invariant culture - log-friendly.</summary>
        private static string RectText(Rect rect)
        {
            return "(" + N(rect.x) + ", " + N(rect.y) + ", " + N(rect.width) + ", " + N(rect.height)
                + ")";
        }

        /// <summary>One layout number, one decimal, invariant culture.</summary>
        private static string N(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>Per-frame tick: the layout audit's countdown, then the refresh timer.</summary>
        /// <remarks>
        /// The audit runs BEFORE the early-outs and outside the timer: it must complete even while
        /// the window is hidden (a resize followed by an F2 hide is still a size the player set) and
        /// it must not be delayed by the 0.2 s refresh.
        /// </remarks>
        private void Update()
        {
            RunLayoutAuditWhenSettled();

            if (!_isWindowOpen)
            {
                return;
            }

            // The library's F2 hiding writes `display` without this class knowing, so the loop stops
            // with it: refreshing an invisible list five times a second is work nobody can see.
            if (_root != null && _root.resolvedStyle.display == DisplayStyle.None)
            {
                return;
            }

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer < RefreshSeconds)
            {
                return;
            }

            _refreshTimer = 0f;
            SafeRefresh();
        }

        /// <summary>Rebuilds the report, catching and reporting once rather than throwing into the UI.</summary>
        /// <remarks>
        /// Called from the timer AND from the query's own <c>Changed</c> event, so a dropdown change is
        /// applied at once instead of up to one interval later. An exception here would otherwise be
        /// thrown out of a UI Toolkit event callback, and the first one is logged in full: the window
        /// is visibly wrong by then, and five identical warnings a second would bury every other line
        /// in the log.
        /// </remarks>
        private void SafeRefresh()
        {
            if (!_bound)
            {
                return;
            }

            try
            {
                Refresh();
            }
            catch (Exception exception)
            {
                if (_refreshFailureLogged)
                {
                    return;
                }

                _refreshFailureLogged = true;
                Write(_warn, "ui-report: refreshing the report threw (" + exception.GetType().Name
                    + ": " + exception.Message + ") - the list keeps whatever it last showed and this "
                    + "line is written once; further failures are not logged");
            }
        }

        /// <summary>
        /// Re-arms the focus observer on the current message center, if it is a new one.
        /// </summary>
        /// <param name="game">The game instance.</param>
        /// <remarks>
        /// <para>
        /// <b>This exists because the focus route is a message, and a message that nobody handles is
        /// indistinguishable from a message that was never published.</b> The report asks the map to
        /// focus an item by publishing <c>MapRequestFocusMessage</c> (see
        /// <see cref="RequestFocus"/> for why it is that and not the legacy's private method), and the
        /// game answers by broadcasting <c>MapFocusChangedMessage</c>. Observing the answer turns "the
        /// map did not move" into one of two readable facts - the request was answered, or it went
        /// unanswered - instead of a mystery. The game's own log carries the second case too
        /// (<c>Publishing message with no subscriber</c>), but this line says which request it was.
        /// </para>
        /// <para>
        /// Called from <see cref="Refresh"/>, which already holds the game instance, and only
        /// subscribes when the center is a different object - so a save load re-arms it and no
        /// ordinary refresh does any work here.
        /// </para>
        /// </remarks>
        private void EnsureFocusObserver(GameInstance game)
        {
            if (game == null)
            {
                return;
            }

            MessageCenter messages = game.Messages;
            if (messages == null || ReferenceEquals(messages, _observedCenter))
            {
                return;
            }

            bool rearming = _observedCenter != null;
            _observedCenter = messages;
            messages.PersistentSubscribe<MapFocusChangedMessage>(OnMapFocusChanged);

            if (rearming)
            {
                Write(_log, "ui-report: re-armed the map focus observer on a new message center (the "
                    + "save load replaced it, which orphans every handler on the old one)");
            }
        }

        /// <summary>Logs the map's own focus broadcast, so a request's fate is one grep away.</summary>
        /// <param name="message">The broadcast.</param>
        /// <remarks>
        /// Silent while the report is closed: the subscription outlives the window, and every marker
        /// the player focuses in the map would otherwise be logged for the rest of the session.
        /// </remarks>
        private void OnMapFocusChanged(MessageCenterMessage message)
        {
            if (!_isWindowOpen)
            {
                return;
            }

            MapFocusChangedMessage focus = message as MapFocusChangedMessage;
            MapItem item = focus == null ? null : focus.FocusedMapItem;
            if (item == null)
            {
                return;
            }

            Write(_log, "ui-report: the map focused its item '" + item.ItemName
                + "' - the focus request was answered (resetZoom=" + focus.ShouldResetZoom + ")");
        }

        /// <summary>
        /// Proves, at most once per session, that an over-long vessel name is elided inside the
        /// window (U6g).
        /// </summary>
        /// <param name="vesselName">The name just written to the label.</param>
        /// <remarks>
        /// <para>
        /// <b>Bounded by its own condition, not only by a flag:</b> it fires on the first refresh
        /// whose name needs MORE width than the label has - the exact case the header fix exists
        /// for - and never again. A run whose vessels all have short names therefore writes nothing,
        /// and the line can never become per-frame noise (the window refreshes five times a second
        /// while it is open).
        /// </para>
        /// <para>
        /// It reads back three facts rather than one: the label's laid-out width, the text's
        /// unconstrained width (<c>MeasureTextSize</c>, so the two numbers are comparable), and the
        /// two style properties the fix sets (<c>text-overflow</c>, <c>white-space</c>) - so a
        /// passing line says both "the rules are in effect" and "this name is being cut by them".
        /// </para>
        /// </remarks>
        private void ProbeNameLayoutIfElided(string vesselName)
        {
            if (_nameLayoutProbeLogged || string.IsNullOrEmpty(vesselName))
            {
                return;
            }

            // Not laid out yet: the root is display:none until the report is first opened, so the
            // first refresh can run with a 0-width label and would prove nothing. Try again next
            // refresh - the window is open by definition by then.
            float width = _nameLabel.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f)
            {
                return;
            }

            float textWidth = _nameLabel.MeasureTextSize(vesselName, 0f,
                VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            if (textWidth <= width)
            {
                return;
            }

            _nameLayoutProbeLogged = true;

            Write(_log, "ui-report: name-layout - the vessel name '" + vesselName + "' needs "
                + textWidth.ToString("0.#", CultureInfo.InvariantCulture) + " px of text, but the "
                + "name label is only " + width.ToString("0.#", CultureInfo.InvariantCulture)
                + " px wide, so it elides at the window edge instead of running under the range row "
                + "(name-label: text-overflow=" + _nameLabel.resolvedStyle.textOverflow
                + ", white-space=" + _nameLabel.resolvedStyle.whiteSpace
                + ", min-width: 0 and overflow: hidden from .vessel-report__name; "
                + "display-tooltip-when-elided keeps the full name on hover)");
        }

        /// <summary>
        /// The first time a connection row's name is too long for its label, prints the numbers that
        /// prove the row absorbed it (U6i).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The row's counterpart of <see cref="ProbeNameLayoutIfElided"/>, and it is a separate probe
        /// because the two defects are different. The header's fix is proven by the label's own width;
        /// the row's fix is proven by the DIRECTION TAG still being inside the row - before U6i the
        /// label carried no elision and every wrapper above it was <c>flex-shrink: 0</c>, so a long
        /// name grew the left group, pushed "In"/"Out" out of the viewport and painted under the
        /// ScrollView's scrollbar.
        /// </para>
        /// <para>
        /// <b>Every rect below is a <c>worldBound</c> and every comparison is between two of them</b> -
        /// same space by construction, which is the lesson the layout audit's own verdict was fixed
        /// for in this phase. Nothing here is translated: the tag is compared against the container it
        /// is drawn in, both in panel coordinates.
        /// </para>
        /// <para>
        /// <b>Bounded: once per session</b> (not once per name), and it only fires on the long-name
        /// case - a session whose vessels all have short names writes nothing. It is called from the
        /// refresh (five times a second), not from <c>Update</c>, and it is a read-only walk of the
        /// list the pool just filled: a row created on this pass has no laid-out width yet, so the
        /// probe simply tries again on the next one, exactly as the header probe does.
        /// </para>
        /// </remarks>
        private void ProbeRowOverflowIfElided()
        {
            if (_rowOverflowProbeLogged || _connectionsTarget == null)
            {
                return;
            }

            for (int i = 0; i < _connectionsTarget.childCount; i++)
            {
                VisualElement row = _connectionsTarget[i];
                if (row == null)
                {
                    continue;
                }

                Label name = row.Q<Label>("name-label");
                if (name == null || string.IsNullOrEmpty(name.text))
                {
                    continue;
                }

                // Either a row the pool created on this pass (no layout yet) or a pooled row the pool
                // has hidden. Both are "no answer yet", not "this row is fine".
                float width = name.resolvedStyle.width;
                if (float.IsNaN(width) || width <= 0f)
                {
                    continue;
                }

                float textWidth = name.MeasureTextSize(name.text, 0f,
                    VisualElement.MeasureMode.Undefined, 0f,
                    VisualElement.MeasureMode.Undefined).x;
                if (textWidth <= width)
                {
                    continue;
                }

                _rowOverflowProbeLogged = true;

                VisualElement container = row.Q<VisualElement>("row__container");
                VisualElement tag = row.Q<VisualElement>("direction-tag");
                Rect containerRect = container == null ? new Rect() : container.worldBound;
                Rect tagRect = tag == null ? new Rect() : tag.worldBound;

                // Containment, with the audit's own 0.5 px tolerance, plus `Rect.Overlaps` as the
                // independent second read: a tag that overlaps the container but is not inside it is
                // the partially-clipped case, and it must not read as clean.
                const float tolerance = 0.5f;
                bool tagInside = tag != null && container != null
                    && tagRect.xMin >= containerRect.xMin - tolerance
                    && tagRect.xMax <= containerRect.xMax + tolerance
                    && tagRect.yMin >= containerRect.yMin - tolerance
                    && tagRect.yMax <= containerRect.yMax + tolerance;

                Write(_log, "ui-report: row-overflow - the name '" + name.text + "' needs "
                    + N(textWidth) + " px of text, but the row's name label is only " + N(width)
                    + " px wide, so it elides (text-overflow=" + name.resolvedStyle.textOverflow
                    + ", white-space=" + name.resolvedStyle.whiteSpace
                    + "; min-width: 0 and overflow: hidden come from "
                    + ".row__container .connection-row__name and display-tooltip-when-elided keeps the "
                    + "full name on hover). The direction tag is " + (tagInside ? "inside" : "OUTSIDE")
                    + " the row container"
                    + (tag == null
                        ? " (there is no direction tag on this row)"
                        : " (tag x " + N(tagRect.xMin) + ".." + N(tagRect.xMax) + " y "
                            + N(tagRect.yMin) + ".." + N(tagRect.yMax) + ", container x "
                            + N(containerRect.xMin) + ".." + N(containerRect.xMax) + " y "
                            + N(containerRect.yMin) + ".." + N(containerRect.yMax) + "; it "
                            + (containerRect.Overlaps(tagRect) ? "overlaps" : "does not overlap")
                            + " the container)")
                    + "; row__container worldBound=" + RectText(containerRect)
                    + " - every rect here is a worldBound and the comparison is same-space");
                return;
            }
        }

        /// <summary>Re-reads the engine and rewrites every row, the header and the band list.</summary>
        private void Refresh()
        {
            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            if (game == null)
            {
                return;
            }

            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            NetworkEngine engine = plugin == null ? null : plugin.Network;
            if (engine == null)
            {
                // Only reachable before the engine exists, which the plugin does in
                // OnPreInitialized - so a report opened this early is a bug, not a state.
                Write(_error, "ui-report: there is no network engine (the plugin has not built one), "
                    + "so the report cannot list anything");
                return;
            }

            // Before any early-out below: a window that is open is a window whose focus requests must
            // be observable, including on the pass where the engine is still empty.
            EnsureFocusObserver(game);

            int count = engine.NodeCount;
            if (count == 0)
            {
                // The first pass has not run yet (the game rebuilds the graph on its own three-second
                // timer). Not a failure and NOT a reason to close: closing on the click would read as
                // "the button is broken". The window stays open and the counts line below says why it
                // is empty.
                ClearLists();
                WriteCounts("-", 0, 0, 0, "the engine has no nodes yet - the first graph pass has "
                    + "not run (the game rebuilds it on its own timer)");
                return;
            }

            VesselComponent vessel = ActiveVessel(game);
            if (vessel == null)
            {
                Write(_warn, "ui-report: there is no active vessel to report on, so the report is "
                    + "closing (the legacy closed it for the same reason)");
                IsWindowOpen = false;
                return;
            }

            IGGuid owner = vessel.GlobalId;
            int index;
            if (!engine.TryGetIndex(owner, out index) || index < 0 || index >= count)
            {
                // The engine's own answer for "not in the CommNet" - a stowed antenna, another body's
                // sphere, or a pass that predates a save load. The legacy closed the window here and
                // logged the same fact; this port keeps that behaviour and names the vessel and the
                // node count, which is what makes the difference between "not connected" and "the
                // engine is empty" visible in the log.
                Write(_warn, "ui-report: the active vessel has no node in the last network pass "
                    + "(nodes=" + count + "), so there is nothing to report and the window is closing "
                    + "- the legacy's own response to the same condition");
                IsWindowOpen = false;
                return;
            }

            NetworkNodeSnapshot mine = engine.Snapshot(index);
            UniverseModel universe = UniverseOf(game);
            string vesselName = NameOf(universe, mine, owner);

            // 1. The header.
            if (_nameLabel != null)
            {
                _nameLabel.text = vesselName;
                ProbeNameLayoutIfElided(vesselName);
            }

            if (_rangeLabel != null)
            {
                _rangeLabel.text = Localize.Text(LocalizedStrings.RangeLabelKey,
                    Units.PrintSI(mine.MaxRange, Units.SymbolMeters).RTEColor("#E7CA76"));
            }

            if (_powerIcon != null)
            {
                _powerIcon.style.display = mine.HasEnoughResources
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            // 2. The links, from the tree.
            BuildLinks(engine, index, mine, universe, count);
            int linkCount = _links.Count;

            _shown.Clear();
            for (int i = 0; i < _links.Count; i++)
            {
                if (_query.Matches(_links[i]))
                {
                    _shown.Add(_links[i]);
                }
            }

            _query.ApplySort(_shown);

            if (_connectionsTarget != null)
            {
                _connectionsTarget.PoolChildren(_shown,
                    (ConnectionRow row, NetworkConnectionViewController controller)
                        => controller.Bind(this, row));
            }

            // U6i: the row-overflow probe reads the labels the pool just laid out. Called here, from
            // the refresh (five times a second), not from Update (sixty): a row created on this pass
            // has no laid-out width yet, and the probe retries on the next refresh.
            ProbeRowOverflowIfElided();

            // 3. The band table, from the engine's own per-node range array.
            _bandRows.Clear();
            for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
            {
                double range = engine.BandRangeOf(index, bandIndex);
                if (range > 0.0)
                {
                    _bandRows.Add(new BandRowData { BandIndex = bandIndex, RangeMeters = range });
                }
            }

            if (_bandsList != null)
            {
                _bandsList.PoolChildren(_bandRows,
                    (BandRowData row, BandRowController controller) => controller.Bind(row));
            }

            // 4. The counts, once per real change.
            WriteCounts(vesselName, linkCount, _shown.Count, _bandRows.Count, null);
        }

        /// <summary>Builds every link the reported vessel is an endpoint of, from the tree.</summary>
        /// <param name="engine">The engine's last pass.</param>
        /// <param name="index">The vessel's node index in that pass.</param>
        /// <param name="mine">The vessel's own snapshot.</param>
        /// <param name="universe">The universe model, for names. May be <c>null</c>.</param>
        /// <param name="count">The pass's node count.</param>
        /// <remarks>
        /// One inbound edge plus every outbound one. The tree holds one edge per node, so "outbound"
        /// is a scan for the vessel's children - the same scan the renderer's pass performs - and the
        /// whole method allocates nothing: the rows go into a reused list.
        /// </remarks>
        private void BuildLinks(NetworkEngine engine, int index, NetworkNodeSnapshot mine,
            UniverseModel universe, int count)
        {
            _links.Clear();

            int parent = engine.PredecessorOf(index);
            if (parent >= 0 && parent < count && parent != index)
            {
                _links.Add(MakeLink(engine, index, parent, false, mine, universe, index));
            }

            for (int child = 0; child < count; child++)
            {
                if (child == index || engine.PredecessorOf(child) != index)
                {
                    continue;
                }

                _links.Add(MakeLink(engine, index, child, true, mine, universe, child));
            }
        }

        /// <summary>Builds one row for the pair whose tree edge is <c>predecessor -> target</c>.</summary>
        /// <param name="engine">The engine's last pass.</param>
        /// <param name="vesselIndex">The reported vessel's node index.</param>
        /// <param name="otherIndex">The other end's node index.</param>
        /// <param name="vesselIsSource">Whether the vessel is the edge's source (the parent side).</param>
        /// <param name="mine">The vessel's own snapshot.</param>
        /// <param name="universe">The universe model, for names. May be <c>null</c>.</param>
        /// <param name="targetIndex">The edge's target index - what the band and the cost are indexed by.</param>
        /// <returns>The row.</returns>
        /// <remarks>
        /// <b>The band and the distance are indexed by the edge's TARGET, not by the vessel.</b> That is
        /// the engine's own convention (<c>SelectedBandOf</c> and <c>CostOf</c> both document "the edge
        /// whose target is this index"), and getting it backwards would colour every outbound row with
        /// the vessel's own inbound band.
        /// </remarks>
        private ConnectionRow MakeLink(NetworkEngine engine, int vesselIndex, int otherIndex,
            bool vesselIsSource, NetworkNodeSnapshot mine, UniverseModel universe, int targetIndex)
        {
            NetworkNodeSnapshot other = engine.Snapshot(otherIndex);

            double squared = engine.CostOf(targetIndex);
            double distance = 0.0;
            if (squared > 0.0 && squared < double.MaxValue && !double.IsInfinity(squared))
            {
                distance = Math.Sqrt(squared);
            }

            double minRange = Math.Min(mine.MaxRange, other.MaxRange);

            ConnectionRow row = new ConnectionRow();
            row.OtherIndex = otherIndex;
            row.OtherOwner = other.Owner;
            row.OtherName = NameOf(universe, other, other.Owner);
            row.OtherIsRelay = other.IsRelay;
            row.OtherHasEnoughResources = other.HasEnoughResources;
            row.OtherIsControlSource = other.IsControlSource;
            row.DistanceMeters = distance;
            row.MinRangeMeters = minRange;
            row.SignalStrength = (float)SignalStrength(distance, minRange);
            row.BandIndex = engine.SelectedBandOf(targetIndex);
            row.VesselIsSource = vesselIsSource;
            row.VesselHasEnoughResources = mine.HasEnoughResources;
            return row;
        }

        /// <summary>The signal strength between two ends, by the legacy's own formula.</summary>
        /// <param name="distance">The two ends' distance in metres.</param>
        /// <param name="minRange">The smaller of the two ends' ranges.</param>
        /// <returns>0..1.</returns>
        /// <remarks>
        /// <c>relative = 1 - distance / minRange</c>, then <c>(3 - 2*relative) * relative²</c> - the
        /// legacy's <c>NetworkConnection.SignalStrength()</c>, which it took from the CommNet wiki's
        /// own curve, and the same shape the game's antenna maths uses. Written out rather than
        /// shared because the legacy had exactly one caller and this port has one too.
        /// </remarks>
        private static double SignalStrength(double distance, double minRange)
        {
            if (minRange <= 0.0)
            {
                return 0.0;
            }

            double relative = 1.0 - distance / minRange;
            if (relative < 0.0)
            {
                relative = 0.0;
            }
            else if (relative > 1.0)
            {
                relative = 1.0;
            }

            return (3.0 - 2.0 * relative) * relative * relative;
        }

        /// <summary>Empties both lists, for a refresh that has nothing to show.</summary>
        private void ClearLists()
        {
            _links.Clear();
            _shown.Clear();
            _bandRows.Clear();

            if (_connectionsTarget != null)
            {
                _connectionsTarget.PoolChildren(_shown,
                    (ConnectionRow row, NetworkConnectionViewController controller)
                        => controller.Bind(this, row));
            }

            if (_bandsList != null)
            {
                _bandsList.PoolChildren(_bandRows,
                    (BandRowData row, BandRowController controller) => controller.Bind(row));
            }
        }

        /// <summary>
        /// Writes the counts line, at most once per change.
        /// </summary>
        /// <param name="vesselName">The reported vessel's name, or <c>-</c>.</param>
        /// <param name="links">Every link the vessel is an endpoint of.</param>
        /// <param name="shown">The rows the filter let through - what the list should hold.</param>
        /// <param name="bands">The bands with a positive range.</param>
        /// <param name="reason">A note to append, or <c>null</c>.</param>
        /// <remarks>
        /// <b>The empty-window rule, satisfied positively.</b> "The window shows nothing" must be
        /// distinguishable from "the query was never asked", so this names the vessel, the numbers on
        /// both sides of the filter, and what the two containers ACTUALLY hold after the pool ran
        /// (<c>childCount</c>) - which is the only number that says whether anything reached the
        /// screen. It is written on open and on every real change, never per refresh, because five
        /// identical lines a second would bury the one that matters.
        /// </remarks>
        private void WriteCounts(string vesselName, int links, int shown, int bands, string reason)
        {
            bool sameVessel = string.Equals(vesselName, _loggedVesselName, StringComparison.Ordinal);
            if (sameVessel && links == _loggedLinks && shown == _loggedShown && bands == _loggedBands)
            {
                return;
            }

            _loggedVesselName = vesselName;
            _loggedLinks = links;
            _loggedShown = shown;
            _loggedBands = bands;

            string containers = "window childCount=" + (_root == null ? "no root" : _root.childCount.ToString())
                + ", connections-list childCount="
                + (_connectionsTarget == null ? "no target" : _connectionsTarget.childCount.ToString())
                + ", bands-list childCount="
                + (_bandsList == null ? "MISSING" : _bandsList.childCount.ToString());

            Write(_log, "ui-report: report for '" + vesselName + "' - " + links + " link(s) built, "
                + shown + " shown after the filter, " + bands + " band(s); " + containers + ", "
                + _query.Describe() + (reason == null ? string.Empty : "; " + reason));
        }

        /// <summary>Focuses the reported vessel's own map marker.</summary>
        /// <remarks>
        /// The legacy's <c>FocusVessel</c>. It resolved the vessel from its own cached field and called
        /// the renderer's <c>FocusOnMap</c>; here the vessel is resolved again (this port's report holds
        /// no vessel between refreshes) and the marker is focused directly.
        /// </remarks>
        private void FocusReportedVessel()
        {
            if (!MapIsUp("the focus button"))
            {
                return;
            }

            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            VesselComponent vessel = ActiveVessel(game);
            if (vessel == null)
            {
                Write(_warn, "ui-report: the focus button was clicked with no active vessel, so there "
                    + "is nothing to focus");
                return;
            }

            if (!TryFindMapItem(game, vessel.GlobalId, false, out Map3DFocusItem item))
            {
                return;
            }

            if (RequestFocus(game, item))
            {
                Write(_log, "ui-report: focus -> '" + vessel.Name + "' on the map (published "
                    + FocusMessageName + " for its map item)");
            }
        }

        /// <summary>The name of the game's focus message, for the log lines above.</summary>
        /// <remarks>
        /// A constant, so every line that names the route names the same thing as the call site
        /// (see <see cref="RequestFocus"/> for what the route is and why it is not the legacy's).
        /// </remarks>
        private const string FocusMessageName = "MapRequestFocusMessage";

        /// <summary>
        /// Asks the map view to focus one map marker, through the game's own message.
        /// </summary>
        /// <param name="game">The game instance.</param>
        /// <param name="item">The marker to focus. Must not be <c>null</c>.</param>
        /// <returns><c>true</c> when the request was published.</returns>
        /// <remarks>
        /// <para>
        /// <b>The legacy called <c>Map3DFocusItem.FocusSimObject()</c>, and that member is PRIVATE on
        /// this pin.</b> Measured on the installed runtime: the type exposes 33 declared members and
        /// the focus is not one of them - reflection over
        /// <c>$KSP2_ROOT/KSP2_x64_Data/Managed/Assembly-CSharp.dll</c> reports
        /// <c>private Void FocusSimObject()</c> (and <c>private Void ControlVessel()</c>, the row's
        /// other action). The legacy compiled against a <b>publicized</b> copy of the game assembly,
        /// where every private member is rewritten public; this port compiles against the shipped one
        /// (AGENTS.md §2), so those two calls are CS1061 here and must not be reintroduced.
        /// </para>
        /// <para>
        /// The replacement is the game's own request path, and it is documented as exactly that:
        /// <c>KSP.Messages.MapRequestFocusMessage</c> - "Represents a message that requests the map
        /// view to focus on a specific ⟨MapItem⟩", with one public <c>MapItem MapItem</c> property.
        /// Publishing it is a public, supported call: the map view's own subscriber does the work the
        /// private method used to be called for. <c>MapFocusChangedMessage</c> is the *notification*
        /// that comes back when the focus lands - it is deliberately NOT what this publishes, because
        /// announcing a change that has not happened is not the same as requesting it.
        /// </para>
        /// <para>
        /// The marker's <c>AssociatedMapItem</c> is its identity in the map's own model (a public
        /// getter, verified by the same reflection run); the control source's KSC marker has one too,
        /// so both the vessel rows and the KSC row go through this single route.
        /// </para>
        /// </remarks>
        private bool RequestFocus(GameInstance game, Map3DFocusItem item)
        {
            if (game == null || item == null)
            {
                return false;
            }

            MapItem mapItem = item.AssociatedMapItem;
            if (mapItem == null)
            {
                Write(_warn, "ui-report: that map marker has no associated MapItem, so there is nothing "
                    + "to ask the map to focus on (the marker exists but its map data does not)");
                return false;
            }

            MessageCenter messages = game.Messages;
            if (messages == null)
            {
                Write(_warn, "ui-report: there is no message center, so the focus request cannot be "
                    + "published (normal only outside a session)");
                return false;
            }

            messages.Publish(new MapRequestFocusMessage { MapItem = mapItem });
            return true;
        }

        /// <summary>Focuses one link's other end on the map. Called by the row that was clicked.</summary>
        /// <param name="owner">The other end's owner id.</param>
        /// <param name="isControlSource">Whether the other end is the control source (the KSC).</param>
        public void FocusOnMap(IGGuid owner, bool isControlSource)
        {
            if (!MapIsUp("the row"))
            {
                return;
            }

            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            if (!TryFindMapItem(game, owner, isControlSource, out Map3DFocusItem item))
            {
                return;
            }

            if (RequestFocus(game, item))
            {
                Write(_log, "ui-report: focus -> " + owner
                    + (isControlSource ? " (the control source)" : string.Empty)
                    + " (published " + FocusMessageName + " for its map item)");
            }
        }

        /// <summary>Takes control of one link's other end, when it is a vessel.</summary>
        /// <param name="owner">The other end's owner id.</param>
        /// <param name="isControlSource">Whether the other end is the control source (the KSC).</param>
        /// <remarks>
        /// <c>Map3DFocusItem.ControlVessel()</c> is the game's own "take control of this vessel" route
        /// (mlist 58411). A control source is not a vessel - the row hides the button for it, and this
        /// method refuses it as well, so neither half depends on the other being right.
        /// </remarks>
        public void ControlVesselOnMap(IGGuid owner, bool isControlSource)
        {
            if (isControlSource)
            {
                Write(_warn, "ui-report: the control button was asked to take control of the control "
                    + "source, which is not a vessel - nothing happened (the row hides this button for "
                    + "that link)");
                return;
            }

            if (!MapIsUp("the control button"))
            {
                return;
            }

            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            KSP.Sim.impl.ViewController view = game == null ? null : game.ViewController;
            if (view == null)
            {
                Write(_warn, "ui-report: there is no view controller, so this vessel cannot be taken "
                    + "control of from here");
                return;
            }

            // The game's own veto, asked before the switch rather than after: a vessel that cannot be
            // left (a cutscene, a pending vehicle change) would otherwise be switched out from under
            // whatever asked for it.
            if (!view.CanObserverLeaveTheActiveVessel())
            {
                Write(_warn, "ui-report: the game says the observer cannot leave the active vessel "
                    + "right now, so this switch was not attempted (the same guard the game's own "
                    + "vessel-switch UI uses)");
                return;
            }

            try
            {
                view.SetActiveVehicle(owner);
            }
            catch (Exception exception)
            {
                Write(_warn, "ui-report: taking control of " + owner + " threw ("
                    + exception.GetType().Name + ": " + exception.Message + ") - the game's own "
                    + "SetActiveVehicle refused it");
                return;
            }

            Write(_log, "ui-report: control -> " + owner + " (ViewController.SetActiveVehicle, the "
                + "game's own public vessel-switch route)");
        }

        /// <summary>Whether a map action can run at all, logging once per click when it cannot.</summary>
        /// <param name="what">The control that was clicked, for the line.</param>
        /// <returns><c>true</c> when the map view is alive.</returns>
        /// <remarks>
        /// The legacy's <c>MapViewHelper.IsInMapViewOrNotify()</c>, minus its notification - see the
        /// file header. <c>EventListener.IsInMapView</c> is a fast path, never the only gate: the map
        /// item lookup below is the real one, and it warns by name when the marker is absent.
        /// </remarks>
        private bool MapIsUp(string what)
        {
            if (EventListener.IsInMapView)
            {
                return true;
            }

            Write(_warn, "ui-report: " + what + " was used with no map view alive, so the action was "
                + "not attempted (the report closes with the map, so this is a stale click)");
            return false;
        }

        /// <summary>Finds one owner's map marker.</summary>
        /// <param name="game">The game instance.</param>
        /// <param name="owner">The owner's id.</param>
        /// <param name="isControlSource">Whether to look the control source's own id up instead.</param>
        /// <param name="item">Receives the marker, or <c>null</c>.</param>
        /// <returns><c>true</c> when a live marker was found.</returns>
        /// <remarks>
        /// The renderer's own route, duplicated deliberately rather than shared: the renderer's twin is
        /// private to its pass, and the two have opposite failure responses (it warns once per guid per
        /// session and skips a line; this one is answering a click, so it warns with the guid it could
        /// not find and the click does nothing). The control-source swap is the part that matters: the
        /// KSC's node is owned by the KSC, not by a vessel, so a link to it draws and focusses only
        /// through that substitution.
        /// </remarks>
        private bool TryFindMapItem(GameInstance game, IGGuid owner, bool isControlSource,
            out Map3DFocusItem item)
        {
            item = null;

            MapCore mapCore;
            if (game == null || !game.Map.TryGetMapCore(out mapCore) || mapCore == null
                || mapCore.map3D == null)
            {
                Write(_warn, "ui-report: the map core is not available, so no map marker can be found "
                    + "and the action did nothing (normal outside a session)");
                return false;
            }

            Dictionary<IGGuid, Map3DFocusItem> mapItems = mapCore.map3D.AllMapSelectableItems;
            if (mapItems == null)
            {
                Write(_warn, "ui-report: the map has no selectable items yet, so no map marker can be "
                    + "found and the action did nothing");
                return false;
            }

            IGGuid key = isControlSource ? mapCore.KSCGUID : owner;
            if (mapItems.TryGetValue(key, out item) && item != null)
            {
                return true;
            }

            item = null;
            Write(_warn, "ui-report: no map marker for " + key + (isControlSource ? " (the control "
                + "source)" : string.Empty) + " - the marker may be culled or not yet created, and the "
                + "action did nothing");
            return false;
        }

        /// <summary>The active vessel, or <c>null</c>.</summary>
        /// <param name="game">The game instance.</param>
        /// <returns>The vessel the map is reporting on.</returns>
        /// <remarks>
        /// The port's established route (<c>ConnectionsRenderer</c> and <c>NetworkProbe</c> both use
        /// it). <c>requireValidInSim: false</c> is deliberate: in the map view the vessel may be in a
        /// state the strict overload rejects, and the report is a read-only view of it.
        /// </remarks>
        private static VesselComponent ActiveVessel(GameInstance game)
        {
            if (game == null)
            {
                return null;
            }

            KSP.Sim.impl.ViewController view = game.ViewController;
            if (view == null)
            {
                return null;
            }

            VesselComponent vessel;
            if (!view.TryGetActiveSimVessel(out vessel, false))
            {
                return null;
            }

            return vessel;
        }

        /// <summary>The universe model, or <c>null</c>.</summary>
        /// <param name="game">The game instance.</param>
        /// <returns>The model names are resolved through.</returns>
        private static UniverseModel UniverseOf(GameInstance game)
        {
            if (game == null)
            {
                return null;
            }

            try
            {
                return game.UniverseModel;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>A display name for one node.</summary>
        /// <param name="universe">The universe model, or <c>null</c>.</param>
        /// <param name="node">The node.</param>
        /// <param name="owner">The node's owner id, for the fallback.</param>
        /// <returns>The sim object's own name, the control source's label, or the owner id.</returns>
        /// <remarks>
        /// The control source is the KSC, which is not a vessel and has no sim-object name of its own -
        /// the legacy showed its own localized label for it and this port keeps that key
        /// (<c>CommNext/Simulation/KSCCommNet</c>). The final fallback is the owner id rather than an
        /// empty string: a row with a guid on it is a report of a real problem, and an empty label
        /// reads as a layout bug.
        /// </remarks>
        private static string NameOf(UniverseModel universe, in NetworkNodeSnapshot node, IGGuid owner)
        {
            if (node.IsControlSource)
            {
                return Localize.Text(LocalizedStrings.KSCCommNet);
            }

            SimulationObjectModel simObject = universe == null ? null : universe.FindSimObject(owner);
            string name = simObject == null ? null : simObject.Name;
            return string.IsNullOrEmpty(name) ? owner.ToString() : name;
        }

        /// <summary>A one-word description of a queried element, for a failure line.</summary>
        /// <param name="element">The element, or <c>null</c>.</param>
        /// <returns><c>ok</c> or <c>MISSING</c>.</returns>
        private static string Describe(VisualElement element)
        {
            return element == null ? "MISSING" : "ok";
        }

        /// <summary>Writes through a callback that is allowed to be absent.</summary>
        /// <param name="write">The callback, or <c>null</c>.</param>
        /// <param name="message">The line.</param>
        /// <remarks>
        /// Null-safe because <c>OnEnable</c> can run before the manager hands the sinks over: a log
        /// call on a null delegate inside the boot path is the documented way to turn a live mod into
        /// a dead one (dev guide 61.3).
        /// </remarks>
        private static void Write(Action<string> write, string message)
        {
            if (write != null)
            {
                write(message);
            }
        }
    }
}
