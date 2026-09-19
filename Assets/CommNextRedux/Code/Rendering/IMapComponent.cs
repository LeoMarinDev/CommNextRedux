// CommNextRedux - the map-component identity contract.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/Rendering/Behaviors/IMapComponent.cs - five lines, carried
//   over unchanged in meaning: a map component is a thing the renderer can find again by id.
//
// WHY IT EXISTS
//   The renderer keys its live objects by a string built from the two map items a connection joins,
//   and it needs to read that key back off a component it is about to prune or that has just
//   destroyed itself. The interface is what lets the component and the renderer agree on the shape
//   without the renderer knowing the component's type - which is what will let Phase 7 add rulers
//   that share the same prune path without either side enumerating the other.
//
//   ONE DELIBERATE CHANGE FROM THE LEGACY: it declared `string Id { get; protected set; }`. That is
//   a default-interface-member accessor (C# 8) whose runtime support on this Mono is not something
//   this port wants to depend on for a five-line interface, and no member of it needs the
//   protection - the renderer only reads the id, and the component only writes it. Declared here as
//   a plain read/write property, which every implementing type already satisfies.

namespace CommNextRedux.Rendering
{
    /// <summary>
    /// A map-space object the renderer can identify and prune.
    /// </summary>
    /// <remarks>
    /// Deliberately free of any game type: this is the seam between the renderer's bookkeeping and
    /// what is actually drawn, and it is the only piece of the rendering tree that Phase 7's rulers
    /// will have to satisfy as well.
    /// </remarks>
    public interface IMapComponent
    {
        /// <summary>
        /// The stable identity of this component, unique among the objects one renderer manages.
        /// </summary>
        /// <remarks>
        /// For a connection it is the two map-item guids joined in a canonical (sorted) order, so the
        /// same pair of nodes always produces the same key - whichever way round a refresh names them
        /// - and the renderer can recognise a line it has already drawn.
        /// </remarks>
        string Id { get; set; }
    }
}
