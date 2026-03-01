using System.Collections.Generic;
using Game.Services;
using Game.Services.Commands;
using Game.UI.Army;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class ArmyDetailsUI : MonoBehaviour
    {
        private UIDocument _uiDocument;
        public UnityEvent onClose = new();

        [SerializeField]
        private ArmyUnitRenderer armyUnitRenderer;

        private Label _nameLabel;
        private Label _sizeLabel;
        private Label _moraleLabel;
        private Label _supplyLabel;
        private Label _actionsLeftLabel;
        private Label _commanderNameLabel;
        private Button _resupplyButton;
        private Button _splitButton;
        private Button _mergeButton;
        private Button _disbandButton;
        private Button _changeCommanderButton;

        private ResupplyUnitPanel _resupplyUnitPanel;
        private SplitUnitPanel _splitUnitPanel;
        private ChangeCommanderPanel _changeCommanderPanel;
        private MergePanel2 _mergePanel;

        private readonly ReactiveProperty<int> _unitId = new(-1);

        private List<IBasePanel> _panels;
        private List<Button> _buttons;

        private void Start()
        {
            _uiDocument = GetComponent<UIDocument>();
            var root = _uiDocument.rootVisualElement;

            root.Q<Button>("CloseButton")
                .RegisterCallback<ClickEvent>(_ => OnClose());

            _nameLabel = root.Q<Label>("ArmyNameLabel");
            _sizeLabel = root.Q<Label>("SizeLabel");
            _moraleLabel = root.Q<Label>("MoraleLabel");
            _supplyLabel = root.Q<Label>("SupplyLabel");
            _actionsLeftLabel = root.Q<Label>("ActionsLeftLabel");
            _commanderNameLabel = root.Q<Label>("CommanderNameLabel");
            _resupplyButton = root.Q<Button>("ResupplyButton");
            _splitButton = root.Q<Button>("SplitButton");
            _changeCommanderButton = root.Q<Button>("ChangeCommanderButton");
            _changeCommanderButton = root.Q<Button>("ChangeCommanderButton");
            _mergeButton = root.Q<Button>("MergeButton");
            _disbandButton = root.Q<Button>("DisbandButton");

            _resupplyUnitPanel = new(root.Q("ResupplyUnitPanel"));
            _splitUnitPanel = new(root.Q("SplitUnitPanel"));
            _changeCommanderPanel = new(root.Q("ChangeCommanderPanel"));
            _mergePanel = new(root.Q("MergePanel"));

            _panels = new() { _resupplyUnitPanel, _splitUnitPanel, _changeCommanderPanel, _mergePanel };
            _buttons = new() { _resupplyButton, _splitButton, _changeCommanderButton, _mergeButton, _disbandButton };

            root.visible = false;

            armyUnitRenderer.unitClicked.AddListener(unitId =>
            {
                if (_mergePanel.IsEnabled()) return;
                _unitId.Value = unitId;
                root.visible = true;
            });

            armyUnitRenderer.unitClicked.AddListener(id =>
            {
                if (_mergePanel.IsEnabled())
                {
                    _mergePanel.SetTargetUnitId(id);
                }
            });

            _resupplyButton.RegisterCallback<ClickEvent>(_ =>
            {
                EnablePanel(_resupplyUnitPanel);
            });

            _splitButton.RegisterCallback<ClickEvent>(_ =>
            {
                EnablePanel(_splitUnitPanel);
            });

            _changeCommanderButton.RegisterCallback<ClickEvent>(_ =>
            {
                EnablePanel(_changeCommanderPanel);
            });

            _mergeButton.RegisterCallback<ClickEvent>(_ =>
            {
                _mergePanel.SetSourceUnitId(_unitId.CurrentValue);
                EnablePanel(_mergePanel);
            });

            _disbandButton.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    var unitId = _unitId.CurrentValue;
                    var unit = GameService.Main.State.CurrentValue.Army.Find(u => u.Id == unitId);
                    if (unit != null)
                    {
                        ArmyCommands.DecreaseArmy(unitId, unit.Size);
                        OnClose();
                    }
                }
            });

            _mergePanel.onMerge.AddListener(() =>
            {
                OnClose();
            });

            _resupplyUnitPanel.onClose.AddListener(() => _resupplyUnitPanel.SetEnabled(false));
            _splitUnitPanel.onClose.AddListener(() => _splitUnitPanel.SetEnabled(false));
            _changeCommanderPanel.onClose.AddListener(() => _changeCommanderPanel.SetEnabled(false));
            _mergePanel.onClose.AddListener(() => _mergePanel.SetEnabled(false));

            var observer1 = _unitId.CombineLatest(GameService.Main.State, (x, y) => (x, y));
            var observer2 = GameService.Main.currentPhase;

            observer1.CombineLatest(observer2, (x, y) => (x, y)).Subscribe(tuple =>
            {
                var ((unitId, state), phase) = tuple;
                UpdateUi(unitId, phase);
            });
        }

        private void OnClose()
        {
            _unitId.Value = -1;
            _uiDocument.rootVisualElement.visible = false;
        }

        private void EnablePanel(IBasePanel panel)
        {
            foreach (var p in _panels)
            {
                if (p == panel)
                {
                    p.SetEnabled(true);
                }
                else
                {
                    p.SetEnabled(false);
                }
            }
        }

        private void SetAllPanelsEnabled(bool isEnabled)
        {
            foreach (var p in _panels)
            {
                p.SetEnabled(isEnabled);
            }
        }

        private void SetAllButtonsEnabled(bool isEnabled)
        {
            foreach (var b in _buttons)
            {
                b.SetEnabled(isEnabled);
            }
        }

        private void UpdateUi(int unitId, GameService.GamePhase phase)
        {
            var unit = GameService.Main.State.CurrentValue.Army.Find(u => u.Id == unitId);
            if (unit == null) return;

            _nameLabel.text = unit.Name;
            _sizeLabel.text = $"{unit.Size:N0}";
            _moraleLabel.text = $"{unit.Morale}%";
            _supplyLabel.text = $"{unit.Supply}%";
            _actionsLeftLabel.text = $"{unit.ActionLeft}";

            var commander = GameService.Main.State.CurrentValue.Person.Find(p => p.Id == unit.CommanderId);
            _commanderNameLabel.text = commander == null ? "No commander" : commander.Name;

            var setButtonsEnabled = true;

            if (unit.CountryId == GameService.Main.PlayerCountry.Id)
            {
                _resupplyUnitPanel.SetSelectedUnit(unit);
                _splitUnitPanel.SetSelectedUnit(unit);
                _changeCommanderPanel.SetCurrentUnit(unit);
            }
            else
            {
                SetAllPanelsEnabled(false);
                setButtonsEnabled = false;
            }

            if (unit.ActionLeft == 0)
            {
                SetAllPanelsEnabled(false);
                setButtonsEnabled = false;
            }

            if (phase != GameService.GamePhase.PlayerAction)
            {
                setButtonsEnabled = false;
            }

            SetAllButtonsEnabled(setButtonsEnabled);
        }
    }
}