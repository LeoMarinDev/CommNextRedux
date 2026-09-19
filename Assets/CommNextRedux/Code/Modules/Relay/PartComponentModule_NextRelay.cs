// CommNextRedux - the relay's part component module.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Relay/PartComponentModule_NextRelay.cs
//
//   Carried over: `PartBehaviourModuleType`, the `DataTransmitter` link, the two lookups in
//   `OnStart` (`TryGetByType<Data_NextRelay>` and
//   `TryGetModuleData<PartComponentModule_DataTransmitter, Data_Transmitter>`), `OnUpdate`, and the
//   whole resource loop - `ResourceConsumptionUpdate`, its `_hasOutstandingRequest` /
//   `_returnedRequestResolutionState` bookkeeping, and the two private fields behind them.
//
// THE LOOP IS PORTED, ON THE GAME'S OWN REQUEST MACHINERY (Phase 5, and this is route 2)
//
//   `Data_NextRelay.SetupResourceRequest` allocates the request on this module's
//   `resourceFlowRequestBroker`, and this module is what drives it: activate while `EnableRelay` is
//   on, post the rate as a command every tick, read `WasLastTickDeliveryAccepted` back into
//   `HasResourcesToOperate`. That is the legacy's loop, on the supported `ModuleData`
//   request path - the in-repo OrbitalSurvey port
//   (`PartComponentModule_OrbitalSurvey.ResourceConsumptionUpdate`) runs the same shape, and a stock
//   module (`Data_Mine`) overrides the same `SetupResourceRequest`.
//
//   AN EARLIER ROUTE IS REFUTED AND MUST NOT RETURN: handing the cost to the stock transmitter
//   (writing `Data_Transmitter.RequiredResources` in Lua, reading
//   `PartComponentModule_DataTransmitter.IsTransmitterActive()` back as "power"). The first is a
//   DEPLOYMENT check - `if (!_requiresDeployment) return true; return _dataDeployable.IsExtended;` -
//   and the stock transmitter's own `OnUpdate` skips its resource block unless
//   `IsTransmitting.GetValue()`, i.e. unless a science report is in flight. That route measured a
//   folded dish instead of a battery and drew nothing while relaying. The full refutation, with the
//   IL, is in `Data_NextRelay.cs`'s header; no part of this mod's power path touches either member.
//
// WHAT STAYS BEHIND: `RefreshCommNetIfNecessary`. The legacy refreshed its own CommNet node whenever
//   `HasResourcesToOperate` flipped. This port does not need it: the node state is rebuilt by
//   `NetworkEngine.CollectNodeStates` on every graph pass (roughly every three seconds), and the
//   relay/band gate reads the flag from there.
//
//   That is a statement about the ENGINE's copy of the flag, and it is only as fresh as the last
//   tick that wrote it - which is the L22 defect. On a vessel that is not the controlled one the
//   game does not fixed-update the part owner unless the module is registered for background
//   resource processing, so this class's `ResourceConsumptionUpdate` never runs there, the flag
//   freezes at whatever its last tick left, and the engine faithfully rebuilds a graph from a stale
//   `false`. The registration is in `CommNextReduxPlugin.EnsureBackgroundResourceProcessing`; the
//   evidence is `Deploy/obj/u06e-l22-network.md`.

using System;
using System.Globalization;
using CommNextRedux.Network;
using KSP.Game;
using KSP.Modules;
using KSP.Sim.impl;
using KSP.Sim.ResourceSystem;
using ReduxLib.Logging;

namespace CommNextRedux.Modules.Relay
{
    /// <summary>
    /// Owns <see cref="Module_NextRelay"/> on a live part.
    /// </summary>
    public class PartComponentModule_NextRelay : PartComponentModule
    {
        /// <summary>
        /// Named so that every line this mod logs is reachable with a single grep for
        /// <c>[CommNextRedux]</c>. See `Deploy/obj/PORT-PROGRESS.md` for the log contract.
        /// </summary>
        private static readonly ILogger Logger = ReduxLib.ReduxLib.GetLogger("CommNextRedux");

        /// <summary>The behaviour module that owns this component module.</summary>
        public override Type PartBehaviourModuleType => typeof(Module_NextRelay);

        /// <summary>
        /// The transmitter this relay relays through. Linked by <see cref="OnStart"/>.
        /// </summary>
        public Data_Transmitter DataTransmitter { get; private set; }

        private Data_NextRelay _dataRelay;

        // -----------------------------------------------------------------------------------------
        // EC management, the legacy's own fields (Phase 5, route 2)
        // -----------------------------------------------------------------------------------------

        /// <summary>The broker's answer to the last command this module posted.</summary>
        private FlowRequestResolutionState _returnedRequestResolutionState;

        /// <summary>Whether a command was posted and its verdict not yet read back.</summary>
        private bool _hasOutstandingRequest;

        /// <summary>The request-active state last written to the log, so only transitions log.</summary>
        private bool _loggedRequestActive;

        /// <summary>
        /// The relay's own <c>HasResourcesToOperate</c> as last logged. Starts <c>true</c>, which is
        /// the field's own default: a relay that has never starved logs nothing.
        /// </summary>
        private bool _loggedHasResources = true;

        /// <summary>Whether <see cref="OnStart"/> registered this module for the game's updates.</summary>
        /// <remarks>
        /// Tracked so <see cref="OnShutdown"/> cannot unregister a module that never registered - the
        /// early return in <see cref="OnStart"/> (no data module on this part) leaves it false.
        /// </remarks>
        private bool _registeredForUpdates;

        /// <summary>
        /// Resolves the data and the companion transmitter, reports both, and allocates the relay's
        /// resource request.
        /// </summary>
        /// <param name="universalTime">
        /// Unused, as in the legacy: the request is level-driven, so nothing here needs the clock.
        /// </param>
        public override void OnStart(double universalTime)
        {
            if (!DataModules.TryGetByType<Data_NextRelay>(out _dataRelay))
            {
                Logger.LogError("Unable to find a Data_NextRelay in the PartComponentModule for "
                                + (Part != null ? Part.PartName : "<no part>"));
                return;
            }

            Data_Transmitter dataTransmitter = null;
            bool hasTransmitter = Part != null
                                  && Part.TryGetModuleData<PartComponentModule_DataTransmitter,
                                      Data_Transmitter>(out dataTransmitter);
            DataTransmitter = dataTransmitter;

            // The EC rate is formatted with the invariant culture on purpose: this line is read
            // back by a script, and a comma decimal separator would break the parse on exactly
            // the machines whose locale uses one.
            //
            // `range=` is the read-back of the Lua range rebalance: it reports the transmitter's
            // own `CommunicationRange` after the patch manager has finished with the part
            // definition, so a patch that silently matched nothing shows up here as the stock
            // range rather than as a missing line. Resolved member:
            // `KSP.Modules.Data_Transmitter.CommunicationRange`, a `float64` field (monodis
            // --fields on Assembly-CSharp.dll, flist 43755) - not a ModuleProperty, so there is
            // no GetValue() here.
            Logger.LogInfo("relay-module part='" + (Part != null ? Part.PartName : "<no part>")
                           + "' ec=" + _dataRelay.RequiredResource.Rate.ToString("0.###", CultureInfo.InvariantCulture)
                           + "/" + _dataRelay.RequiredResource.ResourceName
                           + " enableRelay=" + _dataRelay.EnableRelay.GetValue()
                           + " transmitter=" + hasTransmitter
                           + " range=" + (hasTransmitter
                               ? dataTransmitter.CommunicationRange.ToString("0.###", CultureInfo.InvariantCulture)
                               : "<none>"));

            // Allocate the request. Whether it is ever activated is decided per tick, so that the
            // `RelaysRequirePower` setting and the relay's own switch are honoured at runtime - the
            // legacy's own ordering (its comment: "This will be applied only if the
            // `RelayRequiresPower` setting is true").
            _dataRelay.SetupResourceRequest(resourceFlowRequestBroker);

            // `OnUpdate` is the game's, but it only ticks a component module the part knows about.
            // OrbitalSurvey's module registers itself here for exactly this reason, and unregisters
            // in `OnShutdown`; without it this module would never be called and the relay would draw
            // nothing at all - the failure mode this phase exists to fix.
            if (Part != null)
            {
                Part.RegisterModuleForUpdates(this);
                _registeredForUpdates = true;
            }
        }

        /// <summary>
        /// The game's per-tick hook: posts this relay's resource command and reads the verdict back.
        /// </summary>
        /// <param name="universalTime">Unused: the request is level-driven, not integrated.</param>
        /// <param name="deltaUniversalTime">Passed on to the loop, which does not need it either.</param>
        public override void OnUpdate(double universalTime, double deltaUniversalTime)
        {
            ResourceConsumptionUpdate(deltaUniversalTime);
        }

        /// <summary>
        /// Takes this module off the part's update list, so a torn-down part cannot be ticked.
        /// </summary>
        public override void OnShutdown()
        {
            if (_registeredForUpdates && Part != null)
            {
                Part.UnregisterModuleForUpdates(this);
                _registeredForUpdates = false;
            }
        }

        /// <summary>
        /// Keeps the relay's resource request aligned with the relay's state, once per tick.
        /// </summary>
        /// <param name="deltaTime">
        /// Unused, exactly as in the OrbitalSurvey precedent: the broker does the accounting, and
        /// this method only stands the request up or down and posts the current rate.
        /// </param>
        /// <remarks>
        /// <para>
        /// The three branches are the legacy's, in its order: the no-cost case, the read-back of the
        /// previous command's verdict, then the request aligned to <c>EnableRelay</c> plus the
        /// command for this tick.
        /// </para>
        /// <para>
        /// <b>Both state families log their transitions, and only their transitions.</b> The L5
        /// launch gate ("the relay EC draw starts and stops") has to be visible in the log without a
        /// per-tick flood, so a line is written when the request goes active or inactive, and when
        /// <c>HasResourcesToOperate</c> flips - never otherwise. Every line carries the
        /// <c>relay-power</c> token and the mod's own log prefix.
        /// </para>
        /// </remarks>
        private void ResourceConsumptionUpdate(double deltaTime)
        {
            ResourceFlowRequestBroker broker = resourceFlowRequestBroker;
            if (broker == null || _dataRelay == null)
            {
                return;
            }

            bool relayEnabled = _dataRelay.EnableRelay.GetValue();
            bool chargeForPower = NetworkConfig.RelaysRequirePowerEnabled && !InfinitePowerEnabled();

            // A part whose `SetupResourceRequest` could not build a request (it logged why: no broker,
            // an empty `RequiredResource`, or a resource name the game does not know) has no entity to
            // activate, no command to post and no verdict to read. It is treated as the no-cost case
            // rather than crashing on a null config every tick, and rather than reporting a starvation
            // that is really a definition problem - the same "never silently sever" rule the
            // node-state pass follows. `RequestEntity` is only ever handed to the broker when this is
            // true, so an unallocated entity (`Entity.Null`, the request that was never built) never
            // reaches it.
            bool hasRequest = _dataRelay.RequestConfig != null;
            string reason;

            if (!chargeForPower || !hasRequest)
            {
                // 1. The relay must not be charged: the player turned the requirement off, the save
                //    carries the campaign's InfinitePower option, or there is no request to charge.
                //    Force "has resources", stand any live request down and skip the read-back
                //    entirely - the legacy's first branch, extended by the no-request case.
                //
                //    `_hasOutstandingRequest` is cleared rather than left standing so the first
                //    charged tick cannot read a stale verdict off a request that was inactive.
                reason = !hasRequest
                    ? "no-request"
                    : (NetworkConfig.RelaysRequirePowerEnabled ? "infinite-power" : "relays-not-required");
                _dataRelay.HasResourcesToOperate = true;
                _hasOutstandingRequest = false;
                if (hasRequest && broker.IsRequestActive(_dataRelay.RequestEntity))
                {
                    broker.SetRequestInactive(_dataRelay.RequestEntity);
                }
            }
            else
            {
                // 2. Read the previous tick's verdict back. This is the whole of the power state: the
                //    game's own answer to "did this request actually get its EC".
                if (_hasOutstandingRequest)
                {
                    _returnedRequestResolutionState =
                        broker.GetRequestState(_dataRelay.RequestEntity);
                    _dataRelay.HasResourcesToOperate =
                        _returnedRequestResolutionState.WasLastTickDeliveryAccepted;
                }

                _hasOutstandingRequest = false;

                // 3. Align the request with the relay's own switch, then post this tick's command.
                if (!relayEnabled)
                {
                    reason = "relay-disabled";
                    if (broker.IsRequestActive(_dataRelay.RequestEntity))
                    {
                        broker.SetRequestInactive(_dataRelay.RequestEntity);
                        _dataRelay.HasResourcesToOperate = false;
                    }
                }
                else
                {
                    reason = "relay-enabled";
                    if (broker.IsRequestInactive(_dataRelay.RequestEntity))
                    {
                        broker.SetRequestActive(_dataRelay.RequestEntity);
                    }

                    // The command array is built per call rather than cached, and the config inside it
                    // is re-read from the data every tick: the OrbitalSurvey precedent, and the reason
                    // it is the safe shape is that nothing promises `SetupResourceRequest` runs only
                    // once - a cached buffer holding a superseded config object would post a stale
                    // `FlowUnits` and the relay would quietly draw the wrong amount.
                    _dataRelay.RequestConfig.FlowUnits = (double)_dataRelay.RequiredResource.Rate;
                    broker.SetCommands(
                        _dataRelay.RequestEntity,
                        1.0,
                        new ResourceFlowRequestCommandConfig[] { _dataRelay.RequestConfig });
                    _hasOutstandingRequest = true;
                }
            }

            // One place for both transition families, so a tick that changes nothing logs nothing.
            bool requestActive = hasRequest && broker.IsRequestActive(_dataRelay.RequestEntity);
            if (requestActive != _loggedRequestActive)
            {
                _loggedRequestActive = requestActive;
                LogPowerTransition("request", requestActive ? "active" : "inactive", reason);
            }

            if (_loggedHasResources != _dataRelay.HasResourcesToOperate)
            {
                _loggedHasResources = _dataRelay.HasResourcesToOperate;
                LogPowerTransition("hasResources", _loggedHasResources ? "true" : "false", reason);
            }
        }

        /// <summary>
        /// Writes one power-transition line: the positive marker the L5 launch gate greps for.
        /// </summary>
        /// <param name="what">The field that changed - <c>request</c> or <c>hasResources</c>.</param>
        /// <param name="value">Its new value.</param>
        /// <param name="reason">Why this tick put it there.</param>
        private void LogPowerTransition(string what, string value, string reason)
        {
            Logger.LogInfo("relay-power part='" + PartName + "' " + what + "=" + value
                           + " reason=" + reason
                           + " hasResources=" + _dataRelay.HasResourcesToOperate
                           + " ec=" + _dataRelay.RequiredResource.Rate.ToString(
                               "0.###", CultureInfo.InvariantCulture)
                           + "/" + _dataRelay.RequiredResource.ResourceName);
        }

        /// <summary>This part's name, or a placeholder before the part is bound.</summary>
        private string PartName
        {
            get { return Part != null ? Part.PartName : "<no part>"; }
        }

        /// <summary>
        /// Whether the save carries the campaign's infinite-electricity difficulty option.
        /// </summary>
        /// <returns><c>true</c> when power must not gate this relay.</returns>
        /// <remarks>
        /// The legacy asked a helper that does not exist on 0.2.8.5. The campaign-level option is
        /// `GameManager.Instance.Game.SessionManager.IsDifficultyOptionEnabled("InfinitePower")`,
        /// the same check the in-repo OrbitalSurvey port makes and the same one
        /// <c>NetworkEngine</c> makes before it enforces the gate - so a sandbox save can never be
        /// starved by a setting whose effect the player cannot see. (The cheat menu has its own
        /// toggle, <c>GameInstance.CheatSystem.GetInfiniteElectricity()</c>; the difficulty option is
        /// preferred because it is the campaign-level statement and it has the in-repo precedent.)
        /// </remarks>
        private static bool InfinitePowerEnabled()
        {
            try
            {
                GameInstance game = GameManager.Instance != null ? GameManager.Instance.Game : null;
                SessionManager session = game != null ? game.SessionManager : null;
                return session != null
                       && session.IsDifficultyOptionEnabled(NetworkEngine.InfinitePowerOptionId);
            }
            catch (Exception)
            {
                // Mid-load, or no session yet. "Power matters" is the safe reading: it is the shipped
                // default and the one the relay mechanic is defined by.
                return false;
            }
        }
    }
}
