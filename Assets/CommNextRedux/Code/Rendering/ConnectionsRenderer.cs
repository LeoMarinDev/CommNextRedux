// CommNextRedux - owns every drawn comm link and every range ruler: creates one per tree edge or
// eligible node, reuses it, prunes it.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/ConnectionsRenderer.cs - this is that file's
//   connection half plus (from Phase 7 on) its ruler half: the 0.5 s poll (`_timerRemaining` /
//   `UpdateRenderings`), the mode properties with their clear-on-None / mark-dirty behaviour, the
//   id-keyed `_connections` and `_rulers` dictionaries, the `keepIds` / `removeIds` prune, and the
//   `OnMapConnectionDestroyed` / `OnMapRulerDestroyed` handshakes. The legacy's report half and its
//   debug half are NOT here - see "the phase boundary" below.
//
// THE ONE SHAPE CHANGE: A STATIC CLASS TICKED BY THE PLUGIN, NOT A SCENE MONOBEHAVIOUR
//   The legacy was a `MonoBehaviour` on a GameObject it created, with `Instance` assigned in
//   `Start()`. This port is a static class whose `Tick` the plugin calls from its own `Update`.
//   Reasons, in order: the loader already creates exactly one `KerbalMod` MonoBehaviour and has
//   demonstrated that its `Update` runs, so a second GameObject adds a lifetime question and no
//   capability; the sibling port's renderer is static for the same reason; and the per-link
//   components - which DO need to be MonoBehaviours, because Unity drives their per-frame
//   re-read - reach their owner through a static call instead of a singleton that can be null for
//   one frame after a scene load. Everything the legacy's instance did to its own state, this
//   class does to its own statics, in the same order.
//
// WHY PRUNING IS LOAD-BEARING RATHER THAN TIDINESS
//   Every refresh walks the tree and produces a fresh set of wanted ids. The graph is rebuilt by
//   the game roughly every three seconds and the map view can rebuild every marker on a reference
//   body change, so a renderer that only ever added would leak one LineRenderer per edge per
//   rebuild - hundreds of objects and draw calls within a minute, on a map that looks correct the
//   whole time. The prune runs on every refresh, including the refreshes that add nothing.
//
// WHAT THE PASS DOES NOT DEPEND ON
//   `EventListener.IsInMapView` is a fast path, never the gate: a stale `true` (a session torn
//   down without the map messages arriving) costs one wasted poll, and a stale `false` costs one
//   poll at most because the messages arrive on entry. The gate that decides whether a line can be
//   drawn is the map dictionary itself - `AllMapSelectableItems` is null in the flight scene, which
//   is normal, and the pass reports that as a state rather than as an error.
//
// THE PHASE BOUNDARY
//   Dropped here, each with its owner: the vessel-report lines (`_reportConnections`,
//   `ConfigureForReport`, `ReportVessel` - P8), the debug-positions dump (`#if DEBUG_MAP_POSITIONS`
//   - dead), the two prefab statics (`RulerSpherePrefab`/`TestSpherePrefab` - replaced in P7 by
//   `RulerGeometry`, which resolves the same mesh out of the bundle and builds the GameObject in
//   code), and the selected-band filter (`SelectedBandIndex`, replaced in P6 by the engine's
//   per-edge `SelectedBandOf`; the legacy also used it to tint and to gate RULERS, and P7 records
//   what that leaves - see the ruler pass below). Each is recorded in
//   Deploy/obj/divergences.md.
//
// THE TWO FAMILIES ARE TWO PASSES, DELIBERATELY
//   The lines and the rulers share the tree, the markers and the poll interval, and nothing else:
//   they answer to different modes, they fail for different reasons (a shader, a mesh, a tint
//   property), and the diagnostic probe prints them on separate lines. So they are two methods on
//   one tick, each with its own early returns and its own last-pass state, and a throw in one is
//   caught without cancelling the other. A single pass that shared its `state=` token would report
//   "the map drew nothing" when only the rulers had failed - which is the exact ambiguity the
//   probe's state field exists to remove.

using System;
using System.Collections.Generic;
using System.Globalization;
using CommNextRedux.Network;
using CommNextRedux.Network.Bands;
using KSP.Game;
using KSP.Map;
using KSP.Sim.impl;
using UnityEngine;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// The map renderer: one line per edge of the engine's tree, one range sphere per eligible node,
    /// both refreshed on a 0.5 s poll.
    /// </summary>
    /// <remarks>
    /// Static by design - see the file header. Every public member is safe to call before a session
    /// exists; the renderer simply reports that it cannot draw yet.
    /// </remarks>
    public static class ConnectionsRenderer
    {
        /// <summary>
        /// How long a refresh pass waits between runs.
        /// </summary>
        /// <remarks>
        /// The legacy's own interval. It is a `Time.deltaTime` accumulation, so it follows the game's
        /// time scale exactly as the legacy's did - the map is not a place where pausing stops the
        /// camera moving, and the map messages still force an immediate pass by way of
        /// <see cref="MarkAsDirty"/>.
        /// </remarks>
        public const float RefreshSeconds = 0.5f;

        /// <summary>The Unity layer the game's map objects live on.</summary>
        public const string MapLayerName = "Map";

        /// <summary>The name every line GameObject carries, so a scene dump identifies them.</summary>
        public const string ConnectionObjectName = "CommNextRedux Connection";

        /// <summary>The live links, keyed by the pair id <see cref="MapConnectionComponent.GetID"/> builds.</summary>
        private static readonly Dictionary<string, MapConnectionComponent> Connections =
            new Dictionary<string, MapConnectionComponent>();

        /// <summary>The live rulers, keyed by the node guid of the marker each one is drawn on.</summary>
        /// <remarks>
        /// The same identity the connection dictionary uses, for the same reason: a save load
        /// rebuilds the simulation object graph, so a registry keyed on object reference would
        /// accumulate entries whose marker is gone and whose guid reads back null. Strings survive
        /// the rebuild; references do not.
        /// </remarks>
        private static readonly Dictionary<string, MapRulerComponent> Rulers =
            new Dictionary<string, MapRulerComponent>();

        /// <summary>Metres per map unit, captured once per ruler pass and read by every sphere.</summary>
        /// <remarks>
        /// The legacy read `map3D.GetSpaceProvider().Map3DScaleInv` from inside each sphere's
        /// per-frame `Update`; the port captures it once per refresh pass instead, which is the same
        /// value for every ruler in that pass and one game accessor call instead of one per ruler per
        /// frame. Defaults to 1, not 0: a sphere that scaled before the map resolved would otherwise
        /// divide by zero.
        /// </remarks>
        private static double _map3dScaleInv = 1.0;

        /// <summary>Whether the map's own scale factor has been logged once for this session.</summary>
        private static bool _mapScaleWarned;
        private static bool _rulerScaleLogged;


        /// <summary>Node guids already reported as having no map item, so the poll warns once each.</summary>
        private static readonly HashSet<IGGuid> WarnedMissingMapItems = new HashSet<IGGuid>();

        /// <summary>
        /// Node guids already reported as having a marker with no map item bound yet - the same
        /// once-per-session treatment, and a separate set because it is a different cause.
        /// </summary>
        private static readonly HashSet<IGGuid> WarnedUnboundMapItems = new HashSet<IGGuid>();

        /// <summary>The engine-tree pairs the active mode wants drawn, as <c>source * count + target</c>.</summary>
        private static readonly HashSet<long> ActivePathPairs = new HashSet<long>();

        private static Action<string> _log;
        private static Action<string> _warn;
        private static Action<string> _error;
        private static bool _initialized;

        private static float _timerRemaining;

        private static ConnectionsDisplayMode _connectionsDisplayMode = ConnectionsDisplayMode.Lines;
        private static RulersDisplayMode _rulersDisplayMode = RulersDisplayMode.None;

        private static bool _mapLayerResolved;
        private static int _mapLayer = -1;

        private static bool _mapCoreWarnedThisPass;
        private static bool _noMaterialLogged;
        private static bool _activeVesselWarnedThisPass;

        // --- the last pass, as the probe reads it -------------------------------------------------
        private static bool _lastMapView;
        private static int _lastMapItemCount = -1;   // -1 == the dictionary was null
        private static int _lastCreated;
        private static int _lastUpdated;
        private static int _lastRemoved;
        private static string _lastReason = "not-run";

        // --- the last RULER pass, as its own probe line reads it -----------------------------------
        private static int _lastRulerNodeCount = -1;   // -1 == the engine had no nodes
        private static int _lastRulersConsidered;
        private static int _lastRulersCreated;
        private static int _lastRulersUpdated;
        private static int _lastRulersRemoved;
        private static string _lastRulersReason = "not-run";
        private static bool _noRulerGeometryLogged;
        private static bool _noRulerMaterialLogged;

        /// <summary>
        /// Whether the renderer has its log writers, i.e. whether the plugin has initialized it.
        /// </summary>
        public static bool IsInitialized
        {
            get { return _initialized; }
        }

        /// <summary>How many links are currently drawn.</summary>
        public static int LiveCount
        {
            get { return Connections.Count; }
        }

        /// <summary>How many range rulers are currently drawn.</summary>
        public static int LiveRulerCount
        {
            get { return Rulers.Count; }
        }

        /// <summary>
        /// Metres per map unit, as the last ruler pass read it.
        /// </summary>
        /// <remarks>
        /// Public because each <see cref="MapSphereRulerComponent"/> sizes itself against it, and
        /// because the probe prints it: a sphere of the wrong size is diagnosable from this number
        /// and the node's range alone. <c>1</c> until the map resolves.
        /// </remarks>
        public static double Map3dScaleInv
        {
            get { return _map3dScaleInv; }
        }

        /// <summary>Which connection lines are drawn, and which are destroyed on the transition out.</summary>
        /// <remarks>
        /// The setter is the legacy's: <c>None</c> destroys everything already drawn rather than
        /// merely skipping the redraw, and every other value forces a pass now instead of waiting up
        /// to half a second. Hoisting the clear into the caller would leave the previous mode's lines
        /// on the map, which is the defect the setter exists to prevent.
        /// </remarks>
        public static ConnectionsDisplayMode ConnectionsDisplayMode
        {
            get { return _connectionsDisplayMode; }
            set
            {
                if (_connectionsDisplayMode == value)
                {
                    return;
                }

                _connectionsDisplayMode = value;
                Write(_log, "render-lines: connections mode -> " + value);

                if (value == ConnectionsDisplayMode.None)
                {
                    ClearConnections();
                }
                else
                {
                    MarkAsDirty();
                }
            }
        }

        /// <summary>
        /// Which range rulers are drawn, and which are destroyed on the transition out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The legacy's setter, restored to full meaning now that the rulers exist: <c>None</c>
        /// destroys every ruler already drawn rather than merely skipping the redraw, and every other
        /// value forces a pass now instead of waiting up to half a second.
        /// </para>
        /// <para>
        /// <b>The field's default is <c>None</c>, and the session's value comes from the config.</b>
        /// These are two different things on purpose. Nothing is drawn until a mode has been
        /// <i>applied</i>, so a renderer that is somehow never reached by the plugin cannot put a
        /// sphere on the player's map; the value that is applied at boot is
        /// <see cref="NetworkConfig.RulersMode"/> - the bound <c>Map/Rulers mode</c> entry, which
        /// falls back to <see cref="NetworkConfig.RulersModeDefault"/> (<c>Relays</c>, the legacy's
        /// own default) when the config is unbound. The plugin applies it once the renderer exists
        /// and re-applies it on every change, so the settings UI owns the switch in both directions:
        /// <c>None</c> prunes what is drawn, and any other value re-creates it within half a second.
        /// </para>
        /// </remarks>
        public static RulersDisplayMode RulersDisplayMode
        {
            get { return _rulersDisplayMode; }
            set
            {
                if (_rulersDisplayMode == value)
                {
                    return;
                }

                _rulersDisplayMode = value;
                Write(_log, "render-rulers: mode -> " + value + (value == RulersDisplayMode.None
                    ? " (every ruler destroyed)"
                    : " (the next pass creates the rulers this mode admits)"));

                if (value == RulersDisplayMode.None)
                {
                    ClearRulers();
                }
                else
                {
                    MarkAsDirty();
                }
            }
        }

        /// <summary>
        /// Hands the renderer the plugin's null-guarded writers. Called once at initialization.
        /// </summary>
        /// <param name="log">Informational sink; must not throw.</param>
        /// <param name="warn">Warning sink; must not throw.</param>
        /// <param name="error">Error sink; must not throw.</param>
        public static void Initialize(Action<string> log, Action<string> warn, Action<string> error)
        {
            _log = log;
            _warn = warn;
            _error = error;
            _initialized = true;
        }

        /// <summary>Forces the next <see cref="Tick"/> to run a pass.</summary>
        public static void MarkAsDirty()
        {
            _timerRemaining = 0f;
        }

        /// <summary>
        /// Destroys every drawn link and every drawn ruler, and forgets every per-session
        /// bookkeeping value.
        /// </summary>
        /// <remarks>
        /// The objects are destroyed rather than merely forgotten, so a session that ends while the
        /// map is still standing cannot leave orphaned renderers behind for the next one. Both
        /// dictionaries are cleared before the destroys, so the <c>OnDestroy</c> callbacks that
        /// follow find nothing to remove.
        /// </remarks>
        public static void Reset()
        {
            ClearConnections();
            ClearRulers();

            WarnedMissingMapItems.Clear();
            WarnedUnboundMapItems.Clear();
            ActivePathPairs.Clear();

            _timerRemaining = 0f;
            _mapLayerResolved = false;
            _mapLayer = -1;
            _mapCoreWarnedThisPass = false;
            _noMaterialLogged = false;
            _activeVesselWarnedThisPass = false;
            _lastMapItemCount = -1;
            _lastReason = "reset";

            _map3dScaleInv = 1.0;
            _mapScaleWarned = false;
            _rulerScaleLogged = false;
            _noRulerGeometryLogged = false;
            _noRulerMaterialLogged = false;
            _lastRulerNodeCount = -1;
            _lastRulersConsidered = 0;
            _lastRulersCreated = 0;
            _lastRulersUpdated = 0;
            _lastRulersRemoved = 0;
            _lastRulersReason = "reset";
        }

        /// <summary>
        /// The per-frame poll, called from the plugin's own <c>Update</c>.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c> before a session exists.</param>
        /// <remarks>
        /// Never throws out: the caller is a Unity <c>Update</c>, and an exception thrown from there
        /// aborts the rest of the plugin's frame work rather than one refresh. Each family gets its
        /// own catch for the same reason - a ruler pass that throws must not cost the connection
        /// lines their refresh, and the caught state is what the probe reports.
        /// </remarks>
        public static void Tick(GameInstance game)
        {
            if (!_initialized)
            {
                return;
            }

            _timerRemaining -= Time.deltaTime;
            if (_timerRemaining > 0f)
            {
                return;
            }

            _timerRemaining = RefreshSeconds;

            try
            {
                Refresh(game);
            }
            catch (Exception exception)
            {
                _lastReason = "refresh threw " + exception.GetType().Name;
                Write(_warn, "render-lines: the refresh pass threw (" + exception.GetType().Name + ": "
                    + exception.Message + "); the graph itself is unaffected");
            }

            try
            {
                RefreshRulers(game);
            }
            catch (Exception exception)
            {
                _lastRulersReason = "refresh threw " + exception.GetType().Name;
                Write(_warn, "render-rulers: the ruler pass threw (" + exception.GetType().Name + ": "
                    + exception.Message + "); the connection lines are unaffected");
            }
        }

        /// <summary>
        /// Removes a link Unity has destroyed.
        /// </summary>
        /// <param name="connection">The component whose <c>OnDestroy</c> is running.</param>
        /// <remarks>
        /// Tolerates a cleared or already-emptied dictionary on purpose: this fires from the game's
        /// own map teardown, which can run after <see cref="Reset"/> emptied the dictionary, and a
        /// throw here would surface inside Unity's destruction path where it is nearly impossible to
        /// attribute. Reference comparison, not value comparison - a destroyed component compares
        /// equal to null under Unity's overloaded operator, which would make a value-based removal
        /// match the wrong entry.
        /// </remarks>
        public static void Destroyed(MapConnectionComponent connection)
        {
            if (connection == null || Connections.Count == 0)
            {
                return;
            }

            MapConnectionComponent existing;
            if (Connections.TryGetValue(connection.Id, out existing) && ReferenceEquals(existing, connection))
            {
                Connections.Remove(connection.Id);
            }
        }

        /// <summary>
        /// Removes a ruler Unity has destroyed, or one whose marker has died.
        /// </summary>
        /// <param name="ruler">The component whose <c>Update</c> or <c>OnDestroy</c> is running.</param>
        /// <remarks>
        /// <para>
        /// The legacy's <c>OnMapRulerDestroyed</c> shape, kept: it removed the dictionary entry AND
        /// destroyed the GameObject, and the destroy is the half that matters for the path where the
        /// marker died but the ruler (parented to it) is still alive for a frame. It is a no-op on the
        /// path where Unity is already tearing the GameObject down.
        /// </para>
        /// <para>
        /// Tolerates a cleared or already-emptied dictionary on purpose: this fires from the game's
        /// own map teardown, which can run after <see cref="Reset"/> emptied the dictionary, and a
        /// throw here would surface inside Unity's destruction path where it is nearly impossible to
        /// attribute. Reference comparison, not value comparison - a destroyed component compares
        /// equal to null under Unity's overloaded operator, which would make a value-based removal
        /// match the wrong entry.
        /// </para>
        /// </remarks>
        public static void Destroyed(MapRulerComponent ruler)
        {
            if (ruler == null)
            {
                return;
            }

            if (Rulers.Count > 0)
            {
                MapRulerComponent existing;
                if (Rulers.TryGetValue(ruler.Id, out existing) && ReferenceEquals(existing, ruler))
                {
                    Rulers.Remove(ruler.Id);
                }
            }

            ruler.DestroyRuler();
        }

        /// <summary>
        /// The last pass, in one line, for the diagnostic probe.
        /// </summary>
        /// <returns>A compact description of what the renderer last saw and did.</returns>
        /// <remarks>
        /// This exists so the probe can report the renderer's state on a pass in which the renderer
        /// itself changed nothing - which is the normal steady state - without the renderer logging
        /// twice a second. The probe's own line is the one a post-launch grep reads.
        /// </remarks>
        public static string DescribeLastPass()
        {
            return "mapView=" + _lastMapView
                + " mode=" + _connectionsDisplayMode
                + " rulers=" + _rulersDisplayMode
                + " shader=" + (LineMaterials.ResolvedShaderName ?? "NONE")
                + " mapItems=" + (_lastMapItemCount < 0 ? "null" : _lastMapItemCount.ToString())
                + " live=" + Connections.Count
                + " created=" + _lastCreated
                + " updated=" + _lastUpdated
                + " removed=" + _lastRemoved
                + " state=" + _lastReason;
        }

        /// <summary>
        /// The last ruler pass, in one line, for the diagnostic probe.
        /// </summary>
        /// <returns>A compact description of what the ruler path last saw and did.</returns>
        /// <remarks>
        /// <para>
        /// A separate line from <see cref="DescribeLastPass"/>, and the reason is the same one that
        /// made them separate passes: the state token has to name WHICH family did not draw and why.
        /// A shared line would report the rulers' "no tintable material" as the map's overall state,
        /// or hide it behind the lines' "drawn".
        /// </para>
        /// <para>
        /// <c>geometry</c> is the branch that shipped (<c>bundle-fbx</c> or <c>code-built</c>), so a
        /// silent fallback cannot survive a single grep. <c>sizeof</c>-style detail is deliberately
        /// left out: <c>radius</c> and <c>scaleInv</c> are the two numbers that explain a sphere's
        /// size, and both are here.
        /// </para>
        /// </remarks>
        public static string DescribeRulersPass()
        {
            return "mode=" + _rulersDisplayMode
                + " geometry=" + (RulerGeometry.Branch ?? "NONE")
                + " verts=" + RulerGeometry.VertexCount
                + " radius=" + RulerGeometry.SphereRadius.ToString("0.###", Culture)
                + " material=" + (RulerMaterials.ResolvedShaderName ?? "NONE")
                + " scaleInv=" + _map3dScaleInv.ToString("0.###E+0", Culture)
                + " nodes=" + (_lastRulerNodeCount < 0 ? "none" : _lastRulerNodeCount.ToString())
                + " considered=" + _lastRulersConsidered
                + " live=" + Rulers.Count
                + " created=" + _lastRulersCreated
                + " updated=" + _lastRulersUpdated
                + " removed=" + _lastRulersRemoved
                + " state=" + _lastRulersReason;
        }

        /// <summary>Invariant culture, so a decimal-comma locale cannot change the probe's shape.</summary>
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        /// <summary>Destroys every drawn link. Called on the transition into <c>None</c>.</summary>
        private static void ClearConnections()
        {
            if (Connections.Count == 0)
            {
                return;
            }

            // Snapshot the components first and clear the dictionary before destroying: Destroy is
            // deferred to the end of the frame, but clearing up front means the dictionary is already
            // consistent and the OnDestroy callbacks that follow cannot mutate it mid-iteration.
            List<MapConnectionComponent> doomed = new List<MapConnectionComponent>(Connections.Values);
            Connections.Clear();

            for (int i = 0; i < doomed.Count; i++)
            {
                if (doomed[i] != null)
                {
                    doomed[i].DestroyConnection();
                }
            }
        }

        /// <summary>Destroys every drawn ruler. Called on the transition into <c>None</c>.</summary>
        private static void ClearRulers()
        {
            if (Rulers.Count == 0)
            {
                return;
            }

            // The connection clear's shape, for the same reason: snapshot, clear, then destroy, so
            // the OnDestroy handshakes that follow find an already-consistent dictionary.
            List<MapRulerComponent> doomed = new List<MapRulerComponent>(Rulers.Values);
            Rulers.Clear();

            for (int i = 0; i < doomed.Count; i++)
            {
                if (doomed[i] != null)
                {
                    doomed[i].DestroyRuler();
                }
            }
        }

        /// <summary>
        /// One refresh pass: walk the tree, reuse or create a line per edge, prune what is left over.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c>.</param>
        private static void Refresh(GameInstance game)
        {
            _lastCreated = 0;
            _lastUpdated = 0;
            _lastRemoved = 0;
            _lastMapItemCount = -1;
            _mapCoreWarnedThisPass = false;
            _activeVesselWarnedThisPass = false;

            bool mapView = EventListener.IsInMapView;
            if (mapView != _lastMapView)
            {
                // The transition is the interesting event: a map entry rebuilds every marker, so the
                // lines must be re-created; a map exit destroys them with their parent markers.
                _lastMapView = mapView;
                Write(_log, "render-lines: map view -> " + (mapView ? "entered" : "left")
                    + " (IsInMapView; the map dictionary below is still the gate)");
            }

            if (!LineMaterials.HasMaterial)
            {
                _lastReason = "no line material (see the material line)";
                if (!_noMaterialLogged)
                {
                    _noMaterialLogged = true;
                    Write(_error, "render-lines: no line material exists, so NO line will be created "
                        + "for any link (see the material line above for the shader that failed to "
                        + "resolve); the map is not empty of lines because the network is empty");
                }

                return;
            }

            if (_connectionsDisplayMode == ConnectionsDisplayMode.None)
            {
                _lastReason = "mode=None (nothing drawn)";
                ClearConnections();
                return;
            }

            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin == null)
            {
                _lastReason = "no plugin instance";
                return;
            }

            NetworkEngine engine = plugin.Network;
            if (engine == null)
            {
                _lastReason = "no network engine";
                return;
            }

            MapCore mapCore;
            if (!TryGetMapCore(game, out mapCore))
            {
                _lastReason = "no map core";
                return;
            }

            Map3DView map3D = mapCore.map3D;
            if (map3D == null)
            {
                _lastReason = "mapCore.map3D is null";
                return;
            }

            Dictionary<IGGuid, Map3DFocusItem> mapItems = map3D.AllMapSelectableItems;
            if (mapItems == null)
            {
                // Normal outside the map view: the dictionary is built with the map view. Reported as
                // a state, never as an error - and the pass keeps no stale lines, because a map exit
                // destroyed their parent markers.
                _lastReason = "AllMapSelectableItems is null (no map view)";
                return;
            }

            _lastMapItemCount = mapItems.Count;

            int count = engine.NodeCount;
            if (count == 0)
            {
                _lastReason = "the engine has no nodes";
                return;
            }

            HashSet<long> wanted = null;
            if (_connectionsDisplayMode == ConnectionsDisplayMode.Active)
            {
                wanted = BuildActiveVesselPath(game, engine, count);
                if (wanted == null)
                {
                    // The vessel has no node, so "its path" is empty: draw nothing rather than
                    // everything, which is the legacy's own response to the same condition.
                    _lastReason = "Active mode: no path for the active vessel";
                    PruneNew(new HashSet<string>());
                    return;
                }
            }

            HashSet<string> keepIds = new HashSet<string>();

            for (int target = 0; target < count; target++)
            {
                int source = engine.PredecessorOf(target);
                if (source < 0 || source >= count)
                {
                    continue;
                }

                if (wanted != null && !wanted.Contains((long)source * count + target))
                {
                    continue;
                }

                NetworkNodeSnapshot sourceNode = engine.Snapshot(source);
                NetworkNodeSnapshot targetNode = engine.Snapshot(target);

                Map3DFocusItem sourceItem;
                Map3DFocusItem targetItem;
                if (!TryGetMapItem(mapItems, mapCore, sourceNode, out sourceItem))
                {
                    continue;
                }

                if (!TryGetMapItem(mapItems, mapCore, targetNode, out targetItem))
                {
                    continue;
                }

                // A marker the map holds but has not bound to a map item yet: GetID reads
                // AssociatedMapItem.SimGUID, so an unguarded call would throw out of the whole pass -
                // turning one unbound marker into "the map drew no lines at all". Skipped instead, on
                // the same terms as a missing marker (once per guid), and the pass goes on to the rest
                // of the tree.
                if (!HasMapItem(sourceItem, sourceNode.Owner) || !HasMapItem(targetItem, targetNode.Owner))
                {
                    continue;
                }

                string id = MapConnectionComponent.GetID(sourceItem, targetItem);
                keepIds.Add(id);

                MapConnectionComponent connection;
                if (Connections.TryGetValue(id, out connection) && connection != null)
                {
                    _lastUpdated++;
                    connection.SetColor(ResolveColor(engine, sourceNode, targetNode, target));
                    continue;
                }

                connection = Create(sourceItem, targetItem, sourceNode.IsRelay && targetNode.IsRelay);
                if (connection == null)
                {
                    // The lookup succeeded, so a failure here is the component refusing a marker
                    // whose map item is null, or the material going away mid-pass. Either way there
                    // is no object to keep, and the id must not enter the dictionary.
                    keepIds.Remove(id);
                    continue;
                }

                Connections[id] = connection;
                _lastCreated++;
                connection.SetColor(ResolveColor(engine, sourceNode, targetNode, target));
            }

            PruneNew(keepIds);

            if (_lastCreated + _lastRemoved > 0)
            {
                Write(_log, "render-lines: " + _lastCreated + " created, " + _lastUpdated
                    + " updated, " + _lastRemoved + " removed, " + Connections.Count
                    + " live (" + _connectionsDisplayMode + ", map items=" + mapItems.Count + ")");
            }

            _lastReason = "drawn";
        }

        /// <summary>Destroys every live link the pass did not ask for.</summary>
        /// <param name="keepIds">The pass's complete set of wanted ids.</param>
        private static void PruneNew(HashSet<string> keepIds)
        {
            List<string> removeIds = null;

            foreach (KeyValuePair<string, MapConnectionComponent> pair in Connections)
            {
                if (keepIds.Contains(pair.Key))
                {
                    continue;
                }

                if (removeIds == null)
                {
                    removeIds = new List<string>();
                }

                removeIds.Add(pair.Key);
            }

            if (removeIds == null)
            {
                return;
            }

            for (int i = 0; i < removeIds.Count; i++)
            {
                MapConnectionComponent connection;
                if (!Connections.TryGetValue(removeIds[i], out connection))
                {
                    continue;
                }

                // Removed before the destroy, so the OnDestroy handshake that follows finds nothing
                // to do and cannot mutate the dictionary while this loop reads it.
                Connections.Remove(removeIds[i]);
                _lastRemoved++;

                if (connection != null)
                {
                    connection.DestroyConnection();
                }
            }
        }

        /// <summary>
        /// One ruler pass: walk the tree, create or reuse one sphere per eligible node, prune the rest.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c>.</param>
        /// <remarks>
        /// <para>
        /// A separate pass from <see cref="Refresh"/>, deliberately - see the file header. The map
        /// lookups it repeats (map core, map view, dictionary, engine) are cheap property reads against
        /// a half-second poll, and the alternative - folding the rulers into the line pass - would tie
        /// them to the line pass's early returns, so that <c>ConnectionsDisplayMode.None</c> would
        /// silently stop the rulers being pruned.
        /// </para>
        /// <para>
        /// <b>The mode filter is the legacy's, and it is the whole meaning of
        /// <see cref="RulersDisplayMode"/>:</b> <c>Relays</c> admits a node only when it carries an
        /// enabled relay module, <c>All</c> admits every node with a positive range, and <c>None</c>
        /// never reaches the loop at all. <b>The range gate is the port's replacement for the
        /// legacy's band gate</b>: the legacy skipped a node whose selected band had no range
        /// (`_selectedBandIndex` / `BandRanges[...] <= 0`), and this build has no band selection to
        /// consult, so the node's own <c>MaxRange</c> is what decides. A node with no range has no
        /// sphere to draw, and a zero-radius one would be an invisible object on the map.
        /// </para>
        /// </remarks>
        private static void RefreshRulers(GameInstance game)
        {
            _lastRulersCreated = 0;
            _lastRulersUpdated = 0;
            _lastRulersRemoved = 0;
            _lastRulersConsidered = 0;
            _lastRulerNodeCount = -1;

            if (_rulersDisplayMode == RulersDisplayMode.None)
            {
                // Unconditional, and cheap when there is nothing to clear: a mode change already
                // cleared them, but a session that started in None must not keep a stale set alive.
                _lastRulersReason = "mode=None (nothing drawn)";
                ClearRulers();
                return;
            }

            if (!RulerGeometry.HasMesh)
            {
                _lastRulersReason = "no ruler geometry (see the ruler-geometry line)";

                if (!_noRulerGeometryLogged)
                {
                    _noRulerGeometryLogged = true;
                    Write(_error, "render-rulers: no sphere mesh could be resolved or built, so NO "
                        + "range ruler will be created (see the ruler-geometry line above); this is a "
                        + "geometry failure, not an empty network");
                }

                return;
            }

            if (!RulerMaterials.HasTintCapableMaterial)
            {
                _lastRulersReason = RulerMaterials.HasMaterial
                    ? "no tintable ruler material (see the ruler-material line)"
                    : "no ruler material (see the ruler-material line)";

                if (!_noRulerMaterialLogged)
                {
                    _noRulerMaterialLogged = true;
                    Write(_error, "render-rulers: " + (RulerMaterials.HasMaterial
                        ? "the resolved ruler shader cannot be tinted (it declares no _Color property), "
                          + "so a sphere drawn with it would be an opaque white ball with no way to show "
                          + "connectivity; NO range ruler will be created"
                        : "no ruler material exists, so NO range ruler will be created")
                        + " (see the ruler-material line above); the connection lines carry their own "
                        + "material and are unaffected");
                }

                return;
            }

            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin == null)
            {
                _lastRulersReason = "no plugin instance";
                return;
            }

            NetworkEngine engine = plugin.Network;
            if (engine == null)
            {
                _lastRulersReason = "no network engine";
                return;
            }

            MapCore mapCore;
            if (!TryGetMapCore(game, out mapCore))
            {
                // Shared with the line pass, which warns once per pass under its own prefix; the
                // ruler's own reason token is what the ruler probe reports.
                _lastRulersReason = "no map core";
                return;
            }

            Map3DView map3D = mapCore.map3D;
            if (map3D == null)
            {
                _lastRulersReason = "mapCore.map3D is null";
                return;
            }

            // Captured here, once per pass, for every sphere's own scale arithmetic - and reported by
            // the probe whether or not a ruler is created, so a missing map scale is visible before
            // it can produce a wrongly sized sphere.
            CaptureMap3dScaleInv(map3D);

            Dictionary<IGGuid, Map3DFocusItem> mapItems = map3D.AllMapSelectableItems;
            if (mapItems == null)
            {
                // Normal outside the map view: the map view builds this dictionary on entry and
                // destroys the markers on exit, and a marker-parented ruler dies with its marker.
                _lastRulersReason = "AllMapSelectableItems is null (no map view)";
                return;
            }

            int count = engine.NodeCount;
            _lastRulerNodeCount = count;
            if (count == 0)
            {
                _lastRulersReason = "the engine has no nodes";
                return;
            }

            HashSet<string> keepIds = new HashSet<string>();

            for (int index = 0; index < count; index++)
            {
                NetworkNodeSnapshot node = engine.Snapshot(index);

                if (_rulersDisplayMode == RulersDisplayMode.Relays && !node.IsRelay)
                {
                    continue;
                }

                if (!(node.MaxRange > 0.0))
                {
                    continue;
                }

                _lastRulersConsidered++;

                Map3DFocusItem item;
                if (!TryGetMapItem(mapItems, mapCore, node, out item))
                {
                    continue;
                }

                if (!HasMapItem(item, node.Owner))
                {
                    continue;
                }

                string id = item.AssociatedMapItem.SimGUID.ToString();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                keepIds.Add(id);

                bool connected = index == engine.SourceIndex || engine.PredecessorOf(index) >= 0;

                MapRulerComponent ruler;
                if (Rulers.TryGetValue(id, out ruler) && ruler != null)
                {
                    _lastRulersUpdated++;
                    ruler.SetConnected(connected, null);
                    ruler.CommRange = node.MaxRange;
                    continue;
                }

                MapRulerComponent created = CreateRuler(item, node, connected);

                if (created == null)
                {
                    // No object to keep, so the id must not enter the dictionary - the same rule the
                    // line path follows for a component that refused to configure.
                    keepIds.Remove(id);
                    continue;
                }

                Rulers[created.Id] = created;
                _lastRulersCreated++;
            }

            PruneRulers(keepIds);

            if (_lastRulersCreated + _lastRulersRemoved > 0)
            {
                Write(_log, "render-rulers: " + _lastRulersCreated + " created, " + _lastRulersUpdated
                    + " updated, " + _lastRulersRemoved + " removed, " + Rulers.Count + " live ("
                    + _rulersDisplayMode + ", considered=" + _lastRulersConsidered + ", map items="
                    + mapItems.Count + ")");
            }

            // A distinct token for "the pass ran and drew nothing", because that is the state a
            // failure is most easily mistaken for. `drawn` with `live=0` would have to be read
            // together with `considered=` to mean anything; this says the gate is what refused,
            // which is true for both display modes and is the same in either case - the field that
            // separates them is `mode=`, printed beside it. `considered` counts every node this
            // mode admitted and that has a range, so a non-zero `considered` with `live=0` cannot
            // reach here: those nodes are exactly the ones that were kept.
            _lastRulersReason = _lastRulersConsidered == 0
                ? "no node passed the mode/range gate (see mode=)"
                : "drawn";
        }

        /// <summary>
        /// Creates one ruler object, parented to its marker.
        /// </summary>
        /// <param name="item">The marker the ruler is drawn on. Must be alive and bound.</param>
        /// <param name="node">The node whose range and connectivity the ruler shows.</param>
        /// <param name="connected">Whether the node has an edge in the engine's tree.</param>
        /// <returns>The configured component, or <c>null</c> when it could not be configured.</returns>
        /// <remarks>
        /// <para>
        /// <b>Parented to the marker, not to <c>mapCore.map3D.transform</c>.</b> The legacy parented
        /// its rulers to the map view's own transform; see <see cref="MapRulerComponent"/>'s header,
        /// point 3, for why this port does not.
        /// </para>
        /// <para>
        /// <b><c>SetParent(parent, false)</c>, and the <c>false</c> is load-bearing.</b> Unity's
        /// one-argument <c>SetParent</c> preserves the object's WORLD position, which for a
        /// freshly created object at the origin means writing <c>-marker.position</c> into its local
        /// position - a sphere offset by the marker's own coordinates. The connection path's
        /// one-argument call is safe only because a <c>LineRenderer</c> writes world-space endpoints
        /// and ignores its transform; a mesh object does not have that luxury.
        /// </para>
        /// <para>
        /// The layer is assigned before <c>Track</c> runs, because the sphere child copies the
        /// ruler's layer - so the whole subtree is on the map layer on its first frame rather than a
        /// frame later, which is what the legacy's <c>Start</c>-based assignment cost.
        /// </para>
        /// </remarks>
        private static MapRulerComponent CreateRuler(Map3DFocusItem item, NetworkNodeSnapshot node,
            bool connected)
        {
            GameObject rulerObject = new GameObject(MapRulerComponent.RulerObjectName);

            int layer = ResolveMapLayer();
            if (layer >= 0)
            {
                rulerObject.layer = layer;
            }

            MapRulerComponent ruler = rulerObject.AddComponent<MapRulerComponent>();

            // The connected colour is null on purpose: nothing in this build selects a band colour for
            // a node. See MapRulerComponent's header, point 5.
            if (!ruler.Track(item, connected, null, node.MaxRange))
            {
                UnityEngine.Object.Destroy(rulerObject);
                return null;
            }

            rulerObject.transform.SetParent(item.transform, false);

            if (!_rulerScaleLogged)
            {
                _rulerScaleLogged = true;
                Write(_log, "render-rulers: first ruler node='"
                    + (item.AssociatedMapItem.ItemName ?? "?") + "' relay=" + node.IsRelay
                    + " connected=" + connected
                    + " range=" + node.MaxRange.ToString("0.###E+0", Culture)
                    + " mapScaleInv=" + _map3dScaleInv.ToString("0.###E+0", Culture)
                    + " mapUnits=" + (node.MaxRange / _map3dScaleInv).ToString("0.###", Culture)
                    + " meshRadius=" + RulerGeometry.SphereRadius.ToString("0.###", Culture)
                    + " geometry=" + RulerGeometry.Branch
                    + " markerScale=" + item.transform.lossyScale.ToString("0.###", Culture)
                    + " (the sphere's own localScale is mapUnits / meshRadius)");
            }

            return ruler;
        }

        /// <summary>Destroys every live ruler the pass did not ask for.</summary>
        /// <param name="keepIds">The pass's complete set of wanted ids.</param>
        private static void PruneRulers(HashSet<string> keepIds)
        {
            List<string> removeIds = null;

            foreach (KeyValuePair<string, MapRulerComponent> pair in Rulers)
            {
                if (keepIds.Contains(pair.Key))
                {
                    continue;
                }

                if (removeIds == null)
                {
                    removeIds = new List<string>();
                }

                removeIds.Add(pair.Key);
            }

            if (removeIds == null)
            {
                return;
            }

            for (int i = 0; i < removeIds.Count; i++)
            {
                MapRulerComponent ruler;
                if (!Rulers.TryGetValue(removeIds[i], out ruler))
                {
                    continue;
                }

                // Removed before the destroy, so the OnDestroy handshake that follows finds nothing to
                // do and cannot mutate the dictionary while this loop reads it.
                Rulers.Remove(removeIds[i]);
                _lastRulersRemoved++;

                if (ruler != null)
                {
                    ruler.DestroyRuler();
                }
            }
        }

        /// <summary>
        /// Reads the map's metres-per-unit factor into the static the spheres size themselves against.
        /// </summary>
        /// <param name="map3D">The map view, never null here.</param>
        /// <remarks>
        /// The legacy's <c>GetMap3dScaleInv()</c> was `_mapCore.map3D.GetSpaceProvider().Map3DScaleInv`
        /// with no guard, called from inside each sphere's per-frame <c>Update</c> - so a map view
        /// without a space provider threw once per ruler per frame from inside Unity's update path.
        /// This port resolves the provider once per pass, keeps the last good value when the provider
        /// or the value is unusable, and says so once per session: a sphere sized from a stale factor
        /// is a wrong sphere, and a silent one is worse than a logged one.
        /// </remarks>
        private static void CaptureMap3dScaleInv(Map3DView map3D)
        {
            Map3DSpaceProvider provider = null;

            try
            {
                provider = map3D.GetSpaceProvider();
            }
            catch (Exception exception)
            {
                if (!_mapScaleWarned)
                {
                    _mapScaleWarned = true;
                    Write(_warn, "render-rulers: the map view's space provider threw ("
                        + exception.GetType().Name + ": " + exception.Message + ") - the range spheres "
                        + "keep the last known scale factor (" + _map3dScaleInv.ToString("0.###E+0", Culture)
                        + "); reported once per session");
                }

                return;
            }

            if (provider == null)
            {
                if (!_mapScaleWarned)
                {
                    _mapScaleWarned = true;
                    Write(_warn, "render-rulers: the map view has no space provider, so the range "
                        + "spheres keep the last known scale factor ("
                        + _map3dScaleInv.ToString("0.###E+0", Culture)
                        + "); reported once per session");
                }

                return;
            }

            double scaleInv = provider.Map3DScaleInv;

            // A non-positive or non-finite factor would collapse every sphere to a point or explode it.
            if (!(scaleInv > 0.0) || double.IsInfinity(scaleInv))
            {
                if (!_mapScaleWarned)
                {
                    _mapScaleWarned = true;
                    Write(_warn, "render-rulers: the map scale factor is unusable (" + scaleInv
                        + "), so the range spheres keep the last known value ("
                        + _map3dScaleInv.ToString("0.###E+0", Culture)
                        + "); reported once per session");
                }

                return;
            }

            _map3dScaleInv = scaleInv;
        }

        /// <summary>
        /// Creates one line object, parented to its source marker.
        /// </summary>
        /// <param name="sourceItem">The source map marker. Must be alive.</param>
        /// <param name="targetItem">The target map marker. Must be alive.</param>
        /// <param name="isRelayLink">Whether both ends are relays.</param>
        /// <returns>The configured component, or <c>null</c> when it could not be configured.</returns>
        /// <remarks>
        /// <b>Parented to the source marker, not to <c>mapCore.map3D.transform</c>.</b> The legacy
        /// parented every line to the map view's own transform, which means a line outlives the
        /// marker whose position it reads: the map view tears its markers down and re-creates them on
        /// every entry and on a reference-body change, and a line parented to the map view would have
        /// to be pruned by a refresh pass that may be half a second away. Parenting to the marker is
        /// the sibling port's route and makes Unity destroy the line at the same moment as the marker
        /// it depends on. Endpoints stay world-space regardless (the renderer's default), so the
        /// parenting changes nothing about where the line is drawn.
        /// </remarks>
        private static MapConnectionComponent Create(Map3DFocusItem sourceItem, Map3DFocusItem targetItem,
            bool isRelayLink)
        {
            GameObject lineObject = new GameObject(ConnectionObjectName);
            MapConnectionComponent connection = lineObject.AddComponent<MapConnectionComponent>();

            if (!connection.Configure(sourceItem, targetItem, isRelayLink))
            {
                UnityEngine.Object.Destroy(lineObject);
                return null;
            }

            lineObject.transform.SetParent(sourceItem.transform);

            int layer = ResolveMapLayer();
            if (layer >= 0)
            {
                lineObject.layer = layer;
            }

            return connection;
        }

        /// <summary>
        /// The colour a link between two nodes is drawn in.
        /// </summary>
        /// <param name="engine">The engine, for the per-edge selected band.</param>
        /// <param name="sourceNode">The link's source node.</param>
        /// <param name="targetNode">The link's target node.</param>
        /// <param name="targetIndex">The target's node index, which keys the selected band.</param>
        /// <returns>The band's colour, or the legacy's relay/link fallback.</returns>
        /// <remarks>
        /// <para>
        /// The legacy's rule exactly: the edge's own selected band if the gate picked one, otherwise
        /// the relay colour for a relay-to-relay hop and the plain link colour for everything else.
        /// This is what makes a multi-band network legible - and why the band table had to come back
        /// from the dead in this phase (see <c>NetworkBands</c>'s header).
        /// </para>
        /// <para>
        /// <b>THE single colour seam (U6g).</b> Every drawn link's colour comes out of this method -
        /// both <c>SetColor</c> call sites go through it - so the "Lines" settings' overrides and the
        /// line-opacity multiplier are applied here and nowhere else, by
        /// <see cref="LineAppearance.Apply"/>. The built-in colour stays the argument, so with the
        /// shipped defaults (no key overridden, opacity 1.00) the value returned is the value the
        /// table (or fallback) carries, unchanged.
        /// </para>
        /// </remarks>
        private static Color ResolveColor(NetworkEngine engine, NetworkNodeSnapshot sourceNode,
            NetworkNodeSnapshot targetNode, int targetIndex)
        {
            int bandIndex = engine.SelectedBandOf(targetIndex);
            if (bandIndex >= 0 && bandIndex < NetworkBands.Count)
            {
                return LineAppearance.Apply(LineAppearance.BandSlot(bandIndex), NetworkBands.All[bandIndex].Color);
            }

            return sourceNode.IsRelay && targetNode.IsRelay
                ? LineAppearance.Apply(LineColorSlot.RelayHop, MapConnectionComponent.RelayColor)
                : LineAppearance.Apply(LineColorSlot.Other, NetworkBands.NoBandColor);
        }

        /// <summary>
        /// The active vessel's chain of edges to the control source, as <c>source * count + target</c> keys.
        /// </summary>
        /// <param name="game">The live game instance.</param>
        /// <param name="engine">The engine whose tree is walked.</param>
        /// <param name="count">The node count.</param>
        /// <returns>The pair set, or <c>null</c> when the active vessel has no node.</returns>
        /// <remarks>
        /// The legacy asked its <c>NetworkManager</c> for the path; this port walks the engine's own
        /// spanning tree with <see cref="NetworkEngine.PredecessorOf"/> from the vessel's node up to
        /// the root, which is the same chain by construction. The walk is bounded by <paramref name="count"/>
        /// so a malformed predecessor chain costs a bounded loop rather than a hang.
        /// </remarks>
        private static HashSet<long> BuildActiveVesselPath(GameInstance game, NetworkEngine engine, int count)
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
            if (!view.TryGetActiveSimVessel(out vessel, false) || vessel == null)
            {
                return null;
            }

            int index;
            if (!engine.TryGetIndex(vessel.GlobalId, out index) || index < 0 || index >= count)
            {
                if (!_activeVesselWarnedThisPass)
                {
                    _activeVesselWarnedThisPass = true;
                    Write(_warn, "render-lines: active mode found no network node for the active "
                        + "vessel; nothing is drawn on this pass");
                }

                return null;
            }

            ActivePathPairs.Clear();

            int current = index;
            for (int step = 0; step < count; step++)
            {
                int predecessor = engine.PredecessorOf(current);
                if (predecessor < 0 || predecessor >= count)
                {
                    break;
                }

                ActivePathPairs.Add((long)predecessor * count + current);

                if (predecessor == engine.SourceIndex)
                {
                    break;
                }

                current = predecessor;
            }

            return ActivePathPairs;
        }

        /// <summary>Finds a node's map item, substituting the control source's guid for the KSC's.</summary>
        /// <param name="mapItems">The map view's selectable items.</param>
        /// <param name="mapCore">The map core, for <c>KSCGUID</c>.</param>
        /// <param name="node">The node whose marker is wanted.</param>
        /// <param name="item">Receives the marker, or <c>null</c>.</param>
        /// <returns><c>true</c> when a live marker was found.</returns>
        /// <remarks>
        /// The control source is the KSC, and the KSC's node is owned by the KSC's own guid rather
        /// than by a vessel's - so the lookup key is swapped, which is what makes a KSC-ended link
        /// draw at all. Without the swap every line to the control centre silently disappears. The
        /// lookup itself never indexes: a node whose marker is culled or not yet created warns once
        /// per guid and skips that one link, where the legacy's unguarded index threw a
        /// <c>KeyNotFoundException</c> out of a map callback that the game then swallowed.
        /// </remarks>
        private static bool TryGetMapItem(Dictionary<IGGuid, Map3DFocusItem> mapItems, MapCore mapCore,
            NetworkNodeSnapshot node, out Map3DFocusItem item)
        {
            IGGuid guid = node.IsControlSource ? mapCore.KSCGUID : node.Owner;

            if (mapItems.TryGetValue(guid, out item) && item != null)
            {
                return true;
            }

            item = null;

            if (WarnedMissingMapItems.Add(guid))
            {
                Write(_warn, "render-lines: no map item for node guid " + guid + " (owner "
                    + node.Owner + (node.IsControlSource ? ", the control source" : string.Empty)
                    + ") - that one link is skipped; the marker may be culled or not yet created, and "
                    + "this guid is reported once per session");
            }

            return false;
        }

        /// <summary>Whether a marker the map dictionary returned carries a map item.</summary>
        /// <param name="item">The marker, never null here.</param>
        /// <param name="guid">The node guid the marker was looked up under, for the once-per-session warning.</param>
        /// <returns><c>true</c> when <c>AssociatedMapItem</c> is present.</returns>
        /// <remarks>
        /// <c>GetID</c> reads <c>AssociatedMapItem.SimGUID</c>, so this is the guard that keeps an
        /// unbound marker from throwing out of the pass. <c>Map3DFocusItem</c> itself carries no guid
        /// at all (<c>monodis --method</c>, mlist 58398-58435: <c>get_AssociatedMapItem</c>, the
        /// transform, the target data, and nothing guid-shaped), so the node's own guid is what the
        /// message and the dedup use. The map view binds items a frame or two after it creates the
        /// markers, which is the window this covers.
        /// </remarks>
        private static bool HasMapItem(Map3DFocusItem item, IGGuid guid)
        {
            if (item.AssociatedMapItem != null)
            {
                return true;
            }

            if (WarnedUnboundMapItems.Add(guid))
            {
                Write(_warn, "render-lines: the map marker for guid " + guid + " carries no map item "
                    + "yet - that one link is skipped, and this guid is reported once per session");
            }

            return false;
        }

        /// <summary>Resolves the map core without throwing, warning once per pass when it is absent.</summary>
        private static bool TryGetMapCore(GameInstance game, out MapCore mapCore)
        {
            mapCore = null;

            if (game == null)
            {
                return false;
            }

            if (game.Map.TryGetMapCore(out mapCore) && mapCore != null)
            {
                return true;
            }

            mapCore = null;

            // Once per pass, on purpose: in the flight scene this would otherwise be one line per
            // half-second poll, which buries every other line in the log without adding information.
            if (!_mapCoreWarnedThisPass)
            {
                _mapCoreWarnedThisPass = true;
                Write(_warn, "render-lines: the map core is not available this pass, so no line can be "
                    + "drawn (normal outside a session; reported once per pass)");
            }

            return false;
        }

        /// <summary>
        /// The <c>"Map"</c> layer index, resolved once and never assigned when absent.
        /// </summary>
        /// <returns>The layer index, or <c>-1</c>, which the caller must not assign to <c>gameObject.layer</c>.</returns>
        private static int ResolveMapLayer()
        {
            if (_mapLayerResolved)
            {
                return _mapLayer;
            }

            _mapLayerResolved = true;
            _mapLayer = LayerMask.NameToLayer(MapLayerName);

            if (_mapLayer < 0)
            {
                Write(_warn, "render-lines: LayerMask.NameToLayer(\"" + MapLayerName + "\") returned "
                    + _mapLayer + " - the layer is absent, so the line objects keep layer 0 (a negative "
                    + "index is out of range and is never assigned)");
            }
            else
            {
                Write(_log, "render-lines: layer \"" + MapLayerName + "\" resolved to index " + _mapLayer);
            }

            return _mapLayer;
        }

        private static void Write(Action<string> writer, string message)
        {
            if (writer == null)
            {
                return;
            }

            writer(message);
        }
    }
}
