// CommNextRedux - the D9 diagnostic probe.
//
// LEGACY PROVENANCE
//   The legacy's equivalent was a two-line summary inside the job
//   (GetNextConnectedNodesJob.Execute: "Execute took ...ms (nodes=..., numBodyOcclusions=...,
//   numIntersections=..., connected=.../..., relays=...)"), gated on PluginSettings.EnableProfileLogs.
//   That told you how long the pass took. It could not tell you WHICH body occluded WHICH link, and
//   it could not tell you whether the answer agreed with the game's - which in a port whose whole
//   risk is "the re-implemented algorithm is subtly not the game's" is the only question worth asking.
//
// WHAT THIS PROBE IS FOR
//   Phase 3's deliverable is evidence, and occlusion is invisible on screen until a later phase draws
//   it. So the observable artefact of this phase is a log block, and it has to be falsifiable. Each
//   pass prints five things, and the third and fifth are the ones that can fail:
//
//     1. the pass header  - which mode produced this graph, how many nodes, which source;
//     2. the tree         - connected count and the occlusion census;
//     3. the occlusion verdicts, each naming the body that did the blocking;
//     4. the selected path for the active vessel;
//     5. the oracle       - two lines, see below.
//
// THE ORACLE HAS TWO LINES AND THEY ANSWER DIFFERENT QUESTIONS (F31)
//
//   oracle-algorithm   THE PRIMARY VERDICT, and the one the acceptance criteria below are about. It
//                      runs the game's own GetConnectedNodesJob and compares it against THIS PORT'S
//                      OWN RELAXATION - the code that builds the graph - on the same node array,
//                      with occlusion suppressed and the metric pinned to the game's own accumulated
//                      cost (which this port ships as BestPathMode.ShortestKSC). That is the
//                      same-input comparison: "given identical input, my Dijkstra agrees with the
//                      game's algorithm" is exactly what the deep route (D8) has to earn, and this
//                      line is that claim measured. Its verdicts are EXACT, DIFFERS and
//                      INVENTED-CONNECTIVITY - and DIFFERS means only what it says, because with the
//                      input pinned there is no "by design" difference left for it to hide in.
//
//                      (The criterion this file used to state - "mode=on, anything else -> SUBSET is
//                      the ceiling" - was unreachable whenever the feature worked: SUBSET required
//                      predecessorDiffers == 0, and removing a link that was on a best path
//                      NECESSARILY changes a predecessor. A stale criterion is what made this
//                      oracle's verdict unreadable, so it is recorded here rather than deleted.)
//
//   oracle-removals    The occlusion's effect, and the containment property, on the tree the graph
//                      ACTUALLY received. Per edge it re-derives the pair from the pass's own
//                      geometry and reports whether the edge this port used is an in-range pair in
//                      the game's own edge set (invented-edges must be 0), whether the stored cost is
//                      that pair's squared distance (cost-mismatch must be 0), and the reachability
//                      counters against the game's job. It is EXPECTED to differ from the game's tree
//                      whenever occlusion bites, and it says ONLY-REMOVALS rather than presenting a
//                      difference as a bug.
//
//                      ITS VERDICTS, ONE PER CONDITION (F37 - they used to share one label):
//                        EXACT                 every node agrees with the game's own job - which this
//                                              line can only claim when the configured metric is the
//                                              game's own (otherwise the shipped tree legitimately
//                                              differs for a reason that is not occlusion) or when
//                                              the master switch is off, in which case the shipped
//                                              tree IS the game's own captured tree;
//                        ONLY-REMOVALS         every edge is the game's and nothing this port reaches
//                                              is out of the game's reach; what is left is the pairs
//                                              occlusion removed, named by pathMode=;
//                        INVENTED-CONNECTIVITY the tree reaches a node the game's own algorithm does
//                                              not reach. Never acceptable on any line;
//                        INVENTED-EDGE         an edge of the tree is not an in-range pair of active
//                                              nodes, i.e. not in the game's own edge set. Never
//                                              acceptable;
//                        COST-MISMATCH         a real, in-range edge whose stored cost is not that
//                                              pair's squared distance. Never acceptable.
//
//                      The three failures above are the three ways the containment property can break,
//                      and the counters (mine-only, invented-edges, cost-mismatch) stay on the line so
//                      a verdict never has to be taken on trust. Before F37 all three printed
//                      INVENTED-CONNECTIVITY, which is how a working master switch came to report the
//                      worst verdict the contract has - and it is the same class of defect as F31: a
//                      label that cannot express what actually happened.
//
//   GENERATION ALIGNMENT (F36) - THE INPUT TO Removals, and to the algorithm line's cost column.
//   This audit compares a stored edge cost against the squared distance of the two positions that
//   edge joins, so those two things must come from ONE rebuild. They do, because the vanilla snapshot
//   is taken in ConnectionGraph.OnUpdate immediately after the job's own Complete(), where
//   _previousEdges is the job's output and _nodes is the same job's input (NetworkEngine.CaptureVanilla
//   owns the evidence and the reasoning). Capturing in RebuildConnectionGraph's postfix instead - which
//   is where this file's input used to come from - pairs the previous rebuild's costs with the next
//   rebuild's positions; the satellites move by kilometres in between and every edge reads as a cost
//   mismatch. That is a defect in the evidence's input, never in this comparison: do not "fix" a cost
//   mismatch by loosening the comparison.
//
//   WHY THE JOB IS NOT HANDED THE POST-OCCLUSION EDGE SET. GetConnectedNodesJob forms its edges from
//   pairwise positions and PER-NODE ranges; its only lever is a node's own MaxRange, and it has no
//   per-pair input at all (the reason the deep route is forced - NetworkEngine's D8 header). A
//   filtered edge set therefore cannot be handed to it, and a comparison of the shipped tree against
//   it is a comparison of two different inputs whenever occlusion bites. So the proof is decomposed
//   instead of faked: oracle-algorithm proves the relaxation IS the game's algorithm on the game's
//   edge set, and oracle-removals proves the shipped tree uses only edges out of that set. Together
//   they say the graph this mod publishes is the game's algorithm applied to the game's edge set with
//   the blocked pairs removed - which is the feature, stated as two claims that can each fail.
//
//   UNREACHABLE IS NOT A COST. The game's job initialises an unreachable edge to +Infinity and this
//   port uses double.MaxValue for the same state. They are two encodings of "no edge", and comparing
//   them as values is not a disagreement (F30, measured in L3 at the source node:
//   "cost 1.7976931348623157E+308 (this port) vs Infinity (the game)"). Either side's unset cost is
//   treated as unset; genuine finite costs are still compared exactly, which is the half that can
//   actually catch a bug.
//
//   ACCEPTANCE CRITERIA for the L5 re-run matrix (L3/L4's, with F36/F37 applied):
//
//     Occlusion radius = 0                 -> oracle-algorithm verdict=EXACT
//                                             (proves the Dijkstra + metric match the game's)
//     Occlusion radius = 0.98 (the default) -> oracle-algorithm verdict=EXACT, and the removals line
//                                             reports occluded-pairs > 0 with invented-edges=0 and
//                                             cost-mismatch=0, verdict=ONLY-REMOVALS
//                                             (proves the occlusion integration); the only-removals
//                                             check IS that verdict plus its invented-edges /
//                                             cost-mismatch / mine-only counters
//     EnableCommNextNetwork = false        -> BOTH lines verdict=EXACT (the game against itself: the
//                                             captured tree is the game's own, and this port's
//                                             relaxation reproduces the game's job on the captured
//                                             nodes) - the master-switch proof
//
//   THE RELAY/BAND/RESOURCE GATE AND WHAT IT DOES TO THESE LINES (Phase 5)
//
//   Phase 5 puts a per-pair predicate in front of the occlusion test: two nodes may connect only if
//   both have resources, their band masks intersect, and a shared band covers the distance on both
//   sides. It is a THIRD thing that can remove an edge, so it gets its own line and its own counters -
//   and the lines above keep their exact meanings, for two reasons that must not be confused:
//
//     * the ORACLE'S CONTROL TREE IS UNGATED by construction. The algorithm line's comparison runs the
//       port's own relaxation with occlusion suppressed AND the gate suppressed, because the game's
//       GetConnectedNodesJob has no per-pair input at all: a gated control tree would be a different
//       input from the game's, and the primary verdict would report DIFFERS for a difference the
//       game's algorithm cannot express. So the algorithm line measures the algorithm, never the gate.
//     * the SHIPPED TREE is gated, so the removals line can legitimately report more removals than
//       occlusion accounts for. Its ONLY-REMOVALS verdict stays true - it says every edge is one of the
//       game's and nothing this port reaches is out of the game's reach - but the CAUSE of a removal is
//       no longer unambiguous, which is why its line carries the gate's removal count beside
//       occluded-pairs and names the gate when that count is not zero.
//
//   WHICH IS ALSO WHY THE GATE CANNOT INVALIDATE THE ORACLE ON A DEFAULT INSTALL. The gate is a no-op
//   whenever no band selection has been made: every patched transmitter carries the default band (X),
//   and the port credits a transmitter whose part has no modulator the same band at the same range
//   (NetworkEngine's divergence (b)), so every active node has a non-zero mask containing band X and
//   the mask test cannot fail; the per-band range for that band is the node's own range, lifted to it
//   when the node's MaxRange exceeds its parts' (divergence (c)), so the band-range test cannot fail
//   either; and RelaysRequirePower's resource half only bites on a relay that cannot run. THE
//   CONDITION IS THE DEFAULT BAND SET AND NOTHING ELSE: while `NetworkBands` carries band X alone,
//   `BandsFlags` is non-zero for every active node, the gate removes no pair, and both oracle lines
//   keep their exact meanings - which is what makes a default install the L3/L4 matrix with one more
//   test that passes. A second band in the set, or a user band selection, is the first configuration
//   in which the gate can bite, and this line is where that shows up. The gate's own line prints
//   enforced= so a run in which it did not run is never read as a run in which it found nothing.
//
//   A FAILING REMOVALS LINE SAYS WHICH FAILURE IT IS: INVENTED-CONNECTIVITY (mine-only - the tree
//   reaches further than the game's algorithm), INVENTED-EDGE (an edge outside the game's edge set) or
//   COST-MISMATCH (a real edge with the wrong number on it). All three are never acceptable, in any
//   configuration, on either line. The algorithm line's own three - EXACT, DIFFERS and
//   INVENTED-CONNECTIVITY for mine-only - are unchanged: it has no edge audit, so no conflation was
//   possible there.
//
//   Note what the criteria do NOT depend on: the configured path mode. That is deliberate. The oracle
//   pins the metric to the game's own because the game computes no other, and NearestRelay (the
//   legacy's metric) has no game-side equivalent to be vouched for. The removals line prints
//   pathMode=, so a mode-driven difference can never be misread as a defect - which is the failure
//   this file was rewritten to remove. Run the matrix with Best path mode = ShortestKSC and the
//   removals line is EXACT wherever the tree is untouched, which makes the occlusion effect the only
//   variable left in the comparison.
//
// WHY IT RUNS FROM Update() AND NOT FROM THE PATCH
//   The prefix replaces a method the game calls roughly every three seconds. Running the oracle from
//   inside it would put a second full graph tabulation on the game's own rebuild path, and it would
//   hold the config read, the name lookups and the string formatting inside the one place where a
//   slow frame is visible. The prefix's job is to publish the result; the probe's job is to describe
//   it. This is the sibling port's shape (CommLinesRedux's DiagnosticProbe.Tick) and it is the shape
//   the rule layer asks for: observe from your own tick, do not widen the patch.

using System;
using System.Collections.Generic;
using CommNextRedux.Network;
using CommNextRedux.Network.Bands;
using CommNextRedux.Rendering;
using KSP.Game;
using KSP.Sim;
using KSP.Sim.impl;
using Unity.Collections;
using Unity.Mathematics;

namespace CommNextRedux.Diagnostics
{
    /// <summary>
    /// The connection-graph diagnostic probe: one block per graph rebuild, with an oracle.
    /// </summary>
    public sealed class NetworkProbe
    {
        /// <summary>Most node names resolved in one pass, as a guard against a pathological node count.</summary>
        private const int MaxNameLookups = 512;

        /// <summary>Most nodes shown in one path line.</summary>
        private const int MaxPathNodesShown = 16;

        /// <summary>Most "this link was blocked by" lines printed per pass.</summary>
        private const int MaxBlockedLinkLines = 8;

        /// <summary>Most oracle disagreement lines printed per comparison.</summary>
        private const int MaxOracleLines = 6;

        /// <summary>Most nodes whose band attribution is printed in one pass.</summary>
        private const int MaxBandAttributionLines = 8;

        /// <summary>Most occlusion-geometry crossing lines printed in one frame.</summary>
        private const int MaxCrossingLines = 6;

        private readonly NetworkEngine _engine;
        private readonly Action<string> _log;
        private readonly Action<string> _warn;

        private readonly Dictionary<IGGuid, int> _nodeByOwner = new Dictionary<IGGuid, int>();
        private string[] _names = new string[0];
        private long _loggedGeneration = -1;
        private bool _warnedOracleFailure;
        private bool _warnedNoUniverse;
        private bool _warnedGeometry;

        /// <summary>
        /// The drawn-edge/body pairs that crossed a body's disc when they were last measured.
        /// </summary>
        /// <remarks>
        /// The audit logs a crossing when it <b>appears</b> and stays quiet while it persists, so one
        /// line names a defect rather than one line per frame for the minute it is on screen. Keyed
        /// by <c>(node index &lt;&lt; 16) | body index</c>; swapped with <see cref="_crossingsSeen"/>
        /// every frame rather than rebuilt, so the audit allocates nothing per frame.
        /// </remarks>
        private readonly HashSet<int> _crossings = new HashSet<int>();

        /// <summary>This frame's crossings, written while walking the edges.</summary>
        private readonly HashSet<int> _crossingsSeen = new HashSet<int>();

        /// <summary>The generation whose geometry summary has been logged.</summary>
        private long _geometryGeneration = -1;

        /// <summary>Creates the probe.</summary>
        /// <param name="engine">The engine whose last pass is described.</param>
        /// <param name="log">Informational sink; must not throw.</param>
        /// <param name="warn">Warning sink; must not throw.</param>
        public NetworkProbe(NetworkEngine engine, Action<string> log, Action<string> warn)
        {
            _engine = engine;
            _log = log;
            _warn = warn;
        }

        /// <summary>How many passes the probe has logged. Surfaced for the plugin's own liveness line.</summary>
        public long LoggedPasses { get; private set; }

        /// <summary>
        /// Logs one block for the most recent unlogged pass, if the probe is enabled.
        /// </summary>
        /// <param name="game">The live game instance, or <c>null</c> before a session exists.</param>
        /// <remarks>
        /// Never throws out: the caller is a Unity <c>Update</c>, and an exception there would abort
        /// the plugin's whole per-frame work rather than one diagnostic line.
        /// </remarks>
        public void Tick(GameInstance game)
        {
            if (!NetworkConfig.ProbeEnabled)
            {
                return;
            }

            // The U6e occlusion-geometry audit, and it is deliberately NOT behind the pending-pass
            // gate below: it has to see the geometry on every frame, including the frames on which
            // the game's three-second rebuild timer has not run, because "the drawn line crosses the
            // planet while the verdict is older than the geometry" is one of the two candidate
            // causes it exists to discriminate. See AuditDrawnGeometry.
            try
            {
                AuditDrawnGeometry();
            }
            catch (Exception exception)
            {
                if (!_warnedGeometry)
                {
                    _warnedGeometry = true;
                    _warn("probe occlusion geometry could not be measured ("
                        + exception.GetType().Name + ": " + exception.Message
                        + "); the graph itself is unaffected");
                }
            }

            if (!_engine.HasPendingPass)
            {
                return;
            }

            long generation = _engine.Generation;
            if (generation == _loggedGeneration)
            {
                return;
            }

            _loggedGeneration = generation;
            _engine.ConsumePass();
            LoggedPasses++;

            try
            {
                LogPass(game);
            }
            catch (Exception exception)
            {
                _warn("probe pass could not be formatted (" + exception.GetType().Name + ": "
                    + exception.Message + "); the graph itself is unaffected");
            }
        }

        /// <summary>
        /// Measures every drawn edge against every celestial body, every frame, and names any edge
        /// whose chord crosses a body's visible disc while the graph still draws it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why this exists (U6e, the L22 report).</b> The user reported lines drawn straight
        /// through Kerbin to reach KSC, intermittently - sometimes correct after returning to
        /// <c>1x</c>. Two mechanisms can produce that and the L22 log cannot separate them, because
        /// the probe was off:
        /// </para>
        /// <list type="number">
        /// <item><description>
        /// <b>Stale geometry.</b> The occlusion verdict is computed inside the graph rebuild from the
        /// node positions <i>at that instant</i>, while the drawn line's endpoints are the map
        /// markers, re-read every frame. A line can therefore be drawn under a verdict that was
        /// measured before the vessel moved.
        /// </description></item>
        /// <item><description>
        /// <b>A test sphere smaller than the planet.</b> The occluder is
        /// <c>radius * OcclusionRadiusFactor - 1000 m</c>, and the L22 session ran with the factor at
        /// <c>0.25</c> - a sphere of 149 km around a 600 km planet. Every chord passing between those
        /// two radii is drawn across the disc <i>by configuration</i>.
        /// </description></item>
        /// </list>
        /// <para>
        /// <b>What is logged, and how it discriminates.</b> For each drawn edge the audit measures
        /// the <b>live</b> chord (the graph's own node objects, which the game keeps updating) against
        /// each body's centre and compares the clearance with both radii:
        /// <c>reason=torn-verdict</c> when the live chord is inside the <i>occlusion</i> radius - a
        /// pass run now would cut this edge, so the drawn one is either older than the geometry or
        /// wrong, and <c>moved</c> says which; <c>reason=visual-only</c> when the clearance is
        /// between the occlusion radius and the body's real radius - the line crosses the disc the
        /// player sees and the configured sphere simply does not reach that far. One summary line per
        /// graph generation carries the counts even when nothing crosses, which is the control that
        /// keeps a quiet log meaningful.
        /// </para>
        /// <para>
        /// <b>Measurement only.</b> Nothing here feeds a decision: it reads the engine's last pass,
        /// the live node positions and the body snapshot, and writes log lines. It runs only while
        /// <c>Debug/Network probe</c> is on.
        /// </para>
        /// </remarks>
        private void AuditDrawnGeometry()
        {
            if (_engine.PassIsVanilla || !_engine.OcclusionEnabled || !_engine.OcclusionAvailable)
            {
                _crossings.Clear();
                _crossingsSeen.Clear();
                _geometryGeneration = -1;
                return;
            }

            int count = _engine.NodeCount;
            int bodyCount = _engine.BodyCount;
            if (count <= 0 || bodyCount <= 0)
            {
                return;
            }

            _crossingsSeen.Clear();
            int edges = 0;
            int crossings = 0;
            int torn = 0;
            int printed = 0;

            for (int index = 0; index < count; index++)
            {
                int predecessor = _engine.PredecessorForGraph(index);
                if (predecessor < 0 || predecessor >= count || predecessor == index)
                {
                    continue;
                }

                if (!_engine.TryGetLivePosition(index, out double3 target)
                    || !_engine.TryGetLivePosition(predecessor, out double3 source))
                {
                    // The graph has been rebuilt (or reset) under us, so there is no live geometry to
                    // compare with. Stay quiet rather than report a crossing against a stale list.
                    _crossings.Clear();
                    _crossingsSeen.Clear();
                    return;
                }

                edges++;
                double moved = math.max(
                    math.distance(source, _engine.Snapshot(predecessor).Position),
                    math.distance(target, _engine.Snapshot(index).Position));

                for (int body = 0; body < bodyCount; body++)
                {
                    double clearance = SegmentClearance(source, target, _engine.BodyPosition(body));
                    double real = _engine.BodyRealRadius(body);
                    if (!(clearance < real))
                    {
                        continue;
                    }

                    double effective = _engine.BodyOcclusionRadius(body);
                    bool isTorn = clearance < effective;
                    crossings++;
                    if (isTorn)
                    {
                        torn++;
                    }

                    int key = (index << 16) | (body & 0xFFFF);
                    _crossingsSeen.Add(key);
                    if (_crossings.Contains(key) || printed >= MaxCrossingLines)
                    {
                        continue;
                    }

                    printed++;
                    _log("probe: occlusion-geometry CROSS reason="
                        + (isTorn ? "torn-verdict" : "visual-only")
                        + " body='" + _engine.BodyName(body) + "'"
                        + " edge=" + predecessor + "('" + NameOf(predecessor) + "')"
                        + " -> " + index + "('" + NameOf(index) + "')"
                        + " clearance=" + clearance.ToString("R")
                        + " effective=" + effective.ToString("R")
                        + " real=" + real.ToString("R")
                        + " factor=" + NetworkConfig.OcclusionFactor.ToString("R")
                        + " moved=" + moved.ToString("R")
                        + " gen=" + _engine.Generation);
                }
            }

            // Roll this frame's crossings into the reference set, so the next frame's rising-edge test
            // compares against the state the player is actually looking at. Copy rather than swap,
            // because both sets are readonly fields.
            _crossings.Clear();
            foreach (int key in _crossingsSeen)
            {
                _crossings.Add(key);
            }

            if (_geometryGeneration != _engine.Generation)
            {
                _geometryGeneration = _engine.Generation;
                _log("probe: occlusion-geometry drawn-edges=" + edges
                    + " crossing=" + crossings
                    + " torn-verdict=" + torn
                    + " visual-only=" + (crossings - torn)
                    + " bodies=" + bodyCount
                    + " factor=" + NetworkConfig.OcclusionFactor.ToString("R")
                    + " gen=" + _engine.Generation);
            }
        }

        /// <summary>
        /// The distance from a body's centre to the closest point of the segment between two nodes.
        /// </summary>
        /// <param name="source">One end of the chord.</param>
        /// <param name="target">The other end.</param>
        /// <param name="bodyPosition">The body's centre, in the same frame.</param>
        /// <returns>The clearance in metres; <c>0</c> when the chord passes through the centre.</returns>
        /// <remarks>
        /// <b>This is not the engine's test.</b> <c>Occlusion.IsOccluded</c> answers yes/no against a
        /// given radius with the legacy's exact arithmetic; this returns the <i>distance</i>, which is
        /// what lets the audit compare one chord with two different radii. It is probe-only code for
        /// that reason, and it is deliberately plain: the clamp is the segment's own ends, so a body
        /// beyond either end is measured to the nearer node rather than to an infinite line.
        /// </remarks>
        private static double SegmentClearance(double3 source, double3 target, double3 bodyPosition)
        {
            double3 p = source - bodyPosition;
            double3 d = target - source;
            double lengthSq = math.dot(d, d);
            if (!(lengthSq > 0.0))
            {
                return math.length(p);
            }

            double t = -math.dot(p, d) / lengthSq;
            t = math.clamp(t, 0.0, 1.0);
            return math.length(p + d * t);
        }

        private void LogPass(GameInstance game)
        {
            int count = _engine.NodeCount;
            int source = _engine.SourceIndex;

            ResolveNames(game, count);

            string mode = _engine.PassIsVanilla ? "off" : "on";
            string sourceName = NameOf(source);

            // The source node's OWN range, as the engine saw it on this pass. This is what makes the
            // KSC range override falsifiable from the log: the config line above says what was asked
            // for, this says what the graph was actually built with, and the two are written by
            // different code paths - the config accessor and the game's own node object.
            string sourceRange = source >= 0 && source < count
                ? _engine.Snapshot(source).MaxRange.ToString("R")
                : "n/a";

            _log("probe: pass gen=" + _engine.Generation
                + " mode=" + mode
                + " pathMode=" + NetworkConfig.Mode
                + " kscRange=" + NetworkConfig.Ksc
                + " occlusionRadius=" + NetworkConfig.OcclusionFactor
                + " nodes=" + count
                + " active=" + _engine.ActiveCount
                + " source=" + source + "('" + sourceName + "')"
                + " sourceRange=" + sourceRange);

            _log("probe: tree connected=" + _engine.ConnectedCount + "/" + count
                + " in-range-pairs=" + _engine.InRangePairCount
                + " occluded-pairs=" + _engine.OccludedPairCount
                + " body-tests=" + _engine.BodyTestCount
                + " bodies=" + _engine.BodyCount
                + " occlusion=" + OcclusionState());

            LogOcclusionCensus();
            LogRelayGateCensus();
            LogBandAttribution();
            LogPath(source, sourceName);
            RunOracle(count, source);
            LogRendererState();
        }

        /// <summary>
        /// Logs which part credited each band bit on each node - the instrument F52 asked for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>What this answers.</b> F52 recorded a reading that did not reconcile with the code: a
        /// vessel's node offered all five bands with its relay enabled and band <c>X</c> alone with it
        /// disabled, while <c>no-modulator-parts=0</c> and no path that could credit all five on
        /// evidence was reachable. The corrective measurement named in F52 is exactly this line - the
        /// mask <i>and</i> the part behind each of its bits, on one line, so the census is attributed
        /// by evidence rather than by inference.
        /// </para>
        /// <para>
        /// <b>It is a reading, not a gate.</b> <c>creditor=?</c> means a bit is set with no part
        /// recorded behind it, and <c>defaulted=true</c> means the whole mask came from the
        /// no-evidence safety net (divergence b) rather than from any part. Both are printed rather
        /// than silently resolved, because the point of the line is that a surprising census stays
        /// visible instead of being explained away.
        /// </para>
        /// <para>
        /// Capped at <see cref="MaxBandAttributionLines"/> nodes so a large network cannot turn this
        /// diagnostic into the log's dominant cost; the cap says how many were omitted rather than
        /// truncating in silence.
        /// </para>
        /// </remarks>
        private void LogBandAttribution()
        {
            int count = _engine.NodeCount;
            if (count == 0)
            {
                return;
            }

            int printed = 0;
            for (int i = 0; i < count && printed < MaxBandAttributionLines; i++)
            {
                NetworkNodeSnapshot node = _engine.Snapshot(i);
                if (node.BandsFlags == 0)
                {
                    continue;
                }

                string text = string.Empty;
                for (int bandIndex = 0; bandIndex < NetworkBands.Count; bandIndex++)
                {
                    if ((node.BandsFlags & NetworkBands.MaskOf(bandIndex)) == 0)
                    {
                        continue;
                    }

                    string creditor = _engine.BandCreditorOf(i, bandIndex);
                    text += (text.Length == 0 ? "" : " ") + NetworkBands.GetCode(bandIndex) + "="
                        + (string.IsNullOrEmpty(creditor) ? "?" : creditor);
                }

                _log("probe: band-attribution node=" + i + "('" + NameOf(i) + "')"
                    + " mask=" + NetworkBands.DescribeMask(node.BandsFlags)
                    + " defaulted=" + _engine.BandsAreDefaulted(i)
                    + " creditors=[" + text + "]");
                printed++;
            }

            if (printed == 0)
            {
                _log("probe: band-attribution no node carries a band this pass");
            }
            else if (count > printed)
            {
                _log("probe: band-attribution " + (count - printed)
                    + " further node(s) not printed (cap " + MaxBandAttributionLines + " of " + count + ")");
            }
        }

        /// <summary>
        /// Logs what the map renderer last saw and did - the phase-6 marker, and phase 7's beside it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why the renderer does not log this itself.</b> The renderer polls twice a second and
        /// usually changes nothing, so a per-pass line from it would be two lines a second of
        /// identical text; this line is emitted once per graph rebuild instead, and it reports the
        /// renderer's most recent pass, which is at most half a second stale. A change the renderer
        /// <i>does</i> make is logged by the renderer, at <c>Info</c>, under the same
        /// <c>render-lines</c> marker - so one grep finds both halves. Phase 7's rulers follow the
        /// identical arrangement under their own <c>render-rulers</c> marker.
        /// </para>
        /// <para>
        /// <b>Two lines, one per family, because they fail for unrelated reasons.</b> The lines and
        /// the rulers answer to different modes and depend on different resources (a shader versus a
        /// mesh AND a shader AND the map's scale factor), so a single line's <c>state=</c> token could
        /// only ever name one of them - and would report "the map drew nothing" when the rulers were
        /// the half that failed. The paired form is the same decision the renderer's own two-pass
        /// split makes, and a phase-7 gate reads the second line with the first as its control: the
        /// lines' <c>state=drawn</c> proves the map view and the markers were there, so a
        /// <c>render-rulers</c> line that says otherwise is about the rulers.
        /// </para>
        /// <para>
        /// <b>Attribution, not decoration.</b> L8's map gate has to be answerable independently of
        /// Phase 7's rulers and of the window phases, so this line carries every fact that gate needs:
        /// whether the game says a map view exists, how many markers the map dictionary holds (or that
        /// it is null, which is normal in the flight scene), the mode, the shader name the runtime
        /// lookup actually resolved, and the created/updated/removed counts of the last refresh. A
        /// missing line with the probe on means the probe never ticked; a line reading
        /// <c>state=no line material</c> means the shader lookup failed; a line reading
        /// <c>live=0</c> with <c>mapItems</c> non-zero and <c>state=drawn</c> means the tree has no
        /// drawable edge - three different failures that a single "the map is empty" observation could
        /// not tell apart. The ruler line carries the same shape for its own three: the geometry
        /// branch that shipped, the material it resolved, the map scale factor, and a
        /// <c>state=</c> token that separates "the gate admitted nothing" from "nothing was drawn".
        /// </para>
        /// </remarks>
        private void LogRendererState()
        {
            _log("probe: render-lines " + ConnectionsRenderer.DescribeLastPass());
            _log("probe: render-rulers " + ConnectionsRenderer.DescribeRulersPass());
        }

        /// <summary>
        /// Logs the relay/band state the gate ran on, and - on its own line - what the gate removed.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two lines, because they answer two different questions and a single line could not be read
        /// decisively for either: the census is an <i>input</i> to the pass (what the nodes are), the
        /// gate line is an <i>effect</i> on the edge set (what it cost). The relay figures are also the
        /// positive marker for the L5 relay-power gate - "relay EC draw starts and stops" has to be
        /// visible as <c>relays=</c> and <c>powerless=</c> moving, and a cleared EC store has to move
        /// <c>powerless=</c> from 0 to the number of starving nodes.
        /// </para>
        /// <para>
        /// <b><c>powerless=</c> counts nodes whose relay parts could not pay their own resource
        /// request</b> - the state behind it is <c>Data_NextRelay.HasResourcesToOperate</c>, the field
        /// <c>PartComponentModule_NextRelay</c> writes from the game broker's own delivery verdict. It
        /// is therefore a battery reading and never a deployment one: the relay's EC request is the
        /// only thing that can move this number. Confirmed empty with
        /// <c>relayPowerEnforced=false</c> means the setting (or InfinitePower) was off and the relay
        /// was not charged at all - not that the network was healthy.
        /// </para>
        /// <para>
        /// <b>The gate line's verdict vocabulary is deliberately narrower than the code's behaviour,
        /// never wider</b> (the F41/F42 lesson). <c>NOT-ENFORCED</c> says the gate was not run on this
        /// tree at all - the master switch is off, so this pass is the game's own - and it is printed
        /// INSTEAD of a count rather than beside a zero, because "removed 0" from a gate that never ran
        /// is a claim the code did not earn. <c>NO-REMOVALS</c> means it ran and removed nothing;
        /// <c>REMOVED</c> means it removed something and the three cause counters say which.
        /// </para>
        /// </remarks>
        private void LogRelayGateCensus()
        {
            int count = _engine.NodeCount;
            if (count == 0)
            {
                return;
            }

            string bands = string.Empty;
            for (int bandIndex = 0; bandIndex < _engine.BandCount; bandIndex++)
            {
                bands += (bandIndex == 0 ? "" : " ")
                    + NetworkBands.GetCode(bandIndex) + ":" + _engine.BandNodeCount(bandIndex);
            }

            _log("probe: node-state relays=" + _engine.RelayNodeCount
                + " powerless=" + _engine.PowerlessNodeCount
                + " banded=" + _engine.BandedNodeCount + "/" + count
                + " no-band-evidence=" + _engine.NoBandEvidenceNodeCount
                + " transmitter-parts=" + _engine.TransmitterPartCount
                + " modulator-parts=" + _engine.ModulatorPartCount
                + " no-modulator-parts=" + _engine.AllBandPartCount
                + " relayPowerEnforced=" + _engine.RelayPowerEnforced
                + " rangeLifted=" + _engine.BandRangeLiftedCount
                + " bands=[" + bands + "]");

            int removed = _engine.BandGateRemovedPairCount;
            string verdict;
            string tail;
            if (_engine.PassIsVanilla)
            {
                verdict = "NOT-ENFORCED";
                tail = " (the master switch is off: this pass is the game's own tree and this port's "
                    + "gate was not run on it - the numbers that would have been removed are not "
                    + "measured)";
            }
            else if (removed == 0)
            {
                verdict = "NO-REMOVALS";
                tail = " (the gate ran and every pair it saw passed - the expected reading on a "
                    + "default install, where every node offers the default band)";
            }
            else
            {
                verdict = "REMOVED";
                tail = " (these pairs are NOT in the tree; the occluded-pairs figure on the oracle "
                    + "removals line does not include them)";
            }

            _log("probe: relay-gate considered=" + _engine.GateConsideredPairCount
                + " removed=" + removed
                + " no-power=" + _engine.BandGateNoPowerCount
                + " no-common-band=" + _engine.BandGateNoCommonBandCount
                + " band-range=" + _engine.BandGateBandRangeCount
                + " enforced=" + (!_engine.PassIsVanilla)
                + " verdict=" + verdict + tail);

            int recorded = _engine.RecordedBandBlockedCount;
            if (recorded == 0)
            {
                return;
            }

            int printed = 0;
            for (int i = 0; i < recorded && printed < MaxBlockedLinkLines; i++)
            {
                _engine.GetRecordedBandBlockedLink(i, out int linkSource, out int linkTarget, out int reason);
                _log("probe: relay-gate link " + linkSource + "('" + NameOf(linkSource) + "') -> "
                    + linkTarget + "('" + NameOf(linkTarget) + "') removed for " + BandGateReasonName(reason)
                    + " (source bands=" + NetworkBands.DescribeMask(_engine.Snapshot(linkSource).BandsFlags)
                    + " target bands=" + NetworkBands.DescribeMask(_engine.Snapshot(linkTarget).BandsFlags)
                    + " source power=" + _engine.Snapshot(linkSource).HasEnoughResources
                    + " target power=" + _engine.Snapshot(linkTarget).HasEnoughResources + ")");
                printed++;
            }

            if (recorded > printed)
            {
                _log("probe: relay-gate " + (recorded - printed)
                    + " further removed pair(s) not printed (cap " + MaxBlockedLinkLines + " of "
                    + recorded + " recorded)");
            }
        }

        /// <summary>The engine's removal-reason code as a word, for the per-link lines.</summary>
        /// <param name="reason">0, 1 or 2 - the engine's own encoding.</param>
        private static string BandGateReasonName(int reason)
        {
            switch (reason)
            {
                case 0: return "a node without resources";
                case 1: return "no band in common";
                case 2: return "no shared band's range covering the distance";
                default: return "reason " + reason + " (unknown to this build)";
            }
        }

        private string OcclusionState()
        {
            if (!_engine.OcclusionEnabled)
            {
                return "off (radius factor is 0 - the geometry is not consulted at all)";
            }

            if (!_engine.OcclusionAvailable)
            {
                return "unavailable (the body snapshot failed; see the warning above)";
            }

            // F41. Available is not applied. With the master switch off this pass IS the game's own
            // tree, and the geometry - though it was measured, and its census printed below - never
            // reached the graph. Saying "applied" here would directly contradict the oracle line
            // printed beside it, which scores this same pass EXACT against vanilla; a reader who
            // trusted the stronger word would conclude occlusion had been enforced when it had not.
            if (_engine.PassIsVanilla)
            {
                return "measured over " + _engine.BodyCount
                    + " bodies but NOT applied - the master switch is off, so this pass is the game's own tree";
            }

            return "applied to " + _engine.BodyCount + " bodies";
        }

        private void LogOcclusionCensus()
        {
            // F42. The count is a count of REMOVALS in mode=on but only of candidates in vanilla, where
            // the geometry is still measured and the graph is handed back untouched. The verb is
            // qualified for the same reason OcclusionState() is: this line sits two above an oracle that
            // scores the same pass EXACT against vanilla, and an unqualified "blocked" contradicts it.
            //
            // The per-link lines below keep the unqualified wording deliberately: "blocked by 'Kerbin'"
            // states a geometric fact about the segment, and that fact is the same in both modes. It is
            // this count - and only this count - that means different things.
            bool vanilla = _engine.PassIsVanilla;
            for (int bodyIndex = 0; bodyIndex < _engine.BodyCount; bodyIndex++)
            {
                int blocked = _engine.OccludedByBody(bodyIndex);
                if (blocked <= 0)
                {
                    continue;
                }

                _log("probe: occlusion body '" + _engine.BodyName(bodyIndex)
                    + (vanilla
                        ? "' stands behind " + blocked + " in-range link(s) - occlusion is NOT applied this pass"
                        : "' blocked " + blocked + " in-range link(s)"));
            }

            int recorded = _engine.RecordedBlockedLinkCount;
            if (recorded == 0)
            {
                return;
            }

            int printed = 0;
            for (int i = 0; i < recorded && printed < MaxBlockedLinkLines; i++)
            {
                _engine.GetRecordedBlockedLink(i, out int linkSource, out int linkTarget, out int bodyIndex);
                _log("probe: occlusion link " + linkSource + "('" + NameOf(linkSource) + "') -> "
                    + linkTarget + "('" + NameOf(linkTarget) + "') blocked by '"
                    + _engine.BodyName(bodyIndex) + "'");
                printed++;
            }

            if (recorded > printed)
            {
                _log("probe: occlusion " + (recorded - printed)
                    + " further blocked link(s) not printed (cap " + MaxBlockedLinkLines + " of "
                    + recorded + " recorded)");
            }
        }

        private void LogPath(int source, string sourceName)
        {
            int count = _engine.NodeCount;
            if (count == 0)
            {
                return;
            }

            if (_engine.PassIsVanilla)
            {
                // The game's tree has no notion of "the active vessel's path"; the reachable set is
                // the honest summary, and it is the thing to compare against mode=on.
                _log("probe: path mode=off (the game's own tree) reachable="
                    + _engine.ConnectedCount + "/" + count);
                return;
            }

            if (source < 0 || source >= count)
            {
                _log("probe: path none (no source index)");
                return;
            }

            int active = FindActiveVesselNode();
            if (active < 0)
            {
                _log("probe: path no active vessel node in this graph (reachable="
                    + _engine.ConnectedCount + "/" + count + ")");
                return;
            }

            if (active == source)
            {
                _log("probe: path the active vessel IS the source ('" + NameOf(active) + "')");
                return;
            }

            var reversed = new List<int>();
            int cursor = active;
            int guard = count + 1;
            while (cursor >= 0 && cursor != source && guard-- > 0)
            {
                reversed.Add(cursor);
                cursor = _engine.PredecessorOf(cursor);
            }

            if (cursor != source)
            {
                _log("probe: path none - the active vessel " + active + "('" + NameOf(active)
                    + "') is not connected to the source " + source + "('" + sourceName + "')");
                return;
            }

            reversed.Add(source);
            reversed.Reverse();

            var text = new System.Text.StringBuilder();
            int shown = Math.Min(reversed.Count, MaxPathNodesShown);
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                {
                    text.Append(" -> ");
                }

                int node = reversed[i];
                text.Append(node).Append("('").Append(NameOf(node)).Append("')");
            }

            if (reversed.Count > shown)
            {
                text.Append(" -> ...");
            }

            _log("probe: path " + text + " hops=" + (reversed.Count - 1));
        }

        private int FindActiveVesselNode()
        {
            GameInstance game = GameManager.Instance == null ? null : GameManager.Instance.Game;
            if (game == null)
            {
                return -1;
            }

            KSP.Sim.impl.ViewController view = game.ViewController;
            if (view == null)
            {
                return -1;
            }

            VesselComponent vessel;
            if (!view.TryGetActiveSimVessel(out vessel, false) || vessel == null)
            {
                return -1;
            }

            return _nodeByOwner.TryGetValue(vessel.GlobalId, out int index) ? index : -1;
        }

        // =========================================================================================
        // The oracle.
        // =========================================================================================

        /// <summary>
        /// Runs the game's own algorithm over this pass's node list and compares the two trees.
        /// </summary>
        /// <param name="count">The node count.</param>
        /// <param name="source">The source index.</param>
        /// <remarks>
        /// <para>
        /// <b>Managed code calling the game's own job, not a job of this port's own.</b> Decision D10
        /// bans an <c>IJob</c> and a native DLL belonging to CommNext; it does not ban calling the
        /// game's. <c>GetConnectedNodesJob</c> is a public struct with a public <c>Execute()</c>, and
        /// it allocates its own <c>Allocator.Temp</c> scratch and disposes it before returning
        /// (IL-verified over the whole 150-byte method body), so this is one synchronous call with no
        /// shared state and nothing that outlives it.
        /// </para>
        /// <para>
        /// The two <c>NativeArray</c>s here are the only native memory this port allocates, they are
        /// <c>Allocator.Temp</c>, and they are disposed in the same method that creates them. They are
        /// not the engine's, they are not the graph's, and they never leave this frame.
        /// </para>
        /// </remarks>
        private void RunOracle(int count, int source)
        {
            if (count == 0 || source < 0 || source >= count)
            {
                _log("probe: oracle not run (no usable source index)");
                return;
            }

            var jobNodes = new NativeArray<ConnectionGraph.ConnectionGraphJobNode>(
                count, Allocator.Temp, NativeArrayOptions.ClearMemory);
            var prevEdges = new NativeArray<ConnectionGraph.ConnectionEdge>(
                count, Allocator.Temp, NativeArrayOptions.ClearMemory);

            try
            {
                for (int i = 0; i < count; i++)
                {
                    NetworkNodeSnapshot node = _engine.Snapshot(i);
                    jobNodes[i] = new ConnectionGraph.ConnectionGraphJobNode(
                        node.Position, node.MaxRange, FlagsFor(node));
                }

                var job = new GetConnectedNodesJob
                {
                    Nodes = jobNodes,
                    StartIndex = source,
                    PrevEdges = prevEdges,
                };
                job.Execute();

                // 1. THE PRIMARY VERDICT (F31). This port's own relaxation, run under the game's
                //    conditions, against the game's own job: same nodes, same edge set, same metric,
                //    occlusion suppressed on this side. Any difference at all is therefore a
                //    difference in the relaxation itself - which is the claim D8 has to earn.
                if (_engine.OracleReady)
                {
                    TreeCompare algorithm = Compare(
                        oracle: true, gameEdges: prevEdges, count: count, printDetails: true);
                    LogAlgorithmVerdict(algorithm, count);
                }
                else
                {
                    // The engine rebuilds the control tree only when the probe is enabled, and the
                    // probe ticks only when it is enabled too - so this means the setting changed
                    // between the rebuild and this tick. Comparing a stale control tree would be worse
                    // than not comparing, so say so instead.
                    _log("probe: oracle-algorithm not run (the engine produced no control tree for "
                        + "this pass - the probe setting changed after the graph was rebuilt)");
                }

                // 2. THE REMOVALS VERDICT. The tree the graph actually received, against the game's
                //    in-range edge set. It is expected to differ whenever occlusion bites; what it
                //    may never do is use an edge the game's own edge set does not contain.
                TreeCompare shipped = Compare(
                    oracle: false, gameEdges: prevEdges, count: count, printDetails: false);
                TreeEdgeAudit audit = AuditTreeEdges(count);
                LogRemovalsVerdict(shipped, audit, count);
            }
            catch (Exception exception)
            {
                if (!_warnedOracleFailure)
                {
                    _warnedOracleFailure = true;
                    _warn("probe oracle could not run (" + exception.GetType().Name + ": "
                        + exception.Message + "); the graph itself is unaffected");
                }
            }
            finally
            {
                prevEdges.Dispose();
                jobNodes.Dispose();
            }
        }

        /// <summary>
        /// The game's own <c>ConnectionGraph.GetFlagsFrom</c>, reproduced.
        /// </summary>
        /// <param name="node">The node snapshot to convert.</param>
        /// <returns>The job-node flags.</returns>
        /// <remarks>
        /// The method itself is <c>private static</c> on the installed runtime (IL:
        /// <c>.method private static hidebysig ... GetFlagsFrom</c>), so it cannot be called; its whole
        /// body is two conditional <c>or</c>s of <c>ldc.i4.1</c> and <c>ldc.i4.2</c>, which is what this
        /// reproduces. It is reproduced rather than reflected on purpose - reflection into a private
        /// game method is exactly the kind of unproven API dependency this port does not take.
        /// </remarks>
        private static ConnectionGraphNodeFlags FlagsFor(NetworkNodeSnapshot node)
        {
            ConnectionGraphNodeFlags flags = ConnectionGraphNodeFlags.None;
            if (node.IsActive)
            {
                flags |= ConnectionGraphNodeFlags.IsActive;
            }

            if (node.IsControlSource)
            {
                flags |= ConnectionGraphNodeFlags.IsControlSource;
            }

            return flags;
        }

        /// <summary>
        /// Compares one of this engine's trees against the game's own job output, node by node.
        /// </summary>
        /// <param name="oracle">
        /// <c>true</c> for the oracle's control tree - occlusion suppressed and the game's metric,
        /// which is the same-input comparison - or <c>false</c> for the tree the graph received.
        /// </param>
        /// <param name="gameEdges">The game's own <c>PrevEdges</c> from the fresh job run.</param>
        /// <param name="count">The node count.</param>
        /// <param name="printDetails">
        /// Whether to print one line per difference. The control comparison prints them - a difference
        /// there is a real disagreement and needs attributing. The shipped comparison does not, because
        /// its predecessor differences are expected whenever occlusion bites or the shipped metric is
        /// not the game's, and the occlusion census already names every blocked link; "this port
        /// reaches a node the game does not" is printed either way, because it is never expected.
        /// </param>
        /// <returns>The counters, for the caller's verdict line.</returns>
        private TreeCompare Compare(
            bool oracle,
            NativeArray<ConnectionGraph.ConnectionEdge> gameEdges,
            int count,
            bool printDetails)
        {
            var result = new TreeCompare();
            int printed = 0;

            for (int i = 0; i < count; i++)
            {
                int mine = oracle ? _engine.OraclePredecessorForGraph(i) : _engine.PredecessorForGraph(i);
                double mineCost = oracle ? _engine.OracleCostOf(i) : _engine.CostOf(i);
                int theirs = gameEdges[i].Index;
                double theirCost = gameEdges[i].Cost;

                bool mineReachable = mine >= 0;
                bool theirReachable = theirs >= 0;

                if (!mineReachable && theirReachable)
                {
                    // The game kept a link this port does not have. Expected whenever the occlusion
                    // factor is non-zero - per-pair occlusion is exactly "this port may detach what
                    // the game keeps" - and impossible when it is zero.
                    result.GameOnly++;
                    if (printDetails && printed < MaxOracleLines)
                    {
                        printed++;
                        _log("probe: oracle node " + i + "('" + NameOf(i)
                            + "') reachable for the game via " + theirs + ", not for this port");
                    }

                    continue;
                }

                if (mineReachable && !theirReachable)
                {
                    // THE failure mode. This port invented connectivity the game's own algorithm
                    // does not produce. Must be 0 in every configuration.
                    result.MineOnly++;
                    if (printed < MaxOracleLines)
                    {
                        printed++;
                        _log("probe: oracle node " + i + "('" + NameOf(i)
                            + "') reachable for this port via " + mine
                            + ", NOT reachable for the game - INVENTED CONNECTIVITY");
                    }

                    continue;
                }

                if (mine != theirs)
                {
                    result.PredecessorDiffers++;
                    if (printDetails && printed < MaxOracleLines)
                    {
                        printed++;
                        _log("probe: oracle node " + i + "('" + NameOf(i)
                            + "') predecessor " + mine + " (this port) vs " + theirs
                            + " (the game), both reachable");
                    }

                    continue;
                }

                if (!mineReachable)
                {
                    result.Agree++;
                    continue;
                }

                // F30. Two unset costs are not a disagreement - see IsUnsetCost. A finite cost held
                // against an unset one is NOT covered here and still counts as one, and two finite
                // costs are still compared exactly.
                if (mineCost == theirCost || (IsUnsetCost(mineCost) && IsUnsetCost(theirCost)))
                {
                    result.Agree++;
                }
                else
                {
                    result.CostDiffers++;
                    if (printDetails && printed < MaxOracleLines)
                    {
                        printed++;
                        _log("probe: oracle node " + i + "('" + NameOf(i) + "') same predecessor "
                            + mine + " but cost " + mineCost.ToString("R") + " (this port) vs "
                            + theirCost.ToString("R") + " (the game)");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Whether an edge cost carries the "no edge" sentinel rather than a cost.
        /// </summary>
        /// <param name="cost">The cost to test.</param>
        /// <returns><c>true</c> for NaN, either infinity, or <see cref="double.MaxValue"/>.</returns>
        /// <remarks>
        /// F30. This port writes <see cref="double.MaxValue"/> for "unreachable" (every consumer's
        /// unreachable test, and the initialisation both trees are built from), and the game's
        /// <c>GetConnectedNodesJob</c> writes <c>+&#8734;</c> for the same state - measured in L3 at
        /// the source node, whose entry carries no edge in either tree, as
        /// <c>cost 1.7976931348623157E+308 (this port) vs Infinity (the game)</c>. Comparing the two
        /// encodings as values manufactured a <c>cost-differs</c> that no code change could ever have
        /// fixed, and by itself forced the old verdict to DIFFERS.
        /// </remarks>
        private static bool IsUnsetCost(double cost) =>
            double.IsNaN(cost) || double.IsInfinity(cost) || cost == double.MaxValue;

        /// <summary>
        /// Audits every edge in the tree the graph received against the game's own edge set.
        /// </summary>
        /// <param name="count">The node count.</param>
        /// <returns>The audit counters.</returns>
        /// <remarks>
        /// <para>
        /// <b>This is the explicit "occlusion only ever removes edges" check.</b> It is a different
        /// question from the tree comparison: the comparison's reachability counters can only say
        /// that this port reached nothing the game's algorithm could not also reach, which a subset
        /// relation on edges already implies - they cannot say that the edge SET is a subset, which is
        /// the property the feature is allowed to rely on.
        /// </para>
        /// <para>
        /// Each edge of the shipped tree is therefore re-derived from the pass's own geometry: it must
        /// join two active nodes that pass both range gates - an edge the game's own job would have
        /// considered at all - and the stored cost must be exactly that pair's squared distance, which
        /// is the game's own <c>ConnectionEdge.Cost</c> semantics. The source's entry is skipped: it is
        /// the root of the tree rather than an edge, and every consumer already special-cases it.
        /// </para>
        /// <para>
        /// <b>Both halves must be one generation (F36).</b> The positions this re-derives from and the
        /// cost it compares against are the same rebuild's - the job's own <c>Nodes</c> array and its
        /// own <c>PrevEdges</c> array. The equality below is exact and must stay exact: the game's
        /// <c>V_11</c> <i>is</i> <c>math.distancesq</c> of the same two positions the job read (IL:
        /// <c>math::distancesq</c> at IL_017d, <c>stfld ConnectionEdge::Cost</c> at IL_01ed), so an
        /// inequality is a real disagreement, never a rounding artefact. When this counter fired in L4
        /// it was the <i>input</i> that was stale, not the comparison - fixing a cost mismatch by
        /// loosening the comparison would delete the check rather than repair it.
        /// </para>
        /// </remarks>
        private TreeEdgeAudit AuditTreeEdges(int count)
        {
            var audit = new TreeEdgeAudit();
            int source = _engine.SourceIndex;

            for (int i = 0; i < count; i++)
            {
                if (i == source)
                {
                    continue;
                }

                int predecessor = _engine.PredecessorForGraph(i);
                if (predecessor < 0)
                {
                    continue;
                }

                if (predecessor == i || predecessor >= count)
                {
                    audit.Invented++;
                    continue;
                }

                audit.TreeEdges++;

                NetworkNodeSnapshot from = _engine.Snapshot(predecessor);
                NetworkNodeSnapshot to = _engine.Snapshot(i);
                double distance = math.distancesq(from.Position, to.Position);

                bool inRange = from.IsActive && to.IsActive
                    && distance < from.MaxRange * from.MaxRange
                    && distance < to.MaxRange * to.MaxRange;
                if (!inRange)
                {
                    audit.Invented++;
                    continue;
                }

                if (_engine.CostOf(i) != distance)
                {
                    audit.CostMismatch++;
                }
            }

            return audit;
        }

        /// <summary>
        /// Logs the primary verdict: this port's relaxation against the game's job on identical input.
        /// </summary>
        /// <param name="counts">The comparison counters.</param>
        /// <param name="count">The node count.</param>
        private void LogAlgorithmVerdict(TreeCompare counts, int count)
        {
            string verdict;
            if (counts.MineOnly > 0)
            {
                verdict = "INVENTED-CONNECTIVITY";
            }
            else if (counts.PredecessorDiffers == 0 && counts.CostDiffers == 0 && counts.GameOnly == 0)
            {
                verdict = "EXACT";
            }
            else
            {
                // With the edge set and the metric both pinned to the game's, this is the only thing
                // the word can mean: the relaxation itself disagrees with the game's algorithm.
                verdict = "DIFFERS";
            }

            _log("probe: oracle-algorithm metric=game(" + BestPathMode.ShortestKSC
                + ") edgeset=in-range occlusion=suppressed compared=" + count
                + " agree=" + counts.Agree
                + " predecessor-differs=" + counts.PredecessorDiffers
                + " cost-differs=" + counts.CostDiffers
                + " game-only=" + counts.GameOnly
                + " mine-only=" + counts.MineOnly
                + " verdict=" + verdict);
        }

        /// <summary>
        /// Logs the removals verdict: the tree the graph received against the game's edge set.
        /// </summary>
        /// <param name="counts">The comparison counters for the shipped tree.</param>
        /// <param name="audit">The edge-level audit for the shipped tree.</param>
        /// <param name="count">The node count.</param>
        private void LogRemovalsVerdict(TreeCompare counts, TreeEdgeAudit audit, int count)
        {
            bool gameMetric = NetworkConfig.Mode == BestPathMode.ShortestKSC;

            // F37 - ONE CONDITION, ONE LABEL. This was a single `if` that emitted INVENTED-CONNECTIVITY
            // for any of three different failures, so a master switch that was working perfectly
            // reported the one verdict the contract calls never acceptable, and a reader had no way to
            // tell which condition had fired. Each condition now names itself:
            //
            //   INVENTED-CONNECTIVITY  the tree reaches a node the game's own algorithm does not reach
            //                          ("mine-only"). The label keeps exactly the meaning this file's
            //                          per-node line has always given it, on both lines.
            //   INVENTED-EDGE          an edge of the tree is not an in-range pair of active nodes -
            //                          the game's own edge set does not contain it ("invented-edges").
            //                          The edge-level form of the same class of failure, named apart so
            //                          that it cannot be confused with the reachability one.
            //   COST-MISMATCH          a real, in-range edge whose stored cost is not that pair's
            //                          squared length. Neither of the above: the connectivity is right
            //                          and the number attached to it is wrong.
            //
            // All three are failures in every configuration - the counters stay in the line so a
            // verdict never has to be taken on trust.
            string verdict;
            if (counts.MineOnly > 0)
            {
                verdict = "INVENTED-CONNECTIVITY";
            }
            else if (audit.Invented > 0)
            {
                verdict = "INVENTED-EDGE";
            }
            else if (audit.CostMismatch > 0)
            {
                verdict = "COST-MISMATCH";
            }
            else if ((gameMetric || _engine.PassIsVanilla)
                && counts.PredecessorDiffers == 0
                && counts.CostDiffers == 0
                && counts.GameOnly == 0)
            {
                // The metric has to be the game's for the shipped tree to be expected to match it -
                // except in the vanilla state, where the shipped tree IS the captured game tree and
                // the port's configured metric was never applied to it at all.
                verdict = "EXACT";
            }
            else
            {
                // Every edge is the game's, nothing is invented and nothing this port reaches is out
                // of the game's reach: what is left is the pairs occlusion removed - plus, under
                // NearestRelay, the metric the game does not compute (hence the pathMode field).
                verdict = "ONLY-REMOVALS";
            }

            // Phase 5: the shipped tree's removals have a second possible cause, and this line's
            // occluded-pairs figure cannot express it. A pair the relay/band gate removed is absent
            // from the tree for a reason occlusion had nothing to do with, and it is absent from the
            // occluded-pairs count too (that counter is incremented by the occlusion test alone). The
            // ONLY-REMOVALS verdict stays true - every edge is still one of the game's - but leaving
            // the cause unstated is how a reader attributes a band removal to a planet.
            string gateNote = _engine.BandGateRemovedPairCount > 0 && !_engine.PassIsVanilla
                ? " relay-gate-removed=" + _engine.BandGateRemovedPairCount
                    + " (NOT occlusion - see the relay-gate line above)"
                : string.Empty;

            _log("probe: oracle-removals edgeset=in-range pathMode=" + NetworkConfig.Mode
                + " tree-edges=" + audit.TreeEdges
                + " invented-edges=" + audit.Invented
                + " cost-mismatch=" + audit.CostMismatch
                + " occluded-pairs=" + _engine.OccludedPairCount
                + gateNote
                + " compared=" + count
                + " agree=" + counts.Agree
                + " predecessor-differs=" + counts.PredecessorDiffers
                + " cost-differs=" + counts.CostDiffers
                + " game-only=" + counts.GameOnly
                + " mine-only=" + counts.MineOnly
                + " verdict=" + verdict);

            if (_engine.PassIsVanilla)
            {
                _log("probe: oracle this pass observed the game's own rebuild (mode=off): the removals "
                    + "line's tree IS the game's own, so EXACT there proves the capture is faithful and "
                    + "generation-aligned - it is the game's own job's output checked against the same "
                    + "job's own input - while the algorithm line proves this port's relaxation "
                    + "reproduces the game's own job on the captured nodes; together they are the "
                    + "master switch's proof");
            }
        }

        /// <summary>The counters of one node-by-node tree comparison.</summary>
        private struct TreeCompare
        {
            /// <summary>Nodes whose predecessor and edge cost both match.</summary>
            public int Agree;

            /// <summary>Nodes reachable in both trees, from a different predecessor.</summary>
            public int PredecessorDiffers;

            /// <summary>Nodes with the same predecessor but a different edge cost.</summary>
            public int CostDiffers;

            /// <summary>Nodes this port reaches that the game's algorithm does not. Must be 0.</summary>
            public int MineOnly;

            /// <summary>Nodes the game's algorithm reaches that this port does not.</summary>
            public int GameOnly;
        }

        /// <summary>The edge-level audit of the tree the graph received.</summary>
        private struct TreeEdgeAudit
        {
            /// <summary>Edges in the tree. The source is its root, not an edge.</summary>
            public int TreeEdges;

            /// <summary>Tree edges that are not in-range pairs of active nodes. Must be 0.</summary>
            public int Invented;

            /// <summary>Tree edges whose cost is not the pair's squared distance. Must be 0.</summary>
            public int CostMismatch;
        }

        // =========================================================================================
        // Name resolution.
        // =========================================================================================

        private void ResolveNames(GameInstance game, int count)
        {
            _nodeByOwner.Clear();

            if (_names.Length < count)
            {
                _names = new string[count];
            }

            UniverseModel universe = null;
            if (game != null)
            {
                universe = game.UniverseModel;
            }

            if (universe == null)
            {
                if (!_warnedNoUniverse)
                {
                    _warnedNoUniverse = true;
                    _warn("probe has no universe model; node names are unavailable this pass");
                }
            }

            int lookups = 0;
            for (int i = 0; i < count; i++)
            {
                NetworkNodeSnapshot node = _engine.Snapshot(i);
                _nodeByOwner[node.Owner] = i;

                string name = null;
                if (universe != null && lookups < MaxNameLookups)
                {
                    lookups++;
                    try
                    {
                        SimulationObjectModel simObject = universe.FindSimObject(node.Owner);
                        if (simObject != null)
                        {
                            name = simObject.Name;
                        }
                    }
                    catch (Exception)
                    {
                        // A stale owner during a load is an expected race, not an error worth a line.
                        name = null;
                    }
                }

                _names[i] = name;
            }
        }

        private string NameOf(int index)
        {
            if (index < 0 || index >= _names.Length)
            {
                return "?";
            }

            string name = _names[index];
            return string.IsNullOrEmpty(name) ? "?" : name;
        }
    }
}
