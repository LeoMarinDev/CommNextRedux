// Editor-side audit of BUILT asset bundles.
//
// The render proof instantiates the UXML pages from the throwaway PROJECT, which proves the
// sources are valid but says nothing about what survives the bundle round-trip. This script
// loads the built bundle back (in the editor, where shader references always resolve) and dumps
// the state of every TextCore FontAsset / Material / StyleSheet in it. If a font's material or
// its material's main texture is null here, the loss happened at build/pack time (recipe bug).
// If everything is fine here but the player's TextUtilities.GetTextCoreSettingsForElement still
// null-references, the loss is player-specific (e.g. a shader reference that only resolves in
// the editor) - which is exactly the distinction the in-game diag then measures.
//
// Invoked via:  Unity -batchmode -nographics -quit -projectPath <proj> \
//                     -executeMethod AuditCommNextReduxBundle.Run -logFile -
// Bundle paths come from $COMMNEXTREDUX_AUDIT_BUNDLES (';'-separated); with no env var, every
// *.bundle under BundleOutput/ and BundleVariants/ is audited.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

public static class AuditCommNextReduxBundle
{
    // The page inside the bundle whose instantiated clone is probed for the dropdowns below.
    //
    // PHASE 2 CORRECTION. The donor probed its ROOT page, because FlightPlan kept its dropdowns
    // there. This port's root page is the legacy map toolbar, and the legacy keeps BOTH dropdowns
    // on the vessel report page:
    //
    //   $ rg -o 'name="[^"]*dropdown[^"]*"' mods-outdated/CommNext -g '*.uxml'
    //   name="filter-dropdown"   name="sort-dropdown"      (both in VesselReportWindow.uxml)
    //
    // DumpDropdown treats NOT FOUND as a hard error, so pointing this at the root page would have
    // failed every correct build. It is a separate constant from RootPage for exactly that reason.
    private const string DropdownProbePage = "Assets/CommNextRedux/UI/VesselReportWindow.uxml";

    // The port's root page - the one the runtime window loads, by this exact container path.
    private const string RootPage = "Assets/CommNextRedux/UI/CN_UI.uxml";

    // ---------------------------------------------------------------------------------------------
    // PHASE 7: the one MESH the bundle carries, and the three reads the runtime tries for it.
    // ---------------------------------------------------------------------------------------------
    //
    // The range rulers draw a sphere, and the legacy's sphere geometry is an FBX. It packs as a
    // MESH (which the NO-MATERIAL gate permits - it is the material that cannot ship), and the
    // runtime loads it back through `CommNextRedux.Rendering.RulerGeometry`, whose read order is
    // reproduced here against the DELIVERED bytes:
    //
    //   1. LoadAsset<Mesh>(path)                        - the direct read
    //   2. LoadAsset<GameObject>(path) -> MeshFilter    - an FBX's main asset is a model, and its
    //                                                     Mesh can be a sub-asset rather than an
    //                                                     asset in its own right
    //   3. LoadAllAssets<Mesh>() by name                - the last resort
    //
    // This is the offline half of D34's branch decision. The runtime reports the branch it took as
    // `ruler-geometry: branch=bundle-fbx|code-built` and in every `probe: render-rulers` line; this
    // audit says which branch is *possible*, from the same bytes the player will read, before any
    // launch. The two are not redundant: this one can name which READ works, which is the fact that
    // changes if a future Unity import setting renames the container entry.
    private const string MeshAssetPath = "assets/commnextredux/meshes/rulersphere.fbx";
    private const string MeshName = "RulerSphere";

    // No material is expected in the bundle at all as of Phase 6: the line material is built at
    // runtime (CommNextRedux.Rendering.LineMaterials), and a material packed here would carry a
    // serialized shader reference this player cannot resolve. The NO-MATERIAL assertion below
    // reports that on one greppable line.

    // Every page the bundle must carry, instantiated from the DELIVERED bytes. A container listing
    // proves the asset is packed; only a successful Instantiate proves it survived.
    private static readonly string[] Pages =
    {
        "Assets/CommNextRedux/UI/CN_UI.uxml",
        "Assets/CommNextRedux/UI/VesselReportWindow.uxml",
        "Assets/CommNextRedux/UI/TooltipWindow.uxml",
        "Assets/CommNextRedux/UI/Components/BandRow.uxml",
        "Assets/CommNextRedux/UI/Components/NetworkConnectionView.uxml",
    };

    // Marker names the root page must carry, read out of the legacy map toolbar.
    private static readonly (string Page, string Probe)[] MarkerProbes =
    {
        (RootPage, "toolbar"),
        (RootPage, "lines-button"),
        (RootPage, "rulers-button"),
        (RootPage, "vessel-report-button"),
    };

    // (page, element name, the full type that element must resolve to, that type) - asserted on an
    // instance cloned from the BUILT bytes. In the EDITOR the type comes from the sources copied into
    // the throwaway project; in the PLAYER the same assertion can only succeed if CommNextRedux.dll
    // carries the type. VerifyCommNextReduxUxml makes the project-side half of this assertion.
    //
    // Asserted by TYPE and by NAME, deliberately both. They fail for different reasons and the fix
    // for each is different: a type that does not resolve makes the page unimportable, while a NAME
    // that is dropped leaves the element present but every Q("name") lookup returning null, so a
    // controller binds nothing and the window comes up empty with no error anywhere. The name half
    // is the one that matters at runtime, and it is a property of the IMPORTED asset - so it has to
    // be asserted against the bundle, not only in the project. Why it can go missing at all:
    // Deploy/obj/bundle-verdict.md.
    private static readonly (string Page, string Probe, string Type, Type ClrType)[] ControlProbes =
    {
        ("Assets/CommNextRedux/UI/Components/BandRow.uxml", "band-icon",
         "CommNext.Unity.Runtime.Controls.BandIcon", typeof(CommNext.Unity.Runtime.Controls.BandIcon)),
        ("Assets/CommNextRedux/UI/Components/NetworkConnectionView.uxml", "signal-strength-icon",
         "CommNext.Unity.Runtime.Controls.SignalStrengthIcon", typeof(CommNext.Unity.Runtime.Controls.SignalStrengthIcon)),
        ("Assets/CommNextRedux/UI/VesselReportWindow.uxml", "sort-direction-button",
         "CommNext.Unity.Runtime.Controls.SortDirectionButton", typeof(CommNext.Unity.Runtime.Controls.SortDirectionButton)),
    };

    public static void Run()
    {
        List<string> paths = new List<string>();
        string env = Environment.GetEnvironmentVariable("COMMNEXTREDUX_AUDIT_BUNDLES");
        if (!string.IsNullOrEmpty(env))
        {
            foreach (string p in env.Split(';'))
                if (!string.IsNullOrEmpty(p)) paths.Add(p);
        }
        else
        {
            AddGlob(paths, "BundleOutput");
            AddGlob(paths, "BundleVariants");
        }

        Debug.Log($"[audit] {paths.Count} bundle(s) to audit");
        foreach (string p in paths) AuditOne(p);
        Debug.Log("[audit] done");
        EditorApplication.Exit(0);
    }

    private static void AddGlob(List<string> paths, string dir)
    {
        if (!Directory.Exists(dir)) return;
        string[] files = Directory.GetFiles(dir, "*.bundle", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string f in files)
        {
            if (f.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)) continue;
            paths.Add(f);
        }
    }

    private static void AuditOne(string path)
    {
        Debug.Log($"[audit] ===== {path} ({(File.Exists(path) ? new FileInfo(path).Length.ToString("N0") + " bytes" : "MISSING")})");
        if (!File.Exists(path)) return;

        AssetBundle bundle;
        try { bundle = AssetBundle.LoadFromFile(path); }
        catch (Exception e) { Debug.LogError($"[audit] LoadFromFile THREW {e.GetType().Name}: {e.Message}"); return; }
        if (bundle == null) { Debug.LogError("[audit] LoadFromFile -> null"); return; }

        try
        {
            Debug.Log($"[audit]   bundle.name='{bundle.name}'");
            string[] names = bundle.GetAllAssetNames();
            Array.Sort(names, StringComparer.Ordinal);
            Debug.Log($"[audit]   container assets: {names.Length}");
            foreach (string n in names) Debug.Log($"[audit]     container: {n}");

            UnityEngine.Object[] all = SafeAll(bundle);
            Debug.Log($"[audit]   LoadAllAssets(): {all.Length}");
            foreach (UnityEngine.Object o in all)
            {
                if (o == null) continue;
                FontAsset fa = o as FontAsset;
                if (fa != null) { Debug.Log($"[audit]     FONT '{fa.name}' {DescribeFont(fa)}"); continue; }
                Material m = o as Material;
                if (m != null) { Debug.Log($"[audit]     MAT '{m.name}' {DescribeMaterial(m)}"); continue; }
                Debug.Log($"[audit]     {o.GetType().Name} '{o.name}'");
            }

            FontAsset[] fonts = SafeAllT<FontAsset>(bundle);
            Debug.Log($"[audit]   LoadAllAssets<FontAsset>(): {fonts.Length}");
            foreach (FontAsset fa in fonts) Debug.Log($"[audit]     FONT '{fa.name}' {DescribeFont(fa)}");

            Material[] mats = SafeAllT<Material>(bundle);
            Debug.Log($"[audit]   LoadAllAssets<Material>(): {mats.Length}");
            foreach (Material m in mats) Debug.Log($"[audit]     MAT '{m.name}' {DescribeMaterial(m)}");

            AuditRulerMesh(bundle);

            StyleSheet[] sheets = SafeAllT<StyleSheet>(bundle);
            Debug.Log($"[audit]   LoadAllAssets<StyleSheet>(): {sheets.Length}");
            foreach (StyleSheet s in sheets)
            {
                Debug.Log($"[audit]     SHEET '{s.name}' importedWithErrors={Field(s, "m_ImportedWithErrors")} importedWithWarnings={Field(s, "m_ImportedWithWarnings")}");
                IList assets = SheetAssets(s);
                if (assets == null) { Debug.Log("[audit]       m_Assets: <unreadable>"); continue; }
                Debug.Log($"[audit]       m_Assets.Count={assets.Count}");
                for (int i = 0; i < assets.Count; i++)
                {
                    UnityEngine.Object o = assets[i] as UnityEngine.Object;
                    if (o == null) { Debug.Log($"[audit]       m_Assets[{i}] = NULL (unresolved)"); continue; }
                    FontAsset fa = o as FontAsset;
                    Debug.Log(fa != null
                        ? $"[audit]       m_Assets[{i}] FontAsset '{fa.name}' {DescribeFont(fa)}"
                        : $"[audit]       m_Assets[{i}] {o.GetType().Name} '{o.name}'");
                }
            }

            // -------------------------------------------------------------------------------------
            // The PAGE, instantiated from the DELIVERED bytes. `childCount > 0` is the blank-window
            // gate, and the two dropdowns are the acceptance-gate items: the legacy markup carried
            // the choices in the UXML itself, so a clone that loses them is a silently empty
            // dropdown that only renders its emptiness in the player.
            // -------------------------------------------------------------------------------------
            var roots = new Dictionary<string, TemplateContainer>();
            foreach (string pagePath in Pages)
            {
                VisualTreeAsset page = SafeLoad<VisualTreeAsset>(bundle, pagePath);
                if (page == null)
                {
                    Debug.LogError($"[audit]   PAGE MISSING from the bundle: {pagePath}");
                    continue;
                }

                try
                {
                    TemplateContainer root = page.Instantiate();
                    int cc = root == null ? -1 : root.childCount;
                    Debug.Log($"[audit]   PAGE '{page.name}' childCount={cc} " +
                              $"descendants={(root == null ? -1 : root.Query<VisualElement>().ToList().Count)} " +
                              $"first='{(cc > 0 ? root[0].name : "<none>")}'");
                    if (cc <= 0) Debug.LogError($"[audit]   PAGE CLONED EMPTY: {pagePath} childCount={cc}");
                    if (root != null) roots[pagePath] = root;
                }
                catch (Exception e)
                {
                    // The custom-control failure lands here when the type cannot be resolved.
                    Debug.LogError($"[audit]   PAGE {pagePath} Instantiate THREW {e.GetType().Name}: {e.Message}");
                }
            }

            // The marker names: proof the clone carries this port's root page and not a stale build.
            foreach (var probe in MarkerProbes)
            {
                TemplateContainer root;
                if (!roots.TryGetValue(probe.Page, out root)) continue;
                if (root.Q(probe.Probe) == null)
                    Debug.LogError($"[audit]   MARKER MISSING {probe.Page} :: {probe.Probe}");
                else
                    Debug.Log($"[audit]   marker found: {probe.Page} :: {probe.Probe}");
            }

            // The custom controls, resolved from an instance cloned out of the delivered bytes -
            // by TYPE first (does the control exist in the shipped assembly at all) and then by NAME
            // (is it reachable the way a controller reaches it). Both, because they fail differently.
            foreach (var probe in ControlProbes)
            {
                TemplateContainer root;
                if (!roots.TryGetValue(probe.Page, out root)) continue;

                VisualElement byType = FindByType(root, probe.ClrType);
                if (byType == null)
                {
                    Debug.LogError($"[audit]   CONTROL NOT FOUND BY TYPE {probe.Page} " +
                                   $"(expected an element of type {probe.Type})");
                    continue;
                }
                string actual = byType.GetType().FullName;
                if (actual != probe.Type)
                {
                    Debug.LogError($"[audit]   CONTROL TYPE MISMATCH {probe.Page} :: {probe.Probe} " +
                                   $"expected={probe.Type} actual={actual}");
                    continue;
                }
                Debug.Log($"[audit]   custom control resolved from the bundle (by type): {probe.Page} :: " +
                          $"{probe.Probe} -> {actual}");

                VisualElement byName = root.Q(probe.Probe);
                if (byName == null)
                    Debug.LogError($"[audit]   CONTROL NAME DROPPED {probe.Page} :: '{probe.Probe}' - the " +
                                   $"shipped control of type {actual} carries no such name, so every " +
                                   $"Q(\"{probe.Probe}\") lookup returns null in the player.");
                else if (byName != byType)
                    Debug.LogError($"[audit]   CONTROL NAME COLLISION {probe.Page} :: '{probe.Probe}' resolves " +
                                   $"to {byName.GetType().FullName} but the control under test is {actual}");
                else
                    Debug.Log($"[audit]   custom control reachable by name in the bundle: {probe.Page} :: " +
                              $"Q(\"{probe.Probe}\") -> {actual}");
            }

            // The dropdown gate, on the page that actually carries them.
            TemplateContainer reportRoot;
            if (roots.TryGetValue(DropdownProbePage, out reportRoot))
            {
                DumpDropdown(reportRoot, "filter-dropdown");
                DumpDropdown(reportRoot, "sort-dropdown");
            }
            else
            {
                Debug.LogError($"[audit]   the dropdown page {DropdownProbePage} did not instantiate - " +
                               "the dropdown gate could not run");
            }

            // -------------------------------------------------------------------------------------
            // P6: NO material may be packed, on ONE greppable line.
            // -------------------------------------------------------------------------------------
            // The line material is built in CODE at runtime by CommNextRedux.Rendering.LineMaterials,
            // which resolves its shader with the sibling port's proven
            // `Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent")` chain.
            //
            // Phase 2 packed a material here instead, and its measurement stands: a SERIALIZED shader
            // reference inside an AssetBundle resolves against the set of shaders the player build
            // carries, and `Sprites/Default` is not in it - the material read back from the live
            // player as `Hidden/InternalErrorShader`. What P2 could not measure is the runtime lookup,
            // which is a different mechanism and which the sibling port logs succeeding in three
            // separate launches. So the fallback moved from the bundle into the code, and the bundle
            // must now carry no material at all: a packed one would be dead weight holding a shader
            // reference this player cannot resolve, and the next phase to load a material by name
            // would get an invisible line with no error.
            //
            // This assertion replaces the D3-MATERIAL one and is stricter in the direction that
            // matters: before, a bundle containing an unresolvable material passed as long as the
            // material's shader was non-null IN THE EDITOR; now any material at all is a failure.
            int packedMaterials = SafeAllT<Material>(bundle).Length;
            if (packedMaterials == 0)
            {
                Debug.Log("[audit]   NO-MATERIAL OK the bundle packs 0 materials - the line material is "
                    + "built at runtime (LineMaterials), so nothing shader-bearing ships");
            }
            else
            {
                foreach (Material m in SafeAllT<Material>(bundle))
                {
                    Debug.LogError($"[audit]   NO-MATERIAL FAIL the bundle packs the material '{m.name}' "
                        + "- nothing loads a bundled material any more, and a serialized shader "
                        + "reference cannot resolve in this player");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[audit] audit THREW {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object[] SafeAll(AssetBundle b)
    {
        try { return b.LoadAllAssets() ?? new UnityEngine.Object[0]; }
        catch (Exception e) { Debug.LogWarning($"[audit] LoadAllAssets THREW {e.GetType().Name}: {e.Message}"); return new UnityEngine.Object[0]; }
    }

    private static T[] SafeAllT<T>(AssetBundle b) where T : UnityEngine.Object
    {
        try { return b.LoadAllAssets<T>() ?? new T[0]; }
        catch (Exception e) { Debug.LogWarning($"[audit] LoadAllAssets<{typeof(T).Name}> THREW {e.GetType().Name}: {e.Message}"); return new T[0]; }
    }

    private static T SafeLoad<T>(AssetBundle b, string name) where T : UnityEngine.Object
    {
        try { return b.LoadAsset<T>(name); }
        catch (Exception e) { Debug.LogError($"[audit] LoadAsset<{typeof(T).Name}>('{name}') THREW {e.GetType().Name}: {e.Message}"); return null; }
    }

    // A dropdown that came out of the clone with zero choices is the failure this proves against:
    // it renders, it opens, and it is empty.
    private static void DumpDropdown(VisualElement root, string name)
    {
        DropdownField d = root.Q<DropdownField>(name);
        if (d == null)
        {
            Debug.LogError($"[audit]   DROPDOWN '{name}' NOT FOUND in the instantiated clone");
            return;
        }
        Debug.Log($"[audit]   DROPDOWN '{name}' choices={d.choices.Count} index={d.index} value='{d.value}'");
        if (d.choices.Count > 0)
        {
            Debug.Log($"[audit]     choices[0]='{d.choices[0]}' choices[last]='{d.choices[d.choices.Count - 1]}'");
        }
    }

    private static string DescribeMaterial(Material m)
    {
        if (m == null) return "material=NULL";
        Shader sh = m.shader;
        Texture tex = m.mainTexture;
        string texInfo = tex == null ? "NULL" : $"'{tex.name}' ({tex.width}x{tex.height})";
        return $"mat='{m.name}' shader={(sh == null ? "NULL" : "'" + sh.name + "'")} mainTex={texInfo}";
    }

    /// <summary>
    /// Reports the ruler sphere mesh from the DELIVERED bytes, and which runtime read finds it.
    /// </summary>
    /// <param name="bundle">The loaded bundle, never null here.</param>
    /// <remarks>
    /// <para>
    /// Two questions, on two kinds of line, because they fail differently:
    /// </para>
    /// <para>
    /// <b>Is the mesh in the bundle at all?</b> One <c>MESH</c> line per mesh, with the numbers the
    /// runtime's own scale arithmetic consumes (vertex count and the bounds radius that
    /// <c>RulerGeometry.RadiusOf</c> measures), plus <c>isReadable</c> and the material count of the
    /// model the mesh came from. A shipping bundle with zero meshes makes the runtime take the
    /// code-built branch, and the shell gate treats that as a failure - so this line is the one that
    /// says <i>why</i>.
    /// </para>
    /// <para>
    /// <b>Which read finds it?</b> One <c>MESH-ROUTE</c> line per read the runtime tries, named
    /// exactly as <c>RulerGeometry.TryLoadFromBundle</c> names them. The runtime takes the first that
    /// returns a mesh, so the first <c>MESH-ROUTE ... -> OK</c> line here is the route the player
    /// will take. An FBX's asset path names a MODEL, not a mesh, so the direct read is the one that
    /// may legitimately miss - which is why the other two exist.
    /// </para>
    /// <para>
    /// Property reads are individually guarded: a mesh that cannot report its bounds must be
    /// visible as <c>radius THREW</c> rather than taking the whole audit down, because that failure
    /// would otherwise look like "the audit broke", not "the sphere will be mis-sized".
    /// </para>
    /// </remarks>
    private static void AuditRulerMesh(AssetBundle bundle)
    {
        Mesh[] meshes = SafeAllT<Mesh>(bundle);
        Debug.Log($"[audit]   LoadAllAssets<Mesh>(): {meshes.Length}");

        int named = 0;
        foreach (Mesh mesh in meshes)
        {
            if (mesh == null) continue;
            if (string.Equals(mesh.name, MeshName, StringComparison.Ordinal)) named++;
        }

        if (meshes.Length == 0)
        {
            Debug.LogError("[audit]   MESH FAIL the bundle carries no mesh at all - the range rulers "
                + "would fall back to the code-built sphere (RulerGeometry's own log line names the "
                + "branch), and D34's first choice is not what shipped");
        }
        else
        {
            foreach (Mesh mesh in meshes)
            {
                if (mesh == null) continue;

                string radius;
                try { radius = mesh.bounds.extents.magnitude.ToString("0.######", Culture); }
                catch (Exception e) { radius = "THREW " + e.GetType().Name; }

                string verts;
                try { verts = mesh.vertexCount.ToString(); }
                catch (Exception e) { verts = "THREW " + e.GetType().Name; }

                string readable;
                try { readable = mesh.isReadable.ToString(); }
                catch (Exception e) { readable = "THREW " + e.GetType().Name; }

                // The half-extent along each axis, which is what RadiusOf takes the max of; the
                // float literal the runtime divides by is this number, so it is reported rather
                // than only the vector magnitude above.
                string extents;
                try
                {
                    Vector3 e3 = mesh.bounds.extents;
                    extents = $"{e3.x:0.######},{e3.y:0.######},{e3.z:0.######}";
                }
                catch (Exception e) { extents = "THREW " + e.GetType().Name; }

                Debug.Log($"[audit]     MESH '{mesh.name}' verts={verts} radius={radius} "
                    + $"radiusMaxAxis={RadiusMaxAxis(mesh)} extents={extents} subMeshes={mesh.subMeshCount} "
                    + $"isReadable={readable}");
            }
        }

        // The model asset the FBX path names, and the material slots its renderers carry. The runtime
        // never uses these renderers (it builds its own GameObject and assigns its own material), but
        // a material arriving here is exactly what the NO-MATERIAL gate exists to catch, so the
        // count is reported where a reviewer will see it.
        GameObject model = SafeLoad<GameObject>(bundle, MeshAssetPath);
        if (model != null)
        {
            MeshRenderer[] renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            int slots = 0;
            int filled = 0;
            foreach (MeshRenderer mr in renderers)
            {
                Material[] shared = mr.sharedMaterials;
                if (shared == null) continue;
                slots += shared.Length;
                foreach (Material m in shared)
                {
                    if (m == null) continue;
                    filled++;

                    // WHAT the non-null slot points at, because the count alone cannot distinguish
                    // the two cases that matter: a built-in default material (not packable, resolves
                    // in the player from the built-in resources) and a PROJECT material (a real asset
                    // that a future import could drag into the bundle and trip the gate with). The
                    // project check is the AssetDatabase's own answer, asked here rather than inferred
                    // from the name.
                    string origin;
                    try
                    {
                        // BOTH answers, because they separate the two cases that a material
                        // reference here can be. A built-in default material lives outside the
                        // Assets folder entirely (its asset path names an editor resource such as
                        // `Resources/unity_builtin_extra` or `Library/unity default resources`), so
                        // it cannot be packed and resolves in the player from the built-in
                        // resources; a material under `Assets/` is a real project asset that a
                        // future import could drag into the bundle and trip the gate with.
                        origin = "path='" + (AssetDatabase.GetAssetPath(m) ?? "<none>")
                            + "' inDatabase=" + AssetDatabase.Contains(m);
                    }
                    catch (Exception e) { origin = "AssetDatabase THREW " + e.GetType().Name; }

                    Debug.Log($"[audit]       MESH-MODEL material slot -> {DescribeMaterial(m)} "
                        + $"origin={origin} (the runtime builds its own GameObject and assigns its own "
                        + "material, so this reference is never read by the mod)");
                }
            }
            Debug.Log($"[audit]     MESH-MODEL '{model.name}' renderers={renderers.Length} "
                + $"materialSlots={slots} nonNullMaterials={filled}");
        }

        // The three reads, in the runtime's own order, against the delivered bytes.
        Mesh direct = SafeLoad<Mesh>(bundle, MeshAssetPath);
        Debug.Log($"[audit]   MESH-ROUTE LoadAsset<Mesh>(path) -> "
            + (direct == null ? "null" : "OK '" + direct.name + "' verts=" + direct.vertexCount));

        MeshFilter viaModel = null;
        if (model != null) viaModel = model.GetComponentInChildren<MeshFilter>(true);
        Mesh modelMesh = viaModel == null ? null : viaModel.sharedMesh;
        Debug.Log($"[audit]   MESH-ROUTE LoadAsset<GameObject>(path).MeshFilter.sharedMesh -> "
            + (modelMesh == null ? "null" : "OK '" + modelMesh.name + "' verts=" + modelMesh.vertexCount));

        Mesh byName = null;
        if (named == 1)
        {
            foreach (Mesh mesh in meshes)
            {
                if (mesh != null && string.Equals(mesh.name, MeshName, StringComparison.Ordinal))
                {
                    byName = mesh;
                    break;
                }
            }
        }
        Debug.Log($"[audit]   MESH-ROUTE LoadAllAssets<Mesh>() by name '{MeshName}' (" + named
            + " name match(es)) -> " + (byName == null ? "null" : "OK '" + byName.name + "'"));

        if (direct != null || modelMesh != null || byName != null)
        {
            Debug.Log($"[audit]   MESH OK a mesh is reachable from the delivered bytes by at least one "
                + "of the three runtime reads, so RulerGeometry ships branch=bundle-fbx (0 materials "
                + "travel with it; the sphere's material is built at runtime)");
        }
        else if (meshes.Length > 0)
        {
            Debug.LogError("[audit]   MESH FAIL the bundle carries " + meshes.Length
                + " mesh(es) but NONE is reachable by the three runtime reads - the runtime would "
                + "fall back to the code-built sphere");
        }
    }

    /// <summary>The largest half-extent of a mesh's bounds - <c>RulerGeometry.RadiusOf</c>'s number.</summary>
    /// <param name="mesh">The mesh to measure.</param>
    /// <returns>The radius as text, or a <c>THREW</c> marker.</returns>
    private static string RadiusMaxAxis(Mesh mesh)
    {
        try
        {
            Vector3 e = mesh.bounds.extents;
            float r = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
            return r.ToString("0.######", Culture);
        }
        catch (Exception e2) { return "THREW " + e2.GetType().Name; }
    }

    /// <summary>Invariant culture, so a decimal-comma locale cannot change the audit's shape.</summary>
    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;

    private static string DescribeFont(FontAsset fa)
    {
        if (fa == null) return "<null>";
        StringBuilder sb = new StringBuilder();
        try { sb.Append($"family='{(fa.faceInfo.familyName ?? "?")}' "); } catch { }
        try { sb.Append($"popMode={fa.atlasPopulationMode} "); } catch { }
        try { sb.Append($"sourceFontFile={(fa.sourceFontFile == null ? "null" : "'" + fa.sourceFontFile.name + "'")} "); } catch { }
        try
        {
            Texture2D at = fa.atlasTexture;
            sb.Append($"atlasTexture={(at == null ? "null" : "'" + at.name + "' (" + at.width + "x" + at.height + ")")} ");
        }
        catch (Exception e) { sb.Append($"atlasTexture THREW {e.GetType().Name} "); }
        try { Texture2D[] ats = fa.atlasTextures; sb.Append($"atlasTextures={(ats == null ? "null" : ats.Length.ToString())} "); } catch { }
        try { sb.Append($"material={DescribeMaterial(fa.material)}"); } catch (Exception e) { sb.Append($"material THREW {e.GetType().Name}"); }
        return sb.ToString();
    }

    private static object Field(object o, string name)
    {
        try { return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(o); }
        catch { return null; }
    }

    /// <summary>
    /// Finds the first element in the subtree that is an instance of <paramref name="t"/>.
    /// A hand-rolled walk rather than a UQuery Type overload, so that this gate is never the thing
    /// that is wrong when it reports a failure.
    /// </summary>
    private static VisualElement FindByType(VisualElement root, Type t)
    {
        if (root == null) return null;
        if (t.IsInstanceOfType(root)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            VisualElement hit = FindByType(root[i], t);
            if (hit != null) return hit;
        }
        return null;
    }

    private static IList SheetAssets(StyleSheet s)
    {
        try
        {
            FieldInfo f = typeof(StyleSheet).GetField("assets", BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(s) as IList;
        }
        catch { return null; }
    }
}
