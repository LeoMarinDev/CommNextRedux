// CommNextRedux - one range ruler: the host component that owns a node's sphere and keeps it in
// step with that node's connectivity and range.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/Behaviors/MapRulerComponent.cs - the shape is
//   that file's: `Track` binds the ruler to a map marker and builds the sphere in one call (the
//   legacy's comment: pass everything now "to avoid glitches when the ruler is rendered"), `Update`
//   re-applies the colour every frame and hands off to the renderer when its marker dies,
//   `OnDestroy` deregisters, and `Id` is the marker's own `AssociatedMapItem.SimGUID` as a string.
//   The renderer keys its `_rulers` dictionary on exactly that string, which is also the key it
//   used to find the marker - so the identity is the node's guid, not an object reference. That is
//   the shape the repo's hard rule 6 requires: a save load rebuilds the simulation object graph, and
//   an object-reference-keyed registry accumulates orphans whose `SimGUID` reads back null.
//
// WHAT CHANGED, AND WHY EACH CHANGE IS FORCED
//
//  1. THE PREFAB INSTANTIATION IS GONE, THE OBJECT IS BUILT IN CODE. The legacy called
//     `Instantiate(ConnectionsRenderer.RulerSpherePrefab, transform)`; that prefab carries a
//     material reference and cannot ship at this pin (D3), so the sphere is a new GameObject with a
//     MeshFilter and MeshRenderer, fed the mesh `RulerGeometry` resolved and the material
//     `RulerMaterials` built. Same hierarchy, different origin.
//
//  2. THE DEAD PLACEHOLDER BLOCK IS DROPPED, NOT PORTED. The legacy's `#if
//     SHOW_RELAY_PLACEHOLDER` block drew a second 50 km grey sphere on relay nodes. The symbol is
//     defined nowhere in the legacy tree (`rg SHOW_RELAY_PLACEHOLDER` finds only the two places it
//     is tested), so the block was unreachable in every build the legacy ever produced. Porting
//     dead code is not fidelity.
//
//  3. THE PARENT IS THE MARKER, NOT THE MAP VIEW. The legacy parented its rulers to
//     `_mapCore.map3D.transform`, which means a ruler survives the destruction of the marker whose
//     guid it is registered under: the map view tears every marker down on a map exit and on a
//     reference-body change, and a ruler parented to the map view then has to be pruned by a refresh
//     pass that can be half a second away - and is never pruned at all if the map view itself is
//     torn down first. Parenting to the marker makes Unity destroy the ruler at the same instant as
//     the marker it belongs to. This is the port's own D28 discipline for the connection lines, and
//     the rulers follow it. The sphere is a child of the ruler, so the whole ruler is one subtree.
//
//  4. THE RULER NO LONGER RE-POSITIONS ITSELF. The legacy wrote
//     `transform.position = target.transform.position` every frame because its parent was the map
//     view; a marker-parented ruler is already exactly on its marker with `localPosition` zero, so
//     that write - and the stale-position failure mode where a destroyed marker's position is read -
//     is gone with the parenting change.
//
//  5. THE CONNECTED COLOUR IS THE PORT'S. The legacy's nullable `ConnectedColor` came from the
//     selected-band dropdown, which this build has no producer for: P6 replaced the legacy's global
//     `SelectedBandIndex` with a per-edge `NetworkEngine.SelectedBandOf`, and nothing selects a band
//     for a NODE. The parameter is kept so the decision has a home, and the renderer passes `null`,
//     which resolves to `RulerMaterials.ConnectedTint`. See Deploy/obj/divergences.md.
//
//  6. THE LAYER IS ASSIGNED IN CODE, NOT IN `Start`. The legacy's `Start` set the "Map" layer, which
//     is a frame late for an object created during a refresh pass. The renderer assigns the layer to
//     the ruler GameObject before `Track` runs, and `Track` copies it onto the sphere - so the whole
//     subtree is on the map layer on its first frame.

using KSP.Map;
using UnityEngine;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// One node's range ruler: the host that owns a <see cref="MapSphereRulerComponent"/>.
    /// </summary>
    /// <remarks>
    /// Created and pruned by <see cref="ConnectionsRenderer"/>, which owns the guid-keyed dictionary
    /// this component registers itself into and out of. Nothing else may add this component: its
    /// <c>Update</c> and <c>OnDestroy</c> handshakes both assume that owner.
    /// </remarks>
    [DisallowMultipleComponent]
    public class MapRulerComponent : MonoBehaviour, IMapComponent
    {
        /// <summary>The name every ruler GameObject carries, so a scene dump identifies them.</summary>
        public const string RulerObjectName = "CommNextRedux Ruler";

        /// <summary>The name the sphere child carries, as in the legacy.</summary>
        public const string SphereObjectName = "RulerSphere";

        /// <summary>The map item this ruler is drawn on. Re-read every frame; may die with the map.</summary>
        public Map3DFocusItem TargetItem { get; private set; }

        /// <summary>The guid-keyed identity this ruler is registered under.</summary>
        /// <remarks>
        /// The marker's own <c>AssociatedMapItem.SimGUID</c> as a string - the same value the
        /// renderer used to find the marker in the map view's dictionary, and the same value the
        /// legacy keyed on. A string, never the guid or the marker object: see the file header.
        /// </remarks>
        public string Id { get; set; }

        /// <summary>Whether the node this ruler belongs to has a live edge in the engine's tree.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>The sphere this ruler hosts, or <c>null</c> before a successful <see cref="Track"/>.</summary>
        public MapSphereRulerComponent Sphere
        {
            get { return _sphere; }
        }

        /// <summary>The node's range in metres, forwarded to the sphere.</summary>
        /// <remarks>
        /// The legacy's own forwarding property. Assigning it re-scales through the sphere's setter,
        /// which is how a range change between two refresh passes becomes visible immediately.
        /// </remarks>
        public double CommRange
        {
            get { return _sphere != null ? _sphere.Range : 0.0; }
            set
            {
                if (_sphere != null)
                {
                    _sphere.Range = value;
                }
            }
        }

        private bool _isTracking;
        private Map3DFocusItem _target;
        private MapSphereRulerComponent _sphere;
        private Color? _connectedColor;

        /// <summary>
        /// Binds this ruler to a marker and builds its sphere.
        /// </summary>
        /// <param name="target">The marker to draw the ruler on. Must be alive and bound.</param>
        /// <param name="isConnected">Whether the node has a live edge in the tree.</param>
        /// <param name="connectedColor">A band tint, or <c>null</c> for the port's own. See the header.</param>
        /// <param name="commRange">The node's range, in metres.</param>
        /// <returns><c>true</c> when the ruler is configured; <c>false</c> when the caller must discard it.</returns>
        /// <remarks>
        /// Fails rather than throws for the same reason the sphere's <c>Configure</c> does: this runs
        /// inside the renderer's refresh pass, and a throw here costs every remaining ruler and every
        /// later pass too. A failure means the marker carries no map item to derive an identity from,
        /// or the sphere could not be built - and in both cases there is no object worth keeping.
        /// </remarks>
        public bool Track(Map3DFocusItem target, bool isConnected, Color? connectedColor, double commRange)
        {
            if (target == null)
            {
                return false;
            }

            MapItem mapItem = target.AssociatedMapItem;
            if (mapItem == null)
            {
                return false;
            }

            string id = mapItem.SimGUID.ToString();
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            Id = id;
            _target = target;
            _connectedColor = connectedColor;
            IsConnected = isConnected;

            GameObject sphereObject = new GameObject(SphereObjectName);
            sphereObject.transform.SetParent(transform, false);
            sphereObject.transform.localPosition = Vector3.zero;

            // The ruler's layer was assigned by the renderer before this call, so the child inherits
            // it here rather than a frame later - see the header, point 6.
            sphereObject.layer = gameObject.layer;

            _sphere = sphereObject.AddComponent<MapSphereRulerComponent>();

            if (!_sphere.Configure(RulerGeometry.SphereMesh, RulerGeometry.SphereRadius, commRange, ResolveColor()))
            {
                UnityEngine.Object.Destroy(sphereObject);
                _sphere = null;
                return false;
            }

            _isTracking = true;

            return true;
        }

        /// <summary>
        /// Updates this ruler's connectivity and tint.
        /// </summary>
        /// <param name="isConnected">Whether the node has a live edge in the tree.</param>
        /// <param name="connectedColor">A band tint, or <c>null</c> for the port's own.</param>
        /// <remarks>
        /// The sphere's own <c>SetColor</c> short-circuits an unchanged colour, so this is cheap
        /// enough to call on every refresh pass for every ruler.
        /// </remarks>
        public void SetConnected(bool isConnected, Color? connectedColor)
        {
            IsConnected = isConnected;
            _connectedColor = connectedColor;

            if (_sphere != null)
            {
                _sphere.SetColor(ResolveColor());
            }
        }

        /// <summary>Destroys this ruler's GameObject, and the sphere child with it.</summary>
        /// <remarks>
        /// The renderer removes this component from its dictionary <em>before</em> calling this, so
        /// the <c>OnDestroy</c> handshake below finds nothing to remove and cannot corrupt an
        /// iteration. <c>Destroy</c> is deferred to the end of the frame, which is why that order
        /// matters rather than being tidiness.
        /// </remarks>
        public void DestroyRuler()
        {
            UnityEngine.Object.Destroy(gameObject);
        }

        /// <summary>
        /// Re-applies the colour, and hands off to the renderer when the marker is gone.
        /// </summary>
        /// <remarks>
        /// <b>Unity's overloaded null, used deliberately.</b> A marker the map view has destroyed
        /// reads as null here and does NOT read as null under `is null`; reading
        /// <c>AssociatedMapItem</c> off one throws a <c>MissingReferenceException</c> from inside
        /// <c>Update</c>. The same check appears in <see cref="MapConnectionComponent"/> for the same
        /// reason.
        /// </remarks>
        private void Update()
        {
            if (!_isTracking)
            {
                return;
            }

            if (_target == null)
            {
                ConnectionsRenderer.Destroyed(this);
                return;
            }

            if (_sphere != null)
            {
                _sphere.SetColor(ResolveColor());
            }
        }

        private Color ResolveColor()
        {
            return IsConnected
                ? (_connectedColor ?? RulerMaterials.ConnectedTint)
                : RulerMaterials.DisconnectedTint;
        }

        /// <summary>
        /// Tells the renderer this ruler is gone. Called by Unity on every path that destroys the
        /// GameObject, including a map teardown that destroys the marker this ruler is parented to.
        /// </summary>
        private void OnDestroy()
        {
            if (!_isTracking)
            {
                return;
            }

            ConnectionsRenderer.Destroyed(this);
        }
    }
}
