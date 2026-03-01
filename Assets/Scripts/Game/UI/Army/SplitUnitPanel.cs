using Game.Services;
using Game.Services.Commands;
using Game.Services.Saves;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
using System;

namespace Game.UI.Army
{
    public class SplitUnitPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        public UnityEvent onSelect = new();

        public int GetSelectedUnit() => _selectedUnitId;

        private VisualElement _panel;
        private Label _unitName;
        private Button _splitButton;
        private IntegerField _newSizeField;
        private int _selectedUnitId = -1;
        private readonly ReactiveProperty<int> _maxSplitSize = new(1);

        public SplitUnitPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _newSizeField = _panel.Q<IntegerField>("NewUnitSizeField");
            _splitButton = _panel.Q<Button>("SplitButton");
            _splitButton.RegisterCallback<ClickEvent>(_ => OnSplit());

            _newSizeField.RegisterValueChangedCallback(evt =>
            {
                var newValue = Math.Clamp(evt.newValue, 1, _maxSplitSize.CurrentValue);
                if (newValue != evt.newValue)
                {
                    _newSizeField.value = newValue;
                }
            });

            _panel.Q<Button>("SelectSourceButton").RegisterCallback<ClickEvent>(_ => onSelect.Invoke());
            _panel.Q<Button>("DeselectSourceButton").RegisterCallback<ClickEvent>(_ => SetSelectedUnit(null));

            _unitName = _panel.Q<Label>("SourceName");

            GameService.Main.currentPhase.Subscribe(phase =>
            {
                if (phase != GameService.GamePhase.PlayerAction)
                {
                    onClose.Invoke();
                }
            });
        }

        private void OnSplit()
        {
            if (_selectedUnitId == -1) return;

            int newSize = _newSizeField.value;
            ArmyCommands.SplitArmy(_selectedUnitId, newSize);
            onClose.Invoke();
        }

        public void SetSelectedUnit(ArmyData unit)
        {
            if (unit == null)
            {
                _selectedUnitId = -1;
                _unitName.style.color = Color.gray;
                _unitName.text = "None";
            }
            else
            {
                _selectedUnitId = unit.Id;
                _unitName.style.color = Color.white;
                _unitName.text = unit.Name;
                _maxSplitSize.Value = Math.Max(1, unit.Size - 1);
                // Trigger clamp on current value if it's out of undefined bounds (or just reset to 1?)
                // Let's reset to a safe default if it's 0 or invalid, or clamp.
                _newSizeField.value = Math.Clamp(_newSizeField.value, 1, _maxSplitSize.Value);
            }
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnClose()
        {
            onClose.Invoke();
        }
    }
}