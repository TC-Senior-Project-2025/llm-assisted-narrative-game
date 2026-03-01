using System;
using System.Collections.Generic;
using System.Linq;
using Game.Services;
using Game.Services.Saves;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.Country
{
    public class CountryDetailsUI : MonoBehaviour
    {
        private UIDocument _uiDocument;
        private VisualElement _panel;
        private ListView _listView;

        private List<PersonData> _people;

        private PersonDetailsPanel _personDetailsPanel;

        private bool _showPersonDetailsAlways = false;

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;
            _panel = root.Q("CountryDetailsPanel");
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => SetEnabled(false));
            _listView = root.Q<ListView>();

            _personDetailsPanel = new(root.Q("PersonDetailsPanel"));
            _personDetailsPanel.SetEnabled(false);

            SetEnabled(false);

            GameService.Main.State.Subscribe(currentState =>
            {
                _people = currentState.Person.Where(p => p.CountryId == GameService.Main.PlayerCountry.Id).ToList();
                _listView.itemsSource = _people;
                _listView.Rebuild();
            });

            _listView.bindItem = (element, index) =>
            {
                var button = element.Q<Button>();
                var person = _people[index];
                var nameLabel = button.Q<Label>("NameLabel");
                var roleLabel = button.Q<Label>("RoleLabel");
                var loyaltyLabel = button.Q<Label>("LoyaltyLabel");

                nameLabel.text = $"{person.Name} (Age {person.Age})";
                if (!person.IsAlive)
                {
                    nameLabel.style.color = Color.red;
                }
                else
                {
                    nameLabel.style.color = Color.black;
                }
                roleLabel.text = $"{person.Role}";
                loyaltyLabel.text = $"Loyalty: {GetLoyaltyLevel(person.Loyalty)}";
                loyaltyLabel.style.backgroundColor = GetLoyaltyColor(person.Loyalty);

                button.RegisterCallback<PointerEnterEvent>(_ => OnHoverEnter(person.Id));
                button.RegisterCallback<PointerLeaveEvent>(_ => OnHoverLeave());
                button.RegisterCallback<ClickEvent>(_ => OnClick(person.Id));
            };

            _personDetailsPanel.onClose.AddListener(() =>
            {
                _personDetailsPanel.SetEnabled(false);
                _showPersonDetailsAlways = false;
            });
        }

        private void OnHoverEnter(int personId)
        {
            if (!_showPersonDetailsAlways)
            {
                _personDetailsPanel.personId.Value = personId;
                _personDetailsPanel.SetEnabled(true);
            }
        }

        private void OnClick(int personId)
        {
            _showPersonDetailsAlways = true;
            // _personDetailsPanel.personId.Value = personId;
            // _personDetailsPanel.SetEnabled(true);
        }

        private void OnHoverLeave()
        {
            if (!_showPersonDetailsAlways)
            {
                _personDetailsPanel.SetEnabled(false);
            }
        }

        private string GetLoyaltyLevel(int loyalty)
        {
            if (loyalty > 80) return "Devoted";
            else if (loyalty > 60) return "Loyal";
            else if (loyalty > 40) return "Ambivalent";
            else if (loyalty > 20) return "Disloyal";
            else return "Treacherous";
        }

        private Color GetLoyaltyColor(int loyalty)
        {
            if (loyalty > 80) return Color.aquamarine;
            else if (loyalty > 60) return Color.paleGreen;
            else if (loyalty > 40) return Color.khaki;
            else if (loyalty > 20) return Color.lightSalmon;
            else return Color.lightCoral;
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;

            if (!isEnabled)
            {
                _showPersonDetailsAlways = false;
                _personDetailsPanel.SetEnabled(false);
                _personDetailsPanel.personId.Value = -1;
            }
        }
    }
}