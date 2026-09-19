// CommNextRedux - the map-lifecycle listener.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/ConnectionsRenderer.cs - the legacy had no
//   listener of its own. It polled `MapProvider.Instance.TryGetMapCore` and read
//   `mapCore.map3D.AllMapSelectableItems` on every update, treating a null dictionary as "not in
//   the map view". That is the fallback this port keeps (see `ConnectionsRenderer`), and the
//   reason it is kept is that this listener is a *fast path*, not the source of truth.
//
//   The subscription shape is ported from the in-game-validated sibling port,
//   mods/CommLinesRedux/Assets/CommLinesRedux/Code/EventListener.cs.
//
// WHAT IT IS FOR
//   One boolean - is a map view alive right now - so the renderer can skip its work while the
//   player is in flight view, and so a map *entry* can be noticed immediately rather than up to
//   0.5 s later. The renderer's own gate is still the map dictionary: a wrong `IsInMapView` costs
//   a wasted poll or one missed poll, never a wrong line, and the probe prints both readings
//   side by side so the two disagreeing is a visible fact rather than a silent one.
//
//   Since P8a it is also the single place the game's own map transitions are turned into toolbar
//   visibility (`ApplyToolbarVisibility`). That is the legacy's arrangement - its `MessageListener`
//   set `MapToolbarWindow.IsWindowOpen` from these same two messages, and its AppBar registration
//   is commented out in the legacy source - so there is no AppBar button for the toolbar in this
//   port either. The plugin also asserts the same state once per frame, because a save load
//   replaces the MessageCenter and orphans these handlers with no log line (see below).
//
//   Since P9.2 it carries one more pair of subscriptions, the game's own pause/ESC menu
//   (`IsEscapeMenuOpen`), because the window layer's z-order raise has to stand down while that
//   menu is open: at L14 the raised panels drew OVER it. The shape is deliberately identical -
//   same two-message pair, same `PersistentSubscribe`, same centre-keyed re-arm - since the
//   failure mode being guarded against (a replaced MessageCenter going deaf) is the same one.
//
// WHY PersistentSubscribe AND NOT Subscribe
//   A plain subscription is dropped when the game tears a scene down, and entering or leaving the
//   map is exactly that sequence - the listener would go deaf at the first transition and never
//   recover. `MessageCenter.PersistentSubscribe<TMessage>(Action<MessageCenterMessage>)` is
//   resolved on this pin at method line 57410 (`monodis --method` over Assembly-CSharp.dll under a
//   per-command MONO_PATH prefix, 0 `failed to parse` lines), and it is the shape the sibling port
//   and OrbitalSurvey both use here.
//
// WHY IT RE-ARMS ON THE MESSAGE CENTER ITSELF
//   A save load REPLACES the `MessageCenter` instance, orphaning every handler registered on the
//   old one - the handlers still exist, they simply never fire again, and the symptom is "the mod
//   worked, then went dead" with nothing in the log (dev guide 61.6). `IsRegistered` alone would
//   make that permanent, because it would refuse to subscribe a second time. So the registration
//   is keyed on the instance it was made on: when `game.Messages` is a different object, the
//   subscription is re-made on the new one, and `IsInMapView` is reset because the state the old
//   center's messages established says nothing about the new session.

using System;
using CommNextRedux.UI;
using KSP.Game;
using KSP.Messages;

namespace CommNextRedux
{
    /// <summary>
    /// Tracks whether a map view is alive, from the game's own map messages.
    /// </summary>
    /// <remarks>
    /// Static, like the sibling port's: there is exactly one map, exactly one process, and no state
    /// here that a second instance could own. Every member is safe to call before a session exists.
    /// </remarks>
    public static class EventListener
    {
        /// <summary>
        /// Whether a map view is currently alive, as last reported by the game's map messages.
        /// </summary>
        /// <remarks>
        /// <b>Not the renderer's gate.</b> A stale <c>true</c> is expected whenever a session is
        /// torn down without those messages arriving (the handlers' center can be replaced under
        /// them), so the renderer asks this only as a cheap early-out and draws nothing when the map
        /// dictionary is null regardless. See the file header.
        /// </remarks>
        public static bool IsInMapView { get; private set; }

        /// <summary>
        /// Whether the game's own pause/ESC menu is open, as last reported by its two messages.
        /// </summary>
        /// <remarks>
        /// <b>The gate on the window layer's z-order raise</b> (see
        /// <c>CommNextUIManager.SetPauseSuppressed</c>): the raise wins against the game's map HUD,
        /// and without this flag it wins against the game's ESC menu too - which is the L14 defect.
        /// The messages are the game's own state transitions, published by
        /// <c>KSP.Game.UIManager.SetPauseVisible(bool)</c>; the game's one public predicate,
        /// <c>UIManager.IsEscapeVisible()</c>, answers a different question (it reads a
        /// <c>CanvasGroup.alpha</c> off the menu's GameObject), so it is not a substitute here.
        /// </remarks>
        public static bool IsEscapeMenuOpen { get; private set; }

        /// <summary>
        /// The message center the current subscription was made on, or <c>null</c> before the first
        /// one.
        /// </summary>
        /// <remarks>
        /// The re-arm trigger. Held as the object rather than as a flag because the failure it
        /// guards against is precisely "a flag that is still true about an object that is gone".
        /// </remarks>
        private static MessageCenter _registeredCenter;

        /// <summary>Whether the two map subscriptions are in place on <see cref="_registeredCenter"/>.</summary>
        public static bool IsRegistered
        {
            get { return _registeredCenter != null; }
        }

        /// <summary>
        /// Subscribes to the map messages, once per message center. Safe to call every tick.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c> before a session exists.</param>
        /// <remarks>
        /// Idempotent in the ordinary case and re-arming in the one case that matters: a new
        /// <c>MessageCenter</c> is a new registration, and the old one's state is discarded with it.
        /// </remarks>
        public static void EnsureRegistered(GameInstance game)
        {
            if (game == null)
            {
                return;
            }

            MessageCenter messages = game.Messages;
            if (messages == null)
            {
                return;
            }

            if (ReferenceEquals(messages, _registeredCenter))
            {
                return;
            }

            bool replacing = _registeredCenter != null;

            messages.PersistentSubscribe<MapInitializedMessage>(OnMapInitialized);
            messages.PersistentSubscribe<MapViewLeftMessage>(OnMapViewLeft);
            messages.PersistentSubscribe<EscapeMenuOpenedMessage>(OnEscapeMenuOpened);
            messages.PersistentSubscribe<EscapeMenuClosedMessage>(OnEscapeMenuClosed);
            _registeredCenter = messages;

            // The state the previous center's messages established belongs to the previous session.
            // A reload rebuilds the simulation object graph and every map marker with it, so a
            // carried-over `true` would be a claim about a map that no longer exists. The same is
            // true of the menu flag, and it carries one extra consequence: if a load is performed
            // from the open ESC menu, the panels would otherwise stay at the clone's order for the
            // rest of the session - so clearing the flag is paired with re-applying the raise, in
            // the same call the handlers use (there is exactly one route in and out of suppression).
            IsInMapView = false;
            IsEscapeMenuOpen = false;
            ApplyEscapeMenuSuppression("the message center was replaced");

            if (replacing)
            {
                // Info, and worded so a grep for the dead-listener trap finds it: this line is the
                // proof that the re-arm worked, and its absence across a save load is the proof that
                // it was needed.
                Write("message center replaced (save load) - map subscriptions re-armed on the new "
                    + "instance and IsInMapView reset, plus IsEscapeMenuOpen reset and the z-order "
                    + "raise re-applied");
            }
            else
            {
                Write("subscribed: MapInitializedMessage, MapViewLeftMessage (persistent, so the map "
                    + "transitions cannot deafen them) - plus EscapeMenuOpenedMessage and "
                    + "EscapeMenuClosedMessage for the pause-menu z-order stand-down");
            }
        }

        /// <summary>
        /// Forgets the map-view state without touching the subscriptions.
        /// </summary>
        /// <remarks>
        /// For a future session-shutdown hook. Nothing calls it yet: this port patches no shutdown
        /// method, and <see cref="EnsureRegistered"/>'s re-arm covers the reload case, which is the
        /// one that actually happens. It exists so that the state and the way to clear it live in
        /// the same file.
        /// </remarks>
        public static void Reset()
        {
            IsInMapView = false;
        }

        /// <summary>The map view was created - every marker in it is new.</summary>
        /// <param name="_">The message. Unused: the arrival is the payload.</param>
        /// <remarks>
        /// Nothing is drawn from here. The legacy drew on its own poll and this port keeps that
        /// shape: this only moves the flag, and the renderer's next tick notices. The toolbar is the
        /// one thing shown from here, because "appears the moment the map does" is a visible promise
        /// and waiting for the renderer's next tick would make it arrive a frame late for no reason.
        /// <c>SyncMapView</c> is idempotent, so the plugin's per-frame call cannot fight this one.
        /// </remarks>
        private static void OnMapInitialized(MessageCenterMessage _)
        {
            IsInMapView = true;
            ApplyToolbarVisibility();
        }

        /// <summary>The map view is gone, so no marker can be read.</summary>
        /// <param name="_">The message. Unused.</param>
        private static void OnMapViewLeft(MessageCenterMessage _)
        {
            IsInMapView = false;
            ApplyToolbarVisibility();
        }

        /// <summary>The game's own pause/ESC menu opened, so the z-order raise must stand down.</summary>
        /// <param name="_">The message. Unused: the arrival is the payload.</param>
        /// <remarks>
        /// The publisher is the game's own pause switch, <c>KSP.Game.UIManager.SetPauseVisible(bool)</c>:
        /// measured in the shipped <c>Assembly-CSharp.dll</c> with <c>ikdasm</c>, its
        /// <c>Publish&lt;EscapeMenuOpenedMessage&gt;()</c> call sits in the true branch, right after
        /// <c>GlobalEscapeMenu.SetVisible(true)</c> and <c>Mouse.EnableVirtualCursor(true)</c>, and it
        /// only publishes when the visibility actually changed (the method returns early when the
        /// requested state equals the current one), so this is a transition, not a per-frame event.
        /// </remarks>
        private static void OnEscapeMenuOpened(MessageCenterMessage _)
        {
            IsEscapeMenuOpen = true;
            ApplyEscapeMenuSuppression("the game's own menu opened");
        }

        /// <summary>The game's own pause/ESC menu closed, so the raise is re-run.</summary>
        /// <param name="_">The message. Unused.</param>
        /// <remarks>
        /// Not "the panels are restored from a saved copy": the window layer re-runs its own pass, so
        /// the panels take fresh monotonic orders in creation order and the mutual <c>1&lt;2&lt;3</c>
        /// is re-established and re-logged by that pass.
        /// </remarks>
        private static void OnEscapeMenuClosed(MessageCenterMessage _)
        {
            IsEscapeMenuOpen = false;
            ApplyEscapeMenuSuppression("the game's own menu closed");
        }

        /// <summary>
        /// Pushes the pause-menu state to the window layer's z-order suppression.
        /// </summary>
        /// <param name="context">Which path got here, for the failure line.</param>
        /// <remarks>
        /// <para>
        /// <b>Wrapped for the same reason <see cref="ApplyToolbarVisibility"/> is.</b> Two of the three
        /// callers run inside a message-bus dispatch: the dispatcher logs a throwing subscriber and
        /// then abandons <i>that handler's</i> remaining work, so a fault here would surface as a
        /// stack trace to attribute instead of a named failure. Here the remaining work is nothing -
        /// but the next message, or the next save load's re-arm, re-applies the state either way.
        /// </para>
        /// <para>
        /// The idempotence lives in the window layer, not here: this is called on every menu
        /// transition and on every re-arm, and only a real state change does work or logs.
        /// </para>
        /// </remarks>
        private static void ApplyEscapeMenuSuppression(string context)
        {
            try
            {
                CommNextUIManager.SetPauseSuppressed(IsEscapeMenuOpen);
            }
            catch (Exception exception)
            {
                Write("the pause-menu z-order suppression could not be applied (" + context + "; "
                    + exception.GetType().Name + ": " + exception.Message + "); the panels keep the "
                    + "sorting order they had, and the next menu transition re-applies it");
            }
        }

        /// <summary>
        /// Pushes the freshly-changed state to the window layer.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Wrapped, because this runs inside a message-bus dispatch.</b> The dispatcher carries its
        /// own per-subscriber <c>try/catch</c> (measured: one <c>.try</c> and one <c>catch</c> inside
        /// <c>Publish</c>'s loop, so one throwing subscriber does not stop the others), which means an
        /// exception here is logged as an exception and then <i>this handler's remaining work</i> is
        /// abandoned. There is no remaining work after this call, but a fault would still surface as a
        /// stack trace a reader has to attribute to the mod - so the failure is named instead. The
        /// plugin's own per-frame sync is the retry.
        /// </para>
        /// <para>
        /// A missing window layer is not an error here: it is logged once by the window layer itself,
        /// and at boot the messages can legitimately arrive before <c>OnInitialized</c> has built the
        /// windows.
        /// </para>
        /// </remarks>
        private static void ApplyToolbarVisibility()
        {
            try
            {
                CommNextUIManager.SyncMapView(IsInMapView);
            }
            catch (Exception exception)
            {
                Write("toolbar visibility could not be applied on the map message ("
                    + exception.GetType().Name + ": " + exception.Message + "); the per-frame sync "
                    + "will retry");
            }
        }

        /// <summary>Logs through the plugin, which owns the null-guarded logger access.</summary>
        /// <param name="message">The line to write.</param>
        /// <remarks>
        /// Guarded rather than assumed: a message can arrive while the mod is still initializing, and
        /// the logger is assigned by the loader after <c>Update()</c> can already be ticking. A log
        /// call on a null logger inside the boot path is the documented way to turn "the mod is
        /// alive" into "the mod is dead" (dev guide 61.3).
        /// </remarks>
        private static void Write(string message)
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin == null)
            {
                return;
            }

            plugin.LogLine(message);
        }
    }
}
