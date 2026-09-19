// CommNextRedux - the legacy CommNext custom UI control, vendored and re-authored.
// Origin: mods-outdated/CommNext/src/CommNext.Unity/CommNext.Unity/Assets/Runtime/Controls/SortDirectionButton.cs (CommNext v0.7.0, MIT).
// Namespace and type name are UNCHANGED on purpose: the legacy markup names the type as
// an XML element, and the runtime resolves an element type by (namespace, type name) from
// the loaded assemblies - not by assembly. The port compiles these into CommNextRedux.dll
// (the only assembly the player loads for this mod).
//
// Used by Assets/CommNextRedux/UI/VesselReportWindow.uxml as
//   <CommNext.Unity.Runtime.Controls.SortDirectionButton>.

using System;
using UnityEngine.UIElements;

namespace CommNext.Unity.Runtime.Controls
{
    public class SortDirectionButton : Button
    {
        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlEnumAttributeDescription<SortDirection> _direction = new()
            {
                name = "direction"
            };

            // Base Init() no longer applies "name" in this Unity version, so it's re-applied here -
            // the donor mod's K2UI controls carry this field for the same reason. Without it the
            // markup's name="sort-direction-button" is silently dropped and the lookup returns null.
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
                ((SortDirectionButton)ve).direction = _direction.GetValueFromBag(bag, cc);
            }
        }

        // Define a factory class to expose this control to UXML.
        public new class UxmlFactory : UxmlFactory<SortDirectionButton, UxmlTraits> { }

        private const string USSClassName = "button-sort-direction";

        private SortDirection _direction;

        public SortDirection direction
        {
            get => _direction;
            set
            {
                _direction = value;
                if (_direction == SortDirection.Ascending)
                {
                    RemoveFromClassList("button-sort-direction--descending");
                    AddToClassList("button-sort-direction--ascending");
                }
                else
                {
                    RemoveFromClassList("button-sort-direction--ascending");
                    AddToClassList("button-sort-direction--descending");
                }
            }
        }

        // ReSharper disable once InconsistentNaming
        public event Action<SortDirection> directionChanged;

        public SortDirectionButton()
        {
            AddToClassList(USSClassName);
            clicked += OnClicked;
        }

        private void OnClicked()
        {
            direction = direction == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending;
            directionChanged?.Invoke(direction);
        }
    }
}