using System;
using Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class ArmyUnitPanel
    {
        public VisualElement panel { get; private set; }
        private Button _button;
        private Label _sizeLabel;
        private bool _interactable;

        public UnityEvent onClick = new();

        private VerticalBar _moraleBar;
        private VerticalBar _supplyBar;
        private VisualElement _swordIcon;

        public enum UnitAlignment
        {
            Ours,
            Ally,
            Neutral,
            Enemy
        }

        public ArmyUnitPanel(VisualElement panel)
        {
            this.panel = panel;
            panel.style.position = Position.Absolute;
            panel.style.width = 100;
            panel.style.height = 25;

            _sizeLabel = panel.Q<Label>("SizeLabel");
            _swordIcon = panel.Q("SwordIcon");

            _button = panel.Q<Button>();
            _button.RegisterCallback<ClickEvent>(_ =>
            {
                if (_interactable)
                {
                    onClick.Invoke();
                }
            });

            _moraleBar = new(panel.Q("MoraleBar"));
            _supplyBar = new(panel.Q("SupplyBar"));
        }

        public void SetPosition(float left, float bottom)
        {
            panel.style.left = left;
            panel.style.bottom = bottom;
        }

        public void SetMorale(int morale)
        {
            _moraleBar.SetValue(morale);
        }

        public void SetSupply(int supply)
        {
            _supplyBar.SetValue(supply);
        }

        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            panel.style.opacity = interactable ? 1f : 0.5f;
        }

        public void SetUnitSize(int size)
        {
            _sizeLabel.text = size.FormatWithSuffix();
        }

        public void SetInBattle(bool isInBattle)
        {
            _swordIcon.style.display = isInBattle ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RemoveAllClasses()
        {
            _button.RemoveFromClassList("unit-neutral");
            _button.RemoveFromClassList("unit-enemy");
            _button.RemoveFromClassList("unit-ally");
        }

        public void SetUnitAlignment(UnitAlignment alignment)
        {
            RemoveAllClasses();

            if (alignment == UnitAlignment.Ally)
            {
                _button.AddToClassList("unit-ally");
            }
            else if (alignment == UnitAlignment.Neutral)
            {
                _button.AddToClassList("unit-neutral");
            }
            else if (alignment == UnitAlignment.Enemy)
            {
                _button.AddToClassList("unit-enemy");
            }
        }
    }
}