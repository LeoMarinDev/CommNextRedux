// Pre-deploy render proof for the commnextredux_ui bundle.
//
// Invoked by Tools/build-ui-bundle.sh via a second batchmode run on the same generated
// project:
//
//   Unity -batchmode -nographics -quit -projectPath <proj> \
//         -executeMethod VerifyCommNextReduxUxml.Verify -logFile -
//
// Why this exists ("loads" != "renders"): a bundle the runtime accepts can still contain
// VisualTreeAssets that clone with ZERO children - a 2022.3.5f1-built bundle does exactly that.
// Importing without errors is not evidence the UI renders, so this script instantiates every
// VisualTreeAsset in the project and logs the clone's childCount. Any zero, any load failure and
// any thrown exception is a failure.
//
// It also probes a handful of element names that must exist in the instantiated window. Those
// names are what proves the rebuilt bundle carries this port's pages and not a stale/other
// build - a marker that is missing is a hard failure, not a warning.
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class VerifyCommNextReduxUxml
{
    private const string UiDir = "Assets/CommNextRedux/UI";
    private const string WindowPath = "Assets/CommNextRedux/UI/CN_UI.uxml";

    // (uxml path, element name that must exist in the instantiated tree)
    //
    // RE-POINTED IN THE CommNextRedux TRANSPLANT. The donor's list named FlightPlan's own window
    // chrome ("GUIFrame", "CloseButton", "TabBar", "BottomPanel", "ButtonBar"), which no CommNext
    // page will ever carry - leaving it would have made this gate fail a correct build, which is
    // worse than having no gate at all. The names below are the LEGACY CommNext's, read out of
    // mods-outdated/CommNext/src/CommNext.Unity/CommNext.Unity/Assets/UI/CommNextMapToolbar.uxml
    // (the map toolbar is the mod's signature UI and the natural single root page):
    //
    //   $ rg -o 'name="[^"]+"' .../Assets/UI/CommNextMapToolbar.uxml | sort -u
    //   name="lines-button"  name="rulers-button"  name="toolbar"  name="vessel-report-button"
    //
    // THE UI PHASE OWNS THIS LIST. It must be re-pointed again if the port's root page ends up
    // carrying different names (the legacy also ships VesselReportWindow.uxml and
    // TooltipWindow.uxml, whose elements are listed in Deploy/obj/PORT-PROGRESS.md). A probe that
    // is missing is a HARD FAILURE - so an un-re-pointed list blocks the build rather than
    // silently passing it, which is the safe direction for this gate to be wrong in.
    private static readonly (string Path, string Probe)[] MarkerProbes =
    {
        ("Assets/CommNextRedux/UI/CN_UI.uxml", "toolbar"),
        ("Assets/CommNextRedux/UI/CN_UI.uxml", "lines-button"),
        ("Assets/CommNextRedux/UI/CN_UI.uxml", "rulers-button"),
        ("Assets/CommNextRedux/UI/CN_UI.uxml", "vessel-report-button"),
    };

    // (uxml path, element name, the FULL type name that element must resolve to)
    //
    // PHASE 2 - THE CUSTOM-CONTROL HALF OF THIS GATE.
    //
    // The legacy markup names three of its OWN controls as dotted element types, with no XML
    // namespace prefix:
    //
    //   <CommNext.Unity.Runtime.Controls.BandIcon color="#9A21B4FF" code="X" name="band-icon" .../>
    //
    // The UXML importer resolves that name against the assemblies the throwaway project has
    // loaded, so a missing type is an unimportable page, not a warning. Phase 1 measured the
    // element census with a colon-requiring pattern and concluded no custom element existed -
    // see the PHASE 2 CORRECTION in Tools/build-ui-bundle.sh. It does; these three are it.
    //
    // This is the EDITOR half of the proof (the type resolved from the copied sources into the
    // project's own Assembly-CSharp). The PLAYER half is the same assertion against an instance
    // cloned from the BUILT bundle, made by Tools/diag/DiagPlugin.cs in-game - there the type can
    // only come from CommNextRedux.dll, which is the assembly the port actually ships.
    //
    // PHASE 2 CORRECTION - WHAT THIS GATE ACTUALLY CAUGHT. Its first run reported all three
    // probes as CONTROL NOT FOUND, which reads as "the type did not resolve". It was not: the tree
    // dump added the same day shows all three types resolving correctly. The name attribute was
    // being dropped - `VisualElement.UxmlTraits` declares ZERO attribute fields on 6000.4.1f1, so a
    // legacy-traits control gets only the attributes its own traits declare, and `name` is not one
    // of them unless the control re-declares it. So the probes are run BOTH ways now: by type (does
    // the control exist at all) and by name (is it reachable the way P8's controllers will reach
    // it). Conflating the two turned a one-line fix into a hunt. Evidence: Deploy/obj/bundle-verdict.md.
    private static readonly (string Path, string Probe, string Type, Type ClrType)[] ControlProbes =
    {
        ("Assets/CommNextRedux/UI/Components/BandRow.uxml", "band-icon",
         "CommNext.Unity.Runtime.Controls.BandIcon", typeof(CommNext.Unity.Runtime.Controls.BandIcon)),
        ("Assets/CommNextRedux/UI/Components/NetworkConnectionView.uxml", "signal-strength-icon",
         "CommNext.Unity.Runtime.Controls.SignalStrengthIcon", typeof(CommNext.Unity.Runtime.Controls.SignalStrengthIcon)),
        ("Assets/CommNextRedux/UI/VesselReportWindow.uxml", "sort-direction-button",
         "CommNext.Unity.Runtime.Controls.SortDirectionButton", typeof(CommNext.Unity.Runtime.Controls.SortDirectionButton)),
    };

    public static void Verify()
    {
        int failures = 0;

        var guids = AssetDatabase.FindAssets("t:VisualTreeAsset", new[] { UiDir });
        var paths = new List<string>();
        foreach (var guid in guids)
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        paths.Sort(StringComparer.Ordinal);

        Debug.Log($"[uxml-verify] found {paths.Count} VisualTreeAsset(s) under {UiDir}");

        var instantiated = new Dictionary<string, VisualElement>();
        foreach (var path in paths)
        {
            var vta = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            if (vta == null)
            {
                Debug.LogError($"[uxml-verify] {path} childCount=LOAD-FAILED");
                failures++;
                continue;
            }

            try
            {
                var instance = vta.Instantiate();
                int childCount = instance == null ? -1 : instance.childCount;
                Debug.Log($"[uxml-verify] {path} childCount={childCount}");
                if (childCount <= 0)
                    failures++;
                else
                {
                    instantiated[path] = instance;
                    // The full tree, capped. childCount alone cannot tell "the third element is a
                    // plain VisualElement because the type did not resolve" from "the element is
                    // simply absent", and those are different bugs with different fixes.
                    int budget = 40;
                    DumpTree(instance, path, 0, ref budget);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[uxml-verify] {path} THREW {e.GetType().Name}: {e.Message}");
                failures++;
            }
        }

        foreach (var probe in MarkerProbes)
        {
            if (!instantiated.TryGetValue(probe.Path, out var root))
                continue;   // that template already failed above

            var element = root.Q(probe.Probe);
            if (element == null)
            {
                Debug.LogError($"[uxml-verify] MARKER MISSING {probe.Path} :: {probe.Probe}");
                failures++;
            }
            else
            {
                Debug.Log($"[uxml-verify] marker found: {probe.Path} :: {probe.Probe} ({element.GetType().Name})");
            }
        }

        // The custom-control assertions, run TWO ways. A wrong or missing type is a hard failure: it
        // means the markup names a type the loaded assemblies do not define, which is how a page
        // becomes unimportable and how the build dies. A missing NAME while the type resolves is a
        // different, quieter bug: the element is there but every Q("name") lookup for it returns
        // null, so a controller binds nothing and the window renders empty with no error at all.
        foreach (var probe in ControlProbes)
        {
            if (!instantiated.TryGetValue(probe.Path, out var root))
                continue;   // that template already failed above

            var byType = FindByType(root, probe.ClrType);
            if (byType == null)
            {
                Debug.LogError($"[uxml-verify] CONTROL NOT FOUND BY TYPE {probe.Path} " +
                               $"(expected an element of type {probe.Type})");
                failures++;
                continue;
            }

            string actual = byType.GetType().FullName;
            if (actual != probe.Type)
            {
                Debug.LogError($"[uxml-verify] CONTROL TYPE MISMATCH {probe.Path} :: {probe.Probe} " +
                               $"expected={probe.Type} actual={actual}");
                failures++;
                continue;
            }

            Debug.Log($"[uxml-verify] custom control resolved (by type): {probe.Path} :: {probe.Probe} -> " +
                      $"{actual} (from the sources copied into this project)");

            var byName = root.Q(probe.Probe);
            if (byName == null)
            {
                Debug.LogError($"[uxml-verify] CONTROL NAME DROPPED {probe.Path} :: '{probe.Probe}' - the " +
                               $"control of type {actual} is present but carries no such name, so every " +
                               $"Q(\"{probe.Probe}\") lookup returns null. Re-declare `name` in that control's " +
                               "UxmlTraits (see Code/UI/Controls/*.cs and Deploy/obj/bundle-verdict.md).");
                failures++;
            }
            else if (byName != byType)
            {
                Debug.LogError($"[uxml-verify] CONTROL NAME COLLISION {probe.Path} :: '{probe.Probe}' resolves " +
                               $"to {byName.GetType().FullName} but the control under test is {actual}");
                failures++;
            }
            else
            {
                Debug.Log($"[uxml-verify] custom control reachable by name: {probe.Path} :: " +
                          $"Q(\"{probe.Probe}\") -> {actual}");
            }
        }

        // A window whose root element exists but has no children would still be a blank window,
        // so log the window's own child tree explicitly and fail if the root page is empty.
        if (instantiated.TryGetValue(WindowPath, out var window))
        {
            var childNames = new List<string>();
            for (int i = 0; i < window.childCount; i++)
                childNames.Add(window[i].name);
            Debug.Log($"[uxml-verify] {WindowPath} root ({window.GetType().Name}) childCount={window.childCount} " +
                      $"children=[{string.Join(", ", childNames)}]");
            if (window.childCount <= 0)
                failures++;
        }

        Debug.Log($"[uxml-verify] result: {paths.Count} template(s), {failures} failure(s)");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Finds the first element in the subtree that is an instance of <paramref name="t"/>.
    /// Deliberately a hand-rolled walk rather than a UQuery Type overload: this gate must not be
    /// the thing that is wrong when it reports a failure.
    /// </summary>
    private static VisualElement FindByType(VisualElement root, Type t)
    {
        if (root == null) return null;
        if (t.IsInstanceOfType(root)) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindByType(root[i], t);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// Logs an instantiated tree as `path :: depth-indent TypeFullName name='x' class=[...]`.
    /// A custom control whose type did not resolve shows up here as a plain `VisualElement` (or as
    /// nothing at all), which is the distinction the childCount gate cannot make.
    /// </summary>
    private static void DumpTree(VisualElement root, string path, int depth, ref int budget)
    {
        if (root == null || budget <= 0) return;
        budget--;

        var classes = new List<string>();
        foreach (string c in root.GetClasses()) classes.Add(c);
        string cls = classes.Count == 0 ? "" : " class=[" + string.Join(" ", classes.ToArray()) + "]";
        Debug.Log($"[uxml-verify] tree {path} :: {new string(' ', depth * 2)}{root.GetType().FullName} " +
                  $"name='{(string.IsNullOrEmpty(root.name) ? "<unnamed>" : root.name)}'" + cls);

        for (int i = 0; i < root.childCount; i++)
            DumpTree(root[i], path, depth + 1, ref budget);
    }
}
