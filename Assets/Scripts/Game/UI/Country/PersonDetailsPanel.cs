using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Extensions;
using Game.Services;
using Game.UI.Interfaces;
using Newtonsoft.Json.Linq;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.PlayerLoop;
using UnityEngine.UIElements;

namespace Game.UI.Country
{
    public class PersonDetailsPanel : IBasePanel
    {
        private readonly VisualElement _panel;
        public UnityEvent onClose { get; private set; } = new();
        private readonly ListView _listView;

        private readonly Label _nameLabel;
        private readonly Label _roleLabel;
        private readonly Label _statusLabel;

        public readonly ReactiveProperty<int> personId = new(-1);
        private List<(string, int)> _currentPersonStats = null;

        private bool _isDirty = false;
        private bool _isEnabled = false;

        public PersonDetailsPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => onClose.Invoke());
            _listView = _panel.Q<ListView>();

            _nameLabel = _panel.Q<Label>("NameLabel");
            _roleLabel = _panel.Q<Label>("RoleLabel");
            _statusLabel = _panel.Q<Label>("StatusLabel");

            _listView.bindItem = (element, index) =>
            {
                var statName = element.Q<Label>("StatName");
                var statValue = element.Q<Label>("StatValue");

                statName.text = _currentPersonStats[index].Item1;
                statValue.text = _currentPersonStats[index].Item2.ToString();
            };

            GameService.Main.State.CombineLatestWith(personId).Subscribe(_ =>
            {
                _isDirty = true;
                if (_isEnabled)
                {
                    Render();
                }
            });
        }

        string Humanize(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            key = key.Replace('_', ' ');
            return char.ToUpper(key[0], CultureInfo.InvariantCulture) + key[1..];
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;
            _isEnabled = isEnabled;

            if (isEnabled && _isDirty)
            {
                Render();
            }
        }

        private void Render()
        {
            _isDirty = false;
            var state = GameService.Main.State.CurrentValue;

            if (personId.CurrentValue == -1) return;
            var person = state.Person.SingleOrDefault(p => p.Id == personId.CurrentValue);
            if (person == null) return;

            _nameLabel.text = $"{person.Name} ({person.Age})";
            _roleLabel.text = person.Role;

            _statusLabel.text = person.IsAlive ? "Alive" : "Dead";
            _statusLabel.style.color = person.IsAlive ? Color.softGreen : Color.softRed;

            var mainData = new List<(string, int)>
            {
                ("Morale", person.Stats.Morale.GetValueOrDefault()),
                ("Field Offense", person.Stats.FieldOffense.GetValueOrDefault()),
                ("Field Defense", person.Stats.FieldDefense.GetValueOrDefault()),
                ("Siege Offense", person.Stats.SiegeOffense.GetValueOrDefault()),
                ("Siege Defense", person.Stats.SiegeDefense.GetValueOrDefault())
            };

            var extraData = person.Stats.ExtensionData
                .Select(kvp => (Humanize(kvp.Key), kvp.Value.Value<int>()));

            _currentPersonStats = mainData.Concat(extraData).ToList();

            _listView.itemsSource = _currentPersonStats;
            _listView.Rebuild();
        }
    }
}