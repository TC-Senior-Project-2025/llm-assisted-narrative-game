using Game.UI.Interfaces;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class CountryPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();

        private readonly VisualElement _panel;
        private readonly Button _armyButton;
        private readonly Button _diplomacyButton;
        private readonly Button _battlesButton;
        private readonly Button _countryButton;
        private readonly Button _missionsButton;
        private readonly Button _saveButton;

        public UnityEvent onArmy = new();
        public UnityEvent onDiplomacy = new();
        public UnityEvent onBattles = new();
        public UnityEvent onCountryDetails = new();
        public UnityEvent onMissions = new();
        public UnityEvent onSave = new();

        public CountryPanel(VisualElement panel)
        {
            _panel = panel;
            _armyButton = _panel.Q<Button>("ArmyButton");
            _diplomacyButton = _panel.Q<Button>("DiplomacyButton");
            _battlesButton = _panel.Q<Button>("BattlesButton");
            _countryButton = _panel.Q<Button>("CountryButton");
            _missionsButton = _panel.Q<Button>("MissionsButton");
            _saveButton = _panel.Q<Button>("SaveButton");

            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => onClose.Invoke());

            RegisterButton(_armyButton, onArmy);
            RegisterButton(_diplomacyButton, onDiplomacy);
            RegisterButton(_battlesButton, onBattles);
            RegisterButton(_countryButton, onCountryDetails);
            RegisterButton(_missionsButton, onMissions);
            RegisterButton(_saveButton, onSave);
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;
        }

        private void RegisterButton(Button button, UnityEvent evt)
        {
            button.RegisterCallback<ClickEvent>(_ => evt.Invoke());
        }
    }
}

