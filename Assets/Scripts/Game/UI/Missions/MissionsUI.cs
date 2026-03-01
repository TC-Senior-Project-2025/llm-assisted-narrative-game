using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Extensions;
using Game.Services;
using Game.Services.Events;
using Game.Services.Saves;
using Newtonsoft.Json;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.Missions
{
    public class MissionsUI : MonoBehaviour
    {
        private UIDocument _uiDocument;
        private VisualElement _panel;

        private TextField _missionNameField;
        private TextField _missionDetailsField;
        private DropdownField _personDropdown;
        private Button _executeMissionButton;
        private Label _missionPointsLabel;

        private ReactiveProperty<string> _missionName = new("");
        private ReactiveProperty<string> _missionDetails = new("");
        private ReactiveProperty<int> _personId = new(-1);
        private ReactiveProperty<int> _missionPoints = new(0);

        private List<PersonData> _people;

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;
            _panel = root.Q("MissionsPanel");
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => SetEnabled(false));

            // Queries go under here (your task)
            _missionNameField = _panel.Q<TextField>("MissionNameField");
            _missionDetailsField = _panel.Q<TextField>("MissionDetailsField");
            _personDropdown = _panel.Q<DropdownField>("PersonDropdown");
            _executeMissionButton = _panel.Q<Button>("ExecuteMissionButton");
            _missionPointsLabel = _panel.Q<Label>("MissionPointsLabel");

            // Bind functions
            _executeMissionButton.RegisterCallback<ClickEvent>(evt => _ = OnExecuteMission());

            // Bind value changed
            _missionNameField.RegisterValueChangedCallback(evt =>
            {
                _missionName.Value = evt.newValue;
            });

            _missionDetailsField.RegisterValueChangedCallback(evt =>
            {
                _missionDetails.Value = evt.newValue;
            });

            _personDropdown.RegisterValueChangedCallback(evt =>
            {
                _personId.Value = _personDropdown.index >= 0
                    ? _people[_personDropdown.index].Id
                    : -1;
            });

            // Effects
            GameService.Main.State.Subscribe(state =>
            {
                _people = state.Person.Where(p => p.CountryId == GameService.Main.PlayerCountry.Id && p.IsAlive).ToList();
                _personDropdown.choices = _people.Select(p => $"{p.Name} (Loyalty: {p.Loyalty})").ToList();
                _missionPoints.Value = GameService.Main.PlayerCountry.MissionPoint;
                _missionPointsLabel.text = GameService.Main.PlayerCountry.MissionPoint.ToString();
            });

            _missionName
                .CombineLatestWith(_missionDetails)
                .CombineLatestWith(_personId)
                .CombineLatestWith(_missionPoints)
                .Select(tuple =>
                {
                    var (name, details, personId, missionPoints) = tuple;

                    return !string.IsNullOrWhiteSpace(name)
                        && !string.IsNullOrWhiteSpace(details)
                        && personId != -1
                        && missionPoints > 0;
                })
                .DistinctUntilChanged()
                .Subscribe(canExecute =>
                {
                    _executeMissionButton.SetEnabled(canExecute);
                });

            SetEnabled(false);
        }

        private async Task OnExecuteMission()
        {
            SetEnabled(false);

            GameUI.Main.SetLoading(true);

            // var evt = await EventService.GenerateExampleAsync();

            var evt = await MissionService.HandleMissionAction(_missionName.CurrentValue, _missionDetails.CurrentValue, _personId.CurrentValue);
            var evtJson = JsonConvert.SerializeObject(evt, Formatting.Indented);

            foreach (var outcome in evt.Outcomes)
            {
                EffectService.Main.ApplyEffects(GameService.Main.State.CurrentValue, outcome.Effects);
            }

            GameService.Main.State.ApplyInnerMutations();
            Debug.Log(evtJson);

            GameUI.Main.SetLoading(false);

            var choice = await GameUI.Main.DisplayEventAsync(evt);
            Debug.Log(JsonConvert.SerializeObject(choice, Formatting.Indented));
            EffectService.Main.ApplyEffects(GameService.Main.State.CurrentValue, choice.Effects);

            GameService.Main.State.ApplyInnerMutations();

            // Trigger MemoryService to record mission in history
            GameService.Main.RunMissionHistoryUpdate(evt, choice);
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.visible = isEnabled;
        }
    }
}

