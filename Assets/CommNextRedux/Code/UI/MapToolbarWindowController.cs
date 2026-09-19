// CommNextRedux - the map toolbar: the three-button strip that is the control surface for the
// connection lines and the range rulers.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/MapToolbarWindowController.cs (193 lines), MIT. The
//   element names, the class names it toggles, the tooltip wiring and the three click handlers are
//   the legacy's. Four things are different, and each one is forced by this pin or by a rule the
//   port already carries:
//
//     1. `_root = _window.rootVisualElement[0]` - blind indexing - is replaced by K2D2's guarded
//        shape, re-routed to this pin: `Window.Create` hands back a `PanelRenderer`, the window's
//        content root is fetched with `UitkForKsp2.API.Extensions.GetWindowRoot(renderer)` (it
//        already resolves the clone's template container, so there is NO `[0]` index any more), and
//        the controller refuses to bind with a LOGGED ERROR when that root is null or has no
//        children. A blank window and a missed query are then different facts in the log instead of
//        the same silence (`mods/remote/K2D2Redux/Assets/K2D2/Code/UI/K2D2Window.cs` - the
//        in-game-validated `PanelRenderer` binding - and
//        `mods/remote/FlightPlanRedux/.../FpUiController.SetupDocument`, which records the same
//        no-`[0]` equivalence).
//
//     2. THE BUTTONS WRITE CONFIG, NOT THE RENDERER (D39). The legacy did
//            ConnectionsRenderer.Instance.ConnectionsDisplayMode = ...Next();
//        P7's carried note #2 forbids that here: the mode is config-driven, and a direct renderer
//        write makes `Settings -> Mods` disagree with the map and drops the change on the next
//        load. Both buttons now write `NetworkConfig.Connections.Value` / `NetworkConfig.Rulers.Value`
//        - whose setters drive the renderer - and read the CURRENT mode back for the button's own
//        state. `UpdateButtonState` is also called on the window's show AND on the config callback,
//        so `Settings -> Mods` and the toolbar cannot drift apart in either direction.
//
//     3. The vessel report button OPENS THE REPORT (P8b). It reads the report's live state through
//        `CommNextUIManager.IsVesselReportOpen` and writes the opposite back to
//        `CommNextUIManager.VesselReport.IsWindowOpen`, then re-runs `UpdateButtonState` - so the
//        button is a real toggle that follows the report through its own close button and through the
//        map going away, and never holds a reference to the window it opens. A launch where the
//        report could not be created says so on the click rather than looking dead.
//
//     4. The default position is applied with `Extensions.SetDefaultPosition` - a pinned-set member
//        (`UitkForKsp2.xml`, `M:UitkForKsp2.API.Extensions.SetDefaultPosition(VisualElement,
//        Func<Vector2,Vector2>)`, "the position to set the element to in the reference resolution")
//        - and NOT by writing `_root.transform.position`, which is CSS `translate` and not
//        `left`/`top` (AGENTS.md 9). The legacy's `_isWindowPositionInitialized` flag is gone with
//        it: the handler is documented as one-shot ("called when the element is resized"), so it
//        applies the default once and the library's own drag manipulator - `MoveOptions.
//        IsMovingEnabled = true` - owns the position from then on.
//
//     5. P9 persists that position in the CONFIG (D53), which is why point 4's callback is now
//        `ResolveInitialPosition` rather than `DefaultPosition`: the saved pair is applied there,
//        clamped to the panel, and the baseline for the exit comparison is taken from what it
//        returns. On leaving the map view the position is sampled once and written only if it moved
//        - never during a drag, which this port does not own. Which field a drag actually moves was
//        MEASURED rather than assumed: the game's `DragManipulator.OnPointerMove` writes the INLINE
//        `style.left`/`style.top` and zeroes `transform.position` (IL), so the read prefers that
//        inline pair, and the entry/exit lines print all three carriers (`inline=`, `resolved=`,
//        `worldBound=`) so one launch proves which one moved. Full argument, with the IL, on
//        `NetworkConfig.MapToolbarX`.
//
// THE STYLESHEET IS ATTACHED HERE AND PROVEN HERE
//   `CommNextUIManager.EnsureStylesAttached` runs at bind time (a sheet in a list is not evidence
//   that any rule is in effect) and `UIStyleSheetProof.Prove` runs once on the first real layout
//   pass, reading resolved values back. The verdict is logged at Info or Warning by the proof
//   itself, and `CommNextUIManager.Why(proved)` turns the pair into one honest sentence - the
//   attach line is never allowed to claim that the sheet styled anything.
//
// WHAT HOLDS THE WINDOW OPEN
//   Nothing here. `IsWindowOpen` is written by `EventListener` from the game's own map messages, and
//   the window is created ONCE for the process and only shown and hidden - so entering and leaving
//   the map a hundred times creates one GameObject and leaves no orphan behind. The `ui: window
//   layer up` line appears exactly once per launch, which is the falsifiable form of that claim.

using System;
using CommNextRedux.Network;
using CommNextRedux.Rendering;
using CommNextRedux.UI.Screen;
using CommNextRedux.UI.Tooltip;
using CommNextRedux.UI.Utils;
using I2.Loc;
using ReduxLib.Configuration;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI
{
    /// <summary>
    /// The map toolbar window: the connection-mode button, the ruler-mode button and the vessel
    /// report button.
    /// </summary>
    public class MapToolbarWindowController : MonoBehaviour
    {
        /// <summary>The window UitkForKsp2 is asked for. One instance, for the process.</summary>
        /// <remarks>
        /// `WindowId` must be unique across every mod in the install: it is how the window is
        /// registered, and two mods claiming one id is a collision the loader cannot report usefully.
        /// The legacy's id is kept (it was already unique to CommNext) and re-pointed at this port's
        /// own name for the same reason the assembly is renamed.
        /// <para>
        /// <c>CheckScreenBounds = true</c> is load-bearing twice over, and P9 measured the second
        /// time: <c>Window.SetupRootElement</c> passes it to <c>MakeDraggable(handle, root, …)</c>,
        /// which builds the manipulator as <c>new DragManipulator(allowDraggingOffScreen:
        /// !checkScreenBounds)</c> (IL). So the drag is bounded to the panel content rect, and a
        /// player cannot drag the toolbar out of reach in the first place - which is why P9's own
        /// clamp only has to hold for a position restored from config, and why the clamp copies the
        /// drag's formula rather than inventing a margin.
        /// </para>
        /// </remarks>
        public static WindowOptions WindowOptions = new WindowOptions
        {
            WindowId = "CommNextRedux_MapToolbarWindow",
            Parent = null,
            IsHidingEnabled = true,
            DisableGameInputForTextFields = true,
            MoveOptions = new MoveOptions
            {
                IsMovingEnabled = true,
                CheckScreenBounds = true
            }
        };

        /// <summary>
        /// The distance, in panel pixels, below which two position samples are the same position.
        /// </summary>
        /// <remarks>
        /// Half a pixel: a drag moves the window in whole panel pixels, so any real movement exceeds
        /// it, and the epsilon exists only so a float read that differs in its last bit cannot be
        /// mistaken for a move - see <see cref="SavePositionIfMoved"/>.
        /// </remarks>
        private const float PositionEpsilon = 0.5f;

        private PanelRenderer _renderer;
        private VisualElement _root;
        private Button _linesButton;
        private TooltipManipulator _linesTooltip;
        private Button _rulersButton;
        private TooltipManipulator _rulersTooltip;
        private Button _vesselReportButton;

        private Action<string> _log;
        private Action<string> _warn;
        private Action<string> _error;

        private bool _bound;
        private bool _isWindowOpen;
        private bool _styleProofRun;

        // P9's persistence state. `_positionSettled` is "a real position exists for this window"
        // and `_positionAtShow` is the baseline the exit comparison is made against - see
        // ResolveInitialPosition and SavePositionIfMoved.
        private bool _positionSettled;
        private Vector2 _positionAtShow;

        /// <summary>The toolbar's own root element, or <c>null</c> before a successful bind.</summary>
        public VisualElement Root
        {
            get { return _root; }
        }

        /// <summary>
        /// The window's position, in panel pixels from the panel's top-left corner.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The getter reads `resolvedStyle.left`/`top` - the LAYOUT's view of the position, and the
        /// one the stylesheet proof has printed since P8a. The persistence read is
        /// <see cref="ReadLivePosition"/>, which prefers the INLINE style because that is the field
        /// the game's own drag writes (F69; its IL is quoted on `NetworkConfig.MapToolbarX`). The two
        /// agree once a layout pass has run after a drag, and the entry/exit lines print both plus
        /// `worldBound`, so a launch shows which field moved rather than inheriting an assumption.
        /// </para>
        /// <para>
        /// The setter writes `left`/`top` inline. Nothing in P9 calls it: the saved position is
        /// restored through `Extensions.SetDefaultPosition`, which applies the value on the first
        /// real layout pass - the only moment the element's own size is known, and the size is what
        /// the clamp needs. `transform.position` (CSS translate) is never written here: the game's
        /// drag zeroes it, and mixing the two offsets the window by the drag history (AGENTS.md 9).
        /// </para>
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

        /// <summary>Whether the toolbar is currently shown.</summary>
        /// <remarks>
        /// Setting this shows or hides <see cref="Root"/> and refreshes the button states on a show.
        /// The transition is logged once per change at Info, because "the toolbar did not appear" and
        /// "the map message never arrived" are different failures and this line is what tells them
        /// apart.
        /// </remarks>
        public bool IsWindowOpen
        {
            get { return _isWindowOpen; }
            set
            {
                if (_root == null)
                {
                    Write(_warn, "ui-toolbar: the window is not bound, so it cannot be shown or hidden "
                        + "(the bind failed earlier in this launch - see the error above)");
                    return;
                }

                if (_isWindowOpen == value)
                {
                    return;
                }

                _isWindowOpen = value;

                // P9: the position is sampled on the way IN and compared on the way OUT, and both
                // samples happen BEFORE the display change - a `display: none` element's resolved
                // geometry is a default, so a read taken after the hide would compare a real number
                // against zero and "the toolbar moved" would be reported on every map exit.
                if (_isWindowOpen)
                {
                    SamplePositionOnEntry();
                }
                else
                {
                    SavePositionIfMoved();
                }

                _root.style.display = _isWindowOpen ? DisplayStyle.Flex : DisplayStyle.None;

                Write(_log, "ui-toolbar: " + (_isWindowOpen ? "shown" : "hidden")
                    + (_isWindowOpen ? " (the map view is alive)" : " (the map view is gone)"));

                if (_isWindowOpen)
                {
                    UpdateButtonState();
                }
                else
                {
                    // The pointer was over a button when the map went away: the element under it stops
                    // existing, so its own mouse-leave is not something to rely on, and a tooltip left
                    // floating over the flight view is the visible symptom.
                    TooltipWindowController overlay = CommNextUIManager.Tooltip;
                    if (overlay != null)
                    {
                        overlay.Hide();
                    }
                }
            }
        }

        /// <summary>
        /// Binds the window: resolves the root, wires the three buttons and attaches the stylesheet.
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
        /// The fallback bind path, for a component that is enabled without <see cref="Initialize"/>.
        /// </summary>
        /// <remarks>
        /// Unity calls this immediately from <c>AddComponent</c>, i.e. BEFORE the manager gets to call
        /// <see cref="Initialize"/> - so on the normal path this runs with no sinks and returns. It is
        /// kept because a re-enable after a disable would otherwise leave a live window permanently
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

            // GetWindowRoot already resolves the window's content root out of the clone's template
            // container (`Extensions::GetWindowRoot` -> `Window::ResolveWindowRoot`; the callback and
            // the WindowComponent use the same resolver), so this is the element the 0.2.8.5 shape
            // addressed as `rootVisualElement[0]` - and the `[0]` index is NOT ported with it.
            VisualElement root = UitkForKsp2.API.Extensions.GetWindowRoot(_renderer);
            if (root == null)
            {
                Write(_error, "ui-toolbar: Extensions.GetWindowRoot(PanelRenderer) returned null - the "
                    + "window has not built its visual tree, so the toolbar cannot be wired up and will "
                    + "never appear. This is a Window.Create failure, not a markup failure");
                return;
            }

            if (root.childCount == 0)
            {
                // The blank-window trap, named: a VisualTreeAsset that a different Unity generation
                // serialised clones with ZERO children, and every query then returns null with no
                // exception anywhere. A bundle built by the wrong editor is the usual cause.
                Write(_error, "ui-toolbar: the window root has no children - the cloned "
                    + "VisualTreeAsset is EMPTY, so every element query below will return null. This is "
                    + "the version-mismatch symptom (bundle built by a different Unity than the one "
                    + "loading it); rebuild the UI bundle with 6000.5.8f1");
                return;
            }

            _root = root;
            _bound = true;

            // The sheet first: a query for an element cannot be affected by styling, but the proof
            // below must run against a tree that has the sheet attached, and a failed attach is worth
            // exactly one warning rather than one per check.
            CommNextUIManager.EnsureStylesAttached(_root, _log, _warn);

            BindModeChangeCallbacks();

            _linesButton = _root.Q<Button>("lines-button");
            _rulersButton = _root.Q<Button>("rulers-button");
            _vesselReportButton = _root.Q<Button>("vessel-report-button");

            if (_linesButton == null || _rulersButton == null || _vesselReportButton == null)
            {
                Write(_error, "ui-toolbar: the toolbar template is missing one of its three buttons "
                    + "(lines=" + Describe(_linesButton) + ", rulers=" + Describe(_rulersButton)
                    + ", vessel-report=" + Describe(_vesselReportButton) + ") - the buttons that are "
                    + "present still work, but the control surface is incomplete");
            }

            if (_linesButton != null)
            {
                _linesTooltip = new TooltipManipulator(
                    Translate(LocalizedStrings.ConnectionsDisplayModeLines));
                _linesButton.AddManipulator(_linesTooltip);
                _linesButton.clicked += OnLinesClicked;
            }

            if (_rulersButton != null)
            {
                _rulersTooltip = new TooltipManipulator(Translate(LocalizedStrings.RulersTooltip));
                _rulersButton.AddManipulator(_rulersTooltip);
                _rulersButton.clicked += OnRulersClicked;
            }

            if (_vesselReportButton != null)
            {
                _vesselReportButton.AddManipulator(
                    new TooltipManipulator(Translate(LocalizedStrings.VesselReportTooltip)));
                _vesselReportButton.clicked += OnVesselReportClicked;
            }

            // The proof is one-shot and needs a layout pass: at this instant the window is hidden
            // (`display: none`), so its rect is empty and every resolved value is a default. The
            // event fires on the first real layout - i.e. the first time the map shows the toolbar -
            // and the handler removes itself so a resize cannot re-run it.
            _root.RegisterCallback<GeometryChangedEvent>(OnFirstGeometryChanged);

            // Hidden until a map view says otherwise. Set last, so the bind has finished before the
            // first `IsWindowOpen` write can log a transition.
            _isWindowOpen = false;
            _root.style.display = DisplayStyle.None;

            // Applied now, and re-applied by the library when the element is first laid out. Position
            // is the only thing the sheet cannot style, and getting it wrong puts the toolbar off
            // screen - so it is set explicitly rather than left to the UXML.
            //
            // P9: the callback is ResolveInitialPosition, not DefaultPosition, because that function
            // is the one place that runs when the element's own size is finally known - and knowing
            // the size is what lets a saved position be clamped instead of trusted.
            _root.SetDefaultPosition(ResolveInitialPosition);

            Write(_log, "ui-toolbar: bound - root='" + _root.name + "' (childCount=" + root.childCount
                + "), buttons lines=" + Describe(_linesButton) + " rulers=" + Describe(_rulersButton)
                + " vessel-report=" + Describe(_vesselReportButton) + ", styles="
                + CommNextUIManager.SheetRoute + ", panel " + UIScreenUtils.Describe());
        }

        /// <summary>
        /// Re-tints the two mode buttons when either mode changes outside this window.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The other half of D39.</b> The buttons write the config, so the map and the button agree
        /// on the click path; this makes them agree on the <i>settings</i> path too - `Settings -&gt;
        /// Mods` writes the same entry while the map is open, and without these two callbacks the
        /// button would keep showing the mode the player just left.
        /// </para>
        /// <para>
        /// Registered once, in <see cref="Bind"/>, on the only toolbar the process has, and never
        /// unregistered - the component lives as long as the process does, so an unregister would only
        /// be dead code. Two callbacks is also the shape the plugin's own wiring already uses on these
        /// same entries; <c>ConfigValue.RegisterCallback</c> forwards to an event
        /// (<c>Callbacks +=</c>), so neither clobbers the other. Verified in the pinned source,
        /// <c>Runtime/ReduxLib/Configuration/ConfigValue.cs</c> at <c>54bdefc</c>.
        /// </para>
        /// <para>
        /// A <c>null</c> entry (an unbound config) is skipped rather than dereferenced: the buttons
        /// would simply keep the state they were last given, which is the correct behaviour for a
        /// setting that cannot be written.
        /// </para>
        /// </remarks>
        private void BindModeChangeCallbacks()
        {
            ConfigValue<ConnectionsDisplayMode> connections = NetworkConfig.Connections;
            if (connections != null)
            {
                connections.RegisterCallback((from, to) => UpdateButtonState());
            }

            ConfigValue<RulersDisplayMode> rulers = NetworkConfig.Rulers;
            if (rulers != null)
            {
                rulers.RegisterCallback((from, to) => UpdateButtonState());
            }
        }

        /// <summary>
        /// Where the toolbar sits before the player moves it, in panel coordinates.
        /// </summary>
        /// <param name="size">The element's own size, as the library passes it.</param>
        /// <returns>The top-right corner area, the legacy's own placement.</returns>
        /// <remarks>
        /// An instance method, not a static one, because the live panel's width is the anchor:
        /// `PanelWidth` reads it off <c>_root</c>'s panel and falls back to the reference resolution
        /// when there is none. The arithmetic and the one assumption it makes about the coordinate
        /// space are documented on <c>UIScreenUtils.ToolbarDefaultPosition</c>.
        /// </remarks>
        private Vector2 DefaultPosition(Vector2 size)
        {
            return UIScreenUtils.ToolbarDefaultPosition(size, UIScreenUtils.PanelWidth(_root));
        }

        /// <summary>
        /// The position the window is given on its first real layout pass: the saved position when
        /// there is one, the computed default otherwise.
        /// </summary>
        /// <param name="size">The window's own laid-out size, as the library passes it.</param>
        /// <returns>The position to hand back to <c>SetDefaultPosition</c>.</returns>
        /// <remarks>
        /// <para>
        /// <b>Why this function, and not a write at bind time.</b> <c>Extensions.SetDefaultPosition</c>
        /// registers a <c>GeometryChangedEvent</c> handler that returns immediately while the
        /// element's rect has a zero side, then applies <c>style.position = Absolute</c>,
        /// <c>style.left</c>/<c>style.top</c> and <b>unregisters itself</b> (IL:
        /// <c>Extensions.GeometryChangedHandler</c> ends with
        /// <c>UnregisterCallback&lt;GeometryChangedEvent&gt;</c>). So the value is applied exactly
        /// once, on the first layout pass in which the window has a real size - which for this window
        /// is the first map view - and the size it passes in is the element's own. That instant is
        /// the only one where a clamp can know how much room the window needs, so the saved position
        /// is applied from here.
        /// </para>
        /// <para>
        /// <b>The library registers its own default too, and this one still wins.</b>
        /// <c>Window.SetupRootElement</c> calls <c>root.SetDefaultPosition(...)</c> itself when
        /// <c>IsMovingEnabled</c> and <c>CheckScreenBounds</c> are set, i.e. at <c>Window.Create</c>
        /// time - BEFORE the port's registration in <see cref="Bind"/>. Both handlers run in the same
        /// dispatch, in registration order, and the port's runs second, so the last write is this
        /// one's. That is measurable rather than assumed: L11's log has the toolbar at
        /// <c>left=1792.5 top=300</c> (this port's top-right placement) and never at the <c>0,0</c>
        /// the library's own callback would leave (it clamps <c>transform.position</c>, which this
        /// port never writes, so it resolves to the panel's origin).
        /// </para>
        /// <para>
        /// <b>The clamp is the game's own formula</b> - see <see cref="ClampToPanel"/> - and the
        /// baseline the exit comparison uses is taken from the value this function returns, because
        /// this is the moment the position becomes real.
        /// </para>
        /// </remarks>
        private Vector2 ResolveInitialPosition(Vector2 size)
        {
            Vector2 applied;
            if (NetworkConfig.TryGetSavedMapToolbarPosition(out double savedX, out double savedY))
            {
                Vector2 requested = new Vector2((float)savedX, (float)savedY);
                applied = ClampToPanel(requested, size);
                Write(_log, "ui-toolbar: position=applied from config " + NetworkConfig.MapSection
                    + "/" + NetworkConfig.MapToolbarXKey + "=" + savedX.ToString("0.##") + " "
                    + NetworkConfig.MapToolbarYKey + "=" + savedY.ToString("0.##")
                    + " requested=(" + requested.x.ToString("0.#") + "," + requested.y.ToString("0.#")
                    + ") -> applied=(" + applied.x.ToString("0.#") + ","
                    + applied.y.ToString("0.#") + ") size=" + size.x.ToString("0.#") + "x"
                    + size.y.ToString("0.#") + " panel="
                    + UIScreenUtils.PanelWidth(_root).ToString("0.#") + "x"
                    + UIScreenUtils.PanelHeight(_root).ToString("0.#") + " " + DescribePosition());
            }
            else
            {
                applied = DefaultPosition(size);
                Write(_log, "ui-toolbar: position=default (config " + NetworkConfig.MapSection
                    + "/" + NetworkConfig.MapToolbarXKey + "="
                    + NetworkConfig.MapToolbarXValue.ToString("0.##")
                    + " is the unset sentinel, so nothing has been saved) -> applied=("
                    + applied.x.ToString("0.#") + "," + applied.y.ToString("0.#") + ") size="
                    + size.x.ToString("0.#") + "x" + size.y.ToString("0.#") + " "
                    + DescribePosition());
            }

            _positionSettled = true;
            _positionAtShow = applied;
            return applied;
        }

        /// <summary>
        /// Clamps a position so the whole window stays inside the panel.
        /// </summary>
        /// <param name="position">The requested position, in panel pixels.</param>
        /// <param name="size">The window's own size, in panel pixels.</param>
        /// <returns>The closest position that keeps the window inside the panel.</returns>
        /// <remarks>
        /// The game's own formula, deliberately: <c>DragManipulator.OnPointerMove</c> clamps with
        /// <c>Mathf.Clamp(p, rect.xMin, rect.xMin + Mathf.Max(0, rect.width - size.x))</c> per axis,
        /// where <c>rect</c> is the panel's content rect and <c>size</c> is
        /// <c>worldBound.size</c>. A restored position is therefore kept inside exactly the region a
        /// dragged one is kept inside, and a value saved on a wider display cannot strand the window
        /// off the right or bottom edge of a narrower one. <c>UIScreenUtils.PanelWidth</c>/
        /// <see cref="UIScreenUtils.PanelHeight"/> read that same rect (and fall back to the
        /// reference resolution when the element has no panel yet), so "the panel" has one
        /// definition in this port.
        /// </remarks>
        private Vector2 ClampToPanel(Vector2 position, Vector2 size)
        {
            float maxX = Mathf.Max(0f, UIScreenUtils.PanelWidth(_root) - size.x);
            float maxY = Mathf.Max(0f, UIScreenUtils.PanelHeight(_root) - size.y);
            return new Vector2(Mathf.Clamp(position.x, 0f, maxX), Mathf.Clamp(position.y, 0f, maxY));
        }

        /// <summary>
        /// Records the position the map view opened at - the baseline the exit comparison uses.
        /// </summary>
        /// <remarks>
        /// Taken at ENTRY rather than carried over from the last write, because "the player moved it"
        /// is a question about one map session: a launch that never opens the map writes nothing, and
        /// a session that leaves the toolbar exactly where it found it writes nothing either. On the
        /// very first show the position is not settled yet - the library applies the
        /// default-or-saved position on the first layout pass, which has not run when the show
        /// message arrives - so the baseline comes from <see cref="ResolveInitialPosition"/> instead,
        /// and this method says so out loud rather than sampling a zero.
        /// </remarks>
        private void SamplePositionOnEntry()
        {
            if (!_positionSettled)
            {
                Write(_log, "ui-toolbar: position on entry not settled yet (the first layout pass - "
                    + "where the default or saved position is applied - has not run), so the baseline "
                    + "will be taken from that apply");
                return;
            }

            _positionAtShow = ReadLivePosition();
            Write(_log, "ui-toolbar: position on entry " + DescribePosition()
                + " (the baseline the map exit compares against)");
        }

        /// <summary>
        /// Writes the toolbar's position to the config when the map view closes - and only when it
        /// actually moved.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Never during a drag.</b> The port does not own the movement - the game's manipulator
        /// does - and a write per pointer move would rewrite the config file dozens of times per
        /// gesture. Sampling twice, at entry and at exit, turns "the player moved it" into a single
        /// comparison with a single write.
        /// </para>
        /// <para>
        /// <b>The threshold is half a pixel.</b> A drag moves the window in whole panel pixels, so
        /// any real movement exceeds it; the epsilon is there so a float read that differs in the
        /// last bit cannot be mistaken for a move.
        /// </para>
        /// </remarks>
        private void SavePositionIfMoved()
        {
            if (!_positionSettled)
            {
                Write(_log, "ui-toolbar: position not saved (the window never reached a real layout "
                    + "pass, so there is no position to compare)");
                return;
            }

            Vector2 exit = ReadLivePosition();
            float dx = Mathf.Abs(exit.x - _positionAtShow.x);
            float dy = Mathf.Abs(exit.y - _positionAtShow.y);
            if (dx <= PositionEpsilon && dy <= PositionEpsilon)
            {
                Write(_log, "ui-toolbar: position unchanged at (" + exit.x.ToString("0.#") + ","
                    + exit.y.ToString("0.#") + ") - nothing written to " + NetworkConfig.MapSection
                    + "/" + NetworkConfig.MapToolbarXKey + " " + DescribePosition());
                return;
            }

            if (NetworkConfig.SaveMapToolbarPosition(exit.x, exit.y))
            {
                Write(_log, "ui-toolbar: position moved (" + _positionAtShow.x.ToString("0.#") + ","
                    + _positionAtShow.y.ToString("0.#") + ") -> (" + exit.x.ToString("0.#") + ","
                    + exit.y.ToString("0.#") + ") and was saved to " + NetworkConfig.MapSection + "/"
                    + NetworkConfig.MapToolbarXKey + " + " + NetworkConfig.MapToolbarYKey
                    + ", so the next map view opens there " + DescribePosition());
            }
            else
            {
                Write(_warn, "ui-toolbar: the toolbar moved to (" + exit.x.ToString("0.#") + ","
                    + exit.y.ToString("0.#") + ") but the config entries are not bound, so it was "
                    + "NOT saved - the next map view will open at the previous position");
            }
        }

        /// <summary>
        /// Reads the inline <c>left</c>/<c>top</c> as a position, when both are real pixel lengths.
        /// </summary>
        /// <param name="position">The inline position, when there is one.</param>
        /// <returns><c>true</c> when both axes are inline pixel lengths.</returns>
        /// <remarks>
        /// Inline, because that is the field the game's own drag writes: <c>OnPointerMove</c> ends
        /// with <c>style.position = Absolute</c>, <c>style.left</c>, <c>style.top</c> and
        /// <c>transform.position = Vector3.zero</c> (IL, quoted on
        /// <c>NetworkConfig.MapToolbarX</c>). A keyword on either axis - <c>Null</c> is what an
        /// inline property that was never set reads back as - means the pair is not a position, and a
        /// percent length is not pixels; both fall back to the resolved layout, which is correct once
        /// a layout pass has run after the write.
        /// </remarks>
        private bool TryReadInlinePosition(out Vector2 position)
        {
            position = Vector2.zero;
            if (_root == null)
            {
                return false;
            }

            StyleLength left = _root.style.left;
            StyleLength top = _root.style.top;
            if (left.keyword != StyleKeyword.Undefined || top.keyword != StyleKeyword.Undefined)
            {
                return false;
            }

            Length x = left.value;
            Length y = top.value;
            if (x.unit != LengthUnit.Pixel || y.unit != LengthUnit.Pixel)
            {
                return false;
            }

            position = new Vector2(x.value, y.value);
            return true;
        }

        /// <summary>
        /// The live position for persistence: the inline style when it holds a pixel pair, the
        /// resolved layout otherwise.
        /// </summary>
        /// <returns>The window's current position, in panel pixels.</returns>
        private Vector2 ReadLivePosition()
        {
            return TryReadInlinePosition(out Vector2 inline) ? inline : Position;
        }

        /// <summary>
        /// Names every candidate carrier of the window's position, for the entry and exit lines that
        /// prove which field a drag actually moved.
        /// </summary>
        /// <returns>The inline style, the resolved layout and the world bounds, in one line.</returns>
        /// <remarks>
        /// Three carriers, printed together, because the question "where did the drag put the
        /// window?" is only answerable from the log if all three are visible in it: the inline style
        /// is what the game writes, the resolved layout is what the sheet proof has always printed,
        /// and <c>worldBound</c> is the physical answer a human can check against what they saw. The
        /// keyword is printed with the inline values so "never set" and "set to 0" are different
        /// facts in the log rather than the same number.
        /// </remarks>
        private string DescribePosition()
        {
            if (_root == null)
            {
                return "inline=n/a resolved=n/a worldBound=n/a";
            }

            StyleLength left = _root.style.left;
            StyleLength top = _root.style.top;
            Rect bound = _root.worldBound;
            return "inline=" + left.keyword + "(" + left.value.value.ToString("0.#") + ")/"
                + top.keyword + "(" + top.value.value.ToString("0.#") + ") resolved="
                + _root.resolvedStyle.left.ToString("0.#") + "/"
                + _root.resolvedStyle.top.ToString("0.#") + " worldBound="
                + bound.x.ToString("0.#") + "/" + bound.y.ToString("0.#");
        }

        /// <summary>Refreshes the two mode buttons and the report button from live state.</summary>
        /// <remarks>
        /// <para>
        /// The state comes from the CONFIG, which is the same value the renderer is driven from - so
        /// this method cannot disagree with the map, and the `Settings -> Mods` page cannot disagree
        /// with either. Both modes are read through their config keys' current values rather than
        /// through the renderer's properties, which is the whole of D39.
        /// </para>
        /// <para>
        /// Called on every show and from both config callbacks, so a mode changed in the settings UI
        /// re-tints the button even while the map is open.
        /// </para>
        /// </remarks>
        public void UpdateButtonState()
        {
            if (_root == null)
            {
                return;
            }

            // 1. The connection lines. Read through the accessor, not the entry: this method runs on
            //    window show, which for a launcher-created window can precede the config bind.
            ConnectionsDisplayMode connectionsMode = NetworkConfig.ConnectionsMode;
            if (_linesButton != null)
            {
                SetOneOfClasses(_linesButton, ClassFor(connectionsMode),
                    "toolbar__icon--comm-none", "toolbar__icon--comm-lines", "toolbar__icon--comm-active");
            }

            if (_linesTooltip != null)
            {
                _linesTooltip.TooltipText = DescribeMode(connectionsMode);
            }

            // 2. The range rulers.
            RulersDisplayMode rulersMode = NetworkConfig.RulersMode;
            if (_rulersButton != null)
            {
                SetOneOfClasses(_rulersButton, ClassFor(rulersMode),
                    "toolbar__icon--rulers-none", "toolbar__icon--rulers-relays",
                    "toolbar__icon--rulers-all");
            }

            if (_rulersTooltip != null)
            {
                _rulersTooltip.TooltipText = DescribeMode(rulersMode);
            }

            // 3. The vessel report's own tint, which says "this window is open" rather than "which
            //    mode". Read from the report's live state (`IsWindowOpen` is the conjunction of the
            //    window's intent and the display the window layer is actually applying, so the game's
            //    own hide key untints the button instead of leaving it lying).
            if (_vesselReportButton != null)
            {
                if (CommNextUIManager.IsVesselReportOpen)
                {
                    _vesselReportButton.AddToClassList("toggled");
                }
                else
                {
                    _vesselReportButton.RemoveFromClassList("toggled");
                }
            }
        }

        /// <summary>The lines button: advance the connection mode through the config.</summary>
        /// <remarks>
        /// <b>The write is the whole point of D39, and it is the only write.</b> The renderer is
        /// reached by the config's own change callback (<c>EnsureConnectionsModeWiring</c>), so there
        /// is exactly one path from a click to the map and it goes through the saved value. An
        /// unbound config - a launch where <c>Initialize</c> has not run - is refused out loud
        /// rather than dereferenced.
        /// </remarks>
        private void OnLinesClicked()
        {
            ConfigValue<ConnectionsDisplayMode> entry = NetworkConfig.Connections;
            if (entry == null)
            {
                Write(_warn, "ui-toolbar: the connections config entry is not bound, so this click "
                    + "cannot be saved - the mode was not changed");
                return;
            }

            ConnectionsDisplayMode next = entry.Value.Next();
            entry.Value = next;

            // Not redundant with the config callback: the callback fires in the same frame from the
            // config object, but this keeps the button correct even if a constraint ever refuses the
            // write, because it re-reads the value that the config actually holds.
            UpdateButtonState();
            Write(_log, "ui-toolbar: connections mode -> " + next + " (written to "
                + NetworkConfig.MapSection + " / " + NetworkConfig.ConnectionsModeKey
                + ", so Settings -> Mods and the next load agree)");
        }

        /// <summary>The rulers button: advance the ruler mode through the config.</summary>
        private void OnRulersClicked()
        {
            ConfigValue<RulersDisplayMode> entry = NetworkConfig.Rulers;
            if (entry == null)
            {
                Write(_warn, "ui-toolbar: the rulers config entry is not bound, so this click cannot "
                    + "be saved - the mode was not changed");
                return;
            }

            RulersDisplayMode next = entry.Value.Next();
            entry.Value = next;

            UpdateButtonState();
            Write(_log, "ui-toolbar: rulers mode -> " + next + " (written to "
                + NetworkConfig.MapSection + " / " + NetworkConfig.RulersModeKey
                + ", so Settings -> Mods and the next load agree)");
        }

        /// <summary>The vessel report button: open the report, or close it if it is already open.</summary>
        /// <remarks>
        /// <para>
        /// The legacy's <c>OnVesselReportClicked</c>, whose body read
        /// <c>MainUIManager.Instance.VesselReportWindow.Open()</c>. The port's route is the same in
        /// shape and goes through the window layer's own property, so this button never holds a
        /// reference to the window it is opening.
        /// </para>
        /// <para>
        /// <b>The read is the report's live state, not a cached flag</b> - D39's rule, and the reason
        /// this is a toggle rather than an open: the report can be closed by its own close button and
        /// by the map going away, and a cached "I opened it" would then turn the second click into a
        /// no-op. A launch where the report could not be created says so on the click rather than
        /// looking dead.
        /// </para>
        /// <para>
        /// <b>The tint is refreshed here rather than left to the window's own state change.</b> The
        /// map messages call <c>UpdateButtonState</c>, but nothing calls it when the report closes
        /// itself (the close button, or a vessel leaving the CommNet), so the click that closes it is
        /// what re-reads the state. Measured consequence at L10: the button carried no tint at all
        /// because the property was a constant; now every transition is followed by this one call.
        /// </para>
        /// </remarks>
        private void OnVesselReportClicked()
        {
            VesselReportWindowController report = CommNextUIManager.VesselReport;
            if (report == null)
            {
                Write(_warn, "ui-toolbar: the vessel report window was not created this launch, so this "
                    + "click cannot open it (the window layer logged the reason above - usually a "
                    + "template missing from the bundle)");
                return;
            }

            bool open = CommNextUIManager.IsVesselReportOpen;
            report.IsWindowOpen = !open;

            Write(_log, "ui-toolbar: vessel report " + (open ? "close" : "open") + " requested -> "
                + (report.IsWindowOpen ? "open" : "closed") + " (button "
                + (CommNextUIManager.IsVesselReportOpen ? "tinted" : "untinted") + ")");
        }

        /// <summary>
        /// Runs the stylesheet proof once, on the first layout pass with a real size.
        /// </summary>
        /// <param name="evt">The geometry change.</param>
        private void OnFirstGeometryChanged(GeometryChangedEvent evt)
        {
            if (_styleProofRun)
            {
                return;
            }

            // A hidden window lays out at zero: `display: none` means no geometry, and running the
            // proof there would read four defaults and call the sheet missing. Wait for a real one.
            if (evt.newRect.width <= 0f || evt.newRect.height <= 0f)
            {
                return;
            }

            _styleProofRun = true;
            _root.UnregisterCallback<GeometryChangedEvent>(OnFirstGeometryChanged);

            bool proved = UIStyleSheetProof.Prove(_root, "ui-styles-toolbar", _log, _warn);

            // The one line that ties the two facts together: which branch attached a sheet, and
            // whether that sheet is in effect. Logged as a warning until the read-back passes.
            // The position is here rather than in the bind line because `SetDefaultPosition` applies
            // it on this very layout pass - so this is the first moment the number exists.
            Write(proved ? _log : _warn, CommNextUIManager.Why(proved) + " [" + _root.name
                + " " + evt.newRect.width.ToString("0.#") + "x" + evt.newRect.height.ToString("0.#")
                + " at left=" + Position.x.ToString("0.#") + " top=" + Position.y.ToString("0.#")
                + ", panel " + UIScreenUtils.PanelWidth(_root).ToString("0.#") + " wide]");
        }

        /// <summary>The class name for one connection mode, as the sheet spells it.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The USS class.</returns>
        private static string ClassFor(ConnectionsDisplayMode mode)
        {
            switch (mode)
            {
                case ConnectionsDisplayMode.Lines:
                    return "toolbar__icon--comm-lines";
                case ConnectionsDisplayMode.Active:
                    return "toolbar__icon--comm-active";
                default:
                    return "toolbar__icon--comm-none";
            }
        }

        /// <summary>The class name for one ruler mode, as the sheet spells it.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The USS class.</returns>
        private static string ClassFor(RulersDisplayMode mode)
        {
            switch (mode)
            {
                case RulersDisplayMode.Relays:
                    return "toolbar__icon--rulers-relays";
                case RulersDisplayMode.All:
                    return "toolbar__icon--rulers-all";
                default:
                    return "toolbar__icon--rulers-none";
            }
        }

        /// <summary>Adds the mode's class and removes the two the mode is not.</summary>
        /// <param name="button">The button to re-class.</param>
        /// <param name="wanted">The class the live mode corresponds to.</param>
        /// <param name="all">Every class this button's modes can carry.</param>
        /// <remarks>
        /// All three class names are passed in rather than looked up, so this cannot add a class that
        /// belongs to the other button: the two buttons' classes differ only in their middle segment,
        /// and a cross-button typo would be invisible in a screenshot.
        /// </remarks>
        private static void SetOneOfClasses(Button button, string wanted, params string[] all)
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == wanted)
                {
                    button.AddToClassList(all[i]);
                }
                else
                {
                    button.RemoveFromClassList(all[i]);
                }
            }
        }

        /// <summary>
        /// Resolves a localization key to display text.
        /// </summary>
        /// <param name="key">The key, from <see cref="LocalizedStrings"/>.</param>
        /// <returns>The translated text, or the key itself when the table has no row for it.</returns>
        /// <remarks>
        /// The members of <see cref="LocalizedStrings"/> are `const string` KEYS, not display text -
        /// see that file's own header, which states the contract: "Where a translated value is
        /// actually needed the call site asks for it explicitly with
        /// <c>I2.Loc.LocalizationManager.GetTranslation(...)</c>". The two mode dropdowns honour it
        /// (<c>Data_NextModulator</c> translates its labels); this file did not, and the L10 launch
        /// measured the result - the tooltip rendered `CommNext/UI/ConnectionsDisplayModeLines`
        /// verbatim (F63).
        /// <para>
        /// Falling back to the key is deliberate. A missing row then shows a wrong-but-visible
        /// string instead of an empty tooltip, so the defect is reportable rather than silent.
        /// </para>
        /// </remarks>
        private static string Translate(string key)
        {
            string text = LocalizationManager.GetTranslation(key);
            return string.IsNullOrEmpty(text) ? key : text;
        }

        /// <summary>The localized tooltip text for a connection mode.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The current state, phrased as what the button will do.</returns>
        private static string DescribeMode(ConnectionsDisplayMode mode)
        {
            switch (mode)
            {
                case ConnectionsDisplayMode.Lines:
                    return Translate(LocalizedStrings.ConnectionsDisplayModeLines);
                case ConnectionsDisplayMode.Active:
                    return Translate(LocalizedStrings.ConnectionsDisplayModeActive);
                default:
                    return Translate(LocalizedStrings.ConnectionsDisplayModeNone);
            }
        }

        /// <summary>The localized tooltip text for a ruler mode.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns>The current state.</returns>
        private static string DescribeMode(RulersDisplayMode mode)
        {
            switch (mode)
            {
                case RulersDisplayMode.Relays:
                    return Translate(LocalizedStrings.RulersDisplayModeRelays);
                case RulersDisplayMode.All:
                    return Translate(LocalizedStrings.RulersDisplayModeAll);
                default:
                    return Translate(LocalizedStrings.RulersDisplayModeNone);
            }
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
