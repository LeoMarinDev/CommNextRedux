// CommNextRedux - the one material every range-ruler sphere shares, resolved at runtime.
//
// PROVENANCE
//   The ruler's material in the legacy tree was a *packed asset*: `MapSphereRulerComponent`
//   configured a MeshRenderer whose material came from `RulerSphere.prefab` /
//   `CommRulerShader.mat`, a ShaderGraph material. Neither can ship here, for two independent
//   reasons, both already measured in this port:
//
//     1. A serialized shader reference inside the mod's AssetBundle resolves against the set of
//        shaders the player build carries, and it resolves to `Hidden/InternalErrorShader` - a
//        blank white render. Measured in Phase 2 and recorded as D3/D17.
//     2. The build's NO-MATERIAL gate FAILS the build if any material is packed at all
//        (`Tools/build-ui-bundle.sh`, `[audit] NO-MATERIAL OK`). Phase 6 removed the line material
//        from the bundle and made the removal a gate; this phase may pack a MESH and must not pack
//        a material.
//
//   So the material is built in code, exactly as `LineMaterials` does for the lines, from the same
//   lookup chain - and `LineMaterials`'s route is the in-game-proven one: L8 logged
//     `material: shader resolved to "Sprites/Default" (looked up "Sprites/Default" then "Unlit/Transparent")`
//   in this port, and the sibling CommLinesRedux port logged the same line in three launches with
//   its lines user-confirmed visible on the map.
//
// WHY THIS IS A SIBLING CLASS AND NOT A SECOND METHOD ON LineMaterials
//   The two want the same shader chain but different outcomes, and they must be diagnosable apart:
//   a ruler set that fails to draw has to say so on its own log line and in its own probe field,
//   not as a line-render failure. `LineMaterials` is P6's file with P6's gate history; this file
//   carries P7's, and the probe prints each material's resolved shader name separately.
//
// THE LEGACY'S ALPHA SEMANTICS, AND WHY THE PORT CANNOT COPY THE LEGACY'S COLOURS VERBATIM  (D35)
//   The legacy's `MapRulerComponent` held, verbatim
//     DefaultConnectedColor = new Color(1.433962f, 0.8418202f, 0.1826273f, 0f)   // alpha 0
//     DisconnectedColor    = new Color(0.06603771f, 0.01445956f, 0.01090245f, 1f) // alpha 1
//   and `MapSphereRulerComponent` was handed one of the two: the connected branch used the
//   caller's band colour when there was one and `DefaultConnectedColor` (alpha 0) when there was
//   not. The colour's alpha was therefore NOT the sphere's opacity in the legacy - and that is
//   measured, not assumed: the legacy graph is a TRANSPARENT surface with an explicit alpha path
//   (`CommRulerShader.shadergraph`, BuiltInTarget: `m_SurfaceType: 1`, `m_AlphaMode: 0`, and a
//   `SurfaceDescription.Alpha` block node), while the connected constant carries alpha 0 and the
//   legacy demonstrably drew connected spheres. Whatever drives that alpha block, it is not
//   `_Color.a` - so a connected sphere with `_Color.a = 0` was visible in the legacy.
//
//   On the sprite chain this port uses, `_Color.a` IS the opacity - so copying the legacy's
//   literal alpha-0 connected colour would draw nothing at all, which is the same trap D30 already
//   caught on the line path. The port therefore keeps the legacy's colour VALUES and supplies its
//   own alpha, and the two cases are not symmetric on purpose:
//     * connected - a tint strong enough to read a range sphere against the map, weak enough not
//       to veil the planet and the connection lines under it. Two faces of a `Cull Off` sphere
//       stack per pixel, so `alpha = 0.12` accumulates to roughly 23% opacity.
//     * disconnected - alpha 0: the sphere exists but is invisible. The legacy's disconnected
//       colour is near-black (max channel 0.066) and its alpha was likewise not its opacity, so
//       what it looked like is a fact about a ShaderGraph that cannot ship here; the port draws
//       nothing rather than guess, and the alternative - a dark red ball at the alpha a
//       transparent sphere needs - would be an invented look. The object is still created (the
//       legacy created it too) so that a node that connects mid-session gains its sphere on the
//       next tick with no re-allocation. If a later phase wants the disconnected state visible,
//       the change is this one constant's value.
//   Tunable in one place, by design: the two alphas are the only numbers in this file a reviewer
//   should ever need to change.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// The shared ruler-sphere material, resolved once and shared by every ruler.
    /// </summary>
    /// <remarks>
    /// Shared, not cloned: each sphere assigns it as its <c>sharedMaterial</c> and carries its own
    /// colour in a per-renderer <c>MaterialPropertyBlock</c>. See
    /// <see cref="MapSphereRulerComponent"/>'s header for why the port does not use the
    /// <c>renderer.material</c> route the line path uses.
    /// </remarks>
    public static class RulerMaterials
    {
        /// <summary>The shader name resolved first - the one that honours <c>_Color</c> and its alpha.</summary>
        /// <remarks>
        /// Deliberately the same pair as <see cref="LineMaterials"/>: one validated chain in this
        /// port, not two. A second chain would be a second thing to prove in the player.
        /// </remarks>
        public const string PrimaryShaderName = "Sprites/Default";

        /// <summary>The shader name resolved when the primary is absent from the player's build.</summary>
        public const string FallbackShaderName = "Unlit/Transparent";

        /// <summary>The sprite chain's tint property; the sphere's colour and opacity both live here.</summary>
        public const string TintPropertyName = "_Color";

        /// <summary>Depth test, overridden to draw over the planet instead of behind it.</summary>
        private const string ZTestProperty = "_ZTest";

        /// <summary>Opacity of a connected node's range sphere. See the header for why 0.12.</summary>
        public const float ConnectedAlpha = 0.12f;

        /// <summary>Opacity of a disconnected node's range sphere - the object exists, it draws nothing.</summary>
        public const float DisconnectedAlpha = 0f;

        /// <summary>
        /// The legacy's connected RGB, carried verbatim.
        /// </summary>
        /// <remarks>
        /// The red component is above 1 on purpose - it is an HDR emission colour from the legacy's
        /// ShaderGraph, and it is carried rather than clamped so the port's colour is recognisably
        /// the legacy's. On the sprite chain the headroom costs nothing and cannot blow out: the
        /// blend multiplies by alpha first (<c>0.12 * 1.43396 = 0.172</c>), so no channel reaches
        /// the LDR target's ceiling; the only effect is a slightly stronger red than an LDR gold of
        /// the same hue could give.
        /// </remarks>
        public static readonly Color ConnectedTint =
            new Color(1.433962f, 0.8418202f, 0.1826273f, ConnectedAlpha);

        /// <summary>The legacy's disconnected RGB, carried verbatim, at <see cref="DisconnectedAlpha"/>.</summary>
        public static readonly Color DisconnectedTint =
            new Color(0.06603771f, 0.01445956f, 0.01090245f, DisconnectedAlpha);

        /// <summary>
        /// The <c>_Color</c> property id: the tint every sphere writes, and the capability check.
        /// </summary>
        /// <remarks>
        /// Public because <see cref="MapSphereRulerComponent"/> writes the same property into its
        /// per-renderer property block - one id for the whole mod, hashed once at first access.
        /// </remarks>
        public static readonly int TintPropertyId = Shader.PropertyToID(TintPropertyName);

        /// <summary>The shared ruler material, or <c>null</c> when neither shader resolved.</summary>
        /// <remarks>
        /// Assigned only by <see cref="GenerateMaterial"/>. Every sphere assigns this instance as
        /// its <c>sharedMaterial</c> and never reads <c>renderer.material</c>, so exactly one
        /// material exists for the whole ruler set and no sphere can ever mutate another's colour -
        /// the tint travels in each renderer's own property block. See
        /// <see cref="MapSphereRulerComponent"/>'s header.
        /// </remarks>
        public static Material BaseMaterial { get; private set; }

        /// <summary>The shader name that actually resolved, or <c>null</c> when neither did.</summary>
        public static string ResolvedShaderName { get; private set; }

        /// <summary>Whether <see cref="GenerateMaterial"/> produced a material at all.</summary>
        public static bool HasMaterial
        {
            get { return BaseMaterial is not null; }
        }

        /// <summary>
        /// Whether the resolved material can be tinted, i.e. declares <c>_Color</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This is the gate the rulers need and the lines do not.</b> The fallback shader
        /// (<c>Unlit/Transparent</c>) declares no <c>_Color</c> at all: it samples its texture and
        /// returns it, so on that branch every sphere would be opaque white - a solid ball that
        /// hides the markers behind it - and every colour assignment would be a silent no-op
        /// (<c>Material.color</c> against a shader with no <c>_Color</c> logs a warning and does
        /// nothing). The number of range spheres the player gets in that case is not "a slightly
        /// wrong sphere"; it is an object that makes the map harder to read than no ruler at all.
        /// So the ruler path refuses to create objects unless the resolved material can carry a
        /// tint, and says so with a distinct state token.
        /// </para>
        /// <para>
        /// On this machine the primary resolves, so this is a guard against a different player
        /// build, not a known failure. It is checked rather than assumed because the whole point of
        /// the fallback existing is that a different build is the case it exists for.
        /// </para>
        /// </remarks>
        public static bool HasTintCapableMaterial
        {
            get { return BaseMaterial is not null && BaseMaterial.HasProperty(TintPropertyId); }
        }

        /// <summary>
        /// Resolves the shader and builds the shared ruler material.
        /// </summary>
        /// <param name="log">Receives the resolution line. The plugin supplies its null-guarded writer.</param>
        /// <param name="logWarning">Receives a fallback or a missing tint property. Null-guarded writer.</param>
        /// <param name="logError">Receives the fatal "no shader at all" line. Null-guarded writer.</param>
        /// <returns><c>true</c> when a material was built; <c>false</c> when both names missed.</returns>
        /// <remarks>
        /// Idempotent in effect rather than by a guard, exactly like
        /// <see cref="LineMaterials.GenerateMaterial"/>: a second call replaces the material with an
        /// equivalent one, and every renderer that already read its own <c>material</c> holds a
        /// private copy. Calling it twice is the retry path after a graphics-side failure.
        /// </remarks>
        public static bool GenerateMaterial(Action<string> log, Action<string> logWarning, Action<string> logError)
        {
            Shader shader = Shader.Find(PrimaryShaderName) ?? Shader.Find(FallbackShaderName);

            if (shader is null)
            {
                BaseMaterial = null;
                ResolvedShaderName = null;

                Write(logError, "ruler-material: NEITHER shader resolved - looked up \"" + PrimaryShaderName
                    + "\" then \"" + FallbackShaderName + "\". No range ruler will be created at all "
                    + "(see the renderer's refusal line); this is a shader-resolution failure in the "
                    + "player, not a network failure. The connection lines carry their own, separate "
                    + "lookup and are unaffected.");
                return false;
            }

            ResolvedShaderName = shader.name;

            if (shader.name != PrimaryShaderName)
            {
                Write(logWarning, "ruler-material: \"" + PrimaryShaderName + "\" did not resolve; fell "
                    + "back to \"" + shader.name + "\".");
            }

            Material material = new Material(shader)
            {
                color = ConnectedTint
            };

            // The same pair as the lines, for the same reason: a range sphere is centred on a map
            // marker and usually engulfs the planet, so one that is occluded by the planet reads as
            // "it did not draw". A silent no-op on a shader that does not declare _ZTest.
            material.SetInt(ZTestProperty, (int)CompareFunction.Always);
            material.renderQueue = (int)RenderQueue.Overlay;

            BaseMaterial = material;

            bool tintable = material.HasProperty(TintPropertyId);

            Write(log, "ruler-material: shader resolved to \"" + shader.name + "\" (looked up \""
                + PrimaryShaderName + "\" then \"" + FallbackShaderName + "\"); " + ZTestProperty + "="
                + (int)CompareFunction.Always + ", renderQueue=" + material.renderQueue + ", "
                + TintPropertyName + "=" + tintable + ".");

            if (!tintable)
            {
                Write(logWarning, "ruler-material: the resolved shader \"" + shader.name + "\" declares no "
                    + TintPropertyName + " property, so a range sphere drawn with it would be an opaque "
                    + "white ball with no way to tell a connected node from a disconnected one. The "
                    + "ruler path will refuse to create spheres and report state=no tintable material; "
                    + "the connection lines are unaffected.");
            }

            return true;
        }

        private static void Write(Action<string> writer, string message)
        {
            if (writer is null)
            {
                return;
            }

            writer(message);
        }
    }
}
