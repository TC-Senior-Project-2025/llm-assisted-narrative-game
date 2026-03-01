using System.Threading.Tasks;
using Game.Services;
using Game.Services.Events;
using Game.UI.Army;
using Game.UI.Battles;
using Game.UI.Country;
using Game.UI.Diplomacy;
using Game.UI.Missions;
using Game.UI.Saves;
using R3;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class GameUI : MonoBehaviour
    {
        public static GameUI Main { get; private set; }

        [SerializeField] private VisualTreeAsset optionButtonTemplate;
        [SerializeField] private VisualTreeAsset tooltipTemplate;

        [SerializeField] private ArmyUI armyUi;
        [SerializeField] private DiplomacyUI diplomacyUi;
        [SerializeField] private BattlesUI battlesUi;
        [SerializeField] private CountryDetailsUI countryDetailsUi;
        [SerializeField] private MissionsUI missionsUi;
        [SerializeField] private SaveUI saveUi;

        private UIDocument _uiDocument;
        private EventPanel _eventPanel;
        private EventOutcomePanel _eventOutcomePanel;
        private VisualElement _loadingPanel;
        private ResourceListPanel _resourceListPanel;
        private Label _turnLabel;
        private Label _yearLabel;
        private Label _monthLabel;
        private Button _finishTurnButton;

        private Button _countryButton;
        private CountryPanel _countryPanel;
        private Label _countryButtonTooltip;

        private TaskCompletionSource<bool> _finishTurnTcs = null;

        private void Awake()
        {
            Main = this;

            _uiDocument = GetComponent<UIDocument>();

            _eventPanel = new EventPanel(_uiDocument.rootVisualElement.Q("EventPanel"), optionButtonTemplate);
            _eventOutcomePanel = new EventOutcomePanel(_uiDocument.rootVisualElement.Q("EventOutcomePanel"));

            _loadingPanel = _uiDocument.rootVisualElement.Q("LoadingPanel");
            _turnLabel = _uiDocument.rootVisualElement.Q<Label>("TurnLabel");
            _yearLabel = _uiDocument.rootVisualElement.Q<Label>("YearLabel");
            _monthLabel = _uiDocument.rootVisualElement.Q<Label>("MonthLabel");
            _countryButton = _uiDocument.rootVisualElement.Q<Button>("CountryButton");
            _resourceListPanel = new ResourceListPanel(_uiDocument.rootVisualElement.Q("ResourceList"), tooltipTemplate);

            _finishTurnButton = _uiDocument.rootVisualElement.Q<Button>("FinishTurnButton");
            _finishTurnButton.RegisterCallback<ClickEvent>(_ => FinishTurn());

            _countryPanel = new(_uiDocument.rootVisualElement.Q("CountryPanel"));
            _countryPanel.SetEnabled(false);
            _countryButton.RegisterCallback<ClickEvent>(_ => OnCountryButtonClicked());

            // Setup tooltip for country button
            if (tooltipTemplate != null)
            {
                var tooltipContainer = tooltipTemplate.Instantiate();
                _countryButtonTooltip = tooltipContainer.Q<Label>("Tooltip");
                if (_countryButtonTooltip != null)
                {
                    _countryButtonTooltip.style.display = DisplayStyle.None;
                    _countryButtonTooltip.pickingMode = PickingMode.Ignore;
                    _uiDocument.rootVisualElement.Add(_countryButtonTooltip);

                    _countryButton.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        _countryButtonTooltip.text = "Country Menu";
                        _countryButtonTooltip.style.display = DisplayStyle.Flex;
                        _countryButtonTooltip.BringToFront();
                        var bounds = _countryButton.worldBound;
                        _countryButtonTooltip.style.left = bounds.xMin;
                        _countryButtonTooltip.style.top = bounds.yMax + 5;
                    });
                    _countryButton.RegisterCallback<PointerLeaveEvent>(_ =>
                    {
                        _countryButtonTooltip.style.display = DisplayStyle.None;
                    });
                }
            }

            RegisterCountryPanelEvents();

            _eventPanel.GetPanel().visible = false;
            _eventOutcomePanel.GetPanel().visible = false;
            _loadingPanel.visible = false;
        }

        private void RegisterCountryPanelEvents()
        {
            _countryPanel.onClose.AddListener(() =>
            {
                _countryPanel.SetEnabled(false);
                SetFinishTurnButtonEnabled(true);
            });

            _countryPanel.onArmy.AddListener(() =>
            {
                armyUi.SetEnabled(true);
            });

            _countryPanel.onDiplomacy.AddListener(() =>
            {
                diplomacyUi.SetEnabled(true);
            });

            _countryPanel.onBattles.AddListener(() =>
            {
                battlesUi.SetEnabled(true);
            });

            _countryPanel.onCountryDetails.AddListener(() =>
            {
                countryDetailsUi.SetEnabled(true);
            });

            _countryPanel.onMissions.AddListener(() =>
            {
                missionsUi.SetEnabled(true);
            });

            _countryPanel.onSave.AddListener(() =>
            {
                saveUi.SetEnabled(true);
            });
        }

        private void OnCountryButtonClicked()
        {
            _countryPanel.SetEnabled(true);
            SetFinishTurnButtonEnabled(false);
        }

        private void Start()
        {
            GameService.Main.State.Subscribe(newState =>
            {
                if (newState == null) return;

                try
                {
                    var country = newState.Country[newState.Game.PlayerCountryId];

                    _turnLabel.text = $"TURN {newState.Game.Turn}";
                    _yearLabel.text = $"{Mathf.Abs(newState.Game.CurrentYear)} BCE";
                    _monthLabel.text = $"Month: {newState.Game.CurrentMonth}";
                    _resourceListPanel.UpdateResources(
                        country.Treasury,
                        country.Manpower,
                        country.Stability,
                        country.Efficiency,
                        country.Prestige
                    );
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("Error in subscription lambda: " + ex);
                }
            });

            GameService.Main.currentPhase.Subscribe(p =>
            {
                _countryButton.SetEnabled(p == GameService.GamePhase.PlayerAction);
            });
        }

        public Task<EventChoice> DisplayEventAsync(GameEvent gameEvent)
        {
            return _eventPanel.DisplayEventAsync(gameEvent);
        }

        public Task DisplayEventOutcomeAsync(EventOutcome outcome)
        {
            return _eventOutcomePanel.DisplayEventOutcomeAsync(outcome);
        }

        public void SetLoading(bool isLoading)
        {
            _loadingPanel.visible = isLoading;
        }

        public void SetFinishTurnButtonEnabled(bool buttonEnabled)
        {
            _finishTurnButton.SetEnabled(buttonEnabled);
        }

        public void SetPlayerActionsEnabled(bool playerActionsEnabled)
        {
            // _armyButton.SetEnabled(playerActionsEnabled);
            _countryButton.SetEnabled(playerActionsEnabled);
        }

        public Task WaitForFinishTurnAsync()
        {
            _finishTurnTcs?.TrySetCanceled();
            _finishTurnTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _finishTurnTcs.Task;
        }

        private void FinishTurn()
        {
            if (_finishTurnTcs == null) return;

            _finishTurnTcs?.TrySetResult(true);
            _finishTurnTcs = null;
        }
    }
}
