using UnityEngine.Events;

namespace Animation.Misc
{
    public interface ITogglePanel
    {
        void Show();

        void Hide(bool force = false);

        void Toggle(bool state, bool forceSuddenly = false, bool force = false);
    }
}