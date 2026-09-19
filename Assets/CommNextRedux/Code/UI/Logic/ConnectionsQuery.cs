// CommNextRedux - the vessel report's view models and its filter/sort state.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Logic/ConnectionsQuery.cs (88 lines), MIT. The class's
//   job is the legacy's: hold the filter, the sort key and the direction, bind the three controls to
//   them, and order the rows the window hands it. The enum names and the `Changed` event are the
//   legacy's too. Four things are different, and each one is forced either by this pin or by the
//   data this port actually has:
//
//     1. THE FILTER SET IS SMALLER, AND THAT IS A DATA FACT, NOT A SIMPLIFICATION (D44). The legacy
//        could ask its network job four questions, because the job tabulated a DIRECTED EDGE PER
//        PAIR plus a per-pair geometric verdict:
//
//          Active / Connected  -> is this pair a tree edge?        (the port CAN answer this)
//          InRange             -> is this pair geometrically in range?  (per-pair, NOT exposed)
//          All                 -> every other node in the network   (a node list, not a link list)
//
//        This port's engine exposes exactly one per-pair relation - the spanning-tree edge, through
//        `PredecessorOf` / `SelectedBandOf` - and nothing per-pair for "in range" or "in line of
//        sight": `InRangePairCount`, `OccludedPairCount` and the recorded band-gate removals are all
//        COUNTS, not lookups. So the filter keeps the three questions the engine can answer per
//        link, and the two it cannot are dropped rather than approximated. Re-deriving "in range"
//        from two `MaxRange`s and a distance would be a second, weaker copy of the gate - occlusion
//        and the band match are exactly what it would miss - and a column that disagrees with the
//        map is worse than a column that is absent.
//
//     2. THE DROPDOWNS ARE MATCHED BY INDEX, NOT BY LABEL (D45). The legacy found its enum back
//        out of `evt.newValue` with `AllFilters.Find(f => f.Item2 == evt.newValue).Item1`, i.e. by
//        comparing the *display text* - and the legacy's own label was an I2 `LocalizedString`, so
//        the comparison was against the key. Two entries whose translations collide (or a language
//        that renders two of them identically) would silently select the wrong one, and a lookup
//        miss returned `default` - i.e. silently switched the filter to `All`. The port builds the
//        choice list itself, so the label's position IS the enum's position and the callback maps
//        the index it just read. A miss is reported through the caller's warning sink and changes
//        nothing.
//
//     3. THE CHOICE LABELS ARE TRANSLATED ONCE, AT BIND TIME (D42). They are screen-bound text. The
//        game cannot change language inside a session, so a bind-time translation cannot go stale,
//        and it is the same contract the toolbar's tooltips already use.
//
//     4. SORTING IS TOTAL AND DETERMINISTIC. `List<T>.Sort` is not stable, so a list of rows whose
//        keys tie would come out in a different order on every refresh - at five refreshes a second
//        that is a visibly twitching list. Every comparison falls back to `OtherIndex`, which is
//        unique within a pass, so the order is a function of the data alone.
//
// WHAT THIS FILE DOES NOT DO
//   It does not build the rows. That needs the engine, the universe model and the node graph, and
//   it belongs to the window that has all three; this file answers "which of these rows does the
//   player want to see, and in what order".

using System;
using System.Collections.Generic;
using CommNext.Unity.Runtime.Controls;
using CommNextRedux.UI.Utils;
using KSP.Sim.impl;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Logic
{
    /// <summary>Which of the vessel's links the report lists.</summary>
    /// <remarks>
    /// <see cref="All"/> is the default, and it is the legacy's `Active` set - every edge the vessel
    /// is an endpoint of. See the file header for why the legacy's other three modes are not here.
    /// </remarks>
    public enum ConnectionsFilter
    {
        /// <summary>Every link the vessel is an endpoint of: its one inbound edge and all outbound.</summary>
        All = 0,

        /// <summary>Only the links where this vessel is the edge's source (the tree's parent side).</summary>
        Outbound = 1,

        /// <summary>Only this vessel's own inbound edge (the link that feeds it).</summary>
        Inbound = 2
    }

    /// <summary>What the report orders its links by.</summary>
    /// <remarks>The legacy's four keys, all four computable from this port's per-edge data.</remarks>
    public enum ConnectionsSort
    {
        /// <summary>Shortest link first (ascending) or last (descending).</summary>
        Distance = 0,

        /// <summary>By the band the gate selected for the edge; a bandless link sorts last ascending.</summary>
        Band = 1,

        /// <summary>By the link's signal strength, end to end.</summary>
        SignalStrength = 2,

        /// <summary>By the other end's name, ordinal.</summary>
        Name = 3
    }

    /// <summary>
    /// One row of the report: one link the report's vessel is an endpoint of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A struct, because the list is rebuilt on every refresh and the rows are pooled into the UI -
    /// the legacy's `NetworkConnection` was a class per pair per refresh.
    /// </para>
    /// <para>
    /// <b>Every field has a source in this port's engine</b>, named against it:
    /// <see cref="OtherIndex"/> and the two position-derived values come from
    /// <c>NetworkEngine.Snapshot(i)</c>; <see cref="BandIndex"/> is
    /// <c>NetworkEngine.SelectedBandOf</c> for the edge - the per-edge band the relay/band gate
    /// accepted the pair on, which is the same value the renderer colours the map line with;
    /// <see cref="VesselIsSource"/> is <c>PredecessorOf(other) == vesselIndex</c>, i.e. the legacy's
    /// own `previousIndices[i] == nodeIndex` test for "this is my outbound link".
    /// </para>
    /// <para>
    /// <b>Nothing here is a guess at a per-pair verdict the engine does not publish.</b> There is no
    /// "in range" flag and no "occluded by" field: see the file header. A tree edge passed both
    /// tests by construction - that is what put it in the tree - so on the rows this report can show
    /// they would be constant `true`/`null` anyway.
    /// </para>
    /// </remarks>
    public struct ConnectionRow
    {
        /// <summary>The other end's node index in the pass this row was built from.</summary>
        /// <remarks>
        /// Only valid against the same pass's <c>Snapshot</c> calls, which is why every consumer of
        /// a row is a UI write that happens in the same refresh that built it.
        /// </remarks>
        public int OtherIndex;

        /// <summary>The other end's owner id, captured as a value - never an object reference.</summary>
        public IGGuid OtherOwner;

        /// <summary>The other end's display name, already resolved and already translated.</summary>
        public string OtherName;

        /// <summary>Whether the other end is a relay (`Data_NextRelay.EnableRelay`).</summary>
        public bool OtherIsRelay;

        /// <summary>Whether the other end has resources for its own request.</summary>
        public bool OtherHasEnoughResources;

        /// <summary>Whether the other end is the control source (the KSC on a stock install).</summary>
        public bool OtherIsControlSource;

        /// <summary>The two ends' distance in metres.</summary>
        public double DistanceMeters;

        /// <summary>The smaller of the two ends' own ranges, in metres - the signal denominator.</summary>
        public double MinRangeMeters;

        /// <summary>The link's signal strength, 0..1, by the game's own curve.</summary>
        public float SignalStrength;

        /// <summary>The band the gate selected for this edge, or <c>-1</c> when it selected none.</summary>
        /// <remarks>
        /// <c>-1</c> is a normal answer, not a failure: it is what the source's own incoming edge
        /// would be, what a pass with the band gate switched off produces for every edge, and what a
        /// vanilla capture produces. The renderer falls back to its relay/link colour for it, and the
        /// row hides its band icon.
        /// </remarks>
        public int BandIndex;

        /// <summary>Whether this vessel is the edge's source - the legacy's "Out" direction.</summary>
        public bool VesselIsSource;

        /// <summary>Whether both ends have resources (the legacy's `IsPowered`).</summary>
        public bool IsPowered
        {
            get { return VesselHasEnoughResources && OtherHasEnoughResources; }
        }

        /// <summary>Whether the report's own vessel has resources for its request.</summary>
        public bool VesselHasEnoughResources;

        /// <summary>Whether the edge carries a gate-selected band.</summary>
        public bool HasBand
        {
            get { return BandIndex >= 0; }
        }
    }

    /// <summary>One row of the report's band list: a band the report's vessel offers.</summary>
    /// <remarks>
    /// A struct for the same reason <see cref="ConnectionRow"/> is, and the item type the band list
    /// is pooled over. The legacy pooled a bare <c>int</c> band index and looked the range up again
    /// at bind time; carrying both is the same data with no second lookup.
    /// </remarks>
    public struct BandRowData
    {
        /// <summary>The band's index - its bit position in a <c>BandsFlags</c> mask.</summary>
        public int BandIndex;

        /// <summary>The vessel's range on that band, in metres. Always positive for a listed band.</summary>
        public double RangeMeters;
    }

    /// <summary>
    /// The report's filter, sort key and direction, and the controls that write them.
    /// </summary>
    /// <remarks>
    /// One instance per report window. The list itself lives in the window; this object answers
    /// "does this row belong on screen" and "which order".
    /// </remarks>
    public sealed class ConnectionsQuery
    {
        /// <summary>Which links to list. Defaults to every link the vessel has (the legacy's default).</summary>
        public ConnectionsFilter Filter = ConnectionsFilter.All;

        /// <summary>What to order the links by. Defaults to distance (the legacy's default).</summary>
        public ConnectionsSort Sort = ConnectionsSort.Distance;

        /// <summary>Which way to order them. Defaults to descending (the legacy's default).</summary>
        public SortDirection Direction = SortDirection.Descending;

        /// <summary>Raised when the filter, the sort key or the direction changes.</summary>
        /// <remarks>
        /// The window rebuilds on this - so a dropdown change is applied on the next refresh rather
        /// than waiting up to one refresh interval for the tick to notice.
        /// </remarks>
        public event Action Changed;

        /// <summary>The filter's enum values, in the order the dropdown lists them.</summary>
        public static readonly ConnectionsFilter[] FilterOrder =
        {
            ConnectionsFilter.All,
            ConnectionsFilter.Outbound,
            ConnectionsFilter.Inbound
        };

        /// <summary>The sort key's enum values, in the order the dropdown lists them.</summary>
        public static readonly ConnectionsSort[] SortOrder =
        {
            ConnectionsSort.Distance,
            ConnectionsSort.Band,
            ConnectionsSort.SignalStrength,
            ConnectionsSort.Name
        };

        /// <summary>The translated label for a filter.</summary>
        /// <param name="filter">The filter.</param>
        /// <returns>Display text, translated at this call (D42).</returns>
        public static string LabelOf(ConnectionsFilter filter)
        {
            switch (filter)
            {
                case ConnectionsFilter.Outbound:
                    return Localize.Text(LocalizedStrings.ConnectionOutbound);
                case ConnectionsFilter.Inbound:
                    return Localize.Text(LocalizedStrings.ConnectionInbound);
                default:
                    return Localize.Text(LocalizedStrings.FilterAll);
            }
        }

        /// <summary>The translated label for a sort key.</summary>
        /// <param name="sort">The sort key.</param>
        /// <returns>Display text, translated at this call (D42).</returns>
        public static string LabelOf(ConnectionsSort sort)
        {
            switch (sort)
            {
                case ConnectionsSort.Band:
                    return Localize.Text(LocalizedStrings.SortByBand);
                case ConnectionsSort.SignalStrength:
                    return Localize.Text(LocalizedStrings.SortBySignalStrength);
                case ConnectionsSort.Name:
                    return Localize.Text(LocalizedStrings.SortByName);
                default:
                    return Localize.Text(LocalizedStrings.SortByDistance);
            }
        }

        /// <summary>Writes the query's state into the filter dropdown and follows its changes.</summary>
        /// <param name="dropdownField">The report's filter dropdown.</param>
        /// <param name="warn">Warning sink, for a selection this query cannot map.</param>
        /// <remarks>
        /// The dropdown's own value is set from the query BEFORE the callback is registered, so
        /// seeding the control cannot fire a change event and re-enter the window's rebuild.
        /// </remarks>
        public void BindFilter(DropdownField dropdownField, Action<string> warn)
        {
            if (dropdownField == null)
            {
                return;
            }

            List<string> choices = new List<string>(FilterOrder.Length);
            for (int i = 0; i < FilterOrder.Length; i++)
            {
                choices.Add(LabelOf(FilterOrder[i]));
            }

            dropdownField.choices = choices;
            dropdownField.value = LabelOf(Filter);

            dropdownField.RegisterValueChangedCallback(evt =>
            {
                int index = choices.IndexOf(evt.newValue);
                if (index < 0 || index >= FilterOrder.Length)
                {
                    Write(warn, "ui-report: the filter dropdown reported '" + evt.newValue
                        + "', which is not one of the " + FilterOrder.Length + " labels this window "
                        + "built - the filter is unchanged (" + Filter + ")");
                    return;
                }

                Filter = FilterOrder[index];
                Raise();
            });
        }

        /// <summary>Writes the query's state into the sort dropdown and follows its changes.</summary>
        /// <param name="dropdownField">The report's sort dropdown.</param>
        /// <param name="warn">Warning sink, for a selection this query cannot map.</param>
        public void BindSort(DropdownField dropdownField, Action<string> warn)
        {
            if (dropdownField == null)
            {
                return;
            }

            List<string> choices = new List<string>(SortOrder.Length);
            for (int i = 0; i < SortOrder.Length; i++)
            {
                choices.Add(LabelOf(SortOrder[i]));
            }

            dropdownField.choices = choices;
            dropdownField.value = LabelOf(Sort);

            dropdownField.RegisterValueChangedCallback(evt =>
            {
                int index = choices.IndexOf(evt.newValue);
                if (index < 0 || index >= SortOrder.Length)
                {
                    Write(warn, "ui-report: the sort dropdown reported '" + evt.newValue
                        + "', which is not one of the " + SortOrder.Length + " labels this window "
                        + "built - the sort is unchanged (" + Sort + ")");
                    return;
                }

                Sort = SortOrder[index];
                Raise();
            });
        }

        /// <summary>Writes the query's direction into the direction button and follows its changes.</summary>
        /// <param name="button">The report's sort-direction button.</param>
        /// <remarks>
        /// The control toggles and re-classes itself (`button-sort-direction--ascending` /
        /// `--descending`, both in the sheet), so this only has to record the value and rebuild. The
        /// legacy also raised <c>Changed</c> at bind time, which reached a window whose vessel was
        /// still null and returned immediately - a no-op that is not reproduced.
        /// </remarks>
        public void BindDirection(SortDirectionButton button)
        {
            if (button == null)
            {
                return;
            }

            button.direction = Direction;
            button.directionChanged += direction =>
            {
                Direction = direction;
                Raise();
            };
        }

        /// <summary>Whether a row belongs on screen under the current filter.</summary>
        /// <param name="row">The row.</param>
        /// <returns><c>true</c> when the row should be listed.</returns>
        public bool Matches(ConnectionRow row)
        {
            switch (Filter)
            {
                case ConnectionsFilter.Outbound:
                    return row.VesselIsSource;
                case ConnectionsFilter.Inbound:
                    return !row.VesselIsSource;
                default:
                    return true;
            }
        }

        /// <summary>Orders the rows in place by the current sort key and direction.</summary>
        /// <param name="rows">The rows to order.</param>
        /// <remarks>
        /// Total, so the order cannot twitch between refreshes - see the file header. A bandless link
        /// (`BandIndex == -1`) sorts after every banded link when ascending, because "no band" is not
        /// band zero; when descending it consequently sorts first, which is the same statement read
        /// the other way.
        /// </remarks>
        public void ApplySort(List<ConnectionRow> rows)
        {
            if (rows == null || rows.Count < 2)
            {
                return;
            }

            rows.Sort(Compare);
        }

        /// <summary>One line naming the query's state, for the report's open line.</summary>
        /// <returns>e.g. <c>filter=All sort=Distance desc</c>.</returns>
        public string Describe()
        {
            return "filter=" + Filter + " sort=" + Sort
                + " " + (Direction == SortDirection.Ascending ? "asc" : "desc");
        }

        /// <summary>The comparison behind <see cref="ApplySort"/>.</summary>
        /// <param name="a">One row.</param>
        /// <param name="b">Another row.</param>
        /// <returns>The ordered comparison, already flipped for the direction.</returns>
        private int Compare(ConnectionRow a, ConnectionRow b)
        {
            int compared;
            switch (Sort)
            {
                case ConnectionsSort.Band:
                    compared = BandKey(a).CompareTo(BandKey(b));
                    break;
                case ConnectionsSort.SignalStrength:
                    compared = a.SignalStrength.CompareTo(b.SignalStrength);
                    break;
                case ConnectionsSort.Name:
                    compared = string.Compare(a.OtherName, b.OtherName, StringComparison.Ordinal);
                    break;
                default:
                    compared = a.DistanceMeters.CompareTo(b.DistanceMeters);
                    break;
            }

            if (compared == 0)
            {
                compared = a.OtherIndex.CompareTo(b.OtherIndex);
            }

            return Direction == SortDirection.Ascending ? compared : -compared;
        }

        /// <summary>A bandless link's sort key: after every band, ascending.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The band index, or <see cref="int.MaxValue"/> for "no band".</returns>
        private static int BandKey(ConnectionRow row)
        {
            return row.BandIndex < 0 ? int.MaxValue : row.BandIndex;
        }

        /// <summary>Raises <see cref="Changed"/> for the window to rebuild on.</summary>
        private void Raise()
        {
            Action changed = Changed;
            if (changed != null)
            {
                changed();
            }
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
    }
}
