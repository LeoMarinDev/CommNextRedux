using System;
using CommNextRedux.Diagnostics;
using CommNextRedux.Network;
using CommNextRedux.Patches;
using CommNextRedux.Rendering;
using CommNextRedux.UI;
using CommNextRedux.UI.Utils;
using CommNextRedux.Utilities;
using KSP.Game;
using Redux.ExtraModTypes;
using ReduxLib.Configuration;
using ReduxLib.Logging;
using SpaceWarp2.API.Mods;
using SpaceWarp2.API.Parts;
using SpaceWarp2.UI.API.Settings;

namespace CommNextRedux
{
    /// <summary>
    /// The CommNextRedux entry point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What this type is.</b> The entry point and the only loader-visible type of the mod: it binds
    /// the configuration, builds the network engine and the diagnostic probe, installs the Harmony
    /// patches, resolves both runtime materials and the ruler geometry, and ticks the map listener,
    /// the renderer and the probe. It grew one phase at a time - the scaffold in P1, then the hook,
    /// the engine, the bundle and the line renderer through P6, and the range rulers in P7 - so each
    /// member's remarks name the phase that put it there.
    /// </para>
    /// <para>
    /// <b>There is no entry-point attribute, and that is correct on 0.2.8.5.</b> The contract is
    /// the base type plus <c>swinfo.json</c>'s <c>main_assembly</c>. No
    /// <c>[SpaceWarpPlugin]</c>-style attribute exists in the pinned API set, and no
    /// BepInEx type (<c>BaseUnityPlugin</c>, <c>BepInPlugin</c>) exists in any of the 234
    /// assemblies under <c>$KSP2_ROOT/KSP2_x64_Data/Managed/</c>. The legacy this ports
    /// (<c>mods-outdated/CommNext/src/CommNext/CommNextPlugin.cs</c>) derived from
    /// <c>BaseSpaceWarpPlugin</c> and carried <c>using BepInEx;</c> - both were removed with
    /// SpaceWarp 1.x and neither resolves here.
    /// </para>
    /// <para>
    /// <b>The base type lives in <c>Assembly-CSharp.dll</c>, not <c>ReduxLib.dll</c>.</b>
    /// Measured: <c>monodis --typedef "$KSP2_ROOT/KSP2_x64_Data/Managed/Assembly-CSharp.dll"</c>
    /// finds <c>Redux.ExtraModTypes.KerbalMod</c> at typedef row 2982, and the same flag over
    /// <c>ReduxLib.dll</c> finds nothing. It is also absent from the pinned
    /// <c>xml/ffc94930/ReduxLib.xml</c>, which is expected - the pinned sets document members
    /// that carry XML doc comments, and this type does not. The full command and raw output are
    /// in <c>Deploy/obj/API-DELTA.md</c>.
    /// </para>
    /// </remarks>
    public class CommNextReduxPlugin : KerbalMod
    {
        /// <summary>
        /// The mod's identity, as <c>swinfo.json</c> declares it. It is the loader's key for this
        /// mod, and it is also the name the loader gives this mod's <c>ILogger</c> - which is why
        /// every line this mod writes already begins with <see cref="LogTag"/>.
        /// </summary>
        public const string ModId = "CommNextRedux";

        /// <summary>
        /// The tag every log line this mod writes carries - supplied by the loader, not written
        /// into the message by this class.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this constant exists but is never concatenated into a message.</b>
        /// <c>ReduxLib</c>'s logger prefixes its own name onto every line. Pinned source, commit
        /// <c>54bdefc</c>, <c>Runtime/ReduxLib/Logging/UnityLogger.cs</c>:
        /// </para>
        /// <code>
        /// public override void Log(LogLevel level, object x)
        /// {
        ///     if (level > _provider.CurrentFilterLevel) return;
        ///     UnityEngine.Debug.unityLogger.Log(level.AsLogType(), $"[{_name}] {x}");
        ///     _provider.TriggerLogEvent(this, level, x);
        /// }
        /// </code>
        /// <para>
        /// The loader names that logger after the mod, so a message that also spelled the tag out
        /// would appear twice - <c>[CommNextRedux] [CommNextRedux] ...</c>. That is a measured bug
        /// from the sibling port, recorded as finding F3 in
        /// <c>mods/CommLinesRedux/Deploy/obj/PORT-PROGRESS.md</c>, and the doubling is visible in
        /// a real launch log. The fix there - and the shape copied here - is to keep the constant
        /// for documentation and evidence, and to hand <c>LogInfo</c> an untagged message.
        /// </para>
        /// <para>
        /// The in-game half of that proof, from this machine's live log
        /// (<c>$KSP2_ROOT/Ksp2.log</c>, lines 100-101 of a session that ran the sibling port):
        /// </para>
        /// <code>
        /// [LOG 14:14:17.782] [CommLinesRedux] icon loaded from ... (32x32)
        /// [LOG 14:14:17.788] [CommLinesRedux] Harmony registered: ...
        /// </code>
        /// <para>
        /// One tag per line, supplied by the loader, with no copy in the message.
        /// </para>
        /// </remarks>
        public const string LogTag = "[CommNextRedux]";

        /// <summary>
        /// The mod's display name, and the settings section name registered for it.
        /// </summary>
        private const string DisplayName = "CommNext Redux";

        /// <summary>
        /// The settings section this mod registers, and the string
        /// <see cref="SettingsMenu.RegisterConfigFile"/> is called with. It is the display name so
        /// that <c>Settings -&gt; Mods</c> shows a human-readable heading rather than a mod id.
        /// </summary>
        private const string SettingsSectionName = DisplayName;

        /// <summary>
        /// The liveness marker: one line proving the loader instantiated and initialized this type,
        /// and the fastest way to see that the mod is alive in a log of tens of thousands of lines.
        /// </summary>
        /// <remarks>
        /// It names what the mod actually does, so it is also a version discriminator: a session that
        /// logs this line ran <b>this</b> build and not an earlier one. That property is why it must
        /// be updated in every phase that adds a feature - a stale marker makes a launch grep
        /// ambiguous in exactly the situation the grep exists for.
        /// </remarks>
        private const string InitializedMessage =
            DisplayName + " initialized (phase 8b: the map TOOLBAR, the CONTROL SURFACE and the "
            + "VESSEL REPORT are in - the mod's five custom UI controls are registered with Unity's "
            + "own factory registry before any template is cloned (the P2 player failure, fixed), and "
            + "all three windows are built from the mod's own AssetBundle through Window.Create's "
            + "PanelRenderer (each window's content root fetched with Extensions.GetWindowRoot), "
            + "created in the order toolbar -> vessel report -> tooltip because that order "
            + "IS their z-order. The toolbar shows itself on map entry and hides on map exit, its two "
            + "mode buttons write Settings -> Mods' \"Map / Connections mode\" and \"Map / Rulers "
            + "mode\" and read the state back for their own highlight, and the stylesheet is proven "
            + "in-effect by a resolved-style read-back rather than assumed from the template's "
            + "project:// URI. The third button toggles the vessel report over THIS port's network "
            + "engine - the active vessel's tree edges, its per-band ranges, a filter, a sort and the "
            + "two map actions - and closes with the map; the report's own filter and sort choices and "
            + "its direction tags are translated at the call site, and the two map actions use the "
            + "game's OWN public routes (a MapRequestFocusMessage for focus, "
            + "ViewController.SetActiveVehicle for control) because the legacy's map-item methods are "
            + "private on this pin - measured by reflection, recorded as F64. Three of the legacy "
            + "report's "
            + "columns are gone because this engine publishes no per-link data for them (documented in "
            + "divergences.md). The map connection lines and the range rulers carry on from phase 7 as "
            + "they were. The game's OWN green CommNet line drawer is now silenced by default (a "
            + "postfix on PersistentProfileManager.get_ShowCommNetLines, Map key \"Hide the game's own "
            + "CommNet lines\"), so CommNext's band-coloured lines are the only lines drawn on the map), "
            + "and the relay module registers for background resource processing (U6e: a relay on a "
            + "vessel that is not being controlled keeps paying its EC and stays in the network). "
            + "U6g adds the map LINES' appearance as settings: a new Settings -> Mods section, "
            + "\"Lines\", carries one colour per band (X, S, K, Ka, V), one for a relay-to-relay hop and "
            + "one for every other unbanded link, plus \"Line opacity\" (1.00 = fully solid, 0 = "
            + "invisible) - all seven colours default to EMPTY, which means \"draw the built-in "
            + "colour\", so this build draws exactly what the last one drew until the player types "
            + "something. The values are parsed once into a per-slot cache through the same "
            + "UnityEngine.ColorUtility conversion the band icons use, applied at the renderer's single "
            + "colour seam, and re-applied on change - the next line pass repaints the lines already "
            + "drawn, with no reload. The vessel report's header is now two rows (name, then the range, "
            + "below it) and a long vessel name elides inside the window instead of running under the "
            + "range; the connection rows' direction tag has positive spacing again";

        /// <summary>
        /// The live instance, so later phases can reach the plugin without a
        /// <c>GameObject.Find</c>, and so the Harmony patches can reach the engine and the logger.
        /// Assigned in <see cref="OnPreInitialized"/>, which the loader calls before any subsystem
        /// member of this type could run - and before the patches are installed.
        /// </summary>
        /// <value>The single instance the loader created, or <c>null</c> before load.</value>
        public static CommNextReduxPlugin Instance { get; private set; }

        /// <summary>
        /// The occlusion-aware connection-graph engine.
        /// </summary>
        /// <remarks>
        /// Constructed in <see cref="OnPreInitialized"/> <b>before</b> Harmony is installed, so there
        /// is no window in which a patched <c>RebuildConnectionGraph</c> could run against a null
        /// engine.
        /// </remarks>
        public NetworkEngine Network { get; private set; }

        /// <summary>The D9 diagnostic probe. Reads <see cref="Network"/>'s last pass.</summary>
        public NetworkProbe Probe { get; private set; }

        /// <summary>
        /// Whether Harmony has been installed this process.
        /// </summary>
        /// <remarks>
        /// Set <b>after</b> <c>CreateHarmonyAndPatchAll()</c> returns, never before: a flag set first
        /// would suppress every retry if the patch call threw, and the mod would load with no patches
        /// and no second attempt.
        /// </remarks>
        private bool _harmonyInstalled;

        /// <summary>
        /// Whether <see cref="NetworkConfig.Initialize"/> has bound the configuration.
        /// </summary>
        private bool _configBound;

        /// <summary>
        /// Whether the renderer and its material have been initialized this process.
        /// </summary>
        /// <remarks>
        /// Set <b>after</b> the material has been resolved, never before: a flag set first would
        /// suppress the retry if the graphics side was not ready, and the renderer would refuse every
        /// line for the whole session with a "no material" error as its only trace.
        /// </remarks>
        private bool _rendererReady;

        /// <summary>
        /// Whether the ruler mode has been applied to the renderer and its callback registered.
        /// </summary>
        /// <remarks>
        /// Guarded separately from <see cref="_rendererReady"/>, and set <b>before</b> the work:
        /// registering a config callback is not safely retryable (it appends a delegate), and the
        /// work it guards cannot leave the mod in a worse state than "the mode was not applied" -
        /// which is exactly what a retry would attempt and what a duplicate registration would make
        /// worse rather than better. See <see cref="EnsureRulersModeWiring"/>.
        /// </remarks>
        private bool _rulersModeWired;

        /// <summary>
        /// Whether the connection mode has been applied to the renderer and its callback registered.
        /// </summary>
        /// <remarks>
        /// Phase 8a's twin of <see cref="_rulersModeWired"/>, and it exists separately for the same
        /// reason that one is not folded into <see cref="_rendererReady"/>: it is set <b>before</b> the
        /// work, because two callbacks on one config entry append rather than replace, and a retry
        /// after a throw must not accumulate delegates. See <see cref="EnsureConnectionsModeWiring"/>.
        /// </remarks>
        private bool _connectionsModeWired;

        /// <summary>
        /// Whether the line-appearance cache has been filled and its config callbacks registered
        /// (U6g).
        /// </summary>
        /// <remarks>
        /// The same shape and the same reasons as <see cref="_connectionsModeWired"/>: set first, so a
        /// retry after a throw cannot register a second set of callbacks, and separate from
        /// <see cref="_rendererReady"/> because it is set inside that method's own build-up - see
        /// <see cref="EnsureLineAppearanceWiring"/>.
        /// </remarks>
        private bool _lineAppearanceWired;

        /// <summary>
        /// Whether the stock-comm-line override has been armed and its config callback registered.
        /// </summary>
        /// <remarks>
        /// The same shape and the same reasons as <see cref="_rulersModeWired"/>: it is set
        /// <b>before</b> the work, because a registration that throws must not be retried into a
        /// duplicate delegate - and because the cache it arms must never be left stale by a retry.
        /// It is deliberately separate from <see cref="_rendererReady"/>: the override has nothing to
        /// do with the renderer and must be armed on a launch where the graphics side never comes up.
        /// </remarks>
        private bool _stockLinesOverrideWired;

        /// <summary>
        /// Whether the relay module has been registered for background resource processing.
        /// </summary>
        /// <remarks>
        /// Set <b>before</b> the registration, because that call is not retryable: the shipped
        /// implementation raises <c>ArgumentException</c> when the type is already in SpaceWarp2's
        /// list. See <see cref="EnsureBackgroundResourceProcessing"/>, which is where the U6e defect
        /// this closes is documented.
        /// </remarks>
        private bool _backgroundResourceProcessingRegistered;

        /// <summary>
        /// Whether the settings section has already been registered this session.
        /// </summary>
        /// <remarks>
        /// <c>SettingsMenu.RegisteredConfigFiles</c> is a plain dictionary, so a second insert under
        /// the same section name silently replaces the first entry. This flag makes the call
        /// idempotent. It exists for the same reason it does in the sibling port, and - as there -
        /// it is set <b>after</b> the registration succeeds, never before: a flag set before the
        /// work would suppress every retry if the work threw.
        /// </remarks>
        private bool _settingsRegistered;

        /// <summary>
        /// First loader hook. Captures the instance before anything can reach for it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The base call is deliberate. <c>KerbalMod</c>'s own hook bodies live in
        /// <c>Assembly-CSharp.dll</c>, which none of the toolchain's IL readers can dump
        /// (<c>monodis --filter</c> core-dumps on that 13 MB assembly), so the bodies could not be
        /// read and their emptiness could not be proven. Calling the base is weakly dominant: if
        /// the bodies are empty the call is free, and if they are not, skipping them would silently
        /// drop loader bookkeeping. The sibling port omits the base call and works, which proves the
        /// base is not *required* - it does not prove it is a no-op.
        /// </para>
        /// <para>
        /// Nothing here touches the logger or the configuration: at this point the loader has not
        /// necessarily assigned either, and a log call on a null logger inside the load sequence is
        /// the documented way to turn "mod is alive" into "mod is dead".
        /// </para>
        /// </remarks>
        public override void OnPreInitialized()
        {
            base.OnPreInitialized();
            Instance = this;

            // D-L19-1, and it goes first: Unity's ECS TypeManager has to know this mod's four module
            // types before any part carrying one builds its entity, and a part can be built as soon as
            // an assembly scene is up. Every other step below can wait a frame; this one cannot. See
            // EcsTypeRegistration for the L19 stack this fixes and for why only these types need it.
            EcsTypeRegistration.RegisterModuleTypes();

            // U6e, and it belongs here for the same reason: the game reads this registration when a
            // PART is added to a vessel, so it has to be in place before the first save loads or a
            // vessel is built. See EnsureBackgroundResourceProcessing for the L22 defect it closes.
            EnsureBackgroundResourceProcessing();

            // Order matters and is not cosmetic: bind the configuration first, so the engine's
            // accessors never read an unbound entry; build the engine and the probe second; install
            // Harmony last. A patched RebuildConnectionGraph may not run against a half-built mod.
            EnsureConfiguration();
            EnsureEngineAndProbe();
            EnsureHarmony();
        }

        /// <summary>
        /// Second loader hook: the single liveness line, then the settings registration.
        /// </summary>
        public override void OnInitialized()
        {
            base.OnInitialized();
            LogLine(InitializedMessage);
            LogLine("network core: route=DEEP (the D8 verdict) - RebuildConnectionGraph is replaced, "
                + "occlusion runs in managed code, no IJob and no native DLL ship with this mod");
            EnsureSettingsRegistration();
            EnsureRenderer();
            EnsureUI();
        }

        /// <summary>
        /// Per-frame tick. Drives the map listener, the line renderer and the diagnostic probe.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The graph itself is never touched from here.</b> It is produced inside the patched
        /// <c>RebuildConnectionGraph</c>, driven by the game's own update at its own cadence, and this
        /// method only reads what that pass left behind. That separation is deliberate: the game
        /// rebuilds the CommNet graph roughly every three seconds
        /// (<c>CommNetManager.OnUpdate</c> decrements a 3-second timer and sets its dirty flag when it
        /// rolls over), so there is nothing a per-frame tick could add to the graph and a great deal it
        /// could break.
        /// </para>
        /// <para>
        /// <b>Five things tick here, and only the last one is a diagnostic.</b> The map listener must
        /// run with the probe off - it is what tells the renderer that a map view exists. The renderer
        /// is self-throttled at <c>ConnectionsRenderer.RefreshSeconds</c>, so calling it every frame
        /// costs a float subtraction on 99 % of frames. The toolbar sync is a boolean comparison -
        /// see <c>CommNextUIManager.SyncMapView</c> for why a message-driven value is also asserted
        /// here. The panel-layer retry (P9.3) is the same shape: the windows register themselves when
        /// they are built, the list is empty once they are fixed, and until then it is what turns "the
        /// panel did not exist yet" into a bounded retry instead of a silent no-op. The probe is
        /// guarded by its own config value as well as by its own internal check.
        /// </para>
        /// <para>
        /// Declared as a plain <b>private</b> Unity message, not an override:
        /// <c>Redux.ExtraModTypes.KerbalMod</c> declares no <c>Update</c> of its own, and the
        /// in-game-validated <c>OrbitalSurvey</c> and the sibling <c>CommLinesRedux</c> port drive
        /// their per-frame work from exactly this shape.
        /// </para>
        /// <para>
        /// Each of the three is wrapped separately rather than sharing one guard: an exception thrown
        /// out of a Unity message aborts the rest of this type's frame work, so one failing subsystem
        /// must not stop the other two. The reader's cost of three guards is a few nanoseconds; the
        /// cost of a shared one is that a listener failure would silently stop the renderer too.
        /// </para>
        /// </remarks>
        private void Update()
        {
            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;

            try
            {
                EventListener.EnsureRegistered(game);
            }
            catch (Exception exception)
            {
                LogWarningLine("map listener registration failed (" + exception.GetType().Name + ": "
                    + exception.Message + "); the renderer still draws, it just polls instead");
            }

            try
            {
                ConnectionsRenderer.Tick(game);
            }
            catch (Exception exception)
            {
                LogWarningLine("renderer tick failed (" + exception.GetType().Name + ": "
                    + exception.Message + ")");
            }

            // The toolbar's self-healing half. The map messages drive it in the frame the transition
            // happens, but a save load replaces the MessageCenter and orphans those handlers with no
            // log line (dev guide 61.6) - so the desired visibility is also asserted here, and the
            // comparison inside SyncMapView is what makes a per-frame call free. Same reason, same
            // shape, as EventListener's own re-arm.
            try
            {
                CommNextUIManager.SyncMapView(EventListener.IsInMapView);
            }
            catch (Exception exception)
            {
                LogWarningLine("toolbar visibility sync failed (" + exception.GetType().Name + ": "
                    + exception.Message + ")");
            }

            // P9.3's retry: each window registers itself as it is created, and this drains those
            // registrations once their panel exists. Before F82 this was the library's own one-shot
            // `PanelFactory.Apply`, which returned silently at three points - so a panel that was not
            // ready yet left the window off Unity's UI layer and the game's map acted on clicks that
            // landed on it. It is a `Count` comparison on every frame after the windows are fixed.
            try
            {
                PanelLayerFix.Tick();
            }
            catch (Exception exception)
            {
                LogWarningLine("ui: map-input retry tick failed (" + exception.GetType().Name + ": "
                    + exception.Message + ")");
            }

            // P9.5's teardown half (D65): a window's own pointer poll is paused the moment its element
            // leaves the panel, so an element that is removed or hidden while it is holding the map's
            // actions down would never run the release. This is that release path, and it is a
            // `Count` comparison on every frame when nothing is held.
            try
            {
                PanelInputBlocker.Tick();
            }
            catch (Exception exception)
            {
                LogWarningLine("ui: map-input guard tick failed (" + exception.GetType().Name + ": "
                    + exception.Message + ")");
            }

            NetworkProbe probe = Probe;
            if (probe == null || !NetworkConfig.ProbeEnabled)
            {
                return;
            }

            try
            {
                probe.Tick(game);
            }
            catch (Exception exception)
            {
                LogWarningLine("probe tick failed (" + exception.GetType().Name + ": "
                    + exception.Message + ")");
            }
        }

        /// <summary>
        /// Releases anything this mod is still holding on the game's input, then unbinds.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>P9.5 (D65).</b> <see cref="UI.Utils.PanelInputBlocker"/> holds the map view's mouse
        /// camera actions down while the pointer rests on a window. Every other release path is driven
        /// by the windows themselves - the element's own 100 ms poll, the per-frame
        /// <c>Tick</c> that catches a removed or hidden element, and the map-exit transition in
        /// <c>CommNextUIManager.SyncMapView</c> - and this is the last one: the mod going away with the
        /// pointer still on a window. A disabled action that outlives its owner is the one failure this
        /// guard must not have, so the release is unconditional and it reads every action back.
        /// </para>
        /// <para>
        /// The base type declares no Unity message (measured: <c>Redux.ExtraModTypes.KerbalMod</c> and
        /// its base <c>KSP.Game.KerbalMonoBehaviour</c> carry only the three init hooks, their
        /// properties, a ctor and one static helper), so this hides nothing. It never throws: an
        /// exception out of a Unity teardown message would abort the rest of the object's destruction.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            try
            {
                PanelInputBlocker.ReleaseAll("the mod is being destroyed");
            }
            catch (Exception exception)
            {
                LogWarningLine("ui: map-input release on destroy failed (" + exception.GetType().Name
                    + ": " + exception.Message + ")");
            }
        }

        /// <summary>
        /// Binds the configuration once, and reports the resolved values.
        /// </summary>
        /// <remarks>
        /// The summary is <see cref="NetworkConfig.Initialize"/>'s own return value, logged here and
        /// nowhere else. It is deliberately not rebuilt in this class: the two used to exist side by
        /// side, and the local copy had already fallen behind the real one (it did not mention
        /// <c>relaysRequirePower</c> or <c>profileLogs</c>), so a reader could be shown a configuration
        /// that was not the one bound. One producer, one line.
        /// </remarks>
        private void EnsureConfiguration()
        {
            if (_configBound)
            {
                return;
            }

            IConfigFile configFile = SWConfiguration;
            if (configFile == null)
            {
                // Not fatal: every NetworkConfig accessor falls back to its own default when unbound,
                // so the engine still works with the shipped defaults. It is worth a line because the
                // user's file will not be read.
                LogWarningLine("configuration is not bound (SWConfiguration is null); the network will "
                    + "run on its built-in defaults and Settings -> Mods will show no entries");

                // D-L21-1's override is armed even here, on its built-in default: the game's own
                // green lines are the defect this key exists for, and a broken config bind must not
                // silently bring them back. The line Apply writes names this path.
                EnsureStockCommLinesOverride();
                return;
            }

            LogLine("settings: " + NetworkConfig.Initialize(this));
            _configBound = true;

            // Before EnsureHarmony, so the cache the postfix reads is set before Harmony is installed
            // - there is then no window in which the patched getter can run against the fallback.
            EnsureStockCommLinesOverride();
        }

        /// <summary>
        /// Arms the D-L21-1 override once, and keeps it in step with its config entry after that.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What is being overridden and why the seam is the getter.</b> The pin ships its own
        /// CommNet drawer (<c>KSP.Map.CommNetLineRenderer</c>), which paints every edge of the graph
        /// as one hardcoded green line and defaults to ON; because this mod replaces the graph, that
        /// drawer renders this mod's own tree in flat green over its band-coloured lines. Its draw
        /// body clears its command buffer first and only then evaluates
        /// <c>PersistentProfileManager.get_ShowCommNetLines()</c>, so the getter - not the draw
        /// method - is the point where the game's own early-out runs with the buffer already clear.
        /// </para>
        /// <para>
        /// <b>Armed before Harmony, refreshed by callback.</b> This runs from
        /// <see cref="EnsureConfiguration"/>, which <see cref="OnPreInitialized"/> calls before
        /// <see cref="EnsureHarmony"/> - so the postfix can never observe the unarmed fallback state.
        /// The callback is additive (<c>RegisterCallback</c> appends, it does not replace), so the
        /// generic <c>config change:</c> line <c>NetworkConfig</c> writes for this key still fires
        /// alongside it.
        /// </para>
        /// <para>
        /// <b>Independent of the master switch.</b> <see cref="NetworkConfig.Enable"/> changes whose
        /// graph is used; this only stops the game's drawer painting over whatever is there. A player
        /// who hands the graph back to the game still gets the game's own lines when this entry is
        /// false, and only the mod's capture-driven rendering when it is true.
        /// </para>
        /// </remarks>
        private void EnsureStockCommLinesOverride()
        {
            if (_stockLinesOverrideWired)
            {
                return;
            }

            _stockLinesOverrideWired = true;

            bool bound = NetworkConfig.HideGameCommLines != null;
            StockCommNetLinesPatch.Apply(NetworkConfig.HideGameCommLinesEnabled,
                bound
                    ? "boot; Map/\"" + NetworkConfig.HideGameCommLinesKey + "\""
                    : "boot; the configuration is not bound, so the built-in default applies");

            ConfigValue<bool> entry = NetworkConfig.HideGameCommLines;
            if (entry == null)
            {
                return;
            }

            entry.RegisterCallback((from, to) => StockCommNetLinesPatch.Apply(to,
                "the player changed Map/\"" + NetworkConfig.HideGameCommLinesKey + "\""));
        }

        /// <summary>Builds the engine and the probe once.</summary>
        private void EnsureEngineAndProbe()
        {
            if (Network != null)
            {
                return;
            }

            Network = new NetworkEngine(LogLine, LogWarningLine);
            Probe = new NetworkProbe(Network, LogLine, LogWarningLine);
        }

        /// <summary>
        /// Registers <c>PartComponentModule_NextRelay</c> for background resource processing, once,
        /// so a relay on a vessel that is not the controlled one keeps paying its EC and keeps its
        /// node state current.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The defect this closes (U6e, from the L22 run).</b> In L22 this mod's own engine gated
        /// three of the user's four relay nodes out of the graph: the relays logged
        /// <c>hasResources=false</c> while <c>ec=0.5</c> was healthy, and each one flipped back to
        /// <c>true</c> only while its vessel was the controlled one - which is exactly when that
        /// vessel's edge appeared (1, then 2, then 3, then 4 drawn lines, one per control change).
        /// The field the engine reads is written per tick, and that tick is gated on the part owner
        /// being fixed-updated.
        /// </para>
        /// <para>
        /// <b>Rank-1 proof of the gate.</b> <c>Assembly-CSharp.dll</c>,
        /// <c>PartOwnerComponent::OnFixedUpdate</c>: <c>ResourceFlowRequestManager::UpdateFlowRequests</c>
        /// is called from <c>IL_009c</c>, reached by <c>IL_0042 brtrue</c> when
        /// <c>HasRegisteredPartComponentsForFixedUpdate</c> is set, <c>IL_004f brtrue</c> when the
        /// vessel is the active one, <c>IL_0062 beq</c> for <c>Physics == AtRest</c> (1),
        /// <c>IL_0075 beq</c> for <c>RigidBody</c> (3), and <c>IL_009a</c> for <c>Orbital</c> (2) only
        /// when <c>IsOrbitalPhysicsUnderThrustActive</c> is true; every other state branches to
        /// <c>IL_00a9</c> and skips the call. An on-rails <c>Orbital</c> vessel that is not being
        /// controlled - three of the four in that save - therefore never has its requests updated,
        /// and the relay's flag stays where the last tick left it.
        /// </para>
        /// <para>
        /// <b>And the flag that lifts that gate is the one this call sets.</b>
        /// <c>PartOwnerComponent::Add</c> (<c>IL_0015</c>-<c>IL_002f</c>) tests
        /// <c>SpaceWarp2.API.Parts.PartComponentModuleOverride.RegisteredPartComponentOverrides</c>
        /// with <c>PartComponent::TryGetModule</c>, whose match is
        /// <c>type.IsAssignableFrom(module.GetType())</c> (<c>IL_001b</c>-<c>IL_0027</c>), and sets
        /// <c>HasRegisteredPartComponentsForFixedUpdate</c> on a hit. The registration route is
        /// SpaceWarp2's own public API: <c>SpaceWarp2.dll</c>, method row 109,
        /// <c>void RegisterModuleForBackgroundResourceProcessing&lt;T&gt;()</c>, with the public static
        /// field <c>RegisteredPartComponentOverrides</c> at field row 42.
        /// </para>
        /// <para>
        /// <b>This is a port regression, restored.</b> The legacy called the same registration for the
        /// same module (<c>mods-outdated/CommNext/src/CommNext/CommNextPlugin.cs</c>, under "EC
        /// Background Processing"), and the in-repo sibling registers its own module the same way
        /// (<c>mods/remote/OrbitalSurvey/.../OrbitalSurveyPlugin.cs</c>). This port dropped it on the
        /// belief that the game's broker serviced a background vessel's request by itself -
        /// falsified by L22 - and the two comments that recorded that belief are corrected in the
        /// same change (<c>Data_NextRelay.cs</c>, <c>NetworkConfig.cs</c>).
        /// </para>
        /// <para>
        /// <b>Unconditional, and not gated on the relays-require-power key.</b> The legacy gated it on
        /// that setting, which is why the setting could only be honoured there on a reload. Here the
        /// flag is refreshed on every tick either way, so one registration for the process keeps the
        /// setting live in both states - and with the setting off it is also what clears a stale
        /// <c>false</c> left behind by an earlier state, the one case where gating would silently
        /// strand a relay.
        /// </para>
        /// <para>
        /// <b>One shot, and the flag is set first, because a second call throws.</b> The shipped IL of
        /// the registration raises <c>ArgumentException</c> when the type is already in the list - it
        /// logs "already registered. Skipping." and then throws anyway - so this is a non-retryable
        /// registration, in the same class as the mode wirings, and the flag has to be set before the
        /// call. The call is wrapped so that a failure cannot abort the load sequence.
        /// </para>
        /// </remarks>
        private void EnsureBackgroundResourceProcessing()
        {
            if (_backgroundResourceProcessingRegistered)
            {
                return;
            }

            _backgroundResourceProcessingRegistered = true;

            try
            {
                PartComponentModuleOverride.RegisterModuleForBackgroundResourceProcessing<
                    Modules.Relay.PartComponentModule_NextRelay>();

                // The registration logs through SpaceWarp's own logger ("Registered '<type>' for
                // background resources processing."), which is the positive marker that the list was
                // written; this line says why this mod needs it, and at Info so a post-launch grep
                // finds it (the default filter drops LogDebug).
                LogLine("relay background processing: PartComponentModule_NextRelay is registered with "
                    + "SpaceWarp2 (RegisterModuleForBackgroundResourceProcessing), so a relay on a "
                    + "vessel that is not being controlled is still fixed-updated and its node state "
                    + "cannot freeze at 'no resources' (the L22 defect)");
            }
            catch (Exception exception)
            {
                LogWarningLine("relay background processing: the registration threw ("
                    + exception.GetType().Name + ": " + exception.Message + ") - relays on vessels "
                    + "that are not the controlled one may read 'no resources' and drop out of the "
                    + "network until they are controlled");
            }
        }

        /// <summary>
        /// Initializes the line renderer and resolves its one material, once.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why here and not in <see cref="OnPreInitialized"/>.</b> <c>Shader.Find</c> is a
        /// graphics-side call, and the earlier hook runs before the loader has necessarily brought the
        /// graphics side up - the sibling port puts its equivalent call in its own
        /// <c>OnInitialized</c> for exactly that reason. Nothing can ask for a line before this runs:
        /// the renderer refuses to create an object while <c>LineMaterials.HasMaterial</c> is false,
        /// and it says so once.
        /// </para>
        /// <para>
        /// <b>Idempotent, and the flag is set last.</b> A second call is harmless - the shader lookup
        /// is re-run and the material replaced with an equivalent one, while every renderer that
        /// already read its own copy is unaffected - so the retry that a first-call failure deserves is
        /// simply "call it again", which is what the flag's absence permits.
        /// </para>
        /// </remarks>
        private void EnsureRenderer()
        {
            if (_rendererReady)
            {
                return;
            }

            ConnectionsRenderer.Initialize(LogLine, LogWarningLine, LogErrorLine);
            ConnectionsRenderer.Reset();

            // The material line this logs is the P6 successor of P2's D3 finding, and it is the one
            // line that says which branch of the lookup chain the player actually got. See
            // Deploy/obj/bundle-verdict.md and divergences.md's D3 correction.
            LineMaterials.GenerateMaterial(LogLine, LogWarningLine, LogErrorLine);

            // Phase 7's two ruler resources, resolved here for exactly the same reason: `Shader.Find`
            // and `AssetBundle.LoadFromFile` are both graphics/asset-side calls, and this hook is the
            // first one the loader runs after that side is up. Nothing can ask for a ruler before this
            // runs - the renderer refuses to create one while `RulerGeometry.HasMesh` or
            // `RulerMaterials.HasTintCapableMaterial` is false, and says so once with its own state
            // token. Each logs the branch it resolved, so neither can fall back silently.
            RulerMaterials.GenerateMaterial(LogLine, LogWarningLine, LogErrorLine);
            RulerGeometry.Resolve(ResolveModFolder(), LogLine, LogWarningLine, LogErrorLine);

            // Phase 7's switch. Last, so the material and the mesh are already resolved when the
            // mode is applied and the first ruler pass can draw on the frame after the map opens.
            EnsureRulersModeWiring();

            // Phase 8a's switch, for the same reason and in the same place: the line pass needs the
            // material, and the mode must be applied before the first map frame or the map opens in
            // the renderer's field default rather than the player's saved setting.
            EnsureConnectionsModeWiring();

            // U6g's line appearance. Last of the renderer-side wirings, and after the two mode
            // wirings on purpose: it is a read-through cache over config values that are already
            // bound, it needs nothing the two above set up, and it logs the one boot line that says
            // which colours the pass will draw - so it reads as the last word on the renderer's
            // state rather than as a preamble to it.
            EnsureLineAppearanceWiring();

            _rendererReady = true;
        }

        /// <summary>
        /// Applies the configured ruler mode to the renderer, once, and keeps it in step after that.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this is the one setting with an apply step at all.</b> Every other config entry is
        /// read at its point of use, so a change lands on the next pass with no cache to clear. The
        /// ruler mode cannot work that way: it IS live renderer state, and the renderer's setter is
        /// what owns the two behaviours a config read cannot provide - destroying every ruler already
        /// drawn when the value becomes <c>None</c>, and forcing a pass on the way out of it. Setting
        /// the property once at boot and once per change is therefore the whole mechanism, and a
        /// per-frame poll of the config would only add a comparison to catch a callback that cannot
        /// be missed (see below).
        /// </para>
        /// <para>
        /// <b>The callback is additive, not exclusive.</b> <c>ConfigValue.RegisterCallback</c>
        /// forwards to <c>IConfigEntry.RegisterCallback</c>, whose implementation is
        /// <c>Callbacks += valueChangedCallback</c> - an event, so
        /// <c>NetworkConfig</c>'s own change log for this same key still fires. Verified in the
        /// pinned ReduxLib source (<c>Runtime/ReduxLib/Configuration/{ConfigValue,JsonConfigEntry}.cs</c>
        /// at <c>54bdefc</c>), because two callbacks on one entry is exactly the shape that would
        /// silently clobber the other if the implementation were an assignment.
        /// </para>
        /// <para>
        /// <b>Guarded by its own flag, set first.</b> <c>EnsureRenderer</c> is retryable (its flag is
        /// set last), and registering a callback twice would apply the same value twice - harmless,
        /// because the setter no-ops on an unchanged value, but it would accumulate one delegate per
        /// retry for the life of the process. The flag here prevents that, and it is set before the
        /// registration for the mirror reason: a registration that throws must not be retried into a
        /// duplicate.
        /// </para>
        /// <para>
        /// The boot value comes from the config, never from the setter's field default, and it is
        /// logged by the setter itself (<c>render-rulers: mode -&gt; ...</c>) rather than here.
        /// </para>
        /// </remarks>
        private void EnsureRulersModeWiring()
        {
            if (_rulersModeWired)
            {
                return;
            }

            _rulersModeWired = true;

            ConfigValue<RulersDisplayMode> entry = NetworkConfig.Rulers;
            if (entry != null)
            {
                entry.RegisterCallback((from, to) =>
                {
                    ConnectionsRenderer.RulersDisplayMode = to;
                });
            }

            ConnectionsRenderer.RulersDisplayMode = NetworkConfig.RulersMode;
        }

        /// <summary>
        /// Applies the configured connection mode to the renderer, once, and keeps it in step after.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Phase 8a's half of the same mechanism <see cref="EnsureRulersModeWiring"/> documents in
        /// full</b> - the reasoning is not repeated here, only the two differences.
        /// </para>
        /// <para>
        /// The first difference is the key: <c>Map/Connections mode</c>, which is new in this phase.
        /// Before it existed the lines button had no config to write, so its only possible writer was
        /// the renderer's own property - which is the divergence this phase exists to close (D39).
        /// </para>
        /// <para>
        /// The second is that the renderer's setter owns a different behaviour on the way into
        /// <c>None</c>: it destroys the lines rather than the rulers. That is the whole reason this
        /// entry is applied through the property and not read at its point of use.
        /// </para>
        /// <para>
        /// The flag is set before the work, the registration is additive, and the boot value comes
        /// from the config's fallback rather than from the setter's field default - all three for the
        /// reasons the ruler-mode method gives.
        /// </para>
        /// </remarks>
        private void EnsureConnectionsModeWiring()
        {
            if (_connectionsModeWired)
            {
                return;
            }

            _connectionsModeWired = true;

            ConfigValue<ConnectionsDisplayMode> entry = NetworkConfig.Connections;
            if (entry != null)
            {
                entry.RegisterCallback((from, to) =>
                {
                    ConnectionsRenderer.ConnectionsDisplayMode = to;
                });
            }

            ConnectionsRenderer.ConnectionsDisplayMode = NetworkConfig.ConnectionsMode;
        }

        /// <summary>
        /// Parses the "Lines" colour/opacity entries once and keeps them in step (U6g).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this wiring exists at all.</b> The two mode settings above are applied through a
        /// renderer property because a mode is live renderer state. The line appearance is not: it is
        /// config-shaped data, and its natural home would be a read at the point of use. But the read
        /// happens once per link per renderer pass, and the entries are strings that must go through
        /// <c>ColorUtility.TryParseHtmlString</c> - so parsing them per link would put a managed
        /// string parse on the hot path. Instead they are parsed here (and again on each change) into
        /// <c>LineAppearance</c>'s per-slot cache, which is what the pass reads.
        /// </para>
        /// <para>
        /// <b>Additive, not exclusive.</b> <c>ConfigValue.RegisterCallback</c> is an event
        /// (<c>Callbacks +=</c>, verified at rank 3 in the pinned ReduxLib source the ruler-mode
        /// method cites), so the callback registered for each key inside <c>LineAppearance</c> does
        /// not replace <c>NetworkConfig</c>'s change log for the same key - an edit still produces
        /// both the log line and the repaint request.
        /// </para>
        /// <para>
        /// <b>Guarded by its own flag, set first,</b> for the reasons the two mode methods give:
        /// <c>EnsureRenderer</c> is retryable, and a second subscription would accumulate one
        /// delegate per retry for the life of the process. The one boot <c>lines-appearance:</c> line
        /// is written by the callee, once.
        /// </para>
        /// </remarks>
        private void EnsureLineAppearanceWiring()
        {
            if (_lineAppearanceWired)
            {
                return;
            }

            _lineAppearanceWired = true;

            LineAppearance.Initialize(LogLine, LogWarningLine);
            LineAppearance.Wire();
        }

        /// <summary>
        /// Builds the window layer once: the factories, the bundle, the toolbar and the tooltip.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>After <see cref="EnsureRenderer"/>, and the order is load-bearing.</b> P7's
        /// <c>RulerGeometry.Resolve</c> opens this mod's UI bundle, takes the sphere mesh out of it and
        /// calls <c>Unload(false)</c> on its own handle. Running the window layer second means P7's
        /// handle is already released by the time this one is opened, so the UI's held handle (D37,
        /// never unloaded) cannot be the handle P7 unloads. Reversing the two would leave the UI
        /// reading a bundle that has been unloaded under it.
        /// </para>
        /// <para>
        /// <b>Not retried.</b> Every failure this can hit - no bundle on disk, a template missing from
        /// it, an empty clone - is a deployment or build fact that a second attempt in the same
        /// process cannot change, and each one is logged by name by the window layer itself. The flag
        /// is set by <c>CommNextUIManager</c> only on a full success, so this method can be called
        /// again safely if a later phase finds a reason to.
        /// </para>
        /// <para>
        /// The mod folder comes from <see cref="ResolveModFolder"/> - the same
        /// <c>SWMetadata.Folder.FullName</c> route P7 uses, because <c>Folder</c> is a
        /// <c>DirectoryInfo</c> field on this pin and not a string.
        /// </para>
        /// </remarks>
        private void EnsureUI()
        {
            if (CommNextUIManager.IsInitialized)
            {
                return;
            }

            CommNextUIManager.Initialize(ResolveModFolder(), LogLine, LogWarningLine, LogErrorLine);
        }

        /// <summary>
        /// The deployed mod's own folder, or <c>null</c> when the loader has not assigned it yet.
        /// </summary>
        /// <returns>The folder's full path, or <c>null</c>.</returns>
        /// <remarks>
        /// <c>SWMetadata.Folder</c> is a public <c>System.IO.DirectoryInfo</c> FIELD on
        /// <c>SpaceWarp2.API.Mods.SpaceWarpPluginDescriptor</c>, not a string - verified with
        /// <c>monodis --fields SpaceWarp2.dll</c> (row 66:
        /// <c>class [mscorlib]System.IO.DirectoryInfo Folder: public initonly</c>). It is therefore
        /// <c>.Folder.FullName</c>, which is what the in-game-validated sibling port does with the
        /// same member; treating it as a string is one of the pinned deltas and does not compile.
        /// <para>
        /// A failure here is not fatal: <see cref="RulerGeometry.Resolve"/> treats a null folder as
        /// "the bundle cannot be looked for" and builds the sphere in code, which logs the branch. The
        /// warning exists so the reason is in the log rather than only implied by that branch line.
        /// </para>
        /// </remarks>
        private string ResolveModFolder()
        {
            try
            {
                SpaceWarpPluginDescriptor metadata = SWMetadata;
                if (metadata == null || metadata.Folder == null)
                {
                    LogWarningLine("the mod folder is not assigned yet (SWMetadata.Folder is null), so "
                        + "the legacy ruler mesh cannot be read out of the bundle; the code-built "
                        + "sphere is used instead (see the ruler-geometry line)");
                    return null;
                }

                return metadata.Folder.FullName;
            }
            catch (Exception exception)
            {
                LogWarningLine("reading the mod folder threw (" + exception.GetType().Name + ": "
                    + exception.Message + "); the code-built ruler sphere is used instead (see the "
                    + "ruler-geometry line)");
                return null;
            }
        }

        /// <summary>
        /// Installs the Harmony patches once.
        /// </summary>
        /// <remarks>
        /// <c>KerbalMod.CreateHarmonyAndPatchAll()</c> is the base type's own helper and is the route
        /// the sibling port proved in game; the repo's other mods create a <c>Harmony</c> instance by
        /// hand. Both work on 0.2.8.5 - the base helper is used here because it is the one that has
        /// been demonstrated on this exact loader, and it keeps this class free of a Harmony instance
        /// it would otherwise have to own and dispose.
        /// </remarks>
        private void EnsureHarmony()
        {
            if (_harmonyInstalled)
            {
                return;
            }

            // F40. Harmony's PatchClassProcessor.ReportException LOGS AND RETHROWS, so ONE failing patch
            // class aborts PatchAll for every class ordered after it in the assembly's type order. The
            // first version of this method logged "Harmony registered: ..." unconditionally AFTER the
            // call - a claim, not a check. In the run that exposed F39 that line did not print at all,
            // and the only trace of the failure was a HarmonyException that nothing was grepping for.
            //
            // The honest signal is the absence of an exception: with none, every patch class that
            // matched a real method was applied. Say exactly that, and make the failure unmistakable.
            try
            {
                CreateHarmonyAndPatchAll();
            }
            catch (Exception ex)
            {
                LogLine("HARMONY PATCH FAILURE - this mod is only PARTIALLY patched. "
                    + ex.GetType().Name + ": " + ex.Message);
                LogLine("A failing patch class aborts PatchAll for every class ordered after it, so "
                    + "patches later in this assembly are missing too. Do not trust ANY evidence from "
                    + "this session. The loader logs the full stack trace separately.");
                throw;
            }

            _harmonyInstalled = true;
            LogLine("Harmony patch pass OK (no failing class): ConnectionGraph.RebuildConnectionGraph "
                + "(prefix), ConnectionGraph.OnUpdate (prefix + postfix - the generation-aligned vanilla "
                + "capture), CommNetManager.SetSourceNode(ConnectionGraphNode) (postfix) and "
                + "PersistentProfileManager.get_ShowCommNetLines (postfix - the stock green line "
                + "override, D-L21-1)");
        }

        /// <summary>
        /// Registers this mod's configuration section exactly once, so it appears under
        /// <c>Settings -&gt; Mods</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="SettingsMenu.RegisterConfigFile"/> is pinned, and it is the only member this
        /// port calls from <c>SpaceWarp2.UI</c>:
        /// </para>
        /// <code>
        /// M:SpaceWarp2.UI.API.Settings.SettingsMenu.RegisterConfigFile(System.String,ReduxLib.Configuration.IConfigFile)
        /// </code>
        /// <para>
        /// The logged call is not a formality: on the sibling port the same call was written as a
        /// defensive fallback, was measured to be **load-bearing**, and is the reason the section
        /// appears at all. It stays unconditional here for the same reason.
        /// </para>
        /// <para>
        /// The section name is the display name and the config file is the loader-assigned
        /// <c>SWConfiguration</c>. Both are the shape the sibling port validated in game; the
        /// alternative - constructing an <c>IConfigFile</c> here - would have to guess the file's
        /// name and directory, and <c>IConfigFile</c> is an interface whose implementation lives in
        /// the loader.
        /// </para>
        /// </remarks>
        private void EnsureSettingsRegistration()
        {
            if (_settingsRegistered)
            {
                return;
            }

            IConfigFile configFile = SWConfiguration;
            if (configFile == null)
            {
                LogWarningLine("settings registration skipped: SWConfiguration is not assigned");
                return;
            }

            SettingsMenu.RegisterConfigFile(SettingsSectionName, configFile);
            _settingsRegistered = true;
            LogLine("settings registration: RegisterConfigFile(\"" + SettingsSectionName
                + "\", SWConfiguration) - this is the route the section appears in "
                + "Settings -> Mods through");
        }

        /// <summary>
        /// Writes an informational line through the loader's logger.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why <c>LogInfo</c> and not <c>LogDebug</c>.</b> The filter is a plain numeric compare,
        /// <c>if (level &gt; _provider.CurrentFilterLevel) return;</c>, with
        /// <c>CurrentFilterLevel = LogLevel.Info</c> by default. The levels are
        /// <c>Message = 8</c> and <c>Info = 16</c>, so <c>Info</c> passes and <c>Debug = 32</c> does
        /// not. A <c>LogDebug</c> line therefore never reaches <c>Ksp2.log</c> - file and Unity-log
        /// mirror alike - which makes a missing line ambiguous between "branch not reached",
        /// "level filtered" and "call threw". Every line a post-launch grep must find is written at
        /// <c>Info</c>.
        /// </para>
        /// <para>
        /// The logger is read into a local and null-checked, because the loader assigns
        /// <c>SWLogger</c> after <c>Update()</c> can already be ticking: an unguarded call is a
        /// <c>NullReferenceException</c> inside the load sequence, which reads in the log as "the
        /// mod is dead" rather than as a logging bug. Dropping one line is always cheaper than
        /// dropping the load.
        /// </para>
        /// </remarks>
        public void LogLine(string message)
        {
            ILogger logger = SWLogger;
            if (logger == null)
            {
                return;
            }

            logger.LogInfo(message);
        }

        /// <summary>Writes a warning through the loader's logger, with the same guard as <see cref="LogLine"/>.</summary>
        // NoInlining is not used: this is called once per pass at most, and the guard above is the point.
        public void LogWarningLine(string message)
        {
            ILogger logger = SWLogger;
            if (logger == null)
            {
                return;
            }

            logger.LogWarning(message);
        }

        /// <summary>Writes an error through the loader's logger, with the same guard as <see cref="LogLine"/>.</summary>
        /// <remarks>
        /// Reserved for conditions the user must act on - a failed deploy-side registration - rather
        /// than for anything the engine recovers from. An <c>Error</c> logged inside a <c>catch</c> in
        /// the boot path is exactly the double-fault this codebase has already been bitten by, which is
        /// why the callers here check their logger first.
        /// </remarks>
        public void LogErrorLine(string message)
        {
            ILogger logger = SWLogger;
            if (logger == null)
            {
                return;
            }

            logger.LogError(message);
        }
    }
}
