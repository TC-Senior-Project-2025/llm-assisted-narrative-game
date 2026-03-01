using System.Linq;
using System.Threading.Tasks;
using Game.Services.Events;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class EventPanel
    {
        private readonly VisualElement _panel;
        private readonly VisualElement _eventOptionContainer;
        private readonly Label _eventTitleLabel;
        private readonly Label _eventDescriptionLabel;
        private readonly VisualTreeAsset _optionButtonTemplate;

        private TaskCompletionSource<EventChoice> _displayEventTcs;

        public EventPanel(VisualElement panel, VisualTreeAsset optionButtonTemplate)
        {
            _panel = panel;
            _eventTitleLabel = _panel.Q<Label>("EventTitle");
            _eventDescriptionLabel = _panel.Q<Label>("EventDescription");
            _eventOptionContainer = _panel.Q("OptionContainer");
            _optionButtonTemplate = optionButtonTemplate;
        }

        public VisualElement GetPanel() => _panel;

        public Task<EventChoice> DisplayEventAsync(GameEvent gameEvent)
        {
            _displayEventTcs?.TrySetCanceled();
            _displayEventTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            _eventTitleLabel.text = gameEvent.EventName;
            _eventDescriptionLabel.text = gameEvent.EventDesc;
            _panel.visible = true;

            _eventOptionContainer.Clear();

            if (gameEvent.Choices.Count == 0)
            {
                gameEvent.Choices.Add(new()
                {
                    ChoiceName = "OK",
                    ChoiceDesc = "",
                    Effects = new() { }
                });
            }

            var optionButtons = gameEvent.Choices.Select((o, i) =>
            {
                var optionButton = _optionButtonTemplate.Instantiate();
                var button = optionButton.Q<Button>();
                button.text = o.ChoiceName;
                button.RegisterCallback<ClickEvent>(_ => OnOptionSelect(o));
                return optionButton;
            });

            foreach (var button in optionButtons)
            {
                _eventOptionContainer.Add(button);
            }

            return _displayEventTcs.Task;
        }

        private void OnOptionSelect(EventChoice choice)
        {
            if (_displayEventTcs == null) return;

            _displayEventTcs.TrySetResult(choice);

            _panel.visible = false;
            _displayEventTcs = null;
        }
    }
}