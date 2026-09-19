// CommNextRedux - the connection-graph interception: the D8 deep route.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Patches/ConnectionGraphPatches.cs. The legacy's prefix bound
//   eight private fields of ConnectionGraph and replaced the game's own job with
//   GetNextConnectedNodesJob (a Burst IJob) plus three auxiliary native arrays of its own.
//
// WHAT CHANGED, FIELD BY FIELD (measured against the installed Assembly-CSharp.dll)
//   The runtime's ConnectionGraph, `monodis --fields` under a per-command MONO_PATH prefix, 0 lines
//   matching "failed to parse":
//
//     _allNodes            List<ConnectionGraphNode>                            bound, unchanged
//     _allNodeCount        int32                                                bound, unchanged
//     _hasBuiltGraph       bool                                                 bound, unchanged
//     _isEnabled           bool                                                 not needed
//     _isRunning           bool                                                 read via IsRunning only
//     _jobHandle           JobHandle                                            not needed (no job)
//     _nodes               NativeArray<ConnectionGraphJobNode>                   not needed
//     _prevSourceIndex     int32                                                bound, unchanged
//     _path                List<uint32>                                         not needed
//     _previousEdges       NativeArray<ConnectionEdge>                          RENAMED from the
//                                                                               legacy's
//                                                                               _previousIndices:
//                                                                               NativeArray<int32>
//
//   So seven of the eight the legacy bound survive verbatim and exactly one is renamed. The
//   replacement is strictly richer: ConnectionEdge is { int32 Index ; float64 Cost }, where Cost is
//   the squared length of the edge the predecessor was chosen by (IL-verified in UpdateGraph).
//   The sibling port proved the rename independently.
//
//   Three methods the port contract recorded as public are NOT public on the installed runtime, and
//   that is load-bearing here:
//
//     ResizeCollections(int)                       private
//     GetFlagsFrom(ConnectionGraphNode)             private static
//     DisposeNativeCollections()                    private
//
//   (IL: `.method private hidebysig instance default void ResizeCollections (int32 numNodes)` and the
//   same shape for the other two; the contract's list said otherwise and is corrected in
//   Deploy/obj/API-DELTA.md's successor block in Deploy/obj/PORT-PROGRESS.md.)
//
//   The consequence is that the graph's own growth path is unreachable, so this prefix reproduces the
//   one allocation it needs - `_previousEdges`, at the game's own
//   (Allocator.Persistent, NativeArrayOptions.ClearMemory) and disposed by the game's own
//   DisposeNativeCollections when the session ends, which guards on IsCreated and therefore disposes
//   exactly what this file created. No native memory is owned by this mod: the array is the game's
//   field, in the game's layout, freed by the game's code.
//
//   And `GetFlagsFrom` being private is why the oracle in NetworkProbe reproduces it rather than
//   calling it.
//
// WHY THIS FILE NOW HOLDS TWO PATCHES (F36)
//   The rebuild patch replaces the graph; the second patch, on ConnectionGraph.OnUpdate, is the only
//   place the vanilla tree can be read without mixing two generations. `RebuildConnectionGraph` does
//   not build the graph - it fills `_nodes` and schedules a job (IL_00ee) - so a postfix on it sees
//   the NEXT generation's positions beside the PREVIOUS generation's edges in `_previousEdges`. The
//   completion is in `OnUpdate`, and it is there that the two arrays describe one rebuild. The
//   measurement that proved it, the alternative that was rejected, and the rule a future reader must
//   keep are in NetworkEngine.CaptureVanilla's remarks.

using System;
using System.Collections.Generic;
using CommNextRedux.Network;
using HarmonyLib;
using KSP.Game;
using KSP.Sim;
using Unity.Collections;

namespace CommNextRedux.Patches
{
    /// <summary>
    /// Replaces <c>ConnectionGraph.RebuildConnectionGraph</c> with the occlusion-aware tabulation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The prefix's contract: if it returns <c>false</c>, it has produced a complete result.</b>
    /// This is not a style preference. <c>CommNetManager.OnUpdate</c> sets its own
    /// <c>_isGraphBuilding</c> latch when it calls this method and clears it only when
    /// <c>ConnectionGraph.HasResult</c> reports true - and <c>get_HasResult</c> returns
    /// <c>_hasBuiltGraph</c> directly. In the game's own flow <c>_hasBuiltGraph</c> becomes true one
    /// frame later, in <c>ConnectionGraph.OnUpdate</c>, once the scheduled job has completed; in this
    /// port's flow there is no job, so this prefix sets it itself. A <c>false</c> return that left
    /// <c>_hasBuiltGraph</c> false would latch <c>_isGraphBuilding</c> permanently and the game would
    /// never rebuild its graph again for the rest of the session. Every early-out below therefore
    /// returns <b>true</b> - handing the whole call back to the game - and the one path that returns
    /// <c>false</c> always completes.
    /// </para>
    /// <para>
    /// <b>The game's own body is never called.</b> <c>RebuildConnectionGraph</c> schedules
    /// <c>GetConnectedNodesJob</c> through <c>IJobExtensions.Schedule</c> and sets
    /// <c>_isRunning = true</c> (IL-verified for the whole 263-byte method). Returning <c>false</c>
    /// suppresses all of that, so <c>_isRunning</c> stays false and <c>ConnectionGraph.OnUpdate</c>
    /// finds nothing to complete - which is correct, because there is nothing in flight.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(ConnectionGraph), nameof(ConnectionGraph.RebuildConnectionGraph))]
    public static class ConnectionGraphRebuildPatch
    {
        /// <summary>
        /// Harmony prefix. Returns <c>false</c> only after writing the whole graph.
        /// </summary>
        /// <param name="__instance">The graph being rebuilt.</param>
        /// <param name="____hasBuiltGraph">Bound <c>ConnectionGraph._hasBuiltGraph</c>.</param>
        /// <param name="____allNodes">Bound <c>ConnectionGraph._allNodes</c>.</param>
        /// <param name="____allNodeCount">Bound <c>ConnectionGraph._allNodeCount</c>.</param>
        /// <param name="____previousEdges">Bound <c>ConnectionGraph._previousEdges</c>.</param>
        /// <param name="____prevSourceIndex">Bound <c>ConnectionGraph._prevSourceIndex</c>.</param>
        /// <param name="nodes">The node list the caller passed.</param>
        /// <param name="sourceNodeIndex">The control source's index in that list.</param>
        /// <returns><c>false</c> when this port produced the graph; <c>true</c> to hand back.</returns>
        // ReSharper disable InconsistentNaming
        public static bool Prefix(
            ConnectionGraph __instance,
            ref bool ____hasBuiltGraph,
            ref List<ConnectionGraphNode> ____allNodes,
            ref int ____allNodeCount,
            ref NativeArray<ConnectionGraph.ConnectionEdge> ____previousEdges,
            ref int ____prevSourceIndex,
            List<ConnectionGraphNode> nodes,
            int sourceNodeIndex)
        // ReSharper restore InconsistentNaming
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            NetworkEngine engine = plugin == null ? null : plugin.Network;
            if (engine == null)
            {
                return true;
            }

            // The D1 off-switch, first of its three effects: the original runs unmodified.
            if (!engine.WantsRebuild)
            {
                return true;
            }

            // The game's own in-flight guard. In this port's flow nothing is ever in flight - this
            // file never sets _isRunning and never schedules - so this can only be reached if a
            // vanilla rebuild was already scheduled when the switch was turned back on. Hand the
            // call over and let the game's own warning path handle it.
            if (__instance.IsRunning)
            {
                return true;
            }

            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;

            engine.Prepare(nodes, sourceNodeIndex, game);

            int count = engine.NodeCount;

            // The game's own four bookkeeping writes, in its own order. _allNodes is the list the
            // guid-keyed accessors walk (GetConnectionStatus(IGGuid), GetConnectionDistance(IGGuid)),
            // so it has to hold the same objects the caller passed.
            ____hasBuiltGraph = true;
            if (____allNodes == null)
            {
                ____allNodes = new List<ConnectionGraphNode>(count);
            }
            else
            {
                ____allNodes.Clear();
            }

            if (nodes != null)
            {
                ____allNodes.AddRange(nodes);
            }

            ____allNodeCount = count;

            WritePreviousEdges(ref ____previousEdges, engine, count);

            ____prevSourceIndex = sourceNodeIndex;
            return false;
        }

        // -------------------------------------------------------------------------------------------
        // NO POSTFIX HERE, DELIBERATELY (F36).
        //
        // This class used to carry a postfix that called NetworkEngine.CaptureVanilla whenever
        // WantsRebuild was false, on the reasoning that a postfix runs on every pass and that a `false`
        // return from the prefix does not skip it. Both halves of that reasoning are still true - but
        // the PLACEMENT was wrong, and it was the whole of F36: at the moment a postfix here runs, the
        // original body has just filled `_nodes` from its argument and scheduled the next job, so
        // `_previousEdges` holds the PREVIOUS generation's results while everything else describes the
        // NEXT one. Capturing there paired two generations, and the probe's cost audit then compared a
        // generation-N cost against a generation-N+1 squared distance - kilometres of orbital motion
        // apart, hence `cost-mismatch` on every edge in L4's Run B.
        //
        // The capture now lives in ConnectionGraphOnUpdatePatch, after the job's own Complete(), where
        // the game's own arrays describe exactly one rebuild. Nothing else was weakened: the probe's
        // comparison, the audit's in-range and cost tests and the prefix's write path are unchanged.
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Writes the engine's tree into the graph's own <c>_previousEdges</c>.
        /// </summary>
        /// <param name="previousEdges">Bound <c>ConnectionGraph._previousEdges</c>.</param>
        /// <param name="engine">The engine holding the tree.</param>
        /// <param name="count">The node count.</param>
        /// <remarks>
        /// <para>
        /// The allocation mirrors <c>ResizeCollections</c>'s own line for this field, byte for byte -
        /// <c>new NativeArray&lt;ConnectionEdge&gt;(numNodes, Allocator.Persistent,
        /// NativeArrayOptions.ClearMemory)</c> - because that method is private and cannot be called.
        /// The old array is disposed first, so a node-count change cannot leak it; and the game's own
        /// <c>DisposeNativeCollections</c> (also private, called from <c>Shutdown</c> and from the
        /// finalizer) guards on <c>IsCreated</c> and will dispose this one when the session ends.
        /// </para>
        /// <para>
        /// <c>_nodes</c> is deliberately left uncreated: nothing reads it outside the game's own
        /// rebuild, which this patch replaces, and its dispose is guarded on <c>IsCreated</c>, so
        /// leaving it empty is not a leak.
        /// </para>
        /// </remarks>
        private static void WritePreviousEdges(
            ref NativeArray<ConnectionGraph.ConnectionEdge> previousEdges,
            NetworkEngine engine,
            int count)
        {
            if (!previousEdges.IsCreated || previousEdges.Length != count)
            {
                if (previousEdges.IsCreated)
                {
                    previousEdges.Dispose();
                }

                previousEdges = new NativeArray<ConnectionGraph.ConnectionEdge>(
                    count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            }

            for (int i = 0; i < count; i++)
            {
                ConnectionGraph.ConnectionEdge edge = default;
                edge.Index = engine.PredecessorForGraph(i);
                edge.Cost = engine.CostOf(i);
                previousEdges[i] = edge;
            }
        }
    }

    /// <summary>
    /// Captures the game's own tree at the one instant its own arrays describe one generation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this exists at all (F36).</b> When the master switch is off, the port has to report the
    /// graph it did <i>not</i> build - and it has to report it honestly, because that block is the
    /// master-switch proof. The one place that is possible is here.
    /// </para>
    /// <para>
    /// <b>The body, verified in the installed Assembly-CSharp.dll</b> (<c>monodis --output=</c>; 0 lines
    /// matching <c>failed to parse</c>; method line 37811):
    /// </para>
    /// <code>
    /// IL_0000: if (!_isEnabled) goto IL_0036
    /// IL_0008: if (!_isRunning) goto IL_0036
    /// IL_0010: if (!_jobHandle.IsCompleted()) goto IL_0036
    /// IL_001d: _jobHandle.Complete()      // <- the job's results land in _previousEdges here
    /// IL_002a: _hasBuiltGraph = true
    /// IL_0031: _isRunning = false
    /// IL_0036: ret
    /// </code>
    /// <para>
    /// Three consequences, and the three halves of this patch:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>The capture must happen after <c>Complete()</c>, and the body gives no hook for it.</b> So the
    /// postfix below runs after the whole body and the prefix records whether <c>_isRunning</c> was
    /// true on entry. <c>_isRunning</c> is set true by <c>RebuildConnectionGraph</c> when it schedules
    /// (IL_00fa) and false by exactly this method's completion path (IL_0031), so the pair
    /// <c>__state == true</c> / <c>_isRunning == false</c> is the true-&gt;false transition - i.e. this
    /// frame is the one <c>Complete()</c> ran in. Every early-out leaves <c>_isRunning</c> alone and
    /// therefore cannot capture.
    /// </description></item>
    /// <item><description>
    /// <b>The mode gate is structural, not a second check.</b> In this port's own path the prefix of
    /// <c>RebuildConnectionGraph</c> returns <c>false</c>, so the game's body never runs and
    /// <c>_isRunning</c> is never set true; the prefix below therefore records <c>false</c> and the
    /// postfix never captures. A generation is captured here only if the game's own body actually
    /// scheduled it - which is exactly the condition that makes the capture the game's tree.
    /// </description></item>
    /// <item><description>
    /// <b>Everything read here is that one generation.</b> <c>_nodes</c> was filled from the caller's
    /// list inside that same rebuild and is not rewritten until the next one; <c>_previousEdges</c> is
    /// the array the job wrote; <c>_allNodes</c> and <c>_prevSourceIndex</c> are that rebuild's. That is
    /// the alignment F36 is about, and NetworkEngine.CaptureVanilla asserts it rather than trusting it.
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>This patch does not touch the graph.</b> It reads four fields and hands them to the engine's
    /// snapshot; it writes nothing the game owns. A pure observer - which is why it is safe to leave
    /// installed in every configuration.
    /// </para>
    /// <para>
    /// The one assumption, and it is enforced by the game rather than by this file:
    /// <c>RebuildConnectionGraph</c> returns early while <c>_isRunning</c> is true (IL_0000-0012, with
    /// its own "Cannot rebuild MST. Job already in progress" warning), so nothing can rewrite
    /// <c>_nodes</c> or <c>_allNodes</c> between the completion this postfix follows and the next
    /// generation's schedule.
    /// </para>
    /// </remarks>
    [HarmonyPatch(typeof(ConnectionGraph), nameof(ConnectionGraph.OnUpdate))]
    public static class ConnectionGraphOnUpdatePatch
    {
        /// <summary>
        /// Records whether a job was in flight on entry, so the postfix can tell a completion from an
        /// early return.
        /// </summary>
        /// <param name="____isRunning">Bound <c>ConnectionGraph._isRunning</c>, read, never written.</param>
        /// <param name="__state">Receives <c>_isRunning</c> as it was before the body ran.</param>
        /// <returns>Always <c>true</c>: the body must run - it is the game's own completion.</returns>
        // ReSharper disable InconsistentNaming
        public static bool Prefix(bool ____isRunning, ref bool __state)
        // ReSharper restore InconsistentNaming
        {
            __state = ____isRunning;
            return true;
        }

        /// <summary>
        /// Captures the completed generation, when this frame is the one the job completed in.
        /// </summary>
        /// <param name="__state">
        /// <c>_isRunning</c> as the prefix saw it. <c>true</c> + <paramref name="____isRunning"/>
        /// <c>false</c> is the completion transition.
        /// </param>
        /// <param name="____isRunning">Bound <c>ConnectionGraph._isRunning</c>, after the body ran.</param>
        /// <param name="____allNodes">Bound <c>_allNodes</c>: the generation's node list, for owners.</param>
        /// <param name="____nodes">
        /// Bound <c>_nodes</c>: the job's own input, by value as of <c>Schedule()</c> time. Uncreated in
        /// this port's own path, which the engine's alignment check reports rather than assumes.
        /// </param>
        /// <param name="____previousEdges">Bound <c>_previousEdges</c>: the job's own output.</param>
        /// <param name="____prevSourceIndex">Bound <c>_prevSourceIndex</c>: the generation's source.</param>
        // ReSharper disable InconsistentNaming
        public static void Postfix(
            bool __state,
            bool ____isRunning,
            List<ConnectionGraphNode> ____allNodes,
            NativeArray<ConnectionGraph.ConnectionGraphJobNode> ____nodes,
            NativeArray<ConnectionGraph.ConnectionEdge> ____previousEdges,
            int ____prevSourceIndex)
        // ReSharper restore InconsistentNaming
        {
            if (!__state || ____isRunning)
            {
                // Not this frame: either no job was in flight (this port's own path, or the graph is
                // disabled), or one still is and there is nothing complete to read.
                return;
            }

            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            NetworkEngine engine = plugin == null ? null : plugin.Network;
            if (engine == null)
            {
                return;
            }

            try
            {
                GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
                engine.CaptureVanilla(
                    ____allNodes, ____nodes, ____previousEdges, ____prevSourceIndex, game);
            }
            catch (Exception exception)
            {
                // Never let a diagnostic escape into the game's own update: this postfix sits on the
                // frame the graph becomes usable, and an exception here would take that frame with it.
                plugin.LogWarningLine("could not capture the game's vanilla graph ("
                    + exception.GetType().Name + ": " + exception.Message + ")");
            }
        }
    }
}
