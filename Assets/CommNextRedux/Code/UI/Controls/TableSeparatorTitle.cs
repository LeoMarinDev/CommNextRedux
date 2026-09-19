// CommNextRedux - the legacy CommNext custom UI control, vendored and re-authored.
// Origin: mods-outdated/CommNext/src/CommNext.Unity/CommNext.Unity/Assets/Runtime/Controls/TableSeparatorTitle.cs (CommNext v0.7.0, MIT).
// Namespace and type name are UNCHANGED on purpose: the legacy markup names the type as
// an XML element, and the runtime resolves an element type by (namespace, type name) from
// the loaded assemblies - not by assembly. The port compiles these into CommNextRedux.dll
// (the only assembly the player loads for this mod).
//
// Not referenced by any legacy markup. Vendored so the control family is
//   complete; P8's vessel-report controller is the likely first user.

using UnityEngine.UIElements;

namespace CommNext.Unity.Runtime.Controls
{
    public class TableSeparatorTitle : VisualElement
    {
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlStringAttributeDescription _text = new()
            {
                name = "text"
            };

            // Base Init() no longer applies "name" in this Unity version, so it's re-applied here -
            // the donor mod's K2UI controls carry this field for the same reason. Without it a
            // markup name= on this control is silently dropped and the lookup returns null.
            // Evidence and the controlled probe: Deploy/obj/bundle-verdict.md.
            private readonly UxmlStringAttributeDescription _name = new()
            {
                name = "name",
                defaultValue = ""
            };

            // Use the Init method to assign the value of the progress UXML attribute to the C# progress property.
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ve.name = _name.GetValueFromBag(bag, cc);
                ((TableSeparatorTitle)ve).text = _text.GetValueFromBag(bag, cc);
                // (ve as RadialProgress).progress = m_ProgressAttribute.GetValueFromBag(bag, cc);
            }
        }

        // Define a factory class to expose this control to UXML.
        public new class UxmlFactory : UxmlFactory<TableSeparatorTitle, UxmlTraits> { }

        private const string USSClassName = "table-title";
        private const string USSContainerClassName = "table-title__container";

        private string _text;

        public string text
        {
            get => _text;
            set
            {
                _text = value;
                _labelElement.text = "<color=#595DD5>//</color> " + _text;
            }
        }

        private Label _labelElement;

        public TableSeparatorTitle()
        {
            AddToClassList(USSClassName);
            _labelElement = new Label();
            _labelElement.AddToClassList(USSContainerClassName);
            Add(_labelElement);
        }
    }
}