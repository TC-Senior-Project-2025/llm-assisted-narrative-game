using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class ResourceListPanel
    {
        private readonly VisualElement _panel;
        private readonly ResourcePanel _treasuryPanel;
        private readonly ResourcePanel _manpowerPanel;
        private readonly ResourcePanel _stabilityPanel;
        private readonly ResourcePanel _efficiencyPanel;
        private readonly ResourcePanel _prestigePanel;
        private readonly Label _tooltip;

        public ResourceListPanel(VisualElement panel, VisualTreeAsset tooltipTemplate)
        {
            _panel = panel;

            // Instantiate tooltip from UXML template
            if (tooltipTemplate != null)
            {
                var tooltipContainer = tooltipTemplate.Instantiate();
                _tooltip = tooltipContainer.Q<Label>("Tooltip");

                if (_tooltip != null)
                {
                    _tooltip.style.display = DisplayStyle.None;
                    _tooltip.pickingMode = PickingMode.Ignore;

                    // Add tooltip to root to ensure proper positioning
                    var root = _panel.panel?.visualTree;
                    if (root != null)
                    {
                        root.Add(_tooltip);
                    }
                    else
                    {
                        // Fallback: schedule adding tooltip after panel is attached
                        _panel.RegisterCallback<AttachToPanelEvent>(_ =>
                        {
                            _panel.panel?.visualTree?.Add(_tooltip);
                        });
                    }
                }
            }

            _treasuryPanel = new ResourcePanel(
                _panel.Q("Treasury"),
                ResourcePanel.DisplayType.Suffix,
                "Treasury",
                "Treasury: \nCopper taels in the national reserve."
            );

            _manpowerPanel = new ResourcePanel(
                _panel.Q("Manpower"),
                ResourcePanel.DisplayType.Suffix,
                "Manpower",
                "Manpower: \nAvailable population for military recruitment."
            );

            _stabilityPanel = new ResourcePanel(
                _panel.Q("Stability"),
                ResourcePanel.DisplayType.Percent,
                "Stability",
                "Stability: \nOverall internal order."
            );

            _efficiencyPanel = new ResourcePanel(
                _panel.Q("Efficiency"),
                ResourcePanel.DisplayType.Percent,
                "Efficiency",
                "Efficiency: \nGovernment effectiveness."
            );

            _prestigePanel = new ResourcePanel(
                _panel.Q("Prestige"),
                ResourcePanel.DisplayType.Suffix,
                "Prestige",
                "Prestige: \nNational reputation and influence."
            );

            // Wire up hover events for all resource panels
            SetupHoverEvents(_treasuryPanel);
            SetupHoverEvents(_manpowerPanel);
            SetupHoverEvents(_stabilityPanel);
            SetupHoverEvents(_efficiencyPanel);
            SetupHoverEvents(_prestigePanel);
        }

        private void SetupHoverEvents(ResourcePanel resourcePanel)
        {
            resourcePanel.OnHoverEnter = (description, element) => ShowTooltip(description, element);
            resourcePanel.OnHoverLeave = HideTooltip;
        }

        private void ShowTooltip(string text, VisualElement targetElement)
        {
            if (_tooltip == null) return;

            _tooltip.text = text;
            _tooltip.style.display = DisplayStyle.Flex;
            _tooltip.BringToFront();

            // Position tooltip below the target element
            var targetWorldBound = targetElement.worldBound;
            _tooltip.style.left = targetWorldBound.x;
            _tooltip.style.top = targetWorldBound.yMax + 5;
        }

        private void HideTooltip()
        {
            if (_tooltip == null) return;
            _tooltip.style.display = DisplayStyle.None;
        }

        public void UpdateResources(int treasury, int manpower, int stability, int efficiency, int prestige)
        {
            _treasuryPanel.SetValue(treasury);
            _manpowerPanel.SetValue(manpower);
            _stabilityPanel.SetValue(stability);
            _efficiencyPanel.SetValue(efficiency);
            _prestigePanel.SetValue(prestige);
        }
    }
}
