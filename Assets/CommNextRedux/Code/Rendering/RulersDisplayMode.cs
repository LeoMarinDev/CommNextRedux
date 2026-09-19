// CommNextRedux - which range rulers are drawn.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/RulersDisplayMode.cs - carried over verbatim.
//
// PHASE 6 BOUNDARY - READ THIS BEFORE USING IT
//   This enum is ported NOW, with no ruler rendering behind it, and that is deliberate: it is the
//   type of `ConnectionsRenderer.RulersDisplayMode`, the renderer owns the mode state for both of
//   its object families, and splitting the type out later would mean editing the renderer again in
//   Phase 7 for no reason.
//
//   WHAT THAT MEANS IN THIS PHASE: the mode defaults to `None` and nothing ever sets it to anything
//   else, so the renderer's ruler branch never runs, no ruler object is ever created, and the enum's
//   `Relays`/`All` values have no implementation behind them. Phase 7 (the range rulers) supplies
//   that half: the geometry (`MapRulerComponent`, `MapSphereRulerComponent`) and the two statics
//   the legacy's renderer held for it (`RulerSpherePrefab`, `TestSpherePrefab`) were dropped from
//   this port for exactly that reason.
//
//   The DEFAULT IS ONE PLACE THE PORT DIFFERS FROM THE LEGACY, and it has to be. The legacy started
//   in `RulersDisplayMode.Relays` because it shipped rulers; defaulting to `Relays` here would mean
//   a mode that claims to be on with nothing drawing it, which reads as "the ruler feature is
//   broken" rather than "the ruler feature is not in this build".

using System;

namespace CommNextRedux.Rendering
{
    /// <summary>Which range rulers the map renderer draws. Implemented by Phase 7.</summary>
    public enum RulersDisplayMode
    {
        /// <summary>No rulers.</summary>
        None,

        /// <summary>Only nodes carrying an enabled relay.</summary>
        Relays,

        /// <summary>Every node - relays and plain antennas.</summary>
        All
    }

    /// <summary>The mode's own predicates and its ring order.</summary>
    public static class RulersDisplayModeExtensions
    {
        /// <summary>Whether this mode draws anything at all.</summary>
        /// <param name="mode">The mode.</param>
        /// <returns><c>true</c> for <see cref="RulersDisplayMode.Relays"/> and <see cref="RulersDisplayMode.All"/>.</returns>
        public static bool IsEnabled(this RulersDisplayMode mode)
        {
            return mode != RulersDisplayMode.None;
        }

        /// <summary>
        /// Advances one step around the ring: <c>None -&gt; Relays -&gt; All -&gt; None</c>.
        /// </summary>
        /// <param name="mode">The current mode.</param>
        /// <returns>The next mode.</returns>
        /// <remarks>The legacy's ring order, kept so the controls stay the ones its README describes.</remarks>
        public static RulersDisplayMode Next(this RulersDisplayMode mode)
        {
            switch (mode)
            {
                case RulersDisplayMode.None:
                    return RulersDisplayMode.Relays;
                case RulersDisplayMode.Relays:
                    return RulersDisplayMode.All;
                case RulersDisplayMode.All:
                    return RulersDisplayMode.None;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}
