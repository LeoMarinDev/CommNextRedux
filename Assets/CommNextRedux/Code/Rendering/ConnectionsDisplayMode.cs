// CommNextRedux - which connection lines are drawn.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/ConnectionsDisplayMode.cs - carried over
//   verbatim, the enum and both extensions, including the ring order.
//
// WHY IT IS THREE STATES AND NOT A BOOLEAN
//   `None` is not "off" in the sense of "do nothing": it is the only state in which the already
//   drawn lines are destroyed. The renderer's property setter calls ClearConnections() on the
//   transition into None instead of merely skipping the redraw, so the map is emptied rather than
//   frozen - and a skip-and-leave would leave the previous mode's lines on the map forever, which
//   is why that behaviour must stay in the setter and cannot be hoisted into the caller.
//
//   `Active` is the legacy's "only the active vessel's path to the control source" mode. On this
//   port the path is walked out of `NetworkEngine`'s own spanning tree (PredecessorOf from the
//   vessel's node up to the source) rather than out of the legacy's `TryGetNetworkPath`, because
//   the engine is where this port's tree lives.

using System;

namespace CommNextRedux.Rendering
{
    /// <summary>Which connection lines the map renderer draws.</summary>
    public enum ConnectionsDisplayMode
    {
        /// <summary>Nothing is drawn, and anything already drawn is destroyed.</summary>
        None,

        /// <summary>Every edge of the connection tree.</summary>
        Lines,

        /// <summary>
        /// Only the edges on the active vessel's path to the control source - the vessel's own
        /// chain of relays. An empty path (a vessel with no node) draws nothing.
        /// </summary>
        Active
    }

    /// <summary>The mode's own predicates and its ring order.</summary>
    public static class ConnectionsDisplayModeExtensions
    {
        /// <summary>Whether this mode draws anything at all.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns><c>true</c> for <see cref="ConnectionsDisplayMode.Lines"/> and <see cref="ConnectionsDisplayMode.Active"/>.</returns>
        public static bool IsEnabled(this ConnectionsDisplayMode mode)
        {
            return mode != ConnectionsDisplayMode.None;
        }

        /// <summary>
        /// Advances one step around the ring: <c>None -&gt; Lines -&gt; Active -&gt; None</c>.
        /// </summary>
        /// <param name="mode">The current mode.</param>
        /// <returns>The next mode.</returns>
        /// <remarks>
        /// The ring order is the legacy's and is load-bearing for anyone who has learnt the
        /// controls: the first press enables, the second narrows to the active vessel, the third
        /// clears the map. The <c>ArgumentOutOfRangeException</c> on an unknown value is kept too -
        /// it is the only way a corrupt setting (a hand-edited config holding a number no enum
        /// member has) becomes visible rather than silently cycling.
        /// </remarks>
        public static ConnectionsDisplayMode Next(this ConnectionsDisplayMode mode)
        {
            switch (mode)
            {
                case ConnectionsDisplayMode.None:
                    return ConnectionsDisplayMode.Lines;
                case ConnectionsDisplayMode.Lines:
                    return ConnectionsDisplayMode.Active;
                case ConnectionsDisplayMode.Active:
                    return ConnectionsDisplayMode.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}
