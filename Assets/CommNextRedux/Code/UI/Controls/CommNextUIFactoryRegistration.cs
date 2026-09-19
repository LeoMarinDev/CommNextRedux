// CommNextRedux - manual registration of the port's five custom controls with Unity's internal
// VisualElementFactoryRegistry.
//
// WHY THIS FILE EXISTS (F15, measured in the player by P2)
//   The five controls below are declared in UXML as
//   `<CommNext.Unity.Runtime.Controls.BandIcon ...>` and the player refused them with its own
//   message:
//
//     EXC: Element 'CommNext.Unity.Runtime.Controls.BandIcon' is missing a UxmlElementAttribute
//          and has no registered factory method. Please ensure that you have the correct namespace
//          imported.
//
//   The controls are not broken: each one still carries the legacy's nested
//   `UxmlFactory<TElement, TTraits>` (monodis --typedef on the deployed DLL shows 5x UxmlTraits +
//   5x UxmlFactory, and zero UxmlSerializedData). What is missing is the REGISTRATION. Unity's
//   own scan (`VisualElementFactoryRegistry.RegisterUserFactories`) only walks the assemblies that
//   `GetAllUserAssemblies()` considers user assemblies, and an assembly loaded by the Redux mod
//   loader does not qualify - so the five factories exist and are never found by name.
//
//   The port cannot use the modern route either: the DLL is compiled by plain Roslyn `csc`
//   (`Tools/build.sh`), which runs no Unity source generators, so no
//   `[UxmlElement]`/`UxmlSerializedData` pair is emitted. Registering the legacy factories by hand
//   is the only route that works for a csc-built assembly, and it is the one the in-game-validated
//   sibling mod uses:
//
//     mods/K2D2Redux/Assets/K2D2/Code/KTools/K2UIFactoryRegistration.cs
//     [K2D2] K2UIFactoryRegistration: manually registered 18/18 K2UI custom control factories
//            with VisualElementFactoryRegistry.
//
// THE REGISTRY'S REAL SHAPE (measured against the installed runtime, not assumed)
//   * Type: `UnityEngine.UIElements.VisualElementFactoryRegistry`, in
//     `UnityEngine.UIElementsModule.dll` (monodis --typedef, one hit, that assembly).
//   * State: `private static Dictionary<string, List<IUxmlFactory>> s_Factories` (flist 5638) plus
//     `s_MovedTypesFactories` (flist 5639) - so the key is a **string**, not a Type.
//   * Key: `IBaseUxmlFactory.uxmlQualifiedName`, which the runtime's own reader confirms is a full
//     type name - `VisualElementFactoryRegistry.TryGetValue(string fullTypeName, out ...)` is the
//     lookup the UXML importer uses (mlist 12923). For a legacy `UxmlFactory<T>` that is
//     `typeof(T).FullName`, i.e. exactly the string in the markup.
//   * Registration: `RegisterFactory` is **not public**. There are two overloads
//     (`IBaseUxmlObjectFactory`, mlist 12818; `IUxmlFactory`, mlist 12922), which is why this file
//     does NOT use the donor's single `GetMethod("RegisterFactory", flags)` call: with two
//     overloads that call is one AmbiguousMatchException away from taking the whole UI down, and
//     the wrong overload would register into the object-factory table and silently not help. The
//     parameter type is matched explicitly instead.
//   * Duplicate registration of the same *factory type* is refused by the runtime with
//     "A factory for the type ... was already registered" (IL_0036-IL_0046 of the object overload,
//     same shape on the element one), hence the idempotence flag below.
//
// THE ASSERTION THAT MAKES THIS FALSIFIABLE (two levels, both in the launch log)
//   Registering is not proof that the registry now answers the player's question, so two
//   independent checks run after it:
//
//     1. `Verify` - in this file - asks the registry the *same question the UXML importer asks*.
//        `VisualElementFactoryRegistry.TryGetValue(string fullTypeName, out List<IUxmlFactory>)`
//        is resolved on this pin (mlist 12923 of `UnityEngine.UIElementsModule.dll`, the overload
//        the importer calls) and the factory it hands back is asked for the type it will build
//        (`IBaseUxmlFactory.get_uxmlType`, mlist 12724). So a pass here means: a lookup by the
//        markup's own element name finds a factory, and that factory builds the expected type.
//     2. `UI/Utils/UIControlAssertion` instantiates the three bundle pages that really use these
//        elements and asserts the controls by type AND by name - P2's own probe, line for line,
//        which failed here in the player.
//
//   Neither is a proxy for the other: (1) is cheap and covers all five controls, (2) is the exact
//   code path the player failed on and covers the three the markup names.

using System;
using System.Collections;
using System.Reflection;
using CommNext.Unity.Runtime.Controls;
using UnityEngine.UIElements;

namespace CommNextRedux.UI.Controls
{
    /// <summary>
    /// Registers the port's five custom controls' legacy <c>UxmlFactory</c> instances with Unity's
    /// internal <c>VisualElementFactoryRegistry</c>, by name.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Initialize"/> once, before any UXML that names one of these controls is
    /// instantiated. In this port that is the first thing <c>CommNextUIManager.Initialize</c> does,
    /// which is before the bundle is opened and therefore before any template is cloned.
    /// </remarks>
    internal static class CommNextUIFactoryRegistration
    {
        /// <summary>The full type names the UXML markup uses, in the order they are registered.</summary>
        /// <remarks>
        /// Spelled out rather than reflected over, because the UXML is the contract: each string here
        /// must match a `&lt;CommNext.Unity.Runtime.Controls.X ...&gt;` tag in `Assets/CommNextRedux/UI`
        /// or the registry entry is dead weight. `TabSelector` is deliberate - the file is
        /// `TabsSelector.cs` and the type inside it is singular.
        /// </remarks>
        private static readonly string[] ControlNames =
        {
            "CommNext.Unity.Runtime.Controls.BandIcon",
            "CommNext.Unity.Runtime.Controls.SignalStrengthIcon",
            "CommNext.Unity.Runtime.Controls.SortDirectionButton",
            "CommNext.Unity.Runtime.Controls.TabSelector",
            "CommNext.Unity.Runtime.Controls.TableSeparatorTitle"
        };

        private static bool _registered;

        /// <summary>How many factories were registered, or <c>0</c> before <see cref="Initialize"/>.</summary>
        public static int RegisteredCount { get; private set; }

        /// <summary>How many element names <see cref="Verify"/> resolved, or <c>-1</c> before it ran.</summary>
        public static int VerifiedCount { get; private set; } = -1;

        /// <summary>
        /// Registers every factory, once. Safe to call again; the second call returns immediately.
        /// </summary>
        /// <param name="log">Receives the one-line count on success.</param>
        /// <param name="error">Receives a line per failure - a missing registry, a missing method, or
        /// one factory that threw.</param>
        /// <remarks>
        /// <b>Failures are logged at Error, not swallowed.</b> If this does not run, every UXML that
        /// names a custom control fails to clone in the player and the window is blank - which looks
        /// exactly like "the mod is dead" and is the failure mode this file exists to remove. A
        /// registry that cannot be reached is a fact the user's log must carry.
        /// </remarks>
        public static void Initialize(Action<string> log, Action<string> error)
        {
            if (_registered)
            {
                return;
            }

            _registered = true;

            Type registryType = typeof(VisualElement).Assembly.GetType(
                "UnityEngine.UIElements.VisualElementFactoryRegistry");
            if (registryType == null)
            {
                Write(error, "ui-factories: UnityEngine.UIElements.VisualElementFactoryRegistry was not "
                    + "found in " + typeof(VisualElement).Assembly.GetName().Name + " - this Unity "
                    + "generation's internal UI Toolkit types have moved. The five custom controls "
                    + "(BandIcon, SignalStrengthIcon, SortDirectionButton, TabSelector, "
                    + "TableSeparatorTitle) will fail to load from UXML in the player");
                return;
            }

            MethodInfo registerMethod = ResolveRegisterFactory(registryType);
            if (registerMethod == null)
            {
                Write(error, "ui-factories: no VisualElementFactoryRegistry.RegisterFactory(IUxmlFactory) "
                    + "was found - this Unity generation's internal UI Toolkit API has changed. The five "
                    + "custom controls will fail to load from UXML in the player");
                return;
            }

            IUxmlFactory[] factories =
            {
                new BandIcon.UxmlFactory(),
                new SignalStrengthIcon.UxmlFactory(),
                new SortDirectionButton.UxmlFactory(),
                new TabSelector.UxmlFactory(),
                new TableSeparatorTitle.UxmlFactory()
            };

            int registered = 0;
            for (int i = 0; i < factories.Length; i++)
            {
                try
                {
                    registerMethod.Invoke(null, new object[] { factories[i] });
                    registered++;
                }
                catch (Exception exception)
                {
                    // One factory failing is a partial failure with no other trace: the other four
                    // register and only this control's window/page comes out empty.
                    Write(error, "ui-factories: failed to register " + ControlNames[i] + " ("
                        + exception.GetType().Name + ": " + exception.Message + ")");
                }
            }

            RegisteredCount = registered;

            // Info, not Debug: ReduxLib's filter drops Debug outright, and this line is the proof
            // the fix ran. The names are included so a single grep answers "which controls".
            Write(log, "ui-factories: manually registered " + registered + "/" + factories.Length
                + " CommNext custom control factories with VisualElementFactoryRegistry ("
                + string.Join(", ", ShortNames()) + ")");
        }

        /// <summary>
        /// Finds the element overload of <c>RegisterFactory</c> by its parameter type.
        /// </summary>
        /// <param name="registryType">The internal registry type.</param>
        /// <returns>The method, or <c>null</c> if this Unity build does not have it.</returns>
        /// <remarks>
        /// The two overloads differ only in parameter type, so the parameter is what is matched.
        /// Picking the <c>IBaseUxmlObjectFactory</c> overload by accident would register into the
        /// object-factory table, which the importer never consults for an element tag - a silent
        /// no-op dressed as success.
        /// </remarks>
        private static MethodInfo ResolveRegisterFactory(Type registryType)
        {
            BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
            MethodInfo[] candidates = registryType.GetMethods(flags);
            for (int i = 0; i < candidates.Length; i++)
            {
                MethodInfo candidate = candidates[i];
                if (candidate.Name != "RegisterFactory")
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(IUxmlFactory))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>The five type names without their namespace, for the log line.</summary>
        private static string[] ShortNames()
        {
            string[] names = new string[ControlNames.Length];
            for (int i = 0; i < ControlNames.Length; i++)
            {
                int dot = ControlNames[i].LastIndexOf('.');
                names[i] = dot < 0 ? ControlNames[i] : ControlNames[i].Substring(dot + 1);
            }

            return names;
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

        /// <summary>
        /// Asks the registry the importer's own question for every control name, and reports.
        /// </summary>
        /// <param name="log">Receives one line per resolved name, then the summary.</param>
        /// <param name="warn">Receives one line per name that did not resolve.</param>
        /// <returns>How many of the five names resolved.</returns>
        /// <remarks>
        /// <para>
        /// <b>This is the falsifiable half of the file.</b> Running <see cref="Initialize"/> only
        /// proves an <c>Invoke</c> returned; it cannot prove the registry now answers by name,
        /// because a registration into the wrong table, or under a key that differs from the
        /// markup's element name by one character, succeeds identically. So this reads the
        /// registry's live key set back - <c>s_Factories</c>, the same dictionary
        /// <c>TryGetValue</c> indexes - and then reads <c>uxmlType</c> off the factory it finds.
        /// The pair is what the player needs: a lookup by that exact string succeeds, and it
        /// yields a factory that builds that exact type.
        /// </para>
        /// <para>
        /// A failure is a <b>warning</b>, not an error: the assertion is a diagnostic, and the
        /// page-level probe in <c>UIControlAssertion</c> is what turns a missing registration into
        /// a user-visible miss. Both line sets share the <c>ui-</c> prefix so one grep finds them.
        /// </para>
        /// </remarks>
        public static int Verify(Action<string> log, Action<string> warn)
        {
            int resolved = 0;
            for (int i = 0; i < ControlNames.Length; i++)
            {
                string name = ControlNames[i];
                int count = CountFactories(name);
                if (count <= 0)
                {
                    Write(warn, "ui-factory-check: MISSING | " + name + " | the registry has "
                        + (count < 0 ? "no readable factory table" : "no entry for this name")
                        + " - a UXML that names this element fails in the player with "
                        + "\"has no registered factory method\"");
                    continue;
                }

                Type produced = FirstFactoryType(name);
                if (produced == null)
                {
                    Write(warn, "ui-factory-check: UNREADABLE | " + name + " | an entry exists but its "
                        + "factory could not be read back");
                    continue;
                }

                if (produced.FullName != name)
                {
                    Write(warn, "ui-factory-check: MISMATCH | " + name + " | the registered factory "
                        + "builds '" + produced.FullName + "' - the markup's element name and the "
                        + "factory disagree");
                    continue;
                }

                resolved++;
                Write(log, "ui-factory-check: PASS | " + name + " | registered (factories=" + count
                    + "), the factory builds this exact type");
            }

            VerifiedCount = resolved;
            Write(resolved == ControlNames.Length ? log : warn,
                "ui-factory-check: " + resolved + "/" + ControlNames.Length + " CommNext control element "
                + "names resolve in VisualElementFactoryRegistry - "
                + (resolved == ControlNames.Length
                    ? "the importer's own lookup by name now succeeds for all five"
                    : "at least one control cannot be loaded from UXML in the player"));
            return resolved;
        }

        /// <summary>The type the registered factory builds, for one element name.</summary>
        /// <param name="elementName">The full type name the UXML uses.</param>
        /// <returns>The factory's <c>uxmlType</c>, or <c>null</c> when it cannot be read.</returns>
        /// <remarks>
        /// `IBaseUxmlFactory.uxmlType` is the member the importer instantiates from
        /// (`UnityEngine.UIElementsModule.dll` mlist 12724); it is read through the interface so
        /// this works for both the legacy and the source-generated factory shapes.
        /// </remarks>
        private static Type FirstFactoryType(string elementName)
        {
            try
            {
                Type registryType = typeof(VisualElement).Assembly.GetType(
                    "UnityEngine.UIElements.VisualElementFactoryRegistry");
                if (registryType == null)
                {
                    return null;
                }

                FieldInfo field = registryType.GetField("s_Factories",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                IDictionary dictionary = field == null ? null : field.GetValue(null) as IDictionary;
                if (dictionary == null)
                {
                    return null;
                }

                IList list = dictionary[elementName] as IList;
                if (list == null || list.Count == 0)
                {
                    return null;
                }

                IBaseUxmlFactory factory = list[0] as IBaseUxmlFactory;
                return factory == null ? null : factory.uxmlType;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// How many factories are registered under a given element name, for <see cref="Verify"/>.
        /// </summary>
        /// <param name="elementName">The full type name the UXML uses.</param>
        /// <returns>The factory count, or <c>-1</c> when the registry cannot be read.</returns>
        public static int CountFactories(string elementName)
        {
            try
            {
                IDictionary dictionary = RegistryTable();
                if (dictionary == null)
                {
                    return -1;
                }

                object entry = dictionary[elementName];
                ICollection collection = entry as ICollection;
                return collection == null ? 0 : collection.Count;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>
        /// The registry's live name-to-factory table, through the private field the importer uses.
        /// </summary>
        /// <returns>The table, or <c>null</c> when it cannot be read.</returns>
        /// <remarks>
        /// `VisualElementFactoryRegistry.s_Factories` is `Dictionary&lt;string, List&lt;IUxmlFactory&gt;&gt;`
        /// on this pin (`UnityEngine.UIElementsModule.dll` mlist 12921 is its `get_factories`
        /// accessor; the field itself is private). It is read as non-generic `IDictionary` so this
        /// needs no compile-time reference to the registry's internal shapes.
        /// </remarks>
        private static IDictionary RegistryTable()
        {
            Type registryType = typeof(VisualElement).Assembly.GetType(
                "UnityEngine.UIElements.VisualElementFactoryRegistry");
            if (registryType == null)
            {
                return null;
            }

            FieldInfo field = registryType.GetField("s_Factories",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            return field == null ? null : field.GetValue(null) as IDictionary;
        }
    }
}
