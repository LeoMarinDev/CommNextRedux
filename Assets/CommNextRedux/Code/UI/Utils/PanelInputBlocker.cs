// CommNextRedux - suppress the map view's MOUSE camera controls while the pointer rests on one of
// this mod's windows - and NOTHING else - through the game's own input-lock mechanism, at ACTION
// granularity.
//
// THE SPEC (the player's own words, L17 -> D65)
//   "Actually, let's keep the WASD, because there is not text to enter in these windows. Disable
//    these controls / input when cursor is above the mod's windows: Only the MAP's controls: camera
//    right-drag rotation, camera move, zoom. Keep these controls / input registered whenever: ESC,
//    quicksave/load and time-warp keys keep working."
//
//   So, while the pointer is over a window of this mod:
//
//     SUPPRESS   mouse camera rotation (right-drag), the mouse-driven camera move (middle-drag) and
//                the mouse wheel zoom
//     KEEP       WASD, ESC (the pause menu), quicksave / quickload, the time-warp keys, and every
//                other game control
//
// WHY `WindowOptions.BlockGameInput` IS NOT THE MECHANISM (F84; D62 SUPERSEDED, D65)
//   The library's option attaches `UitkForKsp2.API.Manipulator.GameInputBlockManipulator`, whose
//   `AcquireLock()` calls `Extensions.SetGameInputDisabled(this, true)` -> `ReduxInputManager.
//   SetUitkInputLocks()`, which pushes EIGHT `InputManager.SetInputLock` calls - one of them
//   `InputLocks.GlobalInputDisabled`. Measured with `ikdasm` on the installed `Assembly-CSharp.dll`,
//   that lock names `GlobalInputDefinition`, whose 80 members (the nested
//   `KSP.Input.GameInput/IGlobalActions` interface, `2298338-2298749`) include `OnTogglePauseMenu`
//   (ESC), `OnQuickSave`, `OnQuickLoad`, `OnQuickLoadHold`, `OnTimeWarpIncrease`,
//   `OnTimeWarpDecrease`, `OnTimeWarpStop`, `OnMenuGoBack`, `OnToggleUIVisibility` and
//   `OnConfirmDialogue`. Every one of those must keep working while the cursor is over a window, so
//   nothing here may disable `GlobalInputDefinition`. `Extensions.SetGameInputDisabled` is also
//   `.method assembly` - internal, so it is not a route this port could drive even if its semantics
//   were right (D63).
//
//   The manipulator also holds its lock while `_isPointerOver || _isPointerDown`, and the
//   pointer-DOWN latch is what stretched L16's dead zone out past the window as the pointer kept
//   moving with a button held. This guard is POINTER-SCOPED ONLY: engaged exactly while the pointer
//   is inside the window's rectangle and the pick under it is that window. That is the deliberate
//   divergence from the library, and it is what makes the region exactly the window.
//
// THE LEVER: THE GAME'S OWN PER-ACTION LOCK BRANCH
//   `KSP.Input.InputManager.SetInputLock(InputLockDefinition definition, bool forced = false)` is
//   public, and its body walks `definition.InputLocks` and, per entry, resolves
//   `InputLock.DefinitionID` through `InputManager.InputDefinitions` (a public
//   `Dictionary<string, InputDefinition>`), then does BOTH of these:
//
//     InputDefinition.SetEnabled(InputLock.DefinitionEnabled, forced, false)   // whole definition
//     if (InputDefinition.TryGetAction(InputLock.InputID, out ToggleableInputAction action))
//         action.SetState(InputLock.InputEnabled, forced)                        // ONE action
//
//   The shipped locks in `InputLocks` (public static fields) fill in `DefinitionID` only: `InputID`
//   stays null, `TryGetAction(null, out _)` is false (its first instruction is
//   `String.IsNullOrEmpty(id)` -> store null, return false), and the per-action branch is skipped.
//   That is why every lock the game ships is definition-wide - and that one branch is the whole
//   lever this file uses. Every member above is public, and therefore nameable from a mod assembly
//   (`ikdasm` on `Assembly-CSharp.dll`):
//
//     .class public auto ansi serializable beforefieldinit KSP.Input.InputLockDefinition
//       .class auto ansi serializable nested public beforefieldinit InputLock {
//         .field public string DefinitionID
//         .field public string InputID
//         .field public bool   DefinitionEnabled     // ctor default TRUE
//         .field public bool   InputEnabled          // ctor default TRUE
//       }
//       .field public List<InputLockDefinition/InputLock> InputLocks
//     .class public ... KSP.Input.InputManager {
//       .field public Dictionary<string, InputDefinition> InputDefinitions
//       .method public instance void SetInputLock(InputLockDefinition, bool)
//       .method public instance bool TryGetInputDefinition<(InputDefinition) T>([out] !!T&)
//     }
//     .class public abstract ... KSP.Input.ToggleableInputAction {
//       .method public instance bool        get_Enabled()            // the KSP-side state
//       .method public instance InputAction get_InputAction()        // Unity's own action
//       .method public instance void        SetState(bool enabled, bool forced = false)
//     }
//
//   `SetState` is idempotent: it returns without touching anything when
//   `InputAction.enabled == enabled && _enabled == enabled`, otherwise it calls
//   `InputAction.Enable()/Disable()` and stores `_enabled`. Re-applying the same state is therefore
//   free, which is what lets this file re-assert every poll instead of assuming the state stuck.
//
//   `InputAction.Enable()/Disable()` are PER-ACTION - measured in the installed
//   `Unity.InputSystem.dll`: they call `InputActionState.EnableSingleAction(this)` /
//   `DisableSingleAction(this)` and touch no sibling, and the input system's `InputAction.enabled` is
//   `get_phase() > InputActionPhase.Disabled`, i.e. a real "can this action deliver input right now"
//   read rather than a flag. Both facts are load-bearing here: the first is why restoring one action
//   cannot re-enable the whole map, the second is why the read-back this file logs is evidence.
//
// WHAT EACH SUPPRESSED ACTION ACTUALLY DRIVES (the one consumer is `KSP.Map.MapCameraInputHandler`)
//   A definition's action dictionary is keyed by the InputAction's OWN NAME - the constructor is
//   `_actions.Add(<InputAction>.get_name(), new InputActionBinder<T>(<InputAction>))` per action (IL,
//   `MapViewInputDefinition::.ctor`), and the subscriber does the same
//   (`MapCameraInputHandler::SetupInputMapping` -> `InputDefinition.BindAction<T>(<InputAction>.
//   get_name(), handler)`). Those names are the ones the shipped input asset carries
//   (`globalgamemanagers.assets` / `boot-ksp_scenes_all.bundle`), so both sides agree on the strings
//   below. The five this file locks, and the IL that proves what each one does:
//
//     mousePosition   `OnMousePosition(Vector2)`: `if (!_initialized) return; if (!_camMoveEnabled)
//                     return;` ... `UpdateCameraRotation(dx, dy)` - the pointer delta the right-drag
//                     rotation is made of, applied ONLY while `_camMoveEnabled` is set.
//     mouseSecondary  `OnMouseSecondary(bool value)`: `_camMoveEnabled = value` (and clears
//                     `_lastMouseData` on release) - the right button, i.e. the rotation gate.
//     mouseTertiary   `OnMouseTertiary(bool state)`: `_isDragging = state` plus the camera-mode
//                     message - the middle button, i.e. the gate for `OnCameraMoveXYPerformed`
//                     (`if (SessionManager.IS_ZOOM_MODE || !_isDragging) return;`), which is the ONLY
//                     caller of `SetCameraMoveValues`. Locking the gate kills the mouse-driven camera
//                     move without touching the action that carries its value.
//     cameraRotate    polled every frame in `MapCameraInputHandler::Update()`:
//                     `_inputMap.cameraRotate().ReadValue<Vector2>()` -> `UpdateCameraRotation(x, y)`
//                     - so it has to be locked as an action, not as a callback.
//     cameraZoom      `OnCameraZoom(Vector2)` (the wheel) and the same poll in `Update()` both reach
//                     `UpdateCameraZoom(y, false)`.
//
//   Deliberately NOT locked, with the reason each is safe to leave:
//
//     cameraMoveXY    the brief names it as the WASD camera move and requires WASD to keep working.
//                     Its only consumer is `OnCameraMoveXYPerformed`, which returns unless
//                     `_isDragging` - and `_isDragging` is set by `mouseTertiary` ALONE. Locking the
//                     gate suppresses the mouse pan while leaving this action, and therefore WASD,
//                     whatever it is ultimately bound to, untouched.
//     mousePrimary, resetCamera, altKeyModifier, ctrlKeyModifier, Focus, HideMap, changeALT,
//     changeVEL, nextMapItem, previousMapItem
//                     clicks, the camera-reset key, the precision modifiers, and the map controls the
//                     player did not ask to lose.
//
//   `MapViewInputDefinition` also declares `CAMERA_KB_PAN_PERFORMED = "CameraKBPanPerformed"` and
//   `CAMERA_KB_PAN_CANCELLED = "CameraKBPanCancelled"` (`2309624-2309625`), and `OnCameraKBPanPerformed`
//   / `OnCameraKBPanCancelled` exist - but nothing ever binds either id: there is no
//   `BindAction("CameraKBPanPerformed", ...)` anywhere in the assembly, so a keyboard pan route is
//   dead code on this build and there is no keyboard pan to suppress. Recorded because a reader
//   looking for "the WASD camera move" will find those two names and wonder.
//
// THE POINTER-REGION TEST, MIRRORED FROM THE PROVEN ROUTE
//   `GameInputBlockManipulator.IsPointerOverTarget(VisualElement target)` is the geometry the player
//   already validated in L16 - the dead zone matched the window exactly, and P9.4's own `worldBound`
//   line (`x=0, y=0, w=399.8, h=500.3`) is the rectangle to check it against. Its body is reproduced
//   here verbatim:
//
//     if (target == null || !IsTargetAvailable(target)) return false;
//     IPanel panel = target.panel; if (panel == null) return false;
//     Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, Input.mousePosition);
//     if (!target.worldBound.Contains(p)) return false;
//     VisualElement picked = panel.Pick(p);
//     if (picked == target) return HasVisibleSurface(target);
//     if (picked != null) return Extensions.IsSameElementOrAncestor(target, picked);
//     return false;
//
//   `IsTargetAvailable` and `HasVisibleSurface` are `.method private`, and
//   `Extensions.IsSameElementOrAncestor` is `.method assembly` (all three measured in the installed
//   `UitkForKsp2.dll`), so all three are re-implemented here rather than called.
//   `RuntimePanelUtils.ScreenToPanel` and `IPanel.Pick` are public and are called directly. The
//   target is the window's OWN UXML ROOT - the element the controllers hold as `_root`, which is what
//   `Window.ResolveWindowRoot` resolves to - not `document.rootVisualElement`. `Input.mousePosition`
//   is the legacy `UnityEngine.Input` reading, deliberately the same one the library uses: it is the
//   reading L16's dead zone was measured against.
//
// WHY RELEASE RESTORES A RECORDED STATE INSTEAD OF "true"
//   Acquire records each action's Unity-side state BEFORE it writes anything, and release writes that
//   value back. Restoring an unconditional "true" would re-enable an action the game had disabled for
//   its own reasons; restoring the recorded value cannot. Both directions are read back and logged -
//   `InputAction.enabled` (the state that gates input) and `ToggleableInputAction.Enabled` (the
//   definition's own view, which is the field `SetState` writes) - because a log line that claims a
//   state change without reading it back is not evidence.
//
// WHY THERE IS A PER-FRAME Tick AS WELL AS THE PER-ELEMENT POLL
//   Each window polls through its own element's `schedule.Execute(...).Every(...)`, at the library's
//   own 100 ms. That is the fast path, and it stops by itself once the element leaves the panel. It
//   cannot be the only path: a scheduled item is paused while its element is detached
//   (`VisualElementScheduledItem.CanBeActivated` is false when the element has no panel), so an
//   element removed while it is holding a lock would never run the release. `Tick()` - called once a
//   frame from the plugin's own `Update`, beside `PanelLayerFix.Tick` - releases any target whose
//   element is gone or off screen. A stranded disabled action is the worst possible outcome of this
//   file, so the teardown path does not depend on the element that may have been torn down.
//
// THE RELEASE PATHS, ALL OF THEM READ BACK
//   1. the pointer leaves the window (the element's own poll); 2. the window is hidden or closed (the
//   same poll: its availability test fails on `display: none` at the root or any ancestor, or on a
//   zero-sized rectangle); 3. the element is removed from the panel (Tick, which does not need the
//   scheduler); 4. the map view ends (`CommNextUIManager.SyncMapView(false)` -> `ReleaseAll`, which
//   also asserts that every window's five actions read enabled); 5. the plugin is destroyed (the
//   plugin's `OnDestroy` -> the same `ReleaseAll`).
//
// Logging is `Info`, in the `ui: map-input` family, and every line names the window, the pointer, the
// panel and the actions with their before/after state.
//
// F91'S PROBES (read this before reading the lines they write)
//   L18's defect: the toolbar's guard attached and never engaged - it took no lock in 34.5 s of map
//   play, while the vessel report's guard (same class, same poll, same attach call site) took and
//   balanced ten. Sampling a finished log cannot say whether the poll never ran or the pointer test
//   said "not over" every time, so each surviving candidate got a line. All four are on the ACQUIRE
//   side only; the release path - `Prior` never re-read, the acquisition record re-asserted - is the
//   regression control (F90 / D68) and is untouched.
//
//     probe (b)  once per window, written from inside the element's own scheduled callback: "this
//                window's poll ran". Deliberately written there and NOT in Poll, because Poll is also
//                called synchronously by Attach and a line written there would appear even for a
//                window whose scheduled item never fires - the exact state this line distinguishes.
//                Its control is the vessel report's own line.
//     probe (b)  from Tick - the per-frame path that runs whether or not the item fires - the item's
//                `isActive` and whether its element is still in a panel, written on the first tick and
//                on every change. This separates "the item was never created" and "the element the
//                guard holds is no longer in a panel" from "the item is paused".
//     probe (a)  once per window, when the pointer IS inside the rectangle and the pick still did not
//                land on the window (or landed on the root and HasVisibleSurface read "nothing painted
//                here"): the picked element's name and type, verbatim from `panel.Pick`.
//     probe (d)  rate-limited per window (ProbeNotOverSeconds), when the pointer is outside the
//                rectangle: the raw screen pointer, the screen size, the panel-space pointer, the
//                rectangle and the panel's own root rectangle on one line - the conversion this guard
//                uses can then be read rather than assumed. A pointer visibly resting on the window
//                while this line reports it outside the rectangle is the signature it exists to catch.
//
//   A probe never throws into the guard's decision: each is guarded and reports its own failure.
//   The full candidate table is `Deploy/obj/divergences.md` section F91.

using System;
using System.Collections.Generic;
using KSP.Game;
using KSP.Input;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// Suppresses the map view's mouse-driven camera controls while the pointer is inside one of this
    /// mod's windows, and restores every action it touched when the pointer leaves or the window goes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Static, like the rest of this port's runtime services</b>: one process, one input manager,
    /// one set of windows. The only state is one small record per attached window, and the shared
    /// one-per-process flag that keeps the resolved action inventory off every acquire line.
    /// </para>
    /// <para>
    /// <b>The interval is the library's own 100 ms.</b> Matched rather than chosen: the dead zone the
    /// player measured in L16 was produced by the library's manipulator polling at that rate, so the
    /// region this guard produces is directly comparable with it - and at frame rate the unit of work
    /// here (a handful of dictionary lookups) would be the poll's real cost rather than its rate.
    /// </para>
    /// </remarks>
    internal static class PanelInputBlocker
    {
        /// <summary>The definition whose actions are locked, as <c>AddDefinition</c> keys it.</summary>
        /// <remarks>
        /// `InputManager.AddDefinition&lt;T&gt;` stores under `typeof(T).Name` and `SetInputLock`
        /// looks up the same key, so a lock entry has to carry that exact string. Resolution itself
        /// goes through the typed <c>TryGetInputDefinition&lt;MapViewInputDefinition&gt;</c> overload,
        /// never through this string.
        /// </remarks>
        private const string DefinitionTypeName = "MapViewInputDefinition";

        /// <summary>
        /// The action ids locked while the pointer is over a window, in a fixed order.
        /// </summary>
        /// <remarks>
        /// Each string is the InputAction's own name in the shipped input asset, and therefore a live
        /// key of `MapViewInputDefinition`'s action dictionary - see the file header for both sides of
        /// that measurement. They are still RESOLVED rather than assumed: a name the live definition
        /// does not carry is reported by name and skipped, never silently locked.
        /// </remarks>
        private static readonly string[] ActionIds =
        {
            "mousePosition",
            "mouseSecondary",
            "mouseTertiary",
            "cameraRotate",
            "cameraZoom"
        };

        /// <summary>The poll interval, in milliseconds - the library's own.</summary>
        private const long PollIntervalMs = 100;

        /// <summary>
        /// Minimum separation, in seconds, between two candidate-(d) "not over" lines for one window.
        /// </summary>
        /// <remarks>
        /// That probe fires on the ordinary case (the pointer is simply somewhere else), so without a
        /// limit it would write ten lines a second per window. Ten seconds keeps a hover and its
        /// readings inside one timestamp range while a two-minute map session costs about a dozen lines
        /// per window - and it is the only probe here whose output grows with map time.
        /// </remarks>
        private const float ProbeNotOverSeconds = 10f;

        /// <summary>Every window that has asked to be guarded, in attach order.</summary>
        private static readonly List<Target> Targets = new List<Target>();

        /// <summary>Whether the resolved action inventory has already been written once.</summary>
        /// <remarks>
        /// The inventory is a property of the process, not of a window, and it is the line that proves
        /// the ids this file locks are the ids the running definition carries. Once is evidence; once
        /// per window is noise.
        /// </remarks>
        private static bool _inventoryLogged;

        /// <summary>
        /// Puts one window under the guard: its own scheduler polls the pointer, and every outcome is
        /// written to <paramref name="log"/>.
        /// </summary>
        /// <param name="root">The window's UXML root - the element the controllers hold as <c>_root</c>.</param>
        /// <param name="what">The window's name for the log: <c>toolbar</c>, <c>vessel report</c>, <c>tooltip</c>.</param>
        /// <param name="log">The Info sink, or <c>null</c>.</param>
        /// <remarks>
        /// <para>
        /// Idempotent per element: a second attach for the same root does nothing and writes no line.
        /// A null root is reported and never guarded - an element with no geometry has nothing to test
        /// the pointer against.
        /// </para>
        /// <para>
        /// The first evaluation runs immediately, the way the library's manipulator registers its own
        /// callbacks at window creation: the root is still <c>display: none</c> there and the test says
        /// "not over", so the steady state is reached without depending on a later tick.
        /// </para>
        /// </remarks>
        public static void Attach(VisualElement root, string what, Action<string> log)
        {
            if (root == null)
            {
                Write(log, "ui: map-input - " + what + " has no root element, so the map's camera "
                    + "controls are NOT suppressed over it (the window did not bind - see the error "
                    + "above)");
                return;
            }

            for (int i = 0; i < Targets.Count; i++)
            {
                if (Targets[i].Root == root)
                {
                    return;
                }
            }

            Target target = new Target
            {
                Root = root,
                What = what,
                Log = log,
                Prior = new bool[ActionIds.Length],
                Resolved = new string[ActionIds.Length],
                ReportedMissing = new bool[ActionIds.Length]
            };

            Targets.Add(target);

            try
            {
                // The element's own scheduler: it runs while the element is in a panel, and it stops
                // by itself when the element is detached - which is exactly why the teardown release is
                // not built on it (see Tick). The entry point is PollScheduled, not Poll, so that
                // "this window's poll really ran" is itself evidence (F91 probe (b)).
                target.Item = root.schedule.Execute(() => PollScheduled(target)).Every(PollIntervalMs);
            }
            catch (Exception exception)
            {
                Write(log, "ui: map-input - " + what + " could not be scheduled for pointer polling ("
                    + exception.GetType().Name + ": " + exception.Message + "); the per-frame tick is "
                    + "still watching it, so the guard degrades to frame rate for this window");
            }

            Write(log, "ui: map-input - " + what + " guarded: the map view's mouse camera controls "
                + "(rotation, pan, zoom) are suppressed while the pointer is over it, and nothing else "
                + "is (WASD, ESC, quicksave/load and the time-warp keys are untouched) - poll="
                + PollIntervalMs + "ms on the element's own scheduler, actions="
                + string.Join(", ", ActionIds));

            Poll(target);
        }

        /// <summary>
        /// The per-frame safety net: releases any window whose element has gone, and with it the one
        /// release path that does not depend on the element's own scheduler.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called once a frame from the plugin's <c>Update</c>, beside <c>PanelLayerFix.Tick</c>. The
        /// list holds at most one entry per window, so the steady-state cost is a count comparison;
        /// when something is held, the work is the release that the element can no longer run.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> One caller is a per-frame Unity message: an exception thrown out of it
        /// would abort the rest of that frame's work, and no input state is worth the renderer's tick.
        /// </para>
        /// </remarks>
        public static void Tick()
        {
            if (Targets.Count == 0)
            {
                return;
            }

            for (int i = 0; i < Targets.Count; i++)
            {
                Target target = Targets[i];

                // F91 probe (b), the control side. In its own guard, and before the holding check, so a
                // diagnostic can neither stop the release path below nor be skipped by it.
                ProbePollItemSafely(target);

                try
                {
                    if (!target.Holding)
                    {
                        continue;
                    }

                    if (!IsTargetAvailable(target.Root))
                    {
                        Release(target, "the window's element is gone or off screen");
                    }
                }
                catch (Exception exception)
                {
                    Write(target.Log, "ui: map-input - " + target.What + " guard tick failed ("
                        + exception.GetType().Name + ": " + exception.Message + ")");
                }
            }
        }

        /// <summary>
        /// Releases every window that is still holding the lock, and reports the state of every
        /// window's actions either way.
        /// </summary>
        /// <param name="reason">Why the release is happening, for the log line.</param>
        /// <remarks>
        /// Called from the map-exit path (<c>CommNextUIManager.SyncMapView(false)</c>) and from the
        /// plugin's <c>OnDestroy</c>. It reads the end state even for a window that is not holding: the
        /// point of the call is "nothing is left disabled", and a window that says so without reading it
        /// back is asserting, not proving.
        /// </remarks>
        public static void ReleaseAll(string reason)
        {
            for (int i = 0; i < Targets.Count; i++)
            {
                Target target = Targets[i];
                try
                {
                    if (target.Holding)
                    {
                        Release(target, reason);
                    }
                    else
                    {
                        // Not holding, so this call is the assertion "nothing of mine is left disabled
                        // here" - and it is made by reading the actions, not by trusting the flag.
                        InputManager manager;
                        MapViewInputDefinition definition;
                        string failure;
                        ToggleableInputAction[] actions = Resolve(target, out manager, out definition,
                            out failure);

                        Write(target.Log, "ui: map-input - " + Describe(target) + " holds no lock ("
                            + reason + "); " + (actions == null
                                ? "its actions could NOT be read back (" + failure + "), so nothing is "
                                    + "asserted about them here"
                                : ReadBackLine(target, actions) + " - nothing of this window's is left "
                                    + "disabled"));
                    }
                }
                catch (Exception exception)
                {
                    Write(target.Log, "ui: map-input - " + target.What + " release on '" + reason
                        + "' failed (" + exception.GetType().Name + ": " + exception.Message
                        + "); its actions are re-read on the next poll and released there");
                }
            }
        }

        /// <summary>One window's evaluation: acquire on entry, release on exit, re-assert while held.</summary>
        /// <param name="target">The window.</param>
        /// <remarks>
        /// Never throws back into the scheduler: an exception thrown out of a scheduled item is
        /// reported by the panel and the item keeps running, which turns one bad frame into one bad
        /// frame per 100 ms for the life of the window.
        /// </remarks>
        private static void Poll(Target target)
        {
            try
            {
                if (!IsPointerOverTarget(target))
                {
                    if (target.Holding)
                    {
                        Release(target, "the pointer left the window (or the window is no longer on "
                            + "screen)");
                    }

                    return;
                }

                if (target.Holding)
                {
                    Reassert(target);
                }
                else
                {
                    Acquire(target);
                }
            }
            catch (Exception exception)
            {
                Write(target.Log, "ui: map-input - " + target.What + " pointer poll failed ("
                    + exception.GetType().Name + ": " + exception.Message + ")");
            }
        }

        /// <summary>
        /// The scheduled entry point: the same evaluation, behind F91's probe (b) once-per-window line.
        /// </summary>
        /// <param name="target">The window whose scheduled item fired.</param>
        /// <remarks>
        /// The line is written here rather than in <see cref="Poll"/> on purpose: <c>Poll</c> is also
        /// called synchronously by <see cref="Attach"/>, so a line written there would appear even for a
        /// window whose scheduled item never fires - which is precisely the state the line exists to
        /// distinguish. A window whose line never appears has a poll that never ran, and every other
        /// probe of this guard (and the guard itself) depends on this one running. The vessel report's
        /// own line is the control that proves the probe can fire at all.
        /// </remarks>
        private static void PollScheduled(Target target)
        {
            if (!target.SchedulerReported)
            {
                target.SchedulerReported = true;
                Write(target.Log, "ui: map-input - " + target.What + " probe (b): the element's own "
                    + "scheduler is running this window's pointer poll (written once per window; if "
                    + "this line is missing for a window, that window's poll never runs and every other "
                    + "line of its guard, including its locks, is unreachable)");
            }

            Poll(target);
        }

        /// <summary>Locks the resolved actions, recording what each was before the write.</summary>
        /// <param name="target">The window whose pointer is over it.</param>
        /// <remarks>
        /// The definition-level half of the lock is carried at its CURRENT value
        /// (<c>MapViewInputDefinition.Enabled</c>), which makes that half a no-op by construction:
        /// <c>SetEnabled</c>'s first test is <c>InputSourceMap.enabled == enabled &amp;&amp; Enabled ==
        /// enabled</c>. Only the per-action half changes anything, which is the entire point of using
        /// this branch - the definition stays enabled, so nothing outside the named actions is affected.
        /// </remarks>
        private static void Acquire(Target target)
        {
            InputManager manager;
            MapViewInputDefinition definition;
            string failure;
            ToggleableInputAction[] actions = Resolve(target, out manager, out definition, out failure);
            if (actions == null)
            {
                ReportResolveFailure(target, failure);
                return;
            }

            // The record, read before anything is written: this is what release restores, verbatim.
            target.PriorDefinitionEnabled = definition.Enabled;
            for (int i = 0; i < ActionIds.Length; i++)
            {
                target.Prior[i] = actions[i] != null && ReadUnityState(actions[i]);
            }

            manager.SetInputLock(BuildLock(target, target.PriorDefinitionEnabled, false), false);
            target.Holding = true;

            Write(target.Log, "ui: map-input - " + Describe(target) + " LOCKED " + Count(actions)
                + " of " + definition.Actions.Count + " " + DefinitionTypeName + " action(s): "
                + DescribeState(actions) + " (read back: " + ReadBackLine(target, actions)
                + ") - suppressed: mouse camera rotation (right-drag), the mouse camera move "
                + "(middle-drag) and wheel zoom; left alone: WASD, ESC, quicksave/load, the time-warp "
                + "keys and every other definition");

            // The inventory, once per process, on the first successful resolve. This is the line that
            // proves the ids above are the ids this running definition carries, in its own spelling.
            if (!_inventoryLogged)
            {
                _inventoryLogged = true;
                Write(target.Log, "ui: map-input - " + DefinitionTypeName + " carries "
                    + definition.Actions.Count + " action(s): " + string.Join(", ", Keys(definition))
                    + " - this guard locks only the " + ActionIds.Length + " named on the LOCKED line "
                    + "and touches no other definition");
            }
        }

        /// <summary>Puts back exactly what acquire recorded, and reads the result back.</summary>
        /// <param name="target">The window that was holding the lock.</param>
        /// <param name="reason">Why the release is happening, for the log line.</param>
        private static void Release(Target target, string reason)
        {
            target.Holding = false;

            InputManager manager;
            MapViewInputDefinition definition;
            string failure;
            ToggleableInputAction[] actions = Resolve(target, out manager, out definition, out failure);
            if (actions == null)
            {
                ReportResolveFailure(target, failure);
                return;
            }

            // The exact inverse of acquire: the same actions, each at the state it had before the lock
            // was taken. Not "true" - that would re-enable an action the game had disabled for reasons
            // of its own.
            manager.SetInputLock(BuildLock(target, definition.Enabled, true), false);

            bool restored = IsRestored(target, actions);

            Write(target.Log, "ui: map-input - " + Describe(target) + " RELEASED (" + reason + "): "
                + DescribePrior(target) + " (read back: " + ReadBackLine(target, actions) + ") - "
                + (restored
                    ? "every action is back at the state it had before the lock was taken; nothing is "
                        + "left disabled"
                    : "at least one action does not read the way it did before the lock was taken - see "
                        + "the before/after pair above (if the map view itself has ended, the game "
                        + "disables the map's actions and that, not this guard, is why)"));
        }

        /// <summary>
        /// Re-applies the lock if anything re-enabled one of the actions while it was held.
        /// </summary>
        /// <param name="target">The window being held.</param>
        /// <remarks>
        /// `SetState` is idempotent, so this is a handful of dictionary lookups per poll in the normal
        /// case and one re-apply on the abnormal one - and the abnormal one is a LOGGED line rather
        /// than a silent repair, because "the lock stopped working and started again" is not something
        /// a reader should have to infer.
        /// </remarks>
        private static void Reassert(Target target)
        {
            InputManager manager;
            MapViewInputDefinition definition;
            string failure;
            ToggleableInputAction[] actions = Resolve(target, out manager, out definition, out failure);
            if (actions == null)
            {
                ReportResolveFailure(target, failure);
                return;
            }

            bool somethingIsBack = false;
            for (int i = 0; i < ActionIds.Length; i++)
            {
                if (actions[i] != null && ReadUnityState(actions[i]))
                {
                    somethingIsBack = true;
                    break;
                }
            }

            if (!somethingIsBack)
            {
                return;
            }

            // The recorded priors are deliberately NOT re-read here: they are the acquire-time record
            // that release restores, and overwriting them with a clobbered state would strand the
            // action at the end of the session.
            manager.SetInputLock(BuildLock(target, definition.Enabled, false), false);

            Write(target.Log, "ui: map-input - " + Describe(target) + " re-asserted the lock: an action "
                + "was re-enabled while the pointer was over the window (the game enables whole action "
                + "maps on a transition, which overrides an individually disabled action). "
                + DescribeState(actions) + " (read back: " + ReadBackLine(target, actions) + ")");
        }

        /// <summary>Builds a lock definition for the actions that resolved, and only those.</summary>
        /// <param name="target">The window, carrying the resolved ids and the recorded prior states.</param>
        /// <param name="definitionEnabled">The definition-level half - the no-op value in normal use.</param>
        /// <param name="restore">When true each entry carries the state recorded at acquire.</param>
        /// <returns>The definition to hand to <c>InputManager.SetInputLock</c>.</returns>
        /// <remarks>
        /// An id that did not resolve is left out rather than carried as a null: <c>SetInputLock</c>
        /// would skip it anyway, but the point of this file is that the log says exactly what was and
        /// was not touched.
        /// </remarks>
        private static InputLockDefinition BuildLock(Target target, bool definitionEnabled, bool restore)
        {
            InputLockDefinition definition = new InputLockDefinition();
            definition.InputLocks = new List<InputLockDefinition.InputLock>();

            for (int i = 0; i < ActionIds.Length; i++)
            {
                string id = target.Resolved[i];
                if (id == null)
                {
                    continue;
                }

                InputLockDefinition.InputLock entry = new InputLockDefinition.InputLock();
                entry.DefinitionID = DefinitionTypeName;
                entry.InputID = id;
                entry.DefinitionEnabled = definitionEnabled;
                entry.InputEnabled = restore ? target.Prior[i] : false;
                definition.InputLocks.Add(entry);
            }

            return definition;
        }

        /// <summary>Resolves the manager, the definition and the actions, with a reason on failure.</summary>
        /// <param name="target">The window, for the per-window "missing id" report and the resolved ids.</param>
        /// <param name="manager">The live input manager.</param>
        /// <param name="definition">The live map-view input definition.</param>
        /// <param name="failure">Why resolution failed, or <c>null</c> on success.</param>
        /// <returns>
        /// The resolved actions (an entry is null when its id did not resolve), or <c>null</c> when
        /// resolution failed outright.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Resolved fresh on every call, never cached: a save load can replace the game's whole input
        /// manager (<c>InputManager.Dispose</c> clears <c>InputDefinitions</c> and drops
        /// <c>_currentInputLock</c>), and a cached <c>ToggleableInputAction</c> from the old manager
        /// would then be written to while the new one's actions stayed live - a silent no-op that looks
        /// exactly like a working guard.
        /// </para>
        /// <para>
        /// The ids are matched exactly first and case-insensitively second, and the id that was
        /// actually used is what the lock carries and what the log names. That is the guard against the
        /// one failure mode a hardcoded name has: a name that does not match verbatim resolves to
        /// nothing, and a lock that names nothing does nothing at all, silently.
        /// </para>
        /// </remarks>
        private static ToggleableInputAction[] Resolve(Target target, out InputManager manager,
            out MapViewInputDefinition definition, out string failure)
        {
            manager = null;
            definition = null;
            failure = null;

            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            if (game == null)
            {
                failure = "the game instance is not up yet";
                return null;
            }

            manager = game.InputManager;
            if (manager == null)
            {
                failure = "GameInstance.InputManager is null";
                return null;
            }

            if (!manager.TryGetInputDefinition(out definition) || definition == null)
            {
                failure = "the InputManager has no " + DefinitionTypeName + " (the input definitions "
                    + "are not registered yet, or this runtime is not 0.2.8.5)";
                return null;
            }

            ToggleableInputAction[] actions = new ToggleableInputAction[ActionIds.Length];
            int resolved = 0;

            for (int i = 0; i < ActionIds.Length; i++)
            {
                ToggleableInputAction action;
                if (definition.TryGetAction(ActionIds[i], out action) && action != null)
                {
                    actions[i] = action;
                    target.Resolved[i] = ActionIds[i];
                    resolved++;
                    continue;
                }

                string actual = FindIdIgnoringCase(definition, ActionIds[i]);
                if (actual != null && definition.TryGetAction(actual, out action) && action != null)
                {
                    actions[i] = action;
                    target.Resolved[i] = actual;
                    resolved++;
                    continue;
                }

                target.Resolved[i] = null;

                if (!target.ReportedMissing[i])
                {
                    target.ReportedMissing[i] = true;
                    Write(target.Log, "ui: map-input - " + target.What + ": " + DefinitionTypeName
                        + " has no action '" + ActionIds[i] + "' (exact or case-insensitive), so it is "
                        + "NOT suppressed over this window; the definition carries "
                        + definition.Actions.Count + " action(s)");
                }
            }

            if (resolved == 0)
            {
                failure = "none of the " + ActionIds.Length + " action ids resolved against "
                    + DefinitionTypeName;
                return null;
            }

            return actions;
        }

        /// <summary>The definition's own key for an id, compared ignoring case, or <c>null</c>.</summary>
        /// <param name="definition">The live definition.</param>
        /// <param name="wanted">The id as written in <see cref="ActionIds"/>.</param>
        /// <returns>The definition's own spelling, or <c>null</c> when it has none.</returns>
        private static string FindIdIgnoringCase(MapViewInputDefinition definition, string wanted)
        {
            foreach (string key in definition.Actions.Keys)
            {
                if (string.Equals(key, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }

            return null;
        }

        /// <summary>Reads Unity's own state for one action: whether it can deliver input right now.</summary>
        /// <param name="action">The resolved action.</param>
        /// <returns><c>true</c> when the underlying <c>InputAction</c> is enabled.</returns>
        /// <remarks>
        /// This - not <c>ToggleableInputAction.Enabled</c> - is the state acquire records and release
        /// restores, because it is the flag <c>SetState</c>'s early-out compares and the flag that
        /// decides whether the action delivers input at all. `InputAction.enabled` is
        /// `get_phase() &gt; InputActionPhase.Disabled` on this build, i.e. a reading of the live input
        /// state rather than a cached field.
        /// </remarks>
        private static bool ReadUnityState(ToggleableInputAction action)
        {
            return action.InputAction != null && action.InputAction.enabled;
        }

        /// <summary>The definition's own view of one action's state.</summary>
        /// <param name="action">The resolved action.</param>
        /// <returns><c>true</c> when the definition still considers it enabled.</returns>
        private static bool ReadDefinitionState(ToggleableInputAction action)
        {
            return action.Enabled;
        }

        /// <summary>Whether every resolved action reads back at the state recorded at acquire.</summary>
        /// <param name="target">The window, carrying the record.</param>
        /// <param name="actions">The freshly resolved actions.</param>
        /// <returns><c>true</c> when nothing is left suppressed.</returns>
        private static bool IsRestored(Target target, ToggleableInputAction[] actions)
        {
            for (int i = 0; i < ActionIds.Length; i++)
            {
                if (actions[i] == null)
                {
                    continue;
                }

                if (ReadUnityState(actions[i]) != target.Prior[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Counts the resolved entries of an action array.</summary>
        /// <param name="actions">The array.</param>
        /// <returns>The count.</returns>
        private static int Count(ToggleableInputAction[] actions)
        {
            int count = 0;
            for (int i = 0; i < actions.Length; i++)
            {
                if (actions[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>The definition's action ids, in the dictionary's own order.</summary>
        /// <param name="definition">The live definition.</param>
        /// <returns>The ids.</returns>
        private static List<string> Keys(MapViewInputDefinition definition)
        {
            List<string> keys = new List<string>();
            foreach (string key in definition.Actions.Keys)
            {
                keys.Add(key);
            }

            return keys;
        }

        /// <summary>Reports a resolution failure once per window per distinct reason.</summary>
        /// <param name="target">The window.</param>
        /// <param name="failure">Why resolution failed.</param>
        /// <remarks>
        /// A failure here means the guard did nothing this poll. It is a single line rather than one
        /// per 100 ms, and it is not silent: a window whose actions cannot be resolved is a window
        /// whose map controls are NOT suppressed, which is a state the reader has to be able to tell
        /// apart from a working guard.
        /// </remarks>
        private static void ReportResolveFailure(Target target, string failure)
        {
            if (target.Reason == failure)
            {
                return;
            }

            target.Reason = failure;
            Write(target.Log, "ui: map-input - " + target.What + ": the map's camera controls are NOT "
                + "suppressed over this window (" + failure + ")");
        }

        /// <summary>The window, its rectangle and the pointer, as one readable clause.</summary>
        /// <param name="target">The window.</param>
        /// <returns>The clause, e.g. <c>vessel report (pointer=(x=199, y=250) panel='PanelSettings' root=(x=0, y=0, w=399, h=500))</c>.</returns>
        /// <remarks>
        /// The panel-space pointer is the same space as the `worldBound` line P9.4 logs, so the two can
        /// be compared directly: the pointer is inside the rectangle exactly when the guard engages.
        /// </remarks>
        private static string Describe(Target target)
        {
            return target.What + " (pointer=" + PointerText(target.Root) + " panel='"
                + PanelName(target.Root) + "' root=" + RectText(target.Root) + ")";
        }

        /// <summary>The recorded acquisition states, as <c>before -&gt; restored</c> pairs.</summary>
        /// <param name="target">The window, carrying the record.</param>
        /// <returns>The clause, one pair per resolved action.</returns>
        private static string DescribePrior(Target target)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < ActionIds.Length; i++)
            {
                if (target.Resolved[i] == null)
                {
                    continue;
                }

                parts.Add(target.Resolved[i] + " was " + OnOff(target.Prior[i]));
            }

            return string.Join(", ", parts);
        }

        /// <summary>The resolved actions with the state each was left in.</summary>
        /// <param name="actions">The resolved actions.</param>
        /// <returns>The clause, one entry per resolved action.</returns>
        private static string DescribeState(ToggleableInputAction[] actions)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < ActionIds.Length; i++)
            {
                if (actions[i] == null)
                {
                    continue;
                }

                parts.Add("[" + ActionIds[i] + " " + OnOff(ReadDefinitionState(actions[i])) + "]");
            }

            return string.Join(" ", parts);
        }

        /// <summary>Every resolved action, both state readings, as the read-back payload.</summary>
        /// <param name="target">The window that holds (or held) the lock.</param>
        /// <param name="actions">The freshly resolved actions, or <c>null</c> when resolution failed.</param>
        /// <returns>The clause, e.g. <c>mouseTertiary input=disabled definition=disabled (was enabled)</c>.</returns>
        /// <remarks>
        /// Two readings per action, and the acquisition record as the third value: "input" is Unity's
        /// live state (the one that decides whether the action delivers anything), "definition" is the
        /// KSP-side field <c>SetState</c> writes, and "was" is what acquire recorded. A lock that landed
        /// shows <c>input=disabled</c>; a release that landed shows <c>input</c> equal to <c>was</c>.
        /// </remarks>
        private static string ReadBackLine(Target target, ToggleableInputAction[] actions)
        {
            if (actions == null)
            {
                return "the actions could not be resolved, so no state is read back";
            }

            List<string> parts = new List<string>();
            for (int i = 0; i < ActionIds.Length; i++)
            {
                if (actions[i] == null)
                {
                    parts.Add(ActionIds[i] + "=unresolved");
                    continue;
                }

                string id = target.Resolved[i] ?? ActionIds[i];
                parts.Add(id + " input=" + OnOff(ReadUnityState(actions[i])) + " definition="
                    + OnOff(ReadDefinitionState(actions[i])) + " (was " + OnOff(target.Prior[i]) + ")");
            }

            return string.Join(", ", parts);
        }

        /// <summary>The pointer, in the window's own panel coordinates.</summary>
        /// <param name="element">The window's root.</param>
        /// <returns>The coordinates, or a stand-in when there is no panel.</returns>
        private static string PointerText(VisualElement element)
        {
            IPanel panel = element == null ? null : element.panel;
            if (panel == null)
            {
                return "(no panel)";
            }

            return PointText(RuntimePanelUtils.ScreenToPanel(panel, Input.mousePosition));
        }

        /// <summary>The raw legacy screen pointer - the reading the guard converts.</summary>
        /// <returns>The coordinates, in the screen's own pixels, as <c>Input.mousePosition</c> reads them.</returns>
        private static string ScreenPointerText()
        {
            Vector3 screen = Input.mousePosition;
            return "(x=" + screen.x.ToString("0.#") + ", y=" + screen.y.ToString("0.#") + ")";
        }

        /// <summary>A point in the log's coordinate format.</summary>
        /// <param name="point">The point, in whatever space the caller is printing.</param>
        /// <returns>The coordinates, one decimal place.</returns>
        private static string PointText(Vector2 point)
        {
            return "(x=" + point.x.ToString("0.#") + ", y=" + point.y.ToString("0.#") + ")";
        }

        /// <summary>The panel's own root rectangle - the panel-space extent, for the probe (d) line.</summary>
        /// <param name="element">The window's root.</param>
        /// <returns>The rectangle as text, or a stand-in when there is no panel.</returns>
        private static string PanelRootText(VisualElement element)
        {
            IPanel panel = element == null ? null : element.panel;
            VisualElement tree = panel == null ? null : panel.visualTree;
            return tree == null ? "(no panel)" : RectText(tree);
        }

        /// <summary>An element for the log: its name (or a stand-in) and its concrete type.</summary>
        /// <param name="element">The element, which may be <c>null</c>.</param>
        /// <returns>The text the F91 ledger's probe (a) asks for.</returns>
        private static string ElementText(VisualElement element)
        {
            if (element == null)
            {
                return "<null>";
            }

            string name = string.IsNullOrEmpty(element.name) ? "(unnamed)" : element.name;
            return name + "/" + element.GetType().Name;
        }

        /// <summary>The panel's name, as the library's own layer line reports it.</summary>
        /// <param name="element">The window's root.</param>
        /// <returns>The name, or a stand-in when there is no panel.</returns>
        private static string PanelName(VisualElement element)
        {
            IPanel panel = element == null ? null : element.panel;
            if (panel == null)
            {
                return "(none)";
            }

            VisualElement tree = panel.visualTree;
            string name = tree == null ? null : tree.name;
            return string.IsNullOrEmpty(name) ? "(unnamed panel)" : name;
        }

        /// <summary>The window's rectangle, in its own panel coordinates.</summary>
        /// <param name="element">The window's root.</param>
        /// <returns>The rectangle as text.</returns>
        private static string RectText(VisualElement element)
        {
            if (element == null)
            {
                return "(no element)";
            }

            Rect rect = element.worldBound;
            return "(x=" + rect.x.ToString("0.#") + ", y=" + rect.y.ToString("0.#") + ", w="
                + rect.width.ToString("0.#") + ", h=" + rect.height.ToString("0.#") + ")";
        }

        /// <summary>True/false as the log's on/off words.</summary>
        /// <param name="value">The flag.</param>
        /// <returns><c>"enabled"</c> or <c>"disabled"</c>.</returns>
        private static string OnOff(bool value)
        {
            return value ? "enabled" : "disabled";
        }

        /// <summary>
        /// The library's own pointer test, reproduced: is the pointer inside this window, on a surface
        /// this window owns?
        /// </summary>
        /// <param name="target">The window - the test is made against its root element's panel.</param>
        /// <returns><c>true</c> while the guard should be engaged.</returns>
        /// <remarks>
        /// <para>
        /// Deliberately WITHOUT the library's <c>_isPointerDown</c> latch: the library holds its lock
        /// while the pointer is over OR a button is held, which is what stretched L16's dead zone past
        /// the window. Here the region is exactly the rectangle.
        /// </para>
        /// <para>
        /// <c>RuntimePanelUtils.ScreenToPanel</c> converts the screen position into the WINDOW'S own
        /// panel space, which is the space `worldBound` and `Pick` are in - each of this mod's windows
        /// has its own <c>PanelSettings</c>, so a single screen-to-panel conversion for all three would
        /// be wrong for any window whose panel is scaled differently.
        /// </para>
        /// <para>
        /// <b>The three failure exits carry F91's probes</b> - (d) when the pointer is outside the
        /// rectangle, (a) when it is inside and the pick did not give the window - and they are the only
        /// behaviour this method gained: every return value is the one it returned before. The record
        /// rather than the root element is the parameter because a probe's rate limit and its
        /// once-per-window flags belong to the window.
        /// </para>
        /// </remarks>
        private static bool IsPointerOverTarget(Target target)
        {
            VisualElement root = target.Root;
            if (!IsTargetAvailable(root))
            {
                return false;
            }

            IPanel panel = root.panel;
            if (panel == null)
            {
                return false;
            }

            Vector2 pointer = RuntimePanelUtils.ScreenToPanel(panel, Input.mousePosition);
            if (!root.worldBound.Contains(pointer))
            {
                ProbeNotOverSafely(target, pointer);
                return false;
            }

            VisualElement picked = panel.Pick(pointer);
            if (picked == root)
            {
                if (HasVisibleSurface(root))
                {
                    return true;
                }

                ProbePickSafely(target, pointer, picked, "the pick landed on the root itself, so "
                    + "HasVisibleSurface is the only test left, and it reads 'nothing painted at the "
                    + "pointer' - the surface L18 excluded by reading the stylesheet is now excluded by "
                    + "measurement, or it is the cause");
                return false;
            }

            if (picked != null && IsSameElementOrAncestor(root, picked))
            {
                return true;
            }

            ProbePickSafely(target, pointer, picked, "the pick is neither the root nor a descendant of "
                + "it, so the pointer is inside this window's rectangle while another element owns the "
                + "pixels there");
            return false;
        }

        /// <summary>
        /// F91 probe (a): the pointer is inside the rectangle and the pick did not give the window it.
        /// </summary>
        /// <param name="target">The window.</param>
        /// <param name="pointer">The panel-space pointer that was tested.</param>
        /// <param name="picked">What <c>panel.Pick</c> returned, or <c>null</c>.</param>
        /// <param name="why">Which of the two failure branches this is, for the line.</param>
        /// <remarks>
        /// Once per window: the pick result for a stable layout and a given pointer position is a
        /// property of the tree, not of the frame, and a line per poll would be ten a second. The picked
        /// element is printed the way the F91 ledger asks for it - its name and its type - with the
        /// panel and both rectangles beside it, because "another element owns the pixels" is only
        /// actionable together with where the pixels are.
        /// </remarks>
        private static void ProbePick(Target target, Vector2 pointer, VisualElement picked, string why)
        {
            if (target.PickReported)
            {
                return;
            }

            target.PickReported = true;
            Write(target.Log, "ui: map-input - " + target.What + " probe (a): the pointer IS inside the "
                + "guard's rectangle and this window does not take it - pick="
                + (picked == null ? "<null>" : ElementText(picked))
                + ", pointer=" + PointText(pointer) + ", root=" + RectText(target.Root)
                + ", panel='" + PanelName(target.Root) + "' - " + why);
        }

        /// <summary>Probe (a), guarded so a diagnostic can never change the guard's own decision.</summary>
        /// <param name="target">The window.</param>
        /// <param name="pointer">The panel-space pointer that was tested.</param>
        /// <param name="picked">What <c>panel.Pick</c> returned, or <c>null</c>.</param>
        /// <param name="why">Which failure branch this is.</param>
        private static void ProbePickSafely(Target target, Vector2 pointer, VisualElement picked,
            string why)
        {
            try
            {
                ProbePick(target, pointer, picked, why);
            }
            catch (Exception exception)
            {
                ReportProbeFailure(target, "a", exception);
            }
        }

        /// <summary>
        /// F91 probe (d): the pointer is outside the rectangle, with both pointer readings and both
        /// rectangles on one line.
        /// </summary>
        /// <param name="target">The window.</param>
        /// <param name="pointer">The panel-space pointer the guard tested.</param>
        /// <remarks>
        /// <para>
        /// Rate-limited per window (see <see cref="ProbeNotOverSeconds"/>) because it fires on the
        /// ordinary case - the pointer is simply somewhere else. <c>screen</c> is the raw legacy
        /// <c>Input.mousePosition</c> reading, the same one the guard hands to
        /// <c>RuntimePanelUtils.ScreenToPanel</c>; <c>screenSize</c> and <c>panelRoot</c> are printed
        /// beside it so the conversion can be read off the line rather than assumed, including its
        /// origin convention.
        /// </para>
        /// <para>
        /// This is the line that separates "the rectangle and the drawing disagree" from "the pointer
        /// was never over the window": a pointer visibly resting on this window while this line reports
        /// it outside <c>root</c> is the disagreement, and the four numbers say in which direction.
        /// </para>
        /// </remarks>
        private static void ProbeNotOver(Target target, Vector2 pointer)
        {
            float now = Time.realtimeSinceStartup;
            if (target.NotOverReported && now - target.LastNotOverAt < ProbeNotOverSeconds)
            {
                return;
            }

            target.NotOverReported = true;
            target.LastNotOverAt = now;

            Write(target.Log, "ui: map-input - " + target.What + " probe (d): the pointer is NOT inside "
                + "the guard's rectangle - screen=" + ScreenPointerText() + " screenSize="
                + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height + " panel="
                + PointText(pointer) + " root=" + RectText(target.Root)
                + " panelRoot=" + PanelRootText(target.Root) + " panel='" + PanelName(target.Root)
                + "' (rate-limited to one line per " + ProbeNotOverSeconds.ToString("0") + "s per window)");
        }

        /// <summary>Probe (d), guarded so a diagnostic can never change the guard's own decision.</summary>
        /// <param name="target">The window.</param>
        /// <param name="pointer">The panel-space pointer the guard tested.</param>
        private static void ProbeNotOverSafely(Target target, Vector2 pointer)
        {
            try
            {
                ProbeNotOver(target, pointer);
            }
            catch (Exception exception)
            {
                ReportProbeFailure(target, "d", exception);
            }
        }

        /// <summary>
        /// F91 probe (b)'s control side: what the poll item and its element are, read from the per-frame
        /// tick.
        /// </summary>
        /// <param name="target">The window.</param>
        /// <remarks>
        /// <para>
        /// Written on the first tick and then only when a reading <b>changes</b>, so a steady state costs
        /// one line per window per process - and the two property reads it costs per frame are cheaper
        /// than the string it does not build.
        /// </para>
        /// <para>
        /// It is read here rather than in the poll because <c>Tick</c> runs whether or not the window's
        /// item fires: "never created" (the schedule call threw - its own line above says so),
        /// "reports inactive" (the item is paused) and <c>elementPanel=(none)</c> (the element the guard
        /// holds is not in any panel, which is what a recreated window root looks like from here) are
        /// three different defects that all present as "the guard never fired".
        /// </para>
        /// </remarks>
        private static void ProbePollItem(Target target)
        {
            bool itemActive = target.Item != null && target.Item.isActive;
            bool inPanel = target.Root != null && target.Root.panel != null;
            if (target.PollItemReported && itemActive == target.LastItemActive
                && inPanel == target.LastElementInPanel)
            {
                return;
            }

            bool first = !target.PollItemReported;
            target.PollItemReported = true;
            target.LastItemActive = itemActive;
            target.LastElementInPanel = inPanel;

            Write(target.Log, "ui: map-input - " + target.What + " probe (b): poll item "
                + (target.Item == null
                    ? "was never created"
                    : (itemActive ? "reports active" : "reports inactive"))
                + ", elementPanel=" + (inPanel ? "'" + PanelName(target.Root) + "'" : "(none)")
                + " (" + (first ? "first reading" : "state changed") + ", read from the per-frame tick) "
                + "- this window's poll can only run while the item is active and its element is in a "
                + "panel");
        }

        /// <summary>Probe (b)'s control reading, guarded.</summary>
        /// <param name="target">The window.</param>
        private static void ProbePollItemSafely(Target target)
        {
            try
            {
                ProbePollItem(target);
            }
            catch (Exception exception)
            {
                ReportProbeFailure(target, "b", exception);
            }
        }

        /// <summary>Reports a probe's own failure, so a diagnostic is never a silent one.</summary>
        /// <param name="target">The window whose probe failed.</param>
        /// <param name="probe">The probe's letter, for the line.</param>
        /// <param name="exception">What it threw.</param>
        private static void ReportProbeFailure(Target target, string probe, Exception exception)
        {
            Write(target.Log, "ui: map-input - " + target.What + " probe (" + probe + ") failed ("
                + exception.GetType().Name + ": " + exception.Message + "); the guard's own decision is "
                + "unaffected, but this window's F91 evidence is incomplete");
        }

        /// <summary>The library's availability test, reproduced.</summary>
        /// <param name="target">The window's root.</param>
        /// <returns><c>true</c> when the element has a panel, a real size and no hidden ancestor.</returns>
        /// <remarks>
        /// This is also the guard's own "the window is gone" test: the library hides a window by
        /// writing <c>display: none</c> on this very element, and a hidden element has no size, so both
        /// halves fail together.
        /// </remarks>
        private static bool IsTargetAvailable(VisualElement target)
        {
            if (target == null || target.panel == null)
            {
                return false;
            }

            Rect bounds = target.worldBound;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return false;
            }

            for (VisualElement element = target; element != null; element = element.parent)
            {
                IResolvedStyle style = element.resolvedStyle;
                if (style.display == DisplayStyle.None || style.visibility == Visibility.Hidden)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The library's own-surface test, reproduced.</summary>
        /// <param name="element">The element the pick landed on.</param>
        /// <returns><c>true</c> when that element paints something there.</returns>
        /// <remarks>
        /// Reached only when the pick IS the window root - a pick on any descendant is accepted by the
        /// ancestor test above. A root that paints nothing at the pointer is treated as "not over the
        /// window", which is the library's semantics and therefore the semantics L16's region was
        /// measured with.
        /// </remarks>
        private static bool HasVisibleSurface(VisualElement element)
        {
            IResolvedStyle style = element.resolvedStyle;

            if (style.backgroundColor.a > 0f)
            {
                return true;
            }

            if (!style.backgroundImage.IsEmpty())
            {
                return true;
            }

            if ((style.borderBottomWidth > 0f && style.borderBottomColor.a > 0f)
                || (style.borderLeftWidth > 0f && style.borderLeftColor.a > 0f)
                || (style.borderRightWidth > 0f && style.borderRightColor.a > 0f)
                || (style.borderTopWidth > 0f && style.borderTopColor.a > 0f))
            {
                return true;
            }

            return false;
        }

        /// <summary>Walks up from <paramref name="element"/> looking for <paramref name="possibleAncestor"/>.</summary>
        /// <param name="possibleAncestor">The window root.</param>
        /// <param name="element">The picked element.</param>
        /// <returns><c>true</c> when the picked element is the root or one of its descendants.</returns>
        /// <remarks>
        /// The library's own <c>Extensions.IsSameElementOrAncestor</c> is <c>.method assembly</c>
        /// (internal), so this is its body rather than its call.
        /// </remarks>
        private static bool IsSameElementOrAncestor(VisualElement possibleAncestor, VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current == possibleAncestor)
                {
                    return true;
                }
            }

            return false;
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

        /// <summary>One window's kept state.</summary>
        /// <remarks>
        /// <c>Prior</c> is written once per acquire and never afterwards; <c>Resolved</c> and
        /// <c>ReportedMissing</c> exist so a definition that lacks an id says so once per window rather
        /// than once per 100 ms.
        /// </remarks>
        private sealed class Target
        {
            /// <summary>The window's UXML root - the element the pointer test is made against.</summary>
            internal VisualElement Root;

            /// <summary>The window's name for the log.</summary>
            internal string What;

            /// <summary>The Info sink captured at attach.</summary>
            /// <remarks>
            /// Held rather than reached for through the plugin, for the same reason `PanelLayerFix`
            /// holds its own: a tick must not log through a sink that has been reassigned.
            /// </remarks>
            internal Action<string> Log;

            /// <summary>The element's own polling item, or <c>null</c> when scheduling failed.</summary>
            internal IVisualElementScheduledItem Item;

            /// <summary>Whether this window currently holds the lock.</summary>
            internal bool Holding;

            /// <summary>Each action's Unity-side state as recorded at acquire.</summary>
            internal bool[] Prior;

            /// <summary>The definition-level value carried on the acquire lock.</summary>
            internal bool PriorDefinitionEnabled;

            /// <summary>The id the definition actually carries for each wanted action, or null.</summary>
            internal string[] Resolved;

            /// <summary>Whether an unresolved id has already been reported for this window.</summary>
            internal bool[] ReportedMissing;

            /// <summary>The last resolution failure reported, so it is reported once.</summary>
            internal string Reason;

            /// <summary>Whether F91's probe (b) scheduler line has been written for this window.</summary>
            internal bool SchedulerReported;

            /// <summary>Whether F91's probe (a) pick line has been written for this window.</summary>
            internal bool PickReported;

            /// <summary>Whether F91's probe (d) "not over" line has been written at least once.</summary>
            internal bool NotOverReported;

            /// <summary>When probe (d) last wrote, on <c>Time.realtimeSinceStartup</c>'s clock.</summary>
            internal float LastNotOverAt;

            /// <summary>Whether probe (b)'s per-frame reading has been written at least once.</summary>
            internal bool PollItemReported;

            /// <summary>The poll item's last reported active state.</summary>
            internal bool LastItemActive;

            /// <summary>The element's last reported panel membership.</summary>
            internal bool LastElementInPanel;
        }
    }
}
