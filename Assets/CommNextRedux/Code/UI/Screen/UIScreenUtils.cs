// CommNextRedux - the panel-space measurements the window layer positions itself with.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Screen/UIScreenUtils.cs - 24 lines with two methods,
//   `GetReferenceScreenScaledWidth()` and `GetScaledReferenceCoordinate(float)`.
//
//   Both were built out of three members that DO NOT EXIST in 0.2.8.5's UitkForKsp2, so this file is
//   RE-AUTHORED rather than ported. Measured against the installed runtime:
//
//     Configuration.IsAutomaticScalingEnabled  - absent (the whole property list of
//                                                UitkForKsp2.Configuration is mlist 40-44:
//                                                CurrentScreenWidth, CurrentScreenHeight,
//                                                CurrentScale, ScaledScreenWidth, ScaledScreenHeight)
//     Configuration.ManualUiScale              - absent, same measurement
//
//   The third legacy member is a different story, and the difference matters enough to record:
//   `UitkForKsp2.API.ReferenceResolution.Width` **does exist** on this pin - as a
//   `public static initonly int32` FIELD (monodis --fields UitkForKsp2.dll, flist 92), not a
//   property. The legacy's `ReferenceResolution` was a class in its own generation of the library;
//   here it is a static holder plus three conversion helpers. The premise that the member is missing
//   would have sent a reader looking for a replacement that was already there.
//
// WHAT THE THREE LIVE MEMBERS ACTUALLY MEAN (IL, not documentation)
//   `Configuration.get_CurrentScreenWidth()` is `ldsfld ReferenceResolution::Width` - i.e. it IS the
//   reference resolution, not the physical screen. `get_ScaledScreenWidth()` is
//   `CurrentScale * ReferenceResolution.Width` - i.e. the PHYSICAL pixel width. `get_CurrentScale()`
//   is `UitkForKsp2Plugin.PanelSettings.scale`.
//
//   So the panel's coordinate space - the space an element's `style.left` and the `size` argument of
//   `Extensions.SetDefaultPosition` live in - is the reference space, and the physical screen is that
//   space times the live scale. The legacy's two methods were solving a problem created by automatic
//   scaling being switchable; in this runtime the panel always scales, so:
//
//     * "the reference screen's scaled width" IS `Configuration.CurrentScreenWidth` (the panel is
//       `ReferenceResolution` wide in its own coordinates), and
//     * "a scaled reference coordinate" is that coordinate UNCHANGED - the identity function the
//       legacy's `IsAutomaticScalingEnabled ? coordinate : ...` returned in its automatic branch.
//
//   That is why this file has no `GetScaledReferenceCoordinate`: keeping an identity method would
//   only invite a future reader to scale a number twice. The one thing the legacy could not ask for
//   and this port can is the panel's REAL width, which is what a window pinned to the right edge
//   needs on an aspect ratio the reference resolution does not share - hence `PanelWidth`.

using UitkForKsp2;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Screen
{
    /// <summary>
    /// Panel-space screen metrics for window placement.
    /// </summary>
    /// <remarks>
    /// Every member here answers a question in the UI panel's own coordinate space. Nothing multiplies
    /// by a scale factor: see the file header, where the scaling is shown to be the panel's job.
    /// </remarks>
    public static class UIScreenUtils
    {
        /// <summary>The toolbar's inset from the right edge, in panel coordinates.</summary>
        /// <remarks>
        /// The legacy's `GetScaledReferenceCoordinate(28f)`, which in this runtime is that coordinate
        /// unchanged (see the header). Kept as the legacy's number: the placement is a cosmetic
        /// choice the port has no reason to re-tune, and re-tuning it would make "the toolbar moved"
        /// indistinguishable from "the placement is wrong".
        /// </remarks>
        public const float ToolbarRightInset = 28f;

        /// <summary>The toolbar's inset from the top edge, in panel coordinates.</summary>
        /// <remarks>The legacy's `GetScaledReferenceCoordinate(300f)`, unchanged for the same reason.</remarks>
        public const float ToolbarTopInset = 300f;

        /// <summary>The reference resolution's width, in panel coordinates.</summary>
        /// <remarks>
        /// `Configuration.CurrentScreenWidth` returns exactly `ReferenceResolution.Width` (IL:
        /// `ldsfld`), i.e. a constant of the UI generation rather than the player's monitor. It is the
        /// correct value for "how wide is a panel whose screen matches the reference aspect ratio".
        /// </remarks>
        public static float ReferenceWidth
        {
            get { return Configuration.CurrentScreenWidth; }
        }

        /// <summary>The reference resolution's height, in panel coordinates.</summary>
        public static float ReferenceHeight
        {
            get { return Configuration.CurrentScreenHeight; }
        }

        /// <summary>
        /// The live panel's width in panel coordinates, falling back to
        /// <see cref="ReferenceWidth"/> when the element is not attached yet.
        /// </summary>
        /// <param name="element">An element that is (or is about to be) attached to the panel.</param>
        /// <returns>The panel's content width, or the reference width.</returns>
        /// <remarks>
        /// <para>
        /// This is the value a right-edge placement wants. A panel using `ScaleWithScreenSize` with a
        /// match factor is not exactly <c>ReferenceResolution</c> wide in its own space on a display
        /// whose aspect ratio differs from the reference's - it is the physical width divided by the
        /// live scale - so anchoring with the constant alone would inset or overhang the toolbar by the
        /// difference. The live rect has no such error, and it costs one property read.
        /// </para>
        /// <para>
        /// The fallback is not decoration: `VisualElement.panel` is null until the element is attached,
        /// and `SetDefaultPosition`'s callback is also called on a geometry change, at which point the
        /// panel is live. Before that the reference width is the best available answer.
        /// </para>
        /// </remarks>
        public static float PanelWidth(VisualElement element)
        {
            float width = PanelSize(element, true);
            return width > 0f ? width : ReferenceWidth;
        }

        /// <summary>The live panel's height in panel coordinates, with the same fallback.</summary>
        /// <param name="element">An element that is (or is about to be) attached to the panel.</param>
        /// <returns>The panel's content height, or the reference height.</returns>
        public static float PanelHeight(VisualElement element)
        {
            float height = PanelSize(element, false);
            return height > 0f ? height : ReferenceHeight;
        }

        /// <summary>
        /// The map toolbar's default position: top-right, inset from both edges.
        /// </summary>
        /// <param name="size">The toolbar's own size, as <c>SetDefaultPosition</c> passes it.</param>
        /// <param name="panelWidth">The live panel's width, from <see cref="PanelWidth"/>.</param>
        /// <returns>The position to hand back to <c>SetDefaultPosition</c>.</returns>
        /// <remarks>
        /// <para>
        /// The legacy's arithmetic - right edge inset by the element's own width plus
        /// <see cref="ToolbarRightInset"/>, and <see cref="ToolbarTopInset"/> down from the top - with
        /// one substitution: the width it subtracts from is the <b>live panel's</b>, not the reference
        /// resolution's. On a display whose aspect ratio differs from the reference's the two are not
        /// equal (the panel is the physical size divided by the live scale), and the reference constant
        /// would then put the toolbar off the right edge by the difference. `PanelWidth` falls back to
        /// the reference width when there is no panel yet, which is the only case where the constant is
        /// the best answer available.
        /// </para>
        /// <para>
        /// <b>The space is assumed to be panel coordinates.</b> The XML doc for the member says the
        /// callback returns "the position to set the element to in the reference resolution", and the
        /// library's own reference resolution is 1920x1080 while the panel's is not always. Both
        /// readings agree on a 16:9 display; on a wider one they differ by a scale factor on the
        /// horizontal axis only. The launch settles it: a toolbar at the top right means the space
        /// agreed, and a toolbar inset from the right by a small multiple of its own width means the
        /// library scaled the value and this method must return reference units instead - which is a
        /// one-line change (`ReferenceWidth` instead of `panelWidth`).
        /// </para>
        /// </remarks>
        public static Vector2 ToolbarDefaultPosition(Vector2 size, float panelWidth)
        {
            return new Vector2(panelWidth - size.x - ToolbarRightInset, ToolbarTopInset);
        }

        /// <summary>
        /// One line of the four numbers the panel reports, for the launch log.
        /// </summary>
        /// <returns>A compact description of the reference resolution and the live scale.</returns>
        /// <remarks>
        /// Cheap and worth its line: every window-placement question this phase can raise ("did the
        /// toolbar land on the right edge?", "was the panel wider than the reference?") is answered
        /// from these four numbers plus the position the toolbar logs.
        /// </remarks>
        public static string Describe()
        {
            return "reference=" + ReferenceWidth + "x" + ReferenceHeight
                + " scale=" + Configuration.CurrentScale
                + " physical=" + Configuration.ScaledScreenWidth + "x"
                + Configuration.ScaledScreenHeight;
        }

        /// <summary>Reads the live panel's content rect, one axis at a time.</summary>
        /// <param name="element">The element whose panel to measure.</param>
        /// <param name="width">Which axis: <c>true</c> for width, <c>false</c> for height.</param>
        /// <returns>The measurement, or <c>0</c> when there is no panel.</returns>
        /// <remarks>
        /// `IPanel.visualTree` is the panel's root element and its `contentRect` is the panel's own
        /// size - the same reading a drag clamp uses, and null-guarded here because a window's root is
        /// measured during the geometry callback that can outlive its panel on a teardown.
        /// </remarks>
        private static float PanelSize(VisualElement element, bool width)
        {
            if (element == null)
            {
                return 0f;
            }

            IPanel panel = element.panel;
            if (panel == null)
            {
                return 0f;
            }

            VisualElement root = panel.visualTree;
            if (root == null)
            {
                return 0f;
            }

            Rect rect = root.contentRect;
            return width ? rect.width : rect.height;
        }
    }
}
