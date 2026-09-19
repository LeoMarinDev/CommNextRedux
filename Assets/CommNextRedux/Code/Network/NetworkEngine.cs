// CommNextRedux - the connection-graph engine: occlusion, the path metric and the KSC range.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Network/Compute/GetNextConnectedNodesJob.cs (the relaxation
//   loop and the occlusion test) and
//   mods-outdated/CommNext/src/CommNext/Patches/ConnectionGraphPatches.cs (the body snapshot and the
//   interception point).
//
// D8 - WHY THIS IS THE DEEP ROUTE, AND WHY IT HAD TO BE
//
//   The preferred route was the shallow one: let the game build its own graph and apply occlusion as
//   a mask on the result, reusing KSP.Sim.GetConnectedNodesJob. It is not available, for two
//   independent and measured reasons. Both are IL-level facts about the installed
//   Assembly-CSharp.dll; the commands and raw output are in Deploy/obj/PORT-PROGRESS.md.
//
//   1. THE GAME'S JOB HAS NO OCCLUSION INPUT. KSP.Sim.GetConnectedNodesJob is a
//      `public sealed struct` with exactly three public fields - StartIndex : int32,
//      Nodes : NativeArray<ConnectionGraphJobNode>, PrevEdges : NativeArray<ConnectionEdge> -
//      measured with `monodis --fields` under a per-command MONO_PATH prefix (0 lines matching
//      "failed to parse"). Its public Execute() reads those three and nothing else. The range test
//      it applies is per-NODE (each node's own MaxRange against the pairwise squared distance), so
//      the only lever the job exposes is a node's MaxRange, which is a property of one node and
//      cannot express "node A and node B may not see each other but both may see C".
//      Per-pair occlusion is exactly that kind of statement. The shallow route therefore cannot
//      express it at all - not "with difficulty", not "with a hack": the input does not exist.
//
//   2. THERE IS NO PAIRWISE EDGE SET TO POST-PROCESS. The game writes only the resulting tree, into
//      _previousEdges : NativeArray<ConnectionEdge> where ConnectionEdge is { int32 Index ;
//      float64 Cost } - the predecessor and the edge's squared length. The pairwise matrix lives in
//      Execute's own Allocator.Temp locals and is discarded when it returns. So a postfix that ran
//      after the game's rebuild would receive a tree with the occluded edges already merged into it;
//      deleting an edge from a tree does not produce the tree over the filtered edge set, it
//      produces a forest whose descendants are still marked reachable through the deleted edge. To
//      get the correct answer the postfix would have to re-run a reachability search over a filtered
//      edge set it does not have. That is the deep route, with extra steps.
//
//   The deep route is feasible because RebuildConnectionGraph's own bookkeeping is reachable from a
//   Harmony prefix: five private fields survive verbatim (_hasBuiltGraph, _allNodes, _allNodeCount,
//   _previousEdges, _prevSourceIndex) and the method is driven from exactly one call site
//   (CommNetManager.OnUpdate, IL-verified) with List<ConnectionGraphNode> and int32 in its signature.
//
//   AND IT SHIPS WITH ITS OWN REFUTATION INSTRUMENT. Because the game's job is a public struct whose
//   Execute() can be called from managed code, this engine can rebuild the game's own job-node array,
//   run the game's own algorithm on it, and compare the result with its own tabulation - node by
//   node, bit for bit on the edge cost. That is what NetworkProbe's oracle block does, and the
//   comparison it makes is the same-input one (F31): the probe's primary verdict compares the
//   game's job against THIS ENGINE'S OWN RELAXATION - run by the control call in Prepare /
//   CaptureVanilla, on the same node array, with occlusion suppressed and with the metric pinned to
//   the accumulated cost the game itself computes (which this port ships as
//   BestPathMode.ShortestKSC). Re-implementing Dijkstra is only defensible if the re-implementation
//   is proved against the original, and that is how it is proved.
//
//   THE ONE THING THE ORACLE CANNOT DO, and the reason for the two-line design: the game's job has no
//   per-pair input. Its only lever is a node's own MaxRange (reason 1 above), so a POST-occlusion edge
//   set can never be handed to it, and a comparison of the shipped tree against the game's job is a
//   comparison of two different inputs whenever occlusion bites. So the proof is decomposed: the
//   primary line proves the relaxation is the game's algorithm on the game's own edge set, and the
//   removals line proves the tree the graph actually got uses only edges out of that set. Together
//   they say: this port's graph is the game's algorithm applied to the game's edge set with the
//   blocked pairs removed - which is the feature. The criteria, the verdicts and what each one means
//   are owned by NetworkProbe's header comment.
//
// THE RELAY / BAND GATE (Phase 5) - WHAT IT IS, WHERE IT SITS, AND WHAT IT MAY NOT DO
//
//   Two nodes may form an edge only if all three hold:
//
//     1. both nodes have HasEnoughResources;
//     2. their BandsFlags masks intersect;
//     3. one of the shared bands covers the distance on BOTH sides.
//
//   The state behind (1) and (2) is this port's own side table, because the game has no relay flag
//   and no band concept at all: `KSP.Sim.ConnectionGraphNodeFlags` is
//   `{ None, IsActive, IsControlSource }` (monodis --fields, flist 30184-30187) and a node carries no
//   range but MaxRange. So the pass that fills the side table (`CollectNodeStates`) resolves each
//   node's parts and reads the same three things the legacy read in
//   `TelemetryComponentPatches.RefreshCommNetNode`: `Data_NextRelay.EnableRelay` into `IsRelay`,
//   `Data_NextRelay.HasResourcesToOperate` into `HasEnoughResources`, and the modulator's band
//   selection into `BandsFlags` + a per-band range table. All three are the legacy's own reads -
//   this port invents no state the legacy did not keep.
//
//   THE RESOURCE CONDITION IS THE RELAY'S OWN, and that is a correction rather than a preference.
//   An earlier revision of this phase read `PartComponentModule_DataTransmitter.IsTransmitterActive()`
//   here, on the theory that the stock transmitter owned the relay's draw. Both halves of that
//   theory are refuted by the shipped IL: `IsTransmitterActive()` is a DEPLOYMENT check
//   (`if (!_requiresDeployment) return true; return _dataDeployable.IsExtended;`), and the stock
//   transmitter's own `OnUpdate` skips its resource block unless `IsTransmitting` - it draws only
//   while a science report is in flight, never while relaying. Had it shipped, a folded dish would
//   have read as "no power" and a relay on the pad would have cost nothing. The relay now owns its
//   request (`Data_NextRelay.SetupResourceRequest`, driven by
//   `PartComponentModule_NextRelay.ResourceConsumptionUpdate`) and this pass reads the verdict from
//   `Data_NextRelay.HasResourcesToOperate` - exactly what the legacy read. `IsTransmitterActive()`
//   is not consulted anywhere in this mod.
//
//   THREE DELIBERATE DIVERGENCES FROM THE LEGACY, each with its reason:
//
//   a. THE RESOURCE CONDITION IS THE RELAY'S OWN `Data_NextRelay.HasResourcesToOperate` - the same
//      field the legacy read, because this port runs the legacy's loop (see the section above). It is
//      gated on `RelaysRequirePower` and on the InfinitePower difficulty option exactly as the
//      legacy's loop was: with the setting off, `PartComponentModule_NextRelay` forces
//      `HasResourcesToOperate = true` and stands its request down, so the flag this pass reads is
//      already true and the gate removes nothing. The port therefore owns no second accounting of the
//      same watt - the rate comes from the relay's own `RequiredResource` and the verdict from the
//      game's own broker.
//
//   b. A PART WITH A TRANSMITTER AND NO MODULATOR IS CREDITED WITH EVERY BAND, at its own range,
//      instead of being left dark. The legacy credited bands only for parts carrying both modules, so
//      a part the Lua patch did not reach got `BandsFlags == 0` and could not connect at all. With
//      the default band set (band X only) the gate is a no-op either way, but the legacy's rule means
//      any part outside the patch's reach is silently severed - and the P3-validated oracle's
//      exactness depends on the default graph being the game's own. The same rule is applied at node
//      level: a node that contributed NO band at all (no parts, no transmitter, or a stale owner
//      mid-load) is credited every band at its own MaxRange rather than being cut off. The covered
//      parts are named ONCE in the log, on the first pass that needs them, with a `relay-gate:`
//      marker (`relay-gate: N part(s) carry a transmitter and no modulator, ...`), because a silent
//      relaxation is indistinguishable from a bug.
//
//   c. THE PER-BAND RANGES ARE LIFTED BY ANY EXCESS THE NODE'S OWN RANGE CARRIES. The node's
//      MaxRange can be raised above every part's `CommunicationRange` - the port's own KSC range
//      override does exactly that (`CommNetManagerPatches` writes `newSourceNode.MaxRange`), and the
//      observed vanilla origin already reports `sourceRange=800000000000`. Without the lift, the band
//      test would quietly cut the pairs that override was bought for: a user setting one node's reach
//      expects every band it offers to inherit it. The lift is `node.MaxRange - max(band ranges from
//      parts)`, credited to every band the node has, and it is zero for every node whose range is the
//      game's own - which is every node the legacy ever saw.
//
//   THE GATE RUNS ON THE SHIPPED TREE ONLY. Like the occlusion test, it is suppressed on the oracle's
//   control run: `GetConnectedNodesJob` has no per-pair input at all (see D8 above), so it cannot be
//   handed a filtered edge set, and the primary verdict has to stay a same-input comparison (F31).
//   The gate's removals are therefore attributed by their own counters and their own recorded links,
//   never by the occlusion census; the probe prints the two causes on separate lines.
//
//   THE COUNTERS ARE EXCLUSIVE AND ORDERED (no-power, then no-common-band, then band-range), so the
//   three of them sum to the total and a pair is never counted under two causes.
//
// WHAT WAS DROPPED FROM THE LEGACY, AND WHY (all recorded in Deploy/obj/divergences.md)
//   * the "relays first" queue bias (`canSourceRelay`) - the legacy refused to let a non-relay node
//     act as a source, which changes which tree a given edge set produces and is therefore
//     oracle-breaking. Every active node may act as a source here, which is exactly the game's own
//     rule (Flags & IsActive) and is what makes the oracle exact. `IsRelay` is still recorded and
//     still reported - it is simply not a gate on the source;
//   * the legacy's own IJob/IJobExtensions.Schedule - decision D10. This engine runs synchronously
//     inside the prefix that replaces the game's method. It is not a Burst job; it is the same
//     arithmetic on the main thread.
//   * CommNext.Native.dll / FusedMultiplyAdd - decision D10. See Occlusion.Discriminant.
//
// THE PASS-DURATION LOG (Phase 5, the legacy's other use of `EnableProfileLogs`)
//   `NetworkConfig.ProfileLogsEnabled` times `Prepare` and logs duration + node count, at most once
//   every `ProfileLogIntervalSeconds` (the legacy's own 4 s throttle, measured at
//   GetNextConnectedNodesJob.cs:324). At Info, because LogDebug never reaches Ksp2.log. The legacy's
//   SECOND use of the same flag - node names - is NOT wired to it here: names are gated on the probe,
//   which is the consumer that needs them (`CaptureBodies`: `Name = probe ? body.bodyName : null`).
//
// PHASE 6 - THE THREE THINGS THE MAP RENDERER NEEDS FROM THIS ENGINE, AND WHY THEY LIVE HERE
//   The renderer walks this engine's tree once per 0.5 s to draw one line per edge. The legacy's
//   renderer got everything it needed out of `NetworkManager` and its own job's edge records; this
//   port has neither - the walk moved to the renderer because the engine (not the game) owns the
//   tree - so the three answers it used to have are produced here, beside the pass that produces the
//   tree itself:
//     * `TryGetIndex(IGGuid, out int)` - the legacy's `NetworkManager.Instance.Nodes[guid]`, i.e. the
//       join from a vessel's identity to its node index. Built with the node snapshots, per pass.
//     * `SelectedBandOf(int)` - the legacy's `networkJobConnection.SelectedBand`. Recorded by the
//       band gate at the instant it accepts the pair, so it is the band the gate used and not a
//       re-derivation; see the accessor's remarks for the exact rule.
//     * `BandCreditorOf(int, int)` / `BandsAreDefaulted(int)` - F52's instrument: the part behind
//       each band bit, so a moving band census can be explained rather than inferred. Probe-gated.
//   None of the three is read by the tabulation, the gate, the oracle or any counter P3/P5 released,
//   and none of them can change an edge: they are written beside the tree, never into it.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CommNextRedux.Modules.Modulator;
using CommNextRedux.Modules.Relay;
using CommNextRedux.Network.Bands;
using KSP.Game;
using KSP.Modules;
using KSP.Sim;
using KSP.Sim.impl;
using Unity.Collections;
using Unity.Mathematics;

namespace CommNextRedux.Network
{
    /// <summary>
    /// A by-value copy of one <see cref="ConnectionGraphNode"/> as it was when the graph was last
    /// rebuilt.
    /// </summary>
    /// <remarks>
    /// The copy is deliberate. A save load rebuilds the simulation object graph, so the live
    /// <c>ConnectionGraphNode</c> objects of one session must never be held across a load; copying the
    /// five values the engine and the probe need means the probe reads what it measured, not whatever
    /// the game has since made of it.
    /// </remarks>
    public struct NetworkNodeSnapshot
    {
        /// <summary>The node's owner - a vessel's or the control source's global id.</summary>
        public IGGuid Owner;

        /// <summary>The node's position, in the same frame the graph is tabulated in.</summary>
        public double3 Position;

        /// <summary>The node's own maximum range in metres.</summary>
        public double MaxRange;

        /// <summary>Whether the game considers this node connected to the network at all.</summary>
        public bool IsActive;

        /// <summary>Whether the game considers this node a control source.</summary>
        public bool IsControlSource;

        /// <summary>
        /// Whether any part of this node's owner carries an enabled relay module
        /// (<c>Data_NextRelay.EnableRelay</c>). CommNext's own concept: the game has no relay flag.
        /// </summary>
        /// <remarks>
        /// Recorded and reported, and deliberately NOT a gate: the legacy refused to let a non-relay
        /// node act as a source, and that bias changes the tree an edge set produces - see the file
        /// header. It is the positive marker the probe reports for "this node is a relay".
        /// </remarks>
        public bool IsRelay;

        /// <summary>
        /// Whether every relay part on this node can currently operate its request. Defaults to
        /// <c>true</c> - a node with no relay has nothing to starve.
        /// </summary>
        /// <remarks>
        /// Read from <c>Data_NextRelay.HasResourcesToOperate</c>, which is the same field the legacy
        /// gated on and which <c>PartComponentModule_NextRelay</c> writes from the game broker's own
        /// delivery verdict. Only consulted when
        /// <see cref="NetworkConfig.RelaysRequirePowerEnabled"/> is on and the InfinitePower
        /// difficulty option is off, as in the legacy - with the setting off the flag is forced true
        /// at its source, so this value can never remove an edge by itself.
        /// </remarks>
        public bool HasEnoughResources;

        /// <summary>
        /// Which bands this node offers, as a mask: bit <c>i</c> is <c>NetworkBands.All[i]</c>, set
        /// only when the node has that band at a POSITIVE range.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>0</c> means "no evidence", and the node-state pass never leaves it there: a node that
        /// contributed no band is credited every band at its own range (divergence (b) in the file
        /// header), so a <c>0</c> mask on an active node means the pass did not run for that node.
        /// </para>
        /// <para>
        /// The per-band ranges live beside this mask rather than in this struct,
        /// in <see cref="NetworkEngine.BandRangeOf"/> - one flat array for the whole pass, so a pass
        /// allocates nothing per node.
        /// </para>
        /// </remarks>
        public int BandsFlags;
    }

    /// <summary>
    /// One celestial body's occlusion sphere, as measured for the current pass.
    /// </summary>
    public struct OcclusionBody
    {
        /// <summary>The body's name, for the probe. <c>null</c> when the probe is off.</summary>
        public string Name;

        /// <summary>The body's centre, in the frame the node positions are in.</summary>
        public double3 Position;

        /// <summary>
        /// The effective occlusion radius: <c>radius * OcclusionRadiusFactor - 1000 m</c>. Only
        /// bodies with a positive effective radius are kept.
        /// </summary>
        public double Radius;

        /// <summary>
        /// The body's own radius, before the factor and the tolerance were applied.
        /// </summary>
        /// <remarks>
        /// <b>Measurement support only - no decision reads this field.</b> <see cref="Radius"/> is
        /// what the occlusion test uses; this is the body's visible size, which is what a drawn line
        /// has to be compared with to answer "does this edge cross the disc the player can see?".
        /// The U6e occlusion-geometry audit is its only reader, and it exists because the two radii
        /// are the whole difference between "the pass drew a line it should have cut" and "the
        /// configured sphere is smaller than the planet" - see
        /// <c>Deploy/obj/u06e-l22-network.md</c>.
        /// </remarks>
        public double RealRadius;
    }

    /// <summary>
    /// The occlusion-aware connection-graph tabulation, and the state the diagnostic probe reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The pass is synchronous.</b> <see cref="Prepare"/> is called from the
    /// <c>RebuildConnectionGraph</c> prefix, computes the whole graph, and returns. The prefix then
    /// writes the six private fields of the game's own <see cref="ConnectionGraph"/> from this
    /// engine's output. There is no job handle, no deferred result and no completion callback.
    /// </para>
    /// <para>
    /// <b>Which means the game's own "job is in flight" gate is never engaged.</b>
    /// <c>ConnectionGraph.RebuildConnectionGraph</c> sets <c>_isRunning = true</c> and schedules;
    /// <c>ConnectionGraph.OnUpdate</c> completes it and sets <c>_hasBuiltGraph = true</c>. Suppressing
    /// the first and never setting <c>_isRunning</c> means the result is available <i>in the same
    /// frame as the rebuild</i> rather than one frame later, and <c>CommNetManager.OnUpdate</c>'s
    /// <c>_isGraphBuilding</c> latch clears immediately instead of latched-pending. The one thing
    /// that must never happen is returning <c>false</c> from the prefix without setting
    /// <c>_hasBuiltGraph</c>: <c>_isGraphBuilding</c> would then never clear and the game would stop
    /// rebuilding its graph for the rest of the session. <see cref="Prepare"/> therefore always
    /// produces a complete result, and the prefix's only early-outs are "hand the whole call back to
    /// the game" ones.
    /// </para>
    /// <para>
    /// <b>The graph frame.</b> Node positions are <c>ConnectionGraphNode.Position</c> in the game's
    /// own CommNet frame. Celestial body positions are converted into that same frame through the
    /// control source's <c>TransformModel.celestialFrame</c> - the legacy's mechanism, kept verbatim,
    /// because any other route leaves body centres and node positions in two different frames and
    /// produces plausible-looking nonsense.
    /// </para>
    /// </remarks>
    public sealed class NetworkEngine
    {
        /// <summary>How many blocked links the probe can print, beyond the whole census.</summary>
        public const int MaxRecordedBlockedLinks = 256;

        /// <summary>The 1 km tolerance the legacy subtracts from every body's occlusion radius.</summary>
        /// <remarks>
        /// Legacy constant <c>SeaLevelTerrainTolerance</c> in
        /// <c>CommNext/Patches/ConnectionGraphPatches.cs</c>. It compensates for terrain and
        /// atmosphere: a link that skims a body's surface is usually clear in practice.
        /// </remarks>
        public const double SeaLevelTerrainTolerance = 1000.0;

        private readonly Action<string> _log;
        private readonly Action<string> _warn;

        // ---------------------------------------------------------------------------------------
        // Per-pass node state. Grown, never shrunk, and reused - the game rebuilds roughly every
        // three seconds and there is no reason for that to allocate.
        // ---------------------------------------------------------------------------------------

        private NetworkNodeSnapshot[] _nodes = new NetworkNodeSnapshot[0];
        private double[] _distanceSq = new double[0];
        private double[] _optimum = new double[0];
        private bool[] _processed = new bool[0];
        private int[] _queue = new int[0];
        private int[] _previous = new int[0];
        private double[] _edgeCostSq = new double[0];

        // The oracle's control tree - the same relaxation on the same node array, with occlusion
        // suppressed and the metric pinned to the game's own (F31). Written by Tabulate's control
        // call, read only by the diagnostic probe, and never written into the graph.
        private int[] _oraclePrevious = new int[0];
        private double[] _oracleEdgeCostSq = new double[0];
        private int _queueLength;
        private int _count;
        private int _sourceIndex = -1;

        /// <summary>
        /// The graph node objects the last pass was built from, so an audit can read their
        /// <b>live</b> positions after the pass.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why the engine holds a game list.</b> The pass copies every node's position into
        /// <see cref="_nodes"/> (the snapshot the whole tabulation runs on), and the game keeps
        /// writing the same <c>ConnectionGraphNode</c> objects' <c>Position</c> as the vessels move.
        /// Keeping the list the pass received is therefore the only way to measure how far the drawn
        /// geometry has moved away from the geometry the verdict was computed from - which is one of
        /// the two candidate causes of the U6e "a line is drawn through the planet" report and the
        /// thing the audit in <c>NetworkProbe</c> measures. Read-only observation: nothing here is
        /// written, and the reference is replaced by the next pass.
        /// </para>
        /// <para>
        /// Same frame by construction, which is what makes the comparison meaningful: the snapshot is
        /// <c>node.Position</c> copied, so the two positions are always expressed in the same frame.
        /// </para>
        /// </remarks>
        private List<ConnectionGraphNode> _liveNodes;

        // ---------------------------------------------------------------------------------------
        // Per-pass relay/band state (Phase 5). The mask lives in the node snapshot; the ranges and
        // the node counts live here, one flat array each, so a pass allocates nothing per node.
        // ---------------------------------------------------------------------------------------

        /// <summary>Per-node band ranges: <c>_bandRanges[node * NetworkBands.Count + band]</c>.</summary>
        private double[] _bandRanges = new double[0];

        /// <summary>How many nodes offer each band, for the probe. Length is <see cref="NetworkBands.Count"/>.</summary>
        private readonly int[] _bandNodeCounts = new int[NetworkBands.Count];

        /// <summary>
        /// One node's accumulated band ranges, reused across the pass. Cleared per node, so a node
        /// with no band evidence cannot inherit the previous node's.
        /// </summary>
        private readonly double[] _nodeBandRanges = new double[NetworkBands.Count];

        /// <summary>
        /// Part names credited all bands for lack of a modulator, for the one-shot divergence line.
        /// </summary>
        /// <remarks>
        /// Capped: the line exists to make the relaxation visible, not to enumerate a fleet's every
        /// part. Cleared after it is logged, so the list does not grow for the whole session.
        /// </remarks>
        private readonly List<string> _allBandParts = new List<string>();

        /// <summary>How many part names the divergence line will name before it says "and N more".</summary>
        private const int MaxDivergencePartsNamed = 8;

        /// <summary>Set once the divergence line has been logged; guards the one-shot and the collection.</summary>
        private bool _allBandPartsLogged;

        /// <summary>Set once the node-state read has logged its single failure line.</summary>
        private bool _warnedNodeState;

        /// <summary>
        /// The last time the pass-duration line printed, as a <see cref="Stopwatch"/> timestamp.
        /// </summary>
        /// <remarks>
        /// The legacy's own throttle: <c>GetNextConnectedNodesJob.Execute</c> logged its duration at
        /// most once every four seconds, because the graph rebuilds far more often than a player can
        /// read a log.
        /// </remarks>
        private long _lastProfileLogTimestamp;

        /// <summary>Increments per node with band evidence that had to be lifted by divergence (c).</summary>
        private int _bandRangeLiftedEntries;

        /// <summary>Per-pass gate counters. See the file header for their exclusive ordering.</summary>
        private int _gateConsidered;
        private int _gateNoPower;
        private int _gateNoCommonBand;
        private int _gateBandRange;

        private readonly int[] _bandBlockedSource = new int[MaxRecordedBlockedLinks];
        private readonly int[] _bandBlockedTarget = new int[MaxRecordedBlockedLinks];
        private readonly int[] _bandBlockedReason = new int[MaxRecordedBlockedLinks];
        private int _bandBlockedRecorded;

        // ---------------------------------------------------------------------------------------
        // Per-pass hand-off data for the map renderer (Phase 6), plus the band-credit attribution
        // the probe needs to explain the band census (F52).
        //
        // Nothing here is read by the tabulation, by the gate, by an oracle comparison or by any
        // counter P3/P5 released: it is written beside them and read by the renderer and the probe
        // only. That is deliberate - the released evidence must not be able to move because of it.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The node index each owner GUID had on the last pass - the renderer's join between a
        /// vessel's <c>GlobalId</c> and this engine's node array.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Rebuilt on every pass (<see cref="Prepare"/> and <see cref="CaptureVanilla"/> both clear
        /// and refill it) so it can never describe a node list the game has already replaced. A
        /// re-used dictionary rather than a pair of parallel arrays because the renderer's question is
        /// "which index owns this GUID", asked once per pass about at most a couple of GUIDs, and a
        /// dictionary answers it without a scan.
        /// </para>
        /// <para>
        /// The legacy reached the same answer through <c>NetworkManager.Instance.Nodes[guid]</c>, its
        /// own dictionary whose key was the same owner GUID. This port has no such table - the walk
        /// moved to the renderer - so the lookup is built here, where the node list is authoritative
        /// for the frame.
        /// </para>
        /// </remarks>
        private readonly Dictionary<IGGuid, int> _nodeIndexByOwner = new Dictionary<IGGuid, int>();

        /// <summary>
        /// Per-edge selected band for the last gated pass:
        /// <c>_selectedBand[targetIndex]</c> is the band the gate accepted that edge on.
        /// </summary>
        /// <remarks>
        /// <c>-1</c> is "no band selected", and it is a real state with three causes, all of them
        /// honest: the edge is the tree's source (nothing precedes it), the gate was not run on this
        /// tree at all (the master switch is off - the pass is the game's own), or the edge is not in
        /// the tree (it lost the <c>optimum</c> comparison, or a body occluded it). See
        /// <see cref="SelectedBandOf"/> for the rule that decides which band a pair records.
        /// </remarks>
        private int[] _selectedBand = new int[0];

        /// <summary>
        /// The oracle's own scratch for the same value - never read. A run that enforces no gate
        /// selects no band, so passing this keeps <see cref="_selectedBand"/> untouched.
        /// </summary>
        private int[] _oracleSelectedBand = new int[0];

        /// <summary>
        /// Per-node, per-band part-name attribution: <c>_bandCreditors[node * Count + band]</c> is the
        /// name of the last part that credited that band on that node, or <c>null</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>P6's instrument for F52</b> - the unexplained reading that a vessel's band census moved
        /// with the relay's state while <c>no-modulator-parts=0</c>. The engine must name the part that
        /// credited each band, or the reading has to be explained by inference; this is what lets the
        /// probe print the mask and its cause on one line.
        /// </para>
        /// <para>
        /// Populated only while the probe is on, and off the hot path in the same sense the node-state
        /// pass is: it is written from <c>CollectNodeStates</c>, which already reads every part. With
        /// the probe off the whole table is skipped and no string is touched.
        /// </para>
        /// </remarks>
        private string[] _bandCreditors = new string[0];

        /// <summary>Per node: whether this pass credited its mask from the no-evidence default.</summary>
        private bool[] _bandDefaulted = new bool[0];

        /// <summary>The parts that credited each band on the node being read, reused per node.</summary>
        private readonly string[] _nodeBandCreditors = new string[NetworkBands.Count];

        /// <summary>Whether the current part's name should be recorded (probe on).</summary>
        private bool _recordBandCreditors;

        /// <summary>The name of the part currently being folded in, or <c>null</c>.</summary>
        private string _currentPartName;

        /// <summary>Whether the node being read was credited the all-bands default.</summary>
        private bool _nodeBandsDefaulted;

        // ---------------------------------------------------------------------------------------
        // Per-pass body state.
        // ---------------------------------------------------------------------------------------

        private OcclusionBody[] _bodies = new OcclusionBody[0];
        private int _bodyCount;
        private bool _occlusionAvailable;

        // ---------------------------------------------------------------------------------------
        // Per-pass counters, for the probe.
        // ---------------------------------------------------------------------------------------

        private int[] _occludedPerBody = new int[0];
        private int[] _blockedSource = new int[MaxRecordedBlockedLinks];
        private int[] _blockedTarget = new int[MaxRecordedBlockedLinks];
        private int[] _blockedBody = new int[MaxRecordedBlockedLinks];
        private int _blockedRecorded;

        // ---------------------------------------------------------------------------------------
        // Per-pass node-state census (Phase 5), for the probe and for the L5 relay-power gate.
        // ---------------------------------------------------------------------------------------

        /// <summary>Nodes carrying at least one enabled relay, this pass.</summary>
        public int RelayNodeCount { get; private set; }

        /// <summary>Nodes whose relay parts could not all operate their request, this pass.</summary>
        /// <remarks>
        /// Counts nodes that FAILED the condition - i.e. the population the resource half of the gate
        /// acts on - so <c>0</c> is the healthy reading and the one that keeps the gate a no-op. The
        /// state behind it is <c>Data_NextRelay.HasResourcesToOperate</c>, so a cleared EC store is
        /// what moves this number, never a folded dish.
        /// </remarks>
        public int PowerlessNodeCount { get; private set; }

        /// <summary>Nodes that contributed at least one band, this pass.</summary>
        public int BandedNodeCount { get; private set; }

        /// <summary>
        /// Nodes the state pass could not credit a single band, this pass.
        /// </summary>
        /// <remarks>
        /// These fell through to divergence (b)'s node-level default - no owner, no parts, no
        /// transmitter, or an owner that is stale mid-load - and were given every band at their own
        /// range. A small number is normal (the control source is a node with no parts). Equal to the
        /// node count, it means the pass found no parts at all, which is the one reading that would
        /// make the whole side table fiction - hence its own count rather than a silent default.
        /// </remarks>
        public int NoBandEvidenceNodeCount { get; private set; }

        /// <summary>Parts carrying a data transmitter that the state pass read, this pass.</summary>
        public int TransmitterPartCount { get; private set; }

        /// <summary>Parts carrying a modulator that the state pass read, this pass.</summary>
        public int ModulatorPartCount { get; private set; }

        /// <summary>
        /// Parts credited every band for lack of a modulator, this pass - the divergence's own count.
        /// </summary>
        /// <remarks>
        /// Its first non-zero pass also logs the parts by name, once per session. Zero on a fully
        /// patched install: the Lua patch puts a modulator on every transmitter it touches.
        /// </remarks>
        public int AllBandPartCount { get; private set; }

        /// <summary>
        /// Whether the resource half of the gate was live this pass.
        /// </summary>
        /// <remarks>
        /// <c>false</c> when the setting is off <i>or</i> when the campaign's InfinitePower difficulty
        /// option is on - the two cases in which <c>HasEnoughResources</c> is not allowed to remove
        /// anything. The probe prints it so "no power on the network" and "power is not being
        /// enforced" can never be read as the same run.
        /// </remarks>
        public bool RelayPowerEnforced { get; private set; }

        /// <summary>How many nodes offer the band at <paramref name="bandIndex"/>, this pass.</summary>
        /// <param name="bandIndex">A band index, below <see cref="BandCount"/>.</param>
        /// <returns>The node count.</returns>
        public int BandNodeCount(int bandIndex) => _bandNodeCounts[bandIndex];

        /// <summary>How many (node, band) ranges the range lift of divergence (c) raised, this pass.</summary>
        public int BandRangeLiftedCount => _bandRangeLiftedEntries;

        /// <summary>How many bands the band tables carry. The width of a <c>BandsFlags</c> mask.</summary>
        public int BandCount => NetworkBands.Count;

        /// <summary>
        /// The range node <paramref name="index"/> offers on <paramref name="bandIndex"/>, or <c>0</c>
        /// when the node does not offer that band.
        /// </summary>
        /// <param name="index">Node index.</param>
        /// <param name="bandIndex">A band index, below <see cref="BandCount"/>.</param>
        public double BandRangeOf(int index, int bandIndex) =>
            _bandRanges[index * NetworkBands.Count + bandIndex];

        /// <summary>
        /// Whether node <paramref name="index"/>'s mask came from the no-evidence default rather than
        /// from a part.
        /// </summary>
        /// <param name="index">Node index.</param>
        /// <remarks>
        /// <c>true</c> means the node credited every band because no part of it could be read - the
        /// node-level half of divergence (b). It is what makes the probe's attribution line
        /// interpretable: a mask of all five bands with <c>defaulted=true</c> is not five parts
        /// agreeing, it is the safety net.
        /// </remarks>
        public bool BandsAreDefaulted(int index) => _bandDefaulted[index];

        /// <summary>
        /// The name of the part that credited one band on one node, or <c>null</c> when the band was
        /// credited by the all-bands default or by no part at all.
        /// </summary>
        /// <param name="index">Node index.</param>
        /// <param name="bandIndex">A band index, below <see cref="BandCount"/>.</param>
        /// <returns>The part's <c>PartName</c>, or <c>null</c>.</returns>
        /// <remarks>
        /// <b>Written only while the probe is on</b> - see <c>_bandCreditors</c> - so a probe-off
        /// session returns <c>null</c> for every pair. This is F52's instrument: it turns "the census
        /// moved" into "this part credited this band on this node".
        /// </remarks>
        public string BandCreditorOf(int index, int bandIndex) =>
            _bandCreditors[index * NetworkBands.Count + bandIndex];

        /// <summary>How many pairs the gate considered this pass - its own denominator.</summary>
        /// <remarks>
        /// Every pair that reached the gate, i.e. was in range of both nodes, was reachable from the
        /// source and had not already been processed. It is not <see cref="InRangePairCount"/>: that
        /// counts candidates geometrically, before the reachability checks.
        /// </remarks>
        public int GateConsideredPairCount => _gateConsidered;

        /// <summary>How many pairs the gate removed this pass, for any of its three reasons.</summary>
        public int BandGateRemovedPairCount => _gateNoPower + _gateNoCommonBand + _gateBandRange;

        /// <summary>Of those, how many had a node without resources. Cause 1 of 3.</summary>
        public int BandGateNoPowerCount => _gateNoPower;

        /// <summary>Of those, how many had no band in common at all. Cause 2 of 3.</summary>
        public int BandGateNoCommonBandCount => _gateNoCommonBand;

        /// <summary>
        /// Of those, how many shared a band whose two ranges did not cover the distance. Cause 3 of 3.
        /// </summary>
        public int BandGateBandRangeCount => _gateBandRange;

        /// <summary>How many gate removals were recorded for the probe (capped).</summary>
        public int RecordedBandBlockedCount => _bandBlockedRecorded;

        /// <summary>Reads one recorded gate removal.</summary>
        /// <param name="index">Record index, below <see cref="RecordedBandBlockedCount"/>.</param>
        /// <param name="sourceIndex">Receives the source node index.</param>
        /// <param name="targetIndex">Receives the target node index.</param>
        /// <param name="reason">Receives 0 for no-power, 1 for no-common-band, 2 for band-range.</param>
        public void GetRecordedBandBlockedLink(
            int index,
            out int sourceIndex,
            out int targetIndex,
            out int reason)
        {
            sourceIndex = _bandBlockedSource[index];
            targetIndex = _bandBlockedTarget[index];
            reason = _bandBlockedReason[index];
        }

        /// <summary>Creates the engine.</summary>
        /// <param name="log">Informational sink; must not throw.</param>
        /// <param name="warn">Warning sink; must not throw.</param>
        public NetworkEngine(Action<string> log, Action<string> warn)
        {
            _log = log;
            _warn = warn;
        }

        /// <summary>Number of nodes in the last prepared pass.</summary>
        public int NodeCount => _count;

        /// <summary>The source (control source) index of the last prepared pass, or <c>-1</c>.</summary>
        public int SourceIndex => _sourceIndex;

        /// <summary>
        /// Whether the last pass <b>is</b> the game's own tree (<c>true</c>) rather than one this port
        /// replaced (<c>false</c>).
        /// </summary>
        /// <remarks>
        /// The flag reads the way its name says: <c>true</c> is the vanilla state, <c>false</c> is the
        /// state in which this mod built the graph. (The summary here previously said the opposite,
        /// "replaced the graph (true) or observed the game's (false)", which is inverted against both
        /// the name and the two assignments - <c>Prepare</c> sets <c>false</c>, <c>CaptureVanilla</c>
        /// sets <c>true</c>. Corrected while fixing F41: the oracle's vanilla branch is gated on this
        /// flag being <c>true</c>, so a reader who trusted the old wording would have inverted the
        /// vanilla control.)
        /// </remarks>
        public bool PassIsVanilla { get; private set; }

        /// <summary>Increments once per prepared or captured pass; the probe's de-duplication key.</summary>
        public long Generation { get; private set; }

        /// <summary>Whether a pass has been produced that the probe has not logged yet.</summary>
        public bool HasPendingPass { get; private set; }

        /// <summary>Number of nodes the game marked active in the last pass.</summary>
        public int ActiveCount { get; private set; }

        /// <summary>Number of nodes with a predecessor in the last pass's tree.</summary>
        public int ConnectedCount { get; private set; }

        /// <summary>Number of ordered node pairs that passed both range tests before occlusion.</summary>
        public int InRangePairCount { get; private set; }

        /// <summary>Number of those pairs that occlusion removed.</summary>
        public int OccludedPairCount { get; private set; }

        /// <summary>Number of segment-versus-body tests performed.</summary>
        public int BodyTestCount { get; private set; }

        /// <summary>Number of bodies with a usable occlusion sphere this pass.</summary>
        public int BodyCount => _bodyCount;

        /// <summary>Whether the body snapshot could be taken at all this pass.</summary>
        public bool OcclusionAvailable => _occlusionAvailable;

        /// <summary>Whether the occlusion factor is non-zero for this pass.</summary>
        public bool OcclusionEnabled { get; private set; }

        /// <summary>
        /// Whether this pass produced the oracle's control tree (F31).
        /// </summary>
        /// <remarks>
        /// The control run is gated on the probe being enabled at the moment the graph rebuilt, and the
        /// probe's own tick is gated on the same setting. If the two ever disagree - the user turns the
        /// probe on mid-session, which is a settings change rather than a file edit - the probe must
        /// report that it has nothing to compare rather than diff the previous pass's tree against this
        /// one's node list. A stale control tree is the same class of defect as the stale criterion
        /// F31 removed.
        /// </remarks>
        public bool OracleReady { get; private set; }

        /// <summary>Whether the last pass replaced the graph or observed the game's.</summary>
        /// <remarks>
        /// The prefix asks this before doing anything. It is <c>true</c> only when the master switch is
        /// on and this engine was constructed - the D1 off-switch's first of three effects.
        /// </remarks>
        public bool WantsRebuild => NetworkConfig.NetworkEnabled;

        /// <summary>The snapshot of node <paramref name="index"/> from the last pass.</summary>
        /// <param name="index">Node index.</param>
        /// <returns>The snapshot.</returns>
        public NetworkNodeSnapshot Snapshot(int index) => _nodes[index];

        /// <summary>
        /// The node index a GUID owns in the last pass - the renderer's join between an identity and
        /// this engine's node array.
        /// </summary>
        /// <param name="guid">The owner GUID to resolve, e.g. the active vessel's <c>GlobalId</c>.</param>
        /// <param name="index">Receives the node index, or <c>-1</c>.</param>
        /// <returns><c>true</c> when the GUID owns a node on the last pass.</returns>
        /// <remarks>
        /// <para>
        /// <b>Never throws, and <c>false</c> is a normal answer.</b> A vessel that is not in the
        /// CommNet - on the pad with its antenna stowed, in another body's sphere, or simply after a
        /// save load rebuilt the object graph - has no node, and the caller's response is to draw
        /// nothing for it rather than to fail. The legacy threw <c>KeyNotFoundException</c> out of a
        /// dictionary index in that case.
        /// </para>
        /// <para>
        /// The table is rebuilt per pass, so an index from this method is only valid against the same
        /// pass's <see cref="Snapshot"/> calls. The renderer re-resolves it on every refresh for that
        /// reason.
        /// </para>
        /// </remarks>
        public bool TryGetIndex(IGGuid guid, out int index)
        {
            if (_nodeIndexByOwner.TryGetValue(guid, out index))
            {
                return true;
            }

            index = -1;
            return false;
        }

        /// <summary>
        /// The band the relay/band gate selected for the tree edge whose TARGET is
        /// <paramref name="targetIndex"/> - i.e. the edge <c>PredecessorOf(targetIndex) -&gt; targetIndex</c>.
        /// </summary>
        /// <param name="targetIndex">The node whose incoming edge is asked about.</param>
        /// <returns>The band index, or <c>-1</c> when no band was selected.</returns>
        /// <remarks>
        /// <para>
        /// <b>The rule, and it is the gate's own:</b> a pair records the <i>lowest</i> band index that
        /// is common to both nodes AND whose two per-band ranges both cover the distance - which is
        /// exactly the band the gate accepted the pair on (<see cref="PassesRelayGate"/> returns on the
        /// first such band in index order). So this is not an approximation of a band the gate might
        /// have used; it is the one it did. The legacy's
        /// <c>networkJobConnection.SelectedBand</c> came from its own job's edge record and this is its
        /// equivalent on this port's engine, where no such record exists.
        /// </para>
        /// <para>
        /// <b>Recording happens where the edge is accepted, not where the gate is passed.</b> A pair
        /// can pass the gate and then be occluded or lose the <c>optimum</c> comparison, and an edge
        /// that is not in the tree must not carry a band the renderer would colour a different line
        /// with. The value is written immediately after <c>_previous[targetIndex]</c>, in the same
        /// statement group, so the two cannot disagree.
        /// </para>
        /// <para>
        /// <c>-1</c> means "no band selected": the tree's source, a node with no incoming edge, a pass
        /// in which the gate was not enforced (the master switch off - the tree is the game's own and
        /// no band was chosen for any of its edges), or the vanilla capture. The renderer's fallback
        /// colour for it is the legacy's link/relay colour, which is what its
        /// <c>HasMatchingBand == false</c> branch did.
        /// </para>
        /// </remarks>
        public int SelectedBandOf(int targetIndex) => _selectedBand[targetIndex];

        /// <summary>The predecessor of node <paramref name="index"/> in the last pass's tree.</summary>
        /// <param name="index">Node index.</param>
        /// <returns>The predecessor index, or <c>-1</c> when unreachable.</returns>
        public int PredecessorOf(int index) => _previous[index];

        /// <summary>
        /// The value that belongs in <c>ConnectionGraph._previousEdges[i].Index</c> for this node.
        /// </summary>
        /// <param name="index">Node index.</param>
        /// <returns>The source's index as <c>0</c>, a predecessor index, or <c>-1</c>.</returns>
        /// <remarks>
        /// <para>
        /// This differs from <see cref="PredecessorOf"/> for exactly one node - the source - and the
        /// difference is deliberate. <c>GetConnectedNodesJob.Execute</c> initialises every entry to
        /// <c>{ Index = -1, Cost = +&#8734; }</c> and then <c>UpdateGraph</c> overwrites the
        /// source's entry with <c>{ Index = 0, Cost = +&#8734; }</c> (IL: <c>IL_002d</c>-
        /// <c>IL_0065</c> of that method). So the game stores a predecessor of <b>0</b> - not <c>-1</c> -
        /// for the source, even when the source is not node 0.
        /// </para>
        /// <para>
        /// <b>The source's cost sentinel is the game's <c>+&#8734;</c>, not this port's
        /// <see cref="double.MaxValue"/>, and the two are the same statement.</b> Measured in L3:
        /// <c>oracle node 0 … cost 1.7976931348623157E+308 (this port) vs Infinity (the game)</c>.
        /// Comparing two encodings of "no edge" as if they were values is what produced a false
        /// <c>cost-differs</c> (F30); the probe now treats either as unset. Nothing here changes -
        /// this port keeps writing <see cref="double.MaxValue"/> because that is also every consumer's
        /// "unreachable" test, and a public accessor's meaning must not move.
        /// </para>
        /// <para>
        /// The value is unreadable by any consumer: <c>CalculateConnectionStatus(index)</c> returns
        /// <c>Disconnected</c> whenever <c>index == _prevSourceIndex</c>, whatever the stored index is.
        /// Writing the game's own value anyway keeps this port's <c>_previousEdges</c> byte-comparable
        /// with the game's, which is what lets the probe's oracle compare every node including the
        /// source with no special case.
        /// </para>
        /// </remarks>
        public int PredecessorForGraph(int index) =>
            index == _sourceIndex ? 0 : _previous[index];

        /// <summary>
        /// The squared length of the edge from node <paramref name="index"/>'s predecessor.
        /// </summary>
        /// <param name="index">Node index.</param>
        /// <returns>
        /// The squared edge length, or <see cref="double.MaxValue"/> when unreachable.
        /// </returns>
        /// <remarks>
        /// <b>This is the game's own <c>ConnectionEdge.Cost</c> semantics.</b> Verified in
        /// <c>GetConnectedNodesJob.UpdateGraph</c>: the stored value is
        /// <c>math.distancesq(Nodes[source].Position, Nodes[target].Position)</c> - the squared edge
        /// length - and not the accumulated cost, which goes into the job's private
        /// <c>cheapestCosts</c>. <c>ConnectionGraph.GetConnectionDistance(IGGuid)</c> returns this
        /// field, so writing anything else would silently change the meaning of a public accessor.
        /// </remarks>
        public double CostOf(int index) => _edgeCostSq[index];

        /// <summary>
        /// The oracle's control tree: the predecessor <b>the same relaxation</b> gives when it runs
        /// on this pass's node array with occlusion suppressed and the game's own metric.
        /// </summary>
        /// <param name="index">Node index.</param>
        /// <returns>The source's index as <c>0</c>, a predecessor index, or <c>-1</c>.</returns>
        /// <remarks>
        /// <para>
        /// <b>Read by the diagnostic probe and by nothing else.</b> It is what makes the oracle's
        /// primary verdict a same-input comparison (F31): <c>GetConnectedNodesJob</c> can only ever
        /// be handed the in-range edge set with per-node ranges and positions, so the only way this
        /// port's relaxation and the game's own job can be compared on identical input is for the
        /// port's relaxation to be run under the game's own conditions - occlusion suppressed and
        /// <see cref="BestPathMode.ShortestKSC"/>'s accumulated cost, which is the metric the game
        /// computes. The tree the graph actually received is
        /// <see cref="PredecessorForGraph"/>/<see cref="CostOf"/>; this is the control.
        /// </para>
        /// <para>
        /// The source convention matches <see cref="PredecessorForGraph"/> for the same reason: the
        /// game stores <c>0</c> as the source's predecessor, whatever index the source is.
        /// </para>
        /// </remarks>
        public int OraclePredecessorForGraph(int index) =>
            index == _sourceIndex ? 0 : _oraclePrevious[index];

        /// <summary>The oracle's control tree: the edge cost for <paramref name="index"/>.</summary>
        /// <param name="index">Node index.</param>
        /// <returns>The squared edge length, or <see cref="double.MaxValue"/> when unreachable.</returns>
        public double OracleCostOf(int index) => _oracleEdgeCostSq[index];

        /// <summary>Number of bodies that occluded at least one candidate link.</summary>
        /// <param name="bodyIndex">Index into the body snapshot.</param>
        /// <returns>The number of links that body blocked this pass.</returns>
        public int OccludedByBody(int bodyIndex) => _occludedPerBody[bodyIndex];

        /// <summary>The name of the body at <paramref name="bodyIndex"/>.</summary>
        /// <param name="bodyIndex">Index into the body snapshot.</param>
        /// <returns>The body's name, or the empty string.</returns>
        public string BodyName(int bodyIndex) =>
            _bodies[bodyIndex].Name ?? string.Empty;

        /// <summary>The body's centre, in the frame the node positions are in.</summary>
        /// <param name="bodyIndex">Index into the body snapshot.</param>
        /// <returns>The body's position, as the pass measured it.</returns>
        /// <remarks>
        /// Read by the probe's occlusion-geometry audit only. It is the <b>pass's</b> position, not a
        /// fresh one: an audit that needs a live body position would have to resolve the frame again,
        /// and the U6e question is about the node side of the geometry.
        /// </remarks>
        public double3 BodyPosition(int bodyIndex) => _bodies[bodyIndex].Position;

        /// <summary>The body's own radius, before the occlusion factor and tolerance.</summary>
        /// <param name="bodyIndex">Index into the body snapshot.</param>
        /// <returns>The body's visible radius in metres.</returns>
        public double BodyRealRadius(int bodyIndex) => _bodies[bodyIndex].RealRadius;

        /// <summary>The effective occlusion radius the pass tested that body with.</summary>
        /// <param name="bodyIndex">Index into the body snapshot.</param>
        /// <returns>Metres: the radius <see cref="Occlusion.IsOccluded"/> was given.</returns>
        public double BodyOcclusionRadius(int bodyIndex) => _bodies[bodyIndex].Radius;

        /// <summary>
        /// Reads the <b>live</b> position of a node as the graph holds it now, rather than the copy
        /// the last pass computed its verdicts from.
        /// </summary>
        /// <param name="index">Node index, in the same order as <see cref="Snapshot"/>.</param>
        /// <param name="position">Receives the node's current position.</param>
        /// <returns><c>false</c> when no live node list is available for that index.</returns>
        /// <remarks>
        /// <para>
        /// <b>This exists for one measurement.</b> The difference between this position and
        /// <see cref="Snapshot"/><c>.Position</c> is how far the drawn geometry has moved since the
        /// verdict it is drawn under - the U6e audit's discriminator between "the pass is stale" and
        /// "the configured occlusion sphere is smaller than the planet". No engine decision reads it.
        /// </para>
        /// <para>
        /// The bounds are checked against both this list and the pass's node count, because the game
        /// owns the list and may rebuild it between passes; a stale list must return <c>false</c>
        /// rather than an out-of-range position.
        /// </para>
        /// </remarks>
        public bool TryGetLivePosition(int index, out double3 position)
        {
            List<ConnectionGraphNode> live = _liveNodes;
            if (live == null || index < 0 || index >= _count || index >= live.Count)
            {
                position = default;
                return false;
            }

            position = live[index].Position;
            return true;
        }

        /// <summary>How many occluded links were recorded for the probe (capped).</summary>
        public int RecordedBlockedLinkCount => _blockedRecorded;

        /// <summary>Reads one recorded occluded link.</summary>
        /// <param name="index">Record index, below <see cref="RecordedBlockedLinkCount"/>.</param>
        /// <param name="sourceIndex">Receives the source node index.</param>
        /// <param name="targetIndex">Receives the target node index.</param>
        /// <param name="bodyIndex">Receives the occluding body's index.</param>
        public void GetRecordedBlockedLink(
            int index,
            out int sourceIndex,
            out int targetIndex,
            out int bodyIndex)
        {
            sourceIndex = _blockedSource[index];
            targetIndex = _blockedTarget[index];
            bodyIndex = _blockedBody[index];
        }

        /// <summary>
        /// Computes the whole connection graph for one rebuild, with occlusion applied.
        /// </summary>
        /// <param name="nodes">The node list the game handed to <c>RebuildConnectionGraph</c>.</param>
        /// <param name="sourceNodeIndex">The control source's index in that list.</param>
        /// <param name="game">The live game instance, for the body snapshot.</param>
        /// <remarks>
        /// <para>
        /// Always produces a complete result, including for an empty node list and for an
        /// out-of-range <paramref name="sourceNodeIndex"/> - see the class remarks for why declining
        /// is not an option once the prefix has taken the call over.
        /// </para>
        /// <para>
        /// <b>Faithful to the legacy's relaxation.</b> The queue is every index, the selection is the
        /// lowest frontier value, removal is a swap-back, the range gates are squared-distance
        /// comparisons, the occlusion test runs only after both range gates pass and after the target
        /// has been excluded as already-processed, and the optimum is either the edge's squared length
        /// (<see cref="BestPathMode.NearestRelay"/>) or the accumulated one
        /// (<see cref="BestPathMode.ShortestKSC"/>).
        /// </para>
        /// </remarks>
        public void Prepare(List<ConnectionGraphNode> nodes, int sourceNodeIndex, GameInstance game)
        {
            int count = nodes == null ? 0 : nodes.Count;

            // Phase 5. The legacy's pass-duration log, throttled. Taken before anything else so the
            // measurement covers the whole pass rather than the interesting half of it.
            long profileStart = NetworkConfig.ProfileLogsEnabled ? Stopwatch.GetTimestamp() : 0L;

            Generation++;
            HasPendingPass = true;
            PassIsVanilla = false;

            if (!_routeLogged)
            {
                _routeLogged = true;
                _log("network engine: route=DEEP (RebuildConnectionGraph replaced) engine=single-threaded "
                    + "managed occlusion=Dekker-TwoProduct substitution fmaAbsent=true");
            }

            EnsureNodeCapacity(count);
            _count = count;
            _sourceIndex = sourceNodeIndex;
            _liveNodes = nodes;

            ActiveCount = 0;
            ConnectedCount = 0;
            InRangePairCount = 0;
            OccludedPairCount = 0;
            BodyTestCount = 0;
            _blockedRecorded = 0;
            ResetGateCounters();
            OracleReady = false;   // the control tree is stale until this pass produces a new one

            // F29. The per-body census is per-pass state like the counters above, and it has to be
            // cleared here or it accumulates: this array is only ever allocated - by
            // EnsureBodyCapacity, which returns early once the body count has stabilised - so
            // without this clear every pass adds its blocked links to the previous passes' total.
            // Measured on L3: the census reported 2, 4, 6, ... 66 over 33 passes while the engine's
            // own OccludedPairCount stayed at 2, which is the correct answer. Always cleared over
            // the whole array rather than over _bodyCount, because _bodyCount still holds the
            // previous pass's value at this point.
            Array.Clear(_occludedPerBody, 0, _occludedPerBody.Length);

            // Phase 6, and the same per-pass argument as F29 above: the GUID join and the per-edge
            // selected bands describe THIS pass's node list, so both are dropped before it is built.
            // The old join is cleared before the loop rather than inside it so that a pass with a
            // source index outside the list - the early return below - leaves no join behind at all.
            _nodeIndexByOwner.Clear();
            ResetRenderTables(count, NetworkConfig.ProbeEnabled);

            for (int i = 0; i < count; i++)
            {
                ConnectionGraphNode node = nodes[i];
                _nodes[i] = new NetworkNodeSnapshot
                {
                    Owner = node.Owner,
                    Position = node.Position,
                    MaxRange = node.MaxRange,
                    IsActive = node.IsActive,
                    IsControlSource = node.IsControlSource,
                };

                _nodeIndexByOwner[node.Owner] = i;

                if (node.IsActive)
                {
                    ActiveCount++;
                }
            }

            CaptureBodies(nodes, sourceNodeIndex, game);
            CollectNodeStates(game);

            if (count == 0 || sourceNodeIndex < 0 || sourceNodeIndex >= count)
            {
                // Nothing to tabulate. The game's own method would index its start index out of
                // bounds here; a graph with no source is the honest answer and it keeps
                // HasResult progressing.
                if (count > 0)
                {
                    _warn("connection graph rebuild with source index " + sourceNodeIndex
                        + " outside the node list (0.." + (count - 1) + "); the graph is reported "
                        + "with no source");
                }

                LogPassProfile(profileStart);
                return;
            }

            // The oracle's control run, before the graph's own tabulation so that the two cannot
            // read each other's results (F31). It is the SAME relaxation, on the SAME node array,
            // with the two things the game's own job cannot be told to change pinned to the game's
            // values: occlusion suppressed, and the metric set to the accumulated cost the game
            // itself computes. Its only purpose is to be compared against GetConnectedNodesJob by
            // the probe, and its results live in their own arrays - nothing here reaches the graph.
            // Gated on the probe because a release build should not pay for a diagnostic.
            if (NetworkConfig.ProbeEnabled)
            {
                // selectedBand: the oracle's own array. Its run enforces no gate, so it selects no
                // band and never touches the shipped table - which is what keeps the oracle from
                // being able to change the colour of a line (F31's discipline, extended to P6).
                Tabulate(
                    applyOcclusion: false,
                    gameMetric: true,
                    enforceGates: false,
                    _oraclePrevious,
                    _oracleEdgeCostSq,
                    _oracleSelectedBand);
                OracleReady = true;
            }

            Tabulate(
                applyOcclusion: true,
                gameMetric: NetworkConfig.Mode == BestPathMode.ShortestKSC,
                enforceGates: true,
                _previous,
                _edgeCostSq,
                _selectedBand);

            for (int i = 0; i < count; i++)
            {
                if (_previous[i] >= 0)
                {
                    ConnectedCount++;
                }
            }

            LogPassProfile(profileStart);
        }

        /// <summary>
        /// Records one generation of the game's own tree - the job's own <b>input</b> paired with the
        /// job's own <b>output</b> - so the probe can report the vanilla graph when the master switch
        /// is off.
        /// </summary>
        /// <param name="allNodes">
        /// The game's own <c>ConnectionGraph._allNodes</c>: the list that generation's rebuild was
        /// called with. The only source of a node's <c>Owner</c> - a job node carries flags, not an
        /// identity.
        /// </param>
        /// <param name="gameNodes">
        /// The game's own <c>ConnectionGraph._nodes</c>: the array <c>RebuildConnectionGraph</c> filled
        /// from that list and handed to <c>GetConnectedNodesJob</c> as its <c>Nodes</c> field. Its
        /// <c>Position</c>, <c>MaxRange</c> and <c>Flags</c> are, by value, the ones the job's own
        /// arithmetic consumed.
        /// </param>
        /// <param name="previousEdges">
        /// The game's own <c>_previousEdges</c>: the array the same job wrote as its <c>PrevEdges</c>,
        /// holding that generation's predecessors and edge costs.
        /// </param>
        /// <param name="sourceNodeIndex">The control source's index, from the same generation.</param>
        /// <param name="game">The live game instance, for the occlusion census.</param>
        /// <remarks>
        /// <para>
        /// <b>Reached only from the <c>ConnectionGraph.OnUpdate</c> postfix, immediately after its
        /// <c>JobHandle.Complete()</c>, and that placement is the whole of F36.</b> The three arrays it
        /// reads are the game's own storage for exactly one job, and at that instant they still all
        /// describe the generation the job was run for:
        /// </para>
        /// <list type="bullet">
        /// <item><description>
        /// <c>_previousEdges</c> is the array the job was handed as <c>PrevEdges</c> and wrote in
        /// place; <c>Complete()</c> is the moment those results land.
        /// </description></item>
        /// <item><description>
        /// <c>_nodes</c> is the array the job was handed as <c>Nodes</c>, filled from the caller's list
        /// inside <c>RebuildConnectionGraph</c> and never rewritten between that <c>Schedule()</c> and
        /// this completion - so it is that generation's own input, not the next one's.
        /// </description></item>
        /// <item><description>
        /// <c>_allNodes</c> is that same rebuild's list, and <c>_prevSourceIndex</c> its source index.
        /// </description></item>
        /// </list>
        /// <para>
        /// <b>Why not capture in the <c>RebuildConnectionGraph</c> postfix, which is where this method
        /// used to be called from?</b> Because <c>RebuildConnectionGraph</c> does not build the graph:
        /// it fills <c>_nodes</c> from its argument and <b>schedules a job</b>, then returns
        /// (<c>IJobExtensions::Schedule</c> at IL_00ee, <c>_isRunning = 1</c> at IL_00fa, <c>ret</c> at
        /// IL_0106; the whole body has no <c>Complete()</c>). The completion is in <c>OnUpdate</c>
        /// (method line 37811: <c>get_IsCompleted()</c> -> <c>Complete()</c> at IL_0023 ->
        /// <c>_hasBuiltGraph = true</c>). So by the time the rebuild postfix ran, the <c>nodes</c>
        /// argument and <c>_nodes</c> already described the <b>next</b> generation while
        /// <c>_previousEdges</c> still held the <b>previous</b> one's indices and costs, and this
        /// method stored both into one snapshot. The probe then compared a generation-N cost against a
        /// generation-N+1 squared distance. The satellites are in orbit, so over one ~3 s rebuild an
        /// edge moves by kilometres and the squared distances differ by ~10^10: every edge read as a
        /// cost mismatch. That is F36, measured as <c>cost-mismatch=4</c> in L4's Run B.
        /// </para>
        /// <para>
        /// <b>Buffering the previous generation instead was the alternative and is worse.</b> It would
        /// have to re-pair an edge array with a node snapshot taken one rebuild earlier, i.e. infer the
        /// alignment from two separate observations and special-case the first pass of a session. Here
        /// the pairing is not inferred at all: it is the game's own two arrays for one job, read at the
        /// one instant they agree.
        /// </para>
        /// <para>
        /// <b>The vanilla tree is generation-aligned, and that is not an implementation detail.</b> The
        /// audit re-derives each edge's squared length from these snapshots and compares it with the
        /// stored <c>Cost</c>, so the two sides must come from one rebuild or the comparison is
        /// meaningless. Geometry is taken from <paramref name="gameNodes"/> and not from
        /// <paramref name="allNodes"/> for the same reason: the list holds references to the caller's
        /// live node objects, whose positions the caller may have moved since, while the job node array
        /// is a by-value copy frozen at <c>Schedule()</c> time. A future reader changing this must keep
        /// both halves on one generation.
        /// </para>
        /// <para>
        /// It also runs the occlusion <b>census</b> over the same snapshot - the same test the engine
        /// would have applied, counted rather than enforced. So one launch with the switch off reports
        /// both halves of the comparison: the connectivity the game produced, and the edges this mod
        /// would have removed from it.
        /// </para>
        /// </remarks>
        public void CaptureVanilla(
            List<ConnectionGraphNode> allNodes,
            NativeArray<ConnectionGraph.ConnectionGraphJobNode> gameNodes,
            NativeArray<ConnectionGraph.ConnectionEdge> previousEdges,
            int sourceNodeIndex,
            GameInstance game)
        {
            // F36. The three inputs must describe ONE generation, and that is asserted rather than
            // assumed. The job's own input and output arrays are the same length by construction -
            // ResizeCollections allocates both from one numNodes - and _allNodes is filled from the
            // same rebuild's list, so a mismatch means this was reached from somewhere it was not
            // designed for. Declining produces no pass at all, which is honest; pairing two
            // generations is exactly what produced L4's reported INVENTED-CONNECTIVITY.
            //
            // A zero-node generation is a generation: the lengths only have to agree. (IsCreated is
            // tested first because an uncreated by-value NativeArray argument reports Length 0, and
            // this method must not read a field of an array the game never allocated.)
            int nodeCount = gameNodes.IsCreated ? gameNodes.Length : 0;
            int edgeCount = previousEdges.IsCreated ? previousEdges.Length : 0;
            if (nodeCount != edgeCount || allNodes == null || allNodes.Count != edgeCount)
            {
                WarnOnce(ref _warnedUnalignedCapture,
                    "vanilla capture skipped: the game's node array, edge array and node list do not "
                    + "describe one generation (edges=" + edgeCount
                    + " nodes=" + nodeCount
                    + " allNodes=" + (allNodes == null ? "null" : allNodes.Count.ToString())
                    + "); no vanilla pass is reported until they agree");
                return;
            }

            int count = edgeCount;

            Generation++;
            HasPendingPass = true;
            PassIsVanilla = true;

            if (!_vanillaCaptureLogged)
            {
                // One line, once per session, naming what makes this block evidence: the tree is the
                // game's own job's output and the positions it is audited against are that same job's
                // own input, read from the graph at the one moment they agree.
                _vanillaCaptureLogged = true;
                _log("vanilla capture: generation-aligned - the tree is the game's own job's output and "
                    + "the positions it is audited against are that same job's own input, both read "
                    + "from the graph in ConnectionGraph.OnUpdate right after its Complete()");
            }

            EnsureNodeCapacity(count);
            _count = count;
            _sourceIndex = sourceNodeIndex;

            ActiveCount = 0;
            ConnectedCount = 0;
            InRangePairCount = 0;
            OccludedPairCount = 0;
            BodyTestCount = 0;
            _blockedRecorded = 0;
            ResetGateCounters();
            Array.Clear(_occludedPerBody, 0, _occludedPerBody.Length);   // F29 - see Prepare
            OracleReady = false;   // see Prepare

            // Phase 6. This tree is the game's own and no band gate was applied to it, so every edge
            // reads "no band selected" and the renderer falls back to its link colour. Dropping the
            // GUID join here as well is what stops the renderer resolving an index against a tree
            // this method did not build - the same class of generation mixing F36 was.
            _nodeIndexByOwner.Clear();
            ResetRenderTables(count, probeOn: false);

            for (int i = 0; i < count; i++)
            {
                ConnectionGraph.ConnectionGraphJobNode jobNode = gameNodes[i];
                ConnectionGraphNode identity = allNodes[i];

                // Flags, not the identity object's own booleans: the flags are what GetFlagsFrom
                // produced for this job, so they are the values the job gated on.
                bool active = (jobNode.Flags & ConnectionGraphNodeFlags.IsActive) != 0;

                _nodes[i] = new NetworkNodeSnapshot
                {
                    Owner = identity.Owner,
                    Position = jobNode.Position,
                    MaxRange = jobNode.MaxRange,
                    IsActive = active,
                    IsControlSource = (jobNode.Flags & ConnectionGraphNodeFlags.IsControlSource) != 0,
                };

                _previous[i] = previousEdges[i].Index;
                _edgeCostSq[i] = previousEdges[i].Cost;

                // Phase 6: the GUID join, built from the same identity this snapshot was taken from.
                // No ContainsKey guard: a duplicate owner cannot exist (the graph keys its node
                // dictionary by owner and this list is that dictionary's values), and if one ever did,
                // the later index winning is a defensible answer rather than a crash.
                _nodeIndexByOwner[identity.Owner] = i;

                if (active)
                {
                    ActiveCount++;
                }

                if (_previous[i] >= 0)
                {
                    ConnectedCount++;
                }
            }

            CaptureBodies(allNodes, sourceNodeIndex, game);
            CollectNodeStates(game);
            CensusOcclusion();

            // The oracle's control run in the vanilla state (F31), so the master switch's proof is
            // not merely "the game agrees with itself": the same relaxation that ships is run over
            // the captured node list and must reproduce the game's own job. Only the oracle arrays
            // are written - the tree above is the game's and stays the game's.
            if (NetworkConfig.ProbeEnabled && count > 0 && sourceNodeIndex >= 0 && sourceNodeIndex < count)
            {
                // enforceGates: false. The vanilla branch's tree IS the game's - the gate is not
                // applied to it, and a control run never enforces either. This pass's gate counters
                // therefore stay at 0 and the probe says "not enforced" rather than "removed 0".
                Tabulate(
                    applyOcclusion: false,
                    gameMetric: true,
                    enforceGates: false,
                    _oraclePrevious,
                    _oracleEdgeCostSq,
                    _oracleSelectedBand);
                OracleReady = true;
            }
        }

        /// <summary>Marks the pending pass as logged, so the probe logs each pass once.</summary>
        public void ConsumePass() => HasPendingPass = false;

        /// <summary>Forgets the last tree; called when the session's graph goes away.</summary>
        public void Reset()
        {
            _count = 0;
            _sourceIndex = -1;
            _queueLength = 0;
            _bodyCount = 0;
            _occlusionAvailable = false;
            OcclusionEnabled = false;
            HasPendingPass = false;
            ActiveCount = 0;
            ConnectedCount = 0;
            InRangePairCount = 0;
            OccludedPairCount = 0;
            BodyTestCount = 0;
            _blockedRecorded = 0;
            ResetGateCounters();
            Array.Clear(_occludedPerBody, 0, _occludedPerBody.Length);   // F29 - see Prepare
            OracleReady = false;   // see Prepare

            // Phase 6. The GUID join is dropped entirely - after a reset no node index is valid, and
            // a stale entry here would let the renderer colour a line from the tree the game has
            // already thrown away. The selected bands go back to the "no band" sentinel for the same
            // reason (a stale array would otherwise report the previous session's bands).
            _nodeIndexByOwner.Clear();
            for (int i = 0; i < _selectedBand.Length; i++)
            {
                _selectedBand[i] = -1;
            }
        }

        /// <summary>
        /// Clears the per-pass relay/band gate counters and their recorded links.
        /// </summary>
        /// <remarks>
        /// Per-pass state like everything else the probe reports: without this the counters would
        /// accumulate across the game's three-second rebuilds exactly the way the occlusion census did
        /// before F29.
        /// </remarks>
        private void ResetGateCounters()
        {
            _gateConsidered = 0;
            _gateNoPower = 0;
            _gateNoCommonBand = 0;
            _gateBandRange = 0;
            _bandBlockedRecorded = 0;
        }

        /// <summary>
        /// Clears the Phase 6 hand-off tables for the first <paramref name="count"/> nodes: the
        /// per-edge selected bands (back to the <c>-1</c> sentinel) and, when the probe is on, the
        /// per-band part attribution.
        /// </summary>
        /// <param name="count">How many nodes this pass has.</param>
        /// <param name="probeOn">Whether the probe is collecting, i.e. whether the attribution runs.</param>
        /// <remarks>
        /// <para>
        /// <b>The prefix, not the whole array.</b> These tables are only grown, never shrunk, so a
        /// shorter pass than the previous one leaves valid-looking entries past <c>count</c>. Nothing
        /// reads past <c>count</c> - every accessor is indexed by a live node index - so this is a
        /// hygiene rule rather than a correctness one, and it is here because the F29 lesson was
        /// exactly that a stale tail can be read by code that trusts the array.
        /// </para>
        /// <para>
        /// The attribution table is cleared only when the probe is on, because it is only ever written
        /// when the probe is on: clearing it in the normal path would be O(nodes x bands) of work per
        /// rebuild for a table no one reads.
        /// </para>
        /// </remarks>
        private void ResetRenderTables(int count, bool probeOn)
        {
            for (int i = 0; i < count; i++)
            {
                _selectedBand[i] = -1;
                _bandDefaulted[i] = false;
            }

            if (probeOn)
            {
                Array.Clear(_bandCreditors, 0, count * NetworkBands.Count);
            }
        }

        // =========================================================================================
        // The relaxation. Transcription of GetNextConnectedNodesJob.Execute's inner loop, with the
        // relay/band/resource gate added by Phase 5 and the occlusion test kept verbatim.
        // =========================================================================================

        /// <summary>
        /// Runs the relaxation once over the current node snapshot.
        /// </summary>
        /// <param name="applyOcclusion">
        /// Whether the occlusion test runs. <c>false</c> is the oracle's control run (F31); it does
        /// not consult the body snapshot at all.
        /// </param>
        /// <param name="gameMetric">
        /// Whether the optimum is the accumulated cost - the metric <c>GetConnectedNodesJob</c>
        /// itself computes, which this port ships as <see cref="BestPathMode.ShortestKSC"/>. When
        /// <c>false</c> the legacy's <see cref="BestPathMode.NearestRelay"/> single-edge metric is
        /// used instead, and the game has no equivalent of that result to be compared against.
        /// </param>
        /// <param name="enforceGates">
        /// Whether the relay/band gate runs and the pass counters are written. <c>true</c> only for
        /// the tree the graph receives; the oracle's control run passes <c>false</c>, for the same
        /// reason its occlusion is suppressed - <c>GetConnectedNodesJob</c> has no per-pair input, so
        /// the primary verdict must stay a same-input comparison (F31). A gate that bites on the
        /// control tree would make the primary line report DIFFERS for a difference the game's own
        /// algorithm has no way to express.
        /// </param>
        /// <param name="previous">Receives each node's predecessor, or <c>-1</c>.</param>
        /// <param name="edgeCostSq">
        /// Receives each node's edge cost, or <see cref="double.MaxValue"/> when unreachable.
        /// </param>
        /// <remarks>
        /// <para>
        /// <b>Called twice per pass when the probe is on, and the two calls must not share
        /// results.</b> The scratch (<c>_distanceSq</c>, <c>_optimum</c>, <c>_processed</c>,
        /// <c>_queue</c>, <c>_queueLength</c>) is re-initialised at the top of every call, and the
        /// results go wherever the caller says - so the shipped tree and the oracle's control tree
        /// are produced by the same code and kept in different arrays. Before F31 this method wrote
        /// the private fields directly and could only be run once.
        /// </para>
        /// <para>
        /// <b>The gate's position is the legacy's.</b> Source activity is checked before the target
        /// loop; the target's own state and the band match are checked after the pair is known to be
        /// in range, reachable and unprocessed; the occlusion test comes last, exactly as
        /// <c>GetNextConnectedNodesJob</c> ordered them (band match at 196-231, occlusion at 244+).
        /// The source's resource check is evaluated per pair here rather than once per source, which
        /// is the same edge set with per-pair attribution instead of a node-level skip.
        /// </para>
        /// </remarks>
        private void Tabulate(
            bool applyOcclusion,
            bool gameMetric,
            bool enforceGates,
            int[] previous,
            double[] edgeCostSq,
            int[] selectedBand)
        {
            int count = _count;
            int source = _sourceIndex;
            bool nearestRelay = !gameMetric;
            bool occlude = applyOcclusion && _occlusionAvailable;

            ResetScratch(count, previous, edgeCostSq, selectedBand);

            _distanceSq[source] = 0.0;
            _optimum[source] = 0.0;

            // The queue holds every index. The selection is the lowest frontier value, with a
            // swap-back removal - the same shape NativeList.RemoveAtSwapBack gives the game's own job,
            // which is what makes the tie-break between equal-cost nodes identical in both.
            while (_queueLength > 0)
            {
                int queueIndex = 0;
                int lowerIndex = _queue[0];
                double lowerValue = double.MaxValue;

                for (int j = 0; j < _queueLength; j++)
                {
                    int otherIndex = _queue[j];
                    if (!(_distanceSq[otherIndex] < lowerValue))
                    {
                        continue;
                    }

                    lowerValue = _distanceSq[otherIndex];
                    lowerIndex = otherIndex;
                    queueIndex = j;
                }

                int sourceIndex = lowerIndex;
                _queue[queueIndex] = _queue[_queueLength - 1];
                _queueLength--;

                double sourceDistance = _distanceSq[sourceIndex];
                double sourceSqRange = _nodes[sourceIndex].MaxRange * _nodes[sourceIndex].MaxRange;
                _processed[sourceIndex] = true;

                if (!_nodes[sourceIndex].IsActive)
                {
                    continue;
                }

                for (int targetIndex = 0; targetIndex < count; targetIndex++)
                {
                    if (sourceIndex == targetIndex || !_nodes[targetIndex].IsActive)
                    {
                        continue;
                    }

                    double distance = math.distancesq(
                        _nodes[sourceIndex].Position,
                        _nodes[targetIndex].Position);

                    if (!(distance < sourceSqRange)
                        || !(distance < _nodes[targetIndex].MaxRange * _nodes[targetIndex].MaxRange))
                    {
                        continue;
                    }

                    // From here the target is in range of the source; only then does anything
                    // downstream get to remove it.
                    InRangePairCount++;

                    // We reached this pair only to mark it in range - the inverse pair was already
                    // relaxed. (Also: an unreached source has no edge to contribute.)
                    if (sourceDistance == double.MaxValue)
                    {
                        continue;
                    }

                    if (_processed[targetIndex])
                    {
                        continue;
                    }

                    // THE RELAY/BAND GATE (Phase 5), at the legacy's own position: after the pair is
                    // known to be in range, reachable and unprocessed, and before the occlusion test.
                    // It is the shipped tree's alone - see the method's remarks for why the oracle's
                    // control run suppresses it.
                    //
                    // Phase 6: the gate also reports WHICH band it accepted the pair on. -1 when it
                    // was not consulted at all (the oracle's run, or the master switch off) - and
                    // that value only reaches selectedBand[targetIndex] if the pair survives the
                    // occlusion test and the optimum comparison below. See SelectedBandOf.
                    int acceptedBand = -1;
                    if (enforceGates && !PassesRelayGate(sourceIndex, targetIndex, distance, out acceptedBand))
                    {
                        continue;
                    }

                    if (occlude)
                    {
                        int occluder = FirstOccludingBody(sourceIndex, targetIndex);
                        if (occluder >= 0)
                        {
                            OccludedPairCount++;
                            RecordBlockedLink(sourceIndex, targetIndex, occluder);
                            continue;
                        }
                    }

                    double optimum = nearestRelay ? distance : sourceDistance + distance;
                    if (!(optimum < _optimum[targetIndex]))
                    {
                        continue;
                    }

                    _optimum[targetIndex] = optimum;
                    _distanceSq[targetIndex] = sourceDistance + distance;
                    previous[targetIndex] = sourceIndex;
                    edgeCostSq[targetIndex] = distance;

                    // Phase 6, written in the same statement group as the edge it belongs to so the
                    // pair cannot disagree: this is the tree's incoming edge for targetIndex, and
                    // this is the band the gate accepted it on (-1 if the gate never ran). A later
                    // better route overwrites both together.
                    selectedBand[targetIndex] = acceptedBand;
                }
            }
        }

        /// <summary>
        /// Initialises the per-run scratch and the caller's result arrays for one relaxation.
        /// </summary>
        /// <param name="count">The node count.</param>
        /// <param name="previous">The predecessor array the run will fill.</param>
        /// <param name="edgeCostSq">The edge-cost array the run will fill.</param>
        /// <param name="selectedBand">
        /// The per-edge selected-band array the run will fill, with <c>-1</c> for "no band selected".
        /// </param>
        /// <remarks>
        /// The queue holds every index, so this is also where <c>_queueLength</c> is set. Unreachable
        /// entries are <see cref="double.MaxValue"/> here and in
        /// <c>GetConnectedNodesJob.Execute</c>'s own initialisation, which is why the probe treats
        /// that value and the game's <c>+&#8734;</c> as the same "no edge" sentinel (F30).
        /// <para>
        /// <c>selectedBand</c> is cleared here, with the predecessor it belongs to, rather than being
        /// left to the caller: this is the one place both result arrays are initialised, so a run can
        /// no more inherit a previous run's band than it can inherit its edges. It matters for the
        /// oracle specifically - the two runs share every field they do not take as a parameter, and
        /// the F31 discipline is that only the arrays named here can differ.
        /// </para>
        /// </remarks>
        private void ResetScratch(int count, int[] previous, double[] edgeCostSq, int[] selectedBand)
        {
            _queueLength = count;

            for (int i = 0; i < count; i++)
            {
                _distanceSq[i] = double.MaxValue;
                _optimum[i] = double.MaxValue;
                _processed[i] = false;
                _queue[i] = i;
                previous[i] = -1;
                edgeCostSq[i] = double.MaxValue;
                selectedBand[i] = -1;
            }
        }

        /// <summary>
        /// Counts, without enforcing, the occlusion over every active pair - the vanilla-mode census.
        /// </summary>
        /// <remarks>
        /// Deliberately over the ACTIVE pairs rather than over the pairs reachable from the source, so
        /// it is a property of the geometry alone and comparable between the two switch positions even
        /// though the two trees differ.
        /// </remarks>
        private void CensusOcclusion()
        {
            if (!_occlusionAvailable)
            {
                return;
            }

            int count = _count;
            for (int sourceIndex = 0; sourceIndex < count; sourceIndex++)
            {
                if (!_nodes[sourceIndex].IsActive)
                {
                    continue;
                }

                double sourceSqRange = _nodes[sourceIndex].MaxRange * _nodes[sourceIndex].MaxRange;

                for (int targetIndex = 0; targetIndex < count; targetIndex++)
                {
                    if (sourceIndex == targetIndex || !_nodes[targetIndex].IsActive)
                    {
                        continue;
                    }

                    double distance = math.distancesq(
                        _nodes[sourceIndex].Position,
                        _nodes[targetIndex].Position);

                    if (!(distance < sourceSqRange)
                        || !(distance < _nodes[targetIndex].MaxRange * _nodes[targetIndex].MaxRange))
                    {
                        continue;
                    }

                    InRangePairCount++;

                    int occluder = FirstOccludingBody(sourceIndex, targetIndex);
                    if (occluder < 0)
                    {
                        continue;
                    }

                    OccludedPairCount++;
                    RecordBlockedLink(sourceIndex, targetIndex, occluder);
                }
            }
        }

        private int FirstOccludingBody(int sourceIndex, int targetIndex)
        {
            double3 source = _nodes[sourceIndex].Position;
            double3 target = _nodes[targetIndex].Position;

            for (int bodyIndex = 0; bodyIndex < _bodyCount; bodyIndex++)
            {
                OcclusionBody body = _bodies[bodyIndex];
                BodyTestCount++;

                if (!Occlusion.IsOccluded(source, target, body.Position, body.Radius))
                {
                    continue;
                }

                _occludedPerBody[bodyIndex]++;
                return bodyIndex;
            }

            return -1;
        }

        private void RecordBlockedLink(int sourceIndex, int targetIndex, int bodyIndex)
        {
            if (_blockedRecorded >= MaxRecordedBlockedLinks)
            {
                return;
            }

            _blockedSource[_blockedRecorded] = sourceIndex;
            _blockedTarget[_blockedRecorded] = targetIndex;
            _blockedBody[_blockedRecorded] = bodyIndex;
            _blockedRecorded++;
        }

        // =========================================================================================
        // The relay/band gate and the per-node state behind it (Phase 5).
        //
        // The route: a node's Owner is the simulation object's GlobalId, so the state of a node is
        // the state of that object's parts - exactly the identity NetworkProbe already resolves for
        // a node's name. Resolved members, all measured on Assembly-CSharp.dll 2026-09-15 with
        // `monodis --method` under a per-command MONO_PATH prefix: UniverseModel.FindSimObject(IGGuid)
        // [43527], SimulationObjectModel.PartOwner -> PartOwnerComponent [42537],
        // PartOwnerComponent.Parts -> IEnumerable<PartComponent> [41604],
        // PartComponent.TryGetModule<T> [41079], TryGetModuleData<T,U> [41081],
        // Data_NextRelay.HasResourcesToOperate (this port's own ModuleData field),
        // KSP.Modules.Data_Transmitter.CommunicationRange [flist 43755],
        // PartComponentModule_NextModulator.DataModulator (this port's own accessor).
        // =========================================================================================

        private const int BandGateReasonNoPower = 0;
        private const int BandGateReasonNoCommonBand = 1;
        private const int BandGateReasonBandRange = 2;

        /// <summary>
        /// The pair predicate of the relay/band gate: may these two nodes form an edge?
        /// </summary>
        /// <param name="sourceIndex">The relaxed node.</param>
        /// <param name="targetIndex">The candidate target.</param>
        /// <param name="distanceSq">The pair's squared distance, already computed by the caller.</param>
        /// <param name="acceptedBand">
        /// Receives the band index the pair was accepted on, or <c>-1</c> when it was rejected. See
        /// <see cref="SelectedBandOf"/> for what the caller does with it.
        /// </param>
        /// <returns><c>true</c> when the pair passes all three conditions.</returns>
        /// <remarks>
        /// <para>
        /// The three conditions are evaluated in a fixed order - resources, then a shared band, then
        /// that band's range on both sides - so every removal lands in exactly one counter and the
        /// three counters sum to the total. A pair is never counted under two causes, which is what
        /// lets the probe's line be read as three disjoint statements.
        /// </para>
        /// <para>
        /// <b>The accepted band is the first band that passes, in index order, and that is the whole
        /// definition.</b> It is not a preference or a "best" band: the loop's order is the legacy's
        /// <c>NetworkBands</c> order, the mask is the pair's common bands, and the first one whose two
        /// ranges cover the distance ends the search. So a pair that shares bands 1 and 3 and is in
        /// band-1 range reports 1 even though band 3 also works - which is the same answer the legacy's
        /// job recorded as the edge's <c>SelectedBand</c>, because its own band loop walked the same
        /// order over the same masks.
        /// </para>
        /// <para>
        /// <c>acceptedBand</c> is written on every path, so a caller that reuses the variable cannot
        /// read a stale value from the previous pair: it is set to <c>-1</c> at the top, and to the
        /// band index on the one path that returns <c>true</c>.
        /// </para>
        /// </remarks>
        private bool PassesRelayGate(int sourceIndex, int targetIndex, double distanceSq, out int acceptedBand)
        {
            acceptedBand = -1;
            _gateConsidered++;

            // Cause 1 of 3: a node-level fact, evaluated once for the pair and before any per-band
            // work. The legacy checked the source before the target loop and the target inside it;
            // both are the same predicate on the same edge set, so they are one test here.
            if (!_nodes[sourceIndex].HasEnoughResources || !_nodes[targetIndex].HasEnoughResources)
            {
                _gateNoPower++;
                RecordBandBlockedLink(sourceIndex, targetIndex, BandGateReasonNoPower);
                return false;
            }

            // Cause 2 of 3. The masks are non-zero for every active node the state pass reached, so
            // this is the test a band selection actually moves.
            int commonBands = _nodes[sourceIndex].BandsFlags & _nodes[targetIndex].BandsFlags;
            if (commonBands == 0)
            {
                _gateNoCommonBand++;
                RecordBandBlockedLink(sourceIndex, targetIndex, BandGateReasonNoCommonBand);
                return false;
            }

            // Cause 3 of 3: the legacy's loop, verbatim - the FIRST shared band whose two per-band
            // ranges both cover the distance wins, and a pair whose shared bands cover nothing is
            // removed rather than kept at some fallback range.
            for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
            {
                if ((commonBands & NetworkBands.MaskOf(bandIndex)) == 0)
                {
                    continue;
                }

                double sourceBandRange = _bandRanges[sourceIndex * NetworkBands.Count + bandIndex];
                double targetBandRange = _bandRanges[targetIndex * NetworkBands.Count + bandIndex];
                if (distanceSq < sourceBandRange * sourceBandRange
                    && distanceSq < targetBandRange * targetBandRange)
                {
                    acceptedBand = bandIndex;   // Phase 6: the band this edge will be drawn with.
                    return true;
                }
            }

            _gateBandRange++;
            RecordBandBlockedLink(sourceIndex, targetIndex, BandGateReasonBandRange);
            return false;
        }

        private void RecordBandBlockedLink(int sourceIndex, int targetIndex, int reason)
        {
            if (_bandBlockedRecorded >= MaxRecordedBlockedLinks)
            {
                return;
            }

            _bandBlockedSource[_bandBlockedRecorded] = sourceIndex;
            _bandBlockedTarget[_bandBlockedRecorded] = targetIndex;
            _bandBlockedReason[_bandBlockedRecorded] = reason;
            _bandBlockedRecorded++;
        }

        /// <summary>
        /// Fills the relay/band side table for the pass: per node, <c>IsRelay</c>,
        /// <c>HasEnoughResources</c>, a <c>BandsFlags</c> mask and a per-band range.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c>.</param>
        /// <remarks>
        /// <para>
        /// Called from <see cref="Prepare"/> and from <see cref="CaptureVanilla"/>, so a pass reports
        /// the state it was gated on in both switch positions - which is what makes the master
        /// switch's two-mode evidence comparable.
        /// </para>
        /// <para>
        /// <b>Allocation-free per pass.</b> Every buffer is a field: the per-band ranges are one flat
        /// array, the per-node accumulator is one small array cleared per node, and the only list is
        /// the capped, one-shot part-name list for the divergence line. This runs inside the game's
        /// own rebuild.
        /// </para>
        /// <para>
        /// <b>A node the pass cannot read keeps its safe default</b> - every band at its own range -
        /// rather than being cut off. A stale owner mid-load is expected, not an error, so the failure
        /// path is the same as the no-evidence path and neither logs per node.
        /// </para>
        /// </remarks>
        private void CollectNodeStates(GameInstance game)
        {
            int count = _count;

            RelayNodeCount = 0;
            PowerlessNodeCount = 0;
            BandedNodeCount = 0;
            NoBandEvidenceNodeCount = 0;
            TransmitterPartCount = 0;
            ModulatorPartCount = 0;
            AllBandPartCount = 0;
            _bandRangeLiftedEntries = 0;
            Array.Clear(_bandNodeCounts, 0, _bandNodeCounts.Length);

            // The whole array, not count * NetworkBands.Count: this is only ever allocated by
            // EnsureNodeCapacity, which returns early once the node count has stabilised - the same
            // reason the occlusion census needed F29.
            Array.Clear(_bandRanges, 0, _bandRanges.Length);

            RelayPowerEnforced = NetworkConfig.RelaysRequirePowerEnabled && !InfinitePowerEnabled(game);
            bool requirePower = RelayPowerEnforced;
            bool collectPartNames = !_allBandPartsLogged;

            // F52's instrument (Phase 6): name the part behind each band bit, so the probe can print
            // a node's mask together with its cause. Off unless the probe is on - this is the one
            // place in the state pass that touches a string.
            bool collectAttribution = NetworkConfig.ProbeEnabled;
            _recordBandCreditors = collectAttribution;

            UniverseModel universe = null;
            if (game != null)
            {
                try
                {
                    universe = game.UniverseModel;
                }
                catch (Exception)
                {
                    universe = null;
                }
            }

            for (int i = 0; i < count; i++)
            {
                NetworkNodeSnapshot node = _nodes[i];
                bool isRelay = false;
                bool hasEnoughResources = true;
                int bandsFlags = 0;
                bool bandsDefaulted = false;
                Array.Clear(_nodeBandRanges, 0, _nodeBandRanges.Length);
                if (collectAttribution)
                {
                    Array.Clear(_nodeBandCreditors, 0, _nodeBandCreditors.Length);
                }

                try
                {
                    SimulationObjectModel simObject = universe == null
                        ? null
                        : universe.FindSimObject(node.Owner);
                    PartOwnerComponent partOwner = simObject == null ? null : simObject.PartOwner;

                    if (partOwner != null)
                    {
                        foreach (PartComponent part in partOwner.Parts)
                        {
                            if (part == null)
                            {
                                continue;
                            }

                            // Phase 6: which part is currently being folded in, for the band
                            // attribution only. Read here rather than inside AccumulatePartState so
                            // the property is touched once per part, and only when the probe wants it.
                            _currentPartName = collectAttribution ? part.PartName : null;

                            AccumulatePartState(
                                part,
                                requirePower,
                                collectPartNames,
                                ref isRelay,
                                ref hasEnoughResources,
                                ref bandsFlags);
                        }
                    }
                }
                catch (Exception exception)
                {
                    // One line per session, no per-node logging: a half-built owner during a load is
                    // the expected case, and the node keeps the safe default below either way.
                    WarnNodeStateOnce(exception);
                }

                if (bandsFlags == 0)
                {
                    // Divergence (b), node level. No evidence is not "no band": a node whose parts
                    // could not be read - or which has no transmitter at all - is credited every band
                    // at its own range instead of being severed. See the file header.
                    NoBandEvidenceNodeCount++;
                    bandsFlags = NetworkBands.AllBandsMask;
                    bandsDefaulted = true;
                    double nodeRange = node.MaxRange > 0.0 ? node.MaxRange : 0.0;
                    for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
                    {
                        _nodeBandRanges[bandIndex] = nodeRange;
                    }
                }
                else
                {
                    // Divergence (c): the node's own range may exceed every part's - the KSC range
                    // override is exactly that - and every band the node offers inherits the excess,
                    // so the gate can never undo a reach the node's own range test allowed.
                    double maxCredited = 0.0;
                    for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
                    {
                        if (_nodeBandRanges[bandIndex] > maxCredited)
                        {
                            maxCredited = _nodeBandRanges[bandIndex];
                        }
                    }

                    double lift = node.MaxRange - maxCredited;
                    if (lift > 0.0)
                    {
                        for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
                        {
                            if ((bandsFlags & NetworkBands.MaskOf(bandIndex)) == 0)
                            {
                                continue;
                            }

                            _nodeBandRanges[bandIndex] += lift;
                            _bandRangeLiftedEntries++;
                        }
                    }
                }

                node.IsRelay = isRelay;
                node.HasEnoughResources = hasEnoughResources;
                node.BandsFlags = bandsFlags;
                _nodes[i] = node;

                for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
                {
                    _bandRanges[i * NetworkBands.Count + bandIndex] = _nodeBandRanges[bandIndex];
                    if ((bandsFlags & NetworkBands.MaskOf(bandIndex)) != 0)
                    {
                        _bandNodeCounts[bandIndex]++;
                    }

                    if (collectAttribution)
                    {
                        // F52: the part that credited this bit, or null when the default did. A
                        // bit set with no creditor is a bug in the instrument, not a reading, and
                        // the probe prints it as "?" so that is visible rather than silent.
                        _bandCreditors[i * NetworkBands.Count + bandIndex] = _nodeBandCreditors[bandIndex];
                    }
                }

                if (collectAttribution)
                {
                    _bandDefaulted[i] = bandsDefaulted;
                }

                if (isRelay)
                {
                    RelayNodeCount++;
                }

                if (!hasEnoughResources)
                {
                    PowerlessNodeCount++;
                }

                if (bandsFlags != 0)
                {
                    BandedNodeCount++;
                }
            }

            LogAllBandPartsOnce();
        }

        /// <summary>
        /// Folds one part into this node's relay/band accumulators. The legacy's
        /// <c>RefreshCommNetNode</c> loop body, part for part.
        /// </summary>
        /// <param name="part">The part to read.</param>
        /// <param name="requirePower">Whether the relay's own resource verdict gates a relay part this pass.</param>
        /// <param name="collectPartNames">
        /// Whether an all-bands credit should record the part's name for the one-shot divergence line.
        /// </param>
        /// <param name="isRelay">Accumulates "this node has an enabled relay".</param>
        /// <param name="hasEnoughResources">Accumulates "every relay part can operate".</param>
        /// <param name="bandsFlags">Accumulates the node's bands.</param>
        /// <remarks>
        /// The band ranges themselves accumulate into <see cref="_nodeBandRanges"/>, one entry per
        /// band, because the caller clears it per node and writes it into the flat per-pass array.
        /// </remarks>
        private void AccumulatePartState(
            PartComponent part,
            bool requirePower,
            bool collectPartNames,
            ref bool isRelay,
            ref bool hasEnoughResources,
            ref int bandsFlags)
        {
            Data_NextRelay relayData = null;
            bool partIsRelay = false;
            if (part.TryGetModuleData<PartComponentModule_NextRelay, Data_NextRelay>(out relayData)
                && relayData != null
                && relayData.EnableRelay != null)
            {
                partIsRelay = relayData.EnableRelay.GetValue();
                isRelay |= partIsRelay;
            }

            PartComponentModule_DataTransmitter transmitter;
            if (!part.TryGetModule<PartComponentModule_DataTransmitter>(out transmitter) || transmitter == null)
            {
                return;
            }

            TransmitterPartCount++;

            // The legacy ANDed `data.HasResourcesToOperate` here, its own loop's verdict, and this port
            // asks the same field of the same data class - the relay's own `ModuleData` owns the
            // resource request again (`Data_NextRelay.SetupResourceRequest`), so that flag is again the
            // honest reading of the same fact. It is still asked only of a part that IS a relay: a
            // plain antenna must not be able to make its whole node unreachable.
            //
            // NOT `IsTransmitterActive()`: that member answers "is this dish deployed"
            // (`if (!_requiresDeployment) return true; return _dataDeployable.IsExtended;`), so a
            // folded antenna would have read as a starved relay. See the file header.
            if (partIsRelay && requirePower && relayData != null)
            {
                hasEnoughResources &= relayData.HasResourcesToOperate;
            }

            Data_Transmitter transmitterData;
            if (!part.TryGetModuleData<PartComponentModule_DataTransmitter, Data_Transmitter>(out transmitterData)
                || transmitterData == null)
            {
                return;
            }

            // `data.CommunicationRange`, not `transmitter.CommunicationRangeMeters`: the latter is
            // what the legacy read, but it is a derived property, while the field is what this port's
            // Lua rebalance writes and therefore what every node's own range is built from - and the
            // band test must not disagree with the range test the engine already ran.
            double range = transmitterData.CommunicationRange;
            if (!(range > 0.0))
            {
                // A transmitter that reports no range offers no band. Only the mask bit is
                // range-bearing, so a zero range cannot set one - the gate's contract.
                return;
            }

            PartComponentModule_NextModulator modulator;
            Data_NextModulator modulatorData = null;
            if (part.TryGetModule<PartComponentModule_NextModulator>(out modulator) && modulator != null)
            {
                modulatorData = modulator.DataModulator;
            }

            if (modulatorData == null)
            {
                // Divergence (b), part level: a transmitter with no modulator takes every band. The
                // legacy left such a part dark, which severs any part the Lua patch did not reach.
                CreditAllBands(range, ref bandsFlags);
                AllBandPartCount++;
                if (collectPartNames)
                {
                    CollectAllBandPart(part);
                }

                return;
            }

            ModulatorPartCount++;

            if (modulatorData.OmniBand != null && modulatorData.OmniBand.GetValue())
            {
                CreditAllBands(range, ref bandsFlags);
                return;
            }

            if (modulatorData.Band != null)
            {
                CreditBand(NetworkBands.GetBandIndex(modulatorData.Band.GetValue()), range, ref bandsFlags);
            }

            if (modulatorData.SecondaryBand != null)
            {
                CreditBand(
                    NetworkBands.GetBandIndex(modulatorData.SecondaryBand.GetValue()),
                    range,
                    ref bandsFlags);
            }
        }

        /// <summary>Credits one band at a range, max-combining with whatever this node already has.</summary>
        /// <param name="bandIndex">The band, or <c>-1</c> for a code this build does not know.</param>
        /// <param name="range">The transmitter's range in metres.</param>
        /// <param name="bandsFlags">The node's mask, updated in place.</param>
        private void CreditBand(int bandIndex, double range, ref int bandsFlags)
        {
            if (bandIndex < 0)
            {
                // An unknown code - a save from a build with more bands, or the empty second band -
                // contributes nothing. -1 is the legacy's sentinel and its callers tested for it.
                return;
            }

            if (range > _nodeBandRanges[bandIndex])
            {
                _nodeBandRanges[bandIndex] = range;
            }

            bandsFlags |= NetworkBands.MaskOf(bandIndex);

            if (_recordBandCreditors)
            {
                // Phase 6 / F52. The LAST part to credit this band on this node, which is the part
                // the probe should name when it explains the mask: every part that offers a band
                // offers the node that band, so naming one of them is enough to answer "where did
                // this bit come from", and the last is the one the loop just read.
                _nodeBandCreditors[bandIndex] = _currentPartName;
            }
        }

        /// <summary>Credits every band at one range - the omni case and the divergence's default.</summary>
        /// <param name="range">The transmitter's range in metres.</param>
        /// <param name="bandsFlags">The node's mask, updated in place.</param>
        private void CreditAllBands(double range, ref int bandsFlags)
        {
            for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
            {
                CreditBand(bandIndex, range, ref bandsFlags);
            }
        }

        /// <summary>
        /// Whether the InfinitePower difficulty option is on - the check that replaces the legacy's
        /// <c>DifficultyUtils.HasInfinitePower</c>, which does not exist on 0.2.8.5.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c>.</param>
        /// <returns><c>true</c> only when the option is readable and enabled.</returns>
        /// <remarks>
        /// <c>SessionManager.IsDifficultyOptionEnabled(string)</c>, resolved at method line 30228, with
        /// the option id <c>"InfinitePower"</c> - a real id in the assembly
        /// (<c>strings -a Assembly-CSharp.dll | grep -cx InfinitePower</c> = 1) and the same call the
        /// in-game-validated sibling port makes
        /// (<c>mods/OrbitalSurvey/.../PartComponentModule_OrbitalSurvey.cs:197</c>). The cheat menu
        /// also has its own toggle (<c>GameInstance.CheatSystem.GetInfiniteElectricity()</c>, method
        /// line 784); the difficulty option is preferred because it is the campaign-level statement
        /// and because it is the shape already proved in game here.
        /// </remarks>
        private static bool InfinitePowerEnabled(GameInstance game)
        {
            if (game == null)
            {
                return false;
            }

            try
            {
                SessionManager session = game.SessionManager;
                return session != null && session.IsDifficultyOptionEnabled(InfinitePowerOptionId);
            }
            catch (Exception)
            {
                // Mid-load, or no session yet. "Power matters" is the safe reading: it is the shipped
                // default and the one the relay mechanic is defined by.
                return false;
            }
        }

        /// <summary>The difficulty option id for infinite electricity, as the game spells it.</summary>
        public const string InfinitePowerOptionId = "InfinitePower";

        private void CollectAllBandPart(PartComponent part)
        {
            if (_allBandParts.Count >= MaxDivergencePartsNamed)
            {
                return;
            }

            string name = part.PartName;
            _allBandParts.Add(string.IsNullOrEmpty(name) ? "<unnamed part>" : name);
        }

        /// <summary>
        /// Logs, once per session, which parts the missing-modulator divergence covered.
        /// </summary>
        /// <remarks>
        /// The relaxation is a deliberate divergence from the legacy, and a relaxation nobody can see
        /// is indistinguishable from a bug - so the first pass that uses it says so, names the parts
        /// (capped), and never mentions it again. Cleared rather than merely latched, so the list
        /// cannot grow for the rest of the session.
        /// </remarks>
        private void LogAllBandPartsOnce()
        {
            if (_allBandPartsLogged)
            {
                return;
            }

            if (_allBandParts.Count == 0)
            {
                return;
            }

            _allBandPartsLogged = true;
            _log("relay-gate: " + AllBandPartCount
                + " part(s) carry a transmitter and no modulator, so they were credited with every "
                + "band at their own range instead of being left dark (the legacy left such a part "
                + "with no bands at all - see this file's header, divergence (b)): "
                + string.Join(", ", _allBandParts.ToArray())
                + (_allBandParts.Count < AllBandPartCount
                    ? ", ... (" + (AllBandPartCount - _allBandParts.Count) + " more not named)"
                    : string.Empty));
            _allBandParts.Clear();
        }

        private void WarnNodeStateOnce(Exception exception)
        {
            if (_warnedNodeState)
            {
                return;
            }

            _warnedNodeState = true;
            _warn("could not read a node's parts while collecting relay/band state ("
                + exception.GetType().Name + ": " + exception.Message
                + "); nodes that fail are treated as offering every band at their own range, so the "
                + "gate never severs a node it could not read");
        }

        /// <summary>
        /// Logs how long the pass took and what it covered, at most once every
        /// <see cref="ProfileLogIntervalSeconds"/>.
        /// </summary>
        /// <param name="profileStart">
        /// The <see cref="Stopwatch.GetTimestamp"/> taken at the top of the pass, or <c>0</c> when the
        /// setting was off - in which case this does nothing.
        /// </param>
        /// <remarks>
        /// The legacy's own line and throttle (<c>GetNextConnectedNodesJob.Execute</c>: "Execute took
        /// ...ms (nodes=..., connected=.../..., relays=...)", at most once every 4 seconds). At Info,
        /// because <c>LogDebug</c> never reaches <c>Ksp2.log</c>. The relay count is this port's
        /// addition: the node-state pass that computes it is what the pass now pays for.
        /// </remarks>
        private void LogPassProfile(long profileStart)
        {
            if (profileStart == 0L)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            double elapsedMs = (now - profileStart) * 1000.0 / Stopwatch.Frequency;

            if (_lastProfileLogTimestamp != 0L
                && now - _lastProfileLogTimestamp < (long)(Stopwatch.Frequency * ProfileLogIntervalSeconds))
            {
                return;
            }

            _lastProfileLogTimestamp = now;
            _log("network pass: took " + elapsedMs.ToString("0.###", CultureInfo.InvariantCulture)
                + "ms (nodes=" + _count
                + " active=" + ActiveCount
                + " connected=" + ConnectedCount + "/" + _count
                + " relays=" + RelayNodeCount
                + " powerless=" + PowerlessNodeCount
                + " band-gate-removed=" + BandGateRemovedPairCount
                + ", at most one line every " + ProfileLogIntervalSeconds + "s)");
        }

        /// <summary>How often the pass-duration line may print, in seconds. The legacy's 4.</summary>
        public const double ProfileLogIntervalSeconds = 4.0;

        // =========================================================================================
        // The body snapshot.
        // =========================================================================================

        private void CaptureBodies(List<ConnectionGraphNode> nodes, int sourceNodeIndex, GameInstance game)
        {
            _bodyCount = 0;
            _occlusionAvailable = false;
            OcclusionEnabled = false;

            double factor = NetworkConfig.OcclusionFactor;
            if (!(factor > 0.0))
            {
                // Occlusion is switched off by the user. Not a failure - and worth saying plainly,
                // because it is the configuration the oracle block needs.
                return;
            }

            OcclusionEnabled = true;

            if (game == null)
            {
                WarnOnce(ref _warnedNoGame, "no game instance at rebuild time; occlusion is skipped");
                return;
            }

            ITransformFrame frame = ResolveGraphFrame(nodes, sourceNodeIndex, game);
            if (frame == null)
            {
                return;
            }

            List<CelestialBodyComponent> bodies;
            try
            {
                UniverseModel universe = game.UniverseModel;
                if (universe == null)
                {
                    WarnOnce(ref _warnedNoUniverse, "no universe model at rebuild time; occlusion is skipped");
                    return;
                }

                bodies = universe.GetAllCelestialBodies();
            }
            catch (Exception exception)
            {
                WarnOnce(ref _warnedBodyQuery,
                    "could not enumerate celestial bodies (" + exception.GetType().Name + "); "
                    + "occlusion is skipped");
                return;
            }

            if (bodies == null || bodies.Count == 0)
            {
                WarnOnce(ref _warnedNoBodies, "the universe reports no celestial bodies; occlusion is skipped");
                return;
            }

            EnsureBodyCapacity(bodies.Count);
            bool probe = NetworkConfig.ProbeEnabled;

            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBodyComponent body = bodies[i];
                if (body == null)
                {
                    continue;
                }

                // radius * factor - 1000. Not > 0 is a skip, which is the fix recorded on
                // NetworkConfig.OcclusionRadius: the legacy kept such a body with a negative radius,
                // whose square is positive, so it occluded a 1 km sphere at the body's centre.
                double radius = body.radius * factor - SeaLevelTerrainTolerance;
                if (!(radius > 0.0))
                {
                    continue;
                }

                ITransformModel transform = body.transform;
                if (transform == null)
                {
                    continue;
                }

                _bodies[_bodyCount] = new OcclusionBody
                {
                    Name = probe ? body.bodyName : null,
                    Position = frame.ToLocalPosition(transform.Position),
                    Radius = radius,
                    RealRadius = body.radius,
                };
                _bodyCount++;
            }

            _occlusionAvailable = _bodyCount > 0;

            if (!_occlusionAvailable)
            {
                WarnOnce(ref _warnedNoUsableBodies,
                    "no celestial body has a usable occlusion radius at factor " + factor
                    + "; occlusion is skipped");
            }
        }

        /// <summary>
        /// Resolves the frame the graph's node positions are expressed in.
        /// </summary>
        /// <param name="nodes">The node list under rebuild.</param>
        /// <param name="sourceNodeIndex">The control source's index.</param>
        /// <param name="game">The live game instance.</param>
        /// <returns>The control source's celestial frame, or <c>null</c> when it cannot be resolved.</returns>
        /// <remarks>
        /// The legacy's route, kept exactly: <c>CommNetManager.GetSourceNode()</c> names the control
        /// source, <c>SpaceSimulation.FindSimObject</c> gets its simulation object, and the
        /// <c>TransformModel</c> cast yields <c>celestialFrame</c>. The node list's own entry is the
        /// fallback if the manager's source node is not available, so a session that is mid-load still
        /// produces a frame rather than an exception.
        /// </remarks>
        private ITransformFrame ResolveGraphFrame(
            List<ConnectionGraphNode> nodes,
            int sourceNodeIndex,
            GameInstance game)
        {
            IGGuid owner = default;
            bool haveOwner = false;

            try
            {
                SessionManager session = game.SessionManager;
                CommNetManager commNet = session == null ? null : session.CommNetManager;
                ConnectionGraphNode sourceNode = commNet == null ? null : commNet.GetSourceNode();
                if (sourceNode != null)
                {
                    owner = sourceNode.Owner;
                    haveOwner = true;
                }
            }
            catch (Exception exception)
            {
                WarnOnce(ref _warnedSourceNode,
                    "could not read the control source node (" + exception.GetType().Name + ")");
            }

            if (!haveOwner && nodes != null && sourceNodeIndex >= 0 && sourceNodeIndex < nodes.Count)
            {
                owner = nodes[sourceNodeIndex].Owner;
                haveOwner = true;
            }

            if (!haveOwner)
            {
                WarnOnce(ref _warnedNoSourceOwner,
                    "no control source owner to frame the occlusion geometry in; occlusion is skipped");
                return null;
            }

            SimulationObjectModel simObject;
            try
            {
                SpaceSimulation spaceSimulation = game.SpaceSimulation;
                simObject = spaceSimulation == null ? null : spaceSimulation.FindSimObject(owner);
            }
            catch (Exception exception)
            {
                WarnOnce(ref _warnedFindSimObject,
                    "could not find the control source's simulation object ("
                    + exception.GetType().Name + "); occlusion is skipped");
                return null;
            }

            if (simObject == null)
            {
                WarnOnce(ref _warnedNoSimObject,
                    "the control source has no simulation object yet; occlusion is skipped");
                return null;
            }

            TransformModel transform = simObject.transform as TransformModel;
            if (transform == null)
            {
                WarnOnce(ref _warnedNotTransformModel,
                    "the control source's transform is not a TransformModel; occlusion is skipped");
                return null;
            }

            ITransformFrame frame = transform.celestialFrame;
            if (frame == null)
            {
                WarnOnce(ref _warnedNoFrame,
                    "the control source has no celestial frame; occlusion is skipped");
                return null;
            }

            return frame;
        }

        // =========================================================================================
        // Growth and one-shot warning latches.
        // =========================================================================================

        private void EnsureNodeCapacity(int count)
        {
            if (_nodes.Length >= count)
            {
                return;
            }

            _nodes = new NetworkNodeSnapshot[count];
            _distanceSq = new double[count];
            _optimum = new double[count];
            _processed = new bool[count];
            _queue = new int[count];
            _previous = new int[count];
            _edgeCostSq = new double[count];
            _oraclePrevious = new int[count];
            _oracleEdgeCostSq = new double[count];

            // Phase 6. `_selectedBand` is initialised to -1 rather than left at the array's default
            // of 0: 0 is a real band index, so a fresh array left as-is would claim every edge was
            // gated on band X. `_oracleSelectedBand` is never read, but it is cleared by the same
            // ResetScratch call the oracle's run makes, so it needs the same sentinel.
            _selectedBand = new int[count];
            _oracleSelectedBand = new int[count];
            for (int i = 0; i < count; i++)
            {
                _selectedBand[i] = -1;
                _oracleSelectedBand[i] = -1;
            }

            // One flat table for every node's per-band ranges: node * NetworkBands.Count + band.
            // Grown with the rest, which is what lets CollectNodeStates clear the whole array rather
            // than a prefix - the F29 lesson.
            _bandRanges = new double[count * NetworkBands.Count];

            // F52's attribution table. A fresh string array is all-null, which is the correct
            // "nobody credited this" value, so no initialisation loop is needed.
            _bandCreditors = new string[count * NetworkBands.Count];
            _bandDefaulted = new bool[count];
        }

        private void EnsureBodyCapacity(int count)
        {
            if (_bodies.Length >= count)
            {
                return;
            }

            _bodies = new OcclusionBody[count];
            _occludedPerBody = new int[count];
        }

        private bool _warnedNoGame;
        private bool _warnedNoUniverse;
        private bool _warnedNoBodies;
        private bool _warnedNoUsableBodies;
        private bool _warnedBodyQuery;
        private bool _warnedSourceNode;
        private bool _warnedNoSourceOwner;
        private bool _warnedFindSimObject;
        private bool _warnedNoSimObject;
        private bool _warnedNotTransformModel;
        private bool _warnedNoFrame;
        private bool _warnedUnalignedCapture;
        private bool _routeLogged;
        private bool _vanillaCaptureLogged;

        /// <summary>
        /// Logs a warning at most once per flag.
        /// </summary>
        /// <param name="flag">The latch; set to <c>true</c> by this call.</param>
        /// <param name="message">The message.</param>
        /// <remarks>
        /// The graph rebuilds every three seconds; an unlatched warning here would produce twenty lines
        /// a minute for the rest of the session and bury everything else.
        /// </remarks>
        private void WarnOnce(ref bool flag, string message)
        {
            if (flag)
            {
                return;
            }

            flag = true;
            _warn(message);
        }
    }
}
