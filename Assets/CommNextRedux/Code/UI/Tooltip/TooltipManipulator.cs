// CommNextRedux - the hover-to-tooltip manipulator, ported from the legacy's
// mods-outdated/CommNext/src/CommNext/UI/Tooltip/TooltipManipulator.cs (48 lines), MIT.
//
// WHAT IT DOES
//   Registers the two mouse transitions on the element it is attached to and routes them to the
//   tooltip window. `TooltipText` can be reassigned while the tooltip is on screen, and the
//   visible text follows - which is what the toolbar needs: its two mode buttons change their
//   tooltip on every mode change, and a change made while the pointer happens to rest on the
//   button must not leave a stale string on screen.
//
// WHAT CHANGED IN THE PORT
//   * The route is `CommNextUIManager.Tooltip`, not the legacy's `MainUIManager.Instance.`
//     `TooltipWindow`: this port has no singleton UI manager and, more to the point, the overlay
//     can legitimately be missing (its template failed to load). The legacy would throw a
//     NullReferenceException from inside UI Toolkit's own event dispatch in that case; this one
//     says so once, at Warning, and then stays quiet - the button still works, it simply cannot
//     be explained on hover, and that is a state a bug report needs to be able to see.
//   * `MouseEnterEvent`/`MouseLeaveEvent` are the legacy's pair and are kept. They are what a
//     pointer-capture-free hover needs on this generation, and the buttons this manipulator is
//     attached to are plain `ui:Button`s whose own hover USS only changes a tint.
//
// ONE WARNING PER MANIPULATOR, NOT PER HOVER
//   A missing overlay is a static condition: it is known at bind time and cannot fix itself. So
//   the failure is reported once per manipulator and the flag is never reset - a per-hover warning
//   in a map the player lives in would be thousands of lines, and it would also be the loudest
//   line in a log where the actual failure is one of the port's own `ui-tooltip:` errors, already
//   logged at Error by the overlay's own bind.

using System;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Tooltip
{
    /// <summary>
    /// Shows the mod's tooltip while the pointer rests on the target element.
    /// </summary>
    /// <remarks>
    /// A `MouseManipulator`, so it registers and unregisters with the element's own lifetime - the
    /// two transitions are the only callbacks it owns, and no state outlives the element.
    /// </remarks>
    public class TooltipManipulator : MouseManipulator
    {
        private string _tooltipText;
        private bool _isTooltipVisible;
        private bool _missingOverlayReported;

        /// <summary>
        /// The text to show on hover.
        /// </summary>
        /// <remarks>
        /// Setting this while the tooltip is on screen re-renders it immediately, so a tooltip that
        /// describes live state (a mode) is never one interaction behind.
        /// </remarks>
        public string TooltipText
        {
            get { return _tooltipText; }
            set
            {
                _tooltipText = value;

                if (_isTooltipVisible)
                {
                    Show();
                }
            }
        }

        /// <summary>Builds a manipulator that shows <paramref name="tooltipText"/> on hover.</summary>
        /// <param name="tooltipText">The initial text. May be replaced later.</param>
        public TooltipManipulator(string tooltipText)
        {
            _tooltipText = tooltipText;
        }

        /// <summary>Registers the two hover transitions on the target.</summary>
        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseEnterEvent>(OnMouseIn);
            target.RegisterCallback<MouseLeaveEvent>(OnMouseOut);
        }

        /// <summary>Removes them again.</summary>
        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseEnterEvent>(OnMouseIn);
            target.UnregisterCallback<MouseLeaveEvent>(OnMouseOut);
        }

        /// <summary>The pointer entered the element: show the tooltip.</summary>
        /// <param name="evt">The event. Unused - the arrival is the payload.</param>
        private void OnMouseIn(MouseEnterEvent evt)
        {
            _isTooltipVisible = true;
            Show();
        }

        /// <summary>The pointer left the element: hide the tooltip.</summary>
        /// <param name="evt">The event. Unused.</param>
        private void OnMouseOut(MouseLeaveEvent evt)
        {
            Hide();
            _isTooltipVisible = false;
        }

        /// <summary>Routes a show to the overlay, reporting a missing overlay once.</summary>
        private void Show()
        {
            TooltipWindowController overlay = CommNextUIManager.Tooltip;
            if (overlay == null || !overlay.IsBound)
            {
                if (!_missingOverlayReported)
                {
                    _missingOverlayReported = true;
                    WriteWarning("ui-tooltip: '" + target.name + "' was hovered but there is no bound "
                        + "tooltip overlay to draw with - hover text is unavailable for the rest of "
                        + "this session (the overlay's own bind line above says why, if it ran)");
                }

                return;
            }

            overlay.ToggleTooltip(true, target, _tooltipText);
        }

        /// <summary>Routes a hide to the overlay, which is always safe even if unbound.</summary>
        private void Hide()
        {
            TooltipWindowController overlay = CommNextUIManager.Tooltip;
            if (overlay != null)
            {
                overlay.Hide();
            }
        }

        /// <summary>
        /// Reports a missing overlay through the plugin's warning sink.
        /// </summary>
        /// <param name="message">The line.</param>
        /// <remarks>
        /// The manipulator holds no sinks of its own: it is constructed by the controller that owns
        /// the button (which has them) and by P8b's list rows later, and threading a callback through
        /// every constructor would put this class's plumbing into every call site. The plugin's own
        /// null-guarded accessor is the same route every other type in the port uses.
        /// </remarks>
        private static void WriteWarning(string message)
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin == null)
            {
                return;
            }

            plugin.LogWarningLine(message);
        }
    }
}
