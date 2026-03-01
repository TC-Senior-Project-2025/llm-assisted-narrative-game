using Game.Services.Commands;
using Game.Services.Saves;
using Game.UI.Interfaces;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class MergeUnitPanel : IBasePanel
    {
        private VisualElement _panel;
        public UnityEvent onClose { get; private set; } = new();

        private Label _sourceName;
        private Label _targetName;
        private Button _selectSourceButton;
        private Button _selectTargetButton;
        private Button _deselectSourceButton;
        private Button _deselectTargetButton;
        private Button _mergeButton;

        public UnityEvent onSelectSource = new();
        public UnityEvent onSelectTarget = new();

        private int _selectedSourceId = -1;
        private int _selectedTargetId = -1;
        public int GetSourceId() => _selectedSourceId;
        public int GetTargetId() => _selectedTargetId;

        public MergeUnitPanel(VisualElement panel)
        {
            _panel = panel;
            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => OnClose());

            _sourceName = _panel.Q<Label>("SourceName");
            _targetName = _panel.Q<Label>("TargetName");
            _selectSourceButton = _panel.Q<Button>("SelectSourceButton");
            _selectTargetButton = _panel.Q<Button>("SelectTargetButton");
            _deselectSourceButton = _panel.Q<Button>("DeselectSourceButton");
            _deselectTargetButton = _panel.Q<Button>("DeselectTargetButton");
            _mergeButton = _panel.Q<Button>("MergeButton");

            _selectSourceButton.RegisterCallback<ClickEvent>(_ => onSelectSource.Invoke());
            _selectTargetButton.RegisterCallback<ClickEvent>(_ => onSelectTarget.Invoke());

            _deselectSourceButton.RegisterCallback<ClickEvent>(_ => SetSource(null));
            _deselectTargetButton.RegisterCallback<ClickEvent>(_ => SetTarget(null));

            _mergeButton.RegisterCallback<ClickEvent>(_ => OnMerge());
            DisableMergeButtonIfInvalid();
        }

        private void OnClose()
        {
            SetSource(null);
            SetTarget(null);
            onClose.Invoke();
        }

        private void OnMerge()
        {
            ArmyCommands.MergeArmies(_selectedSourceId, _selectedTargetId);
            SetSource(null);
            SetTarget(null);
        }

        public void SetEnabled(bool enabled)
        {
            _panel.visible = enabled;
        }

        public void SetSource(ArmyData source)
        {
            if (source == null)
            {
                _sourceName.style.color = Color.gray;
                _sourceName.text = "None";
                _selectedSourceId = -1;
            }
            else
            {
                if (source.Id == _selectedTargetId) return;
                _sourceName.style.color = Color.white;
                _sourceName.text = source.Name;
                _selectedSourceId = source.Id;
            }

            DisableMergeButtonIfInvalid();
        }

        public void SetTarget(ArmyData target)
        {
            if (target == null)
            {
                _targetName.style.color = Color.gray;
                _targetName.text = "None";
                _selectedTargetId = -1;
            }
            else
            {
                if (target.Id == _selectedSourceId) return;
                _targetName.style.color = Color.white;
                _targetName.text = target.Name;
                _selectedTargetId = target.Id;
            }

            DisableMergeButtonIfInvalid();
        }

        private void DisableMergeButtonIfInvalid()
        {
            var isEnabled = _selectedSourceId != -1 && _selectedTargetId != -1;
            _mergeButton.SetEnabled(isEnabled);
        }
    }
}