using System.Threading.Tasks;
using Game.Services.Events;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class EventOutcomePanel
    {
        private readonly VisualElement _panel;
        private readonly Label _eventTitleLabel;
        private readonly Label _eventDescriptionLabel;
        private readonly Button _okButton;

        private TaskCompletionSource<bool> _displayEventOutcomeTcs;

        public EventOutcomePanel(VisualElement panel)
        {
            _panel = panel;
            _eventTitleLabel = _panel.Q<Label>("EventOutcomeTitle");
            _eventDescriptionLabel = _panel.Q<Label>("EventOutcomeDescription");
            _okButton = _panel.Q<Button>("OkButton");

            _okButton.RegisterCallback<ClickEvent>(_ => OnOk());
        }

        public VisualElement GetPanel() => _panel;

        public Task DisplayEventOutcomeAsync(EventOutcome outcome)
        {
            _displayEventOutcomeTcs?.TrySetCanceled();
            _displayEventOutcomeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            _eventTitleLabel.text = outcome.OutcomeName;
            _eventDescriptionLabel.text = outcome.OutcomeDesc;
            _panel.visible = true;

            return _displayEventOutcomeTcs.Task;
        }

        private void OnOk()
        {
            if (_displayEventOutcomeTcs == null) return;

            _displayEventOutcomeTcs.TrySetResult(true);

            _panel.visible = false;
            _displayEventOutcomeTcs = null;
        }
    }
}