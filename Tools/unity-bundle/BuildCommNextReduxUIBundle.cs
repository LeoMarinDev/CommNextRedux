// Rebuilds commnextredux_ui.bundle inside the throwaway bundle-build project.
//
// Invoked by Tools/build-ui-bundle.sh via:
//   Unity -batchmode -nographics -quit -projectPath <proj> \
//         -executeMethod BuildCommNextReduxUIBundle.Build -logFile -
//
// Two things are deliberate here:
//
//  1. Build target is pinned via the COMMNEXTREDUX_BUNDLE_TARGET environment variable, defaulting to
//     StandaloneWindows (5). The game is a Windows player running under Proton, so the bundle
//     must be serialised for that platform. EditorUserBuildSettings.activeBuildTarget would, on
//     this Linux box, produce a Linux bundle the game would reject as "not built with the right
//     ... build target". Every functional bundle in the installed Mods tree records
//     StandaloneWindows (5).
//
//  2. Every UXML/USS under the UI source directory is packed through the *explicit*
//     AssetBundleBuild overload, so the .meta files' stored bundle names (and their case
//     disagreements) are never consulted. The runtime loads exactly one lowercase name, so a
//     single canonical name is the only safe shape.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildCommNextReduxUIBundle
{
    private const string BundleName = "commnextredux_ui.bundle";
    private const string OutputDir = "BundleOutput";

    // The runtime resolves assets by these exact paths inside the bundle; the root page is
    // looked up by path, so it must be present under exactly this name.
    private const string RootPage = "Assets/CommNextRedux/UI/CN_UI.uxml";

    // The asset-level UI tree: UXML pages, USS stylesheets, the fonts and images they reference.
    private static readonly string[] SourceDirs =
    {
        "Assets/CommNextRedux/UI",
    };

    // Directories whose textures the stylesheets reference from outside SourceDirs.
    private static readonly string[] ExtraAssetDirs =
    {
    };

    // Non-texture assets the UI needs that are not reachable from SourceDirs. The port's fonts
    // are TMP font assets, which the VisualTreeAsset/StyleSheet finder does not match, so they
    // are collected by type below instead of being named one by one here.
    private static readonly string[] ExtraAssetFiles =
    {
    };

    // The render group. The legacy line material is a ShaderGraph asset, and a .shadergraph
    // only imports when the com.unity.shadergraph package is present - which this minimal
    // project deliberately does not have (its dependency closure is not offline-resolvable, see
    // Tools/build-ui-bundle.sh).
    //
    // PHASE 6: NO MATERIAL IS PACKED AT ALL. Phase 2 authored a built-in-shader material here as
    // the D3 fallback; Phase 6 replaced it with a material built in CODE at runtime
    // (CommNextRedux.Rendering.LineMaterials), because P2's measurement was about a SERIALIZED
    // shader reference (which resolves against the player's built-in set and reads back as
    // Hidden/InternalErrorShader) while the sibling port proves the runtime Shader.Find chain
    // resolves. Nothing loads a bundle material any more, so packing one would only ship a
    // shader reference the player cannot resolve. The audit asserts the absence, so a future
    // phase cannot reintroduce it silently.
    private const string ShaderDir = "Assets/CommNextRedux/Shaders";
    private const string MeshDir = "Assets/CommNextRedux/Meshes";

    // PHASE 7: the one MESH the shipping bundle carries - the legacy range-ruler sphere.
    //
    // A mesh is not a material, and the gate only forbids materials, so this is legal by the same
    // rule that made the material illegal: a mesh is pure geometry and carries no shader reference,
    // while a material's serialized shader reference cannot resolve in this player (D3).
    //
    // The GameObject the rulers draw is built in CODE from this mesh, so what ships is the FBX's
    // geometry alone. The prefabs that sit beside it in the vendored corpus are NOT staged for the
    // shipping build: `RulerSphere.prefab` carries a material reference, and packing it would drag
    // that reference into the bundle through the renderer slot even though nothing loads the prefab.
    private const string RulerMeshAsset = "Assets/CommNextRedux/Meshes/RulerSphere.fbx";

    // COMMNEXTREDUX_RENDER_ASSETS=1 packs the legacy ShaderGraph/prefab chain as well, for the
    // audit to measure. EVIDENCE ONLY: that bundle is never deployed (see the D3 block in
    // Tools/build-ui-bundle.sh).
    private static bool RenderAssetsRequested
    {
        get { return System.Environment.GetEnvironmentVariable("COMMNEXTREDUX_RENDER_ASSETS") == "1"; }
    }

    public static void Build()
    {
        // Assets are packed through the *explicit* AssetBundleBuild overload rather than by
        // stamping .meta files and letting BuildAssetBundles scan the AssetDatabase.
        //
        // This is what makes Unity actually emit the bundle's `AssetBundle` object (class 142) -
        // the container map from "assets/..." paths to objects inside the bundle. Bundles built
        // purely from .meta markings came out WITHOUT that object, while every bundle that loads
        // in this installation does contain it. The 6000.4.1f1 player refuses such a bundle with:
        //
        //   The AssetBundle 'commnextredux_ui.bundle' could not be loaded because it is not
        //   compatible with this newer version of the Unity runtime. Rebuild the AssetBundle
        //   to fix this.
        var assets = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:VisualTreeAsset t:StyleSheet", SourceDirs))
            assets.Add(AssetDatabase.GUIDToAssetPath(guid));

        // Images and fonts are referenced from the stylesheets and the pages, not from the
        // finder above, so they are collected explicitly. Naming them keeps the icons and fonts
        // in the bundle even when a stylesheet's own asset reference is dangling.
        foreach (var dir in ExtraAssetDirs)
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                assets.Add(AssetDatabase.GUIDToAssetPath(guid));

        // Textures and fonts inside the UI tree travel with it. t:FontAsset covers TextCore/TMP
        // font assets (the SDF assets the pages style with); t:Font covers a raw ttf if one is
        // shipped alongside.
        foreach (var dir in SourceDirs)
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D t:FontAsset t:Font", new[] { dir }))
                assets.Add(AssetDatabase.GUIDToAssetPath(guid));

        foreach (var path in ExtraAssetFiles)
            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                assets.Add(path);
            else
                Debug.LogWarning($"[bundle] referenced asset missing from the project: {path}");

        // --- the render group ----------------------------------------------------------------
        // PHASE 6: nothing from the render group is packed. The D3 line material is built at
        // runtime in code (CommNextRedux.Rendering.LineMaterials, the sibling port's proven
        // Shader.Find chain), so a packed material would be dead weight carrying a shader
        // reference this player cannot resolve. See the constants' comment above and the audit's
        // NO-MATERIAL assertion in AuditCommNextReduxBundle.

        // PHASE 7: the rulers' sphere MESH, which is the one render-group asset that ships. It is
        // added before the evidence branch so both paths pack it, and it is added by exact path
        // rather than by directory scan so the prefabs beside it cannot come along.
        if (AssetDatabase.LoadMainAssetAtPath(RulerMeshAsset) == null)
        {
            Debug.LogError($"[bundle] the range-ruler mesh {RulerMeshAsset} is not in the project - "
                + "the rulers would ship with the code-built sphere instead of the legacy mesh, and "
                + "D34's first choice would silently become its fallback. Staging is "
                + "Tools/build-ui-bundle.sh's job; see its mesh block.");
            EditorApplication.Exit(1);
            return;
        }

        assets.Add(RulerMeshAsset);
        Debug.Log($"[bundle] packing the range-ruler mesh (MESH only, no material): {RulerMeshAsset}");

        if (RenderAssetsRequested)
        {
            // Evidence-only pass: pack the legacy ShaderGraph chain and the prefabs/FBX so the
            // audit can report which of them survived the pack. Never deployed.
            Debug.LogWarning("[bundle] COMMNEXTREDUX_RENDER_ASSETS=1 - packing the legacy render " +
                             "chain as EVIDENCE; this bundle is NOT shippable.");
            foreach (var dir in new[] { ShaderDir, MeshDir })
            {
                if (!AssetDatabase.IsValidFolder(dir)) { Debug.LogWarning($"[bundle]   absent: {dir}"); continue; }
                foreach (var guid in AssetDatabase.FindAssets("", new[] { dir }))
                    assets.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        // De-duplicate: the two collection paths above can name the same asset twice.
        var unique = new List<string>();
        var seen = new HashSet<string>();
        foreach (var a in assets) if (!string.IsNullOrEmpty(a) && seen.Add(a)) unique.Add(a);
        assets = unique;

        assets.Sort();
        Debug.Log($"[bundle] packing {assets.Count} asset(s) into '{BundleName}':");
        foreach (var a in assets) Debug.Log($"[bundle]   {a}");

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetBundleVariant = "",
            assetNames = assets.ToArray(),
        };

        if (Directory.Exists(OutputDir))
            Directory.Delete(OutputDir, true);
        Directory.CreateDirectory(OutputDir);

        // Build target is taken from the environment so it can be matched byte-for-byte with
        // what the working KSP2 mods ship. Every functional bundle in the installed Mods tree
        // records BuildTarget.StandaloneWindows (5), not StandaloneWindows64 (19). Unity's
        // rejection message names the build target explicitly, so matching it exactly is the
        // safe choice.
        var targetName = System.Environment.GetEnvironmentVariable("COMMNEXTREDUX_BUNDLE_TARGET");
        var target = string.IsNullOrEmpty(targetName)
            ? BuildTarget.StandaloneWindows
            : (BuildTarget)System.Enum.Parse(typeof(BuildTarget), targetName);

        // BuildAssetBundleOptions.None, not ChunkBasedCompression.
        //
        // This is not cosmetic - it was the second half of the "not compatible with this newer
        // version of the Unity runtime" failure. ChunkBasedCompression emits LZ4HC data blocks;
        // None emits LZMA data blocks. Every bundle that actually loads in this installation
        // carries the LZMA signature, and - the tell - an `AssetBundle` object (class 142)
        // describing the bundle's own container map. The ChunkBasedCompression build silently
        // omitted that object, and the 6000.4.1f1 player refuses to load a bundle without it.
        Debug.Log($"[bundle] building for {target} ({(int)target}) " +
                  $"with {BuildAssetBundleOptions.None}");

        var manifest = BuildPipeline.BuildAssetBundles(
            OutputDir,
            new[] { build },
            BuildAssetBundleOptions.None,
            target);

        if (manifest == null)
        {
            Debug.LogError("[bundle] BuildAssetBundles returned null - see preceding errors.");
            EditorApplication.Exit(1);
            return;
        }

        var built = Path.Combine(OutputDir, BundleName);
        if (!File.Exists(built))
        {
            Debug.LogError($"[bundle] expected output missing: {built}");
            EditorApplication.Exit(1);
            return;
        }

        // Report the bundle's contents so the run log proves the pages are really in there.
        // A bundle that builds but omits the UXML pages would fail at runtime exactly like the
        // version-mismatched one did, so this is the check that matters.
        //
        // NOT via AssetDatabase.GetAssetPathsFromAssetBundle: that API reports the assets the
        // *AssetDatabase* has assigned to that bundle name (the .meta markings), and this builder
        // deliberately packs through the explicit AssetBundleBuild overload instead - so it returns
        // 0 here even when the bundle is correct (measured: 0 assets reported, 953 KB bundle whose
        // container the runtime audit then listed in full). The authoritative container check is
        // AuditCommNextReduxBundle, which reads the built bytes.
        var contents = AssetDatabase.GetAssetPathsFromAssetBundle(BundleName);
        System.Array.Sort(contents);
        Debug.Log($"[bundle] '{BundleName}' AssetDatabase assignment says {contents.Length} asset(s) " +
                  "(always 0 for an explicit AssetBundleBuild - see comment; the audit is authoritative)");

        // The root page must be one of the assets this build was asked to pack, or the window
        // loader would look up a null VisualTreeAsset.
        if (System.Array.IndexOf(assets.ToArray(), RootPage) < 0)
        {
            Debug.LogError($"[bundle] root page {RootPage} is NOT in the requested asset set - " +
                           "the window would load a null VisualTreeAsset.");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[bundle] OK {built} ({new FileInfo(built).Length:N0} bytes)");
        EditorApplication.Exit(0);
    }
}
