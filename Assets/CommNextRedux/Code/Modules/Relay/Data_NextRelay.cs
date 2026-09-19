// CommNextRedux - the relay's module data.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Modules/Relay/Data_NextRelay.cs
//
//   Carried over: `EnableRelay` (the `[KSPState] ModuleProperty<bool>`, default `true`),
//   `RequiredResource` (the `[KSPDefinition] PartModuleResourceSetting` the Lua patches fill in),
//   `HasResourcesToOperate`, `RequestConfig`, the `SetupResourceRequest` override, `ModuleType`,
//   the `Copy` override, and the relay-description part-info entry.
//
// THE RELAY'S EC DRAW IS THIS MODULE'S OWN REQUEST (Phase 5, and this is route 2)
//
//   The legacy owned a resource loop, and so does this port: `SetupResourceRequest` allocates a
//   request on the part's own broker, `PartComponentModule_NextRelay.OnUpdate` activates it while
//   `EnableRelay` is on and reads the game's delivery verdict back into `HasResourcesToOperate`,
//   and `NetworkEngine`'s node-state pass gates the node on that flag. This is the game's supported
//   machinery for ANY `ModuleData` - `KSP.Sim.Definitions.ModuleData.SetupResourceRequest(
//   ResourceFlowRequestBroker)` is the public virtual (mlist 52739 on the staged runtime), and both
//   a stock module (`Redux.Modules.Data_Mine`, mlist 20419) and the in-repo OrbitalSurvey port
//   override it.
//
//   AT 0.2.9.0.104521 THE REQUEST IS KEYED BY A `Unity.Entities.Entity`, NOT BY A STRING TAG.
//   `ModuleData` declares `Entity` and `RequestEntity` (`[Unity.Entities]Unity.Entities.Entity`,
//   mlist 52723-52726); the stock `Redux.Modules.Data_Mine.SetupResourceRequest` reads
//   `get_RequestEntity()`, calls `ResourceFlowRequestBroker.AllocateOrGetRequest(Entity, ushort)`,
//   stores the result back with `set_RequestEntity(...)`, then posts with
//   `SetCommands(Entity, double, ResourceFlowRequestCommandConfig[])` - the IL of the shipped
//   `Assembly-CSharp.dll` (IL_0180-IL_01a6). `ResourceFlowRequestHandle` and the
//   `AllocateOrGetRequest(string, ResourceFlowRequestHandle)` overload do not exist at this pin;
//   code that names either is previous-pin source.
//
//   THE ROUTE THAT WAS TRIED FIRST, AND WHY IT CANNOT COME BACK. An earlier revision of this phase
//   tried to hand the relay's cost to the STOCK transmitter instead: the Lua wrote the relay rate
//   into `Data_Transmitter.RequiredResources` and the gate read
//   `PartComponentModule_DataTransmitter.IsTransmitterActive()` as the power signal. Both halves
//   are refuted by the shipped IL, and a later reader must not walk this path again:
//
//     * `IsTransmitterActive()` is a DEPLOYMENT check, not a power check. Its whole body is
//       `if (!_requiresDeployment) return true; return _dataDeployable.IsExtended;` - so a folded
//       dish would have read as "no power" and an unfolded relay as "powered", whatever the battery
//       said.
//     * `PartComponentModule_DataTransmitter.OnUpdate` returns before its resource block unless
//       `IsTransmitting.GetValue()` - i.e. the stock list is drawn only while a SCIENCE report is
//       in flight, never while merely relaying. A relay on the pad would have cost nothing at all.
//     * The same edit silently repriced the antenna's science transmission to the relay rate
//       (ra-15: 25.0 -> 1.0 EC/s), because it rewrote the stock list the science draw reads.
//
//   So `Data_Transmitter.RequiredResources` is NOT touched anywhere in this port, no part of the
//   power path calls `IsTransmitterActive()`, and the request below is the only draw.
//
//   `RequiredResource` is this mod's own declaration and the only place the relay's rate is
//   written: one constant in the Lua table feeds it, and `PartComponentModule_NextRelay` reads it
//   back both into the log marker and into the request's `FlowUnits`, so the declared rate and the
//   drawn rate cannot drift apart.
//
//   ON-RAILS ACCOUNTING does NOT come free with this route, and this port learned that in L22.
//   The request is serviced by the game's own resource system through the part's broker, but the
//   broker is only pumped for a part owner the game fixed-updates, and `PartOwnerComponent::OnFixedUpdate`
//   skips `ResourceFlowRequestManager::UpdateFlowRequests` for an on-rails `Orbital` vessel that is
//   not being controlled (shipped IL, `IL_003c`-`IL_00a9`: the call at `IL_009c` is reached only via
//   `HasRegisteredPartComponentsForFixedUpdate`, `IsActiveVessel`, `Physics == AtRest`,
//   `Physics == RigidBody`, or `Orbital` under thrust). A relay on a background vessel then keeps
//   whatever `HasResourcesToOperate` its last tick left - measured in L22: three relays sat at
//   `hasResources=false` with a healthy `ec=0.5` until their vessel was taken as the controlled one,
//   and the engine dropped one more edge per unticked relay. The module is registered for background
//   resource processing in `CommNextReduxPlugin.EnsureBackgroundResourceProcessing`
//   (`RegisterModuleForBackgroundResourceProcessing<PartComponentModule_NextRelay>()`), which is what
//   the legacy did and what sets `HasRegisteredPartComponentsForFixedUpdate` when the part is added.
//   The registration is unconditional - gating it on the relays-require-power key is what made the
//   legacy's setting load-bound, and with the key off it is also what clears a stale `false`.
//   Evidence: `Deploy/obj/u06e-l22-network.md`.
//
//   The legacy's second part-info entry (the resource line) is still omitted, for the narrower
//   reason it was omitted before: every part the patches touch declares a rate, so the row would be
//   unconditional, and an OAB row that always says the same thing is noise rather than information.
//   The rate is visible in the part definition and in the relay-module log marker.

using System;
using System.Collections.Generic;
using CommNextRedux.UI;
using CommNextRedux.Utilities;
using I2.Loc;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.Definitions;
using KSP.Sim.ResourceSystem;
using UnityEngine;

namespace CommNextRedux.Modules.Relay
{
    /// <summary>
    /// Serialised data for <see cref="Module_NextRelay"/>: whether the relay is on, what it costs to
    /// run, and the request that actually pays that cost.
    /// </summary>
    [Serializable]
    public class Data_NextRelay : ModuleData
    {
        /// <summary>
        /// The mod's logger, under the one prefix every line of this port is greppable by.
        /// </summary>
        /// <remarks>
        /// Fully qualified on purpose: this file also imports <c>UnityEngine</c> for
        /// <see cref="TooltipAttribute"/>, and a bare <c>ILogger</c> would then be ambiguous between
        /// the mod's logger and Unity's.
        /// </remarks>
        private static readonly ReduxLib.Logging.ILogger Logger =
            ReduxLib.ReduxLib.GetLogger("CommNextRedux");

        /// <summary>The behaviour module this data belongs to.</summary>
        public override Type ModuleType => typeof(Module_NextRelay);

        /// <summary>Whether this part acts as a relay. On by default, as in the legacy.</summary>
        [KSPState]
        [LocalizedField(LocalizedStrings.EnableRelayKey)]
        [PAMDisplayControl(SortIndex = 0)]
        public ModuleProperty<bool> EnableRelay = new ModuleProperty<bool>(true);

        /// <summary>
        /// True when <see cref="EnableRelay"/> carries the <c>ContextKey</c> its PAM row binds
        /// through. False means this instance's data context was never built.
        /// </summary>
        internal bool HasRelayContextKeys
        {
            get { return ModuleDataContextGuard.AllKeyed(EnableRelay); }
        }

        /// <summary>
        /// Makes sure this instance's data context exists before the relay's PAM row is used, and
        /// reports whether it does.
        /// </summary>
        /// <param name="phase">Where the caller is, for the guard's log line.</param>
        /// <param name="detail">The caller's provenance string, logged verbatim.</param>
        /// <remarks>
        /// <para>
        /// <b>The same defect class as the modulator's D-L20-1 (U6b sweep), applied for parity.</b>
        /// <c>Data_NextRelay</c> is registered by the identical game path -
        /// <c>Module_NextRelay.AddDataModules</c> - so it can be handed an instance whose
        /// <c>PrepareDataContext</c> pass never completed, exactly as <c>Data_NextModulator</c> was
        /// at L20. Nothing in this class <i>throws</i> on an unkeyed property (the relay calls no
        /// <c>SetDropdownData</c>/<c>AddProperty</c>), so the symptom would be silent rather than
        /// fatal: a <c>EnableRelay</c> row the PAM cannot bind, because
        /// <c>ModuleData.SetVisible</c> and the PAM's own binder both resolve the property through
        /// <c>_propertyContextLookup</c> by that key. Silent is the harder failure to see, so the
        /// repair is applied here too.
        /// </para>
        /// <para>
        /// A no-op whenever the context is healthy: the check is a null/empty test on one string,
        /// and <c>RebuildDataContext()</c> is only reached when it fails.
        /// </para>
        /// </remarks>
        internal bool EnsureRelayContextPrepared(string phase, string detail)
        {
            return ModuleDataContextGuard.EnsurePrepared(this, Logger, phase, detail, EnableRelay);
        }

        /// <summary>
        /// The resource this relay draws while enabled, written per part by the Lua patches.
        /// </summary>
        /// <remarks>
        /// Shape and defaults are taken from the stock data classes and from the live OrbitalSurvey
        /// port, which declares the identical field and ships it:
        /// <c>{"Rate": 0.5, "ResourceName": "ElectricCharge", "AcceptanceThreshold": 0.1}</c>
        /// (<c>$KSP2_ROOT/pm_cache/antenna_0v_dish_ra-2</c>).
        /// </remarks>
        [KSPDefinition]
        [Tooltip("Resource required to operate this module if it consumes resources")]
        public PartModuleResourceSetting RequiredResource;

        /// <summary>
        /// Whether the last resource tick delivered what this relay asked for. The power half of the
        /// relay/band gate, and this mod's own state rather than the game's.
        /// </summary>
        /// <remarks>
        /// Defaults to <c>true</c> - before the first tick there is no evidence of starvation, and
        /// the legacy's field defaulted the same way. Read by
        /// <c>NetworkEngine.CollectNodeStates</c>, written by
        /// <c>PartComponentModule_NextRelay.ResourceConsumptionUpdate</c>.
        /// </remarks>
        public bool HasResourcesToOperate = true;

        /// <summary>
        /// The command this relay's request posts: which resource, which direction, how much per
        /// second. Rebuilt and re-posted by the component module whenever the relay is running.
        /// </summary>
        public ResourceFlowRequestCommandConfig RequestConfig;

        /// <summary>
        /// Adds the relay's one-line description to the part-info window.
        /// </summary>
        public override List<OABPartData.PartInfoModuleEntry> GetPartInfoEntries(
            Type partBehaviourModuleType,
            List<OABPartData.PartInfoModuleEntry> delegateList)
        {
            if (partBehaviourModuleType != ModuleType) return delegateList;

            delegateList.Add(new OABPartData.PartInfoModuleEntry(
                "",
                s => LocalizationManager.GetTranslation(LocalizedStrings.RelayDescription)));

            return delegateList;
        }

        /// <summary>
        /// Allocates this relay's resource request on the part's own broker.
        /// </summary>
        /// <param name="resourceFlowRequestBroker">The broker the part hands to every module.</param>
        /// <remarks>
        /// <para>
        /// Called once from <c>PartComponentModule_NextRelay.OnStart</c> with the module's own
        /// <c>resourceFlowRequestBroker</c>, exactly as the legacy called it. The request is only
        /// ALLOCATED here; whether it is ever activated is decided per tick, so that
        /// <c>RelaysRequirePower</c> and the <c>EnableRelay</c> switch stay live settings rather than
        /// part-load decisions.
        /// </para>
        /// <para>
        /// The request's key is the inherited <see cref="ModuleData.RequestEntity"/> - the base
        /// class's own entity, so a second call with the already-allocated entity returns the same
        /// request rather than a second one, and a part copy carries its key with it. This is the
        /// game's own shape and not the port's invention: <c>Redux.Modules.Data_Mine</c> reads
        /// <c>get_RequestEntity()</c>, stores the <c>AllocateOrGetRequest</c> result back, and posts
        /// with <c>SetCommands(Entity, ...)</c> (IL of the shipped runtime). The body follows the
        /// in-repo OrbitalSurvey port's override (<c>Data_OrbitalSurvey.SetupResourceRequest</c>),
        /// including the invalid-resource guard - a part whose resource name resolves to nothing
        /// gets a log line instead of a request that can never be satisfied.
        /// </para>
        /// </remarks>
        public override void SetupResourceRequest(ResourceFlowRequestBroker resourceFlowRequestBroker)
        {
            if (resourceFlowRequestBroker == null)
            {
                Logger.LogError("relay-module: no resource flow broker was handed over, so this "
                                + "relay has no EC request to run");
                return;
            }

            if (string.IsNullOrEmpty(RequiredResource.ResourceName))
            {
                // The Lua patches are the only writer of this field, so an empty name means no patch
                // matched this part. One line, because the alternative is a silent no-cost relay.
                Logger.LogError("relay-module: RequiredResource is empty - no patch filled in this "
                                + "part's relay cost, so there is no request to build");
                return;
            }

            ResourceDefinitionID resourceID =
                GameManager.Instance.Game.ResourceDefinitionDatabase.GetResourceIDFromName(
                    RequiredResource.ResourceName);
            if (resourceID == ResourceDefinitionID.InvalidID)
            {
                Logger.LogError("relay-module: there is no resource named "
                                + RequiredResource.ResourceName
                                + ", so this relay has no EC request to run");
                return;
            }

            RequestConfig = new ResourceFlowRequestCommandConfig();
            RequestConfig.FlowResource = resourceID;
            RequestConfig.FlowDirection = FlowDirection.FLOW_OUTBOUND;
            RequestConfig.FlowUnits = 0.0;
            RequestEntity = resourceFlowRequestBroker.AllocateOrGetRequest(RequestEntity);
            resourceFlowRequestBroker.SetCommands(
                RequestEntity,
                1.0,
                new ResourceFlowRequestCommandConfig[] { RequestConfig });
        }

        /// <summary>Keeps a symmetry partner's relay state in step with this one.</summary>
        /// <remarks>Legacy: copied <c>EnableRelay</c> only, and so does this.</remarks>
        public override void Copy(ModuleData sourceModuleData)
        {
            var dataRelay = sourceModuleData as Data_NextRelay;
            if (dataRelay == null) return;

            EnableRelay.SetValue(dataRelay.EnableRelay.GetValue());
        }
    }
}
