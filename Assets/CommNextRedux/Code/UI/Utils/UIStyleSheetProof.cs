// CommNextRedux - the stylesheet proof: the numbers that say whether CommNextStyles.uss reached the
// player's window, as opposed to whether the editor imported it.
//
// WHY THIS FILE EXISTS
//   Every UXML in this mod references the sheet through the Unity EDITOR's asset scheme:
//
//     <Style src="project://database/Assets/UI/CommNextStyles.uss?fileID=...&guid=a2dd25c9...&type=3" />
//
//   That path is the legacy project's; the sheet actually lives at
//   `Assets/CommNextRedux/UI/CommNextStyles.uss` here. Path resolution in `project://database/` is by
//   GUID, and the `.meta` files were preserved for exactly this reason - all 15 `guid=` references in
//   the sheet resolve to real `.meta` files in this project, and the sheet ships inside the bundle as
//   the `CommNextStyles` asset (`Deploy/obj/bundle-audit.log`), so the import side is not in doubt.
//   The bundle side is: P2's own measurement was that an editor-side render proof does not survive the
//   bundle round-trip (the material, F14/D3). Assume nothing; read the resolved numbers in the player.
//
// WHY "ATTACHED" IS NOT THE ASSERTION
//   `CommNextUIManager.EnsureStylesAttached` guarantees a `CommNextStyles` sheet is in a `styleSheets`
//   list on the window's ancestor chain - by the template's own reference when that resolves, or by
//   attaching the bundle's copy when it does not. Neither branch proves the sheet is *in effect*: a
//   sheet that is in the list can have every rule it declares overridden, and a `project://` reference
//   can resolve to a sheet whose own asset references did not survive the bundle. So this file reads
//   the element's RESOLVED values back and asserts values that only the sheet can produce.
//
// THE ASSERTIONS ARE CHOSEN SO THAT ONLY THE SHEET CAN SATISFY THEM
//   The toolbar's UXML carries a LOT of inline style (`style="flex-grow: 0; width: 100px; height: 40px;
//   ..."`), so those properties cannot be used as evidence: an element with no sheet at all satisfies
//   them. Four that the sheet alone provides:
//
//     * `border-top-left-radius` = 8          - `.toolbar { border-radius: 8px }`; nothing inline sets
//                                               a radius and Unity's default is 0.
//     * `background-color` = rgb(0,0,0)       - `.toolbar { background-color: var(--black) }`; the
//                                               value is a CSS VARIABLE, so this one also proves the
//                                               sheet's `:root` block reached the player. An
//                                               unresolved variable leaves the default (transparent).
//     * `border-top-color` = rgb(83,86,204)   - `.toolbar { border-color: var(--purple-4) }`; a second
//                                               variable, and a second rule, so "one rule happened to
//                                               match" cannot explain the result.
//     * the lines button's background image is non-null - the image is reached through
//                                               `url('project://database/Assets/Images/CommIcon.png?guid=...')`
//                                               INSIDE THE SHEET, so this is the one assertion that
//                                               tests asset-reference resolution across the bundle
//                                               round-trip - the same class of failure P2's material
//                                               hit. All three connection-mode classes on that button
//                                               (`--comm-none`, `--comm-lines`, `--comm-active`) set a
//                                               background image, so the check is valid in every mode
//                                               the button can be in.
//
//   The tooltip gets the same treatment, and there it is the *only* page whose image check is worth
//   reading: `.tooltip`'s background image and its tint are unconditional on that element and nothing
//   inline touches either, so a pass means the sheet is in effect on the second window too - which is
//   what makes "the sheet resolved for the whole mod" a statement about two windows rather than one.
//
//   The report page cannot reuse the toolbar's four checks, and **F65** is that measurement: its
//   header row is *named* `toolbar` - the name a `Q("toolbar")` finds first - but it carries none of
//   the sheet's `.toolbar` class, and the page has no `lines-button`. Running the toolbar's checks on
//   it reads four defaults and calls a correctly styled page a failure. `ProveReport` reads a
//   different four, anchored on elements this page really has: `#close-button`'s image (through
//   `DescribeBackground`, so all four `Background` fields are consulted - D43/F62), that button's
//   `:root`-variable tint, its sheet-only width, and the inline dropdown's label, which only a
//   *descendant* rule can hide. That makes "the sheet resolved for the whole mod" a statement about
//   three windows.
//
// WHEN IT RUNS
//   `resolvedStyle` is only meaningful after a layout pass, and `Window.Create` returns a document
//   whose tree has not been laid out yet - reading it at bind time can legitimately return the
//   defaults. So each window runs this from a one-shot `GeometryChangedEvent`, and `CommNextUIManager`
//   logs the settled verdict through `Why(proved)` so the summary line cannot overclaim.

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// Reads a window's resolved style and reports whether the port's stylesheet is in effect.
    /// </summary>
    /// <remarks>
    /// Read-only, and safe to call more than once - it is a measurement, not a state change. The two
    /// pages keep separate verdicts because they can differ: a sheet can be in effect on the toolbar
    /// and not on the tooltip if only one window got the runtime attach.
    /// </remarks>
    public static class UIStyleSheetProof
    {
        /// <summary>`.toolbar { border-radius: 8px }`, the sheet's own value.</summary>
        public const float ExpectedBorderRadius = 8f;

        /// <summary>`.tooltip__text { font-size: 10px }`, the sheet's own value.</summary>
        public const float ExpectedTooltipFontSize = 10f;

        /// <summary>`#close-button { width: 12px }`, the sheet's own value.</summary>
        public const float ExpectedCloseButtonWidth = 12f;

        /// <summary>`-unity-background-image-tint-color: var(--background-lightgray-4)` on
        /// `#close-button` - the variable's value, read out of the sheet's `:root` block.</summary>
        public static readonly Color ExpectedCloseButtonTint = new Color(122f / 255f, 133f / 255f,
            153f / 255f);

        /// <summary>The toolbar verdict from the last <see cref="Prove"/> call.</summary>
        public static bool ToolbarVerdict { get; private set; }

        /// <summary>How many of the toolbar's four checks passed on the last run.</summary>
        public static int ToolbarPassed { get; private set; }

        /// <summary>The tooltip verdict from the last <see cref="ProveTooltip"/> call.</summary>
        public static bool TooltipVerdict { get; private set; }

        /// <summary>How many of the tooltip's three checks passed on the last run.</summary>
        public static int TooltipPassed { get; private set; }

        /// <summary>The report verdict from the last <see cref="ProveReport"/> call.</summary>
        public static bool ReportVerdict { get; private set; }

        /// <summary>How many of the report's four checks passed on the last run.</summary>
        public static int ReportPassed { get; private set; }

        /// <summary>One line naming both verdicts, for the manager's summary.</summary>
        /// <returns>`toolbar=PASS (4/4) tooltip=PASS (3/3)`, or `not run` for a page that never ran.</returns>
        /// <remarks>
        /// A page that never ran is reported as `not run` rather than as a failure, because it is a
        /// different fact: the toolbar not existing and the tooltip not having laid out yet both
        /// leave zero readings, and conflating them would make the summary ambiguous in exactly the
        /// case it exists to disambiguate.
        /// </remarks>
        public static string Summary()
        {
            return "toolbar=" + (ToolbarRan
                    ? (ToolbarVerdict ? "PASS" : "FAIL") + " (" + ToolbarPassed + "/4)"
                    : "not run")
                + " tooltip=" + (TooltipRan
                    ? (TooltipVerdict ? "PASS" : "FAIL") + " (" + TooltipPassed + "/3)"
                    : "not run")
                + " report=" + (ReportRan
                    ? (ReportVerdict ? "PASS" : "FAIL") + " (" + ReportPassed + "/4)"
                    : "not run");
        }

        /// <summary>Whether <see cref="Prove"/> has run at least once.</summary>
        public static bool ToolbarRan { get; private set; }

        /// <summary>Whether <see cref="ProveTooltip"/> has run at least once.</summary>
        public static bool TooltipRan { get; private set; }

        /// <summary>Whether <see cref="ProveReport"/> has run at least once.</summary>
        public static bool ReportRan { get; private set; }

        /// <summary>
        /// Reads the map toolbar's resolved style and logs each number with its verdict.
        /// </summary>
        /// <param name="windowRoot">The window's bound root (the toolbar's TemplateContainer child).</param>
        /// <param name="tag">Line prefix, so a second reading can be told from the first.</param>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink, for the per-check failures.</param>
        /// <returns><c>true</c> when all four checks passed.</returns>
        public static bool Prove(VisualElement windowRoot, string tag, Action<string> log,
            Action<string> warn)
        {
            ToolbarRan = true;
            ToolbarPassed = 0;
            ToolbarVerdict = false;

            if (windowRoot == null)
            {
                Write(warn, tag + ": no window root to measure - the stylesheet cannot be verified");
                return false;
            }

            VisualElement toolbar = windowRoot.Q("toolbar");
            if (toolbar == null)
            {
                Write(warn, tag + ": the window root has no element named 'toolbar' - the template is not "
                    + "the one this proof was written for, so the stylesheet cannot be verified");
                return false;
            }

            IResolvedStyle style = toolbar.resolvedStyle;
            int checks = 0;
            int passed = 0;

            // 1. USS-only rule: the radius.
            checks++;
            float radius = style.borderTopLeftRadius;
            bool radiusOk = Mathf.Abs(radius - ExpectedBorderRadius) < 0.01f;
            if (radiusOk)
            {
                passed++;
            }

            Line(log, warn, radiusOk, tag + ": border-top-left-radius = " + radius.ToString("0.###")
                + " (expected " + ExpectedBorderRadius.ToString("0.###") + " from .toolbar) - "
                + (radiusOk ? "the sheet's .toolbar rule is in effect"
                    : "NO SHEET: the UXML sets no radius and Unity's default is 0"));

            // 2. USS-only colour, reached through the sheet's :root variable table.
            checks++;
            Color background = style.backgroundColor;
            bool backgroundOk = Mathf.Abs(background.r) < 0.004f && Mathf.Abs(background.g) < 0.004f
                && Mathf.Abs(background.b) < 0.004f && Mathf.Abs(background.a - 1f) < 0.004f;
            if (backgroundOk)
            {
                passed++;
            }

            Line(log, warn, backgroundOk, tag + ": toolbar background-color = " + Describe(background)
                + " (expected rgb(0,0,0) a=1 from .toolbar's var(--black)) - "
                + (backgroundOk ? "the sheet's :root variables resolved"
                    : "NO SHEET or no variables: the default background is transparent"));

            // 3. A second variable, from a property no inline style touches.
            checks++;
            Color border = style.borderTopColor;
            bool borderOk = Mathf.Abs(border.r - (83f / 255f)) < 0.004f
                && Mathf.Abs(border.g - (86f / 255f)) < 0.004f
                && Mathf.Abs(border.b - (204f / 255f)) < 0.004f;
            if (borderOk)
            {
                passed++;
            }

            Line(log, warn, borderOk, tag + ": toolbar border-top-color = " + Describe(border)
                + " (expected rgb(83,86,204) from .toolbar's var(--purple-4)) - "
                + (borderOk ? "a second rule and a second variable are in effect"
                    : "NO SHEET: the UXML sets no border colour, so this is the default"));

            // 4. An image reached through a url(...) INSIDE the sheet.
            //
            // READ ALL FOUR FIELDS, NOT .texture (F62). A USS url() to a PNG imported as a Sprite
            // (textureType: 8 - every PNG under Assets/CommNextRedux/UI/Images/ is) resolves into
            // `Background.sprite` and leaves `Background.texture` NULL. A .texture-only read
            // therefore reports a correctly-resolved sheet as a failure - a false RED, which is
            // worse than no gate at all: it hides a real regression behind a permanent false one.
            checks++;
            VisualElement linesButton = toolbar.Q("lines-button");
            string image = null;
            if (linesButton != null)
            {
                image = DescribeBackground(linesButton.resolvedStyle.backgroundImage);
            }

            bool imageOk = image != null;
            if (imageOk)
            {
                passed++;
            }

            Line(log, warn, imageOk, tag + ": lines-button background image = "
                + (image == null ? "null" : image)
                + " (expected the sheet's CommIcon.png in any comm mode) - "
                + (imageOk ? "the sheet's project:// image reference survived the bundle round-trip"
                    : "NO IMAGE: the sheet's url(...) asset reference is null in the player - the sheet's "
                        + "asset references did not resolve, which is F14's failure class"));

            ToolbarPassed = passed;
            ToolbarVerdict = passed == checks;

            Write(log, tag + ": verdict=" + (ToolbarVerdict ? "PASS" : "FAIL") + " (" + passed + "/" + checks
                + " checks) - " + (ToolbarVerdict
                    ? "CommNextStyles.uss is in effect on the player's toolbar"
                    : "the toolbar is unstyled or partly styled; the log lines above name which check "
                        + "failed"));

            return ToolbarVerdict;
        }

        /// <summary>
        /// Reads the vessel report's resolved style and logs each number with its verdict.
        /// </summary>
        /// <param name="windowRoot">The report window's bound root.</param>
        /// <param name="tag">Line prefix, so a second reading can be told from the first.</param>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink, for the per-check failures.</param>
        /// <returns><c>true</c> when all four checks passed.</returns>
        /// <remarks>
        /// <b>Why this is not <see cref="Prove"/> (F65).</b> The report page also carries an element
        /// *named* `toolbar` - it is the header row the legacy template kept - but that element has
        /// none of the sheet's `.toolbar` class and the page has no `lines-button` at all. Feeding
        /// this page to <see cref="Prove"/> therefore reads four defaults and reports
        /// `FAIL (0/4)` on a page that is correctly styled: a permanent false RED, which is the
        /// failure mode the header of this file calls out. It would also write the *toolbar's*
        /// statics on its way out. The four checks below are anchored on elements this page really
        /// has, and every expected value is the sheet's own:
        ///
        /// 1. `#close-button`'s background image - `#close-button { background-image: url(…
        ///    ICO-Close-med.png) }`, and the report's close button carries no inline image. Read
        ///    through <see cref="DescribeBackground"/>, so all four `Background` fields are consulted
        ///    (D43/F62) and the field it arrived in is named in the log.
        /// 2. `#close-button`'s `-unity-background-image-tint-color` = `var(--
        ///    background-lightgray-4)` = rgb(122,133,153) - a variable from the sheet's `:root`
        ///    block, on a property nothing inline sets (Unity's default tint is white).
        /// 3. `#close-button`'s width = 12px - `#close-button { width: 12px }`; the UXML's button has
        ///    no inline width, so a 12 here can only come from the sheet.
        /// 4. the filter dropdown's label is `display: none` - `.dropdown-field--inline
        ///    .unity-popup-field__label { display: none }` is a **descendant** rule, and no inline
        ///    style on the dropdown can hide a child. This is the one check that proves the sheet
        ///    reaches *into* the page rather than only styling its top-level elements.
        /// </remarks>
        public static bool ProveReport(VisualElement windowRoot, string tag, Action<string> log,
            Action<string> warn)
        {
            ReportRan = true;
            ReportPassed = 0;
            ReportVerdict = false;

            if (windowRoot == null)
            {
                Write(warn, tag + ": no window root to measure - the stylesheet cannot be verified");
                return false;
            }

            VisualElement close = windowRoot.Q("close-button");
            if (close == null)
            {
                Write(warn, tag + ": the report page has no element named 'close-button' - the template "
                    + "is not the one this proof was written for, so the stylesheet cannot be verified");
                return false;
            }

            IResolvedStyle style = close.resolvedStyle;
            int checks = 0;
            int passed = 0;

            // 1. An image reached through a url(...) INSIDE the sheet, read across all four fields.
            checks++;
            string image = DescribeBackground(style.backgroundImage);
            bool imageOk = image != null;
            if (imageOk)
            {
                passed++;
            }

            Line(log, warn, imageOk, tag + ": close-button background image = "
                + (image == null ? "null" : image)
                + " (expected the sheet's ICO-Close-med.png) - "
                + (imageOk ? "the sheet's project:// image reference survived the bundle round-trip"
                    : "NO IMAGE: the sheet's url(...) asset reference is null in the player - the sheet's "
                        + "asset references did not resolve, which is F14's failure class"));

            // 2. A colour the sheet reaches through its own :root variable table.
            checks++;
            Color tint = style.unityBackgroundImageTintColor;
            bool tintOk = Mathf.Abs(tint.r - ExpectedCloseButtonTint.r) < 0.004f
                && Mathf.Abs(tint.g - ExpectedCloseButtonTint.g) < 0.004f
                && Mathf.Abs(tint.b - ExpectedCloseButtonTint.b) < 0.004f;
            if (tintOk)
            {
                passed++;
            }

            Line(log, warn, tintOk, tag + ": close-button image tint = " + Describe(tint)
                + " (expected rgb(122,133,153) from #close-button's var(--background-lightgray-4)) - "
                + (tintOk ? "the sheet's :root variables resolved on this page too"
                    : "NO SHEET or no variables: nothing else sets a tint here, so this is the default"));

            // 3. A metric only the sheet declares.
            checks++;
            float width = style.width;
            bool widthOk = Mathf.Abs(width - ExpectedCloseButtonWidth) < 0.5f;
            if (widthOk)
            {
                passed++;
            }

            Line(log, warn, widthOk, tag + ": close-button width = " + width.ToString("0.###")
                + " (expected " + ExpectedCloseButtonWidth.ToString("0.###")
                + " from #close-button) - "
                + (widthOk ? "the sheet's #close-button rule is in effect"
                    : "NO SHEET: the UXML sets no width on this button, so this is the layout's own"));

            // 4. A descendant rule - the inline dropdown hides its own label.
            checks++;
            VisualElement filter = windowRoot.Q("filter-dropdown");
            Label label = filter == null ? null : filter.Q<Label>(className: "unity-popup-field__label");
            DisplayStyle display = label == null ? DisplayStyle.Flex : label.resolvedStyle.display;
            bool labelOk = label != null && display == DisplayStyle.None;
            if (labelOk)
            {
                passed++;
            }

            Line(log, warn, labelOk, tag + ": filter-dropdown's label display = " + display
                + " (expected None from .dropdown-field--inline's descendant rule) - "
                + (labelOk ? "the sheet reaches into the page, not just its top-level elements"
                    : (label == null
                        ? "NO LABEL: the dropdown has no '.unity-popup-field__label' child, so this "
                            + "check cannot report on the sheet"
                        : "NO SHEET: the label is visible, so the descendant rule is not in effect")));

            ReportPassed = passed;
            ReportVerdict = passed == checks;

            Write(log, tag + ": verdict=" + (ReportVerdict ? "PASS" : "FAIL") + " (" + passed + "/" + checks
                + " checks) - " + (ReportVerdict
                    ? "CommNextStyles.uss is in effect on the player's vessel report"
                    : "the report window is unstyled or partly styled; the log lines above name which "
                        + "check failed"));

            return ReportVerdict;
        }

        /// <summary>
        /// Reads the tooltip window's resolved style and logs each number with its verdict.
        /// </summary>
        /// <param name="windowRoot">The window's bound root (the tooltip's TemplateContainer child).</param>
        /// <param name="tag">Line prefix.</param>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink, for the per-check failures.</param>
        /// <returns><c>true</c> when all three checks passed.</returns>
        /// <remarks>
        /// The tooltip's page carries no inline `font-size` and no inline image, so all three checks
        /// here are unconditional. `#tooltip` starts at `opacity: 0` (the sheet hides it until a hover
        /// adds `tooltip__shown`), which is why this method reads images and metrics rather than
        /// visibility: an invisible tooltip is the designed state and must not read as a failure.
        /// </remarks>
        public static bool ProveTooltip(VisualElement windowRoot, string tag, Action<string> log,
            Action<string> warn)
        {
            TooltipRan = true;
            TooltipPassed = 0;
            TooltipVerdict = false;

            if (windowRoot == null)
            {
                Write(warn, tag + ": no window root to measure - the stylesheet cannot be verified");
                return false;
            }

            VisualElement tooltip = windowRoot.Q("tooltip");
            if (tooltip == null)
            {
                Write(warn, tag + ": the window root has no element named 'tooltip' - the template is not "
                    + "the one this proof was written for, so the stylesheet cannot be verified");
                return false;
            }

            int checks = 0;
            int passed = 0;

            // 1. The panel background image, reached through the sheet's own url(...).
            checks++;
            string background = DescribeBackground(tooltip.resolvedStyle.backgroundImage);
            bool backgroundOk = background != null;
            if (backgroundOk)
            {
                passed++;
            }

            Line(log, warn, backgroundOk, tag + ": tooltip background image = "
                + (background == null ? "null" : background)
                + " (expected the sheet's TooltipBg.png) - "
                + (backgroundOk ? "the sheet's project:// image reference survived the bundle round-trip"
                    : "NO IMAGE: the sheet's url(...) asset reference is null in the player"));

            // 2. The tint, which only `.tooltip` declares.
            checks++;
            Color tint = tooltip.resolvedStyle.unityBackgroundImageTintColor;
            bool tintOk = Mathf.Abs(tint.r - (13f / 255f)) < 0.004f
                && Mathf.Abs(tint.g - (14f / 255f)) < 0.004f
                && Mathf.Abs(tint.b - (18f / 255f)) < 0.004f;
            if (tintOk)
            {
                passed++;
            }

            Line(log, warn, tintOk, tag + ": tooltip image tint = " + Describe(tint)
                + " (expected rgb(13,14,18) from .tooltip's -unity-background-image-tint-color) - "
                + (tintOk ? "the sheet's .tooltip rule is in effect (the default tint is white)"
                    : "NO SHEET: nothing else sets a tint on this element"));

            // 3. A metric on the text label.
            checks++;
            Label text = tooltip.Q<Label>("tooltip__text");
            float fontSize = text == null ? -1f : text.resolvedStyle.fontSize;
            bool fontOk = text != null
                && Mathf.Abs(fontSize - ExpectedTooltipFontSize) < 0.01f;
            if (fontOk)
            {
                passed++;
            }

            Line(log, warn, fontOk, tag + ": tooltip__text font-size = " + fontSize.ToString("0.###")
                + " (expected " + ExpectedTooltipFontSize.ToString("0.###") + " from .tooltip__text) - "
                + (fontOk ? "the label's own rule is in effect"
                    : (text == null
                        ? "NO LABEL: 'tooltip__text' is not in this template"
                        : "NO SHEET: the label has no inline font-size, so this is Unity's default")));

            TooltipPassed = passed;
            TooltipVerdict = passed == checks;

            Write(log, tag + ": verdict=" + (TooltipVerdict ? "PASS" : "FAIL") + " (" + passed + "/" + checks
                + " checks) - " + (TooltipVerdict
                    ? "CommNextStyles.uss is in effect on the player's tooltip window"
                    : "the tooltip is unstyled or partly styled; the log lines above name which check "
                        + "failed"));

            return TooltipVerdict;
        }

        /// <summary>Formats a colour compactly enough to read in a log line.</summary>
        /// <param name="color">The colour.</param>
        /// <returns>`r,g,b,a` with three decimals.</returns>
        /// <summary>
        /// Resolves a background image whichever field carries it, and names that field.
        /// </summary>
        /// <param name="background">The resolved background.</param>
        /// <returns>The asset name and the field it came from; <c>null</c> when the background is
        /// empty.</returns>
        /// <remarks>
        /// Measured against <c>UnityEngine.UIElementsModule.dll</c>: <c>Background</c> carries four
        /// mutually-exclusive image fields - <c>sprite</c> (mlist 6665), <c>texture</c> (6663),
        /// <c>renderTexture</c> (6667) and <c>vectorImage</c> (6669) - plus its own
        /// <c>IsEmpty()</c> (6679). A <c>url()</c> to a Sprite-imported PNG lands in
        /// <c>sprite</c>, so a <c>.texture</c>-only read is a false negative (F62). Naming the
        /// field it came from turns the launch log into a measurement of which route shipped.
        /// </remarks>
        private static string DescribeBackground(Background background)
        {
            if (background.IsEmpty())
            {
                return null;
            }

            if (background.sprite != null)
            {
                return "'" + background.sprite.name + "' (via sprite)";
            }

            if (background.texture != null)
            {
                return "'" + background.texture.name + "' (via texture)";
            }

            if (background.vectorImage != null)
            {
                return "'" + background.vectorImage.name + "' (via vectorImage)";
            }

            if (background.renderTexture != null)
            {
                return "'" + background.renderTexture.name + "' (via renderTexture)";
            }

            return "(a background that is not empty, in a field this proof cannot name)";
        }

        private static string Describe(Color color)
        {
            return color.r.ToString("0.###") + "," + color.g.ToString("0.###") + ","
                + color.b.ToString("0.###") + "," + color.a.ToString("0.###");
        }

        /// <summary>Logs a passing check at Info and a failing one as a warning.</summary>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink.</param>
        /// <param name="ok">Whether the check passed.</param>
        /// <param name="message">The line.</param>
        private static void Line(Action<string> log, Action<string> warn, bool ok, string message)
        {
            Write(ok ? log : warn, message);
        }

        /// <summary>Writes through a callback that is allowed to be absent.</summary>
        /// <param name="write">The callback, or <c>null</c>.</param>
        /// <param name="message">The line.</param>
        private static void Write(Action<string> write, string message)
        {
            if (write != null)
            {
                write(message);
            }
        }
    }
}
