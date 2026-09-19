// CommNextRedux - the one-line extension that makes a tooltip declarative, ported from the legacy's
// mods-outdated/CommNext/src/CommNext/UI/Tooltip/TooltipExtensions.cs (13 lines), MIT.
//
// WHY IT IS WORTH A FILE
//   `element.AddTooltip(LocalizedStrings.SomeKey)` says what an element does in one line and reads
//   at the call site as markup rather than as machinery. The legacy used it three times in the
//   vessel report (`_powerIcon`, `_filterDropdown`, `_sortDropdown`); this port adds it to the
//   toolbar's report button and its two mode buttons. P8b's list rows and dropdowns use it too -
//   `BandRowController` and `NetworkConnectionViewController` each attach a tooltip in the legacy -
//   which is why it lands here rather than as a private helper on the toolbar.
//
// NO RE-EXPORT TRAP
//   `TooltipManipulator` is public, so a caller that needs to change its text later constructs one
//   directly (as the toolbar does for its two mode buttons). This extension is for the case where
//   the text is fixed for the element's whole life.

using UnityEngine.UIElements;

namespace CommNextRedux.UI.Tooltip
{
    /// <summary>
    /// Adds the mod's hover tooltip to an element.
    /// </summary>
    public static class TooltipExtensions
    {
        /// <summary>
        /// Attaches a tooltip that shows <paramref name="tooltipText"/> while the pointer is on the
        /// element.
        /// </summary>
        /// <param name="target">The element to annotate.</param>
        /// <param name="tooltipText">The text to show.</param>
        public static void AddTooltip(this VisualElement target, string tooltipText)
        {
            target.AddManipulator(new TooltipManipulator(tooltipText));
        }
    }
}
