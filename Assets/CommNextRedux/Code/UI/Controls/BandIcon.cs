// CommNextRedux - the legacy CommNext custom UI control, vendored and re-authored.
// Origin: mods-outdated/CommNext/src/CommNext.Unity/CommNext.Unity/Assets/Runtime/Controls/BandIcon.cs (CommNext v0.7.0, MIT).
// Namespace and type name are UNCHANGED on purpose: the legacy markup names the type as
// an XML element, and the runtime resolves an element type by (namespace, type name) from
// the loaded assemblies - not by assembly. The port compiles these into CommNextRedux.dll
// (the only assembly the player loads for this mod).
//
// Used by Assets/CommNextRedux/UI/Components/BandRow.uxml and
//   Components/NetworkConnectionView.uxml as <CommNext.Unity.Runtime.Controls.BandIcon>.

using UnityEngine;
using UnityEngine.UIElements;

namespace CommNext.Unity.Runtime.Controls
{
    public class BandIcon : VisualElement
    {
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlColorAttributeDescription _color = new()
            {
                name = "color"
            };

            private readonly UxmlStringAttributeDescription _code = new()
            {
                name = "code"
            };

            // Base Init() no longer applies "name" in this Unity version, so it's re-applied here -
            // the donor mod's K2UI controls carry this field for the same reason. Without it the
            // markup's name="band-icon" is silently dropped and root.Q("band-icon") returns null.
            // Evidence and the controlled probe: Deploy/obj/bundle-verdict.md.
            private readonly UxmlStringAttributeDescription _name = new()
            {
                name = "name",
                defaultValue = ""
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                ve.name = _name.GetValueFromBag(bag, cc);
                ((BandIcon)ve).color = _color.GetValueFromBag(bag, cc);
                ((BandIcon)ve).code = _code.GetValueFromBag(bag, cc);
            }
        }

        // Define a factory class to expose this control to UXML.
        public new class UxmlFactory : UxmlFactory<BandIcon, UxmlTraits> { }

        private const string USSClassName = "band__icon";
        private const string USSCodeClassName = "band__code";

        public Color color
        {
            get => _codeLabel.style.color.value;
            set => _codeLabel.style.color = value;
        }

        public string code
        {
            get => _codeLabel.text;
            set => _codeLabel.text = value;
        }

        public void SetBand(string bandCode, Color bandColor)
        {
            code = bandCode;
            color = bandColor;
        }

        private readonly Label _codeLabel;

        public BandIcon()
        {
            AddToClassList(USSClassName);
            _codeLabel = new Label();
            _codeLabel.AddToClassList(USSCodeClassName);
            Add(_codeLabel);
        }
    }
}