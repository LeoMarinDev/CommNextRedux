// CommNextRedux - the window layer's owner: opens the mod's UI bundle once and builds the two
// windows the map needs.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/MainUIManager.cs - a singleton that created the map
//   toolbar, the vessel report and the tooltip, in that order, and kept a UIDocument per window.
//   This is the same job with three changes, each forced by the 0.2.8.5 surface:
//
//     1. SpaceWarp-1's `AssetManager.GetAsset<VisualTreeAsset>(...)` does not exist. Assets come out
//        of the mod's own prebuilt AssetBundle - the route this port already uses for the ruler
//        mesh (`RulerGeometry`, P7) and the route the in-game-validated sibling port uses.
//     2. The legacy loaded its three UXMLs from the mod's folder through the loader. Here the
//        bundle is opened with `AssetBundle.LoadFromFile(SWMetadata.Folder.FullName + <path>)`.
//     3. There is no vessel report window yet. P8a ships two windows; the third arrives in P8b, and
//        the seam for it is `IsVesselReportOpen` below rather than a half-built window.
//
// THE WINDOW ROOT IS A PanelRenderer (the pinned delta - INVERTED at 0.2.9.0.104521)
//   `Window.Create(WindowOptions, VisualTreeAsset)` returns a `PanelRenderer` in this runtime -
//   measured in the installed `UitkForKsp2.dll` (both `Window.Create` overloads return it, and
//   `VisualElement GetWindowRoot(PanelRenderer)` is mlist 230 of that assembly), with
//   `UnityEngine.UIElements.PanelRenderer` itself typedef 857 of `UnityEngine.UIElementsModule.dll`.
//   The content root is then FETCHED, not read off a document:
//   `UitkForKsp2.API.Extensions.GetWindowRoot(renderer)` resolves the window's root out of the
//   clone's template containers (`Extensions::GetWindowRoot` -> `Window::ResolveWindowRoot`, read by
//   `ikdasm` on the installed DLL) - the element the 0.2.8.5 shape addressed as
//   `document.rootVisualElement[0]`, which is why the controllers no longer index `[0]`.
//   `UIDocument` is previous-pin source: the build script now REFUSES a `UIDocument` typeref in the
//   built assembly for exactly that reason.
//
// THE BUNDLE LIFETIME - A DELIBERATE, RECORDED CHOICE  (D37)
//   This class opens the bundle a second time and NEVER unloads that handle. P7's `RulerGeometry`
//   opens its own handle, extracts the sphere mesh, and calls `Unload(false)`; that is left exactly
//   as it is. Two `LoadFromFile` calls on one path are independent handles, and the ordering here
//   makes the two provably disjoint rather than merely likely to be: `EnsureRenderer` (which runs
//   `RulerGeometry.Resolve`, and therefore P7's load-and-unload) is called by the plugin BEFORE
//   `EnsureUI`, so P7's handle is already released by the time this one is opened. The UI handle is
//   held for the process lifetime because a `VisualTreeAsset` and a `StyleSheet` must stay
//   deserialised for as long as the player can open a window - the whole session.
//
// THE STYLESHEET - ATTACH IS NOT PROOF, AND THE TWO ARE SEPARATE FACTS
//   `EnsureStylesAttached` makes a sheet present and names the branch that did it (the template's
//   own `project://` reference, or the bundle's copy attached at runtime). `UIStyleSheetProof` then
//   reads resolved values back off the live element and says whether the sheet is actually *in
//   effect*. Only the second is the phase gate: a sheet in a `styleSheets` list can have every rule
//   overridden, and a `project://` reference can resolve to a sheet whose image URLs did not. So the
//   attach line is logged as unproven until the read-back passes, and `Why(proved)` is what turns
//   the pair into one honest sentence.
//
// WHY THE TOOLTIP IS CREATED LAST, AND WHY CREATION ORDER IS NO LONGER THE WHOLE STORY
//   The tooltip is drawn on top of everything else, and the legacy says so in its own comment
//   ("should be created last"); this keeps that order literally. Creation order was the WHOLE story
//   until P9.1: each WINDOW gets its own `PanelSettings` (a per-window clone - `Window.Create` ->
//   `PanelFactory.CreateForWindow` -> `UnityEngine.Object.Instantiate<PanelSettings>`), and the
//   document's position among the other panels is a `sortingOrder` the game's own UI also competes
//   for. The port measured the consequence as F75 - a toolbar dragged to the extreme top-right drew
//   UNDER the game's own map HUD - and fixes it in `SyncZOrder` by taking the library's own
//   `OrderManager` route (Register, then BringToFront, toolbar -> report -> tooltip). Creation order
//   is what selects the ORDER of the three calls there; it is no longer the only thing that gives
//   these windows a layer.
//
// AND WHY THAT RAISE STANDS DOWN FOR THE GAME'S OWN PAUSE MENU  (P9.2, F79)
//   A raise that wins against the game's HUD also wins against the game's ESC/pause menu, which is
//   exactly what L14 reported: the pass had run the panels to 1007-1023 and the toolbar and vessel
//   report then drew OVER the menu. So the raise is conditional - `SetPauseSuppressed` holds the
//   three panels at the value the library clone carried before P9.1 (-1, the number every one of
//   them read back as in L10-L13, the launches in which the menu correctly drew on top) while the
//   menu is open, and the same pass is re-run the moment it closes. The suppression deliberately
//   routes AROUND visibility: hiding the toolbar would call `SavePositionIfMoved` (a config write
//   caused by opening a menu, against D53/F74) and hiding the report would lose the player's intent
//   to have it open (its getter ANDs that intent with the live display state, so the toolbar's
//   report button would also mistint). Only `PanelSettings.sortingOrder` is touched.
//
// AND WHY EACH WINDOW'S PANEL IS ALSO MOVED TO UNITY'S UI LAYER  (P9.3, F82)
//   The z-order above decides which window draws on top; it does not decide whether the game's own
//   click handling *notices* the window. That is a separate test, and the player reported it failing:
//   with a trajectory drawn underneath the vessel report, a click on the report also created a
//   maneuver node. `KSP.Map.Map3DManeuvers.IsOtherUIHovered()` - the one gate behind the map's
//   `UpdateManeuverDetection`, `TryHandleScroll` and `ShouldConsumeScroll` - asks the UGUI event system
//   for its raycast hits and returns true only for a hit whose `gameObject.layer` is 5 (unless it is
//   tagged `OrbitalUIElement`). A UI Toolkit window reaches that raycast through its `PanelRaycaster`,
//   whose hit's `gameObject` is `PanelSettings.panel.selectableGameObject` - a GameObject the UGUI
//   interoperability bridge creates and which defaults to layer 0. So each window's panel GameObject is
//   put on layer 5 here, through `PanelLayerFix`, which retries until the panel exists and logs what it
//   found. `WindowOptions.BlockGameInput` is NOT the mechanism for this and is not enabled on any of
//   the three windows: it sets KSP2 input definitions, and the maneuver path polls the legacy
//   `UnityEngine.Input` and never reads them. The report carried it and still leaked the click (the
//   measurement P9.3 was decided on); P9.4 removed it again (D62, F84) because the wheel case it was
//   added for is now the layer's, and holding it costs all eight of those input locks while the
//   pointer merely hovers the window. P9.5 keeps it off and adds `PanelInputBlocker` (D65), which is
//   the action-granular answer to the same requirement: the map view's five mouse camera actions are
//   held down while the pointer is over a window, WASD and every global key are left alone.
//
// WHAT THIS CLASS DOES NOT DO
//
// WHAT THIS CLASS DOES NOT DO
//   It does not create, show or hide anything on its own initiative: the toolbar is opened by the
//   map messages (`EventListener`), and the tooltip is driven by hover (`TooltipManipulator`).

using System;
using System.IO;
using CommNextRedux.UI.Controls;
using CommNextRedux.UI.Tooltip;
using CommNextRedux.UI.Utils;
using UitkForKsp2.API;
using UitkForKsp2.API.Order;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI
{
    /// <summary>
    /// Builds and owns the mod's two windows, and the AssetBundle they are cloned from.
    /// </summary>
    /// <remarks>
    /// Static, like the port's renderer and event listener: there is one process, one panel and one
    /// set of windows, and a second instance could only be a second way to disagree about which
    /// document is live.
    /// </remarks>
    public static class CommNextUIManager
    {
        /// <summary>The deployed bundle's path, relative to the mod's own folder.</summary>
        /// <remarks>
        /// The same literal P7 uses for the ruler mesh, and deliberately duplicated rather than
        /// shared: sharing it would mean one class reaching into the other's constant, and the two
        /// loads are independent by design (see the file header).
        /// </remarks>
        public const string BundleRelativePath = "/assets/bundles/commnextredux_ui.bundle";

        /// <summary>The map toolbar's template, as the bundle names it.</summary>
        /// <remarks>
        /// Read from the bundle audit rather than guessed: `Deploy/obj/bundle-audit.log` lists the
        /// container names of the deployed bundle, and this is line-for-line one of them. Bundle
        /// container names are lower-case and case-sensitive on this platform.
        /// </remarks>
        public const string ToolbarUxmlPath = "assets/commnextredux/ui/cn_ui.uxml";

        /// <summary>The tooltip's template, as the bundle names it.</summary>
        public const string TooltipUxmlPath = "assets/commnextredux/ui/tooltipwindow.uxml";

        /// <summary>The vessel report's template, as the bundle names it.</summary>
        public const string VesselReportUxmlPath = "assets/commnextredux/ui/vesselreportwindow.uxml";

        /// <summary>The stylesheet, as the bundle names it.</summary>
        public const string StylesAssetPath = "assets/commnextredux/ui/commnextstyles.uss";

        /// <summary>The stylesheet asset's own name, used to recognise an already-attached copy.</summary>
        public const string StylesSheetName = "CommNextStyles";

        private static AssetBundle _bundle;
        private static StyleSheet _styles;
        private static bool _initialized;

        /// <summary>Whether <see cref="Initialize"/> has run to the point of creating the windows.</summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>The Info sink the z-order pass writes through, captured in <see cref="Initialize"/>.</summary>
        /// <remarks>
        /// Held rather than reached for through <c>CommNextReduxPlugin.Instance</c> so the pass can
        /// run from any caller after a successful init (the toolbar's map-entry transition is one)
        /// without this class depending on the plugin's own property being live.
        /// </remarks>
        private static Action<string> _zOrderLog;

        /// <summary>How many <see cref="SyncZOrder"/> passes have run this launch.</summary>
        /// <remarks>
        /// An ordinal, not a count of "new" work: the pass bumps every panel it finds, so the number
        /// is there to make the log lines self-identifying (the initialization pass is always 1).
        /// </remarks>
        private static int _zOrderPass;

        /// <summary>Whether the last <see cref="SyncMapView"/> call saw a live map view.</summary>
        /// <remarks>
        /// The transition latch the z-order re-apply hangs off: <see cref="SyncMapView"/> runs every
        /// frame, and this is what turns "each entry into the map" into one comparison instead of a
        /// per-frame pass.
        /// </remarks>
        private static bool _zOrderWasInMap;

        /// <summary>The sorting order the library's per-window panel clone carries before any raise.</summary>
        /// <remarks>
        /// <b>A measured value, not a "low number".</b> Every panel of this mod read back as
        /// <c>-1</c> in L10, L11, L12 and L13 - the launches in which the game's own ESC menu
        /// correctly drew <i>over</i> these windows - and the first L14 pass printed <c>-1</c> as its
        /// "before" value. It is therefore the state the player has already verified as the correct
        /// one for "beneath the game's own UI", and restoring it restores exactly that.
        /// </remarks>
        private const float PausedSortingOrder = -1f;

        /// <summary>Whether the game's own pause/ESC menu is open, so the raise must stand down.</summary>
        /// <remarks>
        /// Written only by <see cref="SetPauseSuppressed"/>, and read only by <see cref="SyncZOrder"/>
        /// (as the guard that stops a map-entry pass raising the panels back over an open menu) and by
        /// the idempotence check at the top of the setter.
        /// </remarks>
        private static bool _pauseSuppressed;

        /// <summary>The map toolbar window, or <c>null</c> when it could not be built.</summary>
        public static MapToolbarWindowController Toolbar { get; private set; }

        /// <summary>The tooltip window, or <c>null</c> when it could not be built.</summary>
        public static TooltipWindowController Tooltip { get; private set; }

        /// <summary>The vessel report window, or <c>null</c> when it could not be built.</summary>
        /// <remarks>
        /// Created BETWEEN the toolbar and the tooltip, and that creation order still decides the
        /// relative stacking: <see cref="SyncZOrder"/> re-applies the panel order on every map entry
        /// by walking the three documents in creation order, so the report stays above the toolbar
        /// that opened it and the tooltip - which the toolbar's own buttons raise - stays above the
        /// report, exactly as P8a established. <c>BringToFrontOnPointerDown</c> is still left at its
        /// default for the same reason it always was - a report that pushed itself to the front on
        /// every click would cover the tooltip the toolbar's buttons are showing.
        /// </remarks>
        public static VesselReportWindowController VesselReport { get; private set; }

        /// <summary>How the bundle was resolved: <c>loaded</c>, <c>absent</c> or <c>failed</c>.</summary>
        public static string BundleRoute { get; private set; }

        /// <summary>
        /// Whether a <c>CommNextStyles</c> sheet is attached to either window (not, on its own, whether
        /// it is in effect - that is <see cref="UIStyleSheetProof"/>).
        /// </summary>
        /// <remarks>
        /// Kept separate from <see cref="SheetRoute"/> because the two answer different questions and
        /// the weaker one must never be logged as the stronger: this says a sheet object is in a
        /// `styleSheets` list somewhere on the window's ancestor chain; the proof says the element
        /// resolved the sheet's own values.
        /// </remarks>
        public static bool StylesAttached
        {
            get { return SheetAttached; }
        }

        /// <summary>Whether the last attach call put or found a sheet.</summary>
        private static bool SheetAttached;

        /// <summary>The reason no sheet is in effect, when that is what happened.</summary>
        private static string SheetAttachWarn;

        /// <summary>
        /// Whether the vessel report window is open. Always <c>false</c> before the report exists.
        /// </summary>
        /// <remarks>
        /// <b>P8b's seam, now live.</b> The legacy's toolbar tints its report button from
        /// `MainUIManager.Instance.VesselReportWindow.IsWindowOpen`; the body below is that same
        /// read, routed through the property rather than a direct dereference so the toolbar's call
        /// site cannot be the thing that throws on a launch where the window layer came up without
        /// the report. The report's own getter answers with both its intent and the window layer's
        /// display state, so a report the player hid with the game's hide key untints the button
        /// rather than claiming to be open.
        /// </remarks>
        public static bool IsVesselReportOpen
        {
            get { return VesselReport != null && VesselReport.IsWindowOpen; }
        }

        /// <summary>
        /// Whether the "no toolbar to show" line has been written this launch.
        /// </summary>
        /// <remarks>
        /// One line, not one per map transition: a launch whose bundle is missing would otherwise
        /// re-print the same sentence every time the player opens the map, and a repeated warning is
        /// read as a new failure by whoever is grepping.
        /// </remarks>
        private static bool _noToolbarLogged;

        /// <summary>
        /// Shows or hides the toolbar for the current map state, and does nothing when it already
        /// agrees.
        /// </summary>
        /// <param name="inMapView">Whether a map view is alive.</param>
        /// <returns><c>true</c> when a toolbar exists and holds that state after this call.</returns>
        /// <remarks>
        /// <para>
        /// <b>One entry point, two callers, and neither is critical on its own.</b> The map messages
        /// (<c>EventListener</c>) call this the instant a transition happens, so the toolbar appears in
        /// the same frame the map does; the plugin's <c>Update</c> calls it every frame, and the
        /// comparison below is what makes that free. The duplication is the point: a save load
        /// replaces the <c>MessageCenter</c> instance and orphans every handler registered on the old
        /// one, which is a measured trap with no log line (dev guide 61.6), so a message-only design
        /// would leave a permanently missing toolbar the second time a save is loaded. The poll is the
        /// self-healing half of the same fix <c>EventListener</c> applies to its subscriptions.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> One caller is a message-bus handler - an exception thrown out of it is
        /// logged by the dispatcher and abandons the rest of that handler's work, and nothing about a
        /// missing window should cost the renderer its listener.
        /// </para>
        /// <para>
        /// The transition itself is logged by <see cref="MapToolbarWindowController.IsWindowOpen"/>'s
        /// setter, once per real change; this method deliberately adds no line of its own except the
        /// one-per-launch "there is no toolbar" case.
        /// </para>
        /// </remarks>
        public static bool SyncMapView(bool inMapView)
        {
            MapToolbarWindowController toolbar = Toolbar;
            if (toolbar == null)
            {
                if (!_noToolbarLogged)
                {
                    _noToolbarLogged = true;
                    CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
                    if (plugin != null)
                    {
                        plugin.LogWarningLine("ui: there is no toolbar window, so the map view's "
                            + "visibility cannot be applied to it - the window layer did not come up "
                            + "this launch (see the ui: error above for the reason)");
                    }
                }

                return false;
            }

            if (toolbar.IsWindowOpen != inMapView)
            {
                toolbar.IsWindowOpen = inMapView;
            }

            // 2. The z-order pass, ONCE PER MAP SESSION. A save load replaces the MessageCenter
            //    instance, and a map re-entry can arrive with the game's own HUD panels above ours,
            //    so the ordering is re-applied at each transition INTO the map rather than only at
            //    init. The `_zOrderWasInMap` latch is what makes this free on the per-frame poll:
            //    it is a bool comparison on a path that already compares the toolbar's state, and it
            //    writes no config and allocates nothing.
            if (inMapView != _zOrderWasInMap)
            {
                _zOrderWasInMap = inMapView;
                if (inMapView)
                {
                    SyncZOrder();
                }
                else
                {
                    // P9.5 (D65): the map is gone, so nothing of this mod's may still be holding the
                    // map's actions down. This is the one release path that is reached even when the
                    // windows' own elements were torn down before their per-element polls could run
                    // (each window's poll is paused once its element leaves the panel), and it reads
                    // every action back - whether that window was holding or not - so a launch that
                    // ends here cannot leave a silently disabled action behind.
                    PanelInputBlocker.ReleaseAll("the map view ended");
                }
            }

            // The report is a map window as well, and it is the window that exists ONCE - created with
            // the toolbar and selected by the dialog manager, never re-created per open. Left alone at a
            // map teardown it would sit there as live UI over the KSC screen whose every action needs a
            // map marker. So it is closed here, by the same authority that shows and hides the toolbar.
            // The legacy closed it from its own `MapViewClosed` path for the same reason; routing it
            // through the one state-applying method keeps the two windows from disagreeing about whether
            // the map is up.
            VesselReportWindowController report = VesselReport;
            if (report != null && report.IsWindowOpen && !inMapView)
            {
                report.IsWindowOpen = false;
                CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
                if (plugin != null)
                {
                    plugin.LogLine("ui: the vessel report was closed because the map view is gone - it "
                        + "reports on map markers, so it does not exist outside it (re-open it with the "
                        + "toolbar's third button)");
                }
            }

            return toolbar.IsWindowOpen == inMapView;
        }

        /// <summary>
        /// Puts this mod's three panels back above the game's own UI, keeping their mutual order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>F75, the defect this exists for.</b> A toolbar dragged to the extreme top-right drew
        /// <i>behind</i> the game's own map HUD panels (GFORCE / CONSUMABLES). Nothing about a
        /// UitkForKsp2 window's place in the screen pool is decided by its creation order once the
        /// game's own UI is in play - each window gets its own cloned <c>PanelSettings</c>, and the
        /// visible layer is that panel's <c>sortingOrder</c>, which the game's UI competes for too
        /// (measured: the game's own assembly calls <c>OrderManager.Register</c> /
        /// <c>BringToFront</c> / <c>Unregister</c>).
        /// </para>
        /// <para>
        /// <b>The route is the library's own ordering pass, in its own call order.</b>
        /// <c>OrderManager.Register(panel)</c> assigns the next monotonic <c>sortingOrder</c> and
        /// records the panel; <c>OrderManager.BringToFront(panel)</c> assigns the next one again - but
        /// <b>only for a panel that is already registered</b>, so a <c>BringToFront</c> without a
        /// <c>Register</c> is a silent no-op. The toolbar's own <c>WindowOptions</c> leave
        /// <c>BringToFrontOnPointerDown</c> at its zero-initialised default, so the library adds no
        /// <c>OrderManipulator</c> and nothing has registered these panels at this point. This call is
        /// therefore the registration, and it runs on every pass (a re-entry into the map after a
        /// panel was somehow unregistered would otherwise silently do nothing).
        /// </para>
        /// <para>
        /// <b>Why toolbar, then report, then tooltip, in that order.</b> Each successful call takes
        /// the next integer, so the sequence is what establishes the stacking: the report ends above
        /// the toolbar that opened it, and the tooltip - which the toolbar's own buttons raise - ends
        /// above both. That is P8a's deliberate order, and walking the documents in creation order is
        /// the mechanism that preserves it: a single call for the toolbar alone would put the toolbar
        /// over the report and the tooltip and break it.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> One caller is the per-frame <c>Update</c> poll's transition branch
        /// (through <see cref="SyncMapView"/>), and a thrown exception there would cost the poll that
        /// keeps the toolbar self-healing; so every step is guarded and a missing document is skipped
        /// rather than dereferenced.
        /// </para>
        /// <para>
        /// <b>What the log line does not claim.</b> This pin exposes no registered-panel count
        /// (<c>OrderManager.Known</c> is a private <c>HashSet&lt;object&gt;</c>; <c>monodis --method</c>
        /// finds only <c>Register</c> / <c>BringToFront</c> / <c>Unregister</c> over
        /// <c>PanelSettings</c> and <c>Canvas</c>, plus <c>Next</c>), so each line reports
        /// <c>registered=true</c> as the call it made and the read-back of the panel's own
        /// <c>sortingOrder</c> as the evidence that the call did something.
        /// </para>
        /// </remarks>
        public static void SyncZOrder()
        {
            // Captured up front so a pass that starts cannot be split by a later null check.
            Action<string> log = _zOrderLog;

            // The stand-down (P9.2, F79). Raising the panels while the game's own pause/ESC menu is
            // open is exactly the L14 defect: 1007-1023 put the toolbar and the vessel report OVER
            // the menu. Skipped rather than silently returned from, because a reader grepping for
            // "ui: z-order" must be able to see that a pass was asked for and stood down. Nothing is
            // lost by returning: SetPauseSuppressed(false) re-runs this method the moment the menu
            // closes, so the raise happens then with fresh numbers.
            if (_pauseSuppressed)
            {
                Write(log, "ui: z-order - pass skipped: the game's own pause/ESC menu is open, so the "
                    + "panels stay at " + Format(PausedSortingOrder) + " (the library clone's value) "
                    + "until it closes - raising them now would put them over the menu");
                return;
            }

            int pass = ++_zOrderPass;
            PanelRenderer toolbar = ReadRenderer(Toolbar);
            PanelRenderer report = ReadRenderer(VesselReport);
            PanelRenderer tooltip = ReadRenderer(Tooltip);

            if (!TryApplyZOrder(toolbar, log, pass, "toolbar"))
            {
                return;
            }

            if (!TryApplyZOrder(report, log, pass, "vessel report"))
            {
                return;
            }

            if (!TryApplyZOrder(tooltip, log, pass, "tooltip"))
            {
                return;
            }

            // The mutual order, as one line a grep can settle. The value is the CREATION-ORDER index
            // of each window's resulting sorting order, read back off the live panels: "1<2<3" is
            // P8a's ordering, and any other value means the fix has re-stacked the mod's own windows.
            string order = ZOrderSummary(toolbar, report, tooltip);
            Write(log, "ui: z-order - pass " + pass + " mutual order " + order + " (1<2<3 means toolbar"
                + " < vessel report < tooltip; that is the P8a ordering this pass must preserve)");
        }

        /// <summary>
        /// Holds the three panels at the library clone's sorting order while the game's own pause/ESC
        /// menu is open, and re-runs the raise when it closes.
        /// </summary>
        /// <param name="suppressed">Whether that menu is open.</param>
        /// <remarks>
        /// <para>
        /// <b>F79, the defect this exists for.</b> The raise is correct only while the game is not
        /// showing its own pause UI. At L14 the pass had run the panels to 1007-1023 and the player's
        /// report is that the toolbar and the vessel report then drew <i>over</i> the ESC menu. The
        /// value written here is not a new guess — <c>-1</c> is what every one of these panels read
        /// back as in L10-L13, the launches in which the menu correctly drew on top, and what L14's
        /// own first pass printed as its "before" number.
        /// </para>
        /// <para>
        /// <b>Why the suppression routes around visibility.</b> Hiding the windows would have to go
        /// through <c>IsWindowOpen</c>, and both setters have side effects this feature must not
        /// cause: the toolbar's setter calls <c>SavePositionIfMoved</c> on a write to <c>false</c>
        /// (a config write produced by opening a menu — against D53/F74, which say the position is
        /// written once, on map exit, only if it moved), and the report's getter ANDs the player's
        /// intent with the live display state, so hiding it would both mistint the toolbar's report
        /// button and lose the intent to have the report open. So this method writes
        /// <c>PanelSettings.sortingOrder</c> and touches nothing else: not visibility, not intent,
        /// not the position baseline.
        /// </para>
        /// <para>
        /// <b>Idempotent by construction</b>, because the handlers that call it run inside a
        /// message-bus dispatch and can arrive in bursts: a repeated <c>true</c> (or <c>false</c>)
        /// does no work and writes no line at all, so each state change produces exactly one set of
        /// lines.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> One caller is a message-bus handler; the guard lives in that caller
        /// (<c>EventListener.ApplyEscapeMenuSuppression</c>), and every step here is a null check plus
        /// a field write.
        /// </para>
        /// </remarks>
        public static void SetPauseSuppressed(bool suppressed)
        {
            if (_pauseSuppressed == suppressed)
            {
                return;
            }

            _pauseSuppressed = suppressed;
            Action<string> log = _zOrderLog;

            if (suppressed)
            {
                SuppressZOrder(log);
                return;
            }

            // Un-suppressing is the ONLY route back up: this is the same pass a map entry runs, not a
            // second raise path, so the panels take fresh monotonic orders in creation order and the
            // mutual `1<2<3` is re-established and re-logged.
            Write(log, "ui: z-order - the game's own menu closed, so the raise is re-run");
            SyncZOrder();
        }

        /// <summary>Lowers the three panels, in creation order, naming the value each one carried.</summary>
        /// <param name="log">The Info sink, or <c>null</c>.</param>
        /// <remarks>
        /// <c>OrderManager.Unregister</c> is called alongside each write because it is what "this panel
        /// is not in the ordering any more" means, and it keeps the library's own bookkeeping honest
        /// while the panel sits at the clone's value. Measured in the shipped <c>UitkForKsp2.dll</c>,
        /// it is a null check plus one <c>HashSet.Remove</c> and nothing else — it does <b>not</b> write
        /// <c>sortingOrder</c> — so it is harmless on a panel that was never registered and cannot undo
        /// the write below.
        /// </remarks>
        private static void SuppressZOrder(Action<string> log)
        {
            int lowered = 0;
            if (SuppressOne(ReadRenderer(Toolbar), log, "toolbar"))
            {
                lowered++;
            }

            if (SuppressOne(ReadRenderer(VesselReport), log, "vessel report"))
            {
                lowered++;
            }

            if (SuppressOne(ReadRenderer(Tooltip), log, "tooltip"))
            {
                lowered++;
            }

            if (lowered == 0)
            {
                // The menu can legitimately open before OnInitialized has built the windows. The flag
                // is already set, so the pass that Initialize would otherwise run is skipped and the
                // raise happens when the menu closes - which is why this is a note, not a failure.
                Write(log, "ui: z-order - the game's own menu opened before the window layer had a "
                    + "panel, so there was nothing to lower; the raise stays suppressed until it "
                    + "closes");
            }
        }

        /// <summary>One panel's half of the stand-down, logged as before -&gt; after.</summary>
        /// <param name="renderer">The window's renderer, or <c>null</c> when the window does not exist.</param>
        /// <param name="log">The Info sink, or <c>null</c>.</param>
        /// <param name="what">The window's name, for the log.</param>
        /// <returns><c>true</c> when a panel was found and lowered.</returns>
        /// <remarks>
        /// The line reports the value that was <i>written</i> (read straight back off the panel), not
        /// the constant, so a write that did not take is visible in the log rather than asserted away.
        /// <c>PanelRenderer.panelSettings</c> is the same per-window clone the 0.2.8.5 shape's
        /// <c>UIDocument.panelSettings</c> pointed at (mlist 6142 of
        /// <c>UnityEngine.UIElementsModule.dll</c>).
        /// </remarks>
        private static bool SuppressOne(PanelRenderer renderer, Action<string> log, string what)
        {
            if (renderer == null)
            {
                return false;
            }

            PanelSettings panel = renderer.panelSettings;
            if (panel == null)
            {
                return false;
            }

            float before = panel.sortingOrder;
            OrderManager.Unregister(panel);
            panel.sortingOrder = PausedSortingOrder;

            Write(log, "ui: z-order - the game's own menu opened: " + what + " sortingOrder "
                + Format(before) + " -> " + Format(panel.sortingOrder) + " (panel=" + PanelName(panel)
                + ")");
            return true;
        }

        /// <summary>
        /// One window's half of a z-order pass: register its panel, raise it, and log the numbers.
        /// </summary>
        /// <param name="renderer">The window's renderer, or <c>null</c> when that window is absent.</param>
        /// <param name="log">The Info sink, or <c>null</c>.</param>
        /// <param name="pass">The pass ordinal, so a re-apply's line is self-identifying.</param>
        /// <param name="what">The window's name, for the log.</param>
        /// <returns><c>false</c> when the window is absent, so the caller stops before it can raise a
        /// later window over an earlier one out of creation order.</returns>
        /// <remarks>
        /// Returns rather than throws on every failure, and the bool is about ORDER, not about
        /// success: a missing middle window must leave the remaining ones unraised, because raising
        /// the tooltip without the report having been raised is exactly the re-stacking this pass
        /// exists to prevent. A panel that is missing while the renderer exists is logged and counted
        /// as applied, because there is nothing further to do about it here.
        /// </remarks>
        private static bool TryApplyZOrder(PanelRenderer renderer, Action<string> log, int pass, string what)
        {
            if (renderer == null)
            {
                Write(log, "ui: z-order - pass " + pass + " " + what + " skipped: this window does not"
                    + " exist this launch (its create call failed earlier - see the ui: lines above)");
                return false;
            }

            PanelSettings panel = renderer.panelSettings;
            if (panel == null)
            {
                Write(log, "ui: z-order - pass " + pass + " " + what + " skipped: the renderer has no"
                    + " PanelSettings, so there is no sortingOrder to set on it");
                return true;
            }

            string name = PanelName(panel);
            float before = panel.sortingOrder;
            OrderManager.Register(panel);
            OrderManager.BringToFront(panel);
            float after = panel.sortingOrder;

            Write(log, "ui: z-order - pass " + pass + " " + what + " sortingOrder " + Format(before)
                + " -> " + Format(after) + " (registered=true panel=" + name + ")");
            return true;
        }

        /// <summary>The panel renderer behind a controller, or <c>null</c> when there is no controller.</summary>
        /// <remarks>
        /// The renderer is read back off the component rather than remembered from the create call, so
        /// a window whose create failed surfaces here as a <c>null</c> instead of as a stale field.
        /// </remarks>
        private static PanelRenderer ReadRenderer(Component window)
        {
            return window == null ? null : window.GetComponent<PanelRenderer>();
        }

        /// <summary>A panel's name, with a readable stand-in for the unnamed case.</summary>
        /// <remarks>
        /// The library names every panel it creates <c>PS[Scaled]::&lt;WindowId&gt;</c> (or
        /// <c>PS[Fixed]::</c>), so a name here is also a check that the panel is one this mod
        /// created. A null name is legal on a UnityEngine.Object and is printed rather than thrown on.
        /// </remarks>
        private static string PanelName(PanelSettings panel)
        {
            string name = panel.name;
            return name == null ? "(unnamed panel)" : name;
        }

        /// <summary>The panel's sorting order, formatted the way the rest of the port's numbers are.</summary>
        private static string Format(float order)
        {
            // A float with an integer's meaning - PanelSettings.sortingOrder is a float32 in this
            // runtime - so the decimal is noise in a log line whose whole job is to be compared.
            return order.ToString("0.#");
        }

        /// <summary>
        /// The three panels' creation-order indices as <c>i&lt;j&lt;k</c>, or a word saying why not.
        /// </summary>
        /// <remarks>
        /// Computed from the live <c>sortingOrder</c> values rather than from what the pass asked for,
        /// which is the point: this is the read-back that makes the logging a measurement instead of
        /// a claim about a call that a silent no-op would also have made.
        /// </remarks>
        private static string ZOrderSummary(PanelRenderer toolbar, PanelRenderer report, PanelRenderer tooltip)
        {
            PanelSettings toolbarPanel = toolbar == null ? null : toolbar.panelSettings;
            PanelSettings reportPanel = report == null ? null : report.panelSettings;
            PanelSettings tooltipPanel = tooltip == null ? null : tooltip.panelSettings;
            if (toolbarPanel == null || reportPanel == null || tooltipPanel == null)
            {
                return "not computable (a window is absent or its panel is null)";
            }

            float[] orders =
            {
                toolbarPanel.sortingOrder,
                reportPanel.sortingOrder,
                tooltipPanel.sortingOrder
            };

            int first = Rank(orders, 0);
            int second = Rank(orders, 1);
            int third = Rank(orders, 2);
            string result = first + (first < second ? "<" : ">=") + second
                + (second < third ? "<" : ">=") + third;

            return result + " (" + Format(orders[0]) + " / " + Format(orders[1]) + " / "
                + Format(orders[2]) + " for toolbar / report / tooltip)";
        }

        /// <summary>
        /// The 1-based position of one entry in the three orders, smallest first.
        /// </summary>
        /// <remarks>
        /// A three-element rank, deliberately written out rather than sorted: the array is the
        /// caller's and must not be reordered, and ties resolve to the earlier index, which is the
        /// reading a human makes of three equal numbers.
        /// </remarks>
        private static int Rank(float[] orders, int index)
        {
            int rank = 1;
            for (int i = 0; i < orders.Length; i++)
            {
                if (i != index && orders[i] < orders[index])
                {
                    rank++;
                }
            }

            return rank;
        }

        /// <summary>
        /// Registers the custom-control factories, opens the bundle and creates the windows.
        /// </summary>
        /// <param name="modFolder">The deployed mod's folder, or <c>null</c> if the loader has not
        /// assigned it yet.</param>
        /// <param name="log">Info sink - the branch lines a post-launch grep must find.</param>
        /// <param name="warn">Warning sink.</param>
        /// <param name="error">Error sink.</param>
        /// <remarks>
        /// <para>
        /// Called once from <c>CommNextReduxPlugin.EnsureUI</c>, which runs inside
        /// <c>OnInitialized</c> after the renderer's own resources are resolved. Nothing here is
        /// retryable in a useful way except the whole call, so a failure leaves
        /// <see cref="IsInitialized"/> false and logs by name; the plugin's flag is set only on a
        /// full success.
        /// </para>
        /// <para>
        /// <b>The factory registration is step one, before any template is touched.</b> A UXML cloned
        /// with the registry empty throws the "missing a UxmlElementAttribute" error the player
        /// already produced once (F15), so the order is not cosmetic.
        /// </para>
        /// </remarks>
        public static void Initialize(string modFolder, Action<string> log, Action<string> warn,
            Action<string> error)
        {
            if (_initialized)
            {
                return;
            }

            // 1. The five custom controls. Must precede every VisualTreeAsset instantiation.
            CommNextUIFactoryRegistration.Initialize(log, error);

            // 1b. And the registry is read back, by the importer's own lookup, so "registered" is a
            //     measurement rather than a claim. F15's failure would appear here as MISSING lines.
            CommNextUIFactoryRegistration.Verify(log, warn);

            // 2. The bundle.
            VisualTreeAsset toolbarTemplate = null;
            VisualTreeAsset tooltipTemplate = null;
            VisualTreeAsset vesselReportTemplate = null;
            if (!OpenBundle(modFolder, log, error))
            {
                return;
            }

            toolbarTemplate = LoadAsset<VisualTreeAsset>(ToolbarUxmlPath);
            tooltipTemplate = LoadAsset<VisualTreeAsset>(TooltipUxmlPath);
            vesselReportTemplate = LoadAsset<VisualTreeAsset>(VesselReportUxmlPath);

            // 2b. P2's player-side probe, run inside the mod: the registration above is a claim about
            //     the registry, and this is the claim that matters - the importer really builds these
            //     controls out of the shipped bundle. Placed here, after the bundle and before any
            //     window, so a failure is attributed before a window can also be empty.
            UIControlAssertion.Run(log, warn);

            if (toolbarTemplate == null)
            {
                Write(error, "ui: the toolbar template '" + ToolbarUxmlPath + "' is not in the bundle "
                    + "(route=" + BundleRoute + ") - the map toolbar cannot be built and the map will "
                    + "show no CommNext UI");
                return;
            }

            // 3. The toolbar. Window.Create builds the renderer's visual tree synchronously, so the
            //    controller can bind in the same frame it is created.
            PanelRenderer toolbarRenderer = Window.Create(MapToolbarWindowController.WindowOptions,
                toolbarTemplate);
            if (toolbarRenderer == null)
            {
                Write(error, "ui: Window.Create returned no PanelRenderer for the map toolbar - the "
                    + "window layer is not up");
                return;
            }

            // 3b. P9.3: the window's panel goes on Unity's built-in UI layer, so the game's own
            //     "is the pointer over UI?" gate (Map3DManeuvers.IsOtherUIHovered) sees this window
            //     and stops acting on the click underneath it (F82). Driven through the renderer and
            //     retried by PanelLayerFix.Tick until the panel exists; reported either way.
            PanelLayerFix.Apply(toolbarRenderer, "toolbar", log);

            MapToolbarWindowController toolbar =
                toolbarRenderer.gameObject.AddComponent<MapToolbarWindowController>();
            toolbar.Initialize(toolbarRenderer, log, warn, error);
            Toolbar = toolbar;

            // 3c. P9.5 (D65): the pointer guard. While the cursor rests on this window's rectangle the
            //     map's mouse-driven camera controls are held down - at ACTION granularity, so WASD,
            //     ESC, quicksave/load and the time-warp keys keep working - and they are released the
            //     moment the pointer leaves. The element is the one the controller bound as `_root`
            //     (`Window.ResolveWindowRoot`'s answer), which is what the library's own
            //     `GameInputBlockManipulator` tests, so the region is the same region L16 measured.
            //     See PanelInputBlocker for why the library's `WindowOptions.BlockGameInput` is not
            //     the mechanism (it disables eight whole definitions, ESC and WASD among them).
            PanelInputBlocker.Attach(toolbar.Root, "toolbar", log);

            // 4. THE VESSEL REPORT, created AFTER the toolbar and BEFORE the tooltip.
            //
            //    The creation order below is the RELATIVE stacking this mod wants, and SyncZOrder
            //    reads it back off the three renderers at the end of this method: the report draws
            //    over the toolbar that opened it, and the tooltip - which the toolbar's own buttons
            //    raise - still draws over both. (Since P9.1 the absolute layer is a panel
            //    `sortingOrder` the pass assigns; the creation order is what selects the pass's call
            //    order, and it is still the thing that must not be rearranged.) Moving this call below
            //    the tooltip's would put the tooltip UNDER the report and the toolbar's tooltips would
            //    disappear the moment the report was opened.
            if (vesselReportTemplate == null)
            {
                Write(warn, "ui: the vessel report template '" + VesselReportUxmlPath + "' is not in the "
                    + "bundle - the toolbar's third button will not be able to open the report");
            }
            else
            {
                PanelRenderer vesselReportRenderer = Window.Create(
                    VesselReportWindowController.WindowOptions, vesselReportTemplate);
                if (vesselReportRenderer == null)
                {
                    Write(warn, "ui: Window.Create returned no PanelRenderer for the vessel report - the "
                        + "toolbar's third button will not be able to open the report");
                }
                else
                {
                    // P9.3: the report's panel is on the UI layer for the same reason the toolbar's is
                    // (F82) - the map's gate reads the layer and no KSP2 input definition. P9.4 turned
                    // the report's BlockGameInput off (D62, F84): it never covered this gate, and it
                    // held all eight input locks while the pointer hovered the window.
                    PanelLayerFix.Apply(vesselReportRenderer, "vessel report", log);

                    VesselReportWindowController vesselReport =
                        vesselReportRenderer.gameObject.AddComponent<VesselReportWindowController>();
                    vesselReport.Initialize(vesselReportRenderer, log, warn, error);
                    VesselReport = vesselReport;

                    // P9.5 (D65): the guard, on the same element the toolbar's is on - the report's
                    // own UXML root (`_root`), the 399.8 x 500.3 rectangle P9.4 logged in L17.
                    PanelInputBlocker.Attach(vesselReport.Root, "vessel report", log);
                }
            }

            // 5. The tooltip, last, so that it is the top-most window in the panel.
            if (tooltipTemplate == null)
            {
                Write(warn, "ui: the tooltip template '" + TooltipUxmlPath + "' is not in the bundle - "
                    + "the toolbar's buttons will have no tooltips");
            }
            else
            {
                PanelRenderer tooltipRenderer = Window.Create(TooltipWindowController.WindowOptions,
                    tooltipTemplate);
                if (tooltipRenderer == null)
                {
                    Write(warn, "ui: Window.Create returned no PanelRenderer for the tooltip - the "
                        + "toolbar's buttons will have no tooltips");
                }
                else
                {
                    // P9.3: the tooltip is a window too - a click that lands on a visible tooltip must
                    // not also reach the map (F82).
                    PanelLayerFix.Apply(tooltipRenderer, "tooltip", log);

                    // P9.5 (D65, F87): NO pointer guard here, deliberately. `#tooltip-root` is a
                    // full-screen overlay (`position: absolute; left/top/right/bottom: 0`) whose every
                    // element is `picking-mode="Ignore"`, so a guard attached to it could never engage
                    // (the pick skips the whole tree) - and if one of those elements ever became
                    // pickable, the guarded region would be the ENTIRE SCREEN, which is the opposite of
                    // what the guard is for. It is also unnecessary: a tooltip is shown only while the
                    // pointer is on the toolbar button that owns it, which is inside the toolbar's own
                    // guarded rectangle.
                    TooltipWindowController tooltip =
                        tooltipRenderer.gameObject.AddComponent<TooltipWindowController>();
                    tooltip.Initialize(tooltipRenderer, log, warn, error);
                    Tooltip = tooltip;
                }
            }

            _initialized = true;
            IsInitialized = true;

            // The sink the z-order pass writes through, captured here so a later re-apply (the
            // map-entry transition in SyncMapView) can log without reaching for the plugin.
            _zOrderLog = log;

            Write(log, "ui: window layer up - bundle=" + BundleRoute + ", toolbar="
                + (Toolbar != null ? "created" : "MISSING") + ", vessel report="
                + (VesselReport != null ? "created (between the toolbar and the tooltip, which is its "
                    + "z-order)" : "MISSING") + ", tooltip="
                + (Tooltip != null ? "created (last, so it draws on top)" : "MISSING"));

            // P9.1: the creation order above is what the z-order pass walks, and the pass itself is
            // logged by name right here so a launch reads "the windows were built, then their panels
            // were ordered". Without this call the panels keep whatever sortingOrder the library's
            // per-window PanelSettings clone happened to carry, which is F75 - a toolbar in the
            // top-right corner drawing under the game's own map HUD.
            //
            // Run unconditionally, even though the windows are only visible in the map: this is the
            // pass whose "before" numbers are the baseline the map-entry passes are compared against,
            // and a boot that cannot show them cannot prove the fix did anything. If this launch is
            // already inside the map (a save loaded straight into it), the SyncMapView call below
            // sees its own transition and runs a second pass - correct, idempotent, and visible in
            // the log as two pass numbers rather than as a surprise.
            SyncZOrder();

            // The map may already be alive when the UI arrives (the window layer is built from
            // `OnInitialized`, and a message can have arrived first), so the state is applied once
            // here. Both later callers are idempotent, so this cannot fight them - and in the one
            // case where that is already true (a save loaded straight into the map came up before the
            // UI did) this call runs a second z-order pass, which its own pass number makes visible
            // rather than surprising. When the map is not up, no pass runs here at all: the next one
            // is the player's first map entry.
            SyncMapView(EventListener.IsInMapView);
        }

        /// <summary>
        /// The held AssetBundle, or <c>null</c> when it could not be opened.
        /// </summary>
        /// <remarks>
        /// Held, never unloaded - see the file header (D37). P7's own handle is a different object
        /// and is still released exactly as it was.
        /// </remarks>
        public static AssetBundle Bundle
        {
            get { return _bundle; }
        }

        /// <summary>Loads one asset out of the held bundle, or returns <c>null</c>.</summary>
        /// <typeparam name="T">The asset type.</typeparam>
        /// <param name="assetPath">The container name, as the bundle lists it.</param>
        /// <returns>The asset, or <c>null</c> when the bundle is absent or the name is not in it.</returns>
        public static T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
        {
            if (_bundle == null)
            {
                return null;
            }

            try
            {
                return _bundle.LoadAsset<T>(assetPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Attaches the bundle's stylesheet to a window root unless a copy is already in effect.
        /// </summary>
        /// <param name="root">The window's root element.</param>
        /// <param name="warn">Warning sink.</param>
        /// <returns><c>true</c> when a sheet is attached to <paramref name="root"/> or an ancestor.</returns>
        /// <remarks>
        /// <para>
        /// <b>Why this exists at all.</b> Every UXML in this mod references `CommNextStyles.uss`
        /// through an editor URI - `project://database/Assets/UI/CommNextStyles.uss?...guid=a2dd25c9...`
        /// - which is the Unity <i>editor's</i> asset scheme. The `.meta` files preserve those GUIDs
        /// (all 15 `guid=` references in the sheet resolve to real `.meta` files in this project, and
        /// the sheet ships inside the bundle as the `CommNextStyles` asset), but P2 measured that the
        /// editor passing is not evidence about the player. So the template's reference is kept
        /// untouched, and this method makes the outcome deterministic: if no sheet named
        /// <see cref="StylesSheetName"/> is attached anywhere from the root up, the bundle's own copy
        /// is attached here.
        /// </para>
        /// <para>
        /// <b>Idempotent and non-destructive.</b> When the template's reference did resolve, the sheet
        /// is already in effect (compared by asset name, because a bundle-loaded copy and an
        /// import-resolved copy are different objects for the same asset) and nothing is added. The
        /// line names the branch it took, and <see cref="Why"/> says what that branch can and cannot
        /// mean - the caller decides the level it logs at, once it has read the numbers back
        /// (<c>UIStyleSheetProof</c>).
        /// </para>
        /// <para>
        /// <b>The search walks up, deliberately.</b> A UXML's stylesheets land on the
        /// <c>TemplateContainer</c> the importer creates, which is an <i>ancestor</i> of the element
        /// this mod's controllers call "the root": `Window.Create` builds the panel's root and
        /// `GetWindowRoot` unwraps the clone's template container, so the controllers bind the
        /// template's own top-level element, one level below the container that carries the sheets.
        /// Measured the
        /// hard way in the sibling port, whose drag manipulator reads `target.styleSheets` on an
        /// element that never carried them. So the check is "in effect for this element", which is
        /// what `styleSheets` being a list on every ancestor actually means.
        /// </para>
        /// </remarks>
        public static bool EnsureStylesAttached(VisualElement root, Action<string> log,
            Action<string> warn)
        {
            if (root == null)
            {
                return false;
            }

            int inspected = 0;
            for (VisualElement element = root; element != null; element = element.parent)
            {
                inspected++;
                int count = element.styleSheets.count;
                for (int i = 0; i < count; i++)
                {
                    StyleSheet sheet = element.styleSheets[i];
                    if (sheet != null && sheet.name == StylesSheetName)
                    {
                        SheetAttached = true;
                        SheetRoute = "template";
                        SheetAttachWarn = null;
                        Write(log, "ui-styles: route=template - '" + StylesSheetName + "' is already in "
                            + "effect on this window (" + (element == root ? "the element itself" : "an "
                            + "ancestor, " + inspected + " level(s) up")
                            + "), so the template's own project:// reference to it resolved in this "
                            + "player (sheets on that element=" + count + ")");
                        return true;
                    }
                }
            }

            if (_styles == null)
            {
                _styles = LoadAsset<StyleSheet>(StylesAssetPath);
            }

            if (_styles == null)
            {
                SheetRoute = "none";
                SheetAttachWarn = "ui-styles: no '" + StylesSheetName + "' sheet in effect on the window "
                    + "and none in the bundle at '" + StylesAssetPath + "' (route=" + BundleRoute
                    + ") - the window draws with Unity's default styles only";
                Write(warn, SheetAttachWarn);
                return false;
            }

            try
            {
                root.styleSheets.Add(_styles);
                SheetAttached = true;
                SheetRoute = "bundle";
                SheetAttachWarn = null;
                Write(log, "ui-styles: route=bundle - '" + StylesSheetName + "' was NOT in effect on this "
                    + "window (the template's project:// reference did not resolve), so the bundle's own "
                    + "copy was attached at runtime - it is in effect now, but the template-level "
                    + "reference is unproven and UIStyleSheetProof must confirm it styled");
                return true;
            }
            catch (Exception exception)
            {
                SheetRoute = "failed";
                SheetAttachWarn = "ui-styles: attaching the bundle's sheet failed ("
                    + exception.GetType().Name + ": " + exception.Message + ") - the window draws with "
                    + "Unity's default styles only";
                Write(warn, SheetAttachWarn);
                return false;
            }
        }

        /// <summary>
        /// What the last <see cref="EnsureStylesAttached"/> call can and cannot claim.
        /// </summary>
        /// <param name="proof">The proof result for the page this call was made on.</param>
        /// <param name="proved">
        /// Whether the resolved-style read-back already passed for this page. When it did, the route
        /// is settled; when it did not, this method says so without contradicting the proof.
        /// </param>
        /// <returns>The line to log, at whatever level the caller chooses.</returns>
        /// <remarks>
        /// The two facts are deliberately kept apart. "A sheet is in the list" and "the sheet styled
        /// this element" are different claims and only the second one is the phase gate: a sheet in
        /// the list can have all of its rules overridden, and a `project://` reference that resolves
        /// to a sheet whose image URLs did not is a sheet that is attached and half-broken. This
        /// method exists so no log line ever asserts the strong claim from the weak fact.
        /// </remarks>
        public static string Why(bool proved)
        {
            if (SheetRoute == null)
            {
                return "ui-styles: nothing was attached - EnsureStylesAttached has not run for a "
                    + "controller that got far enough to call it";
            }

            string what;
            switch (SheetRoute)
            {
                case "template":
                    what = "the template's own project:// reference resolved";
                    break;
                case "bundle":
                    what = "the bundle's copy was attached at runtime";
                    break;
                case "none":
                    what = "there was no sheet to attach";
                    break;
                default:
                    what = "attaching the bundle's copy threw";
                    break;
            }

            if (proved)
            {
                return "ui-styles: route=" + SheetRoute + " (" + what + ") and the resolved-style "
                    + "read-back PASSED - the sheet is in effect, not merely attached";
            }

            if (SheetAttachWarn != null)
            {
                return SheetAttachWarn;
            }

            return "ui-styles: route=" + SheetRoute + " (" + what + ") - the sheet is in the list but "
                + "its rules are NOT confirmed in effect; the read-back lines above say what the element "
                + "actually resolved to";
        }

        /// <summary>Which branch put a sheet in effect: <c>template</c>, <c>bundle</c>, <c>none</c>, <c>failed</c>.</summary>
        public static string SheetRoute { get; private set; }

        /// <summary>Opens the deployed bundle, once, and records which branch resolved.</summary>
        /// <param name="modFolder">The mod's folder, or <c>null</c>.</param>
        /// <param name="log">Info sink, for the held-handle line.</param>
        /// <param name="error">Error sink.</param>
        /// <returns><c>true</c> when the bundle is open.</returns>
        /// <remarks>
        /// A missing bundle is an Error here rather than a Warning: unlike the ruler mesh, which has a
        /// code-built fallback, a window has no fallback. Without the bundle there is no UI at all,
        /// and a blank map with no toolbar must not be a mystery.
        /// </remarks>
        private static bool OpenBundle(string modFolder, Action<string> log, Action<string> error)
        {
            if (_bundle != null)
            {
                return true;
            }

            if (string.IsNullOrEmpty(modFolder))
            {
                BundleRoute = "no-folder";
                Write(error, "ui: the mod folder is not assigned (SWMetadata.Folder is null), so the UI "
                    + "bundle cannot be found and no CommNext window can be built");
                return false;
            }

            string bundlePath = modFolder + BundleRelativePath;
            if (!File.Exists(bundlePath))
            {
                BundleRoute = "absent";
                Write(error, "ui: no UI bundle at " + bundlePath + " - no CommNext window can be built. "
                    + "Deploy assets/bundles/commnextredux_ui.bundle alongside the DLL");
                return false;
            }

            try
            {
                _bundle = AssetBundle.LoadFromFile(bundlePath);
            }
            catch (Exception exception)
            {
                _bundle = null;
                BundleRoute = "failed";
                Write(error, "ui: AssetBundle.LoadFromFile threw for " + bundlePath + " ("
                    + exception.GetType().Name + ": " + exception.Message + ") - no CommNext window can "
                    + "be built");
                return false;
            }

            if (_bundle == null)
            {
                BundleRoute = "failed";
                Write(error, "ui: AssetBundle.LoadFromFile returned null for " + bundlePath + " - no "
                    + "CommNext window can be built");
                return false;
            }

            BundleRoute = "loaded";

            // The held handle is the point of this class's half of the bundle story: the templates and
            // the stylesheet stay deserialised for the session, so nothing here ever calls Unload.
            Write(log, "ui: UI bundle held for the session at " + bundlePath + " (this handle is never "
                + "unloaded; P7's separate handle is still released by RulerGeometry)");
            return true;
        }

        /// <summary>Writes through a callback that is allowed to be absent.</summary>
        /// <param name="write">The callback, or <c>null</c>.</param>
        /// <param name="message">The line.</param>
        private static void Write(Action<string> write, string message)
        {
            if (write != null)
            {
                write(message);
            }
        }
    }
}
