using System;
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

namespace Game.UI.Commandery
{
    public class ChangeGovernorPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        private readonly VisualElement _panel;

        private readonly ReactiveProperty<int> _commanderyId = new(-1);
        private readonly ReactiveProperty<int> _governorId = new(-1);
        private readonly ReactiveProperty<int> _newGovernorId = new(-1);

        private readonly Label _curGovLabel;
        private readonly Label _newGovLabel;

        private readonly Button _removeGovButton;
        private readonly Button _changeGovButton;

        private readonly ListView _listView;
        private List<PersonData> _people;

        private bool _peopleListNeedsUpdate = false;

        public UnityEvent<int, int> onChangeGovernor = new();

        public ChangeGovernorPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _curGovLabel = _panel.Q<Label>("CurrentGovLabel");
            _newGovLabel = _panel.Q<Label>("NewGovLabel");
            _listView = _panel.Q<ListView>();
            _removeGovButton = _panel.Q<Button>("RemoveGovButton");
            _changeGovButton = _panel.Q<Button>("ChangeGovButton");

            _removeGovButton.RegisterCallback<ClickEvent>(_ =>
            {
                _newGovernorId.Value = -1;
            });

            _changeGovButton.RegisterCallback<ClickEvent>(_ => OnChangeGovernor());

            _governorId.Subscribe(pid =>
            {
                if (pid == -1)
                {
                    _curGovLabel.text = "None";
                    _curGovLabel.style.color = Color.gray;
                    return;
                }

                var person = GameService.Main.State.CurrentValue.Person.Find(p => p.Id == pid);
                if (person == null) return;

                _curGovLabel.text = person.Name;
                _curGovLabel.style.color = Color.white;
            });

            GameService.Main.State.Subscribe(_ =>
            {
                _peopleListNeedsUpdate = true;
            });

            _newGovernorId.Subscribe(pid =>
            {
                if (pid == -1)
                {
                    _newGovLabel.text = "None";
                    _newGovLabel.style.color = Color.gray;
                    return;
                }

                var person = GameService.Main.State.CurrentValue.Person.Find(p => p.Id == pid);
                if (person == null) return;

                _newGovLabel.text = person.Name;
                _newGovLabel.style.color = Color.white;
            });

            _listView.bindItem = (item, index) =>
            {
                var button = item.Q<Button>();
                var person = _people[index];

                var nameLabel = item.Q<Label>("NameLabel");
                var roleLabel = item.Q<Label>("RoleLabel");

                nameLabel.text = $"{person.Name} ({person.Age})";
                roleLabel.text = person.Role;

                button.RegisterCallback<ClickEvent>(_ =>
                {
                    _newGovernorId.Value = person.Id;
                });
            };
        }

        private void OnChangeGovernor()
        {
            GameService.Main.CommanderyAction.ExecuteChangeGovernor(
                GameService.Main.State.CurrentValue,
                _commanderyId.CurrentValue,
                _newGovernorId.CurrentValue);
            GameService.Main.State.ApplyInnerMutations();
            onChangeGovernor.Invoke(_commanderyId.CurrentValue, _newGovernorId.CurrentValue);
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;

            if (isEnabled && _peopleListNeedsUpdate)
            {
                var state = GameService.Main.State.CurrentValue;
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

                _people = state.Person
                    .Where(p => p.CountryId == state.Game.PlayerCountryId && !busyPersonIds.Contains(p.Id) && p.IsAlive)
                    .ToList();

                _listView.itemsSource = _people;
                _listView.Rebuild();
                _peopleListNeedsUpdate = false;
            }
        }

        public bool IsEnabled()
        {
            return _panel.style.display == DisplayStyle.Flex;
        }

        public void SetGovernorId(int personId)
        {
            _governorId.Value = personId;
        }

        public void SetCommanderyId(int commanderyId)
        {
            _commanderyId.Value = commanderyId;
        }
    }
}
