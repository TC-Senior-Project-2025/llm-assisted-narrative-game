using System.Collections.Generic;
using System.Linq;
using Game.Services;
using Game.Services.Saves;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI.Diplomacy
{
    [RequireComponent(typeof(UIDocument))]
    public class DiplomacyUI : MonoBehaviour
    {
        private UIDocument _uiDocument;

        [SerializeField]
        private VisualTreeAsset _relationTemplate;

        private VisualElement _mainPanel;
        private VisualElement _container;
        private RelationPanel _relationPanel;
        private TradePanel _tradePanel;
        private DeclareWarPanel _declareWarPanel;
        private BreakAlliancePanel _breakAlliancePanel;

        private List<IBasePanel> _panels;

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;

            _container = root.Q("Container");
            _relationPanel = new(root.Q("RelationPanel"));
            _declareWarPanel = new(root.Q("DeclareWarPanel"));
            _breakAlliancePanel = new(root.Q("BreakAlliancePanel"));
            _tradePanel = new(root.Q("TradePanel"));
            _mainPanel = root.Q("MainPanel");

            _panels = new() { _tradePanel, _declareWarPanel, _breakAlliancePanel };

            _container.Clear();

            root.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => SetEnabled(false));

            SetEnabled(false);
            _relationPanel.SetEnabled(false);
            _tradePanel.SetEnabled(false);
            _declareWarPanel.SetEnabled(false);
            _breakAlliancePanel.SetEnabled(false);

            _relationPanel.onClose.AddListener(() =>
            {
                _relationPanel.SetEnabled(false);
                _tradePanel.SetEnabled(false);
                _declareWarPanel.SetEnabled(false);
            });

            _relationPanel.onTrade.AddListener((countryId) =>
            {
                _tradePanel.SetTradeCountryId(countryId);
                EnablePanel(_tradePanel);
            });

            _relationPanel.onDeclareWar.AddListener((countryId) =>
            {
                EnablePanel(_declareWarPanel);
                _declareWarPanel.targetCountryId.Value = countryId;
            });

            _relationPanel.onBreakAlliance.AddListener((countryId) =>
            {
                EnablePanel(_breakAlliancePanel);
                _breakAlliancePanel.targetCountryId.Value = countryId;
            });

            _tradePanel.onClose.AddListener(() => _tradePanel.SetEnabled(false));
            _breakAlliancePanel.onClose.AddListener(() => _breakAlliancePanel.SetEnabled(false));
            _declareWarPanel.onClose.AddListener(() => _declareWarPanel.SetEnabled(false));

            GameService.Main.State.Subscribe(OnStateUpdate);
        }

        private void EnablePanel(IBasePanel panel)
        {
            foreach (var p in _panels)
            {
                p.SetEnabled(p == panel);
            }
        }

        public void SetEnabled(bool isEnabled)
        {
            _mainPanel.visible = isEnabled;
            if (!isEnabled)
            {
                _relationPanel.SetEnabled(false);
            }
        }

        private void OnStateUpdate(SaveData state)
        {
            _container.Clear();

            var playerCountryId = state.Game.PlayerCountryId;

            var incomingRelations = state.Relation
                .Where(r => r.DstCountryId == playerCountryId)
                .ToDictionary(r => r.SrcCountryId, r => r);

            var outgoingRelations = state.Relation
                .Where(r => r.SrcCountryId == playerCountryId)
                .ToDictionary(r => r.DstCountryId, r => r);

            foreach (var (countryId, relation) in incomingRelations)
            {
                var relationElement = _relationTemplate.Instantiate();
                var country = state.Country[countryId];

                relationElement.Q<Label>("CountryNameLabel").text = country.Name;
                relationElement.Q<Label>("IncomingLabel").text = $"{relation.Value}";
                relationElement.Q<Label>("OutgoingLabel").text = $"{outgoingRelations[countryId].Value}";

                var button = relationElement.Q<Button>();
                button.RegisterCallback<ClickEvent>(_ =>
                {
                    _relationPanel.SetEnabled(true);
                    _relationPanel.SetCountryId(countryId);
                });

                if (relation.IsAtWar)
                {
                    button.style.backgroundColor = new Color(1f, 0.5f, 0.5f); // Light Red
                }
                else if (relation.IsAllied)
                {
                    button.style.backgroundColor = new Color(0.5f, 1f, 0.5f); // Light Green
                }

                _container.Add(relationElement);
            }
        }
    }
}