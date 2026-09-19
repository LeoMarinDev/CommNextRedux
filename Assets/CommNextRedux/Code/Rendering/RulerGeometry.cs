// CommNextRedux - the sphere every range ruler is drawn with: the legacy mesh out of the mod's
// own AssetBundle, or a code-built sphere when the bundle cannot supply one.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/Behaviors/MapSphereRulerComponent.cs was a
//   MonoBehaviour on the GameObject that `Instantiate(ConnectionsRenderer.RulerSpherePrefab, ...)`
//   produced - i.e. the legacy's sphere geometry and its material both came from a Prefab asset
//   loaded out of the mod's bundle through SpaceWarp-1's `AssetManager.GetAsset<GameObject>(...)`.
//   SpaceWarp-1 is gone, and the prefab route is independently dead here: RulerSphere.prefab
//   carries a MATERIAL reference, and a serialized shader reference inside a bundle cannot resolve
//   in the 0.2.8.5 player (D3, `Deploy/obj/bundle-verdict.md` §2.4/§5).
//
// THE BRANCH THAT SHIPS, AND WHY IT IS A CHAIN RATHER THAN ONE ROUTE  (D34)
//   1. `bundle-fbx` - the legacy mesh, packed as a MESH. `RulerSphere.fbx` is material-free (the
//      file contains no Material/Texture node at all), so it satisfies the build's NO-MATERIAL
//      gate; what it must NOT travel with is the prefab or the .mat. The GameObject is built in
//      code (MeshFilter + MeshRenderer), which is the only shape that lets the material be the
//      runtime one.
//   2. `code-built` - a UV sphere generated here, used when the bundle is absent, when the mesh
//      is not in it, or when extraction fails. This is D3's pre-authorised fallback, and it is
//      NOT silent: the branch that resolved is logged once at Info, printed in every
//      `probe: render-rulers` line, and recorded in the phase ledger. A ruler set that quietly
//      fell back would read as "the port uses the legacy mesh" in every later review of the DLL.
//
// WHY THE MESH IS NORMALISED BY ITS OWN RADIUS
//   The legacy's scale arithmetic is `radius = range / Map3DScaleInv` applied to `localScale`, so
//   it is only dimensionally right for a mesh of radius 1. The legacy's RulerSphere.fbx
//   is radius 1 (measured in the bundle audit, `Deploy/obj/p7-bundle-mesh-audit.txt`), which is
//   why the legacy looked right - but nothing in the legacy enforced it. This class reports the
//   mesh's own radius and the caller divides by it, so a mesh swap cannot silently mis-size every
//   sphere on the map. The normalisation is a no-op for the shipped mesh.
//
// WHAT THIS CLASS DOES NOT DO
//   It does not keep the AssetBundle alive. The bundle is opened, the mesh is extracted, and the
//   bundle is then unloaded with `Unload(false)`, which frees its serialized data while leaving
//   the already-loaded Mesh in memory. The legacy never unloaded its bundle and had no reason to;
//   this port loads one mesh out of a bundle that mostly holds UI assets, so holding all of them
//   for the session would be a cost with no consumer (the window phase loads what it needs).

using System;
using System.IO;
using UnityEngine;

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// Resolves - once - the sphere mesh the range rulers are drawn with.
    /// </summary>
    /// <remarks>
    /// Static by design, like <see cref="LineMaterials"/> and for the same reason: the renderer is
    /// a static class ticked by the plugin, so there is no instance for a mesh to hang off. Every
    /// member is safe to read before <see cref="Resolve"/> has run; the ruler path checks
    /// <see cref="HasMesh"/> first and refuses to create objects without it.
    /// </remarks>
    public static class RulerGeometry
    {
        /// <summary>
        /// The bundle-relative path of the legacy model, lower-case exactly as the bundle's
        /// container stores it (Unity lower-cases every container path, and Linux is
        /// case-sensitive - see the repo's hard rule 5).
        /// </summary>
        public const string MeshAssetPath = "assets/commnextredux/meshes/rulersphere.fbx";

        /// <summary>The mesh's own name inside the model, as the bundle audit reports it.</summary>
        public const string MeshName = "RulerSphere";

        /// <summary>
        /// The deployed bundle's path, relative to the mod's own folder.
        /// </summary>
        /// <remarks>
        /// The same file the UI pages live in - one bundle per mod, named for the mod's UI because
        /// that is what it was created for in Phase 2. The mesh was added to it in Phase 7 (D34)
        /// rather than shipping a second bundle: two bundles would double the class-142/version
        /// surface for one 44 KB mesh, and the deployment tree already carries this path.
        /// </remarks>
        public const string BundleRelativePath = "/assets/bundles/commnextredux_ui.bundle";

        /// <summary>The branch that produced the live mesh: <c>bundle-fbx</c> or <c>code-built</c>.</summary>
        public static string Branch { get; private set; }

        /// <summary>Which bundle read produced the mesh, or <c>null</c> when the bundle was not used.</summary>
        public static string BundleRoute { get; private set; }

        /// <summary>The sphere mesh, or <c>null</c> when nothing could be produced.</summary>
        public static Mesh SphereMesh { get; private set; }

        /// <summary>
        /// The mesh's own radius, in its local space, along its largest half-extent.
        /// </summary>
        /// <remarks>
        /// Read from the mesh's bounds rather than assumed to be 1: the ruler scale is
        /// `range / Map3DScaleInv / this`, so a mesh that is not a unit sphere would otherwise
        /// mis-size every ruler by that factor with no other symptom. <c>1</c> before a mesh
        /// resolves, so an early read cannot divide by zero.
        /// </remarks>
        public static float SphereRadius { get; private set; } = 1f;

        /// <summary>The mesh's vertex count, for the probe line and the ledger.</summary>
        public static int VertexCount { get; private set; }

        /// <summary>Whether a mesh resolved, i.e. whether a ruler may be created at all.</summary>
        public static bool HasMesh
        {
            get { return SphereMesh != null; }
        }

        /// <summary>
        /// Resolves the sphere mesh, once.
        /// </summary>
        /// <param name="modFolder">The deployed mod folder, i.e. <c>SWMetadata.Folder.FullName</c>.</param>
        /// <param name="log">Informational sink; must not throw.</param>
        /// <param name="logWarning">Warning sink; must not throw.</param>
        /// <param name="logError">Error sink; must not throw.</param>
        /// <returns><c>true</c> when a mesh is available, from either branch.</returns>
        /// <remarks>
        /// <para>
        /// <b>Idempotent by an early return on success, not by a flag.</b> A failed first call
        /// leaves <see cref="SphereMesh"/> null and every field clean, so calling it again is the
        /// retry. Nothing here depends on the graphics side (a mesh is not a shader), so unlike
        /// <see cref="LineMaterials.GenerateMaterial"/> there is no reason to defer it.
        /// </para>
        /// <para>
        /// <b>A bundle failure is a warning, never an exception.</b> The bundle may legitimately be
        /// absent (an incomplete install), and the code-built sphere is a complete answer, so every
        /// failure path logs and falls through rather than throwing inside the loader's
        /// initialization sequence.
        /// </para>
        /// </remarks>
        public static bool Resolve(string modFolder, Action<string> log, Action<string> logWarning,
            Action<string> logError)
        {
            if (SphereMesh != null)
            {
                return true;
            }

            Mesh mesh = null;
            string route = null;

            if (string.IsNullOrEmpty(modFolder))
            {
                Write(logWarning, "ruler-geometry: the mod folder is not assigned yet, so the legacy "
                    + "mesh cannot be looked for in the bundle; the code-built sphere is used instead "
                    + "(the log line below names the branch that shipped)");
            }
            else
            {
                string bundlePath = modFolder + BundleRelativePath;
                mesh = TryLoadFromBundle(bundlePath, out route, logWarning);
            }

            if (mesh != null)
            {
                Branch = "bundle-fbx";
                BundleRoute = route;
                SphereMesh = mesh;
            }
            else
            {
                Branch = "code-built";
                BundleRoute = null;
                SphereMesh = BuildUnitSphere(SphereMeridians, SphereParallels);
            }

            if (SphereMesh == null)
            {
                Branch = null;
                VertexCount = 0;
                Write(logError, "ruler-geometry: NEITHER branch produced a mesh - the code-built sphere "
                    + "was refused by the runtime, which means Unity is not in a state to create meshes "
                    + "at all. No range ruler will be created; every other part of this mod is "
                    + "unaffected.");
                return false;
            }

            SphereRadius = RadiusOf(SphereMesh, logWarning);
            VertexCount = SphereMesh.vertexCount;

            Write(log, "ruler-geometry: branch=" + Branch + " mesh='" + SphereMesh.name + "' verts="
                + VertexCount + " radius=" + SphereRadius.ToString("0.######", Culture)
                + (route == null ? string.Empty : " (route: " + route + ")")
                + (Branch == "code-built"
                    ? " - the legacy mesh was NOT used; the range spheres are generated in code (see "
                      + "the warning above for which route failed)"
                    : string.Empty));

            return true;
        }

        /// <summary>Meridian segments of the code-built sphere - 24 gives a round silhouette at map scale.</summary>
        public const int SphereMeridians = 24;

        /// <summary>Parallel segments of the code-built sphere.</summary>
        public const int SphereParallels = 16;

        /// <summary>
        /// Tries every bundle read that can produce the legacy mesh, in a fixed order.
        /// </summary>
        /// <param name="bundlePath">The absolute path of the deployed bundle.</param>
        /// <param name="route">Receives the name of the read that succeeded.</param>
        /// <param name="logWarning">Warning sink; must not throw.</param>
        /// <returns>The mesh, or <c>null</c> when no read produced one.</returns>
        /// <remarks>
        /// <para>
        /// <b>Three reads, because which one works for a model file is not part of the API
        /// contract.</b> An FBX packs as a model: its container entry names a GameObject, and its
        /// Mesh is a sub-asset of that model rather than an asset in its own right. So
        /// <c>LoadAsset&lt;Mesh&gt;(path)</c> may legitimately return <c>null</c> here, and the two
        /// routes that do work are loading the model object and reading its <c>MeshFilter</c>, or
        /// taking the mesh out of the bundle's asset list by name. All three are attempted and the
        /// one that hit is named in the log and in the probe, so the fact is recorded rather than
        /// assumed. The bundle audit in <c>Tools/unity-bundle/</c> runs the same three reads
        /// offline against the delivered bytes.
        /// </para>
        /// <para>
        /// A missing bundle file is a warning and not an error: the code-built branch is a
        /// complete answer, and the fact that the file is missing is worth saying once.
        /// </para>
        /// </remarks>
        private static Mesh TryLoadFromBundle(string bundlePath, out string route, Action<string> logWarning)
        {
            route = null;

            if (!File.Exists(bundlePath))
            {
                Write(logWarning, "ruler-geometry: no bundle at " + bundlePath + " - the legacy mesh "
                    + "cannot be read; falling back to the code-built sphere");
                return null;
            }

            AssetBundle bundle = null;

            try
            {
                bundle = AssetBundle.LoadFromFile(bundlePath);

                if (bundle == null)
                {
                    Write(logWarning, "ruler-geometry: AssetBundle.LoadFromFile returned null for "
                        + bundlePath + " - falling back to the code-built sphere");
                    return null;
                }

                Mesh mesh = bundle.LoadAsset<Mesh>(MeshAssetPath);
                if (mesh != null)
                {
                    route = "LoadAsset<Mesh>(path)";
                    return mesh;
                }

                GameObject model = bundle.LoadAsset<GameObject>(MeshAssetPath);
                if (model != null)
                {
                    MeshFilter filter = model.GetComponentInChildren<MeshFilter>(true);
                    if (filter != null && filter.sharedMesh != null)
                    {
                        route = "LoadAsset<GameObject>(path).MeshFilter.sharedMesh";
                        return filter.sharedMesh;
                    }

                    Write(logWarning, "ruler-geometry: the bundle's model asset '" + model.name
                        + "' carries no MeshFilter with a mesh; trying the asset list");
                }

                Mesh[] meshes = bundle.LoadAllAssets<Mesh>();
                if (meshes != null && meshes.Length > 0)
                {
                    Mesh named = null;
                    for (int i = 0; i < meshes.Length; i++)
                    {
                        if (meshes[i] != null && meshes[i].name == MeshName)
                        {
                            named = meshes[i];
                            break;
                        }
                    }

                    if (named != null)
                    {
                        route = "LoadAllAssets<Mesh>() by name '" + MeshName + "'";
                        return named;
                    }

                    // A single unnamed mesh is still unambiguous; more than one is not, and picking
                    // one of several would be a guess. Say so rather than guessing.
                    if (meshes.Length == 1 && meshes[0] != null)
                    {
                        route = "LoadAllAssets<Mesh>() (the bundle's only mesh)";
                        return meshes[0];
                    }

                    Write(logWarning, "ruler-geometry: the bundle carries " + meshes.Length
                        + " meshes and none is named '" + MeshName + "'; none is claimed");
                }

                Write(logWarning, "ruler-geometry: the bundle at " + bundlePath + " carries no mesh at '"
                    + MeshAssetPath + "' by any of the three reads - falling back to the code-built sphere");
                return null;
            }
            catch (Exception exception)
            {
                Write(logWarning, "ruler-geometry: reading the bundle threw (" + exception.GetType().Name
                    + ": " + exception.Message + ") - falling back to the code-built sphere");
                return null;
            }
            finally
            {
                // Unload(false): the bundle's serialized data goes, the Mesh already loaded stays.
                // Unload(true) would destroy the mesh this method just returned.
                if (bundle != null)
                {
                    try
                    {
                        bundle.Unload(false);
                    }
                    catch (Exception exception)
                    {
                        Write(logWarning, "ruler-geometry: unloading the bundle threw ("
                            + exception.GetType().Name + ": " + exception.Message
                            + "); the mesh is unaffected");
                    }
                }
            }
        }

        /// <summary>The largest half-extent of a mesh's bounds, with unreadable or degenerate bounds guarded.</summary>
        /// <param name="mesh">The mesh to measure.</param>
        /// <param name="logWarning">Warning sink; must not throw.</param>
        /// <returns>The radius, or <c>1</c> when the bounds are unusable or unreadable.</returns>
        /// <remarks>
        /// <para>
        /// <b>Guarded even though the read is now guaranteed to work.</b> <c>Mesh.bounds</c> is not
        /// vertex data, so it is readable on a mesh imported with Read/Write Disabled;
        /// <c>Tools/build-ui-bundle.sh</c> nevertheless stages the legacy FBX with
        /// <c>isReadable: 1</c>, because the first pack of this mesh measured
        /// <c>isReadable=False</c> and this read sits on the boot path. Measured from the delivered
        /// bytes after that fix: <c>isReadable=True</c>, max-axis radius <c>1.000001</c>, 1223
        /// vertices. A future player could still refuse the read, and unguarded a bounds exception
        /// would escape <see cref="Resolve"/> into the loader's initialisation and take the
        /// connection lines down with the rulers.
        /// </para>
        /// <para>
        /// The fallback is <c>1</c> because that is what a unit sphere means, and the shipped mesh
        /// <i>is</i> a unit sphere to within a millionth - so the fallback's whole cost is that
        /// rounding, never a mis-sized ruler.
        /// </para>
        /// </remarks>
        private static float RadiusOf(Mesh mesh, Action<string> logWarning)
        {
            try
            {
                Vector3 extents = mesh.bounds.extents;
                float radius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));

                // A mesh with degenerate bounds would divide every sphere by zero; 1 is the honest
                // fallback because it preserves the entry's range semantics exactly.
                return radius > 0.0001f ? radius : 1f;
            }
            catch (Exception exception)
            {
                Write(logWarning, "ruler-geometry: reading the mesh bounds threw ("
                    + exception.GetType().Name + ": " + exception.Message + "), so the sphere's scale "
                    + "is normalised against 1 - see the ledger for the mesh's measured radius, which "
                    + "the bundle audit reports from the delivered bytes");
                return 1f;
            }
        }

        /// <summary>
        /// Builds a UV sphere of radius 1 around the origin.
        /// </summary>
        /// <param name="meridians">Segments around the equator.</param>
        /// <param name="parallels">Segments from pole to pole.</param>
        /// <returns>The mesh, with normals and one sub-mesh.</returns>
        /// <remarks>
        /// <para>
        /// <b>Radius 1 deliberately.</b> Every caller scales by `range / Map3DScaleInv / radius`, and
        /// a unit sphere makes the radius factor exactly 1 - which is the same convention the legacy
        /// mesh uses (measured radius 1) and the same convention this class normalises any other
        /// mesh to.
        /// </para>
        /// <para>
        /// <b>Outward winding, but it does not matter.</b> The ruler material resolves through the
        /// sprite chain, which is two-sided (`Cull Off`), so a sphere is visible from inside and
        /// outside - which is what makes it read as a translucent bubble rather than as a disc.
        /// The winding is still the conventional outward one so the mesh is correct for any other
        /// consumer.
        /// </para>
        /// </remarks>
        private static Mesh BuildUnitSphere(int meridians, int parallels)
        {
            if (meridians < 3)
            {
                meridians = 3;
            }

            if (parallels < 2)
            {
                parallels = 2;
            }

            int vertexCount = (parallels + 1) * (meridians + 1);
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];

            int v = 0;
            for (int lat = 0; lat <= parallels; lat++)
            {
                // 0 at the north pole, pi at the south: sin/cos give y directly.
                float theta = Mathf.PI * lat / parallels;
                float y = Mathf.Cos(theta);
                float ringRadius = Mathf.Sin(theta);

                for (int lon = 0; lon <= meridians; lon++)
                {
                    float phi = 2f * Mathf.PI * lon / meridians;
                    Vector3 point = new Vector3(ringRadius * Mathf.Cos(phi), y, ringRadius * Mathf.Sin(phi));

                    vertices[v] = point;
                    normals[v] = point;                    // unit sphere: the position is the normal
                    uvs[v] = new Vector2((float)lon / meridians, 1f - (float)lat / parallels);
                    v++;
                }
            }

            int[] triangles = new int[parallels * meridians * 6];
            int t = 0;
            for (int lat = 0; lat < parallels; lat++)
            {
                for (int lon = 0; lon < meridians; lon++)
                {
                    int rowStart = lat * (meridians + 1);
                    int nextRowStart = (lat + 1) * (meridians + 1);

                    int a = rowStart + lon;
                    int b = nextRowStart + lon;
                    int c = rowStart + lon + 1;
                    int d = nextRowStart + lon + 1;

                    triangles[t++] = a;
                    triangles[t++] = b;
                    triangles[t++] = c;

                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = d;
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "CommNextReduxRulerSphere";
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>Invariant culture, so a decimal comma locale cannot change the log's shape.</summary>
        private static readonly System.Globalization.CultureInfo Culture =
            System.Globalization.CultureInfo.InvariantCulture;

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
