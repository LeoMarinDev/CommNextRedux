// CommNextRedux - the one material every map connection line shares, resolved at runtime.
//
// PROVENANCE
//   mods/CommLinesRedux/Assets/CommLinesRedux/Code/Visuals/PluginMaterials.cs - the in-game
//   validated route this class is ported from, line for line: the lookup chain, the logging, the
//   refusal, and the _ZTest/render-queue pair. That port reads its shader with `Shader.Find` and
//   its log records the PRIMARY name resolving on this machine's player.
//
// WHY A RUNTIME Shader.Find AND NOT A MATERIAL IN THE BUNDLE
//   Phase 2 measured a SERIALIZED shader reference inside the mod's AssetBundle and found it
//   resolving to `Hidden/InternalErrorShader`: a serialized reference resolves against the set of
//   shaders the player build actually carries, and the built-in set a stock Unity player carries
//   does not include the sprite shaders by serialized path. That measurement stands.
//
//   `Shader.Find` is a DIFFERENT mechanism - it searches the shaders the player has loaded,
//   including the built-in ones - and the sibling port proves it SUCCEEDS: three separate launches
//   each logged
//     `[CommLinesRedux] material: shader resolved to "Sprites/Default" (looked up "Sprites/Default"
//     then "Unlit/Transparent")`
//   (`mods/CommLinesRedux/Deploy/obj/launch-2-matrix.md:59`, `launch-3-matrix.md:43`,
//   `launch-5-matrix.md:43`), with its FINAL-REPORT.md recording the lines as user-confirmed
//   visible in map view. The PRIMARY resolved, not the fallback.
//
//   So this port ships NO material asset at all and builds the material in code, exactly as the
//   sibling port does. Phase 6 removed `commnextlinemat.mat` from the bundle outright and made
//   the removal a gate: `Tools/build-ui-bundle.sh` fails the build if ANY material is packed
//   (`[audit] NO-MATERIAL OK`), because a packed material is dead weight carrying exactly the
//   serialized reference measured above. See Deploy/obj/divergences.md, D3.
//
// WHY A NULL SHADER IS THE DANGEROUS CASE
//   `new Material(null)` succeeds, the LineRenderer accepts it, the colour is set, nothing throws -
//   and the player sees no line at all. The failure is silent and indistinguishable from "the mod
//   is dead", so every outcome below is logged, and the renderer refuses to create objects when the
//   lookup failed rather than filling the map with invisible ones.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// The shared line material, resolved once and cloned per line by the renderer.
    /// </summary>
    public static class LineMaterials
    {
        /// <summary>The shader name resolved first - the one that honours <c>_Color</c> and its alpha.</summary>
        public const string PrimaryShaderName = "Sprites/Default";

        /// <summary>The shader name resolved when the primary is absent from the player's build.</summary>
        public const string FallbackShaderName = "Unlit/Transparent";

        private const string ZTestProperty = "_ZTest";

        /// <summary>
        /// The shared line material, or <c>null</c> when neither shader resolved.
        /// </summary>
        /// <remarks>
        /// Assigned only by <see cref="GenerateMaterial"/>. Every line's renderer is handed this
        /// instance and then reads its own <c>material</c> property, which instantiates a per-line
        /// copy on first read - so one shared material and one colour per line coexist. A
        /// <c>null</c> value means nothing may be drawn; the renderer checks
        /// <see cref="HasMaterial"/> first.
        /// </remarks>
        public static Material BaseMaterial { get; private set; }

        /// <summary>The shader name that actually resolved, or <c>null</c> when neither did.</summary>
        public static string ResolvedShaderName { get; private set; }

        /// <summary>Whether <see cref="GenerateMaterial"/> produced a usable material.</summary>
        public static bool HasMaterial
        {
            get { return BaseMaterial != null; }
        }

        /// <summary>
        /// Resolves the shader and builds the shared material.
        /// </summary>
        /// <param name="log">Receives the resolution line. The plugin supplies its null-guarded writer.</param>
        /// <param name="logWarning">Receives a shader-name miss. The plugin supplies its null-guarded writer.</param>
        /// <param name="logError">Receives the fatal "no shader at all" line. The plugin supplies its null-guarded writer.</param>
        /// <returns><c>true</c> when a material was built; <c>false</c> when both names missed.</returns>
        /// <remarks>
        /// <para>
        /// Idempotent in effect rather than by a guard: a second call replaces the material with an
        /// equivalent one, which is harmless because every renderer that has already read its own
        /// <c>material</c> holds a private copy and is unaffected. Calling it twice is therefore a
        /// cheap way to retry after a graphics-side failure the first time (the sibling port builds
        /// it in <c>OnInitialized</c> for exactly that reason: a shader lookup is a graphics-side
        /// call, and the earlier hook is not the place for one).
        /// </para>
        /// <para>
        /// A null writer is tolerated - the plugin's writers are null-guarded too, but a diagnostics
        /// path that can throw while reporting a failure is worse than one that says nothing.
        /// </para>
        /// </remarks>
        public static bool GenerateMaterial(Action<string> log, Action<string> logWarning, Action<string> logError)
        {
            Shader shader = Shader.Find(PrimaryShaderName) ?? Shader.Find(FallbackShaderName);

            if (shader == null)
            {
                BaseMaterial = null;
                ResolvedShaderName = null;

                Write(logError, "material: NEITHER shader resolved - looked up \"" + PrimaryShaderName
                    + "\" then \"" + FallbackShaderName + "\". No connection line will be created at "
                    + "all (see the renderer's refusal line); this is a shader-resolution failure in "
                    + "the player, not a connection failure.");
                return false;
            }

            ResolvedShaderName = shader.name;

            if (shader.name != PrimaryShaderName)
            {
                Write(logWarning, "material: \"" + PrimaryShaderName + "\" did not resolve; fell back "
                    + "to \"" + shader.name + "\". The lines are still drawn, but on a shader this port "
                    + "has not validated - if they look wrong, that is the one fact to check first.");
            }

            Material material = new Material(shader)
            {
                color = Color.green
            };

            // Draw over the planet rather than being occluded by it, and last among the 3D objects.
            // Two properties, both taken from the validated sibling port, and both needed for the
            // same reason: a connection line spans from a vessel's marker out to a body, so a line
            // that passes behind the planet is a line that reads as "it did not draw". SetInt with a
            // property the resolved shader does not declare is a silent no-op, so this cannot itself
            // break the render.
            material.SetInt(ZTestProperty, (int)CompareFunction.Always);
            material.renderQueue = (int)RenderQueue.Overlay;

            BaseMaterial = material;

            Write(log, "material: shader resolved to \"" + shader.name + "\" (looked up \""
                + PrimaryShaderName + "\" then \"" + FallbackShaderName + "\"); " + ZTestProperty + "="
                + (int)CompareFunction.Always + ", renderQueue=" + material.renderQueue + ".");

            return true;
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
