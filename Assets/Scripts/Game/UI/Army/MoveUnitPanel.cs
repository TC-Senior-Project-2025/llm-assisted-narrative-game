using Game.UI.Interfaces;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class MoveUnitPanel : IBasePanel
    {
        private VisualElement _panel;
        private Label _movingUnitsLabel;

        public UnityEvent onClose { get; private set; } = new();

        public UnityEvent onResetMovement = new();
        public UnityEvent onUndo = new();
        public UnityEvent onConfirmMove = new();

        private Button _confirmMoveButton;
        private Button _undoMoveButton;

        public MoveUnitPanel(VisualElement panel)
        {
            _panel = panel;
            _movingUnitsLabel = _panel.Q<Label>("MovingUnitsLabel");


            _panel.Q<Button>("CloseButton").RegisterCallback<ClickEvent>(_ => onClose.Invoke());
            _panel.Q<Button>("CancelMove").RegisterCallback<ClickEvent>(_ => onResetMovement.Invoke());

            _confirmMoveButton = _panel.Q<Button>("ConfirmMove");
            _undoMoveButton = _panel.Q<Button>("UndoMove");

            _confirmMoveButton.RegisterCallback<ClickEvent>(_ => onConfirmMove.Invoke());
            _undoMoveButton.RegisterCallback<ClickEvent>(_ => onUndo.Invoke());
        }

        public void SetEnabled(bool enabled)
        {
            _panel.visible = enabled;
        }

        public void SetUndoButtonEnabled(bool enabled)
        {
            _undoMoveButton.SetEnabled(enabled);
        }

        public void SetMovedUnitsCount(int count)
        {
            _movingUnitsLabel.text = $"Moving {count} units";
        }
    }
}

