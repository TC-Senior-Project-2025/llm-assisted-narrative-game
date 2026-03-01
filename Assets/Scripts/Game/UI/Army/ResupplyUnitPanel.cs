using System;
using Extensions;
using Game.Services;
using Game.Services.Commands;
using Game.Services.Saves;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class ResupplyUnitPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();

        private readonly VisualElement _panel;
        private readonly Label _unitName;
        private readonly Label _supplyNeededLabel;
        private readonly Label _totalCostLabel;
        private readonly IntegerField _resupplyAmountField;
        private readonly Button _resupplyButton;

        private int _selectedUnitId = -1;
        private readonly ReactiveProperty<int> _neededSupply = new(0);
        private readonly ReactiveProperty<int> _amount = new(0);
        private readonly ReactiveProperty<int> _armySize = new(0);

        private const string tooExpensiveClassName = "cost-label-too-expensive";

        public ResupplyUnitPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _unitName = _panel.Q<Label>("SourceName");
            _resupplyAmountField = _panel.Q<IntegerField>("ResupplyAmount");
            _resupplyButton = _panel.Q<Button>("ResupplyButton");
            _supplyNeededLabel = _panel.Q<Label>("SupplyNeededLabel");
            _totalCostLabel = _panel.Q<Label>("TotalCostLabel");

            _panel.Q<Button>("DeselectSourceButton").RegisterCallback<ClickEvent>(_ => SetSelectedUnit(null));
            _resupplyButton.RegisterCallback<ClickEvent>(_ => OnResupply());

            _resupplyAmountField.RegisterValueChangedCallback(evt =>
            {
                var newValue = Math.Clamp(evt.newValue, 0, _neededSupply.CurrentValue);
                _amount.Value = newValue;
                _resupplyAmountField.value = newValue;
            });

            _armySize
                .CombineLatestWith(_amount)
                .CombineLatestWith(_neededSupply)
                .Subscribe(tuple =>
            {
                var (armySize, amount, neededSupply) = tuple;
                double costPerSupply = armySize * GameConstants.SupplyPrice;
                int totalCost = (int)Math.Ceiling(costPerSupply * amount);

                var enableButton = true;

                if (totalCost > GameService.Main.PlayerCountry.Treasury)
                {
                    if (!_totalCostLabel.ClassListContains(tooExpensiveClassName))
                    {
                        _totalCostLabel.AddToClassList(tooExpensiveClassName);
                    }

                    enableButton = false;
                }
                else
                {
                    if (_totalCostLabel.ClassListContains(tooExpensiveClassName))
                    {
                        _totalCostLabel.RemoveFromClassList(tooExpensiveClassName);
                    }
                }

                if (amount == 0)
                {
                    enableButton = false;
                }

                if (_selectedUnitId == -1)
                {
                    _totalCostLabel.text = "-";
                    _supplyNeededLabel.text = "-";
                    enableButton = false;
                }
                else
                {
                    _supplyNeededLabel.text = $"{neededSupply}%";
                    _totalCostLabel.text = $"{totalCost:N0}";
                }

                if (amount > neededSupply)
                {
                    enableButton = false;
                }

                _resupplyButton.SetEnabled(enableButton);
            });

            GameService.Main.currentPhase.Subscribe(phase =>
            {
                if (phase != GameService.GamePhase.PlayerAction)
                {
                    onClose.Invoke();
                }
            });
        }

        private void OnResupply()
        {
            if (_selectedUnitId == -1) return;

            var treasury = GameService.Main.PlayerCountry.Treasury;
            var costPerSupply = _armySize.CurrentValue / 100_000f;
            var totalCost = Mathf.CeilToInt(costPerSupply * _amount.CurrentValue);

            if (totalCost > treasury) return;

            ArmyCommands.ResupplyArmy(_selectedUnitId, _amount.CurrentValue);
            onClose.Invoke();
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        public void SetSelectedUnit(ArmyData unit)
        {
            if (unit == null)
            {
                _selectedUnitId = -1;
                _unitName.style.color = Color.gray;
                _unitName.text = "None";

                _armySize.Value = 0;
                _neededSupply.Value = 0;
            }
            else
            {
                _selectedUnitId = unit.Id;
                _unitName.style.color = Color.white;
                _unitName.text = unit.Name;

                _armySize.Value = unit.Size;
                _neededSupply.Value = Mathf.Max(0, 100 - unit.Supply);
                _resupplyAmountField.value = _neededSupply.Value;
                _amount.Value = _neededSupply.Value;
            }
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}

