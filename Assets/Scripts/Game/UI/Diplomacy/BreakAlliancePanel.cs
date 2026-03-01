using System;
using Extensions;
using Game.Services;
using Game.Services.Saves;
using Game.Services.Sounds;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Diplomacy
{
    public class BreakAlliancePanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        private readonly VisualElement panel;

        private readonly Label _titleLabel;
        private readonly Label _descLabel;
        private readonly Button _confirmBreakAlliance;

        public ReactiveProperty<int> targetCountryId = new(-1);

        public BreakAlliancePanel(VisualElement _panel)
        {
            panel = _panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _titleLabel = _panel.Q<Label>("Title");
            _descLabel = _panel.Q<Label>("Description");
            _confirmBreakAlliance = _panel.Q<Button>("ConfirmBreakAlliance");
            _confirmBreakAlliance.RegisterCallback<ClickEvent>(_ => OnConfirmBreakAlliance());

            GameService.Main.State.CombineLatest(targetCountryId, (x, y) => (x, y)).Subscribe(tuple =>
            {
                var (state, countryId) = tuple;

                var country = state.Country[countryId];
                _titleLabel.text = $"Break Alliance with {country.Name}";
                _descLabel.text = $"You are breaking the alliance with {country.Name}. Your units in their land will be transported back to your country.";
            });
        }

        private DiplomacyService.DealProposal BuildBreakAlliance()
        {
            var playerCountry = GameService.Main.PlayerCountry;
            var targetCountry = GameService.Main.State.CurrentValue.Country[targetCountryId.CurrentValue];

            var breakAlliance = new DiplomacyService.DealProposal(playerCountry, targetCountry);
            var incomingResourceType = DiplomacyService.DealItemType.BreakAlliance;
            var outgoingResourceType = DiplomacyService.DealItemType.BreakAlliance;

            breakAlliance.AddItem(false, incomingResourceType, 0);
            breakAlliance.AddItem(true, outgoingResourceType, 0);

            return breakAlliance;
        }

        private void OnConfirmBreakAlliance()
        {
            var breakAlliance = BuildBreakAlliance();
            DiplomacyService.Main.ExecuteDeal(breakAlliance);
            GameService.Main.State.ApplyInnerMutations();
            // SfxService.Main.Play(SfxService.Main.swordsClash);
            onClose.Invoke();
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        public void SetEnabled(bool isEnabled)
        {
            panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}