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
    public class DeclareWarPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        private readonly VisualElement panel;

        private readonly Label _titleLabel;
        private readonly Label _descLabel;
        private readonly Button _confirmDeclareWarButton;

        public ReactiveProperty<int> targetCountryId = new(-1);

        public DeclareWarPanel(VisualElement _panel)
        {
            panel = _panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _titleLabel = _panel.Q<Label>("Title");
            _descLabel = _panel.Q<Label>("Description");
            _confirmDeclareWarButton = _panel.Q<Button>("ConfirmDeclareWar");
            _confirmDeclareWarButton.RegisterCallback<ClickEvent>(_ => OnConfirmDeclareWar());

            GameService.Main.State.CombineLatest(targetCountryId, (x, y) => (x, y)).Subscribe(tuple =>
            {
                var (state, countryId) = tuple;

                var country = state.Country[countryId];
                _titleLabel.text = $"Declare War on {country.Name}";
                _descLabel.text = $"You are declaring war on {country.Name}. Are you sure?";
            });
        }

        private DiplomacyService.DealProposal BuildWarDecleration()
        {
            var playerCountry = GameService.Main.PlayerCountry;
            var targetCountry = GameService.Main.State.CurrentValue.Country[targetCountryId.CurrentValue];

            var warDeclaration = new DiplomacyService.DealProposal(playerCountry, targetCountry);
            var incomingResourceType = DiplomacyService.DealItemType.DeclareWar;
            var outgoingResourceType = DiplomacyService.DealItemType.DeclareWar;

            warDeclaration.AddItem(false, incomingResourceType, 0);
            warDeclaration.AddItem(true, outgoingResourceType, 0);

            return warDeclaration;
        }

        private void OnConfirmDeclareWar()
        {
            var warDeclaration = BuildWarDecleration();
            DiplomacyService.Main.ExecuteDeal(warDeclaration);
            GameService.Main.State.ApplyInnerMutations();
            SfxService.Main.Play(SfxService.Main.swordsClash);
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