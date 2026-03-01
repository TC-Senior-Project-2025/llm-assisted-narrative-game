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

namespace Game.UI.Diplomacy
{
    public class TradePanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        private readonly VisualElement panel;

        private readonly Label _titleLabel;

        private readonly Label _scoreLabel;
        private readonly Label _evaluationLabel;
        private readonly Button _confirmTradeButton;

        private readonly ReactiveProperty<int> _tradeCountryId = new(-1);

        // Multiple trade item rows per side
        private readonly List<TradeItemRow> _weReceiveRows = new();
        private readonly List<TradeItemRow> _theyReceiveRows = new();

        // Track how many rows are currently visible
        private int _visibleWeReceiveCount = 1;
        private int _visibleTheyReceiveCount = 1;

        // Add buttons
        private readonly Button _addRequestButton;
        private readonly Button _addOfferButton;

        // Reactive trigger to recalculate deal whenever any row changes
        private readonly ReactiveProperty<int> _dealUpdateTrigger = new(0);

        private readonly List<string> _baseTradeOptions = new() { "None", "Gold", "Manpower", "Commandery", "Prestige" };

        private const string acceptClassname = "accept";
        private const string rejectClassname = "reject";

        public TradePanel(VisualElement _panel)
        {
            panel = _panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _titleLabel = _panel.Q<Label>("Title");

            // Initialize all rows for "WeReceive" side by finding direct children with ResourceDropdownField
            var weReceiveContainer = _panel.Q("WeReceive");
            foreach (var child in weReceiveContainer.Children())
            {
                if (child.Q<DropdownField>("ResourceDropdownField") != null)
                {
                    var row = new TradeItemRow(child, isIncoming: true, this);
                    _weReceiveRows.Add(row);
                }
            }
            Debug.Log($"[TradePanel] Found {_weReceiveRows.Count} WeReceive rows");

            // Initialize all rows for "TheyReceive" side
            var theyReceiveContainer = _panel.Q("TheyReceive");
            foreach (var child in theyReceiveContainer.Children())
            {
                if (child.Q<DropdownField>("ResourceDropdownField") != null)
                {
                    var row = new TradeItemRow(child, isIncoming: false, this);
                    _theyReceiveRows.Add(row);
                }
            }
            Debug.Log($"[TradePanel] Found {_theyReceiveRows.Count} TheyReceive rows");

            // Get the Add buttons
            _addRequestButton = weReceiveContainer.Q<Button>("AddRequestButton");
            _addOfferButton = theyReceiveContainer.Q<Button>("AddOfferButton");

            // Register Add button click handlers
            _addRequestButton?.RegisterCallback<ClickEvent>(_ => OnAddRequestRow());
            _addOfferButton?.RegisterCallback<ClickEvent>(_ => OnAddOfferRow());

            _scoreLabel = _panel.Q<Label>("ScoreLabel");
            _evaluationLabel = _panel.Q<Label>("EvaluationLabel");
            _confirmTradeButton = _panel.Q<Button>("ConfirmTradeButton");
            _confirmTradeButton.RegisterCallback<ClickEvent>(_ => OnConfirmTrade());

            // Update dropdown choices when game state or trade country changes
            GameService.Main.State.CombineLatestWith(_tradeCountryId).Subscribe(tuple =>
            {
                var (gameState, tradeCountryId) = tuple;

                var isAtWar = gameState.Relation.Any(
                    r => r.SrcCountryId == gameState.Game.PlayerCountryId && r.DstCountryId == tradeCountryId && r.IsAtWar
                );

                var isAllied = gameState.Relation.Any(
                    r => r.SrcCountryId == gameState.Game.PlayerCountryId && r.DstCountryId == tradeCountryId && r.IsAllied
                );

                var isOurCountryAtWar = gameState.Relation.Any(
                    r => r.SrcCountryId == gameState.Game.PlayerCountryId && r.IsAtWar
                );

                // Build choices for "We Receive" (incoming)
                var weReceiveOptions = new List<string>(_baseTradeOptions);
                if (isAllied && isOurCountryAtWar)
                {
                    weReceiveOptions.Add("Call to arms");
                }

                // Build choices for "They Receive" (outgoing)
                var theyReceiveOptions = new List<string>(_baseTradeOptions);
                if (isAtWar)
                {
                    theyReceiveOptions.Add("Peace treaty");
                }
                if (!isAllied && !isAtWar)
                {
                    theyReceiveOptions.Add("Alliance");
                }

                // Update all row dropdowns
                foreach (var row in _weReceiveRows)
                {
                    row.UpdateChoices(weReceiveOptions);
                }
                foreach (var row in _theyReceiveRows)
                {
                    row.UpdateChoices(theyReceiveOptions);
                }
            });

            // Update title when trade country changes
            _tradeCountryId.Subscribe(countryId =>
            {
                if (countryId < 0)
                {
                    _titleLabel.text = "Trade";
                    return;
                }

                var country = GameService.Main.State.CurrentValue.Country[countryId];
                _titleLabel.text = $"Trade with {country.Name}";
            });

            // Subscribe to deal update trigger to recalculate the deal and refresh options
            _dealUpdateTrigger.Subscribe(_ =>
            {
                RefreshOptions();
                RecalculateDeal();
            });

            // Initialize row visibility
            UpdateRowVisibility();

            // Initial options refresh
            RefreshOptions();
        }

        private void RefreshOptions()
        {
            // --- We Receive Side ---
            var weReceiveResources = _weReceiveRows.Select(r => r.Resource).Where(r => !string.IsNullOrEmpty(r) && r != "None" && r != "Commandery").ToHashSet();
            var weReceiveCommanderies = _weReceiveRows.Select(r => r.CommanderyId).Where(id => id >= 0).ToHashSet();

            // Base options for We Receive
            var weReceiveBaseOptions = new List<string>(_baseTradeOptions);
            var (gameState, tradeCountryId) = (GameService.Main.State.CurrentValue, _tradeCountryId.CurrentValue);

            var isAllied = gameState.Relation.Any(r => r.SrcCountryId == gameState.Game.PlayerCountryId && r.DstCountryId == tradeCountryId && r.IsAllied);
            var isOurCountryAtWar = gameState.Relation.Any(r => r.SrcCountryId == gameState.Game.PlayerCountryId && r.IsAtWar);

            if (isAllied && isOurCountryAtWar) weReceiveBaseOptions.Add("Call to arms");

            foreach (var row in _weReceiveRows)
            {
                // Filter resources: Allow current selection, "None", "Commandery", and anything not used elsewhere
                var currentRes = row.Resource;
                var filteredOptions = weReceiveBaseOptions.Where(o =>
                    o == "None" ||
                    o == "Commandery" ||
                    (o == currentRes) ||
                    !weReceiveResources.Contains(o)
                ).ToList();
                row.UpdateChoices(filteredOptions);

                // Filter commanderies if this row is selecting a commandery
                if (currentRes == "Commandery")
                {
                    row.RefreshCommanderyChoices(weReceiveCommanderies);
                }
            }

            // --- They Receive Side ---
            var theyReceiveResources = _theyReceiveRows.Select(r => r.Resource).Where(r => !string.IsNullOrEmpty(r) && r != "None" && r != "Commandery").ToHashSet();
            var theyReceiveCommanderies = _theyReceiveRows.Select(r => r.CommanderyId).Where(id => id >= 0).ToHashSet();

            // Base options for They Receive
            var theyReceiveBaseOptions = new List<string>(_baseTradeOptions);
            var isAtWar = gameState.Relation.Any(r => r.SrcCountryId == gameState.Game.PlayerCountryId && r.DstCountryId == tradeCountryId && r.IsAtWar);

            if (isAtWar) theyReceiveBaseOptions.Add("Peace treaty");
            if (!isAllied && !isAtWar) theyReceiveBaseOptions.Add("Alliance");

            foreach (var row in _theyReceiveRows)
            {
                var currentRes = row.Resource;
                var filteredOptions = theyReceiveBaseOptions.Where(o =>
                    o == "None" ||
                    o == "Commandery" ||
                    (o == currentRes) ||
                    !theyReceiveResources.Contains(o)
                ).ToList();
                row.UpdateChoices(filteredOptions);

                if (currentRes == "Commandery")
                {
                    row.RefreshCommanderyChoices(theyReceiveCommanderies);
                }
            }
        }

        private void OnAddRequestRow()
        {
            if (_visibleWeReceiveCount < _weReceiveRows.Count)
            {
                _visibleWeReceiveCount++;
                UpdateRowVisibility();
                RefreshOptions();
            }
        }

        private void OnAddOfferRow()
        {
            if (_visibleTheyReceiveCount < _theyReceiveRows.Count)
            {
                _visibleTheyReceiveCount++;
                UpdateRowVisibility();
                RefreshOptions();
            }
        }

        private void UpdateRowVisibility()
        {
            // Show/hide WeReceive rows based on visible count
            for (int i = 0; i < _weReceiveRows.Count; i++)
            {
                _weReceiveRows[i].SetVisible(i < _visibleWeReceiveCount);
            }

            // Show/hide TheyReceive rows based on visible count
            for (int i = 0; i < _theyReceiveRows.Count; i++)
            {
                _theyReceiveRows[i].SetVisible(i < _visibleTheyReceiveCount);
            }

            // Hide Add button if all rows are visible
            if (_addRequestButton != null)
            {
                _addRequestButton.style.display = _visibleWeReceiveCount >= _weReceiveRows.Count
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }

            if (_addOfferButton != null)
            {
                _addOfferButton.style.display = _visibleTheyReceiveCount >= _theyReceiveRows.Count
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
            }
        }

        public void TriggerDealUpdate()
        {
            _dealUpdateTrigger.Value++;
        }

        private void RecalculateDeal()
        {
            var isConfirmTradeButtonInteractable = true;

            // Collect all valid items from VISIBLE rows on both sides
            var incomingItems = _weReceiveRows
                .Take(_visibleWeReceiveCount)
                .Where(r => r.IsValid())
                .Select(r => r.GetDealItem())
                .ToList();

            var outgoingItems = _theyReceiveRows
                .Take(_visibleTheyReceiveCount)
                .Where(r => r.IsValid())
                .Select(r => r.GetDealItem())
                .ToList();

            // Must have at least one valid item total (one-sided deals are allowed)
            if (!incomingItems.Any() && !outgoingItems.Any())
            {
                isConfirmTradeButtonInteractable = false;
                _scoreLabel.text = "0";
                _evaluationLabel.text = "-";
                StyleLabelBasedOnScore(0, _scoreLabel);
                StyleLabelBasedOnScore(0, _evaluationLabel);
            }
            else
            {
                // Validate all incoming items have sufficient resources from the other country
                foreach (var row in _weReceiveRows.Take(_visibleWeReceiveCount).Where(r => r.IsValid()))
                {
                    if (!HasSufficientResources(_tradeCountryId.CurrentValue, row.Resource, row.Amount, row.CommanderyId))
                    {
                        isConfirmTradeButtonInteractable = false;
                        break;
                    }
                }

                // Validate all outgoing items have sufficient resources from player
                if (isConfirmTradeButtonInteractable)
                {
                    foreach (var row in _theyReceiveRows.Take(_visibleTheyReceiveCount).Where(r => r.IsValid()))
                    {
                        if (!HasSufficientResources(GameService.Main.PlayerCountry.Id, row.Resource, row.Amount, row.CommanderyId))
                        {
                            isConfirmTradeButtonInteractable = false;
                            break;
                        }
                    }
                }

                // Always evaluate and show score when there are any items
                var deal = BuildDeal();
                var score = DiplomacyService.Main.EvaluateDeal(deal, out string reason);

                _scoreLabel.text = $"{score}";
                if (score < -50)
                {
                    _evaluationLabel.text = "Insulting";
                    _evaluationLabel.style.color = Color.red;
                }
                else if (score < -5)
                {
                    _evaluationLabel.text = "Unacceptable";
                    _evaluationLabel.style.color = Color.orangeRed;
                }
                else if (score <= 5)
                {
                    _evaluationLabel.text = "Unimpressed";
                    _evaluationLabel.style.color = Color.orange;
                }
                else if (score <= 50)
                {
                    _evaluationLabel.text = "Satisfying";
                    _evaluationLabel.style.color = Color.yellowGreen;
                }
                else
                {
                    _evaluationLabel.text = "Flattering";
                    _evaluationLabel.style.color = Color.green;
                }

                StyleLabelBasedOnScore(score, _scoreLabel);
                StyleLabelBasedOnScore(score, _evaluationLabel);

                // Deal button is enabled even if score <= 0
                // Backend will reject unfair deals
            }

            _confirmTradeButton.SetEnabled(isConfirmTradeButtonInteractable);
        }

        private bool HasSufficientResources(int countryId, string resourceType, int amount, int commanderyId)
        {
            if (countryId < 0 || string.IsNullOrEmpty(resourceType) || resourceType == "None")
                return false;

            return resourceType switch
            {
                "Gold" => GetCountryGold(countryId) >= amount,
                "Manpower" => GetCountryManpower(countryId) >= amount,
                "Prestige" => GetCountryPrestige(countryId) >= amount,
                "Commandery" => commanderyId >= 0 && CommanderyBelongsToCountry(commanderyId, countryId),
                "Alliance" => true,
                "Peace treaty" => true,
                "Call to arms" => true,
                _ => false
            };
        }

        public int GetMaxResourceAmount(string resourceType, int countryId)
        {
            if (string.IsNullOrEmpty(resourceType) || countryId < 0)
                return 0;

            return resourceType switch
            {
                "Gold" => GetCountryGold(countryId),
                "Manpower" => GetCountryManpower(countryId),
                "Prestige" => Math.Min(GetCountryPrestige(countryId), 10), // Capped at 10
                "Alliance" => 1,
                "Call to arms" => 1,
                "Peace treaty" => 1,
                _ => int.MaxValue
            };
        }

        private bool CommanderyBelongsToCountry(int commanderyId, int countryId)
        {
            if (commanderyId < 0) return false;

            var commandery = GameService.Main.State.CurrentValue.Commandery.Values
                .FirstOrDefault(c => c.Id == commanderyId);

            return commandery != null && commandery.CountryId == countryId;
        }

        private int GetCountryManpower(int countryId)
        {
            return GameService.Main.State.CurrentValue.Country[countryId].Manpower;
        }

        private int GetCountryGold(int countryId)
        {
            return GameService.Main.State.CurrentValue.Country[countryId].Treasury;
        }

        private int GetCountryPrestige(int countryId)
        {
            return GameService.Main.State.CurrentValue.Country[countryId].Prestige;
        }

        private void StyleLabelBasedOnScore(int score, Label label)
        {
            if (score > 0)
            {
                if (label.ClassListContains(rejectClassname))
                {
                    label.RemoveFromClassList(rejectClassname);
                }

                if (!label.ClassListContains(acceptClassname))
                {
                    label.AddToClassList(acceptClassname);
                }
            }
            else
            {
                if (label.ClassListContains(acceptClassname))
                {
                    label.RemoveFromClassList(acceptClassname);
                }

                if (!label.ClassListContains(rejectClassname))
                {
                    label.AddToClassList(rejectClassname);
                }
            }
        }

        public (List<string> choices, Dictionary<string, int> idMap) GetCommanderyChoicesWithIds(int countryId)
        {
            var commanderies = GameService.Main.State.CurrentValue.Commandery.Values
                .Where(c => c.CountryId == countryId)
                .ToList();

            var choices = commanderies.Select(c => c.Name).ToList();
            var idMap = commanderies.ToDictionary(c => c.Name, c => c.Id);

            return (choices, idMap);
        }

        public (List<string> choices, Dictionary<string, int> idMap) GetEnemyCountryChoicesWithIds(int countryId)
        {
            var playerEnemyCountries = DiplomacyService.Main.GetEnemyCountries(countryId);
            var targetEnemyCountries = DiplomacyService.Main.GetEnemyCountries(_tradeCountryId.CurrentValue);

            // Get IDs of countries the target is already at war with
            var targetEnemyIds = new HashSet<int>(targetEnemyCountries.Select(c => c.Id));

            // Filter out enemies that the target country is already fighting
            var availableEnemies = playerEnemyCountries
                .Where(c => !targetEnemyIds.Contains(c.Id))
                .ToList();

            var choices = availableEnemies.Select(c => c.Name).ToList();
            var idMap = availableEnemies.ToDictionary(c => c.Name, c => c.Id);

            return (choices, idMap);
        }

        private DiplomacyService.DealProposal BuildDeal()
        {
            var playerCountry = GameService.Main.PlayerCountry;
            var targetCountry = GameService.Main.State.CurrentValue.Country[_tradeCountryId.CurrentValue];

            var deal = new DiplomacyService.DealProposal(playerCountry, targetCountry);

            // Add all valid incoming items from VISIBLE rows (requests from other country)
            foreach (var row in _weReceiveRows.Take(_visibleWeReceiveCount).Where(r => r.IsValid()))
            {
                var item = row.GetDealItem();
                deal.AddItem(false, item.Type, item.Value);
            }

            // Add all valid outgoing items from VISIBLE rows (offers from player)
            foreach (var row in _theyReceiveRows.Take(_visibleTheyReceiveCount).Where(r => r.IsValid()))
            {
                var item = row.GetDealItem();
                deal.AddItem(true, item.Type, item.Value);
            }

            return deal;
        }

        private void OnConfirmTrade()
        {
            var deal = BuildDeal();
            if (DiplomacyService.Main.TryAcceptDeal(deal, out string rejectionReason))
            {
                GameService.Main.State.ApplyInnerMutations();
                OnClose();
            }
            else
            {
                Debug.Log($"Deal rejected: {rejectionReason}");
                _evaluationLabel.text = "Refused!";
                StyleLabelBasedOnScore(0, _evaluationLabel);
            }
        }

        private void OnClose()
        {
            ResetValues();
            onClose.Invoke();
        }

        private void ResetValues()
        {
            _tradeCountryId.Value = -1;

            // Reset all rows
            foreach (var row in _weReceiveRows)
            {
                row.Reset();
            }
            foreach (var row in _theyReceiveRows)
            {
                row.Reset();
            }

            // Reset visibility to just the first row
            _visibleWeReceiveCount = 1;
            _visibleTheyReceiveCount = 1;
            UpdateRowVisibility();

            _scoreLabel.text = "0";
            _evaluationLabel.text = "-";
        }

        public void SetEnabled(bool isEnabled)
        {
            panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetTradeCountryId(int countryId)
        {
            _tradeCountryId.Value = countryId;
        }

        public int TradeCountryId => _tradeCountryId.CurrentValue;
    }

    /// <summary>
    /// Represents a single row of trade item selection (dropdown + commandery/war dropdown + amount field)
    /// </summary>
    public class TradeItemRow
    {
        private readonly VisualElement _container;
        private readonly DropdownField _resourceDropdown;
        private readonly DropdownField _commanderyDropdown;
        private readonly DropdownField _warDropdown;
        private readonly IntegerField _amountField;
        private readonly TradePanel _parent;
        private readonly bool _isIncoming;

        private Dictionary<string, int> _commanderyMap = new();
        private Dictionary<string, int> _warMap = new();

        public string Resource => _resourceDropdown?.value;
        public int Amount { get; private set; }
        public int CommanderyId { get; private set; } = -1;
        public int WarTargetId { get; private set; } = -1;

        public TradeItemRow(VisualElement container, bool isIncoming, TradePanel parent)
        {
            _container = container;
            _parent = parent;
            _isIncoming = isIncoming;

            _resourceDropdown = container.Q<DropdownField>("ResourceDropdownField");
            _commanderyDropdown = container.Q<DropdownField>("CommanderyDropdownField");
            _warDropdown = container.Q<DropdownField>("WarDropdownField");
            _amountField = container.Q<IntegerField>();

            // Initialize visibility
            if (_commanderyDropdown != null) _commanderyDropdown.style.display = DisplayStyle.None;
            if (_warDropdown != null) _warDropdown.style.display = DisplayStyle.None;

            // Register callbacks
            _resourceDropdown?.RegisterValueChangedCallback(OnResourceChanged);
            _commanderyDropdown?.RegisterValueChangedCallback(OnCommanderyChanged);
            _warDropdown?.RegisterValueChangedCallback(OnWarTargetChanged);
            _amountField?.RegisterValueChangedCallback(OnAmountChanged);
        }

        public void SetVisible(bool visible)
        {
            _container.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void UpdateChoices(List<string> choices)
        {
            if (_resourceDropdown != null)
            {
                var currentValue = _resourceDropdown.value;
                _resourceDropdown.choices = choices;

                // If current value is no longer valid, reset (unless it's null/empty which is fine)
                if (!string.IsNullOrEmpty(currentValue) && !choices.Contains(currentValue))
                {
                    _resourceDropdown.value = choices.FirstOrDefault(); // Usually "None"
                }
            }
        }

        public void RefreshCommanderyChoices(HashSet<int> allSelectedIds)
        {
            if (_commanderyDropdown == null || Resource != "Commandery") return;

            int countryId = _isIncoming ? _parent.TradeCountryId : GameService.Main.PlayerCountry.Id;
            var (choices, idMap) = _parent.GetCommanderyChoicesWithIds(countryId);

            // Filter choices: Allow if not selected elsewhere, OR if it's the current selection of this row
            var filteredChoices = new List<string>();
            var filteredMap = new Dictionary<string, int>();

            foreach (var kvp in idMap)
            {
                // If it's NOT in the selected list, OR it IS the one currently selected by THIS row
                // (We check against CommanderyId because that's our current internal state)
                if (!allSelectedIds.Contains(kvp.Value) || kvp.Value == CommanderyId)
                {
                    filteredChoices.Add(kvp.Key);
                    filteredMap.Add(kvp.Key, kvp.Value);
                }
            }

            _commanderyDropdown.choices = filteredChoices;
            _commanderyMap = filteredMap;

            // Validate current selection
            var currentVal = _commanderyDropdown.value;
            if (!string.IsNullOrEmpty(currentVal) && !filteredChoices.Contains(currentVal))
            {
                _commanderyDropdown.value = null; // or first available
                CommanderyId = -1;
            }
        }

        private void OnResourceChanged(ChangeEvent<string> evt)
        {
            Debug.Log($"Resource changed: {evt.newValue}");

            // Reset secondary fields
            CommanderyId = -1;
            WarTargetId = -1;
            Amount = 0;

            if (evt.newValue == "Call to arms" && _isIncoming)
            {
                // Show war dropdown, hide others
                if (_commanderyDropdown != null) _commanderyDropdown.style.display = DisplayStyle.None;
                if (_amountField != null) _amountField.style.display = DisplayStyle.None;
                if (_warDropdown != null)
                {
                    _warDropdown.style.display = DisplayStyle.Flex;
                    var (choices, idMap) = _parent.GetEnemyCountryChoicesWithIds(GameService.Main.PlayerCountry.Id);
                    _warDropdown.choices = choices;
                    _warMap = idMap;
                }
            }
            else if (evt.newValue == "Commandery")
            {
                // Show commandery dropdown, hide others
                if (_warDropdown != null) _warDropdown.style.display = DisplayStyle.None;
                if (_amountField != null) _amountField.style.display = DisplayStyle.None;
                if (_commanderyDropdown != null)
                {
                    _commanderyDropdown.style.display = DisplayStyle.Flex;
                    // Initial load - full list. It will be filtered by RefreshOptions shortly after trigger.
                    int countryId = _isIncoming ? _parent.TradeCountryId : GameService.Main.PlayerCountry.Id;
                    var (choices, idMap) = _parent.GetCommanderyChoicesWithIds(countryId);
                    _commanderyDropdown.choices = choices;
                    _commanderyMap = idMap;
                }
            }
            else if (evt.newValue == "Alliance" || evt.newValue == "Peace treaty")
            {
                // No secondary input needed
                if (_commanderyDropdown != null) _commanderyDropdown.style.display = DisplayStyle.None;
                if (_warDropdown != null) _warDropdown.style.display = DisplayStyle.None;
                if (_amountField != null) _amountField.style.display = DisplayStyle.None;
                Amount = 1; // Dummy value for validation
            }
            else if (evt.newValue == "None" || string.IsNullOrEmpty(evt.newValue))
            {
                // Hide all secondary inputs
                if (_commanderyDropdown != null) _commanderyDropdown.style.display = DisplayStyle.None;
                if (_warDropdown != null) _warDropdown.style.display = DisplayStyle.None;
                if (_amountField != null) _amountField.style.display = DisplayStyle.None;
            }
            else
            {
                // Standard resource: show amount field
                if (_commanderyDropdown != null) _commanderyDropdown.style.display = DisplayStyle.None;
                if (_warDropdown != null) _warDropdown.style.display = DisplayStyle.None;
                if (_amountField != null)
                {
                    _amountField.style.display = DisplayStyle.Flex;
                    _amountField.value = 0;
                }
            }

            _parent.TriggerDealUpdate();
        }

        private void OnCommanderyChanged(ChangeEvent<string> evt)
        {
            if (_commanderyMap.TryGetValue(evt.newValue, out int commanderyId))
            {
                CommanderyId = commanderyId;
                Amount = 1; // For validation purposes
            }
            _parent.TriggerDealUpdate();
        }

        private void OnWarTargetChanged(ChangeEvent<string> evt)
        {
            if (_warMap.TryGetValue(evt.newValue, out int warTargetId))
            {
                WarTargetId = warTargetId;
                Amount = 1; // For validation purposes
            }
            _parent.TriggerDealUpdate();
        }

        private void OnAmountChanged(ChangeEvent<int> evt)
        {
            int countryId = _isIncoming ? _parent.TradeCountryId : GameService.Main.PlayerCountry.Id;
            var maxAmount = _parent.GetMaxResourceAmount(Resource, countryId);
            maxAmount = Math.Max(0, maxAmount);
            Amount = Math.Clamp(evt.newValue, 0, maxAmount);

            if (evt.newValue > maxAmount)
            {
                _amountField?.SetValueWithoutNotify(maxAmount);
            }
            else if (evt.newValue < 0)
            {
                _amountField?.SetValueWithoutNotify(0);
            }

            _parent.TriggerDealUpdate();
        }

        public bool IsValid()
        {
            if (string.IsNullOrEmpty(Resource) || Resource == "None")
                return false;

            if (Resource == "Commandery")
                return CommanderyId >= 0;

            if (Resource == "Call to arms")
                return WarTargetId >= 0;

            if (Resource == "Alliance" || Resource == "Peace treaty")
                return true;

            // Standard resource: need amount > 0
            return Amount > 0;
        }

        public DiplomacyService.DealItem GetDealItem()
        {
            var type = Resource switch
            {
                "Gold" => DiplomacyService.DealItemType.Gold,
                "Manpower" => DiplomacyService.DealItemType.Manpower,
                "Prestige" => DiplomacyService.DealItemType.Prestige,
                "Commandery" => DiplomacyService.DealItemType.Commandery,
                "Call to arms" => DiplomacyService.DealItemType.CallToArms,
                "Alliance" => DiplomacyService.DealItemType.Alliance,
                "Peace treaty" => DiplomacyService.DealItemType.Peace,
                _ => throw new Exception($"Unknown resource type: {Resource}")
            };

            int value = type switch
            {
                DiplomacyService.DealItemType.Commandery => CommanderyId,
                DiplomacyService.DealItemType.CallToArms => WarTargetId,
                DiplomacyService.DealItemType.Alliance => 1,
                DiplomacyService.DealItemType.Peace => 1,
                _ => Amount
            };

            return new DiplomacyService.DealItem { Type = type, Value = value };
        }

        public void Reset()
        {
            _resourceDropdown?.SetValueWithoutNotify(null);
            _commanderyDropdown?.SetValueWithoutNotify(null);
            _warDropdown?.SetValueWithoutNotify(null);
            _amountField?.SetValueWithoutNotify(0);

            if (_commanderyDropdown != null) _commanderyDropdown.style.display = DisplayStyle.None;
            if (_warDropdown != null) _warDropdown.style.display = DisplayStyle.None;
            if (_amountField != null) _amountField.style.display = DisplayStyle.Flex;

            CommanderyId = -1;
            WarTargetId = -1;
            Amount = 0;
        }
    }
}