using System;
using Extensions;
using Game.Services;
using Game.Services.Commands;
using Game.Services.Saves;
using Game.UI.Interfaces;
using R3;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class MergePanel2 : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        private readonly VisualElement _panel;
        private readonly Label _targetName;
        // private readonly Button _deselectTargetButton;
        private readonly Button _mergeButton;

        private readonly ReactiveProperty<int> _sourceUnitId = new(-1);
        private readonly ReactiveProperty<int> _targetUnitId = new(-1);

        public UnityEvent onMerge = new();

        public MergePanel2(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _targetName = _panel.Q<Label>("TargetName");
            _mergeButton = _panel.Q<Button>("MergeButton");

            _targetUnitId.Subscribe(targetId =>
            {
                if (targetId == _sourceUnitId.CurrentValue) return;

                if (targetId == -1)
                {
                    _targetName.text = "None";
                    _targetName.style.color = Color.gray;
                }

                var sourceUnit = GameService.Main.State.CurrentValue.Army.Find(u => u.Id == _sourceUnitId.CurrentValue);
                var targetUnit = GameService.Main.State.CurrentValue.Army.Find(u => u.Id == targetId);

                if (targetUnit == null) return;

                if (targetUnit.LocationId != sourceUnit.LocationId) return;

                _targetName.text = targetUnit.Name;
                _targetName.style.color = Color.white;
            });

            _sourceUnitId.CombineLatestWith(_targetUnitId).Subscribe(tuple =>
            {
                var (sourceId, targetId) = tuple;
                _mergeButton.SetEnabled(sourceId != -1 && targetId != -1);
            });

            _mergeButton.RegisterCallback<ClickEvent>(_ => OnMerge());
        }

        private void OnMerge()
        {
            ArmyCommands.MergeArmies(_sourceUnitId.CurrentValue, _targetUnitId.CurrentValue);
            OnClose();
            onMerge.Invoke();
        }

        private void OnClose()
        {
            onClose.Invoke();
        }

        public void SetEnabled(bool isEnabled)
        {
            _panel.style.display = isEnabled ? DisplayStyle.Flex : DisplayStyle.None;

            if (isEnabled)
            {
                SetTargetUnitId(-1);
            }
        }

        public void SetSourceUnitId(int unitId)
        {
            _sourceUnitId.Value = unitId;
        }

        public void SetTargetUnitId(int unitId)
        {
            _targetUnitId.Value = unitId;
        }

        public bool IsEnabled()
        {
            return _panel.style.display == DisplayStyle.Flex;
        }
    }
}

