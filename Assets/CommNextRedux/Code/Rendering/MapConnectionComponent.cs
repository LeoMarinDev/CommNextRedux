// CommNextRedux - one drawn CommNet link: a LineRenderer whose endpoints are two map markers.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/Behaviors/MapConnectionComponent.cs - the shape
//   is this file's: `InternalConfigure` builds the renderer, `Configure` colours it, `Update`
//   re-reads both marker transforms every frame and hands off to the renderer when a marker dies,
//   `OnDestroy` deregisters, and `GetID` is the pair key. The legacy also carried the vessel-report
//   half (`ConfigureForReport`, `SetNetworkConnection`, the `_isSource`/`_Report`/`_Length`/
//   `_Connected` shader properties); that half is dropped here - see below.
//
// WHAT CHANGED, AND WHY EACH CHANGE IS FORCED
//
//  1. THE MATERIAL IS THE PROVEN ROUTE'S, NOT THE SHADERGRAPH'S. The legacy set a
//     MaterialPropertyBlock and wrote `_Color`/`_Report`/`_Length`/`_Connected` into it, because
//     its own ShaderGraph read those four by name. That material cannot ship at this pin (P2's
//     measurement) and the replacement is a built-in sprite shader whose only colour input is the
//     material's own `_Color`. So the colour is assigned with `_renderer.material.color = color`,
//     which is the route the in-game-validated sibling port uses: the first read of `.material`
//     instantiates a private copy per line, so two lines never fight over one shared material.
//
//  2. NO COLOR GRADIENT, DELIBERATELY. The legacy set a gradient whose colour keys were
//     `Color.black` at both ends with an alpha ramp, and that was coherent only because its
//     ShaderGraph ignored vertex colour and read `_Color` from the property block. On the proven
//     route the built-in shader MULTIPLIES the vertex colour by `_Color`, so a black gradient would
//     draw black lines - a silent, plausible-looking wrong answer. The gradient is therefore not
//     set at all, which leaves LineRenderer's default white-to-white vertex colours and lets
//     `_Color` through unchanged. Recorded in Deploy/obj/divergences.md.
//
//  3. TWO VERTICES, NOT TEN. The legacy laid ten interpolated vertices between its endpoints for
//     the shader's arrow/flow effect; with that effect dropped (a plain line has nothing to
//     interpolate) a straight link needs exactly its two endpoints. The sibling port's proven
//     `PositionCount = 2` is the shape kept here.
//
//  4. THE REPORT HALF IS P8'S. `ConfigureForReport` and the three report-only shader properties
//     existed to drive the vessel report window's arrows. The user's decision is the proven route
//     only, so the animated report lines are dropped, not stubbed. See Deploy/obj/divergences.md.
//
// WHERE THE PER-FRAME RE-READ COMES FROM
//   Both endpoints are re-read every frame rather than cached at setup time. The map markers move:
//   the map view re-positions a vessel's marker continuously and re-creates markers wholesale when
//   the camera's reference body changes. A cached position detaches the line from its marker on the
//   first frame the marker moves, which reads as "the line is in the wrong place" rather than as a
//   caching bug.

using CommNextRedux.Network.Bands;
using KSP.Map;
using KSP.Sim.impl;
using UnityEngine;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// One comm link drawn on the map: a two-vertex <c>LineRenderer</c> between two map markers.
    /// </summary>
    /// <remarks>
    /// Created and pruned by <see cref="ConnectionsRenderer"/>, which owns the pair-keyed
    /// dictionary this component registers itself into and out of. Nothing else may add this
    /// component: its <c>OnDestroy</c> handshake assumes that owner.
    /// </remarks>
    [DisallowMultipleComponent]
    public class MapConnectionComponent : MonoBehaviour, IMapComponent
    {
        /// <summary>
        /// The line's width, in the map view's own scale.
        /// </summary>
        /// <remarks>
        /// The sibling port's proven value (<c>CommLineConnection.Width</c>), not the legacy's
        /// <c>startWidth</c>/<c>endWidth</c> pair of 0.045/0.04: those two drove a ten-vertex line
        /// with a taper, and a two-vertex line has no taper to describe. <c>widthMultiplier</c>
        /// rather than <c>startWidth</c>/<c>endWidth</c> so the two endpoints cannot drift apart.
        /// </remarks>
        public const float LineWidth = 0.03f;

        /// <summary>The number of vertices a straight link needs: its two endpoints.</summary>
        public const int PositionCount = 2;

        /// <summary>
        /// The colour of a link whose two ends are both relays and for which no band was selected.
        /// </summary>
        /// <remarks>
        /// The legacy's <c>RelayColor</c> (<c>MapConnectionComponent.cs:33</c>), RGB for RGB. A
        /// relay-to-relay hop is the trunk of the network and the legacy marked it visibly; the
        /// non-relay fallback is <see cref="NetworkBands.NoBandColor"/>.
        /// </remarks>
        public static readonly Color RelayColor = new Color(0.215f, 0.858f, 0.847f, 1.0f);

        /// <summary>The map item this link starts at. Re-read every frame; may die with the map.</summary>
        public Map3DFocusItem SourceItem { get; private set; }

        /// <summary>The map item this link ends at. Re-read every frame; may die with the map.</summary>
        public Map3DFocusItem TargetItem { get; private set; }

        /// <summary>The pair key this link is registered under. See <see cref="GetID"/>.</summary>
        public string Id { get; set; }

        /// <summary>Whether both ends are relays, which decides the fallback colour.</summary>
        public bool IsRelayLink { get; private set; }

        /// <summary>The colour last applied, after the band/relay fallback was resolved.</summary>
        public Color CurrentColor { get; private set; }

        private LineRenderer _renderer;
        private Vector3[] _positions;
        private bool _configured;

        /// <summary>
        /// The stable key for a link between two map items.
        /// </summary>
        /// <param name="source">One end's map item.</param>
        /// <param name="target">The other end's map item.</param>
        /// <returns><c>"&lt;guid&gt;-&lt;guid&gt;"</c>, with the two guids in a canonical order.</returns>
        /// <remarks>
        /// <b>Canonical, not order-of-arguments.</b> The pair is sorted by ordinal string comparison
        /// so that the same two markers always produce the same key whichever way round the caller
        /// names them. That matters because the key is the identity of a drawn line: an
        /// order-of-arguments key would draw a second, exactly overlapping line the first time a
        /// rebuild happened to name the same pair the other way round, and the only symptom would be
        /// a slightly brighter line - the renderer's <c>_connections</c> dictionary is keyed on this
        /// string alone and does a single, unidirectional lookup.
        /// </remarks>
        public static string GetID(Map3DFocusItem source, Map3DFocusItem target)
        {
            string sourceId = source.AssociatedMapItem.SimGUID.ToString();
            string targetId = target.AssociatedMapItem.SimGUID.ToString();

            return string.CompareOrdinal(sourceId, targetId) <= 0
                ? sourceId + "-" + targetId
                : targetId + "-" + sourceId;
        }

        /// <summary>
        /// Builds the renderer and binds this component to two markers.
        /// </summary>
        /// <param name="source">The link's start marker. Must be alive.</param>
        /// <param name="target">The link's end marker. Must be alive.</param>
        /// <param name="isRelayLink">Whether both ends are relays.</param>
        /// <returns><c>true</c> when the component is usable; <c>false</c> when a marker carries no map item and the caller must discard this object.</returns>
        /// <remarks>
        /// The material is assigned from <see cref="LineMaterials.BaseMaterial"/> only; the caller
        /// checks <see cref="LineMaterials.HasMaterial"/> first, because creating this object with a
        /// null material produces a link that is invisible without ever throwing.
        /// </remarks>
        public bool Configure(Map3DFocusItem source, Map3DFocusItem target, bool isRelayLink)
        {
            if (source == null || target == null)
            {
                return false;
            }

            MapItem sourceItem = source.AssociatedMapItem;
            MapItem targetItem = target.AssociatedMapItem;
            if (sourceItem == null || targetItem == null)
            {
                return false;
            }

            SourceItem = source;
            TargetItem = target;
            IsRelayLink = isRelayLink;
            Id = GetID(source, target);

            _renderer = gameObject.AddComponent<LineRenderer>();
            _renderer.material = LineMaterials.BaseMaterial;
            _renderer.widthMultiplier = LineWidth;
            _renderer.positionCount = PositionCount;

            // useWorldSpace is deliberately left at its default (true): the endpoints written below
            // are world-space marker positions. The object is parented to the source marker only so
            // that the map view's own teardown destroys the line with the marker it belongs to.

            _positions = new Vector3[PositionCount];
            _configured = true;

            UpdatePositions();

            return true;
        }

        /// <summary>
        /// Applies a colour to this one line.
        /// </summary>
        /// <param name="color">The resolved colour. Its alpha is kept: the band table's alpha is 1.</param>
        /// <remarks>
        /// A no-op before <see cref="Configure"/> has completed, and that guard is load-bearing: a
        /// refresh pass colours every live link, including one on the pass it was created.
        /// </remarks>
        public void SetColor(Color color)
        {
            if (!_configured || _renderer == null)
            {
                return;
            }

            CurrentColor = color;

            // Reading `.material` (not `.sharedMaterial`) is what gives each line its own colour:
            // the first read instantiates a copy of the shared material and assigns it back.
            _renderer.material.color = color;
        }

        /// <summary>Destroys this component's GameObject, which removes the renderer with it.</summary>
        /// <remarks>
        /// The renderer removes this component from its dictionary <em>before</em> calling this, so
        /// the <c>OnDestroy</c> handshake below finds nothing to remove and cannot corrupt an
        /// iteration. <c>Destroy</c> is deferred to the end of the frame, which is why that order
        /// matters rather than being tidiness.
        /// </remarks>
        public void DestroyConnection()
        {
            UnityEngine.Object.Destroy(gameObject);
        }

        /// <summary>Re-reads both marker transforms and writes them to the renderer.</summary>
        private void Update()
        {
            if (!_configured)
            {
                return;
            }

            // Unity's overloaded null, used deliberately: a marker the map view has destroyed reads
            // as null here and does NOT read as null under `is null`. Reading `.transform.position`
            // off one throws MissingReferenceException from inside Update, so this check is the
            // difference between a clean teardown and a flood of exceptions attributed to nothing.
            if (SourceItem == null || TargetItem == null)
            {
                DestroyConnection();
                return;
            }

            UpdatePositions();
        }

        private void UpdatePositions()
        {
            if (_renderer == null || _positions == null)
            {
                return;
            }

            _positions[0] = SourceItem.transform.position;
            _positions[1] = TargetItem.transform.position;

            _renderer.SetPositions(_positions);
        }

        /// <summary>
        /// Tells the renderer this link is gone. Called by Unity on every path that destroys the
        /// GameObject, including a map-view teardown that destroys the marker this line was
        /// parented to.
        /// </summary>
        private void OnDestroy()
        {
            ConnectionsRenderer.Destroyed(this);
        }
    }
}
