using System.Collections.Generic;
using System.Linq;
using Extensions;
using Game.Services;
using Game.Services.Saves;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Diplomacy
{
    public class RelationPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        private VisualElement _panel;
        private Label _titleLabel;

        private ReactiveProperty<int> _countryId = new(-1);

        public UnityEvent<int> onTrade = new();
        public UnityEvent<int> onDeclareWar = new();
        public UnityEvent<int> onBreakAlliance = new();

        private readonly Button _declareWarButton;
        private readonly Button _breakAllianceButton;

        public RelationPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());
            _titleLabel = _panel.Q<Label>("TitleLabel");

            _declareWarButton = _panel.Q<Button>("DeclareWarButton");
            _breakAllianceButton = _panel.Q<Button>("BreakAlliance");

            _panel.Q<Button>("TradeButton").RegisterCallback<ClickEvent>(_ =>
            {
                if (_countryId.CurrentValue != -1)
                {
                    onTrade.Invoke(_countryId.CurrentValue);
                }
            });

            _declareWarButton.RegisterCallback<ClickEvent>(_ =>
            {
                if (_countryId.CurrentValue != -1)
                {
                    onDeclareWar.Invoke(_countryId.CurrentValue);
                }
            });

            _breakAllianceButton.RegisterCallback<ClickEvent>(_ =>
            {
                if (_countryId.CurrentValue != -1)
                {
                    onBreakAlliance.Invoke(_countryId.CurrentValue);
                }
            });

            _countryId.Subscribe(countryId =>
            {
                if (countryId == -1) return;

                var country = GameService.Main.State.CurrentValue.Country.GetValueOrDefault(countryId);
                _titleLabel.text = $"Relations with {country.Name}";
            });

            GameService.Main.State.CombineLatestWith(_countryId).Subscribe(tuple =>
            {
                var (gameState, countryId) = tuple;
                UpdateButtons(gameState, countryId);
            });
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;
        }

        private void UpdateButtons(SaveData save, int countryId)
        {
            var playerCountryId = save.Game.PlayerCountryId;

            var isAtWar = save.Relation.Any(
                r => r.SrcCountryId == countryId && r.DstCountryId == playerCountryId && r.IsAtWar);

            var isAllied = save.Relation.Any(
                r => r.SrcCountryId == countryId && r.DstCountryId == playerCountryId && r.IsAllied);

            Debug.Log($"isAllied {countryId} {isAllied}");

            _declareWarButton.SetEnabled(!isAtWar && !isAllied);
            _breakAllianceButton.SetEnabled(isAllied);
        }

        public void SetCountryId(int countryId)
        {
            _countryId.Value = countryId;
        }
    }

}

