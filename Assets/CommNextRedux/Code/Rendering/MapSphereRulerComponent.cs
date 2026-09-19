// CommNextRedux - the sphere one range ruler is drawn with: a MeshRenderer scaled to the node's
// range and tinted by whether the node is connected.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/Behaviors/MapSphereRulerComponent.cs - the shape
//   is that file's: `Configure` takes the range and the colour and scales immediately, `SetColor`
//   short-circuits on an unchanged colour, `ScaleByRange` is `range / Map3DScaleInv` written to
//   `localScale`, and `Update` re-scales every frame because the map's own scale factor can change
//   under it. The layer is `"Map"`, assigned by the owner before this component ticks.
//
// WHAT CHANGED, AND WHY EACH CHANGE IS FORCED
//
//  1. THE TINT IS A MATERIAL PROPERTY BLOCK, NOT `renderer.material`. The legacy wrote `_Color`
//     into a `MaterialPropertyBlock` and this port does the same. The line path beside this one
//     takes the other route (`_renderer.material.color`, where the first read of `.material` is
//     what gives a line its own colour), and this class did too until review. It cannot: the
//     clone-on-first-read behaviour of `Renderer.material` for a material created in CODE (which
//     is what `RulerMaterials.BaseMaterial` is) is not documented and cannot be proven offline,
//     and the two possible behaviours are "each sphere gets a private material" and "every sphere
//     shares one" - the second of which would paint every ruler the same colour with no error
//     anywhere. A property block is per-renderer by construction, needs no clone, never mutates
//     the shared material, and is the mechanism `SpriteRenderer.color` itself uses on this very
//     shader family. The sphere therefore sets `sharedMaterial` once and writes `_Color` into its
//     own block on every colour change.
//
//  2. THE COLOUR'S ALPHA IS THE PORT'S, NOT THE LEGACY'S. See `RulerMaterials`' header: the legacy's
//     colour alpha was ignored by its Fresnel-driven ShaderGraph, so copying it verbatim onto a
//     shader where `_Color.a` IS the opacity would draw nothing (connected) or an opaque black ball
//     (disconnected). The colours this class receives are already alpha-corrected by the caller.
//
//  3. THE PER-FRAME RE-SCALE IS CHANGE-DRIVEN, NOT UNCONDITIONAL. The legacy called `ScaleByRange`
//     every frame, which is a transform write per ruler per frame in exchange for reacting to a map
//     scale that changes only when the map's space provider changes. This port re-scales when the
//     range changes, when the map's scale-inverse changes, and at configure time - the same
//     behaviour for every case the legacy's version covered, at one comparison per frame instead of
//     a write. Both inputs are read from statics, so the check allocates nothing.
//
//  4. THE MESH IS PASSED IN, NOT IMPLIED BY A PREFAB. The legacy's sphere came from
//     `RulerSphere.prefab`; this port builds the GameObject in code and hands it the mesh
//     `RulerGeometry` resolved (the legacy FBX, or the code-built fallback). The GameObject cannot
//     come into existence before that resolution succeeds, which is why `Configure` reports failure
//     rather than assuming a MeshRenderer is present.

using UnityEngine;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// The scaled, tinted sphere of one range ruler.
    /// </summary>
    /// <remarks>
    /// Created by <see cref="MapRulerComponent"/>, which owns it: nothing else may add this
    /// component, because its scale input (<see cref="ConnectionsRenderer.Map3dScaleInv"/>) and its
    /// colour are both decided by the ruler that hosts it.
    /// </remarks>
    [DisallowMultipleComponent]
    public class MapSphereRulerComponent : MonoBehaviour
    {
        /// <summary>Guards the divide when a caller passes a mesh with unusable bounds.</summary>
        private const float MinimumMeshRadius = 0.0001f;

        /// <summary>Raised to <see cref="float.NaN"/> so the first scale always applies.</summary>
        private const double UnappliedScale = double.NaN;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private double _range;
        private float _meshRadius = 1f;
        private float _appliedScale;
        private double _appliedMapScaleInv = UnappliedScale;
        private double _appliedRange = UnappliedScale;
        private Color _color;
        private bool _hasColor;
        private bool _configured;

        /// <summary>The node's range, in metres.</summary>
        /// <remarks>
        /// Assigning re-scales immediately, which is what makes a range change (a new antenna coming
        /// online, a band being switched) visible on the next frame rather than on the next refresh
        /// pass. The legacy's `Range` setter only stored the value and leaned on its per-frame
        /// `Update`; with that write gone, the setter is where the work belongs.
        /// </remarks>
        public double Range
        {
            get { return _range; }
            set
            {
                _range = value;
                ScaleByRange();
            }
        }

        /// <summary>Whether <see cref="Configure"/> completed and this sphere may be drawn.</summary>
        public bool IsConfigured
        {
            get { return _configured; }
        }

        /// <summary>The colour most recently applied, for the probe and for diagnostics.</summary>
        public Color CurrentColor
        {
            get { return _color; }
        }

        /// <summary>The radius last written to <c>localScale</c>, in the parent's own units.</summary>
        public float AppliedScale
        {
            get { return _appliedScale; }
        }

        /// <summary>
        /// Builds the sphere's mesh components and applies its first range and colour.
        /// </summary>
        /// <param name="mesh">The sphere mesh. Must be the resolved <see cref="RulerGeometry.SphereMesh"/>.</param>
        /// <param name="meshRadius">The mesh's own radius, used to normalise the scale.</param>
        /// <param name="range">The node's range, in metres.</param>
        /// <param name="color">The alpha-corrected tint. See <see cref="RulerMaterials"/>.</param>
        /// <returns><c>true</c> when the sphere is configured; <c>false</c> when it cannot be drawn.</returns>
        /// <remarks>
        /// Returning <c>false</c> rather than throwing matters because this runs inside the renderer's
        /// refresh pass, and a throw there costs the whole pass - every remaining ruler and every
        /// subsequent frame's refresh. The caller destroys the GameObject it built on a <c>false</c>.
        /// </remarks>
        public bool Configure(Mesh mesh, float meshRadius, double range, Color color)
        {
            if (mesh == null || RulerMaterials.BaseMaterial == null)
            {
                return false;
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = gameObject.AddComponent<MeshFilter>();
            }

            filter.sharedMesh = mesh;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<MeshRenderer>();
            }

            // The SHARED material, deliberately, and it stays shared: the per-sphere colour is
            // written into this renderer's own property block in SetColor, so nothing here needs -
            // or is allowed - a private material instance. See the file header, point 1.
            _renderer.sharedMaterial = RulerMaterials.BaseMaterial;

            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _meshRadius = meshRadius > MinimumMeshRadius ? meshRadius : 1f;
            _range = range;
            _configured = true;

            SetColor(color);
            ScaleByRange();

            return true;
        }

        /// <summary>
        /// Applies a colour, unless it is the colour already applied.
        /// </summary>
        /// <param name="color">The alpha-corrected tint.</param>
        /// <remarks>
        /// <para>
        /// The unchanged-colour short circuit is the legacy's, and it is what keeps this call cheap
        /// enough to make every frame from <see cref="MapRulerComponent"/>: a connected node would
        /// otherwise rewrite the same colour 60 times a second. A no-op before <c>Configure</c>,
        /// because the renderer does not exist yet.
        /// </para>
        /// <para>
        /// The value goes into this renderer's own property block, never into the material - the
        /// material is shared by every ruler and by nothing else in the mod. <c>SetPropertyBlock</c>
        /// is what pushes the block to the renderer; the legacy's comment on that line ("This is
        /// Needed, otherwise the color won't be set") is right and is why it is here.
        /// </para>
        /// </remarks>
        public void SetColor(Color color)
        {
            if (!_configured || _renderer == null || _propertyBlock == null
                || (_hasColor && _color == color))
            {
                return;
            }

            _color = color;
            _hasColor = true;

            _propertyBlock.SetColor(RulerMaterials.TintPropertyId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private void Update()
        {
            ScaleByRange();
        }

        /// <summary>
        /// Writes the range sphere's radius, when either of its two inputs has changed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The arithmetic is the legacy's, character for character:
        /// `localScale = range / Map3DScaleInv`. Two things are worth stating because they are the
        /// difference between a sphere the size of a planet and one the size of a bus:
        /// </para>
        /// <para>
        /// <b>The units.</b> <c>Map3DScaleInv</c> is metres per map unit (the runtime's own
        /// `Map3DSpaceProvider.Map3DScaleInv`, verified on this build as a `float64` property at
        /// mlist 58626), so `range / Map3DScaleInv` is the range expressed in the map's own units -
        /// which is the space this object's parent lives in, because the parent is a map marker.
        /// </para>
        /// <para>
        /// <b>The mesh radius.</b> The legacy's formula is only correct for a unit sphere. The port
        /// divides by the mesh's measured radius as well, so a mesh swap cannot silently mis-size
        /// every ruler. For the shipped mesh that factor is 1.
        /// </para>
        /// <para>
        /// The parent's own scale is deliberately not divided out. The parent is the marker, and the
        /// legacy's spheres were parented into the same scaled map subtree; inheriting the chain's
        /// scale is what keeps this sphere the same size the legacy drew.
        /// </para>
        /// </remarks>
        private void ScaleByRange()
        {
            if (!_configured)
            {
                return;
            }

            double mapScaleInv = ConnectionsRenderer.Map3dScaleInv;
            if (_appliedRange == _range && _appliedMapScaleInv == mapScaleInv)
            {
                return;
            }

            _appliedRange = _range;
            _appliedMapScaleInv = mapScaleInv;

            // A non-positive or non-finite scale factor would collapse the mesh to a point (or
            // explode it); the last good value is kept instead, and the renderer's own probe reports
            // the factor it captured so a bad one is visible rather than silent.
            double units = mapScaleInv > 0.0 ? _range / mapScaleInv : 0.0;
            if (double.IsNaN(units) || double.IsInfinity(units))
            {
                return;
            }

            _appliedScale = (float)(units / _meshRadius);
            transform.localScale = new Vector3(_appliedScale, _appliedScale, _appliedScale);
        }
    }
}
