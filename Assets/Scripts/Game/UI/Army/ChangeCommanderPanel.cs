using System;
using System.Collections.Generic;
using System.Linq;
using Game.Services;
using Game.Services.Commands;
using Game.Services.Saves;
using Game.UI.Interfaces;
using R3;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class ChangeCommanderPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();

        private readonly VisualElement _panel;
        private readonly ListView _listView;

        private readonly Label _currentCommanderLabel;
        private readonly Label _newCommanderLabel;
        private readonly Button _changeCommanderButton;
        private readonly Button _removeCommanderButton;

        private List<PersonData> _people;
        private ReactiveProperty<int> _armyId = new(-1);
        private ReactiveProperty<int> _currentCommanderId = new(-1);
        private ReactiveProperty<int> _newCommanderId = new(-1);

        public ChangeCommanderPanel(VisualElement panel)
        {
            _panel = panel;
            _listView = _panel.Q<ListView>();

            _currentCommanderLabel = _panel.Q<Label>("CurrentCommanderLabel");
            _newCommanderLabel = _panel.Q<Label>("NewCommanderLabel");
            _changeCommanderButton = _panel.Q<Button>("ChangeCommanderButton");
            _removeCommanderButton = _panel.Q<Button>("RemoveCommanderButton");

            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _removeCommanderButton.RegisterCallback<ClickEvent>(_ =>
            {
                _newCommanderId.Value = -1;
            });

            _changeCommanderButton.RegisterCallback<ClickEvent>(_ => OnChangeCommander());

            GameService.Main.State.Subscribe(state =>
            {
                if (state == null) return;

                var busyPersonIds = new HashSet<int>();

                // Add commanders from armies
                if (state.Army != null)
                {
                    foreach (var army in state.Army)
                    {
                        if (army.CommanderId.HasValue) busyPersonIds.Add(army.CommanderId.Value);
                    }
                }

                // Add governors from commanderies
                if (state.Commandery != null)
                {
                    foreach (var cmd in state.Commandery.Values)
                    {
                        if (cmd.CommanderId.HasValue) busyPersonIds.Add(cmd.CommanderId.Value);
                    }
                }

                var people = state.Person
                    .Where(p => p.CountryId == state.Game.PlayerCountryId && !busyPersonIds.Contains(p.Id) && p.IsAlive)
                    .ToList();
                _people = people;
                _listView.itemsSource = _people;
                _listView.Rebuild();
            });

            _listView.bindItem = (element, index) =>
            {
                var button = element.Q<Button>();
                var person = _people[index];
                //button.text = person.Name;

                var nameLabel = element.Q<Label>("NameLabel");
                var roleLabel = element.Q<Label>("RoleLabel");

                nameLabel.text = $"{person.Name} ({person.Age})";
                roleLabel.text = person.Role;

                button.RegisterCallback<ClickEvent>(_ =>
                {
                    if (person.Id != _currentCommanderId.CurrentValue)
                    {
                        _newCommanderId.Value = person.Id;
                    }
                });
            };

            _currentCommanderId.Subscribe(personId =>
            {
                if (personId == -1)
                {
                    _currentCommanderLabel.text = "None";
                }

                var person = GameService.Main.State.CurrentValue.Person.Find(p => p.Id == personId);
                if (person == null) return;

                _currentCommanderLabel.text = person.Name;
            });

            _newCommanderId.Subscribe(personId =>
            {
                bool setChangeCommanderButtonEnabled = true;

                if (personId == -1)
                {
                    _newCommanderLabel.text = "None";
                }
                else
                {
                    var person = GameService.Main.State.CurrentValue.Person.Find(p => p.Id == personId);
                    if (person == null)
                    {
                        setChangeCommanderButtonEnabled = false;
                    }
                    else
                    {
                        _newCommanderLabel.text = person.Name;
                        if (person.Id == _currentCommanderId.CurrentValue)
                        {
                            setChangeCommanderButtonEnabled = false;
                        }
                    }
                }

                _changeCommanderButton.SetEnabled(setChangeCommanderButtonEnabled);
            });
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        private void OnChangeCommander()
        {
            ArmyCommands.ChangeCommander(_armyId.CurrentValue, _newCommanderId.CurrentValue);
            onClose.Invoke();
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetCurrentUnit(ArmyData unit)
        {
            _armyId.Value = unit.Id;
            _currentCommanderId.Value = unit.CommanderId == null ? -1 : unit.CommanderId.Value;
            _newCommanderId.Value = _currentCommanderId.CurrentValue;
        }
    }
}