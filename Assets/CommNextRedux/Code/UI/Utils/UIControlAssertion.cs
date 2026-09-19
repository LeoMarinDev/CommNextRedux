// CommNextRedux - the in-port player-side assertion that the five custom controls really resolve.
//
// WHY THIS EXISTS AT ALL
//   `CommNextUIFactoryRegistration` registers the legacy `UxmlFactory` instances with Unity's
//   internal `VisualElementFactoryRegistry`, and its own `Verify` reads the registry back - but a
//   registry that answers a lookup is not the same claim as "a UXML that names this control
//   instantiates in this player". P2 measured the second claim FAILING here, in the player, on
//   code the editor had already accepted:
//
//     EXC: Element 'CommNext.Unity.Runtime.Controls.BandIcon' is missing a UxmlElementAttribute
//          and has no registered factory method.
//
//   So this class runs P2's own probe inside the shipped mod: it clones the three bundle pages that
//   actually name a custom control, and for each control asserts it is present, of the expected
//   type, and reachable by its markup name. The probe table below is `Tools/diag/DiagPlugin.cs`'s
//   (`ControlProbes`, line for line) - the instrument that produced the FAIL, moved from a
//   temporary diag mod into the mod itself so every future launch re-answers the question.
//
//   This is deliberately NOT a second registry check: `CommNextUIFactoryRegistration.Verify` proves
//   the registry answers by name, and this proves the importer then builds the element. Neither is
//   a proxy for the other, and the failure they describe is different in each case.
//
// WHY TYPE, NAME **AND VALUE**
//   They fail for different reasons. A type that does not resolve makes the page unimportable - the
//   whole window is empty and the log carries the importer's exception. A NAME that is dropped
//   leaves the element present and correctly typed while every `Q("name")` for it returns null, so
//   a controller binds nothing and the window comes up empty with no error anywhere. On 6000.4.1f1
//   a legacy-traits control loses `name` unless it re-declares it in its own `UxmlTraits`
//   (`VisualElement.UxmlTraits` declares zero attribute fields on this generation); the port's five
//   controls each declare it, and this is the assertion that proves the fix survived packing.
//
//   A VALUE that is dropped is the quietest of the three, and Unity warns about exactly it at
//   import (`Control ... uses the deprecated UxmlTraits API. Its attributes were ignored on import,
//   which may cause visual errors or missing data.` - four such lines in `uxml-verify.log`). The
//   element is present, typed and named, and draws a band code of `""` in the default colour. So
//   the markup's own literals are read back off the live control (`code="X"`, `color="#B325D4FF"`,
//   `strength="Full"`, `direction="Descending"`), which is the only check that distinguishes
//   "the legacy traits path still delivers attributes in this player" from "it delivers names".
//
// WHY THREE PAGES AND FIVE CONTROLS
//   Only three of the five controls are named by a shipped UXML in this phase: `BandIcon`
//   (BandRow + NetworkConnectionView), `SignalStrengthIcon` (NetworkConnectionView) and
//   `SortDirectionButton` (VesselReportWindow). `TabSelector` and `TableSeparatorTitle` are P8b's
//   windows' controls; registering them is still right - it is one line and they are already
//   compiled in - but nothing in this build can instantiate them from markup, so asserting them
//   here would be asserting an untruth. `CommNextUIFactoryRegistration.Verify` covers all five, at
//   the level it can honestly cover them.
//
// THE INSTANTIATION IS REAL, AND SO IS ITS COST
//   Each of the three templates is cloned once, into a detached element that is never attached to a
//   panel and never destroyed by anything else. That is exactly the code path the player failed on
//   (`VisualTreeAsset.Instantiate`), it runs once per launch, and the clones are dropped on the
//   floor when this method returns. A panel-less clone is legal: no layout runs, and the three
//   controls' constructors build nothing but sub-elements and style values.

using System;
using CommNext.Unity.Runtime.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Utils
{
    /// <summary>
    /// Clones the bundle pages that name a custom control and asserts each one is present, typed,
    /// named and carrying the attribute values the markup declares.
    /// </summary>
    /// <remarks>
    /// Static, like the rest of this port's runtime services: one process, one bundle, one answer. It
    /// holds no state beyond the last run's counts, which exist so a later phase can print a verdict
    /// without re-running the clones.
    /// </remarks>
    public static class UIControlAssertion
    {
        /// <summary>One control on one page: the container, the element's name, its full type name.</summary>
        /// <remarks>
        /// The container is the bundle's own container name, lower-case, as `bundle-audit.log` lists
        /// it - bundle names are case-sensitive on this platform and a mis-cased name loads nothing.
        /// The element name and the type name are the markup's, verbatim.
        /// </remarks>
        private sealed class Probe
        {
            /// <summary>Creates a probe.</summary>
            /// <param name="container">The bundle container the control lives on.</param>
            /// <param name="element">The markup's <c>name</c> attribute for the control.</param>
            /// <param name="typeName">The control's full type name.</param>
            /// <param name="expectedCode">The markup's own <c>code</c> value, for a <c>BandIcon</c>.</param>
            /// <param name="expectedColor">The markup's own <c>color</c> value, for a <c>BandIcon</c>.</param>
            /// <param name="expectedEnum">The markup's own enum attribute value, as text.</param>
            public Probe(string container, string element, string typeName, string expectedCode = null,
                Color? expectedColor = null, string expectedEnum = null)
            {
                Container = container;
                Element = element;
                TypeName = typeName;
                ExpectedCode = expectedCode;
                ExpectedColor = expectedColor;
                ExpectedEnum = expectedEnum;
            }

            /// <summary>The bundle container to clone.</summary>
            public string Container { get; }

            /// <summary>The element name to look up with <c>Q</c>.</summary>
            public string Element { get; }

            /// <summary>The full type name the element must have.</summary>
            public string TypeName { get; }

            /// <summary>
            /// The value the markup gives the control's <c>code</c> attribute, or <c>null</c> to skip.
            /// </summary>
            /// <remarks>
            /// <b>Why a value check at all, when the name and the type already passed.</b> Unity warns
            /// at import that a legacy-<c>UxmlTraits</c> control's attributes "were ignored on import"
            /// (four such lines in <c>Deploy/obj/uxml-verify.log</c>). Names survive - proven in the
            /// bundle audit - but a control whose *values* were dropped is a band icon with no band
            /// code and a default colour: present, correctly typed, correctly named and wrong. This
            /// reads the markup's own literal back out of the running player, which is the only check
            /// that tells those two cases apart.
            /// </remarks>
            public string ExpectedCode { get; }

            /// <summary>The value the markup gives the control's <c>color</c> attribute.</summary>
            public Color? ExpectedColor { get; }

            /// <summary>
            /// The markup's enum attribute value, compared against the control's own enum's
            /// <c>ToString()</c> (<c>strength="Full"</c>, <c>direction="Descending"</c>).
            /// </summary>
            public string ExpectedEnum { get; }
        }

        /// <summary>How far a markup colour may sit from the read-back value and still count as equal.</summary>
        /// <remarks>
        /// The markup writes <c>#B325D4FF</c> and the control stores the parsed <c>Color</c> in an
        /// inline style, so the two agree to well under one 8-bit step. The tolerance is one 8-bit
        /// step so a rounding difference cannot produce a false failure.
        /// </remarks>
        private const float ColorTolerance = 1f / 255f;

        private const string PagePrefix = "assets/commnextredux/ui/";

        /// <summary>
        /// The four control instances this build can honestly assert, on the three pages that name a
        /// custom control.
        /// </summary>
        /// <remarks>
        /// P2's table, plus the second control on the connection view (the same page names both
        /// <c>SignalStrengthIcon</c> and <c>BandIcon</c>, and the diag probe asserted only the first).
        /// Adding it costs one more lookup on a clone that is already made.
        /// </remarks>
        private static readonly Probe[] Probes =
        {
            new Probe(PagePrefix + "components/bandrow.uxml",
                "band-icon", "CommNext.Unity.Runtime.Controls.BandIcon",
                "X", new Color(0x9A / 255f, 0x21 / 255f, 0xB4 / 255f, 1f)),
            new Probe(PagePrefix + "components/networkconnectionview.uxml",
                "signal-strength-icon", "CommNext.Unity.Runtime.Controls.SignalStrengthIcon",
                expectedEnum: "Full"),
            new Probe(PagePrefix + "components/networkconnectionview.uxml",
                "band-icon", "CommNext.Unity.Runtime.Controls.BandIcon",
                "X", new Color(0xB3 / 255f, 0x25 / 255f, 0xD4 / 255f, 1f)),
            new Probe(PagePrefix + "vesselreportwindow.uxml",
                "sort-direction-button", "CommNext.Unity.Runtime.Controls.SortDirectionButton",
                expectedEnum: "Descending")
        };

        /// <summary>Whether <see cref="Run"/> has completed.</summary>
        public static bool Ran { get; private set; }

        /// <summary>How many probes passed.</summary>
        public static int Passed { get; private set; }

        /// <summary>How many probes ran at all - fewer than four means a page would not load.</summary>
        public static int Attempted { get; private set; }

        /// <summary>
        /// Runs every probe, logging one line each and a verdict.
        /// </summary>
        /// <param name="log">Receives the per-probe PASS lines and the verdict when it passes.</param>
        /// <param name="warn">Receives the per-probe FAIL lines and the verdict when it fails.</param>
        /// <returns><c>true</c> when every probe passed.</returns>
        /// <remarks>
        /// <para>
        /// <b>Failure is a warning, not an error.</b> The consequence of a failed probe is a window
        /// that draws empty; the `ui-factories:` registration lines above it are what carry the cause
        /// at Error level when the cause is the registration. Logging both as errors would make the
        /// cause and the symptom indistinguishable in a grep.
        /// </para>
        /// <para>
        /// <b>Never throws.</b> This runs inside the window layer's own initialization, and a probe
        /// that threw would take the toolbar down with it - turning a diagnostic into the outage it
        /// was meant to detect.
        /// </para>
        /// </remarks>
        public static bool Run(Action<string> log, Action<string> warn)
        {
            Ran = true;
            Passed = 0;
            Attempted = 0;

            if (CommNextUIManager.Bundle == null)
            {
                Write(warn, "ui-control-check: no bundle is open, so the custom controls cannot be "
                    + "asserted in this player - the window layer's own error above says why");
                return false;
            }

            string currentContainer = null;
            VisualElement currentRoot = null;
            int failed = 0;

            for (int i = 0; i < Probes.Length; i++)
            {
                Probe probe = Probes[i];

                if (probe.Container != currentContainer)
                {
                    currentContainer = probe.Container;
                    currentRoot = Instantiate(probe.Container, warn);
                }

                if (currentRoot == null)
                {
                    // The page itself would not load - one line per probe is noise, and the load
                    // failure above already named the container.
                    failed++;
                    continue;
                }

                Attempted++;
                if (Assert(currentRoot, probe, log, warn))
                {
                    Passed++;
                }
                else
                {
                    failed++;
                }
            }

            bool ok = failed == 0 && Attempted == Probes.Length;
            Write(ok ? log : warn, "ui-control-check: " + Passed + "/" + Probes.Length + " custom "
                + "control element(s) resolve on their own pages - "
                + (ok
                    ? "the importer builds every control this build's markup names, by type and by name"
                    : "at least one control does not survive the bundle round-trip in this player; the "
                        + "per-probe FAIL lines above say which and how"));

            return ok;
        }

        /// <summary>Clones one bundle page, or logs why it could not.</summary>
        /// <param name="container">The container name to load.</param>
        /// <param name="warn">Warning sink.</param>
        /// <returns>The cloned root, or <c>null</c>.</returns>
        private static VisualElement Instantiate(string container, Action<string> warn)
        {
            try
            {
                VisualTreeAsset template = CommNextUIManager.LoadAsset<VisualTreeAsset>(container);
                if (template == null)
                {
                    Write(warn, "ui-control-check: '" + container + "' is not in the bundle - it carries "
                        + "no probeable control in this build");
                    return null;
                }

                return template.Instantiate();
            }
            catch (Exception exception)
            {
                Write(warn, "ui-control-check: cloning '" + container + "' threw ("
                    + exception.GetType().Name + ": " + exception.Message + ") - a custom control on "
                    + "this page did not resolve, which is F15's exact failure shape");
                return null;
            }
        }

        /// <summary>Asserts one control on an already-cloned page.</summary>
        /// <param name="root">The cloned page.</param>
        /// <param name="probe">The control to assert.</param>
        /// <param name="log">Info sink, for the PASS line.</param>
        /// <param name="warn">Warning sink, for the FAIL lines.</param>
        /// <returns><c>true</c> when the element exists, has the expected type and carries the name.</returns>
        /// <remarks>
        /// The order of the three checks is deliberate: a missing element is reported as missing,
        /// never as a name mismatch with a null actual value, so each line names one cause.
        /// </remarks>
        private static bool Assert(VisualElement root, Probe probe, Action<string> log,
            Action<string> warn)
        {
            try
            {
                VisualElement byName = root.Q(probe.Element);
                if (byName == null)
                {
                    Write(warn, "ui-control-check: FAIL | " + probe.Element + " | not found on '"
                        + probe.Container + "' - either the control did not instantiate (F15) or the "
                        + "markup no longer names it");
                    return false;
                }

                if (byName.GetType().FullName != probe.TypeName)
                {
                    Write(warn, "ui-control-check: FAIL | " + probe.Element + " | TYPE MISMATCH on '"
                        + probe.Container + "' - expected " + probe.TypeName + ", got "
                        + byName.GetType().FullName);
                    return false;
                }

                VisualElement byType = FindByType(root, byName.GetType());
                if (byType == null)
                {
                    Write(warn, "ui-control-check: FAIL | " + probe.Element + " | '" + probe.TypeName
                        + "' is instantiated but not reachable by type on '" + probe.Container + "'");
                    return false;
                }

                if (!ReferenceEquals(byName, byType))
                {
                    Write(warn, "ui-control-check: FAIL | " + probe.Element + " | NAME COLLISION on '"
                        + probe.Container + "' - the name resolves to a different instance than the "
                        + "first element of that type");
                    return false;
                }

                if (!AssertValues(byName, probe, log, warn))
                {
                    return false;
                }

                Write(log, "ui-control-check: PASS | " + probe.Element + " | on '" + probe.Container
                    + "' -> " + probe.TypeName + " (by type, by name and with the markup's own values)");
                return true;
            }
            catch (Exception exception)
            {
                Write(warn, "ui-control-check: FAIL | " + probe.Element + " | threw ("
                    + exception.GetType().Name + ": " + exception.Message + ")");
                return false;
            }
        }

        /// <summary>
        /// Reads the markup's own attribute values back off the instantiated control.
        /// </summary>
        /// <param name="element">The control found by name.</param>
        /// <param name="probe">The probe, carrying the values the markup declares.</param>
        /// <param name="log">Info sink.</param>
        /// <param name="warn">Warning sink.</param>
        /// <returns><c>true</c> when every value the probe declares was applied.</returns>
        /// <remarks>
        /// <para>
        /// The counterpart to <c>CommNextUIFactoryRegistration</c>'s registry read-back, one level
        /// further along: the registry can answer correctly and the control still be built with every
        /// attribute dropped, because Unity 6's importer warns that a legacy-<c>UxmlTraits</c>
        /// control's attributes "were ignored on import". The name case is proven by the lookup
        /// above; these are the *values* that a dropped attribute would silently take with it, and
        /// they are read off the live object, not off the asset.
        /// </para>
        /// <para>
        /// One line per probe on success, listing every value it read, so a single grep over the
        /// launch log answers "did the markup's attributes reach the player".
        /// </para>
        /// </remarks>
        private static bool AssertValues(VisualElement element, Probe probe, Action<string> log,
            Action<string> warn)
        {
            bool ok = true;
            System.Text.StringBuilder read = new System.Text.StringBuilder();

            BandIcon band = element as BandIcon;
            if (band != null)
            {
                if (probe.ExpectedCode != null)
                {
                    bool codeOk = band.code == probe.ExpectedCode;
                    ok &= codeOk;
                    read.Append(" code='" + band.code + "'");
                    if (!codeOk)
                    {
                        Write(warn, "ui-control-check: FAIL | " + probe.Element + " | ATTRIBUTE VALUE on '"
                            + probe.Container + "' - code is '" + band.code + "', the markup says '"
                            + probe.ExpectedCode + "' (the attribute was not applied)");
                    }
                }

                if (probe.ExpectedColor.HasValue)
                {
                    Color want = probe.ExpectedColor.Value;
                    Color got = band.color;
                    bool colorOk = Mathf.Abs(got.r - want.r) <= ColorTolerance
                        && Mathf.Abs(got.g - want.g) <= ColorTolerance
                        && Mathf.Abs(got.b - want.b) <= ColorTolerance;
                    ok &= colorOk;
                    read.Append(" color=" + Describe(got));
                    if (!colorOk)
                    {
                        Write(warn, "ui-control-check: FAIL | " + probe.Element + " | ATTRIBUTE VALUE on '"
                            + probe.Container + "' - color is " + Describe(got) + ", the markup says "
                            + Describe(want) + " (the attribute was not applied)");
                    }
                }
            }

            if (probe.ExpectedEnum != null)
            {
                string actual = element is SignalStrengthIcon strength
                    ? strength.strength.ToString()
                    : element is SortDirectionButton direction
                        ? direction.direction.ToString()
                        : null;
                bool enumOk = actual == probe.ExpectedEnum;
                ok &= enumOk;
                read.Append(" enum='" + (actual ?? "<not an enum control>") + "'");
                if (!enumOk)
                {
                    Write(warn, "ui-control-check: FAIL | " + probe.Element + " | ATTRIBUTE VALUE on '"
                        + probe.Container + "' - the enum attribute reads '" + actual
                        + "', the markup says '" + probe.ExpectedEnum + "' (the attribute was not applied)");
                }
            }

            if (ok)
            {
                Write(log, "ui-control-check: values | " + probe.Element + " | on '" + probe.Container
                    + "' read back the markup's own attributes:" + read + " (the legacy UxmlTraits "
                    + "attributes ARE applied in this player)");
            }

            return ok;
        }

        /// <summary>Formats a colour compactly enough to read in a log line.</summary>
        /// <param name="color">The colour.</param>
        /// <returns>`r,g,b` with the raw 8-bit values.</returns>
        private static string Describe(Color color)
        {
            return "(" + Mathf.RoundToInt(color.r * 255f) + "," + Mathf.RoundToInt(color.g * 255f) + ","
                + Mathf.RoundToInt(color.b * 255f) + ")";
        }

        /// <summary>The first element in a subtree that is an instance of the given type.</summary>
        /// <param name="root">The subtree to walk.</param>
        /// <param name="type">The type to find.</param>
        /// <returns>The element, or <c>null</c>.</returns>
        /// <remarks>
        /// A hand-rolled walk rather than a UQuery overload, so the instrument is never the thing
        /// that is wrong - the same reason P2's diag plugin did it by hand.
        /// </remarks>
        private static VisualElement FindByType(VisualElement root, Type type)
        {
            if (root == null)
            {
                return null;
            }

            if (type.IsInstanceOfType(root))
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                VisualElement hit = FindByType(root[i], type);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
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
