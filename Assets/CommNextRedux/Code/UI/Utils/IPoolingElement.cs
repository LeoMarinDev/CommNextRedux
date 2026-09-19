// CommNextRedux - one element's contract with the list pool.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Utils/IPoolingElement.cs (11 lines), MIT. Ported as-is:
//   the interface is one property and the port's own row controllers are its only implementers, so
//   there is nothing in it that this pin can invalidate.
//
// WHY IT EXISTS AT ALL
//   `UIToolkitExtensions.PoolChildren` reuses the rows a list already has instead of rebuilding a
//   `VisualTreeAsset` clone per item per refresh. To do that it must be able to get from a parent's
//   child element back to the controller that owns it - and that is exactly what `Root` is for: the
//   controller writes `Root.userData = this` at construction (`UIToolkitElement`), and the pool
//   reads `child.userData as TElement` back out. The vessel report refreshes on a 0.2 s tick while
//   it is open, so without the pool a 30-row report would clone 30 templates five times a second.

using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// A controller that owns one row of a pooled list: the row's root element, and nothing else.
    /// </summary>
    /// <remarks>
    /// Deliberately minimal. The pool must not need to know what a row <i>is</i> - the binder the
    /// caller passes is where the row's data is written - so the only thing that has to be part of
    /// the contract is how to reach the element to parent and to remove.
    /// </remarks>
    public interface IPoolingElement
    {
        /// <summary>The element this controller manages, as parented into the list.</summary>
        VisualElement Root { get; }

        /// <summary>
        /// Whether this row can be bound - <c>false</c> for a controller whose template failed to
        /// clone, which the pool drops rather than shows.
        /// </summary>
        /// <remarks>
        /// <b>Why the pool needs this at all.</b> A row that cannot be bound would sit in the list as
        /// a blank element with a correct row count in the log - "the window shows nothing" with no
        /// failure anywhere, which is the exact ambiguity this port's empty-window rule exists to
        /// remove. The failed controller has already logged the reason at Error by the time the pool
        /// asks, so this is a classification, never a second report.
        /// </remarks>
        bool Usable { get; }
    }
}
