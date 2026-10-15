using UnityEngine;
using UnityEngine.Events;

namespace Animation.Misc
{
    public class TogglePanelWrapper : MonoBehaviour, ITogglePanel
    {
        [SerializeField] private TogglePanel[] panels;

        private void Awake()
        {
            foreach (var panel in panels)
            {
                panel.Wrapper = this;
            }
        }


        public void Show()
        {
            foreach (var panel in panels)
            {
                panel.Wrapper = null;
                panel.Show();
                panel.Wrapper = this;
            }
        }

        public void Hide(bool force = false)
        {
            foreach (var panel in panels)
            {
                panel.Wrapper = null;
                panel.Hide(force);
                panel.Wrapper = this;
            }
        }

        public void Toggle(bool state, bool forceSuddenly = false, bool force = false)
        {
            foreach (var panel in panels)
            {
                panel.Wrapper = null;
                panel.Toggle(state, forceSuddenly, force);
                panel.Wrapper = this;
            }
        }
    }
}