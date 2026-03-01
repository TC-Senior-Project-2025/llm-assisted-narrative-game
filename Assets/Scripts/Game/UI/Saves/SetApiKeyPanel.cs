using Game.Services;
using Game.UI.Interfaces;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace Game.UI.Saves
{
    public class SetApiKeyPanel : IBasePanel
    {
        public UnityEvent onClose { get; private set; } = new();
        public UnityEvent<string> onSaved { get; private set; } = new();

        private readonly VisualElement _panel;
        private readonly Button _confirmButton;
        private readonly TextField _apiKeyField;

        public SetApiKeyPanel(VisualElement panel)
        {
            _panel = panel;

            _panel.Q<Button>("CloseButton")
                .RegisterCallback<ClickEvent>(_ => OnClose());

            _apiKeyField = _panel.Q<TextField>("ApiKeyField");
            _confirmButton = _panel.Q<Button>("ConfirmButton");

            _confirmButton.RegisterCallback<ClickEvent>(_ => SaveApiKey());

            _apiKeyField.RegisterValueChangedCallback(_ => RefreshConfirmState());
            RefreshConfirmState();
        }

        private void SaveApiKey()
        {
            var key = _apiKeyField.value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(key)) return;

            UserSettingsStore.SetApiKey(key);

            onSaved.Invoke(key);
            OnClose();
        }

        private void RefreshConfirmState()
        {
            var key = _apiKeyField.value?.Trim() ?? "";
            _confirmButton.SetEnabled(!string.IsNullOrWhiteSpace(key));
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
                _apiKeyField.value = UserSettingsStore.GetApiKey();
                RefreshConfirmState();
            }
        }
    }
}
