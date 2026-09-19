// CommNextRedux - a controller backed by its own clone of one bundle page.
//
// LEGACY PROVENANCE
//   mods-outdated/CommNext/src/CommNext/UI/Utils/UIToolkitElement.cs (28 lines), MIT. The shape is
//   the legacy's - a base class that clones a template and parks `this` in the clone's `userData`
//   so the list pool can find the controller again. Two things had to change, and the second one is
//   a real behaviour difference.
//
//   1. THE ASSET ROUTE. The legacy loaded through SpaceWarp 1:
//
//        AssetManager.GetAsset<VisualTreeAsset>($"{ModGuid}/commnext_ui/ui/{assetPath.ToLower()}")
//
//      That member does not exist on this pin. Assets come out of the mod's own prebuilt
//      AssetBundle, which is the route P7 already uses for the ruler mesh and P8a for the three
//      window templates: `CommNextUIManager.LoadAsset<VisualTreeAsset>(container)`. The container
//      name is lower-case and is read from `Deploy/obj/bundle-audit.log` - bundle container names
//      are case-sensitive on this platform, and the legacy's `.ToLower()` is why.
//
//   2. A FAILED TEMPLATE NO LONGER THROWS, IT REPORTS. The legacy's `Instantiate()` dereferenced the
//      load result, so a template that failed to load - or a bundle that was not there - was a
//      `NullReferenceException` from inside the row's constructor, thrown on a refresh tick while
//      the window was already on screen. Here `Root` is never null (a controller whose template
//      failed gets an empty placeholder root and logs at Error, once per controller type), and
//      `Usable` says whether the clone really exists. `Bind` refuses to run on an unusable row, so
//      a missing template costs one Error line and one empty row instead of an exception storm.
//
// WHY `userData` AND NOT A DICTIONARY
//   It is the legacy's mechanism and it is the right one here: the association is per element, it
//   dies with the element, and it cannot go stale the way a side table keyed by index would. The
//   pool verifies it - `ReferenceEquals(element.Root, child)` - so a `userData` that carries
//   something else is treated as "not one of ours" rather than cast blindly.

using System;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// Base class for a controller whose visual tree is one clone of a bundle page.
    /// </summary>
    /// <remarks>
    /// The derived class resolves its own named elements in its constructor (as the legacy did) and
    /// decides what "usable" means for its own markup - see the two row controllers.
    /// </remarks>
    public class UIToolkitElement
    {
        /// <summary>The row's root element. Never <c>null</c>, even when the template failed.</summary>
        protected VisualElement _root;

        /// <summary>Whether the template cloned and the row can be bound.</summary>
        protected bool _templateLoaded;

        /// <summary>The bundle container this row was cloned from, for the failure lines.</summary>
        private readonly string _container;

        /// <summary>The row's root element, as the list pool parents it.</summary>
        public VisualElement Root
        {
            get { return _root; }
        }

        /// <summary>Whether this row's template cloned. A row that is not usable cannot be bound.</summary>
        /// <remarks>
        /// Virtual, because "the template cloned" is not the whole story for a row that also has to
        /// resolve named elements inside it: a page that cloned but is missing the label it binds
        /// would otherwise be pooled into the list and rendered as an empty box, with a correct row
        /// count in the log. Both row controllers refine this.
        /// </remarks>
        public virtual bool Usable
        {
            get { return _templateLoaded; }
        }

        /// <summary>
        /// Clones one bundle page and parks <c>this</c> in the clone's <c>userData</c>.
        /// </summary>
        /// <param name="container">The bundle container name, lower-case, e.g.
        /// <c>assets/commnextredux/ui/components/networkconnectionview.uxml</c>.</param>
        public UIToolkitElement(string container)
        {
            _container = container;
            _root = new VisualElement();

            VisualTreeAsset template = CommNextUIManager.LoadAsset<VisualTreeAsset>(container);
            if (template == null)
            {
                LogError(GetType().Name + ": '" + container + "' is not in the UI bundle (route="
                    + CommNextUIManager.BundleRoute + "), so this row cannot be built - the row is a "
                    + "placeholder and the list it belongs to will be short. The bundle's own bind "
                    + "line above says whether it opened at all");
                return;
            }

            try
            {
                _root = template.Instantiate();
            }
            catch (Exception exception)
            {
                LogError(GetType().Name + ": cloning '" + container + "' threw ("
                    + exception.GetType().Name + ": " + exception.Message + ") - a custom control on "
                    + "this page did not resolve (the ui-control-check lines above cover that case); "
                    + "the row is a placeholder");
                return;
            }

            if (_root == null)
            {
                _root = new VisualElement();
                LogError(GetType().Name + ": cloning '" + container + "' returned no element - the "
                    + "row is a placeholder");
                return;
            }

            _templateLoaded = true;
            _root.userData = this;
        }

        /// <summary>
        /// Reports a condition the user's log must carry, through the plugin's own sink.
        /// </summary>
        /// <param name="message">The line.</param>
        /// <remarks>
        /// Null-guarded on <c>Instance</c>: a row can legitimately be constructed before the plugin
        /// has finished initialising, and a log call on a null sink inside a constructor is the
        /// documented way to turn a live mod into a dead one (dev guide 61.3).
        /// </remarks>
        protected static void LogError(string message)
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin != null)
            {
                plugin.LogErrorLine(message);
            }
        }

        /// <summary>Reports a recoverable condition, with the same null guard as <see cref="LogError"/>.</summary>
        /// <param name="message">The line.</param>
        protected static void LogWarning(string message)
        {
            CommNextReduxPlugin plugin = CommNextReduxPlugin.Instance;
            if (plugin != null)
            {
                plugin.LogWarningLine(message);
            }
        }
    }
}
