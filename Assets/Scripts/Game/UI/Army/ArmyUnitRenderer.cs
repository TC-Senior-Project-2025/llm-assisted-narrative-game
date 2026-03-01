using System.Collections.Generic;
using System.Linq;
using Extensions;
using Game.Services;
using Game.Services.Saves;
using Game.World.Map;
using R3;
using UnityEngine;
using UnityEngine.UIElements;
using Game.Services.Commands;
using UnityEngine.Events;
using Game.Services.Sounds;

namespace Game.UI.Army
{
    public class ArmyUnitRenderer : MonoBehaviour
    {
        public static ArmyUnitRenderer Main { get; private set; }

        public UnityEvent<int> unitClicked = new();
        [SerializeField] private VisualTreeAsset armyUnitTemplate;
        [SerializeField] private GameObject arrowPrefab;

        private ArmyUI _armyUI;
        private Dictionary<int, ArmyData> _units = new();
        private Camera _camera;
        private MoveUnitArrowRenderer _arrowRenderer;
        private UIDocument _uiDocument;
        private VisualElement _container;
        private readonly Dictionary<int, ArmyUnitPanel> _spawnedUnits = new();
        private readonly ReactiveProperty<int> _selectedUnitId = new(-1);
        private ReactiveProperty<SaveData> GameState => GameService.Main.State;

        private HashSet<int> _enemyCountryIds;
        private HashSet<int> _allyCountryIds;

        void Awake()
        {
            Main = this;
        }

        void Start()
        {
            _uiDocument = GetComponent<UIDocument>();

            _armyUI = GetComponent<ArmyUI>();
            _arrowRenderer = new(transform, arrowPrefab);

            _camera = Camera.main;
            _container = _uiDocument.rootVisualElement.Q("Container");

            GameMap.Main.Picker.provinceClicked.AddListener(OnProvinceClicked);
            GameMap.Main.Picker.hoverChanged.AddListener(OnProvinceHovered);
            GameMap.Main.Picker.provinceRightClicked.AddListener(OnProvinceRightClicked);

            // Select merge source
            _armyUI.mergeUnitPanel.onSelectSource.AddListener(() =>
            {
                var unit = _units.GetValueOrDefault(_selectedUnitId.CurrentValue);
                if (unit == null) return;
                if (unit.CountryId != GameState.CurrentValue.Game.PlayerCountryId) return;

                _armyUI.mergeUnitPanel.SetSource(unit);
            });

            // Select merge target
            _armyUI.mergeUnitPanel.onSelectTarget.AddListener(() =>
            {
                var unit = _units.GetValueOrDefault(_selectedUnitId.CurrentValue);
                if (unit == null) return;
                if (unit.CountryId != GameState.CurrentValue.Game.PlayerCountryId) return;

                _armyUI.mergeUnitPanel.SetTarget(unit);
            });

            GameState.Subscribe(gameState =>
            {
                _units = gameState.Army.Where(u =>
                    {
                        var isAllied = u.CountryId == gameState.Game.PlayerCountryId;
                        var isNeighbor = GameMap.Main.FogOfWar.GetFogOfWarValue(u.LocationId)
                            != MapFogOfWar.VisibilityState.None;
                        return isAllied || isNeighbor;
                    }).ToDictionary(u => u.Id, u => u);


                _enemyCountryIds = new HashSet<int>(
                    GameService.Main.State.CurrentValue.Relation
                        .Where(r => r.IsAtWar && r.DstCountryId == gameState.Game.PlayerCountryId)
                        .Select(r => r.SrcCountryId)
                );

                _allyCountryIds = new HashSet<int>(
                    GameService.Main.State.CurrentValue.Relation
                        .Where(r => r.IsAllied && r.DstCountryId == gameState.Game.PlayerCountryId)
                        .Select(r => r.SrcCountryId)
                );
            });

            _selectedUnitId.Subscribe(newId =>
            {
                if (newId == -1)
                {
                    GameMap.Main.Highlighter.SetHighlightEnabled(false);
                    _arrowRenderer.Clear();
                }
                else
                {
                    if (_armyUI.currentState.CurrentValue == ArmyUI.State.Moving)
                    {
                        GameMap.Main.Highlighter.SetHighlightEnabled(true);

                        var unit = _units.GetValueOrDefault(newId);
                        if (unit == null) return;

                        GameMap.Main.Highlighter.HighlightNeighbors(unit.LocationId);
                    }
                }
            });

            _armyUI.currentState.Subscribe(state =>
            {
                if (state == ArmyUI.State.Closed)
                {
                    _selectedUnitId.Value = -1;
                }

                _container.visible = true;
            });
        }

        private void OnClosed()
        {
            // _detailsPanel.SetEnabled(false);
        }

        void Update()
        {
            var unitsToRemove = new List<int>();
            foreach (var spawnedId in _spawnedUnits.Keys)
            {
                if (!_units.ContainsKey(spawnedId))
                {
                    unitsToRemove.Add(spawnedId);
                }
            }

            foreach (var id in unitsToRemove)
            {
                _container.Remove(_spawnedUnits[id].panel);
                _spawnedUnits.Remove(id);
            }

            var provinceUnitCounts = new Dictionary<int, int>();
            var playerCountryId = GameState.CurrentValue.Game.PlayerCountryId;

            foreach (var (unitId, unit) in _units)
            {
                if (!_spawnedUnits.TryGetValue(unit.Id, out var unitUi))
                {
                    unitUi = new(armyUnitTemplate.Instantiate());
                    _container.Add(unitUi.panel);
                    _spawnedUnits.Add(unit.Id, unitUi);

                    unitUi.onClick.AddListener(() => OnUnitClicked(unit.Id));
                }

                var provinceId = unit.LocationId;
                var centroid = GameMap.Main.Renderer.GetProvinceCenter(provinceId);
                var screenPos = _camera.WorldToScreenPoint(centroid);
                
                // Convert screen position to panel coordinates (handles UI scaling)
                // For X: convert from screen to panel space (top-left origin)
                // For Y: SetPosition uses style.bottom, so we need bottom-origin Y
                var panel = _uiDocument.rootVisualElement.panel;
                var panelPosTop = RuntimePanelUtils.ScreenToPanel(panel,
                    new Vector2(screenPos.x, Screen.height - screenPos.y));
                var panelPosBottom = RuntimePanelUtils.ScreenToPanel(panel,
                    new Vector2(screenPos.x, screenPos.y));

                provinceUnitCounts.TryAdd(provinceId, 0);
                var offset = provinceUnitCounts[provinceId] * 20;
                provinceUnitCounts[provinceId]++;

                unitUi.SetPosition(panelPosTop.x - 50, panelPosBottom.y - 12 + offset);

                ArmyUnitPanel.UnitAlignment alignment;
                if (unit.CountryId == playerCountryId)
                {
                    alignment = ArmyUnitPanel.UnitAlignment.Ours;
                }
                else if (_enemyCountryIds.Contains(unit.CountryId))
                {
                    alignment = ArmyUnitPanel.UnitAlignment.Enemy;
                }
                else if (_allyCountryIds.Contains(unit.CountryId))
                {
                    alignment = ArmyUnitPanel.UnitAlignment.Ally;
                }
                else
                {
                    alignment = ArmyUnitPanel.UnitAlignment.Neutral;
                }

                unitUi.SetUnitSize(unit.Size);
                unitUi.SetMorale(unit.Morale);
                unitUi.SetSupply(unit.Supply);
                unitUi.SetUnitAlignment(alignment);

                if (BattleService.Main.IsUnitInBattle(unit.Id))
                {
                    unitUi.SetInBattle(true);
                }
                else
                {
                    unitUi.SetInBattle(false);
                }

                var isInteractable = true;

                switch (_armyUI.currentState.CurrentValue)
                {
                    case ArmyUI.State.Moving:
                        isInteractable = alignment == ArmyUnitPanel.UnitAlignment.Ours && unit.ActionLeft > 0;
                        break;
                    case ArmyUI.State.Merging:
                        var sourceId = _armyUI.mergeUnitPanel.GetSourceId();
                        var targetId = _armyUI.mergeUnitPanel.GetTargetId();

                        if (alignment != ArmyUnitPanel.UnitAlignment.Ours)
                        {
                            isInteractable = false;
                        }
                        else if (sourceId == unitId || targetId == unitId)
                        {
                            isInteractable = false;
                        }
                        else
                        {
                            var selectedUnit = _units.GetValueOrDefault(sourceId) ?? _units.GetValueOrDefault(targetId);
                            if (selectedUnit != null && selectedUnit.LocationId != unit.LocationId)
                            {
                                isInteractable = false;
                            }
                        }
                        break;
                }

                unitUi.SetInteractable(isInteractable);
            }
        }

        private void OnUnitClicked(int unitId)
        {
            var unit = _units.GetValueOrDefault(unitId);
            if (unit == null) return;

            unitClicked.Invoke(unitId);
            if (unit.CountryId != GameState.CurrentValue.Game.PlayerCountryId)
            {
                _selectedUnitId.Value = -1;
                return;
            }

            // if (_armyUI.currentState.CurrentValue == ArmyUI.State.Closed)
            // {
            //     Services.Sounds.SfxService.Main.Play(Services.Sounds.SfxService.Main.click);
            //     _armyUI.SetEnabled(true);
            //     GameUI.Main.SetFinishTurnButtonEnabled(false);
            // }

            // switch (_armyUI.currentState.CurrentValue)
            // {
            //     case ArmyUI.State.Splitting:
            //         _armyUI.splitUnitPanel.SetSelectedUnit(unit);
            //         break;
            //     case ArmyUI.State.Resupplying:
            //         _armyUI.resupplyUnitPanel.SetSelectedUnit(unit);
            //         break;
            // }

            _selectedUnitId.Value = unitId;
        }

        private void OnProvinceClicked(Color32 provinceColor)
        {
            if (_armyUI.currentState.CurrentValue == ArmyUI.State.Creating) return;

            // _detailsPanel.SetEnabled(false);
            _selectedUnitId.Value = -1;

            // if (_armyUI.currentState.CurrentValue != ArmyUI.State.Closed)
            // {
            //     _armyUI.SetEnabled(false);
            // }
        }

        private void OnProvinceHovered(Color32 provinceColor)
        {
            _arrowRenderer.Clear();

            if (provinceColor.SameAs(Color.clear)) return;
            // if (_armyUI.currentState.CurrentValue != ArmyUI.State.Moving) return;

            var destPos = GameMap.Main.Renderer.GetProvinceCenter(provinceColor);
            var destProvinceId = GameMap.Main.Provider.GetProvinceId(provinceColor);

            if (_selectedUnitId.CurrentValue == -1) return;

            var unit = _units.GetValueOrDefault(_selectedUnitId.CurrentValue);
            if (unit == null) return;

            if (unit.CountryId != GameService.Main.PlayerCountry.Id) return;
            if (unit.ActionLeft == 0) return;

            var sourcePos = GameMap.Main.Renderer.GetProvinceCenter(unit.LocationId);
            if (!GameMap.Main.Connections.IsNeighborOf(unit.LocationId, destProvinceId)) return;

            _arrowRenderer.Draw(sourcePos, destPos);
        }

        private void OnProvinceRightClicked(Color32 provinceColor)
        {
            if (provinceColor.SameAs(Color.clear)) return;

            var unit = _units.GetValueOrDefault(_selectedUnitId.CurrentValue);
            if (unit == null) return;
            if (unit.ActionLeft == 0) return;

            if (GameService.Main.currentPhase.CurrentValue != GameService.GamePhase.PlayerAction)
            {
                return;
            }

            var provinceId = GameMap.Main.Provider.GetProvinceId(provinceColor);
            if (provinceId == unit.LocationId) return;

            SfxService.Main.Play(SfxService.Main.armyUnitMove);
            ArmyCommands.MoveArmy(unit.Id, provinceId);

            _selectedUnitId.Value = -1;
        }
    }
}