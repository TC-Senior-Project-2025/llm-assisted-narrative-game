using System.Collections.Generic;
using Extensions;
using Game.Services;
using Game.Services.Sounds;
using Game.UI.Commandery;
using Game.World.Map;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class ProvinceUI : MonoBehaviour
    {
        public static ProvinceUI Main { get; private set; }
        private UIDocument _uiDocument;
        private VisualElement _provincePanel;
        private Label _provinceNameLabel;
        private Label _provinceOwnerLabel;
        private Label _populationLabel;
        private Label _wealthLabel;
        private Label _unrestLabel;
        private Label _GarrisonLabel;
        private Label _DefensivenessLabel;
        private Label _governorLabel;
        private Button _changeGovernorButton;
        private Button _refillGarrisonButton;
        private Button _depleteGarrisonButton;

        private ChangeGovernorPanel _changeGovernorPanel;

        private ReactiveProperty<int> _currentProvinceId = new(-1);

        public int CurrentProvinceId => _currentProvinceId.CurrentValue;
        public bool IsProvinceSelected => _currentProvinceId.CurrentValue != -1;

        private void Start()
        {
            Main = this;
            _uiDocument = GetComponent<UIDocument>();
            _provincePanel = _uiDocument.rootVisualElement.Q<VisualElement>("ProvinceDetailsPanel");
            _provinceNameLabel = _provincePanel.Q<Label>("ProvinceNameLabel");
            _provinceOwnerLabel = _provincePanel.Q<Label>("ProvinceOwnerLabel");
            _populationLabel = _provincePanel.Q<Label>("PopulationLabel");
            _wealthLabel = _provincePanel.Q<Label>("WealthLabel");
            _unrestLabel = _provincePanel.Q<Label>("UnrestLabel");
            _GarrisonLabel = _provincePanel.Q<Label>("GarrisonLabel");
            _DefensivenessLabel = _provincePanel.Q<Label>("DefensivenessLabel");
            _governorLabel = _provincePanel.Q<Label>("GovernorLabel");

            _provincePanel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(OnCloseButtonClicked);
            _changeGovernorButton = _provincePanel.Q<Button>("ChangeGovernorButton");
            _refillGarrisonButton = _provincePanel.Q<Button>("RefillGarrisonButton");
            _depleteGarrisonButton = _provincePanel.Q<Button>("DepleteGarrisonButton");

            _changeGovernorPanel = new(_uiDocument.rootVisualElement.Q("ChangeGovernorPanel"));
            _changeGovernorPanel.SetEnabled(false);

            _refillGarrisonButton?.RegisterCallback<ClickEvent>(OnRefillGarrisonClicked);
            _depleteGarrisonButton?.RegisterCallback<ClickEvent>(OnDepleteGarrisonClicked);

            _changeGovernorButton.RegisterCallback<ClickEvent>(_ =>
            {
                _changeGovernorPanel.SetEnabled(true);
                _changeGovernorPanel.SetCommanderyId(_currentProvinceId.CurrentValue);
            });

            _changeGovernorPanel.onClose.AddListener(() =>
            {
                _changeGovernorPanel.SetEnabled(false);
                _changeGovernorPanel.SetCommanderyId(-1);
            });

            _changeGovernorPanel.onChangeGovernor.AddListener((commanderyId, _) =>
            {
                OnProvinceIdUpdated(commanderyId);
            });

            _currentProvinceId.Subscribe(OnProvinceIdUpdated);

            _provincePanel.visible = false;

            GameMap.Main.Picker.provinceDoubleClicked.AddListener(OnProvinceDoubleClicked);
        }

        private void OnCloseButtonClicked(ClickEvent evt)
        {
            SfxService.Main.Play(SfxService.Main.click);
            _provincePanel.visible = false;
            _changeGovernorPanel.SetEnabled(false);
        }

        private void OnProvinceDoubleClicked(Color32 color)
        {
            _changeGovernorPanel.SetEnabled(false);

            if (color.SameAs(Color.clear))
            {
                _provincePanel.visible = false;
                _currentProvinceId.Value = -1;
                return;
            }

            var provinceId = GameMap.Main.Provider.GetProvinceId(color);
            _currentProvinceId.Value = provinceId;
        }

        private void OnProvinceIdUpdated(int provinceId)
        {
            var ownerId = GameMap.Main.Provider.GetProvinceCountryId(provinceId);

            var gameState = GameService.Main.State.CurrentValue;
            var province = gameState.Commandery.GetValueOrDefault(provinceId);

            _provinceNameLabel.text = province.Name;
            _provinceOwnerLabel.text = gameState.Country.GetValueOrDefault(ownerId).Name;

            var population = province.Population;
            _populationLabel.text = $"{population:N0}";

            var wealth = province.Wealth;
            _wealthLabel.text = $"{wealth:N0}";
            var playerCountryId = gameState.Game.PlayerCountryId;
            var isPlayerOwned = ownerId == playerCountryId;

            bool isBorderingPlayer = false;
            if (province.Neighbors != null)
            {
                foreach (var neighborId in province.Neighbors)
                {
                    if (gameState.Commandery.TryGetValue(neighborId, out var neighbor))
                    {
                        if (neighbor.CountryId == playerCountryId)
                        {
                            isBorderingPlayer = true;
                            break;
                        }
                    }
                }
            }

            bool showMilitaryInfo = isPlayerOwned || isBorderingPlayer;

            var unrest = province.Unrest;
            _unrestLabel.text = showMilitaryInfo ? $"{unrest:N0}" : "N/A";
            var Garrison = province.Garrisons;
            _GarrisonLabel.text = showMilitaryInfo ? $"{Garrison:N0}" : "N/A";
            var Defensiveness = province.Defensiveness;
            _DefensivenessLabel.text = showMilitaryInfo ? $"{Defensiveness:N0}" : "N/A";

            var commanderId = province.CommanderId;
            string governorName = "None";
            if (commanderId.HasValue)
            {
                var person = gameState.Person.Find(p => p.Id == commanderId.Value);
                if (person != null)
                {
                    governorName = person.Name;
                }
            }
            _governorLabel.text = governorName;

            var displayStyle = isPlayerOwned ? DisplayStyle.Flex : DisplayStyle.None;

            if (_changeGovernorButton != null) _changeGovernorButton.style.display = displayStyle;
            if (_refillGarrisonButton != null) _refillGarrisonButton.style.display = displayStyle;
            if (_depleteGarrisonButton != null) _depleteGarrisonButton.style.display = displayStyle;

            _provincePanel.visible = true;
        }

        private void OnRefillGarrisonClicked(ClickEvent evt)
        {
            SfxService.Main.Play(SfxService.Main.click);
            var save = GameService.Main.State.CurrentValue;

            if (_currentProvinceId.CurrentValue == -1) return;

            if (!save.Commandery.TryGetValue(_currentProvinceId.CurrentValue, out var commandery)) return;
            if (!save.Country.TryGetValue(commandery.CountryId, out var country)) return;

            int current = commandery.Garrisons;
            int target = ((current / 1000) + 1) * 1000;
            int amount = target - current;

            if (amount == 0) amount = 1000;

            var result = GameService.Main.CommanderyAction.ExecuteIncreaseGarrison(save, country, commandery, amount);
            if (result == "Success")
            {
                _GarrisonLabel.text = $"{commandery.Garrisons:N0}";
                GameService.Main.State.OnNext(save);
            }
            else
            {
                Debug.LogWarning($"Increase Garrison Failed: {result}");
            }
        }

        private void OnDepleteGarrisonClicked(ClickEvent evt)
        {
            SfxService.Main.Play(SfxService.Main.click);
            var save = GameService.Main.State.CurrentValue;

            if (_currentProvinceId.CurrentValue == -1) return;

            if (!save.Commandery.TryGetValue(_currentProvinceId.CurrentValue, out var commandery)) return;
            if (!save.Country.TryGetValue(commandery.CountryId, out var country)) return;

            int current = commandery.Garrisons;
            int target = (current % 1000 == 0) ? current - 1000 : (current / 1000) * 1000;
            if (target < 0) target = 0;

            int amount = current - target;
            if (amount <= 0) return;

            var result = GameService.Main.CommanderyAction.ExecuteDecreaseGarrison(save, country, commandery, amount);
            if (result == "Success")
            {
                _GarrisonLabel.text = $"{commandery.Garrisons:N0}";
                GameService.Main.State.OnNext(save);
            }
            else
            {
                Debug.LogWarning($"Decrease Garrison Failed: {result}");
            }
        }
    }
}
