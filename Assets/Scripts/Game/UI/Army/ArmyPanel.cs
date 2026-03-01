using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Army
{
    public class ArmyPanel
    {
        private VisualElement _panel;
        private Button _createButton;
        private Button _mergeButton;
        private Button _closeButton;

        public UnityEvent onCreate = new();
        public UnityEvent onMerge = new();
        public UnityEvent onClose = new();

        public ArmyPanel(VisualElement panel)
        {
            _panel = panel;
            _createButton = panel.Q<Button>("CreateButton");
            _mergeButton = panel.Q<Button>("MergeButton");
            _closeButton = panel.Q<Button>("CloseButton");

            RegisterButton(_createButton, onCreate);
            RegisterButton(_closeButton, onClose);
            RegisterButton(_mergeButton, onMerge);

        }

        private void RegisterButton(Button button, UnityEvent evt)
        {
            button.RegisterCallback<ClickEvent>(_ => evt.Invoke());
        }

        public void SetAllButtonsEnabled(bool enabled)
        {
            _createButton.SetEnabled(enabled);
            _mergeButton.SetEnabled(enabled);
            _closeButton.SetEnabled(enabled);
        }

        public void SetEnabled(bool enabled)
        {
            _panel.visible = enabled;
        }
    }
}

