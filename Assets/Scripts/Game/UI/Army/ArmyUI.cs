using System;
using System.Collections.Generic;
using Extensions;
using Game.Services;
using Game.UI.Interfaces;
using Game.World.Map;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class ArmyUI : MonoBehaviour
    {
        public enum State
        {
            Closed,
            Open,
            Creating,
            Moving,
            Merging
        }

        public ReactiveProperty<State> currentState = new(State.Closed);

        public CreateUnitPanel createUnitPanel;
        public MergeUnitPanel mergeUnitPanel;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private ArmyPanel _armyPanel;
        private IBasePanel _previousPanel;

        public UnityEvent onClose = new();

        void Awake()
        {
            _uiDocument = GetComponent<UIDocument>();
            _root = _uiDocument.rootVisualElement;
            createUnitPanel = new(_root.Q("CreateUnitPanel"));
            mergeUnitPanel = new(_root.Q("MergeUnitPanel"));
        }

        void Start()
        {
            _armyPanel = new(_root.Q("ArmyPanel"));
            RegisterPanel(_armyPanel.onCreate, createUnitPanel, State.Creating);
            RegisterPanel(_armyPanel.onMerge, mergeUnitPanel, State.Merging);

            _armyPanel.onClose.AddListener(() => SetEnabled(false));
            GameMap.Main.Picker.provinceClicked.AddListener(ProvinceClicked);

            Init();
        }

        private void ProvinceClicked(Color32 color)
        {
            switch (currentState.CurrentValue)
            {
                case State.Open:
                case State.Closed:
                    return;
            }

            if (color.SameAs(Color.clear)) return;
            var provinceId = GameMap.Main.Provider.GetProvinceId(color);
            var province = GameService.Main.State.CurrentValue.Commandery.GetValueOrDefault(provinceId);

            if (province == null) return;

            switch (currentState.CurrentValue)
            {
                case State.Creating:
                    var countryId = GameService.Main.PlayerCountry.Id;
                    if (province.CountryId != countryId) return;
                    createUnitPanel.SetLocationId(provinceId);
                    break;
            }
        }

        private void RegisterPanel(UnityEvent actionEvent, IBasePanel panel, State newState)
        {
            actionEvent.AddListener(() => ShowPanel(panel, newState));
            panel.onClose.AddListener(() => ShowPanel(null, State.Open));
        }

        private void ShowPanel(IBasePanel panel, State newState)
        {
            if (panel == null)
            {
                _armyPanel.SetAllButtonsEnabled(true);
            }
            else
            {
                _armyPanel.SetAllButtonsEnabled(false);
            }

            currentState.Value = newState;

            _previousPanel?.SetEnabled(false);
            panel?.SetEnabled(true);
            _previousPanel = panel;
        }

        private void Init()
        {
            _armyPanel.SetEnabled(false);
        }

        public void SetEnabled(bool isEnabled)
        {
            _armyPanel.SetEnabled(isEnabled);

            if (isEnabled)
            {
                _armyPanel.SetAllButtonsEnabled(true);
                currentState.Value = State.Open;
            }
            else
            {
                onClose.Invoke();
                currentState.Value = State.Closed;
            }
        }
    }
}

