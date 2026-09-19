// CommNextRedux - the UI Toolkit helpers this port actually uses.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Utils/UIToolkitExtensions.cs - 147 lines, six public
//   members. Three of them are NOT ported here and the reasons are different in each case, so they
//   are recorded rather than silently dropped:
//
//   * `SetDefaultPosition(Func<Vector2,Vector2>)` and `CenterByDefault()` are NOT the legacy's own
//     methods. They are `UitkForKsp2.API.Extensions` members that the legacy's controllers called as
//     extensions - measured in the installed runtime:
//
//       UitkForKsp2.API.Extensions | 239: SetDefaultPosition(VisualElement element,
//                                        Func<Vector2,Vector2> calculatePosition)
//       UitkForKsp2.API.Extensions | 240: CenterByDefault(VisualElement element)
//
//     Nothing has to be ported for either; the port calls the same library members the legacy did, and
//     the toolbar's position comes out of `SetDefaultPosition` exactly as before.
//
//   * `StopMouseEventsPropagation()` is DROPPED, and it is not a porting oversight: the API it drives
//     does not exist on this pin. It walked a hand-written list of `InputAction`s
//     (`Game.Input.MapView.mousePrimary`, `Game.Input.Flight.CameraZoom`, ...) and disabled them on
//     pointer-enter, re-enabling them on pointer-leave. What this runtime offers for the same job is
//     `ReduxLib.GameInterfaces.IInputManager`, whose complete member list is measured as
//     `Ready`, `SetUitkInputLocks()`, `RestoreUitkInputLocks()`, `SetUitkTextInputLocks()`,
//     `RestoreUitkTextInputLocks()`, `BindHideAction`/`UnbindHideAction` - there is no per-action
//     surface to walk, and `GameInstance.Input`'s action groups are a different mechanism entirely.
//     The supported replacement is UitkForKsp2's own per-window switch,
//     `WindowOptions.BlockGameInput = true`, which adds a `GameInputBlockManipulator` to the document
//     root (IL: `SetupRootElement` calls `Extensions.BlockGameInput(root)` when the option is set) and
//     that manipulator is what calls `IInputManager.SetUitkInputLocks()` / `RestoreUitkInputLocks()`
//     on its own pointer-enter/leave pair. Both in-repo ports that are validated in game use it:
//     `mods/K2D2Redux/Assets/K2D2/Code/K2D2_Plugin.cs` sets `windowOptions.BlockGameInput = true` and
//     names `Redux.ApiImpls.ReduxInputManager.SetUitkInputLocks()` in its comment, and
//     `mods/FlightPlanRedux/Assets/FlightPlan/Code/FlightPlanPlugin.cs` documents the same option.
//     The one legacy caller of the dropped method was `VesselReportWindowController` - P8b's window -
//     so P8b enabled `BlockGameInput` on it instead of porting the hack. **P9.4 removed it again**
//     (D62, F84): the option's cost is all eight KSP2 input locks for as long as the pointer merely
//     hovers the window, and the one case it was added for - a wheel over the list must not zoom the
//     map - is now answered by the layer fix (D60). No window in this port sets the option today.
//
//   * `PoolChildren` and `ToggleClassesIf` belonged to the vessel report's list rendering and landed
//     with it in P8b (below). Both are the legacy's, and each has one behaviour change, stated on
//     the method.
//
// WHAT IS HERE
//   `RTEColor(string, Color)` - the rich-text colour helper. D27 (the modulator's Part Action Menu
//   dropdowns) uses it: each dropdown row's label is the band's display name tinted with the band's
//   own colour, which is the legacy's own rendering of the same list.
//   `RTEColor(string, string)` - the same tag with a literal colour, used by the report's rows.
//   `ToggleClassesIf` - the two-class-list swap the report's direction tag needs.
//   `PoolChildren` - the row pool the report's two lists are built with.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// The UI Toolkit helpers shared by this port's windows and part data.
    /// </summary>
    public static class UIToolkitExtensions
    {
        /// <summary>
        /// Wraps the text in a rich-text colour tag, for a UI Toolkit label with rich text enabled.
        /// </summary>
        /// <param name="text">The text to colour.</param>
        /// <param name="color">The colour, written as `#RRGGBB`.</param>
        /// <returns>The tagged string.</returns>
        /// <remarks>
        /// Alpha is dropped, as in the legacy: the tag is `#RRGGBB`, which `ColorUtility.ToHtmlStringRGB`
        /// produces by definition. The port's band colours are all `a = 1` (divergence: the legacy's S and
        /// Ka entries carried `a = 0` - see `NetworkBands`), so nothing is lost in practice, and a
        /// translucent rich-text colour is not something this tag can express anyway.
        /// </remarks>
        public static string RTEColor(this string text, string color)
        {
            return "<color=" + color + ">" + text + "</color>";
        }

        /// <summary>
        /// Wraps the text in a rich-text colour tag built from a <see cref="Color"/>.
        /// </summary>
        /// <param name="text">The text to colour.</param>
        /// <param name="color">The colour.</param>
        /// <returns>The tagged string.</returns>
        /// <remarks>
        /// The overload the Part Action Menu path uses, and the only live caller of the string form
        /// above - so both are exercised by the same dropdown.
        /// </remarks>
        public static string RTEColor(this string text, Color color)
        {
            return RTEColor(text, "#" + ColorUtility.ToHtmlStringRGB(color));
        }

        /// <summary>
        /// Adds one class list and removes the other, according to a boolean.
        /// </summary>
        /// <param name="element">The element to re-class.</param>
        /// <param name="toggle">Which list wins: <c>true</c> for <paramref name="classesIf"/>.</param>
        /// <param name="classesIf">The classes to add when <paramref name="toggle"/> is true.</param>
        /// <param name="classesOtherwise">The classes to add when it is false.</param>
        /// <remarks>
        /// The legacy's implementation, unchanged in behaviour: the "otherwise" list is removed first
        /// when the toggle is on, so an element that somehow carries both keeps exactly one. The call
        /// sites are the report's direction tag (<c>direction__tag--outbound</c> vs
        /// <c>direction__tag--inbound</c>, which the sheet says are colour-and-rotation opposites) and
        /// the power icon (<c>icon--no-power</c> vs <c>icon--no-power-active</c>, the faded form for
        /// "the fault is on this vessel").
        /// </remarks>
        public static void ToggleClassesIf(this VisualElement element, bool toggle,
            string[] classesIf, string[] classesOtherwise)
        {
            if (element == null || classesIf == null || classesOtherwise == null)
            {
                return;
            }

            for (int i = 0; i < classesOtherwise.Length; i++)
            {
                if (toggle)
                {
                    element.RemoveFromClassList(classesOtherwise[i]);
                }
                else
                {
                    element.AddToClassList(classesOtherwise[i]);
                }
            }

            for (int i = 0; i < classesIf.Length; i++)
            {
                if (toggle)
                {
                    element.AddToClassList(classesIf[i]);
                }
                else
                {
                    element.RemoveFromClassList(classesIf[i]);
                }
            }
        }

        /// <summary>
        /// Makes a list's children match a list of items, reusing the rows already parented.
        /// </summary>
        /// <typeparam name="TItem">The item type - the report's rows are structs.</typeparam>
        /// <typeparam name="TElement">The row controller type.</typeparam>
        /// <param name="parent">The element the rows live in.</param>
        /// <param name="items">The items to show, in order. May be <c>null</c> for "nothing".</param>
        /// <param name="binder">Writes one item into one row.</param>
        /// <remarks>
        /// <para>
        /// <b>What is the legacy's.</b> The reuse rule, the order, the trim-from-the-end, and the
        /// <c>userData</c>-carries-the-controller contract. A row that already exists is never
        /// re-cloned, which is the whole point: the report re-binds every 0.2 s while it is open.
        /// </para>
        /// <para>
        /// <b>What is not, and why each is a fix rather than a liberty.</b> (1) The surplus trim is a
        /// <c>RemoveAt</c> loop on the parent rather than <c>parent.Remove(children[i])</c> on a
        /// snapshot of the old children: the legacy's snapshot is stale the moment the first insert
        /// happens, so a list that grew and then shrank could try to remove children that had already
        /// been reordered. (2) A child whose <c>userData</c> is not this row type - or is a controller
        /// whose <c>Root</c> is a different element - is treated as foreign and pushed past the end
        /// instead of being cast, which is what the legacy's `(TElement)children[i].userData` would
        /// have thrown on. (3) A row whose template failed (<c>Root</c> is a placeholder) is skipped
        /// rather than added: the row's own constructor has already logged the reason.
        /// </para>
        /// <para>
        /// <b>The parent must be the list's content container, not a <c>ScrollView</c> itself.</b>
        /// <c>ScrollView.contentContainer</c> is where <c>ScrollView.Add</c> forwards to on this
        /// generation; pooling into the <c>ScrollView</c> element would put rows beside its viewport
        /// instead of inside it, which looks like "the window is empty" while the row count in the
        /// log is correct. The report passes the container explicitly for that reason.
        /// </para>
        /// </remarks>
        public static void PoolChildren<TItem, TElement>(
            this VisualElement parent,
            List<TItem> items,
            Action<TItem, TElement> binder)
            where TElement : class, IPoolingElement, new()
        {
            if (parent == null || binder == null)
            {
                return;
            }

            int wanted = items == null ? 0 : items.Count;

            for (int i = 0; i < wanted; i++)
            {
                TElement element = null;
                if (i < parent.childCount)
                {
                    VisualElement child = parent.ElementAt(i);
                    element = child == null ? null : child.userData as TElement;
                    if (element != null && !ReferenceEquals(element.Root, child))
                    {
                        // A controller that has been reparented, or a stale userData on a reused
                        // element: not one of ours in this position.
                        element = null;
                    }
                }

                if (element == null || !element.Usable)
                {
                    if (element != null && !element.Usable)
                    {
                        // A placeholder from a failed template: take it out and let the next child
                        // move up, so a live row is never hidden behind a dead one.
                        parent.RemoveAt(i);
                        i--;
                        continue;
                    }

                    TElement created = new TElement();
                    VisualElement row = created.Root;
                    if (row == null)
                    {
                        continue;
                    }

                    if (i < parent.childCount)
                    {
                        parent.Insert(i, row);
                    }
                    else
                    {
                        parent.Add(row);
                    }

                    element = created;
                }

                binder(items[i], element);
            }

            while (parent.childCount > wanted)
            {
                parent.RemoveAt(parent.childCount - 1);
            }
        }
    }
}
