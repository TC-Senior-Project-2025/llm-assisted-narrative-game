using UnityEngine.Events;

namespace Game.UI.Interfaces
{
    public interface IBasePanel
    {
        public UnityEvent onClose { get; }

        public void SetEnabled(bool isEnabled);
    }
}