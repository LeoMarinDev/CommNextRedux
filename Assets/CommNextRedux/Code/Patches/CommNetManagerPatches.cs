// CommNextRedux - the KSC range override and origin move.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Patches/CommNetManagerPatches.cs, the SetSourceNode postfix
//   (and only that one - see "what was dropped" below).
//
// WHAT IT DOES, AND WHY IT IS TWO THINGS
//   The KSC's CommNet node is a simulation object named `kerbin_CommNetOrigin`, and without this patch
//   it sits at the CENTRE of Kerbin with the range the game gives it. Both halves are wrong for a
//   player: the range should be measured from the launch site, and the range itself should be the
//   setting the user chose. So the patch moves the origin's position onto `kerbin_KSC_Object` and then
//   sets the node's MaxRange from `Network -> KSC range`.
//
//   Both name keys are the game's own, and both were measured in the installed data rather than
//   assumed: `kerbin_CommNetOrigin` and `kerbin_KSC_Object` each appear in the shipped Addressables
//   bundles under $KSP2_ROOT/KSP2_x64_Data/StreamingAssets/aa/StandaloneWindows64/
//   (`celestialbody_json_assets_all.bundle`, `celestialbody-scaled-kerbin_assets_all.bundle`, and for
//   the KSC object also `defaultlocalgroup_assets_all.bundle`).
//
// WHY THE OVERLOAD MATTERS
//   CommNetManager has two: SetSourceNode(IGGuid) and SetSourceNode(ConnectionGraphNode). Only the
//   node overload carries the MaxRange this patch edits, and it is the one the legacy patched. The
//   patch is declared against the node overload explicitly by type, not by name alone, so a future
//   overload cannot silently capture it.
//
// WHAT WAS DROPPED FROM THE LEGACY
//   The legacy also patched CommNetManager.OnUpdate with a prefix returning false, skipping the game's
//   whole update in favour of its own LateUpdate, so that the graph would be built after every body's
//   position had been written. This port does not do that, and the reason is that the skip was solving
//   a problem this design does not have: the body positions are read live, inside the rebuild, from
//   the same accessors the game itself reads them from (NetworkEngine.CaptureBodies), so there is no
//   stale copy for a later tick to refresh. Skipping the game's own update while replacing only one of
//   its four steps would be a strictly larger behavioural divergence for no benefit. Recorded in
//   Deploy/obj/divergences.md.
//   The legacy's CommNetManager.Initialize / Shutdown postfixes are also not here: they existed to keep
//   its NetworkManager's node registry in step with the session, and the node registry this port needs
//   is Phase 5's.

using System;
using CommNextRedux.Network;
using HarmonyLib;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;

namespace CommNextRedux.Patches
{
    /// <summary>
    /// Moves the KSC's CommNet origin onto the space centre and applies the configured range.
    /// </summary>
    /// <remarks>
    /// Runs as a postfix, not a prefix: the node has to exist before its range can be set, and the
    /// game's own <c>SetSourceNode</c> is what puts it into the graph.
    /// </remarks>
    [HarmonyPatch(typeof(CommNetManager), nameof(CommNetManager.SetSourceNode),
        new[] { typeof(ConnectionGraphNode) })]
    public static class CommNetManagerSetSourceNodePatch
    {
        /// <summary>The simulation object key of the KSC's CommNet control source.</summary>
        /// <remarks>Measured in the shipped Addressables bundles - see the file header.</remarks>
        public const string CommNetOriginNameKey = "kerbin_CommNetOrigin";

        /// <summary>The simulation object key of the space centre itself.</summary>
        /// <remarks>Measured in the shipped Addressables bundles - see the file header.</remarks>
        public const string SpaceCenterNameKey = "kerbin_KSC_Object";

        private static KscRangeMode _loggedMode = (KscRangeMode)(-1);
        private static bool _loggedSuppressed;
        private static bool _warnedMissingSpaceCenter;

        /// <summary>
        /// Harmony postfix on the node overload: the KSC override, or nothing when the switch is off.
        /// </summary>
        /// <param name="newSourceNode">The node the game just made the control source.</param>
        /// <remarks>
        /// <c>ConnectionGraphNode</c> is a class, so mutating <paramref name="newSourceNode"/> here is
        /// visible to the caller without a <c>ref</c> - the same mechanism the legacy relied on. A
        /// <c>ref</c> parameter is still used so the binding is explicit.
        /// </remarks>
        // ReSharper disable InconsistentNaming
        public static void Postfix(ref ConnectionGraphNode newSourceNode)
        // ReSharper restore InconsistentNaming
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;

            // The D1 off-switch, second of its three effects: MaxRange is not touched and the origin is
            // not moved. Note the early return is BEFORE the name test, so the log line below reports
            // the suppression once and only once, rather than once per session start.
            if (!NetworkConfig.NetworkEnabled)
            {
                if (!_loggedSuppressed)
                {
                    _loggedSuppressed = true;
                    Log(plugin, "KSC range override: SUPPRESSED (Enable CommNext network is false) - "
                        + "the game's own control-source node is left exactly as it was");
                }

                return;
            }

            if (newSourceNode == null)
            {
                return;
            }

            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            if (game == null)
            {
                return;
            }

            UniverseModel universe = game.UniverseModel;
            if (universe == null)
            {
                return;
            }

            SimulationObjectModel origin;
            try
            {
                origin = universe.FindSimObject(newSourceNode.Owner);
            }
            catch (Exception exception)
            {
                Log(plugin, "KSC range override: could not resolve the control source ("
                    + exception.GetType().Name + ")");
                return;
            }

            if (origin == null || origin.Name != CommNetOriginNameKey)
            {
                // Some other node is the control source - a vessel, or a modded source. Not ours.
                return;
            }

            SimulationObjectModel spaceCenter;
            try
            {
                spaceCenter = universe.FindSimObjectByNameKey(SpaceCenterNameKey);
            }
            catch (Exception exception)
            {
                if (!_warnedMissingSpaceCenter)
                {
                    _warnedMissingSpaceCenter = true;
                    Log(plugin, "KSC range override: could not look up '" + SpaceCenterNameKey
                        + "' (" + exception.GetType().Name + ")");
                }

                return;
            }

            if (spaceCenter == null)
            {
                if (!_warnedMissingSpaceCenter)
                {
                    _warnedMissingSpaceCenter = true;
                    Log(plugin, "KSC range override: '" + SpaceCenterNameKey + "' not found; the "
                        + "CommNet origin stays at its own position and only the range is overridden");
                }
            }
            else
            {
                origin.transform.Position = spaceCenter.transform.Position;
            }

            double range = NetworkConfig.RangeFor(NetworkConfig.Ksc);
            newSourceNode.MaxRange = range;

            if (_loggedMode != NetworkConfig.Ksc)
            {
                _loggedMode = NetworkConfig.Ksc;
                Log(plugin, "KSC range override: mode=" + NetworkConfig.Ksc + " -> MaxRange="
                    + range + " m; origin moved to '" + SpaceCenterNameKey + "' ("
                    + (spaceCenter == null ? "NOT FOUND - position unchanged" : "found")
                    + ")");
            }
        }

        private static void Log(CommNextReduxPlugin plugin, string message)
        {
            if (plugin == null)
            {
                return;
            }

            plugin.LogLine(message);
        }
    }
}
