using System;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class VerticalBar
    {
        private VisualElement _counterFill;

        public VerticalBar(VisualElement element)
        {
            _counterFill = element.Q("CounterFill");
        }

        public void SetValue(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            _counterFill.style.height = Length.Percent(100 - percent);
        }
    }

}