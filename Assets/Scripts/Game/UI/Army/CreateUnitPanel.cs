using System;
using System.Collections.Generic;
using Game.Services;
using Game.Services.Saves;
using Game.Services.Commands;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class CreateUnitPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();

        private readonly VisualElement _panel;
        private readonly TextField _nameField;
        private readonly IntegerField _unitSizeField;
        private readonly Label _costLabel;
        private readonly Label _locationName;
        private readonly Button _createButton;
        private readonly Button _selectLocationButton;
        private readonly Button _deselectLocationButton;

        private readonly ReactiveProperty<int> _size = new(0);
        private readonly ReactiveProperty<int> _deployLocationId = new(-1);

        private const string tooExpensiveClassName = "cost-label-too-expensive";

        public CreateUnitPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _nameField = _panel.Q<TextField>("UnitName");
            _unitSizeField = _panel.Q<IntegerField>("UnitSize");
            _costLabel = _panel.Q<Label>("TotalCostLabel");
            _createButton = _panel.Q<Button>("CreateButton");
            _locationName = panel.Q<Label>("LocationName");
            _selectLocationButton = panel.Q<Button>("SelectLocationButton");
            _deselectLocationButton = panel.Q<Button>("DeselectLocationButton");

            _createButton.RegisterCallback<ClickEvent>(_ => OnCreate());
            _deselectLocationButton.RegisterCallback<ClickEvent>(_ => SetLocationId(-1));

            _unitSizeField.RegisterValueChangedCallback(evt =>
            {
                var country = GameService.Main.PlayerCountry;
                var maxSize = CalculateMaxRecruitSize(country);
                var newValue = Math.Clamp(evt.newValue, 1, maxSize); // Min size always 1?

                if (newValue != evt.newValue)
                {
                    _unitSizeField.value = newValue; // This will trigger callback again but with same value, should be safe
                }

                _size.Value = newValue;
            });

            _size.CombineLatest(_deployLocationId, (x, y) => (x, y)).Subscribe(tuple =>
            {
                var (size, deployLocationId) = tuple;

                var totalCost = Mathf.CeilToInt(size / 100.0f);
                _costLabel.text = $"{totalCost:N0}";

                var enableButton = true;

                if (size == 0)
                {
                    enableButton = false;
                }

                if (totalCost > GameService.Main.PlayerCountry.Treasury)
                {
                    if (!_costLabel.ClassListContains(tooExpensiveClassName))
                    {
                        _costLabel.AddToClassList(tooExpensiveClassName);
                    }

                    enableButton = false;
                }
                else
                {
                    if (_costLabel.ClassListContains(tooExpensiveClassName))
                    {
                        _costLabel.RemoveFromClassList(tooExpensiveClassName);
                    }
                }

                var province = GameService.Main.State.CurrentValue.Commandery.GetValueOrDefault(deployLocationId);
                if (province == null)
                {
                    _locationName.text = "None";
                    _locationName.style.color = Color.gray;
                    enableButton = false;
                }
                else
                {
                    _locationName.text = province.Name;
                    _locationName.style.color = Color.white;
                }

                _createButton.SetEnabled(enableButton);
            });
        }

        public void SetLocationId(int locationId)
        {
            _deployLocationId.Value = locationId;
        }

        private void OnCreate()
        {
            var totalCost = Mathf.CeilToInt(_size.CurrentValue / 100.0f);
            var treasury = GameService.Main.PlayerCountry.Treasury;

            if (totalCost > treasury) return;

            ArmyCommands.CreateArmy(
                GameService.Main.PlayerCountry.Id,
                _deployLocationId.CurrentValue,
                _nameField.value,
                _size.CurrentValue
            );
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;
            // Refresh validation when opening?
            if (isEnabled)
            {
                var country = GameService.Main.PlayerCountry;
                var maxSize = CalculateMaxRecruitSize(country);
                _unitSizeField.value = Math.Clamp(_unitSizeField.value, 1, maxSize);

                // Set default location from selected province if player-owned
                if (ProvinceUI.Main != null && ProvinceUI.Main.IsProvinceSelected)
                {
                    var selectedId = ProvinceUI.Main.CurrentProvinceId;
                    var province = GameService.Main.State.CurrentValue.Commandery.GetValueOrDefault(selectedId);
                    if (province != null && province.CountryId == country.Id)
                    {
                        SetLocationId(selectedId);
                    }
                }
            }
        }

        private int CalculateMaxRecruitSize(CountryData country)
        {
            if (country == null) return 0;
            int maximumIncrease = Math.Max((int)((double)country.Manpower * (100 + country.Prestige) / 1000), 10000);
            return Math.Min(maximumIncrease, country.Manpower);
        }
    }
}