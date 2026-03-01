using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    public class AnimatedNumberLabel
    {
        private readonly Label _label;
        private IVisualElementScheduledItem _scheduler;
        private IVisualElementScheduledItem _flashScheduler;

        private float _currentValue;
        private float _targetValue;
        private float _startValue;
        private float _elapsed;
        private float _duration;

        private float _flashElapsed;
        private float _flashDuration = 0.2f;
        private Color _flashStartColor;
        private Color _flashTargetColor;

        private readonly Func<int, string> _formatter;

        public static readonly Func<int, string> DefaultFormatter = v => $"{v}";
        public static readonly Func<int, string> ThousandsFormatter = v => $"{v:N0}";

        private static readonly Color GreenFlash = new Color(0.3f, 1f, 0.3f, 1f);
        private static readonly Color RedFlash = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly Color DefaultColor = Color.white;

        public AnimatedNumberLabel(
            Label label,
            int initialValue = 0,
            Func<int, string> formatter = null)
        {
            _label = label;
            _currentValue = initialValue;

            _formatter = formatter ?? DefaultFormatter;
            _label.text = _formatter(initialValue);
            _label.style.color = DefaultColor;
        }

        public void SetValue(float newValue, float duration = 0.1f, float flashDuration = 0.5f)
        {
            if (Mathf.Approximately(newValue, _currentValue))
                return;

            _startValue = _currentValue;
            _targetValue = newValue;
            _duration = duration;
            _elapsed = 0f;
            
            _scheduler?.Pause();
            _scheduler = _label.schedule
                .Execute(Update)
                .Every(16); // ~60fps

            // Start color flash
            StartFlash(newValue > _currentValue, flashDuration);
        }

        private void StartFlash(bool isPositive, float duration)
        {
            _flashDuration = duration;
            _flashElapsed = 0f;
            _flashStartColor = isPositive ? GreenFlash : RedFlash;
            _flashTargetColor = DefaultColor;

            _label.style.color = _flashStartColor;

            _flashScheduler?.Pause();
            _flashScheduler = _label.schedule
                .Execute(UpdateFlash)
                .Every(16); // ~60fps
        }

        private void UpdateFlash()
        {
            _flashElapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_flashElapsed / _flashDuration);

            _label.style.color = Color.Lerp(_flashStartColor, _flashTargetColor, t);

            if (t < 1f) return;

            _label.style.color = _flashTargetColor;
            _flashScheduler.Pause();
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            var t = Mathf.Clamp01(_elapsed / _duration);

            _currentValue = Mathf.Lerp(_startValue, _targetValue, t);
            _label.text = _formatter(Mathf.RoundToInt(_currentValue));
            
            if (t < 1f) return;

            _currentValue = _targetValue;
            _label.text = _formatter(Mathf.RoundToInt(_currentValue));
            _scheduler.Pause();
        }
    }
}