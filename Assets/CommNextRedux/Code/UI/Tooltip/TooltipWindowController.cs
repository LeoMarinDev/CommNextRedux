// CommNextRedux - the tooltip window: one full-screen, click-through overlay that the toolbar's
// buttons borrow to draw their tooltips in.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Tooltip/TooltipWindowController.cs (64 lines), MIT. The
//   window options, the two element names and the placement arithmetic are the legacy's. Three
//   things differ:
//
//     1. `_root = _window.rootVisualElement[0]` - blind indexing - is replaced by the guarded shape
//        (`mods/remote/K2D2Redux/Assets/K2D2/Code/UI/K2D2Window.cs`, re-routed to this pin): the
//        `PanelRenderer` this component lives on is the one `Window.Create` returned, its window root
//        is fetched with `UitkForKsp2.API.Extensions.GetWindowRoot` (which already resolved the
//        clone's template container, so there is no `[0]` any more), and a null root and
//        `root.childCount == 0` are both refused with a LOGGED ERROR, so an empty clone reads as
//        "the bundle is wrong" rather than as "the tooltip never appears".
//
//     2. Visibility is the SHEET's class, not an inline opacity write. The legacy did
//        `_tooltip.style.opacity = 1` while its own stylesheet declares
//        `.tooltip.tooltip__shown { opacity: 1 }` - the class is the sheet's contract and it carries
//        the fade (`transition-property: all, opacity` / `transition-duration: 0s, 0.3s`). The port
//        adds and removes the class, so the transition the sheet declares is the one that runs (D39).
//        The inline write also made the tooltip's visibility unreachable from USS, which is a
//        debugging dead end the class route does not have.
//
//     3. A hide on map exit, which the legacy did not have (and needed). The tooltip is shown on
//        hover; if the map view is left while the cursor is over a button, the toolbar goes to
//        `display: none` under the pointer and the tooltip would otherwise stay on screen over the
//        flight view. `Hide()` is called from the toolbar's own hide branch.
//
// THE ROOT'S OWN SIZE IS THE COORDINATE SPACE
//   `#tooltip-root` spans the whole screen (`position: absolute; left/top/right/bottom: 0`), so the
//   legacy's `-_root.worldBound.xMin + target.worldBound.xMin + width / 2` expresses the target's
//   centre in the overlay's own local space. That is kept verbatim, and the sheet's own
//   `translate: -50% -100%` then centres the tooltip's box above the point. Both halves are needed:
//   without the translate the tooltip's left edge sits at the target's centre.

using System;
using CommNextRedux.UI.Utils;
using UitkForKsp2.API;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Tooltip
{
    /// <summary>
    /// The overlay window that draws the toolbar's tooltips.
    /// </summary>
    /// <remarks>
    /// <b>Must be the last window created.</b> A UitkForKsp2 window's place in the panel is the
    /// order it was created in, and this one has to draw over the toolbar it annotates.
    /// <c>CommNextUIManager</c> creates it last for that reason, and the UXML makes every element
    /// `picking-mode="Ignore"` so a full-screen overlay cannot swallow a click meant for the map.
    /// </remarks>
    public class TooltipWindowController : MonoBehaviour
    {
        /// <summary>The window UitkForKsp2 is asked for.</summary>
        /// <remarks>
        /// Not movable and not screen-bound-checked: this window is positioned by the code below on
        /// every hover, and a drag handle on a click-through overlay would be a way to lose it.
        /// </remarks>
        public static WindowOptions WindowOptions = new WindowOptions
        {
            WindowId = "CommNextRedux_TooltipWindow",
            Parent = null,
            IsHidingEnabled = true,
            MoveOptions = new MoveOptions
            {
                IsMovingEnabled = false,
                CheckScreenBounds = false
            }
        };

        private PanelRenderer _renderer;
        private VisualElement _root;
        private VisualElement _tooltip;
        private Label _tooltipText;

        private Action<string> _log;
        private Action<string> _warn;
        private Action<string> _error;

        private bool _bound;
        private bool _styleProofRun;
        private bool _shown;
        private bool _firstShowLogged;

        /// <summary>Whether the overlay is bound to a live tree.</summary>
        /// <remarks>
        /// The toolbar asks this before routing a hover here: an unbound overlay must not be the
        /// reason a button's own callback throws.
        /// </remarks>
        public bool IsBound
        {
            get { return _bound && _tooltip != null; }
        }

        /// <summary>Whether a tooltip is currently on screen.</summary>
        public bool IsShown
        {
            get { return _shown; }
        }

        /// <summary>Binds the overlay: resolves the root and the two elements it draws with.</summary>
        /// <param name="renderer">The <c>PanelRenderer</c> <c>Window.Create</c> returned.</param>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink.</param>
        /// <param name="error">Error sink.</param>
        /// <remarks>
        /// Idempotent, like the toolbar's: the manager's call and the <c>OnEnable</c> fallback can
        /// both arrive, and only the first one with a live renderer binds.
        /// </remarks>
        public void Initialize(PanelRenderer renderer, Action<string> log, Action<string> warn,
            Action<string> error)
        {
            _log = log;
            _warn = warn;
            _error = error;

            if (renderer != null)
            {
                _renderer = renderer;
            }

            Bind();
        }

        /// <summary>The fallback bind path - see the toolbar's twin for why it exists.</summary>
        private void OnEnable()
        {
            if (_log == null)
            {
                return;
            }

            Bind();
        }

        /// <summary>The one-time bind, guarded so a second enable cannot double-subscribe.</summary>
        private void Bind()
        {
            if (_bound || _renderer == null)
            {
                return;
            }

            // GetWindowRoot resolves the window's content root out of the clone's template container
            // (`Extensions::GetWindowRoot` -> `Window::ResolveWindowRoot`), so this is the element the
            // 0.2.8.5 shape addressed as `rootVisualElement[0]` - the `[0]` index is NOT ported.
            VisualElement root = UitkForKsp2.API.Extensions.GetWindowRoot(_renderer);
            if (root == null)
            {
                Write(_error, "ui-tooltip: Extensions.GetWindowRoot(PanelRenderer) returned null - the "
                    + "window has not built its visual tree, so no tooltip can ever be shown");
                return;
            }

            if (root.childCount == 0)
            {
                Write(_error, "ui-tooltip: the window root has no children - the cloned "
                    + "VisualTreeAsset is EMPTY (the version-mismatch symptom); rebuild the UI bundle "
                    + "with 6000.5.8f1 and the toolbar's tooltips will come back with it");
                return;
            }

            _root = root;
            _bound = true;

            CommNextUIManager.EnsureStylesAttached(_root, _log, _warn);

            _tooltip = _root.Q<VisualElement>("tooltip");
            _tooltipText = _tooltip == null ? null : _tooltip.Q<Label>("tooltip__text");

            if (_tooltip == null || _tooltipText == null)
            {
                Write(_error, "ui-tooltip: the overlay template is missing its parts (tooltip="
                    + (_tooltip == null ? "MISSING" : "ok") + ", tooltip__text="
                    + (_tooltipText == null ? "MISSING" : "ok") + ") - hovering a toolbar button will "
                    + "do nothing");
            }

            // One-shot, on the first layout pass with a real size: the window starts at opacity 0,
            // which is the sheet's designed state and not a failure - the proof reads images and
            // metrics, never visibility.
            _root.RegisterCallback<GeometryChangedEvent>(OnFirstGeometryChanged);

            Write(_log, "ui-tooltip: bound - root='" + _root.name + "' (childCount=" + root.childCount
                + "), elements tooltip=" + (_tooltip == null ? "MISSING" : "ok") + " text="
                + (_tooltipText == null ? "MISSING" : "ok") + ", styles="
                + CommNextUIManager.SheetRoute);
        }

        /// <summary>Runs the stylesheet proof once, on the first real layout pass.</summary>
        /// <param name="evt">The geometry change.</param>
        private void OnFirstGeometryChanged(GeometryChangedEvent evt)
        {
            if (_styleProofRun)
            {
                return;
            }

            if (evt.newRect.width <= 0f || evt.newRect.height <= 0f)
            {
                return;
            }

            _styleProofRun = true;
            _root.UnregisterCallback<GeometryChangedEvent>(OnFirstGeometryChanged);

            bool proved = UIStyleSheetProof.ProveTooltip(_root, "ui-styles-tooltip", _log, _warn);
            Write(proved ? _log : _warn, CommNextUIManager.Why(proved) + " [tooltip "
                + evt.newRect.width.ToString("0.#") + "x" + evt.newRect.height.ToString("0.#") + "]");
        }

        /// <summary>
        /// Shows or hides the tooltip, and moves it to the target when showing.
        /// </summary>
        /// <param name="isVisible">Whether to show it.</param>
        /// <param name="target">The element the tooltip annotates. Only needed when showing.</param>
        /// <param name="text">The text. Only used when showing.</param>
        /// <remarks>
        /// Called by <c>TooltipManipulator</c> on the hover transitions, so it must be cheap and
        /// must never throw: it runs inside UI Toolkit's own event dispatch, where an exception
        /// leaves the tooltip mid-transition.
        /// </remarks>
        public void ToggleTooltip(bool isVisible, VisualElement target, string text = "")
        {
            if (!IsBound)
            {
                return;
            }

            if (!isVisible)
            {
                Hide();
                return;
            }

            if (target == null)
            {
                Write(_warn, "ui-tooltip: a hover was reported with no target element, so there is "
                    + "nothing to position the tooltip against");
                return;
            }

            _tooltipText.text = text;

            // The target's centre, in this overlay's own local space. `_root` spans the screen, so
            // its worldBound origin is the offset being removed - the legacy's arithmetic verbatim.
            Rect rootBounds = _root.worldBound;
            Rect targetBounds = target.worldBound;
            _tooltip.style.left = -rootBounds.xMin + targetBounds.xMin + (targetBounds.width / 2f);
            _tooltip.style.top = -rootBounds.yMin + targetBounds.yMin - 5f;

            SetShown(true);

            // ONE line for the whole session, and no per-hover tracing at all. A tooltip is a UI
            // event: it is not something a player opens the log to understand, so per-hover lines
            // would be pure noise (dev guide 63.1). What is worth a line is the FIRST one, because
            // "tooltips work" is otherwise only observable by hovering, and its presence proves the
            // manipulator -> manager -> overlay path end to end. After that, silence.
            if (!_firstShowLogged)
            {
                _firstShowLogged = true;
                Write(_log, "ui-tooltip: first tooltip of this session shown ('"
                    + OneLine(_tooltipText.text) + "') - the hover -> manipulator -> overlay path works; "
                    + "further tooltips are not logged");
            }
        }

        /// <summary>Hides the tooltip if it is on screen. Safe to call at any time.</summary>
        /// <remarks>
        /// The toolbar calls this when the map view goes away: a hover that is interrupted by the
        /// annotated element disappearing does not reliably deliver its own mouse-leave, and a
        /// tooltip left floating over the flight view is the visible symptom.
        /// </remarks>
        public void Hide()
        {
            if (!IsBound)
            {
                return;
            }

            SetShown(false);
        }

        /// <summary>Adds or removes the sheet's own shown class.</summary>
        /// <param name="shown">Whether the tooltip should be visible.</param>
        /// <remarks>
        /// The transition, the opacity values and the class name all live in `CommNextStyles.uss`;
        /// nothing here writes a style, which is what keeps the tooltip's appearance readable from
        /// one file. Silent by design: the show/hide pair fires on every hover, and its only log
        /// line is the first show of the session (see <see cref="ToggleTooltip"/>).
        /// </remarks>
        private void SetShown(bool shown)
        {
            if (_shown == shown)
            {
                return;
            }

            _shown = shown;
            if (shown)
            {
                _tooltip.AddToClassList("tooltip__shown");
            }
            else
            {
                _tooltip.RemoveFromClassList("tooltip__shown");
            }
        }

        /// <summary>Collapses any newline in a log line, so one tooltip is one line.</summary>
        /// <param name="text">The tooltip text.</param>
        /// <returns>The text with newlines escaped.</returns>
        private static string OneLine(string text)
        {
            if (text == null)
            {
                return "";
            }

            return text.Replace("\r", " ").Replace("\n", " \\n ");
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
