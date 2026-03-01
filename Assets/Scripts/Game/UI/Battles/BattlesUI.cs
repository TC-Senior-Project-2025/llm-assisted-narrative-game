using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Services;
using Game.Services.Saves;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.Battles
{
    public class BattlesUI : MonoBehaviour
    {
        private UIDocument _uiDocument;
        private ListView _listView;
        private VisualElement _panel;

        private List<BattleData> _battles;

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;
            _listView = root.Q<ListView>();
            _panel = root.Q("BattlesPanel");
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => SetEnabled(false));

            GameService.Main.State.Subscribe(currentState =>
            {
                _battles = currentState.Battle;
                _listView.itemsSource = _battles;
                _listView.Rebuild();
            });

            var textInfo = CultureInfo.CurrentCulture.TextInfo;

            _listView.bindItem = (element, index) =>
            {
                var button = element.Q<Button>();
                var battle = _battles[index];
                var location = GameService.Main.State.CurrentValue.Commandery[battle.LocationId];

                var battleNameLabel = button.Q<Label>("BattleName");
                battleNameLabel.text = $"{textInfo.ToTitleCase(battle.Phase)} of {location.Name}";

                var attackerLossesLabel = button.Q<Label>("AttackerLossesLabel");
                var defenderLossesLabel = button.Q<Label>("DefenderLossesLabel");
                attackerLossesLabel.text = $"Attacker losses: {battle.AttackerLosses}";
                defenderLossesLabel.text = $"Defender losses: {battle.DefenderLosses}";

                var assaultButton = button.Q<Button>("AssaultButton");
                if (assaultButton != null)
                {
                    var state = GameService.Main.State.CurrentValue;
                    var playerCountryId = state.Game.PlayerCountryId;
                    var isAttacker = false;

                    // Check if player is one of the attackers
                    foreach (var armyId in battle.AttackerArmyIds)
                    {
                        var army = state.Army.Find(a => a.Id == armyId);
                        if (army != null && army.CountryId == playerCountryId)
                        {
                            isAttacker = true;
                            break;
                        }
                    }

                    assaultButton.userData = battle;
                    assaultButton.UnregisterCallback<ClickEvent>(OnAssaultButtonClicked);
                    if (isAttacker)
                    {
                        assaultButton.RegisterCallback<ClickEvent>(OnAssaultButtonClicked);
                    }

                    if (isAttacker && battle.Phase == "siege")
                    {
                        assaultButton.text = "Siege";
                        assaultButton.style.color = Color.black;
                        assaultButton.style.backgroundColor = Color.yellow;
                        assaultButton.style.display = DisplayStyle.Flex;
                    }
                    else if (isAttacker && battle.Phase == "assault")
                    {
                        assaultButton.text = "Assault";
                        assaultButton.style.color = Color.black;
                        assaultButton.style.backgroundColor = Color.red;
                        assaultButton.style.display = DisplayStyle.Flex;
                    }
                    else
                    {
                        assaultButton.style.display = DisplayStyle.None;
                    }
                }
            };

            SetEnabled(false);
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;
        }

        private void OnAssaultButtonClicked(ClickEvent evt)
        {
            if (evt.target is Button btn && btn.userData is BattleData battle)
            {
                if (battle.Phase == "siege")
                {
                    battle.Phase = "assault";
                }
                else if (battle.Phase == "assault")
                {
                    battle.Phase = "siege";
                }

                // Push state update
                GameService.Main.State.OnNext(GameService.Main.State.CurrentValue);
            }
        }
    }
}