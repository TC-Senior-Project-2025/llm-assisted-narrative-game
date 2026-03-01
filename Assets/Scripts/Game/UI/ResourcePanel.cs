using System;
using Extensions;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class ResourcePanel
    {
        private readonly VisualElement _panel;
        private readonly AnimatedNumberLabel _label;
        private readonly string _resourceName;

        public Action<string, VisualElement> OnHoverEnter;
        public Action OnHoverLeave;

        public enum DisplayType
        {
            ThousandsSeperator,
            Suffix,
            Percent
        }

        public ResourcePanel(VisualElement panel, DisplayType displayType, string resourceName, string resourceDescription)
        {
            _panel = panel;
            _resourceName = resourceName;

            Func<int, string> formatter = displayType switch
            {
                DisplayType.Percent => num => $"{num}%",
                DisplayType.ThousandsSeperator => num => $"{num:N0}",
                DisplayType.Suffix => num => $"{num.FormatWithSuffix()}",
                _ => throw new ArgumentOutOfRangeException(nameof(displayType), displayType, null)
            };

            _label = new AnimatedNumberLabel(_panel.Q<Label>(), 0, formatter);

            // Register hover events
            _panel.RegisterCallback<PointerEnterEvent>(_ => OnHoverEnter?.Invoke(resourceDescription, _panel));
            _panel.RegisterCallback<PointerLeaveEvent>(_ => OnHoverLeave?.Invoke());
        }

        public void SetValue(int newValue)
        {
            _label.SetValue(newValue);
        }
    }
}