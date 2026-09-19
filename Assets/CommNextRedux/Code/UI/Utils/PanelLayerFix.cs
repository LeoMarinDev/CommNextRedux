// CommNextRedux - put each window's panel GameObject on Unity's built-in "UI" layer, so the game's
// own "is the pointer over UI?" test sees this mod's windows.
//
// THE DEFECT, AND WHY IT IS NOT A MISSING `BlockGameInput`  (F82)
//   The player reported that a click on the vessel report (or the toolbar) also reaches the map: with
//   a trajectory drawn underneath the window, the click creates a maneuver node. The map's click gate
//   is ONE boolean, `KSP.Map.Map3DManeuvers.IsOtherUIHovered()` (`ikdasm` on Assembly-CSharp.dll):
//
//     _uiRaycastHits = KSP.UI.UIRaycaster.GetCurrentRaycastResults();
//     if (_uiRaycastHits.Count == 0) return false;
//     foreach (RaycastResult hit in _uiRaycastHits)
//         if (hit.gameObject.layer == 5 && !hit.gameObject.CompareTag("OrbitalUIElement"))
//             return true;
//     return false;
//
//   `KSP.UI.UIRaycaster.GetCurrentRaycastResults` forwarders to `EventSystem.RaycastAll` - the UGUI
//   event system, not UI Toolkit's own dispatcher. It has exactly three callers, all in
//   `Map3DManeuvers`: `UpdateManeuverDetection` (the maneuver-point detect/scrub path - the player's
//   "create maneuver"), `TryHandleScroll` and `ShouldConsumeScroll`. So ONE
//   "is a layer-5 GameObject under the UGUI cursor" is the game's whole contract for "do not act on
//   this click", and this mod's windows were not satisfying it.
//
//   `WindowOptions.BlockGameInput` cannot satisfy it. That option only adds the library's
//   `GameInputBlockManipulator`, whose whole effect is `IInputManager.SetUitkInputLocks(...)`, i.e.
//   KSP2 *input definitions* (`FlightInputDisabled`, `MapViewInputDisabled`, ...). The maneuver path
//   does not read them: `ManeuverInputListener()` polls the legacy `UnityEngine.Input`
//   (`Input.GetMouseButtonDown(0)`) and calls `TryGetItemUnderCursor<MapManeuverInputTarget>()`. The
//   vessel report carried `BlockGameInput = true` when P9.3 measured this and still leaked the click,
//   and the sibling port's own evidence file records that its `BlockGameInput` was never observed
//   working in game. P9.4 has since turned the report's off as well (D62, F84 - see its option), so no
//   window sets it today and the layer is the only one of the two mechanisms the map's gate reads.
//
// HOW A UI TOOLKIT PANEL REACHES THAT RAYCAST AT ALL
//   By its `PanelRaycaster`. Measured in the shipped assemblies:
//
//     UnityEngine.UI.dll    `UnityEngine.EventSystems.EventSystem`'s ctor constructs an internal
//                           `UnityEngine.UIElements.UIToolkitInteroperabilityBridge`, which
//                           subscribes to `UIElementsRuntimeUtility.onCreatePanel`.
//     UnityEngine.UI.dll    `UIToolkitInteroperabilityBridge.CreatePanelGameObject(panel)` builds
//                           `new GameObject(panel.name, typeof(PanelEventHandler),
//                            typeof(PanelRaycaster))`, parents it under the EventSystem and calls
//                           `panel.set_selectableGameObject(<that GameObject>)`.
//     UnityEngine.UI.dll    `PanelRaycaster.Raycast(...)` writes
//                           `RaycastResult.gameObject = m_Panel.selectableGameObject`.
//
//   So the `gameObject` in every hit this mod's windows produce is exactly
//   `PanelSettings.panel.selectableGameObject`, and its layer is the number `IsOtherUIHovered()`
//   compares against 5. A GameObject built by `new GameObject(...)` defaults to layer 0, and nothing
//   in UGUI ever writes its layer.
//
// THE ROUTE, AND WHY IT IS REFLECTION
//   `UitkForKsp2.Panel.PanelFactory` already tries this exact fix - `Apply(PanelSettings)` ends in
//   `selectableGameObject.layer = 5` - but it is scheduled once, through a delegate, and it returns
//   SILENTLY at three points (`panelSettings == null`, `panel == null`, `gameObject == null`). It
//   also cannot be observed: it logs nothing on any path. That is the defect this file exists for:
//   the write is re-driven across frames, and every outcome is logged.
//
//   The chain itself is reached the same way the library reaches it, because the members are not
//   nameable from here - measured with `ikdasm` on the installed UnityEngine.UIElementsModule.dll:
//
//     .class public ... UnityEngine.UIElements.PanelSettings          <- public, nameable
//     .method assembly hidebysig specialname instance class BaseRuntimePanel
//             get_panel()                                             <- INTERNAL, so `panel` needs
//                                                                        reflection
//     .class private abstract ... UnityEngine.UIElements.BaseRuntimePanel
//                                                                     <- INTERNAL type, so its
//                                                                        members are not statically
//                                                                        reachable either
//     .method public ... instance [UnityEngine.CoreModule]UnityEngine.GameObject
//             get_selectableGameObject()                              <- public ON AN INTERNAL TYPE
//
//   `PanelRenderer.panelSettings` IS public (`.method public ... get_panelSettings()`, mlist 6142 of
//   `UnityEngine.UIElementsModule.dll`), so the public surface is entered there and only the last two
//   hops are reflected. The flags are the library's
//   own: `Instance | Public | NonPublic` (its cctor's `ldc.i4.s 52`).
//
// THE HYPOTHESIS IS FALSIFIABLE, AND THE LOG SAYS WHICH WAY IT WENT
//   If the layer was ALREADY 5 on a window, the layer is not the cause of the leak - the panel's
//   `PanelRaycaster` may not be participating in the EventSystem at all - and that is reported as
//   `already 5`, distinctly from the `0 -> 5` write. A give-up is reported by name with the reason
//   and the attempt count. Nothing on this path can fail silently.

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// Puts a window's panel GameObject on Unity's built-in UI layer (5), retrying across frames, and
    /// reports what it found and what it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Static, like the rest of this port's runtime services</b>: one process, one event system, one
    /// set of panels. The only state is the small list of windows still waiting for their panel to
    /// exist, which is drained by <see cref="Tick"/> and never grows again.
    /// </para>
    /// <para>
    /// <b>The retry is the point, not a convenience.</b> The panel behind a <c>PanelSettings</c> is
    /// created on demand (<c>PanelSettings/RuntimePanelAccess.get_panel()</c> builds it when it is
    /// still null), and the GameObject this file writes to is created by the UGUI interoperability
    /// bridge from a panel-created callback - so "is it there yet" is an ordering question with no
    /// static answer. A single attempt at window creation can legitimately find nothing, which is
    /// exactly how the library's own one-shot version can no-op without a trace.
    /// </para>
    /// </remarks>
    internal static class PanelLayerFix
    {
        /// <summary>Unity's built-in <c>UI</c> layer - the number the game's gate compares against.</summary>
        /// <remarks>
        /// Not a project setting this port chose: `IsOtherUIHovered()` compares `hit.gameObject.layer`
        /// to the literal `5` (`ikdasm` on `Assembly-CSharp.dll`, `Map3DManeuvers::IsOtherUIHovered`
        /// IL_0032 `ldc.i4.5`), and every one of the game's own blocking elements is on that layer.
        /// </remarks>
        private const int UiLayer = 5;

        /// <summary>How long a window is retried before the failure is reported by name.</summary>
        /// <remarks>
        /// Five seconds of wall clock at one attempt per frame. Long enough for a panel that is created
        /// a frame or two after `Window.Create` returns, short enough that the log line is still
        /// attributable to the launch rather than to some later event.
        /// </remarks>
        private const float GiveUpAfterSeconds = 5f;

        /// <summary>The windows whose panel has not been resolved yet, in creation order.</summary>
        private static readonly List<Pending> Waiting = new List<Pending>();

        /// <summary><c>PanelSettings.panel</c>, resolved once - an internal property, so by reflection.</summary>
        private static PropertyInfo _panelProperty;

        /// <summary><c>BaseRuntimePanel.selectableGameObject</c>, resolved once, against the panel type.</summary>
        private static PropertyInfo _selectableGameObjectProperty;

        /// <summary>Whether the two reflection members have been resolved (or proven unresolvable).</summary>
        private static bool _propertiesResolved;

        /// <summary>Why the reflection chain could not be resolved, or <c>null</c> when it could.</summary>
        /// <remarks>
        /// A non-null value is permanent for the process - a missing property cannot appear later - so
        /// it skips the retry budget and is reported on the first attempt instead of five seconds
        /// later.
        /// </remarks>
        private static string _resolveFailure;

        /// <summary>
        /// Registers one window and makes the first attempt immediately, so the common case writes the
        /// layer in the frame the window is created.
        /// </summary>
        /// <param name="renderer">The window's <c>PanelRenderer</c>, as <c>Window.Create</c> returned it.</param>
        /// <param name="what">The window's name for the log: <c>toolbar</c>, <c>vessel report</c>, <c>tooltip</c>.</param>
        /// <param name="log">The Info sink, or <c>null</c>.</param>
        /// <remarks>
        /// Idempotent per renderer: a second call for the same window does nothing and writes no line.
        /// </remarks>
        public static void Apply(PanelRenderer renderer, string what, Action<string> log)
        {
            if (renderer == null)
            {
                Write(log, "ui: map-input - " + what + " could NOT be put on the UI layer; the map may "
                    + "still react under this window (Window.Create returned no PanelRenderer, so there "
                    + "is no panel to reach)");
                return;
            }

            for (int i = 0; i < Waiting.Count; i++)
            {
                if (Waiting[i].Renderer == renderer)
                {
                    return;
                }
            }

            Pending pending = new Pending
            {
                Renderer = renderer,
                What = what,
                Log = log,
                StartedAt = Time.realtimeSinceStartup,
                Reason = "not resolved yet"
            };

            Waiting.Add(pending);

            if (!TryApplyOne(pending))
            {
                Write(log, "ui: map-input - " + what + " panel not resolvable yet (" + pending.Reason
                    + "); retrying once a frame for up to " + GiveUpAfterSeconds.ToString("0.#")
                    + "s");
            }
        }

        /// <summary>
        /// Retries every window that is still waiting, and gives up on each one loudly when its budget
        /// runs out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called once a frame from the plugin's <c>Update</c>, beside the window layer's other
        /// self-healing poll. The list is empty on every frame after the windows have been fixed, so
        /// the steady-state cost is one <c>Count</c> comparison.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> One caller is a per-frame Unity message: an exception thrown out of it
        /// would abort the rest of that frame's work, and no layer write is worth the renderer's tick.
        /// </para>
        /// </remarks>
        public static void Tick()
        {
            if (Waiting.Count == 0)
            {
                return;
            }

            float elapsedLimit = GiveUpAfterSeconds;
            float now = Time.realtimeSinceStartup;

            // Backwards, because entries are removed as they resolve.
            for (int i = Waiting.Count - 1; i >= 0; i--)
            {
                Pending pending = Waiting[i];
                if (TryApplyOne(pending))
                {
                    Waiting.RemoveAt(i);
                    continue;
                }

                if (now - pending.StartedAt >= elapsedLimit)
                {
                    GiveUp(pending);
                    Waiting.RemoveAt(i);
                }
            }
        }

        /// <summary>One window's attempt: read the chain, and either write the layer or say why not.</summary>
        /// <param name="pending">The window being fixed.</param>
        /// <returns><c>true</c> when this window is finished with - fixed, already correct, or given up on.</returns>
        /// <remarks>
        /// The success line reports the layer read straight back off the GameObject, so a write that did
        /// not take is visible rather than asserted away. The <c>already 5</c> branch is deliberately a
        /// different sentence: it means the layer was not the cause of the click-through, and a reader
        /// must be able to tell that from the log without knowing this file.
        /// </remarks>
        private static bool TryApplyOne(Pending pending)
        {
            pending.Attempts++;

            EnsureProperties();
            if (_resolveFailure != null)
            {
                GiveUp(pending, _resolveFailure);
                return true;
            }

            try
            {
                PanelSettings settings = pending.Renderer.panelSettings;
                if (settings == null)
                {
                    pending.Reason = "the PanelRenderer has no PanelSettings";
                    return false;
                }

                object panel = _panelProperty.GetValue(settings, null);
                if (panel == null)
                {
                    pending.Reason = "PanelSettings.panel is null - the runtime panel has not been "
                        + "created yet";
                    return false;
                }

                GameObject selectable = _selectableGameObjectProperty.GetValue(panel, null) as GameObject;
                if (selectable == null)
                {
                    pending.Reason = "the panel has no selectableGameObject - no PanelRaycaster "
                        + "GameObject has been registered for it, so there is no layer to write";
                    return false;
                }

                string selectableName = Name(selectable, "(unnamed GameObject)");
                string panelName = Name(settings, "(unnamed panel)");
                int before = selectable.layer;

                if (before == UiLayer)
                {
                    Write(pending.Log, "ui: map-input - " + pending.What + " panel layer already "
                        + UiLayer + " - nothing was written (selectableGameObject='" + selectableName
                        + "' panel=" + panelName + ")");
                    return true;
                }

                selectable.layer = UiLayer;

                Write(pending.Log, "ui: map-input - " + pending.What + " panel layer " + before + " -> "
                    + selectable.layer + " (selectableGameObject='" + selectableName + "' panel="
                    + panelName + ")");
                return true;
            }
            catch (Exception exception)
            {
                pending.Reason = "reading the panel chain threw (" + exception.GetType().Name + ": "
                    + exception.Message + ")";
                return false;
            }
        }

        /// <summary>Reports a window whose panel never became reachable, naming the last reason.</summary>
        /// <param name="pending">The window that ran out of attempts.</param>
        /// <remarks>
        /// The shape is the one a post-launch grep is written against: everything after
        /// <c>could NOT be put on the UI layer</c> is the reason, and the elapsed time and attempt count
        /// are what distinguish "the panel never existed" from "this file never ran".
        /// </remarks>
        private static void GiveUp(Pending pending)
        {
            GiveUp(pending, pending.Reason);
        }

        /// <summary>Reports a window that cannot be fixed at all, for a reason known up front.</summary>
        /// <param name="pending">The window.</param>
        /// <param name="reason">Why it cannot be fixed.</param>
        private static void GiveUp(Pending pending, string reason)
        {
            Write(pending.Log, "ui: map-input - " + pending.What + " could NOT be put on the UI layer; "
                + "the map may still react under this window (" + reason + "; "
                + (Time.realtimeSinceStartup - pending.StartedAt).ToString("0.0") + "s, "
                + pending.Attempts + " attempt(s))");
        }

        /// <summary>Resolves the two reflection members once, and records why when it cannot.</summary>
        /// <remarks>
        /// <para>
        /// The library resolves the same pair in a static constructor with no null checks, so a runtime
        /// where either is missing would poison its whole type with a <c>TypeInitializationException</c>.
        /// Here a miss is an ordinary value: the failure string is carried to the log line, and nothing
        /// throws.
        /// </para>
        /// <para>
        /// The flags are the library's own three - instance, public and non-public - because
        /// <c>panel</c> is internal and <c>selectableGameObject</c> is public on an internal type.
        /// </para>
        /// </remarks>
        private static void EnsureProperties()
        {
            if (_propertiesResolved)
            {
                return;
            }

            _propertiesResolved = true;

            try
            {
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type settingsType = typeof(PanelSettings);

                _panelProperty = settingsType.GetProperty("panel", flags);
                if (_panelProperty == null)
                {
                    _resolveFailure = "this runtime's " + settingsType.FullName + " has no 'panel' "
                        + "property, so the runtime panel behind this window cannot be reached";
                    return;
                }

                Type panelType = _panelProperty.PropertyType;
                _selectableGameObjectProperty = panelType.GetProperty("selectableGameObject", flags);
                if (_selectableGameObjectProperty == null)
                {
                    _resolveFailure = "this runtime's " + panelType.FullName + " has no "
                        + "'selectableGameObject' property, so there is no GameObject whose layer the "
                        + "game's UI-hover test would read";
                }
            }
            catch (Exception exception)
            {
                _resolveFailure = "resolving the panel's reflection chain threw ("
                    + exception.GetType().Name + ": " + exception.Message + ")";
            }
        }

        /// <summary>A UnityEngine.Object's name, with a stand-in for the null-name case.</summary>
        /// <param name="target">The object.</param>
        /// <param name="fallback">What to print when it has no name.</param>
        /// <returns>The name, or the fallback.</returns>
        /// <remarks>
        /// The same shape `CommNextUIManager` uses for its own panel names, and for the same reason: a
        /// name is also a check that the object is one this mod created, and a null name is legal on a
        /// UnityEngine.Object - printed, never thrown on.
        /// </remarks>
        private static string Name(UnityEngine.Object target, string fallback)
        {
            string name = target.name;
            return string.IsNullOrEmpty(name) ? fallback : name;
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

        /// <summary>One window waiting for its panel, with the attempts and the last reason.</summary>
        private sealed class Pending
        {
            /// <summary>The window's panel renderer.</summary>
            internal PanelRenderer Renderer;

            /// <summary>The window's name for the log.</summary>
            internal string What;

            /// <summary>The Info sink captured at registration.</summary>
            /// <remarks>
            /// Held rather than reached for through the plugin, so a tick cannot log through a sink that
            /// has been reassigned - the same reason `CommNextUIManager` holds its own z-order sink.
            /// </remarks>
            internal Action<string> Log;

            /// <summary>When the window was registered, for the give-up budget.</summary>
            internal float StartedAt;

            /// <summary>How many attempts have been made, printed on the give-up path.</summary>
            internal int Attempts;

            /// <summary>Why the last attempt did not resolve, printed on the give-up path.</summary>
            internal string Reason;
        }
    }
}
